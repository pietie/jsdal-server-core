using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Text;
using jsdal_server_core.Settings.ObjectModel;
using NUglify;
using jsdal_server_core;
using System.Text.RegularExpressions;
using jsdal_server_core.Settings;
using jsdal_server_core.Changes;
using System.Collections.Concurrent;

namespace jsdal_server_core
{

    // TODO: move somewhere appropriate
    class Constants
    {
        public static int MAX_NUMBER_OF_RESULTS { get { return 5; } }
    }
    public class JsFileGenerator
    {

        public static bool StartsWithNum(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            var charCode = s[0];
            return charCode >= 48/*0*/ && charCode <= 57/*9*/;
        }

        public static string MakeNameJsSafe(string s)
        {
            if (JsFileGenerator.StartsWithNum(s)) s = "_" + s;
            return s.Replace(" ", "_").Replace("#", "").Replace(".", "_").Replace("-", "_");
        }

        public static void GenerateJsFileV2(string source, Endpoint endpoint, JsFile jsFile, Dictionary<string, ChangeDescriptor> fullChangeSet = null, bool rulesChanged = false)
        {
            var generateMetric = new Performance.ExecutionBase("GenerateJsFileV2");
            var noChanges = false;

            try
            {
                // TODO: Figure out out casing on this property 
                string jsNamespace = null;
                if (string.IsNullOrWhiteSpace(jsNamespace)) jsNamespace = endpoint.MetadataConnection.InitialCatalog;

                var jsSafeNamespace = MakeNameJsSafe(jsNamespace);

                var routineContainerTemplate = WorkSpawner.TEMPLATE_RoutineContainer;
                var routineTemplate = WorkSpawner.TEMPLATE_Routine;
                var typescriptDefinitionsContainer = WorkSpawner.TEMPLATE_TypescriptDefinitions;

                endpoint.ApplyRules(jsFile);

                var includedRoutines = (from row in endpoint.CachedRoutines
                                        where !row.IsDeleted && (row.RuleInstructions[jsFile]?.Included ?? false == true)
                                        orderby row.FullName
                                        select row).ToList();

                List<KeyValuePair<string, ChangeDescriptor>> changesInFile = null;


                if (fullChangeSet != null)
                {
                    changesInFile = fullChangeSet.Where(change => includedRoutines.Count(inc => inc.FullName.Equals(change.Key, StringComparison.OrdinalIgnoreCase)) > 0).ToList();

                    // TODO: Consider if this is a good idea?
                    //       If we can reasonably say that there are no changes to routines that this JsFile cares about, why regenerate this file and why give it a new Version
                    if (changesInFile.Count == 0)
                    {
                        noChanges = true;
                        return;
                    }
                }

                var jsSchemaLookupForJsFunctions = new Dictionary<string, List<string>/*Routine defs*/>();
                var tsSchemaLookup = new Dictionary<string, List<string>/*Routine defs*/>();

                var typeScriptParameterAndResultTypesSB = new StringBuilder();

                var serverMethodPlugins = PluginLoader.Instance.PluginAssemblies
                            .SelectMany(pa => pa.Plugins)
                            .Where(p => p.Type == PluginType.ServerMethod && endpoint.Application.IsPluginIncluded(p.Guid.ToString()));

                var uniqueSchemas = new List<string>();

                var mainLoopMetric = generateMetric.BeginChildStage("Main loop");

                includedRoutines.ForEach(r =>
                {
                    try
                    {
                        if (r.TypescriptMethodStub == null)
                        {
                            r.PrecalculateJsGenerationValues(endpoint);
                        }

                        var jsSchemaName = JsFileGenerator.MakeNameJsSafe(r.Schema);
                        var jsFunctionName = JsFileGenerator.MakeNameJsSafe(r.Routine);

                        if (!jsSchemaLookupForJsFunctions.ContainsKey(jsSchemaName))
                        {
                            jsSchemaLookupForJsFunctions.Add(jsSchemaName, new List<string>());
                        }

                        if (!tsSchemaLookup.ContainsKey(jsSchemaName))
                        {
                            tsSchemaLookup.Add(jsSchemaName, new List<string>());
                        }

                        if (!uniqueSchemas.Contains(r.Schema))
                        {
                            uniqueSchemas.Add(r.Schema);
                        }

                        var schemaIx = uniqueSchemas.IndexOf(r.Schema);

                        // .js
                        {
                            var jsFunctionDefLine = routineTemplate.Replace("<<FUNC_NAME>>", jsFunctionName).Replace("<<SCHEMA_IX>>", schemaIx.ToString()).Replace("<<ROUTINE>>", r.Routine);

                            if (r.Type.Equals(string.Intern("PROCEDURE"), StringComparison.OrdinalIgnoreCase)) jsFunctionDefLine = jsFunctionDefLine.Replace("<<CLASS>>", "S");
                            else jsFunctionDefLine = jsFunctionDefLine.Replace("<<CLASS>>", "U");

                            jsSchemaLookupForJsFunctions[jsSchemaName].Add(jsFunctionDefLine);
                        }

                        // .tsd
                        {
                            typeScriptParameterAndResultTypesSB.AppendLine(r.TypescriptParameterTypeDefinition);
                            typeScriptParameterAndResultTypesSB.AppendLine(r.TypescriptOutputParameterTypeDefinition);
                            typeScriptParameterAndResultTypesSB.AppendLine(r.TypescriptResultSetDefinitions);
                            tsSchemaLookup[jsSchemaName].Add(r.TypescriptMethodStub);
                        }
                    }
                    catch (Exception ex)
                    {
                        SessionLog.Exception(ex);
                        // TODO: quit whole process
                    }
                });

                mainLoopMetric.End();

                var finalSBMetric = generateMetric.BeginChildStage("Final SB");

                var schemaAndRoutineDefs = string.Join("\r\n", jsSchemaLookupForJsFunctions.Select(s => "\tx." + s.Key + " = {\r\n\t\t" + string.Join(",\r\n\t\t", s.Value.ToArray()) + "\r\n\t}\r\n").ToArray());
                var tsSchemaAndRoutineDefs = string.Join("\r\n", tsSchemaLookup.Select(s => "\t\tclass " + s.Key + " {\r\n" + string.Join(";\r\n", s.Value.ToArray()) + "\r\n\t\t}\r\n").ToArray());

                var finalSB = new StringBuilder(routineContainerTemplate);

                jsFile.IncrementVersion();

                // record changes against new version

                if (changesInFile != null && changesInFile.Count > 0)
                {
                    JsFileChangesTracker.Instance.AddUpdate(endpoint, jsFile, changesInFile.Select(kv => kv.Value).ToList());
                }

                if (rulesChanged)
                {
                    JsFileChangesTracker.Instance.AddUpdate(endpoint, jsFile, new List<ChangeDescriptor> { ChangeDescriptor.Create("System", "One or more rules changed.") });
                }

                finalSB.Replace("<<DATE>>", DateTime.Now.ToString("dd MMM yyyy, HH:mm"))
                    .Replace("<<FILE_VERSION>>", jsFile.Version.ToString())
                    .Replace("<<SERVER_NAME>>", Environment.MachineName)
                    .Replace("<<FILENAME>>", jsFile.Filename)
                    .Replace("<<UNIQUE_SCHEMAS>>", string.Join(',', uniqueSchemas.Select(k => $"'{k}'")))
                    .Replace("<<Catalog>>", jsSafeNamespace)
                    .Replace("<<ROUTINES>>", schemaAndRoutineDefs)
                ;

                var finalTypeScriptSB = new StringBuilder();

                finalTypeScriptSB = finalTypeScriptSB.Append(typescriptDefinitionsContainer);

                // Custom/User types
                if (endpoint.CustomTypeLookupWithTypeScriptDef.Count > 0)
                {
                    var customTSD = from kv in endpoint.CustomTypeLookupWithTypeScriptDef select $"\t\ttype {kv.Key} = {kv.Value}";
                    typeScriptParameterAndResultTypesSB.Insert(0, string.Join("\r\n", customTSD) + "\r\n");
                }

                var resultAndParameterTypes = typeScriptParameterAndResultTypesSB.ToString();

                finalTypeScriptSB.Replace("<<DATE>>", DateTime.Now.ToString("dd MMM yyyy, HH:mm"))
                    .Replace("<<FILE_VERSION>>", jsFile.Version.ToString())
                    .Replace("<<SERVER_NAME>>", Environment.MachineName)
                    .Replace("<<Catalog>>", jsSafeNamespace)
                    .Replace("<<ResultAndParameterTypes>>", resultAndParameterTypes)
                    .Replace("<<MethodsStubs>>", tsSchemaAndRoutineDefs)
                ;

                finalSBMetric.End();

                var toStringMetric = generateMetric.BeginChildStage("ToString");
                var typescriptDefinitionsOutput = finalTypeScriptSB.ToString();
                var finalOutput = finalSB.ToString();
                toStringMetric.End();

                var filePath = endpoint.OutputFilePath(jsFile);
                var minfiedFilePath = endpoint.MinifiedOutputFilePath(jsFile);
                var tsTypingsFilePath = endpoint.OutputTypeScriptTypingsFilePath(jsFile);

                var minifyMetric = generateMetric.BeginChildStage("Minify");

                var minifiedSource = Uglify.Js(finalOutput/*, { }*/).Code;

                minifyMetric.End();

                if (!Directory.Exists(endpoint.OutputDir)) { Directory.CreateDirectory(endpoint.OutputDir); }

                var fileOutputMetric = generateMetric.BeginChildStage("Write");

                var jsFinalBytes = System.Text.Encoding.UTF8.GetBytes(finalOutput);
                var jsFinalMinifiedBytes = System.Text.Encoding.UTF8.GetBytes(minifiedSource);

                jsFile.ETag = Controllers.PublicController.ComputeETag(jsFinalBytes);
                jsFile.ETagMinified = Controllers.PublicController.ComputeETag(jsFinalMinifiedBytes);

                File.WriteAllText(filePath, finalOutput);
                File.WriteAllText(minfiedFilePath, minifiedSource);
                File.WriteAllText(tsTypingsFilePath, typescriptDefinitionsOutput);

                fileOutputMetric.End();
            }
            finally
            {
                generateMetric.End();

                SessionLog.InfoToFileOnly($"{endpoint.Pedigree.PadRight(25, ' ')} - {generateMetric.DurationInMS.ToString().PadLeft(4)} ms {jsFile.Filename.PadRight(20)} (source={source};rulesChanged={rulesChanged};changes={!noChanges}); {generateMetric.ChildDurationsSingleLine()}");
            }
        }


        // Obsolete
        // public static void GenerateJsFile(string source, Endpoint endpoint, JsFile jsFile, Dictionary<string, ChangeDescriptor> fullChangeSet = null, bool rulesChanged = false)
        // {
        //     int overAlltick = Environment.TickCount;
        //     var correlationGuid = Guid.NewGuid();
        //     int time1 = 0, time2 = 0, time3 = 0, time4 = 0, time5 = 0, time6 = 0;
        //     var noChanges = false;

        //     try
        //     {
        //         // TODO: Figure out what to do with JsNamespace CASING. Sometimes case changes in connection string and that breaks all existing code
        //         string jsNamespace = null;//endpoint.JsNamespace;
        //         if (string.IsNullOrWhiteSpace(jsNamespace)) jsNamespace = endpoint.MetadataConnection.InitialCatalog;

        //         var jsSafeNamespace = MakeNameJsSafe(jsNamespace);

        //         var typeScriptParameterAndResultTypesSB = new StringBuilder();

        //         var routineContainerTemplate = WorkSpawner.TEMPLATE_RoutineContainer;
        //         var routineTemplate = WorkSpawner.TEMPLATE_Routine;
        //         var typescriptDefinitionsContainer = WorkSpawner.TEMPLATE_TypescriptDefinitions;

        //         // maps Custom SQL types to TypeScript definitions
        //         var customTypeLookupWithTypeScriptDef = new ConcurrentDictionary<string, string>();

        //         endpoint.ApplyRules(jsFile);

        //         var includedRoutines = (from row in endpoint.CachedRoutines
        //                                 where !row.IsDeleted && (row.RuleInstructions[jsFile]?.Included ?? false == true)
        //                                 orderby row.FullName
        //                                 select row).ToList();

        //         List<KeyValuePair<string, ChangeDescriptor>> changesInFile = null;


        //         if (fullChangeSet != null)
        //         {
        //             changesInFile = fullChangeSet.Where(change => includedRoutines.Count(inc => inc.FullName.Equals(change.Key, StringComparison.OrdinalIgnoreCase)) > 0).ToList();

        //             // TODO: Consider if this is a good idea?
        //             //       If we can reasonably say that there are no changes to routines that this JsFile cares about, why regenerate this file and why give it a new Version
        //             if (changesInFile.Count == 0)
        //             {
        //                 noChanges = true;
        //                 return;
        //             }
        //         }

        //         SessionLog.InfoToFileOnly($"{endpoint.Pedigree.PadRight(25, ' ')} - generating {jsFile.Filename} (source={source};rulesChanged={rulesChanged}) {correlationGuid}");

        //         var jsSchemaLookupForJsFunctions = new Dictionary<string, List<string>/*Routine defs*/>();
        //         var tsSchemaLookup = new Dictionary<string, List<string>/*Routine defs*/>();


        //         var serverMethodPlugins = PluginLoader.Instance.PluginAssemblies
        //                     .SelectMany(pa => pa.Plugins)
        //                     .Where(p => p.Type == PluginType.ServerMethod && endpoint.Application.IsPluginIncluded(p.Guid.ToString()));

        //         var uniqueSchemas = new List<string>();


        //         // TODO: use System.Threading.Tasks.Parallel.ForEach()
        //         includedRoutines.ForEach(r =>
        //         //System.Threading.Tasks.Parallel.ForEach(includedRoutines, (r, i)=> 


        //         {
        //             try
        //             {
        //                 int tick = Environment.TickCount;

        //                 var schemaName = r.Schema;
        //                 var routineName = r.Routine;

        //                 // javascript does not allow types to start with a number so just prepend with an underscore
        //                 // declaring jsFunctionName as routine name before alteration and alter if necessary, keep Original routineName for execution
        //                 var jsFunctionName = JsFileGenerator.MakeNameJsSafe(routineName);
        //                 var jsSchemaName = JsFileGenerator.MakeNameJsSafe(schemaName);

        //                 if (!jsSchemaLookupForJsFunctions.ContainsKey(jsSchemaName))
        //                 {
        //                     jsSchemaLookupForJsFunctions.Add(jsSchemaName, new List<string>());
        //                 }

        //                 if (!tsSchemaLookup.ContainsKey(jsSchemaName))
        //                 {
        //                     tsSchemaLookup.Add(jsSchemaName, new List<string>());
        //                 }

        //                 // needs to be true so that DAL.js can identify parameters from the [options] parm passed in
        //                 var includeParams = false; // 27/03/2019, PL: DAL api changed so parms are no longer necessary in .js file

        //                 time1 += Environment.TickCount - tick;


        //                 if (r.Type.Equals("PROCEDURE", StringComparison.OrdinalIgnoreCase)
        //                                                   || r.Type.Equals("FUNCTION", StringComparison.OrdinalIgnoreCase)
        //                                                   || r.Type.Equals("TVF", StringComparison.OrdinalIgnoreCase)
        //                                                   )
        //                 {

        //                     tick = Environment.TickCount;

        //                     //string factoryRef = null;

        //                     if (!uniqueSchemas.Contains(r.Schema))
        //                     {
        //                         uniqueSchemas.Add(r.Schema);
        //                     }

        //                     var schemaIx = uniqueSchemas.IndexOf(r.Schema);

        //                     // if (r.Type.Equals("PROCEDURE", StringComparison.OrdinalIgnoreCase))
        //                     // {
        //                     //     // if ((uniqueSchemas[r.Schema] & 1) != 1)
        //                     //     // {
        //                     //     //     uniqueSchemas[r.Schema] |= 1/*Use 1 for PROCs*/;
        //                     //     //     //routineFactorySB.AppendLine($"\tvar S{schemaIx} = function(r,o) {{ return new ss(SC[{schemaIx}],r,o); }}");
        //                     //     // }

        //                     //     factoryRef = $"S{schemaIx}";
        //                     // }
        //                     // else
        //                     // {
        //                     //     //  if ((uniqueSchemas[r.Schema] & 2) != 2)
        //                     //     // {
        //                     //     //     uniqueSchemas[r.Schema] |= 2/*Use 2 for UDFss*/;
        //                     //     //     //routineFactorySB.AppendLine($"\tvar U{schemaIx} = function(r,o) {{ return new uu(SC[{schemaIx}],r,o); }}");
        //                     //     // }

        //                     //     factoryRef = $"U{schemaIx}";
        //                     // }

        //                     var jsFunctionLine = routineTemplate.Replace("<<FUNC_NAME>>", jsFunctionName).Replace("<<SCHEMA_IX>>", schemaIx.ToString()).Replace("<<ROUTINE>>", routineName);

        //                     if (r.Type.Equals("PROCEDURE", StringComparison.OrdinalIgnoreCase)) jsFunctionLine = jsFunctionLine.Replace("<<CLASS>>", "S");
        //                     else jsFunctionLine = jsFunctionLine.Replace("<<CLASS>>", "U");


        //                     string jsParameters = null;

        //                     string tsParameterTypeDefName = null;
        //                     string typeScriptOutputParameterTypeName = null;

        //                     time2 += Environment.TickCount - tick;


        //                     if (r.Parameters != null)
        //                     {
        //                         tick = Environment.TickCount;

        //                         tsParameterTypeDefName = $"{ jsSafeNamespace }_{ schemaName}_{ jsFunctionName}Parameters";


        //                         var sprocParameters = from p in r.Parameters
        //                                               where !string.IsNullOrEmpty(p.Name)
        //                                               select new
        //                                               {
        //                                                   Name = StartsWithNum(p.Name.TrimStart('@')) ? (/*"_" + */p.Name.TrimStart('@')) : p.Name.TrimStart('@'),
        //                                                   p.IsResult,
        //                                                   p.SqlDataType,
        //                                                   p.IsOutput,
        //                                                   HasDefault = p.HasDefault,
        //                                                   JavascriptDataType = RoutineParameterV2.GetDataTypeForJavaScriptComment(p.SqlDataType, p.CustomType),
        //                                                   TypescriptDataType = RoutineParameterV2.GetTypescriptTypeFromSql(p.SqlDataType, p.CustomType, ref customTypeLookupWithTypeScriptDef)
        //                                               };


        //                         jsParameters = string.Join(",", sprocParameters.Select(p => $"\"{p.Name}\"").ToArray());


        //                         var tsParameters = from p in sprocParameters
        //                                            select string.Format("{0}{2}: {1}", StartsWithNum(p.Name) ? ("$" + p.Name) : p.Name, p.TypescriptDataType, p.HasDefault ? "?" : "");

        //                         var typeScriptParameterDef = string.Format("\t\ttype {0} = {{ {1} }}", tsParameterTypeDefName, string.Join(", ", tsParameters));

        //                         typeScriptParameterAndResultTypesSB.AppendLine(typeScriptParameterDef);


        //                         var outputParms = (from p in r.Parameters
        //                                            where !string.IsNullOrEmpty(p.Name) && !p.IsResult
        //                                              && p.IsOutput
        //                                            select string.Format("{0}?: {1}", StartsWithNum(p.Name.TrimStart('@')) ? (/*"_" + */p.Name.TrimStart('@')) : p.Name.TrimStart('@')
        //                                             , RoutineParameterV2.GetTypescriptTypeFromSql(p.SqlDataType, p.CustomType, ref customTypeLookupWithTypeScriptDef))).ToList();



        //                         if (outputParms.Count > 0)
        //                         {
        //                             typeScriptOutputParameterTypeName = string.Format("{0}_{1}_{2}OutputParms", jsSafeNamespace, schemaName, jsFunctionName);
        //                             var typeScriptOutputParameterTypeDef = string.Format("\t\ttype {0} = {{ {1} }}", typeScriptOutputParameterTypeName, string.Join(", ", outputParms));


        //                             typeScriptParameterAndResultTypesSB.AppendLine(typeScriptOutputParameterTypeDef);
        //                         }

        //                         time3 += Environment.TickCount - tick;
        //                     }// if (r.Parameters != null)

        //                     if (!includeParams) { jsParameters = null; }

        //                     if (!string.IsNullOrEmpty(jsParameters))
        //                     {
        //                         jsFunctionLine = jsFunctionLine.Replace("<<PARM_DEFINITION>>", jsParameters);
        //                     }
        //                     else
        //                     {
        //                         jsFunctionLine = jsFunctionLine.Replace("<<PARM_DEFINITION>>", "");
        //                     }

        //                     var resultTypes = new List<string>();

        //                     if (r.ResultSetMetadata != null)
        //                     {
        //                         tick = Environment.TickCount;

        //                         int resultIx = 0;

        //                         foreach (var kv in r.ResultSetMetadata)
        //                         {
        //                             var columnCounter = new SortedList<string, int>();

        //                             // e.g. Icev0_Accpac_ItemMakeMappingGetListResult0
        //                             var tsResultTypeDefName = string.Format("{0}_{1}_{2}Result{3}", jsSafeNamespace, schemaName, jsFunctionName, resultIx++);

        //                             var tsColumnDefs = new List<string>();

        //                             int nonNameColIx = 0;

        //                             //kv.Value = Table[0...N]
        //                             foreach (var col in kv.Value)
        //                             {
        //                                 var colName = col.ColumnName.ToString();
        //                                 var dbDataType = col.DbDataType.ToString();

        //                                 if (string.IsNullOrWhiteSpace(colName))
        //                                 {
        //                                     colName = string.Format("Unknown{0:000}", ++nonNameColIx);
        //                                 }

        //                                 var originalColName = colName;

        //                                 // handle duplicate column names
        //                                 if (columnCounter.ContainsKey(colName))
        //                                 {
        //                                     //!this.Log.Warning("Duplicate column name encountered for column '{0}' on routine {1}", colName, r.FullName);

        //                                     var n = columnCounter[colName];
        //                                     colName += ++n; // increase by one to give a new unique name
        //                                     columnCounter[originalColName] = n;
        //                                 }
        //                                 else
        //                                 {
        //                                     columnCounter.Add(colName, 1);
        //                                 }

        //                                 colName = QuoteColumnNameIfNecessary(colName);

        //                                 // a bit backwards but gets the job done
        //                                 var typeScriptDataType = RoutineParameterV2.GetTypescriptTypeFromSql(dbDataType, null, ref customTypeLookupWithTypeScriptDef);

        //                                 // Col: TypeScript DataType
        //                                 var ln = string.Format("{0}: {1}", colName, typeScriptDataType);
        //                                 tsColumnDefs.Add(ln);

        //                             }

        //                             var typeScriptResultDef = string.Format("\t\tclass {0} {{ {1} }}", tsResultTypeDefName, string.Join("; ", tsColumnDefs));

        //                             resultTypes.Add(tsResultTypeDefName);

        //                             typeScriptParameterAndResultTypesSB.AppendLine(typeScriptResultDef);

        //                         }

        //                         time4 += Environment.TickCount - tick;
        //                     } // if (r.ResultSetMetadata != null)

        //                     tick = Environment.TickCount;

        //                     var tsArguments = "";

        //                     if (!string.IsNullOrEmpty(tsParameterTypeDefName))
        //                     {
        //                         tsArguments = string.Format("_.{0}", tsParameterTypeDefName);
        //                     }
        //                     else
        //                     {
        //                         tsArguments = "{ }";
        //                     }

        //                     if (r.Type.Equals("FUNCTION", StringComparison.OrdinalIgnoreCase))
        //                     {
        //                         if (r.Parameters != null)
        //                         {
        //                             var resultParm = r.Parameters.First(p => p.IsResult);

        //                             var tsMethodStub = string.Format("\t\t\tstatic {0}(parameters?: {1}): IUDFExecGeneric<{2}, {1}>", jsFunctionName, tsArguments
        //                             , RoutineParameterV2.GetTypescriptTypeFromSql(resultParm.SqlDataType, resultParm.CustomType, ref customTypeLookupWithTypeScriptDef));

        //                             tsSchemaLookup[jsSchemaName].Add(tsMethodStub);
        //                         }
        //                         else
        //                         {
        //                             SessionLog.Warning($"Cannot generate UDF method stub because the parameters collection is empty. Does the object still exist? Endpoint = {endpoint.Name} ({endpoint.Id}), Routine={r.FullName}");
        //                         }

        //                     }
        //                     else
        //                     {
        //                         var outputParmType = "void";
        //                         // check if there are output parameters present
        //                         if (!string.IsNullOrWhiteSpace(typeScriptOutputParameterTypeName))
        //                         {
        //                             outputParmType = "_." + typeScriptOutputParameterTypeName;
        //                         }

        //                         if (resultTypes.Count > 0)
        //                         {
        //                             var cnt = resultTypes.Count;

        //                             // cap ISprocExecGeneric to max available. Intellisense will not be able to show the additional results sets although they will be present
        //                             if (cnt > Constants.MAX_NUMBER_OF_RESULTS)
        //                             {
        //                                 //!this.Log.Warning("Result set metadata is capped at {0} result sets. Intellisense will not show more than that although they will be accessible at run-time.", Constants.MAX_NUMBER_OF_RESULTS);
        //                                 cnt = Constants.MAX_NUMBER_OF_RESULTS;
        //                             }

        //                             var tsMethodStub = string.Format("\t\t\tstatic {0}(parameters?: {1}): ISprocExecGeneric{3}<{4}, {2}, {1}>", jsFunctionName, tsArguments, string.Join(",", resultTypes.Take(Constants.MAX_NUMBER_OF_RESULTS).Select(rt => "_." + rt)), cnt, outputParmType);

        //                             tsSchemaLookup[jsSchemaName].Add(tsMethodStub);
        //                         }
        //                         else
        //                         { // NO RESULTSET
        //                             var tsMethodStub = string.Format("\t\t\tstatic {0}(parameters?: {1}): ISprocExecGeneric0<{2}, {1}>", jsFunctionName, tsArguments, outputParmType);

        //                             tsSchemaLookup[jsSchemaName].Add(tsMethodStub);
        //                         }

        //                     }

        //                     jsSchemaLookupForJsFunctions[jsSchemaName].Add(jsFunctionLine);

        //                     time5 += Environment.TickCount - tick;
        //                 }
        //                 else
        //                 {
        //                     throw new Exception("Unsupported routine type: " + r.Type ?? "(null)");
        //                 }

        //             }
        //             catch (Exception ex)
        //             {
        //                 SessionLog.Exception(ex);
        //                 // TODO: quit whole process
        //             }

        //         }); // foreach (var r in routineList)

        //         int tick = Environment.TickCount;

        //         var schemaAndRoutineDefs = string.Join("\r\n", jsSchemaLookupForJsFunctions.Select(s => "\tx." + s.Key + " = {\r\n\t\t" + string.Join(",\r\n\t\t", s.Value.ToArray()) + "\r\n\t}\r\n").ToArray());
        //         var tsSchemaAndRoutineDefs = string.Join("\r\n", tsSchemaLookup.Select(s => "\t\tclass " + s.Key + " {\r\n" + string.Join(";\r\n", s.Value.ToArray()) + "\r\n\t\t}\r\n").ToArray());

        //         var finalSB = new StringBuilder(routineContainerTemplate);

        //         jsFile.IncrementVersion();

        //         // record changes against new version

        //         if (changesInFile != null && changesInFile.Count > 0)
        //         {
        //             JsFileChangesTracker.Instance.AddUpdate(endpoint, jsFile, changesInFile.Select(kv => kv.Value).ToList());
        //         }

        //         if (rulesChanged)
        //         {
        //             JsFileChangesTracker.Instance.AddUpdate(endpoint, jsFile, new List<ChangeDescriptor> { ChangeDescriptor.Create("System", "One or more rules changed.") });
        //         }

        //         finalSB.Replace("<<DATE>>", DateTime.Now.ToString("dd MMM yyyy, HH:mm"))
        //             .Replace("<<FILE_VERSION>>", jsFile.Version.ToString())
        //             .Replace("<<SERVER_NAME>>", Environment.MachineName)
        //             .Replace("<<UNIQUE_SCHEMAS>>", string.Join(',', uniqueSchemas.Select(k => $"'{k}'")))
        //             .Replace("<<Catalog>>", jsSafeNamespace)
        //             .Replace("<<ROUTINES>>", schemaAndRoutineDefs)
        //         ;

        //         var finalTypeScriptSB = new StringBuilder();

        //         finalTypeScriptSB = finalTypeScriptSB.Append(typescriptDefinitionsContainer);

        //         // Custom/User types
        //         if (customTypeLookupWithTypeScriptDef.Count > 0)
        //         {
        //             var customTSD = from kv in customTypeLookupWithTypeScriptDef select $"\t\ttype {kv.Key} = {kv.Value};";
        //             typeScriptParameterAndResultTypesSB.Insert(0, string.Join("\r\n", customTSD));
        //         }

        //         var resultAndParameterTypes = typeScriptParameterAndResultTypesSB.ToString();

        //         finalTypeScriptSB.Replace("<<DATE>>", DateTime.Now.ToString("dd MMM yyyy, HH:mm"))
        //             .Replace("<<FILE_VERSION>>", jsFile.Version.ToString())
        //             .Replace("<<SERVER_NAME>>", Environment.MachineName)
        //             .Replace("<<Catalog>>", jsSafeNamespace)
        //             .Replace("<<ResultAndParameterTypes>>", resultAndParameterTypes)
        //             .Replace("<<MethodsStubs>>", tsSchemaAndRoutineDefs)
        //         ;

        //         var typescriptDefinitionsOutput = finalTypeScriptSB.ToString();

        //         var finalOutput = finalSB.ToString();

        //         var filePath = endpoint.OutputFilePath(jsFile);
        //         var minfiedFilePath = endpoint.MinifiedOutputFilePath(jsFile);
        //         var tsTypingsFilePath = endpoint.OutputTypeScriptTypingsFilePath(jsFile);

        //         var minifiedSource = Uglify.Js(finalOutput/*, { }*/).Code;

        //         if (!Directory.Exists(endpoint.OutputDir)) { Directory.CreateDirectory(endpoint.OutputDir); }

        //         var jsFinalBytes = System.Text.Encoding.UTF8.GetBytes(finalOutput);
        //         var jsFinalMinifiedBytes = System.Text.Encoding.UTF8.GetBytes(minifiedSource);

        //         jsFile.ETag = Controllers.PublicController.ComputeETag(jsFinalBytes);
        //         jsFile.ETagMinified = Controllers.PublicController.ComputeETag(jsFinalMinifiedBytes);

        //         File.WriteAllText(filePath, finalOutput);
        //         File.WriteAllText(minfiedFilePath, minifiedSource);
        //         File.WriteAllText(tsTypingsFilePath, typescriptDefinitionsOutput);

        //         time6 += Environment.TickCount - tick;

        //     }
        //     catch (Exception ex)
        //     {
        //         throw;
        //     }
        //     finally
        //     {
        //         if (!noChanges)
        //         {
        //             var overallTime = Environment.TickCount - overAlltick;
        //             SessionLog.InfoToFileOnly($"{correlationGuid} completed in {overallTime}ms\tt1={time1} t2={time2} t3={time3} t4={time4} t5={time5} t6={time6}");
        //         }
        //     }
        // }

        private static Regex ValidJavascriptVarName;

        // returns the column name in quotes if not a valid javascript property name
        public static string QuoteColumnNameIfNecessary(string colName)
        {
            if (ValidJavascriptVarName == null)
            {
                // http://stackoverflow.com/a/9337047
                string pattern = @"^(?!(?:do|if|in|for|let|new|try|var|case|else|enum|eval|false|null|this|true|void|with|break|catch|class|const|super|throw|while|yield|delete|export|import|public|return|static|switch|typeof|default|extends|finally|package|private|continue|debugger|function|arguments|interface|protected|implements|instanceof)$)[$A-Z\_a-z\xaa\xb5\xba\xc0-\xd6\xd8-\xf6\xf8-\u02c1\u02c6-\u02d1\u02e0-\u02e4\u02ec\u02ee\u0370-\u0374\u0376\u0377\u037a-\u037d\u0386\u0388-\u038a\u038c\u038e-\u03a1\u03a3-\u03f5\u03f7-\u0481\u048a-\u0527\u0531-\u0556\u0559\u0561-\u0587\u05d0-\u05ea\u05f0-\u05f2\u0620-\u064a\u066e\u066f\u0671-\u06d3\u06d5\u06e5\u06e6\u06ee\u06ef\u06fa-\u06fc\u06ff\u0710\u0712-\u072f\u074d-\u07a5\u07b1\u07ca-\u07ea\u07f4\u07f5\u07fa\u0800-\u0815\u081a\u0824\u0828\u0840-\u0858\u08a0\u08a2-\u08ac\u0904-\u0939\u093d\u0950\u0958-\u0961\u0971-\u0977\u0979-\u097f\u0985-\u098c\u098f\u0990\u0993-\u09a8\u09aa-\u09b0\u09b2\u09b6-\u09b9\u09bd\u09ce\u09dc\u09dd\u09df-\u09e1\u09f0\u09f1\u0a05-\u0a0a\u0a0f\u0a10\u0a13-\u0a28\u0a2a-\u0a30\u0a32\u0a33\u0a35\u0a36\u0a38\u0a39\u0a59-\u0a5c\u0a5e\u0a72-\u0a74\u0a85-\u0a8d\u0a8f-\u0a91\u0a93-\u0aa8\u0aaa-\u0ab0\u0ab2\u0ab3\u0ab5-\u0ab9\u0abd\u0ad0\u0ae0\u0ae1\u0b05-\u0b0c\u0b0f\u0b10\u0b13-\u0b28\u0b2a-\u0b30\u0b32\u0b33\u0b35-\u0b39\u0b3d\u0b5c\u0b5d\u0b5f-\u0b61\u0b71\u0b83\u0b85-\u0b8a\u0b8e-\u0b90\u0b92-\u0b95\u0b99\u0b9a\u0b9c\u0b9e\u0b9f\u0ba3\u0ba4\u0ba8-\u0baa\u0bae-\u0bb9\u0bd0\u0c05-\u0c0c\u0c0e-\u0c10\u0c12-\u0c28\u0c2a-\u0c33\u0c35-\u0c39\u0c3d\u0c58\u0c59\u0c60\u0c61\u0c85-\u0c8c\u0c8e-\u0c90\u0c92-\u0ca8\u0caa-\u0cb3\u0cb5-\u0cb9\u0cbd\u0cde\u0ce0\u0ce1\u0cf1\u0cf2\u0d05-\u0d0c\u0d0e-\u0d10\u0d12-\u0d3a\u0d3d\u0d4e\u0d60\u0d61\u0d7a-\u0d7f\u0d85-\u0d96\u0d9a-\u0db1\u0db3-\u0dbb\u0dbd\u0dc0-\u0dc6\u0e01-\u0e30\u0e32\u0e33\u0e40-\u0e46\u0e81\u0e82\u0e84\u0e87\u0e88\u0e8a\u0e8d\u0e94-\u0e97\u0e99-\u0e9f\u0ea1-\u0ea3\u0ea5\u0ea7\u0eaa\u0eab\u0ead-\u0eb0\u0eb2\u0eb3\u0ebd\u0ec0-\u0ec4\u0ec6\u0edc-\u0edf\u0f00\u0f40-\u0f47\u0f49-\u0f6c\u0f88-\u0f8c\u1000-\u102a\u103f\u1050-\u1055\u105a-\u105d\u1061\u1065\u1066\u106e-\u1070\u1075-\u1081\u108e\u10a0-\u10c5\u10c7\u10cd\u10d0-\u10fa\u10fc-\u1248\u124a-\u124d\u1250-\u1256\u1258\u125a-\u125d\u1260-\u1288\u128a-\u128d\u1290-\u12b0\u12b2-\u12b5\u12b8-\u12be\u12c0\u12c2-\u12c5\u12c8-\u12d6\u12d8-\u1310\u1312-\u1315\u1318-\u135a\u1380-\u138f\u13a0-\u13f4\u1401-\u166c\u166f-\u167f\u1681-\u169a\u16a0-\u16ea\u16ee-\u16f0\u1700-\u170c\u170e-\u1711\u1720-\u1731\u1740-\u1751\u1760-\u176c\u176e-\u1770\u1780-\u17b3\u17d7\u17dc\u1820-\u1877\u1880-\u18a8\u18aa\u18b0-\u18f5\u1900-\u191c\u1950-\u196d\u1970-\u1974\u1980-\u19ab\u19c1-\u19c7\u1a00-\u1a16\u1a20-\u1a54\u1aa7\u1b05-\u1b33\u1b45-\u1b4b\u1b83-\u1ba0\u1bae\u1baf\u1bba-\u1be5\u1c00-\u1c23\u1c4d-\u1c4f\u1c5a-\u1c7d\u1ce9-\u1cec\u1cee-\u1cf1\u1cf5\u1cf6\u1d00-\u1dbf\u1e00-\u1f15\u1f18-\u1f1d\u1f20-\u1f45\u1f48-\u1f4d\u1f50-\u1f57\u1f59\u1f5b\u1f5d\u1f5f-\u1f7d\u1f80-\u1fb4\u1fb6-\u1fbc\u1fbe\u1fc2-\u1fc4\u1fc6-\u1fcc\u1fd0-\u1fd3\u1fd6-\u1fdb\u1fe0-\u1fec\u1ff2-\u1ff4\u1ff6-\u1ffc\u2071\u207f\u2090-\u209c\u2102\u2107\u210a-\u2113\u2115\u2119-\u211d\u2124\u2126\u2128\u212a-\u212d\u212f-\u2139\u213c-\u213f\u2145-\u2149\u214e\u2160-\u2188\u2c00-\u2c2e\u2c30-\u2c5e\u2c60-\u2ce4\u2ceb-\u2cee\u2cf2\u2cf3\u2d00-\u2d25\u2d27\u2d2d\u2d30-\u2d67\u2d6f\u2d80-\u2d96\u2da0-\u2da6\u2da8-\u2dae\u2db0-\u2db6\u2db8-\u2dbe\u2dc0-\u2dc6\u2dc8-\u2dce\u2dd0-\u2dd6\u2dd8-\u2dde\u2e2f\u3005-\u3007\u3021-\u3029\u3031-\u3035\u3038-\u303c\u3041-\u3096\u309d-\u309f\u30a1-\u30fa\u30fc-\u30ff\u3105-\u312d\u3131-\u318e\u31a0-\u31ba\u31f0-\u31ff\u3400-\u4db5\u4e00-\u9fcc\ua000-\ua48c\ua4d0-\ua4fd\ua500-\ua60c\ua610-\ua61f\ua62a\ua62b\ua640-\ua66e\ua67f-\ua697\ua6a0-\ua6ef\ua717-\ua71f\ua722-\ua788\ua78b-\ua78e\ua790-\ua793\ua7a0-\ua7aa\ua7f8-\ua801\ua803-\ua805\ua807-\ua80a\ua80c-\ua822\ua840-\ua873\ua882-\ua8b3\ua8f2-\ua8f7\ua8fb\ua90a-\ua925\ua930-\ua946\ua960-\ua97c\ua984-\ua9b2\ua9cf\uaa00-\uaa28\uaa40-\uaa42\uaa44-\uaa4b\uaa60-\uaa76\uaa7a\uaa80-\uaaaf\uaab1\uaab5\uaab6\uaab9-\uaabd\uaac0\uaac2\uaadb-\uaadd\uaae0-\uaaea\uaaf2-\uaaf4\uab01-\uab06\uab09-\uab0e\uab11-\uab16\uab20-\uab26\uab28-\uab2e\uabc0-\uabe2\uac00-\ud7a3\ud7b0-\ud7c6\ud7cb-\ud7fb\uf900-\ufa6d\ufa70-\ufad9\ufb00-\ufb06\ufb13-\ufb17\ufb1d\ufb1f-\ufb28\ufb2a-\ufb36\ufb38-\ufb3c\ufb3e\ufb40\ufb41\ufb43\ufb44\ufb46-\ufbb1\ufbd3-\ufd3d\ufd50-\ufd8f\ufd92-\ufdc7\ufdf0-\ufdfb\ufe70-\ufe74\ufe76-\ufefc\uff21-\uff3a\uff41-\uff5a\uff66-\uffbe\uffc2-\uffc7\uffca-\uffcf\uffd2-\uffd7\uffda-\uffdc][$A-Z\_a-z\xaa\xb5\xba\xc0-\xd6\xd8-\xf6\xf8-\u02c1\u02c6-\u02d1\u02e0-\u02e4\u02ec\u02ee\u0370-\u0374\u0376\u0377\u037a-\u037d\u0386\u0388-\u038a\u038c\u038e-\u03a1\u03a3-\u03f5\u03f7-\u0481\u048a-\u0527\u0531-\u0556\u0559\u0561-\u0587\u05d0-\u05ea\u05f0-\u05f2\u0620-\u064a\u066e\u066f\u0671-\u06d3\u06d5\u06e5\u06e6\u06ee\u06ef\u06fa-\u06fc\u06ff\u0710\u0712-\u072f\u074d-\u07a5\u07b1\u07ca-\u07ea\u07f4\u07f5\u07fa\u0800-\u0815\u081a\u0824\u0828\u0840-\u0858\u08a0\u08a2-\u08ac\u0904-\u0939\u093d\u0950\u0958-\u0961\u0971-\u0977\u0979-\u097f\u0985-\u098c\u098f\u0990\u0993-\u09a8\u09aa-\u09b0\u09b2\u09b6-\u09b9\u09bd\u09ce\u09dc\u09dd\u09df-\u09e1\u09f0\u09f1\u0a05-\u0a0a\u0a0f\u0a10\u0a13-\u0a28\u0a2a-\u0a30\u0a32\u0a33\u0a35\u0a36\u0a38\u0a39\u0a59-\u0a5c\u0a5e\u0a72-\u0a74\u0a85-\u0a8d\u0a8f-\u0a91\u0a93-\u0aa8\u0aaa-\u0ab0\u0ab2\u0ab3\u0ab5-\u0ab9\u0abd\u0ad0\u0ae0\u0ae1\u0b05-\u0b0c\u0b0f\u0b10\u0b13-\u0b28\u0b2a-\u0b30\u0b32\u0b33\u0b35-\u0b39\u0b3d\u0b5c\u0b5d\u0b5f-\u0b61\u0b71\u0b83\u0b85-\u0b8a\u0b8e-\u0b90\u0b92-\u0b95\u0b99\u0b9a\u0b9c\u0b9e\u0b9f\u0ba3\u0ba4\u0ba8-\u0baa\u0bae-\u0bb9\u0bd0\u0c05-\u0c0c\u0c0e-\u0c10\u0c12-\u0c28\u0c2a-\u0c33\u0c35-\u0c39\u0c3d\u0c58\u0c59\u0c60\u0c61\u0c85-\u0c8c\u0c8e-\u0c90\u0c92-\u0ca8\u0caa-\u0cb3\u0cb5-\u0cb9\u0cbd\u0cde\u0ce0\u0ce1\u0cf1\u0cf2\u0d05-\u0d0c\u0d0e-\u0d10\u0d12-\u0d3a\u0d3d\u0d4e\u0d60\u0d61\u0d7a-\u0d7f\u0d85-\u0d96\u0d9a-\u0db1\u0db3-\u0dbb\u0dbd\u0dc0-\u0dc6\u0e01-\u0e30\u0e32\u0e33\u0e40-\u0e46\u0e81\u0e82\u0e84\u0e87\u0e88\u0e8a\u0e8d\u0e94-\u0e97\u0e99-\u0e9f\u0ea1-\u0ea3\u0ea5\u0ea7\u0eaa\u0eab\u0ead-\u0eb0\u0eb2\u0eb3\u0ebd\u0ec0-\u0ec4\u0ec6\u0edc-\u0edf\u0f00\u0f40-\u0f47\u0f49-\u0f6c\u0f88-\u0f8c\u1000-\u102a\u103f\u1050-\u1055\u105a-\u105d\u1061\u1065\u1066\u106e-\u1070\u1075-\u1081\u108e\u10a0-\u10c5\u10c7\u10cd\u10d0-\u10fa\u10fc-\u1248\u124a-\u124d\u1250-\u1256\u1258\u125a-\u125d\u1260-\u1288\u128a-\u128d\u1290-\u12b0\u12b2-\u12b5\u12b8-\u12be\u12c0\u12c2-\u12c5\u12c8-\u12d6\u12d8-\u1310\u1312-\u1315\u1318-\u135a\u1380-\u138f\u13a0-\u13f4\u1401-\u166c\u166f-\u167f\u1681-\u169a\u16a0-\u16ea\u16ee-\u16f0\u1700-\u170c\u170e-\u1711\u1720-\u1731\u1740-\u1751\u1760-\u176c\u176e-\u1770\u1780-\u17b3\u17d7\u17dc\u1820-\u1877\u1880-\u18a8\u18aa\u18b0-\u18f5\u1900-\u191c\u1950-\u196d\u1970-\u1974\u1980-\u19ab\u19c1-\u19c7\u1a00-\u1a16\u1a20-\u1a54\u1aa7\u1b05-\u1b33\u1b45-\u1b4b\u1b83-\u1ba0\u1bae\u1baf\u1bba-\u1be5\u1c00-\u1c23\u1c4d-\u1c4f\u1c5a-\u1c7d\u1ce9-\u1cec\u1cee-\u1cf1\u1cf5\u1cf6\u1d00-\u1dbf\u1e00-\u1f15\u1f18-\u1f1d\u1f20-\u1f45\u1f48-\u1f4d\u1f50-\u1f57\u1f59\u1f5b\u1f5d\u1f5f-\u1f7d\u1f80-\u1fb4\u1fb6-\u1fbc\u1fbe\u1fc2-\u1fc4\u1fc6-\u1fcc\u1fd0-\u1fd3\u1fd6-\u1fdb\u1fe0-\u1fec\u1ff2-\u1ff4\u1ff6-\u1ffc\u2071\u207f\u2090-\u209c\u2102\u2107\u210a-\u2113\u2115\u2119-\u211d\u2124\u2126\u2128\u212a-\u212d\u212f-\u2139\u213c-\u213f\u2145-\u2149\u214e\u2160-\u2188\u2c00-\u2c2e\u2c30-\u2c5e\u2c60-\u2ce4\u2ceb-\u2cee\u2cf2\u2cf3\u2d00-\u2d25\u2d27\u2d2d\u2d30-\u2d67\u2d6f\u2d80-\u2d96\u2da0-\u2da6\u2da8-\u2dae\u2db0-\u2db6\u2db8-\u2dbe\u2dc0-\u2dc6\u2dc8-\u2dce\u2dd0-\u2dd6\u2dd8-\u2dde\u2e2f\u3005-\u3007\u3021-\u3029\u3031-\u3035\u3038-\u303c\u3041-\u3096\u309d-\u309f\u30a1-\u30fa\u30fc-\u30ff\u3105-\u312d\u3131-\u318e\u31a0-\u31ba\u31f0-\u31ff\u3400-\u4db5\u4e00-\u9fcc\ua000-\ua48c\ua4d0-\ua4fd\ua500-\ua60c\ua610-\ua61f\ua62a\ua62b\ua640-\ua66e\ua67f-\ua697\ua6a0-\ua6ef\ua717-\ua71f\ua722-\ua788\ua78b-\ua78e\ua790-\ua793\ua7a0-\ua7aa\ua7f8-\ua801\ua803-\ua805\ua807-\ua80a\ua80c-\ua822\ua840-\ua873\ua882-\ua8b3\ua8f2-\ua8f7\ua8fb\ua90a-\ua925\ua930-\ua946\ua960-\ua97c\ua984-\ua9b2\ua9cf\uaa00-\uaa28\uaa40-\uaa42\uaa44-\uaa4b\uaa60-\uaa76\uaa7a\uaa80-\uaaaf\uaab1\uaab5\uaab6\uaab9-\uaabd\uaac0\uaac2\uaadb-\uaadd\uaae0-\uaaea\uaaf2-\uaaf4\uab01-\uab06\uab09-\uab0e\uab11-\uab16\uab20-\uab26\uab28-\uab2e\uabc0-\uabe2\uac00-\ud7a3\ud7b0-\ud7c6\ud7cb-\ud7fb\uf900-\ufa6d\ufa70-\ufad9\ufb00-\ufb06\ufb13-\ufb17\ufb1d\ufb1f-\ufb28\ufb2a-\ufb36\ufb38-\ufb3c\ufb3e\ufb40\ufb41\ufb43\ufb44\ufb46-\ufbb1\ufbd3-\ufd3d\ufd50-\ufd8f\ufd92-\ufdc7\ufdf0-\ufdfb\ufe70-\ufe74\ufe76-\ufefc\uff21-\uff3a\uff41-\uff5a\uff66-\uffbe\uffc2-\uffc7\uffca-\uffcf\uffd2-\uffd7\uffda-\uffdc0-9\u0300-\u036f\u0483-\u0487\u0591-\u05bd\u05bf\u05c1\u05c2\u05c4\u05c5\u05c7\u0610-\u061a\u064b-\u0669\u0670\u06d6-\u06dc\u06df-\u06e4\u06e7\u06e8\u06ea-\u06ed\u06f0-\u06f9\u0711\u0730-\u074a\u07a6-\u07b0\u07c0-\u07c9\u07eb-\u07f3\u0816-\u0819\u081b-\u0823\u0825-\u0827\u0829-\u082d\u0859-\u085b\u08e4-\u08fe\u0900-\u0903\u093a-\u093c\u093e-\u094f\u0951-\u0957\u0962\u0963\u0966-\u096f\u0981-\u0983\u09bc\u09be-\u09c4\u09c7\u09c8\u09cb-\u09cd\u09d7\u09e2\u09e3\u09e6-\u09ef\u0a01-\u0a03\u0a3c\u0a3e-\u0a42\u0a47\u0a48\u0a4b-\u0a4d\u0a51\u0a66-\u0a71\u0a75\u0a81-\u0a83\u0abc\u0abe-\u0ac5\u0ac7-\u0ac9\u0acb-\u0acd\u0ae2\u0ae3\u0ae6-\u0aef\u0b01-\u0b03\u0b3c\u0b3e-\u0b44\u0b47\u0b48\u0b4b-\u0b4d\u0b56\u0b57\u0b62\u0b63\u0b66-\u0b6f\u0b82\u0bbe-\u0bc2\u0bc6-\u0bc8\u0bca-\u0bcd\u0bd7\u0be6-\u0bef\u0c01-\u0c03\u0c3e-\u0c44\u0c46-\u0c48\u0c4a-\u0c4d\u0c55\u0c56\u0c62\u0c63\u0c66-\u0c6f\u0c82\u0c83\u0cbc\u0cbe-\u0cc4\u0cc6-\u0cc8\u0cca-\u0ccd\u0cd5\u0cd6\u0ce2\u0ce3\u0ce6-\u0cef\u0d02\u0d03\u0d3e-\u0d44\u0d46-\u0d48\u0d4a-\u0d4d\u0d57\u0d62\u0d63\u0d66-\u0d6f\u0d82\u0d83\u0dca\u0dcf-\u0dd4\u0dd6\u0dd8-\u0ddf\u0df2\u0df3\u0e31\u0e34-\u0e3a\u0e47-\u0e4e\u0e50-\u0e59\u0eb1\u0eb4-\u0eb9\u0ebb\u0ebc\u0ec8-\u0ecd\u0ed0-\u0ed9\u0f18\u0f19\u0f20-\u0f29\u0f35\u0f37\u0f39\u0f3e\u0f3f\u0f71-\u0f84\u0f86\u0f87\u0f8d-\u0f97\u0f99-\u0fbc\u0fc6\u102b-\u103e\u1040-\u1049\u1056-\u1059\u105e-\u1060\u1062-\u1064\u1067-\u106d\u1071-\u1074\u1082-\u108d\u108f-\u109d\u135d-\u135f\u1712-\u1714\u1732-\u1734\u1752\u1753\u1772\u1773\u17b4-\u17d3\u17dd\u17e0-\u17e9\u180b-\u180d\u1810-\u1819\u18a9\u1920-\u192b\u1930-\u193b\u1946-\u194f\u19b0-\u19c0\u19c8\u19c9\u19d0-\u19d9\u1a17-\u1a1b\u1a55-\u1a5e\u1a60-\u1a7c\u1a7f-\u1a89\u1a90-\u1a99\u1b00-\u1b04\u1b34-\u1b44\u1b50-\u1b59\u1b6b-\u1b73\u1b80-\u1b82\u1ba1-\u1bad\u1bb0-\u1bb9\u1be6-\u1bf3\u1c24-\u1c37\u1c40-\u1c49\u1c50-\u1c59\u1cd0-\u1cd2\u1cd4-\u1ce8\u1ced\u1cf2-\u1cf4\u1dc0-\u1de6\u1dfc-\u1dff\u200c\u200d\u203f\u2040\u2054\u20d0-\u20dc\u20e1\u20e5-\u20f0\u2cef-\u2cf1\u2d7f\u2de0-\u2dff\u302a-\u302f\u3099\u309a\ua620-\ua629\ua66f\ua674-\ua67d\ua69f\ua6f0\ua6f1\ua802\ua806\ua80b\ua823-\ua827\ua880\ua881\ua8b4-\ua8c4\ua8d0-\ua8d9\ua8e0-\ua8f1\ua900-\ua909\ua926-\ua92d\ua947-\ua953\ua980-\ua983\ua9b3-\ua9c0\ua9d0-\ua9d9\uaa29-\uaa36\uaa43\uaa4c\uaa4d\uaa50-\uaa59\uaa7b\uaab0\uaab2-\uaab4\uaab7\uaab8\uaabe\uaabf\uaac1\uaaeb-\uaaef\uaaf5\uaaf6\uabe3-\uabea\uabec\uabed\uabf0-\uabf9\ufb1e\ufe00-\ufe0f\ufe20-\ufe26\ufe33\ufe34\ufe4d-\ufe4f\uff10-\uff19\uff3f]*$";
                ValidJavascriptVarName = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.ECMAScript);
            }

            if (ValidJavascriptVarName.IsMatch(colName)) return colName;
            else return "\"" + colName + "\"";
        }

    }
}
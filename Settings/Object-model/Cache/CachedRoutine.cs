using System;
using System.Linq;
using System.Collections.Generic;
//using Newtonsoft.Json;
using System.Text;
using System.Collections.Concurrent;
using System.Text.Json.Serialization;

namespace jsdal_server_core.Settings.ObjectModel
{
    [Serializable]
    public enum RoutineIncludeExcludeInstructionSource
    {
        Unknown = 0,
        DatabaseMetadata = 10,
        DbSourceLevel = 20,
        JsFileLevel = 30
    }

    [Serializable]
    public class RoutineIncludeExcludeInstruction
    {
        public BaseRule Rule;
        public bool? Included;
        public bool? Excluded;
        public string Reason;
        public RoutineIncludeExcludeInstructionSource? Source;

        public static RoutineIncludeExcludeInstruction Create(CachedRoutine routine, List<BaseRule> appRules, DefaultRuleMode defaultRuleMode, List<BaseRule> fileRules = null)
        {
            var instruction = new RoutineIncludeExcludeInstruction();

            // apply Metadata first
            if (routine.jsDALMetadata != null && routine.jsDALMetadata.jsDAL != null)
            {
                if (routine.jsDALMetadata.jsDAL != null)
                {
                    if (routine.jsDALMetadata.jsDAL.exclude ?? false)
                    {
                        instruction.Source = RoutineIncludeExcludeInstructionSource.DatabaseMetadata;
                        instruction.Excluded = routine.jsDALMetadata.jsDAL.exclude;
                        if (instruction.Excluded ?? false) instruction.Reason = "T-SQL metadata";
                    }
                    else if (!routine.jsDALMetadata.jsDAL.include ?? false)
                    {
                        instruction.Source = RoutineIncludeExcludeInstructionSource.DatabaseMetadata;
                        instruction.Included = routine.jsDALMetadata.jsDAL.include;
                        if (instruction.Included ?? false) instruction.Reason = "T-SQL metadata";
                    }
                }
            }

            if (instruction.Reason != null) return instruction;

            // always apply APP-level rules first
            foreach (var rule in appRules)
            {
                if (rule.Apply(routine))
                {
                    if (defaultRuleMode == DefaultRuleMode.ExcludeAll)
                    {
                        instruction.Included = true;
                        instruction.Reason = rule.ToString();
                    }
                    else if (defaultRuleMode == DefaultRuleMode.IncludeAll)
                    {
                        instruction.Excluded = true;
                        instruction.Reason = rule.ToString();
                    }
                    else throw new Exception("Unsupported DefaultRuleMode: " + defaultRuleMode.ToString());

                    instruction.Rule = rule;
                    instruction.Source = RoutineIncludeExcludeInstructionSource.DbSourceLevel;

                    return instruction;
                }
            };

            if (instruction.Rule != null) return instruction;

            // apply JSFile level
            if (fileRules != null)
            {
                foreach (var fileRule in fileRules)
                {
                    if (fileRule == null) continue;

                    if (fileRule.Apply(routine))
                    {
                        if (defaultRuleMode == DefaultRuleMode.ExcludeAll)
                        {
                            instruction.Included = true;
                            instruction.Reason = fileRule.ToString(); // TODO: Consider recording a more substantial reference to the rule
                        }
                        else if (defaultRuleMode == DefaultRuleMode.IncludeAll)
                        {
                            instruction.Excluded = true;
                            instruction.Reason = fileRule.ToString();
                        }
                        else throw new Exception("Unsupported DefaultRuleMode: " + defaultRuleMode.ToString());

                        instruction.Rule = fileRule;
                        instruction.Source = RoutineIncludeExcludeInstructionSource.JsFileLevel;

                        return instruction;
                    }

                };

            }

            if (defaultRuleMode == DefaultRuleMode.ExcludeAll) instruction.Excluded = true;
            else instruction.Included = true;

            instruction.Rule = null;
            instruction.Source = RoutineIncludeExcludeInstructionSource.DbSourceLevel;
            instruction.Reason = "Default";

            return instruction;
        }

    }


    [Serializable]
    public class CachedRoutine
    {
        public string Schema;
        public string Routine;
        
        [JsonConverter(typeof(InternedStringConverter))]
        public string Type; //e.g. Proc, UDF or Table-valued function
        public long RowVer;
        public string ParametersHash;
        public List<RoutineParameterV2> Parameters;
        public long ResultSetRowver;
        public Dictionary<string/*TableName*/, List<ResultSetFieldMetadata>> ResultSetMetadata;
        public string ResultSetHash;
        public string ResultSetError;
        public jsDALMetadata jsDALMetadata;
        public bool IsDeleted;

        public string TypescriptParameterTypeDefinition { get; set; }
        public string TypescriptOutputParameterTypeDefinition { get; set; }

        //?public List<string> TypescriptResultTypes { get; set; }
        public string TypescriptResultSetDefinitions { get; set; }
        public string TypescriptMethodStub { get; set; }

        public string PrecalcError { get; set; }


        [Newtonsoft.Json.JsonIgnore]
        public string FullName { get { return $"[{this.Schema}].[{this.Routine}]"; } }

        // TODO:  RuleInstructions require revisting. Don't believe it should live on the CachedRoutine itself. Rather each JsFile should calculate it's own list???
        [Newtonsoft.Json.JsonIgnore]
        public Dictionary<JsFile/*If null then DB-level*/, RoutineIncludeExcludeInstruction> RuleInstructions;

        public CachedRoutine()
        {
            this.RuleInstructions = new Dictionary<JsFile, RoutineIncludeExcludeInstruction>();
        }

        // public int RoughSizeInBytes()
        // {
        //     var s= Schema.ByteSize() + Routine.ByteSize() + Type.ByteSize() + ParametersHash.ByteSize()
        //             + sizeof(long/*RowVer*/) + sizeof(long/*ResultSetRowver*/) + ResultSetHash.ByteSize() + ResultSetError.ByteSize()
        //             + TypescriptParameterTypeDefinition.ByteSize() + TypescriptOutputParameterTypeDefinition.ByteSize()
        //             + TypescriptResultSetDefinitions.ByteSize() + TypescriptMethodStub.ByteSize() + sizeof(bool/*IsDeleted*/);



        //     /*


        // public bool IsDeleted;


        // public List<RoutineParameterV2> Parameters;
        // public Dictionary<string, List < ResultSetFieldMetadata >> ResultSetMetadata;

        // public jsDALMetadata jsDALMetadata;



        //     */

        // }

        public bool Equals(CachedRoutine r)
        {
            return this.Schema.Equals(r.Schema, StringComparison.OrdinalIgnoreCase)
                && this.Routine.Equals(r.Routine, StringComparison.OrdinalIgnoreCase);
        }

        public bool Equals(string schema, string routineName)
        {
            if (schema.StartsWith('[') && schema.EndsWith(']')) schema = schema.Substring(1, schema.Length - 2);
            if (routineName.StartsWith('[') && routineName.EndsWith(']')) routineName = routineName.Substring(1, routineName.Length - 2);

            return this.Schema.Equals(schema, StringComparison.OrdinalIgnoreCase)
            && this.Routine.Equals(routineName, StringComparison.OrdinalIgnoreCase);
        }

        public bool Contains(string txt)
        {
            string schema = null;

            if (txt.Contains('.'))
            {
                schema = txt.Substring(0, txt.IndexOf('.'));
                txt = txt.Substring(schema.Length + 1);

                return this.Schema.Contains(schema, StringComparison.OrdinalIgnoreCase) && this.Routine.Contains(txt, StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                return this.Schema.Contains(txt, StringComparison.OrdinalIgnoreCase) || this.Routine.Contains(txt, StringComparison.OrdinalIgnoreCase);
            }
        }

        public bool EqualsQuery(string txt)
        {
            string schema = null;

            if (txt.Contains('.'))
            {
                schema = txt.Substring(0, txt.IndexOf('.'));
                txt = txt.Substring(schema.Length + 1);

                return this.Equals(schema, txt);
            }
            else
            {
                return this.FullName.Equals(txt, StringComparison.OrdinalIgnoreCase);
            }
        }

        public RoutineIncludeExcludeInstruction ApplyRules(Application app, JsFile jsFileContext)
        {
            var instruction = new RoutineIncludeExcludeInstruction();

            // apply Metadata first
            if (this.jsDALMetadata != null && this.jsDALMetadata.jsDAL != null)
            {
                if (this.jsDALMetadata.jsDAL != null)
                {
                    if (this.jsDALMetadata.jsDAL.exclude ?? false)
                    {
                        instruction.Source = RoutineIncludeExcludeInstructionSource.DatabaseMetadata;
                        instruction.Excluded = this.jsDALMetadata.jsDAL.exclude;
                        if (instruction.Excluded ?? false) instruction.Reason = "T-SQL metadata";
                    }
                    else if (!this.jsDALMetadata.jsDAL.include ?? false)
                    {
                        instruction.Source = RoutineIncludeExcludeInstructionSource.DatabaseMetadata;
                        instruction.Included = this.jsDALMetadata.jsDAL.include;
                        if (instruction.Included ?? false) instruction.Reason = "T-SQL metadata";
                    }
                }
            }

            if (instruction.Reason != null) return instruction;

            // apply DB source level
            foreach (var dbRule in app.Rules)
            {

                if (dbRule == null) continue;

                if (dbRule.Apply(this))
                {
                    if (app.DefaultRuleMode == (int)DefaultRuleMode.ExcludeAll)
                    {
                        instruction.Included = true;
                        instruction.Reason = dbRule.ToString();
                    }
                    else if (app.DefaultRuleMode == (int)DefaultRuleMode.IncludeAll)
                    {
                        instruction.Excluded = true;
                        instruction.Reason = dbRule.ToString();
                    }
                    else throw new Exception("Unsupported DefaultRuleMode: " + app.DefaultRuleMode);

                    instruction.Rule = dbRule;
                    instruction.Source = RoutineIncludeExcludeInstructionSource.DbSourceLevel;

                    return instruction;
                }
            };



            if (instruction.Rule != null) return instruction;

            // apply JSFile level
            if (jsFileContext != null)
            {
                foreach (var fileRule in jsFileContext.Rules)
                {
                    if (fileRule == null) continue;

                    if (fileRule.Apply(this))
                    {
                        if (app.DefaultRuleMode == (int)DefaultRuleMode.ExcludeAll)
                        {
                            instruction.Included = true;
                            instruction.Reason = fileRule.ToString(); // TODO: Consider recording a more substantial reference to the rule
                        }
                        else if (app.DefaultRuleMode == (int)DefaultRuleMode.IncludeAll)
                        {
                            instruction.Excluded = true;
                            instruction.Reason = fileRule.ToString();
                        }
                        else throw new Exception("Unsupported DefaultRuleMode: " + app.DefaultRuleMode);

                        instruction.Rule = fileRule;
                        instruction.Source = RoutineIncludeExcludeInstructionSource.JsFileLevel;

                        return instruction;
                    }

                };

            }

            if (app.DefaultRuleMode == (int)DefaultRuleMode.ExcludeAll) instruction.Excluded = true;
            else instruction.Included = true;

            instruction.Rule = null;
            instruction.Source = RoutineIncludeExcludeInstructionSource.DbSourceLevel;
            instruction.Reason = "Default";
            return instruction;
        }

        // compute & persist some of the TypeScript definitions for reuse during .js generation
        public void PrecalculateJsGenerationValues(Endpoint endpoint)
        {
            try
            {
                if (this.IsDeleted)
                {
                    this.TypescriptOutputParameterTypeDefinition = this.TypescriptParameterTypeDefinition = this.TypescriptMethodStub = this.TypescriptResultSetDefinitions = null;
                    return;
                }

                // TODO: Figure out what to do with JsNamespace CASING. Sometimes case changes in connection string and that breaks all existing code
                string jsNamespace = null;//endpoint.JsNamespace;
                if (string.IsNullOrWhiteSpace(jsNamespace)) jsNamespace = endpoint.MetadataConnection.InitialCatalog;

                if (string.IsNullOrWhiteSpace(jsNamespace)) jsNamespace = "NotSet";

                var jsSafeNamespace = JsFileGenerator.MakeNameJsSafe(jsNamespace);

                var jsSchemaName = JsFileGenerator.MakeNameJsSafe(this.Schema);
                var jsFunctionName = JsFileGenerator.MakeNameJsSafe(this.Routine);

                var customTypeLookupWithTypeScriptDef = endpoint.CustomTypeLookupWithTypeScriptDef;

                var paramTypeScriptDefList = new List<string>();

                this.Parameters.Where(p => !string.IsNullOrEmpty(p.Name)).ToList().ForEach(p =>
                  {
                      var tsType = RoutineParameterV2.GetTypescriptTypeFromSql(p.SqlDataType, p.CustomType, ref customTypeLookupWithTypeScriptDef);
                      var name = p.Name.TrimStart('@');

                      if (JsFileGenerator.StartsWithNum(name)) name = "$" + name;

                      var tsDefinition = $"{name}{(p.HasDefault ? "?" : "")}: {tsType}";

                      paramTypeScriptDefList.Add(tsDefinition);
                  });

                var tsParameterTypeDefName = $"{ jsSafeNamespace }_{ jsSchemaName }_{ jsFunctionName }Parameters";

                // TypeScript input parameter definitions
                this.TypescriptParameterTypeDefinition = $"type {tsParameterTypeDefName} = {{ { string.Join(", ", paramTypeScriptDefList) } }}";

                // TypeScript output parameter definitions
                var tsOutputParamDefs = (from p in this.Parameters
                                         where !string.IsNullOrEmpty(p.Name)
                                           && !p.IsResult
                                           && p.IsOutput
                                         select string.Format("{0}?: {1}", JsFileGenerator.StartsWithNum(p.Name.TrimStart('@')) ? (/*"_" + */p.Name.TrimStart('@')) : p.Name.TrimStart('@')
                                          , RoutineParameterV2.GetTypescriptTypeFromSql(p.SqlDataType, p.CustomType, ref customTypeLookupWithTypeScriptDef))).ToList();

                string typeScriptOutputParameterTypeName = null;

                if (tsOutputParamDefs.Count > 0)
                {
                    typeScriptOutputParameterTypeName = string.Format("{0}_{1}_{2}OutputParms", jsSafeNamespace, jsSchemaName, jsFunctionName);

                    // TypeScript OUPUT parameter type definition
                    this.TypescriptOutputParameterTypeDefinition = string.Format("type {0} = {{ {1} }}", typeScriptOutputParameterTypeName, string.Join(", ", tsOutputParamDefs));
                }

                var resultTypes = new List<string>();

                if (this.ResultSetMetadata != null)
                {
                    (resultTypes, this.TypescriptResultSetDefinitions) = BuildResultSetTypescriptDefs(jsSafeNamespace, jsSchemaName, jsFunctionName, ref customTypeLookupWithTypeScriptDef);
                }

                this.TypescriptMethodStub = BuildMethodTypescriptStub(jsFunctionName, tsParameterTypeDefName, typeScriptOutputParameterTypeName, resultTypes, ref customTypeLookupWithTypeScriptDef);
            }
            catch (Exception ex)
            {
                ExceptionLogger.LogExceptionThrottled(ex, "PrecalculateJsGenerationValues", 2, $"{endpoint.Pedigree} - {this.Schema}.{this.Routine}");
            }
        }

        private string BuildMethodTypescriptStub(string jsFunctionName, string tsParameterTypeDefName,
                string typeScriptOutputParameterTypeName,
                List<string> resultTypes,
                ref ConcurrentDictionary<string, string> customTypeLookupWithTypeScriptDef)
        {
            var tsArguments = "";

            if (!string.IsNullOrEmpty(tsParameterTypeDefName))
            {
                tsArguments = string.Format("_.{0}", tsParameterTypeDefName);
            }
            else
            {
                tsArguments = "{ }";
            }

            if (this.Type.Equals("FUNCTION", StringComparison.OrdinalIgnoreCase))
            {
                if (this.Parameters != null)
                {
                    var resultParm = this.Parameters.FirstOrDefault(p => p.IsResult);

                    if (resultParm == null) return null;

                    var tsMethodStub = string.Format("static {0}(parameters?: {1}): IUDFExecGeneric<{2}, {1}>", jsFunctionName, tsArguments,
                            RoutineParameterV2.GetTypescriptTypeFromSql(resultParm.SqlDataType, resultParm.CustomType,
                            ref customTypeLookupWithTypeScriptDef));

                    //!tsSchemaLookup[jsSchemaName].Add(tsMethodStub);

                    return tsMethodStub;
                }
                else
                {
                    return null;
                    //!SessionLog.Warning($"Cannot generate UDF method stub because the parameters collection is empty. Does the object still exist? Endpoint = {endpoint.Name} ({endpoint.Id}), Routine={r.FullName}");
                }

            }
            else
            {
                var outputParmType = "void";
                // check if there are output parameters present
                if (!string.IsNullOrWhiteSpace(typeScriptOutputParameterTypeName))
                {
                    outputParmType = "_." + typeScriptOutputParameterTypeName;
                }

                if (resultTypes.Count > 0)
                {
                    var cnt = resultTypes.Count;

                    // cap ISprocExecGeneric to max available. Intellisense will not be able to show the additional results sets although they will be present
                    if (cnt > Constants.MAX_NUMBER_OF_RESULTS)
                    {
                        //!this.Log.Warning("Result set metadata is capped at {0} result sets. Intellisense will not show more than that although they will be accessible at run-time.", Constants.MAX_NUMBER_OF_RESULTS);
                        cnt = Constants.MAX_NUMBER_OF_RESULTS;
                    }

                    var tsMethodStub = string.Format("static {0}(parameters?: {1}): ISprocExecGeneric{3}<{4}, {2}, {1}>", jsFunctionName, tsArguments, string.Join(",", resultTypes.Take(Constants.MAX_NUMBER_OF_RESULTS).Select(rt => "_." + rt)), cnt, outputParmType);

                    //!tsSchemaLookup[jsSchemaName].Add(tsMethodStub);

                    return tsMethodStub;
                }
                else
                { // NO RESULTSET
                    var tsMethodStub = string.Format("static {0}(parameters?: {1}): ISprocExecGeneric0<{2}, {1}>", jsFunctionName, tsArguments, outputParmType);

                    //!tsSchemaLookup[jsSchemaName].Add(tsMethodStub);
                    return tsMethodStub;
                }

            }
        }

        private (List<string>, string) BuildResultSetTypescriptDefs(string jsSafeNamespace, string jsSchemaName, string jsFunctionName, ref ConcurrentDictionary<string, string> customTypeLookupWithTypeScriptDef)
        {
            var resultTypes = new List<string>();
            var typeScriptParameterAndResultTypesSB = new StringBuilder();

            int resultIx = 0;

            foreach (var kv in this.ResultSetMetadata)
            {
                var columnCounter = new SortedList<string, int>();

                // e.g. Icev0_Accpac_ItemMakeMappingGetListResult0
                var tsResultTypeDefName = string.Format("{0}_{1}_{2}Result{3}", jsSafeNamespace, jsSchemaName, jsFunctionName, resultIx++);

                var tsColumnDefs = new List<string>();

                int nonNameColIx = 0;

                //kv.Value = Table[0...N]
                foreach (var col in kv.Value)
                {
                    var colName = col.ColumnName.ToString();
                    var dbDataType = col.DbDataType.ToString();

                    if (string.IsNullOrWhiteSpace(colName))
                    {
                        colName = string.Format("Unknown{0:000}", ++nonNameColIx);
                    }

                    var originalColName = colName;

                    // handle duplicate column names
                    if (columnCounter.ContainsKey(colName))
                    {
                        //!this.Log.Warning("Duplicate column name encountered for column '{0}' on routine {1}", colName, r.FullName);
                        var n = columnCounter[colName];
                        colName += ++n; // increase by one to give a new unique name
                        columnCounter[originalColName] = n;
                    }
                    else
                    {
                        columnCounter.Add(colName, 1);
                    }

                    colName = JsFileGenerator.QuoteColumnNameIfNecessary(colName);

                    // a bit backwards but gets the job done
                    var typeScriptDataType = RoutineParameterV2.GetTypescriptTypeFromSql(dbDataType, null, ref customTypeLookupWithTypeScriptDef);

                    // Col: TypeScript DataType
                    var ln = string.Format("{0}: {1}", colName, typeScriptDataType);
                    tsColumnDefs.Add(ln);

                }

                // TODO: What to return and what to persist?

                var typeScriptResultDef = string.Format("class {0} {{ {1} }}", tsResultTypeDefName, string.Join("; ", tsColumnDefs));

                resultTypes.Add(tsResultTypeDefName);
                typeScriptParameterAndResultTypesSB.AppendLine(typeScriptResultDef);

            } // foreach Result Set

            return (resultTypes, typeScriptParameterAndResultTypesSB.ToString());
        }



    }
}

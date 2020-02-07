using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OM = jsdal_server_core.Settings.ObjectModel;

namespace jsdal_server_core
{
    public class ScriptGeneratorBase
    {
        protected Dictionary<string, List<Definition>> JavaScriptDefinitions;
        protected Dictionary<string, List<Definition>> TypescriptDefinitions;
        protected private List<string> ConverterLookup; // holds all variations of converters and their options (so each unique "signature")...that applies to THIS (ServerMethod) plugin
        protected byte[] JavaScriptDefinitionsHash;
        protected byte[] TypescriptDefinitionsHash;

        public string AssemblyInstanceId // TODO: Not sure if I need this on this level
        {
            get; private set;
        }

        public PluginInfo PluginInfo
        {
            get; private set;
        }

        public ScriptGeneratorBase(string assemblyInstanceId, PluginInfo pluginInfo)
        {
            this.AssemblyInstanceId = assemblyInstanceId;
            this.PluginInfo = pluginInfo;
        }

        protected virtual void Process()
        {
            throw new NotImplementedException("Base class does not provide an implementation");
        }

        // public void HandleAssemblyUpdated(PluginInfo pluginInfo)
        // {
        //     this.PluginInfo = pluginInfo;
        //     this.Process();
        // }

        public void CombineOutput(OM.Application appContext, ref Dictionary<string/*Namespace*/, List<Definition>> combinedJS, ref Dictionary<string/*Namespace*/, List<Definition>> combinedTSD, ref List<string> combinedConverterLookup)
        {
            // converters
            foreach (var c in this.ConverterLookup)
            {
                if (combinedConverterLookup.IndexOf(c) == -1)
                {
                    combinedConverterLookup.Add(c);
                }
            }

            foreach (var namespaceKV in this.JavaScriptDefinitions)
            {
                // js
                foreach (var definition in namespaceKV.Value)
                {
                    if (!combinedJS.ContainsKey(namespaceKV.Key))
                    {
                        combinedJS.Add(namespaceKV.Key, new List<Definition>());
                    }

                    if (combinedJS[namespaceKV.Key].FirstOrDefault(m => m.MethodName.Equals(definition.MethodName, StringComparison.Ordinal)) != null)
                    {
                        // TODO: Consider allowing overloads
                        SessionLog.Warning($"{appContext.Project.Name}/{appContext.Name} - ServerMethods - conflicting method name '{definition.MethodName}'.");
                        continue;
                    }

                    var hasConverter = definition.InputConverter != null || definition.OutputConverter != null || definition.ResultsConverter != null;

                    if (hasConverter)
                    {
                        var convertersSB = new StringBuilder("{ ");
                        var lst = new List<string>();

                        string inputConverter = definition.InputConverter;
                        string outputConverter = definition.OutputConverter;
                        string resultConverter = definition.ResultsConverter;

                        foreach (var converterJson in combinedConverterLookup)
                        {
                            if (inputConverter != null)
                            {
                                inputConverter = inputConverter.Replace(converterJson, combinedConverterLookup.IndexOf(converterJson).ToString());
                            }

                            if (outputConverter != null)
                            {
                                outputConverter = outputConverter.Replace(converterJson, combinedConverterLookup.IndexOf(converterJson).ToString());
                            }

                            if (resultConverter != null)
                            {
                                resultConverter = resultConverter.Replace(converterJson, combinedConverterLookup.IndexOf(converterJson).ToString());
                            }
                        }

                        if (inputConverter != null)
                        {
                            lst.Add($"input: {{ {inputConverter} }}");
                        }

                        if (outputConverter != null)
                        {
                            lst.Add($"output: {{ {outputConverter} }}");
                        }

                        if (resultConverter != null)
                        {
                            lst.Add($"results: {{ {resultConverter} }}");
                        }

                        convertersSB.Append(string.Join(", ", lst));

                        convertersSB.Append(" }");


                        definition.Line = definition.Line.Replace("<<CONV_SEP>>", ", ");
                        definition.Line = definition.Line.Replace("<<CONVERTERS>>", convertersSB.ToString());
                    }

                    combinedJS[namespaceKV.Key].Add(definition);
                }
            }

            // tsd
            foreach (var namespaceKV in this.TypescriptDefinitions)
            {
                foreach (var definition in namespaceKV.Value)
                {
                    if (!combinedTSD.ContainsKey(namespaceKV.Key))
                    {
                        combinedTSD.Add(namespaceKV.Key, new List<Definition>());
                    }

                    if (combinedTSD[namespaceKV.Key].FirstOrDefault(m => m.MethodName.Equals(definition.MethodName, StringComparison.Ordinal)) != null)
                    {
                        // just skip, should have already been handled on the .js side
                        continue;
                    }

                    combinedTSD[namespaceKV.Key].Add(definition);
                }
            }
        }
    }

    public class Definition
    {
        public string MethodName { get; set; }
        public string Line { get; set; }
        public List<string> TypesLines { get; set; }

        public string InputConverter { get; set; }
        public string OutputConverter { get; set; }
        public string ResultsConverter { get; set; }
    }

}
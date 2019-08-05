using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Scripting;
using jsdal_server_core.PluginManagement;
using jsdal_server_core.Settings.ObjectModel.Plugins;

namespace jsdal_server_core
{
    public static class CSharpCompilerHelper
    {

        private static MetadataReference[] GetCommonMetadataReferences()
        {
            var assemblyBasePath = System.IO.Path.GetDirectoryName(typeof(object).Assembly.Location);

            MetadataReference[] all = { MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                                        //?MetadataReference.CreateFromFile(Path.Combine(assemblyBasePath, "mscorlib.dll")),
                                        MetadataReference.CreateFromFile(Path.Combine(assemblyBasePath, "System.dll")),
                                        MetadataReference.CreateFromFile(Path.Combine(assemblyBasePath, "System.Core.dll")),
                                        MetadataReference.CreateFromFile(Path.Combine(assemblyBasePath, "System.Runtime.dll")),
                                        MetadataReference.CreateFromFile(Path.Combine(assemblyBasePath, "System.Collections.dll")),
                                        MetadataReference.CreateFromFile(Path.Combine(assemblyBasePath, "System.Data.dll")),
                                        //MetadataReference.CreateFromFile(typeof(System.Collections.ArrayList).Assembly.Location),
                                        //MetadataReference.CreateFromFile(typeof(System.Collections.Generic.Dictionary<string,string>).Assembly.Location),
                                        MetadataReference.CreateFromFile(typeof(System.Data.SqlClient.SqlConnection).Assembly.Location),
                                        Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(Path.GetFullPath("./plugins/jsdal-plugin.dll"))
            };

            return all;


            // var compilationOptions = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
            // //            generalDiagnosticOption: ReportDiagnostic.Suppress 
            // specificDiagnosticOptions: new Dictionary<string, ReportDiagnostic>
            //                                 {
            //                                 { "CS1701", ReportDiagnostic.Suppress }, // Binding redirects
            //                                 { "CS1702", ReportDiagnostic.Suppress },
            //                                 { "CS1705", ReportDiagnostic.Suppress }
            //                                 }
            // );
        }
        public static async Task<(bool, List<string>)> Evaluate(string code)
        {
            try
            {
                var options = ScriptOptions.Default.WithReferences(GetCommonMetadataReferences());

                await CSharpScript.EvaluateAsync(code, options, globals: null, globalsType: null, cancellationToken: CancellationToken.None);

                return (true, null);
            }
            catch (CompilationErrorException ce)
            {
                return (false, (from d in ce.Diagnostics
                                select d.ToString()).ToList());
            }
        }

        // look for all supported types of plugins and validate
        public static bool ParseForPluginTypes(string existingId, string code, out List<BasePluginRuntime> parsedPlugins, out List<string> problems)
        {
            problems = new List<string>();
            parsedPlugins = new List<BasePluginRuntime>();

            try
            {
                var allPluginTypes = new Type[] { typeof(jsdal_plugin.ServerMethodPlugin), typeof(jsdal_plugin.ExecutionPlugin), typeof(jsdal_plugin.BackgroundThreadPlugin) };
                var allPluginTypeFullNames = from t in allPluginTypes select t.FullName;

                var tree = CSharpSyntaxTree.ParseText(code);
                var compilation = CSharpCompilation.Create("TmpAsm", syntaxTrees: new[] { tree }, references: GetCommonMetadataReferences());
                var model = compilation.GetSemanticModel(tree);
                var root = model.SyntaxTree.GetRoot();

                // get a list of all supported Plugin classes
                var pluginClasses = root
                            .DescendantNodes()
                            .OfType<ClassDeclarationSyntax>()
                            .Select(cls => new { ClassDeclaration = cls, Symbol = model.GetDeclaredSymbol(cls) })
                            .Where(cls => allPluginTypeFullNames.Contains(cls.Symbol.BaseType.ToString()))
                            //.Where(cls => cls.Symbol.BaseType.ToString() == typeof(BasePluginRuntime).FullName)
                            .ToList()
                            ;

                if (pluginClasses.Count == 0)
                {
                    problems.Add($"You need to have at least one type that inherits from on of the following: {string.Join(", ", allPluginTypeFullNames)}.");
                    return false;
                }

                foreach (var pc in pluginClasses)
                {
                    var pluginDataAttrib = pc.Symbol.GetAttributes().FirstOrDefault(a => a.AttributeConstructor?.ContainingType?
                                                                                .ConstructedFrom?
                                                                                .ToDisplayString()
                                                                                .Equals("jsdal_plugin.PluginDataAttribute", StringComparison.OrdinalIgnoreCase) ?? false);

                    if (pluginDataAttrib != null)
                    {
                        if (pluginDataAttrib.ConstructorArguments.Length == 0)
                        {
                            problems.Add($"Type '{pc.Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}' has a PluginData attribute with no value. Please provide a valid GUID value.");
                        }
                        else
                        {
                            var namePart = pluginDataAttrib.ConstructorArguments[0];
                            var guidPart = pluginDataAttrib.ConstructorArguments[1];
                            var descPart = pluginDataAttrib.ConstructorArguments[2];

                            var newParsedPlugin = new BasePluginRuntime();

                            parsedPlugins.Add(newParsedPlugin);

                            if (namePart.IsNull || string.IsNullOrWhiteSpace(namePart.Value.ToString()))
                            {
                                problems.Add($"Type '{pc.Symbol.ToDisplayString()}' has a PluginData attribute with a null or empty Name value.");
                            }
                            else
                            {
                                newParsedPlugin.Name = namePart.Value.ToString();
                            }

                            if (guidPart.IsNull || string.IsNullOrWhiteSpace(guidPart.Value.ToString()) || !Guid.TryParse(guidPart.Value.ToString(), out var gg) || gg == Guid.Empty)
                            {
                                problems.Add($"Type '{pc.Symbol.ToDisplayString()}' has a PluginData attribute with an invalid Guid value.");
                            }
                            else
                            {
                                newParsedPlugin.PluginGuid = gg.ToString().ToLower();
                            }

                            if (!descPart.IsNull)
                            {
                                newParsedPlugin.Description = descPart.Value.ToString();
                                if (string.IsNullOrWhiteSpace(newParsedPlugin.Description)) newParsedPlugin.Description = null;
                            }

                            // if adding a new module
                            if (existingId == null || existingId.Equals("new", StringComparison.OrdinalIgnoreCase ))
                            {
                                var existing = ServerMethodManager
                                                    .GetRegistrations()
                                                    .FirstOrDefault(r => r.PluginGuid.Equals(newParsedPlugin.PluginGuid, StringComparison.OrdinalIgnoreCase));

                                if (existing != null)
                                {
                                    problems.Add($"The type '{pc.Symbol.ToDisplayString()}' has a Plugin Guid that conflicts with an existing loaded plugin. The conflict occurred with '{existing.TypeInfo.FullName}'.");
                                }
                            }

                        }
                    }
                    else
                    {
                        problems.Add($"The type '{pc.Symbol.ToDisplayString()}' is missing a PluginData attribute declaration.");
                    }
                }

                return problems.Count == 0;
            }
            catch (CompilationErrorException ce)
            {
                problems.Add("Compilation errors.");

                foreach (var d in ce.Diagnostics)
                {
                    problems.Add(d.ToString());
                }

                return false;
            }
        }


        // TODO: Decide on better name...or allow for different validations (e.g. Plugins, ServerMethods, SignalR loops, Background Threads?)
        // TODO: Why do we have/need the intermediate RuntimePluginType??? BasePluginRuntime is really just metadata so we don't keep instances of the objects around? But we will instantiate those objects anyway....?
        // SourcePluginType: Type of plugin to look for
        // RuntimePluginType: The type of Plugin to instantiate
        public static bool ParseAgainstBase<SourcePluginType, RuntimePluginType>(string existingId, string code, out List<RuntimePluginType> parsedPlugins, out List<string> problems) where RuntimePluginType : Settings.ObjectModel.Plugins.BasePluginRuntime, new()
        {
            problems = new List<string>();
            parsedPlugins = new List<RuntimePluginType>();

            try
            {
                var tree = CSharpSyntaxTree.ParseText(code);
                var compilation = CSharpCompilation.Create("TmpAsm", syntaxTrees: new[] { tree }, references: GetCommonMetadataReferences());
                var model = compilation.GetSemanticModel(tree);
                var root = model.SyntaxTree.GetRoot();

                var pluginClasses = root
                            .DescendantNodes()
                            .OfType<ClassDeclarationSyntax>()
                            .Select(cls => new { ClassDeclaration = cls, Symbol = model.GetDeclaredSymbol(cls) })
                            .Where(cls => cls.Symbol.BaseType.ToString() == typeof(SourcePluginType).FullName)
                            .ToList()
                            ;

                if (pluginClasses.Count == 0)
                {
                    problems.Add($"You need to have at least one type that inherits from '{typeof(SourcePluginType).FullName}'.");
                    return false;
                }

                foreach (var pc in pluginClasses)
                {
                    var pluginDataAttrib = pc.Symbol.GetAttributes().FirstOrDefault(a => a.AttributeConstructor?.ContainingType?
                                                                                .ConstructedFrom?
                                                                                .ToDisplayString()
                                                                                .Equals("jsdal_plugin.PluginDataAttribute", StringComparison.OrdinalIgnoreCase) ?? false);

                    if (pluginDataAttrib != null)
                    {
                        if (pluginDataAttrib.ConstructorArguments.Length == 0)
                        {
                            problems.Add($"Type '{pc.Symbol.ToDisplayString()}' has a PluginData attribute with no value. Please provide a valid GUID value.");
                        }
                        else
                        {
                            var namePart = pluginDataAttrib.ConstructorArguments[0];
                            var guidPart = pluginDataAttrib.ConstructorArguments[1];
                            var descPart = pluginDataAttrib.ConstructorArguments[2];

                            var newParsedPlugin = new RuntimePluginType();

                            parsedPlugins.Add(newParsedPlugin);

                            if (namePart.IsNull || string.IsNullOrWhiteSpace(namePart.Value.ToString()))
                            {
                                problems.Add($"Type '{pc.Symbol.ToDisplayString()}' has a PluginData attribute with a null or empty Name value.");
                            }
                            else
                            {
                                newParsedPlugin.Name = namePart.Value.ToString();
                            }

                            if (guidPart.IsNull || string.IsNullOrWhiteSpace(guidPart.Value.ToString()) || !Guid.TryParse(guidPart.Value.ToString(), out var gg) || gg == Guid.Empty)
                            {
                                problems.Add($"Type '{pc.Symbol.ToDisplayString()}' has a PluginData attribute with an invalid Guid value.");
                            }
                            else
                            {
                                newParsedPlugin.PluginGuid = gg.ToString().ToLower();
                            }

                            if (!descPart.IsNull)
                            {
                                newParsedPlugin.Description = descPart.Value.ToString();
                                if (string.IsNullOrWhiteSpace(newParsedPlugin.Description)) newParsedPlugin.Description = null;
                            }

                            // if adding a new module
                            if (existingId == null)
                            {
                                var existing = ServerMethodManager
                                                    .GetRegistrations()
                                                    .FirstOrDefault(r => r.PluginGuid.Equals(newParsedPlugin.PluginGuid, StringComparison.OrdinalIgnoreCase));

                                if (existing != null)
                                {
                                    problems.Add($"The type '{pc.Symbol.ToDisplayString()}' has a Plugin Guid that conflicts with an existing loaded plugin. The conflict occurred with '{existing.TypeInfo.FullName}'.");
                                }
                            }

                        }
                    }
                    else
                    {
                        problems.Add($"The type '{pc.Symbol.ToDisplayString()}' is missing a PluginData attribute declaration.");
                    }
                }

                return problems.Count == 0;
            }
            catch (CompilationErrorException ce)
            {
                problems.Add("Compilation errors.");

                foreach (var d in ce.Diagnostics)
                {
                    problems.Add(d.GetMessage());
                }
                return false;
            }
        }

    }

    // // public class FindClassesWithBase<T> : CSharpSyntaxRewriter
    // // {
    // //     private readonly SemanticModel _model;

    // //     public List<INamedTypeSymbol> PluginClasses { get; private set; } = new List<INamedTypeSymbol>();

    // //     public FindClassesWithBase(SemanticModel model)
    // //     {
    // //         _model = model;
    // //     }

    // //     public override SyntaxNode VisitClassDeclaration(ClassDeclarationSyntax node)
    // //     {
    // //         var symbol = _model.GetDeclaredSymbol(node);

    // //         if (InheritsFrom<T>(symbol))
    // //         {
    // //             PluginClasses.Add(symbol);
    // //         }

    // //         return node;
    // //     }

    // //     private bool InheritsFrom<T>(INamedTypeSymbol symbol)
    // //     {
    // //         while (true)
    // //         {
    // //             if (symbol.ToString() == typeof(T).FullName)
    // //             {
    // //                 return true;
    // //             }
    // //             if (symbol.BaseType != null)
    // //             {
    // //                 symbol = symbol.BaseType;
    // //                 continue;
    // //             }
    // //             break;
    // //         }
    // //         return false;
    // //     }
    // // }

}

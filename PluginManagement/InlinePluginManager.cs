using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using jsdal_server_core.Settings.ObjectModel.Plugins.InlinePlugins;
using jsdal_server_core.Settings.ObjectModel.Plugins;
using System.Threading.Tasks;

namespace jsdal_server_core
{
    public class InlinePluginManager
    {
        public static string InlinePluginLibPath
        {
            get { return "./data/inline-plugins.json"; }
        }

        private static InlinePluginManager _instance;

        public List<InlinePluginModule> Modules { get; set; }

        private InlinePluginManager()
        {
            this.Modules = new List<InlinePluginModule>();
        }

        public static InlinePluginManager Instance
        {
            get
            {
                if (_instance == null) _instance = new InlinePluginManager();
                return _instance;
            }
        }

        public async void Init()
        {
            try
            {
                var inlinePluginManager = Load();

                if (inlinePluginManager != null)
                {
                    foreach (var mod in inlinePluginManager.Modules)
                    {
                        var path = Path.Combine(Path.GetFullPath(InlinePluginModule.InlinePluginPath), mod.Id);

                        if (File.Exists(path))
                        {
                            var code = File.ReadAllText(path);

                            var (isValid, problems, parsedPluginCollection) = await EvalAndParseAsync(mod.Id, code);

                            if (InlinePluginModule.Create(mod.Id, mod.Name, mod.Description, code, isValid, parsedPluginCollection, saveToDisk: false, out var newModule, out var error))
                            {
                                this.Modules.Add(newModule);
                                //this.Save();
                            }

                            if (!string.IsNullOrEmpty(error))
                            {
                                SessionLog.Error($"Inline plugin {mod.Name} ({mod.Id}) error: {error}");
                            }

                            if (problems?.Count > 0)
                            {
                                SessionLog.Error($"Inline plugin {mod.Name} ({mod.Id}) failed to compile with the following error(s): {string.Join(", ", problems)}");
                            }
                        }
                        else
                        {
                            SessionLog.Error($"Inline module {mod.Name} not found at '{path}'");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                SessionLog.Exception(ex);
            }
        }

        private async Task<(bool/*isValid*/, List<string>/*problems*/, List<BasePluginRuntime>/*parsedPluginCollection*/)> EvalAndParseAsync(string id, string code)
        {
            bool isValid;
            List<string> problems = null;
            List<BasePluginRuntime> parsedPluginCollection = null;

            (isValid, problems) = await CSharpCompilerHelper.Evaluate(code);

            if (isValid) // if compilation went well
            {
                isValid = CSharpCompilerHelper.ParseForPluginTypes(id, code, out parsedPluginCollection, out problems);
            }

            return (isValid, problems, parsedPluginCollection);
        }

        public async Task<(string/*error*/, string/*id*/, List<string>/*problems*/)> AddUpdateModuleAsync(string id, string name, string description, string code)
        {
            InlinePluginModule newModule = null;
            string error = null;

            var (isValid, problems, parsedPluginCollection) = await EvalAndParseAsync(id, code);

            if (id == null || id.Equals("new", StringComparison.OrdinalIgnoreCase))
            {
                if (InlinePluginModule.Create(null, name, description, code, isValid, parsedPluginCollection, saveToDisk: true, out newModule, out error))
                {
                    this.Modules.Add(newModule);
                    this.Save();
                }
                else
                {
                    id = null;
                }
            }
            else
            {
                var existing = this.Modules.FirstOrDefault(mod => mod.Id.Equals(id));

                if (existing != null)
                {
                    existing.Update(name, description, code, isValid, parsedPluginCollection, saveToDisk: false, out error);
                }
                else
                {
                    SessionLog.Error($"Module {name} ({id}) not found.");
                }
            }

            return (error, id, problems);
        }

        private InlinePluginManager Load()
        {
            var path = Path.GetFullPath(InlinePluginLibPath);

            //this.Modules = new List<InlinePluginModule>();

            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var pm = System.Text.Json.JsonSerializer.Deserialize<InlinePluginManager>(json);
                return pm;
                //this.Modules = pm.Modules;
            }
            return null;
        }

        private void Save()
        {
            var path = Path.GetFullPath(InlinePluginLibPath);
            var fi = new FileInfo(path);

            if (!fi.Directory.Exists)
            {
                fi.Directory.Create();
            }

            var json = System.Text.Json.JsonSerializer.Serialize(this);

            File.WriteAllText(path, json);
        }


    }

}
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using Newtonsoft.Json;
using System.Linq;
using System;

namespace jsdal_server_core
{
    public class InlineModuleManifest
    {
        private static readonly string InlinePluginManifestPath = "./data/inline-plugins.json";

        private List<InlineModuleManifestEntry> _entries;
        private static InlineModuleManifest _instance;
        public ReadOnlyCollection<InlineModuleManifestEntry> Entries { get; private set; }
        public static InlineModuleManifest Instance
        {
            get
            {
                lock (InlinePluginManifestPath)
                {
                    if (_instance == null) _instance = new InlineModuleManifest();
                    return _instance;
                }
            }
        }
        private InlineModuleManifest()
        {

        }

        public void Init()
        {
            this.Load();
        }

        private void Load()
        {
            try
            {
                if (File.Exists(InlinePluginManifestPath))
                {
                    var json = File.ReadAllText(InlinePluginManifestPath);

                    if (!string.IsNullOrWhiteSpace(json))
                    {

                        var entries = JsonConvert.DeserializeObject<InlineModuleManifestEntry[]>(json);

                        _entries = new List<InlineModuleManifestEntry>(entries);
                        Entries = _entries.AsReadOnly();
                    }
                    else
                    {
                        _entries = new List<InlineModuleManifestEntry>();
                        Entries = _entries.AsReadOnly();
                    }
                }
                else
                {
                    _entries = new List<InlineModuleManifestEntry>();
                    Entries = _entries.AsReadOnly();
                }
            }
            catch (Exception ex)
            {
                ExceptionLogger.LogException(ex);
                SessionLog.Exception(ex);
            }
        }

        private object _saveLock = new object();
        private void Save()
        {
            lock (_saveLock)
            {
                var path = Path.GetFullPath(InlinePluginManifestPath);
                var fi = new FileInfo(path);

                if (!fi.Directory.Exists)
                {
                    fi.Directory.Create();
                }

                var json = System.Text.Json.JsonSerializer.Serialize(this.Entries.ToArray());

                File.WriteAllText(path, json);
            }
        }

        public InlineModuleManifestEntry GetEntryById(string id)
        {
            if (this.Entries == null) return null;
            return this.Entries.FirstOrDefault(e => e.Id.Equals(id));
        }

        public void AddUpdateSource(string id, string name, string description, string code)
        {
            lock (_entries)
            {
                var existing = _entries.FirstOrDefault(e => e.Id.Equals(id, System.StringComparison.Ordinal));

                if (existing != null)
                {

                    existing.Name = name;
                    existing.Description = description;
                }
                else
                {
                    id = shortid.ShortId.Generate(false, false, 6);
                    _entries.Add(new InlineModuleManifestEntry() { Id = id, Name = name, Description = description });
                }

                var sourcePath = Path.Combine(PluginLoader.InlinePluginSourcePath, id);
                var fi = new FileInfo(sourcePath);

                if (!fi.Directory.Exists) fi.Directory.Create();

                File.WriteAllText(sourcePath, code);
                this.Save();

            }
        }
    }

    public class InlineModuleManifestEntry
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
    }
}
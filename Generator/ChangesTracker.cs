using System;
using System.Linq;
using System.Collections.Generic;
using jsdal_server_core.Settings.ObjectModel;
using System.Text;

namespace jsdal_server_core
{
    public class JsFileChangesTracker
    {
        private static JsFileChangesTracker instance = new JsFileChangesTracker();
        public static JsFileChangesTracker Instance
        {
            get
            {
                return instance;
            }
        }

        public Dictionary<string/*endpoint uri*/, Dictionary<string/*JsFilename.VERSION*/, List<string>/*list of changes*/>> _entries;

        public JsFileChangesTracker()
        {
            this._entries = new Dictionary<string, Dictionary<string, List<string>>>();
        }

        public void AddUpdate(Endpoint endpoint, JsFile jsFile, List<string> changes)
        {
            try
            {
                if (!_entries.ContainsKey(endpoint.Pedigree))
                {
                    _entries.Add(endpoint.Pedigree, new Dictionary<string, List<string>>());
                }

                var epEntry = _entries[endpoint.Pedigree];
                var jsFileKey = $"{jsFile.Filename.ToLower()}.{jsFile.Version}";

                if (!epEntry.ContainsKey(jsFileKey))
                {
                    epEntry.Add(jsFileKey, new List<string>());
                }

                // add unique entries
                epEntry[jsFileKey].AddRange(changes.Where(c => !epEntry[jsFileKey].Exists(existing => existing.Equals(c, StringComparison.OrdinalIgnoreCase))));
            }
            catch (Exception ex)
            {
                SessionLog.Exception(ex);
            }
        }

        public string BuildChangeList(Endpoint endpoint, JsFile jsFile, int fromVersion, int toVersion)
        {
            var sb = new StringBuilder();

            if (!_entries.ContainsKey(endpoint.Pedigree)) return null;

            var epEntry = _entries[endpoint.Pedigree];

            for (var i = fromVersion+1; i <= toVersion; i++)
            {
                var jsFileKey = $"{jsFile.Filename.ToLower()}.{i}";

                if (!epEntry.ContainsKey(jsFileKey) || epEntry[jsFileKey].Count == 0) continue;

                sb.AppendJoin('\n', epEntry[jsFileKey]);
            }

            return sb.ToString();
        }
    }
}

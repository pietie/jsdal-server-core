using System;
using System.Linq;
using System.Collections.Generic;
using jsdal_server_core.Settings.ObjectModel;
using System.Text;
using Newtonsoft.Json;
using System.Text.RegularExpressions;

namespace jsdal_server_core.Changes
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

        public Dictionary<string/*endpoint uri*/, Dictionary<string/*JsFilename.VERSION*/, List<ChangeDescriptor>/*list of changes*/>> _entries;

        public JsFileChangesTracker()
        {
            this._entries = new Dictionary<string, Dictionary<string, List<ChangeDescriptor>>>();
        }

        public void AddUpdate(Endpoint endpoint, JsFile jsFile, List<ChangeDescriptor> changes)
        {
            try
            {
                if (!_entries.ContainsKey(endpoint.Pedigree))
                {
                    _entries.Add(endpoint.Pedigree, new Dictionary<string, List<ChangeDescriptor>>());
                }

                var epEntry = _entries[endpoint.Pedigree];
                var jsFileKey = $"{jsFile.Filename.ToLower()}.{jsFile.Version}";

                if (!epEntry.ContainsKey(jsFileKey))
                {
                    epEntry.Add(jsFileKey, new List<ChangeDescriptor>());
                }

                // add unique entries
                epEntry[jsFileKey].AddRange(changes.Where(c => !epEntry[jsFileKey].Exists(existing => existing.Description.Equals(c.Description, StringComparison.OrdinalIgnoreCase))));
            }
            catch (Exception ex)
            {
                SessionLog.Exception(ex);
            }
        }

        public int CountChanges(Endpoint endpoint, JsFile jsFile, int fromVersion, int toVersion, string filterJson)
        {
            // TODO: take filterJson into account
            if (!_entries.ContainsKey(endpoint.Pedigree)) return 0;

            var epEntry = _entries[endpoint.Pedigree];

            int totalChanges = 0;

            string changedByFilter = null;

            Regex changedByRegex = null;

            if (filterJson != null)
            {
                var filter = JsonConvert.DeserializeObject<dynamic>(filterJson);

                if (filter["changedBy"] != null)
                {
                    changedByFilter = filter["changedBy"];
                    changedByRegex = new Regex(changedByFilter);
                }
            }

            for (var i = fromVersion + 1; i <= toVersion; i++)
            {
                var jsFileKey = $"{jsFile.Filename.ToLower()}.{i}";

                if (!epEntry.ContainsKey(jsFileKey) || epEntry[jsFileKey].Count == 0) continue;

                totalChanges += epEntry[jsFileKey].Count(chg =>
                {
                    if (string.IsNullOrWhiteSpace(changedByFilter)) return true;
                    return changedByRegex.IsMatch(chg.ChangedBy);

                });
            }

            return totalChanges;
        }

        public List<ChangeDescriptor> BuildChangeList(Endpoint endpoint, JsFile jsFile, int fromVersion, int toVersion)
        {
            var changes = new List<ChangeDescriptor>();

            if (!_entries.ContainsKey(endpoint.Pedigree)) return null;

            var epEntry = _entries[endpoint.Pedigree];

            for (var i = fromVersion + 1; i <= toVersion; i++)
            {
                var jsFileKey = $"{jsFile.Filename.ToLower()}.{i}";

                if (!epEntry.ContainsKey(jsFileKey) || epEntry[jsFileKey].Count == 0) continue;

                var applicableChanges = epEntry[jsFileKey].Where(chg =>
                {
                    // only add if unique (as same sproc could have be changed across versions)
                    if (changes.Exists(existing=>existing.Description.Equals(chg.Description, StringComparison.OrdinalIgnoreCase))) return false;
                    else return true;
                });

                changes.AddRange(applicableChanges);

            }

            return changes;
        }
    }
}

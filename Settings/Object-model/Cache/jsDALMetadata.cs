using System;
using Newtonsoft.Json;

namespace jsdal_server_core.Settings.ObjectModel
{
    [Serializable]
    public class jsDALMetadata
    {
        public jsDAL jsDAL { get; set; }
        public string Error { get; set; }

        public bool Equals(jsDALMetadata other)
        {
            // TODO: This is a lazy way of comparing these objects...do it properly?
            var s1 = JsonConvert.SerializeObject(this);
            var s2 = JsonConvert.SerializeObject(other);

            return s1.Equals(s2, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Serializable]
    public class jsDAL
    {
        public bool? fmtOnlyResultSet { get; set; }
        public bool? exclude { get; set; }
        public bool? include { get; set; }
        public jsDALCache cache { get; set; }
        public jsDALSecurity security { get; set; }
    }
    [Serializable]
    public class jsDALSecurity
    {
        public bool? requiresCaptcha { get; set; }
        public bool? requiresWindowAuth { get; set; }
    }
    [Serializable]
    public class jsDALCache
    {
        public int hours { get; set; }
    }
}
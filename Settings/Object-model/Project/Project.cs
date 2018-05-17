using System;
using System.Linq;
using System.Collections.Generic;
using shortid;
using Newtonsoft.Json;

namespace jsdal_server_core.Settings.ObjectModel
{
    public class Project
    {
        public string Name { get; set; }
        public string Guid { get; set; } // TODO: Remove!
        
        [JsonProperty("Apps")] public List<Application> Applications { get; set; }

        public Project()
        {
            this.Applications = new List<Application>();
        }

        public void UpdateParentReferences()
        { // this cannot be done during JSON deserialization without adding ugly ref tags to the JSON
            if (this.Applications != null)
            {
                this.Applications.ForEach(app=>app.UpdateParentReferences(this));
            }
        }

        public Application getDatabaseSource(string logicalName)
        {
            if (this.Applications == null) return null;
            return this.Applications.FirstOrDefault(dbs => dbs.Name.Equals(logicalName, StringComparison.OrdinalIgnoreCase));
        }

        public void removeConnectionString(Application dbSource)
        {
            this.Applications.Remove(dbSource);
        }

        // // public CommonReturnValueWithDbSource addMetadataConnectionString(string name, string dataSource,
        // //     string catalog, string username,
        // //     string password, string jsNamespace,
        // //     int defaultRoleMode,
        // //     int port, string instanceName)/*: { success: boolean, userError ?: string, dbSource ?: DatabaseSource }*/
        // // {
        // //     if (this.DatabaseSources == null) this.DatabaseSources = new List<DatabaseSource>();

        // //     var cs = new DatabaseSource();

        // //     cs.Name = name;
        // //     cs.DefaultRuleMode = defaultRoleMode;

        // //     var ret = cs.addUpdateDatabaseConnection(true/*isMetadataConnection*/, null, name, dataSource, catalog, username, password, port, instanceName);

        // //     if (!ret.isSuccess) return ret;

        // //     cs.JsNamespace = jsNamespace;

        // //     this.DatabaseSources.Add(cs);

        // //     return CommonReturnValueWithDbSource.success(cs);
        // // }

    }
}
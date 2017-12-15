using System;
using System.Linq;
using System.Collections.Generic;
using shortid;

namespace jsdal_server_core.Settings.ObjectModel
{
    public class Project
    {
        public string Name { get; set; }
        public string Guid { get; set; }
        public List<DatabaseSource> DatabaseSources { get; set; }

        // public toJSON()
        // {
        //     return { Name: this.Name, Guid: this.Guid, DatabaseSources: this.DatabaseSources };
        // }

        public Project()
        {
            this.DatabaseSources = new List<DatabaseSource>();
        }

        /*        public static createFromJson(rawJson: any): Project {
                let project = new Project();

                project.Name = rawJson.Name;
                project.Guid = rawJson.Guid;

                for (let i = 0; i<rawJson.DatabaseSources.length; i++) {
                    let dbs = rawJson.DatabaseSources[i];
                project.DatabaseSources.push(DatabaseSource.createFromJson(dbs));
                }

                //        console.dir(project);

                return project;
            }*/

        public DatabaseSource getDatabaseSource(string logicalName)
        {
            if (this.DatabaseSources == null) return null;
            return this.DatabaseSources.FirstOrDefault(dbs => dbs.Name.Equals(logicalName, StringComparison.OrdinalIgnoreCase));
        }

        public void removeConnectionString(DatabaseSource dbSource)
        {
            this.DatabaseSources.Remove(dbSource);
        }

        public CommonReturnValueWithDbSource addMetadataConnectionString(string name, string dataSource,
            string catalog, string username,
            string password, string jsNamespace,
            int defaultRoleMode,
            int port, string instanceName)/*: { success: boolean, userError ?: string, dbSource ?: DatabaseSource }*/
        {
            if (this.DatabaseSources == null) this.DatabaseSources = new List<DatabaseSource>();

            var cs = new DatabaseSource();

            cs.CacheKey = ShortId.Generate();
            cs.Name = name;
            cs.DefaultRuleMode = defaultRoleMode;

            var ret = cs.addUpdateDatabaseConnection(true/*isMetadataConnection*/, null, name, dataSource, catalog, username, password, port, instanceName);

            if (!ret.isSuccess) return ret;

            cs.JsNamespace = jsNamespace;

            this.DatabaseSources.Add(cs);

            return CommonReturnValueWithDbSource.success(cs);
        }

    }
}
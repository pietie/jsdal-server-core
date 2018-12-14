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
        //public string Guid { get; set; } // TODO: Remove!

        [JsonProperty("Apps")] public List<Application> Applications { get; set; }

        

        public Project()
        {
            this.Applications = new List<Application>();
        }

        public void UpdateParentReferences()
        { // this cannot be done during JSON deserialization without adding ugly ref tags to the JSON
            if (this.Applications != null)
            {
                this.Applications.ForEach(app => app.UpdateParentReferences(this));
            }
        }

        public Application GetApplication(string logicalName)
        {
            if (this.Applications == null) return null;
            return this.Applications.FirstOrDefault(dbs => dbs.Name.Equals(logicalName, StringComparison.OrdinalIgnoreCase));
        }

        public CommonReturnValueWithApplication AddApplication(string name, string jsNamespace, int defaultRuleMode)
        {
            if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0)
            {
                return CommonReturnValueWithApplication.userError("Application names may not be empty or contain special characters. Valid characters include A to Z and 0 to 9.");
            }

            if (this.Applications == null) this.Applications = new List<Application>();

            var app = new Application();

            app.Name = name;
            app.JsNamespace = jsNamespace;
            app.DefaultRuleMode = defaultRuleMode;

            this.Applications.Add(app);

            return CommonReturnValueWithApplication.success(app);
        }

        public CommonReturnValueWithApplication UpdateApplication(string name, string jsNamespace, int defaultRuleMode)
        {
            if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0)
            {
                return CommonReturnValueWithApplication.userError("Application names may not be empty or contain special characters. Valid characters include A to Z and 0 to 9.");
            }

            if (this.Applications == null) this.Applications = new List<Application>();

            var app = new Application();

            app.Name = name;
            app.JsNamespace = jsNamespace;
            app.DefaultRuleMode = defaultRuleMode;

            this.Applications.Add(app);

            return CommonReturnValueWithApplication.success(app);
        }

        public bool DeleteApplication(Application app)
        {
            return this.Applications.Remove(app);
        }

       

    }
}
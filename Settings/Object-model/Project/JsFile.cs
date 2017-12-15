using System;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using shortid;

namespace jsdal_server_core.Settings.ObjectModel
{
    public class JsFile
    {
        public static JsFile _dbLevel = new JsFile("DbLevel");
        public static JsFile DBLevel { get { return JsFile._dbLevel; } }


        public string Filename { get; set; }
        public string Guid { get; set; }
        public int Version { get; set; }
        public List<BaseRule> Rules { get; set; }

        public JsFile(string guid)
        {
            this.Rules = new List<BaseRule>();
            this.Guid = guid;
            this.Version = 1;
        }

        public JsFile()
        {
            this.Rules = new List<BaseRule>();
            this.Guid = ShortId.Generate();
            this.Version = 1;
        }

        public void incrementVersion()
        {
            this.Version++;
        }

        /**public static JsFile createFromJson(rawJson: any): JsFile {
        let jsfile = new JsFile();

        jsfile.Filename = rawJson.Filename;
        jsfile.Guid = rawJson.Guid;
        jsfile.Version = parseInt(rawJson.Version);

        if (isNaN(jsfile.Version)) jsfile.Version = 1;

        for (let i = 0; i<rawJson.Rules.length; i++) {
            jsfile.Rules.push(BaseRule.createFromJson(rawJson.Rules[i]));
        }

        return jsfile;
    }**/

        public CommonReturnValue addRule(RuleType ruleType, string txt)
        {
            BaseRule rule = null;

            switch (ruleType) // TODO: Check for duplicated rules?
            {
                case RuleType.Schema:
                    rule = new SchemaRule(txt);
                    break;
                case RuleType.Specific:
                    {
                        var parts = txt.Split('.');
                        var schema = "dbo";
                        var name = txt;

                        if (parts.Length > 1)
                        {
                            schema = parts[0];
                            name = parts[1];
                        }

                        rule = new SpecificRule(schema, name);
                    }
                    break;
                case RuleType.Regex:
                    {
                        try
                        {
                            var regexTest = new Regex(txt);
                        }
                        catch (Exception ex)
                        {
                            return CommonReturnValue.userError("Invalid regex pattern: " + ex.ToString());
                        }

                        rule = new RegexRule(txt);
                        break;
                    }
                default:
                    throw new Exception($"Unsupported rule type: ${ruleType}");
            }

            rule.Guid = ShortId.Generate();

            this.Rules.Add(rule);

            return CommonReturnValue.success();
        }

        public CommonReturnValue deleteRule(string ruleGuid)
        {
            var existingRule = this.Rules.FirstOrDefault(r => r.Guid == ruleGuid);

            if (existingRule == null)
            {
                return CommonReturnValue.userError("The specified rule was not found.");
            }

            //this.Rules.splice(this.Rules.IndexOf(existingRule), 1);
            this.Rules.Remove(existingRule);

            return CommonReturnValue.success();
        }

    }
}

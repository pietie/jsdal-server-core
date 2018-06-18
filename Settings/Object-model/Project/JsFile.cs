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

        public string Id { get; set; }
        public int Version { get; set; }
        public List<BaseRule> Rules { get; set; }

        public JsFile(string id)
        {
            this.Rules = new List<BaseRule>();
            this.Id = id;
            this.Version = 1;
        }

        public JsFile()
        {
            this.Rules = new List<BaseRule>();
            this.Id = ShortId.Generate();
            this.Version = 1;
        }

        public void incrementVersion()
        {
            this.Version++;
        }

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
                        rule = SpecificRule.FromFullname(txt);
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

            rule.Id = ShortId.Generate(useNumbers: true, useSpecial:true, length: 6);

            this.Rules.Add(rule);

            return CommonReturnValue.success();
        }

        public CommonReturnValue UpdateRule(string ruleId, string txt)
        {
            var existingRule = this.Rules.FirstOrDefault(r => r?.Id?.Equals(ruleId, StringComparison.Ordinal) ?? false);

            if (existingRule == null)
            {
                return CommonReturnValue.userError("The specified rule was not found.");
            }

            existingRule.Update(txt);

            return CommonReturnValue.success();
        }

        public CommonReturnValue DeleteRule(string ruleId)
        {
            var existingRule = this.Rules.FirstOrDefault(r => r.Id.Equals(ruleId, StringComparison.Ordinal));

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

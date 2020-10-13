using System;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using shortid;
using Newtonsoft.Json;
using System.IO;

namespace jsdal_server_core.Settings.ObjectModel
{
    [Serializable]
    public class JsFile
    {
        public static JsFile _dbLevel = new JsFile("DbLevel");
        public static JsFile DBLevel { get { return JsFile._dbLevel; } }


        public string Filename { get; set; }

        public string Id { get; set; }
        public int Version { get; set; }
        public List<BaseRule> Rules { get; set; }

        public string ETag { get; set; }
        public string ETagMinified { get; set; }

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


        public void AfterDeserializationInit(List<Endpoint> endpoints)
        {
            RecomputeETag(endpoints);
        }

        private void RecomputeETag(List<Endpoint> endpoints)
        {
            var jsFileEtagCalculated = false;
            var jsFileMinEtagCalculated = false;

            foreach (var ep in endpoints)
            {
                var filePath = ep.OutputFilePath(this);
                var minfiedFilePath = ep.MinifiedOutputFilePath(this);

                if (File.Exists(filePath))
                {
                    this.ETag = Controllers.PublicController.ComputeETag(File.ReadAllBytes(filePath));
                    jsFileEtagCalculated = true;
                }

                if (File.Exists(minfiedFilePath))
                {
                    this.ETagMinified = Controllers.PublicController.ComputeETag(File.ReadAllBytes(minfiedFilePath));
                    jsFileMinEtagCalculated = true;
                }

                if (jsFileEtagCalculated && jsFileMinEtagCalculated) return;
            }
        }

        public void IncrementVersion()
        {
            this.Version++;
        }

        public CommonReturnValue AddRule(RuleType ruleType, string txt)
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
                            return CommonReturnValue.UserError("Invalid regex pattern: " + ex.ToString());
                        }

                        rule = new RegexRule(txt);
                        break;
                    }
                default:
                    throw new Exception($"Unsupported rule type: ${ruleType}");
            }

            rule.Id = ShortId.Generate(useNumbers: true, useSpecial: true, length: 6);

            this.Rules.Add(rule);

            return CommonReturnValue.Success();
        }

        public CommonReturnValue UpdateRule(string ruleId, string txt)
        {
            var existingRule = this.Rules.FirstOrDefault(r => r?.Id?.Equals(ruleId, StringComparison.Ordinal) ?? false);

            if (existingRule == null)
            {
                return CommonReturnValue.UserError("The specified rule was not found.");
            }

            existingRule.Update(txt);

            return CommonReturnValue.Success();
        }

        public CommonReturnValue DeleteRule(string ruleId)
        {
            var existingRule = this.Rules.FirstOrDefault(r => r.Id.Equals(ruleId, StringComparison.Ordinal));

            if (existingRule == null)
            {
                return CommonReturnValue.UserError("The specified rule was not found.");
            }

            //this.Rules.splice(this.Rules.IndexOf(existingRule), 1);
            this.Rules.Remove(existingRule);

            return CommonReturnValue.Success();
        }

    }
}

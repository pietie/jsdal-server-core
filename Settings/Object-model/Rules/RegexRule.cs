using System;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace jsdal_server_core.Settings.ObjectModel
{
    public class RegexRule : BaseRule
    {
        public string Match;

        public RegexRule(): base() { this.Type = (int)RuleType.Regex; }
        public RegexRule(string match) : base()
        {
            this.Match = match;
            this.Type = (int)RuleType.Regex;
        }

        public override bool Apply(CachedRoutine routine)
        {
            if (string.IsNullOrWhiteSpace(this.Match)) return false;
            //var reg = new RegExp(this.Match.Replace("\\", "\\\\"), RegexOptions.None);
            var reg = new Regex(this.Match.Replace("\\", "\\\\"), RegexOptions.None);

            return reg.Match(routine.Routine).Success;
        }

        public new int RuleProcessOrder { get { return 2; } }
        public override string ToString() { return this.Match; }

        public override void Update(string txt)
        {
            this.Match = txt;
        }
    }

}

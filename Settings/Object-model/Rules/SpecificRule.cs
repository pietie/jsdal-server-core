using System;
using System.Linq;
using System.Collections.Generic;

namespace jsdal_server_core.Settings.ObjectModel
{
    public class SpecificRule : BaseRule
    {

        public string Schema;
        public string Routine;

        public SpecificRule() : base() { this.Type = (int)RuleType.Specific; }


        public SpecificRule(string schema, string routine) : base()
        {
            if (!string.IsNullOrWhiteSpace(schema) && !string.IsNullOrWhiteSpace(routine))
            {
                // remove quoted identifier ('[..]') if present
                if (schema[0] == '[' && schema[schema.Length - 1] == ']') schema = schema.Substring(1, schema.Length - 2);
                if (routine[0] == '[' && routine[routine.Length - 1] == ']') routine = routine.Substring(1, routine.Length - 2);
            }

            this.Schema = schema;
            this.Routine = routine;
            this.Type = (int)RuleType.Specific;
        }

        public static SpecificRule FromFullname(string txt)
        {
            var parts = txt.Split('.');
            var schema = "dbo";
            var name = txt;

            if (parts.Length > 1)
            {
                schema = parts[0];
                name = parts[1];
            }

            return new SpecificRule(schema, name);
        }

        public override bool Apply(CachedRoutine routine)
        {
            return routine.Schema.Equals(this.Schema, StringComparison.OrdinalIgnoreCase)
                && routine.Routine.Equals(this.Routine, StringComparison.OrdinalIgnoreCase)
                ;
        }
        public new int RuleProcessOrder { get { return 0; } }

        public override string ToString()
        {
            return $"[{this.Schema}].[{this.Routine}]";
        }

        public override void Update(string txt)
        {
            var tmp = SpecificRule.FromFullname(txt);
            
            this.Schema = tmp.Schema;
            this.Routine = tmp.Routine;
        }
    }
}

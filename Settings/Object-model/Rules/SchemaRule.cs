using System;
using System.Linq;
using System.Collections.Generic;

namespace jsdal_server_core.Settings.ObjectModel
{
    public class SchemaRule : BaseRule
    {
        public SchemaRule(): base() {this.Type = (int)RuleType.Schema; }
        public SchemaRule(string name) : base()
        {
            this.Name = name;
            this.Type = (int)RuleType.Schema;
        }

        public override bool Apply(CachedRoutine routine)
        {
            return routine.Schema.Equals(this.Name, StringComparison.OrdinalIgnoreCase);
        }
        public new int RuleProcessOrder { get { return 1; } }

        
    }
}

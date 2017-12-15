using System;
using System.Linq;
using System.Collections.Generic;

namespace jsdal_server_core.Settings.ObjectModel
{
    public class BaseRule
    {

        public string Name;
        public string Guid;
        public int RuleProcessOrder;
        public int Type = -1;

        public virtual bool apply(CachedRoutine routine)
        {
            throw new NotImplementedException();
        }

        public override string ToString()
        {
            return this.Name;
        }
    }
}

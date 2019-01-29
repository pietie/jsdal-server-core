using System;
using System.Linq;
using System.Collections.Generic;
using jsdal_server_core.Settings.ObjectModel;
using System.Text;
using shortid;

namespace jsdal_server_core.Changes
{
    public class ChangeDescriptor
    {
       //? public string Id { get; private set; }
        public DateTime? CreatedUtc { get; private set; }
        public string ChangedBy { get; private set; }

        public string Description { get; private set; }

        public ChangeDescriptor()
        {
        //?    this.Id = ShortId.Generate(5);
            this.CreatedUtc = DateTime.UtcNow;
        }

        public static ChangeDescriptor Create(string changedBy, string description)
        {
            return new ChangeDescriptor() { 
                ChangedBy = changedBy,
                Description = description
            };
        }

    }

}
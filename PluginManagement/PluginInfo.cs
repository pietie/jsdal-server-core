using System;
using System.Reflection;

namespace jsdal_server_core
{
    public class PluginInfo
    {
        public PluginInfo()
        {
        }
        public Guid Guid { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }

        public TypeInfo TypeInfo { get; set; }

        public Assembly Assembly { get; set; }

        public Settings.ObjectModel.PluginType Type { get; set; }
    }

     
}
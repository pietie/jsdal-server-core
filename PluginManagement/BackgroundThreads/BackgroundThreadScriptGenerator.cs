namespace jsdal_server_core
{
    public class BackgroundThreadScriptGenerator : ScriptGeneratorBase
    {
        private BackgroundThreadScriptGenerator(string assemblyInstanceId, PluginInfo pluginInfo) : base(assemblyInstanceId, pluginInfo)
        {
        }

        public static BackgroundThreadScriptGenerator Create(string assemblyInstanceId, PluginInfo pluginInfo)
        {
            var ret = new BackgroundThreadScriptGenerator(assemblyInstanceId, pluginInfo);


            return ret;
        }

    }
}
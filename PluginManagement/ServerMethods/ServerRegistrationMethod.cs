using System;
using System.Reflection;

namespace jsdal_server_core.ServerMethods
{
    public class ServerMethodRegistrationMethod
    {
        public ServerMethodRegistrationMethod(ServerMethodPluginRegistration reg)
        {
            this.Registration = reg;
        }

        public ServerMethodPluginRegistration Registration { get; private set; }

        public string Namespace { get; set; } // inherited if null and class level is specified otherwhise none
        public string Name { get; set; }
        public MethodInfo MethodInfo { get; set; }

        public void Execute(/*TODO: Parms */)
        {
            try
            {
                // ...
                //this.MethodInfo.Invoke(PluginInstance, ...);
            }
            catch (Exception ex)
            {
                // TODO: !!!
            }
        }


    }
}
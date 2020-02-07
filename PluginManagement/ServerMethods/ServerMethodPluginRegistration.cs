using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using jsdal_plugin;
using System.Collections.ObjectModel;
using System.Text;
using OM = jsdal_server_core.Settings.ObjectModel;

namespace jsdal_server_core.PluginManagement
{
    // Represents a single instance of a class that derives from one of the plugin classes (ServerMethodPlugin, ExecutionPlugin)
    // An assembly may contain multiples of these
    public class ServerMethodPluginRegistration
    {
        public Assembly Assembly { get; private set; }
        public TypeInfo TypeInfo { get; private set; }
        public string PluginGuid { get; private set; }
        private readonly List<ServerMethodRegistrationMethod> _methods;
        public ReadOnlyCollection<ServerMethodRegistrationMethod> Methods { get; private set; }

        public ServerMethodScriptGenerator ScriptGenerator { get; private set; }

        public string PluginAssemblyInstanceId { get; private set; }


        private ServerMethodPluginRegistration(Assembly assembly, TypeInfo typeInfo, Guid pluginGuid, string pluginAssemblyInstanceId)
        {
            _methods = new List<ServerMethodRegistrationMethod>();

            this.Methods = _methods.AsReadOnly();
            this.Assembly = assembly;
            this.TypeInfo = typeInfo;
            this.PluginGuid = pluginGuid.ToString();
            this.PluginAssemblyInstanceId = pluginAssemblyInstanceId;
        }

        public static ServerMethodPluginRegistration Create(string pluginAssemblyInstanceId, PluginInfo pluginInfo)
        {
            var reg = new ServerMethodPluginRegistration(pluginInfo.Assembly, pluginInfo.TypeInfo, pluginInfo.Guid, pluginAssemblyInstanceId);

            var classLevelAttrib = pluginInfo.TypeInfo.GetCustomAttribute(typeof(ServerMethodAttribute)) as ServerMethodAttribute;

            // static methods not supported 
            var methods = pluginInfo.TypeInfo.GetMethods(BindingFlags.Public /* | BindingFlags.Static*/ | BindingFlags.Instance);

            string classLevelNamespace = classLevelAttrib?.Namespace;

            var serverMethodCollection = (from mi in methods
                                          select new
                                          {
                                              MethodInfo = mi,
                                              ServerMethodAttribute = mi.GetCustomAttribute(typeof(ServerMethodAttribute)) as ServerMethodAttribute
                                          })
                                              .Where(m => m.ServerMethodAttribute != null);

            if (serverMethodCollection.Count() == 0)
            {
                SessionLog.Warning($"No server method's found in plugin '{pluginInfo.Name}' ({pluginInfo.Guid}) from assembly {pluginInfo.Assembly.FullName}. Add a [ServerMethod] attribute to the methods you want to expose.");
            }
            else
            {

                foreach (var m in serverMethodCollection)
                {
                    string ns = m.ServerMethodAttribute?.Namespace;

                    if (ns == null)
                    {
                        ns = classLevelNamespace;
                    }

                    reg.AddMethod(m.MethodInfo.Name, ns, m.MethodInfo);
                }
            }

            reg.ScriptGenerator = ServerMethodScriptGenerator.Create(reg, pluginInfo);


            return reg;
        }

        public ServerMethodRegistrationMethod AddMethod(string name, string nameSpace, MethodInfo methodInfo)
        {
            var method = new ServerMethodRegistrationMethod(this);

            method.Name = name;
            method.Namespace = nameSpace;
            method.AssemblyMethodInfo = methodInfo;

            this._methods.Add(method);
            return method;
        }

    }
}
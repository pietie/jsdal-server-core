using System;
using jsdal_server_core.Settings.ObjectModel.Plugins;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace jsdal_server_core.Settings.ObjectModel
{
    public class PluginJsonConverter : JsonConverter
    {

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            var target = serializer.Deserialize<Newtonsoft.Json.Linq.JObject>(reader);

            var type = (PluginType)(long)((JValue)(target["Type"])).Value;

            if (type == PluginType.Execution)
            {
                var sr = new ExecPluginRuntime();

                // if (target["Name"] != null) sr.Name = target["Name"].ToString();
                // if (target["Id"] != null) sr.Id = target["Id"].ToString();

                return sr;
            }
            else if (type == PluginType.ServerMethod)
            {
                var sr = new ServerMethodPluginRuntime();

                // if (target["Routine"] != null) sr.Routine = target["Routine"].ToString();
                // if (target["Schema"] != null) sr.Schema = target["Schema"].ToString();
                // if (target["Name"] != null) sr.Name = target["Name"].ToString();
                // if (target["Id"] != null) sr.Id = target["Id"].ToString();

                return sr;
            }
            // else if (type == PluginType.DbNotifyMethod)
            // {
            //     var rr = new DbNotifyMethodPlugin();

            //     // if (target["Name"] != null) rr.Name = target["Name"].ToString();
            //     // if (target["Id"] != null) rr.Id = target["Id"].ToString();
            //     // if (target["Match"] != null) rr.Match = target["Match"].ToString();

            //     return rr;

            // }

            throw new NotSupportedException(string.Format("Type {0} unexpected.", type));
        }

        public override bool CanWrite
        {
            get
            {
                return false;

            }
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            serializer.Serialize(writer, value);
        }


        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(BaseRule);
        }
    }
}
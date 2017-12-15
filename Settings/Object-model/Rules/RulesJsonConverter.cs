using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace jsdal_server_core.Settings.ObjectModel
{
    public class RuleJsonConverter : JsonConverter
    {

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            var target = serializer.Deserialize<Newtonsoft.Json.Linq.JObject>(reader);

            var type = (RuleType)(long)((JValue)(target["Type"])).Value;

            if (type == RuleType.Schema)
            {
                var sr = new SchemaRule();

                if (target["Name"] != null) sr.Name = target["Name"].ToString();
                if (target["Guid"] != null) sr.Guid = target["Guid"].ToString();

                return sr;
            }
            else if (type == RuleType.Specific)
            {
                var sr = new SpecificRule();

                if (target["Routine"] != null) sr.Routine = target["Routine"].ToString();
                if (target["Schema"] != null) sr.Schema = target["Schema"].ToString();
                if (target["Name"] != null) sr.Name = target["Name"].ToString();
                if (target["Guid"] != null) sr.Guid = target["Guid"].ToString();

                return sr;
            }
            else if (type == RuleType.Regex)
            {
                var rr = new RegexRule();

                if (target["Name"] != null) rr.Name = target["Name"].ToString();
                if (target["Guid"] != null) rr.Guid = target["Guid"].ToString();
                if (target["Match"] != null) rr.Match = target["Match"].ToString();

                return rr;

            }

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
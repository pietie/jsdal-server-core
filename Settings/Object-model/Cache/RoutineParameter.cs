using System;
using System.Linq;
using Newtonsoft.Json;
using System.Xml.Serialization;
using System.Collections.Generic;

namespace jsdal_server_core.Settings.ObjectModel
{

    public class RoutineParameterV2
    {
        [XmlAttribute]
        public string Name { get; set; }

        [XmlAttribute]
        public bool IsOutput { get; set; }

        [XmlAttribute("Max")]
        public int MaxLength { get; set; }

        [XmlAttribute]
        public int Precision { get; set; }

        [XmlAttribute]
        public int Scale { get; set; }

        [XmlAttribute("Type")]
        public string SqlDataType { get; set; }

        [XmlAttribute("DefVal")]
        public string DefaultValue { get; set; }

        [XmlAttribute("Result")]
        public bool IsResult { get; set; }


        //private string _userType;

        [XmlElement("UserType")]
        [JsonIgnore]
        public string UserType
        {
            //?get { return _userType; }
            get { return null; }
            set
            {
                if (value != null)
                {
                    var options = new System.Text.Json.JsonSerializerOptions() { };

                    this.CustomType = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, RoutineParameterCustomType>>>(value, options);
                }
            }
        }

        [XmlIgnore]
        public Dictionary<string, Dictionary<string, RoutineParameterCustomType>> CustomType { get; set; }


        [XmlIgnore]
        [JsonIgnore]
        public bool HasDefault
        {
            get { return this.DefaultValue != null; }
        }

        public string Hash()
        {
            return $"{SqlDataType},{Name},{MaxLength},{DefaultValue},{Scale},{Precision},{IsResult}".ToLower();
        }



        public static string GetTypescriptTypeFromSql(string sqlType, Dictionary<string, Dictionary<string, RoutineParameterCustomType>> customType,
            ref Dictionary<string, string> customTypeLookupWithTypeScriptDef)
        {
            var elems = sqlType.ToLower().Split('.'); // types like geography could come through as sys.CATALOG.geography
            var dt = elems[elems.Length - 1];
            switch (dt)
            {
                case "table type":
                    return "Object";
                case "time":
                    return "Date";
                case "date":
                    return "Date";
                case "datetime":
                    return "Date";
                case "smalldatetime":
                    return "Date";
                case "int":
                    return "number";
                case "smallint":
                    return "number";
                case "bigint":
                    return "number";
                case "real":
                    return "number";
                case "bit":
                    return "boolean";
                case "nvarchar":
                    return "string";
                case "varchar":
                    return "string";
                case "text":
                    return "string";
                case "ntext":
                    return "string";
                case "varbinary":
                    return "Blob"; // TODO: Not sure about this one...worst case, make it a string
                case "decimal":
                    return "number";
                case "uniqueidentifier":
                    return "string";
                case "money":
                    return "number";
                case "char":
                    return "string";
                case "nchar":
                    return "string";
                case "xml":
                    return "string";
                case "float":
                    return "number";
                case "image":
                    return "Blob"; // TODO: Not sure about this one...worst case, make it a string
                case "tinyint":
                    return "number";
                case "geography":
                    return "jsDAL.LatLng";
                case "sql_variant":
                    return "string";
                case "timestamp":
                    return "string";
                case "binary":
                    return "Blob";  // TODO: Not sure about this one...worst case, make it a string                
                case "numeric":
                    return "number";
                case "sysname":
                    return "any";
                default:
                    {
                        if (customType != null && customType.Keys.Count > 0)
                        {
                            //?  lock (customTypeLookupWithTypeScriptDef)
                            {
                                var customTypeName = customType.Keys.First();
                                var typeName = $"$CustomType_{customTypeName}";

                                if (customTypeLookupWithTypeScriptDef.ContainsKey(typeName))
                                {
                                    return typeName;
                                }

                                var properties = new Dictionary<string, string>();

                                foreach (var kv in customType[customTypeName])
                                {
                                    var fieldName = kv.Key;
                                    var dataType = kv.Value.DataType;

                                    var tsTypeDef = GetTypescriptTypeFromSql(kv.Value.DataType, null, ref customTypeLookupWithTypeScriptDef);

                                    properties.Add(fieldName, tsTypeDef);
                                }

                                var customTSD = string.Join(", ", from kv in properties select $"{kv.Key}: {kv.Value}");
                                customTypeLookupWithTypeScriptDef.Add(typeName, $"{{{ customTSD }}}[]");//TODO: Custom types are not necessarily arrays?

                                return typeName;
                            }
                        }

                        return "any";
                    }
            }
        }

        public static string GetCSharpDataTypeFromSqlDbType(string sqlDataType)
        {
            var elems = sqlDataType.ToLower().Split('.'); // types like geography could come through as sys.CATALOG.geography
            var dt = elems[elems.Length - 1];
            switch (dt)
            {
                case "table type":
                    return typeof(object).FullName;
                case "time":
                    return typeof(DateTime).FullName;
                case "date":
                    return typeof(DateTime).FullName;
                case "datetime":
                    return typeof(DateTime).FullName;
                case "smalldatetime":
                    return typeof(DateTime).FullName;
                case "int":
                    return typeof(int).FullName;
                case "smallint":
                    return typeof(int).FullName;
                case "bigint":
                    return typeof(long).FullName;
                case "bit":
                    return typeof(bool).FullName;
                case "nvarchar":
                    return typeof(string).FullName;
                case "varchar":
                    return typeof(string).FullName;
                case "text":
                    return typeof(string).FullName;
                case "ntext":
                    return typeof(string).FullName;
                case "varbinary":
                    return typeof(string).FullName;
                case "decimal":
                    return typeof(decimal).FullName;
                case "uniqueidentifier":
                    return typeof(Guid).FullName;
                case "money":
                    return typeof(double).FullName;
                case "char":
                    return typeof(string).FullName;
                case "nchar":
                    return typeof(string).FullName;
                case "xml":
                    return "XmlString";
                case "float":
                    return typeof(double).FullName;
                case "image":
                    return typeof(byte[]).FullName;
                case "tinyint":
                    return typeof(int).FullName;
                case "geography":
                    return typeof(object).FullName;
                case "sql_variant":
                    return typeof(string).FullName;
                case "timestamp":
                    return typeof(string).FullName;
                case "numeric":
                    return typeof(decimal).FullName;
                default:

                    throw new Exception("GetDataTypeForCSharp::Unsupported data type: " + sqlDataType);
            }
        }

        public static string GetDataTypeForJavaScriptComment(string sqlType, Dictionary<string, Dictionary<string, RoutineParameterCustomType>> customType)
        {
            var elems = sqlType.ToLower().Split('.'); // types like geography could come through as sys.CATALOG.geography
            var dt = elems[elems.Length - 1];
            switch (dt)
            {
                case "table type":
                    return "TableType";
                case "time":
                    return "Date";
                case "date":
                    return "DateTime";
                case "datetime":
                    return "DateTime";
                case "smalldatetime":
                    return "DateTime";
                case "int":
                    return "int";
                case "smallint":
                    return "int";
                case "bigint":
                    return "long";
                case "bit":
                    return "bool";
                case "nvarchar":
                    return "string";
                case "varchar":
                    return "string";
                case "text":
                    return "string";
                case "ntext":
                    return "string";
                case "varbinary":
                    return "varbinary";
                case "binary":
                    return "binary";
                case "decimal":
                    return "float";
                case "uniqueidentifier":
                    return "Guid";
                case "money":
                    return "float";
                case "char":
                    return "string";
                case "nchar":
                    return "string";
                case "xml":
                    return "XmlString";
                case "float":
                    return "float";
                case "image":
                    return "byteArray";
                case "tinyint":
                    return "int";
                case "geography":
                    return "geography";
                case "sql_variant":
                    return "string";
                case "timestamp":
                    return "string";
                case "sysname":
                    return "string";
                default:
                    {
                        if (customType != null)
                        {
                            var key = customType.Keys.FirstOrDefault();

                            if (key != null) return key;
                        }

                        throw new Exception("getDataTypeForJavaScriptComment::Unsupported data type: " + sqlType);
                    }
            }
        }
    }

    public class RoutineParameterCustomType
    {
        public int Ordinal { get; set; }
        public string DataType { get; set; }

        public int MaxLength { get; set; }
        public int Precision { get; set; }
        public int Scale { get; set; }
        public bool IsNullable { get; set; }
    }
}

using System;
using System.Linq;
using System.Xml.Serialization;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Text.Json.Serialization;

namespace jsdal_server_core.Settings.ObjectModel
{

    [Serializable]
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
        [JsonConverter(typeof(InternedStringConverter))]
        public string SqlDataType { get; set; }

        [XmlAttribute("DefVal")]
        public string DefaultValue { get; set; }

        [XmlAttribute("Result")]
        public bool IsResult { get; set; }


        //private string _userType;

        [XmlElement("UserType")]
        [Newtonsoft.Json.JsonIgnore]
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
        [Newtonsoft.Json.JsonIgnore]
        public bool HasDefault
        {
            get { return this.DefaultValue != null; }
        }

        public string Hash()
        {
            return $"{SqlDataType},{Name},{MaxLength},{DefaultValue},{Scale},{Precision},{IsResult}".ToLower();
        }



        public static string GetTypescriptTypeFromSql(string sqlType, Dictionary<string, Dictionary<string, RoutineParameterCustomType>> customType,
            ref ConcurrentDictionary<string, string> customTypeLookupWithTypeScriptDef)
        {
            var elems = sqlType.ToLower().Split('.'); // types like geography could come through as sys.CATALOG.geography
            var dt = elems[elems.Length - 1];

            switch (dt)
            {
                case Strings.SQL.TABLE_TYPE:
                    return Strings.TS.OBJECT;
                case Strings.SQL.TIME:
                    return Strings.TS.DATE;
                case Strings.SQL.DATE:
                    return Strings.TS.DATE;
                case Strings.SQL.DATETIME:
                    return Strings.TS.DATE;
                case Strings.SQL.SMALLDATETIME:
                    return Strings.TS.DATE;
                case Strings.SQL.INT:
                    return Strings.TS.NUMBER;
                case Strings.SQL.SMALLINT:
                    return Strings.TS.NUMBER;
                case Strings.SQL.BIGINT:
                    return Strings.TS.NUMBER;
                case Strings.SQL.REAL:
                    return Strings.TS.NUMBER;
                case Strings.SQL.BIT:
                    return Strings.TS.BOOLEAN;
                case Strings.SQL.NVARCHAR:
                    return Strings.TS.STRING;
                case Strings.SQL.VARCHAR:
                    return Strings.TS.STRING;
                case Strings.SQL.TEXT:
                    return Strings.TS.STRING;
                case Strings.SQL.NTEXT:
                    return Strings.TS.STRING;
                case Strings.SQL.VARBINARY:
                    return Strings.TS.BLOB; // TODO: Not sure about this one...worst case, make it a string
                case Strings.SQL.DECIMAL:
                    return Strings.TS.NUMBER;
                case Strings.SQL.UNIQUEIDENTIFIER:
                    return Strings.TS.STRING;
                case Strings.SQL.MONEY:
                    return Strings.TS.NUMBER;
                case Strings.SQL.CHAR:
                    return Strings.TS.STRING;
                case Strings.SQL.NCHAR:
                    return Strings.TS.STRING;
                case Strings.SQL.XML:
                    return Strings.TS.STRING;
                case Strings.SQL.FLOAT:
                    return Strings.TS.NUMBER;
                case Strings.SQL.IMAGE:
                    return Strings.TS.BLOB; // TODO: Not sure about this one...worst case, make it a string
                case Strings.SQL.TINYINT:
                    return Strings.TS.NUMBER;
                case Strings.SQL.GEOGRAPHY:
                    return Strings.TS.jsDAL_LatLng;
                case Strings.SQL.SQL_VARIANT:
                    return Strings.TS.STRING;
                case Strings.SQL.TIMESTAMP:
                    return Strings.TS.STRING;
                case Strings.SQL.BINARY:
                    return Strings.TS.BLOB; // TODO: Not sure about this one...worst case, make it a string
                case Strings.SQL.NUMERIC:
                    return Strings.TS.NUMBER;
                case Strings.SQL.SYSNAME:
                    return Strings.TS.ANY;
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

                                //TODO: Custom types are not necessarily arrays?
                                if (!customTypeLookupWithTypeScriptDef.TryAdd(typeName, $"{{{ customTSD }}}[]"))
                                {
                                    SessionLog.Error($"Failed to add custom type {typeName} to dictionary");
                                }

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
                case Strings.SQL.TABLE_TYPE:
                    return Strings.DotNetTypes.Object;
                case Strings.SQL.TIME:
                    return Strings.DotNetTypes.DateTime;
                case Strings.SQL.DATE:
                    return Strings.DotNetTypes.DateTime;
                case Strings.SQL.DATETIME:
                    return Strings.DotNetTypes.DateTime;
                case Strings.SQL.SMALLDATETIME:
                    return Strings.DotNetTypes.DateTime;
                case Strings.SQL.INT:
                    return Strings.DotNetTypes.Int32;
                case Strings.SQL.SMALLINT:
                    return Strings.DotNetTypes.Int32;
                case Strings.SQL.BIGINT:
                    return Strings.DotNetTypes.Int64;
                case Strings.SQL.BIT:
                    return Strings.DotNetTypes.Boolean;
                case Strings.SQL.NVARCHAR:
                    return Strings.DotNetTypes.String;
                case Strings.SQL.VARCHAR:
                    return Strings.DotNetTypes.String;
                case Strings.SQL.TEXT:
                    return Strings.DotNetTypes.String;
                case Strings.SQL.NTEXT:
                    return Strings.DotNetTypes.String;
                case Strings.SQL.VARBINARY:
                    return Strings.DotNetTypes.String;
                case Strings.SQL.DECIMAL:
                    return Strings.DotNetTypes.Decimal;
                case Strings.SQL.UNIQUEIDENTIFIER:
                    return Strings.DotNetTypes.Guid;
                case Strings.SQL.MONEY:
                    return Strings.DotNetTypes.Double;
                case Strings.SQL.CHAR:
                    return Strings.DotNetTypes.String;
                case Strings.SQL.NCHAR:
                    return Strings.DotNetTypes.String;
                case Strings.SQL.XML:
                    return Strings.DotNetTypes.XmlString;
                case Strings.SQL.FLOAT:
                    return Strings.DotNetTypes.Double;
                case Strings.SQL.IMAGE:
                    return Strings.DotNetTypes.ByteArray;
                case Strings.SQL.TINYINT:
                    return Strings.DotNetTypes.Int32;
                case Strings.SQL.GEOGRAPHY:
                    return Strings.DotNetTypes.Object;
                case Strings.SQL.SQL_VARIANT:
                    return Strings.DotNetTypes.String;
                case Strings.SQL.TIMESTAMP:
                    return Strings.DotNetTypes.String;
                case Strings.SQL.NUMERIC:
                    return Strings.DotNetTypes.Decimal;
                default:
                    throw new Exception("GetDataTypeForCSharp::Unsupported data type: " + sqlDataType);
            }
        }

        // public static string GetDataTypeForJavaScriptComment(string sqlType, Dictionary<string, Dictionary<string, RoutineParameterCustomType>> customType)
        // {
        //     var elems = sqlType.ToLower().Split('.'); // types like geography could come through as sys.CATALOG.geography
        //     var dt = elems[elems.Length - 1];
        //     switch (dt)
        //     {
        //         case "table type":
        //             return "TableType";
        //         case "time":
        //             return "Date";
        //         case "date":
        //             return "DateTime";
        //         case "datetime":
        //             return "DateTime";
        //         case "smalldatetime":
        //             return "DateTime";
        //         case "int":
        //             return "int";
        //         case "smallint":
        //             return "int";
        //         case "bigint":
        //             return "long";
        //         case "bit":
        //             return "bool";
        //         case "nvarchar":
        //             return "string";
        //         case "varchar":
        //             return "string";
        //         case "text":
        //             return "string";
        //         case "ntext":
        //             return "string";
        //         case "varbinary":
        //             return "varbinary";
        //         case "binary":
        //             return "binary";
        //         case "decimal":
        //             return "float";
        //         case "uniqueidentifier":
        //             return "Guid";
        //         case "money":
        //             return "float";
        //         case "char":
        //             return "string";
        //         case "nchar":
        //             return "string";
        //         case "xml":
        //             return "XmlString";
        //         case "float":
        //             return "float";
        //         case "image":
        //             return "byteArray";
        //         case "tinyint":
        //             return "int";
        //         case "geography":
        //             return "geography";
        //         case "sql_variant":
        //             return "string";
        //         case "timestamp":
        //             return "string";
        //         case "sysname":
        //             return "string";
        //         default:
        //             {
        //                 if (customType != null)
        //                 {
        //                     var key = customType.Keys.FirstOrDefault();

        //                     if (key != null) return key;
        //                 }

        //                 throw new Exception("getDataTypeForJavaScriptComment::Unsupported data type: " + sqlType);
        //             }
        //     }
        // }
    }

    [Serializable]
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

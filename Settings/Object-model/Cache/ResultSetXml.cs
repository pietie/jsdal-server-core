using System;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.Xml.Serialization;

namespace jsdal_server_core.Settings.ObjectModel
{
    // wraps the ResultXml generated by SQL Server
    [Serializable]
    [XmlRoot("FieldCollection")]
    public class SqlResultSetFieldCollection
    {
        public SqlResultSetFieldCollection()
        {
            this.Fields = new List<SqlResultSetField>();
        }

        [XmlElement("Field")]
        public List<SqlResultSetField> Fields { get; set; }
    }


    public class SqlResultSetField
    {
        [XmlAttribute("ix")]
        public int ColumnOrdinal { get; set; }
        [XmlAttribute("name")]
        public string Name { get; set; }
        [XmlAttribute("type")]
        public string Type { get; set; }
        [XmlAttribute("size")]
        public int Size { get; set; }
        [XmlAttribute("prec")]
        public int Precision { get; set; }
        [XmlAttribute("scale")]
        public int Scale { get; set; }
        [XmlAttribute("error_number")]
        public int ErrorNumber { get; set; }
        [XmlAttribute("error_severity")]
        public int ErrorSeverity { get; set; }
        [XmlAttribute("error_msg")]
        public string ErrorMsg { get; set; }
        [XmlAttribute("error_type")]
        public int ErrorType { get; set; }
        [XmlAttribute("error_desc")]
        public string ErrorDescription { get; set; }
    }

    public class ResultSetFieldMetadata
    {
        public string ColumnName { get; set; }
        public string DataType { get; set; }
        public string DbDataType { get; set; }
        public int ColumnSize { get; set; }
        public int NumericalPrecision { get; set; }
        public int NumericalScale { get;set; }
    }
}
using System;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.Xml.Serialization;

namespace jsdal_server_core.Settings.ObjectModel
{


    [Serializable]
    [XmlRoot("Routine")]

    public class RoutineParameterContainerV2
    {
        public RoutineParameterContainerV2()
        {
            this.Parameters = new List<RoutineParameterV2>();
        }

        [XmlElement("Parm")]
        public List<RoutineParameterV2> Parameters { get; set; }
    }



}
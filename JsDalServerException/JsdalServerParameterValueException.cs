using System;

namespace jsdal_server_core
{
    public class JsdalServerParameterValueException : Exception
    {
        public JsdalServerParameterValueException(string paramName, string msg) : base($"Parameter: @{paramName} - {msg}")
        {

        }
    }
}
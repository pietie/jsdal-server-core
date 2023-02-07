using System;
using System.Text;
using jsdal_server_core.Settings.ObjectModel;

namespace jsdal_server_core;

public class JsDALExecutionException : Exception
{

    public string Schema { get; private set; }
    public string Routine { get; private set; }
    public string Endpoint { get; private set; }

    public ExecutionPolicy ExecutionPolicy { get; private set; }
    public JsDALExecutionException(string message, Exception innerException, string schema, string routine, string endpoint, ExecutionPolicy execPolicy = null) : base(message, innerException)
    {
        this.Schema = schema;
        this.Routine = routine;
        this.Endpoint = endpoint;
        this.ExecutionPolicy = execPolicy;
    }

    public override string ToString()
    {
        var sb = new StringBuilder(base.ToString());

        sb.AppendLine();
        sb.Append($"Endpoint: {this.Endpoint}");
        sb.AppendLine();
        sb.Append($"Routine: [{this.Schema}].[{this.Routine}]");


        return sb.ToString();
    }
}
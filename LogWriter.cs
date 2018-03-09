using System;
using System.IO;
using System.Text;

namespace jsdal_server_core
{
    public class LogWriter : StreamWriter
    {
        public LogWriter(Stream stream) : base(stream)
        {
        }

        public LogWriter(Stream stream, Encoding encoding) : base(stream, encoding)
        {
        }

        public LogWriter(Stream stream, Encoding encoding, int bufferSize) : base(stream, encoding, bufferSize)
        {
        }

        public LogWriter(Stream stream, Encoding encoding, int bufferSize, bool leaveOpen) : base(stream, encoding, bufferSize, leaveOpen)
        {
        }

        public LogWriter(string path) : base(path)
        {
        }

        public LogWriter(string path, bool append) : base(path, append)
        {
        }

        public LogWriter(string path, bool append, Encoding encoding) : base(path, append, encoding)
        {
        }

        public LogWriter(string path, bool append, Encoding encoding, int bufferSize) : base(path, append, encoding, bufferSize)
        {
        }

        public override void WriteLine(string value)
        {
            base.WriteLine(string.Format("{0:HH:mm:ss}\t{1}", DateTime.Now, value));
        }

        public override void WriteLine(string format, params object[] arg)
        {
            base.WriteLine(format, arg);
        }
        public override void WriteLine(string format, object arg0, object arg1, object arg2)
        {
            base.WriteLine(format, arg0, arg1, arg2);
        }

        public override void WriteLine(string format, object arg0, object arg1)
        {
            base.WriteLine(format, arg0, arg1);
        }


        public override System.Text.Encoding Encoding { get { return System.Text.Encoding.UTF8; } }
    }



}


// TODO: Move to separate lib?
/*
public class jsDALPlugin
{
    public Dictionary<string, string> QueryString { get; private set; }

    public string Name { get; protected set; }
    public string Description { get; protected set; }

    private void InitPlugin(Dictionary<string, string> queryStringCollection)
    {
        this.QueryString = queryStringCollection;
    }

    public jsDALPlugin()
    {

    }

    public virtual void OnConnectionOpened(System.Data.SqlClient.SqlConnection con) { }
}

 */

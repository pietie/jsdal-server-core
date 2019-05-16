using System;

namespace jsdal_server_core.Performance.Bucket
{
    public class BucketBase
    {
        private string _format;
        public BucketBase()
        {

        }

        public void Add(BucketMetric measure, object value)
        {
            if (this._format == null) throw new Exception("Bucket's format not set");

            var key = DateTime.Now.ToString(this._format);


        }

        public static BucketBase CreatePerSecond()
        {
            return new BucketBase() { _format = "YYYYMMDDHHMMSS" };
        }
        public static BucketBase CreatePerMinute()
        {
            return new BucketBase() { _format = "YYYYMMDDHHMM" };
        }
    }

    public enum BucketMetric
    {
        Cnt = 100,
        Duration = 200,
        Rows = 300
    }
}
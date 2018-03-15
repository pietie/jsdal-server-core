using System;

namespace jsdal_server_core
{
    public static class MyExtensions
    {
        public static long? ToEpochMS(this DateTime? dt)
        {
            if (!dt.HasValue) return null;
            var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            return Convert.ToInt64((dt.Value.ToUniversalTime() - epoch).TotalSeconds) * 1000;
        }

    }
}
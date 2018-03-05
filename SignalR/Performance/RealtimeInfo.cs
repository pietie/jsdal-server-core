namespace jsdal_server_core.Hubs
{
    public class RealtimeInfo
    {
        public string name { get; set; }
        public long? createdEpoch { get; set; }
        public long? durationMS { get; set; }

        public int rowsAffected { get;set; }
    }

}
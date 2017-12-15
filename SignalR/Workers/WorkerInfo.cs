namespace jsdal_server_core.Hubs
{
    public class WorkerInfo
    {
        public string id { get; set; }
        public string name { get; set; }
        public string desc { get; set; }
        public string status { get; set; }
        public string lastProgress { get; set; }

        public bool isRunning { get; set; }

        /*
        new
                        {
                            id = wl.ID,
                            name = wl.DBSource.Name,
                            desc = wl.Description,
                            status = wl.Status,
                            /*lastProgress = wl.lastProgress,
                            lastProgressMoment = wl.lastProgressMoment,
                            lastConnectMoment = wl.lastConnectedMoment,
        isRunning = wl.IsRunning */
    }

}
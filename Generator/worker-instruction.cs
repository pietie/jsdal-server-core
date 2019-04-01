using jsdal_server_core.Settings.ObjectModel;

namespace jsdal_server_core
{
    public class WorkerInstruction
    {
        public WorkerInstructionType Type { get;set; }
        public JsFile JsFile { get;set; }
    }

    public enum WorkerInstructionType
    {
        Unknown = 0,
        RegenAllFiles = 10,
        RegenSpecificFile = 20
    }
}
namespace jsdal_server_core.Settings.ObjectModel
{
    public class CommonReturnValue
    {
        protected bool? successVal;
        protected string userErrorMsg;

        public static CommonReturnValue userError(string ue)
        {
            return new CommonReturnValue() { successVal = false, userErrorMsg = ue };
        }
// TODO: Redo all property and method names
        public string userErrorVal { get { return userErrorMsg; } }

        public static CommonReturnValue success() { return new CommonReturnValue() { successVal = true }; }

        public bool IsSuccess { get { return successVal ?? false; } }
    }

    public class CommonReturnValueWithApplication : CommonReturnValue
    {
        public Application app;

        public static CommonReturnValueWithApplication success(Application app)
        {
            var ret = new CommonReturnValueWithApplication();

            ret.successVal = true;
            ret.app = app;

            return ret;
        }

        public new static CommonReturnValueWithApplication userError(string ue)
        {
            return new CommonReturnValueWithApplication() { successVal = false, userErrorMsg = ue };
        }
    }
}
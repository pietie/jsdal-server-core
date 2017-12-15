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

        public bool isSuccess { get { return successVal ?? false; } }
    }

    public class CommonReturnValueWithDbSource : CommonReturnValue
    {
        public DatabaseSource dbSource;

        public static CommonReturnValueWithDbSource success(DatabaseSource dbs)
        {
            var ret = new CommonReturnValueWithDbSource();

            ret.successVal = true;
            ret.dbSource = dbs;

            return ret;
        }

        public new static CommonReturnValueWithDbSource userError(string ue)
        {
            return new CommonReturnValueWithDbSource() { successVal = false, userErrorMsg = ue };
        }
    }
}
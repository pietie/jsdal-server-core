using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace jsdal_server_core
{
    class UserManagement
    {
        private static readonly string UserFilename = "users.json";
        private static List<jsDALServerUser> users;

        public static bool adminUserExists
        {
            get
            {
                if (UserManagement.users == null) return false;
                return UserManagement.users.FirstOrDefault(u => u.isAdmin) != null;
            }
        }

        public static void addUser(jsDALServerUser user)
        {
            // TODO: Validate values. Check for existing users..blah blah
            if (UserManagement.users == null) UserManagement.users = new List<jsDALServerUser>();

            UserManagement.users.Add(user);
        }

        public static void loadUsersFromFile()
        {
            try
            {
                UserManagement.users = new List<jsDALServerUser>();

                if (!File.Exists(UserManagement.UserFilename)) return;

                var content = File.ReadAllText(UserManagement.UserFilename, System.Text.Encoding.UTF8);

                var users = JsonConvert.DeserializeObject<jsDALServerUser[]>(content);

                UserManagement.users = users.ToList();
            }
            catch (Exception e)
            {
                //!ExceptionLogger.logException(e);
            }
        }

        public static void saveToFile()
        {
            try
            {
                var json = JsonConvert.SerializeObject(UserManagement.users);

                File.WriteAllText(UserManagement.UserFilename, json, System.Text.Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                //!SessionLog.Exception(ex);
            }
        }

        public static bool validate(string username, string password)
        {
            //return UserManagement.users.find(u => u.username === username && u.password == password) != null;
            return UserManagement.users.FirstOrDefault(u => u.username.Equals(username, StringComparison.OrdinalIgnoreCase)
             && u.password.Equals(password, StringComparison.OrdinalIgnoreCase)) != null;
        }

    }


    class jsDALServerUser
    {
        public string username;
        public string password;
        public bool isAdmin;
    }
}
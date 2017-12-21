using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace jsdal_server_core.Settings.ObjectModel
{
    public class Connection
    {
        public string Name;
        public string Guid;
        public string ConnectionString;

        [JsonIgnore]
        public int port;
        [JsonIgnore]
        public string instanceName;

        public bool Unsafe = false; // if set true it means the ConnectionString is not encrypted

        private SqlConnectionStringBuilder _connectionStringBuilder;

        // public toJSON()
        // {
        //     return {
        //     Name: this.Name,
        //         Guid: this.Guid,
        //         ConnectionString: this.ConnectionString,
        //         Unsafe: this.Unsafe,
        //         port: this.port,
        //         instanceName: this.instanceName
        //     };
        // }

        [JsonIgnore]
        public string userID
        {
            get
            {
                if (this._connectionStringBuilder == null) this._connectionStringBuilder = new SqlConnectionStringBuilder(this.ConnectionStringDecrypted);
                return this._connectionStringBuilder.UserID;
            }
        }
        [JsonIgnore]
        public string password
        {
            get
            {
                if (this._connectionStringBuilder == null) this._connectionStringBuilder = new SqlConnectionStringBuilder(this.ConnectionStringDecrypted);
                return this._connectionStringBuilder.Password;
            }
        }

        [JsonIgnore]
        public string dataSource
        {
            get
            {
                if (this._connectionStringBuilder == null) this._connectionStringBuilder = new SqlConnectionStringBuilder(this.ConnectionStringDecrypted);
                return this._connectionStringBuilder?.DataSource?.Split(',')[0]; // Return DataSource without the port
            }

        }

        [JsonIgnore]
        public string initialCatalog
        {
            get
            {
                if (this._connectionStringBuilder == null) this._connectionStringBuilder = new SqlConnectionStringBuilder(this.ConnectionStringDecrypted);
                return this._connectionStringBuilder.InitialCatalog;
            }
        }

        [JsonIgnore]
        public bool integratedSecurity
        {
            get
            {
                if (this._connectionStringBuilder == null) this._connectionStringBuilder = new SqlConnectionStringBuilder(this.ConnectionStringDecrypted);
                return this._connectionStringBuilder.IntegratedSecurity;
            }
        }

        /*    public static createFromJson(rawJson: any): Connection {

                let connection = new Connection();

            connection.Name = rawJson.Name;
                connection.Guid = rawJson.Guid;
                connection.ConnectionString = rawJson.ConnectionString;
                connection.Unsafe = !!rawJson.Unsafe;
                connection.port = rawJson.port != null ? rawJson.port : 1433;
        connection.instanceName = rawJson.instanceName;

                return connection;
            }*/

        private string _descryptedConnectionString;

        [JsonIgnore]
        public string ConnectionStringDecrypted
        {
            get
            {
                if (string.IsNullOrEmpty(this._descryptedConnectionString))
                {
                    if (this.Unsafe)
                    {
                        this._descryptedConnectionString = this.ConnectionString;
                    }
                    else
                    {
                        try
                        {
                            this._descryptedConnectionString = ConnectionStringSecurity.decrypt(this.ConnectionString);
                        }
                        catch (Exception e)
                        {
                            this._descryptedConnectionString = "";
                            SessionLog.Error("Failed to decrypt ConnectionString.");
                            SessionLog.Exception(e);
                        }
                    }
                }

                try
                {
                    // validate 
                    if (this._descryptedConnectionString != null)
                    {
                        new SqlConnectionStringBuilder(this._descryptedConnectionString);
                    }

                }
                catch
                {
                    this._descryptedConnectionString = "";
                }

                return this._descryptedConnectionString;
            }
        }

        public void update(string name, string dataSource, string catalog, string username, string password, int port, string instanceName)
        {
            string connectionString = null;

            this.port = port;
            this.instanceName = instanceName;

            if (!string.IsNullOrWhiteSpace(username))
            {
                connectionString = $"Data Source={ dataSource },{ port }; Initial Catalog={ catalog }; Persist Security Info = False; User ID={ username }; Password={ password }";
            }
            else
            {
                // use windows auth
                connectionString = $"Data Source={ dataSource },{ port }; Initial Catalog={ catalog }; Persist Security Info=False; Integrated Security=sspi";
            }

            this.Name = name;

            this._descryptedConnectionString = null;
            this._connectionStringBuilder = null;

            this.Unsafe = false;
            this.ConnectionString = ConnectionStringSecurity.encrypt(connectionString);

        }

    }
}


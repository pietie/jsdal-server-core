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
        public Connection()
        {

        }
        public string ConnectionString;

        [JsonIgnore]
        public Endpoint Endpoint;


        [JsonIgnore]
        public string Type;

        //[JsonIgnore] public string instanceName;

        public bool Unsafe = false; // if set true it means the ConnectionString is not encrypted

        private SqlConnectionStringBuilder _connectionStringBuilder;

        [JsonIgnore]
        public string UserID
        {
            get
            {
                if (this._connectionStringBuilder == null) this._connectionStringBuilder = new SqlConnectionStringBuilder(this.ConnectionStringDecrypted);
                return this._connectionStringBuilder.UserID;
            }
        }
        [JsonIgnore]
        public string Password
        {
            get
            {
                if (this._connectionStringBuilder == null) this._connectionStringBuilder = new SqlConnectionStringBuilder(this.ConnectionStringDecrypted);
                return this._connectionStringBuilder.Password;
            }
        }

        [JsonIgnore]
        public string DataSource
        {
            get
            {
                if (this._connectionStringBuilder == null) this._connectionStringBuilder = new SqlConnectionStringBuilder(this.ConnectionStringDecrypted);
                return this._connectionStringBuilder?.DataSource?.Split(',')[0]; // Return DataSource without the port
            }

        }

        [JsonIgnore]
        public string InitialCatalog
        {
            get
            {
                if (this._connectionStringBuilder == null) this._connectionStringBuilder = new SqlConnectionStringBuilder(this.ConnectionStringDecrypted);
                return this._connectionStringBuilder.InitialCatalog;
            }
        }

        [JsonIgnore]
        public bool IntegratedSecurity
        {
            get
            {
                if (this._connectionStringBuilder == null) this._connectionStringBuilder = new SqlConnectionStringBuilder(this.ConnectionStringDecrypted);
                return this._connectionStringBuilder.IntegratedSecurity;
            }
        }

        [JsonIgnore]
        public int Port
        {
            get
            {
                if (this._connectionStringBuilder == null) this._connectionStringBuilder = new SqlConnectionStringBuilder(this.ConnectionStringDecrypted);

                var elems = this._connectionStringBuilder.DataSource.Split(',');

                if (elems.Length == 2) return int.Parse(elems[1]);
                else return 1433;
            }
        }

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
                            this._descryptedConnectionString = null;
                            if (!string.IsNullOrWhiteSpace(this.ConnectionString))
                            {
                                this._descryptedConnectionString = ConnectionStringSecurity.Instance.Decrypt(this.ConnectionString);

                                if (this._descryptedConnectionString == null)
                                {
                                       SessionLog.Error($"Failed to decrypt {this.Type} ConnectionString on {this.Endpoint.Pedigree}.");
                                }
                            }
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

        public void Update(Endpoint endpoint, string type, string dataSource, string catalog, string username, string password, int port, string instanceName)
        {
            string connectionString = null;


            if (!string.IsNullOrWhiteSpace(username))
            {
                // retain current password if no new one was specified
                if (string.IsNullOrWhiteSpace(password) && !string.IsNullOrWhiteSpace(this.ConnectionStringDecrypted))
                {
                    password = this.Password;
                }

                connectionString = $"Data Source={ dataSource },{ port }; Initial Catalog={ catalog }; Persist Security Info = False; User ID={ username }; Password={ password }";
            }
            else
            {
                // use windows auth
                connectionString = $"Data Source={ dataSource },{ port }; Initial Catalog={ catalog }; Persist Security Info=False; Integrated Security=sspi";
            }

            this._descryptedConnectionString = null;
            this._connectionStringBuilder = null;

            this.Unsafe = false;
            this.ConnectionString = ConnectionStringSecurity.Instance.Encrypt(connectionString);
            this.Endpoint = endpoint;
            this.Type = type;
        }

    }
}


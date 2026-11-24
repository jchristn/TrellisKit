namespace TrellisKit.Core.Database
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Database connection settings. Changes require a restart.
    /// </summary>
    public class DatabaseSettings
    {
        #region Public-Members

        /// <summary>
        /// Database provider. Default Postgresql (the only provider).
        /// </summary>
        public DatabaseTypeEnum Type { get; set; } = DatabaseTypeEnum.Postgresql;

        /// <summary>
        /// Server hostname. Default "127.0.0.1".
        /// </summary>
        public string Hostname { get; set; } = "127.0.0.1";

        /// <summary>
        /// Server port. Default 5432. Minimum 1, maximum 65535.
        /// </summary>
        public int Port
        {
            get
            {
                return _Port;
            }
            set
            {
                _Port = Math.Clamp(value, 1, 65535);
            }
        }

        /// <summary>
        /// Database name. Default "trelliskit".
        /// </summary>
        public string DatabaseName { get; set; } = "trelliskit";

        /// <summary>
        /// Username. Default "postgres".
        /// </summary>
        public string Username { get; set; } = "postgres";

        /// <summary>
        /// Password. Default empty. Supply through the TK_DB_PASSWORD environment variable in production.
        /// </summary>
        public string Password { get; set; } = String.Empty;

        /// <summary>
        /// Optional PostgreSQL schema (search_path). Default null, which uses "public".
        /// </summary>
        public string? Schema { get; set; } = null;

        /// <summary>
        /// Command timeout in seconds. Default 30. Minimum 1, maximum 600.
        /// </summary>
        public int CommandTimeoutSeconds
        {
            get
            {
                return _CommandTimeoutSeconds;
            }
            set
            {
                _CommandTimeoutSeconds = Math.Clamp(value, 1, 600);
            }
        }

        /// <summary>
        /// Maximum pooled connections per server process for server databases. Default 20. Minimum 1, maximum 1000.
        /// Keep (instances x MaxPoolSize) below the server's max_connections; requests beyond the pool wait for a
        /// free connection rather than failing.
        /// </summary>
        public int MaxPoolSize
        {
            get
            {
                return _MaxPoolSize;
            }
            set
            {
                _MaxPoolSize = Math.Clamp(value, 1, 1000);
            }
        }

        #endregion

        #region Private-Members

        private int _MaxPoolSize = 20;
        private int _Port = 5432;
        private int _CommandTimeoutSeconds = 30;

        #endregion
    }
}

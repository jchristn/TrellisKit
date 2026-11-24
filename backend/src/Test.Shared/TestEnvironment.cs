namespace Test.Shared
{
    using System;
    using System.IO;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Test configuration shared by every host. Every backend test runs against PostgreSQL (docs/TRELLISKIT.md
    /// section 13) with pgvector: each test gets its own schema. Connection values come from TK_TEST_PG_HOST,
    /// TK_TEST_PG_PORT, TK_TEST_PG_DATABASE, TK_TEST_PG_USERNAME, and TK_TEST_PG_PASSWORD, defaulting to the CI service
    /// container (127.0.0.1:5432, trelliskit_test, postgres). Start a throwaway server locally with
    /// docker run -d -e POSTGRES_PASSWORD=Test_Passw0rd -e POSTGRES_DB=trelliskit_test -p 127.0.0.1:5432:5432 pgvector/pgvector:pg17.
    /// </summary>
    public static class TestEnvironment
    {
        #region Public-Members

        /// <summary>
        /// Database provider (always PostgreSQL).
        /// </summary>
        public static DatabaseTypeEnum Provider
        {
            get { return DatabaseTypeEnum.Postgresql; }
        }

        /// <summary>
        /// Root directory for temporary test files.
        /// </summary>
        public static string TempRoot
        {
            get
            {
                string path = Path.Combine(Path.GetTempPath(), "trelliskit-tests");
                Directory.CreateDirectory(path);
                return path;
            }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Create isolated database settings for one test: a new PostgreSQL schema.
        /// </summary>
        /// <returns>Settings.</returns>
        public static DatabaseSettings CreateDatabaseSettings()
        {
            string unique = Guid.NewGuid().ToString("N").Substring(0, 12);
            return new DatabaseSettings
            {
                Type = DatabaseTypeEnum.Postgresql,
                Hostname = Read("TK_TEST_PG_HOST", "127.0.0.1"),
                Port = Int32.Parse(Read("TK_TEST_PG_PORT", "5432"), System.Globalization.CultureInfo.InvariantCulture),
                DatabaseName = Read("TK_TEST_PG_DATABASE", "trelliskit_test"),
                Username = Read("TK_TEST_PG_USERNAME", "postgres"),
                Password = Read("TK_TEST_PG_PASSWORD", "Test_Passw0rd"),
                Schema = "t_" + unique
            };
        }

        #endregion

        #region Private-Methods

        private static string Read(string name, string fallback)
        {
            string? value = Environment.GetEnvironmentVariable(name);
            return String.IsNullOrWhiteSpace(value) ? fallback : value;
        }

        #endregion
    }
}

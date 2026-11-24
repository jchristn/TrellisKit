namespace TrellisKit.Core.Database
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A versioned, tracked schema migration. Statements must be idempotent and run in a single transaction.
    /// </summary>
    public class SchemaMigration
    {
        #region Public-Members

        /// <summary>
        /// Placeholder replaced in statements with the SQL literal of <see cref="DatabaseDriverBase.MigrationPropertyId"/>.
        /// </summary>
        public const string PropertyIdPlaceholder = "{{propertyId}}";

        /// <summary>
        /// Migration version. Applied in ascending order. Minimum 1.
        /// </summary>
        public int Version
        {
            get { return _Version; }
            set { _Version = Math.Max(1, value); }
        }

        /// <summary>
        /// Short description recorded in the schema_migrations table.
        /// </summary>
        public string Description { get; set; } = String.Empty;

        /// <summary>
        /// Ordered SQL statements. Never null.
        /// </summary>
        public List<string> Statements
        {
            get { return _Statements; }
            set { _Statements = value ?? new List<string>(); }
        }

        #endregion

        #region Private-Members

        private int _Version = 1;
        private List<string> _Statements = new List<string>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate an empty migration.
        /// </summary>
        public SchemaMigration()
        {
        }

        /// <summary>
        /// Instantiate a migration.
        /// </summary>
        /// <param name="version">Version, minimum 1.</param>
        /// <param name="description">Description.</param>
        /// <param name="statements">Ordered statements.</param>
        public SchemaMigration(int version, string description, List<string> statements)
        {
            Version = version;
            Description = description ?? String.Empty;
            Statements = statements;
        }

        #endregion
    }
}

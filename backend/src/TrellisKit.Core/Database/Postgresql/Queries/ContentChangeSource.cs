namespace TrellisKit.Core.Database.Postgresql.Queries
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// One member-content table the content changelog triggers watch (docs/INTEGRATION.md content review contract): which
    /// columns are the target, the author, the reviewed text, and the media references, how a soft delete is recognized,
    /// and which rows are not member contributions (the filter). Generates the three triggers (insert, update, delete).
    /// Immutable.
    /// </summary>
    internal sealed class ContentChangeSource
    {
        #region Internal-Members

        internal string Key { get; }

        internal string Table { get; }

        internal string TargetType { get; }

        internal string IdColumn { get; }

        internal IReadOnlyList<string> AuthorColumns { get; }

        internal IReadOnlyList<string> TextColumns { get; }

        internal IReadOnlyList<string> MediaColumns { get; }

        /// <summary>
        /// Soft-delete timestamp column, "~column" when a text column becoming null is the deletion, or null.
        /// </summary>
        internal string? DeletedColumn { get; }

        /// <summary>
        /// Context type: a literal type name, "@column" to read it from a column, or null.
        /// </summary>
        internal string? ContextType { get; }

        internal string? ContextIdColumn { get; }

        /// <summary>
        /// SQL condition on the row ({r} stands for NEW or OLD) that every recorded row meets, or null.
        /// </summary>
        internal string? Filter { get; }

        /// <summary>
        /// Skip inserts and deletes of rows whose text columns are all null or empty.
        /// </summary>
        internal bool RequireText { get; }

        #endregion

        #region Private-Members

        private bool HasTimestampDelete
        {
            get { return DeletedColumn != null && !DeletedColumn.StartsWith('~'); }
        }

        #endregion

        #region Constructors-and-Factories

        internal ContentChangeSource(string key, string table, string targetType, string idColumn, string authorColumns, string textColumns, string mediaColumns,
            string? deletedColumn = null, string? contextType = null, string? contextIdColumn = null, string? filter = null, bool requireText = false)
        {
            Key = key;
            Table = table;
            TargetType = targetType;
            IdColumn = idColumn;
            AuthorColumns = Split(authorColumns);
            TextColumns = Split(textColumns);
            MediaColumns = Split(mediaColumns);
            DeletedColumn = deletedColumn;
            ContextType = contextType;
            ContextIdColumn = contextIdColumn;
            Filter = filter;
            RequireText = requireText;
        }

        #endregion

        #region Internal-Methods

        /// <summary>
        /// DROP and CREATE statements of the source's triggers.
        /// </summary>
        internal List<string> Statements()
        {
            List<string> statements = new List<string>();
            string args = "'" + TargetType + "', '" + IdColumn + "', '" + String.Join(",", AuthorColumns) + "', '" + String.Join(",", TextColumns) + "', '" +
                String.Join(",", MediaColumns) + "', '" + (DeletedColumn ?? String.Empty) + "', '" + (ContextType ?? String.Empty) + "', '" + (ContextIdColumn ?? String.Empty) + "'";
            string call = " FOR EACH ROW";
            string function = " EXECUTE FUNCTION trelliskit_content_change(" + args + ");";

            statements.Add("DROP TRIGGER IF EXISTS contentchanges_" + Key + "_ins ON " + Table + ";");
            statements.Add("CREATE TRIGGER contentchanges_" + Key + "_ins AFTER INSERT ON " + Table + call + When(InsertConditions()) + function);

            List<string> changed = TextColumns.Concat(MediaColumns).Select(c => "OLD." + c + " IS DISTINCT FROM NEW." + c).ToList();
            List<string> update = new List<string>();
            if (Filter != null) update.Add(Row("NEW"));
            if (HasTimestampDelete)
            {
                update.Add("(OLD." + DeletedColumn + " IS NULL OR NEW." + DeletedColumn + " IS NULL)");
                changed.Add("(OLD." + DeletedColumn + " IS NULL AND NEW." + DeletedColumn + " IS NOT NULL)");
            }
            statements.Add("DROP TRIGGER IF EXISTS contentchanges_" + Key + "_upd ON " + Table + ";");
            if (changed.Count > 0)
            {
                update.Add("(" + String.Join(" OR ", changed) + ")");
                statements.Add("CREATE TRIGGER contentchanges_" + Key + "_upd AFTER UPDATE ON " + Table + call + When(update) + function);
            }

            List<string> delete = new List<string>();
            if (Filter != null) delete.Add(Row("OLD"));
            if (HasTimestampDelete) delete.Add("OLD." + DeletedColumn + " IS NULL");
            if (RequireText || (DeletedColumn != null && DeletedColumn.StartsWith('~'))) delete.Add(HasText("OLD"));
            statements.Add("DROP TRIGGER IF EXISTS contentchanges_" + Key + "_del ON " + Table + ";");
            statements.Add("CREATE TRIGGER contentchanges_" + Key + "_del AFTER DELETE ON " + Table + call + When(delete) + function);
            return statements;
        }

        #endregion

        #region Private-Methods

        private List<string> InsertConditions()
        {
            List<string> insert = new List<string>();
            if (Filter != null) insert.Add(Row("NEW"));
            if (RequireText) insert.Add(HasText("NEW"));
            return insert;
        }

        private string Row(string alias)
        {
            return "(" + Filter!.Replace("{r}", alias, StringComparison.Ordinal) + ")";
        }

        private string HasText(string alias)
        {
            return "(" + String.Join(" OR ", TextColumns.Select(c => "COALESCE(" + alias + "." + c + ", '') <> ''")) + ")";
        }

        private static string When(List<string> conditions)
        {
            return conditions.Count == 0 ? String.Empty : " WHEN (" + String.Join(" AND ", conditions) + ")";
        }

        private static List<string> Split(string columns)
        {
            return (columns ?? String.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        }

        #endregion
    }
}

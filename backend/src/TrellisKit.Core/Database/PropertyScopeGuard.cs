namespace TrellisKit.Core.Database
{
    using System;
    using System.Text;
    using System.Text.RegularExpressions;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Observability;

    /// <summary>
    /// Last line of the property isolation defence (docs/MULTIPROPERTY_CONTRACT.md section 4.3): before a
    /// property-bound driver executes a statement, every reference to a property-owned table (FROM, JOIN, INTO, UPDATE,
    /// outside string literals) must be matched by a propertyid mention in the same statement. A statement that fails
    /// the check is never executed.
    /// </summary>
    public static class PropertyScopeGuard
    {
        #region Private-Members

        private static readonly Regex _TableReference = new Regex("\\b(?:from|join|into|update)\\s+([a-z_][a-z0-9_]*)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex _PropertyColumn = new Regex("\\bpropertyid\\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Verify one statement.
        /// </summary>
        /// <param name="statement">SQL statement.</param>
        /// <exception cref="PropertyScopeException">A property-owned table is referenced without a propertyid predicate.</exception>
        public static void Verify(string statement)
        {
            if (String.IsNullOrWhiteSpace(statement)) return;
            string code = StripLiterals(statement).ToLowerInvariant();
            int references = 0;
            string? first = null;
            foreach (Match match in _TableReference.Matches(code))
            {
                string table = match.Groups[1].Value;
                if (!PropertyTables.IsOwned(table)) continue;
                references++;
                first ??= table;
            }
            if (references == 0) return;
            int scoped = _PropertyColumn.Matches(code).Count;
            if (scoped >= references) return;
            TrellisKitTelemetry.Increment(TrellisKitTelemetry.IsolationViolations, "kind", "missing_scope");
            throw new PropertyScopeException("missing_scope", "A statement references property-owned table " + first + " without a propertyid predicate (" + references + " table references, " + scoped + " propertyid mentions).");
        }

        #endregion

        #region Private-Methods

        private static string StripLiterals(string sql)
        {
            StringBuilder builder = new StringBuilder(sql.Length);
            bool inLiteral = false;
            for (int i = 0; i < sql.Length; i++)
            {
                char c = sql[i];
                if (inLiteral)
                {
                    if (c == '\'')
                    {
                        if (i + 1 < sql.Length && sql[i + 1] == '\'') i++;
                        else
                        {
                            inLiteral = false;
                            builder.Append("''");
                        }
                    }
                    continue;
                }
                if (c == '\'')
                {
                    inLiteral = true;
                    continue;
                }
                builder.Append(c);
            }
            return builder.ToString();
        }

        #endregion
    }
}

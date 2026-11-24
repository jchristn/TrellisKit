namespace TrellisKit.Core.Database.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Provider-specific SQL fragments used by the shared method implementations. The provider supplies one in its
    /// Queries folder, so every provider-specific fragment is visible in one place.
    /// </summary>
    internal abstract class SqlDialect
    {
        internal abstract string Name { get; }

        internal abstract string Str(string? value);

        internal abstract string Ts(DateTime value);

        internal abstract string Bool(bool value);

        internal abstract string Num(long value);

        internal abstract string Num(double value);

        /// <summary>
        /// Expression that formats a timestamp column as its UTC day, YYYY-MM-DD.
        /// </summary>
        internal abstract string SubscriptionUpdateWithMetrics(string updateSql, string propertyId, string subscriptionId, DateTime occurredUtc);

        internal abstract string DayOf(string column);

        /// <summary>
        /// Whether an exception from the provider is a unique constraint violation.
        /// </summary>
        internal abstract bool IsUniqueViolation(Exception exception);

        internal string Ts(DateTime? value)
        {
            if (value == null) return "NULL";
            return Ts(value.Value);
        }

        internal string Num(int value)
        {
            return Num((long)value);
        }

        internal string Num(int? value)
        {
            if (value == null) return "NULL";
            return Num((long)value.Value);
        }

        internal string Enum<T>(T value) where T : struct, System.Enum
        {
            return Str(value.ToString());
        }

        internal string Enum<T>(T? value) where T : struct, System.Enum
        {
            if (value == null) return "NULL";
            return Str(value.Value.ToString());
        }

        internal string Contains(string column, string text)
        {
            string escaped = text.ToLowerInvariant().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
            return "lower(" + column + ") LIKE " + Str("%" + escaped + "%") + " ESCAPE '\\'";
        }

        internal string EqualsIgnoreCase(string column, string value)
        {
            return "lower(" + column + ") = lower(" + Str(value) + ")";
        }

        /// <summary>
        /// Index-friendly predicate: expression starts with a (lowercase) prefix. PostgreSQL uses LIKE with
        /// text_pattern_ops indexes.
        /// </summary>
        internal abstract string StartsWith(string expression, string prefix);

        /// <summary>
        /// Escape a value for use inside a LIKE pattern with ESCAPE '\'.
        /// </summary>
        internal static string LikeEscape(string text)
        {
            return (text ?? String.Empty).Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
        }

        internal string InList(IEnumerable<string> values)
        {
            List<string> literals = values.Distinct(StringComparer.Ordinal).Select(v => Str(v)).ToList();
            if (literals.Count == 0) return "(NULL)";
            return "(" + String.Join(", ", literals) + ")";
        }
    }
}

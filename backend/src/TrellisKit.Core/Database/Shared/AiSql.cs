namespace TrellisKit.Core.Database.Shared
{
    using System;
    using System.Data;
    using System.Globalization;

    /// <summary>
    /// Small SQL and row helpers shared by the AI method sets.
    /// </summary>
    internal static class AiSql
    {
        internal static string Dbl(SqlDialect d, double? value)
        {
            if (value == null || Double.IsNaN(value.Value) || Double.IsInfinity(value.Value)) return "NULL";
            return d.Num(value.Value);
        }

        internal static string Long(SqlDialect d, long? value)
        {
            return value == null ? "NULL" : d.Num(value.Value);
        }

        internal static string Bool(SqlDialect d, bool? value)
        {
            return value == null ? "NULL" : d.Bool(value.Value);
        }

        internal static bool? NullableBool(DataRow row, string column)
        {
            if (!row.Table.Columns.Contains(column)) return null;
            object value = row[column];
            if (value == null || value == DBNull.Value) return null;
            return Convert.ToBoolean(value, CultureInfo.InvariantCulture);
        }

        internal static double? NullableDouble(DataRow row, string column)
        {
            if (!row.Table.Columns.Contains(column)) return null;
            object value = row[column];
            if (value == null || value == DBNull.Value) return null;
            return Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }

        internal static string Normalize(string value)
        {
            return (value ?? String.Empty).Trim().ToLowerInvariant();
        }
    }
}

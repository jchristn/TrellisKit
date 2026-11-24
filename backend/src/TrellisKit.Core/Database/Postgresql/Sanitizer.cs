namespace TrellisKit.Core.Database.Postgresql
{
    using System;
    using System.Globalization;

    /// <summary>
    /// PostgreSQL literal formatting for handwritten SQL. Assumes standard_conforming_strings = on (the default).
    /// </summary>
    internal static class Sanitizer
    {
        internal static string Str(string? value)
        {
            if (value == null) return "NULL";
            return "'" + value.Replace("\0", String.Empty).Replace("'", "''") + "'";
        }

        internal static string Ts(DateTime value)
        {
            return "'" + value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.ffffffZ", CultureInfo.InvariantCulture) + "'::timestamptz";
        }

        internal static string Ts(DateTime? value)
        {
            if (value == null) return "NULL";
            return Ts(value.Value);
        }

        internal static string Bool(bool value)
        {
            return value ? "TRUE" : "FALSE";
        }

        internal static string Num(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        internal static string Num(long value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        internal static string Num(double value)
        {
            if (Double.IsNaN(value) || Double.IsInfinity(value)) return "0";
            return value.ToString("R", CultureInfo.InvariantCulture);
        }
    }
}

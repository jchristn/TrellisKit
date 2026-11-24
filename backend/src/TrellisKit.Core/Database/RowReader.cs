namespace TrellisKit.Core.Database
{
    using System;
    using System.Data;
    using System.Globalization;

    /// <summary>
    /// Typed accessors for <see cref="DataRow"/> values. Each accessor accepts native types and their text forms.
    /// Missing columns and DBNull map to defaults.
    /// </summary>
    public static class RowReader
    {
        #region Public-Methods

        /// <summary>
        /// Read a non-null string. Returns empty when the value is null or the column is missing.
        /// </summary>
        /// <param name="row">Row.</param>
        /// <param name="column">Column name.</param>
        /// <returns>Value.</returns>
        public static string GetString(DataRow row, string column)
        {
            return GetNullableString(row, column) ?? String.Empty;
        }

        /// <summary>
        /// Read a nullable string.
        /// </summary>
        /// <param name="row">Row.</param>
        /// <param name="column">Column name.</param>
        /// <returns>Value, or null.</returns>
        public static string? GetNullableString(DataRow row, string column)
        {
            object? value = Raw(row, column);
            if (value == null) return null;
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Read a 32-bit integer. Returns 0 when null.
        /// </summary>
        /// <param name="row">Row.</param>
        /// <param name="column">Column name.</param>
        /// <returns>Value.</returns>
        public static int GetInt(DataRow row, string column)
        {
            object? value = Raw(row, column);
            if (value == null) return 0;
            return Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Read a nullable 32-bit integer.
        /// </summary>
        /// <param name="row">Row.</param>
        /// <param name="column">Column name.</param>
        /// <returns>Value, or null.</returns>
        public static int? GetNullableInt(DataRow row, string column)
        {
            object? value = Raw(row, column);
            if (value == null) return null;
            return Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Read an enum stored as its member name. Returns the fallback when null or unrecognized.
        /// </summary>
        /// <typeparam name="T">Enum type.</typeparam>
        /// <param name="row">Row.</param>
        /// <param name="column">Column name.</param>
        /// <param name="fallback">Value used when the column is null or unrecognized.</param>
        /// <returns>Value.</returns>
        public static T GetEnum<T>(DataRow row, string column, T fallback) where T : struct, Enum
        {
            return GetNullableEnum<T>(row, column) ?? fallback;
        }

        /// <summary>
        /// Read a nullable enum stored as its member name. Returns null when null or unrecognized.
        /// </summary>
        /// <typeparam name="T">Enum type.</typeparam>
        /// <param name="row">Row.</param>
        /// <param name="column">Column name.</param>
        /// <returns>Value, or null.</returns>
        public static T? GetNullableEnum<T>(DataRow row, string column) where T : struct, Enum
        {
            string? text = GetNullableString(row, column);
            if (String.IsNullOrEmpty(text)) return null;
            if (Enum.TryParse<T>(text, true, out T value) && Enum.IsDefined(value)) return value;
            return null;
        }

        /// <summary>
        /// Read a 64-bit integer. Returns 0 when null.
        /// </summary>
        /// <param name="row">Row.</param>
        /// <param name="column">Column name.</param>
        /// <returns>Value.</returns>
        public static long GetLong(DataRow row, string column)
        {
            object? value = Raw(row, column);
            if (value == null) return 0;
            return Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Read a nullable 64-bit integer.
        /// </summary>
        /// <param name="row">Row.</param>
        /// <param name="column">Column name.</param>
        /// <returns>Value, or null.</returns>
        public static long? GetNullableLong(DataRow row, string column)
        {
            object? value = Raw(row, column);
            if (value == null) return null;
            return Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Read a double. Returns 0 when null.
        /// </summary>
        /// <param name="row">Row.</param>
        /// <param name="column">Column name.</param>
        /// <returns>Value.</returns>
        public static double GetDouble(DataRow row, string column)
        {
            object? value = Raw(row, column);
            if (value == null) return 0;
            return Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Read a boolean stored as a native boolean, an integer (0 or 1), or text ("true", "1").
        /// Returns false when null.
        /// </summary>
        /// <param name="row">Row.</param>
        /// <param name="column">Column name.</param>
        /// <returns>Value.</returns>
        public static bool GetBool(DataRow row, string column)
        {
            object? value = Raw(row, column);
            if (value == null) return false;
            if (value is bool b) return b;
            if (value is string s) return s == "1" || String.Equals(s, "true", StringComparison.OrdinalIgnoreCase);
            return Convert.ToInt64(value, CultureInfo.InvariantCulture) != 0;
        }

        /// <summary>
        /// Read a UTC timestamp stored as a native timestamp or ISO 8601 text. Returns DateTime.MinValue when null.
        /// </summary>
        /// <param name="row">Row.</param>
        /// <param name="column">Column name.</param>
        /// <returns>Value with Kind = Utc.</returns>
        public static DateTime GetDateTime(DataRow row, string column)
        {
            return GetNullableDateTime(row, column) ?? DateTime.MinValue;
        }

        /// <summary>
        /// Read a nullable UTC timestamp stored as a native timestamp or ISO 8601 text.
        /// </summary>
        /// <param name="row">Row.</param>
        /// <param name="column">Column name.</param>
        /// <returns>Value with Kind = Utc, or null.</returns>
        /// <exception cref="FormatException">The stored text is not a valid timestamp.</exception>
        public static DateTime? GetNullableDateTime(DataRow row, string column)
        {
            object? value = Raw(row, column);
            if (value == null) return null;
            if (value is DateTime dt)
            {
                // All timestamps are stored as UTC. DataTable may surface them as Unspecified, which must not be
                // reinterpreted as local time.
                if (dt.Kind == DateTimeKind.Utc) return dt;
                if (dt.Kind == DateTimeKind.Local) return dt.ToUniversalTime();
                return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
            }
            if (value is DateTimeOffset dto) return dto.UtcDateTime;
            string text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? String.Empty;
            return DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
        }

        #endregion

        #region Private-Methods

        private static object? Raw(DataRow row, string column)
        {
            if (row == null || String.IsNullOrEmpty(column)) return null;
            if (!row.Table.Columns.Contains(column)) return null;
            object value = row[column];
            if (value == null || value == DBNull.Value) return null;
            return value;
        }

        #endregion
    }
}

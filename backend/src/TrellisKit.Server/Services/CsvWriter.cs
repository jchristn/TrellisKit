namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text;

    /// <summary>
    /// Minimal RFC 4180 CSV builder. Values that a spreadsheet would treat as formulas are prefixed with an apostrophe.
    /// Thread safety: instances are not thread-safe.
    /// </summary>
    public class CsvWriter
    {
        #region Private-Members

        private readonly StringBuilder _Builder = new StringBuilder();

        #endregion

        #region Public-Methods

        /// <summary>
        /// Append a row.
        /// </summary>
        /// <param name="values">Values; null becomes an empty field.</param>
        /// <returns>This writer.</returns>
        public CsvWriter Row(IEnumerable<object?> values)
        {
            _Builder.Append(String.Join(",", values.Select(Field))).Append("\r\n");
            return this;
        }

        /// <summary>
        /// Append a row.
        /// </summary>
        /// <param name="values">Values.</param>
        /// <returns>This writer.</returns>
        public CsvWriter Row(params object?[] values)
        {
            return Row((IEnumerable<object?>)values);
        }

        /// <summary>
        /// The CSV text.
        /// </summary>
        /// <returns>Text.</returns>
        public override string ToString()
        {
            return _Builder.ToString();
        }

        #endregion

        #region Private-Methods

        private static string Field(object? value)
        {
            string text;
            if (value == null) text = String.Empty;
            else if (value is DateTime dt) text = dt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
            else if (value is bool b) text = b ? "true" : "false";
            else text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? String.Empty;

            if (text.Length > 0 && (text[0] == '=' || text[0] == '+' || text[0] == '@' || text[0] == '\t' || text[0] == '\r' || (text[0] == '-' && text.Length > 1 && !Char.IsDigit(text[1]))))
                text = "'" + text;
            if (text.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0) text = "\"" + text.Replace("\"", "\"\"") + "\"";
            return text;
        }

        #endregion
    }
}

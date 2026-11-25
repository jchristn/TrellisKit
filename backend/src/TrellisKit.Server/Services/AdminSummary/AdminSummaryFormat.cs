namespace TrellisKit.Server.Services.AdminSummary
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Plain-language formatting of administrator summary values by metric unit. Thread safety: stateless.
    /// </summary>
    public static class AdminSummaryFormat
    {
        #region Public-Methods

        /// <summary>
        /// Format one value in its unit (count, tokens, percent, bytes, ms, usdMicros; anything else as a number).
        /// </summary>
        /// <param name="value">Value.</param>
        /// <param name="unit">Unit.</param>
        /// <returns>Text.</returns>
        public static string Value(double value, string? unit)
        {
            CultureInfo c = CultureInfo.InvariantCulture;
            switch (unit)
            {
                case "percent":
                    return value.ToString("0.#", c) + "%";
                case "bytes":
                    return Bytes(value);
                case "ms":
                    return value < 1000 ? value.ToString("0", c) + " ms" : (value / 1000).ToString("0.0#", c) + " s";
                case "usdMicros":
                    double usd = value / 1000000;
                    return "$" + usd.ToString(Math.Abs(usd) < 1 && usd != 0 ? "0.0000" : "#,0.00", c);
                default:
                    return Math.Abs(value - Math.Round(value)) < 0.0001 ? value.ToString("#,0", c) : value.ToString("#,0.0#", c);
            }
        }

        /// <summary>
        /// The email line for a row: the value, then the previous day with the change, then the seven-day average.
        /// </summary>
        /// <param name="row">Row (Value, PreviousValue, SevenDayAverage, Change, Complete, Suppressed, Unit).</param>
        /// <returns>Text.</returns>
        /// <exception cref="ArgumentNullException">row is null.</exception>
        public static string Display(AdminSummaryRow row)
        {
            ArgumentNullException.ThrowIfNull(row);
            if (row.Suppressed) return "Withheld (group too small)";
            if (row.Value == null) return "No data";
            List<string> parts = new List<string>();
            if (row.PreviousValue != null)
            {
                string previous = "day before " + Value(row.PreviousValue.Value, row.Unit);
                if (row.Change != null) previous += ", " + (row.Change.Value >= 0 ? "+" : "") + (row.Change.Value * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
                parts.Add(previous);
            }
            if (row.SevenDayAverage != null) parts.Add("7-day average " + Value(row.SevenDayAverage.Value, row.Unit));
            if (!row.Complete) parts.Add("partial day");
            string text = Value(row.Value.Value, row.Unit);
            return parts.Count == 0 ? text : text + " (" + String.Join("; ", parts) + ")";
        }

        #endregion

        #region Private-Methods

        private static string Bytes(double value)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB", "PB" };
            double v = Math.Abs(value);
            int i = 0;
            while (v >= 1024 && i < units.Length - 1)
            {
                v /= 1024;
                i++;
            }
            return (value < 0 ? "-" : "") + v.ToString(i == 0 ? "0" : "0.#", CultureInfo.InvariantCulture) + " " + units[i];
        }

        #endregion
    }
}

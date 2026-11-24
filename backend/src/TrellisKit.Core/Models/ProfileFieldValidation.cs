namespace TrellisKit.Core.Models
{
    using System;

    /// <summary>
    /// Validation rules of a profile field (docs/MULTIPROPERTY_CONTRACT.md section 9.6). Unused rules are null or false.
    /// </summary>
    public class ProfileFieldValidation
    {
        #region Public-Members

        /// <summary>
        /// Required (allowed only with collectAt SignUp or Profile).
        /// </summary>
        public bool Required { get; set; } = false;

        /// <summary>
        /// Text types: minimum length.
        /// </summary>
        public int? MinLength { get; set; } = null;

        /// <summary>
        /// Text types: maximum length (Text default 200, cap 1000; LongText default 2000, cap 10000).
        /// </summary>
        public int? MaxLength { get; set; } = null;

        /// <summary>
        /// Number: minimum.
        /// </summary>
        public double? MinValue { get; set; } = null;

        /// <summary>
        /// Number: maximum.
        /// </summary>
        public double? MaxValue { get; set; } = null;

        /// <summary>
        /// Number: whole numbers only.
        /// </summary>
        public bool IntegerOnly { get; set; } = false;

        /// <summary>
        /// Text: .NET regular expression (at most 200 characters, 100 ms timeout).
        /// </summary>
        public string? Pattern { get; set; } = null;

        /// <summary>
        /// Text: message shown when the pattern does not match.
        /// </summary>
        public string? PatternMessage { get; set; } = null;

        /// <summary>
        /// MultiSelect and History: minimum entries.
        /// </summary>
        public int? MinEntries { get; set; } = null;

        /// <summary>
        /// MultiSelect and History: maximum entries (History 1 to 50, default 12).
        /// </summary>
        public int? MaxEntries { get; set; } = null;

        /// <summary>
        /// Date: earliest date (YYYY-MM-DD).
        /// </summary>
        public string? MinDate { get; set; } = null;

        /// <summary>
        /// Date: latest date (YYYY-MM-DD).
        /// </summary>
        public string? MaxDate { get; set; } = null;

        /// <summary>
        /// Date: the date may not be in the future (UTC).
        /// </summary>
        public bool NotInFuture { get; set; } = false;

        #endregion
    }
}

namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A stored profile value of one member and field (profilevalues plus, for MultiSelect, profilevalueoptions).
    /// </summary>
    public class ProfileValue
    {
        #region Public-Members

        /// <summary>
        /// Member.
        /// </summary>
        public string UserId { get; set; } = String.Empty;

        /// <summary>
        /// Field.
        /// </summary>
        public string FieldId { get; set; } = String.Empty;

        /// <summary>
        /// Text value, option key, rank key, or country code.
        /// </summary>
        public string? ValueText { get; set; } = null;

        /// <summary>
        /// Lowercased, trimmed, accent-folded text (Text and Email).
        /// </summary>
        public string? ValueNormalized { get; set; } = null;

        /// <summary>
        /// Number value.
        /// </summary>
        public double? ValueNumber { get; set; } = null;

        /// <summary>
        /// Date value (YYYY-MM-DD).
        /// </summary>
        public string? ValueDate { get; set; } = null;

        /// <summary>
        /// Boolean value.
        /// </summary>
        public bool? ValueBool { get; set; } = null;

        /// <summary>
        /// Rank level.
        /// </summary>
        public int? ValueLevel { get; set; } = null;

        /// <summary>
        /// Free text of a freeText option.
        /// </summary>
        public string? OtherText { get; set; } = null;

        /// <summary>
        /// MultiSelect keys in order.
        /// </summary>
        public List<string> OptionKeys
        {
            get { return _OptionKeys; }
            set { _OptionKeys = value ?? new List<string>(); }
        }

        /// <summary>
        /// Who wrote the value.
        /// </summary>
        public string? UpdatedByUserId { get; set; } = null;

        /// <summary>
        /// When the value was written (UTC).
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private List<string> _OptionKeys = new List<string>();

        #endregion
    }
}

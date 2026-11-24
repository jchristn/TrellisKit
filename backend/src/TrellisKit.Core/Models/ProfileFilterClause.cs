namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A compiled member search profile filter (docs/MULTIPROPERTY_CONTRACT.md section 9.12). Values are validated by the
    /// service; the data layer builds a property-scoped EXISTS subquery from it.
    /// </summary>
    public class ProfileFilterClause
    {
        #region Public-Members

        /// <summary>
        /// Field.
        /// </summary>
        public string FieldId { get; set; } = String.Empty;

        /// <summary>
        /// Field type (selects the table and column).
        /// </summary>
        public ProfileFieldTypeEnum FieldType { get; set; } = ProfileFieldTypeEnum.Text;

        /// <summary>
        /// Operator.
        /// </summary>
        public ProfileFilterOperatorEnum Operator { get; set; } = ProfileFilterOperatorEnum.HasValue;

        /// <summary>
        /// Text, normalized text, key, code, or date values (Rank order operators: the option keys at or above or below).
        /// </summary>
        public List<string> Values
        {
            get { return _Values; }
            set { _Values = value ?? new List<string>(); }
        }

        /// <summary>
        /// Number bound or value.
        /// </summary>
        public double? Number { get; set; } = null;

        /// <summary>
        /// Boolean value (eq on Boolean, hasValue).
        /// </summary>
        public bool? Flag { get; set; } = null;

        #endregion

        #region Private-Members

        private List<string> _Values = new List<string>();

        #endregion
    }
}

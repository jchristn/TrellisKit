namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A sub-field of a History profile field (docs/MULTIPROPERTY_CONTRACT.md section 9.2).
    /// </summary>
    public class ProfileSubField
    {
        #region Public-Members

        /// <summary>
        /// Identifier (pfs_ prefix); empty on input for a new sub-field.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Key (the field key pattern), unique within the field.
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// Label, 1 to 60 characters.
        /// </summary>
        public string Label { get; set; } = String.Empty;

        /// <summary>
        /// Type.
        /// </summary>
        public ProfileSubFieldTypeEnum Type { get; set; } = ProfileSubFieldTypeEnum.Text;

        /// <summary>
        /// Whether every entry must have a value.
        /// </summary>
        public bool Required { get; set; } = false;

        /// <summary>
        /// Text only: maximum length, 1 to 500 (default 100).
        /// </summary>
        public int? MaxLength { get; set; } = null;

        /// <summary>
        /// Number only: minimum.
        /// </summary>
        public double? MinValue { get; set; } = null;

        /// <summary>
        /// Number only: maximum.
        /// </summary>
        public double? MaxValue { get; set; } = null;

        /// <summary>
        /// Date only: the date may not be in the future (UTC).
        /// </summary>
        public bool NotInFuture { get; set; } = false;

        /// <summary>
        /// SingleSelect only: options (1 to 50).
        /// </summary>
        public List<ProfileFieldOption> Options
        {
            get { return _Options; }
            set { _Options = value ?? new List<ProfileFieldOption>(); }
        }

        /// <summary>
        /// RankValue only: key of a Rank field of the property.
        /// </summary>
        public string? RankFieldKey { get; set; } = null;

        /// <summary>
        /// RankValue only: whether the value carries a level.
        /// </summary>
        public bool WithLevel { get; set; } = false;

        /// <summary>
        /// Position within the field.
        /// </summary>
        public int Position { get; set; } = 0;

        #endregion

        #region Private-Members

        private List<ProfileFieldOption> _Options = new List<ProfileFieldOption>();

        #endregion
    }
}

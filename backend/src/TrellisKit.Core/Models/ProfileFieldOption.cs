namespace TrellisKit.Core.Models
{
    using System;
    using System.Text.Json.Serialization;

    /// <summary>
    /// An option of a select or rank profile field, or of a SingleSelect sub-field.
    /// </summary>
    public class ProfileFieldOption
    {
        #region Public-Members

        /// <summary>
        /// Identifier (pfo_ prefix); empty on input for a new option.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Key, ^[A-Za-z0-9_-]{1,64}$, unique within the field case-insensitively.
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// Label, 1 to 60 characters.
        /// </summary>
        public string Label { get; set; } = String.Empty;

        /// <summary>
        /// Color (#RGB, #RRGGBB, or #RRGGBBAA); required for Rank options.
        /// </summary>
        public string? Color { get; set; } = null;

        /// <summary>
        /// Rank only: maximum level of this option (0 to 20), overriding rank.maxLevel.
        /// </summary>
        public int? MaxLevel { get; set; } = null;

        /// <summary>
        /// Select types: choosing this option enables a free text value up to 100 characters.
        /// </summary>
        public bool FreeText { get; set; } = false;

        /// <summary>
        /// Hidden from pickers; existing values stay valid and are displayed.
        /// </summary>
        public bool Retired { get; set; } = false;

        /// <summary>
        /// Sub-field the option belongs to (SingleSelect sub-fields), or null. Not serialized.
        /// </summary>
        [JsonIgnore]
        public string? SubFieldId { get; set; } = null;

        #endregion
    }
}

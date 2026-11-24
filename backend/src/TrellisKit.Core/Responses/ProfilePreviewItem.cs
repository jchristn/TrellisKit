namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;

    /// <summary>
    /// One rendered field of a profile field preview (docs/MULTIPROPERTY_CONTRACT.md section 9.11).
    /// </summary>
    public class ProfilePreviewItem
    {
        #region Public-Members

        /// <summary>
        /// Field key.
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// Built-in field.
        /// </summary>
        public bool BuiltIn { get; set; } = false;

        /// <summary>
        /// Label.
        /// </summary>
        public string Label { get; set; } = String.Empty;

        /// <summary>
        /// Type.
        /// </summary>
        public ProfileFieldTypeEnum Type { get; set; } = ProfileFieldTypeEnum.Text;

        /// <summary>
        /// Required.
        /// </summary>
        public bool Required { get; set; } = false;

        /// <summary>
        /// Help text.
        /// </summary>
        public string HelpText { get; set; } = String.Empty;

        /// <summary>
        /// Placeholder.
        /// </summary>
        public string Placeholder { get; set; } = String.Empty;

        /// <summary>
        /// Input control (input, textarea, number, date, switch, url, email, phone, countryPicker, radio, dropdown, checkboxes, rankPicker, historyEditor, image, readOnly).
        /// </summary>
        public string Control { get; set; } = String.Empty;

        /// <summary>
        /// Display form (text, multilineText, number, date, yesNo, link, mailto, tel, countryName, optionLabel, optionLabels, rank, historyList, image).
        /// </summary>
        public string Display { get; set; } = String.Empty;

        /// <summary>
        /// Sample value, or null.
        /// </summary>
        public object? Value { get; set; } = null;

        /// <summary>
        /// Display text of the sample value (rank: the option label, then the level and level label when the level is above 0), or null.
        /// </summary>
        public string? FormattedValue { get; set; } = null;

        /// <summary>
        /// Selectable options (not retired), or null.
        /// </summary>
        public List<ProfileFieldOption>? Options { get; set; } = null;

        #endregion
    }
}

namespace TrellisKit.Core.PropertyConfig
{
    using System;

    /// <summary>
    /// One goal a member can choose.
    /// </summary>
    public class PropertyGoalValue
    {
        #region Public-Members

        /// <summary>
        /// Stable key stored in the database and sent over the API.
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// Display label.
        /// </summary>
        public string Label { get; set; } = String.Empty;

        /// <summary>
        /// Whether choosing this goal enables the goalsOther free text.
        /// </summary>
        public bool FreeText { get; set; } = false;

        #endregion
    }
}

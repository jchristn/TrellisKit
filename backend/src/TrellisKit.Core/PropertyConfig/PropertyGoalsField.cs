namespace TrellisKit.Core.PropertyConfig
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// The goals profile field and its allowed values.
    /// </summary>
    public class PropertyGoalsField
    {
        #region Public-Members

        /// <summary>
        /// Whether members can choose goals.
        /// </summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// Display label.
        /// </summary>
        public string Label { get; set; } = String.Empty;

        /// <summary>
        /// Allowed goals in display order. Keys are unique.
        /// </summary>
        public List<PropertyGoalValue> Values { get; set; } = new List<PropertyGoalValue>();

        #endregion
    }
}

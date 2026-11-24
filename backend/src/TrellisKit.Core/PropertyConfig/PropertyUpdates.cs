namespace TrellisKit.Core.PropertyConfig
{
    using System;

    /// <summary>
    /// Live update client options in property.json (docs/LIVE_UPDATES_CONTRACT.md section 2).
    /// </summary>
    public class PropertyUpdates
    {
        #region Public-Members

        /// <summary>
        /// Minimum minutes between foreground update checks, or null to use the server setting updates.checkIntervalMinutes.
        /// Values are clamped to 1 to 1440.
        /// </summary>
        public int? CheckIntervalMinutes
        {
            get { return _CheckIntervalMinutes; }
            set { _CheckIntervalMinutes = value == null ? null : Math.Clamp(value.Value, 1, 1440); }
        }

        #endregion

        #region Private-Members

        private int? _CheckIntervalMinutes = null;

        #endregion
    }
}

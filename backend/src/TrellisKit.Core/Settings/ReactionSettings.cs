namespace TrellisKit.Core.Settings
{
    using System;

    /// <summary>
    /// Reaction settings (contract section 6). The reaction set itself comes from the property.
    /// </summary>
    public class ReactionSettings
    {
        #region Public-Members

        /// <summary>
        /// Reaction adds and removes a member may make each minute. Default 120. Minimum 1, maximum 10000.
        /// </summary>
        public int PerMinute
        {
            get { return _PerMinute; }
            set { _PerMinute = Math.Clamp(value, 1, 10000); }
        }

        #endregion

        #region Private-Members

        private int _PerMinute = 120;

        #endregion
    }
}

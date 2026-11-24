namespace TrellisKit.Core.Models
{
    using System;

    /// <summary>
    /// A member tag segment filter: the tag key is set, optionally with one value.
    /// </summary>
    public class BroadcastTagFilter
    {
        #region Public-Members

        /// <summary>
        /// Normalized tag key.
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// Required value, or null for any value.
        /// </summary>
        public string? Value { get; set; } = null;

        #endregion
    }
}

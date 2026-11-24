namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Tickets of one type in the metrics window.
    /// </summary>
    public class SupportTypeCount
    {
        #region Public-Members

        /// <summary>
        /// Type key.
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// Type label (the key when the type no longer exists).
        /// </summary>
        public string Label { get; set; } = String.Empty;

        /// <summary>
        /// Tickets created.
        /// </summary>
        public long Count { get; set; } = 0;

        #endregion
    }
}

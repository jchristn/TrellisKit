namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// An extra header without its value.
    /// </summary>
    public class EndpointHeaderView
    {
        #region Public-Members

        /// <summary>
        /// Header name.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Whether a value is stored.
        /// </summary>
        public bool HasValue { get; set; } = false;

        #endregion
    }
}

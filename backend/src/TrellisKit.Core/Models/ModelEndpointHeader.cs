namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// An extra header of a model endpoint; the value is held encrypted.
    /// </summary>
    public class ModelEndpointHeader
    {
        #region Public-Members

        /// <summary>
        /// Header name as entered.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Encrypted value (stored form), or null when none.
        /// </summary>
        public string? ValueSecret { get; set; } = null;

        /// <summary>
        /// Order.
        /// </summary>
        public int Position { get; set; } = 0;

        #endregion
    }
}

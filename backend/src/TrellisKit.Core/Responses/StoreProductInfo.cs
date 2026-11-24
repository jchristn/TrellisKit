namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// In-app purchase availability.
    /// </summary>
    public class StoreProductInfo
    {
        #region Public-Members

        /// <summary>
        /// Whether enabled.
        /// </summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// Store product identifier.
        /// </summary>
        public string ProductId { get; set; } = String.Empty;

        #endregion
    }
}

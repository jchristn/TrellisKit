namespace TrellisKit.Core.Requests
{
    using System;

    /// <summary>
    /// A place search.
    /// </summary>
    public class GeocodeRequest
    {
        #region Public-Members

        /// <summary>
        /// Place text (2 to 200).
        /// </summary>
        public string? Query { get; set; } = null;

        #endregion
    }
}

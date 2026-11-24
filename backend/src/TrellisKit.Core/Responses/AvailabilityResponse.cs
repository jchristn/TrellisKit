namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// Handle availability.
    /// </summary>
    public class AvailabilityResponse
    {
        #region Public-Members

        /// <summary>
        /// Whether the handle can be used.
        /// </summary>
        public bool Available { get; set; } = false;

        #endregion
    }
}

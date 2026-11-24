namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Body of PUT /api/v1/media/{id} and PUT /api/v1/admin/media/{id}.
    /// </summary>
    public class MediaUpdateRequest
    {
        #region Public-Members

        /// <summary>
        /// Alternative text (0 to 1000 characters); null or empty clears it.
        /// </summary>
        public string? AltText { get; set; } = null;

        /// <summary>
        /// Optional labels (replace).
        /// </summary>
        public List<string>? Labels { get; set; } = null;

        /// <summary>
        /// Optional tags (replace).
        /// </summary>
        public Dictionary<string, string>? Tags { get; set; } = null;

        #endregion
    }
}

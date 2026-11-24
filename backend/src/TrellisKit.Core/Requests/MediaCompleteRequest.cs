namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Body of POST /api/v1/media/{id}/complete.
    /// </summary>
    public class MediaCompleteRequest
    {
        #region Public-Members

        /// <summary>
        /// Parts (required for multipart, omitted for single; at most 1000).
        /// </summary>
        public List<MediaCompletePart>? Parts { get; set; } = null;

        #endregion
    }
}

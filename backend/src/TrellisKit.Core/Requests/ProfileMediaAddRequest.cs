namespace TrellisKit.Core.Requests
{
    using System.Collections.Generic;

    /// <summary>
    /// Body of POST /api/v1/users/{id}/media (REST_API.md, Profile multimedia).
    /// </summary>
    public class ProfileMediaAddRequest
    {
        #region Public-Members

        /// <summary>
        /// The caller's unattached uploads, 1 to 10 (fixed bound).
        /// </summary>
        public List<string>? MediaIds { get; set; } = null;

        /// <summary>
        /// Labels applied to every new item.
        /// </summary>
        public List<string>? Labels { get; set; } = null;

        /// <summary>
        /// Tags applied to every new item.
        /// </summary>
        public Dictionary<string, string>? Tags { get; set; } = null;

        #endregion
    }
}

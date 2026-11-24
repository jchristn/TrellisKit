namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Remote asset create and metadata update body.
    /// </summary>
    public class RemoteAssetRequest
    {
        /// <summary>
        /// Key.
        /// </summary>
        public string? Key { get; set; } = null;

        /// <summary>
        /// Alternative text.
        /// </summary>
        public string? AltText { get; set; } = null;

        /// <summary>
        /// Labels.
        /// </summary>
        public List<string>? Labels { get; set; } = null;

        /// <summary>
        /// Tags.
        /// </summary>
        public Dictionary<string, string>? Tags { get; set; } = null;
    }
}

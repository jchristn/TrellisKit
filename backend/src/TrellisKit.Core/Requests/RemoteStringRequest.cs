namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// PUT /api/v1/admin/app/strings/{key} body.
    /// </summary>
    public class RemoteStringRequest
    {
        /// <summary>
        /// Override text.
        /// </summary>
        public string? Value { get; set; } = null;

        /// <summary>
        /// Editor notes.
        /// </summary>
        public string? Notes { get; set; } = null;

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

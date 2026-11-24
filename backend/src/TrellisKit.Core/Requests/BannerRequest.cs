namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Banner create and update body (absent fields keep their values on update).
    /// </summary>
    public class BannerRequest
    {
        /// <summary>
        /// Title.
        /// </summary>
        public string? Title { get; set; } = null;

        /// <summary>
        /// Markdown body.
        /// </summary>
        public string? Body { get; set; } = null;

        /// <summary>
        /// Style.
        /// </summary>
        public string? Style { get; set; } = null;

        /// <summary>
        /// Placement.
        /// </summary>
        public string? Placement { get; set; } = null;

        /// <summary>
        /// Dismissible.
        /// </summary>
        public bool? Dismissible { get; set; } = null;

        /// <summary>
        /// Priority.
        /// </summary>
        public int? Priority { get; set; } = null;

        /// <summary>
        /// Schedule start.
        /// </summary>
        public DateTime? StartsUtc { get; set; } = null;

        /// <summary>
        /// Schedule end.
        /// </summary>
        public DateTime? EndsUtc { get; set; } = null;

        /// <summary>
        /// Link, or null to remove.
        /// </summary>
        public BannerLinkRequest? Link { get; set; } = null;

        /// <summary>
        /// Remote asset id.
        /// </summary>
        public string? ImageAssetId { get; set; } = null;

        /// <summary>
        /// Client types.
        /// </summary>
        public List<string>? Clients { get; set; } = null;

        /// <summary>
        /// Targeting rules.
        /// </summary>
        public List<FlagRuleRequest>? Rules { get; set; } = null;

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

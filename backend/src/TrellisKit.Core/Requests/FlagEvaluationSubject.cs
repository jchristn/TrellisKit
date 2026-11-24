namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Caller attributes for the flag Test as preview.
    /// </summary>
    public class FlagEvaluationSubject
    {
        /// <summary>
        /// ios, android, or web.
        /// </summary>
        public string? Platform { get; set; } = null;

        /// <summary>
        /// mobile or web.
        /// </summary>
        public string? Client { get; set; } = null;

        /// <summary>
        /// App version.
        /// </summary>
        public string? AppVersion { get; set; } = null;

        /// <summary>
        /// Running update UUID or embedded.
        /// </summary>
        public string? UpdateId { get; set; } = null;

        /// <summary>
        /// Runtime version.
        /// </summary>
        public string? RuntimeVersion { get; set; } = null;

        /// <summary>
        /// Update channel.
        /// </summary>
        public string? Channel { get; set; } = null;

        /// <summary>
        /// Signed in.
        /// </summary>
        public bool SignedIn { get; set; } = false;

        /// <summary>
        /// Member id (loads roles, labels, tags, and membership).
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// Install or visitor id (rollout subject when signed out).
        /// </summary>
        public string? InstallId { get; set; } = null;

        /// <summary>
        /// Country code.
        /// </summary>
        public string? Country { get; set; } = null;
    }
}

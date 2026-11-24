namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Caller attributes for feature flag and kill switch evaluation (docs/LIVE_UPDATES_CONTRACT.md section 12.2).
    /// </summary>
    public class FlagEvaluationContext
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
        /// Whether the caller is signed in.
        /// </summary>
        public bool SignedIn { get; set; } = false;

        /// <summary>
        /// Active, Inactive, or None (signed in).
        /// </summary>
        public string? Membership { get; set; } = null;

        /// <summary>
        /// Roles (SystemAdmin, Moderator, FeedModerator, User, ...).
        /// </summary>
        public List<string> Roles { get; set; } = new List<string>();

        /// <summary>
        /// The caller's user labels, staff labels included.
        /// </summary>
        public List<string> UserLabels { get; set; } = new List<string>();

        /// <summary>
        /// The caller's user tags.
        /// </summary>
        public Dictionary<string, string> UserTags { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>
        /// Member id, or null.
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// Rollout subject: member id when signed in, else the install or visitor id.
        /// </summary>
        public string? SubjectId { get; set; } = null;

        /// <summary>
        /// Country code, or null.
        /// </summary>
        public string? Country { get; set; } = null;

        /// <summary>
        /// Whether the caller is a SystemAdmin (kill switch exemption).
        /// </summary>
        public bool IsSystemAdmin { get; set; } = false;

        /// <summary>
        /// Evaluation time.
        /// </summary>
        public DateTime NowUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// The signed-in caller's values of filterable profile fields, by field key: option keys for select fields, else the
        /// value as text. Empty for anonymous callers.
        /// </summary>
        public Dictionary<string, List<string>> ProfileFields { get; set; } = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
    }
}

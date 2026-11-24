namespace TrellisKit.Core.Models
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// The administrator's settings of one CLI agent harness runtime (docs/HARNESSES.md, "Harness profiles"): default
    /// model, permission mode, limits, base URL, and credential. One per runtime and property. The credential is held
    /// in its stored (encrypted) form only.
    /// </summary>
    public class AiHarnessProfile
    {
        #region Public-Members

        /// <summary>
        /// Harness runtime (never ApiEndpoint).
        /// </summary>
        public AiExecutorRuntimeEnum Runtime { get; set; } = AiExecutorRuntimeEnum.ClaudeCode;

        /// <summary>
        /// Model used when a role executor names none, in the harness's own naming, or null for the harness default.
        /// </summary>
        public string? Model { get; set; } = null;

        /// <summary>
        /// Permission mode every call of this runtime uses (Restricted or ReadOnly), or null for the mode each job asks
        /// for.
        /// </summary>
        public string? PermissionMode { get; set; } = null;

        /// <summary>
        /// Longest one call runs, in seconds (30 to 3600), or null for jobs.harnessTimeoutSeconds.
        /// </summary>
        public int? TimeoutSeconds { get; set; } = null;

        /// <summary>
        /// Most job tool calls per executor attempt (1 to 500), or null for jobs.harnessMaxToolCalls.
        /// </summary>
        public int? MaxToolCalls { get; set; } = null;

        /// <summary>
        /// Whether this runtime may use web search and fetch when a job asks for web research. Default true.
        /// </summary>
        public bool AllowWebResearch { get; set; } = true;

        /// <summary>
        /// Base URL of the vendor API or a gateway, or null for the vendor default.
        /// </summary>
        public string? BaseUrl { get; set; } = null;

        /// <summary>
        /// How the harness authenticates. Default Environment.
        /// </summary>
        public HarnessCredentialTypeEnum CredentialType { get; set; } = HarnessCredentialTypeEnum.Environment;

        /// <summary>
        /// Encrypted credential (stored form), or null when none.
        /// </summary>
        public string? CredentialSecret { get; set; } = null;

        /// <summary>
        /// Who saved the profile last, or null.
        /// </summary>
        public string? UpdatedByUserId { get; set; } = null;

        /// <summary>
        /// Created time.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Updated time.
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}

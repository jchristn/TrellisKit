namespace TrellisKit.Core.Requests
{
    using System;

    /// <summary>
    /// Body of PUT /api/v1/admin/ai/harnesses/{runtime} (docs/HARNESSES.md, "Harness profiles"). The profile is
    /// replaced: an omitted or null field returns to its default. The credential is write-only: omit it to keep the
    /// stored one, send an empty string to clear it, or send a value to replace it.
    /// </summary>
    public class AiHarnessProfileRequest
    {
        #region Public-Members

        /// <summary>
        /// Default model in the harness's own naming (at most 200 characters), or null for the harness default.
        /// </summary>
        public string? Model { get; set; } = null;

        /// <summary>
        /// Restricted or ReadOnly, or null for the mode each job asks for.
        /// </summary>
        public string? PermissionMode { get; set; } = null;

        /// <summary>
        /// Timeout of one call in seconds (30 to 3600), or null for jobs.harnessTimeoutSeconds.
        /// </summary>
        public int? TimeoutSeconds { get; set; } = null;

        /// <summary>
        /// Job tool calls per executor attempt (1 to 500), or null for jobs.harnessMaxToolCalls.
        /// </summary>
        public int? MaxToolCalls { get; set; } = null;

        /// <summary>
        /// Whether web research is allowed when a job asks for it. Null means true.
        /// </summary>
        public bool? AllowWebResearch { get; set; } = null;

        /// <summary>
        /// Base URL of the vendor API or a gateway (absolute http or https), or null for the vendor default.
        /// </summary>
        public string? BaseUrl { get; set; } = null;

        /// <summary>
        /// Environment (default), ApiKey, AuthToken, or OAuthToken.
        /// </summary>
        public string? CredentialType { get; set; } = null;

        /// <summary>
        /// The credential (at most 16384 characters). Write-only: omitted keeps the stored value, empty clears it.
        /// </summary>
        public string? Credential { get; set; } = null;

        #endregion
    }
}

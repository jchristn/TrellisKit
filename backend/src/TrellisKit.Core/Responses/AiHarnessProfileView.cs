namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// The administrator's profile of one CLI agent harness runtime (docs/HARNESSES.md, "Harness profiles"). The
    /// credential is never returned; <see cref="HasCredential"/> says whether one is stored.
    /// </summary>
    public class AiHarnessProfileView
    {
        #region Public-Members

        /// <summary>Immutable catalog creation anchor used by enumeration date filters.</summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UnixEpoch;


        /// <summary>
        /// Harness runtime.
        /// </summary>
        public string Runtime { get; set; } = String.Empty;

        /// <summary>
        /// Whether the runtime can take job tools.
        /// </summary>
        public bool SupportsJobTools { get; set; } = false;

        /// <summary>
        /// Credential types the runtime accepts (Environment first).
        /// </summary>
        public List<string> CredentialTypes { get; set; } = new List<string>();

        /// <summary>
        /// Environment variable the base URL is passed in, or null when the runtime takes none.
        /// </summary>
        public string? BaseUrlVariable { get; set; } = null;

        /// <summary>
        /// Environment variables the stored credential is passed in (empty for the Environment type).
        /// </summary>
        public List<string> CredentialVariables { get; set; } = new List<string>();

        /// <summary>
        /// For each stored credential type the runtime accepts, the environment variables the credential is passed in.
        /// </summary>
        public Dictionary<string, List<string>> CredentialVariablesByType { get; set; } = new Dictionary<string, List<string>>();

        /// <summary>
        /// Vendor credential variables the runtime keeps from the worker's environment (the Environment type).
        /// </summary>
        public List<string> EnvironmentVariables { get; set; } = new List<string>();

        /// <summary>
        /// Default model, or null.
        /// </summary>
        public string? Model { get; set; } = null;

        /// <summary>
        /// Restricted or ReadOnly, or null for the mode each job asks for.
        /// </summary>
        public string? PermissionMode { get; set; } = null;

        /// <summary>
        /// Timeout in seconds, or null for the setting.
        /// </summary>
        public int? TimeoutSeconds { get; set; } = null;

        /// <summary>
        /// Job tool calls per executor attempt, or null for the setting.
        /// </summary>
        public int? MaxToolCalls { get; set; } = null;

        /// <summary>
        /// Whether web research is allowed when a job asks for it.
        /// </summary>
        public bool AllowWebResearch { get; set; } = true;

        /// <summary>
        /// Base URL, or null.
        /// </summary>
        public string? BaseUrl { get; set; } = null;

        /// <summary>
        /// Environment, ApiKey, AuthToken, or OAuthToken.
        /// </summary>
        public string CredentialType { get; set; } = "Environment";

        /// <summary>
        /// Whether a credential is stored.
        /// </summary>
        public bool HasCredential { get; set; } = false;

        /// <summary>
        /// jobs.harnessTimeoutSeconds, used when <see cref="TimeoutSeconds"/> is null.
        /// </summary>
        public int DefaultTimeoutSeconds { get; set; } = 600;

        /// <summary>
        /// jobs.harnessMaxToolCalls, used when <see cref="MaxToolCalls"/> is null.
        /// </summary>
        public int DefaultMaxToolCalls { get; set; } = 50;

        /// <summary>
        /// Whether this instance runs harnesses (jobs.runHarnesses).
        /// </summary>
        public bool RunsOnThisInstance { get; set; } = false;

        /// <summary>
        /// Whether a profile was saved for the runtime.
        /// </summary>
        public bool Saved { get; set; } = false;

        /// <summary>
        /// Who saved the profile last, or null.
        /// </summary>
        public string? UpdatedByUserId { get; set; } = null;

        /// <summary>
        /// When the profile was saved last, or null.
        /// </summary>
        public DateTime? UpdatedUtc { get; set; } = null;

        /// <summary>Catalog labels. Empty when none are declared.</summary>
        public List<string> Labels { get; set; } = new List<string>();

        /// <summary>Catalog tags. Empty when none are declared.</summary>
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();

        #endregion
    }
}

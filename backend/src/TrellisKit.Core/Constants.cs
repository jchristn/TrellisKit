namespace TrellisKit.Core
{
    /// <summary>
    /// Application-wide constants: engine identity and identifier prefixes.
    /// Values that an operator may want to change belong in settings, not here.
    /// </summary>
    public static class Constants
    {
        #region Product

        /// <summary>
        /// Default settings filename, resolved relative to the working directory.
        /// </summary>
        public const string DefaultSettingsFilename = "trelliskit.json";

        /// <summary>
        /// Environment variable that overrides the settings file path.
        /// </summary>
        public const string SettingsFileEnvironmentVariable = "TK_SETTINGS_FILE";

        /// <summary>
        /// Name of the application meter and activity source used for telemetry.
        /// </summary>
        public const string TelemetrySourceName = "TrellisKit";

        /// <summary>
        /// Engine name, used only where the engine itself is described. The product name comes from the property.
        /// </summary>
        public const string EngineName = "TrellisKit";

        /// <summary>
        /// Prefix of generated admin tokens.
        /// </summary>
        public const string AdminTokenPrefix = "tk_";

        /// <summary>
        /// Environment variable that selects the property directory.
        /// </summary>
        public const string PropertyDirectoryEnvironmentVariable = "TK_PROPERTY_DIR";

        /// <summary>
        /// Versioned API route prefix.
        /// </summary>
        public const string ApiPrefix = "/api/v1";

        #endregion

        #region Identifier-Prefixes

        /// <summary>
        /// Identifier prefix for request history entries.
        /// </summary>
        public const string RequestHistoryIdPrefix = "req_";

        /// <summary>
        /// Identifier prefix for users.
        /// </summary>
        public const string UserIdPrefix = "usr_";

        /// <summary>
        /// Identifier prefix for authentication sessions.
        /// </summary>
        public const string SessionIdPrefix = "ses_";

        /// <summary>
        /// Identifier prefix for credentials.
        /// </summary>
        public const string CredentialIdPrefix = "crd_";

        /// <summary>
        /// Identifier prefix for external identities.
        /// </summary>
        public const string ExternalIdentityIdPrefix = "eid_";

        /// <summary>
        /// Identifier prefix for refresh tokens.
        /// </summary>
        public const string RefreshTokenIdPrefix = "rft_";

        /// <summary>
        /// Identifier prefix for one-time tokens.
        /// </summary>
        public const string OneTimeTokenIdPrefix = "ott_";

        /// <summary>
        /// Identifier prefix for role assignments.
        /// </summary>
        public const string RoleAssignmentIdPrefix = "rla_";

        /// <summary>
        /// Identifier prefix for rank history entries.
        /// </summary>
        public const string RankHistoryEntryIdPrefix = "lin_";

        /// <summary>
        /// Identifier prefix for subscriptions.
        /// </summary>
        public const string SubscriptionIdPrefix = "sub_";

        /// <summary>
        /// Identifier prefix for subscription events.
        /// </summary>
        public const string SubscriptionEventIdPrefix = "sev_";

        /// <summary>
        /// Identifier prefix for entitlements.
        /// </summary>
        public const string EntitlementIdPrefix = "ent_";

        /// <summary>
        /// Identifier prefix for sign-up codes.
        /// </summary>
        public const string SignupCodeIdPrefix = "suc_";

        /// <summary>
        /// Identifier prefix for sign-up code redemptions.
        /// </summary>
        public const string SignupCodeRedemptionIdPrefix = "scr_";

        /// <summary>
        /// Identifier prefix for sign-up code batches.
        /// </summary>
        public const string SignupCodeBatchIdPrefix = "scb_";

        /// <summary>
        /// Identifier prefix for audit records.
        /// </summary>
        public const string AuditRecordIdPrefix = "aud_";

        /// <summary>
        /// Feed post identifier prefix.
        /// </summary>
        public const string FeedPostIdPrefix = "fpo_";

        /// <summary>
        /// Feed post revision identifier prefix.
        /// </summary>
        public const string FeedPostRevisionIdPrefix = "fpr_";

        /// <summary>
        /// Media identifier prefix.
        /// </summary>
        public const string MediaIdPrefix = "med_";

        /// <summary>
        /// Comment identifier prefix.
        /// </summary>
        public const string CommentIdPrefix = "cmt_";

        /// <summary>
        /// Comment revision identifier prefix.
        /// </summary>
        public const string CommentRevisionIdPrefix = "cmr_";

        /// <summary>
        /// Reaction identifier prefix.
        /// </summary>
        public const string ReactionIdPrefix = "rxn_";

        /// <summary>
        /// Mention identifier prefix.
        /// </summary>
        public const string MentionIdPrefix = "mtn_";

        /// <summary>
        /// Notification identifier prefix.
        /// </summary>
        public const string NotificationIdPrefix = "ntf_";

        /// <summary>
        /// Push token identifier prefix.
        /// </summary>
        public const string PushTokenIdPrefix = "ptk_";

        /// <summary>
        /// Push delivery identifier prefix.
        /// </summary>
        public const string PushDeliveryIdPrefix = "pdl_";

        /// <summary>
        /// Notification fan-out job identifier prefix.
        /// </summary>
        public const string FanoutJobIdPrefix = "fnj_";

        /// <summary>
        /// Report identifier prefix.
        /// </summary>
        public const string ReportIdPrefix = "rpt_";

        /// <summary>
        /// Moderation action identifier prefix.
        /// </summary>
        public const string ModerationActionIdPrefix = "mac_";

        /// <summary>
        /// Link preview identifier prefix.
        /// </summary>
        public const string LinkPreviewIdPrefix = "lpv_";

        /// <summary>
        /// Word filter term identifier prefix.
        /// </summary>
        public const string WordFilterTermIdPrefix = "wft_";

        /// <summary>
        /// Word filter hit identifier prefix.
        /// </summary>
        public const string WordFilterHitIdPrefix = "wfh_";

        /// <summary>
        /// Category email send identifier prefix.
        /// </summary>
        public const string EmailSendIdPrefix = "ems_";

        /// <summary>
        /// Account export job identifier prefix.
        /// </summary>
        public const string AccountExportIdPrefix = "aex_";

        /// <summary>
        /// Property domain identifier prefix.
        /// </summary>
        public const string PropertyDomainIdPrefix = "pdm_";

        /// <summary>
        /// Property app identifier prefix.
        /// </summary>
        public const string PropertyAppIdPrefix = "ppa_";

        /// <summary>
        /// Profile field identifier prefix.
        /// </summary>
        public const string ProfileFieldIdPrefix = "pfd_";

        /// <summary>
        /// Profile field option identifier prefix.
        /// </summary>
        public const string ProfileFieldOptionIdPrefix = "pfo_";

        /// <summary>
        /// Profile field sub-field identifier prefix.
        /// </summary>
        public const string ProfileSubFieldIdPrefix = "pfs_";

        /// <summary>
        /// Profile history entry identifier prefix (entries migrated from rank history keep their lin_ ids).
        /// </summary>
        public const string ProfileHistoryEntryIdPrefix = "phe_";

        /// <summary>
        /// Administrative email message record identifier prefix.
        /// </summary>
        public const string EmailMessageIdPrefix = "eml_";

        /// <summary>
        /// Suppressed email address identifier prefix.
        /// </summary>
        public const string EmailSuppressionIdPrefix = "esu_";

        /// <summary>
        /// Platform administrator identifier prefix.
        /// </summary>
        public const string PlatformAdminIdPrefix = "pad_";

        /// <summary>
        /// Platform administrator session identifier prefix.
        /// </summary>
        public const string PlatformSessionIdPrefix = "pss_";

        /// <summary>
        /// Prefix of platform administrator bearer tokens (opaque; only their SHA-256 is stored).
        /// </summary>
        public const string PlatformTokenPrefix = "tkp_";

        /// <summary>
        /// Identifier length (including prefix) for generated identifiers.
        /// </summary>
        public const int IdLength = 32;

        #endregion

        #region Http

        /// <summary>
        /// Name of the web refresh token cookie.
        /// </summary>
        public const string RefreshCookieName = "tk_refresh";

        /// <summary>
        /// Path of the web refresh token cookie.
        /// </summary>
        public const string RefreshCookiePath = ApiPrefix + "/auth";

        /// <summary>
        /// Header that identifies the client kind (web or mobile).
        /// </summary>
        public const string ClientHeader = "X-Client";

        /// <summary>
        /// Header that carries the client application version.
        /// </summary>
        public const string ClientVersionHeader = "X-Client-Version";

        /// <summary>
        /// Cookie carrying the Local mode media grant.
        /// </summary>
        public const string MediaCookieName = "tk_media";

        /// <summary>
        /// Path of the Local mode media grant cookie (the local file route).
        /// </summary>
        public const string MediaCookiePath = ApiPrefix + "/media/files";

        #endregion
    }
}

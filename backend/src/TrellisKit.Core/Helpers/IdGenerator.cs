namespace TrellisKit.Core.Helpers
{
    using System;

    /// <summary>
    /// Generates K-sortable, prefixed application identifiers using PrettyId.
    /// Thread safety: all members are safe to call concurrently.
    /// </summary>
    public static class IdGenerator
    {
        #region Private-Members

        private static readonly PrettyId.IdGenerator _Generator = new PrettyId.IdGenerator();
        private static readonly object _Lock = new object();

        #endregion

        #region Public-Methods

        /// <summary>
        /// Generate a request history entry identifier (prefix "req_").
        /// </summary>
        /// <returns>Identifier.</returns>
        public static string GenerateRequestHistoryId()
        {
            return Generate(Constants.RequestHistoryIdPrefix);
        }

        /// <summary>
        /// Generate a user identifier (prefix "usr_").
        /// </summary>
        /// <returns>Identifier.</returns>
        public static string GenerateUserId()
        {
            return Generate(Constants.UserIdPrefix);
        }

        /// <summary>
        /// Generate a session identifier (prefix "ses_").
        /// </summary>
        /// <returns>Identifier.</returns>
        public static string GenerateSessionId()
        {
            return Generate(Constants.SessionIdPrefix);
        }

        /// <summary>
        /// Generate an identifier for credentials (prefix "crd_").
        /// </summary>
        /// <returns>Identifier.</returns>
        public static string GenerateCredentialId()
        {
            return Generate(Constants.CredentialIdPrefix);
        }

        /// <summary>
        /// Generate an identifier for external identities (prefix "eid_").
        /// </summary>
        /// <returns>Identifier.</returns>
        public static string GenerateExternalIdentityId()
        {
            return Generate(Constants.ExternalIdentityIdPrefix);
        }

        /// <summary>
        /// Generate an identifier for refresh tokens (prefix "rft_").
        /// </summary>
        /// <returns>Identifier.</returns>
        public static string GenerateRefreshTokenId()
        {
            return Generate(Constants.RefreshTokenIdPrefix);
        }

        /// <summary>
        /// Generate an identifier for one-time tokens (prefix "ott_").
        /// </summary>
        /// <returns>Identifier.</returns>
        public static string GenerateOneTimeTokenId()
        {
            return Generate(Constants.OneTimeTokenIdPrefix);
        }

        /// <summary>
        /// Generate an identifier for role assignments (prefix "rla_").
        /// </summary>
        /// <returns>Identifier.</returns>
        public static string GenerateRoleAssignmentId()
        {
            return Generate(Constants.RoleAssignmentIdPrefix);
        }

        /// <summary>
        /// Generate an identifier for rank history entries (prefix "lin_").
        /// </summary>
        /// <returns>Identifier.</returns>
        public static string GenerateRankHistoryEntryId()
        {
            return Generate(Constants.RankHistoryEntryIdPrefix);
        }

        /// <summary>
        /// Generate an identifier for subscriptions (prefix "sub_").
        /// </summary>
        /// <returns>Identifier.</returns>
        public static string GenerateSubscriptionId()
        {
            return Generate(Constants.SubscriptionIdPrefix);
        }

        /// <summary>
        /// Generate an identifier for subscription events (prefix "sev_").
        /// </summary>
        /// <returns>Identifier.</returns>
        public static string GenerateSubscriptionEventId()
        {
            return Generate(Constants.SubscriptionEventIdPrefix);
        }

        /// <summary>
        /// Generate an identifier for entitlements (prefix "ent_").
        /// </summary>
        /// <returns>Identifier.</returns>
        public static string GenerateEntitlementId()
        {
            return Generate(Constants.EntitlementIdPrefix);
        }

        /// <summary>
        /// Generate an identifier for sign-up codes (prefix "suc_").
        /// </summary>
        /// <returns>Identifier.</returns>
        public static string GenerateSignupCodeId()
        {
            return Generate(Constants.SignupCodeIdPrefix);
        }

        /// <summary>
        /// Generate an identifier for sign-up code redemptions (prefix "scr_").
        /// </summary>
        /// <returns>Identifier.</returns>
        public static string GenerateSignupCodeRedemptionId()
        {
            return Generate(Constants.SignupCodeRedemptionIdPrefix);
        }

        /// <summary>
        /// Generate an identifier for sign-up code batches (prefix "scb_").
        /// </summary>
        /// <returns>Identifier.</returns>
        public static string GenerateSignupCodeBatchId()
        {
            return Generate(Constants.SignupCodeBatchIdPrefix);
        }

        /// <summary>
        /// Generate an identifier for audit records (prefix "aud_").
        /// </summary>
        /// <returns>Identifier.</returns>
        public static string GenerateAuditRecordId()
        {
            return Generate(Constants.AuditRecordIdPrefix);
        }

        /// <summary>
        /// Generate an identifier with an arbitrary prefix.
        /// </summary>
        /// <param name="prefix">Prefix, for example "usr_". Must not be null or empty.</param>
        /// <returns>Identifier of length <see cref="Constants.IdLength"/>.</returns>
        /// <exception cref="ArgumentNullException">The prefix is null or empty.</exception>
        public static string Generate(string prefix)
        {
            if (String.IsNullOrEmpty(prefix)) throw new ArgumentNullException(nameof(prefix));
            lock (_Lock)
            {
                return _Generator.GenerateKSortable(prefix, Constants.IdLength);
            }
        }

        #endregion
    }
}

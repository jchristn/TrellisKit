namespace TrellisKit.Server.Services.Email
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.RegularExpressions;
    using TrellisKit.Server.Services.Email.Templates;

    /// <summary>
    /// Every administrative email template (docs/TRELLISKIT.md section 12.3): the engine's own, fixed by the engine
    /// version, plus templates a host registers with <see cref="Register"/>. A property's JSON templates are layered on top
    /// per property by <see cref="EmailTemplateSet"/>. Optional emails governed by notification preferences (the new feed
    /// post email feed_post, the Following and Forums digest forum_digest, and the others) use this catalog, the queue,
    /// suppressions, and branding like every other template.
    /// Thread safety: registration is locked; reads return snapshots.
    /// </summary>
    public static class EmailTemplateCatalog
    {
        #region Public-Members

        /// <summary>
        /// Every template: the engine's, then registered ones.
        /// </summary>
        public static IReadOnlyList<EmailTemplate> All
        {
            get
            {
                lock (_Lock) return Engine.Concat(_Registered).ToList();
            }
        }

        /// <summary>
        /// The engine's own templates.
        /// </summary>
        public static readonly IReadOnlyList<EmailTemplate> Engine = new List<EmailTemplate>
        {
            new WelcomeEmail(),
            new VerifyEmailEmail(),
            new PasswordResetEmail(),
            new PasswordChangedEmail(),
            new SignInMethodLinkedEmail(),
            new SignInMethodRemovedEmail(),
            new EmailChangedEmail(),
            new MembershipStartedEmail(),
            new RenewalSucceededEmail(),
            new PaymentFailedEmail(),
            new SubscriptionCanceledEmail(),
            new MembershipExpiredEmail(),
            new RefundRevokedEmail(),
            new FreeMembershipGrantedEmail(),
            new FreeMembershipEndingEmail(),
            new FreeMembershipRevokedEmail(),
            new AccountRestrictedEmail(),
            new RestrictionLiftedEmail(),
            new ContentRemovedEmail(),
            new AccountDeletionRequestedEmail(),
            new AccountDeletedEmail(),
            new ExportReadyEmail(),
            new ForumDigestEmail(),
            new FeedPostEmail(),
            new EventReminderEmail(),
            new EventUpdatedEmail(),
            new EventCanceledEmail(),
            new EventWaitlistPromotedEmail(),
            new EventProposalDecisionEmail(),
            new DirectorySubmissionDecisionEmail(),
            new LibraryDigestEmail(),
            new IdeaShippedEmail(),
            new BroadcastEmail(),
            new MessagesUnreadEmail(),
            new StaffNewMemberEmail(),
            new StaffNewSubscriptionEmail(),
            new StaffCancellationEmail(),
            new StaffPaymentFailureEmail(),
            new StaffRefundEmail(),
            new StaffNewReportEmail(),
            new StaffContentAutoHiddenEmail(),
            new StaffModerationOverdueEmail(),
            new StaffContentReviewDigestEmail(),
            new StaffIntegrationFailureEmail(),
            new StaffMetricAlertEmail(),
            new StaffAdminSummaryEmail(),
            new StaffDirectorySubmissionEmail(),
            new StaffEventProposalEmail(),
            new StaffEventRsvpDigestEmail(),
            new TicketCreatedEmail(),
            new TicketReplyEmail(),
            new TicketResolvedEmail(),
            new StaffTicketOpenedEmail(),
            new StaffTicketOverdueEmail(),
            new PlatformAdminInviteEmail(),
            new TestEmail()
        };

        #endregion

        #region Private-Members

        private static readonly Regex _Key = new Regex("^[a-z][a-z0-9_]{0,62}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly object _Lock = new object();
        private static readonly List<EmailTemplate> _Registered = new List<EmailTemplate>();

        #endregion

        #region Public-Methods

        /// <summary>
        /// Find a template by key.
        /// </summary>
        /// <param name="key">Template key.</param>
        /// <returns>Template, or null.</returns>
        public static EmailTemplate? Find(string? key)
        {
            if (String.IsNullOrWhiteSpace(key)) return null;
            return All.FirstOrDefault(t => String.Equals(t.Key, key.Trim(), StringComparison.Ordinal));
        }

        /// <summary>
        /// Find a template by key, throwing when it does not exist (engine call sites).
        /// </summary>
        /// <param name="key">Template key.</param>
        /// <returns>Template.</returns>
        /// <exception cref="ArgumentException">Unknown key.</exception>
        public static EmailTemplate Get(string key)
        {
            return Find(key) ?? throw new ArgumentException("Unknown email template " + key + ".", nameof(key));
        }

        /// <summary>
        /// Whether a value is a valid template key (lowercase letter, then lowercase letters, digits, and underscores; at
        /// most 63 characters).
        /// </summary>
        /// <param name="key">Key.</param>
        /// <returns>True when valid.</returns>
        public static bool IsValidKey(string? key)
        {
            return key != null && _Key.IsMatch(key);
        }

        /// <summary>
        /// Register a host template (code that embeds the engine). Registered templates are queued, delivered, previewed,
        /// overridden, and listed in the admin portal like the engine's own; register at startup, before the server
        /// accepts requests.
        /// </summary>
        /// <param name="template">Template.</param>
        /// <exception cref="ArgumentNullException">template is null.</exception>
        /// <exception cref="ArgumentException">The key is invalid or already used.</exception>
        public static void Register(EmailTemplate template)
        {
            ArgumentNullException.ThrowIfNull(template);
            if (!IsValidKey(template.Key)) throw new ArgumentException("Template key " + template.Key + " is not valid: use a lowercase letter, then lowercase letters, digits, and underscores.", nameof(template));
            lock (_Lock)
            {
                if (Engine.Concat(_Registered).Any(t => String.Equals(t.Key, template.Key, StringComparison.Ordinal)))
                    throw new ArgumentException("Email template " + template.Key + " already exists.", nameof(template));
                _Registered.Add(template);
            }
        }

        /// <summary>
        /// Remove a registered template (engine templates cannot be removed).
        /// </summary>
        /// <param name="key">Key.</param>
        /// <returns>True when a registered template was removed.</returns>
        public static bool Unregister(string? key)
        {
            if (String.IsNullOrWhiteSpace(key)) return false;
            lock (_Lock) return _Registered.RemoveAll(t => String.Equals(t.Key, key.Trim(), StringComparison.Ordinal)) > 0;
        }

        #endregion
    }
}

namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Observability;
    using TrellisKit.Server.Services.Email;
    using TrellisKit.Server.Services.Email.Templates;

    /// <summary>
    /// Following and Forums email digests (docs/PHASE3_CONTRACT.md section 9.3 and resolved decision 9): email is an
    /// opt-in channel for both categories and is delivered as a digest, one email per member and category every
    /// forums.emailDigestIntervalHours (weekly by default), listing the member's unread notifications of that category
    /// updated since the previous digest (at most forums.emailDigestMaxItems, the rest counted). Nothing is sent when there
    /// is nothing unread. A member's first check only starts the schedule. Mail goes through the administrative email
    /// system (template forum_digest: the property's brand, overrides, the queue with retries, and suppressions) with a
    /// one-click unsubscribe for the category. One pass is bounded by its limit and runs under the forum_email_digest
    /// job lease per property.
    /// Thread safety: safe to call concurrently (the worker lease serializes passes; sends are idempotent per window).
    /// </summary>
    public class ForumDigestService
    {
        #region Public-Members

        /// <summary>
        /// The categories delivered as digests.
        /// </summary>
        public static readonly IReadOnlyList<NotificationCategoryEnum> Categories = new[] { NotificationCategoryEnum.Following, NotificationCategoryEnum.Forums };

        /// <summary>
        /// Telemetry email category.
        /// </summary>
        public const string EmailCategory = "forum_digest";

        #endregion

        #region Private-Members

        private readonly ApplicationServices _Services;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public ForumDigestService(ApplicationServices services)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The phrase a digest uses for its category.
        /// </summary>
        /// <param name="category">Following or Forums.</param>
        /// <returns>Phrase.</returns>
        public static string DigestTitle(NotificationCategoryEnum category)
        {
            return category == NotificationCategoryEnum.Following ? "threads you follow" : "your forums";
        }

        /// <summary>
        /// One digest pass: check up to <paramref name="limit"/> due member and category pairs and queue a digest for
        /// each that has unread notifications in the window.
        /// </summary>
        /// <param name="nowUtc">Now.</param>
        /// <param name="limit">Most pairs checked (1 to 1000).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Digests queued.</returns>
        public async Task<int> RunAsync(DateTime nowUtc, int limit, CancellationToken token = default)
        {
            TimeSpan interval = TimeSpan.FromHours(_Services.Settings.Forums.EmailDigestIntervalHours);
            List<EmailDigestCandidate> due = await _Services.Database.NotificationPreferences.ListDigestsDueAsync(Categories, nowUtc - interval, Math.Clamp(limit, 1, 1000), token).ConfigureAwait(false);
            int sent = 0;
            foreach (EmailDigestCandidate candidate in due)
            {
                token.ThrowIfCancellationRequested();
                DateTime? sentUtc = null;
                if (candidate.LastCheckedUtc != null && await SendAsync(candidate, candidate.LastCheckedUtc.Value, token).ConfigureAwait(false))
                {
                    sentUtc = nowUtc;
                    sent++;
                }
                await _Services.Database.NotificationPreferences.MarkDigestAsync(candidate.UserId, candidate.Category, nowUtc, sentUtc, token).ConfigureAwait(false);
            }
            return sent;
        }

        #endregion

        #region Private-Methods

        private async Task<bool> SendAsync(EmailDigestCandidate candidate, DateTime sinceUtc, CancellationToken token)
        {
            long count = await _Services.Database.Notifications.CountUnreadSinceAsync(candidate.UserId, candidate.Category, sinceUtc, token).ConfigureAwait(false);
            if (count == 0) return false;
            List<Notification> items = await _Services.Database.Notifications.ListUnreadSinceAsync(candidate.UserId, candidate.Category, sinceUtc, _Services.Settings.Forums.EmailDigestMaxItems, token).ConfigureAwait(false);
            List<string> lines = items.Select(n => n.Title.Trim() + ": " + _Services.Email.Link("/app" + n.Path)).ToList();
            if (count > items.Count) lines.Add("And " + (count - items.Count) + " more.");
            EmailRequest request = new EmailRequest
            {
                Template = ForumDigestEmail.TemplateKey,
                To = candidate.Email,
                UserId = candidate.UserId,
                IdempotencyKey = "forum_digest:" + candidate.UserId + ":" + candidate.Category + ":" + sinceUtc.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture)
            };
            string digestTitle = DigestTitle(candidate.Category);
            request.Tokens["memberName"] = candidate.DisplayName;
            request.Tokens["count"] = count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            request.Tokens["digestTitle"] = digestTitle;
            request.Tokens["items"] = String.Join("\n", lines);
            request.Tokens["link"] = _Services.Email.Link("/app/notifications");
            string unsubscribe;
            try
            {
                unsubscribe = _Services.FeedPostEmails.UnsubscribeUrl(_Services.FeedPostEmails.CreateUnsubscribeToken(candidate.UserId, candidate.Category));
                request.Headers["List-Unsubscribe"] = "<" + unsubscribe + ">";
                request.Headers["List-Unsubscribe-Post"] = "List-Unsubscribe=One-Click";
            }
            catch (IntegrationNotConfiguredException)
            {
                unsubscribe = _Services.Email.Link("/app/settings/notifications");
            }
            request.Tokens["unsubscribeLink"] = unsubscribe;
            EmailMessageRecord? queued = await _Services.Email.QueueAsync(request, token).ConfigureAwait(false);
            if (queued != null) TrellisKitTelemetry.Increment(TrellisKitTelemetry.Emails, "category", EmailCategory, "outcome", "queued");
            return queued != null;
        }

        #endregion
    }
}

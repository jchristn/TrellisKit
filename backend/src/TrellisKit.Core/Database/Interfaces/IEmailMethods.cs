namespace TrellisKit.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Administrative email data access (emailmessages, emailsuppressions, emailtemplateoverrides, emailstaffevents;
    /// docs/TRELLISKIT.md section 12). Implementations are safe to call concurrently.
    /// </summary>
    public interface IEmailMethods
    {
        /// <summary>
        /// Insert a message record unless one with the same idempotency key exists.
        /// </summary>
        /// <param name="record">Record.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when inserted, false for a duplicate event.</returns>
        /// <exception cref="ArgumentNullException">The record is null.</exception>
        Task<bool> CreateMessageAsync(EmailMessageRecord record, CancellationToken token = default);

        /// <summary>
        /// Read a message record (with its payload).
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Record, or null.</returns>
        Task<EmailMessageRecord?> ReadMessageAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Write a message's delivery state; the payload is removed when <paramref name="clearPayload"/> is set.
        /// </summary>
        /// <param name="record">Record.</param>
        /// <param name="clearPayload">Remove the encrypted payload.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task UpdateMessageAsync(EmailMessageRecord record, bool clearPayload, CancellationToken token = default);

        /// <summary>
        /// Claim queued messages due for an attempt (conditional update, so two instances never claim the same message).
        /// </summary>
        /// <param name="nowUtc">Now.</param>
        /// <param name="holder">Claim holder.</param>
        /// <param name="claimSeconds">Claim lifetime.</param>
        /// <param name="limit">Most messages (1 to 100).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Claimed records.</returns>
        Task<List<EmailMessageRecord>> ClaimDueAsync(DateTime nowUtc, string holder, int claimSeconds, int limit, CancellationToken token = default);

        /// <summary>
        /// Find a message by provider message id.
        /// </summary>
        /// <param name="providerMessageId">Provider id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Record, or null.</returns>
        Task<EmailMessageRecord?> FindByProviderIdAsync(string providerMessageId, CancellationToken token = default);

        /// <summary>
        /// The most recent message of an idempotency key prefix since a time (rate-limited alerts).
        /// </summary>
        /// <param name="keyPrefix">Key prefix.</param>
        /// <param name="sinceUtc">Earliest creation time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when one exists.</returns>
        Task<bool> ExistsSinceAsync(string keyPrefix, DateTime sinceUtc, CancellationToken token = default);

        /// <summary>
        /// One page of message records.
        /// </summary>
        /// <param name="request">Normalized request.</param>
        /// <param name="filter">Filters.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        Task<EnumerationResult<EmailMessageRecord>> EnumerateMessagesAsync(EnumerationRequest request, EmailMessageFilter filter, CancellationToken token = default);

        /// <summary>
        /// Delete records created before a time, in a bounded batch.
        /// </summary>
        /// <param name="beforeUtc">Cutoff.</param>
        /// <param name="batchSize">Most rows (1 to 10000).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows deleted.</returns>
        Task<int> PruneMessagesAsync(DateTime beforeUtc, int batchSize, CancellationToken token = default);

        /// <summary>
        /// Whether an address is suppressed.
        /// </summary>
        /// <param name="email">Address.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when suppressed.</returns>
        Task<bool> IsSuppressedAsync(string email, CancellationToken token = default);

        /// <summary>
        /// Suppress an address (no-op when already suppressed).
        /// </summary>
        /// <param name="suppression">Suppression (id assigned).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The stored suppression.</returns>
        Task<EmailSuppression> AddSuppressionAsync(EmailSuppression suppression, CancellationToken token = default);

        /// <summary>
        /// Read a suppression.
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Suppression, or null.</returns>
        Task<EmailSuppression?> ReadSuppressionAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Remove a suppression with its labels and tags.
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task DeleteSuppressionAsync(string id, CancellationToken token = default);

        /// <summary>
        /// One page of suppressions.
        /// </summary>
        /// <param name="request">Normalized request.</param>
        /// <param name="reason">Reason filter, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        Task<EnumerationResult<EmailSuppression>> EnumerateSuppressionsAsync(EnumerationRequest request, EmailSuppressionReasonEnum? reason, CancellationToken token = default);

        /// <summary>
        /// Every template override of the property (bounded by the template catalog).
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Overrides.</returns>
        Task<List<EmailTemplateOverride>> ReadOverridesAsync(CancellationToken token = default);

        /// <summary>
        /// Write or (with null) delete a template override.
        /// </summary>
        /// <param name="template">Template key.</param>
        /// <param name="value">Override, or null to delete.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task WriteOverrideAsync(string template, EmailTemplateOverride? value, CancellationToken token = default);

        /// <summary>
        /// Every staff event setting of the property (bounded by the template catalog).
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Settings.</returns>
        Task<List<EmailStaffEvent>> ReadStaffEventsAsync(CancellationToken token = default);

        /// <summary>
        /// Write a staff event setting.
        /// </summary>
        /// <param name="value">Setting.</param>
        /// <param name="userId">Who changed it.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task WriteStaffEventAsync(EmailStaffEvent value, string? userId, CancellationToken token = default);
    }
}

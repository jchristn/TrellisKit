namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Account export section "entitlements": membership entitlements (subscriptions, codes, and comps), streamed in keyset batches.
    /// </summary>
    public sealed class EntitlementsExportSection : KeysetExportSection<Entitlement>
    {
        #region Public-Members

        /// <summary>
        /// Property name in the export file.
        /// </summary>
        public override string Name
        {
            get { return "entitlements"; }
        }

        #endregion

        #region Private-Members

        private readonly DatabaseDriverBase _Database;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the section.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <exception cref="ArgumentNullException">The database is null.</exception>
        public EntitlementsExportSection(DatabaseDriverBase database)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override Task<List<Entitlement>> ReadBatchAsync(string userId, string? afterId, int limit, CancellationToken token)
        {
            return _Database.Entitlements.EnumerateByUserAfterAsync(userId, afterId, limit, token);
        }

        /// <inheritdoc />
        protected override string IdOf(Entitlement record)
        {
            return record.Id;
        }

        /// <inheritdoc />
        protected override async Task<List<object>> MapAsync(string userId, List<Entitlement> batch, CancellationToken token)
        {
            DateTime now = DateTime.UtcNow;
            Dictionary<string, Subscription> subscriptions = new Dictionary<string, Subscription>(StringComparer.Ordinal);
            foreach (string id in batch.Where(e => e.Source == TrellisKit.Core.Enums.MembershipSourceEnum.Subscription).Select(e => e.SourceId).Distinct(StringComparer.Ordinal))
            {
                Subscription? subscription = await _Database.Subscriptions.ReadAsync(id, token).ConfigureAwait(false);
                if (subscription != null) subscriptions[id] = subscription;
            }
            return batch.Select(e => (object)new MembershipHistoryItem
            {
                Source = e.Source,
                Store = subscriptions.TryGetValue(e.SourceId, out Subscription? s) ? s.Store : null,
                StartsUtc = e.StartsUtc,
                EndsUtc = e.EndsUtc,
                Status = e.StatusAt(now)
            }).ToList();
        }

        #endregion
    }
}

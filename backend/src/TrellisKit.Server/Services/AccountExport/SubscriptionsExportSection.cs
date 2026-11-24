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
    /// Account export section "subscriptions": store subscriptions, streamed in keyset batches.
    /// </summary>
    public sealed class SubscriptionsExportSection : KeysetExportSection<Subscription>
    {
        #region Public-Members

        /// <summary>
        /// Property name in the export file.
        /// </summary>
        public override string Name
        {
            get { return "subscriptions"; }
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
        public SubscriptionsExportSection(DatabaseDriverBase database)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override Task<List<Subscription>> ReadBatchAsync(string userId, string? afterId, int limit, CancellationToken token)
        {
            return _Database.Subscriptions.EnumerateByUserAfterAsync(userId, afterId, limit, token);
        }

        /// <inheritdoc />
        protected override string IdOf(Subscription record)
        {
            return record.Id;
        }

        /// <inheritdoc />
        protected override async Task<List<object>> MapAsync(string userId, List<Subscription> batch, CancellationToken token)
        {
            User? user = await _Database.Users.ReadAsync(userId, token).ConfigureAwait(false);
            return batch.Select(s => (object)MemberViewBuilder.ToItem(s, user)).ToList();
        }

        #endregion
    }
}

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
    /// Account export section "sessions": every session (active, expired, and revoked), streamed in keyset batches.
    /// </summary>
    public sealed class SessionsExportSection : KeysetExportSection<AuthSession>
    {
        #region Public-Members

        /// <summary>
        /// Property name in the export file.
        /// </summary>
        public override string Name
        {
            get { return "sessions"; }
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
        public SessionsExportSection(DatabaseDriverBase database)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override Task<List<AuthSession>> ReadBatchAsync(string userId, string? afterId, int limit, CancellationToken token)
        {
            return _Database.Sessions.EnumerateByUserAfterAsync(userId, afterId, limit, token);
        }

        /// <inheritdoc />
        protected override string IdOf(AuthSession record)
        {
            return record.Id;
        }

        /// <inheritdoc />
        protected override Task<List<object>> MapAsync(string userId, List<AuthSession> batch, CancellationToken token)
        {
            return Task.FromResult(batch.Select(s => (object)MemberViewBuilder.ToView(s)).ToList());
        }

        #endregion
    }
}

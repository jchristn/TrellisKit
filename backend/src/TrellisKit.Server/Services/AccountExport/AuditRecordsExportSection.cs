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
    /// Account export section "auditRecords": audit records where the member is the actor or the target, streamed in keyset batches.
    /// </summary>
    public sealed class AuditRecordsExportSection : KeysetExportSection<AuditRecord>
    {
        #region Public-Members

        /// <summary>
        /// Property name in the export file.
        /// </summary>
        public override string Name
        {
            get { return "auditRecords"; }
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
        public AuditRecordsExportSection(DatabaseDriverBase database)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override Task<List<AuditRecord>> ReadBatchAsync(string userId, string? afterId, int limit, CancellationToken token)
        {
            return _Database.Audit.EnumerateByUserAfterAsync(userId, afterId, limit, token);
        }

        /// <inheritdoc />
        protected override string IdOf(AuditRecord record)
        {
            return record.Id;
        }

        /// <inheritdoc />
        protected override Task<List<object>> MapAsync(string userId, List<AuditRecord> batch, CancellationToken token)
        {
            return Task.FromResult(batch.Select(a => (object)a).ToList());
        }

        #endregion
    }
}

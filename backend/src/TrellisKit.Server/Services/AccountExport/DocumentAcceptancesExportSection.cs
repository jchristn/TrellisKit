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
    /// Account export section "documentAcceptances": the member's acceptances of property documents (docs/TRELLISKIT.md
    /// section 14), streamed in keyset batches.
    /// </summary>
    public sealed class DocumentAcceptancesExportSection : KeysetExportSection<DocumentAcceptance>
    {
        #region Public-Members

        /// <summary>
        /// Property name in the export file.
        /// </summary>
        public override string Name
        {
            get { return "documentAcceptances"; }
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
        public DocumentAcceptancesExportSection(DatabaseDriverBase database)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override Task<List<DocumentAcceptance>> ReadBatchAsync(string userId, string? afterId, int limit, CancellationToken token)
        {
            return _Database.Documents.ListByUserAsync(userId, afterId, limit, token);
        }

        /// <inheritdoc />
        protected override string IdOf(DocumentAcceptance record)
        {
            return record.Id;
        }

        /// <inheritdoc />
        protected override Task<List<object>> MapAsync(string userId, List<DocumentAcceptance> batch, CancellationToken token)
        {
            List<object> rows = batch.Select(a => (object)new DocumentAcceptanceView
            {
                Id = a.Id,
                DocumentKey = a.DocumentKey,
                DocumentTitle = a.Title,
                Version = a.Version,
                Sequence = a.Sequence,
                VersionId = a.VersionId,
                UserId = a.UserId,
                Method = a.Method,
                Client = a.Client,
                AcceptedUtc = a.AcceptedUtc,
                CreatedUtc = a.CreatedUtc
            }).ToList();
            return Task.FromResult(rows);
        }

        #endregion
    }
}

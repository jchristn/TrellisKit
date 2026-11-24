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
    /// Account export section "redemptions": sign-up code redemptions, streamed in keyset batches.
    /// </summary>
    public sealed class RedemptionsExportSection : KeysetExportSection<SignupCodeRedemption>
    {
        #region Public-Members

        /// <summary>
        /// Property name in the export file.
        /// </summary>
        public override string Name
        {
            get { return "redemptions"; }
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
        public RedemptionsExportSection(DatabaseDriverBase database)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override Task<List<SignupCodeRedemption>> ReadBatchAsync(string userId, string? afterId, int limit, CancellationToken token)
        {
            return _Database.SignupCodeRedemptions.EnumerateByUserAfterAsync(userId, afterId, limit, token);
        }

        /// <inheritdoc />
        protected override string IdOf(SignupCodeRedemption record)
        {
            return record.Id;
        }

        /// <inheritdoc />
        protected override async Task<List<object>> MapAsync(string userId, List<SignupCodeRedemption> batch, CancellationToken token)
        {
            DateTime now = DateTime.UtcNow;
            User? user = await _Database.Users.ReadAsync(userId, token).ConfigureAwait(false);
            Dictionary<string, SignupCode> codes = (await _Database.SignupCodes.ReadManyAsync(batch.Select(r => r.SignupCodeId), token).ConfigureAwait(false)).ToDictionary(c => c.Id, StringComparer.Ordinal);
            return batch.Select(r => (object)MemberViewBuilder.ToView(r, user, codes.GetValueOrDefault(r.SignupCodeId), now)).ToList();
        }

        #endregion
    }
}

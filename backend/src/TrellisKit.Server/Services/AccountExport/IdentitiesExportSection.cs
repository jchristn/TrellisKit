namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;
    using TrellisKit.Server.Serialization;

    /// <summary>
    /// Account export section "identities": linked sign-in providers (bounded by the number of providers).
    /// </summary>
    public sealed class IdentitiesExportSection : IAccountExportSection
    {
        #region Public-Members

        /// <summary>
        /// Property name in the export file.
        /// </summary>
        public string Name
        {
            get { return "identities"; }
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
        public IdentitiesExportSection(DatabaseDriverBase database)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Write the identities array.
        /// </summary>
        /// <param name="writer">Writer positioned after the property name.</param>
        /// <param name="userId">Member identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">The writer is null.</exception>
        public async Task WriteAsync(Utf8JsonWriter writer, string userId, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(writer);
            List<ExternalIdentity> identities = await _Database.ExternalIdentities.EnumerateByUserAsync(userId, token).ConfigureAwait(false);
            writer.WriteStartArray();
            foreach (ExternalIdentity i in identities)
            {
                JsonSerializer.Serialize(writer, new IdentityExportItem { Provider = i.Provider, Email = i.Email, EmailVerified = i.EmailVerified, CreatedUtc = i.CreatedUtc, LastUsedUtc = i.LastUsedUtc }, ApiJson.Api);
            }
            writer.WriteEndArray();
        }

        #endregion
    }
}

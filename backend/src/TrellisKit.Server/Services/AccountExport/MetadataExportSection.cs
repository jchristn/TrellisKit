namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;
    using TrellisKit.Server.Serialization;

    /// <summary>
    /// Account export section "metadata": the public labels and tags of the member's own objects (Phase 1: the member's
    /// User record; staff: entries are never exported). Later slices add their own object types with
    /// <see cref="AddOwnedType"/>. Each entry is bounded (50 labels, 50 tags).
    /// </summary>
    public sealed class MetadataExportSection : IAccountExportSection
    {
        #region Public-Members

        /// <summary>
        /// Property name in the export file.
        /// </summary>
        public string Name
        {
            get { return "metadata"; }
        }

        #endregion

        #region Private-Members

        private readonly DatabaseDriverBase _Database;
        private readonly List<Func<string, CancellationToken, IAsyncEnumerable<MetadataTarget>>> _Owned = new List<Func<string, CancellationToken, IAsyncEnumerable<MetadataTarget>>>();
        private readonly object _Lock = new object();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the section.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <exception cref="ArgumentNullException">The database is null.</exception>
        public MetadataExportSection(DatabaseDriverBase database)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Add a source of objects the member owns (for example their feed posts), streamed in bounded batches by the
        /// caller's implementation. Call during startup.
        /// </summary>
        /// <param name="source">Function returning the member's owned objects.</param>
        /// <exception cref="ArgumentNullException">The source is null.</exception>
        public void AddOwnedType(Func<string, CancellationToken, IAsyncEnumerable<MetadataTarget>> source)
        {
            ArgumentNullException.ThrowIfNull(source);
            lock (_Lock) _Owned.Add(source);
        }

        /// <summary>
        /// Write the metadata array: { targetType, targetId, labels, tags } per owned object.
        /// </summary>
        /// <param name="writer">Writer positioned after the property name.</param>
        /// <param name="userId">Member identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">The writer is null.</exception>
        public async Task WriteAsync(Utf8JsonWriter writer, string userId, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(writer);
            writer.WriteStartArray();
            await WriteOneAsync(writer, TargetTypeEnum.User, userId, token).ConfigureAwait(false);
            List<Func<string, CancellationToken, IAsyncEnumerable<MetadataTarget>>> sources;
            lock (_Lock) sources = new List<Func<string, CancellationToken, IAsyncEnumerable<MetadataTarget>>>(_Owned);
            foreach (Func<string, CancellationToken, IAsyncEnumerable<MetadataTarget>> source in sources)
            {
                await foreach (MetadataTarget target in source(userId, token).ConfigureAwait(false))
                {
                    await WriteOneAsync(writer, target.TargetType, target.TargetId, token).ConfigureAwait(false);
                }
                await writer.FlushAsync(token).ConfigureAwait(false);
            }
            writer.WriteEndArray();
        }

        #endregion

        #region Private-Methods

        private async Task WriteOneAsync(Utf8JsonWriter writer, TargetTypeEnum type, string id, CancellationToken token)
        {
            ObjectMetadata visible = MetadataRules.ForViewer(await _Database.Metadata.ReadAsync(type, id, token).ConfigureAwait(false), false);
            JsonSerializer.Serialize(writer, new MetadataResponse { TargetType = type.ToString(), TargetId = id, Labels = visible.Labels, Tags = new Dictionary<string, string>(visible.Tags) }, ApiJson.Api);
        }

        #endregion
    }
}

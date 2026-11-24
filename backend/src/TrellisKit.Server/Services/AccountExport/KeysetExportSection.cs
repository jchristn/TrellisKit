namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Server.Serialization;

    /// <summary>
    /// Base for export sections that stream one table as a JSON array in keyset batches ordered by id.
    /// Thread safety: stateless apart from the batch size; safe to call concurrently.
    /// </summary>
    /// <typeparam name="T">Stored record type.</typeparam>
    public abstract class KeysetExportSection<T> : IAccountExportSection
    {
        #region Public-Members

        /// <summary>
        /// Property name in the export file.
        /// </summary>
        public abstract string Name { get; }

        /// <summary>
        /// Records read per batch. Default 200, minimum 1, maximum 1000.
        /// </summary>
        public int BatchSize
        {
            get { return _BatchSize; }
            set { _BatchSize = Math.Clamp(value, 1, 1000); }
        }

        #endregion

        #region Private-Members

        private int _BatchSize = 200;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Write the section as a JSON array, one batch at a time.
        /// </summary>
        /// <param name="writer">Writer positioned after the property name.</param>
        /// <param name="userId">Member identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">The writer is null or the member identifier is empty.</exception>
        public async Task WriteAsync(Utf8JsonWriter writer, string userId, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(writer);
            if (String.IsNullOrEmpty(userId)) throw new ArgumentNullException(nameof(userId));
            writer.WriteStartArray();
            string? afterId = null;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                List<T> batch = await ReadBatchAsync(userId, afterId, BatchSize, token).ConfigureAwait(false);
                if (batch.Count == 0) break;
                foreach (object item in await MapAsync(userId, batch, token).ConfigureAwait(false))
                {
                    JsonSerializer.Serialize(writer, item, item.GetType(), ApiJson.Api);
                }
                await writer.FlushAsync(token).ConfigureAwait(false);
                if (batch.Count < BatchSize) break;
                afterId = IdOf(batch[batch.Count - 1]);
            }
            writer.WriteEndArray();
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Read one batch of the member's records with ids after afterId, ordered by id.
        /// </summary>
        /// <param name="userId">Member identifier.</param>
        /// <param name="afterId">Last id of the previous batch, or null.</param>
        /// <param name="limit">Batch size.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Records.</returns>
        protected abstract Task<List<T>> ReadBatchAsync(string userId, string? afterId, int limit, CancellationToken token);

        /// <summary>
        /// Id of a record (the keyset position).
        /// </summary>
        /// <param name="record">Record.</param>
        /// <returns>Id.</returns>
        protected abstract string IdOf(T record);

        /// <summary>
        /// Map a batch to the objects written to the file.
        /// </summary>
        /// <param name="userId">Member identifier.</param>
        /// <param name="batch">Records.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Export objects.</returns>
        protected abstract Task<List<object>> MapAsync(string userId, List<T> batch, CancellationToken token);

        #endregion
    }
}

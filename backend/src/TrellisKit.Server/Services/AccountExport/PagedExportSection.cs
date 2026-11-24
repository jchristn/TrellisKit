namespace TrellisKit.Server.Services
{
    using System;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Responses;
    using TrellisKit.Server.Serialization;

    /// <summary>
    /// Export section that streams a JSON array from a paging function: each call returns one bounded batch and the
    /// position of the next one (NextAfterId: a keyset identifier or a continuation token), and the section stops when
    /// it is null. Used for the content, reaction, block, report, and media sections.
    /// Thread safety: stateless apart from the batch size; safe to call concurrently.
    /// </summary>
    /// <typeparam name="T">Exported item type.</typeparam>
    public sealed class PagedExportSection<T> : IAccountExportSection
    {
        #region Public-Members

        /// <summary>
        /// Property name in the export file.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Items requested per batch. Default 100, minimum 1, maximum 1000 (the paging function may clamp further).
        /// </summary>
        public int BatchSize
        {
            get { return _BatchSize; }
            set { _BatchSize = Math.Clamp(value, 1, 1000); }
        }

        #endregion

        #region Private-Members

        private readonly Func<string, string?, int, CancellationToken, Task<ContentExportPage<T>>> _Page;
        private int _BatchSize = 100;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the section.
        /// </summary>
        /// <param name="name">Property name (camelCase, unique).</param>
        /// <param name="page">Paging function (member, position or null, batch size, token).</param>
        /// <param name="batchSize">Items per batch.</param>
        /// <exception cref="ArgumentNullException">The name or the paging function is null.</exception>
        public PagedExportSection(string name, Func<string, string?, int, CancellationToken, Task<ContentExportPage<T>>> page, int batchSize = 100)
        {
            if (String.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));
            Name = name;
            _Page = page ?? throw new ArgumentNullException(nameof(page));
            BatchSize = batchSize;
        }

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
            string? position = null;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                ContentExportPage<T> page = await _Page(userId, position, _BatchSize, token).ConfigureAwait(false);
                foreach (T item in page.Objects)
                {
                    if (item != null) JsonSerializer.Serialize(writer, item, item.GetType(), ApiJson.Api);
                }
                await writer.FlushAsync(token).ConfigureAwait(false);
                if (String.IsNullOrEmpty(page.NextAfterId) || page.Objects.Count == 0) break;
                position = page.NextAfterId;
            }
            writer.WriteEndArray();
        }

        #endregion
    }
}

namespace TrellisKit.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Models;

    /// <summary>
    /// Link preview cache data access (linkpreviews table). Implementations are safe to call concurrently.
    /// </summary>
    public interface ILinkPreviewMethods
    {
        /// <summary>
        /// Read the current cache row for a normalized URL hash.
        /// </summary>
        /// <param name="urlHash">SHA-256 hex of the normalized URL.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Row, or null.</returns>
        Task<LinkPreview?> ReadByUrlHashAsync(string urlHash, CancellationToken token = default);

        /// <summary>
        /// Read a row by identifier (current or replaced).
        /// </summary>
        /// <param name="id">Preview identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Row, or null.</returns>
        Task<LinkPreview?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Read several rows by identifier.
        /// </summary>
        /// <param name="ids">Identifiers (at most 500 distinct per call are read).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows keyed by identifier; unknown identifiers are absent.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        Task<Dictionary<string, LinkPreview>> ReadManyAsync(IEnumerable<string> ids, CancellationToken token = default);

        /// <summary>
        /// Store a fresh cache row for its url hash. A previous row is never modified in place: a Failed one is
        /// deleted and an Ok one (which content may reference) gets its urlhash renamed to "old:" plus its id, then
        /// the new row is inserted. When a concurrent refresh already inserted a row for the hash, that row wins.
        /// </summary>
        /// <param name="preview">New row.</param>
        /// <param name="previousId">Row being replaced, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The current row for the hash after the call.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task<LinkPreview> ReplaceAsync(LinkPreview preview, string? previousId, CancellationToken token = default);

        /// <summary>
        /// Delete expired Failed rows and expired Ok rows that no feed post or comment references.
        /// </summary>
        /// <param name="nowUtc">Current time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows removed.</returns>
        Task<int> PruneAsync(DateTime nowUtc, CancellationToken token = default);
    }
}

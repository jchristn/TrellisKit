namespace TrellisKit.Server.Workers
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Models;
    using TrellisKit.Server.Services;

    /// <summary>
    /// Recomputes stored excerpts of feed posts, comments, and forum threads that still hold Markdown syntax the
    /// plain-text rules now drop (inline and fenced code, tables, rules, # headings), so lists and notifications made
    /// before those rules show clean text. Rows with an empty body keep their excerpt (it comes from a title or a
    /// linked post). Runs shortly after boot and then daily; a pass with nothing to do is three indexed queries.
    /// Idempotent (a row is written only when its excerpt changes), so it needs no lease.
    /// Thread safety: one pass at a time per instance.
    /// </summary>
    public sealed class ExcerptRefreshWorker : IntervalWorker
    {
        #region Public-Members

        /// <inheritdoc />
        public override string JobName
        {
            get { return "excerpt_refresh"; }
        }

        #endregion

        #region Protected-Members

        /// <inheritdoc />
        protected override TimeSpan InitialDelay
        {
            get { return TimeSpan.FromSeconds(20); }
        }

        /// <inheritdoc />
        protected override TimeSpan Interval
        {
            get { return TimeSpan.FromDays(1); }
        }

        #endregion

        #region Private-Members

        private const int _BatchSize = 200;
        private const int _MaxBatchesPerTable = 50;
        private readonly DatabaseDriverBase _Database;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the worker.
        /// </summary>
        /// <param name="database">Property-bound database driver.</param>
        /// <param name="logger">Logger.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public ExcerptRefreshWorker(DatabaseDriverBase database, AppLogger logger) : base(logger)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Run one pass over the three tables.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Excerpts rewritten.</returns>
        public async Task<int> RefreshAsync(CancellationToken token = default)
        {
            int updated = 0;
            updated += await RefreshTableAsync(_Database.FeedPosts.ExcerptCandidatesAsync, _Database.FeedPosts.UpdateExcerptAsync, token).ConfigureAwait(false);
            updated += await RefreshTableAsync(_Database.Comments.ExcerptCandidatesAsync, _Database.Comments.UpdateExcerptAsync, token).ConfigureAwait(false);
            updated += await RefreshTableAsync(_Database.Threads.ExcerptCandidatesAsync, _Database.Threads.UpdateExcerptAsync, token).ConfigureAwait(false);
            return updated;
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override Task<int> ExecuteAsync(CancellationToken token)
        {
            return RefreshAsync(token);
        }

        #endregion

        #region Private-Methods

        private static async Task<int> RefreshTableAsync(
            Func<string?, int, CancellationToken, Task<List<ExcerptSource>>> candidates,
            Func<string, string, CancellationToken, Task> update,
            CancellationToken token)
        {
            int updated = 0;
            string? after = null;
            for (int batch = 0; batch < _MaxBatchesPerTable; batch++)
            {
                List<ExcerptSource> rows = await candidates(after, _BatchSize, token).ConfigureAwait(false);
                if (rows.Count == 0) break;
                foreach (ExcerptSource row in rows)
                {
                    after = row.Id;
                    if (String.IsNullOrEmpty(row.Body)) continue;
                    string excerpt = MarkdownNormalizer.Excerpt(row.Body);
                    if (String.Equals(excerpt, row.Excerpt, StringComparison.Ordinal)) continue;
                    await update(row.Id, excerpt, token).ConfigureAwait(false);
                    updated++;
                }
                if (rows.Count < _BatchSize) break;
            }
            return updated;
        }

        #endregion
    }
}

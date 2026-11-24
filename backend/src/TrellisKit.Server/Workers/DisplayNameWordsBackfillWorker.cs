namespace TrellisKit.Server.Workers
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Server.Services;

    /// <summary>
    /// Fills displaynamewords for members that have none (accounts created before member search existed, or rows lost
    /// to a failed write). Runs shortly after boot and then daily; a pass with nothing to do is one indexed query.
    /// Idempotent, so it needs no lease. Thread safety: one pass at a time per instance.
    /// </summary>
    public sealed class DisplayNameWordsBackfillWorker : IntervalWorker
    {
        #region Public-Members

        /// <inheritdoc />
        public override string JobName
        {
            get { return "display_name_words_backfill"; }
        }

        #endregion

        #region Protected-Members

        /// <inheritdoc />
        protected override TimeSpan InitialDelay
        {
            get { return TimeSpan.FromSeconds(15); }
        }

        /// <inheritdoc />
        protected override TimeSpan Interval
        {
            get { return TimeSpan.FromDays(1); }
        }

        #endregion

        #region Private-Members

        private readonly MemberSearchService _Search;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the worker.
        /// </summary>
        /// <param name="search">Member search service.</param>
        /// <param name="logger">Logger.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public DisplayNameWordsBackfillWorker(MemberSearchService search, AppLogger logger) : base(logger)
        {
            _Search = search ?? throw new ArgumentNullException(nameof(search));
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override Task<int> ExecuteAsync(CancellationToken token)
        {
            return _Search.BackfillAsync(500, token);
        }

        #endregion
    }
}

namespace TrellisKit.Server.Workers
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Models;
    using TrellisKit.Server.Services;
    using TrellisKit.Server.Services.Ai;

    /// <summary>
    /// Singleton knowledge ingestion worker (jobleases lease "ai_knowledge_index"; docs/AI_CONTRACT.md section 7.3): every
    /// ai.knowledgeIndexIntervalMinutes it runs an incremental pass for each assistant with knowledge on and an embedding
    /// endpoint. Skipped while AI is off or RecallDB is not configured.
    /// Thread safety: one pass at a time per instance; the lease keeps instances from overlapping.
    /// </summary>
    public sealed class KnowledgeIndexWorker : IntervalWorker
    {
        #region Public-Members

        /// <summary>
        /// Lease and job name.
        /// </summary>
        public const string Name = "ai_knowledge_index";

        /// <inheritdoc />
        public override string JobName
        {
            get { return Name; }
        }

        /// <summary>
        /// Identifier of this instance in the lease table.
        /// </summary>
        public string Holder { get; } = "aki-" + Environment.MachineName + "-" + Guid.NewGuid().ToString("N").Substring(0, 12);

        #endregion

        #region Protected-Members

        /// <inheritdoc />
        protected override TimeSpan InitialDelay
        {
            get { return TimeSpan.FromMinutes(2); }
        }

        /// <inheritdoc />
        protected override TimeSpan Interval
        {
            get { return TimeSpan.FromMinutes(Math.Max(1, _Ai.App.Settings.Ai.KnowledgeIndexIntervalMinutes)); }
        }

        #endregion

        #region Private-Members

        private readonly AiServices _Ai;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the worker.
        /// </summary>
        /// <param name="ai">AI services of the property.</param>
        /// <param name="logger">Logger.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public KnowledgeIndexWorker(AiServices ai, AppLogger logger) : base(logger)
        {
            _Ai = ai ?? throw new ArgumentNullException(nameof(ai));
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<int> ExecuteAsync(CancellationToken token)
        {
            if (!_Ai.IsAvailable || !_Ai.VectorStore.IsConfigured) return 0;
            if (!await _Ai.App.Database.JobLeases.TryAcquireAsync(Name, Holder, DateTime.UtcNow, TimeSpan.FromMinutes(30), token).ConfigureAwait(false)) return 0;
            int embedded = 0;
            string? after = null;
            while (true)
            {
                List<Assistant> page = await _Ai.App.Database.Ai.Assistants.ListForIndexingAsync(50, after, token).ConfigureAwait(false);
                if (page.Count == 0) break;
                foreach (Assistant assistant in page) embedded += await _Ai.Indexer.IndexAsync(assistant, token).ConfigureAwait(false);
                after = page[page.Count - 1].Id;
            }
            if (embedded > 0) Logger.Info("[KnowledgeIndexWorker] embedded " + embedded + " knowledge chunks");
            return embedded;
        }

        #endregion
    }
}

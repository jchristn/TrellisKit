namespace TrellisKit.Server.Workers
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Server.Services;
    using TrellisKit.Server.Services.Ai;

    /// <summary>
    /// Singleton endpoint health worker (jobleases lease "ai_endpoint_health"; docs/AI_CONTRACT.md section 4): every pass
    /// checks the enabled endpoints whose next check is due, prunes history, and publishes the health gauge. Skipped while
    /// AI is off for the property.
    /// Thread safety: one pass at a time per instance; the lease keeps instances from overlapping.
    /// </summary>
    public sealed class ModelEndpointHealthWorker : IntervalWorker
    {
        #region Public-Members

        /// <summary>
        /// Lease and job name.
        /// </summary>
        public const string Name = "ai_endpoint_health";

        /// <inheritdoc />
        public override string JobName
        {
            get { return Name; }
        }

        /// <summary>
        /// Identifier of this instance in the lease table.
        /// </summary>
        public string Holder { get; } = "aeh-" + Environment.MachineName + "-" + Guid.NewGuid().ToString("N").Substring(0, 12);

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
            get { return TimeSpan.FromSeconds(10); }
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
        public ModelEndpointHealthWorker(AiServices ai, AppLogger logger) : base(logger)
        {
            _Ai = ai ?? throw new ArgumentNullException(nameof(ai));
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<int> ExecuteAsync(CancellationToken token)
        {
            if (!_Ai.IsAvailable) return 0;
            if (!await _Ai.App.Database.JobLeases.TryAcquireAsync(Name, Holder, DateTime.UtcNow, TimeSpan.FromMinutes(2), token).ConfigureAwait(false)) return 0;
            return await _Ai.Health.RunDueAsync(token).ConfigureAwait(false);
        }

        #endregion
    }
}

namespace TrellisKit.Server.Workers
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Server.Services;
    using TrellisKit.Server.Services.Metrics;

    /// <summary>Per-property metrics aggregation. Runs once a minute; source scans never run in dashboard requests.</summary>
    public sealed class MetricsWorker : IntervalWorker
    {
        private readonly MetricsService _Metrics;
        /// <inheritdoc />
        public override string JobName => "metrics.rollup";
        /// <inheritdoc />
        protected override TimeSpan Interval => TimeSpan.FromMinutes(1);
        /// <inheritdoc />
        protected override TimeSpan InitialDelay => TimeSpan.FromSeconds(15);
        /// <summary>Create a worker for one property's metrics.</summary>
        public MetricsWorker(MetricsService metrics,AppLogger logger):base(logger) { _Metrics=metrics??throw new ArgumentNullException(nameof(metrics)); }
        /// <inheritdoc />
        protected override Task<int> ExecuteAsync(CancellationToken token) { return _Metrics.RunPassAsync(DateTime.UtcNow,token); }
    }
}

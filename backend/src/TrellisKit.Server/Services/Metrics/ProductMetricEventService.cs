namespace TrellisKit.Server.Services.Metrics
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Metrics;

    /// <summary>Opt-in, closed product events. Aggregates never contain member identifiers or member content.</summary>
    public sealed class ProductMetricEventService
    {
        private readonly ApplicationServices _Services;
        private static readonly HashSet<string> _BuiltIn = new HashSet<string>(StringComparer.Ordinal)
        { "signup.started", "signup.registered", "signup.verified", "profile.completed", "member.firstAction", "notification.opened" };
        /// <summary>Create a property-bound event recorder.</summary>
        public ProductMetricEventService(ApplicationServices services) { _Services = services ?? throw new ArgumentNullException(nameof(services)); }
        /// <summary>Validate and record a client event. Only signup.started is anonymous; server lifecycle events cannot be forged.</summary>
        public async Task RecordAsync(ProductMetricEvent payload, string? memberId, CancellationToken token = default)
        {
            if (!_Services.Metrics.Configuration.Enabled || !_Services.Metrics.Configuration.ProductEventsEnabled) return;
            ArgumentNullException.ThrowIfNull(payload);
            if (payload.Dimensions == null || payload.Dimensions.Count > 8) throw Invalid();
            if (payload.Name == "signup.started")
            {
                if (payload.Dimensions.Count != 1 || !payload.Dimensions.TryGetValue("source", out string? source) || !new[] { "web", "mobile", "other" }.Contains(source)) throw Invalid();
                await WriteAsync(payload.Name, source, null, token).ConfigureAwait(false);
                return;
            }
            if (String.IsNullOrEmpty(memberId)) throw new ApiErrorException(401, "Unauthorized", "A member session is required.");
            if (_BuiltIn.Contains(payload.Name ?? String.Empty)) throw Invalid();
            MetricEventDeclaration? declaration = _Services.Metrics.Configuration.Events.FirstOrDefault(e => e.Name == payload.Name);
            if (declaration == null || payload.Dimensions.Any(d => !declaration.Dimensions.TryGetValue(d.Key, out List<string>? values) || !values.Contains(d.Value))) throw Invalid();
            await _Services.Metrics.RecordAsync(new MetricObservation { Metric = "events.custom." + declaration.Name, Value = 1, Dimensions = payload.Dimensions }, token).ConfigureAwait(false);
        }
        /// <summary>Record a trusted lifecycle transition once per member; metric failures never fail the user action.</summary>
        public async Task ObserveAsync(string name, string? memberId, CancellationToken token = default)
        {
            if (!_Services.Metrics.Configuration.Enabled || !_Services.Metrics.Configuration.ProductEventsEnabled || String.IsNullOrEmpty(memberId)) return;
            if (!_BuiltIn.Contains(name) || name == "signup.started") throw new ArgumentException("Unknown lifecycle event.", nameof(name));
            try { await WriteAsync(name, "server", memberId, token).ConfigureAwait(false); }
            catch (Exception e) when (e is not OperationCanceledException) { _Services.Logger.Warn("[Metrics] product event failed: " + e.GetType().Name); }
        }
        /// <summary>Remove deduplication identifiers on account closure; anonymous aggregate counts are retained.</summary>
        public Task EraseAsync(string memberId, CancellationToken token = default)
        {
            return new ProductMetricEventStore(_Services.Database).EraseAsync(memberId, token);
        }
        private Task WriteAsync(string name, string source, string? memberId, CancellationToken token)
        {
            Dictionary<string,string> dimensions = new Dictionary<string,string> { ["event"] = name, ["source"] = source };
            // Notification reads count successful opens; lifecycle milestones are counted at most once.
            if (memberId == null || name == "notification.opened") return _Services.Metrics.RecordAsync(new MetricObservation { Metric = "events.product", Value = 1, Dimensions = dimensions }, token);
            return new ProductMetricEventStore(_Services.Database).RecordOnceAsync(name, memberId, dimensions, token);
        }
        private static ApiErrorException Invalid() { return new ApiErrorException(400, "ValidationFailed", "Use a declared event and finite dimension values."); }
    }
}

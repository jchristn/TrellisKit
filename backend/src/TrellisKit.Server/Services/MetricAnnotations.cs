namespace TrellisKit.Server.Services
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Metrics;
    /// <summary>Best-effort operational annotations with fixed labels, never member or administrator text.</summary>
    public static class MetricAnnotations
    {
        /// <summary>Record an operational transition without changing the result of the underlying action.</summary>
        public static async Task RecordAsync(ApplicationServices services, string kind, string id, string label, CancellationToken token = default)
        {
            if (!services.Metrics.Configuration.Enabled) return;
            try { await services.Metrics.AnnotateAsync(new MetricAnnotation { Kind = kind, Id = id, Label = label }, token).ConfigureAwait(false); }
            catch (Exception e) when (e is not OperationCanceledException) { services.Logger.Warn("[Metrics] annotation failed: " + e.GetType().Name); }
        }
    }
}

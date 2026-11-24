namespace Test.Shared.Suites
{
    using System;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Text.Json;
    using Touchstone.Core;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Metrics;
    using TrellisKit.Server.Services.Metrics;

    /// <summary>Platform authentication, property isolation and comparison semantics.</summary>
    public static class PlatformMetricsSuite
    {
        /// <summary>Build the suite.</summary>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor("PlatformMetrics", "Platform metric comparisons", new[]
            {
                SuiteCases.Http("PlatformMetrics", "IsolationRankingAndAuthorization", "Platform ranks property-scoped metrics and preserves authentication, labels and missing data", MultiPropertyKit.Multi, async (f, ct) =>
                {
                    string platform = await MultiPropertyKit.PlatformLoginAsync(f, ct).ConfigureAwait(false);
                    string second = await MultiPropertyKit.CreateSecondAsync(f, platform, ct).ConfigureAwait(false);
                    DateTime start = MetricsStore.Hour(DateTime.UtcNow.AddDays(-1));
                    MetricDefinition definition = new MetricRegistry(new TrellisKit.Core.PropertyConfig.PropertyMetricsConfiguration()).Find("requests.count")!;
                    foreach (string id in new[] { TestProperty.SampleId, second })
                    {
                        MetricsStore store = new MetricsStore(f.Services.Database.Deployment.ForProperty(id));
                        await store.RecordAsync(new MetricObservation { Metric = definition.Key, OccurredUtc = start, Value = id == second ? 25 : 10 }, definition, ct).ConfigureAwait(false);
                    }
                    object query = new { metric = "requests.count", from = start, to = start.AddHours(1), bucket = "Hour", compare = false };
                    string path = "/api/v1/platform/metrics/comparisons/enumerate";
                    JsonElement ranked = await MultiPropertyKit.PlatformExpectAsync(f, HttpMethod.Post, path, platform, new { query, includeSeries = true }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    JsonElement rows = ranked.GetProperty("objects");
                    Check.Equal(2, rows.GetArrayLength(), "Both properties");
                    Check.Equal(second, Api.Str(rows[0], "propertyId"), "Descending property totals");
                    Check.Equal(25d, rows[0].GetProperty("value").GetDouble(), "No cross-property sum");
                    Check.Equal("Sum", Api.Str(rows[0], "aggregation"), "Count semantics");
                    Check.True(rows[0].GetProperty("data").GetProperty("objects").GetArrayLength() > 0, "Optional chart data");
                    ContentTestKit.CheckMetadata(rows[0], "Comparison");
                    JsonElement page = await MultiPropertyKit.PlatformExpectAsync(f, HttpMethod.Post, path, platform, new { query, maxResults = 1, skip = 1 }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(TestProperty.SampleId, Api.Str(page.GetProperty("objects")[0], "propertyId"), "Stable computed paging");
                    JsonElement labelled = await MultiPropertyKit.PlatformExpectAsync(f, HttpMethod.Post, path, platform, new { query, labels = new[] { "test-fixture" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(second, String.Join(",", labelled.GetProperty("objects").EnumerateArray().Select(r => Api.Str(r, "propertyId"))), "Metadata filtering");
                    await MultiPropertyKit.PlatformExpectAsync(f, HttpMethod.Post, path, null, new { query }, HttpStatusCode.Unauthorized, ct).ConfigureAwait(false);
                    f.PropertyId = TestProperty.SampleId;
                    await MultiPropertyKit.PlatformExpectAsync(f, HttpMethod.Post, path, (await Api.AdminAsync(f, ct).ConfigureAwait(false)).AccessToken, new { query }, HttpStatusCode.Unauthorized, ct).ConfigureAwait(false);
                    await MultiPropertyKit.PlatformExpectAsync(f, HttpMethod.Post, "/api/v1/platform/properties/" + second + "/metrics/query", platform, new { query }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await MultiPropertyKit.PlatformExpectAsync(f, HttpMethod.Post, path, platform, new { query = new { metric = "requests.count", from = start, to = start.AddYears(2) } }, HttpStatusCode.BadRequest, ct).ConfigureAwait(false);
                }),
                SuiteCases.Http("PlatformMetrics", "RetainedHistoryAndDistribution", "Disabled properties retain readable metric history and percentile rankings never average buckets", MultiPropertyKit.Multi, async (f, ct) =>
                {
                    string platform = await MultiPropertyKit.PlatformLoginAsync(f, ct).ConfigureAwait(false);
                    string second = await MultiPropertyKit.CreateSecondAsync(f, platform, ct).ConfigureAwait(false);
                    DateTime start = MetricsStore.Hour(DateTime.UtcNow.AddDays(-1));
                    MetricsStore store = new MetricsStore(f.Services.Database.Deployment.ForProperty(second));
                    MetricDefinition distribution = new MetricRegistry(new TrellisKit.Core.PropertyConfig.PropertyMetricsConfiguration()).Find("requests.latency")!;
                    await store.RecordAsync(new MetricObservation { Metric = distribution.Key, OccurredUtc = start, Value = 10 }, distribution, ct).ConfigureAwait(false);
                    await store.RecordAsync(new MetricObservation { Metric = distribution.Key, OccurredUtc = start.AddHours(1), Value = 1000 }, distribution, ct).ConfigureAwait(false);
                    await MultiPropertyKit.PlatformExpectAsync(f, HttpMethod.Post, "/api/v1/platform/properties/" + second + "/disable", platform, new { reason = "Maintenance" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    object query = new { metric = distribution.Key, from = start, to = start.AddHours(2), bucket = "Hour", compare = false };
                    JsonElement result = await MultiPropertyKit.PlatformExpectAsync(f, HttpMethod.Post, "/api/v1/platform/properties/" + second + "/metrics/query", platform, new { query }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("requests.latency", Api.Str(result, "metric"), "Observation metric context");
                    Check.True(result.GetProperty("objects").EnumerateArray().Max(p=>p.GetProperty("value").GetDouble()) >= 1000, "Worst percentile instead of mean of percentiles");
                    Check.True(result.GetProperty("objects").GetArrayLength() > 0, "Disabled history retained");
                    JsonElement unknown = await MultiPropertyKit.PlatformExpectAsync(f, HttpMethod.Post, "/api/v1/platform/properties/" + second + "/metrics/query", platform, new { query = new { metric = "extension.notRegistered", from = start, to = start.AddHours(1) } }, HttpStatusCode.BadRequest, ct).ConfigureAwait(false);
                    Check.Equal("UnknownMetric", Api.Str(unknown, "error"), "Unavailable extension does not masquerade as zero");
                })
            });
        }
    }
}

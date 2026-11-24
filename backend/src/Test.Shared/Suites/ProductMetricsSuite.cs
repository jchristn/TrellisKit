namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Metrics;
    using TrellisKit.Server.Services.Metrics;
    using Touchstone.Core;

    /// <summary>Opt-in product event privacy, lifecycle idempotency, and anonymous request validation.</summary>
    public static class ProductMetricsSuite
    {
        /// <summary>Suite identifier.</summary>
        public const string Id = "ProductMetrics";
        /// <summary>Build independent database-backed cases.</summary>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Product event privacy and lifecycle", new[]
            {
                SuiteCases.Http(Id, "ConsentAndValidation", "Collection defaults off; anonymous events have a closed payload and bounded rate", async (f, ct) =>
                {
                    object started = new { name = "signup.started", dimensions = new { source = "web" } };
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/metrics/events", null, started, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
                    DataTable empty = await f.Database.ExecuteQueryAsync("SELECT * FROM metricseries WHERE propertyid='" + f.Database.BoundPropertyId + "' AND metric='events.product';", false, ct).ConfigureAwait(false);
                    Check.Equal(0, empty.Rows.Count, "No collection before consent");
                    f.Services.Metrics.Configuration.ProductEventsEnabled = true;
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/metrics/events", null, started, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/metrics/events", null, new { name = "signup.started", dimensions = new { source = "web", email = "private@example.com" } }, HttpStatusCode.BadRequest, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/metrics/events", null, new { name = "signup.registered", dimensions = new { } }, HttpStatusCode.Unauthorized, ct).ConfigureAwait(false);
                    DataTable rows = await f.Database.ExecuteQueryAsync("SELECT dimensions::text FROM metricseries WHERE propertyid='" + f.Database.BoundPropertyId + "' AND metric='events.product';", false, ct).ConfigureAwait(false);
                    Check.Equal(1, rows.Rows.Count, "Only a valid event stored");
                    Check.True(!rows.Rows[0][0].ToString()!.Contains("private"), "No free text persisted");
                    for (int n = 0; n < 26; n++) await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/metrics/events", null, started, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/metrics/events", null, started, HttpStatusCode.TooManyRequests, ct).ConfigureAwait(false);
                }),
                SuiteCases.Http(Id, "MilestonesAtomicAndErasable", "Concurrent lifecycle observations count once; deletion removes identifiers but keeps anonymous counts", async (f, ct) =>
                {
                    f.Services.Metrics.Configuration.ProductEventsEnabled = true;
                    TestMember member = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    ProductMetricEventService service = new ProductMetricEventService(f.Services);
                    DataTable registered = await f.Database.ExecuteQueryAsync("SELECT value FROM metricseries WHERE propertyid='" + f.Database.BoundPropertyId + "' AND metric='events.product' AND dimensions->>'event'='signup.registered';", false, ct).ConfigureAwait(false);
                    Check.Equal(1, registered.Rows.Count, "Registration hook records the milestone");
                    Check.Equal(1d, Convert.ToDouble(registered.Rows[0][0]), "Registration counted once");
                    await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => service.ObserveAsync("member.firstAction", member.Id, ct))).ConfigureAwait(false);
                    DataTable rows = await f.Database.ExecuteQueryAsync("SELECT value,dimensions::text FROM metricseries WHERE propertyid='" + f.Database.BoundPropertyId + "' AND metric='events.product' AND dimensions->>'event'='member.firstAction';", false, ct).ConfigureAwait(false);
                    Check.Equal(1, rows.Rows.Count, "One aggregate");
                    Check.Equal(1d, Convert.ToDouble(rows.Rows[0][0]), "Racing claims count once");
                    Check.True(!rows.Rows[0][1].ToString()!.Contains(member.Id), "No member identifier in aggregates");
                    await service.EraseAsync(member.Id, ct).ConfigureAwait(false);
                    DataTable ids = await f.Database.ExecuteQueryAsync("SELECT * FROM metricfirstactions WHERE propertyid='" + f.Database.BoundPropertyId + "';", false, ct).ConfigureAwait(false);
                    Check.Equal(0, ids.Rows.Count, "Identifiers erased");
                    rows = await f.Database.ExecuteQueryAsync("SELECT value FROM metricseries WHERE propertyid='" + f.Database.BoundPropertyId + "' AND metric='events.product' AND dimensions->>'event'='member.firstAction';", false, ct).ConfigureAwait(false);
                    Check.Equal(1d, Convert.ToDouble(rows.Rows[0][0]), "Counts retained");
                }),
                SuiteCases.Http(Id, "CustomEventAllowlist", "Custom events need member authentication and declared finite categories; built-in lifecycle names are protected", async (f, ct) =>
                {
                    f.Services.Metrics.Configuration.ProductEventsEnabled = true;
                    f.Services.Metrics.Configuration.Events.Add(new MetricEventDeclaration { Name = "lesson.opened", Dimensions = new Dictionary<string,List<string>> { ["format"] = new List<string> { "video", "text" } } });
                    f.Services.Metrics.Registry.Register(new MetricDefinition { Key = "events.custom.lesson.opened", Title = "Lesson opened", Groupings = new List<string> { "format" } });
                    TestMember member = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/metrics/events", null, new { name = "lesson.opened", dimensions = new { format = "video" } }, HttpStatusCode.Unauthorized, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/metrics/events", member.AccessToken, new { name = "lesson.opened", dimensions = new { format = "video" } }, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/metrics/events", member.AccessToken, new { name = "lesson.opened", dimensions = new { format = "private free text" } }, HttpStatusCode.BadRequest, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/metrics/events", member.AccessToken, new { name = "signup.verified", dimensions = new { } }, HttpStatusCode.BadRequest, ct).ConfigureAwait(false);
                }),
                SuiteCases.Http(Id, "PropertyIsolation", "Identical member and milestone keys in two properties do not collide or erase each other", async (f, ct) =>
                {
                    ProductMetricEventStore first = new ProductMetricEventStore(f.Database);
                    ProductMetricEventStore second = new ProductMetricEventStore(f.Raw.ForProperty("app_metrics1_otherproperty"));
                    Dictionary<string,string> dimensions = new Dictionary<string,string> { ["event"]="member.firstAction", ["source"]="server" };
                    await first.RecordOnceAsync("member.firstAction", "usr_same", dimensions, ct).ConfigureAwait(false);
                    await second.RecordOnceAsync("member.firstAction", "usr_same", dimensions, ct).ConfigureAwait(false);
                    await first.EraseAsync("usr_same", ct).ConfigureAwait(false);
                    DataTable other = await f.Raw.ExecuteQueryAsync("SELECT * FROM metricfirstactions WHERE propertyid='app_metrics1_otherproperty';", false, ct).ConfigureAwait(false);
                    Check.Equal(1, other.Rows.Count, "Other property milestone retained");
                })
            });
        }
    }
}

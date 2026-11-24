namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Touchstone.Core;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Shared database contract: migrations and request history methods. Runs against the provider selected by
    /// <see cref="TestEnvironment.Provider"/>, so the CI provider matrix exercises every provider with the same cases.
    /// </summary>
    public static class DatabaseSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "Database";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            string provider = TestEnvironment.Provider.ToString();
            return new TestSuiteDescriptor(Id, "Database contract (" + provider + ")", new[]
            {
                Case("Migrations.Idempotent", "Initializing twice applies each migration once", async (db, ct) =>
                {
                    int version = await db.Deployment.GetAppliedSchemaVersionAsync(ct).ConfigureAwait(false);
                    Check.Equal(db.Deployment.LatestSchemaVersion, version, "Applied version");
                    System.Data.DataTable before = await db.Deployment.ExecuteQueryAsync("SELECT COUNT(*) AS cnt FROM schema_migrations;", false, ct).ConfigureAwait(false);
                    await db.Deployment.InitializeAsync(ct).ConfigureAwait(false);
                    System.Data.DataTable rows = await db.Deployment.ExecuteQueryAsync("SELECT COUNT(*) AS cnt FROM schema_migrations;", false, ct).ConfigureAwait(false);
                    // Version numbers may have gaps (parallel work reserves ranges), so compare counts, not the highest version.
                    Check.Equal(RowReader.GetLong(before.Rows[0], "cnt"), RowReader.GetLong(rows.Rows[0], "cnt"), "Migration rows");
                    System.Data.DataTable latest = await db.Deployment.ExecuteQueryAsync("SELECT COUNT(*) AS cnt, COUNT(DISTINCT version) AS distinctcnt, MAX(version) AS maxversion FROM schema_migrations;", false, ct).ConfigureAwait(false);
                    Check.Equal(RowReader.GetLong(latest.Rows[0], "distinctcnt"), RowReader.GetLong(latest.Rows[0], "cnt"), "Each migration row once");
                    Check.Equal((long)db.Deployment.LatestSchemaVersion, RowReader.GetLong(latest.Rows[0], "maxversion"), "Latest migration applied");
                    Check.True(await db.Driver.PingAsync(ct).ConfigureAwait(false), "Ping");
                }),
                Case("RequestHistory.CreateRead", "Entries round-trip with headers, bodies, and special characters", async (db, ct) =>
                {
                    RequestHistoryEntry entry = Entry("POST", "/api/v1/things", 201, DateTime.UtcNow);
                    entry.RequestHeaders["X-Quote"] = "it's \"quoted\"; DROP TABLE requesthistory; --";
                    entry.RequestBody = "{\"name\":\"O'Neil éè 🥋\"}";
                    entry.RequestBodyBytes = 42;
                    entry.RequestBodyTruncated = true;
                    await db.Driver.RequestHistory.CreateAsync(entry, ct).ConfigureAwait(false);

                    RequestHistoryEntry? read = await db.Driver.RequestHistory.ReadAsync(entry.Id, ct).ConfigureAwait(false);
                    Check.True(read != null, "Entry found");
                    Check.Equal(entry.RequestHeaders["X-Quote"], read!.RequestHeaders["X-Quote"], "Header round-trip");
                    Check.Equal(entry.RequestBody, read.RequestBody, "Body round-trip");
                    Check.Equal(42L, read.RequestBodyBytes, "Original byte count");
                    Check.True(read.RequestBodyTruncated, "Truncated flag");
                    Check.True(Math.Abs((read.CreatedUtc - entry.CreatedUtc).TotalMilliseconds) < 2, "Timestamp precision");
                    Check.True(await db.Driver.RequestHistory.ReadAsync("req_missing", ct).ConfigureAwait(false) == null, "Missing entry");
                }),
                Case("RequestHistory.EnumerateFilters", "Paging, ordering, and every filter field", async (db, ct) =>
                {
                    DateTime now = DateTime.UtcNow;
                    for (int i = 0; i < 12; i++)
                    {
                        await db.Driver.RequestHistory.CreateAsync(Entry(i % 3 == 0 ? "POST" : "GET", i % 2 == 0 ? "/api/v1/health" : "/api/v1/settings", i % 4 == 0 ? 500 : 200, now.AddMinutes(-i)), ct).ConfigureAwait(false);
                    }

                    EnumerationResult<RequestHistoryEntry> page = await db.Driver.RequestHistory.EnumerateAsync(TestEnumeration.Staff(PhaseOneCollections.RequestHistory, 5, 5), new RequestHistoryFilter(), ct).ConfigureAwait(false);
                    Check.Equal(12L, page.TotalRecords, "Total");
                    Check.Equal(2L, page.RecordsRemaining, "Remaining after the second page of five");
                    Check.Equal(5, page.Objects.Count, "Page size");
                    Check.True(page.Objects.Zip(page.Objects.Skip(1), (a, b) => a.CreatedUtc >= b.CreatedUtc).All(x => x), "Newest first");

                    Check.Equal(4L, (await db.Driver.RequestHistory.EnumerateAsync(TestEnumeration.Staff(PhaseOneCollections.RequestHistory), new RequestHistoryFilter { Method = "post" }, ct).ConfigureAwait(false)).TotalRecords, "Method filter");
                    Check.Equal(3L, (await db.Driver.RequestHistory.EnumerateAsync(TestEnumeration.Staff(PhaseOneCollections.RequestHistory), new RequestHistoryFilter { StatusCode = 500 }, ct).ConfigureAwait(false)).TotalRecords, "Status filter");
                    Check.Equal(6L, (await db.Driver.RequestHistory.EnumerateAsync(TestEnumeration.Staff(PhaseOneCollections.RequestHistory), new RequestHistoryFilter { PathContains = "health" }, ct).ConfigureAwait(false)).TotalRecords, "Path filter");
                    Check.Equal(0L, (await db.Driver.RequestHistory.EnumerateAsync(TestEnumeration.Staff(PhaseOneCollections.RequestHistory), new RequestHistoryFilter { PathContains = "HEALTH" }, ct).ConfigureAwait(false)).TotalRecords, "Path filter is case-sensitive");
                    Check.Equal(0L, (await db.Driver.RequestHistory.EnumerateAsync(TestEnumeration.Staff(PhaseOneCollections.RequestHistory), new RequestHistoryFilter { PathContains = "%" }, ct).ConfigureAwait(false)).TotalRecords, "Wildcards are literal");
                    Check.Equal(5L, (await db.Driver.RequestHistory.EnumerateAsync(TestEnumeration.Staff(PhaseOneCollections.RequestHistory), new RequestHistoryFilter { FromUtc = now.AddMinutes(-4.5), ToUtc = now.AddMinutes(1) }, ct).ConfigureAwait(false)).TotalRecords, "Time window");
                    Check.True(page.Objects.All(i => i.Path.Length > 0), "List items mapped");
                }),
                Case("RequestHistory.SummaryGapFilled", "Summary returns every bucket and correct counts", async (db, ct) =>
                {
                    DateTime from = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                    await db.Driver.RequestHistory.CreateAsync(Entry("GET", "/a", 200, from.AddMinutes(1)), ct).ConfigureAwait(false);
                    await db.Driver.RequestHistory.CreateAsync(Entry("GET", "/a", 404, from.AddMinutes(14.9)), ct).ConfigureAwait(false);
                    await db.Driver.RequestHistory.CreateAsync(Entry("GET", "/a", 200, from.AddMinutes(15)), ct).ConfigureAwait(false);
                    await db.Driver.RequestHistory.CreateAsync(Entry("GET", "/a", 503, from.AddMinutes(59.99)), ct).ConfigureAwait(false);
                    await db.Driver.RequestHistory.CreateAsync(Entry("GET", "/a", 200, from.AddMinutes(60)), ct).ConfigureAwait(false);

                    RequestHistorySummary summary = await db.Driver.RequestHistory.SummarizeAsync(
                        new RequestHistoryFilter { FromUtc = from, ToUtc = from.AddHours(1), BucketMinutes = 15 }, ct).ConfigureAwait(false);
                    Check.Equal(4, summary.Buckets.Count, "Bucket count");
                    Check.Equal(from, summary.Buckets[0].BucketStartUtc, "First bucket starts at fromUtc");
                    Check.Equal(4L, summary.TotalCount, "Entries in range (toUtc exclusive)");
                    Check.Equal(2L, summary.TotalFailure, "Failures");
                    Check.Equal(1L, summary.Buckets[0].SuccessCount, "Bucket 0 success");
                    Check.Equal(1L, summary.Buckets[0].FailureCount, "Bucket 0 failure");
                    Check.Equal(1L, summary.Buckets[1].SuccessCount, "Bucket 1 success");
                    Check.Equal(0L, summary.Buckets[2].SuccessCount + summary.Buckets[2].FailureCount, "Empty bucket present");
                    Check.Equal(1L, summary.Buckets[3].FailureCount, "Bucket 3 failure");
                    Check.True(summary.AverageDurationMs > 0, "Average duration");
                }),
                Case("RequestHistory.SummaryValidation", "Summary rejects missing, inverted, and oversized ranges", async (db, ct) =>
                {
                    await Check.ThrowsAsync<ArgumentException>(() => db.Driver.RequestHistory.SummarizeAsync(new RequestHistoryFilter(), ct), "Missing range").ConfigureAwait(false);
                    await Check.ThrowsAsync<ArgumentException>(() => db.Driver.RequestHistory.SummarizeAsync(new RequestHistoryFilter { FromUtc = DateTime.UtcNow, ToUtc = DateTime.UtcNow.AddHours(-1) }, ct), "Inverted range").ConfigureAwait(false);
                    await Check.ThrowsAsync<ArgumentException>(() => db.Driver.RequestHistory.SummarizeAsync(new RequestHistoryFilter { FromUtc = DateTime.UtcNow.AddYears(-1), ToUtc = DateTime.UtcNow, BucketMinutes = 1 }, ct), "Too many buckets").ConfigureAwait(false);
                }),
                Case("RequestHistory.Delete", "Delete one, delete by filter, and prune", async (db, ct) =>
                {
                    DateTime now = DateTime.UtcNow;
                    RequestHistoryEntry keep = Entry("GET", "/keep", 200, now);
                    RequestHistoryEntry one = Entry("GET", "/one", 200, now);
                    await db.Driver.RequestHistory.CreateAsync(keep, ct).ConfigureAwait(false);
                    await db.Driver.RequestHistory.CreateAsync(one, ct).ConfigureAwait(false);
                    await db.Driver.RequestHistory.CreateAsync(Entry("GET", "/bad", 401, now), ct).ConfigureAwait(false);
                    await db.Driver.RequestHistory.CreateAsync(Entry("GET", "/bad", 401, now), ct).ConfigureAwait(false);
                    await db.Driver.RequestHistory.CreateAsync(Entry("GET", "/old", 200, now.AddDays(-40)), ct).ConfigureAwait(false);

                    Check.True(await db.Driver.RequestHistory.DeleteAsync(one.Id, ct).ConfigureAwait(false), "Delete existing");
                    Check.True(!await db.Driver.RequestHistory.DeleteAsync(one.Id, ct).ConfigureAwait(false), "Delete missing");
                    Check.Equal(2, await db.Driver.RequestHistory.DeleteManyAsync(new RequestHistoryFilter { StatusCode = 401 }, ct).ConfigureAwait(false), "Delete by filter");
                    Check.Equal(1, await db.Driver.RequestHistory.PruneAsync(now.AddDays(-30), ct).ConfigureAwait(false), "Prune");
                    EnumerationResult<RequestHistoryEntry> remaining = await db.Driver.RequestHistory.EnumerateAsync(TestEnumeration.Staff(PhaseOneCollections.RequestHistory), new RequestHistoryFilter(), ct).ConfigureAwait(false);
                    Check.Equal(1L, remaining.TotalRecords, "Remaining");
                    Check.Equal(keep.Id, remaining.Objects[0].Id, "Kept entry");
                }),
                Case("RequestHistory.ConcurrentWrites", "200 concurrent inserts all persist", async (db, ct) =>
                {
                    List<Task> writes = Enumerable.Range(0, 200)
                        .Select(i => db.Driver.RequestHistory.CreateAsync(Entry("GET", "/c/" + i, 200, DateTime.UtcNow), ct))
                        .ToList();
                    await Task.WhenAll(writes).ConfigureAwait(false);
                    EnumerationResult<RequestHistoryEntry> page = await db.Driver.RequestHistory.EnumerateAsync(TestEnumeration.Staff(PhaseOneCollections.RequestHistory, 1), new RequestHistoryFilter(), ct).ConfigureAwait(false);
                    Check.Equal(200L, page.TotalRecords, "Rows");
                })
            });
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string caseId, string name, Func<TestDatabase, CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(Id, caseId, name, async ct =>
            {
                TestDatabase db = await TestDatabase.CreateAsync(ct).ConfigureAwait(false);
                await using (db.ConfigureAwait(false))
                {
                    await body(db, ct).ConfigureAwait(false);
                }
            }, new[] { "database", TestEnvironment.Provider.ToString().ToLowerInvariant() });
        }

        private static RequestHistoryEntry Entry(string method, string path, int status, DateTime createdUtc)
        {
            return new RequestHistoryEntry
            {
                Method = method,
                Path = path,
                Url = path,
                StatusCode = status,
                DurationMs = 2.5,
                SourceIp = "127.0.0.1",
                CreatedUtc = createdUtc,
                CompletedUtc = createdUtc.AddMilliseconds(2.5)
            };
        }

        #endregion
    }
}

namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Globalization;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Touchstone.Core;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Server.Services.Usage;

    /// <summary>
    /// Usage metering (docs/USAGE_METERING_CONTRACT.md): period math and DST boundaries, the meter catalog, every meter
    /// against its source rows, idempotent rollups, backfill and closed periods, recorded counters, storage measurement,
    /// the summary, enumeration, and export routes, the platform routes, and isolation between two properties.
    /// </summary>
    public static class UsageSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "Usage";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Usage metering (" + TestEnvironment.Provider + ")", new[]
            {
                SuiteCases.Unit(Id, "Periods.UtcBoundaries", "Every period resolves to the contract bounds, buckets, and previous period in UTC", ct =>
                {
                    DateTime now = new DateTime(2026, 11, 12, 18, 3, 11, DateTimeKind.Utc);
                    UsageZone utc = UsageZone.Utc;
                    UsageWindow day = Resolve(UsagePeriodEnum.LastDay, now, utc);
                    Check.Equal(new DateTime(2026, 11, 11, 19, 0, 0, DateTimeKind.Utc), day.FromUtc, "LastDay from");
                    Check.Equal(UsageBucketEnum.Hour, day.Bucket, "LastDay bucket");
                    Check.Equal(24, day.Buckets.Count, "LastDay buckets");
                    Check.Equal(now.AddHours(-24), day.PreviousToUtc, "LastDay previous end");
                    UsageWindow week = Resolve(UsagePeriodEnum.LastWeek, now, utc);
                    Check.Equal(new DateTime(2026, 11, 6, 0, 0, 0, DateTimeKind.Utc), week.FromUtc, "LastWeek from (7 days including today)");
                    Check.Equal(7, week.Buckets.Count, "LastWeek buckets");
                    Check.Equal(new DateTime(2026, 10, 30, 0, 0, 0, DateTimeKind.Utc), week.PreviousFromUtc, "LastWeek previous from");
                    Check.Equal(now.AddDays(-7), week.PreviousToUtc, "LastWeek previous end");
                    Check.Equal(30, Resolve(UsagePeriodEnum.LastMonth, now, utc).Buckets.Count, "LastMonth buckets");
                    UsageWindow quarter = Resolve(UsagePeriodEnum.Last90Days, now, utc);
                    Check.Equal(90, quarter.Buckets.Count, "Last90Days is the trailing 90 days");
                    Check.Equal(UsageBucketEnum.Day, quarter.Bucket, "Last90Days bucket");
                    Check.Equal(new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc), quarter.FromUtc, "Last90Days from");
                    UsageWindow previousQuarter = Resolve(UsagePeriodEnum.PreviousQuarter, now, utc);
                    Check.Equal(new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc), previousQuarter.FromUtc, "PreviousQuarter is the previous calendar quarter");
                    Check.Equal(new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), previousQuarter.ToUtc, "PreviousQuarter ends at this quarter's start, not now");
                    Check.Equal(new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc), previousQuarter.PreviousFromUtc, "The quarter before it");
                    Check.Equal(92, previousQuarter.Buckets.Count, "PreviousQuarter is daily (92 days)");
                    UsageWindow firstQuarter = Resolve(UsagePeriodEnum.PreviousQuarter, new DateTime(2026, 2, 3, 0, 0, 0, DateTimeKind.Utc), utc);
                    Check.Equal(new DateTime(2025, 10, 1, 0, 0, 0, DateTimeKind.Utc), firstQuarter.FromUtc, "PreviousQuarter crosses the year");
                    UsageWindow year = Resolve(UsagePeriodEnum.LastYear, now, utc);
                    Check.Equal(UsageBucketEnum.Week, year.Bucket, "LastYear bucket");
                    Check.True(year.Buckets.Count >= 53 && year.Buckets.Count <= 54, "LastYear weekly buckets (" + year.Buckets.Count + ")");
                    Check.Equal(year.FromUtc, year.Buckets[0].StartUtc, "First week is clipped to the period");
                    Check.Equal("2026-11-09", year.Buckets[year.Buckets.Count - 1].Label, "Weeks start on Monday");
                    UsageWindow thisWeek = Resolve(UsagePeriodEnum.ThisWeek, now, utc);
                    Check.Equal(new DateTime(2026, 11, 9, 0, 0, 0, DateTimeKind.Utc), thisWeek.FromUtc, "ThisWeek starts Monday");
                    Check.Equal(new DateTime(2026, 11, 5, 18, 3, 11, DateTimeKind.Utc), thisWeek.PreviousToUtc, "ThisWeek previous is the same offset last week");
                    UsageWindow month = Resolve(UsagePeriodEnum.ThisMonth, now, utc);
                    Check.Equal(new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc), month.FromUtc, "ThisMonth from");
                    Check.Equal(new DateTime(2026, 10, 12, 18, 3, 11, DateTimeKind.Utc), month.PreviousToUtc, "ThisMonth previous to date");
                    UsageWindow thisQuarter = Resolve(UsagePeriodEnum.ThisQuarter, now, utc);
                    Check.Equal(new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), thisQuarter.FromUtc, "ThisQuarter is the calendar quarter");
                    Check.Equal(new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc), thisQuarter.PreviousFromUtc, "Previous calendar quarter");
                    UsageWindow thisYear = Resolve(UsagePeriodEnum.ThisYear, now, utc);
                    Check.Equal(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), thisYear.FromUtc, "ThisYear from");
                    Check.Equal(UsageBucketEnum.Week, thisYear.Bucket, "ThisYear in November is weekly");
                    UsageWindow early = Resolve(UsagePeriodEnum.ThisYear, new DateTime(2026, 2, 10, 0, 0, 0, DateTimeKind.Utc), utc);
                    Check.Equal(UsageBucketEnum.Day, early.Bucket, "ThisYear in February is daily");
                    UsageWindow range = UsagePeriodCalculator.Resolve(UsagePeriodEnum.Range, "2026-10-01", "2026-10-10", null, now, utc, 90);
                    Check.Equal(new DateTime(2026, 10, 11, 0, 0, 0, DateTimeKind.Utc), range.ToUtc, "Range to is inclusive");
                    Check.Equal(10, range.Buckets.Count, "Range buckets");
                    Check.Equal(new DateTime(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc), range.PreviousFromUtc, "Range previous is the same number of days before");
                    UsageWindow clamped = UsagePeriodCalculator.Resolve(UsagePeriodEnum.Range, "2026-11-01", "2026-11-30", null, now, utc, 90);
                    Check.Equal(now, clamped.ToUtc, "Range end clamps to now");
                    UsageWindow instants = UsagePeriodCalculator.Resolve(UsagePeriodEnum.Range, "2026-11-12T01:30:00Z", "2026-11-12T05:00:00+00:00", null, now, utc, 90);
                    Check.Equal(UsageBucketEnum.Hour, instants.Bucket, "A short date-time range is hourly");
                    Check.Equal(instants.FromUtc, instants.Buckets[0].StartUtc, "The first hour is clipped");
                    Check.Equal(4, instants.Buckets.Count, "Hours touching the range");
                    return Task.CompletedTask;
                }),
                SuiteCases.Unit(Id, "Periods.Validation", "Bad periods, ranges, and bucket overrides are 400 ValidationFailed with the field named", ct =>
                {
                    DateTime now = new DateTime(2026, 11, 12, 18, 0, 0, DateTimeKind.Utc);
                    Dictionary<string, string> errors = new Dictionary<string, string>();
                    UsagePeriodCalculator.ParsePeriod("Fortnight", errors);
                    Check.True(errors.ContainsKey("period"), "Unknown period");
                    Field(() => UsagePeriodCalculator.Resolve(UsagePeriodEnum.Range, null, "2026-11-01", null, now, UsageZone.Utc, 90), "from");
                    Field(() => UsagePeriodCalculator.Resolve(UsagePeriodEnum.Range, "2026-11-05", "2026-11-01", null, now, UsageZone.Utc, 90), "to");
                    Field(() => UsagePeriodCalculator.Resolve(UsagePeriodEnum.Range, "2026-12-01", "2026-12-05", null, now, UsageZone.Utc, 90), "from");
                    Field(() => UsagePeriodCalculator.Resolve(UsagePeriodEnum.Range, "2018-01-01", "2026-01-01", null, now, UsageZone.Utc, 90), "to");
                    Field(() => UsagePeriodCalculator.Resolve(UsagePeriodEnum.Range, "2026-11-01T00:00:00", "2026-11-02", null, now, UsageZone.Utc, 90), "from");
                    Field(() => UsagePeriodCalculator.Resolve(UsagePeriodEnum.LastMonth, null, null, UsageBucketEnum.Hour, now, UsageZone.Utc, 90), "bucket");
                    Field(() => UsagePeriodCalculator.Resolve(UsagePeriodEnum.Range, "2022-01-01", "2026-01-01", UsageBucketEnum.Day, now, UsageZone.Utc, 90), "bucket");
                    UsageWindow monthly = UsagePeriodCalculator.Resolve(UsagePeriodEnum.Range, "2022-01-01", "2026-01-01", null, now, UsageZone.Utc, 90);
                    Check.Equal(UsageBucketEnum.Month, monthly.Bucket, "A long range is monthly");
                    Check.Equal(49, monthly.Buckets.Count, "Monthly buckets of four years and a day");
                    return Task.CompletedTask;
                }),
                SuiteCases.Unit(Id, "Periods.DaylightSaving", "Local days in a non-UTC zone are 25 and 23 hours long across daylight saving changes, and weeks follow the configured start", ct =>
                {
                    UsageZone ny = UsageZone.From("America/New_York", "Monday");
                    Check.Equal("America/New_York", ny.Id, "Zone resolves");
                    DateTime now = new DateTime(2026, 11, 3, 15, 0, 0, DateTimeKind.Utc);
                    UsageWindow week = Resolve(UsagePeriodEnum.LastWeek, now, ny);
                    Check.Equal(new DateTime(2026, 10, 28, 4, 0, 0, DateTimeKind.Utc), week.FromUtc, "Local midnight in EDT");
                    UsageWindowBucket fallBack = week.Buckets.Single(b => b.Label == "2026-11-01");
                    Check.Equal(25.0, (fallBack.SelectEndUtc - fallBack.SelectStartUtc).TotalHours, "The fall-back day has 25 hours");
                    Check.Equal(new DateTime(2026, 11, 2, 5, 0, 0, DateTimeKind.Utc), week.Buckets.Single(b => b.Label == "2026-11-02").SelectStartUtc, "Local midnight in EST");
                    UsageWindow spring = UsagePeriodCalculator.Resolve(UsagePeriodEnum.Range, "2026-03-08", "2026-03-08", null, new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc), ny, 90);
                    Check.Equal(23.0, (spring.ToUtc - spring.FromUtc).TotalHours, "The spring-forward day has 23 hours");
                    Check.Equal(UsageBucketEnum.Day, spring.Bucket, "Outside the hourly retention a day is one Day bucket");
                    UsageWindow thisMonth = Resolve(UsagePeriodEnum.ThisMonth, now, ny);
                    Check.Equal(new DateTime(2026, 11, 1, 4, 0, 0, DateTimeKind.Utc), thisMonth.FromUtc, "Month starts at local midnight before the change");
                    UsageWindow nyQuarter = UsagePeriodCalculator.Resolve(UsagePeriodEnum.PreviousQuarter, null, null, null, new DateTime(2026, 11, 12, 18, 0, 0, DateTimeKind.Utc), ny, 90);
                    Check.Equal(new DateTime(2026, 7, 1, 4, 0, 0, DateTimeKind.Utc), nyQuarter.FromUtc, "PreviousQuarter starts at local midnight (EDT)");
                    Check.Equal(new DateTime(2026, 10, 1, 4, 0, 0, DateTimeKind.Utc), nyQuarter.ToUtc, "and ends at the next quarter's local midnight");
                    Check.Equal(UsageBucketEnum.Day, nyQuarter.Bucket, "PreviousQuarter is daily");
                    Check.Equal(DayOfWeek.Monday, UsageZone.From("America/New_York", null).WeekStart, "Weeks start on Monday by default");
                    UsageZone sunday = UsageZone.From("America/New_York", "Sunday");
                    Check.Equal(new DateTime(2026, 11, 1, 4, 0, 0, DateTimeKind.Utc), Resolve(UsagePeriodEnum.ThisWeek, now, sunday).FromUtc, "Sunday weeks");
                    Check.Equal(new DateTime(2026, 11, 2, 5, 0, 0, DateTimeKind.Utc), Resolve(UsagePeriodEnum.ThisWeek, now, ny).FromUtc, "Monday weeks");
                    UsageZone fallbackZone = UsageZone.From("Not/AZone", "Blursday");
                    Check.Equal("UTC", fallbackZone.Id, "Unknown zones fall back to UTC");
                    Check.Equal(DayOfWeek.Monday, fallbackZone.WeekStart, "Unknown days fall back to Monday");
                    return Task.CompletedTask;
                }),
                SuiteCases.Unit(Id, "Meters.Catalog", "The catalog has stable unique ids, groups, kinds, and units; groups and ids resolve; record ids are deterministic", ct =>
                {
                    Check.Equal(35, UsageMeters.All.Count, "Meter count");
                    Check.Equal(UsageMeters.All.Count, UsageMeters.All.Select(m => m.Id).Distinct().Count(), "Unique ids");
                    Check.True(UsageMeters.All.All(m => UsageMeters.Groups.Contains(m.Group)), "Every meter is in a group");
                    Check.Equal(6, UsageMeters.Resolve("storage", out string? _).Count, "Storage group");
                    List<UsageMeter> mixed = UsageMeters.Resolve("users.new,ai", out string? none);
                    Check.True(none == null && mixed.Count == 5, "Ids and groups mix");
                    UsageMeters.Resolve("users.new,nope", out string? unknown);
                    Check.Equal("nope", unknown, "Unknown entries are reported");
                    Check.Equal("usd_20261112_users-new", UsageMeters.RecordId("users.new", "2026-11-12"), "Record id");
                    Check.Equal(UsageMeterSourceEnum.Recorded, UsageMeters.Find("api.requests")!.Source, "Requests are recorded");
                    Check.Equal(UsageMeterSourceEnum.Measured, UsageMeters.Find("storage.videoRenditions")!.Source, "Renditions are measured");
                    return Task.CompletedTask;
                }),
                SuiteCases.Database(Id, "Meters.EveryDerivedMeterFromRows", "Each interaction, delivery, deletion, and storage meter counts exactly its defined rows in a bucket and nothing outside it", async (db, ct) =>
                {
                    string p = "'" + TestProperty.SampleId + "'";
                    string inside = "'2026-01-05T10:15:00Z'";
                    string outside = "'2026-01-05T12:15:00Z'";
                    List<string> sql = new List<string>
                    {
                        "INSERT INTO users (propertyid, id, displayname, storeaccounttoken, isdeleted, deletedutc, createdutc, updatedutc) VALUES (" + p + ", 'usr_gone', 'Gone', 'tok_gone', TRUE, " + inside + ", '2025-12-01T00:00:00Z', " + inside + ");",
                        "INSERT INTO pollvotes (propertyid, id, pollid, optionid, userid, createdutc) VALUES (" + p + ", 'pvo_1', 'pol_1', 'pop_1', 'usr_a', " + inside + "), (" + p + ", 'pvo_2', 'pol_1', 'pop_2', 'usr_a', " + inside + "), (" + p + ", 'pvo_3', 'pol_1', 'pop_1', 'usr_b', " + outside + ");",
                        "INSERT INTO forumfollows (propertyid, id, forumid, userid, level, createdutc) VALUES (" + p + ", 'ffo_1', 'frm_1', 'usr_a', 'All', " + inside + ");",
                        "INSERT INTO threadfollows (propertyid, id, threadid, userid, source, createdutc) VALUES (" + p + ", 'tfo_1', 'thr_1', 'usr_a', 'Explicit', " + inside + "), (" + p + ", 'tfo_2', 'thr_1', 'usr_b', 'Author', " + inside + "), (" + p + ", 'tfo_3', 'thr_2', 'usr_b', 'Comment', " + inside + ");",
                        "INSERT INTO aimessages (propertyid, id, conversationid, assistantid, role, content, status, createdutc) VALUES (" + p + ", 'acm_1', 'acv_1', 'ast_1', 'User', 'q', 'Complete', " + inside + "), (" + p + ", 'acm_2', 'acv_1', 'ast_1', 'Assistant', 'a', 'Complete', " + inside + ");",
                        "INSERT INTO pushdeliveries (propertyid, id, notificationid, pushtokenid, status, createdutc) VALUES (" + p + ", 'pdl_1', 'ntf_1', 'ptk_1', 'Sent', " + inside + "), (" + p + ", 'pdl_2', 'ntf_1', 'ptk_2', 'Ok', " + inside + "), (" + p + ", 'pdl_3', 'ntf_1', 'ptk_3', 'Error', " + inside + "), (" + p + ", 'pdl_4', 'ntf_1', 'ptk_4', 'Queued', " + inside + ");",
                        "INSERT INTO emailmessages (propertyid, id, template, category, recipient, recipientnormalized, subject, status, createdutc, sentutc, updatedutc) VALUES (" + p + ", 'eml_1', 't', 'Account', 'a@example.test', 'a@example.test', 's', 'Sent', " + inside + ", " + inside + ", " + inside + "), (" + p + ", 'eml_2', 't', 'Account', 'b@example.test', 'b@example.test', 's', 'Queued', " + inside + ", NULL, " + inside + ");",
                        "INSERT INTO emailsends (propertyid, id, userid, category, sourceid, status, createdutc) VALUES (" + p + ", 'ems_1', 'usr_a', 'FeedPosts', 'fpo_1', 'Sent', " + inside + "), (" + p + ", 'ems_2', 'usr_b', 'FeedPosts', 'fpo_1', 'Skipped', " + inside + ");",
                        "INSERT INTO updateassets (propertyid, sha256hex, hashb64url, contenttype, sizebytes, storedutc, lastreferencedutc) VALUES (" + p + ", 'aa', 'aa', 'application/javascript', 4096, " + inside + ", " + inside + ");",
                        "INSERT INTO accountexports (propertyid, id, userid, status, createdutc, completedutc, expiresutc, sizebytes) VALUES (" + p + ", 'aex_1', 'usr_a', 'Ready', " + inside + ", " + inside + ", '2026-01-06T00:00:00Z', 512), (" + p + ", 'aex_2', 'usr_b', 'Ready', " + inside + ", " + inside + ", '2026-01-05T10:30:00Z', 999);"
                    };
                    foreach (string statement in sql) await db.Deployment.ExecuteQueryAsync(statement, false, ct).ConfigureAwait(false);
                    List<UsageBucketRange> buckets = new List<UsageBucketRange>
                    {
                        new UsageBucketRange { Key = "before", StartUtc = new DateTime(2026, 1, 5, 9, 0, 0, DateTimeKind.Utc), EndUtc = new DateTime(2026, 1, 5, 10, 0, 0, DateTimeKind.Utc) },
                        new UsageBucketRange { Key = "in", StartUtc = new DateTime(2026, 1, 5, 10, 0, 0, DateTimeKind.Utc), EndUtc = new DateTime(2026, 1, 5, 11, 0, 0, DateTimeKind.Utc) }
                    };
                    Dictionary<string, long> expected = new Dictionary<string, long>
                    {
                        { "users.deleted", 1 }, { "pollVotes.new", 2 }, { "follows.new", 2 }, { "assistantMessages.new", 1 }, { "interactions.new", 5 },
                        { "push.sent", 2 }, { "email.sent", 2 }, { "storage.updateAssets", 4096 }, { "storage.exports", 512 }
                    };
                    foreach (KeyValuePair<string, long> pair in expected)
                    {
                        Dictionary<string, long> values = await db.Driver.Usage.ComputeAsync(pair.Key, buckets, true, ct).ConfigureAwait(false);
                        Check.Equal(pair.Value, values["in"], pair.Key + " in the bucket");
                        Check.Equal(0L, values["before"], pair.Key + " before the bucket");
                    }
                    Dictionary<string, long> total = await db.Driver.Usage.ComputeAsync("users.total", buckets, false, ct).ConfigureAwait(false);
                    Check.Equal(1L, total["before"], "A member counts until deleted");
                    Check.Equal(0L, total["in"], "and not after");
                    await Check.ThrowsAsync<ArgumentException>(() => db.Driver.Usage.ComputeAsync("nope", buckets, true, ct), "Unknown meter").ConfigureAwait(false);
                    Dictionary<string, long> other = await db.Deployment.ForProperty(PropertyIds.Generate()).Usage.ComputeAsync("interactions.new", buckets, true, ct).ConfigureAwait(false);
                    Check.Equal(0L, other["in"], "Another property sees none of the rows");
                }),
                SuiteCases.Http(Id, "Meters.MatchSourceRows", "After a pass every meter of the open window equals its source rows: members, active members, posts, threads, comments, interactions, media counts and bytes, storage classes, push, email, AI, and API requests", async (f, ct) =>
                {
                    ContentTestKit.Relax(f);
                    TestMember poster = await ContentTestKit.PosterAsync(f, ct).ConfigureAwait(false);
                    TestMember member = await ContentTestKit.MemberAsync(f, ct).ConfigureAwait(false);
                    string post = await ContentTestKit.PostIdAsync(f, poster.AccessToken, "Usage post one", ct).ConfigureAwait(false);
                    await ContentTestKit.PostIdAsync(f, poster.AccessToken, "Usage post two", ct).ConfigureAwait(false);
                    await ContentTestKit.CommentIdAsync(f, member.AccessToken, post, "Usage comment", null, ct).ConfigureAwait(false);
                    await ContentTestKit.CommentIdAsync(f, poster.AccessToken, post, "Usage reply", null, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/feed/" + post + "/reactions/like", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    string image = await ContentTestKit.MediaAsync(f, poster.Id, MediaKindEnum.Image, ct).ConfigureAwait(false);
                    string video = await ContentTestKit.MediaAsync(f, poster.Id, MediaKindEnum.Video, ct).ConfigureAwait(false);
                    await ContentTestKit.MediaAsync(f, poster.Id, MediaKindEnum.Image, ct, MediaStatusEnum.Uploading).ConfigureAwait(false);
                    string pid = f.Services.PropertyId;
                    await f.Raw.ExecuteQueryAsync("INSERT INTO mediavariants (propertyid, mediaid, name, format, width, height, sizebytes, filename) VALUES ('" + pid + "', '" + image + "', 'thumb', 'webp', 10, 10, 300, 'thumb.webp');", false, ct).ConfigureAwait(false);
                    byte[] rendition = Encoding.UTF8.GetBytes(new string('v', 777));
                    await f.Fakes.ObjectStore.PutAsync(MediaLocationEnum.Transcoded, MediaKeys.VideoPrefix(pid, video) + "720.mp4", rendition, "video/mp4", ct).ConfigureAwait(false);
                    await f.Raw.ExecuteQueryAsync("INSERT INTO aiusagerollups (propertyid, periodstartutc, granularity, assistantid, endpointid, purpose, outcome, istest, requests, prompttokens, completiontokens, costmicros) VALUES ('" + pid +
                        "', date_trunc('hour', now()), 'Hour', 'ast_x', 'aie_x', 'Chat', 'Ok', FALSE, 3, 120, 80, 4500), ('" + pid + "', date_trunc('hour', now()), 'Hour', 'ast_x', 'aie_x', 'Chat', 'Ok', TRUE, 9, 900, 900, 9000);", false, ct).ConfigureAwait(false);
                    for (int i = 0; i < 4; i++) await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/auth/me", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);

                    await PassesAsync(f, ct).ConfigureAwait(false);
                    JsonElement summary = await MetricEnumerationTestKit.UsageAsync(f, "/api/v1/admin/usage/summary?period=LastDay", HttpFixture.AdminToken, ct).ConfigureAwait(false);
                    Check.Equal("Hour", Api.Str(summary, "bucket"), "LastDay is hourly");
                    Check.Equal(24, summary.GetProperty("buckets").GetArrayLength(), "24 buckets");
                    string where = "propertyid = '" + pid + "'";
                    await Same(f, summary, "users.new", "SELECT COUNT(*) AS cnt FROM users WHERE " + where, ct).ConfigureAwait(false);
                    await Same(f, summary, "users.total", "SELECT COUNT(*) AS cnt FROM users WHERE " + where + " AND isdeleted = FALSE", ct).ConfigureAwait(false);
                    await Same(f, summary, "posts.new", "SELECT (SELECT COUNT(*) FROM feedposts WHERE " + where + ") + (SELECT COUNT(*) FROM forumthreads WHERE " + where + ") AS cnt", ct).ConfigureAwait(false);
                    await Same(f, summary, "comments.new", "SELECT COUNT(*) AS cnt FROM comments WHERE " + where, ct).ConfigureAwait(false);
                    await Same(f, summary, "reactions.new", "SELECT COUNT(*) AS cnt FROM reactions WHERE " + where, ct).ConfigureAwait(false);
                    await Same(f, summary, "images.new", "SELECT COUNT(*) AS cnt FROM mediaattachments WHERE " + where + " AND kind = 'Image' AND status <> 'Uploading'", ct).ConfigureAwait(false);
                    Check.Equal(1000L, Total(summary, "videos.bytes"), "Video bytes uploaded");
                    Check.Equal(2L, Total(summary, "images.total") + Total(summary, "videos.total"), "Completed media only (the Uploading row is excluded)");
                    Check.Equal(2000L, Total(summary, "storage.originals"), "Originals");
                    Check.Equal(300L, Total(summary, "storage.imageVariants"), "Image variants");
                    Check.Equal(777L, Total(summary, "storage.videoRenditions"), "Measured renditions");
                    Check.Equal(3077L, Total(summary, "storage.total"), "Storage total is the sum of the classes");
                    Check.True(Total(summary, "interactions.new") == Total(summary, "reactions.new") + Total(summary, "pollVotes.new") + Total(summary, "follows.new") + Total(summary, "assistantMessages.new"), "Interactions are the sum of their parts");
                    Check.Equal(3L, Total(summary, "ai.requests"), "AI requests exclude test traffic");
                    Check.Equal(120L, Total(summary, "ai.promptTokens"), "Prompt tokens");
                    Check.Equal(4500L, Total(summary, "ai.costMicros"), "Cost");
                    Check.True(Total(summary, "users.active") >= 2, "Signed-in members are active (" + Total(summary, "users.active") + ")");
                    Check.True(Total(summary, "api.requests") >= 4, "API requests are recorded (" + Total(summary, "api.requests") + ")");
                    JsonElement users = Meter(summary, "users.new");
                    Check.Equal(24, users.GetProperty("values").GetArrayLength(), "One value per bucket");
                    Check.Equal(Total(summary, "users.new"), users.GetProperty("values").EnumerateArray().Sum(v => v.ValueKind == JsonValueKind.Null ? 0 : v.GetInt64()), "Hourly values sum to the total");
                    Check.Equal(JsonValueKind.Null, Meter(summary, "users.activeMonthly").GetProperty("values")[0].ValueKind, "Daily-only meters have no hourly values");
                    Check.True(Meter(summary, "users.activeMonthly").GetProperty("total").GetInt64() >= 2, "Monthly active members total from the daily row");
                    JsonElement month = await MetricEnumerationTestKit.UsageAsync(f, "/api/v1/admin/usage/summary?period=ThisMonth&meters=content,users.new", HttpFixture.AdminToken, ct).ConfigureAwait(false);
                    Check.Equal(5, month.GetProperty("meters").GetArrayLength(), "Meters filter");
                    Check.Equal(Total(summary, "posts.new"), Total(month, "posts.new"), "Daily rows agree with hourly rows");
                }),
                SuiteCases.Http(Id, "Rollup.IdempotentAndRecorded", "Running passes again never changes a value; recorded counters flush once, and a failed flush keeps them for the next", async (f, ct) =>
                {
                    ContentTestKit.Relax(f);
                    TestMember poster = await ContentTestKit.PosterAsync(f, ct).ConfigureAwait(false);
                    await ContentTestKit.PostIdAsync(f, poster.AccessToken, "Idempotent post", ct).ConfigureAwait(false);
                    await PassesAsync(f, ct).ConfigureAwait(false);
                    string before = await HashAsync(f, ct).ConfigureAwait(false);
                    await f.Services.Usage.RunPassAsync(DateTime.UtcNow, ct).ConfigureAwait(false);
                    await f.Services.Usage.RunPassAsync(DateTime.UtcNow, ct).ConfigureAwait(false);
                    Check.Equal(before, await HashAsync(f, ct).ConfigureAwait(false), "Repeated passes write the same values");

                    UsageRecorder recorder = new UsageRecorder();
                    DateTime hour = UsagePeriodCalculator.FloorHour(DateTime.UtcNow).AddHours(-3);
                    for (int i = 0; i < 5; i++) recorder.RecordRequest(i == 0 ? 503 : 200, hour.AddMinutes(i));
                    Check.Equal(2, await recorder.FlushAsync(f.Database, ct).ConfigureAwait(false), "Two cells flushed");
                    Check.Equal(0, await recorder.FlushAsync(f.Database, ct).ConfigureAwait(false), "A second flush adds nothing");
                    recorder.RecordRequest(200, hour);
                    await Check.ThrowsAsync<OperationCanceledException>(() => recorder.FlushAsync(f.Database, new CancellationToken(true)), "A cancelled flush throws").ConfigureAwait(false);
                    Check.Equal(1, recorder.Pending, "The counts stay pending");
                    await recorder.FlushAsync(f.Database, ct).ConfigureAwait(false);
                    DataTable rows = await f.Raw.ExecuteQueryAsync("SELECT meter, value FROM usagehourly WHERE propertyid = '" + f.Services.PropertyId + "' AND periodstartutc = '" + hour.ToString("o", CultureInfo.InvariantCulture) + "' ORDER BY meter;", false, ct).ConfigureAwait(false);
                    Check.Equal(6L, Value(rows, "api.requests"), "Requests counted once each");
                    Check.Equal(1L, Value(rows, "api.errors"), "Errors counted");
                    await f.Services.Usage.RunPassAsync(DateTime.UtcNow, ct).ConfigureAwait(false);
                    DataTable again = await f.Raw.ExecuteQueryAsync("SELECT meter, value FROM usagehourly WHERE propertyid = '" + f.Services.PropertyId + "' AND periodstartutc = '" + hour.ToString("o", CultureInfo.InvariantCulture) + "' ORDER BY meter;", false, ct).ConfigureAwait(false);
                    Check.Equal(6L, Value(again, "api.requests"), "A rollup pass never changes recorded hourly values");
                }),
                SuiteCases.Http(Id, "Backfill.HistoryAndClosedDays", "The first passes backfill daily rows from the oldest source row in the property time zone (25-hour DST day), close old days, and never change a closed day", async (f, ct) =>
                {
                    await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/property/settings", HttpFixture.AdminToken,
                        new { values = new Dictionary<string, object> { ["usage.timeZone"] = "America/New_York", ["usage.weekStartsOn"] = "Sunday" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    ApiResult bad = await Api.SendAsync(f, HttpMethod.Put, "/api/v1/admin/property/settings", HttpFixture.AdminToken, new { values = new Dictionary<string, object> { ["usage.timeZone"] = "Mars/Olympus" } }, ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.BadRequest, bad.Status, "An unknown zone is refused (" + bad.Text + ")");
                    TestMember early = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    TestMember late = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    string pid = f.Services.PropertyId;
                    // 00:30 EDT and 23:30 EST on 2025-11-02 (local), two different UTC days.
                    await f.Raw.ExecuteQueryAsync("UPDATE users SET createdutc = '2025-11-02T04:30:00Z' WHERE propertyid = '" + pid + "' AND id = '" + early.Id + "';", false, ct).ConfigureAwait(false);
                    await f.Raw.ExecuteQueryAsync("UPDATE users SET createdutc = '2025-11-03T04:30:00Z' WHERE propertyid = '" + pid + "' AND id = '" + late.Id + "';", false, ct).ConfigureAwait(false);
                    int passes = await PassesAsync(f, ct).ConfigureAwait(false);
                    Check.True(passes >= 4, "History of a year takes several bounded passes (" + passes + ")");
                    DataTable day = await f.Raw.ExecuteQueryAsync("SELECT value, closed, timezone, periodstartutc, periodendutc FROM usagedaily WHERE propertyid = '" + pid + "' AND meter = 'users.new' AND day = '2025-11-02';", false, ct).ConfigureAwait(false);
                    Check.Equal(1, day.Rows.Count, "The DST day has a row");
                    Check.Equal(2L, RowReader.GetLong(day.Rows[0], "value"), "Both members are on the local day");
                    Check.True(RowReader.GetBool(day.Rows[0], "closed"), "Old days are closed");
                    Check.Equal("America/New_York", RowReader.GetString(day.Rows[0], "timezone"), "Row records its zone");
                    Check.Equal(25.0, (RowReader.GetDateTime(day.Rows[0], "periodendutc") - RowReader.GetDateTime(day.Rows[0], "periodstartutc")).TotalHours, "25-hour local day");
                    JsonElement range = await MetricEnumerationTestKit.UsageAsync(f, "/api/v1/admin/usage/summary?period=Range&from=2025-11-02&to=2025-11-02&meters=users", HttpFixture.AdminToken, ct).ConfigureAwait(false);
                    Check.Equal(2L, Total(range, "users.new"), "Range total of the local day");
                    Check.Equal("Sunday", Api.Str(range, "weekStartsOn"), "Week start setting");
                    await f.Raw.ExecuteQueryAsync("UPDATE users SET createdutc = now() WHERE propertyid = '" + pid + "' AND id = '" + early.Id + "';", false, ct).ConfigureAwait(false);
                    await f.Services.Usage.RunPassAsync(DateTime.UtcNow, ct).ConfigureAwait(false);
                    DataTable frozen = await f.Raw.ExecuteQueryAsync("SELECT value FROM usagedaily WHERE propertyid = '" + pid + "' AND meter = 'users.new' AND day = '2025-11-02';", false, ct).ConfigureAwait(false);
                    Check.Equal(2L, RowReader.GetLong(frozen.Rows[0], "value"), "A closed day never changes");
                    JsonElement records = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/usage/enumerate", HttpFixture.AdminToken,
                        new { maxResults = 5, ordering = "CreatedAscending", filters = new { meter = "users.new", closed = "true" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    JsonElement first = records.GetProperty("objects")[0];
                    Check.Equal("2025-11-02", Api.Str(first, "day"), "Oldest record first");
                    Check.Equal("usd_20251102_users-new", Api.Str(first, "id"), "Deterministic id");
                    ContentTestKit.CheckMetadata(first, "Usage record");
                    Check.True(records.GetProperty("totalRecords").GetInt64() > 300, "Exact totals");
                    await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/metadata/UsageRecord/usd_20251102_users-new", HttpFixture.AdminToken, new { labels = new[] { "audited" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    JsonElement labeled = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/usage/enumerate", HttpFixture.AdminToken, new { labels = new[] { "audited" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(1, labeled.GetProperty("objects").GetArrayLength(), "Label filters on usage records");
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/usage/enumerate", HttpFixture.AdminToken, new { filters = new { meter = "nope" } }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                }),
                SuiteCases.Http(Id, "Routes.ValidationAccessAndExport", "Summary and export validate their queries, need an administrator, and export stable JSON and CSV files", async (f, ct) =>
                {
                    TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    await PassesAsync(f, ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/admin/usage/summary", member.AccessToken, null, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/admin/usage/summary?period=Fortnight", HttpFixture.AdminToken, null, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/admin/usage/summary?period=Range&from=2026-01-01", HttpFixture.AdminToken, null, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/admin/usage/summary?meters=bogus", HttpFixture.AdminToken, null, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/admin/usage/summary?period=LastYear&bucket=Hour", HttpFixture.AdminToken, null, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    JsonElement summary = await MetricEnumerationTestKit.UsageAsync(f, "/api/v1/admin/usage/summary?period=LastWeek&bucket=Hour", HttpFixture.AdminToken, ct).ConfigureAwait(false);
                    Check.True(summary.GetProperty("buckets").GetArrayLength() > 144, "Hour override");
                    Check.True(summary.GetProperty("backfillComplete").GetBoolean(), "Backfill complete");
                    Check.True(summary.GetProperty("dataThroughUtc").ValueKind == JsonValueKind.String, "Data through time");

                    using (HttpRequestMessage request = f.Request(HttpMethod.Get, "/api/v1/admin/usage/export?period=LastWeek", true))
                    using (HttpResponseMessage response = await f.Client.SendAsync(request, ct).ConfigureAwait(false))
                    {
                        Check.Equal(HttpStatusCode.OK, response.StatusCode, "JSON export");
                        Check.True(response.Content.Headers.ContentDisposition?.FileName?.Contains("usage-" + f.Services.PropertyId, StringComparison.Ordinal) == true, "Attachment name");
                        JsonElement doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false)).RootElement;
                        Check.Equal(1, doc.GetProperty("schemaVersion").GetInt32(), "Schema version");
                        Check.Equal(35, doc.GetProperty("totals").GetArrayLength(), "Totals for every meter");
                        Check.True(doc.GetProperty("rows").GetArrayLength() >= 35, "Daily rows since metering started");
                        Check.True(!doc.GetProperty("complete").GetBoolean(), "Open days make the export incomplete");
                    }
                    using (HttpRequestMessage request = f.Request(HttpMethod.Get, "/api/v1/admin/usage/export?period=LastDay&format=csv&granularity=Hour&meters=users", true))
                    using (HttpResponseMessage response = await f.Client.SendAsync(request, ct).ConfigureAwait(false))
                    {
                        Check.Equal(HttpStatusCode.OK, response.StatusCode, "CSV export");
                        string csv = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                        Check.True(csv.StartsWith("propertyId,meter,kind,unit,granularity,day,periodStartUtc,periodEndUtc,value,closed,timeZone", StringComparison.Ordinal), "CSV header");
                        Check.True(csv.Contains(",users.new,Count,count,Hour,", StringComparison.Ordinal), "Hourly rows");
                    }
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/admin/usage/export?period=Last90Days&granularity=Hour", HttpFixture.AdminToken, null, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/admin/usage/export?format=xml", HttpFixture.AdminToken, null, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                }),
                SuiteCases.Http(Id, "Platform.IsolationAndComparison", "Two properties: each property's usage counts only its own rows; the platform summary, records, export, and comparison table read any property and order by each meter", MultiPropertyKit.Multi, async (f, ct) =>
                {
                    string platform = await MultiPropertyKit.PlatformLoginAsync(f, ct).ConfigureAwait(false);
                    string b = await MultiPropertyKit.CreateSecondAsync(f, platform, ct).ConfigureAwait(false);
                    string a = TestProperty.SampleId;
                    f.PropertyId = a;
                    List<string> aMembers = new List<string>();
                    for (int i = 0; i < 3; i++) aMembers.Add((await Api.RegisterAsync(f, ct).ConfigureAwait(false)).Id);
                    f.PropertyId = b;
                    await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    f.PropertyId = a;
                    await PassesAsync(f, ct).ConfigureAwait(false);
                    f.PropertyId = b;
                    await PassesAsync(f, ct).ConfigureAwait(false);
                    JsonElement bSummary = await MetricEnumerationTestKit.UsageAsync(f, "/api/v1/admin/usage/summary?period=LastDay&meters=users", HttpFixture.AdminToken, ct).ConfigureAwait(false);
                    Check.Equal(1L, Total(bSummary, "users.new"), "B counts only its own member");
                    JsonElement bRecords = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/usage/enumerate", HttpFixture.AdminToken, new { maxResults = 500 }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    string bText = bRecords.GetRawText();
                    Check.True(!bText.Contains(a, StringComparison.Ordinal) && aMembers.All(m => !bText.Contains(m, StringComparison.Ordinal)), "B's records never name A");
                    f.PropertyId = a;
                    JsonElement aSummary = await MetricEnumerationTestKit.UsageAsync(f, "/api/v1/admin/usage/summary?period=LastDay&meters=users", HttpFixture.AdminToken, ct).ConfigureAwait(false);
                    Check.Equal(3L, Total(aSummary, "users.new"), "A counts only its own members");
                    DataTable stray = await f.Raw.ExecuteQueryAsync("SELECT COUNT(*) AS cnt FROM usagedaily WHERE propertyid NOT IN ('" + a + "', '" + b + "');", false, ct).ConfigureAwait(false);
                    Check.Equal(0L, RowReader.GetLong(stray.Rows[0], "cnt"), "Every usage row belongs to a property");

                    JsonElement platformSummary = await MetricEnumerationTestKit.UsageAsync(f, "/api/v1/platform/properties/" + b + "/usage/summary?period=LastDay&meters=users", platform, ct).ConfigureAwait(false);
                    Check.Equal(1L, Total(platformSummary, "users.new"), "Platform summary of B");
                    Check.Equal(b, Api.Str(platformSummary, "propertyId"), "Summary names its property");
                    JsonElement platformRecords = await MultiPropertyKit.PlatformExpectAsync(f, HttpMethod.Post, "/api/v1/platform/properties/" + a + "/usage/enumerate", platform, new { maxResults = 3 }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(3, platformRecords.GetProperty("objects").GetArrayLength(), "Platform records page");
                    ApiResult export = await MultiPropertyKit.PlatformAsync(f, HttpMethod.Get, "/api/v1/platform/properties/" + a + "/usage/export?period=LastDay&format=csv", platform, null, ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.OK, export.Status, "Platform export");
                    Check.True(export.Text.Contains(a, StringComparison.Ordinal) && !export.Text.Contains(b, StringComparison.Ordinal), "Export holds only that property");
                    await MultiPropertyKit.PlatformExpectAsync(f, HttpMethod.Get, "/api/v1/platform/properties/app_unknown/usage/summary", platform, null, HttpStatusCode.NotFound, ct).ConfigureAwait(false);
                    await MultiPropertyKit.PlatformExpectAsync(f, HttpMethod.Get, "/api/v1/platform/properties/" + a + "/usage/summary", null, null, HttpStatusCode.Unauthorized, ct).ConfigureAwait(false);

                    JsonElement table = await MultiPropertyKit.PlatformExpectAsync(f, HttpMethod.Post, "/api/v1/platform/usage/properties/enumerate", platform,
                        new { ordering = "UsersDescending", filters = new { period = "LastDay" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(2, table.GetProperty("objects").GetArrayLength(), "One row per property");
                    Check.Equal(a, Api.Str(table.GetProperty("objects")[0], "propertyId"), "Ordered by members");
                    Check.Equal(3L, table.GetProperty("objects")[0].GetProperty("usersNew").GetInt64(), "Row totals");
                    ContentTestKit.CheckMetadata(table.GetProperty("objects")[0], "Usage row");
                    JsonElement paged = await MultiPropertyKit.PlatformExpectAsync(f, HttpMethod.Post, "/api/v1/platform/usage/properties/enumerate", platform,
                        new { ordering = "UsersDescending", maxResults = 1, skip = 1 }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(b, Api.Str(paged.GetProperty("objects")[0], "propertyId"), "Computed orderings page with skip");
                    JsonElement firstComputed = await MultiPropertyKit.PlatformExpectAsync(f, HttpMethod.Post, "/api/v1/platform/usage/properties/enumerate", platform,
                        new { ordering = "UsersDescending", maxResults = 1 }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    string computedCursor = Api.Str(firstComputed, "continuationToken");
                    Check.True(!String.IsNullOrEmpty(computedCursor), "Computed usage ordering provides a real continuation token");
                    JsonElement nextComputed = await MultiPropertyKit.PlatformExpectAsync(f, HttpMethod.Post, "/api/v1/platform/usage/properties/enumerate", platform,
                        new { ordering = "UsersDescending", maxResults = 1, continuationToken = computedCursor }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(b, Api.Str(nextComputed.GetProperty("objects")[0], "propertyId"), "Computed usage cursor advances to the second property");
                    Check.Equal(2L, nextComputed.GetProperty("totalRecords").GetInt64(), "Computed usage cursor retains exact total");
                    JsonElement labelled = await MultiPropertyKit.PlatformExpectAsync(f, HttpMethod.Post, "/api/v1/platform/usage/properties/enumerate", platform,
                        new { labels = new[] { "test-fixture" }, filters = new { period = "LastDay" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(b, String.Join(",", labelled.GetProperty("objects").EnumerateArray().Select(o => Api.Str(o, "propertyId"))), "Label filter on the usage table");
                    JsonElement labelledComputed = await MultiPropertyKit.PlatformExpectAsync(f, HttpMethod.Post, "/api/v1/platform/usage/properties/enumerate", platform,
                        new { ordering = "UsersDescending", labels = new[] { "test-fixture" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(b, String.Join(",", labelledComputed.GetProperty("objects").EnumerateArray().Select(o => Api.Str(o, "propertyId"))), "Label filter with a computed ordering");
                    JsonElement tagged = await MultiPropertyKit.PlatformExpectAsync(f, HttpMethod.Post, "/api/v1/platform/usage/properties/enumerate", platform,
                        new { tags = new Dictionary<string, string> { ["tier"] = "none-such" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(0, tagged.GetProperty("objects").GetArrayLength(), "Tag filter on the usage table");
                    ApiResult invalid = await MultiPropertyKit.PlatformAsync(f, HttpMethod.Post, "/api/v1/platform/usage/properties/enumerate", platform, new { filters = new { period = "Sometime" } }, ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.BadRequest, invalid.Status, "Bad period filter");
                    foreach (string ordering in TrellisKit.Core.Enumeration.UsageCollections.ComparisonOrderings)
                        await MultiPropertyKit.PlatformExpectAsync(f, HttpMethod.Post, "/api/v1/platform/usage/properties/enumerate", platform, new { ordering }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                })
            });
        }

        #endregion

        #region Private-Methods

        private static UsageWindow Resolve(UsagePeriodEnum period, DateTime now, UsageZone zone)
        {
            return UsagePeriodCalculator.Resolve(period, null, null, null, now, zone, 90);
        }

        private static void Field(Action action, string field)
        {
            ApiErrorException e = Check.Throws<ApiErrorException>(action, "Validation of " + field);
            Check.True(e.FieldErrors != null && e.FieldErrors.ContainsKey(field), "Field " + field + " named (" + String.Join(", ", e.FieldErrors?.Keys ?? Enumerable.Empty<string>()) + ")");
        }

        private static async Task<int> PassesAsync(HttpFixture f, CancellationToken ct)
        {
            int passes = 0;
            await f.Services.Usage.FlushAsync(ct).ConfigureAwait(false);
            for (; passes < 20; passes++)
            {
                await f.Services.Usage.RunPassAsync(DateTime.UtcNow, ct).ConfigureAwait(false);
                UsageState? state = await f.Database.Usage.ReadStateAsync(ct).ConfigureAwait(false);
                DateTime window = UsagePeriodCalculator.FloorHour(DateTime.UtcNow.AddHours(-f.Settings.Usage.CloseAfterHours));
                if (state != null && state.BackfilledUtc != null && state.HourlyCursorUtc >= window) return passes + 1;
            }
            throw new InvalidOperationException("The usage backfill did not finish in 20 passes.");
        }

        private static async Task<string> HashAsync(HttpFixture f, CancellationToken ct)
        {
            string pid = f.Services.PropertyId;
            DataTable table = await f.Raw.ExecuteQueryAsync("SELECT (SELECT COALESCE(md5(string_agg(meter || day || value || closed, ',' ORDER BY meter, day)), '') FROM usagedaily WHERE propertyid = '" + pid + "') || " +
                "(SELECT COALESCE(md5(string_agg(meter || periodstartutc || value, ',' ORDER BY meter, periodstartutc)), '') FROM usagehourly WHERE propertyid = '" + pid + "') AS h;", false, ct).ConfigureAwait(false);
            return RowReader.GetString(table.Rows[0], "h");
        }

        private static JsonElement Meter(JsonElement summary, string meter)
        {
            foreach (JsonElement m in summary.GetProperty("meters").EnumerateArray())
                if (Api.Str(m, "meter") == meter) return m;
            throw new InvalidOperationException("Meter " + meter + " missing from the summary.");
        }

        private static long Total(JsonElement summary, string meter)
        {
            return Meter(summary, meter).GetProperty("total").GetInt64();
        }

        private static long Value(DataTable rows, string meter)
        {
            foreach (DataRow row in rows.Rows)
                if (RowReader.GetString(row, "meter") == meter) return RowReader.GetLong(row, "value");
            return 0;
        }

        private static async Task Same(HttpFixture f, JsonElement summary, string meter, string sql, CancellationToken ct)
        {
            long expected = await ContentTestKit.ScalarAsync(f, sql + ";", ct).ConfigureAwait(false);
            Check.True(expected > 0, meter + ": the fixture created source rows");
            Check.Equal(expected, Total(summary, meter), meter + " equals its source rows");
        }

        #endregion
    }
}

namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Touchstone.Core;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;
    using TrellisKit.Core.Settings;

    /// <summary>
    /// The shared enumeration infrastructure (docs/ENUMERATION_AND_METADATA.md section 2): normalizer rules (defaults,
    /// clamping, validation, staff filters, continuation tokens) and, through the converted admin sign-up code list,
    /// keyset continuation, stability under inserts, skip, every ordering, search, the created window, label and tag
    /// filters, exact admin totals, and the bounded CSV export.
    /// </summary>
    public static class EnumerationSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "Enumeration";

        #endregion

        #region Private-Members

        private const string _Codes = "/api/v1/admin/signup-codes";

        private static readonly EnumerationCollection _Member = new EnumerationCollection("test-member", TargetTypeEnum.FeedPost, "t.id", "t.createdutc", false)
            .Order("NameAscending", "t.name", false, EnumerationKeyKindEnum.Text).Filters("state");

        private static readonly EnumerationCollection _Admin = new EnumerationCollection("test-admin", TargetTypeEnum.User, "t.id", "t.createdutc", true).Filters("state");

        private static readonly EnumerationCollection _Plain = new EnumerationCollection("test-plain", null, "t.id", "t.createdutc", false);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Enumeration (" + TestEnvironment.Provider + ")", new[]
            {
                SuiteCases.Unit(Id, "Catalog.CursorFiltersAndCounts", "Bounded catalogs obey page limits, created windows, metadata, exact totals and scoped stable cursors", CatalogAsync),
                Unit("Normalize.DefaultsAndClamping", "Default 25; member maximum 100, admin 500, hard cap 1000 even when settings say more; minimum 1", () =>
                {
                    EnumerationSettings settings = new EnumerationSettings();
                    Check.Equal(25, EnumerationNormalizer.Normalize(null, _Member, settings, false).MaxResults, "Default");
                    Check.Equal("CreatedDescending", EnumerationNormalizer.Normalize(null, _Member, settings, false).Ordering.Name, "Default ordering");
                    Check.Equal(100, EnumerationNormalizer.Normalize(new EnumerationQuery { MaxResults = 5000 }, _Member, settings, false).MaxResults, "Member clamp");
                    Check.Equal(500, EnumerationNormalizer.Normalize(new EnumerationQuery { MaxResults = 5000 }, _Admin, settings, true).MaxResults, "Admin clamp");
                    Check.Equal(1, EnumerationNormalizer.Normalize(new EnumerationQuery { MaxResults = 0 }, _Member, settings, false).MaxResults, "Minimum 1");
                    Check.Equal(1, EnumerationNormalizer.Normalize(new EnumerationQuery { MaxResults = -7 }, _Admin, settings, true).MaxResults, "Negative becomes 1");
                    EnumerationSettings huge = new EnumerationSettings { MaxResultsAdmin = 50000, MaxResultsMember = 50000 };
                    Check.Equal(EnumerationSettings.HardMaximum, huge.MaxResultsAdmin, "Settings are capped at 1000");
                    Check.Equal(1000, EnumerationNormalizer.Normalize(new EnumerationQuery { MaxResults = 5000 }, _Admin, huge, true).MaxResults, "Hard cap (admin)");
                    Check.Equal(1000, EnumerationNormalizer.Normalize(new EnumerationQuery { MaxResults = 5000 }, _Member, huge, false).MaxResults, "Hard cap (member)");
                    Check.Equal(20, EnumerationNormalizer.Normalize(new EnumerationQuery { MaxResults = 50 }, _Member, settings, false, "", 20).MaxResults, "Route cap");
                    Check.True(EnumerationNormalizer.Normalize(null, _Admin, new EnumerationSettings { CountMemberTotals = false }, true).CountTotals, "Admin totals are always counted");
                    Check.True(!EnumerationNormalizer.Normalize(null, _Member, new EnumerationSettings { CountMemberTotals = false }, false).CountTotals, "Member totals follow the setting");
                }),
                Unit("Normalize.Validation", "Skip bounds, unknown ordering, unknown filter, too many labels or tags, long search, and an inverted window are 400 ValidationFailed", () =>
                {
                    EnumerationSettings settings = new EnumerationSettings();
                    Check.Equal(100000, EnumerationNormalizer.Normalize(new EnumerationQuery { Skip = 100000 }, _Admin, settings, true).Skip, "Largest skip");
                    Invalid(() => EnumerationNormalizer.Normalize(new EnumerationQuery { Skip = -1 }, _Admin, settings, true), "skip", "Negative skip");
                    Invalid(() => EnumerationNormalizer.Normalize(new EnumerationQuery { Skip = 100001 }, _Admin, settings, true), "skip", "Skip above 100000");
                    Invalid(() => EnumerationNormalizer.Normalize(new EnumerationQuery { Ordering = "Sideways" }, _Member, settings, false), "ordering", "Unknown ordering");
                    Check.Equal("NameAscending", EnumerationNormalizer.Normalize(new EnumerationQuery { Ordering = "nameascending" }, _Member, settings, false).Ordering.Name, "Ordering names are case-insensitive");
                    EnumerationQuery unknown = new EnumerationQuery();
                    unknown.SetFilter("color", "red");
                    Invalid(() => EnumerationNormalizer.Normalize(unknown, _Member, settings, false), "filters.color", "Unknown filter");
                    EnumerationQuery known = new EnumerationQuery();
                    known.SetFilter("STATE", "x");
                    EnumerationNormalizer.Normalize(known, _Member, settings, false);
                    Invalid(() => EnumerationNormalizer.Normalize(new EnumerationQuery { Labels = Enumerable.Range(0, 21).Select(i => "l" + i).ToList() }, _Member, settings, false), "labels", "21 labels");
                    EnumerationNormalizer.Normalize(new EnumerationQuery { Labels = Enumerable.Range(0, 20).Select(i => "l" + i).ToList() }, _Member, settings, false);
                    Invalid(() => EnumerationNormalizer.Normalize(new EnumerationQuery { Tags = Enumerable.Range(0, 21).ToDictionary(i => "k" + i, i => "v") }, _Member, settings, false), "tags", "21 tags");
                    Invalid(() => EnumerationNormalizer.Normalize(new EnumerationQuery { Labels = new List<string> { "bad!label" } }, _Member, settings, false), "labels[0]", "Invalid label characters");
                    Invalid(() => EnumerationNormalizer.Normalize(new EnumerationQuery { Labels = new List<string> { "a" } }, _Plain, settings, false), "labels", "A list without metadata rejects label filters");
                    Invalid(() => EnumerationNormalizer.Normalize(new EnumerationQuery { Search = new string('s', 201) }, _Member, settings, false), "search", "Search too long");
                    Check.True(EnumerationNormalizer.Normalize(new EnumerationQuery { Search = "   " }, _Member, settings, false).Query.Search == null, "Blank search is ignored");
                    DateTime now = DateTime.UtcNow;
                    Invalid(() => EnumerationNormalizer.Normalize(new EnumerationQuery { CreatedAfterUtc = now, CreatedBeforeUtc = now.AddMinutes(-1) }, _Member, settings, false), "createdBeforeUtc", "Inverted window");
                    Invalid(() => EnumerationNormalizer.Normalize(new EnumerationQuery { CreatedAfterUtc = now, CreatedBeforeUtc = now }, _Member, settings, false), "createdBeforeUtc", "Empty window");
                }),
                Unit("Normalize.StaffFilters", "staff: label and tag filters are 400 for non-staff and allowed for staff", () =>
                {
                    EnumerationSettings settings = new EnumerationSettings();
                    Invalid(() => EnumerationNormalizer.Normalize(new EnumerationQuery { Labels = new List<string> { "Staff:Review" } }, _Member, settings, false), "labels[0]", "Staff label by a member");
                    Invalid(() => EnumerationNormalizer.Normalize(new EnumerationQuery { Tags = new Dictionary<string, string> { { "staff:queue", "x" } } }, _Member, settings, false), "tags.staff:queue", "Staff tag by a member");
                    EnumerationRequest staff = EnumerationNormalizer.Normalize(new EnumerationQuery { Labels = new List<string> { "staff:review" }, Tags = new Dictionary<string, string> { { "staff:queue", "*" } } }, _Member, settings, true);
                    Check.True(staff.IncludeStaffMetadata, "Staff may filter on staff metadata");
                }),
                Unit("Normalize.ContinuationTokens", "Tokens from another filter set, scope, or collection and tampered tokens are 400 InvalidContinuationToken; a valid token wins over ordering and skip", () =>
                {
                    EnumerationSettings settings = new EnumerationSettings();
                    EnumerationQuery first = new EnumerationQuery { Ordering = "NameAscending", Labels = new List<string> { "a" } };
                    first.SetFilter("state", "Live");
                    EnumerationRequest request = EnumerationNormalizer.Normalize(first, _Member, settings, false, "parent-1");
                    string token = ContinuationTokens.Encode(request.Fingerprint, "NameAscending", "bob", "fpo_1");

                    EnumerationQuery next = new EnumerationQuery { Ordering = "CreatedAscending", Skip = 40, ContinuationToken = token, Labels = new List<string> { "A" } };
                    next.SetFilter("state", "Live");
                    EnumerationRequest resumed = EnumerationNormalizer.Normalize(next, _Member, settings, false, "parent-1");
                    Check.Equal("NameAscending", resumed.Ordering.Name, "Token ordering wins");
                    Check.Equal(0, resumed.Skip, "Token ignores skip");
                    Check.True(resumed.HasPosition && resumed.AfterKey == "bob" && resumed.AfterId == "fpo_1", "Token position");

                    EnumerationQuery otherFilter = new EnumerationQuery { ContinuationToken = token, Labels = new List<string> { "a" } };
                    otherFilter.SetFilter("state", "Draft");
                    InvalidToken(() => EnumerationNormalizer.Normalize(otherFilter, _Member, settings, false, "parent-1"), "Another filter value");
                    InvalidToken(() => EnumerationNormalizer.Normalize(new EnumerationQuery { ContinuationToken = token, Labels = new List<string> { "a" } }, _Member, settings, false, "parent-1"), "Missing filter");
                    EnumerationQuery otherScope = new EnumerationQuery { ContinuationToken = token, Labels = new List<string> { "a" } };
                    otherScope.SetFilter("state", "Live");
                    InvalidToken(() => EnumerationNormalizer.Normalize(otherScope, _Member, settings, false, "parent-2"), "Another scope");
                    EnumerationQuery otherCollection = new EnumerationQuery { ContinuationToken = token };
                    otherCollection.SetFilter("state", "Live");
                    InvalidToken(() => EnumerationNormalizer.Normalize(otherCollection, _Admin, settings, true, "parent-1"), "Another collection");
                    string tampered = token.Substring(0, 20) + (token[20] == 'A' ? "B" : "A") + token.Substring(21);
                    EnumerationQuery tamperedQuery = new EnumerationQuery { ContinuationToken = tampered, Labels = new List<string> { "a" } };
                    tamperedQuery.SetFilter("state", "Live");
                    InvalidToken(() => EnumerationNormalizer.Normalize(tamperedQuery, _Member, settings, false, "parent-1"), "Tampered token");
                    InvalidToken(() => EnumerationNormalizer.Normalize(new EnumerationQuery { ContinuationToken = "not a token" }, _Member, settings, false), "Garbage token");
                    InvalidToken(() => EnumerationNormalizer.Normalize(new EnumerationQuery { ContinuationToken = new string('x', 5000) }, _Member, settings, false), "Oversized token");
                    string badOrdering = ContinuationTokens.Encode(EnumerationNormalizer.Normalize(null, _Member, settings, false).Fingerprint, "Sideways", null, "fpo_1");
                    InvalidToken(() => EnumerationNormalizer.Normalize(new EnumerationQuery { ContinuationToken = badOrdering }, _Member, settings, false), "Token with an unknown ordering");
                }),
                Http("Continuation.EveryRowOnce", "Walking every page by token (GET and POST) returns each row exactly once, newest first, with exact admin totals", async (f, ct) =>
                {
                    List<SignupCode> codes = await SeedAsync(f, "walk", 23, ct).ConfigureAwait(false);
                    List<string> expected = codes.OrderByDescending(c => c.CreatedUtc).Select(c => c.Id).ToList();
                    List<string> viaGet = await WalkGetAsync(f, _Codes + "?labels=walk&maxResults=5", 23, ct).ConfigureAwait(false);
                    Check.True(viaGet.SequenceEqual(expected), "GET walk: every row once, newest first");
                    List<string> viaPost = await WalkPostAsync(f, _Codes + "/enumerate", new Dictionary<string, object?> { { "maxResults", 5 }, { "labels", new[] { "walk" } } }, 23, ct).ConfigureAwait(false);
                    Check.True(viaPost.SequenceEqual(expected), "POST walk matches GET");
                    JsonElement big = await GetAsync(f, _Codes + "?labels=walk&maxResults=5000", ct).ConfigureAwait(false);
                    Check.Equal(500, big.GetProperty("maxResults").GetInt32(), "Admin maximum 500");
                    Check.True(big.GetProperty("endOfResults").GetBoolean() && big.GetProperty("continuationToken").ValueKind == JsonValueKind.Null, "No token at the end");
                    Check.True(big.GetProperty("success").GetBoolean() && big.TryGetProperty("timestampUtc", out JsonElement _) && big.GetProperty("skip").GetInt32() == 0, "Result envelope");
                }),
                Http("Continuation.StableUnderInserts", "Rows inserted between pages never duplicate or skip the rows already being walked", async (f, ct) =>
                {
                    List<SignupCode> codes = await SeedAsync(f, "stable", 12, ct).ConfigureAwait(false);
                    List<string> seen = new List<string>();
                    string? token = null;
                    int pages = 0;
                    int inserted = 0;
                    do
                    {
                        JsonElement page = await GetAsync(f, _Codes + "?labels=stable&maxResults=4" + (token == null ? String.Empty : "&continuationToken=" + Uri.EscapeDataString(token)), ct).ConfigureAwait(false);
                        seen.AddRange(page.GetProperty("objects").EnumerateArray().Select(o => Api.Str(o, "id")));
                        token = page.GetProperty("continuationToken").ValueKind == JsonValueKind.String ? page.GetProperty("continuationToken").GetString() : null;
                        await SeedAsync(f, "stable", 2, ct, DateTime.UtcNow.AddMinutes(10 + pages)).ConfigureAwait(false);
                        inserted += 2;
                        pages++;
                    }
                    while (token != null && pages < 20);
                    Check.Equal(seen.Count, seen.Distinct().Count(), "No duplicates");
                    Check.True(codes.Select(c => c.Id).All(seen.Contains), "No original row skipped");
                    Check.Equal(12, seen.Count, "Newer rows inserted ahead of the position are not returned in this walk");

                    List<string> ascending = new List<string>();
                    token = null;
                    pages = 0;
                    do
                    {
                        JsonElement page = await GetAsync(f, _Codes + "?labels=stable&maxResults=5&ordering=CreatedAscending" + (token == null ? String.Empty : "&continuationToken=" + Uri.EscapeDataString(token)), ct).ConfigureAwait(false);
                        ascending.AddRange(page.GetProperty("objects").EnumerateArray().Select(o => Api.Str(o, "id")));
                        token = page.GetProperty("continuationToken").ValueKind == JsonValueKind.String ? page.GetProperty("continuationToken").GetString() : null;
                        if (pages == 0) await SeedAsync(f, "stable", 3, ct, DateTime.UtcNow.AddHours(5)).ConfigureAwait(false);
                        pages++;
                    }
                    while (token != null && pages < 30);
                    Check.Equal(ascending.Count, ascending.Distinct().Count(), "Ascending: no duplicates");
                    Check.Equal(12 + inserted + 3, ascending.Count, "Ascending: rows appended after the position are reached");
                }),
                Http("Skip.JumpsToAPage", "skip jumps into the list (admin tables), is ignored with a token, and past the end is empty", async (f, ct) =>
                {
                    List<SignupCode> codes = await SeedAsync(f, "skip", 14, ct).ConfigureAwait(false);
                    List<string> expected = codes.OrderByDescending(c => c.CreatedUtc).Select(c => c.Id).ToList();
                    JsonElement page = await GetAsync(f, _Codes + "?labels=skip&maxResults=5&skip=10", ct).ConfigureAwait(false);
                    Check.True(page.GetProperty("objects").EnumerateArray().Select(o => Api.Str(o, "id")).SequenceEqual(expected.Skip(10)), "Rows 11 to 14");
                    Check.Equal(10, page.GetProperty("skip").GetInt32(), "Skip echoed");
                    Check.True(page.GetProperty("endOfResults").GetBoolean() && page.GetProperty("recordsRemaining").GetInt64() == 0, "Last page");
                    JsonElement middle = await GetAsync(f, _Codes + "?labels=skip&maxResults=3&skip=3", ct).ConfigureAwait(false);
                    Check.Equal(8L, middle.GetProperty("recordsRemaining").GetInt64(), "Remaining after a skipped page");
                    string token = middle.GetProperty("continuationToken").GetString()!;
                    JsonElement resumed = await GetAsync(f, _Codes + "?labels=skip&maxResults=3&skip=12&continuationToken=" + Uri.EscapeDataString(token), ct).ConfigureAwait(false);
                    Check.Equal(expected[6], Api.Str(resumed.GetProperty("objects")[0], "id"), "Token wins over skip");
                    JsonElement past = await GetAsync(f, _Codes + "?labels=skip&skip=500", ct).ConfigureAwait(false);
                    Check.True(past.GetProperty("objects").GetArrayLength() == 0 && past.GetProperty("endOfResults").GetBoolean() && past.GetProperty("totalRecords").GetInt64() == 14, "Past the end");
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, _Codes + "?skip=100001", HttpFixture.AdminToken, null, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, _Codes + "?maxResults=lots", HttpFixture.AdminToken, null, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                }),
                Http("Orderings.Every", "Every ordering of the sign-up code list sorts by its key and walks by token in the same order as one large page", async (f, ct) =>
                {
                    List<SignupCode> codes = await SeedAsync(f, "order", 9, ct).ConfigureAwait(false);
                    Dictionary<string, List<string>> expected = new Dictionary<string, List<string>>(StringComparer.Ordinal)
                    {
                        { "CreatedDescending", codes.OrderByDescending(c => c.CreatedUtc).Select(c => c.Id).ToList() },
                        { "CreatedAscending", codes.OrderBy(c => c.CreatedUtc).Select(c => c.Id).ToList() },
                        { "CodeAscending", codes.OrderBy(c => c.Code, StringComparer.Ordinal).Select(c => c.Id).ToList() },
                        { "NameAscending", codes.OrderBy(c => c.Name.ToLowerInvariant(), StringComparer.Ordinal).Select(c => c.Id).ToList() },
                        { "RedemptionCountDescending", codes.OrderByDescending(c => c.RedemptionCount).Select(c => c.Id).ToList() },
                        { "LastRedeemedDescending", codes.OrderByDescending(c => c.LastRedeemedUtc).Select(c => c.Id).ToList() },
                        { "ExpiresAscending", codes.OrderBy(c => c.ExpiresUtc).Select(c => c.Id).ToList() }
                    };
                    Check.True(PhaseOneCollections.SignupCodes.Orderings.Keys.All(expected.ContainsKey), "Every ordering is covered");
                    foreach (KeyValuePair<string, List<string>> ordering in expected)
                    {
                        List<string> walked = await WalkGetAsync(f, _Codes + "?labels=order&maxResults=2&ordering=" + ordering.Key, 9, ct).ConfigureAwait(false);
                        Check.True(walked.SequenceEqual(ordering.Value), ordering.Key + " walk order");
                        JsonElement single = await GetAsync(f, _Codes + "?labels=order&maxResults=100&ordering=" + ordering.Key, ct).ConfigureAwait(false);
                        Check.True(single.GetProperty("objects").EnumerateArray().Select(o => Api.Str(o, "id")).SequenceEqual(walked), ordering.Key + " single page matches the walk");
                    }
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, _Codes + "?ordering=Sideways", HttpFixture.AdminToken, null, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    JsonElement members = await GetAsync(f, "/api/v1/admin/members?ordering=DisplayNameAscending", ct).ConfigureAwait(false);
                    Check.True(members.GetProperty("objects").GetArrayLength() >= 0, "Member orderings accepted");
                    foreach (string ordering in PhaseOneCollections.AdminMembers.Orderings.Keys) await GetAsync(f, "/api/v1/admin/members?ordering=" + ordering, ct).ConfigureAwait(false);
                }),
                Http("Search.AndWindow", "search matches the documented fields case-insensitively and the created window bounds the list", async (f, ct) =>
                {
                    DateTime origin = new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);
                    List<SignupCode> codes = await SeedAsync(f, "window", 6, ct, origin).ConfigureAwait(false);
                    JsonElement byName = await GetAsync(f, _Codes + "?labels=window&search=" + Uri.EscapeDataString(codes[2].Name.ToUpperInvariant()), ct).ConfigureAwait(false);
                    Check.Equal(1L, byName.GetProperty("totalRecords").GetInt64(), "Search by name, any case");
                    JsonElement byCode = await GetAsync(f, _Codes + "?labels=window&search=" + Uri.EscapeDataString(codes[4].Code.Substring(2).ToLowerInvariant()), ct).ConfigureAwait(false);
                    Check.Equal(codes[4].Id, Api.Str(byCode.GetProperty("objects")[0], "id"), "Search by code");
                    JsonElement none = await GetAsync(f, _Codes + "?labels=window&search=%25", ct).ConfigureAwait(false);
                    Check.Equal(0L, none.GetProperty("totalRecords").GetInt64(), "LIKE wildcards are literal");
                    string after = Uri.EscapeDataString(codes[1].CreatedUtc.ToString("o", CultureInfo.InvariantCulture));
                    string before = Uri.EscapeDataString(codes[4].CreatedUtc.ToString("o", CultureInfo.InvariantCulture));
                    JsonElement window = await GetAsync(f, _Codes + "?labels=window&createdAfterUtc=" + after + "&createdBeforeUtc=" + before, ct).ConfigureAwait(false);
                    Check.Equal(3L, window.GetProperty("totalRecords").GetInt64(), "After is inclusive, before is exclusive");
                    JsonElement post = await PostAsync(f, _Codes + "/enumerate", new Dictionary<string, object?> { { "labels", new[] { "window" } }, { "createdAfterUtc", codes[3].CreatedUtc } }, ct).ConfigureAwait(false);
                    Check.Equal(3L, post.GetProperty("totalRecords").GetInt64(), "Window in the POST body");
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, _Codes + "?createdAfterUtc=" + before + "&createdBeforeUtc=" + after, HttpFixture.AdminToken, null, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, _Codes + "?search=" + new string('q', 201), HttpFixture.AdminToken, null, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                }),
                Http("Labels.AllAndAny", "Label filters match All (default) or Any, case-insensitively, from repeated or comma separated parameters and the POST body", async (f, ct) =>
                {
                    List<SignupCode> codes = await SeedAsync(f, "lab", 4, ct).ConfigureAwait(false);
                    await f.Database.Metadata.WriteAsync(TargetTypeEnum.SignupCode, codes[0].Id, new ObjectMetadata { Labels = new List<string> { "lab", "Red", "Blue" } }, ct).ConfigureAwait(false);
                    await f.Database.Metadata.WriteAsync(TargetTypeEnum.SignupCode, codes[1].Id, new ObjectMetadata { Labels = new List<string> { "lab", "red" } }, ct).ConfigureAwait(false);
                    await f.Database.Metadata.WriteAsync(TargetTypeEnum.SignupCode, codes[2].Id, new ObjectMetadata { Labels = new List<string> { "lab", "blue" } }, ct).ConfigureAwait(false);
                    Check.Equal(1L, (await GetAsync(f, _Codes + "?labels=red&labels=BLUE", ct).ConfigureAwait(false)).GetProperty("totalRecords").GetInt64(), "All (repeated)");
                    Check.Equal(1L, (await GetAsync(f, _Codes + "?labels=red,blue", ct).ConfigureAwait(false)).GetProperty("totalRecords").GetInt64(), "All (comma separated)");
                    Check.Equal(3L, (await GetAsync(f, _Codes + "?labels=red&labels=blue&labelMatch=Any", ct).ConfigureAwait(false)).GetProperty("totalRecords").GetInt64(), "Any");
                    Check.Equal(4L, (await GetAsync(f, _Codes + "?labels=LAB", ct).ConfigureAwait(false)).GetProperty("totalRecords").GetInt64(), "Case-insensitive");
                    JsonElement post = await PostAsync(f, _Codes + "/enumerate", new Dictionary<string, object?> { { "labels", new[] { "red", "blue" } }, { "labelMatch", "Any" } }, ct).ConfigureAwait(false);
                    Check.Equal(3L, post.GetProperty("totalRecords").GetInt64(), "Any in the POST body");
                    JsonElement first = (await GetAsync(f, _Codes + "?labels=red&labels=blue", ct).ConfigureAwait(false)).GetProperty("objects")[0];
                    Check.True(first.GetProperty("labels").EnumerateArray().Select(l => l.GetString()).OrderBy(l => l, StringComparer.Ordinal).SequenceEqual(new[] { "Blue", "Red", "lab" }), "Labels on the payload");
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, _Codes + "?labelMatch=Some", HttpFixture.AdminToken, null, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                }),
                Http("Tags.ValueAndPresence", "Tag filters match key=value (case-insensitive key, case-sensitive value) and * for any value", async (f, ct) =>
                {
                    List<SignupCode> codes = await SeedAsync(f, "tagged", 3, ct).ConfigureAwait(false);
                    await f.Database.Metadata.WriteAsync(TargetTypeEnum.SignupCode, codes[0].Id, new ObjectMetadata { Labels = new List<string> { "tagged" }, Tags = new Dictionary<string, string> { { "Level", "Beginner" } } }, ct).ConfigureAwait(false);
                    await f.Database.Metadata.WriteAsync(TargetTypeEnum.SignupCode, codes[1].Id, new ObjectMetadata { Labels = new List<string> { "tagged" }, Tags = new Dictionary<string, string> { { "level", "beginner" }, { "region", "" } } }, ct).ConfigureAwait(false);
                    Check.Equal(1L, (await GetAsync(f, _Codes + "?tag.level=Beginner", ct).ConfigureAwait(false)).GetProperty("totalRecords").GetInt64(), "Exact value");
                    Check.Equal(1L, (await GetAsync(f, _Codes + "?tag.LEVEL=beginner", ct).ConfigureAwait(false)).GetProperty("totalRecords").GetInt64(), "Value is case-sensitive, key is not");
                    Check.Equal(2L, (await GetAsync(f, _Codes + "?tag.level=*", ct).ConfigureAwait(false)).GetProperty("totalRecords").GetInt64(), "Key present");
                    Check.Equal(1L, (await GetAsync(f, _Codes + "?tag.level=*&tag.region=*", ct).ConfigureAwait(false)).GetProperty("totalRecords").GetInt64(), "Every listed tag");
                    JsonElement post = await PostAsync(f, _Codes + "/enumerate", new Dictionary<string, object?> { { "tags", new Dictionary<string, string> { { "level", "*" } } }, { "labels", new[] { "tagged" } } }, ct).ConfigureAwait(false);
                    Check.Equal(2L, post.GetProperty("totalRecords").GetInt64(), "Tags in the POST body");
                    JsonElement tagged = (await GetAsync(f, _Codes + "?tag.level=Beginner", ct).ConfigureAwait(false)).GetProperty("objects")[0];
                    Check.Equal("Beginner", Api.Str(tagged.GetProperty("tags"), "Level"), "Tags on the payload");
                    JsonElement plain = (await GetAsync(f, _Codes + "?labels=tagged&search=" + Uri.EscapeDataString(codes[2].Code), ct).ConfigureAwait(false)).GetProperty("objects")[0];
                    Check.True(plain.GetProperty("tags").ValueKind == JsonValueKind.Object && plain.GetProperty("labels").ValueKind == JsonValueKind.Array, "Empty labels and tags are present");
                }),
                Http("Filters.TypedAndBody", "Collection filters arrive as top-level GET parameters or in the POST body filters, are typed, and unknown ones are 400", async (f, ct) =>
                {
                    List<SignupCode> codes = await SeedAsync(f, "filt", 3, ct).ConfigureAwait(false);
                    Check.Equal(3L, (await GetAsync(f, _Codes + "?labels=filt&status=Active", ct).ConfigureAwait(false)).GetProperty("totalRecords").GetInt64(), "GET filter");
                    JsonElement post = await PostAsync(f, _Codes + "/enumerate", new Dictionary<string, object?> { { "labels", new[] { "filt" } }, { "filters", new Dictionary<string, object> { { "status", "Inactive" } } } }, ct).ConfigureAwait(false);
                    Check.Equal(0L, post.GetProperty("totalRecords").GetInt64(), "POST body filter");
                    ApiResult bad = await Api.SendAsync(f, HttpMethod.Get, _Codes + "?status=Sleeping", HttpFixture.AdminToken, null, ct).ConfigureAwait(false);
                    Check.True(bad.Status == HttpStatusCode.BadRequest && bad.Body.GetProperty("fieldErrors").TryGetProperty("filters.status", out JsonElement _), "Bad filter value");
                    ApiResult unknown = await Api.SendAsync(f, HttpMethod.Get, _Codes + "?colour=red", HttpFixture.AdminToken, null, ct).ConfigureAwait(false);
                    Check.True(unknown.Status == HttpStatusCode.BadRequest && unknown.Body.GetProperty("fieldErrors").TryGetProperty("filters.colour", out JsonElement _), "Unknown filter");
                    ApiResult badToken = await Api.SendAsync(f, HttpMethod.Get, _Codes + "?continuationToken=bogus", HttpFixture.AdminToken, null, ct).ConfigureAwait(false);
                    Check.Equal("InvalidContinuationToken", badToken.Error, "Bad token over HTTP");
                    JsonElement first = await GetAsync(f, _Codes + "?labels=filt&maxResults=1", ct).ConfigureAwait(false);
                    ApiResult moved = await Api.SendAsync(f, HttpMethod.Get, _Codes + "?labels=filt&status=Active&maxResults=1&continuationToken=" + Uri.EscapeDataString(first.GetProperty("continuationToken").GetString()!), HttpFixture.AdminToken, null, ct).ConfigureAwait(false);
                    Check.Equal("InvalidContinuationToken", moved.Error, "Token from another filter set");
                }),
                Http("Csv.Bounded", "CSV exports return at most maxResults rows per call and name the next page in X-Continuation-Token", async (f, ct) =>
                {
                    List<SignupCode> codes = await SeedAsync(f, "csv", 5, ct).ConfigureAwait(false);
                    List<string> rows = new List<string>();
                    string? token = null;
                    int calls = 0;
                    do
                    {
                        ApiResult page = await Api.SendAsync(f, HttpMethod.Get, _Codes + "/export.csv?labels=csv&maxResults=2" + (token == null ? String.Empty : "&continuationToken=" + Uri.EscapeDataString(token)), HttpFixture.AdminToken, null, ct).ConfigureAwait(false);
                        Check.Equal(HttpStatusCode.OK, page.Status, "CSV page");
                        string[] lines = page.Text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
                        Check.True(lines[0].StartsWith("id,code,name", StringComparison.Ordinal) && lines.Length <= 3, "Header plus at most two rows");
                        rows.AddRange(lines.Skip(1));
                        Check.Equal("5", page.Headers.GetValueOrDefault("X-Total-Records"), "Total header");
                        token = page.Headers.TryGetValue("X-Continuation-Token", out string? next) ? next : null;
                        calls++;
                    }
                    while (token != null && calls < 10);
                    Check.Equal(3, calls, "Three calls of two, two, and one row");
                    Check.True(codes.All(c => rows.Count(r => r.StartsWith(c.Id + ",", StringComparison.Ordinal)) == 1), "Every code exactly once");
                    ApiResult defaultPage = await Api.SendAsync(f, HttpMethod.Get, _Codes + "/export.csv", HttpFixture.AdminToken, null, ct).ConfigureAwait(false);
                    Check.True(!defaultPage.Headers.ContainsKey("X-Continuation-Token"), "Small exports fit in one default page of maxResultsAdmin rows");
                }),
                Http("Member.LimitsAndTotals", "Member lists clamp to enumeration.maxResultsMember and admin lists count exact totals", async (f, ct) =>
                {
                    TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    JsonElement history = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/membership/history?maxResults=5000", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(100, history.GetProperty("maxResults").GetInt32(), "Member maximum 100");
                    Check.Equal(1L, history.GetProperty("totalRecords").GetInt64(), "Member totals counted by default");
                    Check.True(history.GetProperty("objects")[0].TryGetProperty("labels", out JsonElement _), "History items carry labels");
                    f.Settings.Enumeration.MaxResultsMember = 7;
                    JsonElement clamped = await PostAsync(f, "/api/v1/membership/history/enumerate", new Dictionary<string, object?> { { "maxResults", 50 } }, ct, member.AccessToken).ConfigureAwait(false);
                    Check.Equal(7, clamped.GetProperty("maxResults").GetInt32(), "Live setting");
                    f.Settings.Enumeration.CountMemberTotals = false;
                    JsonElement uncounted = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/membership/history", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(uncounted.GetProperty("totalRecords").ValueKind == JsonValueKind.Null, "Member totals can be turned off");
                    TestMember social = await Api.SocialAsync(f, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/membership/history", social.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    JsonElement admin = await GetAsync(f, "/api/v1/admin/members", ct).ConfigureAwait(false);
                    Check.True(admin.GetProperty("totalRecords").ValueKind == JsonValueKind.Number && admin.GetProperty("recordsRemaining").ValueKind == JsonValueKind.Number, "Admin totals always counted");
                })
            });
        }

        #endregion

        #region Private-Methods

        private static Task CatalogAsync(CancellationToken ct)
        {
            EnumerationCollection collection = new EnumerationCollection("catalog-test", null, "id", "createdutc", true) { HasInlineMetadata = true };
            EnumerationSettings settings = new EnumerationSettings();
            List<string> source = new List<string> { "a", "b", "c", "d" };
            EnumerationRequest first = EnumerationNormalizer.Normalize(new EnumerationQuery { MaxResults = 2, Ordering = "CreatedAscending" }, collection, settings, true, "property-a");
            EnumerationResult<string> page = CatalogEnumeration.Page(first, source, x => x);
            Check.Equal("a,b", String.Join(",", page.Objects), "Deterministic identifiers break tied epoch timestamps");
            Check.Equal(4L, page.TotalRecords!.Value, "Exact total");
            Check.Equal(2L, page.RecordsRemaining!.Value, "Exact remaining");
            source.Insert(0, "0");
            EnumerationRequest next = EnumerationNormalizer.Normalize(new EnumerationQuery { MaxResults = 2, ContinuationToken = page.ContinuationToken }, collection, settings, true, "property-a");
            EnumerationResult<string> tail = CatalogEnumeration.Page(next, source, x => x);
            Check.Equal("c,d", String.Join(",", tail.Objects), "Insertion before cursor does not repeat entries");
            Check.True(tail.EndOfResults && tail.ContinuationToken == null, "Final page has no cursor");
            bool wrongScope = false;
            try { EnumerationNormalizer.Normalize(new EnumerationQuery { ContinuationToken = page.ContinuationToken }, collection, settings, true, "property-b"); }
            catch (ApiErrorException) { wrongScope = true; }
            Check.True(wrongScope, "Other property cannot reuse cursor");
            EnumerationRequest filter = EnumerationNormalizer.Normalize(new EnumerationQuery { Labels = new List<string> { "BLUE" }, Tags = new Dictionary<string, string> { { "kind", "*" } } }, collection, settings, true);
            EnumerationResult<string> filtered = CatalogEnumeration.Page(filter, source, x => x,
                labels: x => x == "b" ? new[] { "blue" } : Array.Empty<string>(), tags: x => new Dictionary<string, string> { { "Kind", "entry" } });
            Check.Equal("b", String.Join(",", filtered.Objects), "Inline metadata filters match labels case-insensitively and wildcard tag values");
            EnumerationRequest future = EnumerationNormalizer.Normalize(new EnumerationQuery { CreatedAfterUtc = DateTime.UtcNow.AddYears(1) }, collection, settings, true);
            Check.Equal(0, CatalogEnumeration.Page(future, source, x => x).Objects.Count, "Epoch entries respect created window");
            EnumerationRequest capped = EnumerationNormalizer.Normalize(new EnumerationQuery { MaxResults = Int32.MaxValue, Skip = 100 }, collection, settings, true);
            EnumerationResult<string> empty = CatalogEnumeration.Page(capped, source, x => x);
            Check.Equal(settings.MaxResultsAdmin, empty.MaxResults, "Admin cap clamps");
            Check.Equal(0, empty.Objects.Count, "Skip beyond end returns empty page");
            Check.Equal(0L, empty.RecordsRemaining!.Value, "Remaining cannot be negative");
            EnumerationCollection numeric = new EnumerationCollection("catalog-numeric", null, "id", "createdutc", true)
                .Order("CountDescending", "count", true, EnumerationKeyKindEnum.Number).DefaultTo("CountDescending");
            EnumerationRequest numericRequest = EnumerationNormalizer.Normalize(new EnumerationQuery { MaxResults = 2 }, numeric, settings, true);
            List<string> numbers = new List<string> { "2", "10", "100" };
            EnumerationResult<string> numericPage = CatalogEnumeration.Page(numericRequest, numbers, n => n, orderKey: n => n);
            Check.Equal("100,10", String.Join(",", numericPage.Objects), "Numeric keys sort numerically, not lexically");
            EnumerationRequest numericNext = EnumerationNormalizer.Normalize(new EnumerationQuery { MaxResults = 2, ContinuationToken = numericPage.ContinuationToken }, numeric, settings, true);
            Check.Equal("2", String.Join(",", CatalogEnumeration.Page(numericNext, numbers, n => n, orderKey: n => n).Objects), "Numeric cursor uses numeric comparison");

            return Task.CompletedTask;
        }

        private static TestCaseDescriptor Unit(string caseId, string name, Action body)
        {
            return SuiteCases.Unit(Id, caseId, name, ct =>
            {
                body();
                return Task.CompletedTask;
            });
        }

        private static TestCaseDescriptor Http(string caseId, string name, Func<HttpFixture, CancellationToken, Task> body)
        {
            return SuiteCases.Http(Id, caseId, name, body);
        }

        private static void Invalid(Action action, string field, string what)
        {
            ApiErrorException e = Check.Throws<ApiErrorException>(action, what);
            Check.Equal(400, e.StatusCode, what + " status");
            Check.Equal("ValidationFailed", e.Error, what + " error");
            Check.True(e.FieldErrors != null && e.FieldErrors.ContainsKey(field), what + " names " + field + " (got " + String.Join(", ", e.FieldErrors?.Keys ?? Enumerable.Empty<string>()) + ")");
        }

        private static void InvalidToken(Action action, string what)
        {
            ApiErrorException e = Check.Throws<ApiErrorException>(action, what);
            Check.Equal(400, e.StatusCode, what + " status");
            Check.Equal("InvalidContinuationToken", e.Error, what + " error");
        }

        private static async Task<List<SignupCode>> SeedAsync(HttpFixture f, string label, int count, CancellationToken ct, DateTime? origin = null)
        {
            DateTime start = origin ?? DateTime.UtcNow.AddMinutes(-count);
            List<SignupCode> codes = new List<SignupCode>();
            string prefix = label.ToUpperInvariant() + Guid.NewGuid().ToString("N").Substring(0, 4).ToUpperInvariant();
            int[] permutation = Enumerable.Range(0, count).Select(i => (i * 7 + 3) % count).ToArray();
            for (int i = 0; i < count; i++)
            {
                DateTime created = new DateTime(start.Ticks - start.Ticks % TimeSpan.TicksPerMillisecond, DateTimeKind.Utc).AddSeconds(i);
                int p = permutation[i];
                SignupCode code = new SignupCode
                {
                    Id = IdGenerator.GenerateSignupCodeId(),
                    Code = prefix + "X" + (char)('A' + p) + i.ToString("D2", CultureInfo.InvariantCulture),
                    Name = label + "name" + ((p * 5 + 1) % count).ToString("D2", CultureInfo.InvariantCulture),
                    GrantType = GrantTypeEnum.Lifetime,
                    RedemptionCount = (p * 2 + 2) % count + 1,
                    LastRedeemedUtc = created.AddDays(-p - 1),
                    ExpiresUtc = created.AddDays(30 + (p * 4 + 1) % count),
                    CreatedUtc = created,
                    UpdatedUtc = created
                };
                await f.Database.SignupCodes.CreateAsync(code, ct).ConfigureAwait(false);
                codes.Add(code);
            }
            await f.Database.Metadata.WriteManyAsync(TargetTypeEnum.SignupCode, codes.Select(c => c.Id), new ObjectMetadata { Labels = new List<string> { label } }, ct).ConfigureAwait(false);
            return codes;
        }

        private static Task<JsonElement> GetAsync(HttpFixture f, string path, CancellationToken ct)
        {
            return Api.ExpectAsync(f, HttpMethod.Get, path, HttpFixture.AdminToken, null, HttpStatusCode.OK, ct);
        }

        private static Task<JsonElement> PostAsync(HttpFixture f, string path, object body, CancellationToken ct, string? bearer = null)
        {
            return Api.ExpectAsync(f, HttpMethod.Post, path, bearer ?? HttpFixture.AdminToken, body, HttpStatusCode.OK, ct);
        }

        private static async Task<List<string>> WalkGetAsync(HttpFixture f, string path, long total, CancellationToken ct)
        {
            List<string> ids = new List<string>();
            string? token = null;
            for (int guard = 0; guard < 100; guard++)
            {
                JsonElement page = await GetAsync(f, path + (token == null ? String.Empty : "&continuationToken=" + Uri.EscapeDataString(token)), ct).ConfigureAwait(false);
                ids.AddRange(page.GetProperty("objects").EnumerateArray().Select(o => Api.Str(o, "id")));
                Check.Equal(total, page.GetProperty("totalRecords").GetInt64(), "Exact total on every page");
                Check.Equal(total - ids.Count, page.GetProperty("recordsRemaining").GetInt64(), "Exact remaining");
                token = page.GetProperty("continuationToken").ValueKind == JsonValueKind.String ? page.GetProperty("continuationToken").GetString() : null;
                Check.Equal(token == null, page.GetProperty("endOfResults").GetBoolean(), "endOfResults matches the token");
                if (token == null) break;
            }
            return ids;
        }

        private static async Task<List<string>> WalkPostAsync(HttpFixture f, string path, Dictionary<string, object?> body, long total, CancellationToken ct)
        {
            List<string> ids = new List<string>();
            for (int guard = 0; guard < 100; guard++)
            {
                JsonElement page = await PostAsync(f, path, body, ct).ConfigureAwait(false);
                ids.AddRange(page.GetProperty("objects").EnumerateArray().Select(o => Api.Str(o, "id")));
                Check.Equal(total, page.GetProperty("totalRecords").GetInt64(), "Exact total on every page");
                if (page.GetProperty("continuationToken").ValueKind != JsonValueKind.String) break;
                body["continuationToken"] = page.GetProperty("continuationToken").GetString();
            }
            return ids;
        }

        #endregion
    }
}

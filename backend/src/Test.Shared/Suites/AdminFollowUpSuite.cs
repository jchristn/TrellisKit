namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Touchstone.Core;
    using TrellisKit.Core.Models;
    using TrellisKit.Server.Services;

    /// <summary>
    /// Admin and platform contract follow-ups: 409 conflicts name the offending row in fieldErrors; profile field
    /// valueCount reflects backfilled values (key and type lock, typed delete confirmation); computed property metrics
    /// orderings; the platform domain verification and request history entry routes; preview drafts merge by key; and
    /// the member directory under the access model.
    /// </summary>
    public static class AdminFollowUpSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "AdminFollowUp";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Admin and platform follow-ups (" + TestEnvironment.Provider + ")", new[]
            {
                SuiteCases.Http(Id, "RequestHistory.MetricDimensions", "Metric drilldowns retain exact route, status class, error and client dimensions through enumeration, summary and validation", async (f, ct) =>
                {
                    DateTime from = DateTime.UtcNow.AddHours(-2);
                    RequestHistoryEntry first = new RequestHistoryEntry { Method = "GET", Path = "/api/v1/example/one", Url = "/api/v1/example/one", StatusCode = 503, CreatedUtc = from.AddMinutes(1), CompletedUtc = from.AddMinutes(1), MetricRoute = "/api/v1/example/{id}", MetricError = "Unavailable", MetricApp = "web", MetricPlatform = "browser", MetricVersion = "1.2'quoted" };
                    RequestHistoryEntry second = new RequestHistoryEntry { Method = "GET", Path = "/api/v1/example/two", Url = "/api/v1/example/two", StatusCode = 200, CreatedUtc = from.AddMinutes(2), CompletedUtc = from.AddMinutes(2), MetricRoute = "/api/v1/elsewhere/{id}", MetricError = "", MetricApp = "mobile", MetricPlatform = "ios", MetricVersion = "2" };
                    await f.Database.RequestHistory.CreateAsync(first, ct).ConfigureAwait(false);
                    await f.Database.RequestHistory.CreateAsync(second, ct).ConfigureAwait(false);
                    Dictionary<string, string> dimensions = new Dictionary<string, string> { ["routeTemplate"] = first.MetricRoute, ["statusClass"] = "5xx", ["errorCode"] = "Unavailable", ["clientApp"] = "web", ["clientPlatform"] = "browser", ["clientVersion"] = first.MetricVersion };
                    foreach (KeyValuePair<string, string> dimension in dimensions)
                    {
                        JsonElement page = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/request-history/enumerate", HttpFixture.AdminToken, new { createdAfterUtc = from, createdBeforeUtc = from.AddHours(1), filters = new Dictionary<string, string> { [dimension.Key] = dimension.Value } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                        Check.Equal(1L, page.GetProperty("totalRecords").GetInt64(), "Exact filter " + dimension.Key);
                        Check.Equal(first.Id, Api.Str(page.GetProperty("objects")[0], "id"), "Matching record " + dimension.Key);
                    }
                    JsonElement fallback = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/request-history/enumerate", HttpFixture.AdminToken, new { createdAfterUtc = from, createdBeforeUtc = from.AddHours(1), filters = new { errorCode = "200" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(second.Id, Api.Str(fallback.GetProperty("objects")[0], "id"), "Error dimension uses the same status fallback as the rollup");
                    string window = "fromUtc=" + Uri.EscapeDataString(from.ToString("O")) + "&toUtc=" + Uri.EscapeDataString(from.AddHours(1).ToString("O"));
                    JsonElement summary = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/request-history/summary?" + window + "&statusClass=5xx&clientPlatform=browser", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(1L, summary.GetProperty("totalCount").GetInt64(), "Summary honors the same dimensions");
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/request-history/enumerate", HttpFixture.AdminToken, new { filters = new { statusClass = "5 OR 1=1" } }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/request-history/summary?" + window + "&statusClass=500", HttpFixture.AdminToken, null, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                }),
                SuiteCases.Http(Id, "Conflicts.NameTheRow", "409 DomainInUse and AppIdInUse carry fieldErrors domains[i].host and apps[i].appId for the offending rows", MultiPropertyKit.Multi, async (f, ct) =>
                {
                    string platform = await MultiPropertyKit.PlatformLoginAsync(f, ct).ConfigureAwait(false);
                    await MultiPropertyKit.CreateSecondAsync(f, platform, ct).ConfigureAwait(false);
                    f.PropertyId = TestProperty.SampleId;
                    JsonElement current = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/property/domains", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    List<object> domains = current.GetProperty("objects").EnumerateArray().Select(d => (object)new { host = Api.Str(d, "host"), kind = Api.Str(d, "kind") }).ToList();
                    int index = domains.Count;
                    domains.Add(new { host = MultiPropertyKit.SecondWeb, kind = "Web" });
                    ApiResult domain = await Api.SendAsync(f, HttpMethod.Put, "/api/v1/admin/property/domains", HttpFixture.AdminToken, new { domains }, ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.Conflict, domain.Status, "DomainInUse: " + domain.Text);
                    Check.Equal("DomainInUse", domain.Error, "Error");
                    Check.True(domain.Body.GetProperty("fieldErrors").TryGetProperty("domains[" + index + "].host", out JsonElement _), "fieldErrors names the row: " + domain.Text);
                    ApiResult app = await Api.SendAsync(f, HttpMethod.Put, "/api/v1/admin/property/domains", HttpFixture.AdminToken, new { apps = new[] { new { platform = "Ios", appId = MultiPropertyKit.SecondIosApp } } }, ct).ConfigureAwait(false);
                    Check.Equal("AppIdInUse", app.Error, "AppIdInUse: " + app.Text);
                    Check.True(app.Body.GetProperty("fieldErrors").TryGetProperty("apps[0].appId", out JsonElement _), "fieldErrors names the app row: " + app.Text);
                }),
                SuiteCases.Http(Id, "Conflicts.OptionInUse", "409 OptionInUse names each option in use as fieldErrors options.<key>", async (f, ct) =>
                {
                    string id = await SelectFieldAsync(f, ct).ConfigureAwait(false);
                    TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/me/profile", member.AccessToken, new { profile = new { favoriteColor = new { key = "Other", otherText = "Teal" } } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    ApiResult result = await Api.SendAsync(f, HttpMethod.Put, "/api/v1/admin/profile-fields/" + id, HttpFixture.AdminToken, new { options = new object[] { new { key = "Red", label = "Red" } } }, ct).ConfigureAwait(false);
                    Check.Equal("OptionInUse", result.Error, "OptionInUse: " + result.Text);
                    Check.True(result.Body.GetProperty("fieldErrors").TryGetProperty("options.Other", out JsonElement _), "fieldErrors names the option: " + result.Text);
                }),
                SuiteCases.Http(Id, "ValueCount.Backfilled", "valueCount counts backfilled values even when the stored count is stale, so the key and type lock and the typed delete confirmation apply to seeded fields", async (f, ct) =>
                {
                    TestMember member = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    await f.Raw.ExecuteQueryAsync("UPDATE users SET affiliation = 'Backfill Club' WHERE id = '" + member.Id + "';", false, ct).ConfigureAwait(false);
                    List<ProfileField> fields = await f.Database.ProfileFields.ReadAllAsync(ct).ConfigureAwait(false);
                    await f.Database.ProfileValues.BackfillLegacyAsync(ProfileFieldService.LegacyIds(fields), DateTime.UtcNow, ct).ConfigureAwait(false);
                    ProfileField affiliation = fields.First(x => x.Key == "affiliation");
                    await f.Raw.ExecuteQueryAsync("UPDATE profilefields SET valuecount = 0 WHERE id = '" + affiliation.Id + "';", false, ct).ConfigureAwait(false);
                    JsonElement read = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/profile-fields/" + affiliation.Id, HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(read.GetProperty("valueCount").GetInt64() >= 1, "valueCount reflects the backfilled value: " + read.GetProperty("valueCount"));
                    await f.Raw.ExecuteQueryAsync("UPDATE profilefields SET valuecount = 0 WHERE id = '" + affiliation.Id + "';", false, ct).ConfigureAwait(false);
                    JsonElement listed = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/profile-fields/enumerate", HttpFixture.AdminToken, new { search = "affiliation" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(listed.GetProperty("objects").EnumerateArray().First(o => Api.Str(o, "key") == "affiliation").GetProperty("valueCount").GetInt64() >= 1, "The list counts it too");
                    await f.Raw.ExecuteQueryAsync("UPDATE profilefields SET valuecount = 0 WHERE id = '" + affiliation.Id + "';", false, ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/admin/profile-fields/" + affiliation.Id, HttpFixture.AdminToken, new { key = "club" }, HttpStatusCode.Conflict, "FieldHasValues", ct).ConfigureAwait(false);
                    await f.Raw.ExecuteQueryAsync("UPDATE profilefields SET valuecount = 0 WHERE id = '" + affiliation.Id + "';", false, ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Delete, "/api/v1/admin/profile-fields/" + affiliation.Id, HttpFixture.AdminToken, null, HttpStatusCode.Conflict, "FieldHasValues", ct).ConfigureAwait(false);
                }),
                SuiteCases.Http(Id, "Metrics.ComputedOrderings", "Property metrics order by members, errors, and storage (most first), page with skip or stable scoped continuation tokens", MultiPropertyKit.Multi, async (f, ct) =>
                {
                    string platform = await MultiPropertyKit.PlatformLoginAsync(f, ct).ConfigureAwait(false);
                    string second = await MultiPropertyKit.CreateSecondAsync(f, platform, ct).ConfigureAwait(false);
                    f.PropertyId = second;
                    await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    JsonElement members = await MultiPropertyKit.PlatformExpectAsync(f, HttpMethod.Post, "/api/v1/platform/metrics/properties/enumerate", platform, new { ordering = "MembersDescending", maxResults = 25 }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    List<JsonElement> rows = members.GetProperty("objects").EnumerateArray().ToList();
                    Check.True(rows.Count >= 2, "Both properties listed");
                    Check.Equal(second, Api.Str(rows[0], "propertyId"), "Most members first");
                    Check.True(rows.Zip(rows.Skip(1)).All(p => p.First.GetProperty("members").GetInt64() >= p.Second.GetProperty("members").GetInt64()), "Descending by members");
                    Check.Equal(JsonValueKind.Null, members.GetProperty("continuationToken").ValueKind, "No continuation token");
                    JsonElement page = await MultiPropertyKit.PlatformExpectAsync(f, HttpMethod.Post, "/api/v1/platform/metrics/properties/enumerate", platform, new { ordering = "MembersDescending", maxResults = 1, skip = 1 }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(Api.Str(rows[1], "propertyId"), Api.Str(page.GetProperty("objects")[0], "propertyId"), "Skip pages the sorted list");
                    Check.Equal((long)rows.Count, page.GetProperty("totalRecords").GetInt64(), "totalRecords");
                    JsonElement first = await MultiPropertyKit.PlatformExpectAsync(f, HttpMethod.Post, "/api/v1/platform/metrics/properties/enumerate", platform, new { ordering = "MembersDescending", maxResults = 1 }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(!first.GetProperty("endOfResults").GetBoolean(), "First computed page continues");
                    JsonElement next = await MultiPropertyKit.PlatformExpectAsync(f, HttpMethod.Post, "/api/v1/platform/metrics/properties/enumerate", platform, new { maxResults = 1, continuationToken = Api.Str(first, "continuationToken") }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(Api.Str(rows[1], "propertyId"), Api.Str(next.GetProperty("objects")[0], "propertyId"), "Computed cursor advances and restores ordering");

                    foreach (string ordering in new[] { "ErrorsDescending", "StorageDescending" })
                    {
                        JsonElement other = await MultiPropertyKit.PlatformExpectAsync(f, HttpMethod.Post, "/api/v1/platform/metrics/properties/enumerate", platform, new { ordering, maxResults = 25 }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                        string field = ordering == "ErrorsDescending" ? "errors24h" : "storageBytes";
                        List<long> values = other.GetProperty("objects").EnumerateArray().Select(o => o.GetProperty(field).GetInt64()).ToList();
                        Check.True(values.SequenceEqual(values.OrderByDescending(v => v)), ordering + " sorts by " + field);
                    }
                }),
                SuiteCases.Http(Id, "Platform.VerifyDomainAndHistoryEntry", "Platform administrators verify a property's domain with the TXT flow, and read one request history entry with credentials redacted", MultiPropertyKit.Multi, async (f, ct) =>
                {
                    string platform = await MultiPropertyKit.PlatformLoginAsync(f, ct).ConfigureAwait(false);
                    f.PropertyId = TestProperty.SampleId;
                    JsonElement current = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/property/domains", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    List<object> domains = current.GetProperty("objects").EnumerateArray().Select(d => (object)new { host = Api.Str(d, "host"), kind = Api.Str(d, "kind") }).ToList();
                    string host = "verify-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".example.test";
                    domains.Add(new { host, kind = "Web" });
                    JsonElement updated = await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/property/domains", HttpFixture.AdminToken, new { domains }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    JsonElement added = updated.GetProperty("domains").EnumerateArray().First(d => Api.Str(d, "host") == host);
                    string route = "/api/v1/platform/properties/" + TestProperty.SampleId + "/domains/" + Api.Str(added, "id") + "/verify";
                    JsonElement failed = await MultiPropertyKit.PlatformExpectAsync(f, HttpMethod.Post, route, platform, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(failed.GetProperty("verifiedUtc").ValueKind == JsonValueKind.Null, "Not verified without the record");
                    f.Fakes.Txt.Set(PlatformService.VerificationRecordPrefix + host, Api.Str(added.GetProperty("verification"), "recordValue"));
                    JsonElement verified = await MultiPropertyKit.PlatformExpectAsync(f, HttpMethod.Post, route, platform, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(verified.GetProperty("verifiedUtc").ValueKind != JsonValueKind.Null, "Verified through the platform route");
                    await MultiPropertyKit.PlatformExpectAsync(f, HttpMethod.Post, "/api/v1/platform/properties/" + TestProperty.SampleId + "/domains/pdm_missing/verify", platform, null, HttpStatusCode.NotFound, ct).ConfigureAwait(false);

                    await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    JsonElement history = new JsonElement();
                    for (int attempt = 0; attempt < 40; attempt++)
                    {
                        history = await MultiPropertyKit.PlatformExpectAsync(f, HttpMethod.Post, "/api/v1/platform/request-history/enumerate", platform, new { filters = new { propertyId = TestProperty.SampleId, pathContains = "/auth/register" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                        if (history.GetProperty("objects").GetArrayLength() > 0) break;
                        await Task.Delay(50, ct).ConfigureAwait(false);
                    }
                    string entryId = Api.Str(history.GetProperty("objects")[0], "id");
                    JsonElement entry = await MultiPropertyKit.PlatformExpectAsync(f, HttpMethod.Get, "/api/v1/platform/request-history/" + entryId + "?propertyId=" + TestProperty.SampleId, platform, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    string body = entry.GetProperty("requestBody").GetString() ?? String.Empty;
                    Check.True(body.Contains(RequestHistoryCaptureService.RedactedValue, StringComparison.Ordinal) && !body.Contains(Api.Password, StringComparison.Ordinal), "The password is redacted: " + body);
                    Check.True(entry.GetProperty("requestHeaders").ValueKind == JsonValueKind.Object, "Headers included");
                    await MultiPropertyKit.PlatformExpectAsync(f, HttpMethod.Get, "/api/v1/platform/request-history/" + entryId, platform, null, HttpStatusCode.NotFound, ct).ConfigureAwait(false);
                    await MultiPropertyKit.PlatformExpectAsync(f, HttpMethod.Get, "/api/v1/platform/request-history/" + entryId + "?propertyId=app_missing_00000000000000000", platform, null, HttpStatusCode.NotFound, ct).ConfigureAwait(false);
                }),
                SuiteCases.Http(Id, "Preview.MergesByKey", "Preview drafts replace the saved field with the same key and add new keys; other saved fields stay", async (f, ct) =>
                {
                    await SelectFieldAsync(f, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/profile-fields", HttpFixture.AdminToken, new { key = "nickname", label = "Nickname", type = "Text", collectAt = "Profile" }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                    JsonElement preview = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/profile-fields/preview", HttpFixture.AdminToken, new
                    {
                        target = "Editor",
                        viewer = "Self",
                        fields = new object[] { new { key = "nickname", label = "Alias", type = "Text", collectAt = "Profile" }, new { key = "motto", label = "Motto", type = "Text", collectAt = "Profile" } }
                    }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    List<JsonElement> items = preview.GetProperty("items").EnumerateArray().ToList();
                    Check.True(items.Any(i => Api.Str(i, "key") == "favoriteColor"), "Saved fields without a draft stay");
                    Check.Equal("Alias", Api.Str(items.First(i => Api.Str(i, "key") == "nickname"), "label"), "The draft replaces the saved field by key");
                    Check.Equal(1, items.Count(i => Api.Str(i, "key") == "nickname"), "No duplicate key");
                    Check.True(items.Any(i => Api.Str(i, "key") == "motto"), "New keys are added");
                }),
                SuiteCases.Http(Id, "Directory.FollowsTheModel", "The member directory is the directory feature: gated (or members gated) needs a membership, GET /property lists it in gatedFeatures, and mention search stays separate", async (f, ct) =>
                {
                    TestMember free = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/property/access", HttpFixture.AdminToken, new { model = "PartiallyGated", gatedFeatures = new[] { "directory" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    ApiResult directory = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/members/directory/enumerate", free.AccessToken, new { maxResults = 10 }, ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.PaymentRequired, directory.Status, "Gated directory: " + directory.Text);
                    Check.Equal("directory", Api.Str(directory.Body, "feature"), "feature directory");
                    await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/members/search?q=te", free.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    JsonElement property = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/property", null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(property.GetProperty("access").GetProperty("gatedFeatures").EnumerateArray().Any(e => e.GetString() == "directory"), "Clients see directory gated");
                    await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/property/access", HttpFixture.AdminToken, new { gatedFeatures = new[] { "members" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/members/directory/enumerate", free.AccessToken, new { maxResults = 10 }, HttpStatusCode.PaymentRequired, "MembershipRequired", ct).ConfigureAwait(false);
                    JsonElement both = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/property", null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    List<string> gated = both.GetProperty("access").GetProperty("gatedFeatures").EnumerateArray().Select(e => e.GetString()!).ToList();
                    Check.True(gated.Contains("members") && gated.Contains("directory"), "Gating members lists directory: " + String.Join(",", gated));
                    await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/property/access", HttpFixture.AdminToken, new { gatedFeatures = Array.Empty<string>() }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/members/directory/enumerate", free.AccessToken, new { maxResults = 10 }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                }),
                SuiteCases.Http(Id, "Directory.ListsFinishedMembers", "The member directory lists only finished profiles (handle set) and, when the model needs a membership for it, only members and staff; in Open every finished account", async (f, ct) =>
                {
                    string tag = Guid.NewGuid().ToString("N").Substring(0, 6);
                    async Task<TestMember> WithHandleAsync(TestMember m, string handle)
                    {
                        await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/me/profile", m.AccessToken, new { handle }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                        return m;
                    }
                    TestMember viewer = await WithHandleAsync(await Api.MemberAsync(f, ct).ConfigureAwait(false), "viewer" + tag).ConfigureAwait(false);
                    TestMember finished = await WithHandleAsync(await Api.MemberAsync(f, ct).ConfigureAwait(false), "finished" + tag).ConfigureAwait(false);
                    TestMember unfinished = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    TestMember freeAccount = await WithHandleAsync(await Api.RegisterAsync(f, ct).ConfigureAwait(false), "free" + tag).ConfigureAwait(false);
                    TestMember staff = await WithHandleAsync(await Api.AdminAsync(f, ct).ConfigureAwait(false), "staff" + tag).ConfigureAwait(false);
                    async Task<List<string>> ListedAsync()
                    {
                        JsonElement page = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/members/directory/enumerate", viewer.AccessToken, new { maxResults = 100 }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                        return page.GetProperty("objects").EnumerateArray().Select(o => Api.Str(o, "id")).ToList();
                    }
                    List<string> gated = await ListedAsync().ConfigureAwait(false);
                    Check.True(gated.Contains(finished.Id) && gated.Contains(viewer.Id), "FullyGated: finished members are listed");
                    Check.True(!gated.Contains(unfinished.Id), "Unfinished sign-ups (no handle) are not listed");
                    Check.True(!gated.Contains(freeAccount.Id), "FullyGated: accounts without a membership are not listed");
                    Check.True(gated.Contains(staff.Id), "Staff are listed without a membership");
                    await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/property/access", HttpFixture.AdminToken, new { model = "Open" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    List<string> open = await ListedAsync().ConfigureAwait(false);
                    Check.True(open.Contains(freeAccount.Id) && open.Contains(finished.Id), "Open: every finished account is listed");
                    Check.True(!open.Contains(unfinished.Id), "Open: unfinished sign-ups are still not listed");
                    await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/property/access", HttpFixture.AdminToken, new { model = "FullyGated" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                }),
                SuiteCases.Http(Id, "Search.FollowsTheModel", "Global search is the search feature: gated in PartiallyGated it needs a membership (402 feature search), GET /property lists it in gatedFeatures, and gating forums does not gate it", async (f, ct) =>
                {
                    TestMember free = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/property/access", HttpFixture.AdminToken, new { model = "PartiallyGated", gatedFeatures = new[] { "search" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    ApiResult gated = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/search/enumerate", free.AccessToken, new { search = "hello" }, ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.PaymentRequired, gated.Status, "Gated search: " + gated.Text);
                    Check.Equal("search", Api.Str(gated.Body, "feature"), "feature search");
                    Check.Equal("Membership", Api.Str(gated.Body, "required"), "required Membership");
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/search/enumerate", member.AccessToken, new { search = "hello" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/forums/enumerate", free.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    JsonElement property = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/property", null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    List<string> keys = property.GetProperty("access").GetProperty("gatedFeatures").EnumerateArray().Select(e => e.GetString()!).ToList();
                    Check.True(keys.Contains("search") && !keys.Contains("forums"), "Clients see search gated on its own: " + String.Join(",", keys));
                    await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/property/access", HttpFixture.AdminToken, new { gatedFeatures = new[] { "forums" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/search/enumerate", free.AccessToken, new { search = "hello" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/property/access", HttpFixture.AdminToken, new { gatedFeatures = Array.Empty<string>() }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                }),
                SuiteCases.Http(Id, "Search.FeatureOff", "With features.search false the global search routes are 404 NotAvailable, GET /property reports features.search false, and thread search keeps working", p => { p.Features.Search = false; }, async (f, ct) =>
                {
                    TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/search/enumerate", member.AccessToken, new { search = "hello" }, HttpStatusCode.NotFound, "NotAvailable", ct).ConfigureAwait(false);
                    JsonElement property = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/property", null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(!property.GetProperty("features").GetProperty("search").GetBoolean(), "features.search false");
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/threads/enumerate", member.AccessToken, new { search = "hello" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                }),
                SuiteCases.Http(Id, "Directory.FeatureOff", "With features.directory false the member directory routes are 404 NotAvailable in every model", p => { p.Features.Directory = false; }, async (f, ct) =>
                {
                    TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/members/directory/enumerate", member.AccessToken, new { maxResults = 10 }, HttpStatusCode.NotFound, "NotAvailable", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/members/directory", member.AccessToken, null, HttpStatusCode.NotFound, "NotAvailable", ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/members/search?q=te", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                })
            });
        }

        #endregion

        #region Private-Methods

        private static async Task<string> SelectFieldAsync(HttpFixture f, CancellationToken ct)
        {
            JsonElement created = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/profile-fields", HttpFixture.AdminToken, new
            {
                key = "favoriteColor", label = "Favorite color", type = "SingleSelect", options = new object[] { new { key = "Red", label = "Red" }, new { key = "Other", label = "Other", freeText = true } },
                collectAt = "Profile"
            }, HttpStatusCode.Created, ct).ConfigureAwait(false);
            return Api.Str(created, "id");
        }

        #endregion
    }
}

namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Touchstone.Core;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;
    using TrellisKit.Server.Services;
    using TrellisKit.Server.Services.Symbolication;
    using TrellisKit.Server.Workers;

    /// <summary>
    /// Remote strings, banners, remote assets, the ReleaseManager role, profileField and runtimeVersion targeting,
    /// UpdateAssetGcWorker, and source map symbolication (docs/LIVE_UPDATES_CONTRACT.md sections 11, 12, 14, 16, 17, and
    /// the as-built notes).
    /// </summary>
    public static class LiveContentSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "LiveContent";

        #endregion

        #region Private-Members

        private const string _Base = "/api/v1/admin/app";
        private const string _Png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";
        private const string _Base64 = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Live content (" + TestEnvironment.Provider + ")", new[]
            {
                SuiteCases.Unit(Id, "Targeting.ProfileField", "profileField matches the caller's filterable field values with Equals and In, by argument or the first value; anonymous callers never match; malformed conditions are rejected", ct =>
                {
                    FlagEvaluationContext member = new FlagEvaluationContext { SignedIn = true, SubjectId = "u1", NowUtc = DateTime.UtcNow };
                    member.ProfileFields["rank"] = new List<string> { "Advanced" };
                    member.ProfileFields["affiliation"] = new List<string> { "Example Club" };
                    Check.Equal("true", Evaluate(Condition("profileField", "Equals", null, "rank", "advanced"), member), "Equals by first value, case-insensitive");
                    Check.Equal("true", Evaluate(Condition("profileField", "In", "rank", "Beginner", "Advanced"), member), "In by argument");
                    Check.Equal("false", Evaluate(Condition("profileField", "In", "rank", "Beginner"), member), "Not in the list");
                    Check.Equal("false", Evaluate(Condition("profileField", "Equals", null, "city", "Somewhere"), member), "Field without a value");
                    FlagEvaluationContext anonymous = new FlagEvaluationContext { NowUtc = DateTime.UtcNow };
                    anonymous.ProfileFields["rank"] = new List<string> { "Advanced" };
                    Check.Equal("false", Evaluate(Condition("profileField", "Equals", "rank", "Advanced"), anonymous), "Anonymous never matches");
                    FlagEvaluationContext install = new FlagEvaluationContext { RuntimeVersion = "rt-1", Channel = "preview", NowUtc = DateTime.UtcNow };
                    Check.Equal("true", Evaluate(Condition("runtimeVersion", "In", null, "rt-1"), install), "runtimeVersion from the install row");
                    Dictionary<string, string> errors = FeatureFlagEvaluator.Validate(new List<FeatureFlagRule> { Rule(Condition("profileField", "Equals", null, "rank")) }, 20, 10, 100);
                    Check.True(errors.ContainsKey("rules[0].conditions[0].values"), "A field key without a value to compare is rejected");
                    return Task.CompletedTask;
                }),
                SuiteCases.Unit(Id, "Symbolication.DecoderAndParser", "Stack frames in Hermes, V8, and JavaScriptCore shapes parse; VLQ mappings decode to the original source, line, column, and name", ct =>
                {
                    string mappings = Segment(0, 0, 0, 0, 0) + "," + Segment(10, 0, 4, 2, 1) + ";" + Segment(4, 1, 2, 0, 1);
                    SourceMapDecoder decoder = SourceMapDecoder.Parse(Encoding.UTF8.GetBytes("{\"version\":3,\"sourceRoot\":\"app\",\"sources\":[\"src/feed/Composer.jsx\",\"src/api.js\"],\"names\":[\"render\",\"submit\"],\"mappings\":\"" + mappings + "\"}"));
                    List<OriginalPositionView?> found = decoder.Lookup(new List<int[]> { new[] { 0, 11 }, new[] { 0, 3 }, new[] { 1, 9 }, new[] { 7, 0 } });
                    Check.Equal("app/src/feed/Composer.jsx", found[0]!.Source, "Source with sourceRoot");
                    Check.Equal(5, found[0]!.Line, "Original line (1-based)");
                    Check.Equal(3, found[0]!.Column, "Original column (1-based)");
                    Check.Equal("submit", found[0]!.Name, "Name");
                    Check.Equal("render", found[1]!.Name, "Earlier segment");
                    Check.Equal("app/src/api.js", found[2]!.Source, "Relative fields carry across lines");
                    Check.Equal(7, found[2]!.Line, "Line accumulates");
                    Check.True(found[3] == null, "Unmapped line");
                    List<SymbolicatedFrameView> frames = StackTraceParser.Parse("TypeError: x is undefined\n    at submit (address at index.android.bundle:1:12)\n    at index.android.bundle:2:5\nrender@index.ios.bundle:1:4\n    at native", out bool truncated);
                    Check.Equal(5, frames.Count, "Every line is a frame");
                    Check.True(frames[0].Line == null, "Message line has no position");
                    Check.Equal("submit", frames[1].Function, "Hermes function");
                    Check.Equal("index.android.bundle", frames[1].File, "Hermes file");
                    Check.Equal(12, frames[1].Column!.Value, "Hermes column");
                    Check.Equal(2, frames[2].Line!.Value, "Bare frame");
                    Check.Equal("render", frames[3].Function, "JavaScriptCore frame");
                    Check.True(!truncated, "Not truncated");
                    StackTraceParser.Parse(String.Join("\n", Enumerable.Range(0, 250).Select(i => "at f (a.js:1:" + i + ")")), out bool cut);
                    Check.True(cut, "Bounded to 200 frames");
                    Check.Throws<FormatException>(() => SourceMapDecoder.Parse(Encoding.UTF8.GetBytes("{\"version\":3,\"sections\":[{\"offset\":{\"line\":0,\"column\":0},\"url\":\"part.map\"}]}")), "Sections by url are refused");
                    Check.Throws<FormatException>(() => SourceMapDecoder.Parse(Encoding.UTF8.GetBytes("{\"version\":3,\"sources\":[]}")), "A map without mappings or sections is refused");
                    return Task.CompletedTask;
                }),
                SuiteCases.Unit(Id, "Symbolication.IndexMaps", "Index maps (sections) map each position through the section that starts at or before it, relative to the section offset; sections out of order are refused", ct =>
                {
                    string a = "{\"version\":3,\"sources\":[\"a.js\"],\"names\":[\"first\"],\"mappings\":\"" + Segment(0, 0, 0, 0, 0) + "\"}";
                    string b = "{\"version\":3,\"sourceRoot\":\"lib\",\"sources\":[\"b.js\"],\"names\":[],\"mappings\":\"" + Segment(2, 0, 9, 4) + "\"}";
                    string index = "{\"version\":3,\"sections\":[{\"offset\":{\"line\":0,\"column\":0},\"map\":" + a + "},{\"offset\":{\"line\":3,\"column\":10},\"map\":" + b + "}]}";
                    SourceMapDecoder decoder = SourceMapDecoder.Parse(Encoding.UTF8.GetBytes(index));
                    Check.True(decoder.Sources.SequenceEqual(new[] { "a.js", "lib/b.js" }), "Sources of every section");
                    List<OriginalPositionView?> found = decoder.Lookup(new List<int[]> { new[] { 0, 5 }, new[] { 3, 13 }, new[] { 4, 3 }, new[] { 3, 4 } });
                    Check.True(found[0] != null && found[0]!.Source == "a.js" && found[0]!.Line == 1 && found[0]!.Column == 1 && found[0]!.Name == "first", "First section");
                    Check.True(found[1] != null && found[1]!.Source == "lib/b.js" && found[1]!.Line == 10 && found[1]!.Column == 5, "Second section, column relative to its offset");
                    Check.True(found[2] == null, "A later line of the second section without mappings");
                    Check.True(found[3] == null, "Before the second section starts, the first section has no mapping on that line");
                    string nested = "{\"version\":3,\"sections\":[{\"offset\":{\"line\":1,\"column\":0},\"map\":" + index + "}]}";
                    List<OriginalPositionView?> deep = SourceMapDecoder.Parse(Encoding.UTF8.GetBytes(nested)).Lookup(new List<int[]> { new[] { 4, 13 } });
                    Check.True(deep[0] != null && deep[0]!.Source == "lib/b.js", "Nested index map");
                    Check.Throws<FormatException>(() => SourceMapDecoder.Parse(Encoding.UTF8.GetBytes("{\"version\":3,\"sections\":[{\"offset\":{\"line\":3,\"column\":0},\"map\":" + a + "},{\"offset\":{\"line\":1,\"column\":0},\"map\":" + b + "}]}")), "Sections out of order are refused");
                    return Task.CompletedTask;
                }),
                SuiteCases.Http(Id, "Strings.DraftPublishDeltaRestore", "Overrides validate against the catalog and tokens, stay drafts until publish, reach only their client as deltas since a version, delete and restore through versions", async (f, ct) =>
                {
                    StringCatalogEntry plain = AppContentService.Catalog().Entries.First(e => e.Client == "web" && e.Tokens.Count == 0);
                    StringCatalogEntry tokened = AppContentService.Catalog().Entries.First(e => e.Client == "mobile" && e.Tokens.Contains("name"));
                    await Api.ExpectErrorAsync(f, HttpMethod.Put, _Base + "/strings/web.no.such.key", HttpFixture.AdminToken, new { value = "x" }, HttpStatusCode.BadRequest, "UnknownStringKey", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Put, _Base + "/strings/" + plain.Key, HttpFixture.AdminToken, new { value = "Hello {name}" }, HttpStatusCode.BadRequest, "InvalidStringTokens", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Put, _Base + "/strings/" + plain.Key, HttpFixture.AdminToken, new { value = new string('x', plain.MaxLength + 1) }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    JsonElement draft = await Api.ExpectAsync(f, HttpMethod.Put, _Base + "/strings/" + plain.Key, HttpFixture.AdminToken, new { value = "Override one", notes = "Test", labels = new[] { "copy" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(draft.GetProperty("hasDraftChanges").GetBoolean() && !draft.GetProperty("overridden").GetBoolean(), "Draft, not yet overridden");
                    Check.Equal(plain.DefaultValue, Api.Str(draft, "defaultValue"), "Default kept");
                    await Api.ExpectAsync(f, HttpMethod.Put, _Base + "/strings/" + tokened.Key, HttpFixture.AdminToken, new { value = "Welcome to {name}" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    JsonElement web = await Manifest(f, "web", null, null, ct).ConfigureAwait(false);
                    Check.True(!web.GetProperty("strings").GetProperty("upserts").TryGetProperty(plain.Key, out JsonElement _), "Drafts never reach clients");
                    JsonElement drafts = await Api.ExpectAsync(f, HttpMethod.Get, _Base + "/drafts", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(2L, drafts.GetProperty("counts").GetProperty("strings").GetInt64(), "Two string drafts counted");
                    long v0 = drafts.GetProperty("contentVersion").GetInt64();
                    JsonElement published = await Api.ExpectAsync(f, HttpMethod.Post, _Base + "/publish", HttpFixture.AdminToken, new { message = "Copy", expectedVersion = v0 }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                    Check.Equal(2, published.GetProperty("changeCounts").GetProperty("strings").GetInt32(), "String change count");
                    long v1 = v0 + 1;
                    web = await Manifest(f, "web", null, null, ct).ConfigureAwait(false);
                    Check.Equal("Override one", Api.Str(web.GetProperty("strings").GetProperty("upserts"), plain.Key), "Web override");
                    Check.True(!web.GetProperty("strings").GetProperty("upserts").TryGetProperty(tokened.Key, out JsonElement _), "Mobile keys never go to web");
                    JsonElement mobile = await Manifest(f, "mobile", null, null, ct).ConfigureAwait(false);
                    Check.Equal("Welcome to {name}", Api.Str(mobile.GetProperty("strings").GetProperty("upserts"), tokened.Key), "Mobile override keeps its token");
                    JsonElement current = await Manifest(f, "web", v1, null, ct).ConfigureAwait(false);
                    Check.Equal(0, current.GetProperty("strings").GetProperty("upserts").EnumerateObject().Count(), "No changes since the current version");
                    JsonElement removal = await Api.ExpectAsync(f, HttpMethod.Delete, _Base + "/strings/" + plain.Key, HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(removal.GetProperty("pendingDelete").GetBoolean(), "Removal drafted");
                    await Api.ExpectAsync(f, HttpMethod.Post, _Base + "/publish", HttpFixture.AdminToken, new { message = "Remove", expectedVersion = v1 }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                    JsonElement delta = await Manifest(f, "web", v1, null, ct).ConfigureAwait(false);
                    Check.Equal(plain.Key, delta.GetProperty("strings").GetProperty("deletes")[0].GetString()!, "Delta delete");
                    Check.True(!delta.GetProperty("fullRefresh").GetBoolean(), "A delta, not a full refresh");
                    JsonElement restored = await Api.ExpectAsync(f, HttpMethod.Post, _Base + "/versions/" + v1 + "/restore", HttpFixture.AdminToken, new { message = "Back" }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                    Check.Equal(1, restored.GetProperty("changeCounts").GetProperty("strings").GetInt32(), "Restore drafted the removed string");
                    web = await Manifest(f, "web", null, null, ct).ConfigureAwait(false);
                    Check.Equal("Override one", Api.Str(web.GetProperty("strings").GetProperty("upserts"), plain.Key), "Restored override");
                    JsonElement overridden = await Api.ExpectAsync(f, HttpMethod.Post, _Base + "/strings/enumerate", HttpFixture.AdminToken, new { filters = new { overridden = true, client = "web" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(1, overridden.GetProperty("objects").GetArrayLength(), "Overridden filter");
                    JsonElement labeled = await Api.ExpectAsync(f, HttpMethod.Post, _Base + "/strings/enumerate", HttpFixture.AdminToken, new { labels = new[] { "copy" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(plain.Key, Api.Str(labeled.GetProperty("objects")[0], "key"), "Label filter on the key");
                    JsonElement searched = await Api.ExpectAsync(f, HttpMethod.Post, _Base + "/strings/enumerate", HttpFixture.AdminToken, new { search = "Override one" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(searched.GetProperty("objects").EnumerateArray().Any(o => Api.Str(o, "key") == plain.Key), "Search covers override values");
                    JsonElement detail = await Api.ExpectAsync(f, HttpMethod.Get, _Base + "/strings/" + plain.Key, HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(3L, detail.GetProperty("history").GetProperty("totalRecords").GetInt64(), "History of three published revisions");
                    ApiResult page = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/app/strings/enumerate", null, new { filters = new { client = "web" } }, ct, "web").ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.OK, page.Status, "Public strings (" + page.Text + ")");
                    Check.Equal(v1 + 2, page.Body.GetProperty("contentVersion").GetInt64(), "Content version on the page");
                    Check.Equal(plain.Key, Api.Str(page.Body.GetProperty("objects")[0], "key"), "Published override listed");
                }),
                SuiteCases.Http(Id, "Banners.TargetingScheduleAndDismissal", "Banners validate, publish with targeting rules evaluated on the server, follow their schedule, archive as delta deletes, and Critical is never dismissible", async (f, ct) =>
                {
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, _Base + "/banners", HttpFixture.AdminToken, new { title = "Bad", link = new { label = "Go", path = "/a", url = "https://example.test" } }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, _Base + "/banners", HttpFixture.AdminToken, new { title = "Bad", link = new { label = "Go", url = "http://example.test" } }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, _Base + "/banners", HttpFixture.AdminToken, new { title = "Bad", rules = new[] { new { conditions = new[] { new { attribute = "planet", @operator = "In", values = new[] { "x" } } } } } }, HttpStatusCode.BadRequest, "InvalidTargetingRule", ct).ConfigureAwait(false);
                    JsonElement ios = await Api.ExpectAsync(f, HttpMethod.Post, _Base + "/banners", HttpFixture.AdminToken, new
                    {
                        labels = new[] { "maintenance", "staff:internal" }, tags = new Dictionary<string, string> { ["area"] = "service" }, title = "iOS maintenance", body = "Read-only on **Sunday**.", style = "Warning", placement = "Global", priority = 5, link = new { label = "Details", path = "/support" },
                        rules = new[] { new { conditions = new[] { new { attribute = "platform", @operator = "In", values = new[] { "ios" } } } } }
                    }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                    JsonElement critical = await Api.ExpectAsync(f, HttpMethod.Post, _Base + "/banners", HttpFixture.AdminToken, new { title = "Security notice", style = "Critical", dismissible = true, placement = "Home", priority = 9, clients = new[] { "web", "mobile" } }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                    Check.True(!critical.GetProperty("dismissible").GetBoolean(), "Critical is not dismissible");
                    await Api.ExpectAsync(f, HttpMethod.Post, _Base + "/banners", HttpFixture.AdminToken, new { title = "Later", startsUtc = DateTime.UtcNow.AddDays(2) }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Post, _Base + "/banners", HttpFixture.AdminToken, new { title = "Web only", clients = new[] { "web" } }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                    string iosId = Api.Str(ios, "id");
                    await Api.ExpectErrorAsync(f, HttpMethod.Put, _Base + "/banners/" + iosId, HttpFixture.AdminToken, new { priority = 6 }, HttpStatusCode.PreconditionFailed, "PreconditionFailed", ct).ConfigureAwait(false);
                    ApiResult edited = await Api.SendAsync(f, HttpMethod.Put, _Base + "/banners/" + iosId, HttpFixture.AdminToken, new { priority = 6 }, ct, "mobile", new Dictionary<string, string> { ["If-Match"] = Api.Str(ios, "eTag") }).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.OK, edited.Status, "Edit with If-Match (" + edited.Text + ")");
                    Check.Equal(Api.Str(ios, "contentHash"), Api.Str(edited.Body, "contentHash"), "Content hash covers title and body only");
                    await Api.ExpectAsync(f, HttpMethod.Post, _Base + "/publish", HttpFixture.AdminToken, new { message = "Banners", expectedVersion = 0 }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                    JsonElement onIos = await Manifest(f, "mobile", null, null, ct, "ios").ConfigureAwait(false);
                    List<string> iosTitles = onIos.GetProperty("banners").GetProperty("objects").EnumerateArray().Select(b => Api.Str(b, "title")).ToList();
                    Check.True(iosTitles.SequenceEqual(new[] { "Security notice", "iOS maintenance" }), "Priority order, targeting, schedule, and clients (" + String.Join(", ", iosTitles) + ")");
                    Check.True(!onIos.ToString().Contains("\"rules\"", StringComparison.Ordinal), "Rules never reach clients");
                    JsonElement onAndroid = await Manifest(f, "mobile", null, null, ct, "android").ConfigureAwait(false);
                    Check.Equal(1, onAndroid.GetProperty("banners").GetProperty("objects").GetArrayLength(), "Android misses the iOS banner");
                    JsonElement onWeb = await Manifest(f, "web", null, null, ct).ConfigureAwait(false);
                    Check.Equal(2, onWeb.GetProperty("banners").GetProperty("objects").GetArrayLength(), "Web gets the web-only banner and the critical one");
                    Dictionary<string, string> iosHeaders = new Dictionary<string, string> { ["X-Client-Platform"] = "ios" };
                    ApiResult firstBanner = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/app/banners/enumerate", null, new { maxResults = 1 }, ct, "mobile", iosHeaders).ConfigureAwait(false);
                    Check.Equal(1, firstBanner.Body.GetProperty("objects").GetArrayLength(), "Banner page cap");
                    Check.Equal(2L, firstBanner.Body.GetProperty("totalRecords").GetInt64(), "Matching active banners");
                    Check.Equal(1, firstBanner.Body.GetProperty("maxResults").GetInt32(), "Requested maximum preserved");
                    string bannerCursor = Api.Str(firstBanner.Body, "continuationToken");
                    ApiResult secondBanner = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/app/banners/enumerate", null, new { maxResults = 1, continuationToken = bannerCursor }, ct, "mobile", iosHeaders).ConfigureAwait(false);
                    Check.Equal(iosId, Api.Str(secondBanner.Body.GetProperty("objects")[0], "id"), "Continuation reaches lower priority banner");
                    Check.True(secondBanner.Body.GetProperty("endOfResults").GetBoolean(), "Banner terminal page");
                    Check.Equal(JsonValueKind.Null, secondBanner.Body.GetProperty("continuationToken").ValueKind, "No terminal cursor");
                    ApiResult skippedBanner = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/app/banners/enumerate", null, new { maxResults = 1, skip = 1 }, ct, "mobile", iosHeaders).ConfigureAwait(false);
                    Check.Equal(iosId, Api.Str(skippedBanner.Body.GetProperty("objects")[0], "id"), "Banner skip");
                    ApiResult filteredBanner = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/app/banners/enumerate", null, new { maxResults = 1, search = "maintenance", labels = new[] { "maintenance" }, tags = new Dictionary<string, string> { ["area"] = "service" } }, ct, "mobile", iosHeaders).ConfigureAwait(false);
                    Check.Equal(1L, filteredBanner.Body.GetProperty("totalRecords").GetInt64(), "Banner search and metadata apply before paging");
                    Check.True(!filteredBanner.Text.Contains("staff:internal", StringComparison.Ordinal), "Staff metadata stays hidden");
                    ApiResult wrongBannerContext = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/app/banners/enumerate", null, new { maxResults = 1, continuationToken = bannerCursor }, ct, "web").ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.BadRequest, wrongBannerContext.Status, "Banner cursors are client-scoped");
                    await Api.ExpectAsync(f, HttpMethod.Post, _Base + "/banners/" + iosId + "/archive", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Post, _Base + "/publish", HttpFixture.AdminToken, new { message = "Archive", expectedVersion = 1 }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                    JsonElement delta = await Manifest(f, "mobile", 1, null, ct, "ios").ConfigureAwait(false);
                    Check.Equal(iosId, delta.GetProperty("banners").GetProperty("deletes")[0].GetString()!, "Archived banner deleted in the delta");
                    Check.Equal(1, delta.GetProperty("banners").GetProperty("objects").GetArrayLength(), "Complete active list");
                    ApiResult page = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/app/banners/enumerate", null, new { }, ct, "mobile", new Dictionary<string, string> { ["X-Client-Platform"] = "ios" }).ConfigureAwait(false);
                    Check.Equal(1, page.Body.GetProperty("objects").GetArrayLength(), "Public banners for the caller");
                    JsonElement active = await Api.ExpectAsync(f, HttpMethod.Post, _Base + "/banners/enumerate", HttpFixture.AdminToken, new { filters = new { active = true } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(2, active.GetProperty("objects").GetArrayLength(), "Active filter excludes archived and scheduled banners");
                    await Api.ExpectErrorAsync(f, HttpMethod.Delete, _Base + "/banners/" + iosId, HttpFixture.AdminToken, null, HttpStatusCode.Conflict, "Conflict", ct).ConfigureAwait(false);
                }),
                SuiteCases.Http(Id, "Assets.UploadPublishAndServe", "Assets need content before publishing; uploads are re-encoded to WebP and served content addressed; descriptors reach the manifest; keys are unique", async (f, ct) =>
                {
                    JsonElement asset = await Api.ExpectAsync(f, HttpMethod.Post, _Base + "/assets", HttpFixture.AdminToken, new { key = "onboarding.welcome", altText = "Members at an event" }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                    string id = Api.Str(asset, "id");
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, _Base + "/assets", HttpFixture.AdminToken, new { key = "onboarding.welcome" }, HttpStatusCode.Conflict, "Conflict", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, _Base + "/publish", HttpFixture.AdminToken, new { message = "Empty", expectedVersion = 0 }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    ByteArrayContent text = new ByteArrayContent(Encoding.UTF8.GetBytes("not an image"));
                    text.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
                    ApiResult wrong = await Api.SendAsync(f, HttpMethod.Put, _Base + "/assets/" + id + "/content", HttpFixture.AdminToken, null, ct, "web", null, text).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.BadRequest, wrong.Status, "Only images");
                    ByteArrayContent png = new ByteArrayContent(Convert.FromBase64String(_Png));
                    png.Headers.ContentType = new MediaTypeHeaderValue("image/png");
                    ApiResult uploaded = await Api.SendAsync(f, HttpMethod.Put, _Base + "/assets/" + id + "/content", HttpFixture.AdminToken, null, ct, "web", null, png).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.OK, uploaded.Status, "Upload (" + uploaded.Text + ")");
                    Check.Equal("image/webp", Api.Str(uploaded.Body, "contentType"), "Re-encoded to WebP");
                    Check.Equal(1, uploaded.Body.GetProperty("width").GetInt32(), "Width");
                    string url = Api.Str(uploaded.Body, "url");
                    string sha = Api.Str(uploaded.Body, "sha256");
                    Check.True(url.EndsWith("/api/v1/app/content-assets/" + f.Property.Id + "/" + sha, StringComparison.Ordinal), "Content addressed URL");
                    using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, new Uri(url).PathAndQuery))
                    {
                        f.Decorate(request);
                        using (HttpResponseMessage response = await f.Client.SendAsync(request, ct).ConfigureAwait(false))
                        {
                            Check.Equal(HttpStatusCode.OK, response.StatusCode, "Served");
                            Check.Equal("image/webp", response.Content.Headers.ContentType?.MediaType, "Content type");
                            Check.True(response.Headers.CacheControl != null && response.Headers.CacheControl.ToString().Contains("immutable", StringComparison.Ordinal), "Immutable caching");
                        }
                    }
                    using (HttpRequestMessage partial = new HttpRequestMessage(HttpMethod.Get, new Uri(url).PathAndQuery))
                    {
                        f.Decorate(partial);
                        partial.Headers.Range = new RangeHeaderValue(0, 3);
                        using (HttpResponseMessage response = await f.Client.SendAsync(partial, ct).ConfigureAwait(false))
                        {
                            Check.Equal(HttpStatusCode.PartialContent, response.StatusCode, "Range answers 206");
                            byte[] slice = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
                            Check.Equal(4, slice.Length, "Four bytes");
                            Check.True(response.Content.Headers.ContentRange != null && response.Content.Headers.ContentRange.From == 0 && response.Content.Headers.ContentRange.To == 3, "Content-Range");
                            Check.True(Encoding.ASCII.GetString(slice) == "RIFF", "The first bytes of the WebP file");
                        }
                    }
                    using (HttpRequestMessage outside = new HttpRequestMessage(HttpMethod.Get, new Uri(url).PathAndQuery))
                    {
                        f.Decorate(outside);
                        outside.Headers.Range = new RangeHeaderValue(10000000, null);
                        using (HttpResponseMessage response = await f.Client.SendAsync(outside, ct).ConfigureAwait(false))
                            Check.Equal(HttpStatusCode.RequestedRangeNotSatisfiable, response.StatusCode, "A range outside the asset answers 416");
                    }
                    ApiResult unknown = await Api.SendAsync(f, HttpMethod.Get, "/api/v1/app/content-assets/" + f.Property.Id + "/" + new string('a', 64), null, null, ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.NotFound, unknown.Status, "Unknown hash");
                    await Api.ExpectAsync(f, HttpMethod.Post, _Base + "/publish", HttpFixture.AdminToken, new { message = "Assets", expectedVersion = 0 }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                    JsonElement manifest = await Manifest(f, "web", null, null, ct).ConfigureAwait(false);
                    JsonElement descriptor = manifest.GetProperty("assets").GetProperty("upserts")[0];
                    Check.Equal("onboarding.welcome", Api.Str(descriptor, "key"), "Descriptor key");
                    Check.Equal(sha, Api.Str(descriptor, "sha256"), "Descriptor hash");
                    JsonElement assets = await Api.ExpectAsync(f, HttpMethod.Post, _Base + "/assets/enumerate", HttpFixture.AdminToken, new { filters = new { state = "Published" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(1, assets.GetProperty("objects").GetArrayLength(), "State filter");
                    await Api.ExpectErrorAsync(f, HttpMethod.Delete, _Base + "/assets/" + id, HttpFixture.AdminToken, null, HttpStatusCode.Conflict, "Conflict", ct).ConfigureAwait(false);
                    ApiResult page = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/app/assets/enumerate", null, new { }, ct, "web").ConfigureAwait(false);
                    Check.Equal(1, page.Body.GetProperty("objects").GetArrayLength(), "Public assets");
                }),
                SuiteCases.Http(Id, "Manifest.FullRefresh", "A since older than the retained change log, or ahead of the current version, answers fullRefresh with null content parts", async (f, ct) =>
                {
                    StringCatalogEntry entry = AppContentService.Catalog().Entries.First(e => e.Client == "web" && e.Tokens.Count == 0);
                    for (int v = 0; v < 3; v++)
                    {
                        await Api.ExpectAsync(f, HttpMethod.Put, _Base + "/strings/" + entry.Key, HttpFixture.AdminToken, new { value = "Pass " + v }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                        await Api.ExpectAsync(f, HttpMethod.Post, _Base + "/publish", HttpFixture.AdminToken, new { message = "Pass " + v, expectedVersion = v }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                    }
                    await f.Raw.ExecuteQueryAsync("DELETE FROM appcontentchanges WHERE propertyid = '" + f.Property.Id + "' AND version < 3;", false, ct).ConfigureAwait(false);
                    JsonElement stale = await Manifest(f, "web", 1, null, ct).ConfigureAwait(false);
                    Check.True(stale.GetProperty("fullRefresh").GetBoolean(), "Older than the change log");
                    Check.True(stale.GetProperty("strings").ValueKind == JsonValueKind.Null, "Parts omitted");
                    JsonElement ahead = await Manifest(f, "web", 99, null, ct).ConfigureAwait(false);
                    Check.True(ahead.GetProperty("fullRefresh").GetBoolean(), "Ahead of the server");
                    JsonElement recent = await Manifest(f, "web", 2, null, ct).ConfigureAwait(false);
                    Check.True(!recent.GetProperty("fullRefresh").GetBoolean(), "Inside the change log");
                    Check.Equal("Pass 2", Api.Str(recent.GetProperty("strings").GetProperty("upserts"), entry.Key), "Delta upsert");
                }),
                SuiteCases.Http(Id, "ReleaseManager.Access", "ReleaseManager manages releases, flags, kill switches, and content without full admin rights; signing and deploy keys and other admin routes stay SystemAdmin only", async (f, ct) =>
                {
                    TestMember manager = await RoleAsync(f, RoleEnum.ReleaseManager, ct).ConfigureAwait(false);
                    TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    foreach (string path in new[] { "/api/v1/admin/update-groups/enumerate", "/api/v1/admin/updates/channels/enumerate", "/api/v1/admin/updates/runtime-versions/enumerate", _Base + "/flags/enumerate", _Base + "/kill-switches/enumerate", _Base + "/strings/enumerate", _Base + "/banners/enumerate", _Base + "/assets/enumerate", _Base + "/versions/enumerate" })
                    {
                        ApiResult allowed = await Api.SendAsync(f, HttpMethod.Post, path, manager.AccessToken, new { }, ct).ConfigureAwait(false);
                        Check.Equal(HttpStatusCode.OK, allowed.Status, "ReleaseManager reaches " + path + " (" + allowed.Text + ")");
                        ApiResult denied = await Api.SendAsync(f, HttpMethod.Post, path, member.AccessToken, new { }, ct).ConfigureAwait(false);
                        Check.Equal(HttpStatusCode.Forbidden, denied.Status, "A member does not reach " + path);
                    }
                    await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/updates/health", manager.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    JsonElement flag = await Api.ExpectAsync(f, HttpMethod.Post, _Base + "/flags", manager.AccessToken, new { key = "release.managed", name = "Managed", kind = "Boolean", defaultValue = false, offValue = false, labels = new[] { "staff:release" } }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                    Check.Equal("staff:release", flag.GetProperty("labels")[0].GetString()!, "Staff labels on release objects");
                    await Api.ExpectAsync(f, HttpMethod.Post, _Base + "/publish", manager.AccessToken, new { message = "By the release manager", expectedVersion = 0 }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Put, _Base + "/kill-switches/polls", manager.AccessToken, new { killed = true, reason = "Drill" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    foreach (string path in new[] { "/api/v1/admin/updates/signing-keys/enumerate", "/api/v1/admin/updates/deploy-keys/enumerate", "/api/v1/admin/members/enumerate" })
                    {
                        ApiResult denied = await Api.SendAsync(f, HttpMethod.Post, path, manager.AccessToken, new { }, ct).ConfigureAwait(false);
                        Check.Equal(HttpStatusCode.Forbidden, denied.Status, "ReleaseManager cannot reach " + path);
                    }
                    JsonElement me = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/auth/me", manager.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(me.GetProperty("roles").EnumerateArray().Any(r => Api.Str(r, "role") == "ReleaseManager"), "Me lists the role");
                    Check.True(AuthorizationService.IsReleasePath("/api/v1/admin/app/flags") && !AuthorizationService.IsReleasePath("/api/v1/admin/updates/signing-keys/x/retire"), "Path rules");
                }),
                SuiteCases.Http(Id, "Targeting.ProfileFieldManifest", "A profileField rule resolves from the signed-in member's filterable field value; non-filterable fields are rejected", async (f, ct) =>
                {
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, _Base + "/flags", HttpFixture.AdminToken, new
                    {
                        key = "club.panel", name = "Club", kind = "Boolean", defaultValue = false, offValue = false,
                        rules = new[] { new { conditions = new[] { new { attribute = "profileField", @operator = "Equals", argument = "city", values = new[] { "Somewhere" } } }, value = true } }
                    }, HttpStatusCode.BadRequest, "InvalidTargetingRule", ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Post, _Base + "/flags", HttpFixture.AdminToken, new
                    {
                        key = "club.panel", name = "Club", kind = "Boolean", defaultValue = false, offValue = false, clients = new[] { "web" },
                        rules = new[] { new { conditions = new[] { new { attribute = "profileField", @operator = "In", argument = "affiliation", values = new[] { "Example Club", "Other Club" } } }, value = true } }
                    }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Post, _Base + "/publish", HttpFixture.AdminToken, new { message = "Club", expectedVersion = 0 }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                    TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    TestMember other = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/me/profile", member.AccessToken, new { profile = new { affiliation = "Example Club" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True((await Manifest(f, "web", null, member.AccessToken, ct).ConfigureAwait(false)).GetProperty("flags").GetProperty("club.panel").GetBoolean(), "Matching member");
                    Check.True(!(await Manifest(f, "web", null, other.AccessToken, ct).ConfigureAwait(false)).GetProperty("flags").GetProperty("club.panel").GetBoolean(), "Member without the value");
                    Check.True(!(await Manifest(f, "web", null, null, ct).ConfigureAwait(false)).GetProperty("flags").GetProperty("club.panel").GetBoolean(), "Anonymous");
                }),
                UpdateCase("Gc.UnreferencedAssets", "UpdateAssetGcWorker deletes unreferenced assets past the retention window in bounded batches, keeps referenced and recent ones, and takes the lease", async (f, chain, ct) =>
                {
                    Dictionary<string, byte[]> kept = new Dictionary<string, byte[]>();
                    ApiResult live = await UpdateTestKit.UploadAsync(f, HttpFixture.AdminToken, UpdateTestKit.Descriptor("ios", "production", Guid.NewGuid().ToString(), "Kept", "kept", kept), kept, ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.Created, live.Status, "Live upload (" + live.Text + ")");
                    Dictionary<string, byte[]> draftParts = new Dictionary<string, byte[]>();
                    ApiResult draft = await UpdateTestKit.UploadAsync(f, HttpFixture.AdminToken, UpdateTestKit.Descriptor("ios", "production", Guid.NewGuid().ToString(), "Draft", "gone", draftParts, 0), draftParts, ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.Created, draft.Status, "Draft upload (" + draft.Text + ")");
                    await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/admin/update-groups/" + UpdateTestKit.GroupId(draft.Body), HttpFixture.AdminToken, null, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
                    List<string> orphanHashes = draftParts.Values.Select(b => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(b)).ToLowerInvariant()).ToList();
                    List<string> keptHashes = kept.Where(p => !p.Key.StartsWith("sourcemap-", StringComparison.Ordinal)).Select(p => p.Value).Select(b => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(b)).ToLowerInvariant()).ToList();
                    UpdateAssetGcWorker worker = new UpdateAssetGcWorker(f.Database, f.Settings.Updates, f.Services.LiveUpdates.Store, f.Property.Id, f.Services.Logger);
                    Check.Equal(0, await worker.CollectAsync(DateTime.UtcNow, ct).ConfigureAwait(false), "Recent orphans are kept inside the retention window");
                    await f.Raw.ExecuteQueryAsync("UPDATE updateassets SET lastreferencedutc = '2000-01-01T00:00:00Z' WHERE propertyid = '" + f.Property.Id + "';", false, ct).ConfigureAwait(false);
                    int deleted = await worker.CollectAsync(DateTime.UtcNow, ct).ConfigureAwait(false);
                    Check.True(deleted >= 2, "Unreferenced assets deleted (" + deleted + ")");
                    foreach (string hash in orphanHashes.Where(h => !keptHashes.Contains(h)))
                    {
                        ApiResult head = await Api.SendAsync(f, HttpMethod.Head, "/api/v1/admin/updates/assets/" + hash, HttpFixture.AdminToken, null, ct).ConfigureAwait(false);
                        Check.True(head.Status == HttpStatusCode.NotFound || (await f.Database.LiveUpdates.ReadAssetAsync(hash, ct).ConfigureAwait(false)) == null, "Orphan row gone");
                    }
                    foreach (string hash in keptHashes)
                        Check.True(await f.Database.LiveUpdates.ReadAssetAsync(hash, ct).ConfigureAwait(false) != null, "Referenced asset kept");
                    Check.Equal(0, await worker.CollectAsync(DateTime.UtcNow, ct).ConfigureAwait(false), "Nothing left");
                    UpdateAssetGcWorker other = new UpdateAssetGcWorker(f.Database, f.Settings.Updates, f.Services.LiveUpdates.Store, f.Property.Id, f.Services.Logger);
                    Check.True(await f.Database.JobLeases.TryAcquireAsync(UpdateAssetGcWorker.Name, "someone-else", DateTime.UtcNow, TimeSpan.FromMinutes(5), ct).ConfigureAwait(false), "Lease taken elsewhere");
                    Check.Equal(0, await other.CollectAsync(DateTime.UtcNow, ct).ConfigureAwait(false), "A held lease skips the pass");
                }),
                UpdateCase("Gc.SourceMapsAndContentAssets", "Deleting a draft update group queues its private source maps and every stored content image is queued; past the retention window UpdateAssetGcWorker deletes the unreferenced objects in bounded batches and keeps maps and images still referenced", async (f, chain, ct) =>
                {
                    Dictionary<string, byte[]> kept = new Dictionary<string, byte[]>();
                    ApiResult live = await UpdateTestKit.UploadAsync(f, HttpFixture.AdminToken, UpdateTestKit.Descriptor("ios", "production", Guid.NewGuid().ToString(), "Kept", "keptmap", kept), kept, ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.Created, live.Status, "Live upload (" + live.Text + ")");
                    Dictionary<string, byte[]> draftParts = new Dictionary<string, byte[]>();
                    ApiResult draft = await UpdateTestKit.UploadAsync(f, HttpFixture.AdminToken, UpdateTestKit.Descriptor("ios", "production", Guid.NewGuid().ToString(), "Draft", "draftmap", draftParts, 0), draftParts, ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.Created, draft.Status, "Draft upload (" + draft.Text + ")");
                    string Hash(byte[] b) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(b)).ToLowerInvariant();
                    string draftMap = Hash(draftParts.First(p => p.Key.StartsWith("sourcemap-", StringComparison.Ordinal)).Value);
                    string liveMap = Hash(kept.First(p => p.Key.StartsWith("sourcemap-", StringComparison.Ordinal)).Value);
                    string draftKey = UpdateObjectStore.SourceMapKey(f.Property.Id, draftMap);
                    string liveKey = UpdateObjectStore.SourceMapKey(f.Property.Id, liveMap);
                    Check.True(await f.Services.LiveUpdates.Store.HeadAsync(draftKey, ct).ConfigureAwait(false) != null, "Draft source map stored");
                    await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/admin/update-groups/" + UpdateTestKit.GroupId(draft.Body), HttpFixture.AdminToken, null, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
                    Check.Equal(1L, await ContentTestKit.ScalarAsync(f, "SELECT COUNT(*) AS cnt FROM updategcobjects WHERE kind = 'SourceMap' AND sha256hex = '" + draftMap + "';", ct).ConfigureAwait(false), "Deleted draft's source map queued");

                    JsonElement asset = await Api.ExpectAsync(f, HttpMethod.Post, _Base + "/assets", HttpFixture.AdminToken, new { key = "gc.hero", altText = "Hero" }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                    string assetId = Api.Str(asset, "id");
                    ByteArrayContent first = new ByteArrayContent(Convert.FromBase64String(_Png));
                    first.Headers.ContentType = new MediaTypeHeaderValue("image/png");
                    ApiResult replaced = await Api.SendAsync(f, HttpMethod.Put, _Base + "/assets/" + assetId + "/content", HttpFixture.AdminToken, null, ct, "web", null, first).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.OK, replaced.Status, "First upload (" + replaced.Text + ")");
                    string firstSha = Api.Str(replaced.Body, "sha256");
                    ByteArrayContent second = new ByteArrayContent(MediaTestKit.Png(4, 3));
                    second.Headers.ContentType = new MediaTypeHeaderValue("image/png");
                    ApiResult current = await Api.SendAsync(f, HttpMethod.Put, _Base + "/assets/" + assetId + "/content", HttpFixture.AdminToken, null, ct, "web", null, second).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.OK, current.Status, "Second upload (" + current.Text + ")");
                    string secondSha = Api.Str(current.Body, "sha256");
                    Check.True(firstSha != secondSha, "Different images");
                    Check.Equal(2L, await ContentTestKit.ScalarAsync(f, "SELECT COUNT(*) AS cnt FROM updategcobjects WHERE kind = 'ContentAsset' AND sha256hex IN ('" + firstSha + "', '" + secondSha + "');", ct).ConfigureAwait(false), "Both stored images queued");

                    UpdateAssetGcWorker worker = new UpdateAssetGcWorker(f.Database, f.Settings.Updates, f.Services.LiveUpdates.Store, f.Property.Id, f.Services.Logger);
                    await worker.CollectAsync(DateTime.UtcNow, ct).ConfigureAwait(false);
                    Check.True(await f.Services.LiveUpdates.Store.HeadAsync(draftKey, ct).ConfigureAwait(false) != null, "Kept inside the retention window");
                    Check.True(await f.Services.LiveUpdates.Store.HeadAsync(AppContentService.ContentAssetKey(f.Property.Id, firstSha), ct).ConfigureAwait(false) != null, "Replaced image kept inside the retention window");
                    f.Settings.Updates.AssetGcBatchSize = 1;
                    DateTime later = DateTime.UtcNow.AddDays(f.Settings.Updates.AssetGcRetentionDays + 1);
                    int deleted = await worker.CollectAsync(later, ct).ConfigureAwait(false);
                    Check.True(deleted >= 2, "Unreferenced map and image deleted in batches of one (" + deleted + ")");
                    Check.True(await f.Services.LiveUpdates.Store.HeadAsync(draftKey, ct).ConfigureAwait(false) == null, "Deleted draft's source map object removed");
                    Check.True(await f.Services.LiveUpdates.Store.HeadAsync(liveKey, ct).ConfigureAwait(false) != null, "A live update's source map is kept");
                    Check.True(await f.Services.LiveUpdates.Store.HeadAsync(AppContentService.ContentAssetKey(f.Property.Id, firstSha), ct).ConfigureAwait(false) == null, "The replaced image is removed");
                    Check.True(await f.Services.LiveUpdates.Store.HeadAsync(AppContentService.ContentAssetKey(f.Property.Id, secondSha), ct).ConfigureAwait(false) != null, "The draft's current image is kept");
                    Check.Equal(1L, await ContentTestKit.ScalarAsync(f, "SELECT COUNT(*) AS cnt FROM updategcobjects WHERE kind = 'ContentAsset' AND sha256hex = '" + secondSha + "' AND queuedutc > now() + interval '1 day';", ct).ConfigureAwait(false), "A referenced image restarts its wait");
                    Check.Equal(0, await worker.CollectAsync(later, ct).ConfigureAwait(false), "Nothing left to collect");
                }),
                UpdateCase("Symbolication.ReportedFailures", "A launch failure with a stack trace is stored privately, listed per update, and symbolicated with the update's source map by event or pasted text", async (f, chain, ct) =>
                {
                    Dictionary<string, byte[]> parts = new Dictionary<string, byte[]>();
                    Dictionary<string, object?> descriptor = UpdateTestKit.Descriptor("android", "production", Guid.NewGuid().ToString(), "Mapped", "mapped", parts);
                    Dictionary<string, object?> oldMap = (Dictionary<string, object?>)((List<object?>)descriptor["sourceMaps"]!)[0]!;
                    parts.Remove((string)oldMap["part"]!);
                    byte[] map = Encoding.UTF8.GetBytes("{\"version\":3,\"sources\":[\"src/feed/Composer.jsx\"],\"names\":[\"submit\"],\"mappings\":\"" + Segment(0, 0, 0, 0, 0) + "," + Segment(100, 0, 40, 6, 0) + "\"}");
                    Dictionary<string, object?> newMap = UpdateTestKit.Describe(map, (string)oldMap["path"]!, "map", "application/json");
                    newMap.Remove("fileExtension");
                    newMap["part"] = "sourcemap-" + newMap["key"];
                    newMap["bundleKey"] = oldMap["bundleKey"];
                    parts[(string)newMap["part"]!] = map;
                    descriptor["sourceMaps"] = new List<object?> { newMap };
                    ApiResult created = await UpdateTestKit.UploadAsync(f, HttpFixture.AdminToken, descriptor, parts, ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.Created, created.Status, "Upload (" + created.Text + ")");
                    JsonElement update = created.Body.GetProperty("updates")[0];
                    string updateId = Api.Str(update, "id");
                    string uuid = Api.Str(update, "uuid");
                    string stack = "Error: boom\n    at submit (address at index.android.bundle:1:120)";
                    ApiResult events = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/updates/events", null, new
                    {
                        events = new object[]
                        {
                            new { type = "LaunchFailed", updateId = uuid, occurredUtc = DateTime.UtcNow, message = "boom", stackTrace = stack },
                            new { type = "Downloaded", updateId = uuid, occurredUtc = DateTime.UtcNow, message = "ignored", stackTrace = "ignored" }
                        }
                    }, ct, "mobile", new Dictionary<string, string> { ["X-TK-Install-Id"] = "install-symbolication", ["X-TK-Property"] = f.Property.Id }).ConfigureAwait(false);
                    Check.Equal((HttpStatusCode)202, events.Status, "Events accepted (" + events.Text + ")");
                    JsonElement failures = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/updates/" + updateId + "/failures/enumerate", HttpFixture.AdminToken, new { filters = new { type = "LaunchFailed" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(1, failures.GetProperty("objects").GetArrayLength(), "Only failure events keep details");
                    JsonElement failure = failures.GetProperty("objects")[0];
                    Check.True(failure.GetProperty("hasStackTrace").GetBoolean() && Api.Str(failure, "message") == "boom", "Message and stack flag");
                    Check.True(!failure.ToString().Contains("address at", StringComparison.Ordinal), "The list never carries the stack itself");
                    JsonElement byEvent = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/updates/" + updateId + "/symbolicate", HttpFixture.AdminToken, new { eventId = Api.Str(failure, "id") }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(byEvent.GetProperty("sourceMapAvailable").GetBoolean(), "Source map found");
                    JsonElement original = byEvent.GetProperty("frames")[1].GetProperty("original");
                    Check.Equal("src/feed/Composer.jsx", Api.Str(original, "source"), "Original source");
                    Check.Equal(41, original.GetProperty("line").GetInt32(), "Original line");
                    Check.True(Api.Str(byEvent, "text").Contains("src/feed/Composer.jsx:41:7", StringComparison.Ordinal), "Symbolicated text");
                    JsonElement pasted = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/updates/" + updateId + "/symbolicate", HttpFixture.AdminToken, new { stackTrace = "at x (index.android.bundle:1:1)" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(1, pasted.GetProperty("frames")[0].GetProperty("original").GetProperty("line").GetInt32(), "Pasted trace");
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/updates/" + updateId + "/symbolicate", HttpFixture.AdminToken, new { }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/updates/" + updateId + "/symbolicate", HttpFixture.AdminToken, new { eventId = "uev_nope" }, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
                    ApiResult anonymous = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/admin/updates/" + updateId + "/symbolicate", null, new { stackTrace = stack }, ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.Unauthorized, anonymous.Status, "Admin only");
                }),
                UpdateCase("Targeting.RuntimeVersionFromInstall", "runtimeVersion conditions read the install row the update manifest endpoint keeps for X-TK-Install-Id", async (f, chain, ct) =>
                {
                    await UpdateTestKit.ManifestAsync(f, ct, "ios", "preview", "install-runtime-a").ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Post, _Base + "/flags", HttpFixture.AdminToken, new
                    {
                        key = "runtime.gate", name = "Runtime gate", kind = "Boolean", defaultValue = false, offValue = false, clients = new[] { "mobile" },
                        rules = new[] { new { conditions = new object[] { new { attribute = "runtimeVersion", @operator = "In", values = new[] { UpdateTestKit.Runtime } }, new { attribute = "channel", @operator = "In", values = new[] { "preview" } } }, value = true } }
                    }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Post, _Base + "/publish", HttpFixture.AdminToken, new { message = "Runtime", expectedVersion = 0 }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                    ApiResult known = await Api.SendAsync(f, HttpMethod.Get, "/api/v1/app/manifest", null, null, ct, "mobile", new Dictionary<string, string> { ["X-Client-Platform"] = "ios", ["X-TK-Install-Id"] = "install-runtime-a" }).ConfigureAwait(false);
                    Check.True(known.Body.GetProperty("flags").GetProperty("runtime.gate").GetBoolean(), "Install row supplies the runtime version and channel (" + known.Text + ")");
                    ApiResult unknown = await Api.SendAsync(f, HttpMethod.Get, "/api/v1/app/manifest", null, null, ct, "mobile", new Dictionary<string, string> { ["X-Client-Platform"] = "ios", ["X-TK-Install-Id"] = "install-runtime-b" }).ConfigureAwait(false);
                    Check.True(!unknown.Body.GetProperty("flags").GetProperty("runtime.gate").GetBoolean(), "Unknown install does not match");
                })
            });
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor UpdateCase(string caseId, string name, Func<HttpFixture, TestSigningChain, CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(Id, caseId, name, async ct =>
            {
                TestSigningChain chain = new TestSigningChain();
                HttpFixture fixture = await UpdateTestKit.StartAsync(ct, chain).ConfigureAwait(false);
                await using (fixture.ConfigureAwait(false))
                {
                    await body(fixture, chain, ct).ConfigureAwait(false);
                }
            }, new[] { "http", "integration", TestEnvironment.Provider.ToString().ToLowerInvariant() });
        }

        private static async Task<TestMember> RoleAsync(HttpFixture f, RoleEnum role, CancellationToken ct)
        {
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/members/" + member.Id + "/roles", HttpFixture.AdminToken, new { roles = new object[] { new { role = role.ToString() } } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            f.Services.Tokens.InvalidateUser(member.Id);
            return member;
        }

        private static async Task<JsonElement> Manifest(HttpFixture f, string client, long? since, string? bearer, CancellationToken ct, string platform = "ios")
        {
            Dictionary<string, string> headers = new Dictionary<string, string> { ["X-TK-Install-Id"] = "install-" + client + "-" + platform };
            if (client == "mobile") headers["X-Client-Platform"] = platform;
            string path = "/api/v1/app/manifest" + (since == null ? String.Empty : "?since=" + since.Value.ToString(CultureInfo.InvariantCulture));
            ApiResult result = await Api.SendAsync(f, HttpMethod.Get, path, bearer, null, ct, client, headers).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.OK, result.Status, "Manifest (" + result.Text + ")");
            return result.Body;
        }

        private static FeatureFlagCondition Condition(string attribute, string op, string? argument, params string[] values)
        {
            return new FeatureFlagCondition { Attribute = attribute, Operator = op, Argument = argument, Values = values.ToList() };
        }

        private static FeatureFlagRule Rule(FeatureFlagCondition condition)
        {
            return new FeatureFlagRule { Id = "ffr_t", Position = 1, Value = "true", RolloutBasisPoints = 10000, Conditions = new List<FeatureFlagCondition> { condition } };
        }

        private static string Evaluate(FeatureFlagCondition condition, FlagEvaluationContext context)
        {
            FeatureFlag flag = new FeatureFlag { Key = "t.flag", Enabled = true, DefaultValue = "false", OffValue = "false", Rules = new List<FeatureFlagRule> { Rule(condition) } };
            return FeatureFlagEvaluator.Evaluate(flag, context).Value;
        }

        private static string Segment(params int[] fields)
        {
            StringBuilder builder = new StringBuilder();
            foreach (int field in fields)
            {
                int value = field < 0 ? ((-field) << 1) | 1 : field << 1;
                do
                {
                    int digit = value & 31;
                    value >>= 5;
                    if (value > 0) digit |= 32;
                    builder.Append(_Base64[digit]);
                }
                while (value > 0);
            }
            return builder.ToString();
        }

        #endregion
    }
}

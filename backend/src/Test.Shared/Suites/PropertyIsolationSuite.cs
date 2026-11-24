namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Touchstone.Core;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Helpers;

    /// <summary>
    /// Cross-property isolation (docs/MULTIPROPERTY_CONTRACT.md section 4.5). Starts the server in MultiProperty mode
    /// with the sample property (A) and a second test-local property (B), fills both with the same kinds of data (B's
    /// free text carries a marker), then: sweeps every parameterized route in the OpenAPI document as A's staff and A's
    /// member with B's object ids (no 2xx may carry B data, no 5xx), scans every enumeration of A for B's markers and
    /// ids, checks that B's rows are unchanged afterwards, and covers token binding, separate accounts, body references,
    /// continuation tokens, and the static guards. The sweep must reach every route group: feed, media, comments,
    /// notifications, moderation, metadata, enumeration, members, subscriptions, codes, audit, and exports.
    /// </summary>
    public static class PropertyIsolationSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "PropertyIsolation";

        #endregion

        #region Private-Members

        private static readonly Dictionary<string, string> _Groups = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "feed", "/api/v1/feed" },
            { "media", "/api/v1/media" },
            { "comments", "/api/v1/comments" },
            { "notifications", "/notifications" },
            { "moderation", "/api/v1/admin/moderation" },
            { "metadata", "/api/v1/metadata" },
            { "enumeration", "/enumerate" },
            { "members", "/api/v1/admin/members" },
            { "subscriptions", "/api/v1/admin/subscriptions" },
            { "codes", "/api/v1/admin/signup-codes" },
            { "audit", "/api/v1/admin/audit" },
            { "directory", "/api/v1/venues" },
            { "exports", "/api/v1/me/export" },
            { "profile fields", "/api/v1/admin/profile-fields" },
            { "email", "/api/v1/admin/email" }
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Property isolation (MultiProperty, " + TestEnvironment.Provider + ")", new[]
            {
                SuiteCases.Http(Id, "RouteSweep.NoCrossPropertyReadsOrWrites", "Every parameterized route as A's staff and member with B's ids: no 2xx with B data, no 5xx, B's rows unchanged, every route group reached; every enumeration of A is free of B's markers and ids", MultiPropertyKit.Multi, async (f, ct) =>
                {
                    IsolationWorld w = await WorldAsync(f, ct).ConfigureAwait(false);
                    // Usage rows exist in both properties, so the sweep also proves A's usage routes never read or change B's.
                    foreach (string usageProperty in new[] { w.A, w.B })
                        await f.Server.Directory.Find(usageProperty)!.Services.Usage.RunPassAsync(DateTime.UtcNow, ct).ConfigureAwait(false);
                    Dictionary<string, string> before = await SnapshotAsync(f, w.B, ct).ConfigureAwait(false);

                    f.PropertyId = w.A;
                    JsonElement doc = await Api.ExpectAsync(f, HttpMethod.Get, "/openapi.json", null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    List<string> problems = new List<string>();
                    HashSet<string> groups = new HashSet<string>(StringComparer.Ordinal);
                    int swept = 0;
                    List<string> foreign = w.ForeignIds();

                    // Enumerations first: A's own request history later holds A's sweep URLs with B's ids.
                    int enumerations = 0;
                    foreach (JsonProperty path in doc.GetProperty("paths").EnumerateObject())
                    {
                        if (!path.Name.EndsWith("/enumerate", StringComparison.Ordinal) || path.Name.Contains('{') || Skipped(path.Name) || !path.Value.TryGetProperty("post", out JsonElement _)) continue;
                        foreach (string bearer in new[] { HttpFixture.AdminToken, w.AMember })
                        {
                            ApiResult result = await Api.SendAsync(f, HttpMethod.Post, path.Name, bearer, new { key = "isolation", maxResults = 100 }, ct).ConfigureAwait(false);
                            enumerations++;
                            Inspect(w, "POST " + path.Name, result, null, problems);
                        }
                        groups.Add("enumeration");
                        foreach (KeyValuePair<string, string> g in _Groups)
                            if (path.Name.Contains(g.Value, StringComparison.Ordinal)) groups.Add(g.Key);
                    }

                    foreach (JsonProperty path in doc.GetProperty("paths").EnumerateObject())
                    {
                        if (!path.Name.Contains('{') || Skipped(path.Name)) continue;
                        foreach (JsonProperty operation in path.Value.EnumerateObject())
                        {
                            HttpMethod? method = MethodOf(operation.Name);
                            if (method == null) continue;
                            foreach (string bId in foreign)
                            {
                                string route = System.Text.RegularExpressions.Regex.Replace(path.Name, "\\{[^}]+\\}", bId);
                                foreach (string bearer in new[] { HttpFixture.AdminToken, w.AMember })
                                {
                                    object? body = method == HttpMethod.Get || method == HttpMethod.Delete ? null : new { body = "ISOLATION-A probe", reason = "Spam" };
                                    ApiResult result = await Api.SendAsync(f, method, route, bearer, body, ct).ConfigureAwait(false);
                                    swept++;
                                    Inspect(w, method + " " + path.Name + " (" + bId + ")", result, bId, problems);
                                }
                            }
                            foreach (KeyValuePair<string, string> g in _Groups)
                                if (path.Name.Contains(g.Value, StringComparison.Ordinal)) groups.Add(g.Key);
                        }
                    }

                    // Routes that take B's ids in the body (notification read state, bulk metadata).
                    foreach (string route in new[] { "/api/v1/notifications/read", "/api/v1/notifications/unread" })
                    {
                        ApiResult result = await Api.SendAsync(f, HttpMethod.Post, route, w.AMember, new { ids = foreign }, ct).ConfigureAwait(false);
                        Inspect(w, "POST " + route + " (B ids)", result, "*", problems);
                    }
                    ApiResult bulk = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/admin/metadata/bulk", HttpFixture.AdminToken, new { targetType = "FeedPost", targetIds = foreign, addLabels = new[] { "isolation-a" } }, ct).ConfigureAwait(false);
                    Inspect(w, "POST /api/v1/admin/metadata/bulk (B ids)", bulk, "*", problems);

                    Dictionary<string, string> after = await SnapshotAsync(f, w.B, ct).ConfigureAwait(false);
                    foreach (KeyValuePair<string, string> table in before)
                        if (after[table.Key] != table.Value) problems.Add("B's " + table.Key + " rows changed during the sweep");

                    Check.True(swept > 500, "Swept parameterized operations (" + swept + ")");
                    Check.True(enumerations > 30, "Scanned enumerations (" + enumerations + ")");
                    foreach (string g in _Groups.Keys) Check.True(groups.Contains(g), "Route group reached: " + g);
                    Check.True(problems.Count == 0, "Isolation problems:\n  " + String.Join("\n  ", problems.Take(60)));
                    await NoStrayRowsAsync(f, w, ct).ConfigureAwait(false);
                }),

                SuiteCases.Http(Id, "BodyReferences.ResolveOnlyInTheProperty", "B's media, post, member, and sign-up code named in A's request bodies are not found, never attached, reported, or redeemed", MultiPropertyKit.Multi, async (f, ct) =>
                {
                    IsolationWorld w = await WorldAsync(f, ct).ConfigureAwait(false);
                    f.PropertyId = w.A;
                    ApiResult attach = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/feed", w.APoster, new { body = "ISOLATION-A attach", mediaIds = new[] { w.BMedia } }, ct).ConfigureAwait(false);
                    Check.True((int)attach.Status >= 400 && (int)attach.Status < 500, "Attaching B's media fails (" + (int)attach.Status + " " + attach.Text + ")");
                    ApiResult report = await ModerationTestKit.ReportAsync(f, w.AMember, "FeedPost", w.BPost, ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.NotFound, report.Status, "Reporting B's post (" + report.Text + ")");
                    ApiResult reportUser = await ModerationTestKit.ReportAsync(f, w.AMember, "User", w.BMemberId, ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.NotFound, reportUser.Status, "Reporting B's member (" + reportUser.Text + ")");
                    ApiResult redeem = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/signup-codes/redeem", w.AMember, new { code = w.BCode }, ct).ConfigureAwait(false);
                    Check.True(redeem.Status != HttpStatusCode.OK, "Redeeming B's code in A fails (" + (int)redeem.Status + ")");
                    ApiResult segmentRef = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/admin/broadcasts", HttpFixture.AdminToken, new { title = "ISOLATION-A broadcast", inApp = new { body = "x" }, segmentId = w.BSegment }, ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.NotFound, segmentRef.Status, "A broadcast in A cannot use B's segment (" + segmentRef.Text + ")");
                    ApiResult previewRef = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/admin/broadcasts/segments/preview", HttpFixture.AdminToken, new { segmentId = w.BSegment }, ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.NotFound, previewRef.Status, "A cannot preview B's segment (" + previewRef.Text + ")");
                    ApiResult announcement = await Api.SendAsync(f, HttpMethod.Get, "/api/v1/announcements/" + w.BBroadcast, w.AMember, null, ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.NotFound, announcement.Status, "A's member cannot read B's announcement");
                    ApiResult mention = await Api.SendAsync(f, HttpMethod.Get, "/api/v1/members/search?q=" + Uri.EscapeDataString(w.BMarker.Substring(0, 12)), w.AMember, null, ct).ConfigureAwait(false);
                    Check.True(!mention.Text.Contains(w.BMarker, StringComparison.Ordinal), "Member search in A never returns B's members");
                }),

                SuiteCases.Http(Id, "Accounts.SeparatePerProperty", "The same email registers in both properties with different passwords; deleting the account in A leaves B's", MultiPropertyKit.Multi, async (f, ct) =>
                {
                    string platform = await MultiPropertyKit.PlatformLoginAsync(f, ct).ConfigureAwait(false);
                    string b = await MultiPropertyKit.CreateSecondAsync(f, platform, ct).ConfigureAwait(false);
                    string email = Api.UniqueEmail("both");
                    f.PropertyId = TestProperty.SampleId;
                    TestMember inA = await Api.RegisterAsync(f, ct, email).ConfigureAwait(false);
                    f.PropertyId = b;
                    TestMember inB = new TestMember { Email = email, Password = "a different horse battery" };
                    JsonElement registered = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/auth/register", null,
                        new { email, password = inB.Password, displayName = "Same Email", dateOfBirth = "1990-01-01", acceptTerms = true, acceptedDocuments = Api.SignupDocuments }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                    inB.Id = registered.GetProperty("user").GetProperty("id").GetString()!;
                    Check.True(inA.Id != inB.Id, "Two different accounts");
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/auth/login", null, new { email, password = Api.Password }, HttpStatusCode.Unauthorized, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/auth/login", null, new { email, password = inB.Password }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    f.PropertyId = TestProperty.SampleId;
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/auth/login", null, new { email, password = inB.Password }, HttpStatusCode.Unauthorized, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/me", inA.AccessToken, new { confirm = "DELETE" }, HttpStatusCode.Accepted, ct).ConfigureAwait(false);
                    f.PropertyId = b;
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/auth/login", null, new { email, password = inB.Password }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                }),

                SuiteCases.Http(Id, "Tokens.BoundToTheirProperty", "A's access token in B is 401; A's refresh token in B is 401 InvalidRefreshToken; A's token on A still works", MultiPropertyKit.Multi, async (f, ct) =>
                {
                    string platform = await MultiPropertyKit.PlatformLoginAsync(f, ct).ConfigureAwait(false);
                    string b = await MultiPropertyKit.CreateSecondAsync(f, platform, ct).ConfigureAwait(false);
                    f.PropertyId = TestProperty.SampleId;
                    TestMember a = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/auth/me", a.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    f.PropertyId = b;
                    await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/auth/me", a.AccessToken, null, HttpStatusCode.Unauthorized, ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/auth/refresh", null, new { refreshToken = a.RefreshToken }, HttpStatusCode.Unauthorized, "InvalidRefreshToken", ct).ConfigureAwait(false);
                    f.PropertyId = TestProperty.SampleId;
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/auth/refresh", null, new { refreshToken = a.RefreshToken }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                }),

                SuiteCases.Http(Id, "ContinuationTokens.BoundToTheirProperty", "A continuation token from A used in B is 400 InvalidContinuationToken", MultiPropertyKit.Multi, async (f, ct) =>
                {
                    string platform = await MultiPropertyKit.PlatformLoginAsync(f, ct).ConfigureAwait(false);
                    string b = await MultiPropertyKit.CreateSecondAsync(f, platform, ct).ConfigureAwait(false);
                    f.PropertyId = TestProperty.SampleId;
                    for (int i = 0; i < 3; i++) await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    JsonElement page = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/members/enumerate", HttpFixture.AdminToken, new { maxResults = 1 }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    string token = Api.Str(page, "continuationToken");
                    Check.True(token.Length > 0, "A page with more results");
                    f.PropertyId = b;
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/members/enumerate", HttpFixture.AdminToken, new { maxResults = 1, continuationToken = token }, HttpStatusCode.BadRequest, "InvalidContinuationToken", ct).ConfigureAwait(false);
                }),

                SuiteCases.Http(Id, "Media.KeysBoundToTheirProperty", "Object keys start with the property id; A cannot read B's media object through the file route", MultiPropertyKit.Multi, async (f, ct) =>
                {
                    IsolationWorld w = await WorldAsync(f, ct).ConfigureAwait(false);
                    DataTable rows = await f.Raw.ExecuteQueryAsync("SELECT sourcekey FROM mediaattachments WHERE propertyid = '" + w.B + "' AND sourcekey LIKE '" + w.B + "/%';", false, ct).ConfigureAwait(false);
                    Check.True(rows.Rows.Count > 0, "B's uploads are stored under B's prefix");
                    f.PropertyId = w.B;
                    await MediaTestKit.ProcessAsync(f, ct).ConfigureAwait(false);
                    JsonElement view = await MediaTestKit.GetMediaAsync(f, HttpFixture.AdminToken, w.BMedia, ct).ConfigureAwait(false);
                    Check.Equal("Ready", Api.Str(view, "status"), "B's media processed");
                    f.PropertyId = w.A;
                    MediaHttpResult read = await MediaTestKit.SendAsync(f, HttpMethod.Get, "/api/v1/media/files/" + w.BMedia + "/thumb.webp", w.AMember, null, ct).ConfigureAwait(false);
                    Check.True(read.Status != HttpStatusCode.OK, "A cannot read B's media file (" + (int)read.Status + ")");
                    ApiResult meta = await Api.SendAsync(f, HttpMethod.Get, "/api/v1/media/" + w.BMedia, HttpFixture.AdminToken, null, ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.NotFound, meta.Status, "A's staff cannot read B's media record");
                }),

                SuiteCases.Unit(Id, "Guard.RejectsUnscopedStatements", "The scope guard rejects statements on property-owned tables without propertyid (outside literals) and accepts scoped ones and deployment tables", ct =>
                {
                    Check.Throws<PropertyScopeException>(() => PropertyScopeGuard.Verify("SELECT * FROM users WHERE id = 'x';"), "Unscoped select");
                    Check.Throws<PropertyScopeException>(() => PropertyScopeGuard.Verify("SELECT * FROM users u JOIN feedposts f ON f.authoruserid = u.id WHERE u.propertyid = 'a';"), "Unscoped join");
                    Check.Throws<PropertyScopeException>(() => PropertyScopeGuard.Verify("UPDATE comments SET body = 'propertyid' WHERE id = 'x';"), "A literal does not count");
                    PropertyScopeGuard.Verify("SELECT * FROM users WHERE propertyid = 'a' AND id = 'x';");
                    PropertyScopeGuard.Verify("INSERT INTO labels (propertyid, id) VALUES ('a', 'b');");
                    PropertyScopeGuard.Verify("SELECT * FROM properties WHERE id = 'x';");
                    PropertyScopeGuard.Verify("SELECT * FROM jobleases;");
                    Check.True(PropertyTables.Owned.Count >= 46, "Owned tables listed");
                    return Task.CompletedTask;
                }),

                SuiteCases.Database(Id, "Driver.EveryMethodSetIsBound", "Property-owned method sets on the deployment driver refuse to run; ForProperty binds every set; reserved and malformed ids are refused", async (db, ct) =>
                {
                    await Check.ThrowsAsync<PropertyScopeException>(() => db.Deployment.Users.ReadAsync("usr_x", ct), "Deployment driver users").ConfigureAwait(false);
                    await Check.ThrowsAsync<PropertyScopeException>(() => db.Deployment.FeedPosts.ReadAsync("fpo_x", ct), "Deployment driver feed").ConfigureAwait(false);
                    Check.Throws<PropertyScopeException>(() => db.Deployment.ForProperty("all"), "Reserved id");
                    Check.Throws<PropertyScopeException>(() => db.Deployment.ForProperty("example"), "Malformed id");
                    Check.Equal(TestProperty.SampleId, db.Driver.BoundPropertyId, "Bound driver");
                    await Check.ThrowsAsync<PropertyScopeException>(() => db.Driver.ExecuteQueryAsync("SELECT id FROM users;", false, ct), "Bound driver guards raw statements").ConfigureAwait(false);
                    foreach (System.Reflection.PropertyInfo property in typeof(DatabaseDriverBase).GetProperties().Where(p => p.PropertyType.IsInterface && p.PropertyType.Name.EndsWith("Methods", StringComparison.Ordinal)))
                        Check.True(property.GetValue(db.Driver) != null, "Bound driver exposes " + property.Name);
                    string other = PropertyIds.Generate();
                    Check.True(db.Deployment.ForProperty(other).BoundPropertyId == other, "Generated ids bind");
                    Check.True(await db.Deployment.ForProperty(other).Users.ReadAsync("usr_none", ct).ConfigureAwait(false) == null, "Another property sees nothing");
                }),

                AiIsolationCases.Sweep(Id)
            });
        }

        #endregion

        #region Private-Methods

        private static bool Skipped(string path)
        {
            return path.StartsWith("/api/v1/platform", StringComparison.Ordinal) || path.StartsWith("/api/v1/webhooks", StringComparison.Ordinal) ||
                path.StartsWith("/api/v1/health", StringComparison.Ordinal) || path.Contains("/auth/", StringComparison.Ordinal);
        }

        private static HttpMethod? MethodOf(string name)
        {
            switch (name)
            {
                case "get": return HttpMethod.Get;
                case "post": return HttpMethod.Post;
                case "put": return HttpMethod.Put;
                case "patch": return HttpMethod.Patch;
                case "delete": return HttpMethod.Delete;
                default: return null;
            }
        }

        private static void Inspect(IsolationWorld w, string what, ApiResult result, string? substituted, List<string> problems)
        {
            int status = (int)result.Status;
            if (status >= 500)
            {
                problems.Add(what + ": " + status + " " + Cut(result.Text));
                return;
            }
            if (status < 200 || status >= 300) return;
            string text = result.Text ?? String.Empty;
            if (text.Contains(w.BMarker, StringComparison.Ordinal)) problems.Add(what + ": returned B's marker");
            // "*" marks a request that sent every B id itself (an echo of the request is not a leak; the row snapshot
            // proves nothing changed).
            if (substituted == "*") return;
            foreach (string id in w.ForeignIds())
                if (id != substituted && text.Contains(id, StringComparison.Ordinal)) problems.Add(what + ": returned B's id " + id);
        }

        private static string Cut(string? text)
        {
            string value = text ?? String.Empty;
            return value.Length > 200 ? value.Substring(0, 200) : value;
        }

        private static async Task<Dictionary<string, string>> SnapshotAsync(HttpFixture f, string propertyId, CancellationToken ct)
        {
            Dictionary<string, string> hashes = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string table in PropertyTables.Owned.Where(t => t != "requesthistory"))
            {
                DataTable rows = await f.Raw.ExecuteQueryAsync("SELECT COUNT(*) AS cnt, COALESCE(md5(string_agg(md5(t::text), '' ORDER BY md5(t::text))), '') AS h FROM " + table + " t WHERE t.propertyid = '" + propertyId + "';", false, ct).ConfigureAwait(false);
                hashes[table] = RowReader.GetLong(rows.Rows[0], "cnt") + ":" + RowReader.GetString(rows.Rows[0], "h");
            }
            return hashes;
        }

        private static async Task NoStrayRowsAsync(HttpFixture f, IsolationWorld w, CancellationToken ct)
        {
            foreach (string table in PropertyTables.Owned)
            {
                DataTable rows = await f.Raw.ExecuteQueryAsync("SELECT COUNT(*) AS cnt FROM " + table + " WHERE propertyid NOT IN ('" + w.A + "', '" + w.B + "', 'platform', 'unresolved');", false, ct).ConfigureAwait(false);
                Check.Equal(0L, RowReader.GetLong(rows.Rows[0], "cnt"), "No " + table + " row outside a known property");
            }
        }

        private static async Task<IsolationWorld> WorldAsync(HttpFixture f, CancellationToken ct)
        {
            IsolationWorld w = new IsolationWorld { A = TestProperty.SampleId };
            string platform = await MultiPropertyKit.PlatformLoginAsync(f, ct).ConfigureAwait(false);
            w.B = await MultiPropertyKit.CreateSecondAsync(f, platform, ct).ConfigureAwait(false);
            w.BMarker = "ISOLATION-B-" + Guid.NewGuid().ToString("N").Substring(0, 10);

            foreach (string property in new[] { w.A, w.B })
            {
                bool isB = property == w.B;
                string marker = isB ? w.BMarker : "ISOLATION-A-" + Guid.NewGuid().ToString("N").Substring(0, 10);
                f.PropertyId = property;
                TestMember poster = await ContentTestKit.PosterAsync(f, ct, marker + " poster").ConfigureAwait(false);
                TestMember member = await SafetyHelpers.MemberAsync(f, ct, marker + " member").ConfigureAwait(false);
                TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
                string post = await ContentTestKit.PostIdAsync(f, poster.AccessToken, marker + " post body", ct).ConfigureAwait(false);
                await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/metadata/FeedPost/" + post, poster.AccessToken, new { labels = new[] { "isolation", marker.ToLowerInvariant() } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                string comment = await ContentTestKit.CommentIdAsync(f, member.AccessToken, post, marker + " comment", null, ct).ConfigureAwait(false);
                await ContentTestKit.CommentIdAsync(f, poster.AccessToken, post, marker + " reply", comment, ct).ConfigureAwait(false);
                await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/feed/" + post + "/reactions/like", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                JsonElement report = await ModerationTestKit.ReportOkAsync(f, member.AccessToken, "FeedPost", post, ct).ConfigureAwait(false);
                JsonElement code = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/signup-codes", HttpFixture.AdminToken, new { name = marker + " code", grantType = "Lifetime", labels = new[] { "isolation" } }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                string media = await MediaTestKit.UploadSingleAsync(f, poster.AccessToken, "Image", "image/png", MediaTestKit.Png(8, 8), ct).ConfigureAwait(false);
                await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/moderation/word-filter", HttpFixture.AdminToken, new { term = "isolationterm" + (isB ? "b" : "a"), matchType = "Word", action = "Flag" }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/me/export", member.AccessToken, null, HttpStatusCode.Accepted, ct).ConfigureAwait(false);
                // Part 2 objects: a profile field with the same key in both properties, profile values with the marker,
                // an email record, and a suppression.
                JsonElement field = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/profile-fields", HttpFixture.AdminToken,
                    new { key = "isolationNote", label = marker + " field", type = "Text", searchable = true, filterable = true, labels = new[] { "isolation" } }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/me/profile", member.AccessToken, new { profile = new { isolationNote = marker + " value", affiliation = marker + " club" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                JsonElement suppression = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/email/suppressions", HttpFixture.AdminToken,
                    new { email = (isB ? "isolation-b-" : "isolation-a-") + Guid.NewGuid().ToString("N").Substring(0, 8) + "@example.test", note = marker + " note", labels = new[] { "isolation" } }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/email/test", HttpFixture.AdminToken, new { to = member.Email }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                JsonElement messages = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/email/messages/enumerate", HttpFixture.AdminToken, new { maxResults = 1, filters = new { template = "test" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);

                // Live updates and app configuration rows (property-owned like everything else).
                JsonElement flag = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/app/flags", HttpFixture.AdminToken, new { key = "isolation.flag", name = marker + " flag", kind = "Boolean", defaultValue = false, offValue = false, labels = new[] { "isolation" } }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                JsonElement deployKey = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/updates/deploy-keys", HttpFixture.AdminToken, new { name = marker + " ci", channels = new[] { "preview" }, labels = new[] { "isolation" } }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/app/kill-switches/enumerate", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                TrellisKit.Core.Models.UpdateGroup group = new TrellisKit.Core.Models.UpdateGroup { Id = TrellisKit.Core.Helpers.IdGenerator.Generate("upg_"), Channel = "preview", Message = marker + " update", CreatedByName = "test", CreatedUtc = DateTime.UtcNow, UpdatedUtc = DateTime.UtcNow };
                await f.Server.Directory.Find(property)!.Services.Database.LiveUpdates.CreateGroupAsync(group, ct).ConfigureAwait(false);

                // Forum rows.
                ForumTestKit.Relax(f);
                string forum = await ForumTestKit.DefaultForumIdAsync(f, member.AccessToken, ct).ConfigureAwait(false);
                JsonElement pollThread = await ForumTestKit.PollThreadAsync(f, member.AccessToken, forum, ct).ConfigureAwait(false);
                await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/metadata/Thread/" + Api.Str(pollThread, "id"), member.AccessToken, new { labels = new[] { "isolation", marker.ToLowerInvariant() } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                string threadComment = await ForumTestKit.CommentIdAsync(f, poster.AccessToken, Api.Str(pollThread, "id"), marker + " thread comment", ct).ConfigureAwait(false);
                await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/polls/" + Api.Str(pollThread.GetProperty("poll"), "id") + "/votes", poster.AccessToken, new { optionIds = new[] { Api.Str(pollThread.GetProperty("poll").GetProperty("options")[0], "id") } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/forums/" + forum + "/follow", poster.AccessToken, new { level = "NewThreads" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                // Event rows.
                List<string> eventIds = await EventTestKit.IsolationRowsAsync(f, admin, member, marker, ct).ConfigureAwait(false);
                if (isB) w.BIds.AddRange(eventIds);

                // Library rows (series, lesson, levels, progress, plays, follows).
                LibraryTestKit.Relax(f);
                JsonElement librarySeries = await LibraryTestKit.SeriesAsync(f, admin.AccessToken, marker + " series", ct, new { slug = "isolation-series", labels = new[] { "isolation" } }).ConfigureAwait(false);
                JsonElement libraryLesson = await LibraryTestKit.LessonAsync(f, admin.AccessToken, Api.Str(librarySeries, "id"), marker + " lesson", ct, new { slug = "isolation-lesson", audience = "SignedIn" }).ConfigureAwait(false);
                await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/lessons/" + Api.Str(libraryLesson, "id") + "/complete", member.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/series/" + Api.Str(librarySeries, "id") + "/follow", poster.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);

                // Directory rows (Phase 4): a program, a venue with a pin, an instructor linked to it and to the member, and a pending submission.
                DirectoryTestKit.Relax(f);
                JsonElement program = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/directory/programs", HttpFixture.AdminToken, new { key = "isolation", label = marker + " program" }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                JsonElement venue = await DirectoryTestKit.VenueAsync(f, marker + " venue", ct, 12.5, 12.5, new { summary = marker + " summary", programs = new[] { "isolation" }, labels = new[] { "isolation" } }).ConfigureAwait(false);
                JsonElement instructor = await DirectoryTestKit.InstructorAsync(f, marker + " instructor", ct, new { venueIds = new[] { Api.Str(venue, "id") } }).ConfigureAwait(false);
                await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/instructors/" + Api.Str(instructor, "id") + "/link", HttpFixture.AdminToken, new { userId = poster.Id }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                JsonElement submission = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/directory/submissions", member.AccessToken, new { type = "EditVenue", targetId = Api.Str(venue, "id"), fields = new { summary = marker + " proposed" } }, HttpStatusCode.Created, ct).ConfigureAwait(false);

                // Ideas board rows: an idea with a vote, a follow, a comment, and the seeded statuses.
                JsonElement idea = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/ideas", admin.AccessToken, new { title = marker + " idea", body = marker + " idea body", labels = new[] { "isolation" } }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/ideas/" + Api.Str(idea, "id") + "/vote", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                string ideaComment = Api.Str(await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/ideas/" + Api.Str(idea, "id") + "/comments", poster.AccessToken, new { body = marker + " idea comment" }, HttpStatusCode.Created, ct).ConfigureAwait(false), "id");
                JsonElement ideaStatuses = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/ideas/statuses/enumerate", admin.AccessToken, new { maxResults = 1 }, HttpStatusCode.OK, ct).ConfigureAwait(false);

                // Support rows: a ticket type, a ticket with a private attachment, and a staff reply.
                JsonElement ticketType = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/support/ticket-types", HttpFixture.AdminToken, new { key = "Isolation", label = marker + " type", labels = new[] { "isolation" } }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                string ticketMedia = await SupportTestKit.UploadAsync(f, member.AccessToken, ct).ConfigureAwait(false);
                JsonElement ticket = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/tickets", member.AccessToken, new { type = "Isolation", title = marker + " ticket", body = marker + " ticket body", mediaIds = new[] { ticketMedia }, labels = new[] { "isolation" } }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                JsonElement ticketReply = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/tickets/" + Api.Str(ticket, "id") + "/messages", HttpFixture.AdminToken, new { body = marker + " staff reply" }, HttpStatusCode.Created, ct).ConfigureAwait(false);

                // Broadcast rows: a saved segment, a sent broadcast with its delivery and announcement notification.
                TrellisKit.Server.Services.ApplicationServices services = f.Server.Directory.Find(property)!.Services;
                services.Broadcasts.Broadcasts.SendSignal = null;
                await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/metadata/User/" + member.Id, HttpFixture.AdminToken, new { labels = new[] { "isolation-bc" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                JsonElement segment = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/broadcasts/segments", HttpFixture.AdminToken,
                    new { name = marker + " segment", filters = new[] { new Dictionary<string, object> { { "field", "labels" }, { "operator", "any" }, { "value", "isolation-bc" } } }, labels = new[] { "isolation" } }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                JsonElement broadcast = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/broadcasts", admin.AccessToken,
                    new { title = marker + " broadcast", inApp = new { title = marker + " announcement", body = marker + " announcement body" }, segmentId = Api.Str(segment, "id"), labels = new[] { "isolation" } }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/broadcasts/" + Api.Str(broadcast, "id") + "/send", admin.AccessToken, new { confirmCount = 1 }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                for (int pass = 0; pass < 100 && await services.Broadcasts.Sender.RunPassAsync(ct).ConfigureAwait(false) == 0; pass++) await Task.Delay(25, ct).ConfigureAwait(false);
                DataTable delivery = await f.Raw.ExecuteQueryAsync("SELECT id FROM broadcastdeliveries WHERE propertyid = '" + property + "' AND broadcastid = '" + Api.Str(broadcast, "id") + "';", false, ct).ConfigureAwait(false);
                Check.Equal(1, delivery.Rows.Count, "Broadcast delivered in " + (isB ? "B" : "A"));

                // Direct messaging rows: a conversation with messages (one reported, so a snapshot exists).
                JsonElement dm = await MessagingTestKit.StartAsync(f, poster, member.Id, marker + " direct message", ct).ConfigureAwait(false);
                string conversation = Api.Str(dm.GetProperty("conversation"), "id");
                JsonElement dmReply = await MessagingTestKit.SendOkAsync(f, member, conversation, marker + " direct reply", ct).ConfigureAwait(false);
                await ModerationTestKit.ReportOkAsync(f, poster.AccessToken, "Message", Api.Str(dmReply, "id"), ct).ConfigureAwait(false);
                if (!isB)
                {
                    w.AMember = member.AccessToken;
                    w.APoster = poster.AccessToken;
                    continue;
                }
                w.BMemberId = member.Id;
                w.BIds.AddRange(new[] { poster.Id, member.Id, admin.Id, post, comment, Api.Str(report, "id"), Api.Str(code, "id"), media });
                w.BIds.AddRange(new[] { Api.Str(field, "id"), Api.Str(suppression, "id"), Api.Str(messages.GetProperty("objects")[0], "id") });
                w.BIds.AddRange(new[] { Api.Str(flag, "id"), Api.Str(deployKey, "id"), group.Id });
                w.BIds.AddRange(new[] { forum, Api.Str(pollThread, "id"), Api.Str(pollThread.GetProperty("poll"), "id"), threadComment });
                w.BIds.AddRange(new[] { Api.Str(librarySeries, "id"), Api.Str(libraryLesson, "id") });
                w.BIds.AddRange(new[] { Api.Str(program, "id"), Api.Str(venue, "id"), Api.Str(instructor, "id"), Api.Str(submission, "id") });
                w.BIds.AddRange(new[] { Api.Str(idea, "id"), ideaComment, Api.Str(ideaStatuses.GetProperty("objects")[0], "id") });
                w.BIds.AddRange(new[] { Api.Str(ticketType, "id"), Api.Str(ticket, "id"), Api.Str(ticketReply, "id"), ticketMedia });
                w.BIds.AddRange(new[] { Api.Str(segment, "id"), Api.Str(broadcast, "id"), RowReader.GetString(delivery.Rows[0], "id") });
                w.BBroadcast = Api.Str(broadcast, "id");
                w.BSegment = Api.Str(segment, "id");
                w.BIds.AddRange(new[] { conversation, Api.Str(dm.GetProperty("message"), "id"), Api.Str(dmReply, "id"), Api.Str(dm.GetProperty("conversation"), "memberId") });
                w.BMedia = media;
                w.BPost = post;
                w.BCode = Api.Str(code, "code");
            }
            DataTable extra = await f.Raw.ExecuteQueryAsync("SELECT id FROM notifications WHERE propertyid = '" + w.B + "' UNION ALL SELECT id FROM entitlements WHERE propertyid = '" + w.B + "' UNION ALL SELECT id FROM accountexports WHERE propertyid = '" + w.B + "' UNION ALL SELECT id FROM auditrecords WHERE propertyid = '" + w.B + "' LIMIT 12;", false, ct).ConfigureAwait(false);
            foreach (DataRow row in extra.Rows) w.BIds.Add(RowReader.GetString(row, "id"));
            f.PropertyId = w.A;
            return w;
        }

        #endregion
    }
}

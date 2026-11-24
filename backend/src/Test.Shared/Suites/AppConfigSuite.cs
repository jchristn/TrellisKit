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
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;
    using TrellisKit.Server.Services;

    /// <summary>
    /// Server-side feature flags, live kill switches, content versions, the app manifest, and the binary version policy
    /// (docs/LIVE_UPDATES_CONTRACT.md sections 11 to 16).
    /// </summary>
    public static class AppConfigSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "AppConfig";

        #endregion

        #region Private-Members

        private const string _Base = "/api/v1/admin/app";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "App configuration (" + TestEnvironment.Provider + ")", new[]
            {
                SuiteCases.Unit(Id, "Evaluator.Rules", "Rules match in order with AND conditions; rollout buckets are deterministic; disabled flags return offValue; absent data never matches", ct =>
                {
                    FeatureFlag flag = new FeatureFlag
                    {
                        Key = "feed.composerVersion",
                        Enabled = true,
                        DefaultValue = "v1",
                        OffValue = "v0",
                        Rules = new List<FeatureFlagRule>
                        {
                            new FeatureFlagRule { Id = "ffr_staff", Position = 1, Value = "staff", Conditions = new List<FeatureFlagCondition> { new FeatureFlagCondition { Attribute = "userLabels", Operator = "HasAny", Values = new List<string> { "staff:beta" } } } },
                            new FeatureFlagRule { Id = "ffr_ios", Position = 2, Value = "v2", RolloutBasisPoints = 5000, Conditions = new List<FeatureFlagCondition>
                            {
                                new FeatureFlagCondition { Attribute = "platform", Operator = "In", Values = new List<string> { "ios" } },
                                new FeatureFlagCondition { Attribute = "appVersion", Operator = "SemverRange", Values = new List<string> { ">=1.3.0 <2.0.0" } }
                            } }
                        }
                    };
                    FlagEvaluationContext staff = new FlagEvaluationContext { SignedIn = true, UserLabels = new List<string> { "STAFF:beta" }, SubjectId = "u1", NowUtc = DateTime.UtcNow };
                    Check.Equal("staff", FeatureFlagEvaluator.Evaluate(flag, staff).Value, "First matching rule wins");
                    FlagEvaluationContext anonymous = new FlagEvaluationContext { UserLabels = new List<string> { "staff:beta" }, NowUtc = DateTime.UtcNow };
                    Check.Equal("v1", FeatureFlagEvaluator.Evaluate(flag, anonymous).Value, "Labels never match signed out");
                    int inside = 0;
                    for (int i = 0; i < 2000; i++)
                    {
                        FlagEvaluationContext x = new FlagEvaluationContext { Platform = "ios", AppVersion = "1.4.0", SubjectId = "s" + i, NowUtc = DateTime.UtcNow };
                        FlagEvaluationResult r = FeatureFlagEvaluator.Evaluate(flag, x);
                        Check.Equal(r.Value, FeatureFlagEvaluator.Evaluate(flag, x).Value, "Deterministic");
                        if (r.Value == "v2") inside++;
                        else Check.Equal("RuleOutsideRollout", r.Reason, "Outside reason");
                    }
                    Check.True(inside > 850 && inside < 1150, "About half inside (" + inside + ")");
                    Check.Equal("v1", FeatureFlagEvaluator.Evaluate(flag, new FlagEvaluationContext { Platform = "ios", AppVersion = "2.1.0", SubjectId = "s", NowUtc = DateTime.UtcNow }).Value, "Version outside the range");
                    flag.Enabled = false;
                    Check.Equal("v0", FeatureFlagEvaluator.Evaluate(flag, staff).Value, "Disabled returns offValue");
                    Dictionary<string, string> errors = FeatureFlagEvaluator.Validate(new List<FeatureFlagRule>
                    {
                        new FeatureFlagRule { Conditions = new List<FeatureFlagCondition> { new FeatureFlagCondition { Attribute = "planet", Operator = "In", Values = new List<string> { "x" } }, new FeatureFlagCondition { Attribute = "appVersion", Operator = "SemverRange", Values = new List<string> { "nope" } } } }
                    }, 20, 10, 100);
                    Check.True(errors.ContainsKey("rules[0].conditions[0].attribute") && errors.ContainsKey("rules[0].conditions[1].values"), "Invalid rules reported per condition");
                    Check.True(KillSwitchCatalog.FeaturesFor("POST", "/api/v1/auth/register").SequenceEqual(new[] { "signups" }), "Sign-up route");
                    Check.True(KillSwitchCatalog.FeaturesFor("POST", "/api/v1/feed").SequenceEqual(new[] { "feedComposer", "feed" }), "Composer and feed");
                    Check.Equal(0, KillSwitchCatalog.FeaturesFor("GET", "/api/v1/admin/feed").Count, "Admin routes are never blocked");
                    return Task.CompletedTask;
                }),
                SuiteCases.Http(Id, "Flags.DraftPublishRestore", "Drafts never reach clients; publish makes a version; the manifest resolves flags per caller; conflicts, If-Match, archive, and restore behave", async (f, ct) =>
                {
                    JsonElement created = await Api.ExpectAsync(f, HttpMethod.Post, _Base + "/flags", HttpFixture.AdminToken, new
                    {
                        key = "feed.newComposer",
                        name = "New composer",
                        kind = "Boolean",
                        defaultValue = false,
                        offValue = false,
                        clients = new[] { "mobile" },
                        rules = new[] { new { description = "iOS", conditions = new[] { new { attribute = "platform", @operator = "In", values = new[] { "ios" } } }, value = true, rolloutPercentage = 100 } },
                        labels = new[] { "feed" }
                    }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                    string id = Api.Str(created, "id");
                    Check.True(created.GetProperty("hasDraftChanges").GetBoolean(), "Draft");
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, _Base + "/flags", HttpFixture.AdminToken, new { key = "feed.newComposer", name = "Dup", kind = "Boolean", defaultValue = false, offValue = false }, HttpStatusCode.Conflict, "FlagKeyInUse", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, _Base + "/flags", HttpFixture.AdminToken, new { key = "x.y", name = "Bad", kind = "Boolean", defaultValue = false, offValue = false, rules = new[] { new { conditions = new[] { new { attribute = "planet", @operator = "In", values = new[] { "x" } } }, value = true } } }, HttpStatusCode.BadRequest, "InvalidTargetingRule", ct).ConfigureAwait(false);
                    JsonElement before = await Manifest(f, "ios", null, ct).ConfigureAwait(false);
                    Check.True(!before.GetProperty("flags").TryGetProperty("feed.newComposer", out JsonElement _), "Draft not in the manifest");
                    JsonElement drafts = await Api.ExpectAsync(f, HttpMethod.Get, _Base + "/drafts", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(1L, drafts.GetProperty("totalRecords").GetInt64(), "One draft");
                    Check.Equal(1L, drafts.GetProperty("counts").GetProperty("flags").GetInt64(), "Exact flag count context");
                    Check.Equal(0L, drafts.GetProperty("contentVersion").GetInt64(), "Publish optimistic version context");
                    JsonElement hiddenDrafts = await Api.ExpectAsync(f, HttpMethod.Post, _Base + "/drafts/enumerate", HttpFixture.AdminToken, new { maxResults = 1, search = "absent-draft" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(0, hiddenDrafts.GetProperty("objects").GetArrayLength(), "Draft search applies");
                    Check.Equal(1L, hiddenDrafts.GetProperty("counts").GetProperty("flags").GetInt64(), "Publish counts remain global across a filtered page");

                    long version = drafts.GetProperty("contentVersion").GetInt64();
                    JsonElement published = await Api.ExpectAsync(f, HttpMethod.Post, _Base + "/publish", HttpFixture.AdminToken, new { message = "Composer", expectedVersion = version }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                    Check.Equal(version + 1, published.GetProperty("version").GetInt64(), "Next version");
                    Check.Equal(1, published.GetProperty("changeCounts").GetProperty("flags").GetInt32(), "One flag change");
                    JsonElement ios = await Manifest(f, "ios", null, ct).ConfigureAwait(false);
                    Check.True(ios.GetProperty("flags").GetProperty("feed.newComposer").GetBoolean(), "iOS rule");
                    Check.Equal(version + 1, ios.GetProperty("contentVersion").GetInt64(), "Manifest content version");
                    JsonElement android = await Manifest(f, "android", null, ct).ConfigureAwait(false);
                    Check.True(!android.GetProperty("flags").GetProperty("feed.newComposer").GetBoolean(), "Default for android");
                    ApiResult web = await Api.SendAsync(f, HttpMethod.Get, "/api/v1/app/manifest", null, null, ct, "web").ConfigureAwait(false);
                    Check.True(!web.Body.GetProperty("flags").TryGetProperty("feed.newComposer", out JsonElement _), "Mobile-only flag not sent to web");
                    Check.True(web.Body.GetProperty("binary").ValueKind == JsonValueKind.Null, "No binary policy on web");
                    Check.Equal(KillSwitchCatalog.FeatureKeys.Count, ios.GetProperty("killSwitches").EnumerateObject().Count(), "Every kill switch listed");

                    JsonElement detail = await Api.ExpectAsync(f, HttpMethod.Get, _Base + "/flags/" + id, HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    string etag = Api.Str(detail.GetProperty("flag"), "eTag");
                    await Api.ExpectErrorAsync(f, HttpMethod.Put, _Base + "/flags/" + id, HttpFixture.AdminToken, new { enabled = false }, HttpStatusCode.PreconditionFailed, "PreconditionFailed", ct).ConfigureAwait(false);
                    ApiResult updated = await Api.SendAsync(f, HttpMethod.Put, _Base + "/flags/" + id, HttpFixture.AdminToken, new { enabled = false }, ct, "mobile", new Dictionary<string, string> { ["If-Match"] = etag }).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.OK, updated.Status, "Update with If-Match (" + updated.Text + ")");
                    ApiResult keyChange = await Api.SendAsync(f, HttpMethod.Put, _Base + "/flags/" + id, HttpFixture.AdminToken, new { key = "feed.other" }, ct, "mobile", new Dictionary<string, string> { ["If-Match"] = Api.Str(updated.Body, "eTag") }).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.BadRequest, keyChange.Status, "Key immutable after publish");
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, _Base + "/publish", HttpFixture.AdminToken, new { message = "Stale", expectedVersion = version }, HttpStatusCode.Conflict, "ContentVersionConflict", ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Post, _Base + "/publish", HttpFixture.AdminToken, new { message = "Off", expectedVersion = version + 1 }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                    Check.True(!(await Manifest(f, "ios", null, ct).ConfigureAwait(false)).GetProperty("flags").GetProperty("feed.newComposer").GetBoolean(), "Disabled flag returns offValue");
                    JsonElement restored = await Api.ExpectAsync(f, HttpMethod.Post, _Base + "/versions/" + (version + 1) + "/restore", HttpFixture.AdminToken, new { message = "Back on" }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                    Check.Equal("Restore", Api.Str(restored, "kind"), "Restore version");
                    Check.True((await Manifest(f, "ios", null, ct).ConfigureAwait(false)).GetProperty("flags").GetProperty("feed.newComposer").GetBoolean(), "Restored state");
                    await Api.ExpectAsync(f, HttpMethod.Post, _Base + "/flags/" + id + "/archive", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Post, _Base + "/publish", HttpFixture.AdminToken, new { message = "Archive", expectedVersion = version + 3 }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                    Check.True(!(await Manifest(f, "ios", null, ct).ConfigureAwait(false)).GetProperty("flags").TryGetProperty("feed.newComposer", out JsonElement _), "Archived flags leave the manifest");
                    JsonElement history = await Api.ExpectAsync(f, HttpMethod.Post, _Base + "/flags/" + id + "/history/enumerate", HttpFixture.AdminToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(4L, history.GetProperty("totalRecords").GetInt64(), "Four published revisions");
                    JsonElement versions = await Api.ExpectAsync(f, HttpMethod.Post, _Base + "/versions/enumerate", HttpFixture.AdminToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(version + 4, versions.GetProperty("objects")[0].GetProperty("version").GetInt64(), "Newest version first");
                    JsonElement changes = await Api.ExpectAsync(f, HttpMethod.Get, _Base + "/versions/" + (version + 1), HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(1, changes.GetProperty("changes").GetProperty("objects").GetArrayLength(), "Version changes preview");
                    JsonElement evaluate = await Api.ExpectAsync(f, HttpMethod.Post, _Base + "/flags/" + id + "/evaluate", HttpFixture.AdminToken, new { @as = new { platform = "ios", installId = "x" }, useDraft = false }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("Rule", Api.Str(evaluate, "reason"), "Test as");
                    JsonElement listed = await Api.ExpectAsync(f, HttpMethod.Post, _Base + "/flags/enumerate", HttpFixture.AdminToken, new { labels = new[] { "feed" }, filters = new { state = "Archived" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(1, listed.GetProperty("objects").GetArrayLength(), "Label and state filters");
                }),
                SuiteCases.Http(Id, "Flags.StaffTargetingNeverLeaks", "A rule on a staff label resolves on the server; the manifest carries only the value", async (f, ct) =>
                {
                    TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/metadata/User/" + member.Id, HttpFixture.AdminToken, new { labels = new[] { "staff:beta-tester" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Post, _Base + "/flags", HttpFixture.AdminToken, new
                    {
                        key = "beta.panel", name = "Beta panel", kind = "Variant", variants = new[] { "off", "on" }, defaultValue = "off", offValue = "off", clients = new[] { "mobile", "web" },
                        rules = new[] { new { conditions = new[] { new { attribute = "userLabels", @operator = "HasAny", values = new[] { "staff:beta-tester" } } }, value = "on" } }
                    }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Post, _Base + "/publish", HttpFixture.AdminToken, new { message = "Beta", expectedVersion = 0 }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                    JsonElement signedIn = await Manifest(f, "ios", member.AccessToken, ct).ConfigureAwait(false);
                    Check.Equal("on", Api.Str(signedIn.GetProperty("flags"), "beta.panel"), "Staff label rule applies");
                    Check.True(signedIn.GetProperty("evaluatedFor").GetProperty("signedIn").GetBoolean(), "Signed in");
                    Check.True(!signedIn.ToString().Contains("staff:", StringComparison.Ordinal), "Staff metadata never leaves the server");
                    Check.Equal("off", Api.Str((await Manifest(f, "ios", null, ct).ConfigureAwait(false)).GetProperty("flags"), "beta.panel"), "Anonymous default");
                    ApiResult cached = await Api.SendAsync(f, HttpMethod.Get, "/api/v1/app/manifest?since=1", null, null, ct, "mobile", new Dictionary<string, string> { ["X-Client-Platform"] = "ios" }).ConfigureAwait(false);
                    string etag = cached.Headers["ETag"];
                    ApiResult notModified = await Api.SendAsync(f, HttpMethod.Get, "/api/v1/app/manifest?since=1", null, null, ct, "mobile", new Dictionary<string, string> { ["X-Client-Platform"] = "ios", ["If-None-Match"] = etag }).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.NotModified, notModified.Status, "304 for an unchanged manifest");
                    Check.True(cached.Body.GetProperty("property").ValueKind == JsonValueKind.Null, "No property block with since");
                }),
                SuiteCases.Http(Id, "KillSwitches.LiveEnforcement", "A kill switch blocks the feature's API routes at once with 503 FeatureDisabled, exempts SystemAdmin, targets by app version, and is audited", async (f, ct) =>
                {
                    JsonElement switches = await Api.ExpectAsync(f, HttpMethod.Post, _Base + "/kill-switches/enumerate", HttpFixture.AdminToken, new { maxResults = 100 }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal((long)KillSwitchCatalog.FeatureKeys.Count, switches.GetProperty("totalRecords").GetInt64(), "Every feature key has a switch");
                    TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Put, _Base + "/kill-switches/feed", HttpFixture.AdminToken, new { killed = true }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    JsonElement killed = await Api.ExpectAsync(f, HttpMethod.Put, _Base + "/kill-switches/feed", HttpFixture.AdminToken, new { killed = true, message = "The feed is paused.", reason = "Incident 42" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(killed.GetProperty("killedForEveryone").GetBoolean(), "Killed for everyone");
                    Check.Equal("Incident 42", Api.Str(killed, "lastChangedReason"), "Reason kept");
                    ApiResult blocked = await Api.SendAsync(f, HttpMethod.Get, "/api/v1/feed", member.AccessToken, null, ct).ConfigureAwait(false);
                    Check.Equal((HttpStatusCode)503, blocked.Status, "Blocked");
                    Check.Equal("FeatureDisabled", blocked.Error, "FeatureDisabled");
                    Check.Equal("60", blocked.RetryAfter, "Retry-After");
                    await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed", admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/feed", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    JsonElement manifest = await Manifest(f, "ios", member.AccessToken, ct).ConfigureAwait(false);
                    Check.True(manifest.GetProperty("killSwitches").GetProperty("feed").GetProperty("killed").GetBoolean(), "Manifest reports the kill");
                    Check.Equal("The feed is paused.", Api.Str(manifest.GetProperty("killSwitches").GetProperty("feed"), "message"), "Message");
                    await Api.ExpectAsync(f, HttpMethod.Put, _Base + "/kill-switches/feed", HttpFixture.AdminToken, new
                    {
                        killed = true, reason = "Old app only",
                        rules = new[] { new { conditions = new[] { new { attribute = "appVersion", @operator = "SemverRange", values = new[] { "<2.0.0" } } } } }
                    }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    ApiResult oldApp = await Api.SendAsync(f, HttpMethod.Get, "/api/v1/feed", member.AccessToken, null, ct, "mobile", new Dictionary<string, string> { ["X-Client-Version"] = "1.5.0" }).ConfigureAwait(false);
                    Check.Equal((HttpStatusCode)503, oldApp.Status, "Old app blocked");
                    ApiResult newApp = await Api.SendAsync(f, HttpMethod.Get, "/api/v1/feed", member.AccessToken, null, ct, "mobile", new Dictionary<string, string> { ["X-Client-Version"] = "2.1.0" }).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.OK, newApp.Status, "New app allowed");
                    await Api.ExpectAsync(f, HttpMethod.Put, _Base + "/kill-switches/signups", HttpFixture.AdminToken, new { killed = true, reason = "Spam wave" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    ApiResult signup = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/auth/register", null, new { email = Api.UniqueEmail(), password = Api.Password, displayName = "Blocked", dateOfBirth = "1990-01-01", acceptTerms = true, acceptedDocuments = Api.SignupDocuments }, ct).ConfigureAwait(false);
                    Check.Equal((HttpStatusCode)503, signup.Status, "Anonymous sign-up blocked");
                    await Api.ExpectAsync(f, HttpMethod.Put, _Base + "/kill-switches/feed", HttpFixture.AdminToken, new { killed = false, reason = "Fixed" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Put, _Base + "/kill-switches/nope", HttpFixture.AdminToken, new { killed = true, reason = "x" }, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
                    JsonElement killedOnly = await Api.ExpectAsync(f, HttpMethod.Post, _Base + "/kill-switches/enumerate", HttpFixture.AdminToken, new { filters = new { killed = true } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("signups", Api.Str(killedOnly.GetProperty("objects")[0], "featureKey"), "Killed filter");
                    System.Data.DataTable audit = await f.Raw.ExecuteQueryAsync("SELECT COUNT(*) AS cnt FROM auditrecords WHERE propertyid = '" + f.Property.Id + "' AND action = 'appConfig.killSwitch';", false, ct).ConfigureAwait(false);
                    Check.Equal(4L, Convert.ToInt64(audit.Rows[0]["cnt"], System.Globalization.CultureInfo.InvariantCulture), "Audited");
                }),
                SuiteCases.Http(Id, "WellKnown.Unconfigured", "Without mobile.appleTeamId and mobile.androidCertFingerprints the association files are 404", (TrellisKit.Core.PropertyConfig.PropertyConfiguration p) => { }, async (f, ct) =>
                {
                    Check.Equal(HttpStatusCode.NotFound, (await Api.SendAsync(f, HttpMethod.Get, "/api/v1/public/pages/well-known/apple-app-site-association", null, null, ct).ConfigureAwait(false)).Status, "No Apple association");
                    Check.Equal(HttpStatusCode.NotFound, (await Api.SendAsync(f, HttpMethod.Get, "/api/v1/public/pages/well-known/assetlinks.json", null, null, ct).ConfigureAwait(false)).Status, "No asset links");
                }),
                SuiteCases.Http(Id, "WellKnown.Configured", "The association files name the app from property.json so iOS and Android offer to save its passwords for the website", p =>
                {
                    p.Mobile.AppleTeamId = "ABCDE12345";
                    p.Mobile.AndroidCertFingerprints = new List<string> { "aa:bb:cc", "AA:BB:CC", " " };
                }, async (f, ct) =>
                {
                    JsonElement apple = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/public/pages/well-known/apple-app-site-association", null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("ABCDE12345." + f.Property.Mobile.BundleId, apple.GetProperty("webcredentials").GetProperty("apps")[0].GetString()!, "Team and bundle id");
                    JsonElement links = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/public/pages/well-known/assetlinks.json", null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    JsonElement target = links[0].GetProperty("target");
                    Check.Equal("delegate_permission/common.get_login_creds", links[0].GetProperty("relation")[0].GetString()!, "Login credentials relation");
                    Check.Equal(f.Property.Mobile.AndroidPackage, Api.Str(target, "package_name"), "Package");
                    Check.Equal("android_app", Api.Str(target, "namespace"), "Namespace");
                    Check.Equal(1, target.GetProperty("sha256_cert_fingerprints").GetArrayLength(), "Fingerprints normalized and deduplicated");
                }),
                SuiteCases.Http(Id, "BinaryPolicy.PropertyAndApi", "minimumBinaryVersion and latestBinaryVersion reach GET /api/v1/property and the manifest; mobile.enforceMinimumOnApi returns 426 outside exempt routes", p =>
                {
                    p.Mobile.MinimumBinaryVersion = JsonDocument.Parse("{\"ios\":\"2.0.0\",\"android\":\"1.0.0\"}").RootElement.Clone();
                    p.Mobile.LatestBinaryVersion = JsonDocument.Parse("\"3.0.0\"").RootElement.Clone();
                    p.Mobile.StoreUrls = new Dictionary<string, string> { ["ios"] = "https://apps.apple.test/app/id1" };
                }, async (f, ct) =>
                {
                    JsonElement property = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/property", null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("2.0.0", Api.Str(property.GetProperty("mobile").GetProperty("minimumBinaryVersion"), "ios"), "Minimum served");
                    Check.Equal("3.0.0", property.GetProperty("mobile").GetProperty("latestBinaryVersion").GetString()!, "Latest served");
                    Check.Equal(15, property.GetProperty("updates").GetProperty("checkIntervalMinutes").GetInt32(), "Check interval served");
                    ApiResult manifest = await Api.SendAsync(f, HttpMethod.Get, "/api/v1/app/manifest", null, null, ct, "mobile", new Dictionary<string, string> { ["X-Client-Platform"] = "ios", ["X-Client-Version"] = "1.9.0" }).ConfigureAwait(false);
                    JsonElement binary = manifest.Body.GetProperty("binary");
                    Check.True(binary.GetProperty("updateRequired").GetBoolean() && binary.GetProperty("updateRecommended").GetBoolean(), "Required and recommended");
                    Check.Equal("https://apps.apple.test/app/id1", Api.Str(binary, "storeUrl"), "Store URL");
                    Check.True(manifest.Body.GetProperty("property").ValueKind == JsonValueKind.Object, "Property block without since");
                    TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    Dictionary<string, string> old = new Dictionary<string, string> { ["X-Client-Platform"] = "ios", ["X-Client-Version"] = "1.9.0" };
                    Check.Equal(HttpStatusCode.OK, (await Api.SendAsync(f, HttpMethod.Get, "/api/v1/feed", member.AccessToken, null, ct, "mobile", old).ConfigureAwait(false)).Status, "Off by default");
                    f.Settings.AppConfig.EnforceMinimumOnApi = true;
                    ApiResult blocked = await Api.SendAsync(f, HttpMethod.Get, "/api/v1/feed", member.AccessToken, null, ct, "mobile", old).ConfigureAwait(false);
                    Check.Equal((HttpStatusCode)426, blocked.Status, "426 below the minimum");
                    Check.Equal("ClientUpdateRequired", blocked.Error, "ClientUpdateRequired");
                    Check.Equal(HttpStatusCode.OK, (await Api.SendAsync(f, HttpMethod.Get, "/api/v1/auth/me", member.AccessToken, null, ct, "mobile", old).ConfigureAwait(false)).Status, "Exempt route");
                    Check.Equal(HttpStatusCode.OK, (await Api.SendAsync(f, HttpMethod.Get, "/api/v1/feed", member.AccessToken, null, ct, "mobile", new Dictionary<string, string> { ["X-Client-Platform"] = "ios", ["X-Client-Version"] = "2.0.1" }).ConfigureAwait(false)).Status, "Current app allowed");
                })
            });
        }

        #endregion

        #region Private-Methods

        private static async Task<JsonElement> Manifest(HttpFixture f, string platform, string? bearer, CancellationToken ct)
        {
            ApiResult result = await Api.SendAsync(f, HttpMethod.Get, "/api/v1/app/manifest", bearer, null, ct, "mobile", new Dictionary<string, string> { ["X-Client-Platform"] = platform, ["X-TK-Install-Id"] = "install-" + platform }).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.OK, result.Status, "Manifest (" + result.Text + ")");
            return result.Body;
        }

        #endregion
    }
}

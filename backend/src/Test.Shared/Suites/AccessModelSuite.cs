namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using System.Threading;
    using System.Threading.Tasks;
    using Touchstone.Core;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.PropertyConfig;
    using TrellisKit.Core.Security;
    using TrellisKit.Server.Services;
    using TrellisKit.Server.Workers;

    /// <summary>
    /// Access models (docs/ACCESS_MODELS_CONTRACT.md): the route-by-model matrix for every route group and caller tier
    /// under Open, PartiallyGated (two configurations), and FullyGated; the OpenAPI declaration check (every Member
    /// group route has a feature and action declaration); the per-object audience rule with locked stubs and denial
    /// bodies; payments not available in Open; GET /api/v1/property and Me; notification fan-out per model; switching
    /// models (audited, entitlements kept); folder seeding; and isolation of two properties with different models on one
    /// MultiProperty deployment.
    /// </summary>
    public static class AccessModelSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "AccessModel";

        #endregion

        #region Private-Members

        // Callers of the matrix, in column order: anonymous, signed in without the age attestation, signed in
        // (attested, no membership), member, moderator without a membership, SystemAdmin.
        private static readonly string[] _Callers = new[] { "anonymous", "unattested", "signedIn", "member", "moderator", "admin" };

        // Policies of the matrix: F FullyGated; P PartiallyGated with anonymous browsing and feed plus members gated;
        // Q PartiallyGated without anonymous browsing and nothing gated; O Open with anonymous browsing.
        private static readonly string[] _Models = new[] { "F", "P", "Q", "O" };

        // Route, then the expected outcome per model (F, P, Q, O), one character per caller: A allowed, 1 401, 2 402,
        // 3 403, 4 404 NotAvailable.
        private static readonly List<string[]> _Matrix = new List<string[]>
        {
            new[] { "GET /api/v1/auth/me", "1AAAAA", "1AAAAA", "1AAAAA", "1AAAAA" },
            new[] { "PUT /api/v1/me/profile", "1AAAAA", "1AAAAA", "1AAAAA", "1AAAAA" },
            new[] { "GET /api/v1/membership", "1AAAAA", "1AAAAA", "1AAAAA", "444444" },
            new[] { "POST /api/v1/membership/stripe/checkout", "13AAAA", "13AAAA", "13AAAA", "444444" },
            new[] { "POST /api/v1/membership/apple/verify", "13AAAA", "13AAAA", "13AAAA", "444444" },
            new[] { "GET /api/v1/membership/history", "1AAAAA", "1AAAAA", "1AAAAA", "1AAAAA" },
            new[] { "POST /api/v1/signup-codes/redeem", "13AAAA", "13AAAA", "13AAAA", "444444" },
            new[] { "GET /api/v1/feed", "132A2A", "A3AAAA", "13AAAA", "A3AAAA" },
            new[] { "POST /api/v1/feed/enumerate", "132A2A", "A3AAAA", "13AAAA", "A3AAAA" },
            new[] { "POST /api/v1/feed", "132A2A", "132A2A", "13AAAA", "13AAAA" },
            new[] { "GET /api/v1/feed/fpo_x", "132A2A", "A3AAAA", "13AAAA", "A3AAAA" },
            new[] { "PUT /api/v1/feed/fpo_x", "132A2A", "132A2A", "13AAAA", "13AAAA" },
            new[] { "GET /api/v1/feed/fpo_x/comments", "132A2A", "A3AAAA", "13AAAA", "A3AAAA" },
            new[] { "POST /api/v1/feed/fpo_x/comments", "132A2A", "132A2A", "13AAAA", "13AAAA" },
            new[] { "GET /api/v1/feed/fpo_x/reactions", "132A2A", "13AAAA", "13AAAA", "13AAAA" },
            new[] { "PUT /api/v1/feed/fpo_x/reactions/like", "132A2A", "132A2A", "13AAAA", "13AAAA" },
            new[] { "GET /api/v1/comments/cmt_x", "132A2A", "A3AAAA", "13AAAA", "A3AAAA" },
            new[] { "PUT /api/v1/comments/cmt_x", "132A2A", "132A2A", "13AAAA", "13AAAA" },
            new[] { "GET /api/v1/reactions/set", "132A2A", "A3AAAA", "13AAAA", "A3AAAA" },
            new[] { "POST /api/v1/reactions/set/enumerate", "132A2A", "A3AAAA", "13AAAA", "A3AAAA" },
            new[] { "GET /api/v1/media", "132A2A", "13AAAA", "13AAAA", "13AAAA" },
            new[] { "POST /api/v1/media/enumerate", "132A2A", "13AAAA", "13AAAA", "13AAAA" },
            new[] { "GET /api/v1/directory/geocode", "132A2A", "13AAAA", "13AAAA", "13AAAA" },
            new[] { "POST /api/v1/directory/geocode/enumerate", "132A2A", "13AAAA", "13AAAA", "13AAAA" },
            new[] { "POST /api/v1/media/uploads", "132A2A", "13AAAA", "13AAAA", "13AAAA" },
            new[] { "POST /api/v1/media/grant", "132A2A", "13AAAA", "13AAAA", "13AAAA" },
            new[] { "GET /api/v1/users/usr_x", "132A2A", "132A2A", "13AAAA", "13AAAA" },
            new[] { "GET /api/v1/members/search", "132A2A", "132A2A", "13AAAA", "13AAAA" },
            new[] { "POST /api/v1/members/directory/enumerate", "132A2A", "132A2A", "13AAAA", "13AAAA" },
            new[] { "GET /api/v1/notifications", "132A2A", "13AAAA", "13AAAA", "13AAAA" },
            new[] { "POST /api/v1/reports", "132A2A", "13AAAA", "13AAAA", "13AAAA" },
            new[] { "POST /api/v1/users/usr_x/block", "132A2A", "13AAAA", "13AAAA", "13AAAA" },
            new[] { "POST /api/v1/link-previews", "132A2A", "13AAAA", "13AAAA", "13AAAA" },
            new[] { "POST /api/v1/labels/enumerate", "132A2A", "13AAAA", "13AAAA", "13AAAA" },
            new[] { "GET /api/v1/undeclared-future-route", "132A2A", "132A2A", "132A2A", "13AAAA" },
            new[] { "GET /api/v1/admin/moderation/queue", "1333AA", "1333AA", "1333AA", "1333AA" },
            new[] { "GET /api/v1/admin/members", "13333A", "13333A", "13333A", "13333A" },
            new[] { "POST /api/v1/admin/members/usr_x/comp", "13333A", "13333A", "13333A", "444444" },
            new[] { "POST /api/v1/admin/signup-codes", "13333A", "13333A", "13333A", "444444" },
            new[] { "POST /api/v1/admin/signup-codes/batches", "13333A", "13333A", "13333A", "444444" },
            new[] { "PUT /api/v1/admin/signup-codes/suc_x", "13333A", "13333A", "13333A", "444444" },
            new[] { "POST /api/v1/admin/signup-codes/enumerate", "13333A", "13333A", "13333A", "13333A" },
            new[] { "GET /api/v1/admin/subscriptions", "13333A", "13333A", "13333A", "13333A" },
            new[] { "PUT /api/v1/admin/property/access", "13333A", "13333A", "13333A", "13333A" }
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Access models (" + TestEnvironment.Provider + ")", new[]
            {
                SuiteCases.Unit(Id, "Routes.Matrix", "Every route group under every model and caller tier: status codes, 401 and 402 bodies name SignIn or Membership and the feature", ct =>
                {
                    List<string> problems = new List<string>();
                    foreach (string[] row in _Matrix)
                    {
                        string method = row[0].Split(' ')[0];
                        string path = row[0].Split(' ')[1];
                        for (int m = 0; m < _Models.Length; m++)
                        {
                            AuthorizationService authz = Authorizer(Policy(_Models[m]));
                            for (int c = 0; c < _Callers.Length; c++)
                            {
                                AuthorizationDecision decision = authz.Authorize(Caller(_Callers[c]), method, path);
                                char actual = Code(decision);
                                char expected = row[m + 1][c];
                                if (actual != expected) problems.Add(row[0] + " model " + _Models[m] + " caller " + _Callers[c] + ": expected " + expected + " got " + actual + " (" + decision.Error + ")");
                                if (decision.StatusCode == 402 && _Models[m] != "F" && decision.Required != "Membership") problems.Add(row[0] + " model " + _Models[m] + ": 402 without required Membership");
                                if (decision.StatusCode == 402 && _Models[m] == "P" && path.StartsWith("/api/v1/feed", StringComparison.Ordinal) && decision.Feature != "feed") problems.Add(row[0] + ": 402 names feature " + decision.Feature);
                                if (decision.StatusCode == 401 && _Models[m] != "F" && path.StartsWith("/api/v1/feed", StringComparison.Ordinal) && decision.Required != "SignIn") problems.Add(row[0] + " model " + _Models[m] + ": 401 without required SignIn");
                            }
                        }
                    }
                    Check.True(problems.Count == 0, "Route matrix:\n  " + String.Join("\n  ", problems));
                    return Task.CompletedTask;
                }),
                SuiteCases.Unit(Id, "Decider.Tiers", "Read, write, and participate tiers per model and audience; stubs only in PartiallyGated; recipients per model; defaults per model", ct =>
                {
                    AccessPolicy f = Policy("F");
                    AccessPolicy p = Policy("P");
                    AccessPolicy q = Policy("Q");
                    AccessPolicy o = Policy("O");
                    Check.Equal(AccessTierEnum.Member, AccessDecider.ReadTier(p, ContentAudienceEnum.Members), "PartiallyGated Members needs Member");
                    Check.Equal(AccessTierEnum.SignedIn, AccessDecider.ReadTier(o, ContentAudienceEnum.Members), "Open treats Members as SignedIn");
                    Check.Equal(AccessTierEnum.Anonymous, AccessDecider.ReadTier(q, ContentAudienceEnum.Public), "Public needs nothing per object");
                    Check.Equal(AccessTierEnum.Member, AccessDecider.WriteTier(f, FeatureKeyEnum.Feed), "FullyGated writes need Member");
                    Check.Equal(AccessTierEnum.Member, AccessDecider.WriteTier(p, FeatureKeyEnum.Feed), "Gated feature writes need Member");
                    Check.Equal(AccessTierEnum.SignedIn, AccessDecider.WriteTier(q, FeatureKeyEnum.Feed), "Free feature writes need SignedIn");
                    Check.Equal(AccessTierEnum.SignedIn, AccessDecider.WriteTier(o, FeatureKeyEnum.Feed), "Open writes need SignedIn");
                    Check.Equal(AccessTierEnum.Member, AccessDecider.ParticipateTier(q, FeatureKeyEnum.Feed, ContentAudienceEnum.Members), "Participation takes the higher of read and write");
                    Check.Equal(AccessTierEnum.SignedIn, AccessDecider.ParticipateTier(q, FeatureKeyEnum.Feed, ContentAudienceEnum.Public), "Participation needs at least SignedIn");
                    Check.True(AccessDecider.ShowsLocked(q, AccessTierEnum.SignedIn, ContentAudienceEnum.Members), "Stubs for signed-in non-members in PartiallyGated");
                    Check.True(!AccessDecider.ShowsLocked(q, AccessTierEnum.Anonymous, ContentAudienceEnum.Members), "No stubs for signed-out callers");
                    Check.True(!AccessDecider.ShowsLocked(o, AccessTierEnum.SignedIn, ContentAudienceEnum.Members), "No stubs in Open");
                    Check.True(!AccessDecider.ShowsLocked(f, AccessTierEnum.SignedIn, ContentAudienceEnum.Members), "No stubs in FullyGated");
                    Check.True(!AccessDecider.RecipientMayOpen(f, ContentAudienceEnum.SignedIn, false, true, false), "FullyGated recipients need a membership");
                    Check.True(AccessDecider.RecipientMayOpen(q, ContentAudienceEnum.SignedIn, false, true, false), "PartiallyGated SignedIn content reaches non-members");
                    Check.True(!AccessDecider.RecipientMayOpen(q, ContentAudienceEnum.Members, false, true, false), "PartiallyGated Members content does not");
                    Check.True(AccessDecider.RecipientMayOpen(o, ContentAudienceEnum.Members, false, true, false), "Open reaches every attested account");
                    Check.True(!AccessDecider.RecipientMayOpen(o, ContentAudienceEnum.SignedIn, false, false, false), "Unattested recipients never");
                    Check.True(AccessDecider.RecipientsNeedMembership(f, ContentAudienceEnum.Public) && AccessDecider.RecipientsNeedMembership(q, ContentAudienceEnum.Members) && !AccessDecider.RecipientsNeedMembership(q, ContentAudienceEnum.SignedIn) && !AccessDecider.RecipientsNeedMembership(o, ContentAudienceEnum.Members), "Fan-out pre-filter");
                    Check.Equal(ContentAudienceEnum.Members, f.EffectiveDefaultAudience, "FullyGated default Members");
                    Check.Equal(ContentAudienceEnum.SignedIn, o.EffectiveDefaultAudience, "Open default SignedIn");
                    Check.Equal(ContentAudienceEnum.Members, p.DefaultAudienceFor(FeatureKeyEnum.Feed), "Gated feature default Members");
                    Check.Equal(ContentAudienceEnum.SignedIn, q.DefaultAudienceFor(FeatureKeyEnum.Feed), "Free feature takes the configured default");
                    Check.True(AccessPolicy.Validate(AccessModelEnum.Open, null, ContentAudienceEnum.Members).ContainsKey("defaultAudience"), "Members default rejected in Open");
                    Check.True(AccessPolicy.Validate(AccessModelEnum.PartiallyGated, new[] { "feed", "Feed" }, null).ContainsKey("gatedFeatures"), "Duplicate features rejected");
                    Check.True(AccessPolicy.Validate(AccessModelEnum.PartiallyGated, new[] { "chess" }, null).ContainsKey("gatedFeatures"), "Unknown features rejected");
                    Check.True(AccessPolicy.Validate(AccessModelEnum.PartiallyGated, AccessPolicy.FeatureKeys, ContentAudienceEnum.Public).Count == 0, "Every feature key is valid");
                    return Task.CompletedTask;
                }),
                SuiteCases.Unit(Id, "Sample.IsPartiallyGated", "The neutral sample property exercises the most general model: PartiallyGated with anonymous browsing, library and messaging gated, SignedIn default", ct =>
                {
                    PropertyAccess? access = TestProperty.LoadSample().Access;
                    Check.True(access != null && access.Model == AccessModelEnum.PartiallyGated && access.AnonymousBrowsing && access.DefaultAudience == ContentAudienceEnum.SignedIn, "Sample access section");
                    Check.True(AccessPolicy.Normalize(access!.GatedFeatures).SequenceEqual(new[] { FeatureKeyEnum.Library, FeatureKeyEnum.Messaging }), "Sample gated features");
                    return Task.CompletedTask;
                }),
                SuiteCases.Unit(Id, "PropertyDocument.Access", "property.json access: absent is FullyGated; Open makes membership optional; invalid sections are named problems", ct =>
                {
                    PropertyConfiguration sample = TestProperty.LoadSample();
                    sample.Access = null;
                    Check.Equal(AccessModelEnum.FullyGated, AccessPolicy.FromDocument(sample.Access).Model, "Absent access is FullyGated");
                    JsonObject document = (JsonObject)JsonNode.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(TestProperty.SampleDirectory, PropertyLoader.PropertyFileName)))!;
                    document.Remove("membership");
                    document["access"] = new JsonObject { ["model"] = "Open", ["anonymousBrowsing"] = true, ["defaultAudience"] = "SignedIn" };
                    PropertyConfiguration open = PropertyLoader.Parse(document.ToJsonString(), "test");
                    Check.True(PropertyValidator.Validate(open).Count == 0, "Open without membership is valid: " + String.Join("; ", PropertyValidator.Validate(open)));
                    Check.Equal(sample.Membership.MinimumAge == 0 ? 18 : 18, open.Membership.MinimumAge, "Minimum age defaults to 18 without a membership section");
                    document["access"] = new JsonObject { ["model"] = "PartiallyGated", ["gatedFeatures"] = new JsonArray("feed", "chess") };
                    List<string> problems = PropertyValidator.Validate(PropertyLoader.Parse(document.ToJsonString(), "test"));
                    Check.True(problems.Any(x => x.StartsWith("access.gatedFeatures", StringComparison.Ordinal)), "Unknown feature named: " + String.Join("; ", problems));
                    Check.True(problems.Any(x => x.StartsWith("membership.", StringComparison.Ordinal)), "A paid model needs the membership section");
                    return Task.CompletedTask;
                }),
                SuiteCases.Http(Id, "OpenApi.EveryMemberRouteDeclared", "Every post-authentication route of the Member group in the OpenAPI document has an AccessRoutes declaration, and under every model no route answers 402 in Open or skips the paywall in FullyGated", async (f, ct) =>
                {
                    JsonElement doc = await Api.ExpectAsync(f, HttpMethod.Get, "/openapi.json", null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    List<string> undeclared = new List<string>();
                    List<string> problems = new List<string>();
                    int checkedRoutes = 0;
                    foreach (JsonProperty path in doc.GetProperty("paths").EnumerateObject())
                    {
                        string concrete = System.Text.RegularExpressions.Regex.Replace(path.Name, "\\{[^}]+\\}", "x");
                        foreach (JsonProperty operation in path.Value.EnumerateObject())
                        {
                            string method = operation.Name.ToUpperInvariant();
                            if (method != "GET" && method != "POST" && method != "PUT" && method != "PATCH" && method != "DELETE") continue;
                            if (IsPreAuthentication(f, method, concrete)) continue;
                            checkedRoutes++;
                            RouteGroupEnum group = AuthorizationService.Classify(concrete);
                            if (group == RouteGroupEnum.Member && AccessRoutes.Find(method, concrete) == null) undeclared.Add(method + " " + path.Name);
                            AuthorizationDecision open = Authorizer(Policy("O")).Authorize(Caller("signedIn"), method, concrete);
                            if (open.StatusCode == 402) problems.Add("Open answers 402 on " + method + " " + path.Name);
                            AuthorizationDecision full = Authorizer(Policy("F")).Authorize(Caller("signedIn"), method, concrete);
                            if (group == RouteGroupEnum.Member && full.StatusCode != 402) problems.Add("FullyGated lets a non-member reach " + method + " " + path.Name);
                            AuthorizationDecision anonymous = Authorizer(Policy("F")).Authorize(Caller("anonymous"), method, concrete);
                            if (anonymous.IsPermitted) problems.Add("FullyGated lets a signed-out caller reach " + method + " " + path.Name);
                        }
                    }
                    Check.True(checkedRoutes > 100, "Post-authentication routes checked: " + checkedRoutes);
                    Check.True(undeclared.Count == 0, "Member group routes without an AccessRoutes declaration (add a rule in AccessRoutes.Find):\n  " + String.Join("\n  ", undeclared));
                    Check.True(problems.Count == 0, String.Join("\n  ", problems));
                }),
                SuiteCases.Http(Id, "Switching.RoutesFollowTheModel", "Switching FullyGated, PartiallyGated, Open, and back through the admin route applies immediately, is audited, keeps entitlements, and validates the body", async (f, ct) =>
                {
                    TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
                    TestMember free = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    JsonElement current = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/property/access", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("FullyGated", Api.Str(current, "model"), "The fixture seeds FullyGated");
                    await ExpectAccessErrorAsync(f, HttpMethod.Get, "/api/v1/feed", free.AccessToken, HttpStatusCode.PaymentRequired, "MembershipRequired", null, ct).ConfigureAwait(false);
                    Check.True((await MeAsync(f, free, ct).ConfigureAwait(false)).GetProperty("membershipRequired").GetBoolean(), "FullyGated: the paywall step is required");

                    JsonElement partial = await SetAsync(f, new { model = "PartiallyGated", gatedFeatures = new[] { "feed" }, anonymousBrowsing = true, defaultAudience = "SignedIn" }, ct).ConfigureAwait(false);
                    Check.Equal(2, partial.GetProperty("revision").GetInt32(), "Revision bumped");
                    await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed", free.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed", null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await SafetyHelpers.UpdateUserAsync(f, free.Id, u => u.CanPostToFeed = true, ct).ConfigureAwait(false);
                    await ExpectAccessErrorAsync(f, HttpMethod.Post, "/api/v1/feed", free.AccessToken, HttpStatusCode.PaymentRequired, "MembershipRequired", "Membership", ct, new { body = "free post" }).ConfigureAwait(false);
                    Check.True(!(await MeAsync(f, free, ct).ConfigureAwait(false)).GetProperty("membershipRequired").GetBoolean(), "PartiallyGated: no paywall step");
                    await SetAsync(f, new { gatedFeatures = Array.Empty<string>() }, ct).ConfigureAwait(false);
                    JsonElement created = await ContentTestKit.PostAsync(f, free.AccessToken, new { body = "now free" }, ct).ConfigureAwait(false);
                    Check.Equal("SignedIn", Api.Str(created, "audience"), "New posts take the property default audience");

                    JsonElement open = await SetAsync(f, new { model = "Open" }, ct).ConfigureAwait(false);
                    Check.True(!open.GetProperty("purchasesEnabled").GetBoolean() && !open.GetProperty("signupCodesEnabled").GetBoolean(), "Open has no purchases or codes");
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/membership", member.AccessToken, null, HttpStatusCode.NotFound, "NotAvailable", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/membership/stripe/checkout", free.AccessToken, null, HttpStatusCode.NotFound, "NotAvailable", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/signup-codes/validate", free.AccessToken, new { code = "ANY" }, HttpStatusCode.NotFound, "NotAvailable", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/members/" + free.Id + "/comp", HttpFixture.AdminToken, new { grantType = "Lifetime", reason = "x" }, HttpStatusCode.NotFound, "NotAvailable", ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/subscriptions", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/membership/history", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    JsonElement providers = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/auth/providers", null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(!providers.GetProperty("stripe").GetProperty("enabled").GetBoolean() && !providers.GetProperty("appleIap").GetProperty("enabled").GetBoolean() && !providers.GetProperty("googlePlay").GetProperty("enabled").GetBoolean(), "Stores disabled in Open");
                    JsonElement property = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/property", null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("Open", Api.Str(property.GetProperty("access"), "model"), "GET /property reports the model");
                    Check.Equal(JsonValueKind.Null, property.GetProperty("membership").ValueKind, "No membership product in Open");
                    Check.Equal(f.Property.Membership.MinimumAge, property.GetProperty("minimumAge").GetInt32(), "The minimum age stays property-configured");
                    JsonElement memberMe = await MeAsync(f, member, ct).ConfigureAwait(false);
                    Check.True(!memberMe.GetProperty("membership").GetProperty("isActive").GetBoolean(), "Me.membership is inactive in Open");
                    Check.Equal("SignedIn", Api.Str(memberMe, "accessTier"), "Everyone is SignedIn in Open");
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/feed", free.AccessToken, new { body = "x", audience = "Members" }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/users/" + member.Id, free.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);

                    await SetAsync(f, new { model = "FullyGated" }, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/membership", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("Member", Api.Str(await MeAsync(f, member, ct).ConfigureAwait(false), "accessTier"), "Switching back restores the member");
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/feed", free.AccessToken, null, HttpStatusCode.PaymentRequired, "MembershipRequired", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/feed", null, null, HttpStatusCode.Unauthorized, "Unauthorized", ct).ConfigureAwait(false);

                    await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/admin/property/access", HttpFixture.AdminToken, new { model = "Open", defaultAudience = "Members" }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/admin/property/access", HttpFixture.AdminToken, new { gatedFeatures = new[] { "chess" } }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/admin/property/access", free.AccessToken, new { model = "Open" }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/property/access", admin.AccessToken, new { model = "FullyGated" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    long audits = await ContentTestKit.ScalarAsync(f, "SELECT COUNT(*) AS cnt FROM auditrecords WHERE action = 'property.access.update';", ct).ConfigureAwait(false);
                    Check.Equal(5L, audits, "Every change is audited");
                    AccessPolicy? stored = await f.Database.PropertyAccess.ReadAsync(ct).ConfigureAwait(false);
                    Check.True(stored != null && stored.Model == AccessModelEnum.FullyGated && stored.Revision == 6, "Stored row follows the edits");
                }),
                SuiteCases.Http(Id, "Switching.DefaultAudienceNormalized", "A default audience the model cannot use is coerced: FullyGated stores none (always Members) and switching to Open drops a stored Members default; the admin view reports the effective value", async (f, ct) =>
                {
                    JsonElement gated = await SetAsync(f, new { model = "FullyGated", defaultAudience = "SignedIn" }, ct).ConfigureAwait(false);
                    Check.Equal("Members", Api.Str(gated, "defaultAudience"), "FullyGated: the effective default is Members");
                    Check.Equal(JsonValueKind.Null, gated.GetProperty("configuredDefaultAudience").ValueKind, "FullyGated: no configured default is kept");
                    AccessPolicy? stored = await f.Database.PropertyAccess.ReadAsync(ct).ConfigureAwait(false);
                    Check.True(stored != null && stored.DefaultAudience == null, "FullyGated stores no default audience");
                    JsonElement partial = await SetAsync(f, new { model = "PartiallyGated", defaultAudience = "Members" }, ct).ConfigureAwait(false);
                    Check.Equal("Members", Api.Str(partial, "configuredDefaultAudience"), "PartiallyGated keeps a Members default");
                    JsonElement open = await SetAsync(f, new { model = "Open" }, ct).ConfigureAwait(false);
                    Check.Equal("SignedIn", Api.Str(open, "defaultAudience"), "Open: the stale Members default is dropped");
                    Check.Equal(JsonValueKind.Null, open.GetProperty("configuredDefaultAudience").ValueKind, "Open: configured default returns to the model default");
                    await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/admin/property/access", HttpFixture.AdminToken, new { defaultAudience = "Members" }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    await SetAsync(f, new { model = "FullyGated", defaultAudience = (string?)null }, ct).ConfigureAwait(false);
                }),
                SuiteCases.Http(Id, "Audience.PartiallyGated", "PartiallyGated: SignedIn content reaches free accounts, Members content shows as a locked stub in lists and 402 on direct reads, signed-out visitors see only Public content (401 with required SignIn otherwise), participation follows the gated feature, and the public feed stays Public only", async (f, ct) =>
                {
                    ContentTestKit.Relax(f);
                    await SetAsync(f, new { model = "PartiallyGated", anonymousBrowsing = true, gatedFeatures = Array.Empty<string>(), defaultAudience = "SignedIn" }, ct).ConfigureAwait(false);
                    TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
                    TestMember free = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    string publicPost = Api.Str(await ContentTestKit.PostAsync(f, admin.AccessToken, new { body = "public", audience = "Public" }, ct).ConfigureAwait(false), "id");
                    string signedInPost = Api.Str(await ContentTestKit.PostAsync(f, admin.AccessToken, new { body = "signed in", audience = "SignedIn" }, ct).ConfigureAwait(false), "id");
                    string membersPost = Api.Str(await ContentTestKit.PostAsync(f, admin.AccessToken, new { title = "Members session", body = "members only body", audience = "Members" }, ct).ConfigureAwait(false), "id");

                    JsonElement freeFeed = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed", free.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    List<string> freeIds = ContentTestKit.Ids(freeFeed);
                    Check.True(freeIds.Contains(publicPost) && freeIds.Contains(signedInPost) && freeIds.Contains(membersPost), "Free accounts list all three");
                    JsonElement stub = freeFeed.GetProperty("objects").EnumerateArray().First(o => Api.Str(o, "id") == membersPost);
                    Check.True(stub.GetProperty("locked").GetBoolean(), "Members post is locked");
                    Check.Equal(String.Empty, Api.Str(stub, "body"), "Stub has no body");
                    Check.Equal("Members session", Api.Str(stub, "title"), "Stub keeps the title");
                    Check.True(!stub.GetProperty("viewer").GetProperty("canOpen").GetBoolean() && Api.Str(stub.GetProperty("viewer"), "requires") == "Membership", "Stub viewer");
                    JsonElement open = freeFeed.GetProperty("objects").EnumerateArray().First(o => Api.Str(o, "id") == signedInPost);
                    Check.True(!open.GetProperty("locked").GetBoolean() && open.GetProperty("viewer").GetProperty("canComment").GetBoolean(), "SignedIn post open and commentable");
                    ApiResult locked = await Api.SendAsync(f, HttpMethod.Get, "/api/v1/feed/" + membersPost, free.AccessToken, null, ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.PaymentRequired, locked.Status, "Direct read of a Members post: " + locked.Text);
                    Check.Equal("Membership", Api.Str(locked.Body, "required"), "required");
                    Check.Equal("feed", Api.Str(locked.Body, "feature"), "feature");
                    Check.Equal("FeedPost", Api.Str(locked.Body, "targetType"), "targetType");
                    Check.Equal(membersPost, Api.Str(locked.Body, "targetId"), "targetId");
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/feed/" + membersPost + "/comments", free.AccessToken, null, HttpStatusCode.PaymentRequired, "MembershipRequired", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/feed/" + membersPost + "/reactions/like", free.AccessToken, null, HttpStatusCode.PaymentRequired, "MembershipRequired", ct).ConfigureAwait(false);
                    await ContentTestKit.CommentAsync(f, free.AccessToken, signedInPost, "free comment", null, ct).ConfigureAwait(false);
                    JsonElement memberFeed = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(memberFeed.GetProperty("objects").EnumerateArray().All(o => !o.GetProperty("locked").GetBoolean()), "Members see nothing locked");
                    await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed/" + membersPost, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    JsonElement filtered = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed?audience=SignedIn", free.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(ContentTestKit.Ids(filtered).SequenceEqual(new[] { signedInPost }), "Audience filter");

                    JsonElement anonymous = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed", null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(ContentTestKit.Ids(anonymous).SequenceEqual(new[] { publicPost }), "Signed-out visitors list only Public posts, never stubs");
                    JsonElement anonymousPost = anonymous.GetProperty("objects")[0];
                    Check.True(!anonymousPost.GetProperty("viewer").GetProperty("canComment").GetBoolean() && !anonymousPost.GetProperty("viewer").GetProperty("canReact").GetBoolean(), "Signed-out visitors cannot take part");
                    await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed/" + publicPost, null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed/" + publicPost + "/comments", null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    ApiResult signIn = await Api.SendAsync(f, HttpMethod.Get, "/api/v1/feed/" + signedInPost, null, null, ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.Unauthorized, signIn.Status, "Signed-out read of a SignedIn post");
                    Check.Equal("SignIn", Api.Str(signIn.Body, "required"), "required SignIn");
                    await ExpectAccessErrorAsync(f, HttpMethod.Post, "/api/v1/feed", null, HttpStatusCode.Unauthorized, "Unauthorized", "SignIn", ct, new { body = "x" }).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/notifications", null, null, HttpStatusCode.Unauthorized, "Unauthorized", ct).ConfigureAwait(false);
                    JsonElement publicFeed = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/public/feed", null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(ContentTestKit.Ids(publicFeed).SequenceEqual(new[] { publicPost }), "The public (SEO) feed lists only Public posts");
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/public/feed/" + membersPost, null, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);

                    await SetAsync(f, new { gatedFeatures = new[] { "feed" }, anonymousBrowsing = false }, ct).ConfigureAwait(false);
                    JsonElement gated = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed/" + signedInPost, free.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(!gated.GetProperty("viewer").GetProperty("canComment").GetBoolean(), "A gated feature takes away participation for free accounts");
                    await ExpectAccessErrorAsync(f, HttpMethod.Post, "/api/v1/feed/" + signedInPost + "/comments", free.AccessToken, HttpStatusCode.PaymentRequired, "MembershipRequired", "Membership", ct, new { body = "x" }).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/feed", null, null, HttpStatusCode.Unauthorized, "Unauthorized", ct).ConfigureAwait(false);
                    JsonElement memberPost = await ContentTestKit.PostAsync(f, admin.AccessToken, new { body = "gated default" }, ct).ConfigureAwait(false);
                    Check.Equal("Members", Api.Str(memberPost, "audience"), "A gated feature defaults new objects to Members");
                }),
                SuiteCases.Http(Id, "Audience.Open", "Open: stored Members content is SignedIn content, no stubs, no 402 anywhere, and the age attestation still applies", async (f, ct) =>
                {
                    TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
                    string membersPost = Api.Str(await ContentTestKit.PostAsync(f, admin.AccessToken, new { body = "made while FullyGated" }, ct).ConfigureAwait(false), "id");
                    await SetAsync(f, new { model = "Open", anonymousBrowsing = false }, ct).ConfigureAwait(false);
                    TestMember free = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    JsonElement post = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed/" + membersPost, free.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(!post.GetProperty("locked").GetBoolean() && post.GetProperty("viewer").GetProperty("canComment").GetBoolean(), "Members content is open to every account in Open");
                    TestMember social = await Api.SocialAsync(f, ct).ConfigureAwait(false);
                    ApiResult attest = await Api.SendAsync(f, HttpMethod.Get, "/api/v1/feed", social.AccessToken, null, ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.Forbidden, attest.Status, "Unattested accounts still attest first");
                    Check.True(attest.Text.Contains(f.Property.Membership.MinimumAge + " or older", StringComparison.Ordinal), "Quotes the property minimum age: " + attest.Text);
                    Check.True(!(await MeAsync(f, free, ct).ConfigureAwait(false)).GetProperty("membershipRequired").GetBoolean(), "No paywall step in Open");
                }),
                SuiteCases.Http(Id, "Fanout.FollowsTheModel", "New post notifications and emails reach exactly the recipients who may open the post under each model", async (f, ct) =>
                {
                    NotificationTestKit.AllDay(f);
                    TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
                    TestMember free = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    NotificationFanoutWorker worker = NotificationTestKit.FanoutWorker(f);
                    worker.Access = () => f.Services.Access.Current;

                    async Task<bool> ReachesAsync(string userId, string postId)
                    {
                        await worker.RunOnceAsync(ct).ConfigureAwait(false);
                        return (await NotificationTestKit.ListAsync(f, userId, ct).ConfigureAwait(false)).Any(n => n.TargetId == postId && n.Type == NotificationTypeEnum.NewFeedPost);
                    }

                    string fully = Api.Str(await ContentTestKit.PostAsync(f, admin.AccessToken, new { body = "fully" }, ct).ConfigureAwait(false), "id");
                    Check.True(await ReachesAsync(member.Id, fully).ConfigureAwait(false) && !await ReachesAsync(free.Id, fully).ConfigureAwait(false), "FullyGated: members only");
                    await SetAsync(f, new { model = "PartiallyGated", defaultAudience = "SignedIn" }, ct).ConfigureAwait(false);
                    string signedIn = Api.Str(await ContentTestKit.PostAsync(f, admin.AccessToken, new { body = "signed in", audience = "SignedIn" }, ct).ConfigureAwait(false), "id");
                    string members = Api.Str(await ContentTestKit.PostAsync(f, admin.AccessToken, new { body = "members", audience = "Members" }, ct).ConfigureAwait(false), "id");
                    Check.True(await ReachesAsync(free.Id, signedIn).ConfigureAwait(false), "PartiallyGated SignedIn post reaches free accounts");
                    Check.True(!await ReachesAsync(free.Id, members).ConfigureAwait(false) && await ReachesAsync(member.Id, members).ConfigureAwait(false), "PartiallyGated Members post reaches members only");
                    await SetAsync(f, new { model = "Open" }, ct).ConfigureAwait(false);
                    string open = Api.Str(await ContentTestKit.PostAsync(f, admin.AccessToken, new { body = "open" }, ct).ConfigureAwait(false), "id");
                    Check.True(await ReachesAsync(free.Id, open).ConfigureAwait(false) && await ReachesAsync(member.Id, open).ConfigureAwait(false), "Open reaches every account");
                }),
                SuiteCases.Http(Id, "Seeding.DocumentAndFolder", "The stored row is seeded from property.json access, admin edits stand while the document is unchanged, and a changed folder section wins on the next load (SingleProperty)", p =>
                {
                    p.Access = new PropertyAccess { Model = AccessModelEnum.PartiallyGated, AnonymousBrowsing = true, GatedFeatures = new List<string> { "members" }, DefaultAudience = ContentAudienceEnum.SignedIn };
                }, async (f, ct) =>
                {
                    AccessPolicy seeded = (await f.Database.PropertyAccess.ReadAsync(ct).ConfigureAwait(false))!;
                    Check.True(seeded.Model == AccessModelEnum.PartiallyGated && seeded.GatedFeatures.SequenceEqual(new[] { FeatureKeyEnum.Members }) && seeded.AnonymousBrowsing, "Seeded from the document");
                    await SetAsync(f, new { model = "Open" }, ct).ConfigureAwait(false);
                    await f.Services.Access.EnsureSeededAsync(true, ct).ConfigureAwait(false);
                    Check.Equal(AccessModelEnum.Open, f.Services.Access.Current.Model, "An unchanged document keeps the admin edit");
                    f.Property.Access!.Model = AccessModelEnum.FullyGated;
                    await f.Services.Access.EnsureSeededAsync(false, ct).ConfigureAwait(false);
                    Check.Equal(AccessModelEnum.Open, f.Services.Access.Current.Model, "Without the folder rule the stored value stands");
                    await f.Services.Access.EnsureSeededAsync(true, ct).ConfigureAwait(false);
                    Check.Equal(AccessModelEnum.FullyGated, f.Services.Access.Current.Model, "A changed folder section wins");
                    JsonElement property = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/property", null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("FullyGated", Api.Str(property.GetProperty("access"), "model"), "GET /property follows");
                    Check.Equal(1L, await ContentTestKit.ScalarAsync(f, "SELECT COUNT(*) AS cnt FROM auditrecords WHERE action = 'property.access.sync';", ct).ConfigureAwait(false), "The folder sync is audited");
                }),
                SuiteCases.Http(Id, "Isolation.OpenAndFullyGated", "One MultiProperty deployment: the sample FullyGated and a second property Open; each property's routes, Me, and GET /property follow its own model, and changing one never changes the other", MultiPropertyKit.Multi, async (f, ct) =>
                {
                    string platform = await MultiPropertyKit.PlatformLoginAsync(f, ct).ConfigureAwait(false);
                    JsonObject document = MultiPropertyKit.SecondDocument();
                    document.Remove("membership");
                    document["access"] = new JsonObject { ["model"] = "Open", ["anonymousBrowsing"] = true, ["defaultAudience"] = "SignedIn" };
                    JsonElement created = await MultiPropertyKit.PlatformExpectAsync(f, HttpMethod.Post, "/api/v1/platform/properties", platform, new { document }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                    string second = Api.Str(created, "id");
                    await MultiPropertyKit.PlatformExpectAsync(f, HttpMethod.Post, "/api/v1/platform/properties/" + second + "/enable", platform, null, HttpStatusCode.OK, ct).ConfigureAwait(false);

                    f.PropertyId = TestProperty.SampleId;
                    TestMember freeA = await Api.RegisterAsync(f, ct, "same@example.com").ConfigureAwait(false);
                    f.PropertyId = second;
                    TestMember freeB = await Api.RegisterAsync(f, ct, "same@example.com").ConfigureAwait(false);

                    f.PropertyId = TestProperty.SampleId;
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/feed", freeA.AccessToken, null, HttpStatusCode.PaymentRequired, "MembershipRequired", ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/membership", freeA.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/feed", null, null, HttpStatusCode.Unauthorized, "Unauthorized", ct).ConfigureAwait(false);
                    Check.Equal("FullyGated", Api.Str((await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/property", null, null, HttpStatusCode.OK, ct).ConfigureAwait(false)).GetProperty("access"), "model"), "A reports FullyGated");

                    f.PropertyId = second;
                    await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed", freeB.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed", null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/membership", freeB.AccessToken, null, HttpStatusCode.NotFound, "NotAvailable", ct).ConfigureAwait(false);
                    JsonElement propertyB = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/property", null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("Open", Api.Str(propertyB.GetProperty("access"), "model"), "B reports Open");
                    Check.Equal(JsonValueKind.Null, propertyB.GetProperty("membership").ValueKind, "B has no membership product");
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/feed", freeA.AccessToken, null, HttpStatusCode.Unauthorized, "Unauthorized", ct).ConfigureAwait(false);

                    await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/property/access", HttpFixture.AdminToken, new { model = "PartiallyGated" }, HttpStatusCode.BadRequest, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/property/access", HttpFixture.AdminToken, new { anonymousBrowsing = false }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/feed", null, null, HttpStatusCode.Unauthorized, "Unauthorized", ct).ConfigureAwait(false);
                    f.PropertyId = TestProperty.SampleId;
                    await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/property/access", HttpFixture.AdminToken, new { model = "Open", anonymousBrowsing = true }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed", null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    f.PropertyId = second;
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/feed", null, null, HttpStatusCode.Unauthorized, "Unauthorized", ct).ConfigureAwait(false);
                    Check.True(!(await f.Database.PropertyAccess.ReadAsync(ct).ConfigureAwait(false))!.AnonymousBrowsing, "B's row is its own");
                    f.PropertyId = TestProperty.SampleId;
                    Check.Equal(AccessModelEnum.Open, (await f.Database.PropertyAccess.ReadAsync(ct).ConfigureAwait(false))!.Model, "A's row is its own");
                })
            });
        }

        #endregion

        #region Private-Methods

        private static AccessPolicy Policy(string model)
        {
            switch (model)
            {
                case "F":
                    return new AccessPolicy { Model = AccessModelEnum.FullyGated, AnonymousBrowsing = true, GatedFeatures = new List<FeatureKeyEnum> { FeatureKeyEnum.Feed } };
                case "P":
                    return new AccessPolicy { Model = AccessModelEnum.PartiallyGated, AnonymousBrowsing = true, GatedFeatures = new List<FeatureKeyEnum> { FeatureKeyEnum.Feed, FeatureKeyEnum.Members } };
                case "Q":
                    return new AccessPolicy { Model = AccessModelEnum.PartiallyGated, AnonymousBrowsing = false, DefaultAudience = ContentAudienceEnum.SignedIn };
                default:
                    return new AccessPolicy { Model = AccessModelEnum.Open, AnonymousBrowsing = true };
            }
        }

        private static AuthorizationService Authorizer(AccessPolicy policy)
        {
            return new AuthorizationService(18) { Access = () => policy };
        }

        private static RequestContext Caller(string caller)
        {
            switch (caller)
            {
                case "anonymous":
                    return new RequestContext { IsAuthenticated = false, PrincipalKind = PrincipalKindEnum.Anonymous, Scheme = ActorService.AnonymousScheme };
                case "unattested":
                    return new RequestContext { IsAuthenticated = true, UserId = "usr_unattested" };
                case "signedIn":
                    return new RequestContext { IsAuthenticated = true, UserId = "usr_signedin", AgeAttested = true };
                case "member":
                    return new RequestContext { IsAuthenticated = true, UserId = "usr_member", AgeAttested = true, HasMembership = true };
                case "moderator":
                    return new RequestContext { IsAuthenticated = true, UserId = "usr_moderator", AgeAttested = true, IsModerator = true };
                default:
                    return new RequestContext { IsAuthenticated = true, UserId = "usr_admin", AgeAttested = true, HasMembership = true, IsAdmin = true };
            }
        }

        private static char Code(AuthorizationDecision decision)
        {
            if (decision.IsPermitted) return 'A';
            switch (decision.StatusCode)
            {
                case 401: return '1';
                case 402: return '2';
                case 403: return '3';
                case 404: return '4';
                default: return '?';
            }
        }

        private static bool IsPreAuthentication(HttpFixture f, string method, string path)
        {
            WatsonWebserver.Core.HttpMethod m = Enum.Parse<WatsonWebserver.Core.HttpMethod>(method);
            WatsonWebserver.Core.Routing.RoutingGroup group = f.Server.Server.Routes.PreAuthentication;
            if (group.Static.Match(m, path, out WatsonWebserver.Core.Routing.StaticRoute? _) != null) return true;
            if (group.Parameter.Match(m, path, out System.Collections.Specialized.NameValueCollection _, out WatsonWebserver.Core.Routing.ParameterRoute? _) != null) return true;
            return group.Dynamic.Match(m, path, out WatsonWebserver.Core.Routing.DynamicRoute? _) != null;
        }

        private static async Task<JsonElement> SetAsync(HttpFixture f, object body, CancellationToken ct)
        {
            return await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/property/access", HttpFixture.AdminToken, body, HttpStatusCode.OK, ct).ConfigureAwait(false);
        }

        private static async Task<JsonElement> MeAsync(HttpFixture f, TestMember member, CancellationToken ct)
        {
            return await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/auth/me", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
        }

        private static async Task ExpectAccessErrorAsync(HttpFixture f, HttpMethod method, string path, string? bearer, HttpStatusCode status, string error, string? required, CancellationToken ct, object? body = null)
        {
            ApiResult result = await Api.SendAsync(f, method, path, bearer, body, ct).ConfigureAwait(false);
            Check.Equal(status, result.Status, method + " " + path + " status (body: " + result.Text + ")");
            Check.Equal(error, result.Error, method + " " + path + " error");
            if (required != null) Check.Equal(required, Api.Str(result.Body, "required"), method + " " + path + " required");
        }

        #endregion
    }
}

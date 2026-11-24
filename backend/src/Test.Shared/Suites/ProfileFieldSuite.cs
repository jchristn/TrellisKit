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
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.PropertyConfig;
    using TrellisKit.Server.Services;

    /// <summary>
    /// Configurable profile fields (docs/MULTIPROPERTY_CONTRACT.md section 9; docs/TRELLISKIT.md section 11): seeding from
    /// the sample's profileFields, the client definitions on GET /api/v1/property, admin definition rules, value
    /// validation per type, visibility per payload, staff writes, member directory filters, the legacy compatibility
    /// mapping with dual writes, the value backfill, and audits of every profile edit.
    /// </summary>
    public static class ProfileFieldSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "ProfileField";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Configurable profile fields (" + TestEnvironment.Provider + ")", new[]
            {
                SuiteCases.Unit(Id, "Rules.Definitions", "Definition rules: key pattern and reserved keys, options for select and rank types, sub-fields, collectAt and editableBy pairs, and bounds", ct =>
                {
                    List<ProfileField> none = new List<ProfileField>();
                    Check.True(ProfileFieldRules.Validate(new ProfileField { Key = "Bad Key", Label = "x" }, none, String.Empty).ContainsKey("key"), "Key pattern");
                    Check.True(ProfileFieldRules.Validate(new ProfileField { Key = "email", Label = "x" }, none, String.Empty).ContainsKey("key"), "Reserved key");
                    Check.True(ProfileFieldRules.Validate(new ProfileField { Key = "level", Label = "Level", Type = ProfileFieldTypeEnum.Rank }, none, String.Empty).ContainsKey("options"), "Rank needs options");
                    ProfileField rank = new ProfileField { Key = "level", Label = "Level", Type = ProfileFieldTypeEnum.Rank, Options = new List<ProfileFieldOption> { new ProfileFieldOption { Key = "A", Label = "A" } } };
                    Check.True(ProfileFieldRules.Validate(rank, none, String.Empty).ContainsKey("options[0].color"), "Rank options need a color");
                    Check.True(ProfileFieldRules.Validate(new ProfileField { Key = "notes", Label = "Notes", CollectAt = ProfileCollectAtEnum.AdminOnly, EditableBy = ProfileEditableByEnum.Member }, none, String.Empty).ContainsKey("editableBy"), "AdminOnly needs Staff");
                    Check.True(ProfileFieldRules.Validate(new ProfileField { Key = "notes", Label = "Notes", Visibility = ProfileVisibilityEnum.Staff }, none, String.Empty).ContainsKey("visibility"), "Staff visibility needs AdminOnly");
                    Check.True(ProfileFieldRules.Validate(new ProfileField { Key = "count", Label = "Count", Type = ProfileFieldTypeEnum.Number, Validation = new ProfileFieldValidation { MinValue = 5, MaxValue = 1 } }, none, String.Empty).ContainsKey("validation.minValue"), "Inverted bounds");
                    Check.True(ProfileFieldRules.Validate(new ProfileField { Key = "about", Label = "About", Type = ProfileFieldTypeEnum.LongText, Filterable = true }, none, String.Empty).ContainsKey("filterable"), "LongText is not filterable");
                    Check.True(ProfileFieldRules.Validate(new ProfileField { Key = "code", Label = "Code", Validation = new ProfileFieldValidation { Pattern = "([a-" } }, none, String.Empty).ContainsKey("validation.pattern"), "Invalid pattern");
                    ProfileField history = new ProfileField { Key = "past", Label = "Past", Type = ProfileFieldTypeEnum.History, SubFields = new List<ProfileSubField> { new ProfileSubField { Key = "level", Label = "Level", Type = ProfileSubFieldTypeEnum.RankValue, RankFieldKey = "missing" } } };
                    Check.True(ProfileFieldRules.Validate(history, none, String.Empty).ContainsKey("subFields[0].rankFieldKey"), "RankValue names a rank field");
                    Check.Equal(0, ProfileFieldRules.Validate(new ProfileField { Key = "favoriteColor", Label = "Favorite color" }, none, String.Empty).Count, "A plain text field is valid");
                    List<ProfileField> seeded = ProfileFieldRules.FromProfile(TestProperty.LoadSample().Profile);
                    Check.True(new[] { "rank", "affiliation", "city", "region", "country", "goals", "rankHistory" }.All(k => seeded.Any(f => f.Key == k)), "Version 1 conversion seeds the Phase 1 fields");
                    return Task.CompletedTask;
                }),

                SuiteCases.Http(Id, "Seed.PropertyRoute", "The sample's profileFields are seeded with the built-ins; GET /api/v1/property returns slug, profileFields in profile order, the generated legacy profile, features.ai, and an ETag that answers 304", async (f, ct) =>
                {
                    ApiResult first = await Api.SendAsync(f, HttpMethod.Get, "/api/v1/property", null, null, ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.OK, first.Status, "Property");
                    Check.Equal(f.Property.EffectiveSlug, Api.Str(first.Body, "slug"), "slug");
                    List<string> keys = first.Body.GetProperty("profileFields").EnumerateArray().Select(e => Api.Str(e, "key")).ToList();
                    Check.True(keys.Take(2).SequenceEqual(new[] { "displayName", "handle" }), "Built-ins first (" + String.Join(",", keys) + ")");
                    Check.True(new[] { "rank", "affiliation", "city", "region", "country", "goals", "rankHistory", "bio", "avatar" }.All(keys.Contains), "Seeded fields (" + String.Join(",", keys) + ")");
                    Check.Equal(keys.IndexOf("rank") < keys.IndexOf("goals"), true, "Profile order");
                    JsonElement legacy = first.Body.GetProperty("profile");
                    Check.Equal(f.Property.Profile.Rank.Values.Count, legacy.GetProperty("rank").GetProperty("values").GetArrayLength(), "Legacy rank values from the field");
                    Check.True(!first.Body.GetProperty("features").GetProperty("ai").GetBoolean(), "features.ai is false");
                    Check.True(first.Headers.TryGetValue("ETag", out string? etag) && etag.StartsWith("\"v", StringComparison.Ordinal), "ETag");
                    ApiResult again = await Api.SendAsync(f, HttpMethod.Get, "/api/v1/property", null, null, ct, "mobile", new Dictionary<string, string> { { "If-None-Match", etag! } }).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.NotModified, again.Status, "304 for a matching If-None-Match");
                    JsonElement admin = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/profile-fields/enumerate", HttpFixture.AdminToken, new { maxResults = 100, filters = new { builtIn = true } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(4, admin.GetProperty("objects").GetArrayLength(), "Four built-ins");
                }),

                SuiteCases.Http(Id, "Admin.DefinitionLifecycle", "Create, duplicate key 409, update, type change with values 409, option removal with values 409, stats, preview, reorder, built-in locks, and delete with confirmation, each audited", async (f, ct) =>
                {
                    JsonElement created = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/profile-fields", HttpFixture.AdminToken, new
                    {
                        key = "favoriteColor", label = "Favorite color", type = "SingleSelect", options = new object[] { new { key = "Red", label = "Red" }, new { key = "Other", label = "Other", freeText = true } },
                        collectAt = "Profile", filterable = true, labels = new[] { "profile-test" }
                    }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                    string id = Api.Str(created, "id");
                    Check.True(id.StartsWith("pfd_", StringComparison.Ordinal), "pfd_ id");
                    Check.True(created.GetProperty("options")[0].GetProperty("id").GetString()!.StartsWith("pfo_", StringComparison.Ordinal), "pfo_ option ids");
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/profile-fields", HttpFixture.AdminToken, new { key = "favoriteColor", label = "Again" }, HttpStatusCode.Conflict, "FieldKeyInUse", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/profile-fields", HttpFixture.AdminToken, new { key = "email", label = "Email" }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    JsonElement updated = await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/profile-fields/" + id, HttpFixture.AdminToken, new { label = "Colour" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("Colour", Api.Str(updated, "label"), "Label updated");

                    TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/me/profile", member.AccessToken, new { profile = new { favoriteColor = new { key = "Other", otherText = "Teal" } } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/admin/profile-fields/" + id, HttpFixture.AdminToken, new { type = "Text" }, HttpStatusCode.Conflict, "FieldHasValues", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/admin/profile-fields/" + id, HttpFixture.AdminToken, new { options = new object[] { new { key = "Red", label = "Red" } } }, HttpStatusCode.Conflict, "OptionInUse", ct).ConfigureAwait(false);
                    JsonElement retired = await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/profile-fields/" + id, HttpFixture.AdminToken,
                        new { options = new object[] { new { id = Api.Str(created.GetProperty("options")[0], "id"), key = "Red", label = "Red" }, new { id = Api.Str(created.GetProperty("options")[1], "id"), key = "Other", label = "Other", freeText = true, retired = true } } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(retired.GetProperty("options")[1].GetProperty("retired").GetBoolean(), "Retired instead");
                    JsonElement stats = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/profile-fields/" + id + "/stats", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(1L, stats.GetProperty("valueCount").GetInt64(), "valueCount");
                    Check.Equal(1L, stats.GetProperty("objects").EnumerateArray().First(o => Api.Str(o, "key") == "Other").GetProperty("count").GetInt64(), "Option count");
                    JsonElement preview = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/profile-fields/preview", HttpFixture.AdminToken,
                        new { target = "ProfilePage", viewer = "Member", values = new { rank = new { key = "Intermediate", level = 2 } } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    JsonElement item = preview.GetProperty("items").EnumerateArray().First(i => Api.Str(i, "key") == "rank");
                    Check.Equal("rankPicker", Api.Str(item, "control"), "Rank control");
                    Check.Equal("Intermediate, 2 " + f.Property.Profile.Rank.LevelLabel, Api.Str(item, "formattedValue"), "formatRank from configuration");

                    JsonElement bio = (await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/profile-fields/enumerate", HttpFixture.AdminToken, new { search = "bio", filters = new { builtIn = true } }, HttpStatusCode.OK, ct).ConfigureAwait(false)).GetProperty("objects")[0];
                    await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/admin/profile-fields/" + Api.Str(bio, "id"), HttpFixture.AdminToken, new { type = "Text" }, HttpStatusCode.Conflict, "BuiltInField", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Delete, "/api/v1/admin/profile-fields/" + Api.Str(bio, "id"), HttpFixture.AdminToken, null, HttpStatusCode.Conflict, "BuiltInField", ct).ConfigureAwait(false);

                    JsonElement all = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/profile-fields/enumerate", HttpFixture.AdminToken, new { maxResults = 200, ordering = "ProfileOrderAscending" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    List<string> ids = all.GetProperty("objects").EnumerateArray().Select(o => Api.Str(o, "id")).ToList();
                    ids.Remove(id);
                    ids.Insert(0, id);
                    JsonElement order = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/profile-fields/reorder", HttpFixture.AdminToken, new { order = ids }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(id, order.GetProperty("order")[0].GetString(), "Reordered");
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/profile-fields/reorder", HttpFixture.AdminToken, new { order = ids.Skip(1) }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);

                    await Api.ExpectErrorAsync(f, HttpMethod.Delete, "/api/v1/admin/profile-fields/" + id, HttpFixture.AdminToken, null, HttpStatusCode.Conflict, "FieldHasValues", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Delete, "/api/v1/admin/profile-fields/" + id + "?deleteValues=true", HttpFixture.AdminToken, new { confirm = "wrong" }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/admin/profile-fields/" + id + "?deleteValues=true", HttpFixture.AdminToken, new { confirm = "favoriteColor" }, HttpStatusCode.Accepted, ct).ConfigureAwait(false);
                    DataTable left = await f.Raw.ExecuteQueryAsync("SELECT COUNT(*) AS cnt FROM profilevalues WHERE fieldid = '" + id + "';", false, ct).ConfigureAwait(false);
                    Check.Equal(0L, RowReader.GetLong(left.Rows[0], "cnt"), "Values deleted");
                    JsonElement audit = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/audit?targetId=" + id, HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    List<string> actions = audit.GetProperty("objects").EnumerateArray().Select(a => Api.Str(a, "action")).ToList();
                    foreach (string action in new[] { "profileField.create", "profileField.update", "profileField.delete" }) Check.True(actions.Contains(action), "Audited " + action);
                }),

                SuiteCases.Http(Id, "Values.TypesAndVisibility", "Values validate per type; Me carries every Self-visible key; public profiles and cards follow visibility and showOnProfile and showOnCard; staff-only fields are invisible to members and settable only by staff", async (f, ct) =>
                {
                    await CreateAsync(f, new { key = "years", label = "Years", type = "Number", validation = new { minValue = 0, maxValue = 80, integerOnly = true }, filterable = true, showOnCard = true }, ct).ConfigureAwait(false);
                    await CreateAsync(f, new { key = "website", label = "Website", type = "Url" }, ct).ConfigureAwait(false);
                    await CreateAsync(f, new { key = "phone", label = "Phone", type = "Phone", visibility = "Private" }, ct).ConfigureAwait(false);
                    await CreateAsync(f, new { key = "startedOn", label = "Started", type = "Date", validation = new { notInFuture = true } }, ct).ConfigureAwait(false);
                    await CreateAsync(f, new { key = "staffNote", label = "Staff note", type = "Text", collectAt = "AdminOnly", editableBy = "Staff", visibility = "Staff" }, ct).ConfigureAwait(false);
                    await CreateAsync(f, new { key = "badge", label = "Badge", type = "Text", collectAt = "AdminOnly", editableBy = "Staff", visibility = "Members" }, ct).ConfigureAwait(false);
                    TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    TestMember other = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    // The member directory lists only finished profiles (handle set).
                    await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/me/profile", member.AccessToken, new { handle = "card" + Guid.NewGuid().ToString("N").Substring(0, 8) }, HttpStatusCode.OK, ct).ConfigureAwait(false);

                    ApiResult bad = await Api.SendAsync(f, HttpMethod.Put, "/api/v1/me/profile", member.AccessToken, new
                    {
                        profile = new { years = 2.5, website = "ftp://x", phone = "555", startedOn = DateTime.UtcNow.AddDays(5).ToString("yyyy-MM-dd"), country = "ZZ", rank = new { key = "Nope" }, staffNote = "x", badge = "x", unknownField = 1 }
                    }, ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.BadRequest, bad.Status, "Invalid values");
                    JsonElement errors = bad.Body.GetProperty("fieldErrors");
                    foreach (string key in new[] { "years", "website", "phone", "startedOn", "country", "rank" }) Check.True(errors.TryGetProperty("profile." + key, out JsonElement _), "fieldErrors.profile." + key);
                    Check.Equal("Unknown field.", Api.Str(errors, "profile.staffNote"), "Staff fields are unknown to members");
                    Check.Equal("Unknown field.", Api.Str(errors, "profile.unknownField"), "Unknown key");
                    Check.Equal("This field can only be changed by staff.", Api.Str(errors, "profile.badge"), "editableBy Staff");

                    JsonElement me = await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/me/profile", member.AccessToken, new
                    {
                        profile = new { years = 7, website = "https://example.test/me", phone = "+15551234567", startedOn = "2020-01-02", country = "ca", rank = new { key = "Advanced", level = 1 }, goals = new { keys = new[] { "Learning" } } }
                    }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    JsonElement profile = me.GetProperty("profile");
                    Check.Equal(7.0, profile.GetProperty("years").GetDouble(), "Number");
                    Check.Equal("CA", Api.Str(profile, "country"), "Country upper-cased");
                    Check.Equal("Advanced", Api.Str(profile.GetProperty("rank"), "key"), "Rank");
                    Check.True(profile.TryGetProperty("affiliation", out JsonElement aff) && aff.ValueKind == JsonValueKind.Null, "Unset keys are present with null in Me");
                    Check.True(!profile.TryGetProperty("staffNote", out JsonElement _), "Me never carries Staff fields");
                    Check.Equal("Advanced", Api.Str(me, "rank"), "Legacy rank mirrors profile.rank");
                    Check.Equal(1, me.GetProperty("rankLevel").GetInt32(), "Legacy rankLevel mirrors profile.rank.level");
                    Check.Equal("CA", Api.Str(me, "country"), "Legacy country mirrors profile.country");

                    JsonElement staffSet = await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/members/" + member.Id + "/profile", HttpFixture.AdminToken, new { profile = new { staffNote = "Trusted", badge = "Helper" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("Trusted", Api.Str(staffSet.GetProperty("profile"), "staffNote"), "Admin detail carries Staff fields");

                    JsonElement seen = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/users/" + member.Id, other.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    JsonElement visible = seen.GetProperty("profile");
                    Check.True(visible.TryGetProperty("years", out JsonElement _) && visible.TryGetProperty("badge", out JsonElement _), "Public and Members fields for a member viewer");
                    Check.True(!visible.TryGetProperty("phone", out JsonElement _) && !visible.TryGetProperty("goals", out JsonElement _) && !visible.TryGetProperty("staffNote", out JsonElement _), "Private and Staff fields hidden from other members");
                    Check.True(!visible.TryGetProperty("affiliation", out JsonElement _), "Unset fields omitted on public profiles");
                    JsonElement self = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/users/" + member.Id, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(self.GetProperty("profile").TryGetProperty("phone", out JsonElement _), "Private visible to Self");

                    JsonElement directory = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/members/directory/enumerate", other.AccessToken, new { maxResults = 100 }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    JsonElement card = directory.GetProperty("objects").EnumerateArray().First(o => Api.Str(o, "id") == member.Id).GetProperty("profile");
                    Check.True(card.TryGetProperty("rank", out JsonElement _) && card.TryGetProperty("years", out JsonElement _), "Card fields");
                    Check.True(!card.TryGetProperty("website", out JsonElement _), "showOnCard false is not on cards");

                    JsonElement cleared = await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/me/profile", member.AccessToken, new { profile = new { years = (int?)null } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(cleared.GetProperty("profile").GetProperty("years").ValueKind == JsonValueKind.Null, "null clears");
                    ApiResult required = await Api.SendAsync(f, HttpMethod.Put, "/api/v1/me/profile", member.AccessToken, new { profile = new { rank = (object?)null } }, ct).ConfigureAwait(false);
                    Check.Equal("This field is required.", Api.Str(required.Body.GetProperty("fieldErrors"), "profile.rank"), "A required field cannot be cleared");
                    JsonElement audit = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/audit?targetId=" + member.Id, HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    List<string> actions = audit.GetProperty("objects").EnumerateArray().Select(a => Api.Str(a, "action")).ToList();
                    Check.True(actions.Contains("profile.update") && actions.Contains("member.profile"), "Member and staff profile edits are audited (" + String.Join(",", actions) + ")");
                    Check.True(audit.GetProperty("objects").EnumerateArray().All(a => !(Api.Str(a, "summary") ?? String.Empty).Contains("Trusted", StringComparison.Ordinal)), "Audit records key names, never values");
                }),

                SuiteCases.Http(Id, "Values.HistoryAndCompleteness", "History entries validate sub-fields (RankValue, Member, Date), keep their ids, mirror to rankHistory; a new required SignUp field turns profileComplete false with profileMissing", async (f, ct) =>
                {
                    TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    TestMember coach = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    ApiResult bad = await Api.SendAsync(f, HttpMethod.Put, "/api/v1/me/profile", member.AccessToken, new
                    {
                        profile = new { rankHistory = new object[] { new { values = new { rank = new { key = "Nope" }, awardedBy = new { name = "X", userId = "usr_missing" }, dateAwarded = "2999-01-01" } } } }
                    }, ct).ConfigureAwait(false);
                    JsonElement errors = bad.Body.GetProperty("fieldErrors");
                    foreach (string key in new[] { "rank", "awardedBy", "dateAwarded" }) Check.True(errors.TryGetProperty("profile.rankHistory[0]." + key, out JsonElement _), "fieldErrors.profile.rankHistory[0]." + key);
                    JsonElement me = await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/me/profile", member.AccessToken, new
                    {
                        handle = "history." + Guid.NewGuid().ToString("N").Substring(0, 6),
                        profile = new
                        {
                            rank = new { key = "Beginner", level = 0 },
                            rankHistory = new object[] { new { values = new { rank = new { key = "Beginner" }, awardedBy = new { name = "Coach", userId = coach.Id }, affiliation = "Club", dateAwarded = "2015-06-01" } } }
                        }
                    }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    JsonElement entry = me.GetProperty("profile").GetProperty("rankHistory")[0];
                    string entryId = Api.Str(entry, "id");
                    Check.True(entryId.StartsWith("phe_", StringComparison.Ordinal), "phe_ entry id");
                    Check.Equal(coach.Id, Api.Str(entry.GetProperty("values").GetProperty("awardedBy"), "userId"), "Member sub-field");
                    Check.Equal(1, me.GetProperty("rankHistory").GetArrayLength(), "Mirrored to the legacy rankHistory");
                    Check.Equal("Coach", Api.Str(me.GetProperty("rankHistory")[0], "awardedByName"), "Legacy awardedByName");
                    Check.True(me.GetProperty("profileComplete").GetBoolean(), "Complete");
                    JsonElement kept = await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/me/profile", member.AccessToken, new
                    {
                        profile = new { rankHistory = new object[] { new { id = entryId, values = new { rank = new { key = "Intermediate" } } } } }
                    }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(entryId, Api.Str(kept.GetProperty("profile").GetProperty("rankHistory")[0], "id"), "Entry id kept");

                    await CreateAsync(f, new { key = "pronouns", label = "Pronouns", type = "Text", collectAt = "SignUp", validation = new { required = true } }, ct).ConfigureAwait(false);
                    await Task.Delay(50, ct).ConfigureAwait(false);
                    JsonElement now = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/auth/me", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(!now.GetProperty("profileComplete").GetBoolean(), "Incomplete after a new required SignUp field");
                    Check.Equal("pronouns", now.GetProperty("profileMissing")[0].GetString(), "profileMissing names it");
                }),

                SuiteCases.Http(Id, "Directory.Filters", "Member directory filters: in, minRank, hasValue, any, prefix, the GET form, hidden or invalid filters 400 Unknown filter., and the admin legacy rank filter", async (f, ct) =>
                {
                    TestMember viewer = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    TestMember a = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    TestMember b = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    foreach (TestMember finished in new[] { a, b })
                        await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/me/profile", finished.AccessToken, new { handle = "dir" + Guid.NewGuid().ToString("N").Substring(0, 8) }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    string club = "Filterclub" + Guid.NewGuid().ToString("N").Substring(0, 6);
                    await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/me/profile", a.AccessToken, new { profile = new { rank = new { key = "Advanced", level = 2 }, country = "US", affiliation = club + " North", goals = new { keys = new[] { "Teaching" } } } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/me/profile", b.AccessToken, new { profile = new { rank = new { key = "Beginner", level = 0 }, country = "CA" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    async Task<List<string>> Ids(object filter)
                    {
                        JsonElement page = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/members/directory/enumerate", viewer.AccessToken, new { maxResults = 100, filters = new { profile = filter } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                        return page.GetProperty("objects").EnumerateArray().Select(o => Api.Str(o, "id")).ToList();
                    }
                    List<string> us = await Ids(new { country = new { @in = new[] { "US" } } }).ConfigureAwait(false);
                    Check.True(us.Contains(a.Id) && !us.Contains(b.Id), "country in");
                    List<string> usEq = await Ids(new { country = new { eq = "US" } }).ConfigureAwait(false);
                    Check.True(usEq.Contains(a.Id) && !usEq.Contains(b.Id), "country eq is a one-value in");
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/profile-fields", HttpFixture.AdminToken, new
                    {
                        key = "stance", label = "Stance", type = "SingleSelect", options = new object[] { new { key = "Left", label = "Left" }, new { key = "Right", label = "Right" } }, collectAt = "Profile", filterable = true
                    }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/me/profile", a.AccessToken, new { profile = new { stance = new { key = "Left" } } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/me/profile", b.AccessToken, new { profile = new { stance = new { key = "Right" } } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    List<string> left = await Ids(new { stance = new { eq = "left" } }).ConfigureAwait(false);
                    Check.True(left.Contains(a.Id) && !left.Contains(b.Id), "SingleSelect eq matches the option key");
                    ApiResult badEq = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/members/directory/enumerate", viewer.AccessToken, new { filters = new { profile = new { stance = new { eq = "Up" } } } }, ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.BadRequest, badEq.Status, "SingleSelect eq with an unknown key");
                    List<string> advanced = await Ids(new { rank = new { minRank = "Intermediate", minLevel = 1 } }).ConfigureAwait(false);
                    Check.True(advanced.Contains(a.Id) && !advanced.Contains(b.Id), "minRank and minLevel");
                    List<string> prefix = await Ids(new { affiliation = new { prefix = club.ToLowerInvariant() } }).ConfigureAwait(false);
                    Check.True(prefix.SequenceEqual(new[] { a.Id }), "prefix");
                    List<string> none = await Ids(new { affiliation = new { hasValue = false }, country = new { @in = new[] { "CA" } } }).ConfigureAwait(false);
                    Check.True(none.Contains(b.Id) && !none.Contains(a.Id), "hasValue false");
                    ApiResult hidden = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/members/directory/enumerate", viewer.AccessToken, new { filters = new { profile = new { goals = new { any = new[] { "Teaching" } }, rank = new { prefix = "x" } } } }, ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.BadRequest, hidden.Status, "Hidden and invalid filters");
                    Check.Equal("Unknown filter.", Api.Str(hidden.Body.GetProperty("fieldErrors"), "filters.profile.goals"), "A Private field is not filterable by others");
                    Check.Equal("Unknown filter.", Api.Str(hidden.Body.GetProperty("fieldErrors"), "filters.profile.rank"), "An operator the type does not allow");
                    JsonElement get = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/members/directory?profile.country.in=US&profile.country.in=CA&search=" + Uri.EscapeDataString(club), viewer.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(get.GetProperty("objects").EnumerateArray().Select(o => Api.Str(o, "id")).SequenceEqual(new[] { a.Id }), "GET form plus search over a searchable field");
                    JsonElement staffAny = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/members/enumerate", HttpFixture.AdminToken, new { maxResults = 100, filters = new { profile = new { goals = new { any = new[] { "Teaching" } } } } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(staffAny.GetProperty("objects").EnumerateArray().Any(o => Api.Str(o, "id") == a.Id), "Staff filter on Private fields");
                    JsonElement legacy = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/members?rank=Beginner&maxResults=100", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    List<string> beginners = legacy.GetProperty("objects").EnumerateArray().Select(o => Api.Str(o, "id")).ToList();
                    Check.True(beginners.Contains(b.Id) && !beginners.Contains(a.Id), "Legacy rank= maps to filters.profile.rank.in");
                }),

                SuiteCases.Http(Id, "Legacy.Modes", "Sending a legacy field with its profile key is 400; ReadOnly rejects legacy writes and keeps reads; Off stops serving them", async (f, ct) =>
                {
                    TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    ApiResult both = await Api.SendAsync(f, HttpMethod.Put, "/api/v1/me/profile", member.AccessToken, new { rank = "Beginner", profile = new { rank = new { key = "Beginner" } } }, ct).ConfigureAwait(false);
                    Check.Equal("Send either rank or profile.rank, not both.", Api.Str(both.Body.GetProperty("fieldErrors"), "rank"), "Both forms");
                    await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/me/profile", member.AccessToken, new { rank = "Advanced", rankLevel = 1, city = "Springfield" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    f.Settings.ProfileFields.LegacyFieldsMode = LegacyFieldsModeEnum.ReadOnly;
                    ApiResult rejected = await Api.SendAsync(f, HttpMethod.Put, "/api/v1/me/profile", member.AccessToken, new { city = "Elsewhere" }, ct).ConfigureAwait(false);
                    Check.Equal("This field moved to profile.city.", Api.Str(rejected.Body.GetProperty("fieldErrors"), "city"), "ReadOnly rejects legacy writes");
                    JsonElement read = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/auth/me", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("Springfield", Api.Str(read, "city"), "ReadOnly keeps legacy reads");
                    Check.Equal("Springfield", Api.Str(read.GetProperty("profile"), "city"), "Legacy write landed in profile");
                    f.Settings.ProfileFields.LegacyFieldsMode = LegacyFieldsModeEnum.Off;
                    JsonElement off = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/auth/me", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(off.GetProperty("rank").ValueKind == JsonValueKind.Null && off.GetProperty("city").ValueKind == JsonValueKind.Null, "Off: legacy fields not served");
                    JsonElement property = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/property", null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(property.GetProperty("profile").ValueKind == JsonValueKind.Null, "Off: no legacy profile section");
                }),

                SuiteCases.Http(Id, "Seed.BackfillFromLegacyColumns", "The value backfill copies rank, level, affiliation, location, goals with free text, and rank history from the legacy columns, skips members that already have values, and recomputes valueCount", async (f, ct) =>
                {
                    TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    string pid = f.Services.PropertyId;
                    await f.Raw.ExecuteQueriesAsync(new List<string>
                    {
                        "UPDATE users SET rank = 'Intermediate', ranklevel = 2, affiliation = 'Old Club', city = 'Townsville', country = 'gb', goalsother = 'Mentoring' WHERE propertyid = '" + pid + "' AND id = '" + member.Id + "';",
                        "INSERT INTO usergoals (propertyid, userid, goal, position) VALUES ('" + pid + "', '" + member.Id + "', 'Other', 0);",
                        "INSERT INTO rankhistoryentries (propertyid, id, userid, position, rank, awardedbyname, affiliation, dateawarded, createdutc) VALUES ('" + pid + "', 'lin_backfilltest" + member.Id.Substring(4, 6) + "', '" + member.Id + "', 0, 'Beginner', 'Coach Z', 'Old Club', '2010-01-01', now());"
                    }, true, ct).ConfigureAwait(false);
                    List<ProfileField> fields = await f.Database.ProfileFields.ReadAllAsync(ct).ConfigureAwait(false);
                    ProfileLegacyFieldIds ids = ProfileFieldService.LegacyIds(fields);
                    Check.True(ids.Rank != null && ids.Goals != null && ids.RankHistory != null && ids.GoalsFreeTextKey == "Other", "Seeded fields map the legacy columns");
                    await f.Database.ProfileValues.BackfillLegacyAsync(ids, DateTime.UtcNow, ct).ConfigureAwait(false);
                    await f.Database.ProfileValues.BackfillLegacyAsync(ids, DateTime.UtcNow, ct).ConfigureAwait(false);
                    ProfileValueSet set = await f.Database.ProfileValues.ReadForUserAsync(member.Id, ct).ConfigureAwait(false);
                    Check.Equal("Intermediate", set.Values[ids.Rank!].ValueText, "Rank");
                    Check.Equal(2, set.Values[ids.Rank!].ValueLevel, "Level");
                    Check.Equal("old club", set.Values[ids.Affiliation!].ValueNormalized, "Normalized affiliation");
                    Check.Equal("GB", set.Values[ids.Country!].ValueText, "Country upper-cased");
                    Check.Equal("Mentoring", set.Values[ids.Goals!].OtherText, "goalsOther with the free text option");
                    Check.True(set.Values[ids.Goals!].OptionKeys.SequenceEqual(new[] { "Other" }), "Goal keys");
                    Check.True(set.History[ids.RankHistory!].Single().Id.StartsWith("lin_", StringComparison.Ordinal), "History keeps lin_ ids");
                    Check.Equal("Coach Z", set.History[ids.RankHistory!].Single().Values.Single(v => v.SubFieldKey == "awardedBy").ValueText, "awardedBy");
                    ProfileField rank = (await f.Database.ProfileFields.ReadAsync(ids.Rank!, ct).ConfigureAwait(false))!;
                    Check.True(rank.ValueCount >= 1, "valueCount recomputed");
                })
            });
        }

        #endregion

        #region Private-Methods

        private static async Task CreateAsync(HttpFixture f, object body, CancellationToken ct)
        {
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/profile-fields", HttpFixture.AdminToken, body, HttpStatusCode.Created, ct).ConfigureAwait(false);
        }

        #endregion
    }
}

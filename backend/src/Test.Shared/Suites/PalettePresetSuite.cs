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
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;

    /// <summary>
    /// Engine palette presets (docs/TRELLISKIT.md section 2.1): the catalog (every preset passes every WCAG 2.1 AA pair
    /// in both schemes with no override), applying a preset through the palette versions, the administrator's choice of
    /// presets members may pick (audited, served as GET /api/v1/property brand.presets with a changed ETag), and each
    /// member's palette preference (GET and PUT /api/v1/me/appearance) falling back to the brand palette when the preset
    /// is no longer allowed.
    /// </summary>
    public static class PalettePresetSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "PalettePresets";

        #endregion

        #region Private-Members

        private const string _Branding = "/api/v1/admin/property/branding";
        private const string _MemberPresets = "/api/v1/admin/property/branding/member-presets";
        private const string _Appearance = "/api/v1/me/appearance";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Palette presets (" + TestEnvironment.Provider + ")", new[]
            {
                SuiteCases.Unit(Id, "Catalog.EveryPresetPassesAa", "The catalog has at least 12 presets with unique ids and names, every key in both schemes, and every preset passes every WCAG 2.1 AA pair in both schemes without an override", ct =>
                {
                    IReadOnlyList<BrandPreset> all = BrandPresetCatalog.All;
                    Check.True(all.Count >= 12 && all.Count <= BrandPresetCatalog.MaxPresets, "12 to " + BrandPresetCatalog.MaxPresets + " presets (" + all.Count + ")");
                    Check.Equal(all.Count, all.Select(p => p.Id).Distinct(StringComparer.Ordinal).Count(), "Unique ids");
                    Check.Equal(all.Count, all.Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count(), "Unique names");
                    foreach (BrandPreset preset in all)
                    {
                        foreach (string key in BrandColorSet.Keys)
                        {
                            Check.True(BrandContrast.NormalizeHex(preset.Light.Get(key)) != null && BrandContrast.NormalizeHex(preset.Dark.Get(key)) != null, preset.Id + " has " + key + " in both schemes");
                        }
                        Check.True(preset.Description.Length > 0, preset.Id + " has a description");
                        List<BrandContrastCheck> failing = BrandContrast.Check(preset.Light, preset.Dark, null).Where(c => !c.Passes).ToList();
                        Check.True(failing.Count == 0, preset.Id + " passes WCAG 2.1 AA: " + String.Join("; ", failing.Select(BrandContrast.Describe)));
                    }
                    Check.True(BrandPresetCatalog.Find("ocean") != null && BrandPresetCatalog.Find("Ocean") == null && BrandPresetCatalog.Find(null) == null, "Find is exact");
                    foreach (string bad in new[] { "{\"version\":2,\"presets\":[]}", "{\"version\":1,\"presets\":[]}", "{\"version\":1,\"presets\":[{\"id\":\"Bad Id\",\"name\":\"x\"}]}", "{\"version\":1,\"presets\":[{\"id\":\"a\",\"name\":\"A\",\"light\":{},\"dark\":{}}]}" })
                    {
                        bool threw = false;
                        try
                        {
                            BrandPresetCatalog.Parse(bad);
                        }
                        catch (InvalidOperationException)
                        {
                            threw = true;
                        }
                        Check.True(threw, "Rejected: " + bad);
                    }
                    return Task.CompletedTask;
                }),
                SuiteCases.Http(Id, "Admin.ApplyPresetIsAVersion", "The Branding view carries the catalog and the member choices; applying a preset is an ordinary edit (a new version with no override) that revert undoes", async (f, ct) =>
                {
                    JsonElement view = await Api.ExpectAsync(f, HttpMethod.Get, _Branding, HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(BrandPresetCatalog.All.Count, view.GetProperty("presets").GetArrayLength(), "Catalog in the view");
                    Check.Equal("All", Api.Str(view.GetProperty("memberPresets"), "mode"), "Every preset allowed by default");
                    Check.Equal(BrandPresetCatalog.All.Count, view.GetProperty("memberPresets").GetProperty("allowedPresetIds").GetArrayLength(), "Allowed ids");
                    JsonElement ocean = view.GetProperty("presets").EnumerateArray().First(p => Api.Str(p, "id") == "ocean");
                    string seedPrimary = Api.Str(view.GetProperty("light"), "primary");
                    JsonElement saved = await Api.ExpectAsync(f, HttpMethod.Put, _Branding, HttpFixture.AdminToken, new { light = Colors(ocean.GetProperty("light")), dark = Colors(ocean.GetProperty("dark")), note = "Preset: Ocean" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(2, saved.GetProperty("version").GetInt32(), "A new version");
                    Check.Equal(0, saved.GetProperty("overrides").GetArrayLength(), "No override needed");
                    Check.True(saved.GetProperty("contrast").EnumerateArray().All(c => c.GetProperty("passes").GetBoolean()), "Every pair passes");
                    Check.Equal(Api.Str(ocean.GetProperty("dark"), "accentSoft"), Api.Str(saved.GetProperty("dark"), "accentSoft"), "Dark scheme applied");
                    JsonElement undone = await Api.ExpectAsync(f, HttpMethod.Post, _Branding + "/revert", HttpFixture.AdminToken, new { to = "Previous" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(seedPrimary, Api.Str(undone.GetProperty("light"), "primary"), "Revert restores the palette before the preset");
                }),
                SuiteCases.Http(Id, "MemberPresets.ChooseAndServe", "Administrators choose All, None, or Selected presets; GET /api/v1/property brand.presets follows at once in catalog order with a changed ETag; changes are audited, unchanged bodies are not, invalid bodies are 400 with field errors, and members are refused", async (f, ct) =>
                {
                    JsonElement property = await PropertyAsync(f, ct).ConfigureAwait(false);
                    Check.Equal(BrandPresetCatalog.All.Count, property.GetProperty("brand").GetProperty("presets").GetArrayLength(), "Every preset by default");
                    JsonElement first = property.GetProperty("brand").GetProperty("presets")[0];
                    Check.True(Api.Str(first, "name").Length > 0 && Api.Str(first.GetProperty("light"), "onAccentSoft").Length == 7 && Api.Str(first.GetProperty("dark"), "primary").Length == 7, "Presets carry names and every color");
                    string before = await EtagAsync(f, ct).ConfigureAwait(false);
                    JsonElement selected = await Api.ExpectAsync(f, HttpMethod.Put, _MemberPresets, HttpFixture.AdminToken, new { mode = "selected", presetIds = new[] { "ocean", "graphite", "ocean" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("Selected", Api.Str(selected.GetProperty("memberPresets"), "mode"), "Mode Selected");
                    Check.Equal(1, selected.GetProperty("memberPresets").GetProperty("revision").GetInt32(), "Revision 1");
                    property = await PropertyAsync(f, ct).ConfigureAwait(false);
                    Check.Equal("graphite,ocean", String.Join(",", property.GetProperty("brand").GetProperty("presets").EnumerateArray().Select(p => Api.Str(p, "id"))), "Catalog order, duplicates removed");
                    string after = await EtagAsync(f, ct).ConfigureAwait(false);
                    Check.True(after != before, "ETag changed");
                    await Api.ExpectAsync(f, HttpMethod.Put, _MemberPresets, HttpFixture.AdminToken, new { mode = "Selected", presetIds = new[] { "graphite", "ocean" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(1L, await CountAuditAsync(f, ct).ConfigureAwait(false), "An unchanged choice is not audited again");
                    Check.Equal(after, await EtagAsync(f, ct).ConfigureAwait(false), "ETag unchanged");
                    await Api.ExpectAsync(f, HttpMethod.Put, _MemberPresets, HttpFixture.AdminToken, new { mode = "None" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(0, (await PropertyAsync(f, ct).ConfigureAwait(false)).GetProperty("brand").GetProperty("presets").GetArrayLength(), "None: no presets");
                    Check.Equal(2L, await CountAuditAsync(f, ct).ConfigureAwait(false), "Audited");
                    Dictionary<string, string> mode = await FieldErrorsAsync(f, new { mode = "Some" }, ct).ConfigureAwait(false);
                    Check.True(mode.ContainsKey("mode"), "Bad mode");
                    Dictionary<string, string> empty = await FieldErrorsAsync(f, new { mode = "Selected", presetIds = new string[0] }, ct).ConfigureAwait(false);
                    Check.True(empty.ContainsKey("presetIds"), "Selected needs ids");
                    Dictionary<string, string> unknown = await FieldErrorsAsync(f, new { mode = "Selected", presetIds = new[] { "ocean", "neon" } }, ct).ConfigureAwait(false);
                    Check.True(unknown.ContainsKey("presetIds[1]") && !unknown.ContainsKey("presetIds[0]"), "Names the unknown id: " + String.Join(", ", unknown.Keys));
                    TestMember member = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Put, _MemberPresets, member.AccessToken, new { mode = "All" }, HttpStatusCode.Forbidden, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Put, _MemberPresets, null, new { mode = "All" }, HttpStatusCode.Unauthorized, ct).ConfigureAwait(false);
                    Check.True(f.Services.Branding.MemberChoices.Mode == TrellisKit.Core.Enums.MemberPaletteModeEnum.None, "Refused calls change nothing");
                    await f.Services.Branding.EnsureSeededAsync(true, ct).ConfigureAwait(false);
                    Check.True(f.Services.Branding.MemberChoices.Mode == TrellisKit.Core.Enums.MemberPaletteModeEnum.None && f.Services.Branding.MemberChoices.Revision == 2, "Reloaded from the database");
                }),
                SuiteCases.Http(Id, "Appearance.MemberPreference", "A member's palette preference is stored on the account, validated against the allowed presets, falls back to the brand palette when the preset is disallowed (and returns when it is allowed again), and clears with null", async (f, ct) =>
                {
                    TestMember member = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    JsonElement initial = await Api.ExpectAsync(f, HttpMethod.Get, _Appearance, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(initial.GetProperty("palettePreset").ValueKind == JsonValueKind.Null && initial.GetProperty("effectivePreset").ValueKind == JsonValueKind.Null && initial.GetProperty("presetsAvailable").GetBoolean(), "Property palette by default");
                    JsonElement chosen = await Api.ExpectAsync(f, HttpMethod.Put, _Appearance, member.AccessToken, new { palettePreset = "evergreen" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("evergreen", Api.Str(chosen, "effectivePreset"), "Applies");
                    Check.Equal("evergreen", (await f.Database.Users.ReadAsync(member.Id, ct).ConfigureAwait(false))!.PalettePreset, "Stored on the account");
                    Check.Equal("evergreen", Api.Str(await Api.ExpectAsync(f, HttpMethod.Get, _Appearance, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false), "palettePreset"), "Read back");
                    await Api.ExpectErrorAsync(f, HttpMethod.Put, _Appearance, member.AccessToken, new { palettePreset = "neon" }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Put, _MemberPresets, HttpFixture.AdminToken, new { mode = "Selected", presetIds = new[] { "graphite" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    JsonElement fallback = await Api.ExpectAsync(f, HttpMethod.Get, _Appearance, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("evergreen", Api.Str(fallback, "palettePreset"), "The preference is kept");
                    Check.Equal(JsonValueKind.Null, fallback.GetProperty("effectivePreset").ValueKind, "Falls back to the property palette");
                    ApiResult refused = await Api.SendAsync(f, HttpMethod.Put, _Appearance, member.AccessToken, new { palettePreset = "ocean" }, ct).ConfigureAwait(false);
                    Check.True(refused.Status == HttpStatusCode.BadRequest && refused.Body.GetProperty("fieldErrors").TryGetProperty("palettePreset", out JsonElement _), "A disallowed preset is refused: " + refused.Text);
                    await Api.ExpectAsync(f, HttpMethod.Put, _MemberPresets, HttpFixture.AdminToken, new { mode = "All" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("evergreen", Api.Str(await Api.ExpectAsync(f, HttpMethod.Get, _Appearance, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false), "effectivePreset"), "Applies again once allowed");
                    await Api.ExpectAsync(f, HttpMethod.Put, _MemberPresets, HttpFixture.AdminToken, new { mode = "None" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(!(await Api.ExpectAsync(f, HttpMethod.Get, _Appearance, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false)).GetProperty("presetsAvailable").GetBoolean(), "None: no presets available");
                    JsonElement cleared = await Api.ExpectAsync(f, HttpMethod.Put, _Appearance, member.AccessToken, new { palettePreset = (string?)null }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(JsonValueKind.Null, cleared.GetProperty("palettePreset").ValueKind, "Cleared (always allowed)");
                    Check.True((await f.Database.Users.ReadAsync(member.Id, ct).ConfigureAwait(false))!.PalettePreset == null, "Cleared on the account");
                    await Api.ExpectAsync(f, HttpMethod.Get, _Appearance, null, null, HttpStatusCode.Unauthorized, ct).ConfigureAwait(false);
                }),
                SuiteCases.Http(Id, "Platform.MemberPresets", "Platform administrators choose a property's member presets (audited in both audits) without touching another property", MultiPropertyKit.Multi, async (f, ct) =>
                {
                    string platform = await MultiPropertyKit.PlatformLoginAsync(f, ct).ConfigureAwait(false);
                    string second = await MultiPropertyKit.CreateSecondAsync(f, platform, ct).ConfigureAwait(false);
                    JsonElement view = await MultiPropertyKit.PlatformExpectAsync(f, HttpMethod.Put, "/api/v1/platform/properties/" + second + "/branding/member-presets", platform, new { mode = "Selected", presetIds = new[] { "lavender" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("lavender", String.Join(",", view.GetProperty("memberPresets").GetProperty("allowedPresetIds").EnumerateArray().Select(e => e.GetString())), "B allows lavender only");
                    f.PropertyId = second;
                    Check.Equal(1, (await PropertyAsync(f, ct).ConfigureAwait(false)).GetProperty("brand").GetProperty("presets").GetArrayLength(), "B serves one preset");
                    f.PropertyId = TestProperty.SampleId;
                    Check.Equal(BrandPresetCatalog.All.Count, (await PropertyAsync(f, ct).ConfigureAwait(false)).GetProperty("brand").GetProperty("presets").GetArrayLength(), "A is untouched");
                    Check.Equal(1L, await ContentTestKit.ScalarAsync(f, "SELECT COUNT(*) AS cnt FROM auditrecords WHERE propertyid = '" + second + "' AND action = 'property.branding.memberPresets';", ct).ConfigureAwait(false), "B's audit");
                    Check.Equal(1L, await ContentTestKit.ScalarAsync(f, "SELECT COUNT(*) AS cnt FROM auditrecords WHERE propertyid = 'platform' AND action = 'property.branding.memberPresets' AND targetid = '" + second + "';", ct).ConfigureAwait(false), "Platform audit");
                })
            });
        }

        #endregion

        #region Private-Methods

        private static Dictionary<string, string> Colors(JsonElement set)
        {
            Dictionary<string, string> colors = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string key in BrandColorSet.Keys) colors[key] = Api.Str(set, key);
            return colors;
        }

        private static Task<JsonElement> PropertyAsync(HttpFixture f, CancellationToken ct)
        {
            return Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/property", null, null, HttpStatusCode.OK, ct);
        }

        private static async Task<string> EtagAsync(HttpFixture f, CancellationToken ct)
        {
            ApiResult result = await Api.SendAsync(f, HttpMethod.Get, "/api/v1/property", null, null, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.OK, result.Status, "GET /property");
            return result.Headers.TryGetValue("ETag", out string? etag) ? etag : String.Empty;
        }

        private static async Task<Dictionary<string, string>> FieldErrorsAsync(HttpFixture f, object body, CancellationToken ct)
        {
            ApiResult result = await Api.SendAsync(f, HttpMethod.Put, _MemberPresets, HttpFixture.AdminToken, body, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.BadRequest, result.Status, "400 (body: " + result.Text + ")");
            Check.Equal("ValidationFailed", result.Error, "ValidationFailed");
            Dictionary<string, string> errors = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (JsonProperty p in result.Body.GetProperty("fieldErrors").EnumerateObject()) errors[p.Name] = p.Value.ToString();
            return errors;
        }

        private static Task<long> CountAuditAsync(HttpFixture f, CancellationToken ct)
        {
            return ContentTestKit.ScalarAsync(f, "SELECT COUNT(*) AS cnt FROM auditrecords WHERE propertyid = '" + f.Services.Property.Property.Id + "' AND action = 'property.branding.memberPresets';", ct);
        }

        #endregion
    }
}

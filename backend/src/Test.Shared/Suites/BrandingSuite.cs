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
    using Test.Shared.Fakes;
    using Touchstone.Core;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;

    /// <summary>
    /// Brand palettes, the property AI switch, and assistant embed origins (docs/TRELLISKIT.md sections 2.1 and 9.5,
    /// docs/AI_CONTRACT.md section 13.3): WCAG contrast math; seeding from property.json; edits with hex and contrast
    /// validation (field errors naming the pair and the ratio); explicit overrides recorded as audit warnings; versions
    /// with revert to the previous version and to the seed; the folder winning on change; GET /api/v1/property and its
    /// ETag; isolation between two properties and the platform routes; the AI switch taking effect without a restart;
    /// and the https-or-local-http embed origin rule.
    /// </summary>
    public static class BrandingSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "Branding";

        #endregion

        #region Private-Members

        private const string _Branding = "/api/v1/admin/property/branding";
        private const string _Ai = "/api/v1/admin/property/ai";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Branding, AI switch, and embed origins (" + TestEnvironment.Provider + ")", new[]
            {
                SuiteCases.Unit(Id, "Contrast.Math", "WCAG 2.1 ratios, display rounding (never rounds a failure up), hex normalization, and the sample property passing every pair", ct =>
                {
                    Check.True(Math.Abs(BrandContrast.Ratio("#FFFFFF", "#000000") - 21.0) < 0.0001, "White on black is 21:1");
                    Check.True(Math.Abs(BrandContrast.Ratio("#777777", "#777777") - 1.0) < 0.0001, "Same color is 1:1");
                    Check.Equal(4.29, BrandContrast.Display(BrandContrast.Ratio("#CF3335", "#FBE9E9")), "Red on the light red accent");
                    Check.Equal(5.31, BrandContrast.Display(BrandContrast.Ratio("#B8282C", "#FBE9E9")), "Darker red on the light red accent");
                    Check.Equal(4.49, BrandContrast.Display(4.4999), "Never rounds up to a threshold");
                    Check.Equal(2.99, BrandContrast.Display(2.996), "Never rounds up to 3");
                    Check.Equal(4.51, BrandContrast.Display(4.506), "Rounds normally otherwise");
                    Check.Equal("#AABBCC", BrandContrast.NormalizeHex(" #abc "), "Expands #RGB");
                    Check.Equal("#1F5FBF", BrandContrast.NormalizeHex("#1f5fbf"), "Uppercases");
                    Check.True(BrandContrast.NormalizeHex("#12345678") == null && BrandContrast.NormalizeHex("red") == null && BrandContrast.NormalizeHex("#12345") == null, "Rejects alpha, names, and odd lengths");
                    Check.Equal("#112233", BrandContrast.NormalizeDocumentColor("#112233FF"), "Document colors drop alpha");
                    BrandPalette sample = BrandPalette.FromDocument(TestProperty.LoadSample().Brand.Colors);
                    List<BrandContrastCheck> checks = BrandContrast.Check(sample.Light, sample.Dark, null);
                    Check.Equal(BrandContrast.Pairs.Count * 2, checks.Count, "Every pair in both schemes");
                    Check.True(checks.All(c => c.Passes), "The sample passes WCAG 2.1 AA: " + String.Join("; ", checks.Where(c => !c.Passes).Select(BrandContrast.Describe)));
                    Check.Equal("#123456", BrandColorSet.FromScheme(new TrellisKit.Core.PropertyConfig.PropertyColorScheme { Primary = "#123456" }).OnAccentSoft, "onAccentSoft defaults to primary");
                    return Task.CompletedTask;
                }),
                SuiteCases.Unit(Id, "EmbedOrigins.Rule", "https origins are accepted; plain http only for localhost and 127.0.0.1 on any port; paths, wildcards, and other hosts over http are rejected", ct =>
                {
                    Check.Equal("https://partner.example.test", EmbedOrigins.Normalize("https://Partner.Example.test/"), "https, normalized");
                    Check.Equal("https://partner.example.test:8443", EmbedOrigins.Normalize("https://partner.example.test:8443"), "https with a port");
                    Check.Equal("http://localhost:5500", EmbedOrigins.Normalize("http://localhost:5500"), "http localhost with a port");
                    Check.Equal("http://127.0.0.1", EmbedOrigins.Normalize("http://127.0.0.1"), "http 127.0.0.1 without a port");
                    Check.Equal("http://localhost", EmbedOrigins.Normalize("HTTP://LOCALHOST/"), "http localhost, normalized");
                    foreach (string bad in new[] { "http://partner.example.test", "http://192.168.1.2:3000", "http://localhost.example.test", "https://partner.example.test/page", "*", "https://*.example.test", "ftp://localhost", "http://localhost:0", "http://localhost:70000", "https://", "http://[::1]:3000" })
                        Check.True(EmbedOrigins.Normalize(bad) == null, "Rejected: " + bad);
                    Check.True(EmbedOrigins.IsLocalDevelopment("http://localhost:3000") && EmbedOrigins.IsLocalDevelopment("https://127.0.0.1:8443") && !EmbedOrigins.IsLocalDevelopment("https://partner.example.test"), "Local development origins");
                    return Task.CompletedTask;
                }),
                SuiteCases.Http(Id, "Seed.FromDocument", "The palette is seeded from property.json (version 1, every key, onAccentSoft included), GET /api/v1/property serves it, and the admin view checks every pair", async (f, ct) =>
                {
                    JsonElement view = await Api.ExpectAsync(f, HttpMethod.Get, _Branding, HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(1, view.GetProperty("version").GetInt32(), "Version 1");
                    Check.Equal("Seed", Api.Str(view, "source"), "Seeded");
                    Check.Equal(f.Property.Brand.Colors.Light.Primary.ToUpperInvariant(), Api.Str(view.GetProperty("light"), "primary"), "Light primary from the document");
                    Check.Equal(f.Property.Brand.Colors.Dark.AccentSoft.ToUpperInvariant(), Api.Str(view.GetProperty("dark"), "accentSoft"), "Dark accentSoft from the document");
                    Check.True(Api.Str(view.GetProperty("light"), "onAccentSoft").Length == 7, "onAccentSoft present");
                    Check.Equal(BrandContrast.Pairs.Count * 2, view.GetProperty("contrast").GetArrayLength(), "Every pair");
                    Check.True(view.GetProperty("contrast").EnumerateArray().All(c => c.GetProperty("passes").GetBoolean()), "The sample passes");
                    Check.Equal(JsonValueKind.Null, view.GetProperty("previous").ValueKind, "No previous version");
                    Check.True(view.GetProperty("syncsFromFolder").GetBoolean(), "SingleProperty follows the folder");
                    JsonElement property = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/property", null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    JsonElement brand = property.GetProperty("brand");
                    Check.Equal(1, brand.GetProperty("paletteVersion").GetInt32(), "Palette version on GET /property");
                    Check.Equal(Api.Str(view.GetProperty("light"), "onAccentSoft"), Api.Str(brand.GetProperty("colors").GetProperty("light"), "onAccentSoft"), "onAccentSoft on GET /property");
                    Check.Equal(1L, await f.Database.BrandPalettes.CountAsync(ct).ConfigureAwait(false), "One stored version");
                }),
                SuiteCases.Http(Id, "Update.AppliesAndChangesEtag", "An edit stores a new version, is audited, applies to GET /api/v1/property at once, and changes its ETag; an unchanged body stores nothing", async (f, ct) =>
                {
                    string before = await EtagAsync(f, ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.NotModified, await ConditionalAsync(f, before, ct).ConfigureAwait(false), "Unchanged: 304");
                    JsonElement saved = await Api.ExpectAsync(f, HttpMethod.Put, _Branding, HttpFixture.AdminToken, new { light = new Dictionary<string, string> { ["primary"] = "#1a4fa0", ["primaryHover"] = "#163f80" }, note = "Darker blue" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(2, saved.GetProperty("version").GetInt32(), "Version 2");
                    Check.Equal("Edit", Api.Str(saved, "source"), "Edit");
                    Check.Equal("#1A4FA0", Api.Str(saved.GetProperty("light"), "primary"), "Normalized");
                    Check.Equal("Darker blue", Api.Str(saved, "note"), "Note");
                    Check.Equal(1, saved.GetProperty("previous").GetProperty("version").GetInt32(), "Previous is version 1");
                    JsonElement property = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/property", null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("#1A4FA0", Api.Str(property.GetProperty("brand").GetProperty("colors").GetProperty("light"), "primary"), "GET /property follows");
                    Check.Equal(2, property.GetProperty("brand").GetProperty("paletteVersion").GetInt32(), "Palette version 2");
                    string after = await EtagAsync(f, ct).ConfigureAwait(false);
                    Check.True(after != before, "ETag changed");
                    Check.Equal(HttpStatusCode.OK, await ConditionalAsync(f, before, ct).ConfigureAwait(false), "The old ETag gets 200");
                    Check.Equal(HttpStatusCode.NotModified, await ConditionalAsync(f, after, ct).ConfigureAwait(false), "The new ETag gets 304");
                    JsonElement same = await Api.ExpectAsync(f, HttpMethod.Put, _Branding, HttpFixture.AdminToken, new { light = new Dictionary<string, string> { ["primary"] = "#1A4FA0" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(2, same.GetProperty("version").GetInt32(), "No new version for an unchanged body");
                    Check.Equal(1L, await CountAuditAsync(f, "property.branding.update", ct).ConfigureAwait(false), "Audited once");
                    string summary = await AuditSummaryAsync(f, "property.branding.update", ct).ConfigureAwait(false);
                    Check.True(summary.Contains("light.primary", StringComparison.Ordinal) && summary.Contains("#1A4FA0", StringComparison.Ordinal), "Audit names the change: " + summary);
                }),
                SuiteCases.Http(Id, "Validation.HexAndContrast", "Invalid hex values, unknown keys, bad overrides, and pairs below WCAG 2.1 AA are 400 ValidationFailed with field errors naming the key or the pair and its ratio; nothing is stored", async (f, ct) =>
                {
                    Dictionary<string, string> hex = await FieldErrorsAsync(f, new { light = new Dictionary<string, string> { ["primary"] = "red", ["bogus"] = "#000000" }, dark = new Dictionary<string, string> { ["accent"] = "#12345678" } }, ct).ConfigureAwait(false);
                    Check.True(hex.ContainsKey("light.primary") && hex.ContainsKey("light.bogus") && hex.ContainsKey("dark.accent"), "Hex and key errors: " + String.Join(", ", hex.Keys));
                    Dictionary<string, string> contrast = await FieldErrorsAsync(f, new { light = new Dictionary<string, string> { ["onAccentSoft"] = "#F7C9C9", ["accentSoft"] = "#FBE9E9" }, dark = new Dictionary<string, string> { ["onPrimary"] = "#8DB4F0" } }, ct).ConfigureAwait(false);
                    Check.True(contrast.TryGetValue("contrast.light.onAccentSoft/accentSoft", out string? text), "Light pair named: " + String.Join(", ", contrast.Keys));
                    Check.True(text!.Contains(":1", StringComparison.Ordinal) && text.Contains("4.5", StringComparison.Ordinal) && text.Contains("#F7C9C9", StringComparison.Ordinal), "Message names the colors and the ratio: " + text);
                    Check.True(contrast.ContainsKey("contrast.dark.onPrimary/primary") && contrast.ContainsKey("contrast.dark.onPrimary/primaryHover"), "Dark pairs named");
                    Dictionary<string, string> overrides = await FieldErrorsAsync(f, new { contrastOverrides = new object[] { new { scheme = "sepia", pair = "onPrimary/primary" }, new { scheme = "light", pair = "nope/never" } } }, ct).ConfigureAwait(false);
                    Check.True(overrides.ContainsKey("contrastOverrides[0].scheme") && overrides.ContainsKey("contrastOverrides[1].pair"), "Override errors: " + String.Join(", ", overrides.Keys));
                    Dictionary<string, string> note = await FieldErrorsAsync(f, new { note = new string('n', 201) }, ct).ConfigureAwait(false);
                    Check.True(note.ContainsKey("note"), "Note length");
                    Check.Equal(1L, await f.Database.BrandPalettes.CountAsync(ct).ConfigureAwait(false), "Nothing stored");
                    TestMember member = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Get, _Branding, member.AccessToken, null, HttpStatusCode.Forbidden, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Put, _Branding, null, new { }, HttpStatusCode.Unauthorized, ct).ConfigureAwait(false);
                }),
                SuiteCases.Http(Id, "Override.RecordedAsWarning", "A failing pair is saved only with an explicit override, which is stored with the version, shown as overridden, and audited as a warning", async (f, ct) =>
                {
                    object body = new
                    {
                        light = new Dictionary<string, string> { ["onAccentSoft"] = "#E06A6C" },
                        contrastOverrides = new object[] { new { scheme = "light", pair = "onAccentSoft/accentSoft", reason = "Brand review pending" }, new { scheme = "dark", pair = "onPrimary/primary" } }
                    };
                    JsonElement saved = await Api.ExpectAsync(f, HttpMethod.Put, _Branding, HttpFixture.AdminToken, body, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    JsonElement check = saved.GetProperty("contrast").EnumerateArray().First(c => Api.Str(c, "scheme") == "light" && Api.Str(c, "pair") == "onAccentSoft/accentSoft");
                    Check.True(!check.GetProperty("passes").GetBoolean() && check.GetProperty("overridden").GetBoolean(), "Failing and overridden");
                    Check.Equal(1, saved.GetProperty("overrides").GetArrayLength(), "Only the failing pair is stored (the passing dark pair is ignored)");
                    Check.Equal("Brand review pending", Api.Str(saved.GetProperty("overrides")[0], "reason"), "Reason stored");
                    BrandPalette stored = (await f.Database.BrandPalettes.ReadCurrentAsync(ct).ConfigureAwait(false))!;
                    Check.True(stored.Overrides.Count == 1 && stored.Overrides[0].Ratio < 4.5 && stored.Overrides[0].Required == 4.5, "Override row with its ratio");
                    Check.Equal(1L, await CountAuditAsync(f, "property.branding.contrastOverride", ct).ConfigureAwait(false), "One warning audited");
                    string warning = await AuditSummaryAsync(f, "property.branding.contrastOverride", ct).ConfigureAwait(false);
                    Check.True(warning.StartsWith("Warning:", StringComparison.Ordinal) && warning.Contains("onAccentSoft", StringComparison.Ordinal) && warning.Contains(":1", StringComparison.Ordinal) && warning.Contains("Brand review pending", StringComparison.Ordinal), "Warning text: " + warning);
                    Dictionary<string, string> again = await FieldErrorsAsync(f, new { light = new Dictionary<string, string> { ["onAccentSoft"] = "#E58587" } }, ct).ConfigureAwait(false);
                    Check.True(again.ContainsKey("contrast.light.onAccentSoft/accentSoft"), "An override does not carry over to a new edit without being sent");
                }),
                SuiteCases.Http(Id, "Revert.PreviousAndSeed", "Revert restores the version before the current one or the property.json seed as new versions; with one version, Previous is 409 NoPreviousVersion; old versions are pruned to property.maxPaletteVersions", async (f, ct) =>
                {
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, _Branding + "/revert", HttpFixture.AdminToken, new { to = "Previous" }, HttpStatusCode.Conflict, "NoPreviousVersion", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, _Branding + "/revert", HttpFixture.AdminToken, new { to = "Yesterday" }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    string seedPrimary = f.Services.Branding.Current.Light.Primary;
                    await Api.ExpectAsync(f, HttpMethod.Put, _Branding, HttpFixture.AdminToken, new { light = new Dictionary<string, string> { ["primary"] = "#1A4FA0" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Put, _Branding, HttpFixture.AdminToken, new { light = new Dictionary<string, string> { ["primary"] = "#173F80" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    JsonElement previous = await Api.ExpectAsync(f, HttpMethod.Post, _Branding + "/revert", HttpFixture.AdminToken, new { to = "Previous", note = "undo" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(4, previous.GetProperty("version").GetInt32(), "A revert is a new version");
                    Check.Equal("RevertPrevious", Api.Str(previous, "source"), "Source");
                    Check.Equal("#1A4FA0", Api.Str(previous.GetProperty("light"), "primary"), "Restores version 2");
                    JsonElement seed = await Api.ExpectAsync(f, HttpMethod.Post, _Branding + "/revert", HttpFixture.AdminToken, new { to = "seed" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("RevertSeed", Api.Str(seed, "source"), "Seed source");
                    Check.Equal(seedPrimary, Api.Str(seed.GetProperty("light"), "primary"), "Restores the seed");
                    Check.Equal(Api.Str(seed.GetProperty("seed").GetProperty("light"), "primary"), Api.Str(seed.GetProperty("light"), "primary"), "Seed preset matches");
                    Check.Equal(2L, await CountAuditAsync(f, "property.branding.revert", ct).ConfigureAwait(false), "Reverts audited");
                    f.Settings.Property.MaxPaletteVersions = 2;
                    await Api.ExpectAsync(f, HttpMethod.Put, _Branding, HttpFixture.AdminToken, new { light = new Dictionary<string, string> { ["primary"] = "#1A4FA0" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(2L, await f.Database.BrandPalettes.CountAsync(ct).ConfigureAwait(false), "Pruned to the setting");
                    JsonElement undo = await Api.ExpectAsync(f, HttpMethod.Post, _Branding + "/revert", HttpFixture.AdminToken, new { to = "Previous" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(seedPrimary, Api.Str(undo.GetProperty("light"), "primary"), "The previous version survives pruning");
                }),
                SuiteCases.Http(Id, "Versions.ListAndRestoreAny", "The versions list pages the retained versions newest first with their colors and the current flag; revert to Version restores any retained version as a new version (source RevertVersion); the current version is 409 AlreadyCurrent and a pruned one 404; label filters are refused (versions carry none)", async (f, ct) =>
                {
                    string seedPrimary = f.Services.Branding.Current.Light.Primary;
                    await Api.ExpectAsync(f, HttpMethod.Put, _Branding, HttpFixture.AdminToken, new { light = new Dictionary<string, string> { ["primary"] = "#1A4FA0" }, note = "first" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Put, _Branding, HttpFixture.AdminToken, new { light = new Dictionary<string, string> { ["primary"] = "#173F80" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    JsonElement list = await Api.ExpectAsync(f, HttpMethod.Get, _Branding + "/versions", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    List<JsonElement> versions = list.GetProperty("objects").EnumerateArray().ToList();
                    Check.Equal("3,2,1", String.Join(",", versions.Select(v => v.GetProperty("version").GetInt32())), "Newest first");
                    Check.True(versions[0].GetProperty("isCurrent").GetBoolean() && !versions[1].GetProperty("isCurrent").GetBoolean(), "Current flag");
                    Check.Equal("#1A4FA0", Api.Str(versions[1].GetProperty("light"), "primary"), "Colors of each version");
                    Check.Equal("first", Api.Str(versions[1], "note"), "Note");
                    Check.Equal("Seed", Api.Str(versions[2], "source"), "Seed source");
                    Check.Equal(0, versions[0].GetProperty("labels").GetArrayLength(), "Labels present and empty");
                    JsonElement paged = await Api.ExpectAsync(f, HttpMethod.Post, _Branding + "/versions/enumerate", HttpFixture.AdminToken, new { maxResults = 2 }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(2, paged.GetProperty("objects").GetArrayLength(), "Bounded page");
                    Check.True(!paged.GetProperty("endOfResults").GetBoolean(), "More to read");
                    JsonElement rest = await Api.ExpectAsync(f, HttpMethod.Post, _Branding + "/versions/enumerate", HttpFixture.AdminToken, new { maxResults = 2, continuationToken = Api.Str(paged, "continuationToken") }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(1, rest.GetProperty("objects")[0].GetProperty("version").GetInt32(), "Continuation reaches the oldest");
                    JsonElement ascending = await Api.ExpectAsync(f, HttpMethod.Post, _Branding + "/versions/enumerate", HttpFixture.AdminToken, new { ordering = "VersionAscending", filters = new { source = "Edit" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("2,3", String.Join(",", ascending.GetProperty("objects").EnumerateArray().Select(v => v.GetProperty("version").GetInt32())), "Ascending, filtered by source");
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, _Branding + "/versions/enumerate", HttpFixture.AdminToken, new { labels = new[] { "x" } }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);

                    JsonElement restored = await Api.ExpectAsync(f, HttpMethod.Post, _Branding + "/revert", HttpFixture.AdminToken, new { to = "Version", version = 1, note = "back to the start" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(4, restored.GetProperty("version").GetInt32(), "A restore is a new version");
                    Check.Equal("RevertVersion", Api.Str(restored, "source"), "Source RevertVersion");
                    Check.Equal(seedPrimary, Api.Str(restored.GetProperty("light"), "primary"), "Restores version 1");
                    JsonElement middle = await Api.ExpectAsync(f, HttpMethod.Post, _Branding + "/revert", HttpFixture.AdminToken, new { to = "Version", version = 2 }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("#1A4FA0", Api.Str(middle.GetProperty("light"), "primary"), "Restores version 2, not only the previous one");
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, _Branding + "/revert", HttpFixture.AdminToken, new { to = "Version", version = 5 }, HttpStatusCode.Conflict, "AlreadyCurrent", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, _Branding + "/revert", HttpFixture.AdminToken, new { to = "Version", version = 99 }, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, _Branding + "/revert", HttpFixture.AdminToken, new { to = "Version" }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    Check.Equal(2L, await CountAuditAsync(f, "property.branding.revert", ct).ConfigureAwait(false), "Restores audited");
                }),
                SuiteCases.Http(Id, "Seeding.FolderWinsOnChange", "Admin edits stand while property.json brand.colors is unchanged; a changed folder section replaces the palette on the next load in SingleProperty (audited property.branding.sync); without the folder rule the stored palette stands", async (f, ct) =>
                {
                    await Api.ExpectAsync(f, HttpMethod.Put, _Branding, HttpFixture.AdminToken, new { light = new Dictionary<string, string> { ["primary"] = "#1A4FA0" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await f.Services.Branding.EnsureSeededAsync(true, ct).ConfigureAwait(false);
                    Check.Equal("#1A4FA0", f.Services.Branding.Current.Light.Primary, "An unchanged document keeps the admin edit");
                    f.Property.Brand.Colors.Light.Primary = "#0B3D91";
                    f.Property.Brand.Colors.Light.OnAccentSoft = "#0B3D91";
                    await f.Services.Branding.EnsureSeededAsync(false, ct).ConfigureAwait(false);
                    Check.Equal("#1A4FA0", f.Services.Branding.Current.Light.Primary, "Without the folder rule the stored palette stands");
                    await f.Services.Branding.EnsureSeededAsync(true, ct).ConfigureAwait(false);
                    Check.Equal("#0B3D91", f.Services.Branding.Current.Light.Primary, "The changed folder wins");
                    Check.Equal(BrandPaletteSourceEnum.DirectorySync, f.Services.Branding.Current.Source, "Source DirectorySync");
                    Check.Equal(3, f.Services.Branding.Current.Version, "A new version");
                    await f.Services.Branding.EnsureSeededAsync(true, ct).ConfigureAwait(false);
                    Check.Equal(3, f.Services.Branding.Current.Version, "Synchronized once");
                    JsonElement property = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/property", null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("#0B3D91", Api.Str(property.GetProperty("brand").GetProperty("colors").GetProperty("light"), "primary"), "GET /property follows");
                    Check.Equal(1L, await CountAuditAsync(f, "property.branding.sync", ct).ConfigureAwait(false), "Sync audited");
                }),
                SuiteCases.Http(Id, "Isolation.TwoProperties", "One MultiProperty deployment: each property's palette is its own on GET /api/v1/property and the admin routes, the platform routes edit any property (audited in both audits), and the AI switch is per property", MultiPropertyKit.Multi, async (f, ct) =>
                {
                    string platform = await MultiPropertyKit.PlatformLoginAsync(f, ct).ConfigureAwait(false);
                    string second = await MultiPropertyKit.CreateSecondAsync(f, platform, ct).ConfigureAwait(false);
                    f.PropertyId = TestProperty.SampleId;
                    JsonElement a = await Api.ExpectAsync(f, HttpMethod.Put, _Branding, HttpFixture.AdminToken, new { light = new Dictionary<string, string> { ["primary"] = "#1A4FA0" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(2, a.GetProperty("version").GetInt32(), "A is at version 2");
                    f.PropertyId = second;
                    JsonElement b = await Api.ExpectAsync(f, HttpMethod.Get, _Branding, HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(1, b.GetProperty("version").GetInt32(), "B is untouched");
                    Check.True(Api.Str(b.GetProperty("light"), "primary") != "#1A4FA0" || b.GetProperty("version").GetInt32() == 1, "B keeps its own primary");
                    JsonElement viaPlatform = await MultiPropertyKit.PlatformExpectAsync(f, HttpMethod.Put, "/api/v1/platform/properties/" + second + "/branding", platform, new { dark = new Dictionary<string, string> { ["accentSoft"] = "#22304A" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(2, viaPlatform.GetProperty("version").GetInt32(), "Platform edit stores B's version 2");
                    JsonElement propertyB = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/property", null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("#22304A", Api.Str(propertyB.GetProperty("brand").GetProperty("colors").GetProperty("dark"), "accentSoft"), "B serves its own palette");
                    await MultiPropertyKit.PlatformExpectAsync(f, HttpMethod.Get, "/api/v1/platform/properties/" + second + "/branding", platform, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    JsonElement platformVersions = await MultiPropertyKit.PlatformExpectAsync(f, HttpMethod.Get, "/api/v1/platform/properties/" + second + "/branding/versions", platform, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("2,1", String.Join(",", platformVersions.GetProperty("objects").EnumerateArray().Select(v => v.GetProperty("version").GetInt32())), "Platform lists B's versions only");
                    JsonElement platformRestore = await MultiPropertyKit.PlatformExpectAsync(f, HttpMethod.Post, "/api/v1/platform/properties/" + second + "/branding/revert", platform, new { to = "Version", version = 1 }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("RevertVersion", Api.Str(platformRestore, "source"), "Platform restore of any version");
                    await MultiPropertyKit.PlatformExpectAsync(f, HttpMethod.Put, "/api/v1/platform/properties/" + second + "/ai", platform, new { enabled = true }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True((await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/property", null, null, HttpStatusCode.OK, ct).ConfigureAwait(false)).GetProperty("features").GetProperty("ai").GetBoolean(), "B has AI on");
                    f.PropertyId = TestProperty.SampleId;
                    JsonElement propertyA = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/property", null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("#1A4FA0", Api.Str(propertyA.GetProperty("brand").GetProperty("colors").GetProperty("light"), "primary"), "A serves its own palette");
                    Check.True(!propertyA.GetProperty("features").GetProperty("ai").GetBoolean(), "A keeps AI off");
                    Check.Equal(3L, await ContentTestKit.ScalarAsync(f, "SELECT COUNT(*) AS cnt FROM propertybrandpalettes WHERE propertyid = '" + second + "';", ct).ConfigureAwait(false), "B's versions are its own");
                    Check.Equal(1L, await ContentTestKit.ScalarAsync(f, "SELECT COUNT(*) AS cnt FROM auditrecords WHERE propertyid = '" + second + "' AND action = 'property.branding.update';", ct).ConfigureAwait(false), "B's audit");
                    Check.True(await ContentTestKit.ScalarAsync(f, "SELECT COUNT(*) AS cnt FROM auditrecords WHERE propertyid = 'platform' AND action IN ('property.branding.update', 'property.ai.update') AND targetid = '" + second + "';", ct).ConfigureAwait(false) >= 2, "Platform audit");
                    await MultiPropertyKit.PlatformExpectAsync(f, HttpMethod.Get, "/api/v1/platform/properties/" + second + "/branding", null, null, HttpStatusCode.Unauthorized, ct).ConfigureAwait(false);
                }),
                AiTestKit.Case(Id, "Ai.SwitchWithoutRestart", "The AI switch is seeded from property.json, switched by the admin route (audited), reflected by features.ai and the ETag immediately, gates the AI routes, and follows a changed folder value", AiSwitchAsync, null, p => p.Features.Ai = false),
                AiTestKit.Case(Id, "EmbedOrigins.Validation", "Assistant embed origins accept https and local development http (localhost and 127.0.0.1, any port), reject other http origins with a field error naming the entry, and the summary lists the local origins", EmbedOriginsAsync)
            });
        }

        #endregion

        #region Private-Methods

        private static async Task AiSwitchAsync(HttpFixture f, FakeModelServer model, CancellationToken ct)
        {
            JsonElement view = await Api.ExpectAsync(f, HttpMethod.Get, _Ai, HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(!view.GetProperty("enabled").GetBoolean() && !view.GetProperty("seedEnabled").GetBoolean() && view.GetProperty("deploymentEnabled").GetBoolean(), "Seeded off from the document");
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/admin/ai/endpoints", HttpFixture.AdminToken, null, HttpStatusCode.NotFound, "NotAvailable", ct).ConfigureAwait(false);
            string before = await EtagAsync(f, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, _Ai, HttpFixture.AdminToken, new { }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            JsonElement on = await Api.ExpectAsync(f, HttpMethod.Put, _Ai, HttpFixture.AdminToken, new { enabled = true }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(on.GetProperty("enabled").GetBoolean() && on.GetProperty("available").GetBoolean(), "On and available");
            JsonElement property = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/property", null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(property.GetProperty("features").GetProperty("ai").GetBoolean(), "features.ai follows at once");
            Check.True(await EtagAsync(f, ct).ConfigureAwait(false) != before, "ETag changed");
            await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/ai/endpoints", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(1L, await CountAuditAsync(f, "property.ai.update", ct).ConfigureAwait(false), "Audited");
            await Api.ExpectAsync(f, HttpMethod.Put, _Ai, HttpFixture.AdminToken, new { enabled = true }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(1L, await CountAuditAsync(f, "property.ai.update", ct).ConfigureAwait(false), "No audit for an unchanged switch");
            f.Settings.Ai.Enabled = false;
            JsonElement deploymentOff = await Api.ExpectAsync(f, HttpMethod.Get, _Ai, HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(deploymentOff.GetProperty("enabled").GetBoolean() && !deploymentOff.GetProperty("available").GetBoolean(), "The deployment setting still applies");
            Check.True(!(await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/property", null, null, HttpStatusCode.OK, ct).ConfigureAwait(false)).GetProperty("features").GetProperty("ai").GetBoolean(), "features.ai off with the deployment switch");
            f.Settings.Ai.Enabled = true;
            await f.Services.AiSwitch.EnsureSeededAsync(true, ct).ConfigureAwait(false);
            Check.True(f.Services.AiSwitch.Enabled, "An unchanged document keeps the admin switch");
            f.Property.Features.Ai = true;
            await Api.ExpectAsync(f, HttpMethod.Put, _Ai, HttpFixture.AdminToken, new { enabled = false }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await f.Services.AiSwitch.EnsureSeededAsync(true, ct).ConfigureAwait(false);
            Check.True(f.Services.AiSwitch.Enabled, "A changed folder value wins on the next load");
            Check.Equal(1L, await CountAuditAsync(f, "property.ai.sync", ct).ConfigureAwait(false), "Sync audited");
            TestMember member = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, _Ai, member.AccessToken, new { enabled = false }, HttpStatusCode.Forbidden, ct).ConfigureAwait(false);
        }

        private static async Task EmbedOriginsAsync(HttpFixture f, FakeModelServer model, CancellationToken ct)
        {
            string[] origins = new[] { "https://partner.example.test", "http://localhost:5500", "http://127.0.0.1:8080", "HTTP://LOCALHOST:5500/" };
            string assistant = await AiTestKit.ReadyAsync(f, model, ct, new { audience = "Public", embed = new { enabled = true, allowedOrigins = origins } }).ConfigureAwait(false);
            JsonElement admin = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/ai/assistants/" + assistant, HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            List<string> stored = admin.GetProperty("embed").GetProperty("allowedOrigins").EnumerateArray().Select(e => e.GetString()!).ToList();
            Check.Equal("http://127.0.0.1:8080,http://localhost:5500,https://partner.example.test", String.Join(",", stored.OrderBy(o => o, StringComparer.Ordinal)), "Normalized, duplicates removed");
            ApiResult rejected = await Api.SendAsync(f, HttpMethod.Put, "/api/v1/admin/ai/assistants/" + assistant, HttpFixture.AdminToken, new { embed = new { allowedOrigins = new[] { "https://ok.example.test", "http://partner.example.test", "http://192.168.0.10:3000" } } }, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.BadRequest, rejected.Status, "Plain http on another host is rejected: " + rejected.Text);
            JsonElement fieldErrors = rejected.Body.GetProperty("fieldErrors");
            Check.True(fieldErrors.TryGetProperty("embed.allowedOrigins[1]", out JsonElement first) && first.GetString()!.Contains("http://partner.example.test", StringComparison.Ordinal), "Names the entry");
            Check.True(fieldErrors.TryGetProperty("embed.allowedOrigins[2]", out JsonElement _) && !fieldErrors.TryGetProperty("embed.allowedOrigins[0]", out JsonElement _), "Only the bad entries");
            JsonElement unchanged = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/ai/assistants/" + assistant, HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(3, unchanged.GetProperty("embed").GetProperty("allowedOrigins").GetArrayLength(), "Nothing changed");
            JsonElement removed = await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/ai/assistants/" + assistant, HttpFixture.AdminToken, new { embed = new { allowedOrigins = new[] { "https://partner.example.test" } } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(1, removed.GetProperty("embed").GetProperty("allowedOrigins").GetArrayLength(), "Local origins removed");
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/ai/assistants/" + assistant, HttpFixture.AdminToken, new { embed = new { allowedOrigins = origins } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            JsonElement summary = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/assistants/" + assistant, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            List<string> allowed = summary.GetProperty("embed").GetProperty("allowedOrigins").EnumerateArray().Select(e => e.GetString()!).ToList();
            Check.True(allowed.Contains("http://localhost:5500") && allowed.Contains("http://127.0.0.1:8080"), "The embed page may post to the local origins (" + String.Join(", ", allowed) + ")");
        }

        private static async Task<string> EtagAsync(HttpFixture f, CancellationToken ct)
        {
            ApiResult result = await Api.SendAsync(f, HttpMethod.Get, "/api/v1/property", null, null, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.OK, result.Status, "GET /property");
            return result.Headers.TryGetValue("ETag", out string? etag) ? etag : String.Empty;
        }

        private static async Task<HttpStatusCode> ConditionalAsync(HttpFixture f, string etag, CancellationToken ct)
        {
            ApiResult result = await Api.SendAsync(f, HttpMethod.Get, "/api/v1/property", null, null, ct, "mobile", new Dictionary<string, string> { ["If-None-Match"] = etag }).ConfigureAwait(false);
            return result.Status;
        }

        private static async Task<Dictionary<string, string>> FieldErrorsAsync(HttpFixture f, object body, CancellationToken ct)
        {
            ApiResult result = await Api.SendAsync(f, HttpMethod.Put, _Branding, HttpFixture.AdminToken, body, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.BadRequest, result.Status, "400 (body: " + result.Text + ")");
            Check.Equal("ValidationFailed", result.Error, "ValidationFailed");
            Dictionary<string, string> errors = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (JsonProperty p in result.Body.GetProperty("fieldErrors").EnumerateObject()) errors[p.Name] = p.Value.ToString();
            return errors;
        }

        private static Task<long> CountAuditAsync(HttpFixture f, string action, CancellationToken ct)
        {
            return ContentTestKit.ScalarAsync(f, "SELECT COUNT(*) AS cnt FROM auditrecords WHERE propertyid = '" + f.Services.Property.Property.Id + "' AND action = '" + action + "';", ct);
        }

        private static async Task<string> AuditSummaryAsync(HttpFixture f, string action, CancellationToken ct)
        {
            DataTable table = await f.Raw.ExecuteQueryAsync("SELECT summary FROM auditrecords WHERE propertyid = '" + f.Services.Property.Property.Id + "' AND action = '" + action + "' ORDER BY createdutc DESC LIMIT 1;", false, ct).ConfigureAwait(false);
            return table.Rows.Count == 0 ? String.Empty : RowReader.GetNullableString(table.Rows[0], "summary") ?? String.Empty;
        }

        #endregion
    }
}

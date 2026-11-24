namespace Test.Shared.Suites
{
    using System;
    using System.IO;
    using System.Net;
    using System.Net.Http;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Touchstone.Core;
    using TrellisKit.Core.PropertyConfig;

    /// <summary>
    /// GET /api/v1/property and GET /api/v1/property/assets/{file} over HTTP: the public subset, absolute asset URLs,
    /// cache headers, and path traversal attempts.
    /// </summary>
    public static class PropertyRoutesSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "PropertyRoutes";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Property routes (" + TestEnvironment.Provider + ")", new[]
            {
                SuiteCases.Http(Id, "Property.Public", "The property document is anonymous, omits web, and carries absolute brand URLs", async (f, ct) =>
                {
                    JsonElement body = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/property", null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(f.Property.Id, Api.Str(body, "id"), "Id");
                    Check.Equal(f.Property.Name, Api.Str(body, "name"), "Name");
                    Check.True(!body.TryGetProperty("web", out JsonElement _), "No web section");
                    // "directory" is the Phase 4 directory configuration (docs/PHASE4_CONTRACT.md section 0.2), never the server folder.
                    Check.True(body.TryGetProperty("directory", out JsonElement directorySection) && directorySection.ValueKind == JsonValueKind.Object &&
                        directorySection.TryGetProperty("venueLabel", out JsonElement _) && directorySection.TryGetProperty("map", out JsonElement _), "directory is the directory configuration");
                    Check.True(String.IsNullOrEmpty(f.Property.Directory) || !body.GetRawText().Contains(f.Property.Directory.Replace("\\", "\\\\"), StringComparison.Ordinal), "No server folder");
                    Check.True(body.TryGetProperty("events", out JsonElement _) && body.TryGetProperty("library", out JsonElement _), "events and library configuration");
                    JsonElement brand = body.GetProperty("brand");
                    Check.Equal(f.BaseUrl + "/api/v1/property/assets/logo.png", Api.Str(brand, "logoUrl"), "Absolute logo URL");
                    Check.Equal(f.BaseUrl + "/api/v1/property/assets/icon.png", Api.Str(brand, "iconUrl"), "Absolute icon URL");
                    Check.Equal(f.Property.Brand.Colors.Light.Primary, Api.Str(brand.GetProperty("colors").GetProperty("light"), "primary"), "Colors");
                    Check.Equal(f.Property.Profile.Rank.Values.Count, body.GetProperty("profile").GetProperty("rank").GetProperty("values").GetArrayLength(), "Ranks");
                    Check.Equal(f.Property.Reactions.Count, body.GetProperty("reactions").GetArrayLength(), "Reactions");
                    Check.Equal(f.Property.Membership.PriceUsdCents, body.GetProperty("membership").GetProperty("priceUsdCents").GetInt32(), "Price");
                    Check.True(body.GetProperty("features").GetProperty("signupCodes").ValueKind == JsonValueKind.True, "Features");
                }),
                SuiteCases.Http(Id, "Regression.Property.ForwardedHostIgnored", "X-Forwarded-Host never steers the cached property document's absolute URLs; unknown Host headers fall back to the configured API domain", async (f, ct) =>
                {
                    ApiResult forwarded = await Api.SendAsync(f, HttpMethod.Get, "/api/v1/property", null, null, ct, "web",
                        new System.Collections.Generic.Dictionary<string, string> { { "X-Forwarded-Host", "evil.example.net" } }).ConfigureAwait(false);
                    Check.Equal(f.BaseUrl + "/api/v1/property/assets/logo.png", Api.Str(forwarded.Body.GetProperty("brand"), "logoUrl"), "X-Forwarded-Host ignored");
                    Check.True(!forwarded.Text.Contains("evil.example.net", StringComparison.Ordinal), "No trace of the forwarded host");
                    f.HostHeader = "evil.example.net";
                    JsonElement unknown = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/property", null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("https://" + f.Property.Domains.Api + "/api/v1/property/assets/logo.png", Api.Str(unknown.GetProperty("brand"), "logoUrl"), "Unknown Host uses the configured API domain");
                    f.HostHeader = f.Property.Domains.Api;
                    JsonElement configured = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/property", null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("https://" + f.Property.Domains.Api + "/api/v1/property/assets/logo.png", Api.Str(configured.GetProperty("brand"), "logoUrl"), "Configured Host over https");
                }),
                SuiteCases.Http(Id, "Regression.Property.CapabilityFlags", "features.forums and features.search are engine-computed (Phase 3: true), beside the property's own switches", async (f, ct) =>
                {
                    JsonElement features = (await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/property", null, null, HttpStatusCode.OK, ct).ConfigureAwait(false)).GetProperty("features");
                    Check.True(features.GetProperty("forums").ValueKind == JsonValueKind.True, "features.forums true");
                    Check.True(features.GetProperty("search").ValueKind == JsonValueKind.True, "features.search true");
                    Check.True(features.TryGetProperty("signupCodes", out JsonElement _) && features.TryGetProperty("publicSamples", out JsonElement _), "Property switches kept");
                }),
                SuiteCases.Http(Id, "Regression.Property.PublicListingSwitches", "events.publicListingsEnabled, directory.publicListingsEnabled, and library.publicSamplesEnabled are served so clients skip disabled public routes", async (f, ct) =>
                {
                    bool eventsBefore = f.Settings.Events.PublicListingsEnabled;
                    try
                    {
                        f.Settings.Events.PublicListingsEnabled = false;
                        JsonElement off = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/property", null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                        Check.True(off.GetProperty("events").GetProperty("publicListingsEnabled").ValueKind == JsonValueKind.False, "Events listings off");
                        Check.True(off.GetProperty("directory").GetProperty("publicListingsEnabled").ValueKind == JsonValueKind.False, "Directory listings off by default");
                        Check.True(off.GetProperty("library").GetProperty("publicSamplesEnabled").ValueKind == JsonValueKind.True, "Library samples on by default");
                        await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/public/events/enumerate", null, new { maxResults = 3 }, HttpStatusCode.NotFound, ct).ConfigureAwait(false);
                        f.Settings.Events.PublicListingsEnabled = true;
                        JsonElement on = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/property", null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                        Check.True(on.GetProperty("events").GetProperty("publicListingsEnabled").ValueKind == JsonValueKind.True, "Events listings on");
                    }
                    finally
                    {
                        f.Settings.Events.PublicListingsEnabled = eventsBefore;
                    }
                }),
                SuiteCases.Http(Id, "Assets.ServeAndCache", "Brand images are served with their type, cache headers, and a working ETag", async (f, ct) =>
                {
                    byte[] expected = await File.ReadAllBytesAsync(Path.Combine(f.Property.Directory, "brand", "logo.png"), ct).ConfigureAwait(false);
                    string etag;
                    using (HttpResponseMessage response = await f.Client.GetAsync("/api/v1/property/assets/logo.png", ct).ConfigureAwait(false))
                    {
                        Check.Equal(HttpStatusCode.OK, response.StatusCode, "Status");
                        Check.Equal("image/png", response.Content.Headers.ContentType?.MediaType, "Content type");
                        Check.True(response.Headers.CacheControl != null && response.Headers.CacheControl.Public && response.Headers.CacheControl.MaxAge > TimeSpan.Zero, "Cache-Control");
                        Check.Equal(expected.Length, (await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false)).Length, "Bytes");
                        etag = response.Headers.ETag?.ToString() ?? String.Empty;
                        Check.True(etag.Length > 2, "ETag");
                    }
                    using (HttpRequestMessage conditional = new HttpRequestMessage(HttpMethod.Get, "/api/v1/property/assets/logo.png"))
                    {
                        conditional.Headers.TryAddWithoutValidation("If-None-Match", etag);
                        using (HttpResponseMessage response = await f.Client.SendAsync(conditional, ct).ConfigureAwait(false))
                            Check.Equal(HttpStatusCode.NotModified, response.StatusCode, "Not modified");
                    }
                    using (HttpResponseMessage ico = await f.Client.GetAsync("/api/v1/property/assets/icon.ico", ct).ConfigureAwait(false))
                        Check.Equal("image/x-icon", ico.Content.Headers.ContentType?.MediaType, "Icon type");
                }),
                SuiteCases.Http(Id, "Assets.Traversal", "Traversal, hidden, nested, missing, and non-image names are 404", async (f, ct) =>
                {
                    // A bare "%2E%2E" segment is collapsed by the HTTP client itself, so it is covered by the loader suite instead.
                    foreach (string path in new[]
                    {
                        "/api/v1/property/assets/..%2Fproperty.json",
                        "/api/v1/property/assets/%2E%2E%2Fproperty.json",
                        "/api/v1/property/assets/..%5Cproperty.json",
                        "/api/v1/property/assets/.hidden",
                        "/api/v1/property/assets/brand%2Flogo.png",
                        "/api/v1/property/assets/%2Fetc%2Fpasswd",
                        "/api/v1/property/assets/missing.png",
                        "/api/v1/property/assets/notes.txt"
                    })
                    {
                        using (HttpResponseMessage response = await f.Client.GetAsync(path, ct).ConfigureAwait(false))
                        {
                            Check.Equal(HttpStatusCode.NotFound, response.StatusCode, "Status for " + path);
                            string text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                            Check.True(!text.Contains("schemaVersion", StringComparison.Ordinal), "No property file leaked for " + path);
                        }
                    }
                }, CopyWithTextFile)
            });
        }

        #endregion

        #region Private-Methods

        private static void CopyWithTextFile(PropertyConfiguration property)
        {
            string copy = TestProperty.CopySample();
            File.WriteAllText(Path.Combine(copy, "brand", "notes.txt"), "not an image");
            File.WriteAllText(Path.Combine(copy, "brand", ".hidden"), "hidden");
            property.Directory = copy;
        }

        #endregion
    }
}

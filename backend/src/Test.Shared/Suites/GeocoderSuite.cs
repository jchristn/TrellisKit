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
    using Test.Shared.Fakes;
    using Touchstone.Core;
    using TrellisKit.Server.Services;

    /// <summary>
    /// Phase 4 geocoding and the map tile proxy (docs/PHASE4_CONTRACT.md sections 3.5 and 3.6) against fake loopback
    /// providers (never a real provider): Nominatim and Http adapters, the cache, the manual pin rule, the failure path,
    /// place search, regeocoding through GeocodeWorker, SSRF refusal, request history redaction, and the tile proxy.
    /// </summary>
    public static class GeocoderSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "Geocoder";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Geocoder (" + TestEnvironment.Provider + ")", new[]
            {
                Case("Geocoder.NominatimAndCache", "Staff address writes geocode through a Nominatim compatible fake with the generic User-Agent and no member data; repeated addresses hit the cache", NominatimAsync),
                Case("Geocoder.AwsCredentials", "AWS Location accepts encrypted property credentials, retains the default chain option, and rotates the cached adapter when credentials change", AwsCredentialsAsync),
                Case("Geocoder.HttpPlaceSearch", "Place search through a Pelias style fake answers at most 5 candidates, and needs a configured geocoder (503 otherwise)", PlaceSearchAsync),
                Case("Geocoder.ManualPinAndRegeocode", "Regeocoding through GeocodeWorker fills missing pins and keeps manual ones unless the address changes", ManualAsync),
                Case("Geocoder.FailurePath", "A provider error saves the venue with geo.source None (needs a pin); an explicit geocode request with no match is 400 GeocodeFailed", FailureAsync),
                Case("Geocoder.SsrfRefusal", "A base URL on a private or loopback address leaves the geocoder not configured under the production address policy", SsrfAsync),
                SuiteCases.Unit(Id, "Geocoder.HistoryRedaction", "Request history redacts near.lat and near.lng and the near query parameter", RedactionAsync),
                Case("Tiles.Proxy", "The tile proxy fetches only the configured origin with the secret key, rewrites documents without the key, caches answers, answers 404 when off and 503 when misconfigured", TilesAsync)
            });
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string caseId, string name, Func<HttpFixture, CancellationToken, Task> body)
        {
            return SuiteCases.Http(Id, caseId, name, (f, ct) =>
            {
                DirectoryTestKit.Relax(f);
                return body(f, ct);
            });
        }

        private static object Address(string line1)
        {
            return new { line1, locality = "Exampleville", region = "EX", postalCode = "00000", countryCode = "US" };
        }

        private static async Task AwsCredentialsAsync(HttpFixture f, CancellationToken ct)
        {
            await DirectoryTestKit.SettingsAsync(f, new { values = new Dictionary<string,object> {
                ["directory.geocoder.provider"] = "AwsLocation", ["directory.geocoder.placeIndexName"] = "test-places",
                ["directory.geocoder.region"] = "us-west-2", ["directory.geocoder.accessKeyId"] = "test-access", ["directory.geocoder.apiKey"] = "test-secret" } }, ct).ConfigureAwait(false);
            TrellisKit.Core.Integrations.Interfaces.IGeocoder first = await f.Services.Directory.Geocoding.GeocoderAsync(ct).ConfigureAwait(false);
            Check.True(first.IsConfigured, "Configured with a property credential pair");
            // Inspect the SDK's resolved credential object without making a network request or invoking the default chain.
            System.Reflection.MethodInfo factory = typeof(TrellisKit.Core.Integrations.Implementations.AwsLocationGeocoder).GetMethod("Client", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            Amazon.LocationService.AmazonLocationServiceClient sdk = (Amazon.LocationService.AmazonLocationServiceClient)factory.Invoke(first, null)!;
            System.Reflection.PropertyInfo credentialProperty = typeof(Amazon.Runtime.AmazonServiceClient).GetProperty("ExplicitAWSCredentials", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)!;
            Amazon.Runtime.AWSCredentials credentials = (Amazon.Runtime.AWSCredentials)credentialProperty.GetValue(sdk)!;
            Check.True(credentials is Amazon.Runtime.BasicAWSCredentials, "SDK uses the explicitly configured pair");
            Amazon.Runtime.ImmutableCredentials pair = credentials.GetCredentials();
            Check.Equal("test-access", pair.AccessKey, "Configured access key");
            Check.Equal("test-secret", pair.SecretKey, "Decrypted provider secret reaches SDK");
            JsonElement settings = await Api.ExpectAsync(f, HttpMethod.Get, DirectoryTestKit.Settings, HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(!settings.GetRawText().Contains("test-secret", StringComparison.Ordinal), "Secret never returned by settings reads");
            await DirectoryTestKit.SettingsAsync(f, new { values = new Dictionary<string,object> { ["directory.geocoder.apiKey"] = "rotated-secret" } }, ct).ConfigureAwait(false);
            TrellisKit.Core.Integrations.Interfaces.IGeocoder rotated = await f.Services.Directory.Geocoding.GeocoderAsync(ct).ConfigureAwait(false);
            Check.True(!Object.ReferenceEquals(first, rotated), "Secret change invalidates cached adapter");
            await DirectoryTestKit.SettingsAsync(f, new { values = new Dictionary<string,object> { ["directory.geocoder.accessKeyId"] = "" } }, ct).ConfigureAwait(false);
            Check.True((await f.Services.Directory.Geocoding.GeocoderAsync(ct).ConfigureAwait(false)).IsConfigured, "Empty access key retains the default credential chain");
        }

        private static async Task NominatimAsync(HttpFixture f, CancellationToken ct)
        {
            await using (FakeDirectoryServer server = new FakeDirectoryServer())
            {
                server.Json("/search", DirectoryTestKit.Nominatim(37.5, -122.25, "100 Example Street"));
                await DirectoryTestKit.UseGeocoderAsync(f, "Nominatim", server.BaseUrl, ct, new Dictionary<string, object> { { "directory.geocoder.email", "ops@example.test" } }).ConfigureAwait(false);
                JsonElement property = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/property", null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                Check.True(property.GetProperty("directory").GetProperty("geocoderConfigured").GetBoolean(), "geocoderConfigured");
                JsonElement venue = await DirectoryTestKit.VenueAsync(f, "Geocoded Club", ct, extra: new { address = Address("100 Example Street") }).ConfigureAwait(false);
                Check.Equal("Geocoder", Api.Str(venue.GetProperty("geo"), "source"), "Geocoded");
                Check.Equal(37.5, venue.GetProperty("geo").GetProperty("lat").GetDouble(), "Latitude from the provider");
                Check.Equal(1, server.Requests.Count, "One provider call");
                string request = server.Requests[0];
                Check.True(request.Contains("format=jsonv2", StringComparison.Ordinal) && request.Contains("email=ops%40example.test", StringComparison.Ordinal), "Nominatim parameters (" + request + ")");
                Check.True(server.Headers[0].Contains("User-Agent: TrellisKit-Geocoder/", StringComparison.Ordinal), "Generic User-Agent");
                Check.True(!server.Headers[0].Contains("Authorization", StringComparison.OrdinalIgnoreCase), "No member credentials");
                await DirectoryTestKit.VenueAsync(f, "Second Geocoded Club", ct, extra: new { address = Address("100  example street") }).ConfigureAwait(false);
                Check.Equal(1, server.Requests.Count, "Normalized addresses hit the cache");
                Check.Equal(1L, await f.Database.DirectoryListings.GeocodeCache.CountAsync(ct).ConfigureAwait(false), "One cache entry");
                Check.Equal(0, await f.Services.Directory.Admin.PrunePassAsync(ct).ConfigureAwait(false), "Nothing expired yet");
                await f.Raw.ExecuteQueryAsync("UPDATE geocoderesults SET expiresutc = now() - interval '1 day';", false, ct).ConfigureAwait(false);
                Check.Equal(1, await f.Services.Directory.Admin.PrunePassAsync(ct).ConfigureAwait(false), "GeocodeCachePruneWorker deletes expired entries");
            }
        }

        private static async Task PlaceSearchAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/directory/geocode/enumerate", member.AccessToken, new { filters = new { query = "Exampleville" } }, (HttpStatusCode)503, "IntegrationNotConfigured", ct).ConfigureAwait(false);
            await using (FakeDirectoryServer server = new FakeDirectoryServer())
            {
                string features = String.Join(",", Enumerable.Range(0, 8).Select(i => "{\"type\":\"Feature\",\"geometry\":{\"type\":\"Point\",\"coordinates\":[" + (-122 + i * 0.01).ToString(System.Globalization.CultureInfo.InvariantCulture) +
                    ",37.5]},\"properties\":{\"label\":\"Exampleville " + i + "\"},\"bbox\":[-122.1,37.4,-121.9,37.6]}"));
                server.Json("/v1/search", "{\"type\":\"FeatureCollection\",\"features\":[" + features + "]}");
                await DirectoryTestKit.UseGeocoderAsync(f, "Http", server.BaseUrl + "/v1", ct, new Dictionary<string, object> { { "directory.geocoder.apiKey", "secret-geo-key" }, { "directory.geocoder.authHeaderName", "X-Api-Key" } }).ConfigureAwait(false);
                JsonElement found = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/directory/geocode/enumerate", member.AccessToken, new { filters = new { query = "Exampleville" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                Check.Equal(5, found.GetProperty("objects").GetArrayLength(), "At most 5 candidates");
                Check.Equal("Exampleville 0", Api.Str(found.GetProperty("objects")[0], "label"), "Label");
                Check.Equal(37.4, found.GetProperty("objects")[0].GetProperty("bbox").GetProperty("south").GetDouble(), "Bounding box");
                JsonElement first = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/directory/geocode?query=Exampleville&maxResults=2", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                Check.Equal(5L, first.GetProperty("totalRecords").GetInt64(), "Totals describe finite provider candidate set");
                JsonElement second = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/directory/geocode/enumerate", member.AccessToken, new { maxResults = 2, continuationToken = Api.Str(first, "continuationToken"), filters = new { query = "Exampleville" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                Check.Equal("Exampleville 2", Api.Str(second.GetProperty("objects")[0], "label"), "Provider rank advances through stable candidate cursor");
                await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/directory/geocode/enumerate", member.AccessToken, new { maxResults = 2, continuationToken = Api.Str(first, "continuationToken"), filters = new { query = "Other place" } }, HttpStatusCode.BadRequest, "InvalidContinuationToken", ct).ConfigureAwait(false);
                JsonElement filtered = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/directory/geocode/enumerate", member.AccessToken, new { filters = new { query = "Exampleville" }, search = "4" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                Check.Equal(1, filtered.GetProperty("objects").GetArrayLength(), "Candidate search is applied after bounded provider retrieval");
                string? captured = null;
                for (int attempt = 0; attempt < 40 && captured == null; attempt++)
                {
                    JsonElement history = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/request-history?pathContains=%2Fdirectory%2Fgeocode&method=GET", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    JsonElement item = history.GetProperty("objects").EnumerateArray().FirstOrDefault();
                    if (item.ValueKind == JsonValueKind.Object) { captured = Api.Str(item, "id"); Check.Equal("/api/v1/directory/geocode", Api.Str(item, "url"), "Place query omitted from captured URL"); }
                    else await Task.Delay(50, ct).ConfigureAwait(false);
                }
                Check.True(captured != null, "Geocode GET history captured without location text");
                JsonElement detail = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/request-history/" + captured, HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                Check.True(detail.GetProperty("requestBody").ValueKind == JsonValueKind.Null && detail.GetProperty("responseBody").ValueKind == JsonValueKind.Null, "No provider query or candidates captured");
                Check.True(server.Requests[0].StartsWith("/v1/search?text=Exampleville", StringComparison.Ordinal), "Pelias style request (" + server.Requests[0] + ")");
                Check.True(server.Headers[0].Contains("X-Api-Key: secret-geo-key", StringComparison.Ordinal), "Key in the configured header");
                Check.Equal(0L, await f.Database.DirectoryListings.GeocodeCache.CountAsync(ct).ConfigureAwait(false), "Place search stores nothing");
                await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/directory/geocode/enumerate", member.AccessToken, new { filters = new { query = "x" } }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                JsonElement settings = await Api.ExpectAsync(f, HttpMethod.Get, DirectoryTestKit.Settings, HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                Check.True(!settings.ToString().Contains("secret-geo-key", StringComparison.Ordinal), "Secrets never returned");
                f.Settings.Directory.GeocodePerMinute = 1;
                ApiResult limited = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/directory/geocode/enumerate", member.AccessToken, new { filters = new { query = "Exampleville" } }, ct).ConfigureAwait(false);
                Check.Equal((HttpStatusCode)429, limited.Status, "Rate limited");
            }
        }

        private static async Task ManualAsync(HttpFixture f, CancellationToken ct)
        {
            string manual = await DirectoryTestKit.VenueIdAsync(f, "Manual Club", ct, 10.0, 10.0, new { address = Address("5 Example Road") }).ConfigureAwait(false);
            string missing = await DirectoryTestKit.VenueIdAsync(f, "Missing Pin Club", ct, extra: new { address = Address("6 Example Road") }).ConfigureAwait(false);
            await using (FakeDirectoryServer server = new FakeDirectoryServer())
            {
                server.Json("/search", DirectoryTestKit.Nominatim(20.0, 20.0, "Example Road"));
                await DirectoryTestKit.UseGeocoderAsync(f, "Nominatim", server.BaseUrl, ct).ConfigureAwait(false);
                JsonElement queued = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/directory/regeocode", HttpFixture.AdminToken, new { onlyMissing = false }, HttpStatusCode.Accepted, ct).ConfigureAwait(false);
                Check.Equal(1, queued.GetProperty("queued").GetInt32(), "Manual pins are not queued");
                Check.Equal(1, await f.Services.Directory.Admin.GeocodePassAsync(ct).ConfigureAwait(false), "GeocodeWorker pass");
                JsonElement filled = await Api.ExpectAsync(f, HttpMethod.Get, DirectoryTestKit.AdminVenues + "/" + missing, HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                Check.Equal("Geocoder", Api.Str(filled.GetProperty("geo"), "source"), "Missing pin filled");
                JsonElement kept = await Api.ExpectAsync(f, HttpMethod.Get, DirectoryTestKit.AdminVenues + "/" + manual, HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                Check.Equal("Manual", Api.Str(kept.GetProperty("geo"), "source"), "Manual pin kept");
                Check.Equal(10.0, kept.GetProperty("geo").GetProperty("lat").GetDouble(), "Manual position kept");
                JsonElement moved = await Api.ExpectAsync(f, HttpMethod.Put, DirectoryTestKit.AdminVenues + "/" + manual, HttpFixture.AdminToken, new { address = Address("7 Example Road") }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                Check.Equal("Geocoder", Api.Str(moved.GetProperty("geo"), "source"), "An address change replaces the manual pin");
                Check.Equal(20.0, moved.GetProperty("geo").GetProperty("lat").GetDouble(), "New position");
            }
        }

        private static async Task FailureAsync(HttpFixture f, CancellationToken ct)
        {
            await using (FakeDirectoryServer server = new FakeDirectoryServer())
            {
                server.Respond("/search", 500, "text/plain", _ => "boom");
                await DirectoryTestKit.UseGeocoderAsync(f, "Nominatim", server.BaseUrl, ct).ConfigureAwait(false);
                JsonElement venue = await DirectoryTestKit.VenueAsync(f, "Failing Club", ct, extra: new { address = Address("9 Example Lane") }).ConfigureAwait(false);
                Check.Equal("None", Api.Str(venue.GetProperty("geo"), "source"), "Saved without a pin");
                Check.True(venue.GetProperty("needsPin").GetBoolean(), "Needs a pin");
                server.Json("/search", "[]");
                await Api.ExpectErrorAsync(f, HttpMethod.Put, DirectoryTestKit.AdminVenues + "/" + Api.Str(venue, "id"), HttpFixture.AdminToken, new { address = Address("10 Example Lane"), geocode = true }, HttpStatusCode.BadRequest, "GeocodeFailed", ct).ConfigureAwait(false);
            }
        }

        private static async Task SsrfAsync(HttpFixture f, CancellationToken ct)
        {
            await using (FakeDirectoryServer server = new FakeDirectoryServer())
            {
                server.Json("/search", DirectoryTestKit.Nominatim(1, 1, "x"));
                await DirectoryTestKit.UseGeocoderAsync(f, "Nominatim", server.BaseUrl, ct).ConfigureAwait(false);
                f.Services.Directory.Geocoding.AddressPolicy = new TrellisKit.Core.Integrations.Implementations.PublicAddressPolicy();
                Check.True(!await f.Services.Directory.Geocoding.IsConfiguredAsync(ct).ConfigureAwait(false), "A loopback base URL is refused");
                JsonElement venue = await DirectoryTestKit.VenueAsync(f, "Refused Club", ct, extra: new { address = Address("1 Private Way") }).ConfigureAwait(false);
                Check.Equal("None", Api.Str(venue.GetProperty("geo"), "source"), "No geocoding");
                Check.Equal(0, server.Requests.Count, "Nothing was fetched");
                await DirectoryTestKit.SettingsAsync(f, new { values = new Dictionary<string, object> { { "directory.geocoder.baseUrl", "http://10.0.0.5:8080" } } }, ct).ConfigureAwait(false);
                Check.True(!await f.Services.Directory.Geocoding.IsConfiguredAsync(ct).ConfigureAwait(false), "A private address is refused");
                await Api.ExpectErrorAsync(f, HttpMethod.Put, DirectoryTestKit.Settings, HttpFixture.AdminToken, new { values = new Dictionary<string, object> { { "directory.geocoder.baseUrl", "file:///etc/passwd" } } }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            }
        }

        private static Task RedactionAsync(CancellationToken ct)
        {
            string? body = RequestHistoryCaptureService.RedactBody("{\"filters\":{\"near\":{\"lat\":37.77,\"lng\":-122.42,\"radiusKm\":25}}}", false);
            Check.True(body != null && !body.Contains("37.77", StringComparison.Ordinal) && !body.Contains("-122.42", StringComparison.Ordinal) && body.Contains("radiusKm", StringComparison.Ordinal), "near.lat and near.lng redacted (" + body + ")");
            string? url = RequestHistoryCaptureService.RedactUrl("/api/v1/venues?near=37.77,-122.42,25&maxResults=5");
            Check.True(url != null && !url.Contains("37.77", StringComparison.Ordinal) && url.Contains("maxResults=5", StringComparison.Ordinal), "near query parameter redacted (" + url + ")");
            return Task.CompletedTask;
        }

        private static async Task TilesAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/directory/tiles/style.json", member.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            await using (FakeDirectoryServer server = new FakeDirectoryServer())
            {
                string origin = server.BaseUrl;
                server.Respond("/maps/style.json", 200, "application/json", target => "{\"sources\":{\"base\":{\"url\":\"" + origin + "/maps/tiles.json?key=proxy-secret\"}},\"echo\":\"" + target + "\"}");
                server.Respond("/maps/tiles/1/2/3.pbf", 200, "application/x-protobuf", _ => "TILEDATA");
                f.Services.Directory.Tiles.AddressPolicy = new LoopbackTestAddressPolicy();
                await DirectoryTestKit.SettingsAsync(f, new { values = new Dictionary<string, object> { { "directory.map.provider", "MapLibre" }, { "directory.map.styleUrl", origin + "/maps/style.json" }, { "directory.map.tileProxy", true } } }, ct).ConfigureAwait(false);
                await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/directory/tiles/style.json", member.AccessToken, null, (HttpStatusCode)503, "IntegrationNotConfigured", ct).ConfigureAwait(false);
                await DirectoryTestKit.SettingsAsync(f, new { values = new Dictionary<string, object> { { "directory.map.proxyKey", "proxy-secret" }, { "directory.map.tileBaseUrl", origin + "/maps" } } }, ct).ConfigureAwait(false);
                f.Services.Directory.Tiles.AddressPolicy = new LoopbackTestAddressPolicy();
                JsonElement property = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/property", null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                Check.Equal("/api/v1/directory/tiles/maps/style.json", Api.Str(property.GetProperty("directory").GetProperty("map"), "styleUrl"), "The style URL points at the proxy");
                ApiResult style = await Api.SendAsync(f, HttpMethod.Get, "/api/v1/directory/tiles/style.json", member.AccessToken, null, ct).ConfigureAwait(false);
                Check.Equal(HttpStatusCode.OK, style.Status, "Style proxied (" + style.Text + ")");
                Check.True(!style.Text.Contains("proxy-secret", StringComparison.Ordinal), "The key never reaches the client");
                Check.True(style.Text.Contains("/api/v1/directory/tiles/tiles.json", StringComparison.Ordinal) && !style.Text.Contains(origin + "/maps/tiles.json", StringComparison.Ordinal), "Document URLs rewritten to the proxy");
                Check.True(server.Requests[0].Contains("key=proxy-secret", StringComparison.Ordinal), "The key is added upstream");
                Check.Equal("public, max-age=86400", style.Headers.TryGetValue("Cache-Control", out string? cache) ? cache : null, "Cache-Control");
                ApiResult tile = await Api.SendAsync(f, HttpMethod.Get, "/api/v1/directory/tiles/tiles/1/2/3.pbf?key=client-tries", member.AccessToken, null, ct).ConfigureAwait(false);
                Check.Equal(HttpStatusCode.OK, tile.Status, "Tile proxied");
                Check.Equal("TILEDATA", tile.Text, "Tile body");
                Check.True(!server.Requests.Any(r => r.Contains("client-tries", StringComparison.Ordinal)), "A client key parameter is dropped");
                int before = server.Requests.Count;
                await Api.SendAsync(f, HttpMethod.Get, "/api/v1/directory/tiles/tiles/1/2/3.pbf", member.AccessToken, null, ct).ConfigureAwait(false);
                Check.Equal(before, server.Requests.Count, "Cached tiles are not fetched again");
                ApiResult escape = await Api.SendAsync(f, HttpMethod.Get, "/api/v1/directory/tiles/..%2F..%2Fsecret", member.AccessToken, null, ct).ConfigureAwait(false);
                Check.True((int)escape.Status == 400 || (int)escape.Status == 404, "Paths cannot leave the origin (" + (int)escape.Status + ")");
                ApiResult missing = await Api.SendAsync(f, HttpMethod.Get, "/api/v1/directory/tiles/nope.pbf", member.AccessToken, null, ct).ConfigureAwait(false);
                Check.Equal(HttpStatusCode.NotFound, missing.Status, "Upstream 404");
            }
        }

        #endregion
    }
}

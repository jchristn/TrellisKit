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
    /// Phase 4 spatial queries (docs/PHASE4_CONTRACT.md sections 2.1, 3.2 to 3.4): radius filtering with the Haversine
    /// distance, DistanceAscending keyset continuation, bounding boxes across the antimeridian, poles, InvalidGeo, map
    /// clustering bounds and counts, and the visibility of Approximate and LocalityOnly venues.
    /// </summary>
    public static class GeoQuerySuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "GeoQuery";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Geo queries (" + TestEnvironment.Provider + ")", new[]
            {
                SuiteCases.Unit(Id, "Geo.Math", "Haversine distances, radius boxes split at the antimeridian and open at the poles, and grid cell sizes", MathAsync),
                Case("Geo.RadiusAndKeyset", "near keeps venues within the radius ordered by distance with exact distances, pages by (distance, id) without gaps or repeats, and rejects bad input with InvalidGeo", RadiusAsync),
                Case("Geo.BoxAntimeridianPoles", "bbox filters, a box with west greater than east crosses the antimeridian, near queries close to a pole and across the antimeridian find their venues", BoxAsync),
                Case("Geo.MapClusters", "The map returns points while they fit and grid clusters otherwise, with counts that add up and bounds that hold their venues; LocalityOnly venues never appear", MapAsync),
                Case("Geo.Visibility", "Exact pins have 5 decimals, Approximate 2 with no street address, LocalityOnly none with whole-kilometer distances; staff see the exact values", VisibilityAsync)
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

        private static Task MathAsync(CancellationToken ct)
        {
            double d = GeoMath.HaversineKm(37.7749, -122.4194, 34.0522, -118.2437);
            Check.True(Math.Abs(d - 559.1) < 2, "San Francisco to Los Angeles is about 559 km (" + d + ")");
            List<GeoBox> split = GeoMath.NearBoxes(new GeoNear { Lat = 0, Lng = 179.9, RadiusKm = 50 });
            Check.Equal(2, split.Count, "A box across the antimeridian splits in two");
            Check.True(split.All(b => b.West <= b.East), "Split boxes do not wrap");
            List<GeoBox> pole = GeoMath.NearBoxes(new GeoNear { Lat = 89.9, Lng = 10, RadiusKm = 50 });
            Check.Equal(1, pole.Count, "One band at a pole");
            Check.True(pole[0].West == -180 && pole[0].East == 180 && pole[0].North == 90, "The polar band spans every longitude and is clamped at 90");
            double cell = GeoMath.CellSize(new GeoBox { South = 0, West = 0, North = 10, East = 10 }, 3, 20);
            Check.True((Math.Floor(10 / cell) + 2) * (Math.Floor(10 / cell) + 2) <= 20, "Cells fit the limit");
            Check.True(Math.Abs(DirectoryTextSimilarity("Example Club Downtown", "Example Club Downtwn") - 0.95) < 0.05, "Similar names score high");
            Check.True(DirectoryTextSimilarity("Example Club", "Different Studio") < 0.5, "Different names score low");
            return Task.CompletedTask;
        }

        private static double DirectoryTextSimilarity(string a, string b)
        {
            return TrellisKit.Server.Services.DirectoryText.Similarity(a, b);
        }

        private static async Task RadiusAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            Dictionary<string, double> expected = new Dictionary<string, double>();
            for (int i = 0; i < 7; i++)
            {
                double lat = 37.0 + i * 0.05;
                string id = await DirectoryTestKit.VenueIdAsync(f, "Radius Club " + i, ct, lat, -122.0).ConfigureAwait(false);
                expected[id] = GeoMath.HaversineKm(37.0, -122.0, lat, -122.0);
            }
            string far = await DirectoryTestKit.VenueIdAsync(f, "Far Club", ct, 40.0, -122.0).ConfigureAwait(false);
            await DirectoryTestKit.VenueIdAsync(f, "No Pin Club", ct).ConfigureAwait(false);
            List<string> seen = new List<string>();
            List<double> distances = new List<double>();
            string? token = null;
            int pages = 0;
            do
            {
                JsonElement page = await DirectoryTestKit.ListAsync(f, member.AccessToken, new { maxResults = 3, continuationToken = token, filters = new { near = new { lat = 37.0, lng = -122.0, radiusKm = 40 } } }, ct).ConfigureAwait(false);
                foreach (JsonElement venue in page.GetProperty("objects").EnumerateArray())
                {
                    seen.Add(Api.Str(venue, "id"));
                    distances.Add(venue.GetProperty("distanceKm").GetDouble());
                }
                token = page.TryGetProperty("continuationToken", out JsonElement t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
                pages++;
            } while (token != null && pages < 10);
            Check.Equal(7, seen.Count, "Every venue within 40 km once");
            Check.Equal(7, seen.Distinct().Count(), "No repeats across pages");
            Check.True(pages >= 3, "Paged (" + pages + ")");
            Check.True(!seen.Contains(far), "Venues outside the radius are left out");
            for (int i = 1; i < distances.Count; i++) Check.True(distances[i] >= distances[i - 1], "DistanceAscending order");
            foreach (string id in seen) Check.True(Math.Abs(expected[id] - distances[seen.IndexOf(id)]) <= 0.1, "Distance to one decimal");
            JsonElement small = await DirectoryTestKit.ListAsync(f, member.AccessToken, new { filters = new { near = "37.0,-122.0,6" } }, ct).ConfigureAwait(false);
            Check.Equal(2, small.GetProperty("objects").GetArrayLength(), "GET form of near with a smaller radius");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/venues/enumerate", member.AccessToken, new { filters = new { near = new { lat = 91, lng = 0 } } }, HttpStatusCode.BadRequest, "InvalidGeo", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/venues/enumerate", member.AccessToken, new { filters = new { near = new { lat = 1, lng = 181 } } }, HttpStatusCode.BadRequest, "InvalidGeo", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/venues/enumerate", member.AccessToken, new { filters = new { near = new { lat = 1, lng = 1, radiusKm = 5000 } } }, HttpStatusCode.BadRequest, "InvalidGeo", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/venues/enumerate", member.AccessToken, new { filters = new { near = new { lat = 1, lng = 1 }, bbox = new { south = 0, west = 0, north = 2, east = 2 } } }, HttpStatusCode.BadRequest, "InvalidGeo", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/venues/enumerate", member.AccessToken, new { ordering = "DistanceAscending" }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            JsonElement named = await DirectoryTestKit.ListAsync(f, member.AccessToken, new { maxResults = 100 }, ct).ConfigureAwait(false);
            List<string> names = named.GetProperty("objects").EnumerateArray().Select(v => Api.Str(v, "name")).ToList();
            Check.True(names.SequenceEqual(names.OrderBy(n => n.ToLowerInvariant(), StringComparer.Ordinal)), "NameAscending without near");
        }

        private static async Task BoxAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            string east = await DirectoryTestKit.VenueIdAsync(f, "East Of Line", ct, -16.0, 179.6).ConfigureAwait(false);
            string west = await DirectoryTestKit.VenueIdAsync(f, "West Of Line", ct, -16.1, -179.6).ConfigureAwait(false);
            string zero = await DirectoryTestKit.VenueIdAsync(f, "Meridian Club", ct, -16.0, 0.5).ConfigureAwait(false);
            string polar = await DirectoryTestKit.VenueIdAsync(f, "Polar Club", ct, 89.95, 120.0).ConfigureAwait(false);
            JsonElement crossing = await DirectoryTestKit.ListAsync(f, member.AccessToken, new { filters = new { bbox = new { south = -17, west = 179, north = -15, east = -179 } } }, ct).ConfigureAwait(false);
            Check.True(DirectoryTestKit.Ids(crossing).OrderBy(i => i).SequenceEqual(new[] { east, west }.OrderBy(i => i)), "A crossing box holds both sides of the antimeridian");
            JsonElement normal = await DirectoryTestKit.ListAsync(f, member.AccessToken, new { filters = new { bbox = "-17,0,-15,1" } }, ct).ConfigureAwait(false);
            Check.True(DirectoryTestKit.Ids(normal).SequenceEqual(new List<string> { zero }), "A normal box (GET form)");
            JsonElement across = await DirectoryTestKit.ListAsync(f, member.AccessToken, new { filters = new { near = new { lat = -16.05, lng = 179.9, radiusKm = 100 } } }, ct).ConfigureAwait(false);
            Check.True(DirectoryTestKit.Ids(across).Contains(east) && DirectoryTestKit.Ids(across).Contains(west), "A near query across the antimeridian");
            JsonElement pole = await DirectoryTestKit.ListAsync(f, member.AccessToken, new { filters = new { near = new { lat = 89.9, lng = -60.0, radiusKm = 50 } } }, ct).ConfigureAwait(false);
            Check.True(DirectoryTestKit.Ids(pole).SequenceEqual(new List<string> { polar }), "A near query at the pole finds venues on the other side of it");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/venues/enumerate", member.AccessToken, new { filters = new { bbox = new { south = 10, west = 0, north = 5, east = 1 } } }, HttpStatusCode.BadRequest, "InvalidGeo", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/venues/enumerate", member.AccessToken, new { filters = new { bbox = "1,2,3" } }, HttpStatusCode.BadRequest, "InvalidGeo", ct).ConfigureAwait(false);
        }

        private static async Task MapAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            List<string> ids = new List<string>();
            for (int i = 0; i < 24; i++)
                ids.Add(await DirectoryTestKit.VenueIdAsync(f, "Map Club " + i, ct, 10.0 + (i % 6) * 1.5, 20.0 + (i / 6) * 1.5).ConfigureAwait(false));
            await DirectoryTestKit.VenueIdAsync(f, "Hidden Locality Club", ct, 11.0, 21.0, new { addressVisibility = "LocalityOnly" }).ConfigureAwait(false);
            object box = new { south = 9.0, west = 19.0, north = 19.0, east = 26.0 };
            JsonElement points = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/directory/map/enumerate", member.AccessToken, new { bbox = box, zoom = 6, maxResults = 100 }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(24, points.GetProperty("objects").GetArrayLength(), "Every venue is a point while they fit; LocalityOnly is left out");
            Check.True(points.GetProperty("objects").EnumerateArray().All(o => Api.Str(o, "kind") == "Point"), "Points");
            Check.True(points.GetProperty("endOfResults").GetBoolean(), "One page");
            JsonElement filtered = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/directory/map/enumerate", member.AccessToken, new { bbox = new { south = 9.0, west = 19.0, north = 11.0, east = 21.0 }, zoom = 8 }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(1, filtered.GetProperty("objects").GetArrayLength(), "Viewport bounds");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/directory/map/enumerate", member.AccessToken, new { zoom = 3 }, HttpStatusCode.BadRequest, "InvalidGeo", ct).ConfigureAwait(false);
            JsonElement firstPage = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/directory/map/enumerate", member.AccessToken, new { bbox = box, zoom = 6, maxResults = 3 }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(3, firstPage.GetProperty("objects").GetArrayLength(), "Map respects transport page size");
            Check.True(firstPage.GetProperty("objects").EnumerateArray().All(feature => Api.Str(feature, "kind") == "Point"), "Small pages do not change clustering");
            string cursor = Api.Str(firstPage, "continuationToken");
            JsonElement secondPage = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/directory/map/enumerate", member.AccessToken, new { bbox = box, zoom = 6, maxResults = 3, continuationToken = cursor }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(24L, secondPage.GetProperty("totalRecords").GetInt64(), "Exact feature totals persist across pages");
            Check.Equal(0, firstPage.GetProperty("objects").EnumerateArray().Select(feature => Api.Str(feature, "id")).Intersect(secondPage.GetProperty("objects").EnumerateArray().Select(feature => Api.Str(feature, "id"))).Count(), "Map continuation has no repeats");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/directory/map/enumerate", member.AccessToken, new { bbox = box, zoom = 7, continuationToken = cursor }, HttpStatusCode.BadRequest, "InvalidContinuationToken", ct).ConfigureAwait(false);

            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/property/settings", HttpFixture.AdminToken, new { values = new Dictionary<string, object> { ["directory.map.maxFeatures"] = 50 } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            for (int i = 24; i < 52; i++) await DirectoryTestKit.VenueIdAsync(f, "Map extra " + i, ct, 10.0 + (i % 6) * 1.5, 20.0 + (i % 4) * 1.5).ConfigureAwait(false);
            JsonElement clusters = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/directory/map/enumerate", member.AccessToken, new { bbox = box, zoom = 3, maxResults = 100 }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            List<JsonElement> features = clusters.GetProperty("objects").EnumerateArray().ToList();
            Check.True(features.Count <= 50 && features.Any(o => Api.Str(o, "kind") == "Cluster"), "Clusters when the venues do not fit (" + features.Count + ")");
            int total = features.Sum(o => Api.Str(o, "kind") == "Cluster" ? o.GetProperty("count").GetInt32() : 1);
            Check.Equal(52, total, "Cluster counts add up");
            foreach (JsonElement c in features.Where(o => Api.Str(o, "kind") == "Cluster"))
            {
                JsonElement b = c.GetProperty("bbox");
                double lat = c.GetProperty("lat").GetDouble();
                double lng = c.GetProperty("lng").GetDouble();
                Check.True(lat >= b.GetProperty("south").GetDouble() - 1e-6 && lat <= b.GetProperty("north").GetDouble() + 1e-6 && lng >= b.GetProperty("west").GetDouble() - 1e-6 && lng <= b.GetProperty("east").GetDouble() + 1e-6, "Center inside its bounds");
            }
            Check.Equal((long)features.Count, clusters.GetProperty("totalRecords").GetInt64(), "Cluster total counts features");
            Check.Equal(52L, clusters.GetProperty("matchedRecords").GetInt64(), "Matched venues are a separate scalar");

        }

        private static async Task VisibilityAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            object address = new { line1 = "1 Example Way", locality = "Exampleville", region = "EX", postalCode = "00001", countryCode = "US" };
            string exact = await DirectoryTestKit.VenueIdAsync(f, "Exact Club", ct, 37.123456789, -122.987654321, new { address }).ConfigureAwait(false);
            string approx = await DirectoryTestKit.VenueIdAsync(f, "Approximate Club", ct, 37.123456789, -122.987654321, new { address, addressVisibility = "Approximate" }).ConfigureAwait(false);
            string locality = await DirectoryTestKit.VenueIdAsync(f, "Locality Club", ct, 37.2, -122.9, new { address, addressVisibility = "LocalityOnly" }).ConfigureAwait(false);
            JsonElement e = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/venues/" + exact, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(37.12346, e.GetProperty("geo").GetProperty("lat").GetDouble(), "Exact: 5 decimals");
            Check.Equal("1 Example Way", Api.Str(e.GetProperty("address"), "line1"), "Exact address");
            JsonElement a = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/venues/" + approx, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(37.12, a.GetProperty("geo").GetProperty("lat").GetDouble(), "Approximate: 2 decimals");
            Check.Equal("Approximate", Api.Str(a.GetProperty("geo"), "precision"), "Approximate precision");
            Check.True(a.GetProperty("address").GetProperty("line1").ValueKind == JsonValueKind.Null && a.GetProperty("address").GetProperty("postalCode").ValueKind == JsonValueKind.Null, "No street address");
            Check.True(!a.ToString().Contains("37.1234", StringComparison.Ordinal), "The exact pin never leaks");
            JsonElement l = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/venues/" + locality, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(l.GetProperty("geo").GetProperty("lat").ValueKind == JsonValueKind.Null, "LocalityOnly: no pin");
            Check.Equal("Exampleville", Api.Str(l.GetProperty("address"), "locality"), "Locality shown");
            JsonElement near = await DirectoryTestKit.ListAsync(f, member.AccessToken, new { filters = new { near = new { lat = 37.0, lng = -122.9, radiusKm = 50 } } }, ct).ConfigureAwait(false);
            JsonElement nearLocality = DirectoryTestKit.Find(near, locality);
            double km = nearLocality.GetProperty("distanceKm").GetDouble();
            Check.Equal(Math.Round(km), km, "LocalityOnly distances are whole kilometers");
            JsonElement map = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/directory/map/enumerate", member.AccessToken, new { bbox = new { south = 36.0, west = -124.0, north = 38.0, east = -121.0 }, zoom = 9 }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            List<JsonElement> points = map.GetProperty("objects").EnumerateArray().ToList();
            Check.Equal(2, points.Count, "LocalityOnly venues never on the map");
            JsonElement approxPoint = points.First(p => Api.Str(p.GetProperty("venue"), "id") == approx);
            Check.Equal(37.12, approxPoint.GetProperty("venue").GetProperty("geo").GetProperty("lat").GetDouble(), "Approximate points at the rounded position");
            JsonElement staff = await Api.ExpectAsync(f, HttpMethod.Get, DirectoryTestKit.AdminVenues + "/" + approx, HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(Math.Abs(staff.GetProperty("geo").GetProperty("lat").GetDouble() - 37.123457) < 1e-6, "Staff see the exact pin");
            Check.Equal("1 Example Way", Api.Str(staff.GetProperty("address"), "line1"), "Staff see the exact address");
        }

        #endregion
    }
}

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

    /// <summary>
    /// Map-first event search (docs/PHASE4_CONTRACT.md section 4.2): the bbox filter of the event list (antimeridian,
    /// online events only with includeOnline, never with near), and POST /api/v1/events/map/enumerate (points while
    /// they fit, grid clusters otherwise, Online events never on the map, the stricter of event and venue address
    /// visibility, staff exact positions, blocked hosts, filters, and the rate limit).
    /// </summary>
    public static class EventMapSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "EventMap";

        #endregion

        #region Private-Members

        private const string _Map = "/api/v1/events/map/enumerate";
        private const string _List = "/api/v1/events/enumerate";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Events map (" + TestEnvironment.Provider + ")", new[]
            {
                Case("EventMap.BboxFilter", "filters.bbox keeps events positioned in the box (also across the antimeridian), leaves Online events out unless includeOnline, and rejects near with bbox and bad boxes with InvalidGeo", BboxAsync),
                Case("EventMap.PointsAndClusters", "The events map returns points while they fit and grid clusters otherwise with counts that add up; Online events never appear; filters and maxResults apply; bbox is required", PointsAsync),
                Case("EventMap.Visibility", "Approximate events sit at 2 decimals, LocalityOnly events stay off the map, an event at an Approximate or LocalityOnly venue is never more precise than the venue, and event staff see exact positions", VisibilityAsync),
                Case("EventMap.Access", "Events hosted only by a member the caller blocked are left off the map, and events.mapPerMinute limits the route with 429", AccessAsync)
            });
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string caseId, string name, Func<HttpFixture, CancellationToken, Task> body)
        {
            return SuiteCases.Http(Id, caseId, name, (f, ct) =>
            {
                EventTestKit.Relax(f);
                DirectoryTestKit.Relax(f);
                f.Settings.Events.MapPerMinute = 10000;
                return body(f, ct);
            });
        }

        private static object Place(double lat, double lng, string visibility = "Exact")
        {
            return new { name = "Example Hall", addressVisibility = visibility, address = new { line1 = "100 Example Street", locality = "Exampleville", region = "EX", postalCode = "00000", countryCode = "US" }, geo = new { lat, lng } };
        }

        private static Task<string> AtAsync(HttpFixture f, string title, double lat, double lng, CancellationToken ct, string visibility = "Exact", object? extra = null)
        {
            Dictionary<string, object?> body = new Dictionary<string, object?> { { "location", Place(lat, lng, visibility) } };
            if (extra != null)
                foreach (System.Reflection.PropertyInfo p in extra.GetType().GetProperties()) body[p.Name] = p.GetValue(extra);
            return PublishedWithAsync(f, HttpFixture.AdminToken, title, body, ct);
        }

        private static async Task<string> PublishedWithAsync(HttpFixture f, string bearer, string title, Dictionary<string, object?> extra, CancellationToken ct)
        {
            Dictionary<string, object?> body = EventTestKit.Body(title, EventTestKit.Local(10, 18), EventTestKit.Local(10, 20));
            foreach (KeyValuePair<string, object?> pair in extra) body[pair.Key] = pair.Value;
            JsonElement created = await EventTestKit.CreateAsync(f, bearer, body, ct).ConfigureAwait(false);
            string id = Api.Str(created, "id");
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/events/" + id + "/publish", bearer, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            return id;
        }

        private static Task<string> OnlineAsync(HttpFixture f, string title, CancellationToken ct)
        {
            return PublishedWithAsync(f, HttpFixture.AdminToken, title, new Dictionary<string, object?> { { "format", "Online" }, { "location", null }, { "online", new { platformLabel = "Video call", joinUrl = "https://meet.example.test/room" } } }, ct);
        }

        private static async Task<JsonElement> MapAsync(HttpFixture f, string bearer, object body, CancellationToken ct)
        {
            return await Api.ExpectAsync(f, HttpMethod.Post, _Map, bearer, body, HttpStatusCode.OK, ct).ConfigureAwait(false);
        }

        private static List<JsonElement> Points(JsonElement page)
        {
            return page.GetProperty("objects").EnumerateArray().Where(o => Api.Str(o, "kind") == "Point").ToList();
        }

        private static List<string> PointIds(JsonElement page)
        {
            return Points(page).Select(p => Api.Str(p.GetProperty("event"), "id")).ToList();
        }

        private static async Task BboxAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            string inside = await AtAsync(f, "Inside meetup", 10.5, 20.5, ct).ConfigureAwait(false);
            string outside = await AtAsync(f, "Outside meetup", 30.0, 20.5, ct).ConfigureAwait(false);
            string east = await AtAsync(f, "East of the line", -16.0, 179.6, ct).ConfigureAwait(false);
            string west = await AtAsync(f, "West of the line", -16.1, -179.6, ct).ConfigureAwait(false);
            string online = await OnlineAsync(f, "Online session", ct).ConfigureAwait(false);
            JsonElement box = await Api.ExpectAsync(f, HttpMethod.Post, _List, member.AccessToken, new { filters = new { bbox = new { south = 10, west = 20, north = 11, east = 21 } } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(EventTestKit.Ids(box).SequenceEqual(new[] { inside }), "Only the event in the box (" + String.Join(",", EventTestKit.Ids(box)) + ")");
            JsonElement withOnline = await Api.ExpectAsync(f, HttpMethod.Post, _List, member.AccessToken, new { filters = new { bbox = "10,20,11,21", includeOnline = true } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(EventTestKit.Ids(withOnline).OrderBy(i => i).SequenceEqual(new[] { inside, online }.OrderBy(i => i)), "includeOnline adds Online events (GET form of bbox)");
            JsonElement crossing = await Api.ExpectAsync(f, HttpMethod.Post, _List, member.AccessToken, new { filters = new { bbox = new { south = -17, west = 179, north = -15, east = -179 } } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(EventTestKit.Ids(crossing).OrderBy(i => i).SequenceEqual(new[] { east, west }.OrderBy(i => i)), "A box crossing the antimeridian holds both sides");
            Check.True(!EventTestKit.Ids(crossing).Contains(outside), "Outside events are left out");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, _List, member.AccessToken, new { filters = new { near = new { lat = 10, lng = 20 }, bbox = new { south = 10, west = 20, north = 11, east = 21 } } }, HttpStatusCode.BadRequest, "InvalidGeo", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, _List, member.AccessToken, new { filters = new { bbox = new { south = 11, west = 20, north = 10, east = 21 } } }, HttpStatusCode.BadRequest, "InvalidGeo", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, _List, member.AccessToken, new { filters = new { bbox = "1,2,3" } }, HttpStatusCode.BadRequest, "InvalidGeo", ct).ConfigureAwait(false);
        }

        private static async Task PointsAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            List<string> ids = new List<string>();
            for (int i = 0; i < 12; i++) ids.Add(await AtAsync(f, "Map meetup " + i, 10.0 + (i % 4) * 1.5, 20.0 + (i / 4) * 1.5, ct, "Exact", i == 0 ? new { capacity = 1 } : null).ConfigureAwait(false));
            await OnlineAsync(f, "Online map session", ct).ConfigureAwait(false);
            object box = new { south = 9.0, west = 19.0, north = 19.0, east = 26.0 };
            JsonElement points = await MapAsync(f, member.AccessToken, new { bbox = box, zoom = 6, maxResults = 100 }, ct).ConfigureAwait(false);
            Check.True(PointIds(points).OrderBy(i => i).SequenceEqual(ids.OrderBy(i => i)), "Every positioned event is a point while they fit; Online events never");
            Check.True(points.GetProperty("endOfResults").GetBoolean(), "One page");
            JsonElement first = Points(points).First(p => Api.Str(p.GetProperty("event"), "id") == ids[0]).GetProperty("event");
            Check.Equal("Map meetup 0", Api.Str(first, "title"), "Point carries the title");
            Check.Equal("Exact", Api.Str(first.GetProperty("geo"), "precision"), "Exact precision");
            Check.Equal("Exampleville", Api.Str(first, "locality"), "Locality");
            Check.True(first.TryGetProperty("startLocal", out JsonElement local) && local.GetString()!.Length >= 16, "Local start");
            JsonElement small = await MapAsync(f, member.AccessToken, new { bbox = new { south = 9.0, west = 19.0, north = 11.0, east = 21.0 }, zoom = 8 }, ct).ConfigureAwait(false);
            Check.True(PointIds(small).SequenceEqual(new[] { ids[0] }), "Viewport bounds");
            await EventTestKit.RsvpAsync(f, member.AccessToken, ids[0], "Going", ct).ConfigureAwait(false);
            JsonElement spots = await MapAsync(f, member.AccessToken, new { bbox = box, zoom = 6, filters = new { hasSpots = true } }, ct).ConfigureAwait(false);
            Check.True(!PointIds(spots).Contains(ids[0]) && PointIds(spots).Count == 11, "hasSpots leaves the full event out");
            JsonElement going = await MapAsync(f, member.AccessToken, new { bbox = box, zoom = 6, filters = new { rsvpStatus = new[] { "Going" } } }, ct).ConfigureAwait(false);
            Check.True(PointIds(going).SequenceEqual(new[] { ids[0] }), "rsvpStatus is the caller's");
            JsonElement searched = await MapAsync(f, member.AccessToken, new { bbox = box, zoom = 6, search = "meetup 11" }, ct).ConfigureAwait(false);
            Check.True(PointIds(searched).SequenceEqual(new[] { ids[11] }), "Search applies");
            JsonElement past = await MapAsync(f, member.AccessToken, new { bbox = box, zoom = 6, filters = new { when = "Past" } }, ct).ConfigureAwait(false);
            Check.Equal(0, PointIds(past).Count, "when applies");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, _Map, member.AccessToken, new { zoom = 3 }, HttpStatusCode.BadRequest, "InvalidGeo", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, _Map, member.AccessToken, new { bbox = box, filters = new { near = new { lat = 10, lng = 20 } } }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            JsonElement firstPage = await MapAsync(f, member.AccessToken, new { bbox = box, zoom = 6, maxResults = 3 }, ct).ConfigureAwait(false);
            Check.Equal(3, firstPage.GetProperty("objects").GetArrayLength(), "Transport page size does not force clustering");
            Check.True(firstPage.GetProperty("objects").EnumerateArray().All(feature => Api.Str(feature, "kind") == "Point"), "Configured budget keeps point identities stable");
            string cursor = Api.Str(firstPage, "continuationToken");
            JsonElement secondPage = await MapAsync(f, member.AccessToken, new { bbox = box, zoom = 6, maxResults = 3, continuationToken = cursor }, ct).ConfigureAwait(false);
            Check.Equal(12L, secondPage.GetProperty("totalRecords").GetInt64(), "Exact total counts features");
            Check.Equal(0, PointIds(firstPage).Intersect(PointIds(secondPage)).Count(), "Map continuation does not repeat points");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, _Map, member.AccessToken, new { bbox = box, zoom = 7, continuationToken = cursor }, HttpStatusCode.BadRequest, "InvalidContinuationToken", ct).ConfigureAwait(false);

            f.Settings.Events.MapMaxFeatures = 50;
            for (int i = 12; i < 52; i++) await AtAsync(f, "Map extra " + i, 10.0 + (i % 4) * 1.5, 20.0 + (i % 3) * 1.5, ct).ConfigureAwait(false);
            JsonElement clusters = await MapAsync(f, member.AccessToken, new { bbox = box, zoom = 3, maxResults = 100 }, ct).ConfigureAwait(false);
            List<JsonElement> features = clusters.GetProperty("objects").EnumerateArray().ToList();
            Check.True(features.Count <= 50 && features.Any(o => Api.Str(o, "kind") == "Cluster"), "Clusters when the events do not fit (" + features.Count + ")");
            Check.Equal(52, features.Sum(o => Api.Str(o, "kind") == "Cluster" ? o.GetProperty("count").GetInt32() : 1), "Cluster counts add up");
            foreach (JsonElement c in features.Where(o => Api.Str(o, "kind") == "Cluster"))
            {
                JsonElement b = c.GetProperty("bbox");
                double lat = c.GetProperty("lat").GetDouble();
                Check.True(lat >= b.GetProperty("south").GetDouble() - 1e-6 && lat <= b.GetProperty("north").GetDouble() + 1e-6, "Cluster center inside its bounds");
            }
            Check.Equal((long)features.Count, clusters.GetProperty("totalRecords").GetInt64(), "Cluster total is feature count");
            Check.Equal(52L, clusters.GetProperty("matchedRecords").GetInt64(), "Matched events are a separate scalar");

        }

        private static async Task VisibilityAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            object address = new { line1 = "1 Example Way", locality = "Exampleville", region = "EX", postalCode = "00001", countryCode = "US" };
            string exact = await AtAsync(f, "Exact meetup", 37.123456789, -122.987654321, ct).ConfigureAwait(false);
            string approx = await AtAsync(f, "Approximate meetup", 37.223456789, -122.887654321, ct, "Approximate").ConfigureAwait(false);
            string locality = await AtAsync(f, "Locality meetup", 37.3, -122.8, ct, "LocalityOnly").ConfigureAwait(false);
            string approxVenue = await DirectoryTestKit.VenueIdAsync(f, "Approximate Venue", ct, 37.4234567, -122.7876543, new { address, addressVisibility = "Approximate" }).ConfigureAwait(false);
            string hiddenVenue = await DirectoryTestKit.VenueIdAsync(f, "Locality Venue", ct, 37.5234567, -122.6876543, new { address, addressVisibility = "LocalityOnly" }).ConfigureAwait(false);
            string atApprox = await PublishedWithAsync(f, HttpFixture.AdminToken, "At the approximate venue", new Dictionary<string, object?> { { "location", new { venueId = approxVenue, addressVisibility = "Exact" } } }, ct).ConfigureAwait(false);
            string atHidden = await PublishedWithAsync(f, HttpFixture.AdminToken, "At the locality venue", new Dictionary<string, object?> { { "location", new { venueId = hiddenVenue, addressVisibility = "Exact" } } }, ct).ConfigureAwait(false);
            object box = new { south = 36.0, west = -124.0, north = 38.0, east = -121.0 };
            JsonElement map = await MapAsync(f, member.AccessToken, new { bbox = box, zoom = 9 }, ct).ConfigureAwait(false);
            List<string> ids = PointIds(map);
            Check.True(ids.Contains(exact) && ids.Contains(approx) && ids.Contains(atApprox), "Exact and Approximate events are on the map (" + String.Join(",", ids) + ")");
            Check.True(!ids.Contains(locality), "LocalityOnly events stay off the map");
            Check.True(!ids.Contains(atHidden), "An event at a LocalityOnly venue stays off the map even when the event says Exact");
            JsonElement e = Points(map).First(p => Api.Str(p.GetProperty("event"), "id") == exact).GetProperty("event").GetProperty("geo");
            Check.Equal(37.12346, e.GetProperty("lat").GetDouble(), "Exact: 5 decimals");
            JsonElement a = Points(map).First(p => Api.Str(p.GetProperty("event"), "id") == approx).GetProperty("event").GetProperty("geo");
            Check.Equal(37.22, a.GetProperty("lat").GetDouble(), "Approximate: 2 decimals");
            Check.Equal("Approximate", Api.Str(a, "precision"), "Approximate precision");
            JsonElement v = Points(map).First(p => Api.Str(p.GetProperty("event"), "id") == atApprox).GetProperty("event").GetProperty("geo");
            Check.Equal(37.42, v.GetProperty("lat").GetDouble(), "An Approximate venue rounds its events even when the event says Exact");
            Check.True(!map.ToString().Contains("37.4234", StringComparison.Ordinal) && !map.ToString().Contains("37.5234", StringComparison.Ordinal), "Venue pins never leak");
            JsonElement limited = await MapAsync(f, member.AccessToken, new { bbox = box, zoom = 2, maxResults = 1 }, ct).ConfigureAwait(false);
            Check.Equal(1, limited.GetProperty("objects").GetArrayLength(), "Visibility-safe points honor the transport cap");
            Check.Equal(3L, limited.GetProperty("matchedRecords").GetInt64(), "Matched count includes only placeable events");
            Check.True(!limited.ToString().Contains("37.4234", StringComparison.Ordinal), "Paged map never leaks exact approximate coordinates");
            JsonElement staff = await MapAsync(f, HttpFixture.AdminToken, new { bbox = box, zoom = 9 }, ct).ConfigureAwait(false);
            List<string> staffIds = PointIds(staff);
            Check.True(staffIds.Contains(locality) && staffIds.Contains(atHidden), "Event staff see every positioned event");
            JsonElement s = Points(staff).First(p => Api.Str(p.GetProperty("event"), "id") == approx).GetProperty("event").GetProperty("geo");
            Check.Equal(37.22346, s.GetProperty("lat").GetDouble(), "Event staff see exact positions");
        }

        private static async Task AccessAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember member = await SafetyHelpers.MemberAsync(f, ct, "Map Viewer").ConfigureAwait(false);
            TestMember host = await SafetyHelpers.MemberAsync(f, ct, "Map Host").ConfigureAwait(false);
            string hosted = await AtAsync(f, "Hosted meetup", 5.5, 5.5, ct, "Exact", new { hostUserIds = new[] { host.Id } }).ConfigureAwait(false);
            string other = await AtAsync(f, "Other meetup", 5.6, 5.6, ct).ConfigureAwait(false);
            object box = new { south = 5.0, west = 5.0, north = 6.0, east = 6.0 };
            JsonElement before = await MapAsync(f, member.AccessToken, new { bbox = box, zoom = 9 }, ct).ConfigureAwait(false);
            Check.True(PointIds(before).Contains(hosted) && PointIds(before).Contains(other), "Both on the map");
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/users/" + host.Id + "/block", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement after = await MapAsync(f, member.AccessToken, new { bbox = box, zoom = 9 }, ct).ConfigureAwait(false);
            Check.True(!PointIds(after).Contains(hosted) && PointIds(after).Contains(other), "Events hosted only by a blocked member are left off");
            f.Settings.Events.MapPerMinute = 1;
            TestMember limited = await SafetyHelpers.MemberAsync(f, ct, "Map Limited").ConfigureAwait(false);
            await MapAsync(f, limited.AccessToken, new { bbox = box, zoom = 9 }, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, _Map, limited.AccessToken, new { bbox = box, zoom = 9 }, HttpStatusCode.TooManyRequests, "RateLimited", ct).ConfigureAwait(false);
        }

        #endregion
    }
}

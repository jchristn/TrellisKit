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

    /// <summary>
    /// Helpers of the Phase 4 directory suites.
    /// </summary>
    internal static class DirectoryTestKit
    {
        internal const string AdminVenues = "/api/v1/admin/venues";
        internal const string AdminInstructors = "/api/v1/admin/instructors";
        internal const string Settings = "/api/v1/admin/directory/settings";
        internal const string Submissions = "/api/v1/directory/submissions";
        internal const string Review = "/api/v1/admin/directory/submissions";

        internal static void Relax(HttpFixture f)
        {
            ContentTestKit.Relax(f);
            f.Settings.Directory.SubmissionsPerDay = 1000;
            f.Settings.Directory.NewAccountSubmissionsPerDay = 1000;
            f.Settings.Directory.MaxPendingSubmissionsPerMember = 100;
            f.Settings.Directory.GeocodePerMinute = 10000;
            f.Settings.Directory.GeocodePerDay = 100000;
            f.Settings.Directory.MapPerMinute = 10000;
            f.Settings.Directory.TilesPerMinute = 100000;
            f.Settings.RateLimits.MemberReadsPerMinute = 100000;
            f.Settings.RateLimits.PublicFeedPerMinute = 100000;
        }

        internal static async Task<JsonElement> VenueAsync(HttpFixture f, string name, CancellationToken ct, double? lat = null, double? lng = null, object? extra = null, string? bearer = null)
        {
            Dictionary<string, object?> body = new Dictionary<string, object?> { { "name", name } };
            if (lat != null && lng != null) body["geo"] = new { lat, lng };
            if (extra != null)
                foreach (System.Reflection.PropertyInfo p in extra.GetType().GetProperties()) body[p.Name] = p.GetValue(extra);
            return await Api.ExpectAsync(f, HttpMethod.Post, AdminVenues, bearer ?? HttpFixture.AdminToken, body, HttpStatusCode.Created, ct).ConfigureAwait(false);
        }

        internal static async Task<string> VenueIdAsync(HttpFixture f, string name, CancellationToken ct, double? lat = null, double? lng = null, object? extra = null)
        {
            return Api.Str(await VenueAsync(f, name, ct, lat, lng, extra).ConfigureAwait(false), "id");
        }

        internal static async Task<JsonElement> InstructorAsync(HttpFixture f, string name, CancellationToken ct, object? extra = null)
        {
            Dictionary<string, object?> body = new Dictionary<string, object?> { { "displayName", name } };
            if (extra != null)
                foreach (System.Reflection.PropertyInfo p in extra.GetType().GetProperties()) body[p.Name] = p.GetValue(extra);
            return await Api.ExpectAsync(f, HttpMethod.Post, AdminInstructors, HttpFixture.AdminToken, body, HttpStatusCode.Created, ct).ConfigureAwait(false);
        }

        internal static async Task<JsonElement> SettingsAsync(HttpFixture f, object body, CancellationToken ct)
        {
            return await Api.ExpectAsync(f, HttpMethod.Put, Settings, HttpFixture.AdminToken, body, HttpStatusCode.OK, ct).ConfigureAwait(false);
        }

        internal static async Task UseGeocoderAsync(HttpFixture f, string provider, string baseUrl, CancellationToken ct, Dictionary<string, object>? more = null)
        {
            f.Services.Directory.Geocoding.AddressPolicy = new LoopbackTestAddressPolicy();
            Dictionary<string, object> values = new Dictionary<string, object> { { "directory.geocoder.provider", provider }, { "directory.geocoder.baseUrl", baseUrl }, { "directory.geocoder.requestsPerSecond", 50 } };
            if (more != null) foreach (KeyValuePair<string, object> pair in more) values[pair.Key] = pair.Value;
            await SettingsAsync(f, new { values }, ct).ConfigureAwait(false);
        }

        internal static string Nominatim(double lat, double lng, string label)
        {
            return "[{\"display_name\":\"" + label + "\",\"lat\":\"" + lat.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "\",\"lon\":\"" +
                lng.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "\",\"boundingbox\":[\"1\",\"2\",\"3\",\"4\"]}]";
        }

        internal static async Task<TestMember> ModeratorAsync(HttpFixture f, CancellationToken ct, bool membership = true)
        {
            TestMember member = membership ? await Api.MemberAsync(f, ct).ConfigureAwait(false) : await Api.RegisterAsync(f, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/moderators", HttpFixture.AdminToken, new { userId = member.Id, resourceType = "Directory", resourceId = "*" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            f.Services.Tokens.InvalidateUser(member.Id);
            return member;
        }

        internal static List<string> Ids(JsonElement page)
        {
            return page.GetProperty("objects").EnumerateArray().Select(o => Api.Str(o, "id")).ToList();
        }

        internal static JsonElement Find(JsonElement page, string id)
        {
            return page.GetProperty("objects").EnumerateArray().First(o => Api.Str(o, "id") == id);
        }

        internal static Task<long> CountAsync(HttpFixture f, string sql, CancellationToken ct)
        {
            return ContentTestKit.ScalarAsync(f, sql, ct);
        }

        internal static async Task<JsonElement> ListAsync(HttpFixture f, string bearer, object body, CancellationToken ct)
        {
            return await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/venues/enumerate", bearer, body, HttpStatusCode.OK, ct).ConfigureAwait(false);
        }
    }
}

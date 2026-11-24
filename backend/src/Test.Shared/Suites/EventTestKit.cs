namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.PropertyConfig;
    using TrellisKit.Server.Services;

    /// <summary>
    /// Helpers of the Phase 4 event suites.
    /// </summary>
    internal static class EventTestKit
    {
        internal const string Zone = "America/New_York";

        internal static void Relax(HttpFixture f)
        {
            ContentTestKit.Relax(f);
            f.Settings.Events.RsvpsPerMinute = 10000;
            f.Settings.Events.RsvpsPerDay = 100000;
            f.Settings.Events.IcsPerHour = 10000;
            f.Settings.Events.ProposalsPerDay = 100;
            f.Settings.Events.MaxPendingProposalsPerMember = 50;
            f.Settings.RateLimits.PublicFeedPerMinute = 10000;
        }

        internal static void Categories(PropertyConfiguration p)
        {
            p.EventsSection = new PropertyEventsSection
            {
                Categories = new List<PropertyOptionSeed>
                {
                    new PropertyOptionSeed { Key = "meetup", Label = "Meetup", Color = "#1F5FBF" },
                    new PropertyOptionSeed { Key = "workshop", Label = "Workshop", Color = "#2E7D32" }
                }
            };
        }

        internal static async Task<JsonElement> SetAccessAsync(HttpFixture f, object body, CancellationToken ct)
        {
            return await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/property/access", HttpFixture.AdminToken, body, HttpStatusCode.OK, ct).ConfigureAwait(false);
        }

        internal static Task PartiallyGatedAsync(HttpFixture f, CancellationToken ct, params string[] gated)
        {
            return SetAccessAsync(f, new { model = "PartiallyGated", anonymousBrowsing = true, gatedFeatures = gated, defaultAudience = "SignedIn" }, ct);
        }

        internal static async Task<List<string>> IsolationRowsAsync(HttpFixture f, TestMember admin, TestMember member, string marker, CancellationToken ct)
        {
            Relax(f);
            JsonElement category = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/events/categories", admin.AccessToken, new { key = "isolation", label = marker + " category" }, HttpStatusCode.Created, ct).ConfigureAwait(false);
            string single = await PublishedAsync(f, admin.AccessToken, marker + " event", ct, new { labels = new[] { "isolation", marker.ToLowerInvariant() }, capacity = 5 }).ConfigureAwait(false);
            JsonElement rsvp = await RsvpAsync(f, member.AccessToken, single, "Going", ct).ConfigureAwait(false);
            Dictionary<string, object?> body = Body(marker + " series", "2027-03-02T18:00", "2027-03-02T19:00");
            body["recurrence"] = new { frequency = "Weekly", count = 2 };
            JsonElement series = await CreateAsync(f, admin.AccessToken, body, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/me/calendar-feed", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/events/proposals", member.AccessToken, Body(marker + " proposal", Local(30, 10), Local(30, 11)), HttpStatusCode.Created, ct).ConfigureAwait(false);
            return new List<string> { Api.Str(category, "id"), single, Api.Str(rsvp.GetProperty("myRsvp"), "id"), Api.Str(series, "id"), Api.Str(series.GetProperty("recurrence"), "id") };
        }

        internal static async Task<TestMember> ManagerAsync(HttpFixture f, CancellationToken ct, string name = "Event Manager")
        {
            TestMember m = await SafetyHelpers.MemberAsync(f, ct, name).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/moderators", HttpFixture.AdminToken, new { userId = m.Id, resourceType = "Events", resourceId = "*" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            f.Services.Tokens.InvalidateUser(m.Id);
            return m;
        }

        internal static string Local(int daysAhead, int hour, int minute = 0)
        {
            DateTime local = DateTime.UtcNow.Date.AddDays(daysAhead).AddHours(hour).AddMinutes(minute);
            return local.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture);
        }

        internal static Dictionary<string, object?> Body(string title, string startLocal, string endLocal, object? extra = null, string zone = Zone)
        {
            Dictionary<string, object?> body = new Dictionary<string, object?>
            {
                { "title", title },
                { "summary", title + " summary" },
                { "description", "About " + title },
                { "format", "InPerson" },
                { "startLocal", startLocal },
                { "endLocal", endLocal },
                { "timeZone", zone },
                { "location", new { name = "Example Hall", address = new { line1 = "100 Example Street", locality = "Exampleville", region = "EX", postalCode = "00000", countryCode = "US" } } }
            };
            if (extra != null)
            {
                foreach (System.Reflection.PropertyInfo p in extra.GetType().GetProperties()) body[p.Name] = p.GetValue(extra);
            }
            return body;
        }

        internal static async Task<JsonElement> CreateAsync(HttpFixture f, string bearer, object body, CancellationToken ct)
        {
            return await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/events", bearer, body, HttpStatusCode.Created, ct).ConfigureAwait(false);
        }

        internal static async Task<string> PublishedAsync(HttpFixture f, string bearer, string title, CancellationToken ct, object? extra = null, int daysAhead = 10)
        {
            JsonElement created = await CreateAsync(f, bearer, Body(title, Local(daysAhead, 18), Local(daysAhead, 20), extra), ct).ConfigureAwait(false);
            string id = Api.Str(created, "id");
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/events/" + id + "/publish", bearer, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            return id;
        }

        internal static async Task<JsonElement> RsvpAsync(HttpFixture f, string bearer, string eventId, string status, CancellationToken ct, int guests = 0)
        {
            return await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/events/" + eventId + "/rsvp", bearer, new { status, guestCount = guests }, HttpStatusCode.OK, ct).ConfigureAwait(false);
        }

        internal static string MyStatus(JsonElement e)
        {
            JsonElement mine = e.GetProperty("myRsvp");
            return mine.ValueKind == JsonValueKind.Null ? "None" : Api.Str(mine, "status");
        }

        internal static List<string> Ids(JsonElement page)
        {
            return page.GetProperty("objects").EnumerateArray().Select(o => Api.Str(o, "id")).ToList();
        }

        internal static async Task<EventRecord> RecordAsync(HttpFixture f, string id, CancellationToken ct)
        {
            return (await f.Database.Events.Records.ReadAsync(id, ct).ConfigureAwait(false))!;
        }

        internal static Task<long> CountAsync(HttpFixture f, string sql, CancellationToken ct)
        {
            return ContentTestKit.ScalarAsync(f, sql, ct);
        }

        internal static async Task<List<EventRecord>> OccurrencesAsync(HttpFixture f, string recurrenceId, CancellationToken ct)
        {
            return await f.Database.Events.Records.ListOccurrencesAsync(recurrenceId, 5000, ct).ConfigureAwait(false);
        }

        internal static async Task<int> FanoutAsync(HttpFixture f, CancellationToken ct)
        {
            return await ForumTestKit.FanoutAsync(f, ct).ConfigureAwait(false);
        }

        internal static async Task<List<Notification>> NotificationsAsync(HttpFixture f, string userId, NotificationTypeEnum type, CancellationToken ct)
        {
            return await ForumTestKit.NotificationsAsync(f, userId, type, ct).ConfigureAwait(false);
        }

        internal static void Clock(HttpFixture f, DateTime nowUtc)
        {
            DateTime fixedNow = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc);
            f.Services.Events.SetClock(() => fixedNow);
        }

        internal static async Task<List<TestMember>> MembersAsync(HttpFixture f, int count, CancellationToken ct, string prefix = "Attendee")
        {
            List<TestMember> members = new List<TestMember>();
            for (int i = 0; i < count; i++) members.Add(await SafetyHelpers.MemberAsync(f, ct, prefix + " " + i.ToString(CultureInfo.InvariantCulture)).ConfigureAwait(false));
            return members;
        }
    }
}

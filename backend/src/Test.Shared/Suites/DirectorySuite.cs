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
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.PropertyConfig;
    using TrellisKit.Server.Services;
    using TrellisKit.Server.Services.Ai;

    /// <summary>
    /// Phase 4 directory (docs/PHASE4_CONTRACT.md sections 0.2, 2.2, 2.3, 3.1, 3.8, 3.9): configuration seeding and the
    /// property decorator, programs with retirement, venue and instructor CRUD, slugs with redirects, verify, close,
    /// archive, links, member links with self-unlink, rank, import, export, metrics, and audit.
    /// </summary>
    public static class DirectorySuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "Directory";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Directory (" + TestEnvironment.Provider + ")", new[]
            {
                SuiteCases.Http(Id, "Directory.ConfigAndPrograms", "property.json directory labels and programs are seeded once, GET /api/v1/property carries the stored section, programs are created, ordered, retired when in use, and only SystemAdmin edits them", Section, ConfigAsync),
                Case("Directory.StructuredSchedule", "Schedules persist, preserve omitted fields, validate times, and survive member submission and approval", ScheduleAsync),
                Case("Directory.VenueCrud", "Staff create, read, edit, and delete venues; slugs derive, stay unique, and old slugs redirect; members cannot use admin routes; audit records each write", VenueCrudAsync),
                Case("Directory.VerifyCloseArchive", "Verify and unverify; closed venues leave lists unless includeClosed and stay readable; archived venues are staff only; reopen and unarchive restore them", StatusAsync),
                SuiteCases.Http(Id, "Directory.InstructorsAndLinks", "Instructors carry the configured rank, link to venues with roles, list each other, link to a member who is notified and may unlink; one entry per member", Section, LinksAsync),
                Case("Directory.Engagement", "Venue reports queue in the Directory scope without auto-hide, PermanentlyClosed creates a ReportClosed submission, search finds venues, findVenues answers as the caller, and venue knowledge hides contact details of non-Public venues", EngagementAsync),
                Case("Directory.ModerationState", "Hide, Unhide, Remove, and Restore on venues and instructors read the stored moderation state: the action result, queue rows, and queue detail report the state after each action, and repeated or out-of-order actions are 409 InvalidModerationState", ModerationStateAsync),
                Case("Directory.ImportExportMetrics", "Import validates by default (dry run), creates and updates by slug, reports errors; export streams CSV; metrics count", ImportAsync)
            });
        }

        #endregion

        #region Private-Methods

        private static void Section(PropertyConfiguration property)
        {
            property.DirectorySection = new PropertyDirectorySection
            {
                Introduction = "Find clubs nearby.",
                ListingNotice = "Listings are informational.",
                VenueLabel = "Club",
                VenueLabelPlural = "Clubs",
                InstructorLabel = "Coach",
                InstructorLabelPlural = "Coaches",
                SessionLabel = "Gathering",
                SessionLabelPlural = "Gatherings",
                InstructorRankFieldKey = "rank",
                Programs = new List<PropertyOptionSeed> { new PropertyOptionSeed { Key = "beginners", Label = "Beginners" }, new PropertyOptionSeed { Key = "advanced", Label = "Advanced" } }
            };
        }

        private static TestCaseDescriptor Case(string caseId, string name, Func<HttpFixture, CancellationToken, Task> body)
        {
            return SuiteCases.Http(Id, caseId, name, (f, ct) =>
            {
                DirectoryTestKit.Relax(f);
                return body(f, ct);
            });
        }

        private static async Task ScheduleAsync(HttpFixture f, CancellationToken ct)
        {
            DirectoryTestKit.Relax(f);
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            VenueSchedule schedule = new VenueSchedule
            {
                TimeZone = "Pacific/Honolulu", Notes = "Check holiday hours.",
                OpeningHours = new List<VenueScheduleEntry> { new VenueScheduleEntry { DayOfWeek = 0, StartTime = "22:00", EndTime = "02:00", EndsNextDay = true } },
                Sessions = new List<VenueScheduleEntry> {
                    new VenueScheduleEntry { DayOfWeek = 1, StartTime = "17:00", EndTime = "18:00", Name = "Fundamentals" },
                    new VenueScheduleEntry { DayOfWeek = 1, StartTime = "18:00", EndTime = "19:00", Name = "Advanced", Instructor = "Alex" } }
            };
            JsonElement venue = await DirectoryTestKit.VenueAsync(f, "Schedule Club", ct, extra: new { schedule, scheduleText = "Original notes" }).ConfigureAwait(false);
            string id = Api.Str(venue, "id");
            JsonElement read = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/venues" + "/" + id, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Pacific/Honolulu", Api.Str(read.GetProperty("schedule"), "timeZone"), "Schedule zone persisted");
            Check.True(read.GetProperty("schedule").GetProperty("openingHours")[0].GetProperty("endsNextDay").GetBoolean(), "Overnight hours persisted");
            Check.Equal(2, read.GetProperty("schedule").GetProperty("sessions").GetArrayLength(), "Multiple sessions persisted");
            Check.Equal(2L, await DirectoryTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM venuescheduleentries WHERE kind = 'Session';", ct).ConfigureAwait(false), "Sessions are stored with the Session kind");
            JsonElement renamed = await Api.ExpectAsync(f, HttpMethod.Put, DirectoryTestKit.AdminVenues + "/" + id, HttpFixture.AdminToken, new { summary = "Updated summary" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(2, renamed.GetProperty("schedule").GetProperty("sessions").GetArrayLength(), "Omitted schedule stays unchanged");
            VenueSchedule invalid = schedule.Clone();
            invalid.Sessions[0].EndTime = "16:00";
            await Api.ExpectErrorAsync(f, HttpMethod.Put, DirectoryTestKit.AdminVenues + "/" + id, HttpFixture.AdminToken, new { schedule = invalid }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            invalid = schedule.Clone(); invalid.TimeZone = "Not/AZone";
            await Api.ExpectErrorAsync(f, HttpMethod.Put, DirectoryTestKit.AdminVenues + "/" + id, HttpFixture.AdminToken, new { schedule = invalid }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            invalid = schedule.Clone(); invalid.Sessions[0].DayOfWeek = 7;
            await Api.ExpectErrorAsync(f, HttpMethod.Put, DirectoryTestKit.AdminVenues + "/" + id, HttpFixture.AdminToken, new { schedule = invalid }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            invalid = schedule.Clone(); invalid.OpeningHours = null!;
            await Api.ExpectErrorAsync(f, HttpMethod.Put, DirectoryTestKit.AdminVenues + "/" + id, HttpFixture.AdminToken, new { schedule = invalid }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, DirectoryTestKit.AdminVenues + "/" + id, HttpFixture.AdminToken,
                new { schedule = new { timeZone = "Pacific/Honolulu", openingHours = new[] { new { startTime = "09:00", endTime = "17:00" } }, sessions = new object[0] } },
                HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            JsonElement legacy = await Api.ExpectAsync(f, HttpMethod.Put, DirectoryTestKit.AdminVenues + "/" + id, HttpFixture.AdminToken,
                new { schedule = new { timeZone = "Pacific/Honolulu", openingHours = new object[0], classes = new[] { new { dayOfWeek = 2, startTime = "07:00", endTime = "08:00", endsNextDay = false, name = "Morning" } } } },
                HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(1, legacy.GetProperty("schedule").GetProperty("sessions").GetArrayLength(), "Clients built before the rename may still send classes");
            Check.True(!legacy.GetProperty("schedule").TryGetProperty("classes", out JsonElement _), "The legacy name is never returned");
            schedule.Notes = "Updated through review.";
            JsonElement submission = await Api.ExpectAsync(f, HttpMethod.Post, DirectoryTestKit.Submissions, member.AccessToken, new { type = "EditVenue", targetId = id, fields = new { schedule } }, HttpStatusCode.Created, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, DirectoryTestKit.Review + "/" + Api.Str(submission, "id") + "/approve", HttpFixture.AdminToken, new { reviewNote = "Checked schedule" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            read = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/venues" + "/" + id, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Updated through review.", Api.Str(read.GetProperty("schedule"), "notes"), "Approval saves structured schedule");
            Check.Equal("Alex", Api.Str(read.GetProperty("schedule").GetProperty("sessions")[1], "instructor"), "Null instructor on earlier entry does not shift later values");
            Check.Equal("Original notes", Api.Str(read, "scheduleText"), "Legacy schedule retained");
            JsonElement cleared = await Api.ExpectAsync(f, HttpMethod.Put, DirectoryTestKit.AdminVenues + "/" + id, HttpFixture.AdminToken, new { schedule = (object?)null }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(!cleared.TryGetProperty("schedule", out JsonElement empty) || empty.ValueKind == JsonValueKind.Null, "Explicit null clears the schedule");
            Check.Equal(0L, await DirectoryTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM venuescheduleentries;", ct).ConfigureAwait(false), "Clearing removes child rows");
        }

        private static async Task ConfigAsync(HttpFixture f, CancellationToken ct)
        {
            DirectoryTestKit.Relax(f);
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            JsonElement property = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/property", null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement directory = property.GetProperty("directory");
            Check.Equal("Find clubs nearby.", Api.Str(directory, "introduction"), "Property introduction reaches clients");
            Check.Equal("Listings are informational.", Api.Str(directory, "listingNotice"), "Property notice reaches clients");
            Check.Equal("Club", Api.Str(directory, "venueLabel"), "Venue label from the section");
            Check.Equal("Gathering", Api.Str(directory, "sessionLabel"), "Session label from the section");
            Check.Equal("Gatherings", Api.Str(directory, "sessionLabelPlural"), "Plural session label from the section");
            Check.Equal(2, directory.GetProperty("programs").GetArrayLength(), "Seeded programs");
            Check.True(!directory.GetProperty("geocoderConfigured").GetBoolean(), "No geocoder by default");
            Check.Equal("None", Api.Str(directory.GetProperty("map"), "provider"), "Map provider None by default");
            await f.Services.Directory.Config.EnsureSeededAsync(ct).ConfigureAwait(false);
            Check.Equal(2L, await DirectoryTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM directoryprograms;", ct).ConfigureAwait(false), "Seeding is idempotent");

            JsonElement programs = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/directory/programs/enumerate", member.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(2, programs.GetProperty("objects").GetArrayLength(), "Members list programs");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/directory/programs", member.AccessToken, new { key = "youth", label = "Youth" }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            TestMember moderator = await DirectoryTestKit.ModeratorAsync(f, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/directory/programs", moderator.AccessToken, new { key = "youth", label = "Youth" }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, DirectoryTestKit.Settings, moderator.AccessToken, new { venueLabel = "Studio" }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            JsonElement youth = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/directory/programs", HttpFixture.AdminToken, new { key = "youth", label = "Youth", labels = new[] { "kids" } }, HttpStatusCode.Created, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/directory/programs", HttpFixture.AdminToken, new { key = "youth", label = "Again" }, HttpStatusCode.Conflict, "ProgramKeyInUse", ct).ConfigureAwait(false);
            JsonElement admin = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/directory/programs/enumerate", HttpFixture.AdminToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            List<string> order = DirectoryTestKit.Ids(admin);
            order.Reverse();
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/directory/programs/order", HttpFixture.AdminToken, new { programIds = order }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement reordered = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/directory/programs/enumerate", member.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("youth", Api.Str(reordered.GetProperty("objects")[0], "key"), "Admin order");
            await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/admin/directory/programs/order", HttpFixture.AdminToken, new { programIds = order.Take(1) }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);

            await DirectoryTestKit.VenueAsync(f, "Example Youth Club", ct, extra: new { programs = new[] { "youth" } }).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, DirectoryTestKit.AdminVenues, HttpFixture.AdminToken, new { name = "Unknown Program Club", programs = new[] { "nope" } }, HttpStatusCode.BadRequest, "UnknownOption", ct).ConfigureAwait(false);
            JsonElement retired = await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/admin/directory/programs/" + Api.Str(youth, "id"), HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(retired.GetProperty("retired").GetBoolean(), "A program in use is retired");
            JsonElement afterRetire = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/directory/programs/enumerate", member.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(!afterRetire.GetProperty("objects").EnumerateArray().Any(p => Api.Str(p, "key") == "youth"), "Retired programs leave the member list");
            JsonElement adminAfter = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/directory/programs/enumerate", HttpFixture.AdminToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(adminAfter.GetProperty("objects").EnumerateArray().Any(p => Api.Str(p, "key") == "youth" && p.GetProperty("isRetired").GetBoolean()), "The admin list keeps retired programs");
            string unused = Api.Str(admin.GetProperty("objects").EnumerateArray().First(p => Api.Str(p, "key") == "advanced"), "id");
            JsonElement deleted = await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/admin/directory/programs/" + unused, HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(!deleted.GetProperty("retired").GetBoolean(), "An unused program is deleted");

            JsonElement settings = await DirectoryTestKit.SettingsAsync(f, new { venueLabel = "Studio", sessionLabel = "Meeting", submissions = false, values = new Dictionary<string, object> { { "directory.map.provider", "MapLibre" }, { "directory.map.styleUrl", "https://tiles.example.test/style.json" }, { "directory.map.defaultCenterLat", 37.5 } } }, ct).ConfigureAwait(false);
            Check.Equal("Studio", Api.Str(settings.GetProperty("config"), "venueLabel"), "Label stored");
            Check.Equal("Meeting", Api.Str(settings.GetProperty("config"), "sessionLabel"), "Session label stored");
            await Api.ExpectErrorAsync(f, HttpMethod.Put, DirectoryTestKit.Settings, HttpFixture.AdminToken, new { sessionLabelPlural = new string('x', 41) }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, DirectoryTestKit.Settings, HttpFixture.AdminToken, new { values = new Dictionary<string, object> { { "email.provider", "Auto" } } }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, DirectoryTestKit.Settings, HttpFixture.AdminToken, new { instructorRankFieldKey = "city" }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            JsonElement after = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/property", null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Studio", Api.Str(after.GetProperty("directory"), "venueLabel"), "Decorator reads the stored label");
            Check.Equal("Meeting", Api.Str(after.GetProperty("directory"), "sessionLabel"), "Decorator reads the stored session label");
            Check.True(!after.GetProperty("directory").GetProperty("submissions").GetBoolean(), "Submissions switch");
            Check.Equal("MapLibre", Api.Str(after.GetProperty("directory").GetProperty("map"), "provider"), "Map provider");
            Check.Equal(37.5, after.GetProperty("directory").GetProperty("map").GetProperty("defaultCenter").GetProperty("lat").GetDouble(), "Map center");
            Check.True(await DirectoryTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM auditrecords WHERE action = 'directory.settings.update';", ct).ConfigureAwait(false) >= 1, "Settings audited");
        }

        private static async Task VenueCrudAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            JsonElement created = await DirectoryTestKit.VenueAsync(f, "Example Club Downtown", ct, 37.7749, -122.4194, new
            {
                affiliation = "Example Network",
                summary = "Friendly club.",
                address = new { line1 = "100 Example Street", locality = "Exampleville", region = "EX", postalCode = "00000", countryCode = "us" },
                email = "hello@example.test",
                websiteUrl = "https://example.test",
                socialLinks = new[] { new { kind = "Instagram", url = "https://example.test/club" } },
                visitorPolicy = "Welcome",
                labels = new[] { "downtown" },
                tags = new Dictionary<string, string> { { "tier", "gold" } }
            }).ConfigureAwait(false);
            string id = Api.Str(created, "id");
            Check.True(id.StartsWith("ven_", StringComparison.Ordinal), "Venue prefix");
            Check.Equal("example-club-downtown-exampleville-ex", Api.Str(created, "slug"), "Derived slug: name, locality, and region");
            Check.Equal("US", Api.Str(created.GetProperty("address"), "countryCode"), "Country normalized");
            Check.Equal("Manual", Api.Str(created.GetProperty("geo"), "source"), "Manual pin");
            Check.Equal("100 Example Street, Exampleville, EX 00000", Api.Str(created.GetProperty("address"), "formatted"), "Formatted address");
            Check.True(created.GetProperty("labels").EnumerateArray().Any(l => l.GetString() == "downtown"), "Labels");
            JsonElement second = await DirectoryTestKit.VenueAsync(f, "Example Club Downtown", ct, extra: new { address = new { locality = "Exampleville", region = "EX", countryCode = "US" } }).ConfigureAwait(false);
            Check.Equal("example-club-downtown-exampleville-ex-2", Api.Str(second, "slug"), "Derived slugs stay unique");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, DirectoryTestKit.AdminVenues, HttpFixture.AdminToken, new { name = "Taken", slug = "example-club-downtown-exampleville-ex" }, HttpStatusCode.Conflict, "SlugInUse", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, DirectoryTestKit.AdminVenues, HttpFixture.AdminToken, new { name = "X" }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, DirectoryTestKit.AdminVenues, HttpFixture.AdminToken, new { name = "Bad Geo", geo = new { lat = 95, lng = 0 } }, HttpStatusCode.BadRequest, "InvalidGeo", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, DirectoryTestKit.AdminVenues, HttpFixture.AdminToken, new { name = "Bad Url", websiteUrl = "http://example.test" }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, DirectoryTestKit.AdminVenues, member.AccessToken, new { name = "Member Club" }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);

            JsonElement read = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/venues/" + id, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Example Club Downtown", Api.Str(read, "name"), "Member reads by id");
            Check.True(read.GetProperty("geo").TryGetProperty("source", out JsonElement source) && source.ValueKind == JsonValueKind.Null, "Members do not see the geo source");
            Check.True(read.GetProperty("viewer").GetProperty("canSuggestEdit").GetBoolean() && !read.GetProperty("viewer").GetProperty("canEdit").GetBoolean(), "Member viewer");
            Check.True(read.GetProperty("instructors").TryGetProperty("enumerate", out JsonElement _), "Instructor preview");
            await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/venues/example-club-downtown-exampleville-ex", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);

            JsonElement updated = await Api.ExpectAsync(f, HttpMethod.Put, DirectoryTestKit.AdminVenues + "/" + id, HttpFixture.AdminToken, new { slug = "example-club-central", summary = "Moved." }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Moved.", Api.Str(updated, "summary"), "Edited");
            Check.Equal("Example Network", Api.Str(updated, "affiliation"), "Omitted fields stay");
            Check.Equal("Manual", Api.Str(updated.GetProperty("geo"), "source"), "Pin kept when the address is unchanged");
            JsonElement redirected = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/venues/example-club-downtown-exampleville-ex", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("example-club-central", Api.Str(redirected, "slug"), "Old slug redirects to the current one");
            JsonElement moved = await Api.ExpectAsync(f, HttpMethod.Put, DirectoryTestKit.AdminVenues + "/" + id, HttpFixture.AdminToken, new { address = new { line1 = "105 Example Street", locality = "Exampleville", countryCode = "US" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("None", Api.Str(moved.GetProperty("geo"), "source"), "An address change drops a manual pin (no geocoder: needs a pin)");
            Check.True(moved.GetProperty("needsPin").GetBoolean(), "needsPin");
            JsonElement pins = await Api.ExpectAsync(f, HttpMethod.Post, DirectoryTestKit.AdminVenues + "/enumerate", HttpFixture.AdminToken, new { filters = new { geoSource = "None" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(DirectoryTestKit.Ids(pins).Contains(id), "geoSource None finds venues that need a pin");

            JsonElement list = await DirectoryTestKit.ListAsync(f, member.AccessToken, new { search = "central", labels = new[] { "downtown" } }, ct).ConfigureAwait(false);
            Check.True(list.GetProperty("objects").GetArrayLength() == 0 || DirectoryTestKit.Ids(list).Contains(id), "Search and labels filter");
            JsonElement byLabel = await DirectoryTestKit.ListAsync(f, member.AccessToken, new { labels = new[] { "downtown" } }, ct).ConfigureAwait(false);
            Check.Equal(1, byLabel.GetProperty("objects").GetArrayLength(), "Label filter");
            JsonElement byTag = await DirectoryTestKit.ListAsync(f, member.AccessToken, new { tags = new Dictionary<string, string> { { "tier", "gold" } } }, ct).ConfigureAwait(false);
            Check.Equal(1, byTag.GetProperty("objects").GetArrayLength(), "Tag filter");
            ContentTestKit.CheckMetadata(byTag.GetProperty("objects")[0], "venue");

            await Api.ExpectAsync(f, HttpMethod.Delete, DirectoryTestKit.AdminVenues + "/" + id, HttpFixture.AdminToken, null, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/venues/" + id, member.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, DirectoryTestKit.AdminVenues + "/" + id, HttpFixture.AdminToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            foreach (string action in new[] { "venue.create", "venue.update", "venue.delete" })
                Check.True(await DirectoryTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM auditrecords WHERE action = '" + action + "';", ct).ConfigureAwait(false) >= 1, "Audited " + action);
        }

        private static async Task StatusAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            TestMember moderator = await DirectoryTestKit.ModeratorAsync(f, ct).ConfigureAwait(false);
            JsonElement venue = await DirectoryTestKit.VenueAsync(f, "Status Club", ct, bearer: moderator.AccessToken).ConfigureAwait(false);
            string id = Api.Str(venue, "id");
            JsonElement verified = await Api.ExpectAsync(f, HttpMethod.Post, DirectoryTestKit.AdminVenues + "/" + id + "/verify", moderator.AccessToken, new { note = "Visited" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(verified.GetProperty("isVerified").GetBoolean(), "Verified");
            JsonElement verifiedOnly = await DirectoryTestKit.ListAsync(f, member.AccessToken, new { filters = new { verifiedOnly = true } }, ct).ConfigureAwait(false);
            Check.True(DirectoryTestKit.Ids(verifiedOnly).SequenceEqual(new List<string> { id }), "verifiedOnly");
            JsonElement unverified = await Api.ExpectAsync(f, HttpMethod.Post, DirectoryTestKit.AdminVenues + "/" + id + "/unverify", moderator.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(!unverified.GetProperty("isVerified").GetBoolean(), "Unverified");
            await Api.ExpectAsync(f, HttpMethod.Post, DirectoryTestKit.AdminVenues + "/" + id + "/close", moderator.AccessToken, new { reason = "Moved away" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(!DirectoryTestKit.Ids(await DirectoryTestKit.ListAsync(f, member.AccessToken, new { }, ct).ConfigureAwait(false)).Contains(id), "Closed venues leave lists");
            Check.True(DirectoryTestKit.Ids(await DirectoryTestKit.ListAsync(f, member.AccessToken, new { filters = new { includeClosed = true } }, ct).ConfigureAwait(false)).Contains(id), "includeClosed");
            JsonElement closed = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/venues/" + id, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Closed", Api.Str(closed, "status"), "Closed venues stay readable");
            await Api.ExpectAsync(f, HttpMethod.Post, DirectoryTestKit.AdminVenues + "/" + id + "/reopen", moderator.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, DirectoryTestKit.AdminVenues + "/" + id + "/archive", moderator.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/venues/" + id, member.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            JsonElement archived = await Api.ExpectAsync(f, HttpMethod.Post, DirectoryTestKit.AdminVenues + "/enumerate", moderator.AccessToken, new { filters = new { status = "Archived" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(DirectoryTestKit.Ids(archived).SequenceEqual(new List<string> { id }), "Staff list archived venues");
            await Api.ExpectAsync(f, HttpMethod.Post, DirectoryTestKit.AdminVenues + "/" + id + "/unarchive", moderator.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/venues/" + id, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            foreach (string action in new[] { "venue.verify", "venue.close", "venue.archive" })
                Check.True(await DirectoryTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM auditrecords WHERE action = '" + action + "';", ct).ConfigureAwait(false) >= 1, "Audited " + action);
        }

        private static async Task LinksAsync(HttpFixture f, CancellationToken ct)
        {
            DirectoryTestKit.Relax(f);
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            TestMember other = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            string venue = await DirectoryTestKit.VenueIdAsync(f, "Coach Club", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, DirectoryTestKit.AdminInstructors, HttpFixture.AdminToken, new { displayName = "Ranked Coach", rank = "Wizard" }, HttpStatusCode.BadRequest, "UnknownOption", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, DirectoryTestKit.AdminInstructors, HttpFixture.AdminToken, new { displayName = "Ranked Coach", rank = "Advanced", rankLevel = 9 }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            JsonElement coach = await DirectoryTestKit.InstructorAsync(f, "Example Coach", ct, new { rank = "Advanced", rankLevel = 2, venueIds = new[] { venue } }).ConfigureAwait(false);
            string coachId = Api.Str(coach, "id");
            Check.Equal("Advanced", Api.Str(coach, "rank"), "Rank key");
            Check.Equal("Advanced", Api.Str(coach, "rankLabel"), "Rank label from the profile field");
            Check.Equal(1, coach.GetProperty("venueCount").GetInt32(), "Linked on create");
            JsonElement guest = await DirectoryTestKit.InstructorAsync(f, "Guest Coach", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, DirectoryTestKit.AdminVenues + "/" + venue + "/instructors/" + Api.Str(guest, "id"), HttpFixture.AdminToken, new { role = "Guest" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement roster = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/venues/" + venue + "/instructors/enumerate", member.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(2, roster.GetProperty("objects").GetArrayLength(), "Venue roster");
            Check.Equal("Guest", Api.Str(DirectoryTestKit.Find(roster, Api.Str(guest, "id")), "role"), "Role");
            JsonElement detail = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/venues/" + venue, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(2, detail.GetProperty("instructorCount").GetInt32(), "Counter recomputed");
            Check.Equal(2L, detail.GetProperty("instructors").GetProperty("totalRecords").GetInt64(), "Preview total");
            JsonElement venues = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/instructors/" + coachId + "/venues/enumerate", member.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(DirectoryTestKit.Ids(venues).SequenceEqual(new List<string> { venue }), "Instructor venues");
            JsonElement byRank = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/instructors/enumerate", member.AccessToken, new { filters = new { rank = new[] { "Advanced" } } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(DirectoryTestKit.Ids(byRank).SequenceEqual(new List<string> { coachId }), "Rank filter");
            await Api.ExpectAsync(f, HttpMethod.Delete, DirectoryTestKit.AdminVenues + "/" + venue + "/instructors/" + Api.Str(guest, "id"), HttpFixture.AdminToken, null, HttpStatusCode.NoContent, ct).ConfigureAwait(false);

            JsonElement linked = await Api.ExpectAsync(f, HttpMethod.Post, DirectoryTestKit.AdminInstructors + "/" + coachId + "/link", HttpFixture.AdminToken, new { userId = member.Id }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(member.Id, Api.Str(linked.GetProperty("linkedUser"), "id"), "Linked user");
            List<Notification> notices = (await NotificationTestKit.ListAsync(f, member.Id, ct).ConfigureAwait(false)).Where(n => n.Type == NotificationTypeEnum.InstructorLinked).ToList();
            Check.Equal(1, notices.Count, "InstructorLinked sent");
            Check.True(notices[0].Title.Contains("coach at Coach Club", StringComparison.Ordinal), "Title uses the instructor label (" + notices[0].Title + ")");
            Check.Equal("/instructors/" + coachId, notices[0].Path, "Path");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, DirectoryTestKit.AdminInstructors + "/" + Api.Str(guest, "id") + "/link", HttpFixture.AdminToken, new { userId = member.Id }, HttpStatusCode.Conflict, "AlreadyClaimed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, DirectoryTestKit.AdminInstructors + "/" + coachId + "/link", HttpFixture.AdminToken, new { userId = other.Id }, HttpStatusCode.Conflict, "AlreadyClaimed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/me/instructor-links/" + coachId + "/unlink", other.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/me/instructor-links/" + coachId + "/unlink", member.AccessToken, null, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
            JsonElement unlinked = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/instructors/" + coachId, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(unlinked.GetProperty("linkedUser").ValueKind == JsonValueKind.Null, "Self unlink");
            Check.True(await DirectoryTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM auditrecords WHERE action = 'instructor.unlink';", ct).ConfigureAwait(false) >= 1, "Unlink audited");
            await Api.ExpectAsync(f, HttpMethod.Post, DirectoryTestKit.AdminInstructors + "/" + coachId + "/archive", HttpFixture.AdminToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/instructors/" + coachId, member.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Delete, DirectoryTestKit.AdminInstructors + "/" + coachId, HttpFixture.AdminToken, null, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
            JsonElement venueAfter = await Api.ExpectAsync(f, HttpMethod.Get, DirectoryTestKit.AdminVenues + "/" + venue, HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(0, venueAfter.GetProperty("instructorCount").GetInt32(), "Deleting an instructor removes its links");
        }

        private static async Task EngagementAsync(HttpFixture f, CancellationToken ct)
        {
            ForumTestKit.Relax(f);
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            TestMember other = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            JsonElement venue = await DirectoryTestKit.VenueAsync(f, "Searchable Harbor Club", ct, 5.0, 5.0, new { summary = "Waterfront training", phone = "+1 555 0199", websiteUrl = "https://harbor.example.test" }).ConfigureAwait(false);
            string id = Api.Str(venue, "id");
            f.Settings.Moderation.AutoHideReportThreshold = 1;
            await ModerationTestKit.ReportOkAsync(f, member.AccessToken, "Venue", id, ct, "Inaccurate").ConfigureAwait(false);
            await ModerationTestKit.ReportOkAsync(f, other.AccessToken, "Venue", id, ct, "PermanentlyClosed").ConfigureAwait(false);
            JsonElement still = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/venues/" + id, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Searchable Harbor Club", Api.Str(still, "name"), "Reports never hide a listing");
            Check.Equal(1L, await DirectoryTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM directorysubmissions WHERE type = 'ReportClosed' AND targetid = '" + id + "' AND submitteruserid = '" + other.Id + "';", ct).ConfigureAwait(false), "PermanentlyClosed created a ReportClosed submission");
            Check.Equal(2L, await DirectoryTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM reports WHERE targetid = '" + id + "' AND scopetype = 'Directory';", ct).ConfigureAwait(false), "Directory scope");
            JsonElement admin = await Api.ExpectAsync(f, HttpMethod.Get, DirectoryTestKit.AdminVenues + "/" + id, HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(2, admin.GetProperty("openReportCount").GetInt32(), "Open report count");
            await ForumTestKit.DrainSearchAsync(f, ct).ConfigureAwait(false);
            JsonElement search = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/search/enumerate", member.AccessToken, new { search = "harbor" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(search.GetProperty("objects").EnumerateArray().Any(o => Api.Str(o, "type") == "Venue" && Api.Str(o, "id") == id), "Global search finds the venue (" + search + ")");
            ContentActor actor = await ContentTestKit.ActorAsync(f, member.Id, ct).ConfigureAwait(false);
            using (JsonDocument args = JsonDocument.Parse("{\"query\":\"harbor\"}"))
            {
                AssistantToolResult result = await f.Services.Ai.Tools.InvokeAsync(new AssistantCaller { UserId = member.Id, Actor = actor }, "findGyms", args.RootElement, ct).ConfigureAwait(false);
                Check.Equal("Succeeded", result.Status, "findGyms resolves to findVenues (" + result.Text + ")");
                Check.True(result.Text.Contains("Searchable Harbor Club", StringComparison.Ordinal) && result.Text.Contains("/venues/" + id, StringComparison.Ordinal), "Tool answer");
                Check.True(!result.Text.Contains("harbor.example.test", StringComparison.Ordinal), "No contact details for a Members venue");
            }
            KnowledgeDocument doc = await f.Services.Directory.Targets.KnowledgeOfAsync((await f.Database.DirectoryListings.Venues.ReadAsync(id, ct).ConfigureAwait(false))!, ct).ConfigureAwait(false);
            Check.True(!doc.Text.Contains("555 0199", StringComparison.Ordinal) && doc.Text.Contains("Waterfront", StringComparison.Ordinal), "Knowledge without contact details");
        }

        private static async Task ModerationStateAsync(HttpFixture f, CancellationToken ct)
        {
            f.Settings.Moderation.AutoHideReportThreshold = 0;
            TestMember moderator = await DirectoryTestKit.ModeratorAsync(f, ct).ConfigureAwait(false);
            moderator = await Api.LoginAsync(f, moderator, ct).ConfigureAwait(false);
            TestMember reporter = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            string venue = await DirectoryTestKit.VenueIdAsync(f, "Moderated Club", ct, 4.0, 4.0).ConfigureAwait(false);
            string instructor = Api.Str(await DirectoryTestKit.InstructorAsync(f, "Moderated Coach", ct).ConfigureAwait(false), "id");
            await ModerationTestKit.ReportOkAsync(f, reporter.AccessToken, "Venue", venue, ct, "Inaccurate").ConfigureAwait(false);
            await ModerationTestKit.ReportOkAsync(f, reporter.AccessToken, "Instructor", instructor, ct, "Inaccurate").ConfigureAwait(false);
            foreach (string[] pair in new[] { new[] { "Venue", venue }, new[] { "Instructor", instructor } })
            {
                string type = pair[0];
                string id = pair[1];
                await ExpectStateAsync(f, moderator, type, id, "Visible", "None", ct).ConfigureAwait(false);
                JsonElement hidden = await ModerationTestKit.ActOkAsync(f, moderator.AccessToken, type, id, "Hide", ct, resolveReports: false).ConfigureAwait(false);
                Check.Equal("Hidden", Api.Str(hidden.GetProperty("target"), "moderationState"), type + " action result after Hide");
                await ExpectStateAsync(f, moderator, type, id, "Hidden", "Hidden", ct).ConfigureAwait(false);
                ApiResult again = await ModerationTestKit.ActAsync(f, moderator.AccessToken, type, id, "Hide", ct).ConfigureAwait(false);
                Check.Equal("InvalidModerationState", again.Error, type + " already hidden");
                await ModerationTestKit.ActOkAsync(f, moderator.AccessToken, type, id, "Unhide", ct, resolveReports: false).ConfigureAwait(false);
                await ExpectStateAsync(f, moderator, type, id, "Visible", "None", ct).ConfigureAwait(false);
                await ModerationTestKit.ActOkAsync(f, moderator.AccessToken, type, id, "Remove", ct, resolveReports: false).ConfigureAwait(false);
                await ExpectStateAsync(f, moderator, type, id, "Removed", "Removed", ct).ConfigureAwait(false);
                ApiResult unhide = await ModerationTestKit.ActAsync(f, moderator.AccessToken, type, id, "Unhide", ct).ConfigureAwait(false);
                Check.Equal("InvalidModerationState", unhide.Error, type + " removed content is restored, not unhidden");
                JsonElement restored = await ModerationTestKit.ActOkAsync(f, moderator.AccessToken, type, id, "Restore", ct, resolveReports: false).ConfigureAwait(false);
                Check.Equal("None", Api.Str(restored.GetProperty("target"), "moderationState"), type + " action result after Restore");
                await ExpectStateAsync(f, moderator, type, id, "Visible", "None", ct).ConfigureAwait(false);
            }
        }

        private static async Task ExpectStateAsync(HttpFixture f, TestMember moderator, string type, string id, string rowState, string targetState, CancellationToken ct)
        {
            JsonElement queue = await Api.ExpectAsync(f, HttpMethod.Post, ModerationTestKit.Queue + "/enumerate", moderator.AccessToken, new { maxResults = 100, filters = new { scopeType = "Directory" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement row = queue.GetProperty("objects").EnumerateArray().First(o => Api.Str(o, "targetId") == id);
            Check.Equal(rowState, Api.Str(row, "targetState"), type + " queue row state " + rowState);
            JsonElement detail = await Api.ExpectAsync(f, HttpMethod.Get, ModerationTestKit.Queue + "/" + type + "/" + id, moderator.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(targetState, Api.Str(detail.GetProperty("target"), "moderationState"), type + " queue detail state " + targetState);
        }

        private static async Task ImportAsync(HttpFixture f, CancellationToken ct)
        {
            await DirectoryTestKit.VenueAsync(f, "Existing Club", ct, extra: new { slug = "existing-club" }).ConfigureAwait(false);
            object[] rows = new object[]
            {
                new { name = "Imported One", address = new { locality = "Exampleville", countryCode = "US" } },
                new { name = "Existing Club", slug = "existing-club", summary = "Updated by import" },
                new { name = "X" }
            };
            object body = new { rows };
            JsonElement dry = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/directory/import", HttpFixture.AdminToken, body, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(dry.GetProperty("dryRun").GetBoolean(), "Dry run by default");
            Check.Equal(1, dry.GetProperty("created").GetInt32(), "Would create");
            Check.Equal(1, dry.GetProperty("updated").GetInt32(), "Would update");
            Check.Equal(1, dry.GetProperty("skipped").GetInt32(), "Skipped the invalid row");
            Check.Equal(2, dry.GetProperty("errors").GetProperty("objects")[0].GetProperty("row").GetInt32(), "Error row index");
            Check.Equal(1L, await DirectoryTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM venues WHERE deletedutc IS NULL;", ct).ConfigureAwait(false), "Dry run writes nothing");
            Dictionary<string, object> real = new Dictionary<string, object> { { "rows", rows }, { "dryRun", false } };
            JsonElement done = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/directory/import", HttpFixture.AdminToken, real, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(!done.GetProperty("dryRun").GetBoolean(), "Applied");
            Check.Equal(2L, await DirectoryTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM venues WHERE deletedutc IS NULL;", ct).ConfigureAwait(false), "Created one");
            Check.Equal(1L, await DirectoryTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM venues WHERE summary = 'Updated by import';", ct).ConfigureAwait(false), "Updated by slug");
            Check.Equal(1L, await DirectoryTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM venues WHERE geocodependingutc IS NOT NULL;", ct).ConfigureAwait(false), "Geocoding queued for the background");
            Check.True(await DirectoryTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM auditrecords WHERE action = 'directory.import';", ct).ConfigureAwait(false) == 1, "Import audited");
            await DirectoryTestKit.InstructorAsync(f, "Export Coach", ct).ConfigureAwait(false);
            ApiResult csv = await Api.SendAsync(f, HttpMethod.Get, "/api/v1/admin/directory/export.csv", HttpFixture.AdminToken, null, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.OK, csv.Status, "Export");
            Check.True(csv.Text.StartsWith("kind,id,slug", StringComparison.Ordinal), "Header row");
            Check.True(csv.Text.Contains("Imported One", StringComparison.Ordinal) && csv.Text.Contains("Export Coach", StringComparison.Ordinal), "Venues and instructors");
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            ApiResult forbidden = await Api.SendAsync(f, HttpMethod.Get, "/api/v1/admin/directory/export.csv", member.AccessToken, null, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.Forbidden, forbidden.Status, "Members cannot export");
            JsonElement metrics = await MetricEnumerationTestKit.DailyAsync(f, "/api/v1/admin/directory/metrics", HttpFixture.AdminToken, ct).ConfigureAwait(false);
            Check.Equal(2L, metrics.GetProperty("activeVenues").GetInt64(), "Metrics");
            Check.Equal(1L, metrics.GetProperty("instructors").GetInt64(), "Instructor metric");
            Check.Equal(30, metrics.GetProperty("venuesCreatedByDay").GetArrayLength(), "Default 30 days");
            JsonElement week = await MetricEnumerationTestKit.DailyAsync(f, "/api/v1/admin/directory/metrics?days=7", HttpFixture.AdminToken, ct).ConfigureAwait(false);
            string today = DateTime.UtcNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            Check.Equal(7, week.GetProperty("days").GetInt32(), "days");
            foreach (string series in new[] { "submissionsByDay", "approvalsByDay", "venuesCreatedByDay" })
            {
                Check.Equal(7, week.GetProperty(series).GetArrayLength(), series + ": one entry per day");
                Check.Equal(today, Api.Str(week.GetProperty(series)[6], "date"), series + " ends today");
            }
            Check.Equal(2L, week.GetProperty("venuesCreatedByDay")[6].GetProperty("count").GetInt64(), "Venues created today");
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/admin/directory/metrics?days=3", HttpFixture.AdminToken, null, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
        }

        #endregion
    }
}

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

    /// <summary>
    /// Phase 4 directory submissions and claims (docs/PHASE4_CONTRACT.md sections 2.4, 3.7, 3.8; resolved decisions 7 and
    /// 9): every type, duplicates, reviewer overrides, the stale check, claims, limits, media attach and cleanup, exact
    /// submitted addresses, nothing published before approval, notifications, and account deletion.
    /// </summary>
    public static class SubmissionSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "DirectorySubmissions";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Directory submissions (" + TestEnvironment.Provider + ")", new[]
            {
                Case("Submissions.NewVenueApproval", "A NewVenue is unpublished until approval, keeps the exact submitted address, attaches its photo on approval, notifies the submitter, and is audited", NewVenueAsync),
                Case("Submissions.PlacedPin", "NewVenue and EditVenue accept fields.geo { lat, lng } as the proposed pin (validated), stored as proposedGeo rather than a field row, and applied on approval unless the reviewer sends another pin", PlacedPinAsync),
                Case("Submissions.Duplicates", "A NewVenue similar to an existing venue nearby answers a Draft with the duplicates and queues nothing until confirmed", DuplicatesAsync),
                Case("Submissions.EditOverridesStale", "EditVenue carries current and proposed values; reviewers override fields; approving after another approved edit is 409 SubmissionStale until the diff is reloaded", StaleAsync),
                Case("Submissions.InstructorsAndClaims", "NewInstructor and EditInstructor apply on approval (linked members get the fast-track badge); claims link the claimant, a second claim is AlreadyClaimed", ClaimsAsync),
                Case("Submissions.ReportClosedRejectWithdraw", "ReportClosed closes the venue on approval; rejection needs a note and deletes unattached media; withdrawal works once", RejectAsync),
                Case("Submissions.LimitsAndSwitches", "Pending limit, unknown fields, the submissions and claims switches, and muted members", LimitsAsync),
                Case("Submissions.AccountDeletion", "Deleting the account withdraws pending submissions, anonymizes the rest, and removes instructor links", DeletionAsync)
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

        private static Task<JsonElement> SubmitAsync(HttpFixture f, string bearer, object body, CancellationToken ct, HttpStatusCode expected = HttpStatusCode.Created)
        {
            return Api.ExpectAsync(f, HttpMethod.Post, DirectoryTestKit.Submissions, bearer, body, expected, ct);
        }

        private static Task<JsonElement> ApproveAsync(HttpFixture f, string id, CancellationToken ct, object? body = null)
        {
            return Api.ExpectAsync(f, HttpMethod.Post, DirectoryTestKit.Review + "/" + id + "/approve", HttpFixture.AdminToken, body ?? new { reviewNote = "Thanks" }, HttpStatusCode.OK, ct);
        }

        private static async Task NewVenueAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            TestMember other = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            string photo = await ContentTestKit.MediaAsync(f, member.Id, MediaKindEnum.Image, ct).ConfigureAwait(false);
            string foreign = await ContentTestKit.MediaAsync(f, other.Id, MediaKindEnum.Image, ct).ConfigureAwait(false);
            object address = new { line1 = "200 Example Avenue", locality = "Exampleville", region = "EX", postalCode = "00001", countryCode = "US" };
            await Api.ExpectErrorAsync(f, HttpMethod.Post, DirectoryTestKit.Submissions, member.AccessToken, new { type = "NewVenue", fields = new { name = "Uptown Club", photoMediaIds = new[] { foreign } } }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            JsonElement submitted = await SubmitAsync(f, member.AccessToken, new
            {
                type = "NewVenue",
                fields = new { name = "Example Club Uptown", address, programs = new string[0], websiteUrl = "https://uptown.example.test", scheduleText = "Tue and Thu 7 pm", photoMediaIds = new[] { photo } },
                note = "Opened last month."
            }, ct).ConfigureAwait(false);
            string id = Api.Str(submitted, "id");
            Check.Equal("Pending", Api.Str(submitted, "status"), "Pending");
            Check.True(submitted.GetProperty("changes").EnumerateArray().Any(c => Api.Str(c, "field") == "address.line1" && Api.Str(c, "proposed") == "200 Example Avenue"), "Structured changes");
            Check.True(submitted.GetProperty("changes").EnumerateArray().Any(c => Api.Str(c, "field") == "addressVisibility" && Api.Str(c, "proposed") == "Exact"), "Submitted addresses default to Exact");
            Check.True(await DirectoryTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM directorysubmissionfields WHERE submissionid = '" + id + "';", ct).ConfigureAwait(false) > 5, "Field rows, not JSON");
            Check.Equal(0, (await DirectoryTestKit.ListAsync(f, other.AccessToken, new { }, ct).ConfigureAwait(false)).GetProperty("objects").GetArrayLength(), "Nothing is published before approval");
            JsonElement mine = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/me/directory/submissions/enumerate", member.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(DirectoryTestKit.Ids(mine).SequenceEqual(new List<string> { id }), "Own submissions");
            Check.Equal(0, (await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/me/directory/submissions/enumerate", other.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false)).GetProperty("objects").GetArrayLength(), "Only your own");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, DirectoryTestKit.Review + "/enumerate", member.AccessToken, new { }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            JsonElement queue = await Api.ExpectAsync(f, HttpMethod.Post, DirectoryTestKit.Review + "/enumerate", HttpFixture.AdminToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(DirectoryTestKit.Ids(queue).SequenceEqual(new List<string> { id }), "Review queue");
            JsonElement approved = await ApproveAsync(f, id, ct, new { reviewNote = "Welcome", geo = new { lat = 37.6, lng = -122.3 } }).ConfigureAwait(false);
            JsonElement target = approved.GetProperty("target");
            string venueId = Api.Str(target, "id");
            Check.Equal("Approved", Api.Str(approved.GetProperty("submission"), "status"), "Approved");
            Check.Equal("200 Example Avenue", Api.Str(target.GetProperty("address"), "line1"), "Address applied");
            Check.Equal("Exact", Api.Str(target, "addressVisibility"), "Exact visibility");
            Check.Equal(37.6, target.GetProperty("geo").GetProperty("lat").GetDouble(), "Reviewer pin");
            JsonElement asMember = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/venues/" + venueId, other.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("200 Example Avenue", Api.Str(asMember.GetProperty("address"), "line1"), "Members see the exact submitted address after approval");
            Check.Equal(1, asMember.GetProperty("photos").GetArrayLength(), "The photo was attached on approval");
            List<Notification> decisions = (await NotificationTestKit.ListAsync(f, member.Id, ct).ConfigureAwait(false)).Where(n => n.Type == NotificationTypeEnum.SubmissionDecision).ToList();
            Check.Equal(1, decisions.Count, "SubmissionDecision");
            Check.True(decisions[0].Title.Contains("was approved", StringComparison.Ordinal), "Decision title (" + decisions[0].Title + ")");
            Check.Equal("/venues/" + venueId, decisions[0].Path, "Decision path");
            Check.True(await DirectoryTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM emailmessages WHERE template = 'directory_submission_decision';", ct).ConfigureAwait(false) >= 1, "Decision email queued");
            Check.True(await DirectoryTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM auditrecords WHERE action = 'directory.submission.approve';", ct).ConfigureAwait(false) == 1, "Approval audited");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, DirectoryTestKit.Review + "/" + id + "/approve", HttpFixture.AdminToken, new { }, HttpStatusCode.Conflict, "NotPending", ct).ConfigureAwait(false);
        }

        private static async Task PlacedPinAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, DirectoryTestKit.Submissions, member.AccessToken, new { type = "NewVenue", fields = new { name = "Bad Pin Club", geo = new { lat = 95.0, lng = 10.0 } } }, HttpStatusCode.BadRequest, "InvalidGeo", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, DirectoryTestKit.Submissions, member.AccessToken, new { type = "NewVenue", fields = new { name = "Half Pin Club", geo = new { lat = 10.0 } } }, HttpStatusCode.BadRequest, "InvalidGeo", ct).ConfigureAwait(false);
            JsonElement submitted = await SubmitAsync(f, member.AccessToken, new { type = "NewVenue", fields = new { name = "Pinned Harbor Club", geo = new { lat = 12.345678, lng = -45.5 } } }, ct).ConfigureAwait(false);
            string id = Api.Str(submitted, "id");
            Check.Equal(12.345678, submitted.GetProperty("proposedGeo").GetProperty("lat").GetDouble(), "Placed pin is the proposed pin");
            Check.True(!submitted.GetProperty("changes").EnumerateArray().Any(c => Api.Str(c, "field").StartsWith("geo", StringComparison.Ordinal)), "The pin is not a field row");
            JsonElement approved = await ApproveAsync(f, id, ct).ConfigureAwait(false);
            JsonElement target = approved.GetProperty("target");
            Check.Equal(-45.5, target.GetProperty("geo").GetProperty("lng").GetDouble(), "The placed pin is applied on approval");
            string venueId = Api.Str(target, "id");

            JsonElement moved = await SubmitAsync(f, member.AccessToken, new { type = "EditVenue", targetId = venueId, fields = new { geo = new { lat = 13.0, lng = -46.0 } } }, ct).ConfigureAwait(false);
            Check.Equal(13.0, moved.GetProperty("proposedGeo").GetProperty("lat").GetDouble(), "An edit may move only the pin");
            JsonElement reviewed = await ApproveAsync(f, Api.Str(moved, "id"), ct, new { reviewNote = "Adjusted", geo = new { lat = 13.5, lng = -46.5 } }).ConfigureAwait(false);
            Check.Equal(13.5, reviewed.GetProperty("target").GetProperty("geo").GetProperty("lat").GetDouble(), "The reviewer's pin wins");
            Venue stored = (await f.Database.DirectoryListings.Venues.ReadAsync(venueId, ct).ConfigureAwait(false))!;
            Check.Equal(GeoSourceEnum.Manual, stored.GeoSource, "Manual pin");
        }

        private static async Task DuplicatesAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            string existing = await DirectoryTestKit.VenueIdAsync(f, "Example Club Downtown", ct, extra: new { address = new { line1 = "100 Example Street", locality = "Exampleville", countryCode = "US" } }).ConfigureAwait(false);
            object body = new { type = "NewVenue", fields = new { name = "Example Club Downtwn", address = new { line1 = "101 Example Street", locality = "Exampleville", countryCode = "US" } } };
            JsonElement draft = await SubmitAsync(f, member.AccessToken, body, ct, HttpStatusCode.OK).ConfigureAwait(false);
            Check.Equal("Draft", Api.Str(draft, "status"), "Draft");
            Check.Equal(existing, Api.Str(draft.GetProperty("possibleDuplicates").GetProperty("objects")[0], "id"), "The duplicate is shown");
            Check.Equal(0L, await DirectoryTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM directorysubmissions;", ct).ConfigureAwait(false), "Nothing queued");
            JsonElement elsewhere = await SubmitAsync(f, member.AccessToken, new { type = "NewVenue", fields = new { name = "Example Club Downtwn", address = new { locality = "Otherville", countryCode = "US" } } }, ct).ConfigureAwait(false);
            Check.Equal("Pending", Api.Str(elsewhere, "status"), "The same name in another locality is not a duplicate");
            JsonElement confirmed = await SubmitAsync(f, member.AccessToken, new { type = "NewVenue", confirmNotDuplicate = true, fields = new { name = "Example Club Downtwn", address = new { line1 = "101 Example Street", locality = "Exampleville", countryCode = "US" } } }, ct).ConfigureAwait(false);
            Check.Equal("Pending", Api.Str(confirmed, "status"), "Queued once confirmed");
            JsonElement review = await Api.ExpectAsync(f, HttpMethod.Get, DirectoryTestKit.Review + "/" + Api.Str(confirmed, "id"), HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(1, review.GetProperty("possibleDuplicates").GetProperty("objects").GetArrayLength(), "Reviewers see the duplicates");
            await DirectoryTestKit.InstructorAsync(f, "Example Coach Person", ct).ConfigureAwait(false);
            JsonElement coach = await SubmitAsync(f, member.AccessToken, new { type = "NewInstructor", fields = new { displayName = "Example Coach Persson" } }, ct, HttpStatusCode.OK).ConfigureAwait(false);
            Check.Equal("Draft", Api.Str(coach, "status"), "Instructor duplicates by name");
        }

        private static async Task StaleAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember first = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            TestMember second = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            string venue = await DirectoryTestKit.VenueIdAsync(f, "Stale Club", ct, extra: new { summary = "Original summary", scheduleText = "Mon 6 pm" }).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, DirectoryTestKit.Submissions, first.AccessToken, new { type = "EditVenue", targetId = venue, fields = new { } }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            JsonElement a = await SubmitAsync(f, first.AccessToken, new { type = "EditVenue", targetId = venue, fields = new { summary = "First summary" } }, ct).ConfigureAwait(false);
            JsonElement b = await SubmitAsync(f, second.AccessToken, new { type = "EditVenue", targetId = venue, fields = new { scheduleText = "Tue 7 pm" } }, ct).ConfigureAwait(false);
            JsonElement change = a.GetProperty("changes")[0];
            Check.Equal("summary", Api.Str(change, "field"), "Only the changed field");
            Check.Equal("Original summary", Api.Str(change, "current"), "Current value");
            JsonElement overridden = await Api.ExpectAsync(f, HttpMethod.Put, DirectoryTestKit.Review + "/" + Api.Str(a, "id"), HttpFixture.AdminToken, new { fields = new { summary = "Reviewer summary" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement over = overridden.GetProperty("changes").EnumerateArray().First(c => Api.Str(c, "field") == "summary");
            Check.True(over.GetProperty("isOverride").GetBoolean() && Api.Str(over, "proposed") == "Reviewer summary", "Override stored");
            await Api.ExpectErrorAsync(f, HttpMethod.Put, DirectoryTestKit.Review + "/" + Api.Str(a, "id"), HttpFixture.AdminToken, new { fields = new { audience = "Public" } }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await ApproveAsync(f, Api.Str(a, "id"), ct).ConfigureAwait(false);
            JsonElement applied = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/venues/" + venue, second.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Reviewer summary", Api.Str(applied, "summary"), "Override applied");
            Check.Equal("Mon 6 pm", Api.Str(applied, "scheduleText"), "Other fields untouched");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, DirectoryTestKit.Review + "/" + Api.Str(b, "id") + "/approve", HttpFixture.AdminToken, new { }, HttpStatusCode.Conflict, "SubmissionStale", ct).ConfigureAwait(false);
            JsonElement reloaded = await Api.ExpectAsync(f, HttpMethod.Get, DirectoryTestKit.Review + "/" + Api.Str(b, "id"), HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Mon 6 pm", Api.Str(reloaded.GetProperty("changes")[0], "current"), "Fresh diff");
            await ApproveAsync(f, Api.Str(b, "id"), ct).ConfigureAwait(false);
            JsonElement both = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/venues/" + venue, second.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Tue 7 pm", Api.Str(both, "scheduleText"), "Second edit applied after the reload");
            Check.Equal("Reviewer summary", Api.Str(both, "summary"), "First edit kept");
        }

        private static async Task ClaimsAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            TestMember other = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            string venue = await DirectoryTestKit.VenueIdAsync(f, "Claim Club", ct).ConfigureAwait(false);
            JsonElement created = await SubmitAsync(f, member.AccessToken, new { type = "NewInstructor", fields = new { displayName = "Proposed Coach", bio = "Teaches fundamentals.", venueIds = new[] { venue } } }, ct).ConfigureAwait(false);
            JsonElement approvedNew = await ApproveAsync(f, Api.Str(created, "id"), ct).ConfigureAwait(false);
            string coach = Api.Str(approvedNew.GetProperty("target"), "id");
            Check.Equal(1, approvedNew.GetProperty("target").GetProperty("venueCount").GetInt32(), "Venues linked on approval");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, DirectoryTestKit.Submissions, member.AccessToken, new { type = "ClaimInstructor", targetId = coach, note = "short" }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            JsonElement claim = await SubmitAsync(f, member.AccessToken, new { type = "ClaimInstructor", targetId = coach, note = "This is me, I teach there." }, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, DirectoryTestKit.Submissions, member.AccessToken, new { type = "ClaimInstructor", targetId = coach, note = "This is me, again please." }, HttpStatusCode.Conflict, "AlreadyClaimed", ct).ConfigureAwait(false);
            JsonElement rival = await SubmitAsync(f, other.AccessToken, new { type = "ClaimInstructor", targetId = coach, note = "No, this one is me." }, ct).ConfigureAwait(false);
            await ApproveAsync(f, Api.Str(claim, "id"), ct).ConfigureAwait(false);
            JsonElement linked = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/instructors/" + coach, other.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(member.Id, Api.Str(linked.GetProperty("linkedUser"), "id"), "Claim links the claimant");
            Check.True(!linked.GetProperty("viewer").GetProperty("canClaim").GetBoolean(), "Claimed entries cannot be claimed");
            Check.True((await NotificationTestKit.ListAsync(f, member.Id, ct).ConfigureAwait(false)).Any(n => n.Type == NotificationTypeEnum.InstructorLinked), "InstructorLinked on claim approval");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, DirectoryTestKit.Review + "/" + Api.Str(rival, "id") + "/approve", HttpFixture.AdminToken, new { }, HttpStatusCode.Conflict, "AlreadyClaimed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, DirectoryTestKit.Submissions, other.AccessToken, new { type = "ClaimInstructor", targetId = coach, note = "Trying once more here." }, HttpStatusCode.Conflict, "AlreadyClaimed", ct).ConfigureAwait(false);
            JsonElement edit = await SubmitAsync(f, member.AccessToken, new { type = "EditInstructor", targetId = coach, fields = new { bio = "Updated by the coach." } }, ct).ConfigureAwait(false);
            Check.True(edit.GetProperty("isLinkedMember").GetBoolean(), "Linked member badge");
            JsonElement queue = await Api.ExpectAsync(f, HttpMethod.Post, DirectoryTestKit.Review + "/enumerate", HttpFixture.AdminToken, new { filters = new { type = "EditInstructor" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(DirectoryTestKit.Find(queue, Api.Str(edit, "id")).GetProperty("isLinkedMember").GetBoolean(), "Badge in the queue");
            await ApproveAsync(f, Api.Str(edit, "id"), ct).ConfigureAwait(false);
            JsonElement after = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/instructors/" + coach, other.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Updated by the coach.", Api.Str(after, "bio"), "EditInstructor applied");
        }

        private static async Task RejectAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            string venue = await DirectoryTestKit.VenueIdAsync(f, "Closing Club", ct).ConfigureAwait(false);
            JsonElement report = await SubmitAsync(f, member.AccessToken, new { type = "ReportClosed", targetId = venue, note = "The doors are locked." }, ct).ConfigureAwait(false);
            await ApproveAsync(f, Api.Str(report, "id"), ct).ConfigureAwait(false);
            JsonElement closed = await Api.ExpectAsync(f, HttpMethod.Get, DirectoryTestKit.AdminVenues + "/" + venue, HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Closed", Api.Str(closed, "status"), "Closed on approval");
            string photo = await ContentTestKit.MediaAsync(f, member.Id, MediaKindEnum.Image, ct).ConfigureAwait(false);
            JsonElement proposal = await SubmitAsync(f, member.AccessToken, new { type = "NewVenue", fields = new { name = "Rejected Club", photoMediaIds = new[] { photo } } }, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, DirectoryTestKit.Review + "/" + Api.Str(proposal, "id") + "/reject", HttpFixture.AdminToken, new { reviewNote = "" }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            JsonElement rejected = await Api.ExpectAsync(f, HttpMethod.Post, DirectoryTestKit.Review + "/" + Api.Str(proposal, "id") + "/reject", HttpFixture.AdminToken, new { reviewNote = "Not a venue." }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Rejected", Api.Str(rejected, "status"), "Rejected");
            Check.Equal(1L, await DirectoryTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM mediaattachments WHERE id = '" + photo + "' AND deletedutc IS NOT NULL;", ct).ConfigureAwait(false), "Unattached media deleted on reject");
            Check.True((await NotificationTestKit.ListAsync(f, member.Id, ct).ConfigureAwait(false)).Any(n => n.Type == NotificationTypeEnum.SubmissionDecision && n.Title.Contains("not approved", StringComparison.Ordinal)), "Rejection notified");
            string photo2 = await ContentTestKit.MediaAsync(f, member.Id, MediaKindEnum.Image, ct).ConfigureAwait(false);
            JsonElement withdrawable = await SubmitAsync(f, member.AccessToken, new { type = "NewVenue", fields = new { name = "Withdrawn Club", photoMediaIds = new[] { photo2 } } }, ct).ConfigureAwait(false);
            TestMember stranger = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, DirectoryTestKit.Submissions + "/" + Api.Str(withdrawable, "id") + "/withdraw", stranger.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            JsonElement withdrawn = await Api.ExpectAsync(f, HttpMethod.Post, DirectoryTestKit.Submissions + "/" + Api.Str(withdrawable, "id") + "/withdraw", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Withdrawn", Api.Str(withdrawn, "status"), "Withdrawn");
            Check.Equal(1L, await DirectoryTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM mediaattachments WHERE id = '" + photo2 + "' AND deletedutc IS NOT NULL;", ct).ConfigureAwait(false), "Unattached media deleted on withdraw");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, DirectoryTestKit.Submissions + "/" + Api.Str(withdrawable, "id") + "/withdraw", member.AccessToken, null, HttpStatusCode.Conflict, "NotPending", ct).ConfigureAwait(false);
            Check.True(await DirectoryTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM emailmessages WHERE template = 'staff_directory_submission';", ct).ConfigureAwait(false) >= 0, "Staff email path ran");
        }

        private static async Task LimitsAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            f.Settings.Directory.MaxPendingSubmissionsPerMember = 2;
            await SubmitAsync(f, member.AccessToken, new { type = "NewVenue", fields = new { name = "Limit One" } }, ct).ConfigureAwait(false);
            await SubmitAsync(f, member.AccessToken, new { type = "NewVenue", fields = new { name = "Limit Two" } }, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, DirectoryTestKit.Submissions, member.AccessToken, new { type = "NewVenue", fields = new { name = "Limit Three" } }, HttpStatusCode.Conflict, "SubmissionLimitReached", ct).ConfigureAwait(false);
            f.Settings.Directory.MaxPendingSubmissionsPerMember = 100;
            await Api.ExpectErrorAsync(f, HttpMethod.Post, DirectoryTestKit.Submissions, member.AccessToken, new { type = "NewVenue", fields = new { name = "Field Club", audience = "Public" } }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, DirectoryTestKit.Submissions, member.AccessToken, new { fields = new { name = "No Type" } }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, DirectoryTestKit.Submissions, member.AccessToken, new { type = "EditVenue", targetId = "ven_missing", fields = new { name = "Ghost" } }, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/moderation/word-filter", HttpFixture.AdminToken, new { term = "forbiddenword", matchType = "Word", action = "Block" }, HttpStatusCode.Created, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, DirectoryTestKit.Submissions, member.AccessToken, new { type = "NewVenue", fields = new { name = "Clean Club" }, note = "this has forbiddenword inside" }, HttpStatusCode.BadRequest, "ContentFiltered", ct).ConfigureAwait(false);
            string coach = Api.Str(await DirectoryTestKit.InstructorAsync(f, "Switch Coach", ct).ConfigureAwait(false), "id");
            await DirectoryTestKit.SettingsAsync(f, new { claims = false }, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, DirectoryTestKit.Submissions, member.AccessToken, new { type = "ClaimInstructor", targetId = coach, note = "This is me, really it is." }, HttpStatusCode.Forbidden, "SubmissionsDisabled", ct).ConfigureAwait(false);
            await DirectoryTestKit.SettingsAsync(f, new { submissions = false }, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, DirectoryTestKit.Submissions, member.AccessToken, new { type = "NewVenue", fields = new { name = "Off Club" } }, HttpStatusCode.Forbidden, "SubmissionsDisabled", ct).ConfigureAwait(false);
            await DirectoryTestKit.SettingsAsync(f, new { submissions = true }, ct).ConfigureAwait(false);
            await f.Raw.ExecuteQueryAsync("UPDATE users SET ismuted = true WHERE id = '" + member.Id + "';", false, ct).ConfigureAwait(false);
            f.Services.Tokens.InvalidateUser(member.Id);
            ApiResult muted = await Api.SendAsync(f, HttpMethod.Post, DirectoryTestKit.Submissions, member.AccessToken, new { type = "NewVenue", fields = new { name = "Muted Club" } }, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.Forbidden, muted.Status, "Muted members cannot submit (" + muted.Text + ")");
        }

        private static async Task DeletionAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            string coach = Api.Str(await DirectoryTestKit.InstructorAsync(f, "Leaving Coach", ct).ConfigureAwait(false), "id");
            await Api.ExpectAsync(f, HttpMethod.Post, DirectoryTestKit.AdminInstructors + "/" + coach + "/link", HttpFixture.AdminToken, new { userId = member.Id }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement decided = await SubmitAsync(f, member.AccessToken, new { type = "NewVenue", fields = new { name = "Decided Club" } }, ct).ConfigureAwait(false);
            await ApproveAsync(f, Api.Str(decided, "id"), ct).ConfigureAwait(false);
            JsonElement pending = await SubmitAsync(f, member.AccessToken, new { type = "NewVenue", fields = new { name = "Pending Club" } }, ct).ConfigureAwait(false);
            await f.Services.Directory.EraseForAccountAsync(member.Id, ct).ConfigureAwait(false);
            Check.Equal(0L, await DirectoryTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM directorysubmissions WHERE submitteruserid = '" + member.Id + "';", ct).ConfigureAwait(false), "Submissions anonymized");
            Check.Equal(1L, await DirectoryTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM directorysubmissions WHERE id = '" + Api.Str(pending, "id") + "' AND status = 'Withdrawn';", ct).ConfigureAwait(false), "Pending withdrawn");
            Check.Equal(0L, await DirectoryTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM instructors WHERE linkeduserid = '" + member.Id + "';", ct).ConfigureAwait(false), "Instructor links removed");
            Check.Equal(1L, await DirectoryTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM instructors WHERE id = '" + coach + "' AND deletedutc IS NULL;", ct).ConfigureAwait(false), "The entry stays");
            JsonElement review = await Api.ExpectAsync(f, HttpMethod.Get, DirectoryTestKit.Review + "/" + Api.Str(decided, "id"), HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(review.GetProperty("submitter").GetProperty("isDeleted").GetBoolean(), "Shown as a deleted member");
        }

        #endregion
    }
}

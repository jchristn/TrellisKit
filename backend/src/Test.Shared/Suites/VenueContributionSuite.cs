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
    /// Venue reviews and member media (docs/PHASE4_CONTRACT.md sections 3.12 and 3.13): one review per member, edits
    /// and deletes by the author only, rating aggregates on detail and list rows, orderings and filters, the instructor
    /// reply with notifications, moderation through reports and actions, word filter and mutes, blocks, rate limits,
    /// the per-property switches, and media add, list, visibility, and removal rights.
    /// </summary>
    public static class VenueContributionSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "VenueContribution";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Venue reviews and media (" + TestEnvironment.Provider + ")", new[]
            {
                Case("VenueContribution.ReviewLifecycle", "A member reviews a venue once (409 AlreadyReviewed), only the author edits and deletes, validation and the per-property switch apply, and the venue detail and list rows carry the average, count, distribution, and the caller's own review", LifecycleAsync),
                Case("VenueContribution.OrderingsAndFilters", "Reviews list newest first by default, RatingDescending and RatingAscending order by stars with newest first within a rating, rating and hasReply filters apply, and continuation pages do not repeat", OrderingAsync),
                Case("VenueContribution.InstructorReply", "Linked instructors are notified of a new review and post one reply that notifies the reviewer; other members and the reviewer cannot reply; staff delete a reply with an audit", ReplyAsync),
                Case("VenueContribution.Moderation", "Reports queue in the Directory scope for directory moderators, Hide drops the review from lists and the aggregates while the author still sees it, Remove and Restore apply, the word filter blocks and flags, and muted members cannot review", ModerationAsync),
                Case("VenueContribution.BlocksAndLimits", "Blocked members' reviews and uploads are hidden from each other both ways, replies across a block are refused, and the daily and new-account review limits return 429", BlocksAsync),
                Case("VenueContribution.Media", "Members add uploads to a venue, items list and are readable as media, other members cannot remove them, the uploader deletes, directory staff and linked instructors remove with an audit, SystemAdmin uploads are protected, reports queue, and the switch turns adds off", MediaAsync)
            });
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string caseId, string name, Func<HttpFixture, CancellationToken, Task> body)
        {
            return SuiteCases.Http(Id, caseId, name, (f, ct) =>
            {
                DirectoryTestKit.Relax(f);
                f.Settings.Directory.ReviewsPerDay = 1000;
                f.Settings.Directory.NewAccountReviewsPerDay = 1000;
                f.Settings.RateLimits.EditsPerHour = 10000;
                return body(f, ct);
            });
        }

        private static string Reviews(string venueId)
        {
            return "/api/v1/venues/" + venueId + "/reviews";
        }

        private static async Task<JsonElement> ReviewAsync(HttpFixture f, TestMember member, string venueId, int rating, string body, CancellationToken ct)
        {
            return await Api.ExpectAsync(f, HttpMethod.Post, Reviews(venueId), member.AccessToken, new { rating, body }, HttpStatusCode.Created, ct).ConfigureAwait(false);
        }

        private static async Task<JsonElement> ListAsync(HttpFixture f, string bearer, string venueId, object query, CancellationToken ct)
        {
            return await Api.ExpectAsync(f, HttpMethod.Post, Reviews(venueId) + "/enumerate", bearer, query, HttpStatusCode.OK, ct).ConfigureAwait(false);
        }

        private static async Task<JsonElement> VenueAsync(HttpFixture f, string bearer, string venueId, CancellationToken ct)
        {
            return await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/venues/" + venueId, bearer, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
        }

        private static async Task<TestMember> LinkedInstructorAsync(HttpFixture f, string venueId, string name, CancellationToken ct)
        {
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            string instructorId = Api.Str(await DirectoryTestKit.InstructorAsync(f, name, ct).ConfigureAwait(false), "id");
            await Api.ExpectAsync(f, HttpMethod.Put, DirectoryTestKit.AdminVenues + "/" + venueId + "/instructors/" + instructorId, HttpFixture.AdminToken, new { role = "Head" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, DirectoryTestKit.AdminInstructors + "/" + instructorId + "/link", HttpFixture.AdminToken, new { userId = member.Id }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            return member;
        }

        private static async Task LifecycleAsync(HttpFixture f, CancellationToken ct)
        {
            string venueId = await DirectoryTestKit.VenueIdAsync(f, "Review Lifecycle Club", ct, 10.0, 10.0).ConfigureAwait(false);
            TestMember author = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            TestMember other = await Api.MemberAsync(f, ct).ConfigureAwait(false);

            JsonElement property = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/property", null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement rules = property.GetProperty("directory").GetProperty("reviews");
            Check.True(rules.GetProperty("enabled").GetBoolean(), "Reviews on by default");
            Check.Equal(10, rules.GetProperty("minLength").GetInt32(), "Minimum length published");
            Check.True(property.GetProperty("directory").GetProperty("memberMedia").GetBoolean(), "Member media on by default");

            JsonElement before = await VenueAsync(f, author.AccessToken, venueId, ct).ConfigureAwait(false);
            Check.True(before.GetProperty("viewer").GetProperty("canReview").GetBoolean(), "canReview before reviewing");
            Check.True(before.GetProperty("ratingAverage").ValueKind == JsonValueKind.Null, "No average without reviews");

            await Api.ExpectErrorAsync(f, HttpMethod.Post, Reviews(venueId), author.AccessToken, new { rating = 0, body = "Great place to train." }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, Reviews(venueId), author.AccessToken, new { rating = 6, body = "Great place to train." }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, Reviews(venueId), author.AccessToken, new { rating = 4, body = "Short" }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, Reviews(venueId), author.AccessToken, new { rating = 4 }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, Reviews(venueId), null, new { rating = 4, body = "Friendly and organized." }, HttpStatusCode.Unauthorized, "Unauthorized", ct).ConfigureAwait(false);

            JsonElement created = await ReviewAsync(f, author, venueId, 4, "Friendly and well organized classes.", ct).ConfigureAwait(false);
            string reviewId = Api.Str(created, "id");
            Check.True(reviewId.StartsWith("vrv_", StringComparison.Ordinal), "Review id prefix");
            Check.True(created.GetProperty("viewer").GetProperty("isAuthor").GetBoolean() && created.GetProperty("viewer").GetProperty("canEdit").GetBoolean(), "Author viewer");
            Check.True(created.TryGetProperty("labels", out _) && created.TryGetProperty("tags", out _), "Labels and tags");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, Reviews(venueId), author.AccessToken, new { rating = 5, body = "Another review of the same place." }, HttpStatusCode.Conflict, "AlreadyReviewed", ct).ConfigureAwait(false);
            await ReviewAsync(f, other, venueId, 1, "Too crowded on weekday evenings.", ct).ConfigureAwait(false);

            JsonElement detail = await VenueAsync(f, author.AccessToken, venueId, ct).ConfigureAwait(false);
            Check.Equal(2, detail.GetProperty("ratingCount").GetInt32(), "Two ratings");
            Check.Equal(2.5, detail.GetProperty("ratingAverage").GetDouble(), "Average of 4 and 1");
            JsonElement summary = detail.GetProperty("reviews");
            Check.Equal(1, summary.GetProperty("distribution").GetProperty("4").GetInt32(), "Distribution 4 stars");
            Check.Equal(1, summary.GetProperty("distribution").GetProperty("1").GetInt32(), "Distribution 1 star");
            Check.Equal(0, summary.GetProperty("distribution").GetProperty("5").GetInt32(), "Distribution 5 stars");
            Check.Equal(reviewId, Api.Str(summary.GetProperty("mine"), "id"), "The caller's own review");
            Check.True(!detail.GetProperty("viewer").GetProperty("canReview").GetBoolean(), "No canReview after reviewing");
            JsonElement rows = await DirectoryTestKit.ListAsync(f, other.AccessToken, new { }, ct).ConfigureAwait(false);
            JsonElement row = DirectoryTestKit.Find(rows, venueId);
            Check.Equal(2, row.GetProperty("ratingCount").GetInt32(), "List rows carry the count");
            Check.Equal(2.5, row.GetProperty("ratingAverage").GetDouble(), "List rows carry the average");

            await Api.ExpectErrorAsync(f, HttpMethod.Put, Reviews(venueId) + "/" + reviewId, other.AccessToken, new { rating = 1 }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Delete, Reviews(venueId) + "/" + reviewId, other.AccessToken, null, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            JsonElement edited = await Api.ExpectAsync(f, HttpMethod.Put, Reviews(venueId) + "/" + reviewId, author.AccessToken, new { rating = 5 }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(5, edited.GetProperty("rating").GetInt32(), "Rating edited");
            Check.Equal("Friendly and well organized classes.", Api.Str(edited, "body"), "Body kept when omitted");
            Check.True(edited.GetProperty("editedUtc").ValueKind == JsonValueKind.String, "Edit time recorded");
            Check.Equal(3.0, (await VenueAsync(f, other.AccessToken, venueId, ct).ConfigureAwait(false)).GetProperty("ratingAverage").GetDouble(), "Average recomputed after the edit");
            JsonElement one = await Api.ExpectAsync(f, HttpMethod.Get, Reviews(venueId) + "/" + reviewId, other.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(!one.GetProperty("viewer").GetProperty("canEdit").GetBoolean() && one.GetProperty("viewer").GetProperty("canReport").GetBoolean(), "Other members may report, not edit");

            await Api.ExpectAsync(f, HttpMethod.Delete, Reviews(venueId) + "/" + reviewId, author.AccessToken, null, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
            JsonElement afterDelete = await VenueAsync(f, author.AccessToken, venueId, ct).ConfigureAwait(false);
            Check.Equal(1, afterDelete.GetProperty("ratingCount").GetInt32(), "Deleted review leaves the count");
            Check.True(afterDelete.GetProperty("reviews").GetProperty("mine").ValueKind == JsonValueKind.Null, "No own review after deleting");
            await ReviewAsync(f, author, venueId, 3, "Trying again after a few more visits.", ct).ConfigureAwait(false);

            await DirectoryTestKit.SettingsAsync(f, new { values = new Dictionary<string, object> { { "directory.reviewsEnabled", false } } }, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, Reviews(venueId) + "/enumerate", other.AccessToken, new { }, HttpStatusCode.Forbidden, "ReviewsDisabled", ct).ConfigureAwait(false);
            JsonElement off = await VenueAsync(f, other.AccessToken, venueId, ct).ConfigureAwait(false);
            Check.True(!off.GetProperty("reviews").GetProperty("enabled").GetBoolean() && off.GetProperty("ratingCount").GetInt32() == 0, "Switch off hides reviews and ratings");
            await DirectoryTestKit.SettingsAsync(f, new { values = new Dictionary<string, object> { { "directory.reviewsEnabled", true } } }, ct).ConfigureAwait(false);
        }

        private static async Task OrderingAsync(HttpFixture f, CancellationToken ct)
        {
            string venueId = await DirectoryTestKit.VenueIdAsync(f, "Review Ordering Club", ct).ConfigureAwait(false);
            int[] ratings = { 3, 5, 1, 5, 2 };
            List<string> ids = new List<string>();
            foreach (int rating in ratings)
            {
                TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                ids.Add(Api.Str(await ReviewAsync(f, member, venueId, rating, "Review with " + rating + " stars for the list.", ct).ConfigureAwait(false), "id"));
                await Task.Delay(1100, ct).ConfigureAwait(false);
            }
            TestMember reader = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            List<string> newest = DirectoryTestKit.Ids(await ListAsync(f, reader.AccessToken, venueId, new { }, ct).ConfigureAwait(false));
            Check.Equal(String.Join(",", Enumerable.Reverse(ids)), String.Join(",", newest), "Newest first by default");
            List<string> highest = DirectoryTestKit.Ids(await ListAsync(f, reader.AccessToken, venueId, new { ordering = "RatingDescending" }, ct).ConfigureAwait(false));
            Check.Equal(String.Join(",", new[] { ids[3], ids[1], ids[0], ids[4], ids[2] }), String.Join(",", highest), "Highest first, newest first within a rating");
            List<string> lowest = DirectoryTestKit.Ids(await ListAsync(f, reader.AccessToken, venueId, new { ordering = "RatingAscending" }, ct).ConfigureAwait(false));
            Check.Equal(String.Join(",", new[] { ids[2], ids[4], ids[0], ids[3], ids[1] }), String.Join(",", lowest), "Lowest first, newest first within a rating");
            List<string> paged = new List<string>();
            string? token = null;
            for (int i = 0; i < 5; i++)
            {
                JsonElement page = await ListAsync(f, reader.AccessToken, venueId, token == null ? new { ordering = "RatingDescending", maxResults = 2 } : (object)new { ordering = "RatingDescending", maxResults = 2, continuationToken = token }, ct).ConfigureAwait(false);
                paged.AddRange(DirectoryTestKit.Ids(page));
                if (page.GetProperty("endOfResults").GetBoolean()) break;
                token = Api.Str(page, "continuationToken");
            }
            Check.Equal(String.Join(",", highest), String.Join(",", paged), "Keyset pages match the single page");
            JsonElement fives = await ListAsync(f, reader.AccessToken, venueId, new { filters = new { rating = new[] { 5 } } }, ct).ConfigureAwait(false);
            Check.Equal(2, fives.GetProperty("objects").GetArrayLength(), "Rating filter");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, Reviews(venueId) + "/enumerate", reader.AccessToken, new { filters = new { rating = new[] { 7 } } }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            JsonElement unanswered = await ListAsync(f, reader.AccessToken, venueId, new { filters = new { hasReply = false } }, ct).ConfigureAwait(false);
            Check.Equal(5, unanswered.GetProperty("objects").GetArrayLength(), "hasReply filter");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, Reviews(venueId) + "/enumerate", reader.AccessToken, new { ordering = "Nope" }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
        }

        private static async Task ReplyAsync(HttpFixture f, CancellationToken ct)
        {
            string venueId = await DirectoryTestKit.VenueIdAsync(f, "Review Reply Club", ct).ConfigureAwait(false);
            TestMember coach = await LinkedInstructorAsync(f, venueId, "Reply Coach", ct).ConfigureAwait(false);
            TestMember reviewer = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            TestMember bystander = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            JsonElement coachVenue = await VenueAsync(f, coach.AccessToken, venueId, ct).ConfigureAwait(false);
            Check.True(coachVenue.GetProperty("viewer").GetProperty("canReplyToReviews").GetBoolean() && coachVenue.GetProperty("viewer").GetProperty("canManageMedia").GetBoolean(), "Linked instructor capabilities");
            Check.True(!(await VenueAsync(f, bystander.AccessToken, venueId, ct).ConfigureAwait(false)).GetProperty("viewer").GetProperty("canReplyToReviews").GetBoolean(), "Members cannot reply");

            string reviewId = Api.Str(await ReviewAsync(f, reviewer, venueId, 5, "The head coach explains everything well.", ct).ConfigureAwait(false), "id");
            List<Notification> coachNotes = await NotificationTestKit.ListAsync(f, coach.Id, ct).ConfigureAwait(false);
            Notification? note = coachNotes.FirstOrDefault(n => n.Type == NotificationTypeEnum.VenueReview);
            Check.True(note != null, "Linked instructor notified of the review");
            Check.True(note!.Path != null && note.Path.Contains("review=" + reviewId, StringComparison.Ordinal), "Notification links the review");

            JsonElement seen = await Api.ExpectAsync(f, HttpMethod.Get, Reviews(venueId) + "/" + reviewId, coach.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(seen.GetProperty("viewer").GetProperty("canReply").GetBoolean(), "canReply for the instructor");
            await Api.ExpectErrorAsync(f, HttpMethod.Put, Reviews(venueId) + "/" + reviewId + "/reply", bystander.AccessToken, new { body = "Not my place to answer." }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, Reviews(venueId) + "/" + reviewId + "/reply", coach.AccessToken, new { body = "" }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            JsonElement replied = await Api.ExpectAsync(f, HttpMethod.Put, Reviews(venueId) + "/" + reviewId + "/reply", coach.AccessToken, new { body = "Thank you for training with us!" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Thank you for training with us!", Api.Str(replied.GetProperty("reply"), "body"), "Reply stored");
            Check.Equal("Reply Coach", Api.Str(replied.GetProperty("reply"), "instructorName"), "Reply names the instructor entry");
            JsonElement edited = await Api.ExpectAsync(f, HttpMethod.Put, Reviews(venueId) + "/" + reviewId + "/reply", coach.AccessToken, new { body = "Thanks, see you on the mats." }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(edited.GetProperty("reply").GetProperty("editedUtc").ValueKind == JsonValueKind.String, "Reply edit recorded");
            Check.Equal(1, (await NotificationTestKit.ListAsync(f, reviewer.Id, ct).ConfigureAwait(false)).Count(n => n.Type == NotificationTypeEnum.VenueReviewReply), "Reviewer notified once");
            JsonElement replies = await ListAsync(f, bystander.AccessToken, venueId, new { filters = new { hasReply = true } }, ct).ConfigureAwait(false);
            Check.Equal(1, replies.GetProperty("objects").GetArrayLength(), "hasReply filter finds it");
            Check.True(!replies.GetProperty("objects")[0].GetProperty("viewer").GetProperty("canDeleteReply").GetBoolean(), "Members cannot delete the reply");

            string ownId = Api.Str(await ReviewAsync(f, coach, venueId, 5, "I teach here and love this place.", ct).ConfigureAwait(false), "id");
            await Api.ExpectErrorAsync(f, HttpMethod.Put, Reviews(venueId) + "/" + ownId + "/reply", coach.AccessToken, new { body = "Replying to myself." }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);

            TestMember moderator = await DirectoryTestKit.ModeratorAsync(f, ct).ConfigureAwait(false);
            JsonElement cleared = await Api.ExpectAsync(f, HttpMethod.Delete, Reviews(venueId) + "/" + reviewId + "/reply", moderator.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(cleared.GetProperty("reply").ValueKind == JsonValueKind.Null, "Staff removed the reply");
            Check.Equal(1L, await ModerationTestKit.AuditCountAsync(f, "venue.review.reply.remove", reviewId, ct).ConfigureAwait(false), "Staff reply removal audited");
            await Api.ExpectErrorAsync(f, HttpMethod.Delete, Reviews(venueId) + "/" + reviewId + "/reply", moderator.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
        }

        private static async Task ModerationAsync(HttpFixture f, CancellationToken ct)
        {
            string venueId = await DirectoryTestKit.VenueIdAsync(f, "Review Moderation Club", ct).ConfigureAwait(false);
            TestMember author = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            TestMember reporter = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            TestMember moderator = await DirectoryTestKit.ModeratorAsync(f, ct).ConfigureAwait(false);
            string reviewId = Api.Str(await ReviewAsync(f, author, venueId, 1, "This review breaks the community rules.", ct).ConfigureAwait(false), "id");

            await ModerationTestKit.ReportOkAsync(f, reporter.AccessToken, "VenueReview", reviewId, ct, "Harassment").ConfigureAwait(false);
            Check.Equal(1L, await DirectoryTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM reports WHERE targetid = '" + reviewId + "' AND scopetype = 'Directory';", ct).ConfigureAwait(false), "Report in the Directory scope");
            Check.Equal(1L, await DirectoryTestKit.CountAsync(f, "SELECT openreportcount AS cnt FROM venuereviews WHERE id = '" + reviewId + "';", ct).ConfigureAwait(false), "Open report count");
            JsonElement queue = await Api.ExpectAsync(f, HttpMethod.Post, ModerationTestKit.Queue + "/enumerate", moderator.AccessToken, new { filters = new { scopeType = "Directory" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement item = queue.GetProperty("objects").EnumerateArray().First(o => Api.Str(o, "targetId") == reviewId);
            Check.Equal("VenueReview", Api.Str(item, "targetType"), "Queue item type");
            Check.Equal("Review Moderation Club", Api.Str(item.GetProperty("preview"), "title"), "Queue preview names the venue");
            JsonElement detail = await Api.ExpectAsync(f, HttpMethod.Get, ModerationTestKit.Queue + "/VenueReview/" + reviewId, moderator.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(detail.ToString().Contains("breaks the community rules", StringComparison.Ordinal), "Queue detail shows the review");
            ApiResult memberQueue = await Api.SendAsync(f, HttpMethod.Post, ModerationTestKit.Queue + "/enumerate", reporter.AccessToken, new { }, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.Forbidden, memberQueue.Status, "Members have no queue");

            await ModerationTestKit.ActOkAsync(f, moderator.AccessToken, "VenueReview", reviewId, "Hide", ct, "Harassing language").ConfigureAwait(false);
            Check.Equal(0, (await ListAsync(f, reporter.AccessToken, venueId, new { }, ct).ConfigureAwait(false)).GetProperty("objects").GetArrayLength(), "Hidden reviews leave member lists");
            Check.Equal(1, (await ListAsync(f, moderator.AccessToken, venueId, new { }, ct).ConfigureAwait(false)).GetProperty("objects").GetArrayLength(), "Staff still list hidden reviews");
            JsonElement venue = await VenueAsync(f, author.AccessToken, venueId, ct).ConfigureAwait(false);
            Check.Equal(0, venue.GetProperty("ratingCount").GetInt32(), "Hidden reviews leave the aggregates");
            Check.Equal("Hidden", Api.Str(venue.GetProperty("reviews").GetProperty("mine"), "moderationState"), "The author sees their hidden review");
            Check.True(!venue.GetProperty("viewer").GetProperty("canReview").GetBoolean(), "A hidden review still counts as the member's one review");
            await ModerationTestKit.ActOkAsync(f, moderator.AccessToken, "VenueReview", reviewId, "Unhide", ct, "Reconsidered").ConfigureAwait(false);
            Check.Equal(1, (await VenueAsync(f, author.AccessToken, venueId, ct).ConfigureAwait(false)).GetProperty("ratingCount").GetInt32(), "Unhide restores the aggregates");
            await ModerationTestKit.ActOkAsync(f, moderator.AccessToken, "VenueReview", reviewId, "Remove", ct, "Removed for harassment").ConfigureAwait(false);
            Check.Equal(1L, await ModerationTestKit.AuditCountAsync(f, "moderation.remove", reviewId, ct).ConfigureAwait(false), "Removal audited");
            await Api.ExpectErrorAsync(f, HttpMethod.Get, Reviews(venueId) + "/" + reviewId, author.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            Check.True((await NotificationTestKit.ListAsync(f, author.Id, ct).ConfigureAwait(false)).Any(n => n.Type == NotificationTypeEnum.ModerationAction), "Author notified of the removal");
            await ModerationTestKit.ActOkAsync(f, HttpFixture.AdminToken, "VenueReview", reviewId, "Restore", ct, "Restored on appeal").ConfigureAwait(false);
            Check.Equal(1, (await VenueAsync(f, author.AccessToken, venueId, ct).ConfigureAwait(false)).GetProperty("ratingCount").GetInt32(), "Restore brings the rating back");

            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            string adminReview = Api.Str(await ReviewAsync(f, admin, venueId, 5, "Staff review written by an admin.", ct).ConfigureAwait(false), "id");
            ApiResult protectedAct = await ModerationTestKit.ActAsync(f, moderator.AccessToken, "VenueReview", adminReview, "Hide", ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.Forbidden, protectedAct.Status, "Directory moderators cannot hide a SystemAdmin's review");

            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/moderation/word-filter", HttpFixture.AdminToken, new { term = "reviewblockword", matchType = "Word", action = "Block" }, HttpStatusCode.Created, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/moderation/word-filter", HttpFixture.AdminToken, new { term = "reviewflagword", matchType = "Word", action = "Flag" }, HttpStatusCode.Created, ct).ConfigureAwait(false);
            TestMember writer = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, Reviews(venueId), writer.AccessToken, new { rating = 2, body = "This has reviewblockword inside it." }, HttpStatusCode.BadRequest, "ContentFiltered", ct).ConfigureAwait(false);
            string flagged = Api.Str(await ReviewAsync(f, writer, venueId, 2, "This has reviewflagword inside it.", ct).ConfigureAwait(false), "id");
            Check.Equal(1L, await DirectoryTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM reports WHERE targetid = '" + flagged + "' AND reason = 'Filter' AND scopetype = 'Directory';", ct).ConfigureAwait(false), "Flag files a Directory system report");

            TestMember muted = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            await SafetyHelpers.UpdateUserAsync(f, muted.Id, u => { u.IsMuted = true; u.MutedUntilUtc = DateTime.UtcNow.AddDays(1); }, ct).ConfigureAwait(false);
            f.Services.Tokens.InvalidateUser(muted.Id);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, Reviews(venueId), muted.AccessToken, new { rating = 3, body = "Muted members cannot post this." }, HttpStatusCode.Forbidden, "Muted", ct).ConfigureAwait(false);
        }

        private static async Task BlocksAsync(HttpFixture f, CancellationToken ct)
        {
            string venueId = await DirectoryTestKit.VenueIdAsync(f, "Review Blocks Club", ct).ConfigureAwait(false);
            TestMember a = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            TestMember b = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            TestMember c = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            string aReview = Api.Str(await ReviewAsync(f, a, venueId, 4, "Review by the blocking member.", ct).ConfigureAwait(false), "id");
            string bReview = Api.Str(await ReviewAsync(f, b, venueId, 2, "Review by the blocked member.", ct).ConfigureAwait(false), "id");
            string bUpload = await ContentTestKit.MediaAsync(f, b.Id, MediaKindEnum.Image, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/venues/" + venueId + "/media", b.AccessToken, new { mediaIds = new[] { bUpload } }, HttpStatusCode.Created, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/users/" + b.Id + "/block", a.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);

            List<string> forA = DirectoryTestKit.Ids(await ListAsync(f, a.AccessToken, venueId, new { }, ct).ConfigureAwait(false));
            List<string> forB = DirectoryTestKit.Ids(await ListAsync(f, b.AccessToken, venueId, new { }, ct).ConfigureAwait(false));
            List<string> forC = DirectoryTestKit.Ids(await ListAsync(f, c.AccessToken, venueId, new { }, ct).ConfigureAwait(false));
            Check.True(!forA.Contains(bReview) && forA.Contains(aReview), "The blocker does not see the blocked member's review");
            Check.True(!forB.Contains(aReview) && forB.Contains(bReview), "The blocked member does not see the blocker's review");
            Check.Equal(2, forC.Count, "Others see both");
            await Api.ExpectErrorAsync(f, HttpMethod.Get, Reviews(venueId) + "/" + bReview, a.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            Check.Equal(0, (await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/venues/" + venueId + "/media/enumerate", a.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false)).GetProperty("objects").GetArrayLength(), "Blocked uploads hidden");
            Check.Equal(2, (await VenueAsync(f, a.AccessToken, venueId, ct).ConfigureAwait(false)).GetProperty("ratingCount").GetInt32(), "Aggregates count every live review");

            string blockedVenue = await DirectoryTestKit.VenueIdAsync(f, "Review Blocks Coach Club", ct).ConfigureAwait(false);
            TestMember coach = await LinkedInstructorAsync(f, blockedVenue, "Blocked Coach", ct).ConfigureAwait(false);
            string toCoach = Api.Str(await ReviewAsync(f, c, blockedVenue, 3, "A review the coach cannot answer.", ct).ConfigureAwait(false), "id");
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/users/" + coach.Id + "/block", c.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, Reviews(blockedVenue) + "/" + toCoach + "/reply", coach.AccessToken, new { body = "Thanks for visiting." }, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);

            f.Settings.Directory.ReviewsPerDay = 2;
            f.Settings.Directory.NewAccountReviewsPerDay = 1;
            TestMember fresh = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            string v1 = await DirectoryTestKit.VenueIdAsync(f, "Limit Club One", ct).ConfigureAwait(false);
            string v2 = await DirectoryTestKit.VenueIdAsync(f, "Limit Club Two", ct).ConfigureAwait(false);
            string v3 = await DirectoryTestKit.VenueIdAsync(f, "Limit Club Three", ct).ConfigureAwait(false);
            await ReviewAsync(f, fresh, v1, 5, "First review of the day here.", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, Reviews(v2), fresh.AccessToken, new { rating = 5, body = "Second review is over the new account limit." }, (HttpStatusCode)429, "RateLimited", ct).ConfigureAwait(false);
            f.Settings.Comments.NewAccountHours = 0;
            await ReviewAsync(f, fresh, v2, 5, "Second review once the account is established.", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, Reviews(v3), fresh.AccessToken, new { rating = 5, body = "Third review is over the daily limit." }, (HttpStatusCode)429, "RateLimited", ct).ConfigureAwait(false);
        }

        private static async Task MediaAsync(HttpFixture f, CancellationToken ct)
        {
            string venueId = await DirectoryTestKit.VenueIdAsync(f, "Media Club", ct).ConfigureAwait(false);
            string media = "/api/v1/venues/" + venueId + "/media";
            TestMember uploader = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            TestMember other = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            JsonElement venue = await VenueAsync(f, uploader.AccessToken, venueId, ct).ConfigureAwait(false);
            Check.True(venue.GetProperty("viewer").GetProperty("canAddMedia").GetBoolean() && !venue.GetProperty("viewer").GetProperty("canManageMedia").GetBoolean(), "Member media capabilities");

            string photo = await ContentTestKit.MediaAsync(f, uploader.Id, MediaKindEnum.Image, ct).ConfigureAwait(false);
            string second = await ContentTestKit.MediaAsync(f, uploader.Id, MediaKindEnum.Image, ct).ConfigureAwait(false);
            string notMine = await ContentTestKit.MediaAsync(f, other.Id, MediaKindEnum.Image, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, media, uploader.AccessToken, new { mediaIds = Array.Empty<string>() }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, media, uploader.AccessToken, new { mediaIds = new[] { notMine } }, HttpStatusCode.Conflict, "MediaNotAttachable", ct).ConfigureAwait(false);
            JsonElement added = await Api.ExpectAsync(f, HttpMethod.Post, media, uploader.AccessToken, new { mediaIds = new[] { photo, second }, labels = new[] { "interior" } }, HttpStatusCode.Created, ct).ConfigureAwait(false);
            List<JsonElement> items = added.GetProperty("objects").EnumerateArray().ToList();
            Check.Equal(2, items.Count, "Two items added");
            string itemId = Api.Str(items[0], "id");
            string secondItem = Api.Str(items[1], "id");
            Check.True(itemId.StartsWith("vmd_", StringComparison.Ordinal), "Item id prefix");
            Check.Equal(photo, Api.Str(items[0].GetProperty("media"), "id"), "Item carries its media");
            Check.True(items[0].GetProperty("labels").EnumerateArray().Any(l => l.GetString() == "interior"), "Labels applied");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, media, uploader.AccessToken, new { mediaIds = new[] { photo } }, HttpStatusCode.Conflict, "MediaNotAttachable", ct).ConfigureAwait(false);
            Check.Equal(2, (await VenueAsync(f, other.AccessToken, venueId, ct).ConfigureAwait(false)).GetProperty("mediaCount").GetInt32(), "mediaCount");

            JsonElement list = await Api.ExpectAsync(f, HttpMethod.Post, media + "/enumerate", other.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(2, list.GetProperty("objects").GetArrayLength(), "Other members list the items");
            Check.True(!list.GetProperty("objects")[0].GetProperty("viewer").GetProperty("canRemove").GetBoolean(), "Other members cannot remove");
            JsonElement labelled = await Api.ExpectAsync(f, HttpMethod.Post, media + "/enumerate", other.AccessToken, new { labels = new[] { "interior" }, filters = new { kind = "Image" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(2, labelled.GetProperty("objects").GetArrayLength(), "Label and kind filters");
            await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/media/" + photo, other.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Delete, media + "/" + itemId, other.AccessToken, null, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, media + "/enumerate", null, new { }, HttpStatusCode.Unauthorized, "Unauthorized", ct).ConfigureAwait(false);

            await ModerationTestKit.ReportOkAsync(f, other.AccessToken, "VenueMedia", secondItem, ct, "SexualContent").ConfigureAwait(false);
            Check.Equal(1L, await DirectoryTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM reports WHERE targetid = '" + secondItem + "' AND scopetype = 'Directory';", ct).ConfigureAwait(false), "Media report in the Directory scope");

            await Api.ExpectAsync(f, HttpMethod.Delete, media + "/" + itemId, uploader.AccessToken, null, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
            Check.Equal("t", (await f.Raw.ExecuteQueryAsync("SELECT (deletedutc IS NOT NULL)::text AS d FROM mediaattachments WHERE id = '" + photo + "';", false, ct).ConfigureAwait(false)).Rows[0]["d"].ToString()!.Substring(0, 1), "The uploader's delete purges the media");
            Check.Equal(1, (await VenueAsync(f, other.AccessToken, venueId, ct).ConfigureAwait(false)).GetProperty("mediaCount").GetInt32(), "mediaCount after delete");

            TestMember moderator = await DirectoryTestKit.ModeratorAsync(f, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Delete, media + "/" + secondItem, moderator.AccessToken, new { reason = "Not this venue" }, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
            Check.Equal(1L, await ModerationTestKit.AuditCountAsync(f, "venue.media.remove", secondItem, ct).ConfigureAwait(false), "Staff removal audited");
            Check.Equal("Removed", await ModerationTestKit.StateAsync(f, "venuemedia", secondItem, ct).ConfigureAwait(false), "Staff removal sets Removed");
            Check.Equal(0L, await DirectoryTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM reports WHERE targetid = '" + secondItem + "' AND status = 'Open';", ct).ConfigureAwait(false), "Removal resolves open reports");
            Check.Equal(0, (await Api.ExpectAsync(f, HttpMethod.Post, media + "/enumerate", other.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false)).GetProperty("objects").GetArrayLength(), "Removed items leave member lists");
            Check.Equal(1, (await Api.ExpectAsync(f, HttpMethod.Post, media + "/enumerate", moderator.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false)).GetProperty("objects").GetArrayLength(), "Staff list removed items");
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/media/" + second, other.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);

            TestMember coach = await LinkedInstructorAsync(f, venueId, "Media Coach", ct).ConfigureAwait(false);
            string third = await ContentTestKit.MediaAsync(f, other.Id, MediaKindEnum.Image, ct).ConfigureAwait(false);
            string thirdItem = Api.Str((await Api.ExpectAsync(f, HttpMethod.Post, media, other.AccessToken, new { mediaIds = new[] { third } }, HttpStatusCode.Created, ct).ConfigureAwait(false)).GetProperty("objects")[0], "id");
            JsonElement coachList = await Api.ExpectAsync(f, HttpMethod.Post, media + "/enumerate", coach.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(coachList.GetProperty("objects")[0].GetProperty("viewer").GetProperty("canRemove").GetBoolean(), "Linked instructors may remove");
            await Api.ExpectAsync(f, HttpMethod.Delete, media + "/" + thirdItem, coach.AccessToken, null, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
            Check.Equal("Removed", await ModerationTestKit.StateAsync(f, "venuemedia", thirdItem, ct).ConfigureAwait(false), "Instructor removal sets Removed");

            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            string adminPhoto = await ContentTestKit.MediaAsync(f, admin.Id, MediaKindEnum.Image, ct).ConfigureAwait(false);
            string adminItem = Api.Str((await Api.ExpectAsync(f, HttpMethod.Post, media, admin.AccessToken, new { mediaIds = new[] { adminPhoto } }, HttpStatusCode.Created, ct).ConfigureAwait(false)).GetProperty("objects")[0], "id");
            await Api.ExpectErrorAsync(f, HttpMethod.Delete, media + "/" + adminItem, coach.AccessToken, null, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);

            await DirectoryTestKit.SettingsAsync(f, new { values = new Dictionary<string, object> { { "directory.memberMediaEnabled", false } } }, ct).ConfigureAwait(false);
            string late = await ContentTestKit.MediaAsync(f, uploader.Id, MediaKindEnum.Image, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, media, uploader.AccessToken, new { mediaIds = new[] { late } }, HttpStatusCode.Forbidden, "MemberMediaDisabled", ct).ConfigureAwait(false);
            Check.True(!(await VenueAsync(f, uploader.AccessToken, venueId, ct).ConfigureAwait(false)).GetProperty("viewer").GetProperty("canAddMedia").GetBoolean(), "canAddMedia off with the switch");
            await DirectoryTestKit.SettingsAsync(f, new { values = new Dictionary<string, object> { { "directory.memberMediaEnabled", true } } }, ct).ConfigureAwait(false);

            await f.Services.Directory.EraseForAccountAsync(other.Id, ct).ConfigureAwait(false);
            Check.Equal(0L, await DirectoryTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM venuemedia WHERE uploaderuserid = '" + other.Id + "' AND deletedutc IS NULL;", ct).ConfigureAwait(false), "Account deletion removes the member's items");
        }

        #endregion
    }
}

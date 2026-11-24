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
    using TrellisKit.Core.Models;
    using TrellisKit.Server.Services.ContentReview;

    /// <summary>
    /// The content changelog and its Terms of Service review (docs/INTEGRATION.md content review contract, Phase 4a): the
    /// triggers write one row per create, edit, and delete with the actor and client, in the same transaction as the
    /// change; the content.review job marks rows Clean or Flagged from the Moderation role's reply and notifies
    /// administrators about flags; the admin queue filters and routes its actions through moderation; members are refused.
    /// </summary>
    public static class ContentReviewSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "ContentReview";

        #endregion

        #region Private-Members

        private const string Queue = "/api/v1/admin/content-review";
        private const string Clean = "{\"verdict\":\"Clean\",\"categories\":[],\"severity\":\"None\",\"note\":\"Ordinary training talk.\"}";
        private const string Flagged = "Here is the verdict: {\"verdict\":\"Flagged\",\"categories\":[\"HarassmentAndHate\",\"threats\"],\"severity\":\"High\",\"note\":\"Threatens another member.\"}";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Content changelog and Terms of Service review (" + TestEnvironment.Provider + ")", new[]
            {
                Case("Changelog.Contributions", "A post, comment, thread, message, and profile edit each write one row with the right action, actor, client, and snapshot; counter updates write none", ContributionsAsync),
                Case("Changelog.Deletes", "Deleting a post and a message writes Delete rows (no text kept, nothing to review), and edits write Update rows", DeletesAsync),
                Case("Changelog.SameTransaction", "A failed transaction that inserted content leaves no row, a committed one leaves exactly one, and writes outside a request fall back to the author and the System client", SameTransactionAsync),
                AiTestKit.Case(Id, "Review.CleanAndFlagged", "The content.review job marks rows Clean or Flagged from the Moderation role's reply, records the model, notifies administrators once per flag, and appears in the call ledger", ReviewAsync),
                AiTestKit.Case(Id, "Review.WaitsAndRetries", "Without executors the job plans nothing and the summary says why; unreadable verdicts count attempts and the row is flagged for an administrator after contentReview.maxAttempts", WaitsAsync,
                    s => s.ContentReview.MaxAttempts = 2),
                Case("Queue.FiltersAndActions", "The admin queue filters by status, category, severity, targetType, client, and actor; Hide, Warn, Mute, Ban, and Dismiss route through moderation and member actions; members are refused with 403", QueueAsync),
                Case("Digest.EmailsAdministrators", "The daily digest emails administrators when something was flagged in the last day, once per day, and the review jobs are registered and enabled", DigestAsync)
            });
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string caseId, string name, Func<HttpFixture, CancellationToken, Task> body)
        {
            return SuiteCases.Http(Id, caseId, name, (f, ct) =>
            {
                ForumTestKit.Relax(f);
                return body(f, ct);
            });
        }

        private static async Task<List<JsonElement>> RowsAsync(HttpFixture f, object filters, CancellationToken ct, string? bearer = null)
        {
            JsonElement page = await Api.ExpectAsync(f, HttpMethod.Post, Queue + "/enumerate", bearer ?? HttpFixture.AdminToken, new { maxResults = 100, filters }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            return page.GetProperty("objects").EnumerateArray().ToList();
        }

        private static async Task<JsonElement> OneAsync(HttpFixture f, string targetType, string targetId, CancellationToken ct, string action = "Create")
        {
            List<JsonElement> rows = await RowsAsync(f, new { targetType = new[] { targetType }, targetId, action = new[] { action } }, ct).ConfigureAwait(false);
            Check.Equal(1, rows.Count, "One " + action + " row for " + targetType + " " + targetId);
            return rows[0];
        }

        private static string? Actor(JsonElement row)
        {
            JsonElement actor = row.GetProperty("actor");
            return actor.ValueKind == JsonValueKind.Null ? null : Api.Str(actor, "id");
        }

        private static async Task ContributionsAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember poster = await ContentTestKit.PosterAsync(f, ct).ConfigureAwait(false);
            TestMember other = await ContentTestKit.MemberAsync(f, ct, "Changelog Commenter").ConfigureAwait(false);

            JsonElement post = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/feed", poster.AccessToken, new { body = "Morning session recap CHANGELOG-POST" }, HttpStatusCode.Created, ct, "mobile").ConfigureAwait(false);
            string postId = Api.Str(post, "id");
            JsonElement postRow = await OneAsync(f, "FeedPost", postId, ct).ConfigureAwait(false);
            Check.Equal(poster.Id, Actor(postRow), "Post actor");
            Check.Equal("Mobile", Api.Str(postRow, "client"), "X-Client mobile");
            Check.True(Api.Str(postRow, "snapshot").Contains("CHANGELOG-POST", StringComparison.Ordinal), "Post text in the snapshot");
            Check.Equal("Pending", Api.Str(postRow, "reviewStatus"), "Waits for review");

            JsonElement comment = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/feed/" + postId + "/comments", other.AccessToken, new { body = "Great work CHANGELOG-COMMENT" }, HttpStatusCode.Created, ct, "web").ConfigureAwait(false);
            JsonElement commentRow = await OneAsync(f, "Comment", Api.Str(comment, "id"), ct).ConfigureAwait(false);
            Check.Equal(other.Id, Actor(commentRow), "Comment actor");
            Check.Equal("Web", Api.Str(commentRow, "client"), "X-Client web");
            Check.Equal("FeedPost", Api.Str(commentRow, "contextType"), "Comment context type");
            Check.Equal(postId, Api.Str(commentRow, "contextId"), "Comment context id");
            Check.Equal(1, (await RowsAsync(f, new { targetId = postId }, ct).ConfigureAwait(false)).Count, "The post's comment counter update wrote no row");

            string forumId = await ForumTestKit.DefaultForumIdAsync(f, other.AccessToken, ct).ConfigureAwait(false);
            ApiResult thread = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/forums/" + forumId + "/threads", other.AccessToken, new { type = "Discussion", title = "Changelog thread", body = "Thread body CHANGELOG-THREAD" }, ct,
                headers: new Dictionary<string, string> { { "X-TK-Client", "api" } }).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.Created, thread.Status, "Thread created (" + thread.Text + ")");
            JsonElement threadRow = await OneAsync(f, "Thread", Api.Str(thread.Body, "id"), ct).ConfigureAwait(false);
            Check.Equal("Api", Api.Str(threadRow, "client"), "X-TK-Client wins over X-Client");
            Check.Equal(other.Id, Actor(threadRow), "Thread actor");
            Check.True(Api.Str(threadRow, "snapshot").Contains("title: Changelog thread", StringComparison.Ordinal), "Thread title in the snapshot");

            string conversation = await MessagingTestKit.PairAsync(f, poster, other, ct).ConfigureAwait(false);
            JsonElement message = await MessagingTestKit.SendOkAsync(f, poster, conversation, "See you Thursday CHANGELOG-MESSAGE", ct).ConfigureAwait(false);
            JsonElement messageRow = await OneAsync(f, "Message", Api.Str(message, "id"), ct).ConfigureAwait(false);
            Check.Equal(poster.Id, Actor(messageRow), "Message actor");
            Check.Equal("Conversation", Api.Str(messageRow, "contextType"), "Message context");
            Check.Equal(conversation, Api.Str(messageRow, "contextId"), "Conversation id");

            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/me/profile", other.AccessToken, new { bio = "Coach and competitor CHANGELOG-BIO" }, HttpStatusCode.OK, ct, "web").ConfigureAwait(false);
            List<JsonElement> profile = await RowsAsync(f, new { targetType = new[] { "User" }, targetId = other.Id, action = new[] { "Update" } }, ct).ConfigureAwait(false);
            JsonElement bio = profile.First(r => Api.Str(r, "snapshot").Contains("CHANGELOG-BIO", StringComparison.Ordinal));
            Check.Equal(other.Id, Actor(bio), "Profile actor");
            Check.Equal("Web", Api.Str(bio, "client"), "Profile client");

            List<JsonElement> mine = await RowsAsync(f, new { actorUserId = other.Id, client = new[] { "Web" } }, ct).ConfigureAwait(false);
            Check.True(mine.Any(r => Api.Str(r, "targetType") == "Comment") && mine.All(r => Api.Str(r, "client") == "Web"), "Actor and client filters");
        }

        private static async Task DeletesAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember poster = await ContentTestKit.PosterAsync(f, ct).ConfigureAwait(false);
            TestMember other = await ContentTestKit.MemberAsync(f, ct, "Changelog Friend").ConfigureAwait(false);
            string postId = await ContentTestKit.PostIdAsync(f, poster.AccessToken, "First draft", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/feed/" + postId, poster.AccessToken, new { body = "Second draft CHANGELOG-EDIT" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement edit = await OneAsync(f, "FeedPost", postId, ct, "Update").ConfigureAwait(false);
            Check.True(Api.Str(edit, "snapshot").Contains("CHANGELOG-EDIT", StringComparison.Ordinal), "The edit's text");
            Check.Equal("Pending", Api.Str(edit, "reviewStatus"), "Edits are reviewed");

            await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/feed/" + postId, poster.AccessToken, null, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
            JsonElement deleted = await OneAsync(f, "FeedPost", postId, ct, "Delete").ConfigureAwait(false);
            Check.Equal(poster.Id, Actor(deleted), "Delete actor");
            Check.Equal(String.Empty, Api.Str(deleted, "snapshot"), "No text kept on the Delete row");
            Check.Equal("Clean", Api.Str(deleted, "reviewStatus"), "Nothing to review");
            Check.Equal("System", Api.Str(deleted, "reviewerKind"), "Reviewed by the engine");

            string conversation = await MessagingTestKit.PairAsync(f, poster, other, ct).ConfigureAwait(false);
            string messageId = Api.Str(await MessagingTestKit.SendOkAsync(f, poster, conversation, "Oops CHANGELOG-GONE", ct).ConfigureAwait(false), "id");
            await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/messages/" + messageId, poster.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement gone = await OneAsync(f, "Message", messageId, ct, "Delete").ConfigureAwait(false);
            Check.Equal(poster.Id, Actor(gone), "Message delete actor");
            await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/messages/" + messageId, poster.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await OneAsync(f, "Message", messageId, ct, "Delete").ConfigureAwait(false);
        }

        private static async Task SameTransactionAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember author = await ContentTestKit.MemberAsync(f, ct, "Changelog Author").ConfigureAwait(false);
            string property = f.Services.PropertyId;
            string failed = "fpo_" + Guid.NewGuid().ToString("N").Substring(0, 24);
            string Insert(string id, string body)
            {
                return "INSERT INTO feedposts (propertyid, id, authoruserid, kind, body, excerpt, visibility, createdutc, updatedutc) VALUES ('" + property + "', '" + id + "', '" + author.Id +
                    "', 'Original', '" + body + "', '" + body + "', 'Members', NOW(), NOW());";
            }
            bool threw = false;
            try
            {
                await f.Raw.ExecuteQueriesAsync(new List<string> { Insert(failed, "never committed"), Insert(failed, "duplicate key") }, true, ct).ConfigureAwait(false);
            }
            catch (Exception)
            {
                threw = true;
            }
            Check.True(threw, "The second insert failed the transaction");
            Check.Equal(0L, await ContentTestKit.ScalarAsync(f, "SELECT COUNT(*) AS cnt FROM feedposts WHERE id = '" + failed + "';", ct).ConfigureAwait(false), "No post");
            Check.Equal(0L, await ContentTestKit.ScalarAsync(f, "SELECT COUNT(*) AS cnt FROM contentchanges WHERE targetid = '" + failed + "';", ct).ConfigureAwait(false), "No changelog row escapes the rollback");

            string committed = "fpo_" + Guid.NewGuid().ToString("N").Substring(0, 24);
            await f.Raw.ExecuteQueriesAsync(new List<string> { Insert(committed, "committed body") }, true, ct).ConfigureAwait(false);
            JsonElement row = await OneAsync(f, "FeedPost", committed, ct).ConfigureAwait(false);
            Check.Equal("System", Api.Str(row, "client"), "No request: the System client");
            Check.Equal(author.Id, Actor(row), "No request: the author is the actor");
            Check.Equal(author.Id, Api.Str(row.GetProperty("author"), "id"), "Author summary");

            ContentChangeContext.Current = new ContentChangeContext("usr_attributed", ContentChangeClientEnum.Web);
            try
            {
                string attributed = "fpo_" + Guid.NewGuid().ToString("N").Substring(0, 24);
                await f.Raw.ExecuteQueriesAsync(new List<string> { Insert(attributed, "attributed body") }, true, ct).ConfigureAwait(false);
                string reviewless = "fpo_" + Guid.NewGuid().ToString("N").Substring(0, 24);
                await f.Raw.ExecuteQueryAsync(Insert(reviewless, "autocommit body"), false, ct).ConfigureAwait(false);
                DataTable rows = await f.Raw.ExecuteQueryAsync("SELECT targetid, actoruserid, client FROM contentchanges WHERE targetid IN ('" + attributed + "', '" + reviewless + "');", false, ct).ConfigureAwait(false);
                Check.Equal(2, rows.Rows.Count, "Both attributed");
                foreach (DataRow r in rows.Rows)
                {
                    Check.Equal("usr_attributed", RowReader.GetString(r, "actoruserid"), "Transaction-local and session settings carry the actor");
                    Check.Equal("Web", RowReader.GetString(r, "client"), "And the client");
                }
            }
            finally
            {
                ContentChangeContext.Current = null;
            }
            string after = "fpo_" + Guid.NewGuid().ToString("N").Substring(0, 24);
            await f.Raw.ExecuteQueryAsync(Insert(after, "after body"), false, ct).ConfigureAwait(false);
            DataTable leftover = await f.Raw.ExecuteQueryAsync("SELECT actoruserid, client FROM contentchanges WHERE targetid = '" + after + "';", false, ct).ConfigureAwait(false);
            Check.Equal(author.Id, RowReader.GetString(leftover.Rows[0], "actoruserid"), "A pooled connection does not carry the previous actor");
            Check.Equal("System", RowReader.GetString(leftover.Rows[0], "client"), "Nor the previous client");
        }

        private static async Task<string> ModerationRoleAsync(HttpFixture f, FakeModelServer model, CancellationToken ct)
        {
            string endpoint = await AiTestKit.EndpointAsync(f, AiTestKit.OllamaEndpoint(model, "Reviewer " + Guid.NewGuid().ToString("N").Substring(0, 6)), ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/ai/roles/Moderation", HttpFixture.AdminToken, new { executors = new object[] { new { runtime = "ApiEndpoint", endpointId = endpoint } } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            return endpoint;
        }

        private static async Task<JobRun> RunJobAsync(HttpFixture f, string key, CancellationToken ct)
        {
            JsonElement run = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/jobs/" + key + "/run", HttpFixture.AdminToken, new { }, HttpStatusCode.Created, ct).ConfigureAwait(false);
            string runId = Api.Str(run, "id");
            JobRun? current = null;
            for (int pass = 0; pass < 6; pass++)
            {
                await f.Services.Jobs.ProcessAsync(ct).ConfigureAwait(false);
                current = await f.Services.Database.Jobs.ReadRunAsync(runId, ct).ConfigureAwait(false);
                if (current != null && current.Status != JobRunStatusEnum.Queued && current.Status != JobRunStatusEnum.Running) break;
            }
            return current!;
        }

        private static async Task ReviewAsync(HttpFixture f, FakeModelServer model, CancellationToken ct)
        {
            ForumTestKit.Relax(f);
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            await ModerationRoleAsync(f, model, ct).ConfigureAwait(false);
            TestMember poster = await ContentTestKit.PosterAsync(f, ct).ConfigureAwait(false);

            model.ReplyText = Clean;
            string postId = await ContentTestKit.PostIdAsync(f, poster.AccessToken, "Hip escape drills today CHANGELOG-CLEAN", ct).ConfigureAwait(false);
            JobRun first = await RunJobAsync(f, ContentReviewService.ReviewJobKey, ct).ConfigureAwait(false);
            Check.Equal(JobRunStatusEnum.Succeeded, first.Status, "The review run finished");
            JsonElement clean = await OneAsync(f, "FeedPost", postId, ct).ConfigureAwait(false);
            Check.Equal("Clean", Api.Str(clean, "reviewStatus"), "Clean verdict");
            Check.Equal("model:fake-chat", Api.Str(clean, "reviewedBy"), "Reviewed by the model");
            Check.Equal("Model", Api.Str(clean, "reviewerKind"), "Model reviewer");
            Check.Equal("None", Api.Str(clean, "severity"), "No severity");
            Check.True(clean.GetProperty("reviewedUtc").ValueKind != JsonValueKind.Null, "reviewedUtc set");
            Check.True(model.Requests.Any(r => r.Body.Contains("CHANGELOG-CLEAN", StringComparison.Ordinal) && r.Body.Contains("Terms of Service", StringComparison.Ordinal)), "The reviewer prompt carried the text");

            model.ReplyText = Flagged;
            string commentId = await ContentTestKit.CommentIdAsync(f, poster.AccessToken, postId, "I will hurt you CHANGELOG-FLAG", null, ct).ConfigureAwait(false);
            await RunJobAsync(f, ContentReviewService.ReviewJobKey, ct).ConfigureAwait(false);
            JsonElement flagged = await OneAsync(f, "Comment", commentId, ct).ConfigureAwait(false);
            Check.Equal("Flagged", Api.Str(flagged, "reviewStatus"), "Flagged verdict");
            Check.Equal("High", Api.Str(flagged, "severity"), "Severity");
            Check.Equal("HarassmentAndHate", String.Join(",", flagged.GetProperty("categories").EnumerateArray().Select(c => c.GetString())), "Categories (aliases folded)");
            Check.Equal("Threatens another member.", Api.Str(flagged, "reviewerNote"), "Reviewer note");
            Check.True(flagged.GetProperty("notifiedUtc").ValueKind != JsonValueKind.Null, "Administrators notified");

            List<Notification> notes = (await NotificationTestKit.ListAsync(f, admin.Id, ct).ConfigureAwait(false)).Where(n => n.Type == NotificationTypeEnum.ContentFlagged).ToList();
            Check.Equal(1, notes.Count, "One notification for the flagged row");
            Check.Equal(Api.Str(flagged, "id"), notes[0].TargetId, "It points at the row");
            Check.True(notes[0].Path.StartsWith("/admin/content-review", StringComparison.Ordinal), "It opens the queue");
            Check.Equal(PushStateEnum.Pending, notes[0].PushState, "A push is queued");
            Check.True(notes[0].Title.Contains("harassment", StringComparison.Ordinal), "Title names the category");

            await RunJobAsync(f, ContentReviewService.ReviewJobKey, ct).ConfigureAwait(false);
            Check.Equal(1, (await NotificationTestKit.ListAsync(f, admin.Id, ct).ConfigureAwait(false)).Count(n => n.Type == NotificationTypeEnum.ContentFlagged), "Never notified twice");

            JsonElement calls = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/ai/calls/enumerate", HttpFixture.AdminToken, new { filters = new { role = new[] { "Moderation" }, jobKey = ContentReviewService.ReviewJobKey } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(calls.GetProperty("objects").GetArrayLength() >= 2, "Each review is in the call ledger under the job");
            JsonElement summary = await Api.ExpectAsync(f, HttpMethod.Get, Queue + "/summary", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(summary.GetProperty("reviewerReady").GetBoolean(), "Reviewer ready");
            Check.True(summary.GetProperty("flagged").GetInt64() >= 1, "Flagged count");
        }

        private static async Task WaitsAsync(HttpFixture f, FakeModelServer model, CancellationToken ct)
        {
            ForumTestKit.Relax(f);
            TestMember poster = await ContentTestKit.PosterAsync(f, ct).ConfigureAwait(false);
            string postId = await ContentTestKit.PostIdAsync(f, poster.AccessToken, "Waiting for a reviewer", ct).ConfigureAwait(false);
            JsonElement summary = await Api.ExpectAsync(f, HttpMethod.Get, Queue + "/summary", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(!summary.GetProperty("reviewerReady").GetBoolean(), "Not ready");
            Check.Equal("NoExecutors", Api.Str(summary, "reviewerProblem"), "The role has no executors");
            Check.True(Api.Str(summary, "reviewerMessage").Contains("Moderation", StringComparison.Ordinal), "The queue says why");
            JobRun idle = await RunJobAsync(f, ContentReviewService.ReviewJobKey, ct).ConfigureAwait(false);
            Check.Equal(0, idle.ItemsTotal, "Nothing planned without executors");
            Check.Equal("Pending", Api.Str(await OneAsync(f, "FeedPost", postId, ct).ConfigureAwait(false), "reviewStatus"), "The row waits");
            Check.Equal(0, model.Requests.Count(r => r.Path.Contains("chat", StringComparison.Ordinal)), "No model call");

            await ModerationRoleAsync(f, model, ct).ConfigureAwait(false);
            model.ReplyText = "I am not sure what to say.";
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/jobs/" + ContentReviewService.ReviewJobKey, HttpFixture.AdminToken, new { maxAttempts = 1 }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JobRun unreadable = await RunJobAsync(f, ContentReviewService.ReviewJobKey, ct).ConfigureAwait(false);
            Check.True(unreadable.ItemsTotal >= 1 && unreadable.ItemsDead + unreadable.ItemsFailed == unreadable.ItemsTotal, "Unreadable verdicts fail the items");
            Check.Equal("Pending", Api.Str(await OneAsync(f, "FeedPost", postId, ct).ConfigureAwait(false), "reviewStatus"), "Still Pending after one unreadable verdict");
            await RunJobAsync(f, ContentReviewService.ReviewJobKey, ct).ConfigureAwait(false);
            JsonElement row = await OneAsync(f, "FeedPost", postId, ct).ConfigureAwait(false);
            Check.Equal("Flagged", Api.Str(row, "reviewStatus"), "Flagged for an administrator after contentReview.maxAttempts unreadable verdicts");
            Check.Equal("System", Api.Str(row, "reviewerKind"), "By the engine");
            Check.True(row.GetProperty("reviewAttempts").GetInt32() >= 2, "Attempts counted");
            Check.True(Api.Str(row, "lastError").StartsWith("UnreadableVerdict", StringComparison.Ordinal), "Last error recorded");
            Check.True(Api.Str(row, "reviewerNote").Contains("could not classify", StringComparison.Ordinal), "Note explains");
        }

        private static async Task<string> FlagAsync(HttpFixture f, string targetType, string targetId, ContentReviewCategoryEnum category, ContentReviewSeverityEnum severity, CancellationToken ct)
        {
            List<JsonElement> rows = await RowsAsync(f, new { targetType = new[] { targetType }, targetId, action = new[] { "Create" } }, ct).ConfigureAwait(false);
            string id = Api.Str(rows.Single(), "id");
            bool saved = await f.Services.Database.ContentChanges.CompleteReviewAsync(id, ContentReviewStatusEnum.Flagged, new List<ContentReviewCategoryEnum> { category }, severity, "Test flag",
                "model:test", ContentReviewerKindEnum.Model, DateTime.UtcNow, ct).ConfigureAwait(false);
            Check.True(saved, "Flagged " + targetType);
            return id;
        }

        private static async Task QueueAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember poster = await ContentTestKit.PosterAsync(f, ct, "Queue Poster").ConfigureAwait(false);
            TestMember commenter = await ContentTestKit.MemberAsync(f, ct, "Queue Commenter").ConfigureAwait(false);
            TestMember muted = await ContentTestKit.MemberAsync(f, ct, "Queue Muted").ConfigureAwait(false);
            TestMember banned = await ContentTestKit.MemberAsync(f, ct, "Queue Banned").ConfigureAwait(false);
            string postId = await ContentTestKit.PostIdAsync(f, poster.AccessToken, "Buy followers now", ct).ConfigureAwait(false);
            string commentId = Api.Str(await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/feed/" + postId + "/comments", commenter.AccessToken, new { body = "You are useless" }, HttpStatusCode.Created, ct, "web").ConfigureAwait(false), "id");
            string mutedComment = await ContentTestKit.CommentIdAsync(f, muted.AccessToken, postId, "Spam spam spam", null, ct).ConfigureAwait(false);
            string bannedComment = await ContentTestKit.CommentIdAsync(f, banned.AccessToken, postId, "Send me your card number", null, ct).ConfigureAwait(false);
            string dismissed = await ContentTestKit.CommentIdAsync(f, commenter.AccessToken, postId, "Borderline joke", null, ct).ConfigureAwait(false);

            string postRow = await FlagAsync(f, "FeedPost", postId, ContentReviewCategoryEnum.Spam, ContentReviewSeverityEnum.Medium, ct).ConfigureAwait(false);
            string commentRow = await FlagAsync(f, "Comment", commentId, ContentReviewCategoryEnum.HarassmentAndHate, ContentReviewSeverityEnum.High, ct).ConfigureAwait(false);
            string mutedRow = await FlagAsync(f, "Comment", mutedComment, ContentReviewCategoryEnum.Spam, ContentReviewSeverityEnum.Low, ct).ConfigureAwait(false);
            string bannedRow = await FlagAsync(f, "Comment", bannedComment, ContentReviewCategoryEnum.Scam, ContentReviewSeverityEnum.Critical, ct).ConfigureAwait(false);
            string dismissedRow = await FlagAsync(f, "Comment", dismissed, ContentReviewCategoryEnum.HarassmentAndHate, ContentReviewSeverityEnum.Low, ct).ConfigureAwait(false);

            List<JsonElement> flagged = await RowsAsync(f, new { status = new[] { "Flagged" } }, ct).ConfigureAwait(false);
            Check.Equal(5, flagged.Count, "Status filter");
            Check.Equal(2, (await RowsAsync(f, new { category = new[] { "Spam" } }, ct).ConfigureAwait(false)).Count, "Category filter");
            Check.Equal(2, (await RowsAsync(f, new { category = "HarassmentAndHate" }, ct).ConfigureAwait(false)).Count, "Category filter (comma form)");
            Check.Equal(commentRow, Api.Str((await RowsAsync(f, new { severity = new[] { "High" } }, ct).ConfigureAwait(false)).Single(), "id"), "Severity filter");
            Check.Equal(2, (await RowsAsync(f, new { minimumSeverity = "High" }, ct).ConfigureAwait(false)).Count, "Minimum severity filter");
            Check.Equal(4, (await RowsAsync(f, new { status = new[] { "Flagged" }, targetType = new[] { "Comment" } }, ct).ConfigureAwait(false)).Count, "Target type filter");
            Check.True((await RowsAsync(f, new { client = new[] { "Web" }, status = new[] { "Flagged" } }, ct).ConfigureAwait(false)).Select(r => Api.Str(r, "id")).SequenceEqual(new[] { commentRow }), "Client filter");
            Check.Equal(2, (await RowsAsync(f, new { actorUserId = commenter.Id, status = new[] { "Flagged" } }, ct).ConfigureAwait(false)).Count, "Actor filter");
            JsonElement ordered = await Api.ExpectAsync(f, HttpMethod.Post, Queue + "/enumerate", HttpFixture.AdminToken, new { ordering = "SeverityDescending", filters = new { status = new[] { "Flagged" } } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(bannedRow, Api.Str(ordered.GetProperty("objects")[0], "id"), "Critical first");
            JsonElement viaGet = await Api.ExpectAsync(f, HttpMethod.Get, Queue + "?status=Flagged&severity=Critical", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(1, viaGet.GetProperty("objects").GetArrayLength(), "GET form filters");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, Queue + "/enumerate", HttpFixture.AdminToken, new { filters = new { status = new[] { "Maybe" } } }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, Queue + "/enumerate", HttpFixture.AdminToken, new { filters = new { category = new[] { "Gossip" } } }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);

            JsonElement detail = await Api.ExpectAsync(f, HttpMethod.Get, Queue + "/" + postRow, HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("FeedPost", Api.Str(detail, "moderationTargetType"), "Hide and remove apply to posts");
            Check.Equal(poster.Id, Api.Str(detail.GetProperty("author"), "id"), "Author summary");
            await Api.ExpectErrorAsync(f, HttpMethod.Get, Queue + "/chg_missing", HttpFixture.AdminToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);

            await Api.ExpectErrorAsync(f, HttpMethod.Post, Queue + "/" + postRow + "/action", HttpFixture.AdminToken, new { action = "Hide" }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, Queue + "/" + postRow + "/action", HttpFixture.AdminToken, new { action = "Shout", reason = "Spam" }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            JsonElement hid = await Api.ExpectAsync(f, HttpMethod.Post, Queue + "/" + postRow + "/action", HttpFixture.AdminToken, new { action = "Hide", reason = "Spam promotion" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Actioned", Api.Str(hid.GetProperty("change"), "reviewStatus"), "Actioned");
            string hideAction = Api.Str(hid, "moderationActionId");
            Check.Equal(hideAction, Api.Str(hid.GetProperty("change"), "moderationActionId"), "Moderation action kept on the row");
            Check.Equal("Hidden", await ModerationTestKit.StateAsync(f, "feedposts", postId, ct).ConfigureAwait(false), "The post is hidden by the moderation action");
            Check.Equal(1L, await ModerationTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM moderationactions WHERE id = '" + hideAction + "' AND action = 'Hide' AND targetid = '" + postId + "';", ct).ConfigureAwait(false), "Moderation action recorded");
            Check.True(await ModerationTestKit.AuditCountAsync(f, "contentReview.hide", postRow, ct).ConfigureAwait(false) >= 1, "Audited");

            JsonElement warned = await Api.ExpectAsync(f, HttpMethod.Post, Queue + "/" + commentRow + "/action", HttpFixture.AdminToken, new { action = "Warn", reason = "Be respectful" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(1L, await ModerationTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM moderationactions WHERE id = '" + Api.Str(warned, "moderationActionId") + "' AND action = 'Warn' AND targetauthoruserid = '" + commenter.Id + "';", ct).ConfigureAwait(false), "Warn recorded");
            Notification warning = (await NotificationTestKit.ListAsync(f, commenter.Id, ct).ConfigureAwait(false)).Single(n => n.Type == NotificationTypeEnum.ModerationAction);
            Check.True(warning.Title.Contains("warning", StringComparison.Ordinal) && warning.Title.Contains("Be respectful", StringComparison.Ordinal), "The author is warned (" + warning.Title + ")");
            Check.Equal("None", await ModerationTestKit.StateAsync(f, "comments", commentId, ct).ConfigureAwait(false), "A warning leaves the content");

            await Api.ExpectAsync(f, HttpMethod.Post, Queue + "/" + mutedRow + "/action", HttpFixture.AdminToken, new { action = "Mute", reason = "Repeated spam", untilUtc = DateTime.UtcNow.AddDays(3) }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(1L, await ModerationTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM users WHERE id = '" + muted.Id + "' AND ismuted;", ct).ConfigureAwait(false), "Muted through the member action");
            await Api.ExpectAsync(f, HttpMethod.Post, Queue + "/" + bannedRow + "/action", HttpFixture.AdminToken, new { action = "Ban", reason = "Payment scam" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(1L, await ModerationTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM users WHERE id = '" + banned.Id + "' AND isbanned;", ct).ConfigureAwait(false), "Banned through the member action");
            JsonElement dismissedResult = await Api.ExpectAsync(f, HttpMethod.Post, Queue + "/" + dismissedRow + "/action", HttpFixture.AdminToken, new { action = "Dismiss", note = "A joke between friends" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Dismissed", Api.Str(dismissedResult.GetProperty("change"), "reviewStatus"), "Dismissed");
            Check.Equal("admin-token", Api.Str(dismissedResult.GetProperty("change"), "reviewedBy"), "Reviewed by the administrator");
            Check.Equal(0, (await RowsAsync(f, new { status = new[] { "Flagged" } }, ct).ConfigureAwait(false)).Count, "Nothing left flagged");

            string ticketRow = "chg_" + Guid.NewGuid().ToString("N").Substring(0, 28);
            await f.Raw.ExecuteQueryAsync("INSERT INTO contentchanges (propertyid, id, targettype, targetid, action, authoruserid, client, snapshot, reviewstatus, createdutc, updatedutc) VALUES ('" + f.Services.PropertyId +
                "', '" + ticketRow + "', 'PollOption', 'pop_x', 'Create', NULL, 'Web', 'label: x', 'Flagged', NOW(), NOW());", false, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, Queue + "/" + ticketRow + "/action", HttpFixture.AdminToken, new { action = "Remove", reason = "Not allowed" }, HttpStatusCode.Conflict, "NotModeratable", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, Queue + "/" + ticketRow + "/action", HttpFixture.AdminToken, new { action = "Ban", reason = "Nobody" }, HttpStatusCode.Conflict, "NoAuthor", ct).ConfigureAwait(false);

            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, Queue + "/enumerate", member.AccessToken, new { }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, Queue, member.AccessToken, null, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, Queue + "/summary", member.AccessToken, null, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, Queue + "/" + postRow, member.AccessToken, null, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, Queue + "/" + postRow + "/action", member.AccessToken, new { action = "Dismiss" }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            TestMember moderator = await ModerationTestKit.ModeratorAsync(f, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, Queue + "/enumerate", moderator.AccessToken, new { }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
        }

        private static async Task DigestAsync(HttpFixture f, CancellationToken ct)
        {
            JsonElement jobs = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/jobs", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement review = jobs.GetProperty("objects").EnumerateArray().Single(j => Api.Str(j, "key") == ContentReviewService.ReviewJobKey);
            Check.True(review.GetProperty("enabled").GetBoolean(), "content.review is enabled by default");
            Check.Equal("EveryNMinutes", Api.Str(review, "intervalType"), "Every few minutes");
            Check.Equal(5, review.GetProperty("intervalValue").GetInt32(), "Every 5 minutes");
            Check.True(jobs.GetProperty("objects").EnumerateArray().Any(j => Api.Str(j, "key") == ContentReviewService.DigestJobKey && j.GetProperty("enabled").GetBoolean()), "The digest is registered and enabled");
            Check.True(jobs.GetProperty("objects").EnumerateArray().Any(j => Api.Str(j, "key") == ContentReviewService.PruneJobKey), "The retention job is registered");

            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            JobRun quiet = await RunJobAsync(f, ContentReviewService.DigestJobKey, ct).ConfigureAwait(false);
            Check.Equal(JobRunStatusEnum.Succeeded, quiet.Status, "The digest ran");
            Check.Equal(0L, await ContentTestKit.ScalarAsync(f, "SELECT COUNT(*) AS cnt FROM emailmessages WHERE template = 'staff_content_review_digest';", ct).ConfigureAwait(false), "Nothing flagged, no email");

            TestMember poster = await ContentTestKit.PosterAsync(f, ct).ConfigureAwait(false);
            string postId = await ContentTestKit.PostIdAsync(f, poster.AccessToken, "Cheap supplements, message me", ct).ConfigureAwait(false);
            await FlagAsync(f, "FeedPost", postId, ContentReviewCategoryEnum.Spam, ContentReviewSeverityEnum.Medium, ct).ConfigureAwait(false);
            await RunJobAsync(f, ContentReviewService.DigestJobKey, ct).ConfigureAwait(false);
            Check.Equal(1L, await ContentTestKit.ScalarAsync(f, "SELECT COUNT(*) AS cnt FROM emailmessages WHERE template = 'staff_content_review_digest' AND recipientnormalized = '" + admin.Email.ToLowerInvariant() + "';", ct).ConfigureAwait(false),
                "The digest emails the administrator");
            await RunJobAsync(f, ContentReviewService.DigestJobKey, ct).ConfigureAwait(false);
            Check.Equal(1L, await ContentTestKit.ScalarAsync(f, "SELECT COUNT(*) AS cnt FROM emailmessages WHERE template = 'staff_content_review_digest' AND recipientnormalized = '" + admin.Email.ToLowerInvariant() + "';", ct).ConfigureAwait(false),
                "Once per day");
        }

        #endregion
    }
}

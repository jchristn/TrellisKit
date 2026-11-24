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
    using Touchstone.Core;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;

    /// <summary>
    /// Phase 5 ideas board (docs/PHASE5_CONTRACT.md section 7): statuses, submissions, votes, merges, status
    /// notifications and the idea_shipped email, trending, comments and reactions, the public board, audiences per access
    /// model, the feature switch and kill switch, ideas from tickets, moderation, account lifecycle, and search.
    /// </summary>
    public static class IdeaSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "Ideas";

        #endregion

        #region Private-Members

        private const string _Ideas = "/api/v1/ideas";
        private const string _Admin = "/api/v1/admin/ideas";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Ideas board (" + TestEnvironment.Provider + ")", new[]
            {
                Case("Statuses.SeedAndAdmin", "The engine default statuses are seeded once; members list active statuses; SystemAdmin creates, edits, reorders, and retires statuses; SupportAgent and members cannot", StatusesAsync),
                Case("Submissions.Modes", "StaffOnly answers 403 to member submissions; Direct lets members submit with the author's vote and follow, filters words, and rate limits ideasPerDay; the author edits only in the initial status; staff create and edit any idea", SubmissionsAsync),
                Case("Votes.IdempotentCounts", "Votes and unvotes are idempotent, counts match the vote rows, voting follows the idea, closed statuses refuse votes unless voteOnClosed, voters are never exposed and staff see counts by membership", VotesAsync),
                Case("Merge.Unmerge", "Merging moves votes to the target de-duplicated, followers follow the target, the merged idea answers 409 IdeaMerged to votes and comments while its comments stay readable, and unmerge lists it again", MergeAsync),
                Case("Status.NotificationsAndEmail", "A status change notifies voters and followers once per idea while unread (refreshed by later changes), never the actor or opted-out members; notifyVoters false is silent; Shipped emails voters who turned Ideas email on", StatusNotificationsAsync),
                Case("Trending.FixedClock", "The trending pass counts votes inside ideas.trendingDays at a fixed clock and Trending orders by them", TrendingAsync),
                Case("Comments.ReactionsAndMerged", "Comments and reactions on ideas notify the author and the replied-to member, update counts, and merged ideas keep comments readable but refuse new ones", CommentsAsync),
                Case("PublicBoard", "The public board is 404 NotAvailable until ideas.publicBoardEnabled, then lists Public ideas only, cacheable for 60 seconds, without viewer votes", PublicBoardAsync),
                Case("Audiences.PartiallyGated", "PartiallyGated, ideas not gated: Members ideas are locked stubs for free accounts (402 on read), SignedIn ideas take part, visitors see Public ideas only; ideas gated: free accounts get 402 naming ideas", PartiallyGatedAsync),
                SuiteCases.Http(Id, "Feature.OffAndKillSwitch", "features.ideas off answers 404 NotAvailable on member, staff, and status routes", p => p.Features.Ideas = false, FeatureOffAsync),
                Case("KillSwitch", "The ideas kill switch answers 503 FeatureDisabled to members on idea routes", KillSwitchAsync),
                Case("Tickets.PublishAndLink", "publish-idea makes an idea from an Enhancement ticket with the requester's vote and sets the ticket's idea; other types are refused; link-idea links and votes", TicketsAsync),
                Case("Moderation.ScopeAndAutoHide", "Reports on ideas and idea comments file in scope Ideas; SupportAgent handles them, feed moderators do not, forum moderators with every forum handle idea comments; reports reach the auto-hide threshold", ModerationAsync),
                Case("Moderation.PurgeRemoved", "Ideas removed by a moderator are purged by the content purge worker after moderation.retainRemovedDays (title, body, labels cleared, purgedutc set); an Open report holds them and purged ideas cannot be restored", PurgeRemovedAsync),
                Case("Lifecycle.ExportAndDeletion", "The export carries ideaVotes and ideas; deleting the account removes votes (counts recomputed) and follows, keeps reviewed ideas as Deleted member, and deletes ideas still under review", LifecycleAsync),
                Case("Search.Audience", "Ideas join global search and obey their audience", SearchAsync)
            });
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string caseId, string name, Func<HttpFixture, CancellationToken, Task> body)
        {
            return SuiteCases.Http(Id, caseId, name, (f, ct) =>
            {
                Relax(f);
                return body(f, ct);
            });
        }

        private static void Relax(HttpFixture f)
        {
            ForumTestKit.Relax(f);
            f.Settings.Ideas.IdeasPerDay = 1000;
            f.Settings.Ideas.VotesPerMinute = 10000;
        }

        private static async Task PurgeRemovedAsync(HttpFixture f, CancellationToken ct)
        {
            f.Settings.Moderation.AutoHideReportThreshold = 0;
            TestMember admin = await ContentTestKit.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember agent = await SupportAgentAsync(f, ct).ConfigureAwait(false);
            TestMember reporter = await ContentTestKit.MemberAsync(f, ct, "Idea Reporter").ConfigureAwait(false);
            string removed = await StaffIdeaIdAsync(f, admin.AccessToken, "Remove this idea", ct, new { labels = new[] { "purge-me" } }).ConfigureAwait(false);
            string held = await StaffIdeaIdAsync(f, admin.AccessToken, "Held by a report", ct).ConfigureAwait(false);
            string kept = await StaffIdeaIdAsync(f, admin.AccessToken, "Still listed", ct).ConfigureAwait(false);
            await f.Raw.ExecuteQueryAsync("UPDATE ideas SET authoruserid = '" + reporter.Id + "' WHERE id IN ('" + removed + "', '" + held + "');", false, ct).ConfigureAwait(false);
            await ModerationTestKit.ActOkAsync(f, agent.AccessToken, "Idea", removed, "Remove", ct).ConfigureAwait(false);
            await ModerationTestKit.ActOkAsync(f, agent.AccessToken, "Idea", held, "Remove", ct).ConfigureAwait(false);
            TestMember bystander = await ContentTestKit.MemberAsync(f, ct, "Idea Bystander").ConfigureAwait(false);
            await ModerationTestKit.ReportOkAsync(f, bystander.AccessToken, "User", reporter.Id, ct).ConfigureAwait(false);
            await f.Raw.ExecuteQueryAsync("UPDATE reports SET targettype = 'Idea', targetid = '" + held + "' WHERE reporteruserid = '" + bystander.Id + "';", false, ct).ConfigureAwait(false);
            DateTime now = DateTime.UtcNow;
            TrellisKit.Server.Workers.ContentPurgeWorker worker = new TrellisKit.Server.Workers.ContentPurgeWorker(f.Database, f.Services.Moderation, f.Services.Logger);
            await using (worker.ConfigureAwait(false))
            {
                f.Services.Moderation.Clock = () => now.AddDays(f.Settings.Moderation.RetainRemovedDays - 1);
                Check.Equal(0, await worker.RunOnceAsync(ct).ConfigureAwait(false), "Nothing purged inside the retention period");
                f.Services.Moderation.Clock = () => now.AddDays(f.Settings.Moderation.RetainRemovedDays + 1);
                Check.Equal(1, await worker.RunOnceAsync(ct).ConfigureAwait(false), "The removed idea is purged");
                Check.Equal(0, await worker.RunOnceAsync(ct).ConfigureAwait(false), "Idempotent");
            }
            f.Services.Moderation.Clock = () => DateTime.UtcNow;
            Idea purged = (await f.Database.Ideas.ReadAsync(removed, ct).ConfigureAwait(false))!;
            Check.True(purged.PurgedUtc != null && purged.Title.Length == 0 && purged.Body.Length == 0, "Title and body cleared, purgedutc set");
            Check.Equal(0L, await CountAsync(f, "SELECT COUNT(*) AS cnt FROM labels WHERE targetid = '" + removed + "';", ct).ConfigureAwait(false), "Labels deleted");
            Check.True((await f.Database.Ideas.ReadAsync(held, ct).ConfigureAwait(false))!.PurgedUtc == null, "An Open report keeps the idea");
            Check.True((await f.Database.Ideas.ReadAsync(kept, ct).ConfigureAwait(false))!.PurgedUtc == null, "Listed ideas are never purged");
            ApiResult restore = await ModerationTestKit.ActAsync(f, agent.AccessToken, "Idea", removed, "Restore", ct).ConfigureAwait(false);
            Check.Equal("InvalidModerationState", restore.Error, "A purged idea cannot be restored");
        }

        private static async Task<TestMember> SupportAgentAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember member = await ContentTestKit.MemberAsync(f, ct, "Support Agent").ConfigureAwait(false);
            await SafetyHelpers.GrantRoleAsync(f, member.Id, RoleEnum.SupportAgent, ct).ConfigureAwait(false);
            return await Api.LoginAsync(f, member, ct).ConfigureAwait(false);
        }

        private static async Task<JsonElement> StaffIdeaAsync(HttpFixture f, string bearer, string title, CancellationToken ct, object? extra = null)
        {
            Dictionary<string, object?> body = new Dictionary<string, object?> { { "title", title }, { "body", title + " body text" } };
            if (extra != null) foreach (System.Reflection.PropertyInfo p in extra.GetType().GetProperties()) body[p.Name] = p.GetValue(extra);
            return await Api.ExpectAsync(f, HttpMethod.Post, _Admin, bearer, body, HttpStatusCode.Created, ct).ConfigureAwait(false);
        }

        private static async Task<string> StaffIdeaIdAsync(HttpFixture f, string bearer, string title, CancellationToken ct, object? extra = null)
        {
            return Api.Str(await StaffIdeaAsync(f, bearer, title, ct, extra).ConfigureAwait(false), "id");
        }

        private static Task<long> CountAsync(HttpFixture f, string sql, CancellationToken ct)
        {
            return ContentTestKit.ScalarAsync(f, sql, ct);
        }

        private static async Task<JsonElement> VoteAsync(HttpFixture f, string bearer, string ideaId, CancellationToken ct)
        {
            return await Api.ExpectAsync(f, HttpMethod.Put, _Ideas + "/" + ideaId + "/vote", bearer, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
        }

        private static async Task<JsonElement> SetStatusAsync(HttpFixture f, string bearer, string ideaId, string status, CancellationToken ct, string? note = null, bool notify = true)
        {
            return await Api.ExpectAsync(f, HttpMethod.Put, _Admin + "/" + ideaId + "/status", bearer, new { status, statusNote = note, notifyVoters = notify }, HttpStatusCode.OK, ct).ConfigureAwait(false);
        }

        private static async Task<int> FanoutAsync(HttpFixture f, CancellationToken ct)
        {
            return await ForumTestKit.FanoutAsync(f, ct).ConfigureAwait(false);
        }

        private static async Task<List<Notification>> StatusNotificationsOfAsync(HttpFixture f, string userId, CancellationToken ct)
        {
            return await ForumTestKit.NotificationsAsync(f, userId, NotificationTypeEnum.IdeaStatusChanged, ct).ConfigureAwait(false);
        }

        private static async Task<string> TicketAsync(HttpFixture f, string requesterId, string typeKey, CancellationToken ct)
        {
            string id = IdGenerator.Generate("tkt_");
            string now = DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
            long number = await CountAsync(f, "SELECT COUNT(*) + 1000 AS cnt FROM tickets;", ct).ConfigureAwait(false);
            await f.Raw.ExecuteQueryAsync("INSERT INTO tickets (propertyid, id, number, typekey, title, body, status, priority, requesteruserid, lastactivityutc, createdutc, updatedutc) VALUES ('" +
                (f.PropertyId ?? TestProperty.SampleId) + "', '" + id + "', " + number + ", '" + typeKey + "', 'Offline mode for the app', 'Please let me read lessons on the train without signal.', 'Open', 'Normal', '" +
                requesterId + "', '" + now + "', '" + now + "', '" + now + "');", false, ct).ConfigureAwait(false);
            return id;
        }

        private static async Task StatusesAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await ContentTestKit.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember member = await ContentTestKit.MemberAsync(f, ct).ConfigureAwait(false);
            TestMember agent = await SupportAgentAsync(f, ct).ConfigureAwait(false);
            JsonElement page = await Api.ExpectAsync(f, HttpMethod.Post, _Ideas + "/statuses/enumerate", member.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            List<JsonElement> statuses = page.GetProperty("objects").EnumerateArray().ToList();
            Check.True(statuses.Select(s => Api.Str(s, "key")).SequenceEqual(new[] { "UnderReview", "Planned", "InProgress", "Shipped", "Declined" }), "Engine default statuses in order");
            Check.True(statuses[0].GetProperty("isInitial").GetBoolean() && statuses[3].GetProperty("isClosed").GetBoolean() && statuses[4].GetProperty("isClosed").GetBoolean(), "Initial and closed flags");
            ContentTestKit.CheckMetadata(statuses[0], "idea status");
            await f.Services.Ideas.Statuses.EnsureSeededAsync(ct).ConfigureAwait(false);
            Check.Equal(5L, await CountAsync(f, "SELECT COUNT(*) AS cnt FROM ideastatuses;", ct).ConfigureAwait(false), "Seeded once");

            JsonElement created = await Api.ExpectAsync(f, HttpMethod.Post, _Admin + "/statuses", admin.AccessToken, new { key = "NeedsInfo", label = "Needs info", color = "#AA00AA" }, HttpStatusCode.Created, ct).ConfigureAwait(false);
            string statusId = Api.Str(created, "id");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, _Admin + "/statuses", admin.AccessToken, new { key = "needsinfo", label = "Duplicate" }, HttpStatusCode.Conflict, "StatusKeyInUse", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, _Admin + "/statuses", admin.AccessToken, new { key = "Bad key!", label = "Bad" }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, _Admin + "/statuses", agent.AccessToken, new { key = "Agent", label = "Agent" }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, _Admin + "/statuses", member.AccessToken, new { key = "Member", label = "Member" }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, _Admin + "/statuses/enumerate", agent.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement updated = await Api.ExpectAsync(f, HttpMethod.Put, _Admin + "/statuses/" + statusId, admin.AccessToken, new { label = "Needs details", isInitial = true }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Needs details", Api.Str(updated, "label"), "Label edited");
            Check.True(updated.GetProperty("isInitial").GetBoolean(), "New initial status");
            Check.Equal(1L, await CountAsync(f, "SELECT COUNT(*) AS cnt FROM ideastatuses WHERE isinitial;", ct).ConfigureAwait(false), "Exactly one initial status");
            List<string> ids = statuses.Select(s => Api.Str(s, "id")).ToList();
            ids.Insert(0, statusId);
            ids.Reverse();
            JsonElement ordered = await Api.ExpectAsync(f, HttpMethod.Put, _Admin + "/statuses/order", admin.AccessToken, new { ideaStatusIds = ids }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(ordered.GetProperty("objects").EnumerateArray().Select(o => Api.Str(o, "id")).SequenceEqual(ids), "Reordered");
            await Api.ExpectErrorAsync(f, HttpMethod.Put, _Admin + "/statuses/order", admin.AccessToken, new { ideaStatusIds = ids.Take(2).ToList() }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Delete, _Admin + "/statuses/" + statusId, admin.AccessToken, null, HttpStatusCode.Conflict, "StatusInUse", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Delete, _Admin + "/statuses/" + Api.Str(statuses[4], "id"), admin.AccessToken, null, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
            JsonElement active = await Api.ExpectAsync(f, HttpMethod.Post, _Ideas + "/statuses/enumerate", member.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(!active.GetProperty("objects").EnumerateArray().Any(o => Api.Str(o, "key") == "Declined"), "Retired statuses leave the member list");
            JsonElement all = await Api.ExpectAsync(f, HttpMethod.Post, _Admin + "/statuses/enumerate", admin.AccessToken, new { filters = new { includeRetired = true } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(all.GetProperty("objects").EnumerateArray().Any(o => Api.Str(o, "key") == "Declined" && o.GetProperty("isRetired").GetBoolean()), "Staff see retired statuses");
            Check.True(await ModerationTestKit.AuditCountAsync(f, "idea.status.create", statusId, ct).ConfigureAwait(false) == 1, "Audited");
        }

        private static async Task SubmissionsAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await ContentTestKit.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember author = await ContentTestKit.MemberAsync(f, ct, "Idea Author").ConfigureAwait(false);
            TestMember other = await ContentTestKit.MemberAsync(f, ct, "Other Member").ConfigureAwait(false);
            TestMember agent = await SupportAgentAsync(f, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, _Ideas, author.AccessToken, new { title = "Dark mode please", body = "My eyes." }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            JsonElement before = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/property", null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("StaffOnly", Api.Str(before.GetProperty("ideas"), "memberSubmissions"), "GET /property reports ideas.memberSubmissions");
            Check.True(!before.GetProperty("ideas").GetProperty("publicBoardEnabled").GetBoolean() && !before.GetProperty("ideas").GetProperty("voteOnClosed").GetBoolean(), "GET /property ideas flags");
            f.Settings.Ideas.MemberSubmissions = IdeaSubmissionModeEnum.Direct;
            JsonElement after = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/property", null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Direct", Api.Str(after.GetProperty("ideas"), "memberSubmissions"), "The ideas block follows the live setting");
            JsonElement idea = await Api.ExpectAsync(f, HttpMethod.Post, _Ideas, author.AccessToken, new { title = "Dark mode please", body = "My eyes hurt at night.", labels = new[] { "ui" } }, HttpStatusCode.Created, ct).ConfigureAwait(false);
            string ideaId = Api.Str(idea, "id");
            Check.True(ideaId.StartsWith("ida_", StringComparison.Ordinal), "Idea id prefix");
            Check.Equal(1, idea.GetProperty("voteCount").GetInt32(), "The author's vote");
            Check.True(idea.GetProperty("hasVoted").GetBoolean() && idea.GetProperty("isFollowed").GetBoolean(), "The author votes and follows");
            Check.Equal("UnderReview", Api.Str(idea.GetProperty("status"), "key"), "Initial status");
            Check.Equal("Members", Api.Str(idea, "audience"), "FullyGated default audience");
            Check.Equal("Idea", Api.Str(idea.GetProperty("commentTarget"), "type"), "commentTarget");
            Check.True(idea.GetProperty("labels").EnumerateArray().Any(l => l.GetString() == "ui"), "Labels stored");
            Check.True(idea.GetProperty("viewer").GetProperty("canEdit").GetBoolean() && !idea.GetProperty("viewer").GetProperty("canSetStatus").GetBoolean(), "Author viewer");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, _Ideas, author.AccessToken, new { title = "x", body = "" }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, _Ideas, author.AccessToken, new { title = "Public idea", body = "", audience = "Public" }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/moderation/word-filter", HttpFixture.AdminToken, new { term = "forbiddenword", matchType = "Word", action = "Block" }, HttpStatusCode.Created, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, _Ideas, author.AccessToken, new { title = "A forbiddenword idea", body = "text" }, HttpStatusCode.BadRequest, "ContentFiltered", ct).ConfigureAwait(false);

            JsonElement edited = await Api.ExpectAsync(f, HttpMethod.Put, _Ideas + "/" + ideaId, author.AccessToken, new { title = "Dark mode for the website" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Dark mode for the website", Api.Str(edited, "title"), "Author edit");
            await Api.ExpectErrorAsync(f, HttpMethod.Put, _Ideas + "/" + ideaId, other.AccessToken, new { title = "Hijacked" }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            await SetStatusAsync(f, agent.AccessToken, ideaId, "Planned", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, _Ideas + "/" + ideaId, author.AccessToken, new { title = "Late edit" }, HttpStatusCode.Conflict, "IdeaNotEditable", ct).ConfigureAwait(false);
            JsonElement staffEdit = await Api.ExpectAsync(f, HttpMethod.Put, _Admin + "/" + ideaId, agent.AccessToken, new { body = "Edited by support." }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Edited by support.", Api.Str(staffEdit, "body"), "Staff edit any time");
            await Api.ExpectErrorAsync(f, HttpMethod.Put, _Admin + "/" + ideaId, author.AccessToken, new { body = "No" }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);

            JsonElement staffIdea = await StaffIdeaAsync(f, admin.AccessToken, "Staff roadmap item", ct, new { audience = "Public", status = "Planned", showAuthor = false }).ConfigureAwait(false);
            Check.Equal("Public", Api.Str(staffIdea, "audience"), "Staff may publish Public ideas");
            Check.Equal("Planned", Api.Str(staffIdea.GetProperty("status"), "key"), "Staff choose the status");
            JsonElement asMember = await Api.ExpectAsync(f, HttpMethod.Get, _Ideas + "/" + Api.Str(staffIdea, "id"), other.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(asMember.GetProperty("author").ValueKind == JsonValueKind.Null, "showAuthor false hides the author from members");
            Check.Equal(0, asMember.GetProperty("voteCount").GetInt32(), "Staff ideas start without votes");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, _Admin, author.AccessToken, new { title = "Not staff", body = "x" }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            Check.Equal(1L, await ModerationTestKit.AuditCountAsync(f, "idea.create", ideaId, ct).ConfigureAwait(false), "idea.create audited");
            Check.True(await ModerationTestKit.AuditCountAsync(f, "idea.update", ideaId, ct).ConfigureAwait(false) >= 2, "idea.update audited");

            f.Settings.Ideas.IdeasPerDay = 1;
            TestMember limited = await ContentTestKit.MemberAsync(f, ct, "Limited Member").ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, _Ideas, limited.AccessToken, new { title = "First idea today", body = "" }, HttpStatusCode.Created, ct).ConfigureAwait(false);
            ApiResult limitedResult = await Api.SendAsync(f, HttpMethod.Post, _Ideas, limited.AccessToken, new { title = "Second idea today", body = "" }, ct).ConfigureAwait(false);
            Check.Equal((HttpStatusCode)429, limitedResult.Status, "ideasPerDay");
            Check.True(!String.IsNullOrEmpty(limitedResult.RetryAfter), "Retry-After");

            await Api.ExpectAsync(f, HttpMethod.Delete, _Admin + "/" + ideaId, agent.AccessToken, null, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, _Ideas + "/" + ideaId, author.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            Check.Equal(1L, await ModerationTestKit.AuditCountAsync(f, "idea.delete", ideaId, ct).ConfigureAwait(false), "idea.delete audited");
        }

        private static async Task VotesAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await ContentTestKit.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember voter = await ContentTestKit.MemberAsync(f, ct, "Voter").ConfigureAwait(false);
            TestMember second = await ContentTestKit.MemberAsync(f, ct, "Second Voter").ConfigureAwait(false);
            string ideaId = await StaffIdeaIdAsync(f, admin.AccessToken, "Calendar export", ct).ConfigureAwait(false);
            JsonElement once = await VoteAsync(f, voter.AccessToken, ideaId, ct).ConfigureAwait(false);
            JsonElement twice = await VoteAsync(f, voter.AccessToken, ideaId, ct).ConfigureAwait(false);
            Check.Equal(1, once.GetProperty("voteCount").GetInt32(), "One vote");
            Check.Equal(1, twice.GetProperty("voteCount").GetInt32(), "Idempotent vote");
            Check.True(twice.GetProperty("hasVoted").GetBoolean() && twice.GetProperty("isFollowed").GetBoolean(), "Voting follows");
            await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => VoteAsync(f, second.AccessToken, ideaId, ct))).ConfigureAwait(false);
            Check.Equal(2L, await CountAsync(f, "SELECT COUNT(*) AS cnt FROM ideavotes WHERE ideaid = '" + ideaId + "';", ct).ConfigureAwait(false), "Concurrent votes leave one row per member");
            Idea stored = (await f.Database.Ideas.ReadAsync(ideaId, ct).ConfigureAwait(false))!;
            Check.Equal(2, stored.VoteCount, "votecount matches the rows");
            JsonElement removed = await Api.ExpectAsync(f, HttpMethod.Delete, _Ideas + "/" + ideaId + "/vote", voter.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Delete, _Ideas + "/" + ideaId + "/vote", voter.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(1, removed.GetProperty("voteCount").GetInt32(), "Unvote");
            Check.True(!removed.GetProperty("hasVoted").GetBoolean(), "hasVoted false");
            Check.True(!removed.GetRawText().Contains(second.Id, StringComparison.Ordinal), "Voters are never exposed");
            JsonElement follow = await Api.ExpectAsync(f, HttpMethod.Delete, _Ideas + "/" + ideaId + "/follow", voter.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(!follow.GetProperty("isFollowed").GetBoolean(), "Unfollow");
            JsonElement refollow = await Api.ExpectAsync(f, HttpMethod.Put, _Ideas + "/" + ideaId + "/follow", voter.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(refollow.GetProperty("isFollowed").GetBoolean(), "Follow");
            JsonElement staffView = await Api.ExpectAsync(f, HttpMethod.Get, _Admin + "/" + ideaId, admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(1L, staffView.GetProperty("voterCounts").GetProperty("members").GetInt64(), "Staff voter counts by membership");
            JsonElement memberView = await Api.ExpectAsync(f, HttpMethod.Get, _Ideas + "/" + ideaId, voter.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(memberView.GetProperty("voterCounts").ValueKind == JsonValueKind.Null, "Members do not get voter counts");
            JsonElement mine = await Api.ExpectAsync(f, HttpMethod.Post, _Ideas + "/enumerate", second.AccessToken, new { filters = new { votedByMe = true } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(ForumTestKit.Ids(mine).SequenceEqual(new[] { ideaId }), "votedByMe filter");

            await SetStatusAsync(f, admin.AccessToken, ideaId, "Shipped", ct, null, false).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, _Ideas + "/" + ideaId + "/vote", voter.AccessToken, null, HttpStatusCode.Conflict, "IdeaClosed", ct).ConfigureAwait(false);
            JsonElement closedView = await Api.ExpectAsync(f, HttpMethod.Get, _Ideas + "/" + ideaId, voter.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(!closedView.GetProperty("viewer").GetProperty("canVote").GetBoolean(), "viewer.canVote false on a closed status");
            JsonElement open = await Api.ExpectAsync(f, HttpMethod.Post, _Ideas + "/enumerate", voter.AccessToken, new { filters = new { open = true } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(!ForumTestKit.Ids(open).Contains(ideaId), "open filter excludes closed statuses");
            f.Settings.Ideas.VoteOnClosed = true;
            await VoteAsync(f, voter.AccessToken, ideaId, ct).ConfigureAwait(false);
            f.Settings.Ideas.VoteOnClosed = false;

            f.Settings.Ideas.VotesPerMinute = 2;
            TestMember fast = await ContentTestKit.MemberAsync(f, ct, "Fast Voter").ConfigureAwait(false);
            string other = await StaffIdeaIdAsync(f, admin.AccessToken, "Rate limited idea", ct).ConfigureAwait(false);
            await VoteAsync(f, fast.AccessToken, other, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Delete, _Ideas + "/" + other + "/vote", fast.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            ApiResult limited = await Api.SendAsync(f, HttpMethod.Put, _Ideas + "/" + other + "/vote", fast.AccessToken, null, ct).ConfigureAwait(false);
            Check.Equal((HttpStatusCode)429, limited.Status, "votesPerMinute");
        }

        private static async Task MergeAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await ContentTestKit.AdminAsync(f, ct).ConfigureAwait(false);
            List<TestMember> members = await ModerationTestKit.MembersAsync(f, 3, ct, "Merge").ConfigureAwait(false);
            string duplicate = await StaffIdeaIdAsync(f, admin.AccessToken, "Night theme", ct).ConfigureAwait(false);
            string target = await StaffIdeaIdAsync(f, admin.AccessToken, "Dark mode", ct).ConfigureAwait(false);
            await VoteAsync(f, members[0].AccessToken, duplicate, ct).ConfigureAwait(false);
            await VoteAsync(f, members[1].AccessToken, duplicate, ct).ConfigureAwait(false);
            await VoteAsync(f, members[1].AccessToken, target, ct).ConfigureAwait(false);
            await VoteAsync(f, members[2].AccessToken, target, ct).ConfigureAwait(false);
            string comment = Api.Str(await Api.ExpectAsync(f, HttpMethod.Post, _Ideas + "/" + duplicate + "/comments", members[0].AccessToken, new { body = "Before the merge" }, HttpStatusCode.Created, ct).ConfigureAwait(false), "id");

            JsonElement merged = await Api.ExpectAsync(f, HttpMethod.Post, _Admin + "/" + duplicate + "/merge", admin.AccessToken, new { intoIdeaId = target }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(target, Api.Str(merged.GetProperty("mergedInto"), "id"), "mergedInto");
            Idea stored = (await f.Database.Ideas.ReadAsync(target, ct).ConfigureAwait(false))!;
            Check.Equal(3, stored.VoteCount, "Votes moved de-duplicated");
            Check.Equal(3L, await CountAsync(f, "SELECT COUNT(*) AS cnt FROM ideavotes WHERE ideaid = '" + target + "';", ct).ConfigureAwait(false), "One vote per member on the target");
            Check.Equal(1L, await CountAsync(f, "SELECT COUNT(*) AS cnt FROM ideafollows WHERE ideaid = '" + target + "' AND userid = '" + members[0].Id + "';", ct).ConfigureAwait(false), "Followers follow the target");
            ApiResult vote = await Api.SendAsync(f, HttpMethod.Put, _Ideas + "/" + duplicate + "/vote", members[2].AccessToken, null, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.Conflict, vote.Status, "Votes on a merged idea");
            Check.Equal("IdeaMerged", vote.Error, "IdeaMerged");
            Check.Equal(target, Api.Str(vote.Body.GetProperty("mergedInto"), "id"), "The body names the target");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, _Ideas + "/" + duplicate + "/comments", members[2].AccessToken, new { body = "After the merge" }, HttpStatusCode.Conflict, "IdeaMerged", ct).ConfigureAwait(false);
            JsonElement tree = await Api.ExpectAsync(f, HttpMethod.Post, _Ideas + "/" + duplicate + "/comments/enumerate", members[2].AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(ForumTestKit.Ids(tree).Contains(comment), "Comments of a merged idea stay readable");
            JsonElement read = await Api.ExpectAsync(f, HttpMethod.Get, _Ideas + "/" + duplicate, members[2].AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(target, Api.Str(read.GetProperty("mergedInto"), "id"), "A merged idea returns its own payload with mergedInto");
            JsonElement board = await Api.ExpectAsync(f, HttpMethod.Post, _Ideas + "/enumerate", members[2].AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(!ForumTestKit.Ids(board).Contains(duplicate) && ForumTestKit.Ids(board).Contains(target), "Merged ideas leave the board");
            JsonElement withMerged = await Api.ExpectAsync(f, HttpMethod.Post, _Ideas + "/enumerate", members[2].AccessToken, new { filters = new { includeMerged = true } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(ForumTestKit.Ids(withMerged).Contains(duplicate), "includeMerged");
            JsonElement mergedOnly = await Api.ExpectAsync(f, HttpMethod.Post, _Admin + "/enumerate", admin.AccessToken, new { filters = new { merged = true } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(ForumTestKit.Ids(mergedOnly).SequenceEqual(new[] { duplicate }), "Staff merged filter");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, _Admin + "/" + duplicate + "/merge", admin.AccessToken, new { intoIdeaId = target }, HttpStatusCode.Conflict, "AlreadyMerged", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, _Admin + "/" + target + "/merge", admin.AccessToken, new { intoIdeaId = target }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            Check.Equal(1L, await ModerationTestKit.AuditCountAsync(f, "idea.merge", duplicate, ct).ConfigureAwait(false), "idea.merge audited");

            JsonElement unmerged = await Api.ExpectAsync(f, HttpMethod.Post, _Admin + "/" + duplicate + "/unmerge", admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(unmerged.GetProperty("mergedInto").ValueKind == JsonValueKind.Null, "Unmerged");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, _Admin + "/" + duplicate + "/unmerge", admin.AccessToken, null, HttpStatusCode.Conflict, "NotMerged", ct).ConfigureAwait(false);
            await VoteAsync(f, members[2].AccessToken, duplicate, ct).ConfigureAwait(false);
            JsonElement again = await Api.ExpectAsync(f, HttpMethod.Post, _Ideas + "/enumerate", members[2].AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(ForumTestKit.Ids(again).Contains(duplicate), "Unmerged ideas return to the board");
        }

        private static async Task StatusNotificationsAsync(HttpFixture f, CancellationToken ct)
        {
            NotificationTestKit.AllDay(f);
            TestMember agent = await SupportAgentAsync(f, ct).ConfigureAwait(false);
            TestMember voter = await ContentTestKit.MemberAsync(f, ct, "Status Voter").ConfigureAwait(false);
            TestMember follower = await ContentTestKit.MemberAsync(f, ct, "Status Follower").ConfigureAwait(false);
            TestMember quiet = await ContentTestKit.MemberAsync(f, ct, "Quiet Voter").ConfigureAwait(false);
            TestMember bystander = await ContentTestKit.MemberAsync(f, ct, "Bystander").ConfigureAwait(false);
            string ideaId = await StaffIdeaIdAsync(f, agent.AccessToken, "Offline lessons", ct).ConfigureAwait(false);
            await VoteAsync(f, voter.AccessToken, ideaId, ct).ConfigureAwait(false);
            await VoteAsync(f, quiet.AccessToken, ideaId, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, _Ideas + "/" + ideaId + "/follow", follower.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/me/notification-preferences", quiet.AccessToken, new { categories = new[] { new { category = "Ideas", inApp = false } } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement prefs = await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/me/notification-preferences", voter.AccessToken, new { categories = new[] { new { category = "Ideas", email = true } } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(prefs.GetProperty("categories").EnumerateArray().Any(c => Api.Str(c, "category") == "Ideas" && c.GetProperty("email").GetBoolean()), "Ideas email opt-in stored");
            await f.Raw.ExecuteQueryAsync("UPDATE users SET emailverified = TRUE WHERE id IN ('" + voter.Id + "', '" + follower.Id + "');", false, ct).ConfigureAwait(false);

            await SetStatusAsync(f, agent.AccessToken, ideaId, "Planned", ct, "Next quarter.").ConfigureAwait(false);
            await FanoutAsync(f, ct).ConfigureAwait(false);
            List<Notification> voterRows = await StatusNotificationsOfAsync(f, voter.Id, ct).ConfigureAwait(false);
            Check.Equal(1, voterRows.Count, "Voters get IdeaStatusChanged");
            Check.Equal(NotificationCategoryEnum.Ideas, voterRows[0].Category, "Category Ideas");
            Check.Equal("/ideas/" + ideaId, voterRows[0].Path, "Path");
            Check.True((voterRows[0].Body ?? String.Empty).Contains("Planned", StringComparison.Ordinal) && (voterRows[0].Body ?? String.Empty).Contains("Next quarter.", StringComparison.Ordinal), "Body names the status and note (" + voterRows[0].Body + ")");
            Check.True(voterRows[0].Title.Contains("Offline lessons", StringComparison.Ordinal), "Title names the idea");
            Check.Equal(1, (await StatusNotificationsOfAsync(f, follower.Id, ct).ConfigureAwait(false)).Count, "Followers get IdeaStatusChanged");
            Check.Equal(0, (await StatusNotificationsOfAsync(f, quiet.Id, ct).ConfigureAwait(false)).Count, "In-app off: nothing");
            Check.Equal(0, (await StatusNotificationsOfAsync(f, bystander.Id, ct).ConfigureAwait(false)).Count, "Bystanders: nothing");
            Check.Equal(0, (await StatusNotificationsOfAsync(f, agent.Id, ct).ConfigureAwait(false)).Count, "The actor is not notified");

            await SetStatusAsync(f, agent.AccessToken, ideaId, "InProgress", ct).ConfigureAwait(false);
            await FanoutAsync(f, ct).ConfigureAwait(false);
            voterRows = await StatusNotificationsOfAsync(f, voter.Id, ct).ConfigureAwait(false);
            Check.Equal(1, voterRows.Count, "Aggregated per idea while unread");
            Check.True((voterRows[0].Body ?? String.Empty).Contains("In progress", StringComparison.Ordinal), "Refreshed to the new status (" + voterRows[0].Body + ")");
            await f.Raw.ExecuteQueryAsync("UPDATE notifications SET readutc = now() WHERE recipientuserid = '" + voter.Id + "';", false, ct).ConfigureAwait(false);
            await SetStatusAsync(f, agent.AccessToken, ideaId, "Planned", ct, null, false).ConfigureAwait(false);
            await FanoutAsync(f, ct).ConfigureAwait(false);
            Check.Equal(1, (await StatusNotificationsOfAsync(f, voter.Id, ct).ConfigureAwait(false)).Count, "notifyVoters false is silent");

            await SetStatusAsync(f, agent.AccessToken, ideaId, "Shipped", ct, "Available in the latest release.").ConfigureAwait(false);
            await FanoutAsync(f, ct).ConfigureAwait(false);
            Check.Equal(2, (await StatusNotificationsOfAsync(f, voter.Id, ct).ConfigureAwait(false)).Count, "A read notification is not refreshed: a new one");
            List<TrellisKit.Core.Integrations.Models.EmailMessage> shipped = f.Fakes.Email.To(voter.Email, "idea_shipped");
            Check.Equal(1, shipped.Count, "idea_shipped to an opted-in voter");
            Check.True(shipped[0].Subject.Contains("Offline lessons", StringComparison.Ordinal), "Subject names the idea (" + shipped[0].Subject + ")");
            Check.True(shipped[0].TextBody.Contains("Available in the latest release.", StringComparison.Ordinal), "Body carries the note");
            Check.True(shipped[0].Headers.TryGetValue("List-Unsubscribe", out string? unsubscribe) && unsubscribe.Contains("/api/v1/email/unsubscribe?token=", StringComparison.Ordinal), "One-click unsubscribe");
            Check.Equal(0, f.Fakes.Email.To(follower.Email, "idea_shipped").Count, "Followers who did not vote get no email");
            Check.Equal(0, f.Fakes.Email.To(quiet.Email, "idea_shipped").Count, "Voters who did not opt in get no email");
            DataTable row = await f.Raw.ExecuteQueryAsync("SELECT category FROM emailmessages WHERE template = 'idea_shipped' AND userid = '" + voter.Id + "';", false, ct).ConfigureAwait(false);
            Check.Equal("Optional", RowReader.GetString(row.Rows[0], "category"), "Optional email");
            Check.True(await ModerationTestKit.AuditCountAsync(f, "idea.status", ideaId, ct).ConfigureAwait(false) >= 4, "idea.status audited");
        }

        private static async Task TrendingAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await ContentTestKit.AdminAsync(f, ct).ConfigureAwait(false);
            List<TestMember> members = await ModerationTestKit.MembersAsync(f, 3, ct, "Trend").ConfigureAwait(false);
            string old = await StaffIdeaIdAsync(f, admin.AccessToken, "Old favorite", ct).ConfigureAwait(false);
            string fresh = await StaffIdeaIdAsync(f, admin.AccessToken, "Fresh idea", ct).ConfigureAwait(false);
            foreach (TestMember m in members) await VoteAsync(f, m.AccessToken, old, ct).ConfigureAwait(false);
            await VoteAsync(f, members[0].AccessToken, fresh, ct).ConfigureAwait(false);
            DateTime clock = new DateTime(2030, 6, 1, 12, 0, 0, DateTimeKind.Utc);
            await f.Raw.ExecuteQueryAsync("UPDATE ideavotes SET createdutc = '2030-04-01T00:00:00Z' WHERE ideaid = '" + old + "';", false, ct).ConfigureAwait(false);
            await f.Raw.ExecuteQueryAsync("UPDATE ideavotes SET createdutc = '2030-05-30T00:00:00Z' WHERE ideaid = '" + fresh + "';", false, ct).ConfigureAwait(false);
            f.Services.Ideas.Clock = () => clock;
            int changed = await f.Services.Ideas.RecomputeTrendingAsync(ct).ConfigureAwait(false);
            Check.True(changed >= 1, "The pass updates recent counts (" + changed + ")");
            Check.Equal(0, (await f.Database.Ideas.ReadAsync(old, ct).ConfigureAwait(false))!.RecentVoteCount, "Votes outside the window do not count");
            Check.Equal(1, (await f.Database.Ideas.ReadAsync(fresh, ct).ConfigureAwait(false))!.RecentVoteCount, "Votes inside the window count");
            JsonElement trending = await Api.ExpectAsync(f, HttpMethod.Post, _Ideas + "/enumerate", members[1].AccessToken, new { ordering = "Trending" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(fresh, ForumTestKit.Ids(trending)[0], "Trending orders by recent votes");
            JsonElement top = await Api.ExpectAsync(f, HttpMethod.Post, _Ideas + "/enumerate", members[1].AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(old, ForumTestKit.Ids(top)[0], "Top (default) orders by votes");
            JsonElement relevance = await Api.ExpectAsync(f, HttpMethod.Post, _Ideas + "/enumerate", members[1].AccessToken, new { ordering = "Relevance", search = "fresh" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(ForumTestKit.Ids(relevance).SequenceEqual(new[] { fresh }), "Relevance with search");
            JsonElement paged = await Api.ExpectAsync(f, HttpMethod.Post, _Ideas + "/enumerate", members[1].AccessToken, new { maxResults = 1 }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement next = await Api.ExpectAsync(f, HttpMethod.Post, _Ideas + "/enumerate", members[1].AccessToken, new { maxResults = 1, continuationToken = Api.Str(paged, "continuationToken") }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(ForumTestKit.Ids(paged).Concat(ForumTestKit.Ids(next)).SequenceEqual(new[] { old, fresh }), "Keyset pages");
        }

        private static async Task CommentsAsync(HttpFixture f, CancellationToken ct)
        {
            f.Settings.Ideas.MemberSubmissions = IdeaSubmissionModeEnum.Direct;
            TestMember admin = await ContentTestKit.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember author = await ContentTestKit.MemberAsync(f, ct, "Idea Owner").ConfigureAwait(false);
            TestMember commenter = await ContentTestKit.MemberAsync(f, ct, "Commenter").ConfigureAwait(false);
            string ideaId = Api.Str(await Api.ExpectAsync(f, HttpMethod.Post, _Ideas, author.AccessToken, new { title = "Lesson playlists", body = "Group lessons." }, HttpStatusCode.Created, ct).ConfigureAwait(false), "id");
            JsonElement comment = await Api.ExpectAsync(f, HttpMethod.Post, _Ideas + "/" + ideaId + "/comments", commenter.AccessToken, new { body = "Yes, please." }, HttpStatusCode.Created, ct).ConfigureAwait(false);
            string commentId = Api.Str(comment, "id");
            List<Notification> postComments = await ForumTestKit.NotificationsAsync(f, author.Id, NotificationTypeEnum.PostComment, ct).ConfigureAwait(false);
            Check.Equal(1, postComments.Count, "PostComment to the idea's author");
            Check.True(postComments[0].Title.Contains("your idea", StringComparison.Ordinal), "Title names the idea (" + postComments[0].Title + ")");
            Check.Equal("/ideas/" + ideaId + "?comment=" + commentId, postComments[0].Path, "Path to the comment");
            await Api.ExpectAsync(f, HttpMethod.Post, _Ideas + "/" + ideaId + "/comments", author.AccessToken, new { body = "Thanks!", parentCommentId = commentId }, HttpStatusCode.Created, ct).ConfigureAwait(false);
            Check.Equal(1, (await ForumTestKit.NotificationsAsync(f, commenter.Id, NotificationTypeEnum.CommentReply, ct).ConfigureAwait(false)).Count, "CommentReply");
            Idea stored = (await f.Database.Ideas.ReadAsync(ideaId, ct).ConfigureAwait(false))!;
            Check.Equal(2, stored.CommentCount, "commentcount recomputed");
            JsonElement tree = await Api.ExpectAsync(f, HttpMethod.Get, _Ideas + "/" + ideaId + "/comments", commenter.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(ForumTestKit.Ids(tree).SequenceEqual(new[] { commentId }), "Tree");
            Check.Equal(1, tree.GetProperty("objects")[0].GetProperty("children").GetArrayLength(), "Nested reply");
            await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/comments/" + commentId, commenter.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);

            JsonElement reacted = await Api.ExpectAsync(f, HttpMethod.Put, _Ideas + "/" + ideaId + "/reactions/like", commenter.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(1L, reacted.GetProperty("reactionTotal").GetInt64(), "Idea reaction");
            Check.Equal(1, (await f.Database.Ideas.ReadAsync(ideaId, ct).ConfigureAwait(false))!.ReactionTotal, "reactiontotal recomputed");
            List<Notification> reactions = await ForumTestKit.NotificationsAsync(f, author.Id, NotificationTypeEnum.Reaction, ct).ConfigureAwait(false);
            Check.Equal(1, reactions.Count, "Reaction notification");
            Check.Equal("/ideas/" + ideaId, reactions[0].Path, "Reaction path");
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/comments/" + commentId + "/reactions/like", author.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement who = await Api.ExpectAsync(f, HttpMethod.Post, _Ideas + "/" + ideaId + "/reactions/enumerate", author.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(1, who.GetProperty("objects").GetArrayLength(), "Idea reactions enumerate");
            JsonElement view = await Api.ExpectAsync(f, HttpMethod.Get, _Ideas + "/" + ideaId, commenter.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(view.GetProperty("myReactions").EnumerateArray().Any(r => r.GetString() == "like"), "myReactions");
            Check.Equal(2, view.GetProperty("commentCount").GetInt32(), "commentCount");
            await Api.ExpectAsync(f, HttpMethod.Delete, _Ideas + "/" + ideaId + "/reactions/like", commenter.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);

            string target = await StaffIdeaIdAsync(f, admin.AccessToken, "Playlists and collections", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, _Admin + "/" + ideaId + "/merge", admin.AccessToken, new { intoIdeaId = target }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, _Ideas + "/" + ideaId + "/comments", commenter.AccessToken, new { body = "Late" }, HttpStatusCode.Conflict, "IdeaMerged", ct).ConfigureAwait(false);
            JsonElement readable = await Api.ExpectAsync(f, HttpMethod.Post, _Ideas + "/" + ideaId + "/comments/enumerate", commenter.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(ForumTestKit.Ids(readable).Contains(commentId), "Merged comments stay readable");
        }

        private static async Task PublicBoardAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await ContentTestKit.AdminAsync(f, ct).ConfigureAwait(false);
            string publicIdea = await StaffIdeaIdAsync(f, admin.AccessToken, "Public roadmap item", ct, new { audience = "Public" }).ConfigureAwait(false);
            string membersIdea = await StaffIdeaIdAsync(f, admin.AccessToken, "Members roadmap item", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/public/ideas/enumerate", null, new { }, HttpStatusCode.NotFound, "NotAvailable", ct).ConfigureAwait(false);
            f.Settings.Ideas.PublicBoardEnabled = true;
            ApiResult list = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/public/ideas/enumerate", null, new { }, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.OK, list.Status, "Public board (" + list.Text + ")");
            List<string> ids = ForumTestKit.Ids(list.Body);
            Check.True(ids.Contains(publicIdea) && !ids.Contains(membersIdea), "Public ideas only");
            Check.Equal("public, max-age=60", list.Headers.TryGetValue("Cache-Control", out string? cache) ? cache : null, "Cache-Control");
            JsonElement item = list.Body.GetProperty("objects")[0];
            Check.True(!item.GetProperty("hasVoted").GetBoolean() && !item.GetProperty("viewer").GetProperty("canVote").GetBoolean(), "No viewer votes");
            JsonElement one = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/public/ideas/" + publicIdea, null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(publicIdea, Api.Str(one, "id"), "Public read");
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/public/ideas/" + membersIdea, null, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, _Ideas, null, null, HttpStatusCode.Unauthorized, "Unauthorized", ct).ConfigureAwait(false);
        }

        private static async Task PartiallyGatedAsync(HttpFixture f, CancellationToken ct)
        {
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/property/access", HttpFixture.AdminToken, new { model = "PartiallyGated", anonymousBrowsing = true, gatedFeatures = Array.Empty<string>(), defaultAudience = "SignedIn" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember free = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            JsonElement defaulted = await StaffIdeaAsync(f, admin.AccessToken, "Signed in idea", ct).ConfigureAwait(false);
            Check.Equal("SignedIn", Api.Str(defaulted, "audience"), "Ideas not gated: the property default audience");
            string signedIn = Api.Str(defaulted, "id");
            string members = await StaffIdeaIdAsync(f, admin.AccessToken, "Members idea", ct, new { audience = "Members" }).ConfigureAwait(false);
            string publicIdea = await StaffIdeaIdAsync(f, admin.AccessToken, "Visitors idea", ct, new { audience = "Public" }).ConfigureAwait(false);
            JsonElement board = await Api.ExpectAsync(f, HttpMethod.Post, _Ideas + "/enumerate", free.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement stub = board.GetProperty("objects").EnumerateArray().First(o => Api.Str(o, "id") == members);
            Check.True(stub.GetProperty("locked").GetBoolean() && Api.Str(stub, "excerpt").Length == 0, "Members ideas are locked stubs for free accounts");
            Check.True(!stub.GetProperty("viewer").GetProperty("canOpen").GetBoolean() && Api.Str(stub.GetProperty("viewer"), "requires") == "Membership", "Stub viewer");
            ApiResult locked = await Api.SendAsync(f, HttpMethod.Get, _Ideas + "/" + members, free.AccessToken, null, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.PaymentRequired, locked.Status, "402 on a locked idea");
            Check.Equal("ideas", Api.Str(locked.Body, "feature"), "feature ideas");
            Check.Equal("Idea", Api.Str(locked.Body, "targetType"), "targetType Idea");
            await VoteAsync(f, free.AccessToken, signedIn, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, _Ideas + "/" + signedIn + "/comments", free.AccessToken, new { body = "Free account comment" }, HttpStatusCode.Created, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, _Ideas + "/" + members + "/vote", free.AccessToken, null, HttpStatusCode.PaymentRequired, "MembershipRequired", ct).ConfigureAwait(false);
            await VoteAsync(f, member.AccessToken, members, ct).ConfigureAwait(false);
            JsonElement visitor = await Api.ExpectAsync(f, HttpMethod.Post, _Ideas + "/enumerate", null, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(ForumTestKit.Ids(visitor).SequenceEqual(new[] { publicIdea }), "Visitors see Public ideas only, no stubs");
            await Api.ExpectErrorAsync(f, HttpMethod.Get, _Ideas + "/" + signedIn, null, null, HttpStatusCode.Unauthorized, "Unauthorized", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, _Ideas + "/" + publicIdea + "/vote", null, null, HttpStatusCode.Unauthorized, "Unauthorized", ct).ConfigureAwait(false);

            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/property/access", HttpFixture.AdminToken, new { gatedFeatures = new[] { "ideas" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            ApiResult gated = await Api.SendAsync(f, HttpMethod.Post, _Ideas + "/enumerate", free.AccessToken, new { }, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.PaymentRequired, gated.Status, "Gated ideas: free accounts get 402 on the board (" + gated.Text + ")");
            Check.Equal("ideas", Api.Str(gated.Body, "feature"), "feature ideas");
            await Api.ExpectAsync(f, HttpMethod.Post, _Ideas + "/enumerate", member.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement gatedDefault = await StaffIdeaAsync(f, admin.AccessToken, "Gated default", ct).ConfigureAwait(false);
            Check.Equal("Members", Api.Str(gatedDefault, "audience"), "Gated ideas default to Members");
        }

        private static async Task FeatureOffAsync(HttpFixture f, CancellationToken ct)
        {
            Relax(f);
            TestMember member = await ContentTestKit.MemberAsync(f, ct).ConfigureAwait(false);
            TestMember admin = await ContentTestKit.AdminAsync(f, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, _Ideas + "/enumerate", member.AccessToken, new { }, HttpStatusCode.NotFound, "NotAvailable", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, _Ideas + "/ida_missing", member.AccessToken, null, HttpStatusCode.NotFound, "NotAvailable", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, _Admin, admin.AccessToken, new { title = "Nope nope", body = "" }, HttpStatusCode.NotFound, "NotAvailable", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, _Ideas + "/statuses/enumerate", member.AccessToken, new { }, HttpStatusCode.NotFound, "NotAvailable", ct).ConfigureAwait(false);
        }

        private static async Task KillSwitchAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember member = await ContentTestKit.MemberAsync(f, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/app/kill-switches/ideas", HttpFixture.AdminToken, new { killed = true, reason = "Incident" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            ApiResult blocked = await Api.SendAsync(f, HttpMethod.Post, _Ideas + "/enumerate", member.AccessToken, new { }, ct).ConfigureAwait(false);
            Check.Equal((HttpStatusCode)503, blocked.Status, "Kill switch (" + blocked.Text + ")");
            Check.Equal("FeatureDisabled", blocked.Error, "FeatureDisabled");
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/app/kill-switches/ideas", HttpFixture.AdminToken, new { killed = false, reason = "Resolved" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, _Ideas + "/enumerate", member.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
        }

        private static async Task TicketsAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember agent = await SupportAgentAsync(f, ct).ConfigureAwait(false);
            TestMember requester = await ContentTestKit.MemberAsync(f, ct, "Ticket Requester").ConfigureAwait(false);
            TestMember other = await ContentTestKit.MemberAsync(f, ct, "Other Requester").ConfigureAwait(false);
            string enhancement = await TicketAsync(f, requester.Id, "Enhancement", ct).ConfigureAwait(false);
            string billing = await TicketAsync(f, requester.Id, "Billing", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/tickets/" + billing + "/publish-idea", agent.AccessToken, new { }, HttpStatusCode.BadRequest, "NotAnEnhancement", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/tickets/" + enhancement + "/publish-idea", requester.AccessToken, new { }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            JsonElement idea = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/tickets/" + enhancement + "/publish-idea", agent.AccessToken, new { title = "Offline mode" }, HttpStatusCode.Created, ct).ConfigureAwait(false);
            string ideaId = Api.Str(idea, "id");
            Check.Equal("Offline mode", Api.Str(idea, "title"), "Title override");
            Check.True(Api.Str(idea, "body").Contains("train", StringComparison.Ordinal), "Body defaults to the ticket's");
            Check.Equal(1, idea.GetProperty("voteCount").GetInt32(), "The requester votes");
            Check.Equal(1L, await CountAsync(f, "SELECT COUNT(*) AS cnt FROM ideavotes WHERE ideaid = '" + ideaId + "' AND userid = '" + requester.Id + "';", ct).ConfigureAwait(false), "Requester vote row");
            Check.Equal(1L, await CountAsync(f, "SELECT COUNT(*) AS cnt FROM tickets WHERE id = '" + enhancement + "' AND ideaid = '" + ideaId + "';", ct).ConfigureAwait(false), "The ticket's idea is set");
            JsonElement asRequester = await Api.ExpectAsync(f, HttpMethod.Get, _Ideas + "/" + ideaId, requester.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(asRequester.GetProperty("author").ValueKind == JsonValueKind.Null, "showRequester false hides the requester");
            Check.True(asRequester.GetProperty("hasVoted").GetBoolean(), "hasVoted for the requester");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/tickets/" + enhancement + "/publish-idea", agent.AccessToken, new { }, HttpStatusCode.Conflict, "TicketHasIdea", ct).ConfigureAwait(false);
            string otherTicket = await TicketAsync(f, other.Id, "Support", ct).ConfigureAwait(false);
            JsonElement link = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/tickets/" + otherTicket + "/link-idea", agent.AccessToken, new { ideaId }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(otherTicket, Api.Str(link, "ticketId"), "ticketId");
            Check.Equal(ideaId, Api.Str(link, "ideaId"), "ideaId");
            Check.Equal(2, (await f.Database.Ideas.ReadAsync(ideaId, ct).ConfigureAwait(false))!.VoteCount, "The linked requester votes");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/tickets/tkt_missing/link-idea", agent.AccessToken, new { ideaId }, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/tickets/" + otherTicket + "/link-idea", agent.AccessToken, new { }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
        }

        private static async Task ModerationAsync(HttpFixture f, CancellationToken ct)
        {
            f.Settings.Moderation.AutoHideReportThreshold = 3;
            f.Settings.Moderation.ReportsPerHour = 1000;
            f.Settings.Ideas.MemberSubmissions = IdeaSubmissionModeEnum.Direct;
            TestMember agent = await SupportAgentAsync(f, ct).ConfigureAwait(false);
            TestMember feedModerator = await ModerationTestKit.ModeratorAsync(f, ct).ConfigureAwait(false);
            TestMember forumModerator = await ContentTestKit.MemberAsync(f, ct, "Forum Moderator").ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/moderators", HttpFixture.AdminToken, new { userId = forumModerator.Id, resourceType = "Forum", resourceId = "*" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            forumModerator = await Api.LoginAsync(f, forumModerator, ct).ConfigureAwait(false);
            TestMember author = await ContentTestKit.MemberAsync(f, ct, "Spam Author").ConfigureAwait(false);
            List<TestMember> reporters = await ModerationTestKit.MembersAsync(f, 3, ct).ConfigureAwait(false);
            string ideaId = Api.Str(await Api.ExpectAsync(f, HttpMethod.Post, _Ideas, author.AccessToken, new { title = "Buy cheap stuff", body = "Spam spam." }, HttpStatusCode.Created, ct).ConfigureAwait(false), "id");
            string commentId = Api.Str(await Api.ExpectAsync(f, HttpMethod.Post, _Ideas + "/" + ideaId + "/comments", author.AccessToken, new { body = "More spam" }, HttpStatusCode.Created, ct).ConfigureAwait(false), "id");
            await ModerationTestKit.ReportOkAsync(f, reporters[0].AccessToken, "Idea", ideaId, ct).ConfigureAwait(false);
            await ModerationTestKit.ReportOkAsync(f, reporters[0].AccessToken, "Comment", commentId, ct).ConfigureAwait(false);
            DataTable scopes = await f.Raw.ExecuteQueryAsync("SELECT targettype, scopetype, scopeid FROM reports WHERE targetid IN ('" + ideaId + "', '" + commentId + "') ORDER BY targettype;", false, ct).ConfigureAwait(false);
            Check.Equal(2, scopes.Rows.Count, "Two reports");
            Check.True(scopes.Rows.Cast<DataRow>().All(r => RowReader.GetString(r, "scopetype") == "Ideas"), "Scope Ideas");
            Check.Equal("Comment", RowReader.GetString(scopes.Rows[0], "scopeid"), "Comment scope id");
            Check.Equal("Idea", RowReader.GetString(scopes.Rows[1], "scopeid"), "Idea scope id");
            Check.Equal(1, (await f.Database.Ideas.ReadAsync(ideaId, ct).ConfigureAwait(false))!.OpenReportCount, "openreportcount");

            JsonElement agentQueue = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/moderation/queue/enumerate", agent.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            List<string> agentTargets = ModerationTestKit.TargetIds(agentQueue);
            Check.True(agentTargets.Contains(ideaId) && agentTargets.Contains(commentId), "SupportAgent sees idea reports (" + String.Join(",", agentTargets) + ")");
            JsonElement ideaItem = agentQueue.GetProperty("objects").EnumerateArray().First(o => Api.Str(o, "targetId") == ideaId);
            Check.Equal("Buy cheap stuff", Api.Str(ideaItem.GetProperty("preview"), "title"), "Queue preview");
            JsonElement feedQueue = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/moderation/queue/enumerate", feedModerator.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(!ModerationTestKit.TargetIds(feedQueue).Contains(ideaId) && !ModerationTestKit.TargetIds(feedQueue).Contains(commentId), "Feed moderators do not see idea reports");
            JsonElement forumQueue = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/moderation/queue/enumerate", forumModerator.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(ModerationTestKit.TargetIds(forumQueue).Contains(commentId) && !ModerationTestKit.TargetIds(forumQueue).Contains(ideaId), "Forum moderators with every forum see idea comments only");
            ApiResult feedAct = await ModerationTestKit.ActAsync(f, feedModerator.AccessToken, "Idea", ideaId, "Hide", ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.Forbidden, feedAct.Status, "Feed moderators cannot act on ideas (" + feedAct.Text + ")");
            await ModerationTestKit.ActOkAsync(f, forumModerator.AccessToken, "Comment", commentId, "Hide", ct).ConfigureAwait(false);
            JsonElement detail = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/moderation/queue/Idea/" + ideaId, agent.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(ideaId, Api.Str(detail.GetProperty("target"), "id"), "Detail target");
            await ModerationTestKit.ActOkAsync(f, agent.AccessToken, "Idea", ideaId, "Hide", ct).ConfigureAwait(false);
            Check.Equal("Hidden", (await f.Database.Ideas.ReadAsync(ideaId, ct).ConfigureAwait(false))!.ModerationState.ToString(), "SupportAgent hides the idea");
            await Api.ExpectErrorAsync(f, HttpMethod.Get, _Ideas + "/" + ideaId, reporters[1].AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Get, _Ideas + "/" + ideaId, author.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await ModerationTestKit.ActOkAsync(f, agent.AccessToken, "Idea", ideaId, "Unhide", ct).ConfigureAwait(false);

            string second = Api.Str(await Api.ExpectAsync(f, HttpMethod.Post, _Ideas, author.AccessToken, new { title = "More cheap stuff", body = "Spam again." }, HttpStatusCode.Created, ct).ConfigureAwait(false), "id");
            foreach (TestMember reporter in reporters) await ModerationTestKit.ReportOkAsync(f, reporter.AccessToken, "Idea", second, ct).ConfigureAwait(false);
            Check.Equal("Hidden", (await f.Database.Ideas.ReadAsync(second, ct).ConfigureAwait(false))!.ModerationState.ToString(), "Auto-hide at the threshold");
            Check.Equal(1L, await ModerationTestKit.AutoHideCountAsync(f, second, ct).ConfigureAwait(false), "One AutoHide action");
            ApiResult self = await ModerationTestKit.ReportAsync(f, author.AccessToken, "Idea", ideaId, ct).ConfigureAwait(false);
            Check.Equal("CannotReportSelf", self.Error, "Authors cannot report their idea");
        }

        private static async Task LifecycleAsync(HttpFixture f, CancellationToken ct)
        {
            f.Settings.Ideas.MemberSubmissions = IdeaSubmissionModeEnum.Direct;
            TestMember admin = await ContentTestKit.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember leaver = await ContentTestKit.MemberAsync(f, ct, "Leaving Member").ConfigureAwait(false);
            TestMember stayer = await ContentTestKit.MemberAsync(f, ct, "Staying Member").ConfigureAwait(false);
            string underReview = Api.Str(await Api.ExpectAsync(f, HttpMethod.Post, _Ideas, leaver.AccessToken, new { title = "Still under review", body = "x" }, HttpStatusCode.Created, ct).ConfigureAwait(false), "id");
            string planned = Api.Str(await Api.ExpectAsync(f, HttpMethod.Post, _Ideas, leaver.AccessToken, new { title = "Already planned", body = "y" }, HttpStatusCode.Created, ct).ConfigureAwait(false), "id");
            await SetStatusAsync(f, admin.AccessToken, planned, "Planned", ct, null, false).ConfigureAwait(false);
            string staffIdea = await StaffIdeaIdAsync(f, admin.AccessToken, "Community calendar", ct).ConfigureAwait(false);
            await VoteAsync(f, leaver.AccessToken, staffIdea, ct).ConfigureAwait(false);
            await VoteAsync(f, stayer.AccessToken, staffIdea, ct).ConfigureAwait(false);

            string exportId = Api.Str(await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/me/export", leaver.AccessToken, null, HttpStatusCode.Accepted, ct).ConfigureAwait(false), "exportId");
            TrellisKit.Server.Workers.AccountExportWorker worker = new TrellisKit.Server.Workers.AccountExportWorker(f.Services.AccountExports, f.Services.Logger);
            await using (worker.ConfigureAwait(false))
            {
                await worker.RunOnceAsync(ct).ConfigureAwait(false);
            }
            JsonElement status = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/me/export/" + exportId, leaver.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement file = await Api.ExpectAsync(f, HttpMethod.Get, Api.Str(status, "downloadUrl"), null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(3, file.GetProperty("ideaVotes").GetArrayLength(), "Export ideaVotes (two own ideas and one vote)");
            Check.Equal(2, file.GetProperty("ideas").GetArrayLength(), "Export ideas");

            await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/me", leaver.AccessToken, new { confirm = "DELETE" }, HttpStatusCode.Accepted, ct).ConfigureAwait(false);
            Check.Equal(0L, await CountAsync(f, "SELECT COUNT(*) AS cnt FROM ideavotes WHERE userid = '" + leaver.Id + "';", ct).ConfigureAwait(false), "Votes deleted");
            Check.Equal(0L, await CountAsync(f, "SELECT COUNT(*) AS cnt FROM ideafollows WHERE userid = '" + leaver.Id + "';", ct).ConfigureAwait(false), "Follows deleted");
            Check.Equal(1, (await f.Database.Ideas.ReadAsync(staffIdea, ct).ConfigureAwait(false))!.VoteCount, "Counts recomputed");
            Idea deleted = (await f.Database.Ideas.ReadAsync(underReview, ct).ConfigureAwait(false))!;
            Check.True(deleted.DeletedUtc != null, "Ideas still under review are deleted");
            Idea kept = (await f.Database.Ideas.ReadAsync(planned, ct).ConfigureAwait(false))!;
            Check.True(kept.DeletedUtc == null && kept.AuthorUserId == null, "Reviewed ideas are kept without the author");
            JsonElement view = await Api.ExpectAsync(f, HttpMethod.Get, _Ideas + "/" + planned, stayer.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Deleted member", Api.Str(view.GetProperty("author"), "displayName"), "Deleted member");
        }

        private static async Task SearchAsync(HttpFixture f, CancellationToken ct)
        {
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/property/access", HttpFixture.AdminToken, new { model = "PartiallyGated", anonymousBrowsing = false, gatedFeatures = Array.Empty<string>(), defaultAudience = "SignedIn" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember free = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            string open = await StaffIdeaIdAsync(f, admin.AccessToken, "Zebrafish open idea", ct).ConfigureAwait(false);
            string members = await StaffIdeaIdAsync(f, admin.AccessToken, "Zebrafish members idea", ct, new { audience = "Members" }).ConfigureAwait(false);
            await ForumTestKit.DrainSearchAsync(f, ct).ConfigureAwait(false);
            JsonElement freeHits = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/search/enumerate", free.AccessToken, new { search = "zebrafish", filters = new { types = new[] { "Idea" } } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            List<string> freeIds = ForumTestKit.Ids(freeHits);
            Check.True(freeIds.Contains(open) && !freeIds.Contains(members), "Free accounts find SignedIn ideas only (" + String.Join(",", freeIds) + ")");
            Check.Equal("/ideas/" + open, Api.Str(freeHits.GetProperty("objects")[0], "path"), "Result path");
            JsonElement memberHits = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/search/enumerate", member.AccessToken, new { search = "zebrafish" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(ForumTestKit.Ids(memberHits).Contains(members) && ForumTestKit.Ids(memberHits).Contains(open), "Members find both");
            await Api.ExpectAsync(f, HttpMethod.Delete, _Admin + "/" + open, admin.AccessToken, null, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
            await ForumTestKit.DrainSearchAsync(f, ct).ConfigureAwait(false);
            JsonElement afterDelete = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/search/enumerate", member.AccessToken, new { search = "zebrafish" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(!ForumTestKit.Ids(afterDelete).Contains(open), "Deleted ideas leave search");
        }

        #endregion
    }
}

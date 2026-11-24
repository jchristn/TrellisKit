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
    using TrellisKit.Core.Responses;
    using TrellisKit.Server.Services;
    using TrellisKit.Server.Workers;

    /// <summary>
    /// Phase 2 notifications: titles of every type, dedupe priority, self and block suppression, Reaction aggregation
    /// (including concurrent reactions), the notification center enumeration, read and unread, preferences and their
    /// validation, notification hours, the unread count rate limit, account deletion, and the MembershipExpiring hook.
    /// </summary>
    public static class NotificationSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "Notifications";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Notifications (" + TestEnvironment.Provider + ")", new[]
            {
                Case("Titles.EveryType", "Every type renders its English title from the actor's display name and live settings", async (f, ct) =>
                {
                    TestMember owner = await NotificationTestKit.MemberAsync(f, "Example Owner", ct).ConfigureAwait(false);
                    TestMember alex = await NotificationTestKit.MemberAsync(f, "Alex Chen", ct).ConfigureAwait(false);
                    NotificationService n = f.Services.Notifications;
                    Check.Equal("Alex Chen commented on your post", (await n.CreateAsync(NotificationTestKit.Draft(NotificationTypeEnum.PostComment, owner.Id, alex.Id), ct).ConfigureAwait(false))!.Title, "PostComment");
                    Check.Equal("Alex Chen replied to your comment", (await n.CreateAsync(NotificationTestKit.Draft(NotificationTypeEnum.CommentReply, owner.Id, alex.Id), ct).ConfigureAwait(false))!.Title, "CommentReply");
                    Check.Equal("Alex Chen mentioned you in a comment", (await n.CreateAsync(NotificationTestKit.Draft(NotificationTypeEnum.Mention, owner.Id, alex.Id), ct).ConfigureAwait(false))!.Title, "Mention comment");
                    Check.Equal("Alex Chen mentioned you in a post", (await n.CreateAsync(NotificationTestKit.Draft(NotificationTypeEnum.Mention, owner.Id, alex.Id, TargetTypeEnum.FeedPost), ct).ConfigureAwait(false))!.Title, "Mention post");
                    Check.Equal("Alex Chen reacted to your comment", (await n.CreateAsync(NotificationTestKit.Draft(NotificationTypeEnum.Reaction, owner.Id, alex.Id), ct).ConfigureAwait(false))!.Title, "Reaction");
                    NotificationDraft post = NotificationTestKit.Draft(NotificationTypeEnum.NewFeedPost, alex.Id, owner.Id, TargetTypeEnum.FeedPost);
                    post.TitleText = "Welcome to the community";
                    Check.Equal("Example Owner posted: Welcome to the community", (await n.CreateAsync(post, ct).ConfigureAwait(false))!.Title, "NewFeedPost with title");
                    Check.Equal("Example Owner posted", (await n.CreateAsync(NotificationTestKit.Draft(NotificationTypeEnum.NewFeedPost, alex.Id, owner.Id, TargetTypeEnum.FeedPost), ct).ConfigureAwait(false))!.Title, "NewFeedPost without title");
                    NotificationDraft removed = NotificationTestKit.Draft(NotificationTypeEnum.ModerationAction, alex.Id, null);
                    removed.ModerationAction = ModerationActionTypeEnum.Remove;
                    removed.ModerationReason = "Spam";
                    Check.Equal("A moderator removed your comment (Spam)", (await n.CreateAsync(removed, ct).ConfigureAwait(false))!.Title, "ModerationAction");
                    f.Settings.Moderation.ReviewTargetHours = 12;
                    Check.Equal("A report on a comment is past the 12 hour review target", n.RenderTitle(NotificationTestKit.Draft(NotificationTypeEnum.ModerationOverdue, owner.Id, null), null), "ModerationOverdue");
                    f.Settings.Moderation.ReviewTargetHours = 6;
                    Check.Equal("A report on a comment is past the 6 hour review target", (await n.CreateAsync(NotificationTestKit.Draft(NotificationTypeEnum.ModerationOverdue, owner.Id, null), ct).ConfigureAwait(false))!.Title, "Live setting");
                    NotificationDraft expiring = NotificationTestKit.Draft(NotificationTypeEnum.MembershipExpiring, alex.Id, null, TargetTypeEnum.User, alex.Id);
                    expiring.EventUtc = new DateTime(2027, 3, 1, 12, 0, 0, DateTimeKind.Utc);
                    Notification account = (await n.CreateAsync(expiring, ct).ConfigureAwait(false))!;
                    Check.Equal("Your free membership ends on March 1, 2027", account.Title, "MembershipExpiring");
                    Check.Equal(NotificationCategoryEnum.Account, account.Category, "Account category");
                    Check.Equal("/account/membership", account.Path, "Membership path");
                }),
                Case("Titles.Aggregated", "Five reactors aggregate into \"Alex Chen and 4 others reacted to your comment\"", async (f, ct) =>
                {
                    TestMember owner = await NotificationTestKit.MemberAsync(f, "Example Owner", ct).ConfigureAwait(false);
                    string target = "cmt_aggregated";
                    for (int i = 0; i < 4; i++)
                    {
                        TestMember other = await NotificationTestKit.MemberAsync(f, "Reactor " + i, ct).ConfigureAwait(false);
                        await f.Services.Notifications.CreateAsync(NotificationTestKit.Draft(NotificationTypeEnum.Reaction, owner.Id, other.Id, TargetTypeEnum.Comment, target), ct).ConfigureAwait(false);
                    }
                    TestMember alex = await NotificationTestKit.MemberAsync(f, "Alex Chen", ct).ConfigureAwait(false);
                    Notification n = (await f.Services.Notifications.CreateAsync(NotificationTestKit.Draft(NotificationTypeEnum.Reaction, owner.Id, alex.Id, TargetTypeEnum.Comment, target), ct).ConfigureAwait(false))!;
                    Check.Equal("Alex Chen and 4 others reacted to your comment", n.Title, "Aggregated title");
                    Check.Equal(5, n.ActorCount, "Actor count");
                    Check.Equal(alex.Id, n.RecentActorIds[0], "Newest actor first");
                    Check.Equal(3, n.RecentActorIds.Count, "Three recent actors");
                }),
                Case("Dedupe.Priority", "One notification per recipient per source: CommentReply beats PostComment beats Mention", async (f, ct) =>
                {
                    List<NotificationDraft> drafts = new List<NotificationDraft>
                    {
                        NotificationTestKit.Draft(NotificationTypeEnum.Mention, "usr_a", "usr_x"),
                        NotificationTestKit.Draft(NotificationTypeEnum.CommentReply, "usr_a", "usr_x"),
                        NotificationTestKit.Draft(NotificationTypeEnum.Mention, "usr_b", "usr_x"),
                        NotificationTestKit.Draft(NotificationTypeEnum.PostComment, "usr_b", "usr_x"),
                        NotificationTestKit.Draft(NotificationTypeEnum.Mention, "usr_c", "usr_x"),
                        NotificationTestKit.Draft(NotificationTypeEnum.PostComment, "usr_d", "usr_x"),
                        NotificationTestKit.Draft(NotificationTypeEnum.CommentReply, "usr_d", "usr_x")
                    };
                    List<NotificationDraft> kept = NotificationService.SelectHighestPriority(drafts);
                    Check.Equal(4, kept.Count, "One per recipient");
                    Check.Equal(NotificationTypeEnum.CommentReply, kept.Single(d => d.RecipientUserId == "usr_a").Type, "Reply over mention");
                    Check.Equal(NotificationTypeEnum.PostComment, kept.Single(d => d.RecipientUserId == "usr_b").Type, "Post comment over mention");
                    Check.Equal(NotificationTypeEnum.Mention, kept.Single(d => d.RecipientUserId == "usr_c").Type, "Mention alone");
                    Check.Equal(NotificationTypeEnum.CommentReply, kept.Single(d => d.RecipientUserId == "usr_d").Type, "Reply over post comment");

                    TestMember author = await NotificationTestKit.MemberAsync(f, "Author", ct).ConfigureAwait(false);
                    TestMember replier = await NotificationTestKit.MemberAsync(f, "Replier", ct).ConfigureAwait(false);
                    List<Notification> created = await f.Services.Notifications.CreateForSourceAsync(new[]
                    {
                        NotificationTestKit.Draft(NotificationTypeEnum.Mention, author.Id, replier.Id),
                        NotificationTestKit.Draft(NotificationTypeEnum.CommentReply, author.Id, replier.Id)
                    }, ct).ConfigureAwait(false);
                    Check.Equal(1, created.Count, "Only the reply is created");
                    Check.Equal(NotificationTypeEnum.CommentReply, created[0].Type, "Reply");
                }),
                Case("Suppression.SelfAndBlocks", "Never notify the actor, never across a block either way; moderation notices pass blocks", async (f, ct) =>
                {
                    TestMember a = await NotificationTestKit.MemberAsync(f, "Member A", ct).ConfigureAwait(false);
                    TestMember b = await NotificationTestKit.MemberAsync(f, "Member B", ct).ConfigureAwait(false);
                    TestMember c = await NotificationTestKit.MemberAsync(f, "Member C", ct).ConfigureAwait(false);
                    NotificationService n = f.Services.Notifications;
                    Check.True(await n.CreateAsync(NotificationTestKit.Draft(NotificationTypeEnum.CommentReply, a.Id, a.Id), ct).ConfigureAwait(false) == null, "Self suppressed");
                    await NotificationTestKit.BlockAsync(f, a.Id, b.Id, ct).ConfigureAwait(false);
                    Check.True(await n.CreateAsync(NotificationTestKit.Draft(NotificationTypeEnum.CommentReply, a.Id, b.Id), ct).ConfigureAwait(false) == null, "Blocker gets nothing from blocked");
                    Check.True(await n.CreateAsync(NotificationTestKit.Draft(NotificationTypeEnum.Reaction, b.Id, a.Id), ct).ConfigureAwait(false) == null, "Blocked gets nothing from blocker");
                    Check.True(await n.CreateAsync(NotificationTestKit.Draft(NotificationTypeEnum.CommentReply, a.Id, c.Id), ct).ConfigureAwait(false) != null, "Unrelated member passes");
                    Check.Equal(0, await n.CreateManyAsync(new[] { NotificationTestKit.Draft(NotificationTypeEnum.Mention, a.Id, b.Id), NotificationTestKit.Draft(NotificationTypeEnum.Mention, b.Id, b.Id) }, false, ct).ConfigureAwait(false), "Bulk path suppresses too");
                    NotificationDraft moderation = NotificationTestKit.Draft(NotificationTypeEnum.ModerationAction, a.Id, b.Id);
                    Check.True(await n.CreateAsync(moderation, ct).ConfigureAwait(false) != null, "Moderation notice passes a block");
                    Check.Equal(2L, await n.UnreadCountAsync(a.Id, ct).ConfigureAwait(false), "Unread count");
                }),
                Case("Aggregation.Race", "Concurrent reactions from different members on one target produce exactly one unread notification", async (f, ct) =>
                {
                    TestMember owner = await NotificationTestKit.MemberAsync(f, "Owner", ct).ConfigureAwait(false);
                    List<TestMember> reactors = new List<TestMember>();
                    for (int i = 0; i < 8; i++) reactors.Add(await NotificationTestKit.MemberAsync(f, "Reactor " + i, ct).ConfigureAwait(false));
                    string target = "cmt_race";
                    await Task.WhenAll(reactors.Select(r => f.Services.Notifications.CreateAsync(NotificationTestKit.Draft(NotificationTypeEnum.Reaction, owner.Id, r.Id, TargetTypeEnum.Comment, target), ct))).ConfigureAwait(false);
                    List<Notification> rows = await NotificationTestKit.ListAsync(f, owner.Id, ct).ConfigureAwait(false);
                    Check.Equal(1, rows.Count, "One notification");
                    Check.Equal(8, rows[0].ActorCount, "Actor count");
                    Check.Equal(3, rows[0].RecentActorIds.Count, "Three recent actors");
                    Check.Equal("Reaction:Comment:" + target, rows[0].GroupKey, "Group key");
                    Check.True(rows[0].Title.EndsWith("and 7 others reacted to your comment", StringComparison.Ordinal), "Title " + rows[0].Title);
                    await f.Services.Notifications.CreateAsync(NotificationTestKit.Draft(NotificationTypeEnum.Reaction, owner.Id, reactors[0].Id, TargetTypeEnum.Comment, target), ct).ConfigureAwait(false);
                    Check.Equal(8, (await NotificationTestKit.ListAsync(f, owner.Id, ct).ConfigureAwait(false))[0].ActorCount, "Same actor again does not count twice");
                    await f.Services.Notifications.MarkReadAsync(owner.Id, new List<string> { rows[0].Id }, ct).ConfigureAwait(false);
                    Notification fresh = (await f.Services.Notifications.CreateAsync(NotificationTestKit.Draft(NotificationTypeEnum.Reaction, owner.Id, reactors[1].Id, TargetTypeEnum.Comment, target), ct).ConfigureAwait(false))!;
                    Check.True(fresh.Id != rows[0].Id, "A read notification is not updated");
                    Check.Equal(1, fresh.ActorCount, "New notification starts at one actor");
                    await f.Services.Notifications.MarkUnreadAsync(owner.Id, new List<string> { rows[0].Id }, ct).ConfigureAwait(false);
                    Check.Equal(1L, await f.Services.Notifications.UnreadCountAsync(owner.Id, ct).ConfigureAwait(false), "Unread of an aggregated notification yields to the open group");
                }),
                Case("Preferences.InAppOff", "A category with in-app off creates nothing; Moderation and Account cannot be turned off", async (f, ct) =>
                {
                    TestMember owner = await NotificationTestKit.MemberAsync(f, "Owner", ct).ConfigureAwait(false);
                    TestMember other = await NotificationTestKit.MemberAsync(f, "Other", ct).ConfigureAwait(false);
                    JsonElement doc = await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/me/notification-preferences", owner.AccessToken,
                        new { categories = new object[] { new { category = "Reactions", inApp = false } } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    JsonElement reactions = doc.GetProperty("categories")[2];
                    Check.True(!reactions.GetProperty("inApp").GetBoolean() && !reactions.GetProperty("push").GetBoolean(), "In-app off also turns push off");
                    Check.True(await f.Services.Notifications.CreateAsync(NotificationTestKit.Draft(NotificationTypeEnum.Reaction, owner.Id, other.Id), ct).ConfigureAwait(false) == null, "Reaction suppressed");
                    Check.True(await f.Services.Notifications.CreateAsync(NotificationTestKit.Draft(NotificationTypeEnum.CommentReply, owner.Id, other.Id), ct).ConfigureAwait(false) != null, "Replies still on");
                    await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/me/notification-preferences", owner.AccessToken,
                        new { categories = new object[] { new { category = "Account", inApp = false } } }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                }),
                Case("Center.ReadUnread", "read, unread, and read-all change counts; unknown and foreign ids are ignored; ids are validated", async (f, ct) =>
                {
                    TestMember owner = await NotificationTestKit.MemberAsync(f, "Owner", ct).ConfigureAwait(false);
                    TestMember other = await NotificationTestKit.MemberAsync(f, "Other", ct).ConfigureAwait(false);
                    List<string> ids = new List<string>();
                    for (int i = 0; i < 3; i++) ids.Add((await f.Services.Notifications.CreateAsync(NotificationTestKit.Draft(NotificationTypeEnum.CommentReply, owner.Id, other.Id), ct).ConfigureAwait(false))!.Id);
                    string foreign = (await f.Services.Notifications.CreateAsync(NotificationTestKit.Draft(NotificationTypeEnum.CommentReply, other.Id, owner.Id), ct).ConfigureAwait(false))!.Id;
                    JsonElement count = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/notifications/unread-count", owner.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(3L, count.GetProperty("unreadCount").GetInt64(), "Three unread");
                    Check.True(count.GetProperty("latestUpdatedUtc").ValueKind == JsonValueKind.String, "Latest updated");
                    JsonElement read = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/notifications/read", owner.AccessToken, new { ids = new[] { ids[0], foreign, "ntf_unknown" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(2L, read.GetProperty("unreadCount").GetInt64(), "Read one");
                    Check.Equal(1L, await f.Services.Notifications.UnreadCountAsync(other.Id, ct).ConfigureAwait(false), "Foreign untouched");
                    JsonElement unread = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/notifications/unread", owner.AccessToken, new { ids = new[] { ids[0] } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(3L, unread.GetProperty("unreadCount").GetInt64(), "Unread again");
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/notifications/read", owner.AccessToken, new { ids = Array.Empty<string>() }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/notifications/read", owner.AccessToken, new { ids = Enumerable.Range(0, 101).Select(i => "ntf_" + i).ToArray() }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    JsonElement before = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/notifications/read-all", owner.AccessToken, new { beforeUtc = DateTime.UtcNow.AddDays(-1) }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(3L, before.GetProperty("unreadCount").GetInt64(), "beforeUtc in the past marks nothing");
                    JsonElement all = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/notifications/read-all", owner.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(0L, all.GetProperty("unreadCount").GetInt64(), "All read");
                    TestMember lapsed = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/notifications/unread-count", lapsed.AccessToken, null, HttpStatusCode.PaymentRequired, "MembershipRequired", ct).ConfigureAwait(false);
                }),
                Case("Center.Enumeration", "The notification enumeration pages without duplicates, filters unreadOnly, validates, and carries the payload shape", async (f, ct) =>
                {
                    TestMember owner = await NotificationTestKit.MemberAsync(f, "Owner", ct).ConfigureAwait(false);
                    TestMember alex = await NotificationTestKit.MemberAsync(f, "Alex Chen", ct).ConfigureAwait(false);
                    List<string> created = new List<string>();
                    for (int i = 0; i < 5; i++)
                    {
                        NotificationDraft draft = NotificationTestKit.Draft(NotificationTypeEnum.CommentReply, owner.Id, alex.Id);
                        draft.CommentId = "cmt_c" + i;
                        draft.ParentCommentId = "cmt_p" + i;
                        draft.Body = "Does this work for beginners too?";
                        created.Add((await f.Services.Notifications.CreateAsync(draft, ct).ConfigureAwait(false))!.Id);
                        await Task.Delay(5, ct).ConfigureAwait(false);
                    }
                    await f.Services.Notifications.MarkReadAsync(owner.Id, new List<string> { created[0] }, ct).ConfigureAwait(false);
                    List<string> seen = new List<string>();
                    string? continuation = null;
                    int pages = 0;
                    do
                    {
                        JsonElement page = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/notifications/enumerate", owner.AccessToken, new { maxResults = 2, continuationToken = continuation }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                        if (pages == 0) Check.Equal(4L, page.GetProperty("unreadCount").GetInt64(), "unreadCount next to the page");
                        seen.AddRange(NotificationTestKit.Ids(page));
                        continuation = page.GetProperty("continuationToken").ValueKind == JsonValueKind.String ? page.GetProperty("continuationToken").GetString() : null;
                        if (pages++ == 0)
                        {
                            Check.Equal(5L, page.GetProperty("totalRecords").GetInt64(), "Total records");
                            if (continuation != null)
                            {
                                Notification late = (await f.Services.Notifications.CreateAsync(NotificationTestKit.Draft(NotificationTypeEnum.CommentReply, owner.Id, alex.Id), ct).ConfigureAwait(false))!;
                                created.Add(late.Id);
                            }
                        }
                    }
                    while (continuation != null && pages < 10);
                    Check.Equal(seen.Count, seen.Distinct().Count(), "No duplicates");
                    Check.Equal(5, seen.Count, "Every item before the insert, once");
                    Check.Equal(created[4], seen[0], "Newest first");

                    JsonElement first = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/notifications?maxResults=10", owner.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    JsonElement item = first.GetProperty("objects").EnumerateArray().First(o => Api.Str(o, "id") == created[1]);
                    Check.Equal("CommentReply", Api.Str(item, "type"), "Type");
                    Check.Equal("Replies", Api.Str(item, "category"), "Category");
                    Check.Equal("Alex Chen replied to your comment", Api.Str(item, "title"), "Title");
                    Check.Equal("Does this work for beginners too?", Api.Str(item, "body"), "Body");
                    Check.Equal(alex.Id, Api.Str(item.GetProperty("actor"), "id"), "Actor");
                    Check.Equal(1, item.GetProperty("actors").GetArrayLength(), "Actors");
                    Check.Equal(1, item.GetProperty("actorCount").GetInt32(), "Actor count");
                    Check.Equal("Comment", Api.Str(item.GetProperty("target"), "type"), "Target type");
                    Check.Equal("cmt_p1", Api.Str(item.GetProperty("context"), "parentCommentId"), "Context parent");
                    Check.Equal("/feed/fpo_parent?comment=cmt_c1", Api.Str(item, "path"), "Path");
                    Check.True(!item.GetProperty("isRead").GetBoolean() && item.GetProperty("readUtc").ValueKind == JsonValueKind.Null, "Unread");
                    Check.True(item.GetProperty("labels").ValueKind == JsonValueKind.Array && item.GetProperty("tags").ValueKind == JsonValueKind.Object, "Labels and tags");
                    JsonElement system = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/notifications?unreadOnly=true&maxResults=100", owner.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(!NotificationTestKit.Ids(system).Contains(created[0]), "unreadOnly drops read ones");
                    Check.Equal(5, system.GetProperty("objects").GetArrayLength(), "Unread only");
                    JsonElement filtered = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/notifications/enumerate", owner.AccessToken, new { filters = new { unreadOnly = true, category = "Replies" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(5, filtered.GetProperty("objects").GetArrayLength(), "Typed filters in POST");
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/notifications?ordering=Sideways", owner.AccessToken, null, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/notifications?unreadOnly=maybe", owner.AccessToken, null, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/notifications/enumerate", owner.AccessToken, new { continuationToken = "not-a-token" }, HttpStatusCode.BadRequest, "InvalidContinuationToken", ct).ConfigureAwait(false);
                    JsonElement clamped = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/notifications?maxResults=5000", owner.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(clamped.GetProperty("maxResults").GetInt32() <= 100, "maxResults clamped");
                    JsonElement others = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/notifications", alex.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(0, others.GetProperty("objects").GetArrayLength(), "Another member sees none of them");
                }),
                Case("Center.Metadata", "Notification enumeration filters on labels (All, Any) and tags (value and *), and strips staff metadata", async (f, ct) =>
                {
                    TestMember owner = await NotificationTestKit.MemberAsync(f, "Owner", ct).ConfigureAwait(false);
                    TestMember other = await NotificationTestKit.MemberAsync(f, "Other", ct).ConfigureAwait(false);
                    List<string> ids = new List<string>();
                    for (int i = 0; i < 3; i++) ids.Add((await f.Services.Notifications.CreateAsync(NotificationTestKit.Draft(NotificationTypeEnum.CommentReply, owner.Id, other.Id), ct).ConfigureAwait(false))!.Id);
                    await f.Database.Metadata.WriteAsync(TargetTypeEnum.Notification, ids[0], new ObjectMetadata { Labels = new List<string> { "alpha", "beta", "staff:secret" }, Tags = new Dictionary<string, string> { { "level", "high" } } }, ct).ConfigureAwait(false);
                    await f.Database.Metadata.WriteAsync(TargetTypeEnum.Notification, ids[1], new ObjectMetadata { Labels = new List<string> { "alpha" }, Tags = new Dictionary<string, string> { { "level", "low" } } }, ct).ConfigureAwait(false);
                    async Task<List<string>> Query(object body)
                    {
                        JsonElement page = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/notifications/enumerate", owner.AccessToken, body, HttpStatusCode.OK, ct).ConfigureAwait(false);
                        return NotificationTestKit.Ids(page);
                    }
                    Check.Equal(2, (await Query(new { labels = new[] { "alpha" } }).ConfigureAwait(false)).Count, "Label alpha");
                    Check.Equal(1, (await Query(new { labels = new[] { "alpha", "beta" }, labelMatch = "All" }).ConfigureAwait(false)).Count, "All");
                    Check.Equal(2, (await Query(new { labels = new[] { "beta", "alpha" }, labelMatch = "Any" }).ConfigureAwait(false)).Count, "Any");
                    Check.Equal(ids[1], (await Query(new { tags = new Dictionary<string, string> { { "level", "low" } } }).ConfigureAwait(false)).Single(), "Tag value");
                    Check.Equal(2, (await Query(new { tags = new Dictionary<string, string> { { "level", "*" } } }).ConfigureAwait(false)).Count, "Tag present");
                    JsonElement get = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/notifications?labels=beta", owner.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    JsonElement item = get.GetProperty("objects")[0];
                    List<string> labels = item.GetProperty("labels").EnumerateArray().Select(l => l.GetString()!).ToList();
                    Check.True(labels.Contains("alpha") && !labels.Any(l => l.StartsWith("staff:", StringComparison.Ordinal)), "Staff labels stripped for members");
                    Check.Equal("high", Api.Str(item.GetProperty("tags"), "level"), "Tags present");
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/notifications/enumerate", owner.AccessToken, new { labels = new[] { "staff:secret" } }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    await f.Services.Notifications.MarkReadAsync(owner.Id, ids, ct).ConfigureAwait(false);
                    f.Settings.Notifications.RetentionDays = 7;
                    NotificationPruneWorker prune = new NotificationPruneWorker(f.Database, f.Settings.Notifications, f.Services.Logger) { Clock = () => DateTime.UtcNow.AddDays(8) };
                    await using (prune.ConfigureAwait(false)) Check.True(await prune.RunOnceAsync(ct).ConfigureAwait(false) >= 3, "Prune deletes read notifications past retention");
                    Check.Equal(0L, await NotificationTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM labels WHERE targettype = 'Notification';", ct).ConfigureAwait(false), "Labels deleted with them");
                    Check.Equal(0L, await NotificationTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM notifications WHERE recipientuserid = '" + owner.Id + "';", ct).ConfigureAwait(false), "Pruned");
                }),
                Case("Preferences.Document", "GET returns the defaults and the editable matrix; settings supply default hours", async (f, ct) =>
                {
                    f.Settings.Notifications.DefaultHoursStart = "08:30";
                    TestMember member = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    JsonElement doc = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/me/notification-preferences", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(!doc.GetProperty("pushPaused").GetBoolean(), "Not paused");
                    string[] order = { "Replies", "Mentions", "Reactions", "FeedPosts", "Moderation", "Account", "Following", "Forums", "Events", "Library" };
                    bool[] email = { false, false, false, true, false, true, false, false, true, false };
                    bool[] editableInApp = { true, true, true, true, false, false, true, true, true, true };
                    bool[] editableEmail = { false, false, false, true, false, false, true, true, true, true };
                    JsonElement categories = doc.GetProperty("objects");
                    Check.True(categories.GetArrayLength() >= 10, "At least the ten Phase 2 to 4 categories, first and in order (Phase 5 appends its categories)");
                    for (int i = 0; i < 10; i++)
                    {
                        JsonElement c = categories.EnumerateArray().Single(item => Api.Str(item, "category") == order[i]);
                        Check.Equal(order[i], Api.Str(c, "category"), "Order " + i);
                        Check.True(c.GetProperty("inApp").GetBoolean() && c.GetProperty("push").GetBoolean(), order[i] + " in-app and push on");
                        Check.Equal(email[i], c.GetProperty("email").GetBoolean(), order[i] + " email");
                        Check.Equal(editableInApp[i], c.GetProperty("editable").GetProperty("inApp").GetBoolean(), order[i] + " editable in-app");
                        Check.True(c.GetProperty("editable").GetProperty("push").GetBoolean(), order[i] + " editable push");
                        Check.Equal(editableEmail[i], c.GetProperty("editable").GetProperty("email").GetBoolean(), order[i] + " editable email");
                    }
                    JsonElement hours = doc.GetProperty("notificationHours");
                    Check.True(hours.GetProperty("enabled").GetBoolean(), "Hours enabled");
                    Check.Equal("08:30", Api.Str(hours, "start"), "Start from settings");
                    Check.Equal("22:00", Api.Str(hours, "end"), "End default");
                }),
                Case("Preferences.Validation", "PUT rejects non-editable cells, unknown categories, malformed HH:mm, and non-IANA zones; applies the rest", async (f, ct) =>
                {
                    TestMember member = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    const string path = "/api/v1/me/notification-preferences";
                    async Task<JsonElement> Fails(object body, string field)
                    {
                        ApiResult r = await Api.SendAsync(f, HttpMethod.Put, path, member.AccessToken, body, ct).ConfigureAwait(false);
                        Check.Equal(HttpStatusCode.BadRequest, r.Status, field + " status (" + r.Text + ")");
                        Check.Equal("ValidationFailed", r.Error, field + " error");
                        Check.True(r.Body.GetProperty("fieldErrors").TryGetProperty(field, out JsonElement _), field + " field error (" + r.Text + ")");
                        return r.Body;
                    }
                    await Fails(new { categories = new object[] { new { category = "Moderation", inApp = false } } }, "categories[0].inApp").ConfigureAwait(false);
                    await Fails(new { categories = new object[] { new { category = "Replies", push = false }, new { category = "Replies", email = true } } }, "categories[1].email").ConfigureAwait(false);
                    await Fails(new { categories = new object[] { new { category = "Account", email = false } } }, "categories[0].email").ConfigureAwait(false);
                    await Fails(new { categories = new object[] { new { category = "Gossip", push = false } } }, "categories[0].category").ConfigureAwait(false);
                    await Fails(new { notificationHours = new { start = "7:00" } }, "notificationHours.start").ConfigureAwait(false);
                    await Fails(new { notificationHours = new { end = "24:00" } }, "notificationHours.end").ConfigureAwait(false);
                    await Fails(new { notificationHours = new { timeZone = "Mars/Olympus_Mons" } }, "notificationHours.timeZone").ConfigureAwait(false);
                    await Fails(new { notificationHours = new { timeZone = "Pacific Standard Time" } }, "notificationHours.timeZone").ConfigureAwait(false);
                    JsonElement doc0 = await Api.ExpectAsync(f, HttpMethod.Get, path, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(doc0.GetProperty("objects").EnumerateArray().Single(c => Api.Str(c, "category") == "Replies").GetProperty("push").GetBoolean(), "A failed PUT changes nothing");
                    JsonElement doc = await Api.ExpectAsync(f, HttpMethod.Put, path, member.AccessToken, new
                    {
                        categories = new object[] { new { category = "reactions", push = false }, new { category = "Moderation", inApp = true }, new { category = "FeedPosts", email = false } },
                        notificationHours = new { start = "08:00", end = "06:30", timeZone = "America/Los_Angeles" },
                        pushPaused = true
                    }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(!doc.GetProperty("categories")[2].GetProperty("push").GetBoolean() && doc.GetProperty("categories")[2].GetProperty("inApp").GetBoolean(), "Reactions push off only");
                    Check.True(!doc.GetProperty("categories")[3].GetProperty("email").GetBoolean(), "FeedPosts email off");
                    Check.True(doc.GetProperty("pushPaused").GetBoolean(), "Paused");
                    Check.Equal("08:00", Api.Str(doc.GetProperty("notificationHours"), "start"), "Start");
                    Check.Equal("06:30", Api.Str(doc.GetProperty("notificationHours"), "end"), "End");
                    Check.Equal("America/Los_Angeles", Api.Str(doc.GetProperty("notificationHours"), "timeZone"), "Zone");
                    JsonElement partial = await Api.ExpectAsync(f, HttpMethod.Put, path, member.AccessToken, new { pushPaused = false }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(!partial.GetProperty("pushPaused").GetBoolean() && Api.Str(partial.GetProperty("notificationHours"), "start") == "08:00", "Partial update keeps the rest");
                }),
                Case("Hours.FixedClock", "Notification hours: plain window, crossing midnight, start equal to end, disabled, and time zones", ct =>
                {
                    DateTime day = new DateTime(2026, 11, 12, 0, 0, 0, DateTimeKind.Utc);
                    NotificationHours plain = new NotificationHours { StartMinute = 420, EndMinute = 1320, TimeZone = "UTC" };
                    Check.True(!NotificationHoursRule.IsWithin(plain, day.AddHours(6).AddMinutes(59)), "06:59 outside");
                    Check.True(NotificationHoursRule.IsWithin(plain, day.AddHours(7)), "07:00 inside");
                    Check.True(NotificationHoursRule.IsWithin(plain, day.AddHours(21).AddMinutes(59)), "21:59 inside");
                    Check.True(!NotificationHoursRule.IsWithin(plain, day.AddHours(22)), "22:00 outside");
                    NotificationHours night = new NotificationHours { StartMinute = 1320, EndMinute = 360, TimeZone = "UTC" };
                    Check.True(NotificationHoursRule.IsWithin(night, day.AddHours(23)), "23:00 inside a midnight window");
                    Check.True(NotificationHoursRule.IsWithin(night, day.AddHours(3)), "03:00 inside a midnight window");
                    Check.True(!NotificationHoursRule.IsWithin(night, day.AddHours(12)), "12:00 outside a midnight window");
                    Check.True(NotificationHoursRule.IsWithin(new NotificationHours { StartMinute = 600, EndMinute = 600, TimeZone = "UTC" }, day.AddHours(3)), "Start equal to end is all day");
                    Check.True(NotificationHoursRule.IsWithin(new NotificationHours { Enabled = false, StartMinute = 420, EndMinute = 1320 }, day.AddHours(3)), "Disabled is all day");
                    NotificationHours la = new NotificationHours { StartMinute = 420, EndMinute = 1320, TimeZone = "America/Los_Angeles" };
                    Check.True(NotificationHoursRule.IsWithin(la, day.AddHours(16)), "16:00 UTC is 08:00 in Los Angeles");
                    Check.True(!NotificationHoursRule.IsWithin(la, day.AddHours(8)), "08:00 UTC is midnight in Los Angeles");
                    Check.True(!NotificationHoursRule.IsWithin(new NotificationHours { StartMinute = 420, EndMinute = 1320 }, day.AddHours(3)), "No zone means UTC");
                    Check.Equal(420, NotificationHoursRule.ParseMinute("07:00") ?? -1, "Parse");
                    Check.True(NotificationHoursRule.ParseMinute("7:00") == null && NotificationHoursRule.ParseMinute("07:60") == null && NotificationHoursRule.ParseMinute("ab:cd") == null, "Malformed");
                    Check.True(NotificationHoursRule.IsIanaTimeZone("Europe/Berlin") && NotificationHoursRule.IsIanaTimeZone("UTC") && !NotificationHoursRule.IsIanaTimeZone("Pacific Standard Time"), "IANA check");
                    return Task.CompletedTask;
                }),
                Case("RateLimit.UnreadCount", "The unread count route is limited per minute with Retry-After", async (f, ct) =>
                {
                    f.Settings.RateLimits.UnreadCountPerMinute = 2;
                    TestMember member = await NotificationTestKit.MemberAsync(f, "Poller", ct).ConfigureAwait(false);
                    for (int i = 0; i < 2; i++) await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/notifications/unread-count", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/notifications/unread-count"))
                    {
                        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + member.AccessToken);
                        using (HttpResponseMessage response = await f.Client.SendAsync(request, ct).ConfigureAwait(false))
                        {
                            Check.Equal(HttpStatusCode.TooManyRequests, response.StatusCode, "429");
                            Check.True(response.Headers.TryGetValues("Retry-After", out IEnumerable<string>? values) && Int32.Parse(values.First()) > 0, "Retry-After");
                        }
                    }
                }),
                Case("Fanout.IntervalSetting", "The fan-out worker interval is the live setting notifications.fanoutIntervalSeconds (default 15, 1 to 3600), listed in the settings registry with the export limits", async (f, ct) =>
                {
                    NotificationFanoutWorker worker = NotificationTestKit.FanoutWorker(f);
                    await using (worker.ConfigureAwait(false))
                    {
                        Check.Equal(15, worker.IntervalSeconds, "Default 15 seconds");
                        f.Settings.Notifications.FanoutIntervalSeconds = 42;
                        Check.Equal(42, worker.IntervalSeconds, "Follows the live setting");
                        f.Settings.Notifications.FanoutIntervalSeconds = 0;
                        Check.Equal(1, worker.IntervalSeconds, "Clamped to the minimum 1");
                        f.Settings.Notifications.FanoutIntervalSeconds = 99999;
                        Check.Equal(3600, worker.IntervalSeconds, "Clamped to the maximum 3600");
                    }
                    foreach (string key in new[] { "notifications.fanoutIntervalSeconds", "account.exportsPerDay", "account.exportDownloadLinkMinutes" })
                    {
                        JsonElement settings = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/settings?search=" + Uri.EscapeDataString(key), HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                        Check.True(settings.GetRawText().Contains("\"" + key + "\"", StringComparison.Ordinal), "Settings registry lists " + key);
                    }
                }),
                Case("Account.Deletion", "Deleting the account removes received notifications, actor rows, tokens, preferences, hours, and email sends", async (f, ct) =>
                {
                    TestMember owner = await NotificationTestKit.MemberAsync(f, "Leaving", ct).ConfigureAwait(false);
                    TestMember other = await NotificationTestKit.MemberAsync(f, "Staying", ct).ConfigureAwait(false);
                    await f.Services.Notifications.CreateAsync(NotificationTestKit.Draft(NotificationTypeEnum.CommentReply, owner.Id, other.Id), ct).ConfigureAwait(false);
                    await f.Services.Notifications.CreateAsync(NotificationTestKit.Draft(NotificationTypeEnum.Reaction, other.Id, owner.Id), ct).ConfigureAwait(false);
                    await NotificationTestKit.RegisterTokenAsync(f, owner, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/me/notification-preferences", owner.AccessToken, new { categories = new object[] { new { category = "Reactions", push = false } }, pushPaused = true }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    NotificationExportData export = await f.Services.NotificationPreferences.ExportAsync(owner.Id, ct).ConfigureAwait(false);
                    Check.Equal(1, export.PushTokens.Count, "Export lists the token");
                    Check.True(export.NotificationPreferences.PushPaused, "Export carries the document");
                    Check.True(!ApiJsonText(export).Contains("ExponentPushToken", StringComparison.Ordinal), "Export never contains token values");
                    await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/me", owner.AccessToken, new { confirm = "DELETE" }, HttpStatusCode.Accepted, ct).ConfigureAwait(false);
                    Check.Equal(0L, await NotificationTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM notifications WHERE recipientuserid = '" + owner.Id + "';", ct).ConfigureAwait(false), "Notifications");
                    Check.Equal(0L, await NotificationTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM notificationactors WHERE userid = '" + owner.Id + "';", ct).ConfigureAwait(false), "Actor rows");
                    Check.Equal(0L, await NotificationTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM pushtokens WHERE userid = '" + owner.Id + "';", ct).ConfigureAwait(false), "Tokens");
                    Check.Equal(0L, await NotificationTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM notificationpreferences WHERE userid = '" + owner.Id + "';", ct).ConfigureAwait(false), "Preferences");
                    Check.Equal(0L, await NotificationTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM notificationhours WHERE userid = '" + owner.Id + "';", ct).ConfigureAwait(false), "Hours");
                    Check.Equal(0L, await f.Services.Notifications.UnreadCountAsync(other.Id, ct).ConfigureAwait(false), "The other member's notification from the deleted member is gone");
                }),
                Case("Regression.API02.DeletedActorScrubbed", "After account deletion, other members' notifications carry neither the deleted member's name nor their erased text; aggregates re-render", async (f, ct) =>
                {
                    NotificationService n = f.Services.Notifications;
                    TestMember owner = await NotificationTestKit.MemberAsync(f, "Owner Person", ct).ConfigureAwait(false);
                    TestMember zed = await NotificationTestKit.MemberAsync(f, "Zed Leaving", ct).ConfigureAwait(false);
                    TestMember a = await NotificationTestKit.MemberAsync(f, "Alpha Reactor", ct).ConfigureAwait(false);
                    TestMember b = await NotificationTestKit.MemberAsync(f, "Bravo Reactor", ct).ConfigureAwait(false);
                    TestMember c = await NotificationTestKit.MemberAsync(f, "Charlie Reactor", ct).ConfigureAwait(false);
                    NotificationDraft reply = NotificationTestKit.Draft(NotificationTypeEnum.CommentReply, owner.Id, zed.Id);
                    reply.Body = "Zed's secret erased words";
                    await n.CreateAsync(reply, ct).ConfigureAwait(false);
                    foreach (string actor in new[] { a.Id, b.Id, zed.Id })
                        await n.CreateAsync(NotificationTestKit.Draft(NotificationTypeEnum.Reaction, owner.Id, actor, TargetTypeEnum.Comment, "cmt_zed_last"), ct).ConfigureAwait(false);
                    foreach (string actor in new[] { zed.Id, a.Id, b.Id, c.Id })
                        await n.CreateAsync(NotificationTestKit.Draft(NotificationTypeEnum.Reaction, owner.Id, actor, TargetTypeEnum.FeedPost, "fpo_zed_first"), ct).ConfigureAwait(false);
                    NotificationDraft moderation = NotificationTestKit.Draft(NotificationTypeEnum.ModerationAction, owner.Id, zed.Id);
                    moderation.ModerationAction = ModerationActionTypeEnum.Remove;
                    await n.CreateAsync(moderation, ct).ConfigureAwait(false);
                    Check.True((await NotificationTestKit.ListAsync(f, owner.Id, ct).ConfigureAwait(false)).Any(x => x.Title.Contains("Zed", StringComparison.Ordinal)), "Precondition: Zed named");

                    await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/me", zed.AccessToken, new { confirm = "DELETE" }, HttpStatusCode.Accepted, ct).ConfigureAwait(false);

                    List<Notification> after = await NotificationTestKit.ListAsync(f, owner.Id, ct).ConfigureAwait(false);
                    Check.Equal(3, after.Count, "Reply removed; two aggregates and the moderation notice stay");
                    Check.True(after.All(x => !x.Title.Contains("Zed", StringComparison.Ordinal) && !(x.Body ?? String.Empty).Contains("Zed", StringComparison.Ordinal)), "No name or erased text remains");
                    Check.True(after.All(x => x.ActorUserId != zed.Id && !x.RecentActorIds.Contains(zed.Id)), "No actor reference remains");
                    Notification last = after.Single(x => x.TargetId == "cmt_zed_last");
                    Check.Equal("Bravo Reactor and 1 other reacted to your comment", last.Title, "Re-rendered from the newest remaining actor");
                    Check.Equal(2, last.ActorCount, "Count without the deleted member");
                    Check.Equal(b.Id, last.ActorUserId, "Newest remaining actor");
                    Notification first = after.Single(x => x.TargetId == "fpo_zed_first");
                    Check.Equal("Charlie Reactor and 2 others reacted to your post", first.Title, "Count decremented");
                    Check.Equal(3, first.ActorCount, "Three remaining actors");
                    Notification notice = after.Single(x => x.Type == NotificationTypeEnum.ModerationAction);
                    Check.True(notice.ActorUserId == null, "Moderation notice keeps its text without the actor");
                    JsonElement page = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/notifications", owner.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(!page.GetRawText().Contains("Zed", StringComparison.Ordinal), "The API shows no trace of the deleted member");
                }),
                Case("Hook.MembershipExpiring", "The code reminder worker also creates a MembershipExpiring notification at 14 and 3 days", async (f, ct) =>
                {
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/signup-codes", HttpFixture.AdminToken, new { code = "NOTIFY001", name = "Notify", grantType = "DurationMonths", grantMonths = 1 }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                    TestMember member = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/signup-codes/redeem", member.AccessToken, new { code = "NOTIFY001" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    DateTime ends = DateTime.UtcNow.AddDays(10);
                    await SetEndAsync(f, member.Id, ends, ct).ConfigureAwait(false);
                    SignupCodeReminderWorker worker = new SignupCodeReminderWorker(f.Database, f.Services.Membership, f.Services.Email, f.Settings.SignupCodes, f.Settings.Membership, f.Services.Logger) { Notifications = f.Services.Notifications };
                    await using (worker.ConfigureAwait(false))
                    {
                        await worker.RunOnceAsync(ct).ConfigureAwait(false);
                        await worker.RunOnceAsync(ct).ConfigureAwait(false);
                        List<Notification> first = await NotificationTestKit.ListAsync(f, member.Id, ct).ConfigureAwait(false);
                        Check.Equal(1, first.Count, "One notification at 14 days");
                        Check.Equal(NotificationTypeEnum.MembershipExpiring, first[0].Type, "Type");
                        Check.Equal(NotificationCategoryEnum.Account, first[0].Category, "Category");
                        Check.Equal("/account/membership", first[0].Path, "Path");
                        Check.Equal("Your free membership ends on " + ends.ToString("MMMM d, yyyy", System.Globalization.CultureInfo.InvariantCulture), first[0].Title, "Title");
                        Check.Equal(PushStateEnum.Pending, first[0].PushState, "Push queued");
                        await SetEndAsync(f, member.Id, DateTime.UtcNow.AddDays(2), ct).ConfigureAwait(false);
                        await worker.RunOnceAsync(ct).ConfigureAwait(false);
                        Check.Equal(2, (await NotificationTestKit.ListAsync(f, member.Id, ct).ConfigureAwait(false)).Count, "Second at 3 days");
                    }
                })
            });
        }

        #endregion

        #region Private-Methods

        private static string ApiJsonText(object value)
        {
            return TrellisKit.Server.Serialization.ApiJson.Serialize(value);
        }

        private static async Task SetEndAsync(HttpFixture f, string userId, DateTime endsUtc, CancellationToken ct)
        {
            List<SignupCodeRedemption> redemptions = await f.Database.SignupCodeRedemptions.EnumerateByUserAsync(userId, ct).ConfigureAwait(false);
            foreach (SignupCodeRedemption redemption in redemptions)
            {
                redemption.MembershipEndsUtc = endsUtc;
                await f.Database.SignupCodeRedemptions.UpdateAsync(redemption, ct).ConfigureAwait(false);
                Entitlement? entitlement = await f.Database.Entitlements.ReadAsync(redemption.EntitlementId, ct).ConfigureAwait(false);
                entitlement!.EndsUtc = endsUtc;
                await f.Database.Entitlements.UpdateAsync(entitlement, ct).ConfigureAwait(false);
            }
        }

        private static TestCaseDescriptor Case(string caseId, string name, Func<HttpFixture, CancellationToken, Task> body)
        {
            return SuiteCases.Http(Id, caseId, name, body);
        }

        private static TestCaseDescriptor Case(string caseId, string name, Func<CancellationToken, Task> body)
        {
            return SuiteCases.Unit(Id, caseId, name, body);
        }

        #endregion
    }
}

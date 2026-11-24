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

    /// <summary>
    /// Phase 5 direct conversations (docs/PHASE5_CONTRACT.md sections 3.1, 3.2): one conversation per pair (also under
    /// concurrency), requests and their limits, accept, decline and undo, message policies, staff bypass, blocks in both
    /// directions with unblock restore, folders, mutes, delete for me with revival, and viewer flags.
    /// </summary>
    public static class ConversationSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "Conversations";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Direct conversations (" + TestEnvironment.Provider + ")", new[]
            {
                Case("Conversations.StartAndDirectKey", "Start returns 201 then 200 with the same conversation; concurrent starts from both members store one conversation", async (f, ct) =>
                {
                    TestMember a = await MessagingTestKit.MemberAsync(f, ct, "Ann Starter").ConfigureAwait(false);
                    TestMember b = await MessagingTestKit.MemberAsync(f, ct, "Bob Receiver").ConfigureAwait(false);
                    JsonElement first = await MessagingTestKit.StartAsync(f, a, b.Id, "Hello Bob", ct).ConfigureAwait(false);
                    JsonElement conversation = first.GetProperty("conversation");
                    string id = Api.Str(conversation, "id");
                    Check.True(id.StartsWith("cnv_", StringComparison.Ordinal), "cnv_ id");
                    Check.Equal("AwaitingAcceptance", Api.Str(conversation, "requestState"), "Sender awaits acceptance");
                    Check.Equal(1L, conversation.GetProperty("lastSeq").GetInt64(), "lastSeq 1");
                    Check.Equal(2, conversation.GetProperty("members").GetArrayLength(), "Two members");
                    Check.Equal(b.Id, Api.Str(conversation.GetProperty("otherMember"), "id"), "otherMember");
                    Check.Equal(1L, first.GetProperty("message").GetProperty("seq").GetInt64(), "First message seq 1");
                    JsonElement again = await MessagingTestKit.StartAsync(f, a, b.Id, null, ct, HttpStatusCode.OK).ConfigureAwait(false);
                    Check.Equal(id, Api.Str(again.GetProperty("conversation"), "id"), "Same conversation");
                    JsonElement reverse = await MessagingTestKit.StartAsync(f, b, a.Id, null, ct, HttpStatusCode.OK).ConfigureAwait(false);
                    Check.Equal(id, Api.Str(reverse.GetProperty("conversation"), "id"), "Same conversation from the other side");
                    Check.Equal("Pending", Api.Str(reverse.GetProperty("conversation"), "requestState"), "Recipient sees Pending");

                    f.Settings.Messaging.NewAccountHours = 0;
                    TestMember c = await MessagingTestKit.MemberAsync(f, ct, "Cy Racer").ConfigureAwait(false);
                    TestMember d = await MessagingTestKit.MemberAsync(f, ct, "Di Racer").ConfigureAwait(false);
                    List<Task<ApiResult>> racing = Enumerable.Range(0, 8).Select(i => Api.SendAsync(f, HttpMethod.Post, MessagingTestKit.Conversations, i % 2 == 0 ? c.AccessToken : d.AccessToken,
                        new { recipientUserId = i % 2 == 0 ? d.Id : c.Id }, ct)).ToList();
                    ApiResult[] results = await Task.WhenAll(racing).ConfigureAwait(false);
                    Check.True(results.All(r => r.Status == HttpStatusCode.Created || r.Status == HttpStatusCode.OK), "All 201 or 200: " + String.Join(",", results.Select(r => (int)r.Status)));
                    Check.Equal(1, results.Select(r => Api.Str(r.Body.GetProperty("conversation"), "id")).Distinct().Count(), "One conversation id");
                    Check.Equal(1L, await MessagingTestKit.CountAsync(f, "conversations", "directkey LIKE '%" + c.Id + "%' AND directkey LIKE '%" + d.Id + "%'", ct).ConfigureAwait(false), "One row");
                    Check.Equal(2L, await MessagingTestKit.CountAsync(f, "conversationmembers", "conversationid = '" + Api.Str(results[0].Body.GetProperty("conversation"), "id") + "'", ct).ConfigureAwait(false), "Two member rows");
                }),
                Case("Conversations.StartErrors", "Self, unknown, missing recipient, the admin token, and an empty first message are refused without leaving a conversation", async (f, ct) =>
                {
                    TestMember a = await MessagingTestKit.MemberAsync(f, ct).ConfigureAwait(false);
                    TestMember b = await MessagingTestKit.MemberAsync(f, ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, MessagingTestKit.Conversations, a.AccessToken, new { recipientUserId = a.Id }, HttpStatusCode.BadRequest, "CannotMessageSelf", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, MessagingTestKit.Conversations, a.AccessToken, new { recipientUserId = "usr_unknown" }, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, MessagingTestKit.Conversations, a.AccessToken, new { }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, MessagingTestKit.Conversations, HttpFixture.AdminToken, new { recipientUserId = b.Id }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, MessagingTestKit.Conversations, a.AccessToken, new { recipientUserId = b.Id, message = new { clientMessageId = "x1", body = "   " } }, HttpStatusCode.BadRequest, "MessageEmpty", ct).ConfigureAwait(false);
                    Check.Equal(0L, await MessagingTestKit.CountAsync(f, "conversations", "createdbyuserid = '" + a.Id + "'", ct).ConfigureAwait(false), "Nothing stored");
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, MessagingTestKit.Conversations + "/cnv_unknown", a.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
                    f.Settings.Messaging.NewAccountConversationsPerDay = 1;
                    TestMember fresh = await MessagingTestKit.MemberAsync(f, ct).ConfigureAwait(false);
                    await MessagingTestKit.StartIdAsync(f, fresh, a.Id, null, ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, MessagingTestKit.Conversations, fresh.AccessToken, new { recipientUserId = b.Id }, HttpStatusCode.Forbidden, "MessagingRestricted", ct).ConfigureAwait(false);
                    f.Settings.Messaging.NewAccountHours = 0;
                    f.Settings.Messaging.NewConversationsPerDay = 1;
                    TestMember older = await MessagingTestKit.MemberAsync(f, ct).ConfigureAwait(false);
                    await MessagingTestKit.StartIdAsync(f, older, a.Id, null, ct).ConfigureAwait(false);
                    ApiResult limited = await Api.SendAsync(f, HttpMethod.Post, MessagingTestKit.Conversations, older.AccessToken, new { recipientUserId = b.Id }, ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.TooManyRequests, limited.Status, "429 past messaging.newConversationsPerDay");
                    Check.True(Int32.TryParse(limited.RetryAfter, out int retry) && retry > 0, "Retry-After");
                }),
                Case("Conversations.RequestsLimitAcceptDeclineUndo", "A request lands in Requests, allows messaging.requestMessageLimit messages, declines silently, and the recipient can undo from the declined list", async (f, ct) =>
                {
                    f.Settings.Messaging.RequestMessageLimit = 3;
                    TestMember a = await MessagingTestKit.MemberAsync(f, ct, "Ann Asker").ConfigureAwait(false);
                    TestMember b = await MessagingTestKit.MemberAsync(f, ct, "Bea Busy").ConfigureAwait(false);
                    string id = await MessagingTestKit.StartIdAsync(f, a, b.Id, "one", ct).ConfigureAwait(false);
                    await MessagingTestKit.SendOkAsync(f, a, id, "two", ct).ConfigureAwait(false);
                    await MessagingTestKit.SendOkAsync(f, a, id, "three", ct).ConfigureAwait(false);
                    ApiResult fourth = await MessagingTestKit.SendAsync(f, a, id, "four", ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.Forbidden, fourth.Status, "Fourth refused");
                    Check.Equal("RequestLimitReached", fourth.Error, "RequestLimitReached");
                    JsonElement mine = await Api.ExpectAsync(f, HttpMethod.Get, MessagingTestKit.Conversations + "/" + id, a.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("RequestPending", Api.Str(mine.GetProperty("viewer"), "cannotSendReason"), "Sender sees RequestPending");
                    Check.True(!(await MessagingTestKit.ListIdsAsync(f, b, null, ct).ConfigureAwait(false)).Contains(id), "Not in the recipient's Inbox");
                    Check.True((await MessagingTestKit.ListIdsAsync(f, b, new { filters = new { folder = "Requests" } }, ct).ConfigureAwait(false)).Contains(id), "In the recipient's Requests");
                    Check.True((await MessagingTestKit.ListIdsAsync(f, a, null, ct).ConfigureAwait(false)).Contains(id), "In the sender's Inbox");
                    JsonElement counts = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/me/messaging/unread-count", b.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(1L, counts.GetProperty("requests").GetInt64(), "One request");
                    Check.Equal(0L, counts.GetProperty("conversations").GetInt64(), "Requests do not count as unread conversations");
                    JsonElement theirs = await Api.ExpectAsync(f, HttpMethod.Get, MessagingTestKit.Conversations + "/" + id, b.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(theirs.GetProperty("viewer").GetProperty("canAccept").GetBoolean() && theirs.GetProperty("viewer").GetProperty("canDecline").GetBoolean(), "Recipient may accept and decline");
                    Check.Equal("Requests", Api.Str(theirs, "folder"), "Requests folder");

                    await Api.ExpectErrorAsync(f, HttpMethod.Post, MessagingTestKit.Conversations + "/" + id + "/accept", a.AccessToken, null, HttpStatusCode.Conflict, "NotPendingRequest", ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Post, MessagingTestKit.Conversations + "/" + id + "/decline", b.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, MessagingTestKit.Conversations + "/" + id + "/decline", b.AccessToken, null, HttpStatusCode.Conflict, "NotPendingRequest", ct).ConfigureAwait(false);
                    Check.True(!(await MessagingTestKit.ListIdsAsync(f, b, new { filters = new { folder = "All" } }, ct).ConfigureAwait(false)).Contains(id), "Declined leaves every list");
                    JsonElement declined = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/me/messaging/declined/enumerate", b.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(id, Api.Str(declined.GetProperty("objects")[0], "id"), "Declined list");
                    JsonElement sender = await Api.ExpectAsync(f, HttpMethod.Get, MessagingTestKit.Conversations + "/" + id, a.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("AwaitingAcceptance", Api.Str(sender, "requestState"), "The sender is not told");
                    Check.Equal(3, MessagingTestKit.Seqs(await MessagingTestKit.HistoryAsync(f, a, id, null, ct).ConfigureAwait(false)).Count, "The sender still sees their messages");

                    await Api.ExpectAsync(f, HttpMethod.Post, MessagingTestKit.Conversations + "/" + id + "/accept", b.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True((await MessagingTestKit.ListIdsAsync(f, b, null, ct).ConfigureAwait(false)).Contains(id), "Undo: back in the Inbox");
                    await MessagingTestKit.SendOkAsync(f, a, id, "four", ct).ConfigureAwait(false);
                    await MessagingTestKit.SendOkAsync(f, a, id, "five", ct).ConfigureAwait(false);
                    JsonElement accepted = await Api.ExpectAsync(f, HttpMethod.Get, MessagingTestKit.Conversations + "/" + id, a.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(JsonValueKind.Null, accepted.GetProperty("requestState").ValueKind, "Accepted");
                }),
                Case("Conversations.AcceptedLive", "Accepting a request publishes conversation.updated to the requester's socket with requestState null, so the waiting banner clears without a reload", async (f, ct) =>
                {
                    TestMember a = await MessagingTestKit.MemberAsync(f, ct, "Ann Waiting").ConfigureAwait(false);
                    TestMember b = await MessagingTestKit.MemberAsync(f, ct, "Bo Accepting").ConfigureAwait(false);
                    string id = await MessagingTestKit.StartIdAsync(f, a, b.Id, "hello there", ct).ConfigureAwait(false);
                    RealtimeTestClient socket = await RealtimeTestKit.OpenAsync(f, a.AccessToken, ct, new Dictionary<string, object?> { ["capabilities"] = new[] { "messaging" } }).ConfigureAwait(false);
                    await using (socket.ConfigureAwait(false))
                    {
                        await Api.ExpectAsync(f, HttpMethod.Post, MessagingTestKit.Conversations + "/" + id + "/accept", b.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                        JsonElement data = await socket.ExpectDataAsync("conversation.updated", RealtimeTestKit.Wait, ct).ConfigureAwait(false);
                        JsonElement conversation = data.GetProperty("conversation");
                        Check.Equal(id, Api.Str(conversation, "id"), "The accepted conversation");
                        Check.Equal(JsonValueKind.Null, conversation.GetProperty("requestState").ValueKind, "The requester no longer awaits acceptance");
                        Check.True(conversation.GetProperty("viewer").GetProperty("canSend").GetBoolean(), "The requester may send freely");
                    }
                }),
                Case("Conversations.DeclinedSenderRefused", "After a decline, the sender's sends return 403 MessagingNotAllowed", async (f, ct) =>
                {
                    TestMember a = await MessagingTestKit.MemberAsync(f, ct).ConfigureAwait(false);
                    TestMember b = await MessagingTestKit.MemberAsync(f, ct).ConfigureAwait(false);
                    string id = await MessagingTestKit.StartIdAsync(f, a, b.Id, "hello", ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Post, MessagingTestKit.Conversations + "/" + id + "/decline", b.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    ApiResult refused = await MessagingTestKit.SendAsync(f, a, id, "again", ct).ConfigureAwait(false);
                    Check.Equal("MessagingNotAllowed", refused.Error, "MessagingNotAllowed (" + refused.Text + ")");
                }),
                Case("Conversations.ReplyAcceptsAndNotifies", "Replying accepts implicitly; requests create one aggregated MessageRequest notification without push", async (f, ct) =>
                {
                    TestMember a = await MessagingTestKit.MemberAsync(f, ct, "Ann First").ConfigureAwait(false);
                    TestMember c = await MessagingTestKit.MemberAsync(f, ct, "Cal Second").ConfigureAwait(false);
                    TestMember b = await MessagingTestKit.MemberAsync(f, ct, "Bo Target").ConfigureAwait(false);
                    string id = await MessagingTestKit.StartIdAsync(f, a, b.Id, "hi", ct).ConfigureAwait(false);
                    await MessagingTestKit.StartIdAsync(f, c, b.Id, "hey", ct).ConfigureAwait(false);
                    JsonElement notifications = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/notifications/enumerate", b.AccessToken, new { filters = new { type = "MessageRequest" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(1, notifications.GetProperty("objects").GetArrayLength(), "One aggregated row");
                    string title = Api.Str(notifications.GetProperty("objects")[0], "title");
                    Check.True(title.Contains("message request", StringComparison.Ordinal), "Title: " + title);
                    Check.Equal(0L, await MessagingTestKit.CountAsync(f, "notifications", "recipientuserid = '" + b.Id + "' AND type = 'DirectMessage'", ct).ConfigureAwait(false), "No DirectMessage rows");
                    await MessagingTestKit.SendOkAsync(f, b, id, "reply", ct).ConfigureAwait(false);
                    JsonElement view = await Api.ExpectAsync(f, HttpMethod.Get, MessagingTestKit.Conversations + "/" + id, a.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(JsonValueKind.Null, view.GetProperty("requestState").ValueKind, "Sender's request accepted by the reply");
                    JsonElement theirs = await Api.ExpectAsync(f, HttpMethod.Get, MessagingTestKit.Conversations + "/" + id, b.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("Inbox", Api.Str(theirs, "folder"), "Recipient's Inbox");
                    Check.Equal(1L, await MessagingTestKit.CountAsync(f, "messagingcontacts", "userid = '" + b.Id + "' AND contactuserid = '" + a.Id + "'", ct).ConfigureAwait(false), "Known contact recorded");
                }),
                Case("Conversations.Policies", "Known and Nobody policies refuse strangers with 403 MessagingNotAllowed; known contacts and staff pass and staff never create requests", async (f, ct) =>
                {
                    TestMember b = await MessagingTestKit.MemberAsync(f, ct, "Bri Private").ConfigureAwait(false);
                    TestMember known = await MessagingTestKit.MemberAsync(f, ct, "Kim Known").ConfigureAwait(false);
                    TestMember stranger = await MessagingTestKit.MemberAsync(f, ct, "Sam Stranger").ConfigureAwait(false);
                    TestMember staff = await MessagingTestKit.MemberAsync(f, ct, "Stu Staff").ConfigureAwait(false);
                    await MessagingTestKit.GrantAsync(f, staff.Id, RoleEnum.SupportAgent, ResourceTypeEnum.Global, ct).ConfigureAwait(false);
                    await MessagingTestKit.StartIdAsync(f, b, known.Id, "I know you", ct).ConfigureAwait(false);
                    JsonElement settings = await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/me/messaging/settings", b.AccessToken, new { dmPolicy = "Known" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("Known", Api.Str(settings, "dmPolicy"), "Known saved");
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, MessagingTestKit.Conversations, stranger.AccessToken, new { recipientUserId = b.Id, message = new { clientMessageId = "s1", body = "hi" } }, HttpStatusCode.Forbidden, "MessagingNotAllowed", ct).ConfigureAwait(false);
                    string withKnown = Api.Str((await MessagingTestKit.StartAsync(f, known, b.Id, null, ct, HttpStatusCode.OK).ConfigureAwait(false)).GetProperty("conversation"), "id");
                    await MessagingTestKit.SendOkAsync(f, known, withKnown, "hello again", ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/me/messaging/settings", b.AccessToken, new { dmPolicy = "Nobody" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    ApiResult blockedByPolicy = await MessagingTestKit.SendAsync(f, known, withKnown, "and again", ct).ConfigureAwait(false);
                    Check.Equal("MessagingNotAllowed", blockedByPolicy.Error, "Nobody refuses members (" + blockedByPolicy.Text + ")");
                    JsonElement staffStart = await MessagingTestKit.StartAsync(f, staff, b.Id, "From support", ct).ConfigureAwait(false);
                    Check.Equal(JsonValueKind.Null, staffStart.GetProperty("conversation").GetProperty("requestState").ValueKind, "Staff conversations are not requests");
                    await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/me/messaging/settings", b.AccessToken, new { dmPolicy = "Sometimes" }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/me/messaging/settings", b.AccessToken, new { dmPolicy = "Everyone" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    JsonElement request = await MessagingTestKit.StartAsync(f, stranger, b.Id, "Now allowed", ct).ConfigureAwait(false);
                    Check.Equal("AwaitingAcceptance", Api.Str(request.GetProperty("conversation"), "requestState"), "Everyone: strangers land in Requests");
                }),
                Case("Conversations.BlocksBothWays", "A block stops sending both ways, hides the conversation from the blocker only, is never revealed to the blocked member, and unblocking restores it", async (f, ct) =>
                {
                    TestMember a = await MessagingTestKit.MemberAsync(f, ct, "Ann Blocker").ConfigureAwait(false);
                    TestMember b = await MessagingTestKit.MemberAsync(f, ct, "Bob Blocked").ConfigureAwait(false);
                    string id = await MessagingTestKit.PairAsync(f, a, b, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/users/" + b.Id + "/block", a.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("MessagingNotAllowed", (await MessagingTestKit.SendAsync(f, b, id, "are you there", ct).ConfigureAwait(false)).Error, "Blocked member refused");
                    Check.Equal("MessagingNotAllowed", (await MessagingTestKit.SendAsync(f, a, id, "bye", ct).ConfigureAwait(false)).Error, "Blocker refused too");
                    Check.True(!(await MessagingTestKit.ListIdsAsync(f, a, null, ct).ConfigureAwait(false)).Contains(id), "Hidden from the blocker");
                    Check.True((await MessagingTestKit.ListIdsAsync(f, b, null, ct).ConfigureAwait(false)).Contains(id), "Still listed for the blocked member");
                    JsonElement blocker = await Api.ExpectAsync(f, HttpMethod.Get, MessagingTestKit.Conversations + "/" + id, a.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("Blocked", Api.Str(blocker.GetProperty("viewer"), "cannotSendReason"), "Blocker sees Blocked");
                    JsonElement blocked = await Api.ExpectAsync(f, HttpMethod.Get, MessagingTestKit.Conversations + "/" + id, b.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("Unavailable", Api.Str(blocked.GetProperty("viewer"), "cannotSendReason"), "Blocked member sees only Unavailable");
                    Check.Equal(JsonValueKind.Null, blocked.GetProperty("otherLastReadSeq").ValueKind, "No receipts across a block");
                    JsonElement messages = await MessagingTestKit.HistoryAsync(f, b, id, null, ct).ConfigureAwait(false);
                    string messageId = Api.Str(messages.GetProperty("objects")[0], "id");
                    await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/messages/" + messageId + "/reactions/like", b.AccessToken, null, HttpStatusCode.Forbidden, "MessagingNotAllowed", ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/users/" + b.Id + "/block", a.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True((await MessagingTestKit.ListIdsAsync(f, a, null, ct).ConfigureAwait(false)).Contains(id), "Unblock restores the conversation");
                    await MessagingTestKit.SendOkAsync(f, b, id, "back", ct).ConfigureAwait(false);
                }),
                Case("Conversations.FoldersMuteDeleteForMe", "Archive, mute with a time, unmute, and delete for me; a new message from the other member revives it with only newer messages", async (f, ct) =>
                {
                    TestMember a = await MessagingTestKit.MemberAsync(f, ct).ConfigureAwait(false);
                    TestMember b = await MessagingTestKit.MemberAsync(f, ct).ConfigureAwait(false);
                    string id = await MessagingTestKit.PairAsync(f, a, b, ct).ConfigureAwait(false);
                    string path = MessagingTestKit.Conversations + "/" + id;
                    JsonElement archived = await Api.ExpectAsync(f, HttpMethod.Put, path + "/folder", a.AccessToken, new { folder = "Archived" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("Archived", Api.Str(archived, "folder"), "Archived");
                    Check.True(!(await MessagingTestKit.ListIdsAsync(f, a, null, ct).ConfigureAwait(false)).Contains(id), "Not in Inbox");
                    Check.True((await MessagingTestKit.ListIdsAsync(f, a, new { filters = new { folder = "Archived" } }, ct).ConfigureAwait(false)).Contains(id), "In Archived");
                    await Api.ExpectErrorAsync(f, HttpMethod.Put, path + "/folder", a.AccessToken, new { folder = "Requests" }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Put, path + "/folder", a.AccessToken, new { folder = "Inbox" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    DateTime until = DateTime.UtcNow.AddHours(2);
                    JsonElement muted = await Api.ExpectAsync(f, HttpMethod.Put, path + "/mute", a.AccessToken, new { untilUtc = until }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(muted.GetProperty("isMuted").GetBoolean(), "Muted");
                    Check.Equal(JsonValueKind.String, muted.GetProperty("mutedUntilUtc").ValueKind, "Until set");
                    await Api.ExpectErrorAsync(f, HttpMethod.Put, path + "/mute", a.AccessToken, new { untilUtc = DateTime.UtcNow.AddHours(-1) }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    JsonElement unmuted = await Api.ExpectAsync(f, HttpMethod.Delete, path + "/mute", a.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(!unmuted.GetProperty("isMuted").GetBoolean(), "Unmuted");

                    ApiResult deleted = await Api.SendAsync(f, HttpMethod.Delete, path, a.AccessToken, null, ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.NoContent, deleted.Status, "204");
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, path, a.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
                    Check.True(!(await MessagingTestKit.ListIdsAsync(f, a, new { filters = new { folder = "All" } }, ct).ConfigureAwait(false)).Contains(id), "Gone for the caller");
                    Check.True((await MessagingTestKit.ListIdsAsync(f, b, null, ct).ConfigureAwait(false)).Contains(id), "Kept for the other member");
                    await MessagingTestKit.SendOkAsync(f, b, id, "new after delete", ct).ConfigureAwait(false);
                    Check.True((await MessagingTestKit.ListIdsAsync(f, a, null, ct).ConfigureAwait(false)).Contains(id), "Revived by a new message");
                    JsonElement history = await MessagingTestKit.HistoryAsync(f, a, id, null, ct).ConfigureAwait(false);
                    Check.Equal("3", String.Join(",", MessagingTestKit.Seqs(history)), "Only newer messages");
                    JsonElement view = await Api.ExpectAsync(f, HttpMethod.Get, path, a.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(1, view.GetProperty("unreadCount").GetInt32(), "One unread");
                    JsonElement read = await Api.ExpectAsync(f, HttpMethod.Post, path + "/read", a.AccessToken, new { seq = 3 }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(3L, read.GetProperty("myLastReadSeq").GetInt64(), "Read 3");
                    Check.Equal(0, read.GetProperty("unreadCount").GetInt32(), "No unread");
                    JsonElement lower = await Api.ExpectAsync(f, HttpMethod.Post, path + "/read", a.AccessToken, new { seq = 1 }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(3L, lower.GetProperty("myLastReadSeq").GetInt64(), "Monotonic");
                    Check.Equal(HttpStatusCode.NoContent, (await Api.SendAsync(f, HttpMethod.Post, path + "/delivered", a.AccessToken, new { seq = 3 }, ct).ConfigureAwait(false)).Status, "Delivered 204");
                    JsonElement other = await Api.ExpectAsync(f, HttpMethod.Get, path, b.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(3L, other.GetProperty("otherLastReadSeq").GetInt64(), "Receipts visible when both allow them");
                    await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/me/messaging/settings", b.AccessToken, new { readReceipts = false }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    other = await Api.ExpectAsync(f, HttpMethod.Get, path, b.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(JsonValueKind.Null, other.GetProperty("otherLastReadSeq").ValueKind, "Reciprocal: no receipts when off");
                }),
                Case("Conversations.MutedCanSendDirectMessagesUnavailable", "Muted members only reply to staff (403 MessagingRestricted), canSendDirectMessages false is 403 MessagingNotAllowed, and a recipient without access is 403 RecipientUnavailable", async (f, ct) =>
                {
                    TestMember a = await MessagingTestKit.MemberAsync(f, ct).ConfigureAwait(false);
                    TestMember b = await MessagingTestKit.MemberAsync(f, ct).ConfigureAwait(false);
                    TestMember staff = await MessagingTestKit.MemberAsync(f, ct, "Mod Erator").ConfigureAwait(false);
                    await MessagingTestKit.GrantAsync(f, staff.Id, RoleEnum.Moderator, ResourceTypeEnum.Messaging, ct).ConfigureAwait(false);
                    string withB = await MessagingTestKit.PairAsync(f, a, b, ct).ConfigureAwait(false);
                    string withStaff = await MessagingTestKit.StartIdAsync(f, staff, a.Id, "Please read the rules", ct).ConfigureAwait(false);
                    await SafetyHelpers.UpdateUserAsync(f, a.Id, u => { u.IsMuted = true; u.MutedUntilUtc = DateTime.UtcNow.AddDays(1); u.MutedReason = "test"; }, ct).ConfigureAwait(false);
                    Check.Equal("MessagingRestricted", (await MessagingTestKit.SendAsync(f, a, withB, "x", ct).ConfigureAwait(false)).Error, "Muted refused");
                    await MessagingTestKit.SendOkAsync(f, a, withStaff, "Sorry", ct).ConfigureAwait(false);
                    JsonElement view = await Api.ExpectAsync(f, HttpMethod.Get, MessagingTestKit.Conversations + "/" + withB, a.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("MessagingRestricted", Api.Str(view.GetProperty("viewer"), "cannotSendReason"), "Viewer MessagingRestricted");
                    await SafetyHelpers.UpdateUserAsync(f, a.Id, u => { u.IsMuted = false; u.MutedUntilUtc = null; u.CanSendDirectMessages = false; }, ct).ConfigureAwait(false);
                    Check.Equal("MessagingNotAllowed", (await MessagingTestKit.SendAsync(f, a, withB, "x", ct).ConfigureAwait(false)).Error, "canSendDirectMessages false");
                    await SafetyHelpers.UpdateUserAsync(f, a.Id, u => { u.CanSendDirectMessages = true; }, ct).ConfigureAwait(false);
                    await MessagingTestKit.LapseAsync(f, b.Id, ct).ConfigureAwait(false);
                    Check.Equal("RecipientUnavailable", (await MessagingTestKit.SendAsync(f, a, withB, "x", ct).ConfigureAwait(false)).Error, "Recipient without a membership");
                    view = await Api.ExpectAsync(f, HttpMethod.Get, MessagingTestKit.Conversations + "/" + withB, a.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("RecipientUnavailable", Api.Str(view.GetProperty("viewer"), "cannotSendReason"), "Viewer RecipientUnavailable");
                }),
                Case("Conversations.ListSearchAndMetadata", "The list orders by activity, searches the other member's name, filters unreadOnly and withUserId, and carries the caller's private labels", async (f, ct) =>
                {
                    TestMember a = await MessagingTestKit.MemberAsync(f, ct, "Ann Lister").ConfigureAwait(false);
                    TestMember b = await MessagingTestKit.MemberAsync(f, ct, "Zed Quartz").ConfigureAwait(false);
                    TestMember c = await MessagingTestKit.MemberAsync(f, ct, "Yan Mica").ConfigureAwait(false);
                    string withB = await MessagingTestKit.PairAsync(f, a, b, ct).ConfigureAwait(false);
                    string withC = await MessagingTestKit.PairAsync(f, a, c, ct).ConfigureAwait(false);
                    Check.Equal(withC + "," + withB, String.Join(",", await MessagingTestKit.ListIdsAsync(f, a, null, ct).ConfigureAwait(false)), "Most recent first");
                    Check.Equal(withB, String.Join(",", await MessagingTestKit.ListIdsAsync(f, a, new { search = "quartz" }, ct).ConfigureAwait(false)), "Search by name");
                    Check.Equal(withC, String.Join(",", await MessagingTestKit.ListIdsAsync(f, a, new { filters = new { withUserId = c.Id } }, ct).ConfigureAwait(false)), "withUserId");
                    await Api.ExpectAsync(f, HttpMethod.Post, MessagingTestKit.Conversations + "/" + withB + "/read", a.AccessToken, new { seq = 100 }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Post, MessagingTestKit.Conversations + "/" + withC + "/read", a.AccessToken, new { seq = 100 }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await MessagingTestKit.SendOkAsync(f, b, withB, "unread one", ct).ConfigureAwait(false);
                    Check.Equal(withB, String.Join(",", await MessagingTestKit.ListIdsAsync(f, a, new { filters = new { unreadOnly = true } }, ct).ConfigureAwait(false)), "unreadOnly");
                    JsonElement view = await Api.ExpectAsync(f, HttpMethod.Get, MessagingTestKit.Conversations + "/" + withB, a.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    string memberId = Api.Str(view, "memberId");
                    Check.True(memberId.StartsWith("cvm_", StringComparison.Ordinal), "memberId");
                    await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/metadata/ConversationMember/" + memberId, a.AccessToken, new { labels = new[] { "friends" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/metadata/ConversationMember/" + memberId, b.AccessToken, new { labels = new[] { "nope" } }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
                    Check.Equal(withB, String.Join(",", await MessagingTestKit.ListIdsAsync(f, a, new { labels = new[] { "friends" } }, ct).ConfigureAwait(false)), "Label filter");
                    JsonElement theirs = await Api.ExpectAsync(f, HttpMethod.Get, MessagingTestKit.Conversations + "/" + withB, b.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(0, theirs.GetProperty("labels").GetArrayLength(), "Each member's metadata is private");
                    JsonElement counts = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/me/messaging/unread-count", a.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(1L, counts.GetProperty("conversations").GetInt64(), "One unread conversation");
                    Check.Equal(1L, counts.GetProperty("messages").GetInt64(), "One unread message");
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, MessagingTestKit.Conversations + "/enumerate", a.AccessToken, new { filters = new { folder = "Spam" } }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                })
            });
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string caseId, string name, Func<HttpFixture, CancellationToken, Task> body)
        {
            return SuiteCases.Http(Id, caseId, name, body);
        }

        #endregion
    }
}

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
    using TrellisKit.Server.Services;

    /// <summary>
    /// Phase 5 direct message privacy and moderation (docs/PHASE5_CONTRACT.md sections 3.8, 3.9, 11 and as-built notes B
    /// and E): request history never captures message bodies, no staff route lists or opens conversations, reports write
    /// frozen snapshots that later edits do not change, snapshot reads are audited and limited to SystemAdmin and
    /// Messaging moderators, Remove and Restore act on the live message for both members, and RevokeMessaging.
    /// </summary>
    public static class MessagePrivacySuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "MessagePrivacy";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Direct message privacy and moderation (" + TestEnvironment.Provider + ")", new[]
            {
                Case("Privacy.RequestHistoryRedaction", "Request history stores [redacted] instead of request and response bodies of messaging routes", async (f, ct) =>
                {
                    TestMember a = await MessagingTestKit.MemberAsync(f, ct).ConfigureAwait(false);
                    TestMember b = await MessagingTestKit.MemberAsync(f, ct).ConfigureAwait(false);
                    string id = await MessagingTestKit.PairAsync(f, a, b, ct).ConfigureAwait(false);
                    await MessagingTestKit.SendOkAsync(f, a, id, "PRIVATE-MARKER-TEXT", ct).ConfigureAwait(false);
                    await MessagingTestKit.HistoryAsync(f, b, id, null, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/me/messaging/settings", a.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    long captured = 0;
                    for (int i = 0; i < 50 && captured < 3; i++)
                    {
                        captured = await MessagingTestKit.CountAsync(f, "requesthistory", "(path LIKE '/api/v1/conversations%' OR path LIKE '/api/v1/me/messaging%')", ct).ConfigureAwait(false);
                        if (captured < 3) await Task.Delay(100, ct).ConfigureAwait(false);
                    }
                    Check.True(captured >= 3, "Messaging requests captured (" + captured + ")");
                    Check.Equal(0L, await MessagingTestKit.CountAsync(f, "requesthistory", "(requestbody LIKE '%PRIVATE-MARKER%' OR responsebody LIKE '%PRIVATE-MARKER%')", ct).ConfigureAwait(false), "No message text captured");
                    Check.Equal(0L, await MessagingTestKit.CountAsync(f, "requesthistory", "(path LIKE '/api/v1/conversations%' OR path LIKE '/api/v1/me/messaging%') AND (requestbody <> '[redacted]' OR responsebody <> '[redacted]')", ct).ConfigureAwait(false), "Bodies redacted");
                    Check.True(RequestHistoryCaptureService.IsMessagingPath("/api/v1/messages/msg_1"), "Message routes");
                    Check.True(RequestHistoryCaptureService.IsMessagingPath("/api/v1/admin/moderation/messaging/reports/rpt_1/snapshot"), "Snapshot route");
                    Check.True(!RequestHistoryCaptureService.IsMessagingPath("/api/v1/feed"), "Other routes untouched");
                }),
                Case("Privacy.NoStaffConversationAccess", "Staff who are not members get 404 for conversations and history; the admin token is refused; the moderation queue never carries message text", async (f, ct) =>
                {
                    TestMember a = await MessagingTestKit.MemberAsync(f, ct).ConfigureAwait(false);
                    TestMember b = await MessagingTestKit.MemberAsync(f, ct).ConfigureAwait(false);
                    TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
                    string id = await MessagingTestKit.PairAsync(f, a, b, ct).ConfigureAwait(false);
                    JsonElement message = await MessagingTestKit.SendOkAsync(f, b, id, "QUEUE-SECRET text", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, MessagingTestKit.Conversations + "/" + id, admin.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, MessagingTestKit.Conversations + "/" + id + "/messages/enumerate", admin.AccessToken, new { }, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, MessagingTestKit.Conversations + "/" + id, HttpFixture.AdminToken, null, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
                    Check.Equal(0, (await MessagingTestKit.ListIdsAsync(f, admin, null, ct).ConfigureAwait(false)).Count, "Admins list only their own conversations");
                    await ModerationTestKit.ReportOkAsync(f, a.AccessToken, "Message", Api.Str(message, "id"), ct).ConfigureAwait(false);
                    JsonElement queue = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/moderation/messaging/reports/enumerate", admin.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(1, queue.GetProperty("objects").GetArrayLength(), "One report");
                    Check.True(!queue.ToString().Contains("QUEUE-SECRET", StringComparison.Ordinal), "No text in the queue");
                    Check.True(Api.Str(queue.GetProperty("objects")[0], "snapshotId").StartsWith("mrs_", StringComparison.Ordinal), "Snapshot id");
                }),
                Case("Privacy.ReportSnapshots", "Reports write frozen snapshots with context the reporter could see; later edits do not change them; reads are audited and limited to SystemAdmin and Messaging moderators", async (f, ct) =>
                {
                    f.Settings.Messaging.ReportContextMessages = 2;
                    TestMember a = await MessagingTestKit.MemberAsync(f, ct, "Ann Reporter").ConfigureAwait(false);
                    TestMember b = await MessagingTestKit.MemberAsync(f, ct, "Bad Actor").ConfigureAwait(false);
                    TestMember moderator = await MessagingTestKit.MemberAsync(f, ct).ConfigureAwait(false);
                    TestMember feedModerator = await MessagingTestKit.MemberAsync(f, ct).ConfigureAwait(false);
                    await MessagingTestKit.GrantAsync(f, moderator.Id, RoleEnum.Moderator, ResourceTypeEnum.Messaging, ct).ConfigureAwait(false);
                    await SafetyHelpers.GrantRoleAsync(f, feedModerator.Id, RoleEnum.FeedModerator, ct).ConfigureAwait(false);
                    string id = await MessagingTestKit.PairAsync(f, a, b, ct).ConfigureAwait(false);
                    JsonElement deleted = await MessagingTestKit.SendOkAsync(f, b, id, "deleted before the report", ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/messages/" + Api.Str(deleted, "id"), b.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    JsonElement bad = await MessagingTestKit.SendOkAsync(f, b, id, "an abusive message", ct).ConfigureAwait(false);
                    string badId = Api.Str(bad, "id");
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/reports", b.AccessToken, new { targetType = "Message", targetId = badId, reason = "Spam" }, HttpStatusCode.BadRequest, "CannotReportSelf", ct).ConfigureAwait(false);
                    TestMember outsider = await MessagingTestKit.MemberAsync(f, ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/reports", outsider.AccessToken, new { targetType = "Message", targetId = badId, reason = "Spam" }, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
                    JsonElement report = await ModerationTestKit.ReportOkAsync(f, a.AccessToken, "Message", badId, ct).ConfigureAwait(false);
                    string reportId = Api.Str(report, "id");
                    Check.Equal("Messaging", await MessagingTestKit.ScalarAsync(f, "scopetype", "reports", "id = '" + reportId + "'", ct).ConfigureAwait(false), "Scope Messaging");
                    // A Messaging moderator works the moderation queue: it lists the message report (and only Messaging-scope items).
                    JsonElement queue = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/moderation/queue/enumerate", moderator.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    List<JsonElement> queued = queue.GetProperty("objects").EnumerateArray().ToList();
                    Check.True(queued.Any(q => Api.Str(q, "targetId") == badId), "The messaging moderator sees the message report in the queue");
                    Check.True(queued.All(q => Api.Str(q, "targetType") == "Message" || Api.Str(q, "targetType") == "Conversation"), "Only messaging targets in a messaging moderator's queue");
                    await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/messages/" + badId, b.AccessToken, new { body = "an innocent message" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    string path = "/api/v1/admin/moderation/messaging/reports/" + reportId + "/snapshot";
                    JsonElement snapshot = await Api.ExpectAsync(f, HttpMethod.Get, path, moderator.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(badId, Api.Str(snapshot, "reportedMessageId"), "reportedMessageId");
                    List<JsonElement> items = snapshot.GetProperty("messages").EnumerateArray().ToList();
                    Check.Equal(3, items.Count, "The reported message and 2 earlier messages");
                    JsonElement reported = items.Single(i => i.GetProperty("isReported").GetBoolean());
                    Check.Equal("an abusive message", Api.Str(reported, "body"), "Later edits never change the snapshot");
                    JsonElement tombstone = items.Single(i => Api.Str(i, "messageId") == Api.Str(deleted, "id"));
                    Check.Equal("Deleted", Api.Str(tombstone, "state"), "Tombstone placeholder");
                    Check.Equal(String.Empty, Api.Str(tombstone, "body"), "No deleted text in the snapshot");
                    Check.True(Api.Str(items[0], "id").StartsWith("mri_", StringComparison.Ordinal), "Item ids");
                    await Api.ExpectAsync(f, HttpMethod.Get, path, HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(2L, await MessagingTestKit.CountAsync(f, "auditrecords", "action = 'message.snapshot.view'", ct).ConfigureAwait(false), "Every view audited");
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, path, feedModerator.AccessToken, null, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, path, a.AccessToken, null, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/admin/moderation/messaging/reports/rpt_unknown/snapshot", moderator.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
                    JsonElement conversationReport = await ModerationTestKit.ReportOkAsync(f, a.AccessToken, "Conversation", id, ct).ConfigureAwait(false);
                    JsonElement whole = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/moderation/messaging/reports/" + Api.Str(conversationReport, "id") + "/snapshot", moderator.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(badId, Api.Str(whole, "reportedMessageId"), "Conversation report: the latest message");
                    Check.Equal("an innocent message", Api.Str(whole.GetProperty("messages").EnumerateArray().Last(), "body"), "Captured at report time");
                }),
                Case("Privacy.RemoveRestoreRevoke", "Remove and Restore change the live message for both members (also through the moderation extension directly); RevokeMessaging stops sending", async (f, ct) =>
                {
                    TestMember a = await MessagingTestKit.MemberAsync(f, ct).ConfigureAwait(false);
                    TestMember b = await MessagingTestKit.MemberAsync(f, ct).ConfigureAwait(false);
                    TestMember moderator = await MessagingTestKit.MemberAsync(f, ct).ConfigureAwait(false);
                    await MessagingTestKit.GrantAsync(f, moderator.Id, RoleEnum.Moderator, ResourceTypeEnum.Messaging, ct).ConfigureAwait(false);
                    string id = await MessagingTestKit.PairAsync(f, a, b, ct).ConfigureAwait(false);
                    JsonElement bad = await MessagingTestKit.SendOkAsync(f, b, id, "remove me", ct).ConfigureAwait(false);
                    string badId = Api.Str(bad, "id");
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/moderation/messaging/messages/" + badId + "/remove", moderator.AccessToken, new { reason = "abuse" }, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
                    await ModerationTestKit.ReportOkAsync(f, a.AccessToken, "Message", badId, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/moderation/messaging/messages/" + badId + "/remove", moderator.AccessToken, new { reason = "abuse" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    foreach (TestMember viewer in new[] { a, b })
                    {
                        JsonElement page = await MessagingTestKit.HistoryAsync(f, viewer, id, new { maxResults = 1 }, ct).ConfigureAwait(false);
                        Check.Equal("Removed", Api.Str(page.GetProperty("objects")[0], "state"), "Removed for both members");
                        Check.Equal(String.Empty, Api.Str(page.GetProperty("objects")[0], "body"), "No text");
                    }
                    Check.Equal(0L, await MessagingTestKit.CountAsync(f, "reports", "targetid = '" + badId + "' AND status = 'Open'", ct).ConfigureAwait(false), "Reports resolved");
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/moderation/messaging/messages/" + badId + "/remove", moderator.AccessToken, null, HttpStatusCode.Conflict, "NotChanged", ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/moderation/messaging/messages/" + badId + "/restore", moderator.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("Visible", Api.Str((await MessagingTestKit.HistoryAsync(f, a, id, new { maxResults = 1 }, ct).ConfigureAwait(false)).GetProperty("objects")[0], "state"), "Restored");

                    ModerationTarget? target = await f.Services.Messaging.ModerationTarget.LoadAsync(TargetTypeEnum.Message, badId, ct).ConfigureAwait(false);
                    Check.True(target != null && target.ScopeType == "Messaging", "Loaded from the snapshot");
                    Check.True(f.Services.Messaging.ModerationTarget.Handles(TargetTypeEnum.Message) && f.Services.Messaging.ModerationTarget.Handles(TargetTypeEnum.Conversation), "Handles Message and Conversation");
                    await f.Services.Messaging.ModerationTarget.SetStateAsync(target!, ModerationStateEnum.Removed, moderator.Id, "direct", ct).ConfigureAwait(false);
                    Check.Equal("Removed", await MessagingTestKit.ScalarAsync(f, "state", "messages", "id = '" + badId + "'", ct).ConfigureAwait(false), "Extension Remove");
                    await f.Services.Messaging.ModerationTarget.SetStateAsync(target!, ModerationStateEnum.None, moderator.Id, "direct", ct).ConfigureAwait(false);
                    Check.Equal("Visible", await MessagingTestKit.ScalarAsync(f, "state", "messages", "id = '" + badId + "'", ct).ConfigureAwait(false), "Extension Restore");
                    Check.True(await f.Services.Messaging.ModerationTarget.LoadAsync(TargetTypeEnum.Message, "msg_unknown", ct).ConfigureAwait(false) == null, "No snapshot, no target");

                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/moderation/messaging/members/" + b.Id + "/revoke-messaging", a.AccessToken, null, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
                    JsonElement revoked = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/moderation/messaging/members/" + b.Id + "/revoke-messaging", moderator.AccessToken, new { reason = "spam" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(!revoked.GetProperty("canSendDirectMessages").GetBoolean(), "Revoked");
                    Check.Equal("MessagingNotAllowed", (await MessagingTestKit.SendAsync(f, b, id, "more", ct).ConfigureAwait(false)).Error, "Cannot send");
                    Check.Equal(2, MessagingTestKit.Seqs(await MessagingTestKit.HistoryAsync(f, b, id, null, ct).ConfigureAwait(false)).Count - 1, "Can still read");
                    Check.Equal(1L, await MessagingTestKit.CountAsync(f, "auditrecords", "action = 'messaging.revoke' AND targetid = '" + b.Id + "'", ct).ConfigureAwait(false), "Audited");
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/moderation/messaging/members/" + b.Id + "/restore-messaging", moderator.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await MessagingTestKit.SendOkAsync(f, b, id, "restored", ct).ConfigureAwait(false);
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

namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Touchstone.Core;
    using TrellisKit.Server.Services;

    /// <summary>
    /// Phase 5 messaging lifecycle (docs/PHASE5_CONTRACT.md sections 3.6, 4.9, 10, 12): the messages_unread email
    /// (preference default off, sender names and counts only, interval), MessageRetentionWorker, ConversationPurgeWorker,
    /// account deletion tombstones, and the account export sections.
    /// </summary>
    public static class MessagingLifecycleSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "MessagingLifecycle";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Messaging lifecycle (" + TestEnvironment.Provider + ")", new[]
            {
                Case("Lifecycle.UnreadEmail", "messages_unread goes only to members who turned the Messages email on, after messaging.emailAfterMinutes, at most once per interval, with sender names and counts only", async (f, ct) =>
                {
                    TestMember a = await MessagingTestKit.MemberAsync(f, ct, "Alex Writer").ConfigureAwait(false);
                    TestMember b = await MessagingTestKit.MemberAsync(f, ct, "Bea Reader").ConfigureAwait(false);
                    TestMember quiet = await MessagingTestKit.MemberAsync(f, ct, "Quinn Default").ConfigureAwait(false);
                    string id = await MessagingTestKit.PairAsync(f, a, b, ct).ConfigureAwait(false);
                    string other = await MessagingTestKit.PairAsync(f, a, quiet, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Post, MessagingTestKit.Conversations + "/" + id + "/read", b.AccessToken, new { seq = 100 }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    JsonElement prefs = await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/me/notification-preferences", b.AccessToken, new { categories = new[] { new { category = "Messages", email = true } } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    JsonElement messages = prefs.GetProperty("categories").EnumerateArray().Single(c => Api.Str(c, "category") == "Messages");
                    Check.True(messages.GetProperty("email").GetBoolean(), "Email on");
                    await MessagingTestKit.SendOkAsync(f, a, id, "SECRET-EMAIL-TEXT one", ct).ConfigureAwait(false);
                    await MessagingTestKit.SendOkAsync(f, a, id, "two", ct).ConfigureAwait(false);
                    await MessagingTestKit.SendOkAsync(f, a, other, "to the quiet member", ct).ConfigureAwait(false);
                    DateTime now = DateTime.UtcNow;
                    f.Services.Messaging.Clock = () => now.AddMinutes(10);
                    Check.Equal(0, await f.Services.Messaging.Maintenance.EmailPassAsync(ct).ConfigureAwait(false), "Too early");
                    f.Services.Messaging.Clock = () => now.AddMinutes(f.Settings.Messaging.EmailAfterMinutes + 1);
                    Check.Equal(1, await f.Services.Messaging.Maintenance.EmailPassAsync(ct).ConfigureAwait(false), "One email (the quiet member has the default off)");
                    string? subject = await MessagingTestKit.ScalarAsync(f, "subject", "emailmessages", "template = 'messages_unread' AND userid = '" + b.Id + "'", ct).ConfigureAwait(false);
                    Check.True(subject != null && subject.Contains('2'), "Subject counts the messages: " + subject);
                    Check.Equal(0L, await MessagingTestKit.CountAsync(f, "emailmessages", "template = 'messages_unread' AND userid = '" + quiet.Id + "'", ct).ConfigureAwait(false), "Default off");
                    f.Services.Messaging.Clock = () => now.AddMinutes(f.Settings.Messaging.EmailAfterMinutes + 90);
                    await MessagingTestKit.SendOkAsync(f, a, id, "three", ct).ConfigureAwait(false);
                    Check.Equal(0, await f.Services.Messaging.Maintenance.EmailPassAsync(ct).ConfigureAwait(false), "At most one per messaging.emailMinIntervalHours");
                    f.Services.Messaging.Clock = () => now.AddHours(f.Settings.Messaging.EmailMinIntervalHours + 2);
                    Check.Equal(1, await f.Services.Messaging.Maintenance.EmailPassAsync(ct).ConfigureAwait(false), "Next interval, newer unread message");
                    Check.Equal("Ann, Bob and Cy", TrellisKit.Server.Services.Messaging.MessagingMaintenance.SenderNames(new[] { "Ann", "Bob", "Cy" }, 3), "Three names");
                    Check.Equal("Ann, Bob, Cy and 2 others", TrellisKit.Server.Services.Messaging.MessagingMaintenance.SenderNames(new[] { "Ann", "Bob", "Cy" }, 5), "And N others");
                    Check.True(TrellisKit.Server.Services.Email.EmailTemplateCatalog.Find("messages_unread") != null, "Template in the catalog");
                }),
                Case("Lifecycle.RetentionAndPurge", "Retention turns old messages into tombstones; a conversation every member deleted is purged after 30 days with its tombstones", async (f, ct) =>
                {
                    TestMember a = await MessagingTestKit.MemberAsync(f, ct).ConfigureAwait(false);
                    TestMember b = await MessagingTestKit.MemberAsync(f, ct).ConfigureAwait(false);
                    string id = await MessagingTestKit.PairAsync(f, a, b, ct).ConfigureAwait(false);
                    DateTime now = DateTime.UtcNow;
                    Check.Equal(0, await f.Services.Messaging.Maintenance.RetentionPassAsync(ct).ConfigureAwait(false), "retentionDays 0 keeps everything");
                    f.Settings.Messaging.RetentionDays = 7;
                    f.Services.Messaging.Clock = () => now.AddDays(8);
                    Check.Equal(2, await f.Services.Messaging.Maintenance.RetentionPassAsync(ct).ConfigureAwait(false), "Two messages expired");
                    Check.Equal(0L, await MessagingTestKit.CountAsync(f, "messages", "conversationid = '" + id + "' AND body <> ''", ct).ConfigureAwait(false), "Bodies purged");
                    Check.Equal(2L, await MessagingTestKit.CountAsync(f, "messages", "conversationid = '" + id + "' AND state = 'Deleted'", ct).ConfigureAwait(false), "Tombstones kept");
                    f.Settings.Messaging.RetentionDays = 0;
                    f.Services.Messaging.Clock = () => now;
                    await Api.SendAsync(f, HttpMethod.Delete, MessagingTestKit.Conversations + "/" + id, a.AccessToken, null, ct).ConfigureAwait(false);
                    f.Services.Messaging.Clock = () => now.AddDays(40);
                    Check.Equal(0, await f.Services.Messaging.Maintenance.PurgePassAsync(ct).ConfigureAwait(false), "One member keeps it");
                    f.Services.Messaging.Clock = () => now;
                    await Api.SendAsync(f, HttpMethod.Delete, MessagingTestKit.Conversations + "/" + id, b.AccessToken, null, ct).ConfigureAwait(false);
                    f.Services.Messaging.Clock = () => now.AddDays(29);
                    Check.Equal(0, await f.Services.Messaging.Maintenance.PurgePassAsync(ct).ConfigureAwait(false), "Not yet");
                    f.Services.Messaging.Clock = () => now.AddDays(31);
                    Check.Equal(1, await f.Services.Messaging.Maintenance.PurgePassAsync(ct).ConfigureAwait(false), "Purged");
                    Check.Equal(0L, await MessagingTestKit.CountAsync(f, "messages", "conversationid = '" + id + "'", ct).ConfigureAwait(false), "Messages and tombstones purged");
                    Check.Equal(0L, await MessagingTestKit.CountAsync(f, "conversationmembers", "conversationid = '" + id + "'", ct).ConfigureAwait(false), "Members purged");
                    Check.Equal(0L, await MessagingTestKit.CountAsync(f, "conversations", "id = '" + id + "'", ct).ConfigureAwait(false), "Conversation purged");
                }),
                Case("Lifecycle.AccountDeletionAndExport", "Export carries the member's conversations and settings; account deletion turns their messages into Deleted member tombstones while the other member keeps their own", async (f, ct) =>
                {
                    TestMember a = await MessagingTestKit.MemberAsync(f, ct).ConfigureAwait(false);
                    TestMember b = await MessagingTestKit.MemberAsync(f, ct).ConfigureAwait(false);
                    string id = await MessagingTestKit.PairAsync(f, a, b, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/me/messaging/settings", a.AccessToken, new { dmPolicy = "Known" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    JsonElement hidden = await MessagingTestKit.SendOkAsync(f, b, id, "deleted later", ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/messages/" + Api.Str(hidden, "id"), b.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    JsonElement conversations = await ExportAsync(f, "conversations", a.Id, ct).ConfigureAwait(false);
                    List<JsonElement> rows = conversations.EnumerateArray().ToList();
                    Check.Equal(3, rows.Count, "Own and the other member's messages, tombstone included");
                    Check.True(rows.Any(r => r.GetProperty("isMine").GetBoolean()) && rows.Any(r => !r.GetProperty("isMine").GetBoolean()), "Both members' messages");
                    Check.True(!conversations.ToString().Contains("deleted later", StringComparison.Ordinal), "Deleted content is not exported");
                    JsonElement settings = await ExportAsync(f, "messagingSettings", a.Id, ct).ConfigureAwait(false);
                    Check.Equal("Known", Api.Str(settings[0], "dmPolicy"), "Settings exported");

                    Check.True(f.Services.Accounts.AccountClosed != null, "Lifecycle chain");
                    await f.Services.Accounts.AccountClosed!(a.Id, ct).ConfigureAwait(false);
                    Check.Equal(0L, await MessagingTestKit.CountAsync(f, "messages", "authoruserid = '" + a.Id + "'", ct).ConfigureAwait(false), "No messages keep the author");
                    Check.Equal(0L, await MessagingTestKit.CountAsync(f, "conversationmembers", "userid = '" + a.Id + "'", ct).ConfigureAwait(false), "Membership removed");
                    Check.Equal(0L, await MessagingTestKit.CountAsync(f, "messagingsettings", "userid = '" + a.Id + "'", ct).ConfigureAwait(false), "Settings removed");
                    Check.Equal(0L, await MessagingTestKit.CountAsync(f, "messagingcontacts", "userid = '" + a.Id + "' OR contactuserid = '" + a.Id + "'", ct).ConfigureAwait(false), "Contacts removed");
                    JsonElement history = await MessagingTestKit.HistoryAsync(f, b, id, new { ordering = "SeqAscending" }, ct).ConfigureAwait(false);
                    JsonElement first = history.GetProperty("objects")[0];
                    Check.Equal("Deleted", Api.Str(first, "state"), "Tombstone");
                    Check.Equal("Deleted member", Api.Str(first.GetProperty("author"), "displayName"), "Deleted member");
                    Check.Equal("hi back", Api.Str(history.GetProperty("objects")[1], "body"), "The other member keeps their own messages");
                    JsonElement view = await Api.ExpectAsync(f, HttpMethod.Get, MessagingTestKit.Conversations + "/" + id, b.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("RecipientUnavailable", Api.Str(view.GetProperty("viewer"), "cannotSendReason"), "Cannot message a deleted member");
                })
            });
        }

        #endregion

        #region Private-Methods

        private static async Task<JsonElement> ExportAsync(HttpFixture f, string name, string userId, CancellationToken ct)
        {
            IAccountExportSection section = f.Services.AccountExports.Sections.Single(s => s.Name == name);
            using (MemoryStream stream = new MemoryStream())
            {
                using (Utf8JsonWriter writer = new Utf8JsonWriter(stream))
                {
                    await section.WriteAsync(writer, userId, ct).ConfigureAwait(false);
                    await writer.FlushAsync(ct).ConfigureAwait(false);
                }
                using (JsonDocument doc = JsonDocument.Parse(stream.ToArray())) return doc.RootElement.Clone();
            }
        }

        private static TestCaseDescriptor Case(string caseId, string name, Func<HttpFixture, CancellationToken, Task> body)
        {
            return SuiteCases.Http(Id, caseId, name, body);
        }

        #endregion
    }
}

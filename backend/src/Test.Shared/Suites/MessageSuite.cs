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

    /// <summary>
    /// Phase 5 direct messages (docs/PHASE5_CONTRACT.md sections 3.3, 3.5 and as-built note B): contiguous seq under
    /// parallel sends, idempotent retries (also concurrent), one edit inside the window with the original kept, delete
    /// tombstones with the content kept internally, reactions, replies following the quoted state, the word filter,
    /// rate limits, validation, and media limits with request media hidden from the recipient.
    /// </summary>
    public static class MessageSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "Messages";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Direct messages (" + TestEnvironment.Provider + ")", new[]
            {
                Case("Messages.SeqContiguousUnderParallelSends", "Parallel sends by both members get unique, contiguous seq values 1..N in commit order", async (f, ct) =>
                {
                    f.Settings.Messaging.MessagesPerMinute = 1000;
                    TestMember a = await MessagingTestKit.MemberAsync(f, ct).ConfigureAwait(false);
                    TestMember b = await MessagingTestKit.MemberAsync(f, ct).ConfigureAwait(false);
                    string id = await MessagingTestKit.PairAsync(f, a, b, ct).ConfigureAwait(false);
                    List<Task<ApiResult>> sends = Enumerable.Range(0, 24).Select(i => MessagingTestKit.SendAsync(f, i % 2 == 0 ? a : b, id, "parallel " + i, ct)).ToList();
                    ApiResult[] results = await Task.WhenAll(sends).ConfigureAwait(false);
                    Check.True(results.All(r => r.Status == HttpStatusCode.Created), "All 201: " + String.Join(",", results.Select(r => (int)r.Status)));
                    List<long> seqs = results.Select(r => r.Body.GetProperty("seq").GetInt64()).OrderBy(s => s).ToList();
                    Check.Equal(String.Join(",", Enumerable.Range(3, 24)), String.Join(",", seqs), "Contiguous 3..26");
                    JsonElement conversation = await Api.ExpectAsync(f, HttpMethod.Get, MessagingTestKit.Conversations + "/" + id, a.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(26L, conversation.GetProperty("lastSeq").GetInt64(), "lastSeq");
                    Check.Equal(26L, await MessagingTestKit.CountAsync(f, "messages", "conversationid = '" + id + "'", ct).ConfigureAwait(false), "26 rows");
                }),
                Case("Messages.IdempotentRetries", "A repeated clientMessageId returns 200 with the stored message and stores nothing else, also under concurrent retries", async (f, ct) =>
                {
                    TestMember a = await MessagingTestKit.MemberAsync(f, ct).ConfigureAwait(false);
                    TestMember b = await MessagingTestKit.MemberAsync(f, ct).ConfigureAwait(false);
                    string id = await MessagingTestKit.PairAsync(f, a, b, ct).ConfigureAwait(false);
                    string client = Guid.NewGuid().ToString();
                    ApiResult first = await MessagingTestKit.SendAsync(f, a, id, "once", ct, client).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.Created, first.Status, "201");
                    Check.Equal(client, Api.Str(first.Body, "clientMessageId"), "clientMessageId returned to the author");
                    ApiResult retry = await MessagingTestKit.SendAsync(f, a, id, "once", ct, client).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.OK, retry.Status, "200 on retry");
                    Check.Equal(Api.Str(first.Body, "id"), Api.Str(retry.Body, "id"), "Same message");
                    string concurrent = Guid.NewGuid().ToString();
                    ApiResult[] racing = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => MessagingTestKit.SendAsync(f, a, id, "race", ct, concurrent))).ConfigureAwait(false);
                    Check.Equal(1, racing.Count(r => r.Status == HttpStatusCode.Created), "Exactly one 201: " + String.Join(",", racing.Select(r => (int)r.Status)));
                    Check.True(racing.All(r => r.Status == HttpStatusCode.Created || r.Status == HttpStatusCode.OK), "Others 200");
                    Check.Equal(1, racing.Select(r => Api.Str(r.Body, "id")).Distinct().Count(), "One message id");
                    Check.Equal(1L, await MessagingTestKit.CountAsync(f, "messages", "clientmessageid = '" + concurrent + "'", ct).ConfigureAwait(false), "One row");
                    JsonElement conversation = await Api.ExpectAsync(f, HttpMethod.Get, MessagingTestKit.Conversations + "/" + id, a.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(4L, conversation.GetProperty("lastSeq").GetInt64(), "No seq consumed by retries");
                    ApiResult other = await MessagingTestKit.SendAsync(f, b, id, "same key, other author", ct, client).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.Created, other.Status, "The key is per author");
                    JsonElement seen = await MessagingTestKit.HistoryAsync(f, b, id, null, ct).ConfigureAwait(false);
                    Check.True(seen.GetProperty("objects").EnumerateArray().Where(m => Api.Str(m.GetProperty("author"), "id") == a.Id).All(m => m.GetProperty("clientMessageId").ValueKind == JsonValueKind.Null), "Other members never see the author's client ids");
                }),
                Case("Messages.EditOnceWithinWindow", "The author edits once inside messaging.editWindowMinutes; originalBody keeps the previous text; a second edit, a late edit, and another member's edit are refused", async (f, ct) =>
                {
                    TestMember a = await MessagingTestKit.MemberAsync(f, ct).ConfigureAwait(false);
                    TestMember b = await MessagingTestKit.MemberAsync(f, ct).ConfigureAwait(false);
                    string id = await MessagingTestKit.PairAsync(f, a, b, ct).ConfigureAwait(false);
                    JsonElement sent = await MessagingTestKit.SendOkAsync(f, a, id, "See you Thursday", ct).ConfigureAwait(false);
                    string path = "/api/v1/messages/" + Api.Str(sent, "id");
                    Check.True(sent.GetProperty("viewer").GetProperty("canEdit").GetBoolean(), "canEdit");
                    await Api.ExpectErrorAsync(f, HttpMethod.Put, path, b.AccessToken, new { body = "hacked" }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
                    JsonElement edited = await Api.ExpectAsync(f, HttpMethod.Put, path, a.AccessToken, new { body = "See you Friday" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("See you Friday", Api.Str(edited, "body"), "New text");
                    Check.Equal("See you Thursday", Api.Str(edited, "originalBody"), "Original kept");
                    Check.True(edited.GetProperty("isEdited").GetBoolean(), "isEdited");
                    Check.True(!edited.GetProperty("viewer").GetProperty("canEdit").GetBoolean(), "canEdit false after the edit");
                    await Api.ExpectErrorAsync(f, HttpMethod.Put, path, a.AccessToken, new { body = "Saturday" }, HttpStatusCode.Conflict, "EditWindowClosed", ct).ConfigureAwait(false);
                    JsonElement seen = await MessagingTestKit.HistoryAsync(f, b, id, new { maxResults = 1 }, ct).ConfigureAwait(false);
                    Check.Equal("See you Thursday", Api.Str(seen.GetProperty("objects")[0], "originalBody"), "Both members see the edit history");
                    JsonElement late = await MessagingTestKit.SendOkAsync(f, a, id, "late", ct).ConfigureAwait(false);
                    f.Services.Messaging.Clock = () => DateTime.UtcNow.AddMinutes(16);
                    await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/messages/" + Api.Str(late, "id"), a.AccessToken, new { body = "too late" }, HttpStatusCode.Conflict, "EditWindowClosed", ct).ConfigureAwait(false);
                    f.Services.Messaging.Clock = () => DateTime.UtcNow;
                    f.Settings.Messaging.EditWindowMinutes = 0;
                    JsonElement none = await MessagingTestKit.SendOkAsync(f, a, id, "no edits", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/messages/" + Api.Str(none, "id"), a.AccessToken, new { body = "x" }, HttpStatusCode.Conflict, "EditWindowClosed", ct).ConfigureAwait(false);
                }),
                Case("Messages.DeleteTombstoneKeepsContent", "Delete for everyone shows a tombstone to both members, keeps seq contiguous, keeps the content internally, and is idempotent", async (f, ct) =>
                {
                    TestMember a = await MessagingTestKit.MemberAsync(f, ct).ConfigureAwait(false);
                    TestMember b = await MessagingTestKit.MemberAsync(f, ct).ConfigureAwait(false);
                    string id = await MessagingTestKit.PairAsync(f, a, b, ct).ConfigureAwait(false);
                    JsonElement sent = await MessagingTestKit.SendOkAsync(f, a, id, "secret plan DELETE-ME", ct).ConfigureAwait(false);
                    string messageId = Api.Str(sent, "id");
                    await Api.ExpectErrorAsync(f, HttpMethod.Delete, "/api/v1/messages/" + messageId, b.AccessToken, null, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
                    JsonElement tombstone = await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/messages/" + messageId, a.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("Deleted", Api.Str(tombstone, "state"), "Deleted");
                    Check.Equal(String.Empty, Api.Str(tombstone, "body"), "Body empty");
                    Check.Equal(0, tombstone.GetProperty("attachments").GetArrayLength(), "No attachments");
                    await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/messages/" + messageId, a.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    JsonElement history = await MessagingTestKit.HistoryAsync(f, b, id, new { ordering = "SeqAscending" }, ct).ConfigureAwait(false);
                    Check.Equal("1,2,3", String.Join(",", MessagingTestKit.Seqs(history)), "Tombstone kept for gap detection");
                    Check.True(!history.ToString().Contains("DELETE-ME", StringComparison.Ordinal), "No member route returns deleted text");
                    Check.Equal("secret plan DELETE-ME", await MessagingTestKit.ScalarAsync(f, "body", "messages", "id = '" + messageId + "'", ct).ConfigureAwait(false), "Content kept internally");
                    await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/messages/" + messageId, a.AccessToken, new { body = "x" }, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
                    JsonElement conversation = await Api.ExpectAsync(f, HttpMethod.Get, MessagingTestKit.Conversations + "/" + id, b.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(String.Empty, Api.Str(conversation.GetProperty("lastMessage"), "excerpt"), "Excerpt empty for the deleted last message");
                }),
                Case("Messages.LinkPreviews", "The first link gets a server-fetched preview card unless showLinkPreview is false; an edit re-resolves it and a tombstone has none", async (f, ct) =>
                {
                    TestMember a = await MessagingTestKit.MemberAsync(f, ct).ConfigureAwait(false);
                    TestMember b = await MessagingTestKit.MemberAsync(f, ct).ConfigureAwait(false);
                    string id = await MessagingTestKit.PairAsync(f, a, b, ct).ConfigureAwait(false);
                    f.Fakes.LinkPreviews.Page("https://example.test/schedule", "<html><head><meta property=\"og:title\" content=\"Schedule\"><meta property=\"og:description\" content=\"This week.\"></head></html>");
                    f.Fakes.LinkPreviews.Page("https://example.test/venue", "<html><head><meta property=\"og:title\" content=\"Venue\"></head></html>");
                    JsonElement shown = await MessagingTestKit.SendOkAsync(f, a, id, "See https://example.test/schedule", ct).ConfigureAwait(false);
                    Check.True(shown.GetProperty("linkPreview").ValueKind == JsonValueKind.Object, "Preview card (" + shown.GetProperty("linkPreview") + ")");
                    Check.Equal("Schedule", Api.Str(shown.GetProperty("linkPreview"), "title"), "Preview title");
                    JsonElement seen = await MessagingTestKit.HistoryAsync(f, b, id, new { maxResults = 1 }, ct).ConfigureAwait(false);
                    Check.Equal("Schedule", Api.Str(seen.GetProperty("objects")[0].GetProperty("linkPreview"), "title"), "The other member sees the same card");
                    ApiResult hiddenResult = await MessagingTestKit.SendAsync(f, a, id, null, ct, null, new { clientMessageId = Guid.NewGuid().ToString(), body = "Again https://example.test/schedule", showLinkPreview = false }).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.Created, hiddenResult.Status, "send without preview (" + hiddenResult.Text + ")");
                    Check.Equal(JsonValueKind.Null, hiddenResult.Body.GetProperty("linkPreview").ValueKind, "showLinkPreview false");
                    JsonElement edited = await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/messages/" + Api.Str(shown, "id"), a.AccessToken, new { body = "Moved to https://example.test/venue" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("Venue", Api.Str(edited.GetProperty("linkPreview"), "title"), "An edit re-resolves the preview");
                    JsonElement plain = await MessagingTestKit.SendOkAsync(f, a, id, "No links here", ct).ConfigureAwait(false);
                    Check.Equal(JsonValueKind.Null, plain.GetProperty("linkPreview").ValueKind, "No link, no card");
                    JsonElement tombstone = await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/messages/" + Api.Str(shown, "id"), a.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(JsonValueKind.Null, tombstone.GetProperty("linkPreview").ValueKind, "Tombstones have no card");
                    f.Settings.LinkPreviews.Enabled = false;
                    JsonElement off = await MessagingTestKit.SendOkAsync(f, a, id, "Off https://example.test/schedule", ct).ConfigureAwait(false);
                    Check.Equal(JsonValueKind.Null, off.GetProperty("linkPreview").ValueKind, "Disabled link previews");
                }),
                Case("Messages.ReactionsAndReplies", "Reactions use the property set without notifications; replies quote an excerpt that follows the quoted message's state", async (f, ct) =>
                {
                    TestMember a = await MessagingTestKit.MemberAsync(f, ct, "Ann Quoter").ConfigureAwait(false);
                    TestMember b = await MessagingTestKit.MemberAsync(f, ct).ConfigureAwait(false);
                    TestMember outsider = await MessagingTestKit.MemberAsync(f, ct).ConfigureAwait(false);
                    string id = await MessagingTestKit.PairAsync(f, a, b, ct).ConfigureAwait(false);
                    JsonElement quoted = await MessagingTestKit.SendOkAsync(f, a, id, "Are you coming to the meetup?", ct).ConfigureAwait(false);
                    string quotedId = Api.Str(quoted, "id");
                    JsonElement state = await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/messages/" + quotedId + "/reactions/like", b.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(1L, state.GetProperty("reactionTotal").GetInt64(), "One reaction");
                    await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/messages/" + quotedId + "/reactions/notakey", b.AccessToken, null, HttpStatusCode.BadRequest, "UnknownReaction", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/messages/" + quotedId + "/reactions/like", outsider.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
                    JsonElement who = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/messages/" + quotedId + "/reactions/enumerate", a.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(1, who.GetProperty("objects").GetArrayLength(), "Who reacted");
                    Check.Equal(0L, await MessagingTestKit.CountAsync(f, "notifications", "recipientuserid = '" + a.Id + "' AND type = 'Reaction'", ct).ConfigureAwait(false), "No reaction notifications for direct messages");
                    JsonElement seen = await MessagingTestKit.HistoryAsync(f, a, id, new { maxResults = 1 }, ct).ConfigureAwait(false);
                    Check.Equal(1L, seen.GetProperty("objects")[0].GetProperty("reactionCounts").GetProperty("like").GetInt64(), "Counts in the payload");
                    Check.Equal("1", await MessagingTestKit.ScalarAsync(f, "reactiontotal::text", "messages", "id = '" + quotedId + "'", ct).ConfigureAwait(false), "Stored total");
                    await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/messages/" + quotedId + "/reactions/like", b.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);

                    ApiResult reply = await MessagingTestKit.SendAsync(f, b, id, null, ct, null, new { clientMessageId = Guid.NewGuid().ToString(), body = "Yes!", replyToMessageId = quotedId }).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.Created, reply.Status, "Reply sent");
                    JsonElement replyTo = reply.Body.GetProperty("replyTo");
                    Check.Equal(quotedId, Api.Str(replyTo, "id"), "replyTo id");
                    Check.Equal("Ann Quoter", Api.Str(replyTo, "authorDisplayName"), "Quoted author");
                    Check.True(Api.Str(replyTo, "excerpt").StartsWith("Are you coming", StringComparison.Ordinal), "Excerpt");
                    await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/messages/" + quotedId, a.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    JsonElement after = await MessagingTestKit.HistoryAsync(f, b, id, new { maxResults = 1 }, ct).ConfigureAwait(false);
                    JsonElement followed = after.GetProperty("objects")[0].GetProperty("replyTo");
                    Check.True(followed.GetProperty("isDeleted").GetBoolean(), "Quote follows the deletion");
                    Check.Equal(String.Empty, Api.Str(followed, "excerpt"), "Excerpt cleared");
                    string otherConversation = await MessagingTestKit.PairAsync(f, a, outsider, ct).ConfigureAwait(false);
                    ApiResult wrong = await MessagingTestKit.SendAsync(f, a, otherConversation, null, ct, null, new { clientMessageId = Guid.NewGuid().ToString(), body = "x", replyToMessageId = Api.Str(reply.Body, "id") }).ConfigureAwait(false);
                    Check.Equal("ValidationFailed", wrong.Error, "Reply target must be in the same conversation");
                }),
                Case("Messages.WordFilterRateLimitsValidation", "Block terms reject with 400 ContentFiltered, Flag terms never create reports, the send rate limit returns 429 with Retry-After, and invalid bodies are 400", async (f, ct) =>
                {
                    TestMember a = await MessagingTestKit.MemberAsync(f, ct).ConfigureAwait(false);
                    TestMember b = await MessagingTestKit.MemberAsync(f, ct).ConfigureAwait(false);
                    string id = await MessagingTestKit.PairAsync(f, a, b, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/moderation/word-filter", HttpFixture.AdminToken, new { term = "forbiddenword", matchType = "Word", action = "Block" }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/moderation/word-filter", HttpFixture.AdminToken, new { term = "flaggedword", matchType = "Word", action = "Flag" }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                    Check.Equal("ContentFiltered", (await MessagingTestKit.SendAsync(f, a, id, "this has forbiddenword in it", ct).ConfigureAwait(false)).Error, "Block term");
                    Check.Equal(HttpStatusCode.Created, (await MessagingTestKit.SendAsync(f, a, id, "this has flaggedword in it", ct).ConfigureAwait(false)).Status, "Flag term passes");
                    Check.Equal(0L, await MessagingTestKit.CountAsync(f, "reports", "targettype = 'Message'", ct).ConfigureAwait(false), "No system reports for private messages");
                    Check.Equal("MessageEmpty", (await MessagingTestKit.SendAsync(f, a, id, "", ct).ConfigureAwait(false)).Error, "MessageEmpty");
                    Check.Equal("ValidationFailed", (await MessagingTestKit.SendAsync(f, a, id, new string('x', f.Settings.Messaging.MaxBodyLength + 1), ct).ConfigureAwait(false)).Error, "Too long");
                    Check.Equal("ValidationFailed", (await MessagingTestKit.SendAsync(f, a, id, null, ct, null, new { body = "no client id" }).ConfigureAwait(false)).Error, "clientMessageId required");
                    f.Settings.Messaging.MessagesPerMinute = 2;
                    TestMember c = await MessagingTestKit.MemberAsync(f, ct).ConfigureAwait(false);
                    string withC = await MessagingTestKit.StartIdAsync(f, c, a.Id, "one", ct).ConfigureAwait(false);
                    await MessagingTestKit.SendOkAsync(f, c, withC, "two", ct).ConfigureAwait(false);
                    ApiResult limited = await MessagingTestKit.SendAsync(f, c, withC, "three", ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.TooManyRequests, limited.Status, "429");
                    Check.True(Int32.TryParse(limited.RetryAfter, out int seconds) && seconds > 0, "Retry-After (" + limited.RetryAfter + ")");
                }),
                Case("Messages.MediaLimitsAndRequestPlaceholders", "Attachments respect messaging.allowMedia and messaging.maxAttachments; the recipient of a pending request sees placeholders, not media", async (f, ct) =>
                {
                    TestMember a = await MessagingTestKit.MemberAsync(f, ct).ConfigureAwait(false);
                    TestMember b = await MessagingTestKit.MemberAsync(f, ct).ConfigureAwait(false);
                    string id = await MessagingTestKit.StartIdAsync(f, a, b.Id, "intro", ct).ConfigureAwait(false);
                    string media = await SupportTestKit.UploadAsync(f, a.AccessToken, ct, id, "/api/v1/media/uploads", "Message").ConfigureAwait(false);
                    ApiResult sent = await MessagingTestKit.SendAsync(f, a, id, null, ct, null, new { clientMessageId = Guid.NewGuid().ToString(), body = "", mediaIds = new[] { media } }).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.Created, sent.Status, "Attachment-only message (" + sent.Text + ")");
                    JsonElement attachment = sent.Body.GetProperty("attachments")[0];
                    Check.Equal(media, Api.Str(attachment, "mediaId"), "Attachment id");
                    Check.True(!attachment.GetProperty("isHidden").GetBoolean(), "Sender sees the media");
                    Check.Equal(JsonValueKind.Object, attachment.GetProperty("media").ValueKind, "Media payload");
                    JsonElement theirs = await MessagingTestKit.HistoryAsync(f, b, id, new { maxResults = 1 }, ct).ConfigureAwait(false);
                    JsonElement hidden = theirs.GetProperty("objects")[0].GetProperty("attachments")[0];
                    Check.True(hidden.GetProperty("isHidden").GetBoolean(), "Hidden for the request recipient");
                    Check.Equal("Attachment hidden until you accept", Api.Str(hidden, "placeholder"), "Placeholder");
                    Check.Equal(JsonValueKind.Null, hidden.GetProperty("media").ValueKind, "No media payload");
                    await Api.ExpectAsync(f, HttpMethod.Post, MessagingTestKit.Conversations + "/" + id + "/accept", b.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    theirs = await MessagingTestKit.HistoryAsync(f, b, id, new { maxResults = 1 }, ct).ConfigureAwait(false);
                    Check.True(!theirs.GetProperty("objects")[0].GetProperty("attachments")[0].GetProperty("isHidden").GetBoolean(), "Visible after accepting");
                    f.Settings.Messaging.MaxAttachments = 1;
                    string m1 = await SupportTestKit.UploadAsync(f, a.AccessToken, ct, id, "/api/v1/media/uploads", "Message").ConfigureAwait(false);
                    string m2 = await SupportTestKit.UploadAsync(f, a.AccessToken, ct, id, "/api/v1/media/uploads", "Message").ConfigureAwait(false);
                    Check.Equal("MediaLimitExceeded", (await MessagingTestKit.SendAsync(f, a, id, null, ct, null, new { clientMessageId = Guid.NewGuid().ToString(), body = "two", mediaIds = new[] { m1, m2 } }).ConfigureAwait(false)).Error, "Over the limit");
                    Check.Equal("MediaNotAttachable", (await MessagingTestKit.SendAsync(f, b, id, null, ct, null, new { clientMessageId = Guid.NewGuid().ToString(), body = "not mine", mediaIds = new[] { m1 } }).ConfigureAwait(false)).Error, "Not the sender's media");
                    f.Settings.Messaging.AllowMedia = false;
                    Check.Equal("MediaNotAllowed", (await MessagingTestKit.SendAsync(f, a, id, null, ct, null, new { clientMessageId = Guid.NewGuid().ToString(), body = "off", mediaIds = new[] { m1 } }).ConfigureAwait(false)).Error, "Media off");
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

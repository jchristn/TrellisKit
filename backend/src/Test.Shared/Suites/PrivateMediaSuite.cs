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
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Server.Services;

    /// <summary>
    /// Private media (docs/PHASE5_CONTRACT.md sections 3.7 and 6.1): ticket attachments (purpose Ticket) are never readable
    /// through the member-wide media grant, are moved out of every served location after processing, and are read only
    /// through per-object tokens that name the media, scope, viewer, and expiry; every read re-checks the viewer's access.
    /// </summary>
    public static class PrivateMediaSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "PrivateMedia";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Private media (" + TestEnvironment.Provider + ")", new[]
            {
                Case("Private.OwnerAndStaff", "A ticket attachment's per-object URLs work for the requester and support staff, serve Range, and the outputs live under the private source prefix, not the served folders", OwnerAndStaffAsync),
                Case("Private.GrantCookie", "The member-wide media grant cookie and a member bearer token cannot read a private object through the file route; other members cannot see or list it", GrantCookieAsync),
                Case("Private.Denials", "A token for another viewer, an expired token, a forged token, a viewer removed from the scope, and a deleted ticket get 403 or 404", DenialsAsync),
                Case("Private.AttachRules", "Private media attaches only within its purpose and scope; content media never attaches to tickets and ticket media never attaches to content", AttachRulesAsync),
                SuiteCases.Http(Id, "Private.Isolation", "A private media URL issued in one property is refused by another property", MultiPropertyKit.Multi, IsolationAsync)
            });
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string caseId, string name, Func<HttpFixture, CancellationToken, Task> body)
        {
            return SuiteCases.Http(Id, caseId, name, (f, ct) =>
            {
                SupportTestKit.Relax(f);
                return body(f, ct);
            });
        }

        private static async Task<string> TicketWithAttachmentAsync(HttpFixture f, TestMember member, CancellationToken ct)
        {
            string media = await SupportTestKit.UploadAsync(f, member.AccessToken, ct).ConfigureAwait(false);
            JsonElement ticket = await SupportTestKit.TicketAsync(f, member.AccessToken, ct, "Screenshot of the error", "The error is in the attached image.", "Bug", new[] { media }).ConfigureAwait(false);
            return Api.Str(ticket, "id");
        }

        private static async Task<string> MemberUrlAsync(HttpFixture f, string bearer, string ticketId, CancellationToken ct, bool staff = false)
        {
            JsonElement ticket = await Api.ExpectAsync(f, HttpMethod.Get, (staff ? "/api/v1/admin/tickets/" : "/api/v1/tickets/") + ticketId, bearer, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            return SupportTestKit.FirstAttachmentUrl(ticket.GetProperty("messages").GetProperty("objects")[0]);
        }

        private static async Task OwnerAndStaffAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            TestMember agent = await SupportTestKit.AgentAsync(f, ct).ConfigureAwait(false);
            string pending = await SupportTestKit.UploadAsync(f, member.AccessToken, ct).ConfigureAwait(false);
            JsonElement ownView = await MediaTestKit.GetMediaAsync(f, member.AccessToken, pending, ct).ConfigureAwait(false);
            string pendingUrl = ownView.GetProperty("image").GetProperty("variants").GetProperty("thumb").GetProperty("jpegUrl").GetString()!;
            Check.True(pendingUrl.StartsWith(MediaService.PrivateFilesPath + "/", StringComparison.Ordinal), "Private media payloads carry per-object URLs (" + pendingUrl + ")");
            MediaHttpResult pendingRead = await MediaTestKit.SendAsync(f, HttpMethod.Get, pendingUrl, null, null, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.OK, pendingRead.Status, "The owner reads a pending upload (" + pendingRead.Text + ")");

            JsonElement ticket = await SupportTestKit.TicketAsync(f, member.AccessToken, ct, "Screenshot of the error", "The error is in the attached image.", "Bug", new[] { pending }).ConfigureAwait(false);
            string ticketId = Api.Str(ticket, "id");
            JsonElement first = ticket.GetProperty("messages").GetProperty("objects")[0];
            Check.Equal(1, first.GetProperty("attachments").GetArrayLength(), "The opening message carries the attachment");
            MediaAttachment stored = (await f.Database.Media.ReadAsync(pending, ct).ConfigureAwait(false))!;
            Check.Equal(MediaPurposeEnum.Ticket, stored.Purpose, "Purpose stored");
            Check.Equal(ticketId, stored.ScopeId, "Scope bound on create");
            Check.Equal(TargetTypeEnum.TicketMessage, stored.AttachedTargetType, "Attached to the message");

            string url = SupportTestKit.FirstAttachmentUrl(first);
            MediaHttpResult owner = await MediaTestKit.SendAsync(f, HttpMethod.Get, url, null, null, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.OK, owner.Status, "The requester reads the attachment");
            Check.True(owner.Bytes.Length > 100 && owner.Bytes[0] == 0xFF && owner.Bytes[1] == 0xD8, "JPEG bytes");
            Check.True(String.Join(",", owner.HeaderValues("Cache-Control")).Contains("private", StringComparison.Ordinal), "Private cache control");
            MediaHttpResult range = await MediaTestKit.SendAsync(f, HttpMethod.Get, url, null, null, ct, new Dictionary<string, string> { { "Range", "bytes=0-9" } }).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.PartialContent, range.Status, "Range");
            Check.Equal(10, range.Bytes.Length, "Ten bytes");
            Check.True(range.Bytes.SequenceEqual(owner.Bytes.Take(10)), "Range bytes match");

            string staffUrl = await MemberUrlAsync(f, agent.AccessToken, ticketId, ct, true).ConfigureAwait(false);
            Check.True(staffUrl != url, "Each viewer gets their own URL");
            MediaHttpResult staff = await MediaTestKit.SendAsync(f, HttpMethod.Get, staffUrl, null, null, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.OK, staff.Status, "Support staff read the attachment");
            string adminUrl = await MemberUrlAsync(f, HttpFixture.AdminToken, ticketId, ct, true).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.OK, (await MediaTestKit.SendAsync(f, HttpMethod.Get, adminUrl, null, null, ct).ConfigureAwait(false)).Status, "The admin token reads the attachment");

            string served = MediaTestKit.LocalPath(f, "images", MediaKeys.ImagePrefix(f.Property.Id, pending));
            string hidden = MediaTestKit.LocalPath(f, "source", MediaKeys.PrivateImagePrefix(f.Property.Id, pending));
            Check.True(!Directory.Exists(served) || Directory.GetFiles(served).Length == 0, "No served copy in the images folder");
            Check.True(Directory.Exists(hidden) && Directory.GetFiles(hidden).Length > 0, "Outputs under the private source prefix");
            Check.Equal(0L, await SupportTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM mediaattachments WHERE id = '" + pending + "' AND ispublic;", ct).ConfigureAwait(false), "Never public");

            string reply = await SupportTestKit.UploadAsync(f, agent.AccessToken, ct, ticketId).ConfigureAwait(false);
            JsonElement staffMessage = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/tickets/" + ticketId + "/messages", agent.AccessToken, new { body = "See the annotated image.", mediaIds = new[] { reply } }, HttpStatusCode.Created, ct).ConfigureAwait(false);
            Check.Equal(1, staffMessage.GetProperty("attachments").GetArrayLength(), "Staff attach images to replies");
            JsonElement memberTicket = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/tickets/" + ticketId, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            string staffAttachment = SupportTestKit.FirstAttachmentUrl(memberTicket.GetProperty("messages").GetProperty("objects")[1]);
            Check.Equal(HttpStatusCode.OK, (await MediaTestKit.SendAsync(f, HttpMethod.Get, staffAttachment, null, null, ct).ConfigureAwait(false)).Status, "The requester reads the staff attachment");
        }

        private static async Task GrantCookieAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            TestMember other = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            string ticketId = await TicketWithAttachmentAsync(f, member, ct).ConfigureAwait(false);
            MediaAttachment media = (await f.Database.Media.ListByTargetsAsync(TargetTypeEnum.TicketMessage, await f.Database.Support.MessageIdsAsync(ticketId, 10, ct).ConfigureAwait(false), ct).ConfigureAwait(false)).Single();
            List<MediaVariant> variants = await f.Database.Media.ReadVariantsAsync(media.Id, ct).ConfigureAwait(false);
            string fileName = variants.First(v => v.Name == "thumb" && v.Format == "jpeg").FileName;
            foreach (TestMember who in new[] { member, other })
            {
                MediaHttpResult grant = await MediaTestKit.JsonAsync(f, HttpMethod.Post, "/api/v1/media/grant", who.AccessToken, null, ct).ConfigureAwait(false);
                string cookie = Api.Str(grant.Json.GetProperty("cookies")[0], "value");
                MediaHttpResult viaCookie = await MediaTestKit.SendAsync(f, HttpMethod.Get, "/api/v1/media/files/" + media.Id + "/" + fileName, null, null, ct, new Dictionary<string, string> { { "Cookie", "tk_media=" + cookie } }).ConfigureAwait(false);
                Check.Equal(HttpStatusCode.NotFound, viaCookie.Status, "The grant cookie never reads a private object");
                MediaHttpResult viaBearer = await MediaTestKit.SendAsync(f, HttpMethod.Get, "/api/v1/media/files/" + media.Id + "/" + fileName, who.AccessToken, null, ct).ConfigureAwait(false);
                Check.Equal(HttpStatusCode.NotFound, viaBearer.Status, "A member token never reads a private object through the file route");
            }
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/media/" + media.Id, other.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            JsonElement batch = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/media?ids=" + media.Id, other.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(0, batch.GetProperty("objects").GetArrayLength(), "Other members never list private media");
        }

        private static async Task DenialsAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            TestMember other = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            TestMember agent = await SupportTestKit.AgentAsync(f, ct).ConfigureAwait(false);
            string ticketId = await TicketWithAttachmentAsync(f, member, ct).ConfigureAwait(false);
            string url = await MemberUrlAsync(f, member.AccessToken, ticketId, ct).ConfigureAwait(false);
            string file = url.Substring(url.LastIndexOf('/') + 1);
            MediaAttachment media = (await f.Database.Media.ListByTargetsAsync(TargetTypeEnum.TicketMessage, await f.Database.Support.MessageIdsAsync(ticketId, 10, ct).ConfigureAwait(false), ct).ConfigureAwait(false)).Single();

            string Url(string viewer, DateTime expires) => MediaService.PrivateFilesPath + "/" + f.Services.Media.PrivateTokens.Issue(new PrivateMediaGrant
            {
                PropertyId = f.Property.Id, MediaId = media.Id, Purpose = MediaPurposeEnum.Ticket, ScopeId = ticketId, ViewerKey = viewer, ExpiresUtc = expires
            }) + "/" + file;

            MediaHttpResult foreignViewer = await MediaTestKit.SendAsync(f, HttpMethod.Get, Url(other.Id, DateTime.UtcNow.AddMinutes(10)), null, null, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.Forbidden, foreignViewer.Status, "A token for a viewer outside the scope");
            MediaHttpResult expired = await MediaTestKit.SendAsync(f, HttpMethod.Get, Url(member.Id, DateTime.UtcNow.AddMinutes(-1)), null, null, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.Forbidden, expired.Status, "An expired token");
            Check.Equal("InvalidSignature", expired.Error, "InvalidSignature");
            MediaHttpResult forged = await MediaTestKit.SendAsync(f, HttpMethod.Get, MediaService.PrivateFilesPath + "/AAAAforgedtokenAAAA/" + file, null, null, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.Forbidden, forged.Status, "A forged token");
            string wrongScope = MediaService.PrivateFilesPath + "/" + f.Services.Media.PrivateTokens.Issue(new PrivateMediaGrant { PropertyId = f.Property.Id, MediaId = media.Id, Purpose = MediaPurposeEnum.Ticket, ScopeId = "tkt_other", ViewerKey = member.Id, ExpiresUtc = DateTime.UtcNow.AddMinutes(5) }) + "/" + file;
            Check.Equal(HttpStatusCode.NotFound, (await MediaTestKit.SendAsync(f, HttpMethod.Get, wrongScope, null, null, ct).ConfigureAwait(false)).Status, "A token for another scope");
            string tokenPath = url.Substring(0, url.LastIndexOf('/') + 1);
            Check.Equal(HttpStatusCode.NotFound, (await MediaTestKit.SendAsync(f, HttpMethod.Get, tokenPath + "nothing.jpg", null, null, ct).ConfigureAwait(false)).Status, "Unknown file");
            Check.Equal(HttpStatusCode.NotFound, (await MediaTestKit.SendAsync(f, HttpMethod.Get, tokenPath + "..%2Fsecret", null, null, ct).ConfigureAwait(false)).Status, "Unsafe file name");

            string agentUrl = await MemberUrlAsync(f, agent.AccessToken, ticketId, ct, true).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.OK, (await MediaTestKit.SendAsync(f, HttpMethod.Get, agentUrl, null, null, ct).ConfigureAwait(false)).Status, "The agent reads it while a SupportAgent");
            await f.Raw.ExecuteQueryAsync("DELETE FROM roleassignments WHERE userid = '" + agent.Id + "';", false, ct).ConfigureAwait(false);
            f.Services.Tokens.InvalidateUser(agent.Id);
            MediaHttpResult removed = await MediaTestKit.SendAsync(f, HttpMethod.Get, agentUrl, null, null, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.Forbidden, removed.Status, "A viewer removed from the scope loses access with an unexpired URL");

            Check.Equal(HttpStatusCode.OK, (await MediaTestKit.SendAsync(f, HttpMethod.Get, url, null, null, ct).ConfigureAwait(false)).Status, "The requester still reads it");
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/tickets/" + ticketId + "/close", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/admin/tickets/" + ticketId, HttpFixture.AdminToken, null, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
            MediaHttpResult deleted = await MediaTestKit.SendAsync(f, HttpMethod.Get, url, null, null, ct).ConfigureAwait(false);
            Check.True(deleted.Status == HttpStatusCode.NotFound || deleted.Status == HttpStatusCode.Forbidden, "Deleting the ticket ends access (" + (int)deleted.Status + ")");
            Check.True(!Directory.Exists(MediaTestKit.LocalPath(f, "source", MediaKeys.PrivatePrefix(f.Property.Id, media.Id))), "Deleting the ticket deletes the private objects");
        }

        private static async Task AttachRulesAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember member = await ContentTestKit.PosterAsync(f, ct).ConfigureAwait(false);
            TestMember other = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            string ticketA = await SupportTestKit.TicketIdAsync(f, member.AccessToken, ct, "First ticket of mine").ConfigureAwait(false);
            string ticketB = await SupportTestKit.TicketIdAsync(f, member.AccessToken, ct, "Second ticket of mine").ConfigureAwait(false);
            string forA = await SupportTestKit.UploadAsync(f, member.AccessToken, ct, ticketA).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/tickets/" + ticketB + "/messages", member.AccessToken, new { body = "Wrong ticket.", mediaIds = new[] { forA } }, HttpStatusCode.Conflict, "MediaNotAttachable", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/tickets/" + ticketA + "/messages", member.AccessToken, new { body = "Right ticket.", mediaIds = new[] { forA } }, HttpStatusCode.Created, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/tickets/" + ticketA + "/messages", member.AccessToken, new { body = "Again.", mediaIds = new[] { forA } }, HttpStatusCode.Conflict, "MediaNotAttachable", ct).ConfigureAwait(false);

            string privateUpload = await SupportTestKit.UploadAsync(f, member.AccessToken, ct).ConfigureAwait(false);
            ApiResult post = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/feed", member.AccessToken, new { body = "A post with a private image", mediaIds = new[] { privateUpload } }, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.Conflict, post.Status, "Private media never attaches to content (" + post.Text + ")");
            Check.Equal("MediaNotAttachable", post.Error, "MediaNotAttachable");
            string content = await MediaTestKit.UploadSingleAsync(f, member.AccessToken, "Image", "image/png", MediaTestKit.Png(200, 100), ct).ConfigureAwait(false);
            await MediaTestKit.ProcessAsync(f, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/tickets/" + ticketA + "/messages", member.AccessToken, new { body = "Content image.", mediaIds = new[] { content } }, HttpStatusCode.Conflict, "MediaNotAttachable", ct).ConfigureAwait(false);
            string othersUpload = await SupportTestKit.UploadAsync(f, other.AccessToken, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/tickets/" + ticketA + "/messages", member.AccessToken, new { body = "Someone else's image.", mediaIds = new[] { othersUpload } }, HttpStatusCode.Conflict, "MediaNotAttachable", ct).ConfigureAwait(false);

            MediaHttpResult foreignScope = await MediaTestKit.JsonAsync(f, HttpMethod.Post, "/api/v1/tickets/uploads", other.AccessToken, new { kind = "Image", contentType = "image/png", sizeBytes = 100, scopeId = ticketA }, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.NotFound, foreignScope.Status, "Uploading into someone else's ticket");
            MediaHttpResult video = await MediaTestKit.JsonAsync(f, HttpMethod.Post, "/api/v1/tickets/uploads", member.AccessToken, new { kind = "Video", contentType = "video/mp4", sizeBytes = 1000, durationSeconds = 3 }, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.Forbidden, video.Status, "No ticket video unless support.allowVideo");
            MediaHttpResult viaMedia = await MediaTestKit.JsonAsync(f, HttpMethod.Post, "/api/v1/media/uploads", member.AccessToken, new { kind = "Image", contentType = "image/png", sizeBytes = 100, purpose = "Ticket", scopeId = ticketB }, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.Created, viaMedia.Status, "Members may also use the media route with purpose Ticket (" + viaMedia.Text + ")");
            MediaHttpResult badPurpose = await MediaTestKit.JsonAsync(f, HttpMethod.Post, "/api/v1/media/uploads", member.AccessToken, new { kind = "Image", contentType = "image/png", sizeBytes = 100, purpose = "Secret" }, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.BadRequest, badPurpose.Status, "Unknown purpose");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/tickets/uploads/" + content + "/complete", member.AccessToken, new { }, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);

            await f.Database.UserPermissions.UpsertAsync(new UserPermission { UserId = member.Id, CanPost = true, CanPostMedia = false }, ct).ConfigureAwait(false);
            f.Services.Tokens.InvalidateUser(member.Id);
            string restricted = await SupportTestKit.UploadAsync(f, member.AccessToken, ct, ticketA).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/tickets/" + ticketA + "/messages", member.AccessToken, new { body = "Even without media rights.", mediaIds = new[] { restricted } }, HttpStatusCode.Created, ct).ConfigureAwait(false);
        }

        private static async Task IsolationAsync(HttpFixture f, CancellationToken ct)
        {
            SupportTestKit.Relax(f);
            string platform = await MultiPropertyKit.PlatformLoginAsync(f, ct).ConfigureAwait(false);
            string second = await MultiPropertyKit.CreateSecondAsync(f, platform, ct).ConfigureAwait(false);
            f.PropertyId = second;
            TestMember member = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
            string media = await SupportTestKit.UploadAsync(f, member.AccessToken, ct).ConfigureAwait(false);
            JsonElement ticket = await SupportTestKit.TicketAsync(f, member.AccessToken, ct, "Ticket in property B", "Attachment lives in property B.", "Bug", new[] { media }).ConfigureAwait(false);
            string url = SupportTestKit.FirstAttachmentUrl(ticket.GetProperty("messages").GetProperty("objects")[0]);
            Check.Equal(HttpStatusCode.OK, (await MediaTestKit.SendAsync(f, HttpMethod.Get, url, null, null, ct).ConfigureAwait(false)).Status, "B reads its own URL");
            f.PropertyId = TestProperty.SampleId;
            MediaHttpResult cross = await MediaTestKit.SendAsync(f, HttpMethod.Get, url, null, null, ct).ConfigureAwait(false);
            Check.True(cross.Status == HttpStatusCode.Forbidden || cross.Status == HttpStatusCode.NotFound, "Property A refuses B's URL (" + (int)cross.Status + ")");
            Check.True(cross.Bytes.Length < 200 || !(cross.Bytes[0] == 0xFF && cross.Bytes[1] == 0xD8), "No bytes from B");
        }

        #endregion
    }
}

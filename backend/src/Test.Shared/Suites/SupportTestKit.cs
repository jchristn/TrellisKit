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
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Helpers of the Phase 5 support suites (TicketSuite, PrivateMediaSuite).
    /// </summary>
    internal static class SupportTestKit
    {
        internal static void Relax(HttpFixture f)
        {
            f.Settings.Support.TicketsPerDay = 1000;
            f.Settings.Support.RepliesPerHour = 10000;
            f.Settings.Support.MaxOpenTicketsPerMember = 100;
            f.Settings.Media.UploadsPerHour = 10000;
        }

        internal static async Task<TestMember> AgentAsync(HttpFixture f, CancellationToken ct, string displayName = "Support Agent")
        {
            TestMember agent = await Api.RegisterAsync(f, ct, Api.UniqueEmail("agent"), displayName).ConfigureAwait(false);
            await SafetyHelpers.GrantRoleAsync(f, agent.Id, RoleEnum.SupportAgent, ct).ConfigureAwait(false);
            return agent;
        }

        internal static async Task<ApiResult> CreateAsync(HttpFixture f, string bearer, object body, CancellationToken ct, Dictionary<string, string>? headers = null)
        {
            return await Api.SendAsync(f, HttpMethod.Post, "/api/v1/tickets", bearer, body, ct, "mobile", headers).ConfigureAwait(false);
        }

        internal static async Task<JsonElement> TicketAsync(HttpFixture f, string bearer, CancellationToken ct, string title = "Charged twice this month", string body = "I was charged twice for my membership.", string type = "Billing", string[]? mediaIds = null)
        {
            ApiResult result = await CreateAsync(f, bearer, new { type, title, body, mediaIds = mediaIds ?? Array.Empty<string>() }, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.Created, result.Status, "Create ticket (" + result.Text + ")");
            return result.Body;
        }

        internal static async Task<string> TicketIdAsync(HttpFixture f, string bearer, CancellationToken ct, string title = "Charged twice this month")
        {
            return Api.Str(await TicketAsync(f, bearer, ct, title).ConfigureAwait(false), "id");
        }

        internal static async Task<JsonElement> StaffReplyAsync(HttpFixture f, string bearer, string ticketId, string text, CancellationToken ct, string kind = "Reply", string? setStatus = null)
        {
            return await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/tickets/" + ticketId + "/messages", bearer, new { kind, body = text, setStatus }, HttpStatusCode.Created, ct).ConfigureAwait(false);
        }

        internal static async Task<string> UploadAsync(HttpFixture f, string bearer, CancellationToken ct, string? scopeId = null, string path = "/api/v1/tickets/uploads", string? purpose = null)
        {
            byte[] png = MediaTestKit.Png(500, 300);
            MediaHttpResult session = await MediaTestKit.JsonAsync(f, HttpMethod.Post, path, bearer, new { kind = "Image", contentType = "image/png", sizeBytes = png.Length, fileName = "screenshot.png", scopeId, purpose }, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.Created, session.Status, "Upload session (" + session.Text + ")");
            string id = session.Json.GetProperty("media").GetProperty("id").GetString()!;
            string url = session.Json.GetProperty("upload").GetProperty("single").GetProperty("url").GetString()!;
            MediaHttpResult put = await MediaTestKit.PutBytesAsync(f, url, png, "image/png", ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.OK, put.Status, "Upload PUT (" + put.Text + ")");
            string complete = path == "/api/v1/tickets/uploads" ? "/api/v1/tickets/uploads/" + id + "/complete" : "/api/v1/media/" + id + "/complete";
            MediaHttpResult done = await MediaTestKit.JsonAsync(f, HttpMethod.Post, complete, bearer, new { }, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.Accepted, done.Status, "Complete (" + done.Text + ")");
            await MediaTestKit.ProcessAsync(f, ct).ConfigureAwait(false);
            return id;
        }

        internal static Task<long> CountAsync(HttpFixture f, string sql, CancellationToken ct)
        {
            return ContentTestKit.ScalarAsync(f, sql, ct);
        }

        internal static Task<long> EmailsAsync(HttpFixture f, string template, string? userId, CancellationToken ct)
        {
            return CountAsync(f, "SELECT COUNT(*) AS cnt FROM emailmessages WHERE template = '" + template + "'" + (userId == null ? String.Empty : " AND userid = '" + userId + "'") + ";", ct);
        }

        internal static List<string> Ids(JsonElement page)
        {
            return page.GetProperty("objects").EnumerateArray().Select(o => Api.Str(o, "id")).ToList();
        }

        internal static string FirstAttachmentUrl(JsonElement message)
        {
            JsonElement attachment = message.GetProperty("attachments")[0];
            return attachment.GetProperty("image").GetProperty("variants").GetProperty("thumb").GetProperty("jpegUrl").GetString()!;
        }
    }
}

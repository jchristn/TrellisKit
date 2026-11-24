namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Net.WebSockets;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Models;
    using TrellisKit.Server.Services;
    using TrellisKit.Server.Services.Realtime;

    /// <summary>
    /// Realtime helpers shared by the realtime and messaging suites: connect a real socket to the in-test server (with
    /// the fixture's property and app headers), send hello and read welcome, read frames with timeouts, probe refused
    /// upgrades over plain HTTP, issue access tokens with chosen expiries, and register a test renderer that produces
    /// frames of a chosen size.
    /// </summary>
    internal static class RealtimeTestKit
    {
        internal static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

        internal const string BlobKind = "test.blob";

        internal static async Task<RealtimeTestClient> ConnectAsync(HttpFixture f, CancellationToken ct, string? origin = null, Dictionary<string, string>? headers = null)
        {
            ClientWebSocket socket = new ClientWebSocket();
            socket.Options.CollectHttpResponseDetails = true;
            socket.Options.KeepAliveInterval = TimeSpan.Zero;
            if (origin != null) socket.Options.SetRequestHeader("Origin", origin);
            foreach (KeyValuePair<string, string> header in PropertyHeaders(f)) socket.Options.SetRequestHeader(header.Key, header.Value);
            if (headers != null) foreach (KeyValuePair<string, string> header in headers) socket.Options.SetRequestHeader(header.Key, header.Value);
            Uri uri = new Uri("ws://127.0.0.1:" + f.Settings.Rest.Port + RealtimeGateway.Path);
            try
            {
                await socket.ConnectAsync(uri, ct).ConfigureAwait(false);
            }
            catch (WebSocketException e)
            {
                HttpStatusCode status = socket.HttpStatusCode;
                socket.Dispose();
                throw new InvalidOperationException("WebSocket upgrade failed with HTTP " + (int)status + ": " + e.Message, e);
            }
            RealtimeTestClient client = new RealtimeTestClient(socket);
            client.StartPump();
            return client;
        }

        /// <summary>
        /// The HTTP status of an upgrade attempt that the server refuses (0 when it was accepted).
        /// </summary>
        internal static async Task<int> UpgradeStatusAsync(HttpFixture f, CancellationToken ct, string? origin = null, string? subprotocol = null)
        {
            ClientWebSocket socket = new ClientWebSocket();
            socket.Options.CollectHttpResponseDetails = true;
            if (origin != null) socket.Options.SetRequestHeader("Origin", origin);
            if (subprotocol != null) socket.Options.AddSubProtocol(subprotocol);
            foreach (KeyValuePair<string, string> header in PropertyHeaders(f)) socket.Options.SetRequestHeader(header.Key, header.Value);
            try
            {
                await socket.ConnectAsync(new Uri("ws://127.0.0.1:" + f.Settings.Rest.Port + RealtimeGateway.Path), ct).ConfigureAwait(false);
                try
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "probe", ct).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // Probe only.
                }
                return 0;
            }
            catch (WebSocketException)
            {
                return (int)socket.HttpStatusCode;
            }
            finally
            {
                socket.Dispose();
            }
        }

        /// <summary>
        /// A refused upgrade over plain HTTP (status, error code, Retry-After).
        /// </summary>
        internal static Task<ApiResult> RawUpgradeAsync(HttpFixture f, CancellationToken ct, Dictionary<string, string>? extra = null)
        {
            Dictionary<string, string> headers = new Dictionary<string, string>
            {
                ["Upgrade"] = "websocket",
                ["Connection"] = "Upgrade",
                ["Sec-WebSocket-Version"] = "13",
                ["Sec-WebSocket-Key"] = Convert.ToBase64String(Guid.NewGuid().ToByteArray())
            };
            if (extra != null) foreach (KeyValuePair<string, string> pair in extra) headers[pair.Key] = pair.Value;
            return Api.SendAsync(f, HttpMethod.Get, RealtimeGateway.Path, null, null, ct, "web", headers);
        }

        internal static async Task<JsonElement> HelloAsync(RealtimeTestClient client, string accessToken, CancellationToken ct, Dictionary<string, object?>? data = null)
        {
            Dictionary<string, object?> hello = new Dictionary<string, object?> { ["accessToken"] = accessToken, ["client"] = "Mobile", ["appState"] = "Foreground", ["protocol"] = "tk.v1" };
            if (data != null) foreach (KeyValuePair<string, object?> pair in data) hello[pair.Key] = pair.Value;
            await client.SendAsync(new { type = "hello", id = "c1", data = hello }, ct).ConfigureAwait(false);
            JsonElement welcome = await client.ExpectDataAsync("welcome", Wait, ct).ConfigureAwait(false);
            client.Welcome = welcome;
            return welcome;
        }

        internal static async Task<RealtimeTestClient> OpenAsync(HttpFixture f, string accessToken, CancellationToken ct, Dictionary<string, object?>? data = null)
        {
            RealtimeTestClient client = await ConnectAsync(f, ct).ConfigureAwait(false);
            await HelloAsync(client, accessToken, ct, data).ConfigureAwait(false);
            return client;
        }

        /// <summary>
        /// Send hello and expect an error frame followed by a close; returns the close code.
        /// </summary>
        internal static async Task<int?> HelloRefusedAsync(RealtimeTestClient client, string accessToken, CancellationToken ct, Dictionary<string, object?>? data = null)
        {
            Dictionary<string, object?> hello = new Dictionary<string, object?> { ["accessToken"] = accessToken, ["client"] = "Mobile" };
            if (data != null) foreach (KeyValuePair<string, object?> pair in data) hello[pair.Key] = pair.Value;
            await client.SendAsync(new { type = "hello", id = "c1", data = hello }, ct).ConfigureAwait(false);
            return await client.WaitCloseAsync(Wait, ct).ConfigureAwait(false);
        }

        internal static List<string> Capabilities(JsonElement data)
        {
            return data.GetProperty("capabilities").EnumerateArray().Select(c => c.GetString()!).ToList();
        }

        internal static async Task PingAsync(RealtimeTestClient client, CancellationToken ct)
        {
            await client.SendAsync(new { type = "ping", id = "p" + Guid.NewGuid().ToString("N").Substring(0, 6), data = new { } }, ct).ConfigureAwait(false);
            await client.ExpectAsync("pong", Wait, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// An access token of the member's current session issued at a chosen time (expiry = issuedUtc + 60 minutes).
        /// </summary>
        internal static async Task<string> TokenIssuedAtAsync(HttpFixture f, string accessToken, DateTime issuedUtc, CancellationToken ct)
        {
            SessionPrincipal principal = (await f.Services.Tokens.ValidateAccessTokenAsync(accessToken, ct).ConfigureAwait(false))!;
            AuthSession session = (await f.Database.Sessions.ReadAsync(principal.SessionId, ct).ConfigureAwait(false))!;
            return f.Services.Tokens.IssueAccessToken(session, issuedUtc).Token;
        }

        /// <summary>
        /// An access token of the member's current session that expires after a delay.
        /// </summary>
        internal static Task<string> TokenExpiringInAsync(HttpFixture f, string accessToken, TimeSpan delay, CancellationToken ct)
        {
            return TokenIssuedAtAsync(f, accessToken, DateTime.UtcNow.AddMinutes(-f.Settings.Auth.AccessTokenMinutes).Add(delay), ct);
        }

        /// <summary>
        /// Register the test.blob renderer on a property's services: frames with a pad of the given size.
        /// </summary>
        internal static void RegisterBlob(ApplicationServices services, int padBytes, bool droppable = false)
        {
            string pad = new string('x', padBytes);
            services.Realtime.Renderers.Register(BlobKind, null, null, droppable, (context, token) => Task.FromResult<object?>(new Dictionary<string, object?> { ["seq"] = context.Event.Seq, ["pad"] = pad }));
        }

        /// <summary>
        /// Dispatch an event to this task's sockets only (no bus round trip).
        /// </summary>
        internal static RealtimeEvent Local(HttpFixture f, string kind, string userId, long? seq = null, string? conversationId = null)
        {
            RealtimeEvent realtimeEvent = new RealtimeEvent { PropertyId = f.Services.PropertyId, Kind = kind, UserIds = new List<string> { userId }, Seq = seq, ConversationId = conversationId };
            f.Server.Realtime.DispatchLocal(realtimeEvent);
            return realtimeEvent;
        }

        internal static async Task<bool> EventuallyAsync(Func<Task<bool>> condition, TimeSpan timeout, CancellationToken ct)
        {
            DateTime until = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < until)
            {
                if (await condition().ConfigureAwait(false)) return true;
                await Task.Delay(100, ct).ConfigureAwait(false);
            }
            return await condition().ConfigureAwait(false);
        }

        private static List<KeyValuePair<string, string>> PropertyHeaders(HttpFixture f)
        {
            List<KeyValuePair<string, string>> headers = new List<KeyValuePair<string, string>>();
            if (f.PropertyId == null) return headers;
            headers.Add(new KeyValuePair<string, string>("X-TK-Property", f.PropertyId));
            PropertyRuntime? runtime = f.Server.Directory.Find(f.PropertyId);
            string? app = runtime?.Apps.FirstOrDefault(a => a.Enabled)?.AppId;
            if (app != null) headers.Add(new KeyValuePair<string, string>("X-TK-App", app));
            return headers;
        }
    }
}

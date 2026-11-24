namespace Test.Shared.IntegrationTests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Touchstone.Core;
    using TrellisKit.Core.Integrations.Implementations;
    using TrellisKit.Core.Integrations.Models;
    using TrellisKit.Core.Settings;

    /// <summary>
    /// Expo push sender against a fake Expo endpoint: request shape, batches of 100, bearer token, tickets, receipts,
    /// and HTTP errors.
    /// </summary>
    public static class ExpoPushSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "IntegrationExpoPush";

        #endregion

        #region Private-Members

        private const string _BaseUrl = "https://expo.test";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Expo push sender", new[]
            {
                Case("Send.ShapeAndBatches", "Messages are posted in batches of 100 with the contract fields; tickets map back in order", async ct =>
                {
                    using (TestHttp http = new TestHttp())
                    {
                        http.Handler.Map("POST", _BaseUrl + "/--/api/v2/push/send", r =>
                        {
                            using (JsonDocument doc = JsonDocument.Parse(r.Body))
                            {
                                int count = doc.RootElement.GetArrayLength();
                                IEnumerable<string> tickets = Enumerable.Range(0, count).Select(i => i == 1 && doc.RootElement[i].GetProperty("to").GetString()!.Contains("dead", StringComparison.Ordinal)
                                    ? "{\"status\":\"error\",\"message\":\"gone\",\"details\":{\"error\":\"DeviceNotRegistered\"}}"
                                    : "{\"status\":\"ok\",\"id\":\"t-" + doc.RootElement[i].GetProperty("to").GetString() + "\"}");
                                return FakeHttpResponse.Json(200, "{\"data\":[" + String.Join(",", tickets) + "]}");
                            }
                        });
                        PushSettings settings = new PushSettings { ExpoAccessToken = "expo-secret" };
                        ExpoPushSender sender = new ExpoPushSender(settings, http.Client) { BaseUrl = _BaseUrl };
                        List<PushMessage> messages = Enumerable.Range(0, 150).Select(i => new PushMessage
                        {
                            To = i == 1 ? "ExponentPushToken[dead]" : "ExponentPushToken[" + i + "]",
                            Title = "Alex Chen replied to your comment",
                            Body = "Body " + i,
                            Data = new Dictionary<string, string> { { "notificationId", "ntf_" + i }, { "type", "CommentReply" }, { "path", "/feed/fpo_1" } },
                            Badge = 7
                        }).ToList();
                        List<PushTicket> tickets = await sender.SendAsync(messages, ct).ConfigureAwait(false);
                        Check.Equal(150, tickets.Count, "One ticket per message");
                        Check.Equal(2, http.Handler.Count(_BaseUrl + "/--/api/v2/push/send"), "Two batches");
                        Check.True(!tickets[1].IsOk && tickets[1].Error == "DeviceNotRegistered", "Error ticket");
                        Check.Equal("t-ExponentPushToken[149]", tickets[149].Id, "Order kept across batches");
                        RecordedRequest first = http.Handler.Requests[0];
                        Check.Equal("Bearer expo-secret", first.Header("Authorization"), "Bearer token");
                        using (JsonDocument doc = JsonDocument.Parse(first.Body))
                        {
                            Check.Equal(100, doc.RootElement.GetArrayLength(), "First batch of 100");
                            JsonElement m = doc.RootElement[0];
                            Check.Equal("ExponentPushToken[0]", m.GetProperty("to").GetString(), "to");
                            Check.Equal("Alex Chen replied to your comment", m.GetProperty("title").GetString(), "title");
                            Check.Equal("default", m.GetProperty("sound").GetString(), "sound");
                            Check.Equal(7, m.GetProperty("badge").GetInt32(), "badge");
                            Check.Equal("default", m.GetProperty("channelId").GetString(), "channelId");
                            Check.Equal("high", m.GetProperty("priority").GetString(), "priority");
                            Check.Equal("ntf_0", m.GetProperty("data").GetProperty("notificationId").GetString(), "data");
                        }
                        settings.ExpoAccessToken = String.Empty;
                        await sender.SendAsync(messages.Take(1).ToList(), ct).ConfigureAwait(false);
                        Check.True(http.Handler.Requests.Last().Header("Authorization") == null, "No bearer without a token");
                    }
                }),
                Case("Receipts.Parse", "Receipts are read for ticket ids; missing receipts are absent", async ct =>
                {
                    using (TestHttp http = new TestHttp())
                    {
                        http.Handler.MapJson("POST", _BaseUrl + "/--/api/v2/push/getReceipts", 200,
                            "{\"data\":{\"a\":{\"status\":\"ok\"},\"b\":{\"status\":\"error\",\"message\":\"too big\",\"details\":{\"error\":\"MessageTooBig\"}}}}");
                        ExpoPushSender sender = new ExpoPushSender(new PushSettings(), http.Client) { BaseUrl = _BaseUrl };
                        Dictionary<string, PushReceipt> receipts = await sender.GetReceiptsAsync(new List<string> { "a", "b", "c" }, ct).ConfigureAwait(false);
                        Check.True(receipts["a"].IsOk, "Ok receipt");
                        Check.Equal("MessageTooBig", receipts["b"].Error, "Error receipt");
                        Check.True(!receipts.ContainsKey("c"), "Pending receipt absent");
                        using (JsonDocument doc = JsonDocument.Parse(http.Handler.Requests[0].Body))
                        {
                            Check.Equal(3, doc.RootElement.GetProperty("ids").GetArrayLength(), "Ids sent");
                        }
                    }
                }),
                Case("Errors.Http", "HTTP errors and non-JSON bodies throw HttpRequestException", async ct =>
                {
                    using (TestHttp http = new TestHttp())
                    {
                        http.Handler.MapJson("POST", _BaseUrl + "/--/api/v2/push/send", 429, "{\"errors\":[{\"code\":\"TOO_MANY_REQUESTS\"}]}");
                        http.Handler.Map("POST", _BaseUrl + "/--/api/v2/push/getReceipts", r => new FakeHttpResponse { Status = 200, Body = "<html>", ContentType = "text/html" });
                        ExpoPushSender sender = new ExpoPushSender(new PushSettings(), http.Client) { BaseUrl = _BaseUrl };
                        await Check.ThrowsAsync<HttpRequestException>(() => sender.SendAsync(new List<PushMessage> { new PushMessage { To = "ExponentPushToken[x]", Title = "t" } }, ct), "429").ConfigureAwait(false);
                        await Check.ThrowsAsync<HttpRequestException>(() => sender.GetReceiptsAsync(new List<string> { "a" }, ct), "Not JSON").ConfigureAwait(false);
                        Check.Throws<ArgumentNullException>(() => sender.BaseUrl = " ", "Empty base URL");
                    }
                })
            });
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string caseId, string name, Func<CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(Id, caseId, name, body, new[] { "fast", "unit", "integrations" });
        }

        #endregion
    }
}

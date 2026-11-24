namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Text;
    using System.Text.Json;

    /// <summary>
    /// A raw HTTP response read by the media cases (status, every header, and the body bytes).
    /// </summary>
    internal sealed class MediaHttpResult
    {
        internal HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        internal Dictionary<string, List<string>> Headers { get; } = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        internal byte[] Bytes { get; set; } = Array.Empty<byte>();

        internal string Text
        {
            get { return Encoding.UTF8.GetString(Bytes); }
        }

        internal JsonElement Json
        {
            get
            {
                if (Bytes.Length == 0) return default;
                using (JsonDocument doc = JsonDocument.Parse(Bytes)) return doc.RootElement.Clone();
            }
        }

        internal string? Error
        {
            get
            {
                try
                {
                    JsonElement body = Json;
                    return body.ValueKind == JsonValueKind.Object && body.TryGetProperty("error", out JsonElement e) ? e.GetString() : null;
                }
                catch (JsonException)
                {
                    return null;
                }
            }
        }

        internal string? Header(string name)
        {
            return Headers.TryGetValue(name, out List<string>? values) ? values.FirstOrDefault() : null;
        }

        internal List<string> HeaderValues(string name)
        {
            return Headers.TryGetValue(name, out List<string>? values) ? values : new List<string>();
        }
    }
}

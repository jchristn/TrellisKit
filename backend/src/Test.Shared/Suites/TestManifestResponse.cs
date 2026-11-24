namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Text;
    using System.Text.Json;

    /// <summary>
    /// A manifest endpoint response with its multipart parts.
    /// </summary>
    internal sealed class TestManifestResponse
    {
        internal HttpStatusCode Status { get; set; }

        internal string ContentType { get; set; } = String.Empty;

        internal string Text { get; set; } = String.Empty;

        internal Dictionary<string, string> Headers { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        internal List<TestManifestPart> Parts { get; } = new List<TestManifestPart>();

        internal TestManifestPart? Part(string name)
        {
            return Parts.FirstOrDefault(p => p.Name == name);
        }

        internal JsonElement Json(string name)
        {
            TestManifestPart part = Part(name) ?? throw new InvalidOperationException("No " + name + " part in: " + Text);
            using (JsonDocument doc = JsonDocument.Parse(Encoding.UTF8.GetString(part.Body))) return doc.RootElement.Clone();
        }
    }
}

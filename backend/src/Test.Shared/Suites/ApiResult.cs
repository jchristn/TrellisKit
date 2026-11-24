namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Net;
    using System.Net.Http;
    using System.Text.Json;

    /// <summary>
    /// A response read by <see cref="Api"/>.
    /// </summary>
    internal sealed class ApiResult
    {
        internal HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        internal JsonElement Body { get; set; } = default;

        internal string Text { get; set; } = String.Empty;

        internal string? SetCookie { get; set; } = null;

        internal string? ContentType { get; set; } = null;

        internal Dictionary<string, string> Headers { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Retry-After header, or null.
        /// </summary>
        internal string? RetryAfter { get; set; } = null;

        internal string? Error
        {
            get { return Body.ValueKind == JsonValueKind.Object && Body.TryGetProperty("error", out JsonElement e) ? e.GetString() : null; }
        }
    }
}

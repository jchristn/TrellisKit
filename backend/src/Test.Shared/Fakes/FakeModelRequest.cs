namespace Test.Shared.Fakes
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One request received by <see cref="FakeModelServer"/>.
    /// </summary>
    public sealed class FakeModelRequest
    {
        /// <summary>
        /// HTTP method.
        /// </summary>
        public string Method { get; set; } = String.Empty;

        /// <summary>
        /// Path.
        /// </summary>
        public string Path { get; set; } = String.Empty;

        /// <summary>
        /// Query string including the question mark, or empty.
        /// </summary>
        public string Query { get; set; } = String.Empty;

        /// <summary>
        /// Request headers.
        /// </summary>
        public Dictionary<string, string> Headers { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Request body.
        /// </summary>
        public string Body { get; set; } = String.Empty;
    }
}

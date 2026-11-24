namespace Test.Shared.IntegrationTests
{
    using System;

    /// <summary>
    /// A canned response served by <see cref="FakeHttpHandler"/>.
    /// </summary>
    public sealed class FakeHttpResponse
    {
        #region Public-Members

        /// <summary>
        /// HTTP status code.
        /// </summary>
        public int Status { get; set; } = 200;

        /// <summary>
        /// Body text.
        /// </summary>
        public string Body { get; set; } = String.Empty;

        /// <summary>
        /// Content type.
        /// </summary>
        public string ContentType { get; set; } = "application/json";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Create a JSON response.
        /// </summary>
        /// <param name="status">Status code.</param>
        /// <param name="body">JSON body.</param>
        /// <returns>Response.</returns>
        public static FakeHttpResponse Json(int status, string body)
        {
            return new FakeHttpResponse { Status = status, Body = body ?? String.Empty };
        }

        #endregion
    }
}

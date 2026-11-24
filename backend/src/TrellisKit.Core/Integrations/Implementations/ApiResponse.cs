namespace TrellisKit.Core.Integrations.Implementations
{
    using System;
    using System.Net;

    /// <summary>
    /// Status and body of an outbound API call.
    /// Immutable and thread safe.
    /// </summary>
    internal sealed class ApiResponse
    {
        #region Internal-Members

        /// <summary>
        /// HTTP status.
        /// </summary>
        internal HttpStatusCode Status { get; }

        /// <summary>
        /// Response body, never null.
        /// </summary>
        internal string Body { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the response.
        /// </summary>
        /// <param name="status">HTTP status.</param>
        /// <param name="body">Body.</param>
        internal ApiResponse(HttpStatusCode status, string? body)
        {
            Status = status;
            Body = body ?? String.Empty;
        }

        #endregion
    }
}

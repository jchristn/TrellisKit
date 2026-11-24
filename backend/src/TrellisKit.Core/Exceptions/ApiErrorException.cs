namespace TrellisKit.Core.Exceptions
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A request failed with a contract error: an HTTP status, an error code, a message, and optional field errors or a
    /// custom body. Services throw it; route handlers turn it into the error response.
    /// </summary>
    public class ApiErrorException : Exception
    {
        #region Public-Members

        /// <summary>
        /// HTTP status code, for example 400 or 409.
        /// </summary>
        public int StatusCode { get; }

        /// <summary>
        /// Machine-readable error code, for example ValidationFailed.
        /// </summary>
        public string Error { get; }

        /// <summary>
        /// Per-field messages, or null.
        /// </summary>
        public Dictionary<string, string>? FieldErrors { get; }

        /// <summary>
        /// A complete response body to send instead of the standard error body, or null.
        /// </summary>
        public object? Body { get; }

        /// <summary>
        /// Seconds the caller should wait before retrying, sent as the Retry-After header, or null. Every 429 carries one
        /// (60 when not set).
        /// </summary>
        public int? RetryAfterSeconds { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the exception.
        /// </summary>
        /// <param name="statusCode">HTTP status code.</param>
        /// <param name="error">Error code.</param>
        /// <param name="message">Message safe to show to the caller.</param>
        /// <param name="fieldErrors">Per-field messages, or null.</param>
        /// <param name="body">Custom body, or null.</param>
        public ApiErrorException(int statusCode, string error, string message, Dictionary<string, string>? fieldErrors = null, object? body = null)
            : base(message)
        {
            StatusCode = statusCode;
            Error = error ?? String.Empty;
            FieldErrors = fieldErrors;
            Body = body;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// 400 ValidationFailed with field errors.
        /// </summary>
        /// <param name="fieldErrors">Per-field messages.</param>
        /// <param name="message">Message.</param>
        /// <returns>Exception.</returns>
        public static ApiErrorException Validation(Dictionary<string, string> fieldErrors, string message = "One or more fields are invalid.")
        {
            return new ApiErrorException(400, "ValidationFailed", message, fieldErrors);
        }

        /// <summary>
        /// 400 ValidationFailed for one field.
        /// </summary>
        /// <param name="field">Field name.</param>
        /// <param name="fieldMessage">Field message.</param>
        /// <returns>Exception.</returns>
        public static ApiErrorException Field(string field, string fieldMessage)
        {
            return Validation(new Dictionary<string, string> { { field, fieldMessage } });
        }

        /// <summary>
        /// 404 NotFound.
        /// </summary>
        /// <param name="what">What was not found, for example "Member".</param>
        /// <returns>Exception.</returns>
        public static ApiErrorException NotFound(string what)
        {
            return new ApiErrorException(404, "NotFound", what + " not found.");
        }

        /// <summary>
        /// 429 RateLimited with a Retry-After value.
        /// </summary>
        /// <param name="retryAfterSeconds">Seconds until the caller may retry. Minimum 1.</param>
        /// <param name="message">Message.</param>
        /// <returns>Exception.</returns>
        public static ApiErrorException RateLimited(int retryAfterSeconds, string message = "Too many requests. Try again later.")
        {
            return new ApiErrorException(429, "RateLimited", message) { RetryAfterSeconds = Math.Max(1, retryAfterSeconds) };
        }

        /// <summary>
        /// 403 Forbidden.
        /// </summary>
        /// <param name="message">Message.</param>
        /// <returns>Exception.</returns>
        public static ApiErrorException Forbidden(string message)
        {
            return new ApiErrorException(403, "Forbidden", message);
        }

        #endregion
    }
}

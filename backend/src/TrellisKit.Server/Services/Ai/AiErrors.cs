namespace TrellisKit.Server.Services.Ai
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Exceptions;

    /// <summary>
    /// Error responses added by docs/AI_CONTRACT.md section 0.4.
    /// </summary>
    public static class AiErrors
    {
        #region Public-Methods

        /// <summary>
        /// 404 NotAvailable: features.ai is off or ai.enabled is false.
        /// </summary>
        /// <returns>Exception.</returns>
        public static ApiErrorException NotAvailable()
        {
            return new ApiErrorException(404, "NotAvailable", "AI assistants are not available.");
        }

        /// <summary>
        /// 503 ModelUnavailable.
        /// </summary>
        /// <returns>Exception.</returns>
        public static ApiErrorException ModelUnavailable()
        {
            return new ApiErrorException(503, "ModelUnavailable", "The assistant is unavailable right now. Try again later.");
        }

        /// <summary>
        /// 503 ModelBusy with Retry-After 5.
        /// </summary>
        /// <returns>Exception.</returns>
        public static ApiErrorException ModelBusy()
        {
            return new ApiErrorException(503, "ModelBusy", "The assistant is busy. Try again in a few seconds.") { RetryAfterSeconds = 5 };
        }

        /// <summary>
        /// 502 ModelError with a generic message.
        /// </summary>
        /// <returns>Exception.</returns>
        public static ApiErrorException ModelError()
        {
            return new ApiErrorException(502, "ModelError", "The assistant could not answer. Try again.");
        }

        /// <summary>
        /// 504 ModelTimeout.
        /// </summary>
        /// <returns>Exception.</returns>
        public static ApiErrorException ModelTimeout()
        {
            return new ApiErrorException(504, "ModelTimeout", "The assistant took too long to answer. Try again.");
        }

        /// <summary>
        /// 400 EndpointKindMismatch for a purpose.
        /// </summary>
        /// <param name="field">Field path, for example models.embedding.</param>
        /// <param name="expected">Expected kind.</param>
        /// <returns>Exception.</returns>
        public static ApiErrorException KindMismatch(string field, string expected)
        {
            return new ApiErrorException(400, "EndpointKindMismatch", "An assistant purpose points at an endpoint of the wrong kind.",
                new Dictionary<string, string> { { field, "Must be a " + expected + " endpoint." } });
        }

        /// <summary>
        /// 400 AuthTypeNotSupported.
        /// </summary>
        /// <param name="apiType">API type.</param>
        /// <returns>Exception.</returns>
        public static ApiErrorException AuthTypeNotSupported(string apiType)
        {
            return new ApiErrorException(400, "AuthTypeNotSupported", "That authType is not valid for " + apiType + ".",
                new Dictionary<string, string> { { "authType", "Not valid for " + apiType + "." } });
        }

        /// <summary>
        /// 429 AssistantLimitReached with Retry-After until the next UTC midnight.
        /// </summary>
        /// <param name="nowUtc">Now.</param>
        /// <returns>Exception.</returns>
        public static ApiErrorException LimitReached(DateTime nowUtc)
        {
            int seconds = (int)Math.Ceiling((nowUtc.Date.AddDays(1) - nowUtc).TotalSeconds);
            return new ApiErrorException(429, "AssistantLimitReached", "You have used today's messages for this assistant. Try again tomorrow.") { RetryAfterSeconds = Math.Max(1, seconds) };
        }

        /// <summary>
        /// 503 BudgetExceeded: the monthly budget is spent and the hard stop is on.
        /// </summary>
        /// <returns>Exception.</returns>
        public static ApiErrorException BudgetExceeded()
        {
            return new ApiErrorException(503, "BudgetExceeded", "The assistant is paused for the rest of the month.");
        }

        #endregion
    }
}

namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Error body returned for all non-2xx responses.
    /// </summary>
    public class ErrorResponse
    {
        #region Public-Members

        /// <summary>
        /// Machine-readable error code, for example "NotFound" or "ValidationFailed".
        /// </summary>
        public string Error { get; set; } = String.Empty;

        /// <summary>
        /// Human-readable message.
        /// </summary>
        public string Message { get; set; } = String.Empty;

        /// <summary>
        /// Per-field validation errors keyed by field name. Null when not a validation error.
        /// </summary>
        public Dictionary<string, string>? FieldErrors { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate an empty error response.
        /// </summary>
        public ErrorResponse()
        {
        }

        /// <summary>
        /// Instantiate an error response.
        /// </summary>
        /// <param name="error">Error code.</param>
        /// <param name="message">Message.</param>
        /// <param name="fieldErrors">Optional per-field errors.</param>
        public ErrorResponse(string error, string message, Dictionary<string, string>? fieldErrors = null)
        {
            Error = error ?? String.Empty;
            Message = message ?? String.Empty;
            FieldErrors = fieldErrors;
        }

        #endregion
    }
}

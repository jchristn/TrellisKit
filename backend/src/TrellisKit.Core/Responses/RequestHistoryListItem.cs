namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Models;

    /// <summary>
    /// Request history entry as returned in list results (no headers or bodies).
    /// </summary>
    public class RequestHistoryListItem : IMetadataPayload
    {
        #region Public-Members

        /// <summary>
        /// Entry identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// User identifier, or null for anonymous requests.
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// Principal display name, or null.
        /// </summary>
        public string? PrincipalName { get; set; } = null;

        /// <summary>
        /// HTTP method.
        /// </summary>
        public string Method { get; set; } = String.Empty;

        /// <summary>
        /// Request path without the query string.
        /// </summary>
        public string Path { get; set; } = String.Empty;

        /// <summary>
        /// Request path including the query string.
        /// </summary>
        public string Url { get; set; } = String.Empty;

        /// <summary>
        /// Response status code.
        /// </summary>
        public int StatusCode { get; set; } = 0;

        /// <summary>
        /// Duration in milliseconds.
        /// </summary>
        public double DurationMs { get; set; } = 0;

        /// <summary>
        /// Client address, or null.
        /// </summary>
        public string? SourceIp { get; set; } = null;

        /// <summary>
        /// Original request body length in bytes.
        /// </summary>
        public long RequestBodyBytes { get; set; } = 0;

        /// <summary>
        /// Original response body length in bytes.
        /// </summary>
        public long ResponseBodyBytes { get; set; } = 0;

        /// <summary>
        /// UTC time the request began.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// UTC time the response completed, or null.
        /// </summary>
        public DateTime? CompletedUtc { get; set; } = null;

        /// <summary>
        /// Labels visible to the caller (staff: entries only for staff). Never null.
        /// </summary>
        public List<string> Labels
        {
            get { return _Labels; }
            set { _Labels = value ?? new List<string>(); }
        }

        /// <summary>
        /// Tags visible to the caller (staff: keys only for staff). Never null.
        /// </summary>
        public Dictionary<string, string> Tags
        {
            get { return _Tags; }
            set { _Tags = value ?? new Dictionary<string, string>(); }
        }

        #endregion

        #region Private-Members

        private List<string> _Labels = new List<string>();
        private Dictionary<string, string> _Tags = new Dictionary<string, string>();

        #endregion

        #region Public-Methods

        /// <summary>
        /// Create a list item from a full entry.
        /// </summary>
        /// <param name="entry">Entry.</param>
        /// <returns>List item.</returns>
        /// <exception cref="ArgumentNullException">The entry is null.</exception>
        public static RequestHistoryListItem FromModel(RequestHistoryEntry entry)
        {
            ArgumentNullException.ThrowIfNull(entry);
            return new RequestHistoryListItem
            {
                Id = entry.Id,
                UserId = entry.UserId,
                PrincipalName = entry.PrincipalName,
                Method = entry.Method,
                Path = entry.Path,
                Url = entry.Url,
                StatusCode = entry.StatusCode,
                DurationMs = entry.DurationMs,
                SourceIp = entry.SourceIp,
                RequestBodyBytes = entry.RequestBodyBytes,
                ResponseBodyBytes = entry.ResponseBodyBytes,
                CreatedUtc = entry.CreatedUtc,
                CompletedUtc = entry.CompletedUtc
            };
        }

        #endregion
    }
}

namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Captured HTTP request and response. Secrets are redacted from headers before capture.
    /// </summary>
    public class RequestHistoryEntry : IMetadataPayload
    {
        #region Public-Members

        /// <summary>Normalized resource route; variable path segments removed.</summary>
        public string MetricRoute { get; set; } = String.Empty;

        /// <summary>Bounded error code, never response content.</summary>
        public string MetricError { get; set; } = String.Empty;

        /// <summary>Client application header.</summary>
        public string MetricApp { get; set; } = String.Empty;

        /// <summary>Client platform.</summary>
        public string MetricPlatform { get; set; } = String.Empty;

        /// <summary>Client application version.</summary>
        public string MetricVersion { get; set; } = String.Empty;


        /// <summary>
        /// Entry identifier (prefix "req_").
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null or empty.</exception>
        public string Id
        {
            get
            {
                return _Id;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(Id));
                _Id = value;
            }
        }

        /// <summary>
        /// User identifier. Null for anonymous requests.
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// Principal display name, if resolved. Null for anonymous requests.
        /// </summary>
        public string? PrincipalName { get; set; } = null;

        /// <summary>
        /// HTTP method, for example GET.
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
        /// Request duration in milliseconds.
        /// </summary>
        public double DurationMs { get; set; } = 0;

        /// <summary>
        /// Client address as observed by the server. Null if unknown.
        /// </summary>
        public string? SourceIp { get; set; } = null;

        /// <summary>
        /// Request headers with secrets redacted. Never null.
        /// </summary>
        public Dictionary<string, string> RequestHeaders
        {
            get { return _RequestHeaders; }
            set { _RequestHeaders = value ?? new Dictionary<string, string>(); }
        }

        /// <summary>
        /// Request body, possibly truncated. Null when empty.
        /// </summary>
        public string? RequestBody { get; set; } = null;

        /// <summary>
        /// Original request body length in bytes, before truncation.
        /// </summary>
        public long RequestBodyBytes { get; set; } = 0;

        /// <summary>
        /// Whether the request body was truncated.
        /// </summary>
        public bool RequestBodyTruncated { get; set; } = false;

        /// <summary>
        /// Response headers with secrets redacted. Never null.
        /// </summary>
        public Dictionary<string, string> ResponseHeaders
        {
            get { return _ResponseHeaders; }
            set { _ResponseHeaders = value ?? new Dictionary<string, string>(); }
        }

        /// <summary>
        /// Response body, possibly truncated. Null when empty.
        /// </summary>
        public string? ResponseBody { get; set; } = null;

        /// <summary>
        /// Original response body length in bytes, before truncation.
        /// </summary>
        public long ResponseBodyBytes { get; set; } = 0;

        /// <summary>
        /// Whether the response body was truncated.
        /// </summary>
        public bool ResponseBodyTruncated { get; set; } = false;

        /// <summary>
        /// UTC time the request began.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// UTC time the response completed. Null if unknown.
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

        private string _Id = IdGenerator.GenerateRequestHistoryId();
        private Dictionary<string, string> _RequestHeaders = new Dictionary<string, string>();
        private Dictionary<string, string> _ResponseHeaders = new Dictionary<string, string>();

        #endregion
    }
}

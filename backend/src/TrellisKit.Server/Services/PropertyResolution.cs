namespace TrellisKit.Server.Services
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// The outcome of resolving one request to its property (docs/MULTIPROPERTY_CONTRACT.md section 2).
    /// </summary>
    public sealed class PropertyResolution
    {
        #region Public-Members

        /// <summary>
        /// The resolved property, or null for platform, special, and failed resolutions (special routes in
        /// SingleProperty mode carry the one property).
        /// </summary>
        public PropertyRuntime? Runtime { get; set; } = null;

        /// <summary>
        /// How the request was bound.
        /// </summary>
        public PropertyResolutionSourceEnum Source { get; set; } = PropertyResolutionSourceEnum.Special;

        /// <summary>
        /// The matched X-TK-App value, or null.
        /// </summary>
        public string? AppId { get; set; } = null;

        /// <summary>
        /// Whether the request is for the platform realm.
        /// </summary>
        public bool IsPlatform { get; set; } = false;

        /// <summary>
        /// HTTP status of a failed resolution, or 0 when resolved.
        /// </summary>
        public int StatusCode { get; set; } = 0;

        /// <summary>
        /// Error code of a failed resolution (PropertyNotFound, PropertyMismatch, AppNotRegistered, NotAvailable).
        /// </summary>
        public string? Error { get; set; } = null;

        /// <summary>
        /// Error message of a failed resolution.
        /// </summary>
        public string? Message { get; set; } = null;

        /// <summary>
        /// Telemetry outcome label.
        /// </summary>
        public string Outcome { get; set; } = "resolved";

        /// <summary>
        /// The property id stamped on request history, logs, and metrics: the property id, platform, or unresolved.
        /// </summary>
        public string PropertyLabel
        {
            get
            {
                if (Runtime != null) return Runtime.Id;
                return IsPlatform ? TrellisKit.Core.Helpers.PropertyIds.Platform : TrellisKit.Core.Helpers.PropertyIds.Unresolved;
            }
        }

        /// <summary>
        /// Whether resolution failed (the request is answered with <see cref="StatusCode"/>).
        /// </summary>
        public bool Failed
        {
            get { return StatusCode != 0; }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// A failed resolution.
        /// </summary>
        /// <param name="source">Source.</param>
        /// <param name="status">HTTP status.</param>
        /// <param name="error">Error code.</param>
        /// <param name="message">Message.</param>
        /// <param name="outcome">Telemetry outcome.</param>
        /// <returns>Resolution.</returns>
        public static PropertyResolution Fail(PropertyResolutionSourceEnum source, int status, string error, string message, string outcome)
        {
            return new PropertyResolution { Source = source, StatusCode = status, Error = error, Message = message, Outcome = outcome };
        }

        /// <summary>
        /// The standard 404 PropertyNotFound.
        /// </summary>
        /// <param name="source">Source.</param>
        /// <returns>Resolution.</returns>
        public static PropertyResolution NotFound(PropertyResolutionSourceEnum source)
        {
            return Fail(source, 404, "PropertyNotFound", "No enabled property matches this request.", "not_found");
        }

        #endregion
    }
}

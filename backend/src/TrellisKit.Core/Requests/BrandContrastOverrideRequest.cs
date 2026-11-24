namespace TrellisKit.Core.Requests
{
    using System;

    /// <summary>
    /// One contrast override of a branding update: save a pair below its WCAG 2.1 AA minimum (recorded as a warning in
    /// the audit trail).
    /// </summary>
    public class BrandContrastOverrideRequest
    {
        #region Public-Members

        /// <summary>
        /// Color scheme: light or dark.
        /// </summary>
        public string? Scheme { get; set; } = null;

        /// <summary>
        /// Pair identifier, for example onAccentSoft/accentSoft.
        /// </summary>
        public string? Pair { get; set; } = null;

        /// <summary>
        /// Reason, or null. At most 200 characters.
        /// </summary>
        public string? Reason { get; set; } = null;

        #endregion
    }
}

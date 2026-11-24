namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// GET and PUT /api/v1/admin/property/ai (and the platform routes of the same shape): the property's AI switch
    /// (docs/TRELLISKIT.md section 9.5).
    /// </summary>
    public class PropertyAiView
    {
        #region Public-Members

        /// <summary>
        /// Property id.
        /// </summary>
        public string PropertyId { get; set; } = String.Empty;

        /// <summary>
        /// Whether AI is switched on for the property.
        /// </summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// The deployment setting ai.enabled (read only here; platform or server settings).
        /// </summary>
        public bool DeploymentEnabled { get; set; } = false;

        /// <summary>
        /// Whether AI is available: enabled and deploymentEnabled. GET /api/v1/property features.ai reports this.
        /// </summary>
        public bool Available { get; set; } = false;

        /// <summary>
        /// The property.json value (features.ai) the switch is seeded from.
        /// </summary>
        public bool SeedEnabled { get; set; } = false;

        /// <summary>
        /// Whether a changed property.json features.ai replaces the switch on the next start (SingleProperty folder).
        /// </summary>
        public bool SyncsFromFolder { get; set; } = false;

        /// <summary>
        /// Revision, bumped on every change.
        /// </summary>
        public int Revision { get; set; } = 1;

        /// <summary>
        /// Who made the last change, or null.
        /// </summary>
        public string? UpdatedByUserId { get; set; } = null;

        /// <summary>
        /// When the switch last changed (UTC).
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}

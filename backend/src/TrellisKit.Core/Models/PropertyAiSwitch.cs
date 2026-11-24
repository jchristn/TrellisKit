namespace TrellisKit.Core.Models
{
    using System;

    /// <summary>
    /// A property's stored AI switch (table propertyai; docs/TRELLISKIT.md section 9.5): whether the property offers
    /// AI models and assistants. Seeded from property.json features.ai (alias features.assistants), then switched by
    /// property administrators. AI is available when this switch and the deployment setting ai.enabled are both on.
    /// Thread safety: safe to read concurrently when not mutated.
    /// </summary>
    public class PropertyAiSwitch
    {
        #region Public-Members

        /// <summary>
        /// Whether AI is on for the property. Default false.
        /// </summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// Hash of the property.json switch the row was last seeded from (SingleProperty compares it to decide whether
        /// the folder changed), or null.
        /// </summary>
        public string? SourceHash { get; set; } = null;

        /// <summary>
        /// Revision, bumped on every change. Default 1. Minimum 1.
        /// </summary>
        public int Revision
        {
            get { return _Revision; }
            set { _Revision = Math.Max(1, value); }
        }

        /// <summary>
        /// Member or platform administrator who made the last change, or null for seeding and the break-glass token.
        /// </summary>
        public string? UpdatedByUserId { get; set; } = null;

        /// <summary>
        /// When the row was created (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// When the row last changed (UTC).
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private int _Revision = 1;

        #endregion

        #region Public-Methods

        /// <summary>
        /// The switch a property document seeds.
        /// </summary>
        /// <param name="enabled">The document's features.ai.</param>
        /// <returns>Switch (SourceHash set).</returns>
        public static PropertyAiSwitch FromDocument(bool enabled)
        {
            return new PropertyAiSwitch { Enabled = enabled, SourceHash = DocumentHash(enabled) };
        }

        /// <summary>
        /// The source hash of a document value.
        /// </summary>
        /// <param name="enabled">The document's features.ai.</param>
        /// <returns>Hash text.</returns>
        public static string DocumentHash(bool enabled)
        {
            return enabled ? "features.ai=true" : "features.ai=false";
        }

        /// <summary>
        /// A copy.
        /// </summary>
        /// <returns>Copy.</returns>
        public PropertyAiSwitch Clone()
        {
            return new PropertyAiSwitch { Enabled = Enabled, SourceHash = SourceHash, Revision = Revision, UpdatedByUserId = UpdatedByUserId, CreatedUtc = CreatedUtc, UpdatedUtc = UpdatedUtc };
        }

        #endregion
    }
}

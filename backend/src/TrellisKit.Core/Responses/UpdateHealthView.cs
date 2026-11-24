namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// GET /api/v1/admin/updates/health.
    /// </summary>
    public class UpdateHealthView
    {
        /// <summary>
        /// Setting updates.enabled.
        /// </summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// Active or Missing.
        /// </summary>
        public string Signing { get; set; } = String.Empty;

        /// <summary>
        /// Key id of the signing key, or null.
        /// </summary>
        public string? ActiveKeyId { get; set; } = null;

        /// <summary>
        /// Why signing is unavailable, or null.
        /// </summary>
        public string? SigningProblem { get; set; } = null;

        /// <summary>
        /// Ok or Error.
        /// </summary>
        public string Storage { get; set; } = String.Empty;

        /// <summary>
        /// Local or S3.
        /// </summary>
        public string StorageMode { get; set; } = String.Empty;

        /// <summary>
        /// Setting updates.rolloutPlanTemplate.
        /// </summary>
        public string RolloutPlanTemplate { get; set; } = String.Empty;

    }
}

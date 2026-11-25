namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// POST /api/v1/admin/curricula and PUT /api/v1/admin/curricula/{id}.
    /// </summary>
    public class CurriculumRequest
    {
        #region Public-Members

        /// <summary>
        /// Name (1 to 120 characters).
        /// </summary>
        public string? Name { get; set; } = null;

        /// <summary>
        /// Description (at most 2000 characters).
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Rank profile field key whose options group the items, or null.
        /// </summary>
        public string? FieldKey { get; set; } = null;

        /// <summary>
        /// Members (default), Public, or Hidden.
        /// </summary>
        public string? Visibility { get; set; } = null;

        /// <summary>
        /// Sort position (0 to 100000).
        /// </summary>
        public int? Position { get; set; } = null;

        /// <summary>
        /// Labels.
        /// </summary>
        public List<string>? Labels { get; set; } = null;

        /// <summary>
        /// Tags.
        /// </summary>
        public Dictionary<string, string>? Tags { get; set; } = null;

        #endregion
    }
}

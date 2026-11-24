namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A model reported by the provider.
    /// </summary>
    public class ProviderModelView
    {
        #region Public-Members

        /// <summary>
        /// Name.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Display name.
        /// </summary>
        public string? DisplayName { get; set; } = null;

        /// <summary>
        /// Description.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Owner.
        /// </summary>
        public string? OwnedBy { get; set; } = null;

        /// <summary>
        /// Created.
        /// </summary>
        public DateTime? CreatedUtc { get; set; } = null;

        /// <summary>
        /// Size.
        /// </summary>
        public long? SizeBytes { get; set; } = null;

        /// <summary>
        /// Input limit.
        /// </summary>
        public int? InputTokenLimit { get; set; } = null;

        /// <summary>
        /// Output limit.
        /// </summary>
        public int? OutputTokenLimit { get; set; } = null;

        /// <summary>
        /// Tool support.
        /// </summary>
        public bool? SupportsTools { get; set; } = null;

        /// <summary>
        /// Labels.
        /// </summary>
        public List<string> Labels { get; set; } = new List<string>();

        /// <summary>
        /// Tags.
        /// </summary>
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();

        #endregion
    }
}

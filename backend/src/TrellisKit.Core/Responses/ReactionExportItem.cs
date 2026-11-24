namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// A reaction in the account export (contract section 11).
    /// </summary>
    public class ReactionExportItem
    {
        #region Public-Members

        /// <summary>
        /// Target type.
        /// </summary>
        public string TargetType { get; set; } = String.Empty;

        /// <summary>
        /// Target identifier.
        /// </summary>
        public string TargetId { get; set; } = String.Empty;

        /// <summary>
        /// Key.
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// When.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}

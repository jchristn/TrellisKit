namespace TrellisKit.Server.Services.Documents
{
    using System;

    /// <summary>
    /// A gating document the authorization stage checks: members must have accepted at least
    /// <see cref="RequiredSequence"/> of <see cref="Key"/>.
    /// </summary>
    public class DocumentRequirement
    {
        #region Public-Members

        /// <summary>
        /// Document key.
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// Lowest accepted sequence that satisfies the requirement (at least 1).
        /// </summary>
        public int RequiredSequence { get; set; } = 1;

        #endregion
    }
}

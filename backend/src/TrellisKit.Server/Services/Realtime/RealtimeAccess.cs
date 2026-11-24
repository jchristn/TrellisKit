namespace TrellisKit.Server.Services.Realtime
{
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// The capabilities a session may hold on the socket right now (docs/PHASE5_CONTRACT.md sections 0.3 and 4.2), from
    /// <see cref="RealtimeModule.EvaluateAccessAsync"/>.
    /// Thread safety: not thread safe; a snapshot.
    /// </summary>
    public sealed class RealtimeAccess
    {
        #region Public-Members

        /// <summary>
        /// Allowed capabilities (notifications and tickets always; messaging when allowed). Never null.
        /// </summary>
        public List<string> Capabilities { get; set; } = new List<string>();

        /// <summary>
        /// The session's access tier (welcome and access.changed).
        /// </summary>
        public AccessTierEnum AccessTier { get; set; } = AccessTierEnum.SignedIn;

        /// <summary>
        /// Whether the messaging kill switch is on for this caller.
        /// </summary>
        public bool MessagingKilled { get; set; } = false;

        /// <summary>
        /// The messaging kill switch message, or null.
        /// </summary>
        public string? MessagingKilledMessage { get; set; } = null;

        #endregion
    }
}

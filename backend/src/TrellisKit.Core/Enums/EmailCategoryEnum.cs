namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Category of an administrative email (docs/TRELLISKIT.md section 12.3).
    /// </summary>
    public enum EmailCategoryEnum
    {
        /// <summary>
        /// Account, security, billing, and moderation notices; ignore marketing preferences, never sent to suppressed addresses.
        /// </summary>
        Essential,

        /// <summary>
        /// Governed by notification preferences; carries one-click unsubscribe headers.
        /// </summary>
        Optional,

        /// <summary>
        /// Staff notifications with per-event toggles and recipients.
        /// </summary>
        Staff
    }
}

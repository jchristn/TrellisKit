namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Who an email template is addressed to.
    /// </summary>
    public enum EmailRecipientEnum
    {
        /// <summary>
        /// The member the event is about.
        /// </summary>
        Member,

        /// <summary>
        /// Configurable staff recipients (default: the property's SystemAdmins).
        /// </summary>
        Staff,

        /// <summary>
        /// SystemAdmins and moderators.
        /// </summary>
        Moderators,

        /// <summary>
        /// Platform administrators (deployment-level).
        /// </summary>
        PlatformAdmins
    }
}

namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Kind of authenticated principal.
    /// </summary>
    public enum PrincipalKindEnum
    {
        /// <summary>
        /// No credentials.
        /// </summary>
        Anonymous,

        /// <summary>
        /// A member of the resolved property.
        /// </summary>
        Member,

        /// <summary>
        /// A platform administrator on a platform host.
        /// </summary>
        PlatformAdmin,

        /// <summary>
        /// A platform administrator acting as SystemAdmin inside one property through a short-lived, audited delegated session.
        /// </summary>
        Delegated,

        /// <summary>
        /// The break-glass admin token.
        /// </summary>
        BreakGlass
    }
}

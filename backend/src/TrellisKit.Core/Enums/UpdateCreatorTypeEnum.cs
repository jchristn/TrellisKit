namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Credential that created a live update group.
    /// </summary>
    public enum UpdateCreatorTypeEnum
    {
        /// <summary>
        /// A SystemAdmin member session.
        /// </summary>
        Member,

        /// <summary>
        /// The break-glass admin token (or a delegated platform administrator).
        /// </summary>
        AdminToken,

        /// <summary>
        /// An update deploy key (CI).
        /// </summary>
        DeployKey,

        /// <summary>
        /// The server (automatic actions).
        /// </summary>
        System
    }
}

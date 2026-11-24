namespace TrellisKit.Core.Responses
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A role assignment as returned to clients.
    /// </summary>
    public class RoleView
    {
        #region Public-Members

        /// <summary>
        /// Role.
        /// </summary>
        public RoleEnum Role { get; set; } = RoleEnum.User;

        /// <summary>
        /// Resource type.
        /// </summary>
        public ResourceTypeEnum ResourceType { get; set; } = ResourceTypeEnum.Global;

        /// <summary>
        /// Resource identifier or *.
        /// </summary>
        public string ResourceId { get; set; } = "*";

        #endregion
    }
}

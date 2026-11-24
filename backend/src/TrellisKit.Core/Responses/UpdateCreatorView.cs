namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Who created an update group.
    /// </summary>
    public class UpdateCreatorView
    {
        /// <summary>
        /// Credential type.
        /// </summary>
        public UpdateCreatorTypeEnum Type { get; set; } = UpdateCreatorTypeEnum.Member;

        /// <summary>
        /// Member or deploy key id, or null.
        /// </summary>
        public string? Id { get; set; } = null;

        /// <summary>
        /// Display name.
        /// </summary>
        public string Name { get; set; } = String.Empty;
    }
}

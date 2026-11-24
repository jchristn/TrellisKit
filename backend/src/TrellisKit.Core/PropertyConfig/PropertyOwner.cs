namespace TrellisKit.Core.PropertyConfig
{
    using System;

    /// <summary>
    /// The person or organization that runs the property.
    /// </summary>
    public class PropertyOwner
    {
        #region Public-Members

        /// <summary>
        /// Owner display name.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Legal copyright holder shown in copyright notices (for example a company), or null to use <see cref="Name"/>.
        /// </summary>
        public string? CopyrightHolder { get; set; } = null;

        #endregion
    }
}

namespace TrellisKit.Core.PropertyConfig
{
    using System;

    /// <summary>
    /// Words the clients use for members and the community.
    /// </summary>
    public class PropertyTerminology
    {
        #region Public-Members

        /// <summary>
        /// Singular member noun.
        /// </summary>
        public string Member { get; set; } = "member";

        /// <summary>
        /// Plural member noun.
        /// </summary>
        public string Members { get; set; } = "members";

        /// <summary>
        /// Community noun.
        /// </summary>
        public string Community { get; set; } = "community";

        #endregion
    }
}

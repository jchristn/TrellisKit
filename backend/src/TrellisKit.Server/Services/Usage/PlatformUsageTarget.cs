namespace TrellisKit.Server.Services.Usage
{
    using TrellisKit.Core.Models;

    /// <summary>
    /// A registered property and its usage service, resolved for a platform route.
    /// </summary>
    public sealed class PlatformUsageTarget
    {
        #region Public-Members

        /// <summary>
        /// Registry record.
        /// </summary>
        public PropertyRecord Record { get; set; } = new PropertyRecord();

        /// <summary>
        /// Usage service of the property.
        /// </summary>
        public UsageService Usage { get; set; } = null!;

        #endregion
    }
}

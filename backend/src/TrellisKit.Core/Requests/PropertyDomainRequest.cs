namespace TrellisKit.Core.Requests
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A domain in a property create or update body.
    /// </summary>
    public class PropertyDomainRequest
    {
        #region Public-Members

        /// <summary>
        /// Host name (lowercased, port removed).
        /// </summary>
        public string Host { get; set; } = String.Empty;

        /// <summary>
        /// Role of the host.
        /// </summary>
        public DomainKindEnum Kind { get; set; } = DomainKindEnum.Web;

        #endregion
    }
}

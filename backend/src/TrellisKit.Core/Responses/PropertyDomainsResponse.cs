namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// GET and PUT /api/v1/admin/property/domains: the property's domains (at most 20) and apps (at most 10), in the platform
    /// PropertyRecord shapes.
    /// </summary>
    public class PropertyDomainsResponse
    {
        #region Public-Members

        /// <summary>
        /// Property id.
        /// </summary>
        public string PropertyId { get; set; } = String.Empty;

        /// <summary>
        /// Domains.
        /// </summary>
        public List<PlatformDomainResponse> Domains
        {
            get { return _Domains; }
            set { _Domains = value ?? new List<PlatformDomainResponse>(); }
        }

        /// <summary>
        /// Apps.
        /// </summary>
        public List<PlatformAppResponse> Apps
        {
            get { return _Apps; }
            set { _Apps = value ?? new List<PlatformAppResponse>(); }
        }

        #endregion

        #region Private-Members

        private List<PlatformDomainResponse> _Domains = new List<PlatformDomainResponse>();
        private List<PlatformAppResponse> _Apps = new List<PlatformAppResponse>();

        #endregion
    }
}

namespace TrellisKit.Core.Settings
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Link preview settings (contract section 5.9).
    /// </summary>
    public class LinkPreviewSettings
    {
        #region Public-Members

        /// <summary>
        /// Build preview cards for the first link in posts and comments. Default true.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Connect and total timeout of one preview fetch. Default 5. Minimum 1, maximum 30.
        /// </summary>
        public int TimeoutSeconds
        {
            get { return _TimeoutSeconds; }
            set { _TimeoutSeconds = Math.Clamp(value, 1, 30); }
        }

        /// <summary>
        /// Redirect hops followed (each one checked again). Default 3. Minimum 0, maximum 10.
        /// </summary>
        public int MaxRedirects
        {
            get { return _MaxRedirects; }
            set { _MaxRedirects = Math.Clamp(value, 0, 10); }
        }

        /// <summary>
        /// HTML read per page. Default 1048576. Minimum 65536, maximum 10485760.
        /// </summary>
        public int MaxHtmlBytes
        {
            get { return _MaxHtmlBytes; }
            set { _MaxHtmlBytes = Math.Clamp(value, 65536, 10485760); }
        }

        /// <summary>
        /// Largest preview image fetched. Default 5242880. Minimum 65536, maximum 20971520.
        /// </summary>
        public int MaxImageBytes
        {
            get { return _MaxImageBytes; }
            set { _MaxImageBytes = Math.Clamp(value, 65536, 20971520); }
        }

        /// <summary>
        /// How long a successful preview is reused. Default 168. Minimum 1, maximum 8760.
        /// </summary>
        public int CacheHours
        {
            get { return _CacheHours; }
            set { _CacheHours = Math.Clamp(value, 1, 8760); }
        }

        /// <summary>
        /// How long a failed fetch is remembered. Default 60. Minimum 1, maximum 10080.
        /// </summary>
        public int FailureCacheMinutes
        {
            get { return _FailureCacheMinutes; }
            set { _FailureCacheMinutes = Math.Clamp(value, 1, 10080); }
        }

        /// <summary>
        /// Comma-separated host suffixes that are never fetched. Default empty. Never null.
        /// </summary>
        public List<string> BlockedDomains
        {
            get { return _BlockedDomains; }
            set { _BlockedDomains = value ?? new List<string>(); }
        }

        /// <summary>
        /// User-Agent header of preview fetches. Default "TrellisKitLinkPreview/1.0". Never null.
        /// </summary>
        public string UserAgent
        {
            get { return _UserAgent; }
            set { _UserAgent = value ?? String.Empty; }
        }

        /// <summary>
        /// Preview requests a member may make each hour. Default 60. Minimum 1, maximum 10000.
        /// </summary>
        public int PerHour
        {
            get { return _PerHour; }
            set { _PerHour = Math.Clamp(value, 1, 10000); }
        }

        /// <summary>
        /// Outbound fetches to one host each minute, across all members. Default 10. Minimum 1, maximum 1000.
        /// </summary>
        public int PerHostPerMinute
        {
            get { return _PerHostPerMinute; }
            set { _PerHostPerMinute = Math.Clamp(value, 1, 1000); }
        }

        #endregion

        #region Private-Members

        private int _TimeoutSeconds = 5;
        private int _MaxRedirects = 3;
        private int _MaxHtmlBytes = 1048576;
        private int _MaxImageBytes = 5242880;
        private int _CacheHours = 168;
        private int _FailureCacheMinutes = 60;
        private List<string> _BlockedDomains = new List<string>();
        private string _UserAgent = "TrellisKitLinkPreview/1.0";
        private int _PerHour = 60;
        private int _PerHostPerMinute = 10;

        #endregion
    }
}

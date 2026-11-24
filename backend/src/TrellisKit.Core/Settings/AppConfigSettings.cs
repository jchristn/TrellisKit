namespace TrellisKit.Core.Settings
{
    using System;

    /// <summary>
    /// Server-side feature flags, kill switches, and the app content manifest (docs/LIVE_UPDATES_CONTRACT.md sections 11 to 16).
    /// </summary>
    public class AppConfigSettings
    {
        #region Public-Members

        /// <summary>
        /// Most feature flags (not counting kill switches) per property. Default 200. Minimum 10, maximum 1000.
        /// </summary>
        public int MaxFlags
        {
            get { return _MaxFlags; }
            set { _MaxFlags = Math.Clamp(value, 10, 1000); }
        }

        /// <summary>
        /// Most targeting rules per flag or kill switch. Default 20. Minimum 1, maximum 100.
        /// </summary>
        public int MaxRulesPerFlag
        {
            get { return _MaxRulesPerFlag; }
            set { _MaxRulesPerFlag = Math.Clamp(value, 1, 100); }
        }

        /// <summary>
        /// Most conditions per rule. Default 10. Minimum 1, maximum 50.
        /// </summary>
        public int MaxConditionsPerRule
        {
            get { return _MaxConditionsPerRule; }
            set { _MaxConditionsPerRule = Math.Clamp(value, 1, 50); }
        }

        /// <summary>
        /// Most values per condition. Default 100. Minimum 1, maximum 1000.
        /// </summary>
        public int MaxValuesPerCondition
        {
            get { return _MaxValuesPerCondition; }
            set { _MaxValuesPerCondition = Math.Clamp(value, 1, 1000); }
        }

        /// <summary>
        /// How often clients re-fetch the app manifest while in the foreground. Default 15. Minimum 1, maximum 1440.
        /// </summary>
        public int CheckIntervalMinutes
        {
            get { return _CheckIntervalMinutes; }
            set { _CheckIntervalMinutes = Math.Clamp(value, 1, 1440); }
        }

        /// <summary>
        /// Seconds a task may serve cached published content before re-reading the content state. Default 5. Minimum 1, maximum 60.
        /// </summary>
        public int PropagationSeconds
        {
            get { return _PropagationSeconds; }
            set { _PropagationSeconds = Math.Clamp(value, 1, 60); }
        }

        /// <summary>
        /// Seconds a task may serve cached kill switches before re-reading their revision. Default 10. Minimum 1, maximum 60.
        /// </summary>
        public int KillSwitchPropagationSeconds
        {
            get { return _KillSwitchPropagationSeconds; }
            set { _KillSwitchPropagationSeconds = Math.Clamp(value, 1, 60); }
        }

        /// <summary>
        /// Content versions whose change log rows are kept; clients further behind get fullRefresh. Default 200. Minimum 10, maximum 10000.
        /// </summary>
        public int ChangeLogRetentionVersions
        {
            get { return _ChangeLogRetentionVersions; }
            set { _ChangeLogRetentionVersions = Math.Clamp(value, 10, 10000); }
        }

        /// <summary>
        /// When true, member API routes return 426 ClientUpdateRequired to mobile callers below mobile.minimumBinaryVersion. Default false.
        /// </summary>
        public bool EnforceMinimumOnApi { get; set; } = false;

        /// <summary>
        /// App manifest requests per install or visitor id each hour. Default 120. Minimum 1, maximum 10000.
        /// </summary>
        public int ManifestPerClientPerHour
        {
            get { return _ManifestPerClientPerHour; }
            set { _ManifestPerClientPerHour = Math.Clamp(value, 1, 10000); }
        }

        /// <summary>
        /// App manifest requests per IP address each hour. Default 1200. Minimum 1, maximum 100000.
        /// </summary>
        public int ManifestPerIpPerHour
        {
            get { return _ManifestPerIpPerHour; }
            set { _ManifestPerIpPerHour = Math.Clamp(value, 1, 100000); }
        }


        /// <summary>
        /// Remote string overrides per property (drafts and published). Default 2000. Minimum 10, maximum 20000.
        /// </summary>
        public int MaxStringOverrides
        {
            get { return _MaxStringOverrides; }
            set { _MaxStringOverrides = Math.Clamp(value, 10, 20000); }
        }

        /// <summary>
        /// Banners per property, not counting archived ones. Default 100. Minimum 1, maximum 1000.
        /// </summary>
        public int MaxBanners
        {
            get { return _MaxBanners; }
            set { _MaxBanners = Math.Clamp(value, 1, 1000); }
        }

        /// <summary>
        /// Active banners one app manifest carries. Default 20. Minimum 1, maximum 100.
        /// </summary>
        public int MaxActiveBanners
        {
            get { return _MaxActiveBanners; }
            set { _MaxActiveBanners = Math.Clamp(value, 1, 100); }
        }

        /// <summary>
        /// Remote assets per property, not counting archived ones. Default 200. Minimum 1, maximum 2000.
        /// </summary>
        public int MaxRemoteAssets
        {
            get { return _MaxRemoteAssets; }
            set { _MaxRemoteAssets = Math.Clamp(value, 1, 2000); }
        }

        /// <summary>
        /// Largest remote asset upload. Default 10. Minimum 1, maximum 50.
        /// </summary>
        public int MaxAssetMegabytes
        {
            get { return _MaxAssetMegabytes; }
            set { _MaxAssetMegabytes = Math.Clamp(value, 1, 50); }
        }

        /// <summary>
        /// Changed strings, banners, and assets an app manifest delta may carry before it answers fullRefresh. Default 500. Minimum 50, maximum 5000.
        /// </summary>
        public int MaxDeltaItems
        {
            get { return _MaxDeltaItems; }
            set { _MaxDeltaItems = Math.Clamp(value, 50, 5000); }
        }

        /// <summary>
        /// Content re-enumeration requests per install or visitor id each hour. Default 60. Minimum 1, maximum 10000.
        /// </summary>
        public int ContentEnumeratePerHour
        {
            get { return _ContentEnumeratePerHour; }
            set { _ContentEnumeratePerHour = Math.Clamp(value, 1, 10000); }
        }

        #endregion

        #region Private-Members

        private int _MaxFlags = 200;
        private int _MaxRulesPerFlag = 20;
        private int _MaxConditionsPerRule = 10;
        private int _MaxValuesPerCondition = 100;
        private int _CheckIntervalMinutes = 15;
        private int _PropagationSeconds = 5;
        private int _KillSwitchPropagationSeconds = 10;
        private int _ChangeLogRetentionVersions = 200;
        private int _ManifestPerClientPerHour = 120;
        private int _ManifestPerIpPerHour = 1200;
        private int _MaxStringOverrides = 2000;
        private int _MaxBanners = 100;
        private int _MaxActiveBanners = 20;
        private int _MaxRemoteAssets = 200;
        private int _MaxAssetMegabytes = 10;
        private int _MaxDeltaItems = 500;
        private int _ContentEnumeratePerHour = 60;

        #endregion
    }
}

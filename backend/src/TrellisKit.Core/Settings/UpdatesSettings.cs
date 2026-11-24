namespace TrellisKit.Core.Settings
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Live updates: storage, signing keys, limits, retention, rollout automation, and rate limits (docs/LIVE_UPDATES_CONTRACT.md section 22).
    /// </summary>
    public class UpdatesSettings
    {
        #region Public-Members

        /// <summary>
        /// Whether the manifest endpoint serves updates. When false it answers every check with a signed noUpdateAvailable directive. Default true.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Where update assets are stored: Local (under storage.directory/updates) or S3 (updates.bucket). Default Local.
        /// </summary>
        public MediaStorageModeEnum StorageMode { get; set; } = MediaStorageModeEnum.Local;

        /// <summary>
        /// S3 bucket of update assets (S3 mode). Keys mirror URL paths: updates/assets/{propertyId}/{sha256}. Default empty.
        /// </summary>
        public string Bucket
        {
            get { return _Bucket; }
            set { _Bucket = (value ?? String.Empty).Trim(); }
        }

        /// <summary>
        /// Region of the updates bucket. Empty uses media.s3Region. Default empty.
        /// </summary>
        public string S3Region
        {
            get { return _S3Region; }
            set { _S3Region = (value ?? String.Empty).Trim(); }
        }

        /// <summary>
        /// Absolute public base URL of the updates path, for example https://media.example.test/updates; asset URLs are {publicBaseUrl}/assets/{propertyId}/{sha256}. Empty serves assets from this API (GET /api/v1/updates/assets/...). Default empty.
        /// </summary>
        public string PublicBaseUrl
        {
            get { return _PublicBaseUrl; }
            set { _PublicBaseUrl = (value ?? String.Empty).Trim(); }
        }

        /// <summary>
        /// PEM (PKCS#8 or PKCS#1) RSA private key of the deployment signing key (single-property deployments). Default empty. Never logged.
        /// </summary>
        public string SigningKeyPem
        {
            get { return _SigningKeyPem; }
            set { _SigningKeyPem = value ?? String.Empty; }
        }

        /// <summary>
        /// Path of a PEM file holding the deployment signing key; used when SigningKeyPem is empty. Default empty.
        /// </summary>
        public string SigningKeyFile
        {
            get { return _SigningKeyFile; }
            set { _SigningKeyFile = (value ?? String.Empty).Trim(); }
        }

        /// <summary>
        /// Key id of the deployment signing key (the keyid builds embed for it). Default main.
        /// </summary>
        public string SigningKeyId
        {
            get { return _SigningKeyId; }
            set { _SigningKeyId = (value ?? String.Empty).Trim(); }
        }

        /// <summary>
        /// PEM RSA private key of the deployment standby key (key rotation without a new binary). Default empty. Never logged.
        /// </summary>
        public string StandbySigningKeyPem
        {
            get { return _StandbySigningKeyPem; }
            set { _StandbySigningKeyPem = value ?? String.Empty; }
        }

        /// <summary>
        /// Path of a PEM file holding the standby key; used when StandbySigningKeyPem is empty. Default empty.
        /// </summary>
        public string StandbySigningKeyFile
        {
            get { return _StandbySigningKeyFile; }
            set { _StandbySigningKeyFile = (value ?? String.Empty).Trim(); }
        }

        /// <summary>
        /// Key id of the deployment standby key. Default standby.
        /// </summary>
        public string StandbySigningKeyId
        {
            get { return _StandbySigningKeyId; }
            set { _StandbySigningKeyId = (value ?? String.Empty).Trim(); }
        }

        /// <summary>
        /// Minimum minutes between foreground update checks in the app (property updates.checkIntervalMinutes overrides it). Default 15. Minimum 1, maximum 1440.
        /// </summary>
        public int CheckIntervalMinutes
        {
            get { return _CheckIntervalMinutes; }
            set { _CheckIntervalMinutes = Math.Clamp(value, 1, 1440); }
        }

        /// <summary>
        /// Most assets one update may declare. Default 2000. Minimum 10, maximum 10000.
        /// </summary>
        public int MaxAssetsPerUpdate
        {
            get { return _MaxAssetsPerUpdate; }
            set { _MaxAssetsPerUpdate = Math.Clamp(value, 10, 10000); }
        }

        /// <summary>
        /// Largest single asset or source map, in megabytes. Default 50. Minimum 1, maximum 500.
        /// </summary>
        public int MaxAssetMegabytes
        {
            get { return _MaxAssetMegabytes; }
            set { _MaxAssetMegabytes = Math.Clamp(value, 1, 500); }
        }

        /// <summary>
        /// Largest upload request, in megabytes. Default 500. Minimum 10, maximum 5000.
        /// </summary>
        public int MaxUploadMegabytes
        {
            get { return _MaxUploadMegabytes; }
            set { _MaxUploadMegabytes = Math.Clamp(value, 10, 5000); }
        }

        /// <summary>
        /// Most custom channels per property, beyond production, preview, and development. Default 10. Minimum 3, maximum 50.
        /// </summary>
        public int MaxChannels
        {
            get { return _MaxChannels; }
            set { _MaxChannels = Math.Clamp(value, 3, 50); }
        }

        /// <summary>
        /// Days an install row is kept after its last check. Default 90. Minimum 7, maximum 3650.
        /// </summary>
        public int InstallRetentionDays
        {
            get { return _InstallRetentionDays; }
            set { _InstallRetentionDays = Math.Clamp(value, 7, 3650); }
        }

        /// <summary>
        /// Days client events are kept (daily counts are kept with the update). Default 30. Minimum 1, maximum 365.
        /// </summary>
        public int EventRetentionDays
        {
            get { return _EventRetentionDays; }
            set { _EventRetentionDays = Math.Clamp(value, 1, 365); }
        }

        /// <summary>
        /// Launch failure share that raises the alarm metric and is the default automatic halt threshold. Default 5. Minimum 1, maximum 100.
        /// </summary>
        public int LaunchFailureAlarmPercent
        {
            get { return _LaunchFailureAlarmPercent; }
            set { _LaunchFailureAlarmPercent = Math.Clamp(value, 1, 100); }
        }

        /// <summary>
        /// Installs (successes plus failures) an update needs before the automatic halt judges its failure rate. Default 20. Minimum 1, maximum 100000.
        /// </summary>
        public int AutoHaltMinInstalls
        {
            get { return _AutoHaltMinInstalls; }
            set { _AutoHaltMinInstalls = Math.Clamp(value, 1, 100000); }
        }

        /// <summary>
        /// Rollout plan template offered in the admin console: comma-separated percent@hoursAfterPublish stages. Default 5@0,25@24,100@72.
        /// </summary>
        public string RolloutPlanTemplate
        {
            get { return _RolloutPlanTemplate; }
            set { _RolloutPlanTemplate = (value ?? String.Empty).Trim(); }
        }

        /// <summary>
        /// Seconds between passes of the worker that applies rollout stages and automatic halts. Default 60. Minimum 10, maximum 3600.
        /// </summary>
        public int RolloutWorkerSeconds
        {
            get { return _RolloutWorkerSeconds; }
            set { _RolloutWorkerSeconds = Math.Clamp(value, 10, 3600); }
        }

        /// <summary>
        /// Manifest requests per install id each hour. Default 60. Minimum 1, maximum 10000.
        /// </summary>
        public int ChecksPerInstallPerHour
        {
            get { return _ChecksPerInstallPerHour; }
            set { _ChecksPerInstallPerHour = Math.Clamp(value, 1, 10000); }
        }

        /// <summary>
        /// Manifest requests per IP address each hour. Default 600. Minimum 1, maximum 100000.
        /// </summary>
        public int ChecksPerIpPerHour
        {
            get { return _ChecksPerIpPerHour; }
            set { _ChecksPerIpPerHour = Math.Clamp(value, 1, 100000); }
        }

        /// <summary>
        /// Client event batches per install id each hour. Default 30. Minimum 1, maximum 10000.
        /// </summary>
        public int EventsPerInstallPerHour
        {
            get { return _EventsPerInstallPerHour; }
            set { _EventsPerInstallPerHour = Math.Clamp(value, 1, 10000); }
        }


        /// <summary>
        /// Days an update asset no update references is kept before UpdateAssetGcWorker deletes it. Default 30. Minimum 1, maximum 3650.
        /// </summary>
        public int AssetGcRetentionDays
        {
            get { return _AssetGcRetentionDays; }
            set { _AssetGcRetentionDays = Math.Clamp(value, 1, 3650); }
        }

        /// <summary>
        /// Assets UpdateAssetGcWorker deletes per batch. Default 200. Minimum 10, maximum 5000.
        /// </summary>
        public int AssetGcBatchSize
        {
            get { return _AssetGcBatchSize; }
            set { _AssetGcBatchSize = Math.Clamp(value, 10, 5000); }
        }

        /// <summary>
        /// Hours between UpdateAssetGcWorker passes. Default 168. Minimum 1, maximum 720.
        /// </summary>
        public int AssetGcIntervalHours
        {
            get { return _AssetGcIntervalHours; }
            set { _AssetGcIntervalHours = Math.Clamp(value, 1, 720); }
        }

        /// <summary>
        /// Largest source map the symbolication route reads. Default 100. Minimum 1, maximum 1000.
        /// </summary>
        public int MaxSourceMapMegabytes
        {
            get { return _MaxSourceMapMegabytes; }
            set { _MaxSourceMapMegabytes = Math.Clamp(value, 1, 1000); }
        }

        /// <summary>
        /// Absolute public base URL of remote content assets, for example https://api.example.test/app-content (asset URLs are {base}/{propertyId}/{sha256}). Empty serves them from this API (GET /api/v1/app/content-assets/...). Default empty.
        /// </summary>
        public string ContentAssetBaseUrl
        {
            get { return _ContentAssetBaseUrl; }
            set { _ContentAssetBaseUrl = (value ?? String.Empty).Trim().TrimEnd('/'); }
        }

        #endregion

        #region Private-Members

        private int _AssetGcRetentionDays = 30;
        private int _AssetGcBatchSize = 200;
        private int _AssetGcIntervalHours = 168;
        private int _MaxSourceMapMegabytes = 100;
        private string _ContentAssetBaseUrl = String.Empty;
        private string _Bucket = "";
        private string _S3Region = "";
        private string _PublicBaseUrl = "";
        private string _SigningKeyPem = String.Empty;
        private string _SigningKeyFile = "";
        private string _SigningKeyId = "main";
        private string _StandbySigningKeyPem = String.Empty;
        private string _StandbySigningKeyFile = "";
        private string _StandbySigningKeyId = "standby";
        private int _CheckIntervalMinutes = 15;
        private int _MaxAssetsPerUpdate = 2000;
        private int _MaxAssetMegabytes = 50;
        private int _MaxUploadMegabytes = 500;
        private int _MaxChannels = 10;
        private int _InstallRetentionDays = 90;
        private int _EventRetentionDays = 30;
        private int _LaunchFailureAlarmPercent = 5;
        private int _AutoHaltMinInstalls = 20;
        private string _RolloutPlanTemplate = "5@0,25@24,100@72";
        private int _RolloutWorkerSeconds = 60;
        private int _ChecksPerInstallPerHour = 60;
        private int _ChecksPerIpPerHour = 600;
        private int _EventsPerInstallPerHour = 30;

        #endregion
    }
}

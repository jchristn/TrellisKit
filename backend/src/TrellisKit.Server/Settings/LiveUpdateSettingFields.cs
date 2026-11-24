namespace TrellisKit.Server.Settings
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Settings of live updates and app configuration (docs/LIVE_UPDATES_CONTRACT.md section 22; the engine secrets key is defined in SettingsRegistry),
    /// appended to <see cref="SettingsRegistry"/>.
    /// </summary>
    public static class LiveUpdateSettingFields
    {
        #region Public-Methods

        /// <summary>
        /// The definitions, in display order.
        /// </summary>
        /// <returns>Definitions.</returns>
        public static List<SettingFieldDefinition> Build()
        {
            const SettingApplyModeEnum live = SettingApplyModeEnum.Live;
            const SettingApplyModeEnum restart = SettingApplyModeEnum.Restart;
            const string updates = "Live updates";
            const string app = "App configuration";
            return new List<SettingFieldDefinition>
            {
                SettingFieldDefinition.Boolean("updates.enabled", updates, "Serve live updates",
                    "When off, the manifest endpoint answers every check with a signed noUpdateAvailable directive.", live, "TK_UPDATES_ENABLED",
                    s => s.Updates.Enabled, (s, v) => s.Updates.Enabled = v),
                SettingFieldDefinition.Choice<MediaStorageModeEnum>("updates.storageMode", updates, "Update storage",
                    "Local stores update assets under storage.directory/updates; S3 uses updates.bucket.", restart, "TK_UPDATES_STORAGE_MODE",
                    s => s.Updates.StorageMode, (s, v) => s.Updates.StorageMode = v),
                SettingFieldDefinition.Text("updates.bucket", updates, "Updates bucket",
                    "S3 bucket of update assets (keys updates/assets/{propertyId}/{sha256}).", restart, "TK_UPDATES_BUCKET",
                    s => s.Updates.Bucket, (s, v) => s.Updates.Bucket = v),
                SettingFieldDefinition.Text("updates.s3Region", updates, "Updates bucket region",
                    "Region of the updates bucket; empty uses media.s3Region.", restart, "TK_UPDATES_S3_REGION",
                    s => s.Updates.S3Region, (s, v) => s.Updates.S3Region = v),
                SettingFieldDefinition.Text("updates.publicBaseUrl", updates, "Updates public base URL",
                    "Absolute URL of the CDN updates path, for example https://media.example.test/updates (asset URLs are {base}/assets/{propertyId}/{sha256}). Empty serves assets from this API.", live, "TK_UPDATES_PUBLIC_BASE_URL",
                    s => s.Updates.PublicBaseUrl, (s, v) => s.Updates.PublicBaseUrl = OptionalUrl(v)),
                SettingFieldDefinition.Secret("updates.signingKeyPem", updates, "Signing key",
                    "PEM RSA private key that signs manifests (SingleProperty deployments). Supply it through the environment from a secret store; never commit it.", live, "TK_UPDATES_SIGNING_KEY",
                    s => s.Updates.SigningKeyPem, (s, v) => s.Updates.SigningKeyPem = v ?? String.Empty),
                SettingFieldDefinition.Text("updates.signingKeyFile", updates, "Signing key file",
                    "Path of a PEM file with the signing key, used when updates.signingKeyPem is empty (local development: ~/.trelliskit/keys/<propertyId>/).", live, "TK_UPDATES_SIGNING_KEY_FILE",
                    s => s.Updates.SigningKeyFile, (s, v) => s.Updates.SigningKeyFile = v),
                SettingFieldDefinition.Text("updates.signingKeyId", updates, "Signing key id",
                    "Key id of updates.signingKeyPem (the keyid builds embed for it).", live, "TK_UPDATES_SIGNING_KEY_ID",
                    s => s.Updates.SigningKeyId, (s, v) => s.Updates.SigningKeyId = RequireText(v)),
                SettingFieldDefinition.Secret("updates.standbySigningKeyPem", updates, "Standby signing key",
                    "PEM RSA private key of the standby certificate, so signing can switch without a new binary.", live, "TK_UPDATES_STANDBY_SIGNING_KEY",
                    s => s.Updates.StandbySigningKeyPem, (s, v) => s.Updates.StandbySigningKeyPem = v ?? String.Empty),
                SettingFieldDefinition.Text("updates.standbySigningKeyFile", updates, "Standby signing key file",
                    "Path of a PEM file with the standby key, used when updates.standbySigningKeyPem is empty.", live, "TK_UPDATES_STANDBY_SIGNING_KEY_FILE",
                    s => s.Updates.StandbySigningKeyFile, (s, v) => s.Updates.StandbySigningKeyFile = v),
                SettingFieldDefinition.Text("updates.standbySigningKeyId", updates, "Standby key id",
                    "Key id of the standby key.", live, "TK_UPDATES_STANDBY_SIGNING_KEY_ID",
                    s => s.Updates.StandbySigningKeyId, (s, v) => s.Updates.StandbySigningKeyId = RequireText(v)),
                SettingFieldDefinition.Integer("updates.checkIntervalMinutes", updates, "Check interval minutes",
                    "Minimum minutes between foreground update checks in the app (property updates.checkIntervalMinutes overrides it).", live, null, 1, 1440,
                    s => s.Updates.CheckIntervalMinutes, (s, v) => s.Updates.CheckIntervalMinutes = v),
                SettingFieldDefinition.Integer("updates.maxAssetsPerUpdate", updates, "Max assets per update",
                    "Most assets one update may declare.", live, null, 10, 10000,
                    s => s.Updates.MaxAssetsPerUpdate, (s, v) => s.Updates.MaxAssetsPerUpdate = v),
                SettingFieldDefinition.Integer("updates.maxAssetMegabytes", updates, "Max asset megabytes",
                    "Largest single asset or source map.", live, null, 1, 500,
                    s => s.Updates.MaxAssetMegabytes, (s, v) => s.Updates.MaxAssetMegabytes = v),
                SettingFieldDefinition.Integer("updates.maxUploadMegabytes", updates, "Max upload megabytes",
                    "Largest upload request (also set the proxy body limit for /api/v1/admin/updates).", live, null, 10, 5000,
                    s => s.Updates.MaxUploadMegabytes, (s, v) => s.Updates.MaxUploadMegabytes = v),
                SettingFieldDefinition.Integer("updates.maxChannels", updates, "Max custom channels",
                    "Custom channels per property beyond production, preview, and development.", live, null, 3, 50,
                    s => s.Updates.MaxChannels, (s, v) => s.Updates.MaxChannels = v),
                SettingFieldDefinition.Integer("updates.installRetentionDays", updates, "Install retention days",
                    "Days an install row is kept after its last check.", live, null, 7, 3650,
                    s => s.Updates.InstallRetentionDays, (s, v) => s.Updates.InstallRetentionDays = v),
                SettingFieldDefinition.Integer("updates.eventRetentionDays", updates, "Event retention days",
                    "Days client update events are kept (daily counts stay).", live, null, 1, 365,
                    s => s.Updates.EventRetentionDays, (s, v) => s.Updates.EventRetentionDays = v),
                SettingFieldDefinition.Integer("updates.launchFailureAlarmPercent", updates, "Launch failure threshold percent",
                    "Launch failure share that raises the alarm and is the default automatic halt threshold of new groups.", live, null, 1, 100,
                    s => s.Updates.LaunchFailureAlarmPercent, (s, v) => s.Updates.LaunchFailureAlarmPercent = v),
                SettingFieldDefinition.Integer("updates.autoHaltMinInstalls", updates, "Automatic halt minimum installs",
                    "Installs an update needs before the automatic halt judges its failure rate (default of new groups).", live, null, 1, 100000,
                    s => s.Updates.AutoHaltMinInstalls, (s, v) => s.Updates.AutoHaltMinInstalls = v),
                SettingFieldDefinition.Text("updates.rolloutPlanTemplate", updates, "Rollout plan template",
                    "Stages offered in the admin console as comma-separated percent@hoursAfterPublish, for example 5@0,25@24,100@72.", live, null,
                    s => s.Updates.RolloutPlanTemplate, (s, v) => s.Updates.RolloutPlanTemplate = Template(v)),
                SettingFieldDefinition.Integer("updates.rolloutWorkerSeconds", updates, "Rollout worker seconds",
                    "Seconds between passes that apply rollout stages and automatic halts.", live, null, 10, 3600,
                    s => s.Updates.RolloutWorkerSeconds, (s, v) => s.Updates.RolloutWorkerSeconds = v),
                SettingFieldDefinition.Integer("rateLimits.updateChecksPerInstallPerHour", updates, "Update checks per install per hour",
                    "Manifest requests per install id each hour.", live, null, 1, 10000,
                    s => s.Updates.ChecksPerInstallPerHour, (s, v) => s.Updates.ChecksPerInstallPerHour = v),
                SettingFieldDefinition.Integer("rateLimits.updateChecksPerIpPerHour", updates, "Update checks per IP per hour",
                    "Manifest requests per IP address each hour.", live, null, 1, 100000,
                    s => s.Updates.ChecksPerIpPerHour, (s, v) => s.Updates.ChecksPerIpPerHour = v),
                SettingFieldDefinition.Integer("rateLimits.updateEventsPerInstallPerHour", updates, "Update event batches per install per hour",
                    "Client event batches per install id each hour.", live, null, 1, 10000,
                    s => s.Updates.EventsPerInstallPerHour, (s, v) => s.Updates.EventsPerInstallPerHour = v),
                SettingFieldDefinition.Text("updates.contentAssetBaseUrl", updates, "Content assets public base URL",
                    "Absolute URL of the CDN app-content path (remote asset URLs are {base}/{propertyId}/{sha256}). Empty serves them from this API.", live, "TK_UPDATES_CONTENT_ASSET_BASE_URL",
                    s => s.Updates.ContentAssetBaseUrl, (s, v) => s.Updates.ContentAssetBaseUrl = OptionalUrl(v)),
                SettingFieldDefinition.Integer("updates.assetGcRetentionDays", updates, "Unreferenced asset retention days",
                    "Days an update asset that no update references is kept before UpdateAssetGcWorker deletes it.", live, null, 1, 3650,
                    s => s.Updates.AssetGcRetentionDays, (s, v) => s.Updates.AssetGcRetentionDays = v),
                SettingFieldDefinition.Integer("updates.assetGcBatchSize", updates, "Asset garbage collection batch size",
                    "Update assets UpdateAssetGcWorker deletes per batch.", live, null, 10, 5000,
                    s => s.Updates.AssetGcBatchSize, (s, v) => s.Updates.AssetGcBatchSize = v),
                SettingFieldDefinition.Integer("updates.assetGcIntervalHours", updates, "Asset garbage collection interval hours",
                    "Hours between UpdateAssetGcWorker passes (weekly by default).", live, null, 1, 720,
                    s => s.Updates.AssetGcIntervalHours, (s, v) => s.Updates.AssetGcIntervalHours = v),
                SettingFieldDefinition.Integer("updates.maxSourceMapMegabytes", updates, "Max source map megabytes",
                    "Largest private source map the symbolication route reads.", live, null, 1, 1000,
                    s => s.Updates.MaxSourceMapMegabytes, (s, v) => s.Updates.MaxSourceMapMegabytes = v),
                SettingFieldDefinition.Integer("appConfig.maxFlags", app, "Max feature flags",
                    "Feature flags per property, not counting kill switches.", live, null, 10, 1000,
                    s => s.AppConfig.MaxFlags, (s, v) => s.AppConfig.MaxFlags = v),
                SettingFieldDefinition.Integer("appConfig.maxRulesPerFlag", app, "Max rules per flag",
                    "Targeting rules per flag or kill switch.", live, null, 1, 100,
                    s => s.AppConfig.MaxRulesPerFlag, (s, v) => s.AppConfig.MaxRulesPerFlag = v),
                SettingFieldDefinition.Integer("appConfig.maxConditionsPerRule", app, "Max conditions per rule",
                    "Conditions per targeting rule.", live, null, 1, 50,
                    s => s.AppConfig.MaxConditionsPerRule, (s, v) => s.AppConfig.MaxConditionsPerRule = v),
                SettingFieldDefinition.Integer("appConfig.maxValuesPerCondition", app, "Max values per condition",
                    "Values per condition.", live, null, 1, 1000,
                    s => s.AppConfig.MaxValuesPerCondition, (s, v) => s.AppConfig.MaxValuesPerCondition = v),
                SettingFieldDefinition.Integer("appConfig.checkIntervalMinutes", app, "App manifest refresh minutes",
                    "How often clients re-fetch the app manifest while in the foreground.", live, null, 1, 1440,
                    s => s.AppConfig.CheckIntervalMinutes, (s, v) => s.AppConfig.CheckIntervalMinutes = v),
                SettingFieldDefinition.Integer("appConfig.propagationSeconds", app, "Content propagation seconds",
                    "Seconds a task may serve cached published content.", live, null, 1, 60,
                    s => s.AppConfig.PropagationSeconds, (s, v) => s.AppConfig.PropagationSeconds = v),
                SettingFieldDefinition.Integer("appConfig.killSwitchPropagationSeconds", app, "Kill switch propagation seconds",
                    "Seconds a task may serve cached kill switches.", live, null, 1, 60,
                    s => s.AppConfig.KillSwitchPropagationSeconds, (s, v) => s.AppConfig.KillSwitchPropagationSeconds = v),
                SettingFieldDefinition.Integer("appConfig.changeLogRetentionVersions", app, "Change log retention versions",
                    "Content versions whose change log is kept.", live, null, 10, 10000,
                    s => s.AppConfig.ChangeLogRetentionVersions, (s, v) => s.AppConfig.ChangeLogRetentionVersions = v),
                SettingFieldDefinition.Integer("appConfig.maxStringOverrides", app, "Max remote string overrides",
                    "Remote string overrides per property (drafts and published).", live, null, 10, 20000,
                    s => s.AppConfig.MaxStringOverrides, (s, v) => s.AppConfig.MaxStringOverrides = v),
                SettingFieldDefinition.Integer("appConfig.maxBanners", app, "Max banners",
                    "Banners per property, not counting archived ones.", live, null, 1, 1000,
                    s => s.AppConfig.MaxBanners, (s, v) => s.AppConfig.MaxBanners = v),
                SettingFieldDefinition.Integer("appConfig.maxActiveBanners", app, "Max active banners per manifest",
                    "Active banners one app manifest carries (highest priority first).", live, null, 1, 100,
                    s => s.AppConfig.MaxActiveBanners, (s, v) => s.AppConfig.MaxActiveBanners = v),
                SettingFieldDefinition.Integer("appConfig.maxRemoteAssets", app, "Max remote assets",
                    "Remote assets per property, not counting archived ones.", live, null, 1, 2000,
                    s => s.AppConfig.MaxRemoteAssets, (s, v) => s.AppConfig.MaxRemoteAssets = v),
                SettingFieldDefinition.Integer("appConfig.maxAssetMegabytes", app, "Max remote asset megabytes",
                    "Largest remote asset image upload (re-encoded to WebP).", live, null, 1, 50,
                    s => s.AppConfig.MaxAssetMegabytes, (s, v) => s.AppConfig.MaxAssetMegabytes = v),
                SettingFieldDefinition.Integer("appConfig.maxDeltaItems", app, "Max manifest delta items",
                    "Changed strings, banners, and assets one manifest delta carries before it answers fullRefresh.", live, null, 50, 5000,
                    s => s.AppConfig.MaxDeltaItems, (s, v) => s.AppConfig.MaxDeltaItems = v),
                SettingFieldDefinition.Integer("rateLimits.appContentEnumeratePerHour", app, "Content re-enumerations per client per hour",
                    "POST /api/v1/app/strings, banners, and assets enumerate requests per install or visitor id each hour.", live, null, 1, 10000,
                    s => s.AppConfig.ContentEnumeratePerHour, (s, v) => s.AppConfig.ContentEnumeratePerHour = v),
                SettingFieldDefinition.Boolean("mobile.enforceMinimumOnApi", app, "Enforce the minimum app version on the API",
                    "When on, member API routes return 426 ClientUpdateRequired to mobile callers below mobile.minimumBinaryVersion (auth, me, membership, property, and update routes are exempt).", live, null,
                    s => s.AppConfig.EnforceMinimumOnApi, (s, v) => s.AppConfig.EnforceMinimumOnApi = v),
                SettingFieldDefinition.Integer("rateLimits.appManifestPerClientPerHour", app, "App manifests per client per hour",
                    "App manifest requests per install or visitor id each hour.", live, null, 1, 10000,
                    s => s.AppConfig.ManifestPerClientPerHour, (s, v) => s.AppConfig.ManifestPerClientPerHour = v),
                SettingFieldDefinition.Integer("rateLimits.appManifestPerIpPerHour", app, "App manifests per IP per hour",
                    "App manifest requests per IP address each hour.", live, null, 1, 100000,
                    s => s.AppConfig.ManifestPerIpPerHour, (s, v) => s.AppConfig.ManifestPerIpPerHour = v)
            };
        }

        #endregion

        #region Private-Methods

        private static string OptionalUrl(string value)
        {
            string trimmed = (value ?? String.Empty).Trim().TrimEnd('/');
            if (trimmed.Length == 0) return String.Empty;
            if (!Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                throw new ArgumentException("Must be empty or an absolute http or https URL.");
            return trimmed;
        }

        private static string RequireText(string value)
        {
            string trimmed = (value ?? String.Empty).Trim();
            if (trimmed.Length == 0 || trimmed.Length > 64) throw new ArgumentException("Must be 1 to 64 characters.");
            return trimmed;
        }

        private static string Template(string value)
        {
            string trimmed = (value ?? String.Empty).Trim();
            TrellisKit.Server.Services.UpdatePublishService.ParseTemplate(trimmed);
            return trimmed;
        }

        #endregion
    }
}

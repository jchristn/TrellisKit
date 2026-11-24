namespace TrellisKit.Core.Database
{
    using System;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Enumeration collections of live updates and app configuration (docs/LIVE_UPDATES_CONTRACT.md). Table aliases:
    /// updategroups g, updateassetrefs r, updatechannels c, updatedeploykeys k, updatesigningkeys s, updateinstalls i,
    /// featureflags f, contentversions v, appcontentchanges x.
    /// </summary>
    public static class LiveUpdateCollections
    {
        #region Public-Members

        /// <summary>Adoption day buckets within a bounded time window.</summary>
        public static readonly EnumerationCollection AdoptionDays = new EnumerationCollection("update-adoption-days", null, "id", "createdutc", true) { HasInlineMetadata = true }
            .Filters("days").DefaultTo("CreatedAscending");

        /// <summary>Install counts grouped across all versions on an update group.</summary>
        public static readonly EnumerationCollection AdoptionBinaries = new EnumerationCollection("update-adoption-binaries", null, "ic.enumkey", "ic.firstseenutc", true) { HasInlineMetadata = true }
            .Search("ic.k").Order("CountDescending", "ic.cnt", true, EnumerationKeyKindEnum.Number)
            .Order("KeyAscending", "ic.k", false, EnumerationKeyKindEnum.Text).DefaultTo("CountDescending");


        /// <summary>Grouped active installs by embedded signing key. Dates use first-seen time; metadata is empty.</summary>
        public static readonly EnumerationCollection SigningKeyInstalls = new EnumerationCollection("update-signing-key-installs", null, "ic.enumkey", "ic.firstseenutc", true) { HasInlineMetadata = true }
            .Order("CountDescending", "ic.cnt", true, EnumerationKeyKindEnum.Number)
            .Order("KeyAscending", "ic.k", false, EnumerationKeyKindEnum.Text)
            .DefaultTo("CountDescending").Search("ic.k");


        /// <summary>
        /// POST /api/v1/admin/update-groups/enumerate: search over message and git commit; filters platform, channel,
        /// runtimeVersion, status, kind, critical, gitCommit.
        /// </summary>
        public static readonly EnumerationCollection UpdateGroups = new EnumerationCollection("update-groups", TargetTypeEnum.UpdateGroup, "g.id", "g.createdutc", true)
            .Order("UpdatedDescending", "g.updatedutc", true, EnumerationKeyKindEnum.Time)
            .Search("g.message", "g.gitcommit")
            .Filters("platform", "channel", "runtimeVersion", "status", "kind", "critical", "gitCommit");

        /// <summary>
        /// POST /api/v1/admin/updates/{id}/assets/enumerate: search over key and content type. Created window: the
        /// asset's first storage time.
        /// </summary>
        public static readonly EnumerationCollection UpdateAssets = new EnumerationCollection("update-assets", null, "r.sha256hex", "a.lastreferencedutc", true)
            .Order("PositionAscending", "r.position", false, EnumerationKeyKindEnum.Number)
            .DefaultTo("PositionAscending")
            .Search("r.assetkey", "r.contenttype");

        /// <summary>
        /// POST /api/v1/admin/updates/channels/enumerate: search over name; filter builtIn.
        /// </summary>
        public static readonly EnumerationCollection Channels = new EnumerationCollection("update-channels", null, "c.name", "c.createdutc", true)
            .Order("NameAscending", "c.name", false, EnumerationKeyKindEnum.Text)
            .DefaultTo("NameAscending")
            .Search("c.name")
            .Filters("builtIn");

        /// <summary>
        /// POST /api/v1/admin/updates/deploy-keys/enumerate: search over name; filter active.
        /// </summary>
        public static readonly EnumerationCollection DeployKeys = new EnumerationCollection("update-deploy-keys", TargetTypeEnum.UpdateDeployKey, "k.id", "k.createdutc", true)
            .Search("k.name")
            .Filters("active");

        /// <summary>
        /// POST /api/v1/admin/updates/signing-keys/enumerate: search over key id; filter status.
        /// </summary>
        public static readonly EnumerationCollection SigningKeys = new EnumerationCollection("update-signing-keys", TargetTypeEnum.UpdateSigningKey, "s.id", "s.createdutc", true)
            .Order("KeyIdAscending", "s.keyid", false, EnumerationKeyKindEnum.Text)
            .Search("s.keyid")
            .Filters("status");

        /// <summary>
        /// POST /api/v1/admin/updates/runtime-versions/enumerate: grouped installs per platform and runtime version.
        /// Created window: first seen. Filters platform, channel.
        /// </summary>
        public static readonly EnumerationCollection RuntimeVersions = new EnumerationCollection("update-runtime-versions", null, "rv.enumkey", "rv.firstseenutc", true)
            .Order("LastSeenDescending", "rv.lastseenutc", true, EnumerationKeyKindEnum.Time)
            .DefaultTo("LastSeenDescending")
            .Search("rv.runtimeversion")
            .Filters("platform", "channel");

        /// <summary>
        /// POST /api/v1/admin/app/flags/enumerate: search over key, name, description; filters kind, state,
        /// hasDraftChanges, client.
        /// </summary>
        public static readonly EnumerationCollection Flags = new EnumerationCollection("app-flags", TargetTypeEnum.FeatureFlag, "f.objectid", "f.createdutc", true)
            .Order("KeyAscending", "lower(f.flagkey)", false, EnumerationKeyKindEnum.Text)
            .Order("UpdatedDescending", "f.updatedutc", true, EnumerationKeyKindEnum.Time)
            .DefaultTo("KeyAscending")
            .Search("f.flagkey", "f.name", "f.description")
            .Filters("kind", "state", "hasDraftChanges", "client");

        /// <summary>
        /// POST /api/v1/admin/app/flags/{id}/history/enumerate: the published revisions of one flag, newest first.
        /// </summary>
        public static readonly EnumerationCollection FlagHistory = new EnumerationCollection("app-flag-history", null, "f.revisionid", "f.updatedutc", true)
            .Order("VersionDescending", "COALESCE(f.validfromversion, 0)", true, EnumerationKeyKindEnum.Number)
            .DefaultTo("VersionDescending");

        /// <summary>
        /// POST /api/v1/admin/app/kill-switches/enumerate: search over the feature key; filter killed.
        /// </summary>
        public static readonly EnumerationCollection KillSwitches = new EnumerationCollection("app-kill-switches", TargetTypeEnum.FeatureFlag, "f.objectid", "f.createdutc", true)
            .Order("KeyAscending", "lower(f.flagkey)", false, EnumerationKeyKindEnum.Text)
            .DefaultTo("KeyAscending")
            .Search("f.flagkey", "f.message")
            .Filters("killed");

        /// <summary>
        /// POST /api/v1/admin/app/versions/enumerate: search over message.
        /// </summary>
        public static readonly EnumerationCollection Versions = new EnumerationCollection("app-versions", TargetTypeEnum.ContentVersion, "v.id", "v.publishedutc", true)
            .Order("VersionDescending", "v.version", true, EnumerationKeyKindEnum.Number)
            .DefaultTo("VersionDescending")
            .Search("v.message");

        /// <summary>
        /// POST /api/v1/admin/app/versions/{version}/changes/enumerate and POST /api/v1/admin/app/drafts/enumerate:
        /// search over the flag key. Created window: the revision time.
        /// </summary>
        public static readonly EnumerationCollection Changes = new EnumerationCollection("app-changes", TargetTypeEnum.FeatureFlag, "f.objectid", "f.updatedutc", true)
            .Order("KeyAscending", "lower(f.flagkey)", false, EnumerationKeyKindEnum.Text)
            .DefaultTo("KeyAscending")
            .Search("f.flagkey");

        /// <summary>
        /// POST /api/v1/admin/app/strings/enumerate: catalog keys joined with overrides; search over key, default, and
        /// override value; filters client, overridden, hasDraftChanges. Created window: the latest override edit.
        /// Labels and tags attach to the catalog key.
        /// </summary>
        public static readonly EnumerationCollection Strings = new EnumerationCollection("app-strings", TargetTypeEnum.RemoteString, "c.stringkey",
                "(SELECT MAX(sx.updatedutc) FROM remotestrings sx WHERE sx.propertyid = c.propertyid AND sx.stringkey = c.stringkey)", true)
            .Order("KeyAscending", "c.stringkey", false, EnumerationKeyKindEnum.Text)
            .DefaultTo("KeyAscending")
            .Search("c.stringkey", "c.defaultvalue", "(SELECT string_agg(sv.value, ' ') FROM remotestrings sv WHERE sv.propertyid = c.propertyid AND sv.stringkey = c.stringkey AND (sv.isdraft = TRUE OR sv.validtoversion IS NULL))")
            .Filters("client", "overridden", "hasDraftChanges");

        /// <summary>
        /// POST /api/v1/admin/app/strings/{key}/history/enumerate: published revisions of one override, newest first.
        /// </summary>
        public static readonly EnumerationCollection StringHistory = new EnumerationCollection("app-string-history", null, "s.revisionid", "s.updatedutc", true)
            .Order("VersionDescending", "COALESCE(s.validfromversion, 0)", true, EnumerationKeyKindEnum.Number)
            .DefaultTo("VersionDescending");

        /// <summary>
        /// POST /api/v1/admin/app/banners/enumerate: search over title and body; filters active, placement, style, state,
        /// hasDraftChanges.
        /// </summary>
        public static readonly EnumerationCollection Banners = new EnumerationCollection("app-banners", TargetTypeEnum.Banner, "b.objectid", "b.createdutc", true)
            .Order("PriorityDescending", "b.priority", true, EnumerationKeyKindEnum.Number)
            .Order("UpdatedDescending", "b.updatedutc", true, EnumerationKeyKindEnum.Time)
            .DefaultTo("PriorityDescending")
            .Search("b.title", "b.body")
            .Filters("active", "placement", "style", "state", "hasDraftChanges");

        /// <summary>
        /// POST /api/v1/admin/app/banners/{id}/history/enumerate.
        /// </summary>
        public static readonly EnumerationCollection BannerHistory = new EnumerationCollection("app-banner-history", null, "b.revisionid", "b.updatedutc", true)
            .Order("VersionDescending", "COALESCE(b.validfromversion, 0)", true, EnumerationKeyKindEnum.Number)
            .DefaultTo("VersionDescending");

        /// <summary>
        /// POST /api/v1/admin/app/assets/enumerate: search over key and alt text; filters state, hasDraftChanges.
        /// </summary>
        public static readonly EnumerationCollection Assets = new EnumerationCollection("app-assets", TargetTypeEnum.RemoteAsset, "a.objectid", "a.createdutc", true)
            .Order("KeyAscending", "lower(a.assetkey)", false, EnumerationKeyKindEnum.Text)
            .Order("UpdatedDescending", "a.updatedutc", true, EnumerationKeyKindEnum.Time)
            .DefaultTo("KeyAscending")
            .Search("a.assetkey", "a.alttext")
            .Filters("state", "hasDraftChanges");

        /// <summary>
        /// POST /api/v1/admin/app/assets/{id}/history/enumerate.
        /// </summary>
        public static readonly EnumerationCollection AssetHistory = new EnumerationCollection("app-asset-history", null, "a.revisionid", "a.updatedutc", true)
            .Order("VersionDescending", "COALESCE(a.validfromversion, 0)", true, EnumerationKeyKindEnum.Number)
            .DefaultTo("VersionDescending");

        /// <summary>
        /// POST /api/v1/app/strings/enumerate (anonymous): published overrides for the caller's client; filter client.
        /// </summary>
        public static readonly EnumerationCollection PublicStrings = new EnumerationCollection("app-public-strings", null, "s.stringkey", "s.updatedutc", false)
            .Order("KeyAscending", "s.stringkey", false, EnumerationKeyKindEnum.Text)
            .DefaultTo("KeyAscending")
            .Filters("client");

        /// <summary>
        /// POST /api/v1/app/banners/enumerate (anonymous): the active banners for the caller.
        /// </summary>
        public static readonly EnumerationCollection PublicBanners = new EnumerationCollection("app-public-banners", TargetTypeEnum.Banner, "b.objectid", "b.createdutc", false)
            .Order("PriorityDescending", "b.priority", true, EnumerationKeyKindEnum.Number)
            .DefaultTo("PriorityDescending");

        /// <summary>
        /// POST /api/v1/app/assets/enumerate (anonymous): published remote asset descriptors.
        /// </summary>
        public static readonly EnumerationCollection PublicAssets = new EnumerationCollection("app-public-assets", TargetTypeEnum.RemoteAsset, "a.objectid", "a.createdutc", false)
            .Order("KeyAscending", "lower(a.assetkey)", false, EnumerationKeyKindEnum.Text)
            .DefaultTo("KeyAscending")
            .Search("a.assetkey");

        /// <summary>
        /// POST /api/v1/admin/updates/{id}/failures/enumerate: reported failures with a message or stack trace, newest
        /// first; filter type.
        /// </summary>
        public static readonly EnumerationCollection UpdateFailures = new EnumerationCollection("update-failures", TargetTypeEnum.UpdateEvent, "d.eventid", "d.occurredutc", true)
            .Search("d.message")
            .Filters("type");

        #endregion

    }
}

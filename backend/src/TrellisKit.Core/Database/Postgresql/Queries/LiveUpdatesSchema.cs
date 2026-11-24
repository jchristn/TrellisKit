namespace TrellisKit.Core.Database.Postgresql.Queries
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// PostgreSQL statements of migration 30, "Live updates" (docs/LIVE_UPDATES_CONTRACT.md section 23): code updates,
    /// assets, channels, deploy and signing keys, installs, events, rollout plans, feature flags, kill switches, and
    /// content versions. Every table has propertyid first and every index starts with propertyid.
    /// </summary>
    internal static class LiveUpdatesSchema
    {
        internal const int Version = 30;

        internal const string Description = "Live updates";

        internal static List<string> Statements()
        {
            return new List<string>
            {
                "CREATE TABLE IF NOT EXISTS updategroups (propertyid TEXT NOT NULL, id TEXT NOT NULL PRIMARY KEY, kind TEXT NOT NULL, channel TEXT NOT NULL, message TEXT NOT NULL, status TEXT NOT NULL, " +
                "rolloutbasispoints INTEGER NOT NULL, critical BOOLEAN NOT NULL DEFAULT FALSE, criticalmessage TEXT NULL, gitcommit TEXT NULL, gitdirty BOOLEAN NOT NULL DEFAULT FALSE, " +
                "republishof TEXT NULL, promotedfrom TEXT NULL, clientgroupid TEXT NULL, createdbytype TEXT NOT NULL, createdbyid TEXT NULL, createdbyname TEXT NOT NULL, " +
                "autohaltenabled BOOLEAN NOT NULL DEFAULT TRUE, autohaltpercent INTEGER NOT NULL DEFAULT 5, autohaltmininstalls INTEGER NOT NULL DEFAULT 20, haltedutc TIMESTAMPTZ NULL, haltreason TEXT NULL, " +
                "publishedutc TIMESTAMPTZ NULL, pausedutc TIMESTAMPTZ NULL, rolledbackutc TIMESTAMPTZ NULL, supersededutc TIMESTAMPTZ NULL, createdutc TIMESTAMPTZ NOT NULL, updatedutc TIMESTAMPTZ NOT NULL);",
                "CREATE INDEX IF NOT EXISTS idx_updategroups_list ON updategroups (propertyid, createdutc DESC, id DESC);",
                "CREATE INDEX IF NOT EXISTS idx_updategroups_channel ON updategroups (propertyid, channel, status, createdutc);",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_updategroups_client ON updategroups (propertyid, clientgroupid) WHERE clientgroupid IS NOT NULL;",

                "CREATE TABLE IF NOT EXISTS updates (propertyid TEXT NOT NULL, id TEXT NOT NULL PRIMARY KEY, groupid TEXT NOT NULL REFERENCES updategroups (id), uuid TEXT NOT NULL, platform TEXT NOT NULL, " +
                "runtimeversion TEXT NOT NULL, channel TEXT NOT NULL, kind TEXT NOT NULL, status TEXT NOT NULL, rolloutbasispoints INTEGER NOT NULL, createdat TIMESTAMPTZ NOT NULL, " +
                "launchassethash TEXT NULL, launchassetsizebytes BIGINT NOT NULL DEFAULT 0, assetcount INTEGER NOT NULL DEFAULT 0, totalbytes BIGINT NOT NULL DEFAULT 0, createdutc TIMESTAMPTZ NOT NULL, updatedutc TIMESTAMPTZ NOT NULL);",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_updates_uuid ON updates (propertyid, uuid);",
                "CREATE INDEX IF NOT EXISTS idx_updates_select ON updates (propertyid, platform, runtimeversion, channel, status, createdat DESC, id);",
                "CREATE INDEX IF NOT EXISTS idx_updates_group ON updates (propertyid, groupid);",

                "CREATE TABLE IF NOT EXISTS updategroupconfigs (propertyid TEXT NOT NULL, updateid TEXT NOT NULL PRIMARY KEY REFERENCES updates (id), expoclient TEXT NOT NULL);",
                "CREATE INDEX IF NOT EXISTS idx_updategroupconfigs_property ON updategroupconfigs (propertyid);",

                "CREATE TABLE IF NOT EXISTS updateassets (propertyid TEXT NOT NULL, sha256hex TEXT NOT NULL, hashb64url TEXT NOT NULL, contenttype TEXT NOT NULL, sizebytes BIGINT NOT NULL, " +
                "storedutc TIMESTAMPTZ NULL, lastreferencedutc TIMESTAMPTZ NOT NULL, PRIMARY KEY (propertyid, sha256hex));",
                "CREATE TABLE IF NOT EXISTS updateassetrefs (propertyid TEXT NOT NULL, updateid TEXT NOT NULL, position INTEGER NOT NULL, sha256hex TEXT NOT NULL, assetkey TEXT NOT NULL, " +
                "fileextension TEXT NOT NULL, contenttype TEXT NOT NULL, islaunchasset BOOLEAN NOT NULL DEFAULT FALSE, PRIMARY KEY (propertyid, updateid, position));",
                "CREATE INDEX IF NOT EXISTS idx_updateassetrefs_hash ON updateassetrefs (propertyid, sha256hex);",
                "CREATE TABLE IF NOT EXISTS updatesourcemaps (propertyid TEXT NOT NULL, updateid TEXT NOT NULL, position INTEGER NOT NULL, sha256hex TEXT NOT NULL, mapkey TEXT NOT NULL, " +
                "bundlekey TEXT NOT NULL, sizebytes BIGINT NOT NULL, storagekey TEXT NOT NULL, PRIMARY KEY (propertyid, updateid, position));",

                "CREATE TABLE IF NOT EXISTS updatechannels (propertyid TEXT NOT NULL, name TEXT NOT NULL, builtin BOOLEAN NOT NULL DEFAULT FALSE, createdutc TIMESTAMPTZ NOT NULL, PRIMARY KEY (propertyid, name));",
                "CREATE INDEX IF NOT EXISTS idx_updatechannels_created ON updatechannels (propertyid, createdutc);",

                "CREATE TABLE IF NOT EXISTS updatedeploykeys (propertyid TEXT NOT NULL, id TEXT NOT NULL PRIMARY KEY, name TEXT NOT NULL, secrethash TEXT NOT NULL, expiresutc TIMESTAMPTZ NULL, " +
                "lastusedutc TIMESTAMPTZ NULL, revokedutc TIMESTAMPTZ NULL, createdbyuserid TEXT NULL, createdutc TIMESTAMPTZ NOT NULL);",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_updatedeploykeys_secret ON updatedeploykeys (propertyid, secrethash);",
                "CREATE INDEX IF NOT EXISTS idx_updatedeploykeys_created ON updatedeploykeys (propertyid, createdutc);",
                "CREATE TABLE IF NOT EXISTS updatedeploykeychannels (propertyid TEXT NOT NULL, deploykeyid TEXT NOT NULL, channel TEXT NOT NULL, PRIMARY KEY (propertyid, deploykeyid, channel));",

                "CREATE TABLE IF NOT EXISTS updatesigningkeys (propertyid TEXT NOT NULL, id TEXT NOT NULL PRIMARY KEY, keyid TEXT NOT NULL, algorithm TEXT NOT NULL, keybits INTEGER NOT NULL, source TEXT NOT NULL, " +
                "certificatepem TEXT NOT NULL, certificatesha256 TEXT NOT NULL, certificatenotafterutc TIMESTAMPTZ NULL, isca BOOLEAN NOT NULL DEFAULT FALSE, privatekeyciphertext TEXT NULL, " +
                "privatekeynonce TEXT NULL, privatekeykeyid TEXT NULL, status TEXT NOT NULL, lastusedutc TIMESTAMPTZ NULL, createdutc TIMESTAMPTZ NOT NULL, retiredutc TIMESTAMPTZ NULL);",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_updatesigningkeys_keyid ON updatesigningkeys (propertyid, keyid);",

                "CREATE TABLE IF NOT EXISTS updateinstalls (propertyid TEXT NOT NULL, id TEXT NOT NULL PRIMARY KEY, installidhash TEXT NOT NULL, platform TEXT NOT NULL, channel TEXT NOT NULL, " +
                "runtimeversion TEXT NOT NULL, currentupdateuuid TEXT NULL, embeddedupdateuuid TEXT NULL, keyid TEXT NULL, appversion TEXT NULL, firstseenutc TIMESTAMPTZ NOT NULL, lastcheckutc TIMESTAMPTZ NOT NULL);",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_updateinstalls_hash ON updateinstalls (propertyid, installidhash);",
                "CREATE INDEX IF NOT EXISTS idx_updateinstalls_current ON updateinstalls (propertyid, currentupdateuuid, lastcheckutc);",
                "CREATE INDEX IF NOT EXISTS idx_updateinstalls_runtime ON updateinstalls (propertyid, platform, runtimeversion, lastcheckutc);",
                "CREATE INDEX IF NOT EXISTS idx_updateinstalls_prune ON updateinstalls (propertyid, lastcheckutc);",
                "CREATE TABLE IF NOT EXISTS updateinstallfailures (propertyid TEXT NOT NULL, installidhash TEXT NOT NULL, updateuuid TEXT NOT NULL, createdutc TIMESTAMPTZ NOT NULL, PRIMARY KEY (propertyid, installidhash, updateuuid));",
                "CREATE INDEX IF NOT EXISTS idx_updateinstallfailures_update ON updateinstallfailures (propertyid, updateuuid);",

                "CREATE TABLE IF NOT EXISTS updateevents (propertyid TEXT NOT NULL, id TEXT NOT NULL PRIMARY KEY, installidhash TEXT NOT NULL, updateuuid TEXT NULL, type TEXT NOT NULL, errorcode TEXT NULL, " +
                "launchdurationms INTEGER NULL, occurredutc TIMESTAMPTZ NOT NULL, receivedutc TIMESTAMPTZ NOT NULL);",
                "CREATE INDEX IF NOT EXISTS idx_updateevents_update ON updateevents (propertyid, updateuuid, occurredutc);",
                "CREATE INDEX IF NOT EXISTS idx_updateevents_prune ON updateevents (propertyid, receivedutc);",
                "CREATE TABLE IF NOT EXISTS updateeventcounts (propertyid TEXT NOT NULL, updateuuid TEXT NOT NULL, day TEXT NOT NULL, type TEXT NOT NULL, count BIGINT NOT NULL DEFAULT 0, PRIMARY KEY (propertyid, updateuuid, day, type));",

                "CREATE TABLE IF NOT EXISTS updaterolloutstages (propertyid TEXT NOT NULL, groupid TEXT NOT NULL, position INTEGER NOT NULL, basispoints INTEGER NOT NULL, startsutc TIMESTAMPTZ NOT NULL, " +
                "appliedutc TIMESTAMPTZ NULL, PRIMARY KEY (propertyid, groupid, position));",
                "CREATE INDEX IF NOT EXISTS idx_updaterolloutstages_due ON updaterolloutstages (propertyid, appliedutc, startsutc);",

                "CREATE TABLE IF NOT EXISTS featureflags (propertyid TEXT NOT NULL, revisionid TEXT NOT NULL PRIMARY KEY, objectid TEXT NOT NULL, flagkey TEXT NOT NULL, name TEXT NOT NULL, description TEXT NULL, " +
                "kind TEXT NOT NULL, valuetype TEXT NOT NULL, defaultvalue TEXT NOT NULL, offvalue TEXT NOT NULL, enabled BOOLEAN NOT NULL DEFAULT TRUE, message TEXT NULL, blocksserver BOOLEAN NOT NULL DEFAULT TRUE, " +
                "state TEXT NOT NULL, validfromversion BIGINT NULL, validtoversion BIGINT NULL, isdraft BOOLEAN NOT NULL DEFAULT TRUE, editedbyuserid TEXT NULL, createdutc TIMESTAMPTZ NOT NULL, updatedutc TIMESTAMPTZ NOT NULL);",
                "CREATE INDEX IF NOT EXISTS idx_featureflags_current ON featureflags (propertyid, objectid) WHERE validtoversion IS NULL AND isdraft = FALSE;",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_featureflags_key_current ON featureflags (propertyid, lower(flagkey)) WHERE validtoversion IS NULL AND isdraft = FALSE;",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_featureflags_draft ON featureflags (propertyid, objectid) WHERE isdraft = TRUE;",
                "CREATE INDEX IF NOT EXISTS idx_featureflags_version ON featureflags (propertyid, validfromversion, validtoversion);",
                "CREATE TABLE IF NOT EXISTS featureflagvariants (propertyid TEXT NOT NULL, revisionid TEXT NOT NULL, position INTEGER NOT NULL, value TEXT NOT NULL, PRIMARY KEY (propertyid, revisionid, position));",
                "CREATE TABLE IF NOT EXISTS featureflagclients (propertyid TEXT NOT NULL, revisionid TEXT NOT NULL, client TEXT NOT NULL, PRIMARY KEY (propertyid, revisionid, client));",
                "CREATE TABLE IF NOT EXISTS featureflagrules (propertyid TEXT NOT NULL, revisionid TEXT NOT NULL, id TEXT NOT NULL, ownertype TEXT NOT NULL, position INTEGER NOT NULL, " +
                "description TEXT NULL, value TEXT NOT NULL, rolloutbasispoints INTEGER NOT NULL, PRIMARY KEY (propertyid, revisionid, id));",
                "CREATE INDEX IF NOT EXISTS idx_rules_owner ON featureflagrules (propertyid, revisionid, position);",
                "CREATE TABLE IF NOT EXISTS featureflagconditions (propertyid TEXT NOT NULL, revisionid TEXT NOT NULL, ruleid TEXT NOT NULL, position INTEGER NOT NULL, attribute TEXT NOT NULL, " +
                "operator TEXT NOT NULL, argument TEXT NULL, PRIMARY KEY (propertyid, revisionid, ruleid, position));",
                "CREATE TABLE IF NOT EXISTS featureflagconditionvalues (propertyid TEXT NOT NULL, revisionid TEXT NOT NULL, ruleid TEXT NOT NULL, conditionposition INTEGER NOT NULL, position INTEGER NOT NULL, " +
                "value TEXT NOT NULL, PRIMARY KEY (propertyid, revisionid, ruleid, conditionposition, position));",
                "CREATE TABLE IF NOT EXISTS killswitchstate (propertyid TEXT NOT NULL, featurekey TEXT NOT NULL, flagobjectid TEXT NOT NULL, revision BIGINT NOT NULL DEFAULT 0, lastchangedbyuserid TEXT NULL, " +
                "lastchangedbyname TEXT NULL, lastchangedreason TEXT NULL, lastchangedutc TIMESTAMPTZ NULL, PRIMARY KEY (propertyid, featurekey));",

                "CREATE TABLE IF NOT EXISTS contentversions (propertyid TEXT NOT NULL, id TEXT NOT NULL PRIMARY KEY, version BIGINT NOT NULL, message TEXT NOT NULL, kind TEXT NOT NULL, restoredfromversion BIGINT NULL, " +
                "publishedbyuserid TEXT NULL, publishedbyname TEXT NOT NULL, flagchanges INTEGER NOT NULL DEFAULT 0, stringchanges INTEGER NOT NULL DEFAULT 0, bannerchanges INTEGER NOT NULL DEFAULT 0, " +
                "assetchanges INTEGER NOT NULL DEFAULT 0, publishedutc TIMESTAMPTZ NOT NULL);",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_contentversions_version ON contentversions (propertyid, version);",
                "CREATE INDEX IF NOT EXISTS idx_contentversions_published ON contentversions (propertyid, publishedutc);",
                "CREATE TABLE IF NOT EXISTS appcontentchanges (propertyid TEXT NOT NULL, version BIGINT NOT NULL, objecttype TEXT NOT NULL, objectid TEXT NOT NULL, change TEXT NOT NULL, PRIMARY KEY (propertyid, version, objecttype, objectid));",
                "CREATE TABLE IF NOT EXISTS appcontentstate (propertyid TEXT NOT NULL PRIMARY KEY, contentversion BIGINT NOT NULL DEFAULT 0, killswitchrevision BIGINT NOT NULL DEFAULT 0, updatedutc TIMESTAMPTZ NOT NULL);"
            };
        }
    }
}

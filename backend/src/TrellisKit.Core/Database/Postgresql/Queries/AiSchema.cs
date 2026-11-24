namespace TrellisKit.Core.Database.Postgresql.Queries
{
    using System.Collections.Generic;

    /// <summary>
    /// Migration 40, "AI models and assistants" (docs/AI_CONTRACT.md section 18). Every table carries propertyid first
    /// and every index and unique constraint starts with propertyid. Lists are child tables; no JSON columns. Vectors
    /// live in RecallDB (resolved decision 9); aiknowledgeitems and aiknowledgeindexes track ingestion.
    /// </summary>
    internal static class AiSchema
    {
        /// <summary>
        /// Tables created by migration 40, in purge order (children before parents).
        /// </summary>
        internal static readonly string[] Tables = new[]
        {
            "aibudgetalerts", "aidailyusage", "aiusagerollups", "aiusageapplied", "aifeedback", "aimessagestages", "aimessagecitations",
            "aimessages", "aiconversations", "aivisitors", "aiknowledgeitems", "aiknowledgeindexes", "aiknowledgesourcetags",
            "aiknowledgesourcelabels", "aiknowledgesources", "aiassistantembedorigins", "aiassistantstarterprompts", "aiassistantmodels",
            "aiassistants", "aiendpointhealthchecks", "aiendpointheaders", "aiendpoints"
        };

        internal static List<string> Statements()
        {
            return new List<string>
            {
                "CREATE TABLE IF NOT EXISTS aiendpoints (propertyid TEXT NOT NULL, id TEXT NOT NULL PRIMARY KEY, scope TEXT NOT NULL, name TEXT NOT NULL, namenormalized TEXT NOT NULL, description TEXT NULL, " +
                "enabled BOOLEAN NOT NULL, kind TEXT NOT NULL, apitype TEXT NOT NULL, model TEXT NOT NULL, endpointurl TEXT NOT NULL, authtype TEXT NOT NULL, authheadername TEXT NULL, " +
                "authqueryparameter TEXT NULL, authusername TEXT NULL, authvaluesecret TEXT NULL, region TEXT NULL, project TEXT NULL, apiversion TEXT NULL, deployment TEXT NULL, accesskeyid TEXT NULL, " +
                "temperature DOUBLE PRECISION NULL, topp DOUBLE PRECISION NULL, maxtokens INTEGER NULL, contextwindow INTEGER NULL, reasoningeffort TEXT NULL, streaming BOOLEAN NOT NULL, " +
                "timeoutms INTEGER NOT NULL, firstresponsetimeoutms INTEGER NULL, dimensions INTEGER NULL, maxconcurrentrequests INTEGER NOT NULL, maxqueuedepth INTEGER NOT NULL, maxretries INTEGER NOT NULL, " +
                "healthcheckenabled BOOLEAN NOT NULL, healthcheckmode TEXT NOT NULL, healthcheckintervalms INTEGER NOT NULL, healthchecktimeoutms INTEGER NOT NULL, healthythreshold INTEGER NOT NULL, " +
                "unhealthythreshold INTEGER NOT NULL, healthstatus TEXT NOT NULL, lastcheckedutc TIMESTAMPTZ NULL, lastlatencyms BIGINT NULL, lasterror TEXT NULL, consecutivesuccesses INTEGER NOT NULL DEFAULT 0, " +
                "consecutivefailures INTEGER NOT NULL DEFAULT 0, lasthealthyutc TIMESTAMPTZ NULL, lastunhealthyutc TIMESTAMPTZ NULL, nextcheckutc TIMESTAMPTZ NULL, lastunhealthynotifiedutc TIMESTAMPTZ NULL, " +
                "inputpricepermillion DOUBLE PRECISION NULL, outputpricepermillion DOUBLE PRECISION NULL, deletedutc TIMESTAMPTZ NULL, createdbyuserid TEXT NULL, createdutc TIMESTAMPTZ NOT NULL, updatedutc TIMESTAMPTZ NOT NULL);",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_aiendpoints_name ON aiendpoints (propertyid, namenormalized) WHERE deletedutc IS NULL;",
                "CREATE INDEX IF NOT EXISTS idx_aiendpoints_health ON aiendpoints (propertyid, enabled, healthcheckenabled, nextcheckutc);",
                "CREATE INDEX IF NOT EXISTS idx_aiendpoints_created ON aiendpoints (propertyid, createdutc);",
                "CREATE TABLE IF NOT EXISTS aiendpointheaders (propertyid TEXT NOT NULL, endpointid TEXT NOT NULL, namenormalized TEXT NOT NULL, name TEXT NOT NULL, valuesecret TEXT NULL, position INTEGER NOT NULL, " +
                "PRIMARY KEY (propertyid, endpointid, namenormalized));",
                "CREATE TABLE IF NOT EXISTS aiendpointhealthchecks (propertyid TEXT NOT NULL, id TEXT NOT NULL PRIMARY KEY, endpointid TEXT NOT NULL, checkedutc TIMESTAMPTZ NOT NULL, success BOOLEAN NOT NULL, " +
                "latencyms BIGINT NOT NULL, statuscode INTEGER NULL, error TEXT NULL, mode TEXT NOT NULL);",
                "CREATE INDEX IF NOT EXISTS idx_aihealth_endpoint ON aiendpointhealthchecks (propertyid, endpointid, checkedutc DESC);",

                "CREATE TABLE IF NOT EXISTS aiassistants (propertyid TEXT NOT NULL, id TEXT NOT NULL PRIMARY KEY, name TEXT NOT NULL, namenormalized TEXT NOT NULL, description TEXT NULL, enabled BOOLEAN NOT NULL, " +
                "title TEXT NOT NULL, avataremoji TEXT NULL, avatarmediaid TEXT NULL, systemprompt TEXT NOT NULL, temperature DOUBLE PRECISION NULL, topp DOUBLE PRECISION NULL, audience TEXT NOT NULL, " +
                "knowledgeenabled BOOLEAN NOT NULL, knowledgemode TEXT NOT NULL, maxcontextitems INTEGER NOT NULL, maxcontextcharacters INTEGER NOT NULL, citations BOOLEAN NOT NULL, " +
                "messagespermemberperday INTEGER NOT NULL, messagespervisitorperday INTEGER NOT NULL, maxmessagesperconversation INTEGER NOT NULL, maxtokensperreply INTEGER NOT NULL, " +
                "maxmessagecharacters INTEGER NOT NULL, conversationretentiondays INTEGER NOT NULL, streaming BOOLEAN NOT NULL, showreasoning BOOLEAN NOT NULL, disclaimer TEXT NULL, " +
                "embedenabled BOOLEAN NOT NULL, embedlauncherlabel TEXT NULL, embedposition TEXT NOT NULL, moderationmode TEXT NOT NULL, monthlybudgetusd DOUBLE PRECISION NULL, " +
                "budgethardstop BOOLEAN NOT NULL DEFAULT FALSE, deletedutc TIMESTAMPTZ NULL, createdbyuserid TEXT NULL, createdutc TIMESTAMPTZ NOT NULL, updatedutc TIMESTAMPTZ NOT NULL);",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_aiassistants_name ON aiassistants (propertyid, namenormalized) WHERE deletedutc IS NULL;",
                "CREATE INDEX IF NOT EXISTS idx_aiassistants_created ON aiassistants (propertyid, createdutc);",
                "CREATE TABLE IF NOT EXISTS aiassistantmodels (propertyid TEXT NOT NULL, assistantid TEXT NOT NULL, purpose TEXT NOT NULL, endpointid TEXT NOT NULL, PRIMARY KEY (propertyid, assistantid, purpose));",
                "CREATE INDEX IF NOT EXISTS idx_aiassistantmodels_endpoint ON aiassistantmodels (propertyid, endpointid);",
                "CREATE TABLE IF NOT EXISTS aiassistantstarterprompts (propertyid TEXT NOT NULL, assistantid TEXT NOT NULL, position INTEGER NOT NULL, prompt TEXT NOT NULL, PRIMARY KEY (propertyid, assistantid, position));",
                "CREATE TABLE IF NOT EXISTS aiassistantembedorigins (propertyid TEXT NOT NULL, assistantid TEXT NOT NULL, origin TEXT NOT NULL, PRIMARY KEY (propertyid, assistantid, origin));",
                "CREATE TABLE IF NOT EXISTS aiknowledgesources (propertyid TEXT NOT NULL, id TEXT NOT NULL PRIMARY KEY, assistantid TEXT NOT NULL, position INTEGER NOT NULL, targettype TEXT NOT NULL, " +
                "labelmatch TEXT NOT NULL, maxitems INTEGER NOT NULL);",
                "CREATE INDEX IF NOT EXISTS idx_aiknowledgesources_assistant ON aiknowledgesources (propertyid, assistantid, position);",
                "CREATE TABLE IF NOT EXISTS aiknowledgesourcelabels (propertyid TEXT NOT NULL, sourceid TEXT NOT NULL, labelnormalized TEXT NOT NULL, label TEXT NOT NULL, PRIMARY KEY (propertyid, sourceid, labelnormalized));",
                "CREATE TABLE IF NOT EXISTS aiknowledgesourcetags (propertyid TEXT NOT NULL, sourceid TEXT NOT NULL, tagkeynormalized TEXT NOT NULL, tagkey TEXT NOT NULL, tagvalue TEXT NOT NULL, " +
                "PRIMARY KEY (propertyid, sourceid, tagkeynormalized));",
                "CREATE TABLE IF NOT EXISTS aiknowledgeindexes (propertyid TEXT NOT NULL, assistantid TEXT NOT NULL, endpointid TEXT NULL, collectionid TEXT NULL, dimensions INTEGER NULL, status TEXT NOT NULL, " +
                "itemcount INTEGER NOT NULL DEFAULT 0, chunkcount INTEGER NOT NULL DEFAULT 0, lastrunutc TIMESTAMPTZ NULL, lastsuccessutc TIMESTAMPTZ NULL, lasterror TEXT NULL, " +
                "reindexrequested BOOLEAN NOT NULL DEFAULT FALSE, createdutc TIMESTAMPTZ NOT NULL, updatedutc TIMESTAMPTZ NOT NULL, PRIMARY KEY (propertyid, assistantid));",
                "CREATE TABLE IF NOT EXISTS aiknowledgeitems (propertyid TEXT NOT NULL, assistantid TEXT NOT NULL, targettype TEXT NOT NULL, targetid TEXT NOT NULL, sourceid TEXT NULL, title TEXT NULL, " +
                "contenthash TEXT NULL, audience TEXT NULL, chunkcount INTEGER NOT NULL DEFAULT 0, status TEXT NOT NULL, error TEXT NULL, indexedutc TIMESTAMPTZ NULL, updatedutc TIMESTAMPTZ NOT NULL, " +
                "PRIMARY KEY (propertyid, assistantid, targettype, targetid));",
                "CREATE INDEX IF NOT EXISTS idx_aiknowledgeitems_status ON aiknowledgeitems (propertyid, assistantid, status, updatedutc);",

                "CREATE TABLE IF NOT EXISTS aivisitors (propertyid TEXT NOT NULL, id TEXT NOT NULL PRIMARY KEY, createdutc TIMESTAMPTZ NOT NULL, lastseenutc TIMESTAMPTZ NOT NULL, iphash TEXT NULL, surface TEXT NOT NULL);",
                "CREATE INDEX IF NOT EXISTS idx_aivisitors_seen ON aivisitors (propertyid, lastseenutc);",
                "CREATE TABLE IF NOT EXISTS aiconversations (propertyid TEXT NOT NULL, id TEXT NOT NULL PRIMARY KEY, assistantid TEXT NOT NULL, userid TEXT NULL, visitorid TEXT NULL, title TEXT NULL, " +
                "titlesource TEXT NOT NULL, status TEXT NOT NULL, isarchived BOOLEAN NOT NULL DEFAULT FALSE, messagecount INTEGER NOT NULL DEFAULT 0, lastmessageutc TIMESTAMPTZ NULL, " +
                "lastmessagepreview TEXT NULL, surface TEXT NOT NULL, createdutc TIMESTAMPTZ NOT NULL, updatedutc TIMESTAMPTZ NOT NULL);",
                "CREATE INDEX IF NOT EXISTS idx_aiconv_user ON aiconversations (propertyid, userid, assistantid, lastmessageutc DESC, id DESC);",
                "CREATE INDEX IF NOT EXISTS idx_aiconv_visitor ON aiconversations (propertyid, visitorid, lastmessageutc DESC);",
                "CREATE INDEX IF NOT EXISTS idx_aiconv_retention ON aiconversations (propertyid, assistantid, lastmessageutc);",
                "CREATE INDEX IF NOT EXISTS idx_aiconv_created ON aiconversations (propertyid, createdutc);",
                "CREATE TABLE IF NOT EXISTS aimessages (propertyid TEXT NOT NULL, id TEXT NOT NULL PRIMARY KEY, conversationid TEXT NOT NULL, assistantid TEXT NOT NULL, role TEXT NOT NULL, content TEXT NOT NULL, " +
                "reasoning TEXT NULL, status TEXT NOT NULL, errorcode TEXT NULL, endpointid TEXT NULL, endpointname TEXT NULL, model TEXT NULL, usedfallback BOOLEAN NOT NULL DEFAULT FALSE, " +
                "prompttokens INTEGER NOT NULL DEFAULT 0, completiontokens INTEGER NOT NULL DEFAULT 0, reasoningtokens INTEGER NOT NULL DEFAULT 0, latencyms BIGINT NULL, ttftms BIGINT NULL, ttltms BIGINT NULL, " +
                "tokenspersecond DOUBLE PRECISION NULL, finishreason TEXT NULL, costmicros BIGINT NOT NULL DEFAULT 0, moderationverdict TEXT NULL, moderationcategory TEXT NULL, " +
                "issuperseded BOOLEAN NOT NULL DEFAULT FALSE, clientmessageid TEXT NULL, traceid TEXT NULL, replytomessageid TEXT NULL, retrievalquery TEXT NULL, createdutc TIMESTAMPTZ NOT NULL, completedutc TIMESTAMPTZ NULL);",
                "CREATE INDEX IF NOT EXISTS idx_aimessages_conv ON aimessages (propertyid, conversationid, createdutc, id);",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_aimessages_client ON aimessages (propertyid, conversationid, clientmessageid) WHERE clientmessageid IS NOT NULL;",
                "CREATE INDEX IF NOT EXISTS idx_aimessages_inflight ON aimessages (propertyid, conversationid) WHERE status IN ('Pending', 'Streaming');",
                "CREATE TABLE IF NOT EXISTS aimessagecitations (propertyid TEXT NOT NULL, messageid TEXT NOT NULL, position INTEGER NOT NULL, targettype TEXT NOT NULL, targetid TEXT NOT NULL, title TEXT NOT NULL, " +
                "path TEXT NOT NULL, score DOUBLE PRECISION NULL, PRIMARY KEY (propertyid, messageid, position));",
                "CREATE TABLE IF NOT EXISTS aimessagestages (propertyid TEXT NOT NULL, messageid TEXT NOT NULL, position INTEGER NOT NULL, stage TEXT NOT NULL, endpointid TEXT NULL, model TEXT NULL, " +
                "startedutc TIMESTAMPTZ NOT NULL, durationms BIGINT NOT NULL, success BOOLEAN NOT NULL, detail TEXT NULL, prompttokens INTEGER NULL, completiontokens INTEGER NULL, PRIMARY KEY (propertyid, messageid, position));",
                "CREATE TABLE IF NOT EXISTS aifeedback (propertyid TEXT NOT NULL, id TEXT NOT NULL PRIMARY KEY, messageid TEXT NULL, conversationid TEXT NULL, assistantid TEXT NOT NULL, userid TEXT NULL, visitorid TEXT NULL, " +
                "rating TEXT NOT NULL, note TEXT NULL, reasons TEXT NOT NULL, promptcopy TEXT NOT NULL, replycopy TEXT NOT NULL, reviewed BOOLEAN NOT NULL DEFAULT FALSE, reviewedbyuserid TEXT NULL, " +
                "reviewedutc TIMESTAMPTZ NULL, createdutc TIMESTAMPTZ NOT NULL, updatedutc TIMESTAMPTZ NOT NULL);",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_aifeedback_message ON aifeedback (propertyid, messageid) WHERE messageid IS NOT NULL;",
                "CREATE INDEX IF NOT EXISTS idx_aifeedback_queue ON aifeedback (propertyid, assistantid, reviewed, createdutc DESC);",
                "CREATE INDEX IF NOT EXISTS idx_aifeedback_created ON aifeedback (propertyid, createdutc);",

                "CREATE TABLE IF NOT EXISTS aiusagerollups (propertyid TEXT NOT NULL, periodstartutc TIMESTAMPTZ NOT NULL, granularity TEXT NOT NULL, assistantid TEXT NOT NULL, endpointid TEXT NOT NULL, " +
                "purpose TEXT NOT NULL, outcome TEXT NOT NULL, istest BOOLEAN NOT NULL, requests BIGINT NOT NULL DEFAULT 0, errors BIGINT NOT NULL DEFAULT 0, prompttokens BIGINT NOT NULL DEFAULT 0, " +
                "completiontokens BIGINT NOT NULL DEFAULT 0, reasoningtokens BIGINT NOT NULL DEFAULT 0, costmicros BIGINT NOT NULL DEFAULT 0, latencymssum BIGINT NOT NULL DEFAULT 0, latencymsmax BIGINT NOT NULL DEFAULT 0, " +
                "ttftmssum BIGINT NOT NULL DEFAULT 0, ttftcount BIGINT NOT NULL DEFAULT 0, bucket250 BIGINT NOT NULL DEFAULT 0, bucket500 BIGINT NOT NULL DEFAULT 0, bucket1000 BIGINT NOT NULL DEFAULT 0, " +
                "bucket2000 BIGINT NOT NULL DEFAULT 0, bucket5000 BIGINT NOT NULL DEFAULT 0, bucket10000 BIGINT NOT NULL DEFAULT 0, bucket30000 BIGINT NOT NULL DEFAULT 0, bucket60000 BIGINT NOT NULL DEFAULT 0, " +
                "bucketover BIGINT NOT NULL DEFAULT 0, PRIMARY KEY (propertyid, periodstartutc, granularity, assistantid, endpointid, purpose, outcome, istest));",
                "CREATE INDEX IF NOT EXISTS idx_aiusage_period ON aiusagerollups (propertyid, granularity, periodstartutc);",
                "CREATE TABLE IF NOT EXISTS aiusageapplied (propertyid TEXT NOT NULL, messageid TEXT NOT NULL, purpose TEXT NOT NULL, createdutc TIMESTAMPTZ NOT NULL, PRIMARY KEY (propertyid, messageid, purpose));",
                "CREATE INDEX IF NOT EXISTS idx_aiusageapplied_created ON aiusageapplied (propertyid, createdutc);",
                "CREATE TABLE IF NOT EXISTS aidailyusage (propertyid TEXT NOT NULL, assistantid TEXT NOT NULL, subjecttype TEXT NOT NULL, subjectid TEXT NOT NULL, date TEXT NOT NULL, messages INTEGER NOT NULL DEFAULT 0, " +
                "prompttokens BIGINT NOT NULL DEFAULT 0, completiontokens BIGINT NOT NULL DEFAULT 0, costmicros BIGINT NOT NULL DEFAULT 0, PRIMARY KEY (propertyid, assistantid, subjecttype, subjectid, date));",
                "CREATE INDEX IF NOT EXISTS idx_aidailyusage_date ON aidailyusage (propertyid, date);",
                "CREATE TABLE IF NOT EXISTS aibudgetalerts (propertyid TEXT NOT NULL, scope TEXT NOT NULL, scopeid TEXT NOT NULL, month TEXT NOT NULL, level INTEGER NOT NULL, createdutc TIMESTAMPTZ NOT NULL, " +
                "PRIMARY KEY (propertyid, scope, scopeid, month, level));"
            };
        }
    }
}

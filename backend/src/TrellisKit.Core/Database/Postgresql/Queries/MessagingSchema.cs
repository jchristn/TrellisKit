namespace TrellisKit.Core.Database.Postgresql.Queries
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// PostgreSQL statements of migrations 60, "Direct messages", and 61, "Realtime connections"
    /// (docs/PHASE5_CONTRACT.md section 16 and the as-built notes). Every table has propertyid first and every btree
    /// index starts with propertyid. Message bodies are stored as text (resolved decision 4: storage encryption only)
    /// with a generated full text vector for member search of their own conversations (resolved decision 8).
    /// </summary>
    internal static class MessagingSchema
    {
        internal const int MessagesVersion = 60;

        internal const string MessagesDescription = "Direct messages";

        internal const int RealtimeVersion = 61;

        internal const string RealtimeDescription = "Realtime connections";

        internal static List<string> MessagesStatements()
        {
            return new List<string>
            {
                "CREATE TABLE IF NOT EXISTS conversations (propertyid TEXT NOT NULL, id TEXT NOT NULL PRIMARY KEY, kind TEXT NOT NULL, directkey TEXT NULL, lastseq BIGINT NOT NULL DEFAULT 0, " +
                "lastmessageid TEXT NULL, lastmessageutc TIMESTAMPTZ NULL, createdbyuserid TEXT NULL, purgeafterutc TIMESTAMPTZ NULL, createdutc TIMESTAMPTZ NOT NULL, updatedutc TIMESTAMPTZ NOT NULL);",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_conversations_direct ON conversations (propertyid, directkey) WHERE directkey IS NOT NULL;",
                "CREATE INDEX IF NOT EXISTS idx_conversations_purge ON conversations (propertyid, purgeafterutc) WHERE purgeafterutc IS NOT NULL;",

                "CREATE TABLE IF NOT EXISTS conversationmembers (propertyid TEXT NOT NULL, id TEXT NOT NULL PRIMARY KEY, conversationid TEXT NOT NULL, userid TEXT NOT NULL, role TEXT NOT NULL, " +
                "folder TEXT NOT NULL, requeststate TEXT NULL, requestmessagecount INTEGER NOT NULL DEFAULT 0, acceptedutc TIMESTAMPTZ NULL, declinedutc TIMESTAMPTZ NULL, " +
                "lastreadseq BIGINT NOT NULL DEFAULT 0, lastdeliveredseq BIGINT NOT NULL DEFAULT 0, hiddenbeforeseq BIGINT NOT NULL DEFAULT 0, ismuted BOOLEAN NOT NULL DEFAULT FALSE, " +
                "muteduntilutc TIMESTAMPTZ NULL, lastactivityutc TIMESTAMPTZ NOT NULL, joinedutc TIMESTAMPTZ NOT NULL, leftutc TIMESTAMPTZ NULL, deletedutc TIMESTAMPTZ NULL, updatedutc TIMESTAMPTZ NOT NULL);",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_conversationmembers ON conversationmembers (propertyid, conversationid, userid);",
                "CREATE INDEX IF NOT EXISTS idx_conversationmembers_list ON conversationmembers (propertyid, userid, folder, lastactivityutc DESC, id DESC) WHERE deletedutc IS NULL AND leftutc IS NULL;",
                "CREATE INDEX IF NOT EXISTS idx_conversationmembers_changes ON conversationmembers (propertyid, userid, updatedutc, id);",

                "CREATE TABLE IF NOT EXISTS messages (propertyid TEXT NOT NULL, id TEXT NOT NULL PRIMARY KEY, conversationid TEXT NOT NULL, seq BIGINT NOT NULL, clientmessageid TEXT NOT NULL, " +
                "authoruserid TEXT NULL, body TEXT NOT NULL DEFAULT '', originalbody TEXT NULL, excerpt TEXT NOT NULL DEFAULT '', replytomessageid TEXT NULL, linkpreviewid TEXT NULL, " +
                "showlinkpreview BOOLEAN NOT NULL DEFAULT TRUE, attachmentcount INTEGER NOT NULL DEFAULT 0, hasvideo BOOLEAN NOT NULL DEFAULT FALSE, state TEXT NOT NULL, editedutc TIMESTAMPTZ NULL, " +
                "deletedutc TIMESTAMPTZ NULL, moderatedutc TIMESTAMPTZ NULL, moderatedbyuserid TEXT NULL, reactiontotal INTEGER NOT NULL DEFAULT 0, " +
                "searchvector tsvector GENERATED ALWAYS AS (to_tsvector('simple', coalesce(body, ''))) STORED, createdutc TIMESTAMPTZ NOT NULL, updatedutc TIMESTAMPTZ NOT NULL);",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_messages_seq ON messages (propertyid, conversationid, seq);",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_messages_client ON messages (propertyid, conversationid, authoruserid, clientmessageid);",
                "CREATE INDEX IF NOT EXISTS idx_messages_author ON messages (propertyid, authoruserid, createdutc);",
                "CREATE INDEX IF NOT EXISTS idx_messages_retention ON messages (propertyid, createdutc) WHERE state = 'Visible';",
                // GIN cannot lead with the propertyid text column without btree_gin; every search query also filters propertyid
                // and the caller's conversations, so the planner combines this index with those predicates.
                "CREATE INDEX IF NOT EXISTS idx_messages_search ON messages USING GIN (searchvector);",

                "CREATE TABLE IF NOT EXISTS messageattachments (propertyid TEXT NOT NULL, messageid TEXT NOT NULL, mediaid TEXT NOT NULL, position INTEGER NOT NULL, PRIMARY KEY (propertyid, messageid, mediaid));",
                "CREATE INDEX IF NOT EXISTS idx_messageattachments_media ON messageattachments (propertyid, mediaid);",

                "CREATE TABLE IF NOT EXISTS messagereportsnapshots (propertyid TEXT NOT NULL, id TEXT NOT NULL PRIMARY KEY, reportid TEXT NOT NULL, conversationid TEXT NOT NULL, " +
                "reporteruserid TEXT NULL, capturedutc TIMESTAMPTZ NOT NULL, purgeafterutc TIMESTAMPTZ NULL);",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_messagereportsnapshots_report ON messagereportsnapshots (propertyid, reportid);",
                "CREATE INDEX IF NOT EXISTS idx_messagereportsnapshots_purge ON messagereportsnapshots (propertyid, purgeafterutc) WHERE purgeafterutc IS NOT NULL;",
                "CREATE TABLE IF NOT EXISTS messagereportsnapshotitems (propertyid TEXT NOT NULL, id TEXT NOT NULL PRIMARY KEY, snapshotid TEXT NOT NULL, messageid TEXT NOT NULL, seq BIGINT NOT NULL, " +
                "authoruserid TEXT NULL, body TEXT NOT NULL DEFAULT '', originalbody TEXT NULL, state TEXT NOT NULL, isreported BOOLEAN NOT NULL DEFAULT FALSE, attachmentsummary TEXT NULL, " +
                "editedutc TIMESTAMPTZ NULL, createdutc TIMESTAMPTZ NOT NULL);",
                "CREATE INDEX IF NOT EXISTS idx_messagereportsnapshotitems ON messagereportsnapshotitems (propertyid, snapshotid, seq);",

                "CREATE TABLE IF NOT EXISTS messagingsettings (propertyid TEXT NOT NULL, userid TEXT NOT NULL, dmpolicy TEXT NOT NULL, readreceipts BOOLEAN NOT NULL DEFAULT TRUE, " +
                "typingindicators BOOLEAN NOT NULL DEFAULT TRUE, pushpreview TEXT NOT NULL, ignorenotificationhours BOOLEAN NOT NULL DEFAULT FALSE, updatedutc TIMESTAMPTZ NOT NULL, PRIMARY KEY (propertyid, userid));",
                "CREATE TABLE IF NOT EXISTS messagingcontacts (propertyid TEXT NOT NULL, userid TEXT NOT NULL, contactuserid TEXT NOT NULL, source TEXT NOT NULL, createdutc TIMESTAMPTZ NOT NULL, " +
                "PRIMARY KEY (propertyid, userid, contactuserid));",
                "CREATE INDEX IF NOT EXISTS idx_messagingcontacts_contact ON messagingcontacts (propertyid, contactuserid);",
                "CREATE TABLE IF NOT EXISTS messageemailstate (propertyid TEXT NOT NULL, userid TEXT NOT NULL, lastsentutc TIMESTAMPTZ NULL, lastcheckedutc TIMESTAMPTZ NOT NULL, PRIMARY KEY (propertyid, userid));",
                "CREATE TABLE IF NOT EXISTS messagepushstate (propertyid TEXT NOT NULL, conversationid TEXT NOT NULL, userid TEXT NOT NULL, lastpushutc TIMESTAMPTZ NOT NULL, lastpushseq BIGINT NOT NULL DEFAULT 0, " +
                "PRIMARY KEY (propertyid, conversationid, userid));",
                "CREATE TABLE IF NOT EXISTS messaginglapses (propertyid TEXT NOT NULL, userid TEXT NOT NULL, lapsedutc TIMESTAMPTZ NOT NULL, purgedutc TIMESTAMPTZ NULL, PRIMARY KEY (propertyid, userid));",
                "CREATE INDEX IF NOT EXISTS idx_messaginglapses_due ON messaginglapses (propertyid, lapsedutc) WHERE purgedutc IS NULL;",

                // Direct message and ticket media use per-object signed URLs instead of the member-wide grant cookie.
                "ALTER TABLE mediaattachments ADD COLUMN IF NOT EXISTS purpose TEXT NOT NULL DEFAULT 'Content';",
                "ALTER TABLE mediaattachments ADD COLUMN IF NOT EXISTS scopeid TEXT NULL;",
                "CREATE INDEX IF NOT EXISTS idx_mediaattachments_scope ON mediaattachments (propertyid, purpose, scopeid) WHERE scopeid IS NOT NULL;"
            };
        }

        internal static List<string> RealtimeStatements()
        {
            return new List<string>
            {
                "CREATE TABLE IF NOT EXISTS realtimeconnections (propertyid TEXT NOT NULL, id TEXT NOT NULL PRIMARY KEY, userid TEXT NOT NULL, sessionid TEXT NOT NULL, taskid TEXT NOT NULL, " +
                "client TEXT NOT NULL, appstate TEXT NOT NULL, activeconversationid TEXT NULL, connectedutc TIMESTAMPTZ NOT NULL, lastseenutc TIMESTAMPTZ NOT NULL);",
                "CREATE INDEX IF NOT EXISTS idx_realtime_user ON realtimeconnections (propertyid, userid, appstate, lastseenutc);",
                // Deployment-wide prune and drain (reviewed unbound statements, docs/MULTIPROPERTY_CONTRACT.md section 4.3 rule 1).
                "CREATE INDEX IF NOT EXISTS idx_realtime_stale ON realtimeconnections (lastseenutc);",
                "CREATE INDEX IF NOT EXISTS idx_realtime_task ON realtimeconnections (taskid);"
            };
        }
    }
}

namespace TrellisKit.Core.Database.Postgresql.Queries
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// PostgreSQL statements of migration 64, "Broadcasts" (docs/PHASE5_CONTRACT.md sections 8 and 16). Segment filters
    /// are rows, never JSON. Every table has propertyid first and every index starts with propertyid.
    /// </summary>
    internal static class BroadcastsSchema
    {
        internal const int Version = 64;

        internal const string Description = "Broadcasts";

        internal static List<string> Statements()
        {
            return new List<string>
            {
                "CREATE TABLE IF NOT EXISTS broadcastsegments (propertyid TEXT NOT NULL, id TEXT NOT NULL PRIMARY KEY, name TEXT NOT NULL, description TEXT NULL, createdbyuserid TEXT NULL, " +
                "deletedutc TIMESTAMPTZ NULL, createdutc TIMESTAMPTZ NOT NULL, updatedutc TIMESTAMPTZ NOT NULL);",
                "CREATE INDEX IF NOT EXISTS idx_broadcastsegments_list ON broadcastsegments (propertyid, createdutc DESC, id DESC) WHERE deletedutc IS NULL;",
                "CREATE TABLE IF NOT EXISTS broadcastsegmentfilters (propertyid TEXT NOT NULL, id TEXT NOT NULL PRIMARY KEY, segmentid TEXT NULL, broadcastid TEXT NULL, field TEXT NOT NULL, " +
                "operator TEXT NOT NULL, value TEXT NOT NULL, valuekind TEXT NOT NULL DEFAULT 'String', position INTEGER NOT NULL);",
                "CREATE INDEX IF NOT EXISTS idx_broadcastsegmentfilters_segment ON broadcastsegmentfilters (propertyid, segmentid, position) WHERE segmentid IS NOT NULL;",
                "CREATE INDEX IF NOT EXISTS idx_broadcastsegmentfilters_broadcast ON broadcastsegmentfilters (propertyid, broadcastid, position) WHERE broadcastid IS NOT NULL;",

                "CREATE TABLE IF NOT EXISTS broadcasts (propertyid TEXT NOT NULL, id TEXT NOT NULL PRIMARY KEY, title TEXT NOT NULL, inapp BOOLEAN NOT NULL DEFAULT FALSE, push BOOLEAN NOT NULL DEFAULT FALSE, " +
                "email BOOLEAN NOT NULL DEFAULT FALSE, inapptitle TEXT NULL, inappbody TEXT NULL, pushtitle TEXT NULL, pushbody TEXT NULL, emailsubject TEXT NULL, emailpreheader TEXT NULL, " +
                "emailbody TEXT NULL, ctalabel TEXT NULL, ctapath TEXT NULL, segmentid TEXT NULL, category TEXT NOT NULL, essentialreason TEXT NULL, status TEXT NOT NULL, istest BOOLEAN NOT NULL DEFAULT FALSE, " +
                "sendatutc TIMESTAMPTZ NULL, startedutc TIMESTAMPTZ NULL, sentutc TIMESTAMPTZ NULL, canceledutc TIMESTAMPTZ NULL, targeted INTEGER NOT NULL DEFAULT 0, cursoruserid TEXT NULL, " +
                "statinappcreated INTEGER NOT NULL DEFAULT 0, statpushsent INTEGER NOT NULL DEFAULT 0, statpushfailed INTEGER NOT NULL DEFAULT 0, statemailqueued INTEGER NOT NULL DEFAULT 0, " +
                "statemailsent INTEGER NOT NULL DEFAULT 0, statemaildelivered INTEGER NOT NULL DEFAULT 0, statemailfailed INTEGER NOT NULL DEFAULT 0, statemailsuppressed INTEGER NOT NULL DEFAULT 0, " +
                "statskippedpreferences INTEGER NOT NULL DEFAULT 0, createdbyuserid TEXT NULL, createdutc TIMESTAMPTZ NOT NULL, updatedutc TIMESTAMPTZ NOT NULL);",
                "CREATE INDEX IF NOT EXISTS idx_broadcasts_list ON broadcasts (propertyid, createdutc DESC, id DESC);",
                "CREATE INDEX IF NOT EXISTS idx_broadcasts_due ON broadcasts (propertyid, sendatutc) WHERE status = 'Scheduled';",
                "CREATE INDEX IF NOT EXISTS idx_broadcasts_sending ON broadcasts (propertyid, status) WHERE status = 'Sending';",
                "CREATE INDEX IF NOT EXISTS idx_broadcasts_sent ON broadcasts (propertyid, sentutc) WHERE sentutc IS NOT NULL;",

                "CREATE TABLE IF NOT EXISTS broadcastdeliveries (propertyid TEXT NOT NULL, id TEXT NOT NULL PRIMARY KEY, broadcastid TEXT NOT NULL, userid TEXT NOT NULL, inappstatus TEXT NOT NULL, " +
                "pushstatus TEXT NOT NULL, emailstatus TEXT NOT NULL, notificationid TEXT NULL, emailmessageid TEXT NULL, emailqueuedutc TIMESTAMPTZ NULL, createdutc TIMESTAMPTZ NOT NULL, updatedutc TIMESTAMPTZ NOT NULL);",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_broadcastdeliveries ON broadcastdeliveries (propertyid, broadcastid, userid);",
                "CREATE INDEX IF NOT EXISTS idx_broadcastdeliveries_status ON broadcastdeliveries (propertyid, broadcastid, emailstatus);",
                "CREATE INDEX IF NOT EXISTS idx_broadcastdeliveries_user ON broadcastdeliveries (propertyid, userid, createdutc DESC, id DESC);",
                "CREATE INDEX IF NOT EXISTS idx_broadcastdeliveries_email ON broadcastdeliveries (propertyid, emailmessageid) WHERE emailmessageid IS NOT NULL;",
                "CREATE INDEX IF NOT EXISTS idx_broadcastdeliveries_emailqueued ON broadcastdeliveries (propertyid, emailqueuedutc) WHERE emailqueuedutc IS NOT NULL;",
                "CREATE INDEX IF NOT EXISTS idx_broadcastdeliveries_list ON broadcastdeliveries (propertyid, broadcastid, createdutc DESC, id DESC);"
            };
        }
    }
}

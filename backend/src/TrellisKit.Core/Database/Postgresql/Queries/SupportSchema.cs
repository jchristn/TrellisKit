namespace TrellisKit.Core.Database.Postgresql.Queries
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// PostgreSQL statements of migration 62, "Support tickets" (docs/PHASE5_CONTRACT.md sections 6 and 16). Every table
    /// has propertyid first and every index starts with propertyid.
    /// </summary>
    internal static class SupportSchema
    {
        internal const int Version = 62;

        internal const string Description = "Support tickets";

        internal static List<string> Statements()
        {
            return new List<string>
            {
                "CREATE TABLE IF NOT EXISTS tickettypes (propertyid TEXT NOT NULL, id TEXT NOT NULL PRIMARY KEY, key TEXT NOT NULL, label TEXT NOT NULL, description TEXT NULL, helptext TEXT NULL, " +
                "sortorder INTEGER NOT NULL DEFAULT 0, isretired BOOLEAN NOT NULL DEFAULT FALSE, createdutc TIMESTAMPTZ NOT NULL, updatedutc TIMESTAMPTZ NOT NULL);",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_tickettypes_key ON tickettypes (propertyid, lower(key));",
                "CREATE INDEX IF NOT EXISTS idx_tickettypes_order ON tickettypes (propertyid, isretired, sortorder, id);",
                "CREATE TABLE IF NOT EXISTS tickettyperecipients (propertyid TEXT NOT NULL, tickettypeid TEXT NOT NULL, email TEXT NOT NULL, PRIMARY KEY (propertyid, tickettypeid, email));",

                "CREATE TABLE IF NOT EXISTS ticketcounters (propertyid TEXT NOT NULL PRIMARY KEY, nextnumber BIGINT NOT NULL);",

                "CREATE TABLE IF NOT EXISTS tickets (propertyid TEXT NOT NULL, id TEXT NOT NULL PRIMARY KEY, number BIGINT NOT NULL, typekey TEXT NOT NULL, title TEXT NOT NULL, body TEXT NOT NULL, " +
                "status TEXT NOT NULL, priority TEXT NOT NULL, requesteruserid TEXT NULL, assigneeuserid TEXT NULL, ideaid TEXT NULL, includediagnostics BOOLEAN NOT NULL DEFAULT FALSE, " +
                "deviceclient TEXT NULL, deviceclientversion TEXT NULL, deviceplatform TEXT NULL, deviceosversion TEXT NULL, devicemodel TEXT NULL, deviceupdateid TEXT NULL, devicelocale TEXT NULL, " +
                "devicetimezone TEXT NULL, membershipactive BOOLEAN NOT NULL DEFAULT FALSE, membershipsource TEXT NULL, membershipstore TEXT NULL, membershipexpiresutc TIMESTAMPTZ NULL, " +
                "firstresponsedueutc TIMESTAMPTZ NULL, firstresponseutc TIMESTAMPTZ NULL, overduenotifiedutc TIMESTAMPTZ NULL, lastactivityutc TIMESTAMPTZ NOT NULL, lastmemberreplyutc TIMESTAMPTZ NULL, " +
                "resolvedutc TIMESTAMPTZ NULL, closedutc TIMESTAMPTZ NULL, createdutc TIMESTAMPTZ NOT NULL, updatedutc TIMESTAMPTZ NOT NULL);",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_tickets_number ON tickets (propertyid, number);",
                "CREATE INDEX IF NOT EXISTS idx_tickets_inbox ON tickets (propertyid, status, lastactivityutc DESC, id DESC);",
                "CREATE INDEX IF NOT EXISTS idx_tickets_assignee ON tickets (propertyid, assigneeuserid, status);",
                "CREATE INDEX IF NOT EXISTS idx_tickets_requester ON tickets (propertyid, requesteruserid, lastactivityutc DESC);",
                "CREATE INDEX IF NOT EXISTS idx_tickets_due ON tickets (propertyid, firstresponsedueutc) WHERE firstresponseutc IS NULL AND status <> 'Closed';",
                "CREATE INDEX IF NOT EXISTS idx_tickets_resolved ON tickets (propertyid, resolvedutc) WHERE status = 'Resolved';",

                "CREATE TABLE IF NOT EXISTS ticketmessages (propertyid TEXT NOT NULL, id TEXT NOT NULL PRIMARY KEY, ticketid TEXT NOT NULL, kind TEXT NOT NULL, authoruserid TEXT NULL, " +
                "authorrole TEXT NOT NULL, body TEXT NOT NULL, statuschange TEXT NULL, createdutc TIMESTAMPTZ NOT NULL);",
                "CREATE INDEX IF NOT EXISTS idx_ticketmessages_ticket ON ticketmessages (propertyid, ticketid, createdutc, id);",
                "CREATE INDEX IF NOT EXISTS idx_ticketmessages_author ON ticketmessages (propertyid, authoruserid, createdutc);"
            };
        }
    }
}

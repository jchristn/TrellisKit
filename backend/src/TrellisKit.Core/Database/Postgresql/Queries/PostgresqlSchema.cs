namespace TrellisKit.Core.Database.Postgresql.Queries
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// PostgreSQL schema migrations. Append new migrations; never edit an applied one.
    /// </summary>
    internal static class PostgresqlSchema
    {
        internal static readonly IReadOnlyList<SchemaMigration> Migrations = new List<SchemaMigration>
        {
            new SchemaMigration(1, "Request history", new List<string>
            {
                "CREATE TABLE IF NOT EXISTS requesthistory (" +
                "id TEXT NOT NULL PRIMARY KEY, " +
                "userid TEXT NULL, " +
                "principalname TEXT NULL, " +
                "method TEXT NOT NULL, " +
                "path TEXT NOT NULL, " +
                "url TEXT NOT NULL, " +
                "statuscode INTEGER NOT NULL, " +
                "durationms DOUBLE PRECISION NOT NULL, " +
                "sourceip TEXT NULL, " +
                "requestheaders TEXT NULL, " +
                "requestbody TEXT NULL, " +
                "requestbodybytes BIGINT NOT NULL DEFAULT 0, " +
                "requestbodytruncated BOOLEAN NOT NULL DEFAULT FALSE, " +
                "responseheaders TEXT NULL, " +
                "responsebody TEXT NULL, " +
                "responsebodybytes BIGINT NOT NULL DEFAULT 0, " +
                "responsebodytruncated BOOLEAN NOT NULL DEFAULT FALSE, " +
                "createdutc TIMESTAMPTZ NOT NULL, " +
                "completedutc TIMESTAMPTZ NULL);",
                "CREATE INDEX IF NOT EXISTS idx_requesthistory_createdutc ON requesthistory (createdutc);",
                "CREATE INDEX IF NOT EXISTS idx_requesthistory_userid_createdutc ON requesthistory (userid, createdutc);",
                "CREATE INDEX IF NOT EXISTS idx_requesthistory_statuscode ON requesthistory (statuscode);"
            }),
            new SchemaMigration(2, "Identity and membership", new List<string>
            {
                "CREATE TABLE IF NOT EXISTS users (id TEXT NOT NULL PRIMARY KEY, email TEXT NULL, emailverified BOOLEAN NOT NULL DEFAULT FALSE, emailverifiedutc TIMESTAMPTZ NULL, displayname TEXT NOT NULL, handle TEXT NULL, avatarfile TEXT NULL, bio TEXT NULL, rank TEXT NULL, ranklevel INTEGER NOT NULL DEFAULT 0, affiliation TEXT NULL, city TEXT NULL, region TEXT NULL, country TEXT NULL, goalsother TEXT NULL, ageattestedutc TIMESTAMPTZ NULL, canposttofeed BOOLEAN NOT NULL DEFAULT FALSE, canpromotetofeed BOOLEAN NOT NULL DEFAULT FALSE, canuploadvideo BOOLEAN NOT NULL DEFAULT TRUE, cansenddirectmessages BOOLEAN NOT NULL DEFAULT TRUE, ismuted BOOLEAN NOT NULL DEFAULT FALSE, muteduntilutc TIMESTAMPTZ NULL, mutedreason TEXT NULL, isbanned BOOLEAN NOT NULL DEFAULT FALSE, bannedreason TEXT NULL, bannedutc TIMESTAMPTZ NULL, isdeleted BOOLEAN NOT NULL DEFAULT FALSE, deletedutc TIMESTAMPTZ NULL, lastseenutc TIMESTAMPTZ NULL, stripecustomerid TEXT NULL, storeaccounttoken TEXT NOT NULL, failedlogincount INTEGER NOT NULL DEFAULT 0, failedloginwindowutc TIMESTAMPTZ NULL, lockeduntilutc TIMESTAMPTZ NULL, createdutc TIMESTAMPTZ NOT NULL, updatedutc TIMESTAMPTZ NOT NULL);",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_users_email ON users (lower(email));",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_users_handle ON users (lower(handle));",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_users_storeaccounttoken ON users (lower(storeaccounttoken));",
                "CREATE INDEX IF NOT EXISTS idx_users_stripecustomerid ON users (stripecustomerid);",
                "CREATE INDEX IF NOT EXISTS idx_users_createdutc ON users (createdutc);",
                "CREATE INDEX IF NOT EXISTS idx_users_lastseenutc ON users (lastseenutc);",
                "CREATE INDEX IF NOT EXISTS idx_users_rank ON users (rank);",
                "CREATE INDEX IF NOT EXISTS idx_users_isbanned ON users (isbanned);",
                "CREATE INDEX IF NOT EXISTS idx_users_isdeleted ON users (isdeleted);",
                "CREATE TABLE IF NOT EXISTS credentials (id TEXT NOT NULL PRIMARY KEY, userid TEXT NOT NULL REFERENCES users (id), passwordhash TEXT NOT NULL, createdutc TIMESTAMPTZ NOT NULL, updatedutc TIMESTAMPTZ NOT NULL);",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_credentials_userid ON credentials (userid);",
                "CREATE TABLE IF NOT EXISTS externalidentities (id TEXT NOT NULL PRIMARY KEY, userid TEXT NOT NULL REFERENCES users (id), provider TEXT NOT NULL, subject TEXT NOT NULL, email TEXT NULL, emailverified BOOLEAN NOT NULL DEFAULT FALSE, createdutc TIMESTAMPTZ NOT NULL, lastusedutc TIMESTAMPTZ NULL);",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_externalidentities_provider_subject ON externalidentities (provider, subject);",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_externalidentities_userid_provider ON externalidentities (userid, provider);",
                "CREATE TABLE IF NOT EXISTS authsessions (id TEXT NOT NULL PRIMARY KEY, userid TEXT NOT NULL REFERENCES users (id), clienttype TEXT NOT NULL, sourceip TEXT NULL, useragent TEXT NULL, createdutc TIMESTAMPTZ NOT NULL, lastusedutc TIMESTAMPTZ NOT NULL, expiresutc TIMESTAMPTZ NOT NULL, revokedutc TIMESTAMPTZ NULL, revokedreason TEXT NULL);",
                "CREATE INDEX IF NOT EXISTS idx_authsessions_userid ON authsessions (userid, revokedutc);",
                "CREATE INDEX IF NOT EXISTS idx_authsessions_expiresutc ON authsessions (expiresutc);",
                "CREATE TABLE IF NOT EXISTS refreshtokens (id TEXT NOT NULL PRIMARY KEY, sessionid TEXT NOT NULL REFERENCES authsessions (id), userid TEXT NOT NULL, tokenhash TEXT NOT NULL, createdutc TIMESTAMPTZ NOT NULL, expiresutc TIMESTAMPTZ NOT NULL, usedutc TIMESTAMPTZ NULL, replacedbyid TEXT NULL);",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_refreshtokens_tokenhash ON refreshtokens (tokenhash);",
                "CREATE INDEX IF NOT EXISTS idx_refreshtokens_sessionid ON refreshtokens (sessionid);",
                "CREATE INDEX IF NOT EXISTS idx_refreshtokens_expiresutc ON refreshtokens (expiresutc);",
                "CREATE TABLE IF NOT EXISTS onetimetokens (id TEXT NOT NULL PRIMARY KEY, userid TEXT NOT NULL REFERENCES users (id), purpose TEXT NOT NULL, tokenhash TEXT NOT NULL, email TEXT NULL, createdutc TIMESTAMPTZ NOT NULL, expiresutc TIMESTAMPTZ NOT NULL, usedutc TIMESTAMPTZ NULL);",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_onetimetokens_tokenhash ON onetimetokens (tokenhash);",
                "CREATE INDEX IF NOT EXISTS idx_onetimetokens_userid_purpose ON onetimetokens (userid, purpose);",
                "CREATE TABLE IF NOT EXISTS roleassignments (id TEXT NOT NULL PRIMARY KEY, userid TEXT NOT NULL REFERENCES users (id), role TEXT NOT NULL, resourcetype TEXT NOT NULL, resourceid TEXT NOT NULL, grantedbyuserid TEXT NULL, createdutc TIMESTAMPTZ NOT NULL);",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_roleassignments_scope ON roleassignments (userid, role, resourcetype, resourceid);",
                "CREATE INDEX IF NOT EXISTS idx_roleassignments_role ON roleassignments (role);",
                "CREATE TABLE IF NOT EXISTS rankhistoryentries (id TEXT NOT NULL PRIMARY KEY, userid TEXT NOT NULL REFERENCES users (id), position INTEGER NOT NULL, rank TEXT NOT NULL, awardedbyname TEXT NULL, awardedbyuserid TEXT NULL, affiliation TEXT NULL, dateawarded TEXT NULL, createdutc TIMESTAMPTZ NOT NULL);",
                "CREATE INDEX IF NOT EXISTS idx_rankhistoryentries_userid ON rankhistoryentries (userid, position);",
                "CREATE TABLE IF NOT EXISTS usergoals (userid TEXT NOT NULL REFERENCES users (id), goal TEXT NOT NULL, position INTEGER NOT NULL, PRIMARY KEY (userid, goal));",
                "CREATE TABLE IF NOT EXISTS subscriptions (id TEXT NOT NULL PRIMARY KEY, userid TEXT NOT NULL REFERENCES users (id), store TEXT NOT NULL, storesubscriptionid TEXT NOT NULL, productid TEXT NOT NULL, status TEXT NOT NULL, currentperiodstartutc TIMESTAMPTZ NULL, currentperiodendutc TIMESTAMPTZ NULL, autorenew BOOLEAN NOT NULL DEFAULT FALSE, environment TEXT NOT NULL, latesttransactionid TEXT NULL, createdutc TIMESTAMPTZ NOT NULL, updatedutc TIMESTAMPTZ NOT NULL, lasteventutc TIMESTAMPTZ NULL);",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_subscriptions_store_storeid ON subscriptions (store, storesubscriptionid);",
                "CREATE INDEX IF NOT EXISTS idx_subscriptions_userid ON subscriptions (userid, createdutc);",
                "CREATE INDEX IF NOT EXISTS idx_subscriptions_status ON subscriptions (status, lasteventutc);",
                "CREATE INDEX IF NOT EXISTS idx_subscriptions_periodend ON subscriptions (currentperiodendutc);",
                "CREATE TABLE IF NOT EXISTS subscriptionevents (id TEXT NOT NULL PRIMARY KEY, subscriptionid TEXT NULL, userid TEXT NULL, store TEXT NOT NULL, storeeventid TEXT NOT NULL, source TEXT NOT NULL, eventtype TEXT NOT NULL, receivedutc TIMESTAMPTZ NOT NULL, processedutc TIMESTAMPTZ NULL, outcome TEXT NOT NULL, rawpayloadjson TEXT NULL);",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_subscriptionevents_store_eventid ON subscriptionevents (store, storeeventid);",
                "CREATE INDEX IF NOT EXISTS idx_subscriptionevents_subscriptionid ON subscriptionevents (subscriptionid, receivedutc);",
                "CREATE INDEX IF NOT EXISTS idx_subscriptionevents_userid ON subscriptionevents (userid, receivedutc);",
                "CREATE TABLE IF NOT EXISTS entitlements (id TEXT NOT NULL PRIMARY KEY, userid TEXT NOT NULL REFERENCES users (id), source TEXT NOT NULL, sourceid TEXT NOT NULL, startsutc TIMESTAMPTZ NOT NULL, endsutc TIMESTAMPTZ NULL, revokedutc TIMESTAMPTZ NULL, revokereason TEXT NULL, reason TEXT NULL, granttype TEXT NULL, grantedbyuserid TEXT NULL, createdutc TIMESTAMPTZ NOT NULL, updatedutc TIMESTAMPTZ NOT NULL);",
                "CREATE INDEX IF NOT EXISTS idx_entitlements_userid ON entitlements (userid, startsutc, endsutc);",
                "CREATE INDEX IF NOT EXISTS idx_entitlements_source ON entitlements (source, sourceid);",
                "CREATE INDEX IF NOT EXISTS idx_entitlements_endsutc ON entitlements (endsutc);",
                "CREATE TABLE IF NOT EXISTS signupcodebatches (id TEXT NOT NULL PRIMARY KEY, name TEXT NOT NULL, quantity INTEGER NOT NULL, granttype TEXT NOT NULL, grantmonths INTEGER NULL, grantendutc TIMESTAMPTZ NULL, expiresutc TIMESTAMPTZ NULL, createdbyuserid TEXT NULL, createdutc TIMESTAMPTZ NOT NULL);",
                "CREATE TABLE IF NOT EXISTS signupcodes (id TEXT NOT NULL PRIMARY KEY, code TEXT NOT NULL, name TEXT NOT NULL, description TEXT NULL, notes TEXT NULL, granttype TEXT NOT NULL, grantmonths INTEGER NULL, grantendutc TIMESTAMPTZ NULL, maxredemptions INTEGER NULL, redemptioncount INTEGER NOT NULL DEFAULT 0, startsutc TIMESTAMPTZ NULL, expiresutc TIMESTAMPTZ NULL, isactive BOOLEAN NOT NULL DEFAULT TRUE, batchid TEXT NULL REFERENCES signupcodebatches (id), createdbyuserid TEXT NULL, createdutc TIMESTAMPTZ NOT NULL, updatedutc TIMESTAMPTZ NOT NULL, lastredeemedutc TIMESTAMPTZ NULL);",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_signupcodes_code ON signupcodes (lower(code));",
                "CREATE INDEX IF NOT EXISTS idx_signupcodes_batchid ON signupcodes (batchid);",
                "CREATE INDEX IF NOT EXISTS idx_signupcodes_createdutc ON signupcodes (createdutc);",
                "CREATE INDEX IF NOT EXISTS idx_signupcodes_status ON signupcodes (isactive, startsutc, expiresutc);",
                "CREATE TABLE IF NOT EXISTS signupcodetags (signupcodeid TEXT NOT NULL REFERENCES signupcodes (id), tag TEXT NOT NULL, PRIMARY KEY (signupcodeid, tag));",
                "CREATE INDEX IF NOT EXISTS idx_signupcodetags_tag ON signupcodetags (tag);",
                "CREATE TABLE IF NOT EXISTS signupcoderedemptions (id TEXT NOT NULL PRIMARY KEY, signupcodeid TEXT NOT NULL REFERENCES signupcodes (id), userid TEXT NOT NULL REFERENCES users (id), redeemedutc TIMESTAMPTZ NOT NULL, membershipstartsutc TIMESTAMPTZ NOT NULL, membershipendsutc TIMESTAMPTZ NULL, entitlementid TEXT NOT NULL, sourceip TEXT NULL, useragent TEXT NULL, isnewaccount BOOLEAN NOT NULL DEFAULT FALSE, revokedutc TIMESTAMPTZ NULL, revokedbyuserid TEXT NULL, revokereason TEXT NULL, reminderstage INTEGER NOT NULL DEFAULT 0);",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_signupcoderedemptions_code_user ON signupcoderedemptions (signupcodeid, userid);",
                "CREATE INDEX IF NOT EXISTS idx_signupcoderedemptions_userid ON signupcoderedemptions (userid, redeemedutc);",
                "CREATE INDEX IF NOT EXISTS idx_signupcoderedemptions_redeemedutc ON signupcoderedemptions (signupcodeid, redeemedutc);",
                "CREATE INDEX IF NOT EXISTS idx_signupcoderedemptions_endsutc ON signupcoderedemptions (membershipendsutc, reminderstage);",
                "CREATE TABLE IF NOT EXISTS auditrecords (id TEXT NOT NULL PRIMARY KEY, actoruserid TEXT NULL, actorname TEXT NULL, action TEXT NOT NULL, targettype TEXT NULL, targetid TEXT NULL, summary TEXT NULL, sourceip TEXT NULL, createdutc TIMESTAMPTZ NOT NULL);",
                "CREATE INDEX IF NOT EXISTS idx_auditrecords_createdutc ON auditrecords (createdutc);",
                "CREATE INDEX IF NOT EXISTS idx_auditrecords_actor ON auditrecords (actoruserid, createdutc);",
                "CREATE INDEX IF NOT EXISTS idx_auditrecords_target ON auditrecords (targettype, targetid, createdutc);",
                "CREATE INDEX IF NOT EXISTS idx_auditrecords_action ON auditrecords (action, createdutc);"
            }),
            new SchemaMigration(3, "Reserved (no changes)", new List<string>()),
            new SchemaMigration(4, "Feed and engagement", new List<string>
            {
                "CREATE TABLE IF NOT EXISTS feedposts (id TEXT NOT NULL PRIMARY KEY, authoruserid TEXT NOT NULL REFERENCES users (id), kind TEXT NOT NULL, sourcetype TEXT NULL, sourceid TEXT NULL, title TEXT NULL, body TEXT NOT NULL, excerpt TEXT NOT NULL, visibility TEXT NOT NULL, linkpreviewid TEXT NULL, showlinkpreview BOOLEAN NOT NULL DEFAULT TRUE, commentsenabled BOOLEAN NOT NULL DEFAULT TRUE, publishatutc TIMESTAMPTZ NULL, publishedutc TIMESTAMPTZ NULL, ispinned BOOLEAN NOT NULL DEFAULT FALSE, pinnedutc TIMESTAMPTZ NULL, pinnedbyuserid TEXT NULL, editedutc TIMESTAMPTZ NULL, revisioncount INTEGER NOT NULL DEFAULT 0, attachmentcount INTEGER NOT NULL DEFAULT 0, hasvideo BOOLEAN NOT NULL DEFAULT FALSE, commentcount INTEGER NOT NULL DEFAULT 0, reactiontotal INTEGER NOT NULL DEFAULT 0, openreportcount INTEGER NOT NULL DEFAULT 0, moderationstate TEXT NOT NULL DEFAULT 'None', moderatedutc TIMESTAMPTZ NULL, moderatedbyuserid TEXT NULL, moderationreason TEXT NULL, deletedutc TIMESTAMPTZ NULL, deletedbyuserid TEXT NULL, purgedutc TIMESTAMPTZ NULL, createdutc TIMESTAMPTZ NOT NULL, updatedutc TIMESTAMPTZ NOT NULL);",
                "CREATE TABLE IF NOT EXISTS feedpostrevisions (id TEXT NOT NULL PRIMARY KEY, feedpostid TEXT NOT NULL REFERENCES feedposts (id), revision INTEGER NOT NULL, title TEXT NULL, body TEXT NULL, attachmentsummary TEXT NULL, editedbyuserid TEXT NULL, createdutc TIMESTAMPTZ NOT NULL);",
                "CREATE TABLE IF NOT EXISTS mediaattachments (id TEXT NOT NULL PRIMARY KEY, owneruserid TEXT NOT NULL REFERENCES users (id), kind TEXT NOT NULL, status TEXT NOT NULL, failurereason TEXT NULL, contenttype TEXT NOT NULL, originalfilename TEXT NULL, sizebytes BIGINT NOT NULL, declareddurationseconds DOUBLE PRECISION NULL, durationseconds DOUBLE PRECISION NULL, width INTEGER NULL, height INTEGER NULL, alttext TEXT NULL, blurhash TEXT NULL, storagemode TEXT NOT NULL, sourcebucket TEXT NULL, sourcekey TEXT NOT NULL, multipartuploadid TEXT NULL, partsizebytes BIGINT NOT NULL DEFAULT 0, partcount INTEGER NOT NULL DEFAULT 0, outputprefix TEXT NULL, hlsmanifestfile TEXT NULL, mp4file TEXT NULL, posterfile TEXT NULL, posterthumbfile TEXT NULL, isretained BOOLEAN NOT NULL DEFAULT FALSE, ispublic BOOLEAN NOT NULL DEFAULT FALSE, processor TEXT NULL, processorjobid TEXT NULL, attempts INTEGER NOT NULL DEFAULT 0, claimedby TEXT NULL, claimuntilutc TIMESTAMPTZ NULL, processingstartedutc TIMESTAMPTZ NULL, readyutc TIMESTAMPTZ NULL, uploadexpiresutc TIMESTAMPTZ NOT NULL, attachedtargettype TEXT NULL, attachedtargetid TEXT NULL, attachedposition INTEGER NOT NULL DEFAULT 0, attachedutc TIMESTAMPTZ NULL, detachedutc TIMESTAMPTZ NULL, deletedutc TIMESTAMPTZ NULL, createdutc TIMESTAMPTZ NOT NULL, updatedutc TIMESTAMPTZ NOT NULL);",
                "CREATE TABLE IF NOT EXISTS mediavariants (mediaid TEXT NOT NULL REFERENCES mediaattachments (id), name TEXT NOT NULL, format TEXT NOT NULL, width INTEGER NOT NULL, height INTEGER NOT NULL, sizebytes BIGINT NOT NULL, filename TEXT NOT NULL, PRIMARY KEY (mediaid, name, format));",
                "CREATE TABLE IF NOT EXISTS comments (id TEXT NOT NULL PRIMARY KEY, targettype TEXT NOT NULL, targetid TEXT NOT NULL, parentcommentid TEXT NULL, rootcommentid TEXT NOT NULL, replytocommentid TEXT NULL, path TEXT COLLATE \"C\" NOT NULL, depth INTEGER NOT NULL, authoruserid TEXT NOT NULL REFERENCES users (id), body TEXT NULL, excerpt TEXT NULL, linkpreviewid TEXT NULL, showlinkpreview BOOLEAN NOT NULL DEFAULT TRUE, attachmentcount INTEGER NOT NULL DEFAULT 0, childcount INTEGER NOT NULL DEFAULT 0, descendantcount INTEGER NOT NULL DEFAULT 0, reactiontotal INTEGER NOT NULL DEFAULT 0, score INTEGER NOT NULL DEFAULT 0, editedutc TIMESTAMPTZ NULL, revisioncount INTEGER NOT NULL DEFAULT 0, moderationstate TEXT NOT NULL DEFAULT 'None', moderatedutc TIMESTAMPTZ NULL, moderatedbyuserid TEXT NULL, moderationreason TEXT NULL, deletedutc TIMESTAMPTZ NULL, deletedreason TEXT NULL, purgedutc TIMESTAMPTZ NULL, createdutc TIMESTAMPTZ NOT NULL, updatedutc TIMESTAMPTZ NOT NULL);",
                "CREATE TABLE IF NOT EXISTS commentrevisions (id TEXT NOT NULL PRIMARY KEY, commentid TEXT NOT NULL REFERENCES comments (id), revision INTEGER NOT NULL, body TEXT NULL, attachmentsummary TEXT NULL, createdutc TIMESTAMPTZ NOT NULL);",
                "CREATE TABLE IF NOT EXISTS reactions (id TEXT NOT NULL PRIMARY KEY, targettype TEXT NOT NULL, targetid TEXT NOT NULL, userid TEXT NOT NULL, reactionkey TEXT NOT NULL, createdutc TIMESTAMPTZ NOT NULL);",
                "CREATE TABLE IF NOT EXISTS reactioncounts (targettype TEXT NOT NULL, targetid TEXT NOT NULL, reactionkey TEXT NOT NULL, count INTEGER NOT NULL DEFAULT 0, PRIMARY KEY (targettype, targetid, reactionkey));",
                "CREATE TABLE IF NOT EXISTS mentions (id TEXT NOT NULL PRIMARY KEY, sourcetype TEXT NOT NULL, sourceid TEXT NOT NULL, mentioneduserid TEXT NOT NULL, handle TEXT NOT NULL, authoruserid TEXT NOT NULL, notifiedutc TIMESTAMPTZ NULL, createdutc TIMESTAMPTZ NOT NULL);",
                "CREATE TABLE IF NOT EXISTS notifications (id TEXT NOT NULL PRIMARY KEY, recipientuserid TEXT NOT NULL, type TEXT NOT NULL, category TEXT NOT NULL, actoruserid TEXT NULL, actorcount INTEGER NOT NULL DEFAULT 0, targettype TEXT NOT NULL, targetid TEXT NOT NULL, feedpostid TEXT NULL, commentid TEXT NULL, parentcommentid TEXT NULL, title TEXT NOT NULL, body TEXT NULL, path TEXT NOT NULL, groupkey TEXT NULL, readutc TIMESTAMPTZ NULL, pushstate TEXT NOT NULL DEFAULT 'None', lastpushedutc TIMESTAMPTZ NULL, createdutc TIMESTAMPTZ NOT NULL, updatedutc TIMESTAMPTZ NOT NULL);",
                "CREATE TABLE IF NOT EXISTS notificationactors (notificationid TEXT NOT NULL REFERENCES notifications (id), userid TEXT NOT NULL, createdutc TIMESTAMPTZ NOT NULL, PRIMARY KEY (notificationid, userid));",
                "CREATE TABLE IF NOT EXISTS notificationpreferences (userid TEXT NOT NULL, category TEXT NOT NULL, inapp BOOLEAN NOT NULL DEFAULT TRUE, push BOOLEAN NOT NULL DEFAULT TRUE, email BOOLEAN NOT NULL DEFAULT TRUE, updatedutc TIMESTAMPTZ NOT NULL, PRIMARY KEY (userid, category));",
                "CREATE TABLE IF NOT EXISTS notificationhours (userid TEXT NOT NULL PRIMARY KEY, enabled BOOLEAN NOT NULL DEFAULT TRUE, startminute INTEGER NOT NULL DEFAULT 420, endminute INTEGER NOT NULL DEFAULT 1320, timezone TEXT NULL, pushpaused BOOLEAN NOT NULL DEFAULT FALSE, updatedutc TIMESTAMPTZ NOT NULL);",
                "CREATE TABLE IF NOT EXISTS pushtokens (id TEXT NOT NULL PRIMARY KEY, userid TEXT NOT NULL, sessionid TEXT NOT NULL REFERENCES authsessions (id), token TEXT NOT NULL, platform TEXT NOT NULL, deviceid TEXT NULL, devicename TEXT NULL, appversion TEXT NULL, failurecount INTEGER NOT NULL DEFAULT 0, disabledutc TIMESTAMPTZ NULL, lastsuccessutc TIMESTAMPTZ NULL, createdutc TIMESTAMPTZ NOT NULL, lastseenutc TIMESTAMPTZ NOT NULL);",
                "CREATE TABLE IF NOT EXISTS pushdeliveries (id TEXT NOT NULL PRIMARY KEY, notificationid TEXT NOT NULL, pushtokenid TEXT NOT NULL, ticketid TEXT NULL, status TEXT NOT NULL, error TEXT NULL, createdutc TIMESTAMPTZ NOT NULL, receiptcheckedutc TIMESTAMPTZ NULL);",
                "CREATE TABLE IF NOT EXISTS notificationfanouts (id TEXT NOT NULL PRIMARY KEY, type TEXT NOT NULL, sourceid TEXT NOT NULL, lastuserid TEXT NULL, status TEXT NOT NULL, createdcount INTEGER NOT NULL DEFAULT 0, claimedby TEXT NULL, claimuntilutc TIMESTAMPTZ NULL, createdutc TIMESTAMPTZ NOT NULL, completedutc TIMESTAMPTZ NULL);",
                "CREATE TABLE IF NOT EXISTS reports (id TEXT NOT NULL PRIMARY KEY, reporteruserid TEXT NULL, targettype TEXT NOT NULL, targetid TEXT NOT NULL, targetauthoruserid TEXT NULL, scopetype TEXT NOT NULL, scopeid TEXT NOT NULL, reason TEXT NOT NULL, details TEXT NULL, status TEXT NOT NULL, resolvedbyuserid TEXT NULL, resolvedutc TIMESTAMPTZ NULL, resolutionnote TEXT NULL, moderationactionid TEXT NULL, dueutc TIMESTAMPTZ NOT NULL, overduenotifiedutc TIMESTAMPTZ NULL, createdutc TIMESTAMPTZ NOT NULL);",
                "CREATE TABLE IF NOT EXISTS moderationactions (id TEXT NOT NULL PRIMARY KEY, moderatoruserid TEXT NULL, targettype TEXT NOT NULL, targetid TEXT NOT NULL, targetauthoruserid TEXT NULL, action TEXT NOT NULL, reason TEXT NOT NULL, note TEXT NULL, notifiedauthor BOOLEAN NOT NULL DEFAULT FALSE, createdutc TIMESTAMPTZ NOT NULL);",
                "CREATE TABLE IF NOT EXISTS blocks (blockeruserid TEXT NOT NULL, blockeduserid TEXT NOT NULL, createdutc TIMESTAMPTZ NOT NULL, PRIMARY KEY (blockeruserid, blockeduserid));",
                "CREATE TABLE IF NOT EXISTS userpermissions (userid TEXT NOT NULL PRIMARY KEY, canpost BOOLEAN NOT NULL DEFAULT TRUE, canpostmedia BOOLEAN NOT NULL DEFAULT TRUE, updatedutc TIMESTAMPTZ NOT NULL, updatedbyuserid TEXT NULL);",
                "CREATE TABLE IF NOT EXISTS linkpreviews (id TEXT NOT NULL PRIMARY KEY, urlhash TEXT NOT NULL, url TEXT NOT NULL, canonicalurl TEXT NULL, sitename TEXT NULL, title TEXT NULL, description TEXT NULL, imagekey TEXT NULL, imagewidth INTEGER NULL, imageheight INTEGER NULL, status TEXT NOT NULL, failurereason TEXT NULL, fetchedutc TIMESTAMPTZ NOT NULL, expiresutc TIMESTAMPTZ NOT NULL);",
                "CREATE TABLE IF NOT EXISTS wordfilterterms (id TEXT NOT NULL PRIMARY KEY, term TEXT NOT NULL, normalizedterm TEXT NOT NULL, matchtype TEXT NOT NULL, action TEXT NOT NULL, note TEXT NULL, createdbyuserid TEXT NULL, createdutc TIMESTAMPTZ NOT NULL, updatedutc TIMESTAMPTZ NOT NULL);",
                "CREATE TABLE IF NOT EXISTS wordfilterhits (id TEXT NOT NULL PRIMARY KEY, termid TEXT NOT NULL, targettype TEXT NOT NULL, targetid TEXT NULL, field TEXT NOT NULL, action TEXT NOT NULL, createdutc TIMESTAMPTZ NOT NULL);",
                "CREATE TABLE IF NOT EXISTS displaynamewords (userid TEXT NOT NULL, word TEXT NOT NULL, PRIMARY KEY (userid, word));",
                "CREATE TABLE IF NOT EXISTS emailsends (id TEXT NOT NULL PRIMARY KEY, userid TEXT NOT NULL, category TEXT NOT NULL, sourceid TEXT NOT NULL, status TEXT NOT NULL, createdutc TIMESTAMPTZ NOT NULL);",
                "CREATE TABLE IF NOT EXISTS jobleases (jobname TEXT NOT NULL PRIMARY KEY, holder TEXT NOT NULL, leaseuntilutc TIMESTAMPTZ NOT NULL, updatedutc TIMESTAMPTZ NOT NULL);",
                "CREATE INDEX IF NOT EXISTS idx_feedposts_feed ON feedposts (publishedutc DESC, id DESC) WHERE deletedutc IS NULL AND moderationstate = 'None' AND publishedutc IS NOT NULL;",
                "CREATE INDEX IF NOT EXISTS idx_feedposts_pinned ON feedposts (ispinned, pinnedutc);",
                "CREATE INDEX IF NOT EXISTS idx_feedposts_schedule ON feedposts (publishatutc) WHERE publishedutc IS NULL AND deletedutc IS NULL;",
                "CREATE INDEX IF NOT EXISTS idx_feedposts_author ON feedposts (authoruserid, createdutc);",
                "CREATE INDEX IF NOT EXISTS idx_feedposts_public ON feedposts (publishedutc DESC, id DESC) WHERE visibility = 'Public' AND deletedutc IS NULL AND moderationstate = 'None';",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_feedpostrevisions ON feedpostrevisions (feedpostid, revision);",
                "CREATE INDEX IF NOT EXISTS idx_media_owner ON mediaattachments (owneruserid, createdutc);",
                "CREATE INDEX IF NOT EXISTS idx_media_status ON mediaattachments (status, claimuntilutc);",
                "CREATE INDEX IF NOT EXISTS idx_media_target ON mediaattachments (attachedtargettype, attachedtargetid, attachedposition);",
                "CREATE INDEX IF NOT EXISTS idx_media_cleanup ON mediaattachments (status, uploadexpiresutc);",
                "CREATE INDEX IF NOT EXISTS idx_media_unattached ON mediaattachments (attachedtargetid, createdutc) WHERE attachedtargetid IS NULL;",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_media_jobid ON mediaattachments (processorjobid) WHERE processorjobid IS NOT NULL;",
                "CREATE INDEX IF NOT EXISTS idx_comments_top ON comments (targettype, targetid, parentcommentid, score DESC, createdutc, id);",
                "CREATE INDEX IF NOT EXISTS idx_comments_time ON comments (targettype, targetid, parentcommentid, createdutc, id);",
                "CREATE INDEX IF NOT EXISTS idx_comments_parent ON comments (parentcommentid, createdutc, id);",
                "CREATE INDEX IF NOT EXISTS idx_comments_path ON comments (path);",
                "CREATE INDEX IF NOT EXISTS idx_comments_author ON comments (authoruserid, createdutc);",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_commentrevisions ON commentrevisions (commentid, revision);",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_reactions ON reactions (targettype, targetid, userid, reactionkey);",
                "CREATE INDEX IF NOT EXISTS idx_reactions_list ON reactions (targettype, targetid, reactionkey, createdutc);",
                "CREATE INDEX IF NOT EXISTS idx_reactions_user ON reactions (userid, createdutc);",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_mentions ON mentions (sourcetype, sourceid, mentioneduserid);",
                "CREATE INDEX IF NOT EXISTS idx_mentions_user ON mentions (mentioneduserid, createdutc);",
                "CREATE INDEX IF NOT EXISTS idx_notifications_list ON notifications (recipientuserid, updatedutc DESC, id DESC);",
                "CREATE INDEX IF NOT EXISTS idx_notifications_unread ON notifications (recipientuserid) WHERE readutc IS NULL;",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_notifications_group ON notifications (recipientuserid, groupkey) WHERE readutc IS NULL AND groupkey IS NOT NULL;",
                "CREATE INDEX IF NOT EXISTS idx_notifications_push ON notifications (pushstate, createdutc) WHERE pushstate = 'Pending';",
                "CREATE INDEX IF NOT EXISTS idx_notifications_prune ON notifications (readutc);",
                "CREATE INDEX IF NOT EXISTS idx_notifications_created ON notifications (createdutc);",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_pushtokens_token ON pushtokens (token);",
                "CREATE INDEX IF NOT EXISTS idx_pushtokens_user ON pushtokens (userid, lastseenutc);",
                "CREATE INDEX IF NOT EXISTS idx_pushtokens_session ON pushtokens (sessionid);",
                "CREATE INDEX IF NOT EXISTS idx_pushdeliveries_receipts ON pushdeliveries (status, createdutc);",
                "CREATE INDEX IF NOT EXISTS idx_pushdeliveries_notification ON pushdeliveries (notificationid);",
                "CREATE INDEX IF NOT EXISTS idx_notificationfanouts_status ON notificationfanouts (status, claimuntilutc);",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_notificationfanouts_source ON notificationfanouts (type, sourceid);",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_reports_open ON reports (reporteruserid, targettype, targetid) WHERE status = 'Open';",
                "CREATE INDEX IF NOT EXISTS idx_reports_queue ON reports (status, scopetype, createdutc);",
                "CREATE INDEX IF NOT EXISTS idx_reports_target ON reports (targettype, targetid, status);",
                "CREATE INDEX IF NOT EXISTS idx_reports_due ON reports (status, dueutc);",
                "CREATE INDEX IF NOT EXISTS idx_reports_reporter ON reports (reporteruserid, createdutc);",
                "CREATE INDEX IF NOT EXISTS idx_modactions_target ON moderationactions (targettype, targetid, createdutc);",
                "CREATE INDEX IF NOT EXISTS idx_modactions_moderator ON moderationactions (moderatoruserid, createdutc);",
                "CREATE INDEX IF NOT EXISTS idx_modactions_author ON moderationactions (targetauthoruserid, createdutc);",
                "CREATE INDEX IF NOT EXISTS idx_blocks_blocked ON blocks (blockeduserid);",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_linkpreviews_urlhash ON linkpreviews (urlhash);",
                "CREATE INDEX IF NOT EXISTS idx_linkpreviews_expires ON linkpreviews (expiresutc);",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_wordfilterterms_normalized ON wordfilterterms (normalizedterm);",
                "CREATE INDEX IF NOT EXISTS idx_wordfilterhits_term ON wordfilterhits (termid, createdutc);",
                "CREATE INDEX IF NOT EXISTS idx_wordfilterhits_created ON wordfilterhits (createdutc);",
                "CREATE INDEX IF NOT EXISTS idx_displaynamewords_word ON displaynamewords (word text_pattern_ops);",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_emailsends ON emailsends (userid, category, sourceid);",
                "CREATE INDEX IF NOT EXISTS idx_emailsends_user ON emailsends (userid, category, createdutc);",
                "CREATE INDEX IF NOT EXISTS idx_users_handle_prefix ON users (lower(handle) text_pattern_ops);",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_reports_system_open ON reports (targettype, targetid) WHERE status = 'Open' AND reporteruserid IS NULL;"
            }),
            new SchemaMigration(5, "Labels, tags, and content audience", new List<string>
            {
                "CREATE TABLE IF NOT EXISTS labels (id TEXT NOT NULL PRIMARY KEY, targettype TEXT NOT NULL, targetid TEXT NOT NULL, label TEXT NOT NULL, labelnormalized TEXT NOT NULL, createdutc TIMESTAMPTZ NOT NULL);",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_labels_target ON labels (targettype, targetid, labelnormalized);",
                "CREATE INDEX IF NOT EXISTS idx_labels_lookup ON labels (targettype, labelnormalized, targetid);",
                "CREATE TABLE IF NOT EXISTS tags (id TEXT NOT NULL PRIMARY KEY, targettype TEXT NOT NULL, targetid TEXT NOT NULL, tagkey TEXT NOT NULL, tagkeynormalized TEXT NOT NULL, tagvalue TEXT NOT NULL, createdutc TIMESTAMPTZ NOT NULL, updatedutc TIMESTAMPTZ NOT NULL);",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_tags_target ON tags (targettype, targetid, tagkeynormalized);",
                "CREATE INDEX IF NOT EXISTS idx_tags_lookup ON tags (targettype, tagkeynormalized, tagvalue, targetid);",
                "ALTER TABLE feedposts ADD COLUMN IF NOT EXISTS audience TEXT NOT NULL DEFAULT 'Members';",
                "INSERT INTO labels (id, targettype, targetid, label, labelnormalized, createdutc) SELECT 'lbl_' || substr(md5(random()::text || clock_timestamp()::text || t.signupcodeid || t.tag), 1, 28), 'SignupCode', t.signupcodeid, t.tag, lower(t.tag), c.createdutc FROM signupcodetags t JOIN signupcodes c ON c.id = t.signupcodeid WHERE 1 = 1 ON CONFLICT DO NOTHING;",
                "DELETE FROM signupcodetags;",
                "CREATE TABLE IF NOT EXISTS accountexports (id TEXT NOT NULL PRIMARY KEY, userid TEXT NOT NULL, status TEXT NOT NULL, createdutc TIMESTAMPTZ NOT NULL, startedutc TIMESTAMPTZ NULL, completedutc TIMESTAMPTZ NULL, expiresutc TIMESTAMPTZ NULL, sizebytes BIGINT NULL, error TEXT NULL, claimedby TEXT NULL, claimuntilutc TIMESTAMPTZ NULL);",
                "CREATE INDEX IF NOT EXISTS idx_accountexports_user ON accountexports (userid, createdutc);",
                "CREATE INDEX IF NOT EXISTS idx_accountexports_claim ON accountexports (status, claimuntilutc);",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_accountexports_active ON accountexports (userid) WHERE status IN ('Pending', 'Running');"
            }),
            new SchemaMigration(6, "Property registry and scoping", PropertyScoping()),
            new SchemaMigration(7, "Break-glass moderation actor", new List<string>
            {
                "ALTER TABLE moderationactions ADD COLUMN IF NOT EXISTS breakglass BOOLEAN NOT NULL DEFAULT FALSE;"
            }),
            new SchemaMigration(8, "Configurable profile fields", new List<string>
            {
                // docs/MULTIPROPERTY_CONTRACT.md sections 9.7 and 13. Definitions are seeded and legacy values backfilled at
                // startup per property (ProfileFieldSeeder), because field ids are generated per property.
                "CREATE TABLE IF NOT EXISTS profilefields (id TEXT NOT NULL PRIMARY KEY, propertyid TEXT NOT NULL, key TEXT NOT NULL, builtin BOOLEAN NOT NULL DEFAULT FALSE, label TEXT NOT NULL, helptext TEXT NOT NULL DEFAULT '', placeholder TEXT NOT NULL DEFAULT '', type TEXT NOT NULL, collectat TEXT NOT NULL, editableby TEXT NOT NULL, visibility TEXT NOT NULL, showonprofile BOOLEAN NOT NULL DEFAULT TRUE, profileorder INTEGER NOT NULL DEFAULT 0, showoncard BOOLEAN NOT NULL DEFAULT FALSE, searchable BOOLEAN NOT NULL DEFAULT FALSE, filterable BOOLEAN NOT NULL DEFAULT FALSE, enabled BOOLEAN NOT NULL DEFAULT TRUE, required BOOLEAN NOT NULL DEFAULT FALSE, minlength INTEGER NULL, maxlength INTEGER NULL, minvalue DOUBLE PRECISION NULL, maxvalue DOUBLE PRECISION NULL, integeronly BOOLEAN NOT NULL DEFAULT FALSE, pattern TEXT NULL, patternmessage TEXT NULL, minentries INTEGER NULL, maxentries INTEGER NULL, mindate TEXT NULL, maxdate TEXT NULL, notinfuture BOOLEAN NOT NULL DEFAULT FALSE, levellabel TEXT NULL, maxlevel INTEGER NULL, valuecount INTEGER NOT NULL DEFAULT 0, revision INTEGER NOT NULL DEFAULT 1, createdbyuserid TEXT NULL, updatedbyuserid TEXT NULL, createdutc TIMESTAMPTZ NOT NULL, updatedutc TIMESTAMPTZ NOT NULL);",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_profilefields_key ON profilefields (propertyid, lower(key));",
                "CREATE INDEX IF NOT EXISTS idx_profilefields_order ON profilefields (propertyid, enabled, profileorder);",
                "CREATE INDEX IF NOT EXISTS idx_profilefields_created ON profilefields (propertyid, createdutc);",
                "CREATE TABLE IF NOT EXISTS profilefieldoptions (id TEXT NOT NULL PRIMARY KEY, propertyid TEXT NOT NULL, fieldid TEXT NOT NULL, subfieldid TEXT NULL, key TEXT NOT NULL, label TEXT NOT NULL, color TEXT NULL, maxlevel INTEGER NULL, freetext BOOLEAN NOT NULL DEFAULT FALSE, retired BOOLEAN NOT NULL DEFAULT FALSE, position INTEGER NOT NULL, createdutc TIMESTAMPTZ NOT NULL, updatedutc TIMESTAMPTZ NOT NULL);",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_profilefieldoptions_key ON profilefieldoptions (propertyid, fieldid, COALESCE(subfieldid, ''), lower(key));",
                "CREATE INDEX IF NOT EXISTS idx_profilefieldoptions_order ON profilefieldoptions (propertyid, fieldid, position);",
                "CREATE TABLE IF NOT EXISTS profilefieldsubfields (id TEXT NOT NULL PRIMARY KEY, propertyid TEXT NOT NULL, fieldid TEXT NOT NULL, key TEXT NOT NULL, label TEXT NOT NULL, type TEXT NOT NULL, required BOOLEAN NOT NULL DEFAULT FALSE, maxlength INTEGER NULL, minvalue DOUBLE PRECISION NULL, maxvalue DOUBLE PRECISION NULL, notinfuture BOOLEAN NOT NULL DEFAULT FALSE, rankfieldkey TEXT NULL, withlevel BOOLEAN NOT NULL DEFAULT FALSE, position INTEGER NOT NULL, createdutc TIMESTAMPTZ NOT NULL, updatedutc TIMESTAMPTZ NOT NULL);",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_profilefieldsubfields_key ON profilefieldsubfields (propertyid, fieldid, lower(key));",
                "CREATE TABLE IF NOT EXISTS profilevalues (propertyid TEXT NOT NULL, userid TEXT NOT NULL, fieldid TEXT NOT NULL, valuetext TEXT NULL, valuenormalized TEXT NULL, valuenumber DOUBLE PRECISION NULL, valuedate TEXT NULL, valuebool BOOLEAN NULL, valuelevel INTEGER NULL, othertext TEXT NULL, updatedbyuserid TEXT NULL, updatedutc TIMESTAMPTZ NOT NULL, PRIMARY KEY (propertyid, userid, fieldid));",
                "CREATE INDEX IF NOT EXISTS idx_profilevalues_text ON profilevalues (propertyid, fieldid, valuenormalized text_pattern_ops, userid);",
                "CREATE INDEX IF NOT EXISTS idx_profilevalues_number ON profilevalues (propertyid, fieldid, valuenumber, userid);",
                "CREATE INDEX IF NOT EXISTS idx_profilevalues_date ON profilevalues (propertyid, fieldid, valuedate, userid);",
                "CREATE INDEX IF NOT EXISTS idx_profilevalues_key ON profilevalues (propertyid, fieldid, valuetext, valuelevel, userid);",
                "CREATE INDEX IF NOT EXISTS idx_profilevalues_user ON profilevalues (propertyid, userid);",
                "CREATE TABLE IF NOT EXISTS profilevalueoptions (propertyid TEXT NOT NULL, userid TEXT NOT NULL, fieldid TEXT NOT NULL, optionkey TEXT NOT NULL, position INTEGER NOT NULL, PRIMARY KEY (propertyid, userid, fieldid, optionkey));",
                "CREATE INDEX IF NOT EXISTS idx_profilevalueoptions_option ON profilevalueoptions (propertyid, fieldid, optionkey, userid);",
                "CREATE TABLE IF NOT EXISTS profilehistoryentries (id TEXT NOT NULL PRIMARY KEY, propertyid TEXT NOT NULL, userid TEXT NOT NULL, fieldid TEXT NOT NULL, position INTEGER NOT NULL, createdutc TIMESTAMPTZ NOT NULL, updatedutc TIMESTAMPTZ NOT NULL);",
                "CREATE INDEX IF NOT EXISTS idx_profilehistory_user ON profilehistoryentries (propertyid, userid, fieldid, position);",
                "CREATE TABLE IF NOT EXISTS profilehistoryvalues (propertyid TEXT NOT NULL, entryid TEXT NOT NULL, subfieldkey TEXT NOT NULL, valuetext TEXT NULL, valuenormalized TEXT NULL, valuenumber DOUBLE PRECISION NULL, valuedate TEXT NULL, valuebool BOOLEAN NULL, valuelevel INTEGER NULL, valueuserid TEXT NULL, PRIMARY KEY (propertyid, entryid, subfieldkey));",
                "CREATE INDEX IF NOT EXISTS idx_profilehistoryvalues_user ON profilehistoryvalues (propertyid, valueuserid) WHERE valueuserid IS NOT NULL;",
                "CREATE TABLE IF NOT EXISTS profilesearchwords (propertyid TEXT NOT NULL, userid TEXT NOT NULL, fieldid TEXT NOT NULL, word TEXT NOT NULL, PRIMARY KEY (propertyid, userid, fieldid, word));",
                "CREATE INDEX IF NOT EXISTS idx_profilesearchwords_word ON profilesearchwords (propertyid, word text_pattern_ops, userid);"
            }),
            new SchemaMigration(9, "Property settings, administrative email, and domain verification", new List<string>
            {
                // docs/TRELLISKIT.md section 12 and docs/MULTIPROPERTY_CONTRACT.md sections 5.5 and 13 (resolved decision 6).
                "ALTER TABLE properties ADD COLUMN IF NOT EXISTS profilefieldsseededutc TIMESTAMPTZ NULL;",
                "ALTER TABLE properties ADD COLUMN IF NOT EXISTS profilefieldshash TEXT NULL;",
                "ALTER TABLE propertydomains ADD COLUMN IF NOT EXISTS source TEXT NOT NULL DEFAULT 'Platform';",
                "ALTER TABLE propertydomains ADD COLUMN IF NOT EXISTS verificationtoken TEXT NULL;",
                "ALTER TABLE propertydomains ADD COLUMN IF NOT EXISTS verificationcheckedutc TIMESTAMPTZ NULL;",
                "ALTER TABLE propertydomains ADD COLUMN IF NOT EXISTS verificationerror TEXT NULL;",
                "UPDATE propertydomains SET verifiedutc = createdutc WHERE verifiedutc IS NULL;",
                "CREATE TABLE IF NOT EXISTS propertysettings (propertyid TEXT NOT NULL, settingkey TEXT NOT NULL, valuetext TEXT NULL, ciphertext TEXT NULL, nonce TEXT NULL, keyid TEXT NULL, updatedbyuserid TEXT NULL, updatedutc TIMESTAMPTZ NOT NULL, PRIMARY KEY (propertyid, settingkey));",
                "CREATE TABLE IF NOT EXISTS emailmessages (id TEXT NOT NULL PRIMARY KEY, propertyid TEXT NOT NULL, template TEXT NOT NULL, category TEXT NOT NULL, recipient TEXT NOT NULL, recipientnormalized TEXT NOT NULL, userid TEXT NULL, subject TEXT NOT NULL, status TEXT NOT NULL, provider TEXT NULL, providermessageid TEXT NULL, attempts INTEGER NOT NULL DEFAULT 0, nextattemptutc TIMESTAMPTZ NULL, lasterror TEXT NULL, idempotencykey TEXT NULL, payloadciphertext TEXT NULL, payloadnonce TEXT NULL, payloadkeyid TEXT NULL, claimedby TEXT NULL, claimuntilutc TIMESTAMPTZ NULL, createdutc TIMESTAMPTZ NOT NULL, sentutc TIMESTAMPTZ NULL, deliveredutc TIMESTAMPTZ NULL, failedutc TIMESTAMPTZ NULL, updatedutc TIMESTAMPTZ NOT NULL);",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_emailmessages_idempotency ON emailmessages (propertyid, idempotencykey) WHERE idempotencykey IS NOT NULL;",
                "CREATE INDEX IF NOT EXISTS idx_emailmessages_created ON emailmessages (propertyid, createdutc);",
                "CREATE INDEX IF NOT EXISTS idx_emailmessages_due ON emailmessages (propertyid, status, nextattemptutc);",
                "CREATE INDEX IF NOT EXISTS idx_emailmessages_provider ON emailmessages (propertyid, providermessageid);",
                "CREATE INDEX IF NOT EXISTS idx_emailmessages_recipient ON emailmessages (propertyid, recipientnormalized, createdutc);",
                "CREATE TABLE IF NOT EXISTS emailsuppressions (id TEXT NOT NULL PRIMARY KEY, propertyid TEXT NOT NULL, email TEXT NOT NULL, emailnormalized TEXT NOT NULL, reason TEXT NOT NULL, source TEXT NOT NULL, note TEXT NULL, createdutc TIMESTAMPTZ NOT NULL);",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_emailsuppressions_email ON emailsuppressions (propertyid, emailnormalized);",
                "CREATE INDEX IF NOT EXISTS idx_emailsuppressions_created ON emailsuppressions (propertyid, createdutc);",
                "CREATE TABLE IF NOT EXISTS emailtemplateoverrides (propertyid TEXT NOT NULL, template TEXT NOT NULL, subject TEXT NULL, preheader TEXT NULL, heading TEXT NULL, body TEXT NULL, updatedbyuserid TEXT NULL, updatedutc TIMESTAMPTZ NOT NULL, PRIMARY KEY (propertyid, template));",
                "CREATE TABLE IF NOT EXISTS emailstaffevents (propertyid TEXT NOT NULL, template TEXT NOT NULL, enabled BOOLEAN NOT NULL DEFAULT TRUE, recipients TEXT NOT NULL DEFAULT '', updatedbyuserid TEXT NULL, updatedutc TIMESTAMPTZ NOT NULL, PRIMARY KEY (propertyid, template));"
            }),
            new SchemaMigration(10, "Access models", new List<string>
            {
                // docs/ACCESS_MODELS_CONTRACT.md section 1 and docs/MULTIPROPERTY_CONTRACT.md section 10.3. The row is seeded
                // per property at startup from property.json access (AccessPolicyService), so nothing is inserted here.
                "CREATE TABLE IF NOT EXISTS propertyaccess (propertyid TEXT NOT NULL PRIMARY KEY, model TEXT NOT NULL, anonymousbrowsing BOOLEAN NOT NULL DEFAULT FALSE, defaultaudience TEXT NULL, sourcehash TEXT NULL, revision INTEGER NOT NULL DEFAULT 1, updatedbyuserid TEXT NULL, createdutc TIMESTAMPTZ NOT NULL, updatedutc TIMESTAMPTZ NOT NULL);",
                "CREATE TABLE IF NOT EXISTS propertyaccessfeatures (propertyid TEXT NOT NULL, feature TEXT NOT NULL, createdutc TIMESTAMPTZ NOT NULL, PRIMARY KEY (propertyid, feature));",
                "UPDATE feedposts SET audience = 'Public' WHERE visibility = 'Public' AND audience <> 'Public';",
                "CREATE INDEX IF NOT EXISTS idx_feedposts_audience ON feedposts (propertyid, audience, publishedutc DESC, id DESC) WHERE deletedutc IS NULL AND moderationstate = 'None' AND publishedutc IS NOT NULL;"
            }),
            new SchemaMigration(20, "Forums", ForumsMigration()),
            new SchemaMigration(LiveUpdatesSchema.Version, LiveUpdatesSchema.Description, LiveUpdatesSchema.Statements()),
            new SchemaMigration(40, "AI models and assistants", AiSchema.Statements()),
            new SchemaMigration(41, "Assistant moderation snapshots and forum email digests", new List<string>
            {
                // docs/AI_CONTRACT.md section 12.3: one moderation copy per reported or withheld assistant message.
                "CREATE TABLE IF NOT EXISTS aimoderationsnapshots (propertyid TEXT NOT NULL, id TEXT NOT NULL PRIMARY KEY, reportid TEXT NULL, messageid TEXT NOT NULL, conversationid TEXT NOT NULL, " +
                "assistantid TEXT NOT NULL, owneruserid TEXT NULL, reason TEXT NOT NULL, endpointid TEXT NULL, endpointname TEXT NULL, model TEXT NULL, messagecount INTEGER NOT NULL DEFAULT 0, " +
                "createdutc TIMESTAMPTZ NOT NULL, expiresutc TIMESTAMPTZ NOT NULL);",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_aimodsnapshots_message ON aimoderationsnapshots (propertyid, messageid);",
                "CREATE INDEX IF NOT EXISTS idx_aimodsnapshots_expires ON aimoderationsnapshots (propertyid, expiresutc);",
                "CREATE TABLE IF NOT EXISTS aimoderationsnapshotmessages (propertyid TEXT NOT NULL, snapshotid TEXT NOT NULL, position INTEGER NOT NULL, messageid TEXT NOT NULL, role TEXT NOT NULL, " +
                "content TEXT NOT NULL, status TEXT NOT NULL, createdutc TIMESTAMPTZ NOT NULL, PRIMARY KEY (propertyid, snapshotid, position));",
                "CREATE INDEX IF NOT EXISTS idx_aimodsnapmessages_created ON aimoderationsnapshotmessages (propertyid, snapshotid, createdutc, messageid);",
                "CREATE TABLE IF NOT EXISTS aimoderationsnapshotcitations (propertyid TEXT NOT NULL, snapshotid TEXT NOT NULL, position INTEGER NOT NULL, targettype TEXT NOT NULL, targetid TEXT NOT NULL, " +
                "title TEXT NOT NULL, path TEXT NOT NULL, PRIMARY KEY (propertyid, snapshotid, position));",
                // docs/PHASE3_CONTRACT.md section 9.3: Following and Forums email digests, one row per member and category.
                "CREATE TABLE IF NOT EXISTS forumemaildigests (propertyid TEXT NOT NULL, userid TEXT NOT NULL, category TEXT NOT NULL, lastsentutc TIMESTAMPTZ NULL, lastcheckedutc TIMESTAMPTZ NOT NULL, " +
                "PRIMARY KEY (propertyid, userid, category));",
                "CREATE INDEX IF NOT EXISTS idx_forumemaildigests_checked ON forumemaildigests (propertyid, lastcheckedutc);",
                "CREATE INDEX IF NOT EXISTS idx_notifications_digest ON notifications (propertyid, recipientuserid, category, createdutc) WHERE readutc IS NULL;"
            }),
            new SchemaMigration(Phase4Schema.DirectoryVersion, "Directory", Phase4Schema.Directory()),
            new SchemaMigration(Phase4Schema.EventsVersion, "Events", Phase4Schema.Events()),
            new SchemaMigration(Phase4Schema.LibraryVersion, "Library", Phase4Schema.Library()),
            new SchemaMigration(Phase4Schema.DocumentsVersion, "Document media and download renditions", Phase4Schema.Documents()),
            new SchemaMigration(EngagementSchema.Version, "Phase 4 engagement: promoted card source state", EngagementSchema.Statements()),
            new SchemaMigration(MessagingSchema.MessagesVersion, MessagingSchema.MessagesDescription, MessagingSchema.MessagesStatements()),
            new SchemaMigration(MessagingSchema.RealtimeVersion, MessagingSchema.RealtimeDescription, MessagingSchema.RealtimeStatements()),
            new SchemaMigration(SupportSchema.Version, SupportSchema.Description, SupportSchema.Statements()),
            new SchemaMigration(IdeasSchema.Version, IdeasSchema.Description, IdeasSchema.Statements()),
            new SchemaMigration(BroadcastsSchema.Version, BroadcastsSchema.Description, BroadcastsSchema.Statements()),
            new SchemaMigration(UsageSchema.Version, UsageSchema.Description, UsageSchema.Statements()),
            new SchemaMigration(BrandingSchema.Version, BrandingSchema.Description, BrandingSchema.Statements()),
            new SchemaMigration(AiToolsSchema.Version, AiToolsSchema.Description, AiToolsSchema.Statements()),
            new SchemaMigration(DocumentsSchema.Version, DocumentsSchema.Description, DocumentsSchema.Statements()),
            new SchemaMigration(RemoteContentSchema.Version, RemoteContentSchema.Description, RemoteContentSchema.Statements()),
            new SchemaMigration(IdeaPurgeSchema.Version, IdeaPurgeSchema.Description, IdeaPurgeSchema.Statements()),
            new SchemaMigration(SnapshotToolCallsSchema.Version, SnapshotToolCallsSchema.Description, SnapshotToolCallsSchema.Statements()),
            new SchemaMigration(UpdateGcSchema.Version, UpdateGcSchema.Description, UpdateGcSchema.Statements()),
            new SchemaMigration(PalettePresetsSchema.Version, PalettePresetsSchema.Description, PalettePresetsSchema.Statements()),
            new SchemaMigration(VenueContributionsSchema.Version, VenueContributionsSchema.Description, VenueContributionsSchema.Statements()),
            new SchemaMigration(PublicPagesSchema.Version, PublicPagesSchema.Description, PublicPagesSchema.Statements()),
            new SchemaMigration(DiscountCodesSchema.Version, DiscountCodesSchema.Description, DiscountCodesSchema.Statements()),
            new SchemaMigration(PromptTemplatesSchema.Version, PromptTemplatesSchema.Description, PromptTemplatesSchema.Statements()),
            new SchemaMigration(JobsSchema.Version, JobsSchema.Description, JobsSchema.Statements()),
            new SchemaMigration(AiRolesSchema.Version, AiRolesSchema.Description, AiRolesSchema.Statements()),
            new SchemaMigration(ContentChangesSchema.Version, ContentChangesSchema.Description, ContentChangesSchema.Statements()),
            new SchemaMigration(ScopeBenchmarkSchema.Version, ScopeBenchmarkSchema.Description, ScopeBenchmarkSchema.Statements()),
            new SchemaMigration(AiTelemetrySchema.Version, AiTelemetrySchema.Description, AiTelemetrySchema.Statements()),
            new SchemaMigration(MetricsSchema.Version, MetricsSchema.Description, MetricsSchema.Statements()),
            new SchemaMigration(ProductMetricsSchema.Version, ProductMetricsSchema.Description, ProductMetricsSchema.Statements()),
            new SchemaMigration(JobPlanningSchema.Version, JobPlanningSchema.Description, JobPlanningSchema.Statements()),
            new SchemaMigration(VenueScheduleSchema.Version, VenueScheduleSchema.Description, VenueScheduleSchema.Statements()),
            new SchemaMigration(VenueScheduleSessionsSchema.Version, VenueScheduleSessionsSchema.Description, VenueScheduleSessionsSchema.Statements()),
            new SchemaMigration(ProfileMediaSchema.Version, ProfileMediaSchema.Description, ProfileMediaSchema.Statements()),
            new SchemaMigration(RankSchema.Version, RankSchema.Description, RankSchema.Statements()),
            new SchemaMigration(CurriculumSchema.Version, CurriculumSchema.Description, CurriculumSchema.Statements()),
            new SchemaMigration(AttendanceSchema.Version, AttendanceSchema.Description, AttendanceSchema.Statements())
        };

        private static List<string> ForumsMigration()
        {
            // docs/PHASE3_CONTRACT.md section 17. Every table carries propertyid as its first column and every index
            // starts with propertyid. Search uses the engine search index (resolved decision 13), not tsvector columns.
            string live = "deletedutc IS NULL AND moderationstate = 'None' AND publishedutc IS NOT NULL";
            return new List<string>
            {
                "CREATE TABLE IF NOT EXISTS forums (propertyid TEXT NOT NULL, id TEXT NOT NULL PRIMARY KEY, slug TEXT NOT NULL, name TEXT NOT NULL, description TEXT NULL, guidelines TEXT NULL, iconemoji TEXT NULL, iconmediaid TEXT NULL, color TEXT NULL, section TEXT NULL, sortorder INTEGER NOT NULL DEFAULT 0, audience TEXT NOT NULL, threadcreation TEXT NOT NULL, defaultordering TEXT NOT NULL, allowmedia BOOLEAN NOT NULL DEFAULT TRUE, allowvideo BOOLEAN NOT NULL DEFAULT TRUE, maxopenfeedbackpermember INTEGER NULL, isarchived BOOLEAN NOT NULL DEFAULT FALSE, archivedutc TIMESTAMPTZ NULL, threadcount INTEGER NOT NULL DEFAULT 0, commentcount INTEGER NOT NULL DEFAULT 0, lastactivityutc TIMESTAMPTZ NULL, lastthreadid TEXT NULL, createdbyuserid TEXT NULL, deletedutc TIMESTAMPTZ NULL, createdutc TIMESTAMPTZ NOT NULL, updatedutc TIMESTAMPTZ NOT NULL);",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_forums_slug ON forums (propertyid, lower(slug)) WHERE deletedutc IS NULL;",
                "CREATE INDEX IF NOT EXISTS idx_forums_order ON forums (propertyid, isarchived, sortorder, id);",
                "CREATE TABLE IF NOT EXISTS forumthreadtypes (propertyid TEXT NOT NULL, forumid TEXT NOT NULL, threadtype TEXT NOT NULL, PRIMARY KEY (propertyid, forumid, threadtype));",
                "CREATE TABLE IF NOT EXISTS forumthreads (propertyid TEXT NOT NULL, id TEXT NOT NULL PRIMARY KEY, forumid TEXT NOT NULL, kind TEXT NOT NULL, type TEXT NOT NULL, authoruserid TEXT NOT NULL, title TEXT NOT NULL, body TEXT NULL, excerpt TEXT NOT NULL, linkpreviewid TEXT NULL, showlinkpreview BOOLEAN NOT NULL DEFAULT TRUE, audience TEXT NOT NULL, sourcetype TEXT NULL, sourceid TEXT NULL, committargettype TEXT NOT NULL, committargetid TEXT NOT NULL, promotedfeedpostid TEXT NULL, promotedbyuserid TEXT NULL, promotedutc TIMESTAMPTZ NULL, ispinned BOOLEAN NOT NULL DEFAULT FALSE, pinnedutc TIMESTAMPTZ NULL, pinnedbyuserid TEXT NULL, islocked BOOLEAN NOT NULL DEFAULT FALSE, lockedutc TIMESTAMPTZ NULL, lockedbyuserid TEXT NULL, lockreason TEXT NULL, acceptedcommentid TEXT NULL, hasacceptedanswer BOOLEAN NOT NULL DEFAULT FALSE, feedbackstatus TEXT NULL, feedbackfocus TEXT NULL, feedbackreviewerid TEXT NULL, feedbackreviewcommentid TEXT NULL, feedbackstatusutc TIMESTAMPTZ NULL, attachmentcount INTEGER NOT NULL DEFAULT 0, hasvideo BOOLEAN NOT NULL DEFAULT FALSE, commentcount INTEGER NOT NULL DEFAULT 0, uniquecommentercount INTEGER NOT NULL DEFAULT 0, reactiontotal INTEGER NOT NULL DEFAULT 0, openreportcount INTEGER NOT NULL DEFAULT 0, trendingscore DOUBLE PRECISION NOT NULL DEFAULT 0, trendingcomputedutc TIMESTAMPTZ NULL, lastactivityutc TIMESTAMPTZ NOT NULL, lastcommentutc TIMESTAMPTZ NULL, editedutc TIMESTAMPTZ NULL, revisioncount INTEGER NOT NULL DEFAULT 0, moderationstate TEXT NOT NULL DEFAULT 'None', moderatedutc TIMESTAMPTZ NULL, moderatedbyuserid TEXT NULL, moderationreason TEXT NULL, publishedutc TIMESTAMPTZ NULL, deletedutc TIMESTAMPTZ NULL, deletedbyuserid TEXT NULL, purgedutc TIMESTAMPTZ NULL, createdutc TIMESTAMPTZ NOT NULL, updatedutc TIMESTAMPTZ NOT NULL);",
                "CREATE INDEX IF NOT EXISTS idx_threads_active ON forumthreads (propertyid, forumid, lastactivityutc DESC, id DESC) WHERE " + live + ";",
                "CREATE INDEX IF NOT EXISTS idx_threads_hot ON forumthreads (propertyid, forumid, trendingscore DESC, lastactivityutc DESC, id DESC) WHERE " + live + ";",
                "CREATE INDEX IF NOT EXISTS idx_threads_created ON forumthreads (propertyid, forumid, publishedutc DESC, id DESC) WHERE " + live + ";",
                "CREATE INDEX IF NOT EXISTS idx_threads_all_active ON forumthreads (propertyid, lastactivityutc DESC, id DESC) WHERE " + live + ";",
                "CREATE INDEX IF NOT EXISTS idx_threads_pinned ON forumthreads (propertyid, forumid, ispinned, pinnedutc);",
                "CREATE INDEX IF NOT EXISTS idx_threads_author ON forumthreads (propertyid, authoruserid, createdutc);",
                "CREATE INDEX IF NOT EXISTS idx_threads_scored ON forumthreads (propertyid, id) WHERE trendingscore > 0;",
                "CREATE INDEX IF NOT EXISTS idx_threads_feedback ON forumthreads (propertyid, feedbackstatus, createdutc) WHERE type = 'Feedback';",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_threads_source ON forumthreads (propertyid, sourcetype, sourceid) WHERE sourceid IS NOT NULL AND deletedutc IS NULL;",
                "CREATE TABLE IF NOT EXISTS forumthreadrevisions (propertyid TEXT NOT NULL, id TEXT NOT NULL PRIMARY KEY, threadid TEXT NOT NULL, revision INTEGER NOT NULL, title TEXT NOT NULL, body TEXT NULL, type TEXT NOT NULL, attachmentsummary TEXT NULL, editedbyuserid TEXT NULL, editedbyrole TEXT NOT NULL, createdutc TIMESTAMPTZ NOT NULL);",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_threadrevisions ON forumthreadrevisions (propertyid, threadid, revision);",
                "CREATE TABLE IF NOT EXISTS polls (propertyid TEXT NOT NULL, id TEXT NOT NULL PRIMARY KEY, threadid TEXT NOT NULL, multiplechoice BOOLEAN NOT NULL DEFAULT FALSE, maxchoices INTEGER NULL, resultsvisibility TEXT NOT NULL, votervisibility TEXT NOT NULL, allowvotechange BOOLEAN NOT NULL DEFAULT TRUE, closesutc TIMESTAMPTZ NULL, closedutc TIMESTAMPTZ NULL, closedbyuserid TEXT NULL, totalvoters INTEGER NOT NULL DEFAULT 0, firstvoteutc TIMESTAMPTZ NULL, createdutc TIMESTAMPTZ NOT NULL, updatedutc TIMESTAMPTZ NOT NULL);",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_polls_thread ON polls (propertyid, threadid);",
                "CREATE INDEX IF NOT EXISTS idx_polls_closing ON polls (propertyid, closesutc) WHERE closedutc IS NULL AND closesutc IS NOT NULL;",
                "CREATE TABLE IF NOT EXISTS polloptions (propertyid TEXT NOT NULL, id TEXT NOT NULL PRIMARY KEY, pollid TEXT NOT NULL, label TEXT NOT NULL, labelnormalized TEXT NOT NULL, position INTEGER NOT NULL, votecount INTEGER NOT NULL DEFAULT 0, createdutc TIMESTAMPTZ NOT NULL);",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_polloptions_label ON polloptions (propertyid, pollid, labelnormalized);",
                "CREATE INDEX IF NOT EXISTS idx_polloptions_order ON polloptions (propertyid, pollid, position);",
                "CREATE TABLE IF NOT EXISTS pollvotes (propertyid TEXT NOT NULL, id TEXT NOT NULL PRIMARY KEY, pollid TEXT NOT NULL, optionid TEXT NOT NULL, userid TEXT NOT NULL, createdutc TIMESTAMPTZ NOT NULL);",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_pollvotes ON pollvotes (propertyid, pollid, userid, optionid);",
                "CREATE INDEX IF NOT EXISTS idx_pollvotes_user ON pollvotes (propertyid, userid, createdutc);",
                "CREATE INDEX IF NOT EXISTS idx_pollvotes_option ON pollvotes (propertyid, pollid, optionid);",
                "CREATE TABLE IF NOT EXISTS forumfollows (propertyid TEXT NOT NULL, id TEXT NOT NULL PRIMARY KEY, forumid TEXT NOT NULL, userid TEXT NOT NULL, level TEXT NOT NULL, createdutc TIMESTAMPTZ NOT NULL);",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_forumfollows ON forumfollows (propertyid, forumid, userid);",
                "CREATE INDEX IF NOT EXISTS idx_forumfollows_user ON forumfollows (propertyid, userid);",
                "CREATE TABLE IF NOT EXISTS threadfollows (propertyid TEXT NOT NULL, id TEXT NOT NULL PRIMARY KEY, threadid TEXT NOT NULL, userid TEXT NOT NULL, source TEXT NOT NULL, isoptedout BOOLEAN NOT NULL DEFAULT FALSE, createdutc TIMESTAMPTZ NOT NULL);",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_threadfollows ON threadfollows (propertyid, threadid, userid);",
                "CREATE INDEX IF NOT EXISTS idx_threadfollows_user ON threadfollows (propertyid, userid, createdutc);",
                "CREATE TABLE IF NOT EXISTS threadreads (propertyid TEXT NOT NULL, userid TEXT NOT NULL, threadid TEXT NOT NULL, lastreadutc TIMESTAMPTZ NOT NULL, PRIMARY KEY (propertyid, userid, threadid));",
                "CREATE INDEX IF NOT EXISTS idx_threadreads_prune ON threadreads (propertyid, lastreadutc);",
                "CREATE TABLE IF NOT EXISTS forumreads (propertyid TEXT NOT NULL, userid TEXT NOT NULL, forumid TEXT NOT NULL, lastreadutc TIMESTAMPTZ NOT NULL, PRIMARY KEY (propertyid, userid, forumid));",
                "CREATE INDEX IF NOT EXISTS idx_forumreads_prune ON forumreads (propertyid, lastreadutc);",
                "CREATE TABLE IF NOT EXISTS forummutes (propertyid TEXT NOT NULL, id TEXT NOT NULL PRIMARY KEY, forumid TEXT NOT NULL, userid TEXT NOT NULL, untilutc TIMESTAMPTZ NULL, reason TEXT NULL, mutedbyuserid TEXT NULL, endedutc TIMESTAMPTZ NULL, endedbyuserid TEXT NULL, createdutc TIMESTAMPTZ NOT NULL);",
                "CREATE INDEX IF NOT EXISTS idx_forummutes_active ON forummutes (propertyid, userid, forumid, untilutc) WHERE endedutc IS NULL;",
                "CREATE INDEX IF NOT EXISTS idx_forummutes_forum ON forummutes (propertyid, forumid, createdutc);",
                "CREATE TABLE IF NOT EXISTS forummemberprefs (propertyid TEXT NOT NULL, userid TEXT NOT NULL, autofollowoncomment BOOLEAN NOT NULL DEFAULT TRUE, updatedutc TIMESTAMPTZ NOT NULL, PRIMARY KEY (propertyid, userid));",
                "CREATE TABLE IF NOT EXISTS searchqueue (propertyid TEXT NOT NULL, targettype TEXT NOT NULL, targetid TEXT NOT NULL, enqueuedutc TIMESTAMPTZ NOT NULL, attempts INTEGER NOT NULL DEFAULT 0, PRIMARY KEY (propertyid, targettype, targetid));",
                "CREATE INDEX IF NOT EXISTS idx_searchqueue_order ON searchqueue (propertyid, enqueuedutc);",
                "ALTER TABLE feedposts ADD COLUMN IF NOT EXISTS discussionthreadid TEXT NULL;",
                "ALTER TABLE feedposts ADD COLUMN IF NOT EXISTS discussionlinkedbyuserid TEXT NULL;",
                "CREATE INDEX IF NOT EXISTS idx_feedposts_source ON feedposts (propertyid, sourcetype, sourceid) WHERE sourceid IS NOT NULL;",
                "ALTER TABLE comments ADD COLUMN IF NOT EXISTS isacceptedanswer BOOLEAN NOT NULL DEFAULT FALSE;",
                "ALTER TABLE comments ADD COLUMN IF NOT EXISTS isfeedbackreview BOOLEAN NOT NULL DEFAULT FALSE;",
                "ALTER TABLE moderationactions ADD COLUMN IF NOT EXISTS fromforumid TEXT NULL;",
                "ALTER TABLE moderationactions ADD COLUMN IF NOT EXISTS toforumid TEXT NULL;",
                "ALTER TABLE notifications ADD COLUMN IF NOT EXISTS threadid TEXT NULL;",
                "ALTER TABLE notifications ADD COLUMN IF NOT EXISTS forumid TEXT NULL;",
                "ALTER TABLE notifications ADD COLUMN IF NOT EXISTS pollid TEXT NULL;",
                "CREATE INDEX IF NOT EXISTS idx_reports_scope ON reports (propertyid, scopetype, scopeid, status);"
            };
        }

        private static List<string> PropertyScoping()
        {
            // docs/MULTIPROPERTY_CONTRACT.md sections 4.1, 4.2, and 13. Every property-owned table gains propertyid
            // (pre-existing rows belong to the property in property.directory, substituted for the placeholder), every
            // non-primary-key index is rebuilt with propertyid first (same name, columns, and WHERE), except the
            // deployment-wide MediaConvert job index, and the registry and platform tables are created.
            // pgvector is enabled by the database init script (Docker), the test harness, and infrastructure
            // provisioning, outside the migration: CREATE EXTENSION needs elevated rights and serializes concurrent
            // migrations on one server. Migration 6 alters exactly the frozen PropertyTables.ScopedByMigration6 list; tables
            // created by later migrations carry propertyid from their first CREATE TABLE.
            List<string> statements = new List<string>();
            foreach (string table in PropertyTables.ScopedByMigration6)
            {
                statements.Add("ALTER TABLE " + table + " ADD COLUMN IF NOT EXISTS propertyid TEXT NOT NULL DEFAULT " + SchemaMigration.PropertyIdPlaceholder + ";");
                statements.Add("ALTER TABLE " + table + " ALTER COLUMN propertyid DROP DEFAULT;");
            }
            statements.Add("ALTER TABLE authsessions ADD COLUMN IF NOT EXISTS principalkind TEXT NOT NULL DEFAULT 'Member';");
            statements.Add("ALTER TABLE authsessions DROP CONSTRAINT IF EXISTS authsessions_userid_fkey;");
            statements.Add(
                "DO $$ DECLARE r RECORD; BEGIN " +
                "FOR r IN SELECT ic.relname AS indexname, pg_get_indexdef(i.indexrelid) AS def FROM pg_index i " +
                "JOIN pg_class ic ON ic.oid = i.indexrelid JOIN pg_class tc ON tc.oid = i.indrelid JOIN pg_namespace n ON n.oid = tc.relnamespace " +
                "WHERE n.nspname = current_schema() AND NOT i.indisprimary " +
                "AND NOT EXISTS (SELECT 1 FROM pg_constraint pc WHERE pc.conindid = i.indexrelid) " +
                "AND tc.relname IN (" + String.Join(", ", PropertyTables.ScopedByMigration6.Select(t => "'" + t + "'")) + ") " +
                "AND ic.relname <> 'ux_media_jobid' " +
                "AND pg_get_indexdef(i.indexrelid) NOT LIKE '%USING btree (propertyid%' LOOP " +
                "EXECUTE 'DROP INDEX ' || quote_ident(r.indexname); " +
                "EXECUTE regexp_replace(r.def, 'USING btree \\(', 'USING btree (propertyid, '); " +
                "END LOOP; END $$;");
            foreach (string table in PropertyTables.ScopedByMigration6)
                statements.Add("CREATE INDEX IF NOT EXISTS idx_" + table + "_property ON " + table + " (propertyid);");
            statements.AddRange(new List<string>
            {
                "CREATE TABLE IF NOT EXISTS properties (id TEXT NOT NULL PRIMARY KEY, slug TEXT NOT NULL, name TEXT NOT NULL, status TEXT NOT NULL, documentjson TEXT NOT NULL, sourcedirectory TEXT NULL, directoryhash TEXT NULL, enabledutc TIMESTAMPTZ NULL, disabledutc TIMESTAMPTZ NULL, disabledreason TEXT NULL, deletescheduledutc TIMESTAMPTZ NULL, purgestartedutc TIMESTAMPTZ NULL, purgedutc TIMESTAMPTZ NULL, createdutc TIMESTAMPTZ NOT NULL, updatedutc TIMESTAMPTZ NOT NULL);",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_properties_slug ON properties (lower(slug));",
                "CREATE INDEX IF NOT EXISTS idx_properties_status ON properties (status, deletescheduledutc);",
                "CREATE INDEX IF NOT EXISTS idx_properties_updated ON properties (updatedutc);",
                "CREATE TABLE IF NOT EXISTS propertydomains (id TEXT NOT NULL PRIMARY KEY, propertyid TEXT NOT NULL, host TEXT NOT NULL, kind TEXT NOT NULL, verifiedutc TIMESTAMPTZ NULL, createdutc TIMESTAMPTZ NOT NULL);",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_propertydomains_host ON propertydomains (lower(host));",
                "CREATE INDEX IF NOT EXISTS idx_propertydomains_property ON propertydomains (propertyid);",
                "CREATE TABLE IF NOT EXISTS propertyapps (id TEXT NOT NULL PRIMARY KEY, propertyid TEXT NOT NULL, platform TEXT NOT NULL, appid TEXT NOT NULL, storeappid TEXT NULL, enabled BOOLEAN NOT NULL DEFAULT TRUE, createdutc TIMESTAMPTZ NOT NULL, updatedutc TIMESTAMPTZ NOT NULL);",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_propertyapps_app ON propertyapps (platform, lower(appid));",
                "CREATE INDEX IF NOT EXISTS idx_propertyapps_property ON propertyapps (propertyid);",
                "CREATE TABLE IF NOT EXISTS platformadmins (id TEXT NOT NULL PRIMARY KEY, email TEXT NOT NULL, displayname TEXT NOT NULL, passwordhash TEXT NULL, invitetokenhash TEXT NULL, inviteexpiresutc TIMESTAMPTZ NULL, lastloginutc TIMESTAMPTZ NULL, failedlogincount INTEGER NOT NULL DEFAULT 0, lockeduntilutc TIMESTAMPTZ NULL, removedutc TIMESTAMPTZ NULL, createdutc TIMESTAMPTZ NOT NULL, updatedutc TIMESTAMPTZ NOT NULL);",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_platformadmins_email ON platformadmins (lower(email)) WHERE removedutc IS NULL;",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_platformadmins_invite ON platformadmins (invitetokenhash) WHERE invitetokenhash IS NOT NULL;",
                "CREATE TABLE IF NOT EXISTS platformsessions (id TEXT NOT NULL PRIMARY KEY, adminid TEXT NOT NULL REFERENCES platformadmins (id), tokenhash TEXT NOT NULL, sourceip TEXT NULL, useragent TEXT NULL, createdutc TIMESTAMPTZ NOT NULL, lastusedutc TIMESTAMPTZ NOT NULL, expiresutc TIMESTAMPTZ NOT NULL, revokedutc TIMESTAMPTZ NULL);",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_platformsessions_token ON platformsessions (tokenhash);",
                "CREATE INDEX IF NOT EXISTS idx_platformsessions_admin ON platformsessions (adminid, revokedutc);",
                "CREATE INDEX IF NOT EXISTS idx_platformsessions_expires ON platformsessions (expiresutc);"
            });
            return statements;
        }

        internal static string CreateMigrationsTable()
        {
            return "CREATE TABLE IF NOT EXISTS schema_migrations (" +
                "version INTEGER NOT NULL PRIMARY KEY, " +
                "description TEXT NOT NULL, " +
                "appliedutc TIMESTAMPTZ NOT NULL);";
        }

        internal static string RecordMigration(SchemaMigration migration, DateTime appliedUtc)
        {
            return "INSERT INTO schema_migrations (version, description, appliedutc) VALUES (" +
                Sanitizer.Num(migration.Version) + ", " +
                Sanitizer.Str(migration.Description) + ", " +
                Sanitizer.Ts(appliedUtc) + ") ON CONFLICT (version) DO NOTHING;";
        }
    }
}

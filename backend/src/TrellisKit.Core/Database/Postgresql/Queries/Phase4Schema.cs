namespace TrellisKit.Core.Database.Postgresql.Queries
{
    using System.Collections.Generic;

    /// <summary>
    /// Phase 4 schema (docs/PHASE4_CONTRACT.md section 14): migrations 50 Directory, 51 Events, 52 Library, and 53
    /// Document media, download renditions, and extracted document text. Every table carries propertyid as its first
    /// column, every index and unique constraint starts with propertyid, and no column holds JSON (every list is a
    /// child table). Migrations 54 to 59 are held for fixes during the phase.
    /// </summary>
    public static class Phase4Schema
    {
        #region Public-Members

        /// <summary>
        /// Directory migration version.
        /// </summary>
        public const int DirectoryVersion = 50;

        /// <summary>
        /// Events migration version.
        /// </summary>
        public const int EventsVersion = 51;

        /// <summary>
        /// Library migration version.
        /// </summary>
        public const int LibraryVersion = 52;

        /// <summary>
        /// Document media migration version.
        /// </summary>
        public const int DocumentsVersion = 53;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Migration 50: venues, instructors, programs, submissions, and the geocoder cache.
        /// </summary>
        /// <returns>Statements.</returns>
        public static List<string> Directory()
        {
            string ts = "TIMESTAMPTZ NOT NULL";
            return new List<string>
            {
                "CREATE TABLE IF NOT EXISTS directoryconfig (propertyid TEXT NOT NULL PRIMARY KEY, venuelabel TEXT NOT NULL, venuelabelplural TEXT NOT NULL, instructorlabel TEXT NOT NULL, " +
                    "instructorlabelplural TEXT NOT NULL, instructorrankfieldkey TEXT NULL, submissionsenabled BOOLEAN NOT NULL DEFAULT TRUE, claimsenabled BOOLEAN NOT NULL DEFAULT TRUE, " +
                    "sourcehash TEXT NULL, updatedutc " + ts + ");",
                "CREATE TABLE IF NOT EXISTS directoryprograms (propertyid TEXT NOT NULL, id TEXT NOT NULL PRIMARY KEY, key TEXT NOT NULL, label TEXT NOT NULL, sortorder INTEGER NOT NULL DEFAULT 0, " +
                    "isretired BOOLEAN NOT NULL DEFAULT FALSE, createdutc " + ts + ", updatedutc " + ts + ");",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_directoryprograms_key ON directoryprograms (propertyid, lower(key));",
                "CREATE INDEX IF NOT EXISTS idx_directoryprograms_order ON directoryprograms (propertyid, sortorder, id);",
                "CREATE TABLE IF NOT EXISTS venues (propertyid TEXT NOT NULL, id TEXT NOT NULL PRIMARY KEY, slug TEXT NOT NULL, name TEXT NOT NULL, affiliation TEXT NULL, summary TEXT NULL, " +
                    "description TEXT NULL, line1 TEXT NULL, line2 TEXT NULL, locality TEXT NULL, region TEXT NULL, postalcode TEXT NULL, countrycode TEXT NULL, " +
                    "addressvisibility TEXT NOT NULL DEFAULT 'Exact', lat DOUBLE PRECISION NULL, lng DOUBLE PRECISION NULL, geosource TEXT NOT NULL DEFAULT 'None', geocodedutc TIMESTAMPTZ NULL, geocodependingutc TIMESTAMPTZ NULL, " +
                    "timezone TEXT NULL, phone TEXT NULL, email TEXT NULL, websiteurl TEXT NULL, scheduletext TEXT NULL, visitorpolicy TEXT NOT NULL DEFAULT 'Unknown', visitorpolicynote TEXT NULL, " +
                    "isverified BOOLEAN NOT NULL DEFAULT FALSE, verifiedutc TIMESTAMPTZ NULL, verifiedbyuserid TEXT NULL, status TEXT NOT NULL DEFAULT 'Active', audience TEXT NOT NULL, " +
                    "instructorcount INTEGER NOT NULL DEFAULT 0, openreportcount INTEGER NOT NULL DEFAULT 0, moderationstate TEXT NOT NULL DEFAULT 'None', moderatedutc TIMESTAMPTZ NULL, " +
                    "moderatedbyuserid TEXT NULL, moderationreason TEXT NULL, closedutc TIMESTAMPTZ NULL, closedreason TEXT NULL, createdbyuserid TEXT NULL, deletedutc TIMESTAMPTZ NULL, " +
                    "createdutc " + ts + ", updatedutc " + ts + ");",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_venues_slug ON venues (propertyid, lower(slug)) WHERE deletedutc IS NULL;",
                "CREATE INDEX IF NOT EXISTS idx_venues_geo ON venues (propertyid, lat, lng) WHERE status = 'Active' AND deletedutc IS NULL AND moderationstate = 'None';",
                "CREATE INDEX IF NOT EXISTS idx_venues_name ON venues (propertyid, lower(name), id);",
                "CREATE INDEX IF NOT EXISTS idx_venues_locality ON venues (propertyid, countrycode, lower(region), lower(locality));",
                "CREATE INDEX IF NOT EXISTS idx_venues_needspin ON venues (propertyid, geosource) WHERE geosource = 'None';",
                "CREATE INDEX IF NOT EXISTS idx_venues_geocodepending ON venues (propertyid, geocodependingutc, id) WHERE geocodependingutc IS NOT NULL AND deletedutc IS NULL;",
                "CREATE TABLE IF NOT EXISTS venueslugs (propertyid TEXT NOT NULL, slug TEXT NOT NULL, venueid TEXT NOT NULL, createdutc " + ts + ", PRIMARY KEY (propertyid, slug));",
                "CREATE TABLE IF NOT EXISTS venueprograms (propertyid TEXT NOT NULL, venueid TEXT NOT NULL, programkey TEXT NOT NULL, PRIMARY KEY (propertyid, venueid, programkey));",
                "CREATE INDEX IF NOT EXISTS idx_venueprograms_program ON venueprograms (propertyid, programkey, venueid);",
                "CREATE TABLE IF NOT EXISTS venuesociallinks (propertyid TEXT NOT NULL, id TEXT NOT NULL PRIMARY KEY, venueid TEXT NOT NULL, kind TEXT NOT NULL, url TEXT NOT NULL, position INTEGER NOT NULL DEFAULT 0);",
                "CREATE INDEX IF NOT EXISTS idx_venuesociallinks_venue ON venuesociallinks (propertyid, venueid, position);",
                "CREATE TABLE IF NOT EXISTS venuephotos (propertyid TEXT NOT NULL, venueid TEXT NOT NULL, mediaid TEXT NOT NULL, position INTEGER NOT NULL DEFAULT 0, PRIMARY KEY (propertyid, venueid, mediaid));",
                "CREATE TABLE IF NOT EXISTS instructors (propertyid TEXT NOT NULL, id TEXT NOT NULL PRIMARY KEY, slug TEXT NOT NULL, displayname TEXT NOT NULL, bio TEXT NULL, photomediaid TEXT NULL, " +
                    "rankkey TEXT NULL, ranklevel INTEGER NULL, affiliation TEXT NULL, linkeduserid TEXT NULL, linkedutc TIMESTAMPTZ NULL, websiteurl TEXT NULL, status TEXT NOT NULL DEFAULT 'Active', " +
                    "audience TEXT NOT NULL, venuecount INTEGER NOT NULL DEFAULT 0, openreportcount INTEGER NOT NULL DEFAULT 0, moderationstate TEXT NOT NULL DEFAULT 'None', moderatedutc TIMESTAMPTZ NULL, " +
                    "moderatedbyuserid TEXT NULL, moderationreason TEXT NULL, createdbyuserid TEXT NULL, deletedutc TIMESTAMPTZ NULL, createdutc " + ts + ", updatedutc " + ts + ");",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_instructors_slug ON instructors (propertyid, lower(slug)) WHERE deletedutc IS NULL;",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_instructors_linkeduser ON instructors (propertyid, linkeduserid) WHERE linkeduserid IS NOT NULL AND deletedutc IS NULL;",
                "CREATE INDEX IF NOT EXISTS idx_instructors_name ON instructors (propertyid, lower(displayname), id);",
                "CREATE TABLE IF NOT EXISTS instructorslugs (propertyid TEXT NOT NULL, slug TEXT NOT NULL, instructorid TEXT NOT NULL, createdutc " + ts + ", PRIMARY KEY (propertyid, slug));",
                "CREATE TABLE IF NOT EXISTS instructorsociallinks (propertyid TEXT NOT NULL, id TEXT NOT NULL PRIMARY KEY, instructorid TEXT NOT NULL, kind TEXT NOT NULL, url TEXT NOT NULL, position INTEGER NOT NULL DEFAULT 0);",
                "CREATE INDEX IF NOT EXISTS idx_instructorsociallinks_instructor ON instructorsociallinks (propertyid, instructorid, position);",
                "CREATE TABLE IF NOT EXISTS venueinstructors (propertyid TEXT NOT NULL, id TEXT NOT NULL PRIMARY KEY, venueid TEXT NOT NULL, instructorid TEXT NOT NULL, role TEXT NOT NULL DEFAULT 'Regular', createdutc " + ts + ");",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_venueinstructors ON venueinstructors (propertyid, venueid, instructorid);",
                "CREATE INDEX IF NOT EXISTS idx_venueinstructors_instructor ON venueinstructors (propertyid, instructorid);",
                "CREATE TABLE IF NOT EXISTS directorysubmissions (propertyid TEXT NOT NULL, id TEXT NOT NULL PRIMARY KEY, type TEXT NOT NULL, status TEXT NOT NULL, targettype TEXT NOT NULL, targetid TEXT NULL, " +
                    "targetname TEXT NULL, targetupdatedutc TIMESTAMPTZ NULL, submitteruserid TEXT NULL, note TEXT NULL, reviewernote TEXT NULL, revieweruserid TEXT NULL, decidedutc TIMESTAMPTZ NULL, " +
                    "dueutc " + ts + ", overduenotifiedutc TIMESTAMPTZ NULL, proposedlat DOUBLE PRECISION NULL, proposedlng DOUBLE PRECISION NULL, resultid TEXT NULL, createdutc " + ts + ", updatedutc " + ts + ");",
                "CREATE INDEX IF NOT EXISTS idx_submissions_queue ON directorysubmissions (propertyid, status, createdutc, id);",
                "CREATE INDEX IF NOT EXISTS idx_submissions_submitter ON directorysubmissions (propertyid, submitteruserid, status);",
                "CREATE INDEX IF NOT EXISTS idx_submissions_target ON directorysubmissions (propertyid, targetid, status);",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_submissions_claim ON directorysubmissions (propertyid, targetid, submitteruserid) WHERE type = 'ClaimInstructor' AND status = 'Pending';",
                "CREATE TABLE IF NOT EXISTS directorysubmissionfields (propertyid TEXT NOT NULL, id TEXT NOT NULL PRIMARY KEY, submissionid TEXT NOT NULL, field TEXT NOT NULL, proposedvalue TEXT NULL, " +
                    "currentvalue TEXT NULL, position INTEGER NOT NULL DEFAULT 0, isoverride BOOLEAN NOT NULL DEFAULT FALSE);",
                "CREATE INDEX IF NOT EXISTS idx_submissionfields_submission ON directorysubmissionfields (propertyid, submissionid, field, position);",
                "CREATE TABLE IF NOT EXISTS geocoderesults (propertyid TEXT NOT NULL, id TEXT NOT NULL PRIMARY KEY, queryhash TEXT NOT NULL, provider TEXT NOT NULL, lat DOUBLE PRECISION NULL, " +
                    "lng DOUBLE PRECISION NULL, timezone TEXT NULL, formatted TEXT NULL, found BOOLEAN NOT NULL DEFAULT TRUE, expiresutc " + ts + ", createdutc " + ts + ");",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_geocoderesults ON geocoderesults (propertyid, provider, queryhash);",
                "CREATE INDEX IF NOT EXISTS idx_geocoderesults_expires ON geocoderesults (propertyid, expiresutc);"
            };
        }

        /// <summary>
        /// Migration 51: events, occurrences of recurring events, RSVPs, reminders, categories, proposals, and calendar feeds.
        /// </summary>
        /// <returns>Statements.</returns>
        public static List<string> Events()
        {
            string ts = "TIMESTAMPTZ NOT NULL";
            string live = "status = 'Published' AND deletedutc IS NULL AND moderationstate = 'None'";
            return new List<string>
            {
                "CREATE TABLE IF NOT EXISTS eventconfig (propertyid TEXT NOT NULL PRIMARY KEY, sourcehash TEXT NULL, updatedutc " + ts + ");",
                "CREATE TABLE IF NOT EXISTS eventcategories (propertyid TEXT NOT NULL, id TEXT NOT NULL PRIMARY KEY, key TEXT NOT NULL, label TEXT NOT NULL, color TEXT NULL, sortorder INTEGER NOT NULL DEFAULT 0, " +
                    "isretired BOOLEAN NOT NULL DEFAULT FALSE, createdutc " + ts + ", updatedutc " + ts + ");",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_eventcategories_key ON eventcategories (propertyid, lower(key));",
                "CREATE INDEX IF NOT EXISTS idx_eventcategories_order ON eventcategories (propertyid, sortorder, id);",
                "CREATE TABLE IF NOT EXISTS eventrecurrences (propertyid TEXT NOT NULL, id TEXT NOT NULL PRIMARY KEY, frequency TEXT NOT NULL, intervalcount INTEGER NOT NULL DEFAULT 1, " +
                    "occurrencecount INTEGER NULL, untillocaldate TEXT NULL, monthday INTEGER NULL, setposition INTEGER NULL, timezone TEXT NOT NULL, startlocal TEXT NOT NULL, durationminutes INTEGER NOT NULL, " +
                    "isallday BOOLEAN NOT NULL DEFAULT FALSE, materializedthroughutc TIMESTAMPTZ NULL, materializedcount INTEGER NOT NULL DEFAULT 0, icssequence INTEGER NOT NULL DEFAULT 0, " +
                    "templateeventid TEXT NULL, createdbyuserid TEXT NULL, deletedutc TIMESTAMPTZ NULL, createdutc " + ts + ", updatedutc " + ts + ");",
                "CREATE INDEX IF NOT EXISTS idx_eventrecurrences_horizon ON eventrecurrences (propertyid, materializedthroughutc) WHERE deletedutc IS NULL;",
                "CREATE TABLE IF NOT EXISTS eventrecurrencedays (propertyid TEXT NOT NULL, recurrenceid TEXT NOT NULL, weekday TEXT NOT NULL, PRIMARY KEY (propertyid, recurrenceid, weekday));",
                "CREATE TABLE IF NOT EXISTS eventrecurrenceexceptions (propertyid TEXT NOT NULL, id TEXT NOT NULL PRIMARY KEY, recurrenceid TEXT NOT NULL, originalstartutc " + ts + ", kind TEXT NOT NULL, " +
                    "eventid TEXT NULL, createdutc " + ts + ");",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_eventrecurrenceexceptions ON eventrecurrenceexceptions (propertyid, recurrenceid, originalstartutc);",
                "CREATE TABLE IF NOT EXISTS events (propertyid TEXT NOT NULL, id TEXT NOT NULL PRIMARY KEY, slug TEXT NOT NULL, title TEXT NOT NULL, summary TEXT NULL, description TEXT NULL, " +
                    "categorykey TEXT NULL, format TEXT NOT NULL, status TEXT NOT NULL, startutc " + ts + ", endutc " + ts + ", timezone TEXT NOT NULL, isallday BOOLEAN NOT NULL DEFAULT FALSE, " +
                    "recurrenceid TEXT NULL, originalstartutc TIMESTAMPTZ NULL, isexception BOOLEAN NOT NULL DEFAULT FALSE, occurrenceindex INTEGER NULL, " +
                    "venueid TEXT NULL, locationname TEXT NULL, line1 TEXT NULL, line2 TEXT NULL, locality TEXT NULL, region TEXT NULL, postalcode TEXT NULL, countrycode TEXT NULL, " +
                    "addressvisibility TEXT NOT NULL DEFAULT 'Exact', lat DOUBLE PRECISION NULL, lng DOUBLE PRECISION NULL, onlineplatformlabel TEXT NULL, onlinejoinurl TEXT NULL, capacity INTEGER NULL, " +
                    "maxguestsperrsvp INTEGER NOT NULL DEFAULT 0, waitlistenabled BOOLEAN NOT NULL DEFAULT TRUE, rsvpaudience TEXT NOT NULL, rsvpopensutc TIMESTAMPTZ NULL, rsvpclosesutc TIMESTAMPTZ NULL, " +
                    "attendeevisibility TEXT NOT NULL DEFAULT 'Attendees', externalurl TEXT NULL, covermediaid TEXT NULL, commentsenabled BOOLEAN NOT NULL DEFAULT TRUE, " +
                    "goingcount INTEGER NOT NULL DEFAULT 0, guestcount INTEGER NOT NULL DEFAULT 0, interestedcount INTEGER NOT NULL DEFAULT 0, waitlistcount INTEGER NOT NULL DEFAULT 0, " +
                    "checkedincount INTEGER NOT NULL DEFAULT 0, commentcount INTEGER NOT NULL DEFAULT 0, reactiontotal INTEGER NOT NULL DEFAULT 0, openreportcount INTEGER NOT NULL DEFAULT 0, " +
                    "icssequence INTEGER NOT NULL DEFAULT 0, audience TEXT NOT NULL, moderationstate TEXT NOT NULL DEFAULT 'None', moderatedutc TIMESTAMPTZ NULL, moderatedbyuserid TEXT NULL, " +
                    "moderationreason TEXT NULL, canceledutc TIMESTAMPTZ NULL, cancelreason TEXT NULL, canceledbyuserid TEXT NULL, proposedbyuserid TEXT NULL, proposalnote TEXT NULL, " +
                    "reviewnote TEXT NULL, revieweruserid TEXT NULL, reviewedutc TIMESTAMPTZ NULL, promotedfeedpostid TEXT NULL, publishedutc TIMESTAMPTZ NULL, createdbyuserid TEXT NULL, " +
                    "deletedutc TIMESTAMPTZ NULL, createdutc " + ts + ", updatedutc " + ts + ");",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_events_slug ON events (propertyid, lower(slug)) WHERE deletedutc IS NULL;",
                "CREATE INDEX IF NOT EXISTS idx_events_upcoming ON events (propertyid, startutc, id) WHERE " + live + ";",
                "CREATE INDEX IF NOT EXISTS idx_events_venue ON events (propertyid, venueid, startutc);",
                "CREATE INDEX IF NOT EXISTS idx_events_geo ON events (propertyid, lat, lng, startutc);",
                "CREATE INDEX IF NOT EXISTS idx_events_category ON events (propertyid, categorykey, startutc);",
                "CREATE INDEX IF NOT EXISTS idx_events_recurrence ON events (propertyid, recurrenceid, startutc) WHERE recurrenceid IS NOT NULL;",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_events_occurrence ON events (propertyid, recurrenceid, originalstartutc) WHERE recurrenceid IS NOT NULL AND deletedutc IS NULL;",
                "CREATE INDEX IF NOT EXISTS idx_events_proposals ON events (propertyid, status, createdutc, id) WHERE status = 'Proposed';",
                "CREATE TABLE IF NOT EXISTS eventslugs (propertyid TEXT NOT NULL, slug TEXT NOT NULL, eventid TEXT NOT NULL, createdutc " + ts + ", PRIMARY KEY (propertyid, slug));",
                "CREATE TABLE IF NOT EXISTS eventhosts (propertyid TEXT NOT NULL, eventid TEXT NOT NULL, userid TEXT NOT NULL, position INTEGER NOT NULL DEFAULT 0, PRIMARY KEY (propertyid, eventid, userid));",
                "CREATE INDEX IF NOT EXISTS idx_eventhosts_user ON eventhosts (propertyid, userid, eventid);",
                "CREATE TABLE IF NOT EXISTS eventinstructors (propertyid TEXT NOT NULL, eventid TEXT NOT NULL, instructorid TEXT NOT NULL, position INTEGER NOT NULL DEFAULT 0, PRIMARY KEY (propertyid, eventid, instructorid));",
                "CREATE TABLE IF NOT EXISTS eventrsvps (propertyid TEXT NOT NULL, id TEXT NOT NULL PRIMARY KEY, eventid TEXT NOT NULL, userid TEXT NOT NULL, status TEXT NOT NULL, guestcount INTEGER NOT NULL DEFAULT 0, " +
                    "hidefromattendeelists BOOLEAN NOT NULL DEFAULT FALSE, waitlistedutc TIMESTAMPTZ NULL, promotedutc TIMESTAMPTZ NULL, checkedinutc TIMESTAMPTZ NULL, checkedinbyuserid TEXT NULL, " +
                    "createdutc " + ts + ", updatedutc " + ts + ");",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_eventrsvps ON eventrsvps (propertyid, eventid, userid);",
                "CREATE INDEX IF NOT EXISTS idx_eventrsvps_waitlist ON eventrsvps (propertyid, eventid, waitlistedutc, id) WHERE status = 'Waitlisted';",
                "CREATE INDEX IF NOT EXISTS idx_eventrsvps_user ON eventrsvps (propertyid, userid, status);",
                "CREATE TABLE IF NOT EXISTS eventreminders (propertyid TEXT NOT NULL, id TEXT NOT NULL PRIMARY KEY, eventid TEXT NOT NULL, offsetminutes INTEGER NOT NULL, dueutc " + ts + ", " +
                    "sentutc TIMESTAMPTZ NULL, skippedutc TIMESTAMPTZ NULL, claimedutc TIMESTAMPTZ NULL);",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_eventreminders ON eventreminders (propertyid, eventid, offsetminutes);",
                "CREATE INDEX IF NOT EXISTS idx_eventreminders_due ON eventreminders (propertyid, dueutc) WHERE sentutc IS NULL AND skippedutc IS NULL;",
                "CREATE TABLE IF NOT EXISTS calendarfeeds (propertyid TEXT NOT NULL, id TEXT NOT NULL PRIMARY KEY, userid TEXT NOT NULL, secrethash TEXT NOT NULL, isenabled BOOLEAN NOT NULL DEFAULT TRUE, " +
                    "lastfetchedutc TIMESTAMPTZ NULL, createdutc " + ts + ", rotatedutc TIMESTAMPTZ NULL);",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_calendarfeeds_user ON calendarfeeds (propertyid, userid);"
            };
        }

        /// <summary>
        /// Migration 52: series, lessons, markers, resources, captions, progress, plays, follows, levels, and downloads.
        /// </summary>
        /// <returns>Statements.</returns>
        public static List<string> Library()
        {
            string ts = "TIMESTAMPTZ NOT NULL";
            return new List<string>
            {
                "CREATE TABLE IF NOT EXISTS libraryconfig (propertyid TEXT NOT NULL PRIMARY KEY, levelsource TEXT NOT NULL DEFAULT 'None', levelfieldkey TEXT NULL, sourcehash TEXT NULL, " +
                    "downloadsenabled BOOLEAN NULL, completionthresholdpercent INTEGER NULL, publicsamplesenabled BOOLEAN NULL, autofollowonprogress BOOLEAN NULL, commentsenabledbydefault BOOLEAN NULL, updatedutc " + ts + ");",
                "CREATE TABLE IF NOT EXISTS librarylevels (propertyid TEXT NOT NULL, id TEXT NOT NULL PRIMARY KEY, key TEXT NOT NULL, label TEXT NOT NULL, color TEXT NULL, sortorder INTEGER NOT NULL DEFAULT 0, " +
                    "isretired BOOLEAN NOT NULL DEFAULT FALSE, createdutc " + ts + ", updatedutc " + ts + ");",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_librarylevels_key ON librarylevels (propertyid, lower(key));",
                "CREATE TABLE IF NOT EXISTS series (propertyid TEXT NOT NULL, id TEXT NOT NULL PRIMARY KEY, slug TEXT NOT NULL, title TEXT NOT NULL, summary TEXT NULL, description TEXT NULL, " +
                    "covermediaid TEXT NULL, status TEXT NOT NULL DEFAULT 'Draft', sortorder INTEGER NOT NULL DEFAULT 0, audience TEXT NOT NULL, lessoncount INTEGER NOT NULL DEFAULT 0, " +
                    "totaldurationseconds INTEGER NOT NULL DEFAULT 0, publishedutc TIMESTAMPTZ NULL, archivedutc TIMESTAMPTZ NULL, deletedutc TIMESTAMPTZ NULL, createdbyuserid TEXT NULL, " +
                    "createdutc " + ts + ", updatedutc " + ts + ");",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_series_slug ON series (propertyid, lower(slug)) WHERE deletedutc IS NULL;",
                "CREATE INDEX IF NOT EXISTS idx_series_order ON series (propertyid, status, sortorder, id);",
                "CREATE TABLE IF NOT EXISTS seriesslugs (propertyid TEXT NOT NULL, slug TEXT NOT NULL, seriesid TEXT NOT NULL, createdutc " + ts + ", PRIMARY KEY (propertyid, slug));",
                "CREATE TABLE IF NOT EXISTS serieslevels (propertyid TEXT NOT NULL, seriesid TEXT NOT NULL, levelkey TEXT NOT NULL, PRIMARY KEY (propertyid, seriesid, levelkey));",
                "CREATE TABLE IF NOT EXISTS lessons (propertyid TEXT NOT NULL, id TEXT NOT NULL PRIMARY KEY, seriesid TEXT NOT NULL, position INTEGER NOT NULL DEFAULT 0, slug TEXT NOT NULL, title TEXT NOT NULL, " +
                    "summary TEXT NULL, description TEXT NULL, kind TEXT NOT NULL, videomediaid TEXT NULL, durationseconds INTEGER NOT NULL DEFAULT 0, status TEXT NOT NULL DEFAULT 'Draft', " +
                    "publishatutc TIMESTAMPTZ NULL, audience TEXT NOT NULL, downloadable BOOLEAN NOT NULL DEFAULT TRUE, commentsenabled BOOLEAN NOT NULL DEFAULT TRUE, " +
                    "notifystaffoncomment BOOLEAN NOT NULL DEFAULT FALSE, commentcount INTEGER NOT NULL DEFAULT 0, reactiontotal INTEGER NOT NULL DEFAULT 0, openreportcount INTEGER NOT NULL DEFAULT 0, " +
                    "moderationstate TEXT NOT NULL DEFAULT 'None', moderatedutc TIMESTAMPTZ NULL, moderatedbyuserid TEXT NULL, moderationreason TEXT NULL, promotedfeedpostid TEXT NULL, " +
                    "publishedutc TIMESTAMPTZ NULL, archivedutc TIMESTAMPTZ NULL, deletedutc TIMESTAMPTZ NULL, createdbyuserid TEXT NULL, createdutc " + ts + ", updatedutc " + ts + ");",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_lessons_slug ON lessons (propertyid, seriesid, lower(slug)) WHERE deletedutc IS NULL;",
                "CREATE INDEX IF NOT EXISTS idx_lessons_position ON lessons (propertyid, seriesid, position, id);",
                "CREATE INDEX IF NOT EXISTS idx_lessons_published ON lessons (propertyid, publishedutc DESC, id DESC) WHERE status = 'Published' AND deletedutc IS NULL AND moderationstate = 'None';",
                "CREATE INDEX IF NOT EXISTS idx_lessons_schedule ON lessons (propertyid, publishatutc) WHERE status IN ('Scheduled', 'Processing');",
                "CREATE INDEX IF NOT EXISTS idx_lessons_media ON lessons (propertyid, videomediaid);",
                "CREATE TABLE IF NOT EXISTS lessonslugs (propertyid TEXT NOT NULL, seriesid TEXT NOT NULL, slug TEXT NOT NULL, lessonid TEXT NOT NULL, createdutc " + ts + ", PRIMARY KEY (propertyid, seriesid, slug));",
                "CREATE TABLE IF NOT EXISTS lessonlevels (propertyid TEXT NOT NULL, lessonid TEXT NOT NULL, levelkey TEXT NOT NULL, PRIMARY KEY (propertyid, lessonid, levelkey));",
                "CREATE INDEX IF NOT EXISTS idx_lessonlevels_level ON lessonlevels (propertyid, levelkey, lessonid);",
                "CREATE TABLE IF NOT EXISTS lessonmarkers (propertyid TEXT NOT NULL, id TEXT NOT NULL PRIMARY KEY, lessonid TEXT NOT NULL, seconds INTEGER NOT NULL, label TEXT NOT NULL);",
                "CREATE INDEX IF NOT EXISTS idx_lessonmarkers_lesson ON lessonmarkers (propertyid, lessonid, seconds);",
                "CREATE TABLE IF NOT EXISTS lessonresources (propertyid TEXT NOT NULL, id TEXT NOT NULL PRIMARY KEY, lessonid TEXT NOT NULL, kind TEXT NOT NULL, title TEXT NOT NULL, mediaid TEXT NULL, " +
                    "url TEXT NULL, text TEXT NULL, position INTEGER NOT NULL DEFAULT 0);",
                "CREATE INDEX IF NOT EXISTS idx_lessonresources_lesson ON lessonresources (propertyid, lessonid, position);",
                "CREATE TABLE IF NOT EXISTS lessoncaptions (propertyid TEXT NOT NULL, id TEXT NOT NULL PRIMARY KEY, lessonid TEXT NOT NULL, language TEXT NOT NULL, label TEXT NOT NULL, mediaid TEXT NOT NULL, " +
                    "position INTEGER NOT NULL DEFAULT 0);",
                "CREATE INDEX IF NOT EXISTS idx_lessoncaptions_lesson ON lessoncaptions (propertyid, lessonid, position);",
                "CREATE TABLE IF NOT EXISTS lessonprogress (propertyid TEXT NOT NULL, id TEXT NOT NULL PRIMARY KEY, lessonid TEXT NOT NULL, userid TEXT NOT NULL, positionseconds DOUBLE PRECISION NOT NULL DEFAULT 0, " +
                    "maxpositionseconds DOUBLE PRECISION NOT NULL DEFAULT 0, watchedseconds DOUBLE PRECISION NOT NULL DEFAULT 0, completed BOOLEAN NOT NULL DEFAULT FALSE, completedutc TIMESTAMPTZ NULL, " +
                    "completionsource TEXT NULL, lastwatchedutc " + ts + ", clientupdatedutc TIMESTAMPTZ NULL, createdutc " + ts + ", updatedutc " + ts + ");",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_lessonprogress ON lessonprogress (propertyid, userid, lessonid);",
                "CREATE INDEX IF NOT EXISTS idx_lessonprogress_continue ON lessonprogress (propertyid, userid, lastwatchedutc DESC, id DESC) WHERE completed = false;",
                "CREATE INDEX IF NOT EXISTS idx_lessonprogress_lesson ON lessonprogress (propertyid, lessonid, lastwatchedutc DESC, id DESC);",
                "CREATE TABLE IF NOT EXISTS lessonplays (propertyid TEXT NOT NULL, id TEXT NOT NULL PRIMARY KEY, lessonid TEXT NOT NULL, userid TEXT NULL, sessionid TEXT NOT NULL, platform TEXT NOT NULL, " +
                    "playedseconds DOUBLE PRECISION NOT NULL DEFAULT 0, startedutc " + ts + ", lastutc " + ts + ");",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_lessonplays_session ON lessonplays (propertyid, lessonid, sessionid);",
                "CREATE INDEX IF NOT EXISTS idx_lessonplays_started ON lessonplays (propertyid, startedutc);",
                "CREATE INDEX IF NOT EXISTS idx_lessonplays_user ON lessonplays (propertyid, userid, startedutc DESC);",
                "CREATE TABLE IF NOT EXISTS seriesfollows (propertyid TEXT NOT NULL, id TEXT NOT NULL PRIMARY KEY, seriesid TEXT NOT NULL, userid TEXT NOT NULL, source TEXT NOT NULL, " +
                    "isoptedout BOOLEAN NOT NULL DEFAULT FALSE, createdutc " + ts + ");",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_seriesfollows ON seriesfollows (propertyid, seriesid, userid);",
                "CREATE INDEX IF NOT EXISTS idx_seriesfollows_user ON seriesfollows (propertyid, userid);",
                "CREATE TABLE IF NOT EXISTS lessondownloads (propertyid TEXT NOT NULL, id TEXT NOT NULL PRIMARY KEY, lessonid TEXT NOT NULL, userid TEXT NOT NULL, deviceid TEXT NOT NULL, platform TEXT NOT NULL, " +
                    "licenseexpiresutc " + ts + ", revokedutc TIMESTAMPTZ NULL, revokereason TEXT NULL, lastrenewedutc TIMESTAMPTZ NULL, createdutc " + ts + ");",
                "CREATE INDEX IF NOT EXISTS idx_lessondownloads_user ON lessondownloads (propertyid, userid, revokedutc, licenseexpiresutc);",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_lessondownloads ON lessondownloads (propertyid, userid, deviceid, lessonid) WHERE revokedutc IS NULL;"
            };
        }

        /// <summary>
        /// Migration 53: extracted document text (captions and PDF resources) for search and assistant grounding.
        /// The Document media kind and the download_720p rendition use the existing TEXT columns of mediaattachments and
        /// mediavariants, so they need no column change.
        /// </summary>
        /// <returns>Statements.</returns>
        public static List<string> Documents()
        {
            string ts = "TIMESTAMPTZ NOT NULL";
            return new List<string>
            {
                "CREATE TABLE IF NOT EXISTS documentextractions (propertyid TEXT NOT NULL, mediaid TEXT NOT NULL, status TEXT NOT NULL, contenttype TEXT NOT NULL, extractor TEXT NULL, chunker TEXT NULL, " +
                    "chunkcount INTEGER NOT NULL DEFAULT 0, charactercount INTEGER NOT NULL DEFAULT 0, summary TEXT NULL, error TEXT NULL, attempts INTEGER NOT NULL DEFAULT 0, " +
                    "extractedutc TIMESTAMPTZ NULL, createdutc " + ts + ", updatedutc " + ts + ", PRIMARY KEY (propertyid, mediaid));",
                "CREATE INDEX IF NOT EXISTS idx_documentextractions_pending ON documentextractions (propertyid, status, updatedutc) WHERE status = 'Pending';",
                "CREATE TABLE IF NOT EXISTS documentchunks (propertyid TEXT NOT NULL, id TEXT NOT NULL PRIMARY KEY, mediaid TEXT NOT NULL, position INTEGER NOT NULL, pagenumber INTEGER NULL, " +
                    "text TEXT NOT NULL, createdutc " + ts + ");",
                "CREATE INDEX IF NOT EXISTS idx_documentchunks_media ON documentchunks (propertyid, mediaid, position);",
                "ALTER TABLE mediavariants ADD COLUMN IF NOT EXISTS sha256 TEXT NULL;"
            };
        }

        #endregion
    }
}

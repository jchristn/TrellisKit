namespace TrellisKit.Core.Database.Postgresql.Queries
{
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// PostgreSQL statements of migration 101, "Content changelog": the contentchanges table (one row per create, edit,
    /// and delete of a member contribution, docs/INTEGRATION.md content review contract), the trigger function that writes
    /// it in the same transaction as the change, and the triggers on every member-content table in <see cref="Sources"/>.
    /// The actor and client come from the transaction-local settings trelliskit.actor and trelliskit.client
    /// (<see cref="ContentChangeContext"/>), else the row's author and System.
    /// </summary>
    internal static class ContentChangesSchema
    {
        #region Internal-Members

        internal const int Version = 101;

        internal const string Description = "Content changelog";

        /// <summary>
        /// Longest snapshot the trigger stores (characters).
        /// </summary>
        internal const int MaxSnapshotCharacters = 20000;

        /// <summary>
        /// The watched member-content tables. Tables added later register here in a new migration.
        /// </summary>
        internal static readonly IReadOnlyList<ContentChangeSource> Sources = new List<ContentChangeSource>
        {
            new ContentChangeSource("feedposts", "feedposts", "FeedPost", "id", "authoruserid", "title,body", "", "deletedutc", filter: "{r}.sourcetype IS NULL AND {r}.purgedutc IS NULL"),
            new ContentChangeSource("comments", "comments", "Comment", "id", "authoruserid", "body", "", "deletedutc", "@targettype", "targetid", "{r}.purgedutc IS NULL"),
            new ContentChangeSource("forumthreads", "forumthreads", "Thread", "id", "authoruserid", "title,body", "", "deletedutc", "Forum", "forumid", "{r}.sourcetype IS NULL AND {r}.purgedutc IS NULL"),
            new ContentChangeSource("polloptions", "polloptions", "PollOption", "id", "", "label", "", null, "Poll", "pollid"),
            new ContentChangeSource("messages", "messages", "Message", "id", "authoruserid", "body", "", "deletedutc", "Conversation", "conversationid", "{r}.authoruserid IS NOT NULL"),
            new ContentChangeSource("venuereviews", "venuereviews", "VenueReview", "id", "authoruserid", "body", "", "deletedutc", "Venue", "venueid"),
            new ContentChangeSource("venuereviewreplies", "venuereviews", "VenueReviewReply", "id", "replyuserid", "replybody", "", "~replybody", "Venue", "venueid", requireText: true),
            new ContentChangeSource("users", "users", "User", "id", "id", "displayname,handle,bio,affiliation,city,region,goalsother", "avatarfile", "deletedutc"),
            new ContentChangeSource("profilevalues", "profilevalues", "User", "userid", "userid", "valuetext,othertext", "", null, "ProfileField", "fieldid"),
            new ContentChangeSource("mediaattachments", "mediaattachments", "Media", "id", "owneruserid", "alttext", "id", "deletedutc", "@attachedtargettype", "attachedtargetid"),
            new ContentChangeSource("directorysubmissions", "directorysubmissions", "DirectorySubmission", "id", "submitteruserid", "targetname,note", "", null, "@targettype", "targetid"),
            new ContentChangeSource("directorysubmissionfields", "directorysubmissionfields", "DirectorySubmission", "submissionid", "", "proposedvalue", "", null, "SubmissionField", "field", requireText: true),
            new ContentChangeSource("venuemedia", "venuemedia", "VenueMedia", "id", "uploaderuserid", "", "mediaid", "deletedutc", "Venue", "venueid"),
            new ContentChangeSource("events", "events", "Event", "id", "proposedbyuserid,createdbyuserid", "title,summary,description,locationname,proposalnote", "covermediaid", "deletedutc",
                filter: "{r}.recurrenceid IS NULL OR {r}.isexception OR {r}.status = 'Template'"),
            new ContentChangeSource("lessons", "lessons", "Lesson", "id", "createdbyuserid", "title,summary,description", "videomediaid", "deletedutc", "Series", "seriesid"),
            new ContentChangeSource("ideas", "ideas", "Idea", "id", "authoruserid,createdbyuserid", "title,body", "", "deletedutc", filter: "{r}.purgedutc IS NULL"),
            new ContentChangeSource("tickets", "tickets", "Ticket", "id", "requesteruserid", "title,body", ""),
            new ContentChangeSource("ticketmessages", "ticketmessages", "TicketMessage", "id", "authoruserid", "body", "", null, "Ticket", "ticketid", "{r}.kind = 'Reply'"),
            new ContentChangeSource("aimessages", "aimessages", "AssistantMessage", "id", "", "content", "", null, "AssistantConversation", "conversationid", requireText: true)
        };

        /// <summary>
        /// Watched tables whose own later migration creates the table and installs its triggers (not part of migration
        /// 101). Every watched table, for request attribution, is <see cref="AllSources"/>.
        /// </summary>
        internal static readonly IReadOnlyList<ContentChangeSource> LaterSources = new List<ContentChangeSource>
        {
            FavoriteSchema.NoteSource
        };

        /// <summary>
        /// Every watched source: <see cref="Sources"/> and <see cref="LaterSources"/>.
        /// </summary>
        internal static IEnumerable<ContentChangeSource> AllSources
        {
            get { return Sources.Concat(LaterSources); }
        }

        #endregion

        #region Internal-Methods

        internal static List<string> Statements()
        {
            List<string> statements = new List<string>
            {
                "CREATE TABLE IF NOT EXISTS contentchanges (propertyid TEXT NOT NULL, id TEXT NOT NULL PRIMARY KEY, targettype TEXT NOT NULL, targetid TEXT NOT NULL, contexttype TEXT NULL, " +
                    "contextid TEXT NULL, action TEXT NOT NULL, actoruserid TEXT NULL, authoruserid TEXT NULL, client TEXT NOT NULL, snapshot TEXT NOT NULL DEFAULT '', mediarefs TEXT NOT NULL DEFAULT '', " +
                    "reviewstatus TEXT NOT NULL DEFAULT 'Pending', reviewedutc TIMESTAMPTZ NULL, reviewedby TEXT NULL, reviewerkind TEXT NULL, categories TEXT NOT NULL DEFAULT '', " +
                    "severity TEXT NOT NULL DEFAULT 'None', reviewernote TEXT NULL, reviewattempts INTEGER NOT NULL DEFAULT 0, lasterror TEXT NULL, notifiedutc TIMESTAMPTZ NULL, " +
                    "moderationactionid TEXT NULL, createdutc TIMESTAMPTZ NOT NULL, updatedutc TIMESTAMPTZ NOT NULL);",
                "CREATE INDEX IF NOT EXISTS idx_contentchanges_created ON contentchanges (propertyid, createdutc DESC, id DESC);",
                "CREATE INDEX IF NOT EXISTS idx_contentchanges_status ON contentchanges (propertyid, reviewstatus, createdutc, id);",
                "CREATE INDEX IF NOT EXISTS idx_contentchanges_target ON contentchanges (propertyid, targettype, targetid, createdutc);",
                "CREATE INDEX IF NOT EXISTS idx_contentchanges_actor ON contentchanges (propertyid, actoruserid, createdutc) WHERE actoruserid IS NOT NULL;",
                "CREATE INDEX IF NOT EXISTS idx_contentchanges_author ON contentchanges (propertyid, authoruserid) WHERE authoruserid IS NOT NULL;",
                "CREATE INDEX IF NOT EXISTS idx_contentchanges_reviewed ON contentchanges (propertyid, reviewedutc) WHERE reviewstatus = 'Flagged';",
                Function()
            };
            statements.AddRange(Sources.SelectMany(s => s.Statements()));
            return statements;
        }

        #endregion

        #region Private-Methods

        private static string Function()
        {
            // TG_ARGV: 0 target type, 1 id column, 2 author columns, 3 text columns, 4 media columns, 5 deleted column
            // ("~column": the text column becoming null), 6 context type ("@column" reads a column), 7 context id column.
            return "CREATE OR REPLACE FUNCTION trelliskit_content_change() RETURNS trigger LANGUAGE plpgsql AS $fn$\n" +
                "DECLARE\n" +
                "  r jsonb;\n" +
                "  o jsonb;\n" +
                "  act text;\n" +
                "  snap text := '';\n" +
                "  media text := '';\n" +
                "  author text := NULL;\n" +
                "  col text;\n" +
                "  val text;\n" +
                "  ctype text := NULL;\n" +
                "  cid text := NULL;\n" +
                "  deleted text := TG_ARGV[5];\n" +
                "  status text := 'Pending';\n" +
                "  reviewed timestamptz := NULL;\n" +
                "  reviewer text := NULL;\n" +
                "  reviewerkind text := NULL;\n" +
                "  note text := NULL;\n" +
                "  stamp timestamptz := clock_timestamp();\n" +
                "BEGIN\n" +
                "  IF TG_OP = 'DELETE' THEN\n" +
                "    r := to_jsonb(OLD);\n" +
                "    act := 'Delete';\n" +
                "  ELSE\n" +
                "    r := to_jsonb(NEW);\n" +
                "    act := CASE WHEN TG_OP = 'INSERT' THEN 'Create' ELSE 'Update' END;\n" +
                "    IF TG_OP = 'UPDATE' AND deleted <> '' THEN\n" +
                "      o := to_jsonb(OLD);\n" +
                "      IF left(deleted, 1) = '~' THEN\n" +
                "        IF (o ->> substr(deleted, 2)) IS NOT NULL AND (r ->> substr(deleted, 2)) IS NULL THEN act := 'Delete'; END IF;\n" +
                "      ELSIF (o ->> deleted) IS NULL AND (r ->> deleted) IS NOT NULL THEN\n" +
                "        act := 'Delete';\n" +
                "      END IF;\n" +
                "    END IF;\n" +
                "  END IF;\n" +
                "  FOREACH col IN ARRAY string_to_array(TG_ARGV[2], ',') LOOP\n" +
                "    IF author IS NULL THEN author := NULLIF(r ->> col, ''); END IF;\n" +
                "  END LOOP;\n" +
                "  IF act <> 'Delete' THEN\n" +
                "    FOREACH col IN ARRAY string_to_array(TG_ARGV[3], ',') LOOP\n" +
                "      val := r ->> col;\n" +
                "      IF val IS NOT NULL AND btrim(val) <> '' THEN\n" +
                "        snap := snap || CASE WHEN snap = '' THEN '' ELSE E'\\n' END || col || ': ' || val;\n" +
                "      END IF;\n" +
                "    END LOOP;\n" +
                "    FOREACH col IN ARRAY string_to_array(TG_ARGV[4], ',') LOOP\n" +
                "      val := r ->> col;\n" +
                "      IF val IS NOT NULL AND val <> '' THEN media := media || CASE WHEN media = '' THEN '' ELSE ',' END || val; END IF;\n" +
                "    END LOOP;\n" +
                "  END IF;\n" +
                "  IF TG_ARGV[6] <> '' THEN\n" +
                "    ctype := CASE WHEN left(TG_ARGV[6], 1) = '@' THEN r ->> substr(TG_ARGV[6], 2) ELSE TG_ARGV[6] END;\n" +
                "  END IF;\n" +
                "  IF TG_ARGV[7] <> '' THEN cid := r ->> TG_ARGV[7]; END IF;\n" +
                "  IF act = 'Delete' OR (snap = '' AND media = '') THEN\n" +
                "    status := 'Clean';\n" +
                "    reviewed := stamp;\n" +
                "    reviewer := 'system';\n" +
                "    reviewerkind := 'System';\n" +
                "    note := CASE WHEN act = 'Delete' THEN 'Deletion: nothing to review.' ELSE 'No text or media to review.' END;\n" +
                "  END IF;\n" +
                "  INSERT INTO contentchanges (propertyid, id, targettype, targetid, contexttype, contextid, action, actoruserid, authoruserid, client, snapshot, mediarefs, " +
                "reviewstatus, reviewedutc, reviewedby, reviewerkind, reviewernote, createdutc, updatedutc) VALUES (r ->> 'propertyid', " +
                "'chg_' || to_char(stamp AT TIME ZONE 'UTC', 'YYYYMMDDHH24MISSUS') || substr(md5(random()::text || stamp::text), 1, 8), TG_ARGV[0], COALESCE(r ->> TG_ARGV[1], ''), " +
                "ctype, cid, act, COALESCE(NULLIF(current_setting('" + ContentChangeContext.ActorSetting + "', true), ''), author), author, " +
                "COALESCE(NULLIF(current_setting('" + ContentChangeContext.ClientSetting + "', true), ''), 'System'), left(snap, " + MaxSnapshotCharacters + "), media, " +
                "status, reviewed, reviewer, reviewerkind, note, stamp, stamp);\n" +
                "  RETURN NULL;\n" +
                "END;\n" +
                "$fn$;";
        }

        #endregion
    }
}

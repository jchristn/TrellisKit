namespace TrellisKit.Core.Database
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// The property-owned tables (every one carries propertyid; docs/MULTIPROPERTY_CONTRACT.md section 4.1) and the
    /// deployment-level tables. Tables added later are added here and created with propertyid from their first
    /// CREATE TABLE.
    /// </summary>
    public static class PropertyTables
    {
        #region Public-Members

        /// <summary>
        /// Property-owned tables, in purge order (children before parents).
        /// </summary>
        public static IReadOnlyList<string> Owned
        {
            get { return _Owned; }
        }

        /// <summary>
        /// The property-owned tables that existed when schema migration 6 added propertyid. Frozen: migration 6 alters
        /// exactly these, and later tables are created with propertyid.
        /// </summary>
        public static IReadOnlyList<string> ScopedByMigration6
        {
            get { return _ScopedByMigration6; }
        }

        /// <summary>
        /// Deployment-level tables (no propertyid).
        /// </summary>
        public static IReadOnlyList<string> Deployment
        {
            get { return _Deployment; }
        }

        #endregion

        #region Private-Members

        private static readonly List<string> _ScopedByMigration6 = new List<string>
        {
            "labels", "tags", "wordfilterhits", "wordfilterterms", "displaynamewords", "emailsends", "linkpreviews",
            "pushdeliveries", "pushtokens", "notificationfanouts", "notificationactors", "notifications", "notificationhours",
            "notificationpreferences", "moderationactions", "reports", "blocks", "userpermissions", "mentions", "reactioncounts",
            "reactions", "commentrevisions", "comments", "mediavariants", "mediaattachments", "feedpostrevisions", "feedposts",
            "accountexports", "auditrecords", "signupcoderedemptions", "signupcodetags", "signupcodes", "signupcodebatches",
            "entitlements", "subscriptionevents", "subscriptions", "usergoals", "rankhistoryentries", "roleassignments",
            "onetimetokens", "refreshtokens", "authsessions", "externalidentities", "credentials", "users", "requesthistory"
        };

        private static readonly List<string> _Owned = new List<string>
        {
            "favoritecollectionitems", "favoritecollections", "favorites",
            // Lesson and catalog links (migration 135): before lessons and catalogentries (no foreign keys).
            "lessoncatalogentries",
            "profileslugs", "profilepages",
            // Reviewed exercise and technique catalog (docs/CATALOG_IMPORTS.md): children before entries.
            "catalogimportitems", "catalogimportsources", "cataloglocalizednames", "catalogrelationships", "catalogsources", "catalogentryvalues", "catalogentries",
            "propertydocumentacceptances", "propertydocumentversions",
            "propertybrandoverrides", "propertybrandcolors", "propertybrandpalettes", "propertyai", "propertypalettechoices",
            "metricfirstactions", "metricseries", "metricannotations", "metricworkerruns", "metricalertstate", "metricstate",
            "usagehourly", "usagedaily", "usageactivity", "usagemediasizes", "usagestate",
            "documentchunks", "documentextractions",
            "lessondownloads", "seriesfollows", "lessonplays", "lessonprogress", "lessoncaptions", "lessonresources", "lessonmarkers", "lessonlevels", "lessonslugs", "lessons",
            "serieslevels", "seriesslugs", "series", "librarylevels", "libraryconfig",
            "calendarfeeds", "eventreminders", "eventrsvps", "eventinstructors", "eventhosts", "eventslugs", "events", "eventrecurrenceexceptions", "eventrecurrencedays",
            "eventrecurrences", "eventcategories", "eventconfig",
            "aiharnessprofiles", "aicalls", "aitaskroleexecutors", "aitaskroles", "jobitems", "jobruns", "jobdefinitions", "aiprompttemplateversions", "aiprompttemplates", "profilemedia", "venuemedia", "venuereviews", "geocoderesults", "directorysubmissionfields", "directorysubmissions", "venueinstructors", "instructorsociallinks", "instructorslugs", "instructors",
            "venuescheduleentries", "venuephotos", "venuesociallinks", "venueprograms", "venueslugs", "venues", "directoryprograms", "directoryconfig",
            "broadcastdeliveries", "broadcastsegmentfilters", "broadcasts", "broadcastsegments",
            "ideafollows", "ideavotes", "ideas", "ideastatuses",
            "ticketmessages", "tickets", "ticketcounters", "tickettyperecipients", "tickettypes",
            "realtimeconnections",
            "messaginglapses", "messagepushstate", "messageemailstate", "messagingcontacts", "messagingsettings",
            "messagereportsnapshotitems", "messagereportsnapshots", "messageattachments", "messages", "conversationmembers", "conversations",
            "aitoolcallresults", "aitoolcalls", "aitoolswitches", "aiassistanttools",
            "forumemaildigests", "aimoderationsnapshottoolcalls", "aimoderationsnapshotcitations", "aimoderationsnapshotmessages", "aimoderationsnapshots",
            "aibudgetalerts", "aidailyusage", "aiusagerollups", "aiusageapplied", "aifeedback", "aimessagestages", "aimessagecitations",
            "aimessages", "aiconversations", "aivisitors", "aiknowledgeitems", "aiknowledgeindexes", "aiknowledgesourcetags",
            "aiknowledgesourcelabels", "aiknowledgesources", "aiassistantembedorigins", "aiassistantstarterprompts", "aiassistantmodels",
            "aiassistants", "aiendpointhealthchecks", "aiendpointheaders", "aiendpoints",
            "profilesearchwords", "profilehistoryvalues", "profilehistoryentries", "profilevalueoptions", "profilevalues",
            "profilefieldsubfields", "profilefieldoptions", "profilefields", "emailmessages", "emailsuppressions",
            "emailtemplateoverrides", "emailstaffevents", "propertysettings", "propertyaccessfeatures", "propertyaccess",
            "updateeventdetails", "appstringcatalog", "remotestrings", "bannerclients", "banners", "remoteassets",
            "appcontentchanges", "appcontentstate", "contentversions", "killswitchstate", "featureflagconditionvalues", "featureflagconditions",
            "featureflagrules", "featureflagclients", "featureflagvariants", "featureflags", "updaterolloutstages", "updateeventcounts", "updateevents",
            "updateinstallfailures", "updateinstalls", "updatesigningkeys", "updatedeploykeychannels", "updatedeploykeys", "updatechannels",
            "updategcobjects", "updatesourcemaps", "updateassetrefs", "updateassets", "updategroupconfigs", "updates", "updategroups",
            "searchqueue", "forummemberprefs", "forummutes", "forumreads", "threadreads", "threadfollows", "forumfollows", "pollvotes", "polloptions", "polls",
            "forumthreadrevisions", "forumthreads", "forumthreadtypes", "forums",
            "labels", "tags", "wordfilterhits", "wordfilterterms", "displaynamewords", "emailsends", "linkpreviews",
            "pushdeliveries", "pushtokens", "notificationfanouts", "notificationactors", "notifications", "notificationhours",
            "notificationpreferences", "moderationactions", "reports", "blocks", "userpermissions", "mentions", "reactioncounts",
            "reactions", "commentrevisions", "comments", "mediavariants", "mediaattachments", "feedpostrevisions", "feedposts",
            "accountexports", "auditrecords", "signupcoderedemptions", "signupcodetags", "signupcodes", "signupcodebatches",
            "entitlements", "subscriptionevents", "subscriptions", "usergoals", "rankhistoryentries", "roleassignments",
            "onetimetokens", "refreshtokens", "authsessions", "externalidentities", "credentials", "users", "requesthistory",
            // Verified ranks, curricula, and attendance (migrations 132 to 134).
            "attendance", "curriculumprogress", "curriculumitems", "curricula", "lineageentries", "rankrequirements", "rankpromotions", "memberranks",
            // Last on purpose: deleting the content tables above fires the content changelog triggers, so the changelog
            // is purged after them (docs/INTEGRATION.md content review contract).
            "contentchanges"
        };

        private static readonly List<string> _Deployment = new List<string>
        {
            "schema_migrations", "jobleases", "properties", "propertydomains", "propertyapps", "platformadmins", "platformsessions"
        };

        private static readonly HashSet<string> _OwnedSet = new HashSet<string>(_Owned, StringComparer.Ordinal);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether a table is property-owned.
        /// </summary>
        /// <param name="table">Lowercase table name.</param>
        /// <returns>True when property-owned.</returns>
        public static bool IsOwned(string? table)
        {
            return !String.IsNullOrEmpty(table) && _OwnedSet.Contains(table);
        }

        #endregion
    }
}

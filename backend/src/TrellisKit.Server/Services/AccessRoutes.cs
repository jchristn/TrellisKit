namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using TrellisKit.Core;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// The one table of feature and action declarations for routes in the Member route group
    /// (docs/ACCESS_MODELS_CONTRACT.md section 2), and the routes that do not exist in a model (section 4). A route in
    /// the Member group without a declaration needs a membership in FullyGated and PartiallyGated and a signed-in
    /// account in Open; AccessModelSuite fails when the OpenAPI document lists such a route. New content routes add a
    /// rule here when they are registered.
    /// Thread safety: immutable; safe to call concurrently.
    /// </summary>
    public static class AccessRoutes
    {
        #region Private-Members

        private const string _Api = Constants.ApiPrefix;

        #endregion

        #region Public-Methods

        /// <summary>
        /// The declaration of a Member group route.
        /// </summary>
        /// <param name="method">HTTP method (GET, POST, PUT, PATCH, DELETE), case-insensitive; null is treated as GET.</param>
        /// <param name="path">Path without query.</param>
        /// <returns>Declaration, or null when the route is not declared.</returns>
        public static RouteAccess? Find(string? method, string? path)
        {
            string m = (method ?? "GET").Trim().ToUpperInvariant();
            string[] s = Segments(path);
            if (s.Length < 3 || s[0] != "api" || s[1] != "v1") return null;
            bool read = m == "GET" || (m == "POST" && s[s.Length - 1] == "enumerate");
            switch (s[2])
            {
                case "feed":
                    return Feed(m, s, read);
                case "comments":
                    if (s.Length < 4) return null;
                    if (read) return Declare(FeatureKeyEnum.Feed, AccessActionEnum.Read, s.Length == 4 || (s.Length >= 5 && s[4] == "children"), m, s);
                    return Declare(FeatureKeyEnum.Feed, AccessActionEnum.Participate, false, m, s);
                case "reactions":
                    if (read && s.Length >= 4 && s[3] == "set" && (s.Length == 4 || (s.Length == 5 && s[4] == "enumerate"))) return Declare(null, AccessActionEnum.Read, true, m, s);
                    return null;
                case "media":
                    if (s.Length >= 4 && (s[3] == "files" || s[3] == "gallery" || s[3] == "private" || s[3] == "local-uploads" || s[3] == "avatars")) return null;
                    if (read || (s.Length == 4 && s[3] == "grant" && m == "POST")) return Declare(null, AccessActionEnum.Read, false, m, s);
                    return Declare(null, AccessActionEnum.Write, false, m, s);
                case "users":
                    if (s.Length >= 5 && s[4] == "media") return Declare(FeatureKeyEnum.Members, read ? AccessActionEnum.List : AccessActionEnum.Write, false, m, s);
                    if (s.Length == 5 && s[4] == "block") return Account(m, s);
                    if (s.Length == 4 && m == "GET") return Declare(FeatureKeyEnum.Members, AccessActionEnum.Read, false, m, s);
                    return null;
                case "members":
                    // The member directory (/members/directory, /members/directory/enumerate) is the directory feature;
                    // mention search (/members, /members/search, /members/enumerate) is the members feature.
                    if (read && s.Length >= 4 && s[3] == "directory") return Declare(FeatureKeyEnum.Directory, AccessActionEnum.List, false, m, s);
                    if (read) return Declare(FeatureKeyEnum.Members, AccessActionEnum.List, false, m, s);
                    return null;
                case "forums":
                    return Forums(m, s, read);
                case "threads":
                    return Threads(m, s, read);
                case "polls":
                    // /polls/{id}/votes, /polls/{id}/close, /polls/{id}/voters[/enumerate]
                    if (s.Length < 5) return null;
                    if (s[4] == "votes") return Declare(FeatureKeyEnum.Forums, AccessActionEnum.Participate, false, m, s);
                    if (s[4] == "voters") return Declare(FeatureKeyEnum.Forums, AccessActionEnum.Read, false, m, s);
                    return Declare(FeatureKeyEnum.Forums, AccessActionEnum.Write, false, m, s);
                case "search":
                    return read ? Declare(FeatureKeyEnum.Search, AccessActionEnum.List, false, m, s) : null;
                case "venues":
                case "instructors":
                    return DirectoryEntries(m, s, read);
                case "directory":
                    return DirectoryOther(m, s, read);
                case "events":
                    return Events(m, s, read);
                case "series":
                    return Series(m, s, read);
                case "lessons":
                    return Lessons(m, s, read);
                case "library":
                    // /library/levels/enumerate, and POST /library/search (a read that carries its query in the body; each kind
                    // applies its own object access, docs/TRELLISKIT.md section 15).
                    if (s.Length == 4 && s[3] == "search" && m == "POST") return Stub(Declare(FeatureKeyEnum.Library, AccessActionEnum.List, true, m, s));
                    return read ? Stub(Declare(FeatureKeyEnum.Library, AccessActionEnum.List, true, m, s)) : null;
                case "me":
                    // GET /me/training is the one me route in the Member group: session only, and gated like a feature read
                    // (FullyGated needs a membership; PartiallyGated only when training is a gated feature).
                    if (s.Length == 4 && s[3] == "training" && m == "GET") return Declare(FeatureKeyEnum.Training, AccessActionEnum.List, false, m, s);
                    return null;
                case "catalog":
                    // /catalog/entries[/enumerate|/{idOrSlug}|/{id}/relationships]: each published entry decides.
                    return read ? Stub(Declare(FeatureKeyEnum.Catalog, AccessActionEnum.List, true, m, s)) : null;
                case "conversations":
                case "messages":
                    return Messaging(m, s, read);
                case "ideas":
                    return Ideas(m, s, read);
                case "curricula":
                    // /curricula, /curricula/enumerate, /curricula/{id}: verified ranks feature (ranks.enabled), never signed-out.
                    return Declare(FeatureKeyEnum.Ranks, read ? (s.Length == 4 && s[3] != "enumerate" ? AccessActionEnum.Read : AccessActionEnum.List) : AccessActionEnum.Write, false, m, s);
                case "attendance":
                    // /attendance/check-in (Participate), /attendance/check-in-code (Read).
                    return Declare(FeatureKeyEnum.Ranks, read ? AccessActionEnum.Read : AccessActionEnum.Participate, false, m, s);
                case "notifications":
                case "reports":
                case "link-previews":
                case "labels":
                case "tags":
                case "metadata":
                    return Account(m, s);
                default:
                    return null;
            }
        }

        /// <summary>
        /// Whether a route does not exist in the current access model or feature set: purchase, membership, and
        /// sign-up code routes and the admin writes that grant entitlements in Open; the member sign-up code routes when
        /// features.signupCodes is false. Such routes return 404 NotAvailable for every caller.
        /// </summary>
        /// <param name="model">Access model.</param>
        /// <param name="signupCodesFeature">property.json features.signupCodes.</param>
        /// <param name="method">HTTP method, case-insensitive; null is treated as GET.</param>
        /// <param name="path">Path without query.</param>
        /// <returns>True when the route is not available.</returns>
        public static bool IsNotAvailable(AccessModelEnum model, bool signupCodesFeature, string? method, string? path)
        {
            return IsNotAvailable(model, signupCodesFeature, true, method, path);
        }

        /// <summary>
        /// Whether a route does not exist in the current access model or feature set, as
        /// <see cref="IsNotAvailable(AccessModelEnum, bool, string?, string?)"/>, plus the member directory routes when
        /// features.directory is false.
        /// </summary>
        /// <param name="model">Access model.</param>
        /// <param name="signupCodesFeature">property.json features.signupCodes.</param>
        /// <param name="directoryFeature">property.json features.directory.</param>
        /// <param name="method">HTTP method, case-insensitive; null is treated as GET.</param>
        /// <param name="path">Path without query.</param>
        /// <returns>True when the route is not available.</returns>
        public static bool IsNotAvailable(AccessModelEnum model, bool signupCodesFeature, bool directoryFeature, string? method, string? path)
        {
            return IsNotAvailable(model, signupCodesFeature, directoryFeature, true, method, path);
        }

        /// <summary>
        /// Whether a route does not exist in the current access model or feature set, as
        /// <see cref="IsNotAvailable(AccessModelEnum, bool, bool, string?, string?)"/>, plus the global search routes
        /// (/search, /search/enumerate) when features.search is false.
        /// </summary>
        /// <param name="model">Access model.</param>
        /// <param name="signupCodesFeature">property.json features.signupCodes.</param>
        /// <param name="directoryFeature">property.json features.directory.</param>
        /// <param name="searchFeature">property.json features.search.</param>
        /// <param name="method">HTTP method, case-insensitive; null is treated as GET.</param>
        /// <param name="path">Path without query.</param>
        /// <returns>True when the route is not available.</returns>
        public static bool IsNotAvailable(AccessModelEnum model, bool signupCodesFeature, bool directoryFeature, bool searchFeature, string? method, string? path)
        {
            return IsNotAvailable(model, signupCodesFeature, directoryFeature, searchFeature, true, true, method, path);
        }

        /// <summary>
        /// Whether a route is turned off for the property (404 NotAvailable), including the Phase 4 feature switches
        /// (docs/PHASE4_CONTRACT.md section 0.4): features.directory, features.events, and features.library hide their
        /// member, public, me, calendar, and admin routes.
        /// </summary>
        /// <param name="model">Access model.</param>
        /// <param name="signupCodesFeature">features.signupCodes.</param>
        /// <param name="directoryFeature">features.directory.</param>
        /// <param name="searchFeature">features.search.</param>
        /// <param name="eventsFeature">features.events.</param>
        /// <param name="libraryFeature">features.library.</param>
        /// <param name="method">HTTP method.</param>
        /// <param name="path">Request path.</param>
        /// <returns>True when the route is not available.</returns>
        public static bool IsNotAvailable(AccessModelEnum model, bool signupCodesFeature, bool directoryFeature, bool searchFeature, bool eventsFeature, bool libraryFeature, string? method, string? path)
        {
            string m = (method ?? "GET").Trim().ToUpperInvariant();
            string[] s = Segments(path);
            if (s.Length < 3 || s[0] != "api" || s[1] != "v1") return false;
            if (!searchFeature && s[2] == "search") return true;
            if (!directoryFeature && s[2] == "members" && s.Length >= 4 && s[3] == "directory") return true;
            string? phase4 = Phase4Feature(s);
            if (phase4 == "directory" && !directoryFeature) return true;
            if (phase4 == "events" && !eventsFeature) return true;
            if (phase4 == "library" && !libraryFeature) return true;
            bool signupCodes = s[2] == "signup-codes";
            if (signupCodes && !signupCodesFeature) return true;
            if (model != AccessModelEnum.Open) return false;
            if (signupCodes) return true;
            if (s[2] == "membership") return !(s.Length >= 4 && s[3] == "history");
            if (s[2] != "admin" || s.Length < 4) return false;
            if (s[3] == "members" && s.Length == 6 && s[5] == "comp" && m == "POST") return true;
            if (s[3] != "signup-codes") return false;
            if (s.Length == 4 && m == "POST") return true;
            if (s.Length == 5 && s[4] == "batches" && m == "POST") return true;
            return s.Length == 5 && m == "PUT" && s[4] != "batches";
        }

        #endregion

        #region Private-Methods

        private static RouteAccess Feed(string m, string[] s, bool read)
        {
            // /feed, /feed/enumerate, /feed/{id}, /feed/{id}/pin|unpin|revisions|comments|reactions[/...]
            if (s.Length == 3 || (s.Length == 4 && s[3] == "enumerate"))
                return read ? Declare(FeatureKeyEnum.Feed, AccessActionEnum.List, true, m, s) : Declare(FeatureKeyEnum.Feed, AccessActionEnum.Write, false, m, s);
            if (s.Length == 4)
                return read ? Declare(FeatureKeyEnum.Feed, AccessActionEnum.Read, true, m, s) : Declare(FeatureKeyEnum.Feed, AccessActionEnum.Write, false, m, s);
            string sub = s[4];
            if (sub == "comments")
                return read ? Declare(FeatureKeyEnum.Feed, AccessActionEnum.List, true, m, s) : Declare(FeatureKeyEnum.Feed, AccessActionEnum.Participate, false, m, s);
            if (sub == "reactions")
                return read ? Declare(FeatureKeyEnum.Feed, AccessActionEnum.Read, false, m, s) : Declare(FeatureKeyEnum.Feed, AccessActionEnum.Participate, false, m, s);
            if (sub == "revisions") return Declare(FeatureKeyEnum.Feed, AccessActionEnum.Read, false, m, s);
            return Declare(FeatureKeyEnum.Feed, read ? AccessActionEnum.Read : AccessActionEnum.Write, false, m, s);
        }

        private static RouteAccess Forums(string m, string[] s, bool read)
        {
            // /forums, /forums/enumerate, /forums/{idOrSlug}, /forums/{id}/follow|read|threads[/enumerate]
            if (s.Length == 3 || (s.Length == 4 && s[3] == "enumerate")) return Declare(FeatureKeyEnum.Forums, AccessActionEnum.List, true, m, s);
            if (s.Length == 4) return Declare(FeatureKeyEnum.Forums, AccessActionEnum.Read, true, m, s);
            string sub = s[4];
            if (sub == "threads")
                return read ? Declare(FeatureKeyEnum.Forums, AccessActionEnum.List, true, m, s) : Declare(FeatureKeyEnum.Forums, AccessActionEnum.Write, false, m, s);
            if (sub == "read") return Declare(FeatureKeyEnum.Forums, AccessActionEnum.Read, false, m, s);
            return Declare(FeatureKeyEnum.Forums, AccessActionEnum.Participate, false, m, s);
        }

        private static RouteAccess Threads(string m, string[] s, bool read)
        {
            // /threads, /threads/enumerate, /threads/{id}, /threads/{id}/comments|reactions|revisions|follow|...
            if (s.Length == 3 || (s.Length == 4 && s[3] == "enumerate")) return Declare(FeatureKeyEnum.Forums, AccessActionEnum.List, true, m, s);
            if (s.Length == 4)
                return read ? Declare(FeatureKeyEnum.Forums, AccessActionEnum.Read, true, m, s) : Declare(FeatureKeyEnum.Forums, AccessActionEnum.Write, false, m, s);
            string sub = s[4];
            if (sub == "comments")
                return read ? Declare(FeatureKeyEnum.Forums, AccessActionEnum.List, true, m, s) : Declare(FeatureKeyEnum.Forums, AccessActionEnum.Participate, false, m, s);
            if (sub == "reactions" || sub == "follow")
                return read ? Declare(FeatureKeyEnum.Forums, AccessActionEnum.Read, false, m, s) : Declare(FeatureKeyEnum.Forums, AccessActionEnum.Participate, false, m, s);
            if (sub == "revisions") return Declare(FeatureKeyEnum.Forums, AccessActionEnum.Read, false, m, s);
            return Declare(FeatureKeyEnum.Forums, read ? AccessActionEnum.Read : AccessActionEnum.Write, false, m, s);
        }

        private static RouteAccess DirectoryEntries(string m, string[] s, bool read)
        {
            // /venues, /venues/enumerate, /venues/{idOrSlug}, /venues/{id}/instructors/enumerate (and the same for instructors)
            // /venues/{id}/reviews[/...] and /venues/{id}/media[/...]: signed-in member content, no locked stubs (sections 3.12, 3.13).
            if (s.Length >= 5 && s[2] == "venues" && (s[4] == "reviews" || s[4] == "media"))
                return Declare(FeatureKeyEnum.Directory, read ? (s.Length == 6 && s[5] != "enumerate" ? AccessActionEnum.Read : AccessActionEnum.List) : AccessActionEnum.Write, false, m, s);
            if (s.Length == 3 || (s.Length == 4 && s[3] == "enumerate") || s.Length >= 5) return Stub(Declare(FeatureKeyEnum.Directory, read ? AccessActionEnum.List : AccessActionEnum.Write, read, m, s));
            return Stub(Declare(FeatureKeyEnum.Directory, read ? AccessActionEnum.Read : AccessActionEnum.Write, read, m, s));
        }

        private static RouteAccess? DirectoryOther(string m, string[] s, bool read)
        {
            // /directory/map/enumerate, /directory/programs/enumerate, /directory/tiles/{*path}, /directory/geocode,
            // /directory/submissions, /directory/submissions/{id}/withdraw
            if (s.Length < 4) return null;
            switch (s[3])
            {
                case "map":
                case "programs":
                    return Stub(Declare(FeatureKeyEnum.Directory, AccessActionEnum.List, true, m, s));
                case "tiles":
                    return Stub(Declare(FeatureKeyEnum.Directory, AccessActionEnum.Read, true, m, s));
                case "geocode":
                    return Stub(Declare(FeatureKeyEnum.Directory, AccessActionEnum.Read, false, m, s));
                case "submissions":
                    return Declare(FeatureKeyEnum.Directory, AccessActionEnum.Write, false, m, s);
                default:
                    return null;
            }
        }

        private static RouteAccess Events(string m, string[] s, bool read)
        {
            // /events, /events/enumerate, /events/categories/enumerate, /events/map/enumerate, /events/proposals, /events/{idOrSlug},
            // /events/{id}/rsvp|attendees|ics|comments|reactions|promote|unpromote
            if (s.Length == 3 || (s.Length == 4 && s[3] == "enumerate")) return Stub(Declare(FeatureKeyEnum.Events, AccessActionEnum.List, true, m, s));
            if (s[3] == "categories") return Stub(Declare(FeatureKeyEnum.Events, AccessActionEnum.List, true, m, s));
            // /events/map/enumerate: the events map (a list of the viewport's events).
            if (s[3] == "map" && s.Length == 5 && s[4] == "enumerate") return Stub(Declare(FeatureKeyEnum.Events, AccessActionEnum.List, true, m, s));
            if (s[3] == "proposals") return Declare(FeatureKeyEnum.Events, AccessActionEnum.Write, false, m, s);
            if (s.Length == 4) return Stub(Declare(FeatureKeyEnum.Events, AccessActionEnum.Read, true, m, s));
            switch (s[4])
            {
                case "rsvp":
                    return Declare(FeatureKeyEnum.Events, AccessActionEnum.Participate, false, m, s);
                case "attendees":
                    return Stub(Declare(FeatureKeyEnum.Events, AccessActionEnum.List, false, m, s));
                case "occurrences":
                    // /events/{id}/occurrences/enumerate: the occurrences of a recurring event (resolved decision 11).
                    return Stub(Declare(FeatureKeyEnum.Events, AccessActionEnum.List, true, m, s));
                case "ics":
                    return Stub(Declare(FeatureKeyEnum.Events, AccessActionEnum.Read, false, m, s));
                case "comments":
                    return read ? Stub(Declare(FeatureKeyEnum.Events, AccessActionEnum.List, true, m, s)) : Declare(FeatureKeyEnum.Events, AccessActionEnum.Participate, false, m, s);
                case "reactions":
                    return read ? Stub(Declare(FeatureKeyEnum.Events, AccessActionEnum.Read, false, m, s)) : Declare(FeatureKeyEnum.Events, AccessActionEnum.Participate, false, m, s);
                default:
                    return Declare(FeatureKeyEnum.Events, read ? AccessActionEnum.Read : AccessActionEnum.Write, false, m, s);
            }
        }

        private static RouteAccess Series(string m, string[] s, bool read)
        {
            // /series, /series/enumerate, /series/{idOrSlug}, /series/{id}/lessons/enumerate, /series/{id}/follow
            if (s.Length == 3 || (s.Length == 4 && s[3] == "enumerate")) return Stub(Declare(FeatureKeyEnum.Library, AccessActionEnum.List, true, m, s));
            if (s.Length == 4) return Stub(Declare(FeatureKeyEnum.Library, AccessActionEnum.Read, true, m, s));
            if (s[4] == "lessons") return Stub(Declare(FeatureKeyEnum.Library, AccessActionEnum.List, true, m, s));
            return Stub(Declare(FeatureKeyEnum.Library, read ? AccessActionEnum.Read : AccessActionEnum.Participate, false, m, s));
        }

        private static RouteAccess Lessons(string m, string[] s, bool read)
        {
            // /lessons, /lessons/enumerate, /lessons/{idOrSlug}, /lessons/{id}/progress|complete|uncomplete|downloads|
            // comments|reactions|promote|unpromote
            if (s.Length == 3 || (s.Length == 4 && s[3] == "enumerate")) return Stub(Declare(FeatureKeyEnum.Library, AccessActionEnum.List, true, m, s));
            if (s.Length == 4) return Stub(Declare(FeatureKeyEnum.Library, AccessActionEnum.Read, true, m, s));
            switch (s[4])
            {
                case "catalog-entries":
                    // /lessons/{id}/catalog-entries[/enumerate]: techniques in a lesson the caller can see (each lesson decides).
                    return read ? Stub(Declare(FeatureKeyEnum.Library, AccessActionEnum.List, true, m, s)) : Stub(Declare(FeatureKeyEnum.Library, AccessActionEnum.Participate, false, m, s));
                case "downloads":
                    // Downloads need a membership in gated models (sample lessons are streamed, not downloaded).
                    return Declare(FeatureKeyEnum.Library, AccessActionEnum.Participate, false, m, s);
                case "comments":
                    return read ? Stub(Declare(FeatureKeyEnum.Library, AccessActionEnum.List, true, m, s)) : Stub(Declare(FeatureKeyEnum.Library, AccessActionEnum.Participate, false, m, s));
                case "reactions":
                    return read ? Stub(Declare(FeatureKeyEnum.Library, AccessActionEnum.Read, false, m, s)) : Stub(Declare(FeatureKeyEnum.Library, AccessActionEnum.Participate, false, m, s));
                case "promote":
                case "unpromote":
                    return Declare(FeatureKeyEnum.Library, AccessActionEnum.Write, false, m, s);
                default:
                    return Stub(Declare(FeatureKeyEnum.Library, read ? AccessActionEnum.Read : AccessActionEnum.Participate, false, m, s));
            }
        }

        private static RouteAccess Stub(RouteAccess access)
        {
            access.ObjectGated = true;
            return access;
        }


        private static RouteAccess Messaging(string m, string[] s, bool read)
        {
            // docs/PHASE5_CONTRACT.md section 3.1: never signed-out eligible. Reads, receipts (read, delivered), and sync are
            // Read or List; sending, edits, deletes, and reactions are Participate; conversation state changes are Write.
            string last = s[s.Length - 1];
            if (read) return Declare(FeatureKeyEnum.Messaging, last == "enumerate" ? AccessActionEnum.List : AccessActionEnum.Read, false, m, s);
            if (last == "read" || last == "delivered") return Declare(FeatureKeyEnum.Messaging, AccessActionEnum.Read, false, m, s);
            if (s[2] == "messages" || (s.Length == 5 && s[4] == "messages")) return Declare(FeatureKeyEnum.Messaging, AccessActionEnum.Participate, false, m, s);
            return Declare(FeatureKeyEnum.Messaging, AccessActionEnum.Write, false, m, s);
        }

        private static RouteAccess Ideas(string m, string[] s, bool read)
        {
            // docs/PHASE5_CONTRACT.md section 7.1: /ideas, /ideas/enumerate, /ideas/{id} (List, Read, signed-out eligible);
            // /ideas/{id}/comments (List or Participate), /vote, /follow, /reactions (Participate); create and edit (Write).
            // /ideas/statuses and /ideas/statuses/enumerate: the configured statuses (filter chips), a list like the board.
            if (s.Length >= 4 && s[3] == "statuses") return read ? Declare(FeatureKeyEnum.Ideas, AccessActionEnum.List, true, m, s) : Declare(FeatureKeyEnum.Ideas, AccessActionEnum.Write, false, m, s);
            if (s.Length == 3 || (s.Length == 4 && s[3] == "enumerate"))
                return read ? Declare(FeatureKeyEnum.Ideas, AccessActionEnum.List, true, m, s) : Declare(FeatureKeyEnum.Ideas, AccessActionEnum.Write, false, m, s);
            if (s.Length == 4)
                return read ? Declare(FeatureKeyEnum.Ideas, AccessActionEnum.Read, true, m, s) : Declare(FeatureKeyEnum.Ideas, AccessActionEnum.Write, false, m, s);
            string sub = s[4];
            if (sub == "comments")
                return read ? Declare(FeatureKeyEnum.Ideas, AccessActionEnum.List, true, m, s) : Declare(FeatureKeyEnum.Ideas, AccessActionEnum.Participate, false, m, s);
            if (read) return Declare(FeatureKeyEnum.Ideas, AccessActionEnum.Read, false, m, s);
            return Declare(FeatureKeyEnum.Ideas, AccessActionEnum.Participate, false, m, s);
        }

        private static RouteAccess Account(string m, string[] s)
        {
            RouteAccess access = Declare(null, m == "GET" ? AccessActionEnum.Read : AccessActionEnum.Write, false, m, s);
            access.Account = true;
            return access;
        }

        private static RouteAccess Declare(FeatureKeyEnum? feature, AccessActionEnum action, bool anonymous, string m, string[] s)
        {
            return new RouteAccess
            {
                Feature = feature,
                Action = action,
                AnonymousEligible = anonymous && (action == AccessActionEnum.Read || action == AccessActionEnum.List),
                Pattern = m + " /" + String.Join("/", s)
            };
        }

        /// <summary>
        /// The Phase 4 feature key a route belongs to (directory, events, or library), or null. Covers member, me,
        /// public, calendar, and admin routes.
        /// </summary>
        /// <param name="path">Request path.</param>
        /// <returns>Feature key or null.</returns>
        public static string? Phase4FeatureOf(string? path)
        {
            string[] s = Segments(path);
            if (s.Length < 3 || s[0] != "api" || s[1] != "v1") return null;
            return Phase4Feature(s);
        }

        private static string? Phase4Feature(string[] s)
        {
            string first = s[2];
            string second = s.Length >= 4 ? s[3] : String.Empty;
            switch (first)
            {
                case "venues":
                case "instructors":
                case "directory":
                    return "directory";
                case "events":
                case "calendar":
                    return "events";
                case "series":
                case "lessons":
                case "library":
                    return "library";
                case "me":
                    if (second == "directory" || second == "instructor-links") return "directory";
                    if (second == "events" || second == "calendar-feed") return "events";
                    if (second == "library") return "library";
                    return null;
                case "public":
                    if (second == "venues" || second == "directory") return "directory";
                    if (second == "events") return "events";
                    if (second == "lessons") return "library";
                    return null;
                case "admin":
                    if (second == "venues" || second == "instructors" || second == "directory") return "directory";
                    if (second == "events") return "events";
                    if (second == "series" || second == "lessons" || second == "library") return "library";
                    if (second == "metrics" && s.Length >= 5) return s[4] == "events" ? "events" : s[4] == "library" ? "library" : null;
                    return null;
                default:
                    return null;
            }
        }

        private static string[] Segments(string? path)
        {
            string p = (path ?? String.Empty).Split('?')[0];
            return p.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(x => x.ToLowerInvariant()).ToArray();
        }

        #endregion
    }
}

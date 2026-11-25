namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core;

    /// <summary>
    /// The engine feature keys that have kill switches (docs/LIVE_UPDATES_CONTRACT.md section 13.1) and the map from
    /// API routes to features used by server enforcement (section 13.2). Admin and moderation routes are never mapped.
    /// Thread safety: stateless.
    /// </summary>
    public static class KillSwitchCatalog
    {
        #region Public-Members

        /// <summary>
        /// Engine feature keys (fixed list, extended by later phases).
        /// </summary>
        public static readonly IReadOnlyList<string> FeatureKeys = new List<string>
        {
            "signups", "purchases", "signupCodes", "feed", "feedComposer", "comments", "reactions", "mediaUpload", "videoUpload", "linkPreviews",
            "forums", "polls", "directory", "events", "library", "libraryDownloads", "messaging", "assistants", "notificationsPush", "search",
            "support", "ideas", "broadcasts", "realtime",
            "training",
            "saves",
            "catalog"
        };

        #endregion

        #region Private-Members

        private const string _Api = Constants.ApiPrefix;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether a key is an engine feature key.
        /// </summary>
        /// <param name="key">Key.</param>
        /// <returns>True when listed.</returns>
        public static bool IsFeature(string? key)
        {
            return key != null && ((List<string>)FeatureKeys).Contains(key);
        }

        /// <summary>
        /// Features whose kill switches block a route, most specific first.
        /// </summary>
        /// <param name="method">HTTP method (GET, POST, ...).</param>
        /// <param name="path">Path without query.</param>
        /// <returns>Feature keys, empty when none applies.</returns>
        public static List<string> FeaturesFor(string method, string? path)
        {
            List<string> result = new List<string>();
            string p = (path ?? String.Empty).TrimEnd('/');
            string m = (method ?? String.Empty).ToUpperInvariant();
            bool write = m == "POST" || m == "PUT" || m == "PATCH" || m == "DELETE";
            if (p.Length == 0 || Starts(p, _Api + "/admin") || Starts(p, _Api + "/platform") || Starts(p, _Api + "/settings") || Starts(p, _Api + "/request-history")) return result;
            if (p.Equals(_Api + "/auth/register", StringComparison.OrdinalIgnoreCase)) result.Add("signups");
            if (write && Starts(p, _Api + "/membership")) result.Add("purchases");
            if (Starts(p, _Api + "/signup-codes")) result.Add("signupCodes");
            if (p.Contains("/comments", StringComparison.OrdinalIgnoreCase)) result.Add("comments");
            if (p.Contains("/reactions", StringComparison.OrdinalIgnoreCase)) result.Add("reactions");
            if (Starts(p, _Api + "/feed") || Starts(p, _Api + "/public/feed"))
            {
                if (write && !p.Contains("/comments", StringComparison.OrdinalIgnoreCase) && !p.Contains("/reactions", StringComparison.OrdinalIgnoreCase)) result.Add("feedComposer");
                result.Add("feed");
            }
            if (write && (Starts(p, _Api + "/media/uploads") || (Starts(p, _Api + "/media/") && (p.EndsWith("/upload-urls", StringComparison.OrdinalIgnoreCase) || p.EndsWith("/complete", StringComparison.OrdinalIgnoreCase))))) result.Add("mediaUpload");
            if (Starts(p, _Api + "/link-previews")) result.Add("linkPreviews");
            if (Starts(p, _Api + "/forums") || Starts(p, _Api + "/threads")) result.Add("forums");
            if (Starts(p, _Api + "/polls")) result.Add("polls");
            if (Starts(p, _Api + "/directory") || Starts(p, _Api + "/venues") || Starts(p, _Api + "/instructors") || Starts(p, _Api + "/me/directory") ||
                Starts(p, _Api + "/me/instructor-links") || Starts(p, _Api + "/public/venues") || Starts(p, _Api + "/public/directory")) result.Add("directory");
            if (Starts(p, _Api + "/events") || Starts(p, _Api + "/calendar") || Starts(p, _Api + "/me/events") || Starts(p, _Api + "/me/calendar-feed") ||
                Starts(p, _Api + "/public/events")) result.Add("events");
            if (Starts(p, _Api + "/library") || Starts(p, _Api + "/series") || Starts(p, _Api + "/lessons") || Starts(p, _Api + "/me/library") ||
                Starts(p, _Api + "/public/lessons")) result.Add("library");
            // training blocks only the hub; the library, events, and saves keep their own switches.
            if (Starts(p, _Api + "/me/training")) result.Add("training");
            // libraryDownloads blocks only the download routes (and stops background downloads on mobile).
            if ((Starts(p, _Api + "/lessons") && p.EndsWith("/downloads", StringComparison.OrdinalIgnoreCase)) || Starts(p, _Api + "/me/library/downloads")) result.Add("libraryDownloads");
            if (Starts(p, _Api + "/conversations") || Starts(p, _Api + "/messages") || Starts(p, _Api + "/me/messaging")) result.Add("messaging");
            if (Starts(p, _Api + "/tickets") || Starts(p, _Api + "/support")) result.Add("support");
            if (Starts(p, _Api + "/ideas") || Starts(p, _Api + "/public/ideas")) result.Add("ideas");
            if (Starts(p, _Api + "/ws")) result.Add("realtime");
            if (Starts(p, _Api + "/assistants")) result.Add("assistants");
            if (Starts(p, _Api + "/me/push-tokens")) result.Add("notificationsPush");
            if (Starts(p, _Api + "/members/search") || Starts(p, _Api + "/members/enumerate") || Starts(p, _Api + "/search")) result.Add("search");
            if (Starts(p, _Api + "/me/saves") || Starts(p, _Api + "/me/save-collections")) result.Add("saves");
            // The reviewed exercise and technique catalog has its own switch, independent of library.
            if (Starts(p, _Api + "/catalog") || Starts(p, _Api + "/public/catalog")) result.Add("catalog");
            return result;
        }

        #endregion

        #region Private-Methods

        private static bool Starts(string path, string prefix)
        {
            return path.Equals(prefix, StringComparison.OrdinalIgnoreCase) || path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase);
        }

        #endregion
    }
}

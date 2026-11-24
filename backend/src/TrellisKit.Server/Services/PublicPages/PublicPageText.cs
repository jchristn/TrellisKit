namespace TrellisKit.Server.Services.PublicPages
{
    using System;
    using System.Globalization;

    /// <summary>
    /// The engine's neutral English text of public landing pages. Property words (name, venue and instructor labels, the
    /// member word) are passed in; nothing here names a property.
    /// </summary>
    public static class PublicPageText
    {
        #region Public-Members

        /// <summary>
        /// Sign in link.
        /// </summary>
        public const string SignIn = "Sign in";

        /// <summary>
        /// Skip link.
        /// </summary>
        public const string SkipToContent = "Skip to content";

        /// <summary>
        /// Venue app link.
        /// </summary>
        public const string OpenVenueInApp = "Open in the app";

        /// <summary>
        /// Event app link.
        /// </summary>
        public const string OpenEventInApp = "RSVP in the app";

        /// <summary>
        /// Profile app link.
        /// </summary>
        public const string OpenProfileInApp = "View in the app";

        /// <summary>
        /// About heading.
        /// </summary>
        public const string About = "About";

        /// <summary>
        /// Details heading.
        /// </summary>
        public const string Details = "Details";

        /// <summary>
        /// Address fact label.
        /// </summary>
        public const string Address = "Address";

        /// <summary>
        /// Location fact label.
        /// </summary>
        public const string Location = "Location";

        /// <summary>
        /// Schedule heading.
        /// </summary>
        public const string Schedule = "Schedule";

        /// <summary>
        /// Programs fact label.
        /// </summary>
        public const string Programs = "Programs";

        /// <summary>
        /// Visitors fact label.
        /// </summary>
        public const string Visitors = "Visitors";

        /// <summary>
        /// Website fact label.
        /// </summary>
        public const string Website = "Website";

        /// <summary>
        /// Phone fact label.
        /// </summary>
        public const string Phone = "Phone";

        /// <summary>
        /// Links fact label.
        /// </summary>
        public const string Links = "Links";

        /// <summary>
        /// Rating fact label.
        /// </summary>
        public const string Rating = "Rating";

        /// <summary>
        /// Photos heading.
        /// </summary>
        public const string Photos = "Photos";

        /// <summary>
        /// When fact label.
        /// </summary>
        public const string When = "When";

        /// <summary>
        /// Online fact value.
        /// </summary>
        public const string Online = "Online";

        /// <summary>
        /// Format fact label.
        /// </summary>
        public const string Format = "Format";

        /// <summary>
        /// Category fact label.
        /// </summary>
        public const string Category = "Category";

        /// <summary>
        /// Spots fact label.
        /// </summary>
        public const string Spots = "Spots";

        /// <summary>
        /// Canceled event banner.
        /// </summary>
        public const string Canceled = "This event was canceled.";

        /// <summary>
        /// Past event banner.
        /// </summary>
        public const string Ended = "This event has ended.";

        /// <summary>
        /// Not found heading.
        /// </summary>
        public const string NotFoundHeading = "Page not found";

        /// <summary>
        /// Not found body.
        /// </summary>
        public const string NotFoundBody = "This page does not exist or is no longer public.";

        /// <summary>
        /// Home link of the not found page.
        /// </summary>
        public const string Home = "Go to the home page";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Join link label.
        /// </summary>
        /// <param name="name">Property name.</param>
        /// <returns>Text.</returns>
        public static string Join(string name)
        {
            return "Join " + name;
        }

        /// <summary>
        /// Call to action heading.
        /// </summary>
        /// <param name="name">Property name.</param>
        /// <returns>Text.</returns>
        public static string CtaHeading(string name)
        {
            return "More on " + name;
        }

        /// <summary>
        /// Call to action body.
        /// </summary>
        /// <param name="name">Property name.</param>
        /// <param name="member">The property's word for a member.</param>
        /// <returns>Text.</returns>
        public static string CtaBody(string name, string member)
        {
            return "Open this page in the " + name + " app to see everything a " + member + " sees, or join to take part.";
        }

        /// <summary>
        /// Instructors heading (the property's plural instructor label).
        /// </summary>
        /// <param name="plural">Plural label.</param>
        /// <returns>Text.</returns>
        public static string Instructors(string plural)
        {
            return plural;
        }

        /// <summary>
        /// The average rating sentence.
        /// </summary>
        /// <param name="average">Average (1 to 5).</param>
        /// <param name="count">Ratings.</param>
        /// <returns>Text.</returns>
        public static string RatingSummary(double average, int count)
        {
            return average.ToString("0.0", CultureInfo.InvariantCulture) + " out of 5 from " + count.ToString(CultureInfo.InvariantCulture) + (count == 1 ? " rating" : " ratings");
        }

        /// <summary>
        /// Spots left.
        /// </summary>
        /// <param name="left">Spots left.</param>
        /// <returns>Text.</returns>
        public static string SpotsLeft(int left)
        {
            return left <= 0 ? "Full" : left.ToString(CultureInfo.InvariantCulture) + (left == 1 ? " spot left" : " spots left");
        }

        /// <summary>
        /// A visitor policy in words.
        /// </summary>
        /// <param name="policy">Policy name.</param>
        /// <returns>Text, or null for Unknown.</returns>
        public static string? VisitorPolicy(string policy)
        {
            switch (policy)
            {
                case "Welcome": return "Visitors welcome";
                case "ByArrangement": return "Visitors by arrangement";
                case "MembersOnly": return "Members only";
                default: return null;
            }
        }

        /// <summary>
        /// An event format in words.
        /// </summary>
        /// <param name="format">Format name.</param>
        /// <returns>Text.</returns>
        public static string EventFormat(string format)
        {
            switch (format)
            {
                case "Online": return "Online";
                case "Hybrid": return "In person and online";
                default: return "In person";
            }
        }

        /// <summary>
        /// Title of a page: the object name and the property name.
        /// </summary>
        /// <param name="what">Object name.</param>
        /// <param name="name">Property name.</param>
        /// <returns>Text.</returns>
        public static string Title(string what, string name)
        {
            return String.IsNullOrWhiteSpace(what) ? name : what + " | " + name;
        }

        #endregion
    }
}

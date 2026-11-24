namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// What an event edit changed (drives EventUpdated notices, ICS SEQUENCE, reminder rescheduling, and waitlist
    /// promotion; docs/PHASE4_CONTRACT.md section 4.4).
    /// </summary>
    public class EventChangeSet
    {
        #region Public-Members

        /// <summary>
        /// The start, end, time zone, or all-day flag changed.
        /// </summary>
        public bool Time { get; set; } = false;

        /// <summary>
        /// The venue, place, or address changed.
        /// </summary>
        public bool Location { get; set; } = false;

        /// <summary>
        /// The format changed.
        /// </summary>
        public bool Format { get; set; } = false;

        /// <summary>
        /// Capacity raised, removed, or the waitlist turned on (promotion may run).
        /// </summary>
        public bool MoreSpace { get; set; } = false;

        /// <summary>
        /// Any stored field changed.
        /// </summary>
        public bool Any { get; set; } = false;

        /// <summary>
        /// Whether attendees should hear about the change (time, location, or format).
        /// </summary>
        public bool Material
        {
            get { return Time || Location || Format; }
        }

        /// <summary>
        /// Flags for a fan-out source (T time, L location, F format).
        /// </summary>
        public string Flags
        {
            get { return (Time ? "T" : String.Empty) + (Location ? "L" : String.Empty) + (Format ? "F" : String.Empty); }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Merge another change set into this one.
        /// </summary>
        /// <param name="other">Other.</param>
        /// <exception cref="ArgumentNullException">The other set is null.</exception>
        public void Merge(EventChangeSet other)
        {
            ArgumentNullException.ThrowIfNull(other);
            Time |= other.Time;
            Location |= other.Location;
            Format |= other.Format;
            MoreSpace |= other.MoreSpace;
            Any |= other.Any;
        }

        /// <summary>
        /// A short English description of the material change, for example "time and location changed".
        /// </summary>
        /// <param name="flags">Flags (T, L, F).</param>
        /// <returns>Text.</returns>
        public static string Describe(string? flags)
        {
            List<string> parts = new List<string>();
            string f = flags ?? String.Empty;
            if (f.Contains('T', StringComparison.Ordinal)) parts.Add("time");
            if (f.Contains('L', StringComparison.Ordinal)) parts.Add("location");
            if (f.Contains('F', StringComparison.Ordinal)) parts.Add("format");
            if (parts.Count == 0) return "details changed";
            if (parts.Count == 1) return parts[0] + " changed";
            if (parts.Count == 2) return parts[0] + " and " + parts[1] + " changed";
            return parts[0] + ", " + parts[1] + ", and " + parts[2] + " changed";
        }

        #endregion
    }
}

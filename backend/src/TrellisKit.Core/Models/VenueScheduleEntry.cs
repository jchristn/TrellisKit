namespace TrellisKit.Core.Models
{
    /// <summary>One weekly opening interval or session.</summary>
    public class VenueScheduleEntry
    {
        /// <summary>Day index, Monday = 0 through Sunday = 6.</summary>
        public int DayOfWeek { get; set; } = -1;
        /// <summary>Local start time, HH:mm (00:00 to 23:59).</summary>
        public string StartTime { get; set; } = "";
        /// <summary>Local end time, HH:mm (00:00 to 23:59).</summary>
        public string EndTime { get; set; } = "";
        /// <summary>True when the interval ends the following day, including a full 24-hour interval.</summary>
        public bool EndsNextDay { get; set; }
        /// <summary>Required session name (up to 120); null for opening hours.</summary>
        public string? Name { get; set; }
        /// <summary>Optional instructor name (up to 120); null for opening hours.</summary>
        public string? Instructor { get; set; }
        /// <summary>Independent copy of this entry.</summary>
        public VenueScheduleEntry Clone() => (VenueScheduleEntry)MemberwiseClone();
    }
}

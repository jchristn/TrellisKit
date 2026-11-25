namespace TrellisKit.Core.Models
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json.Serialization;

    /// <summary>A weekly schedule in local wall-clock time; absent days have unknown hours, not closed hours.</summary>
    public class VenueSchedule
    {
        /// <summary>IANA time zone for all entries.</summary>
        public string? TimeZone { get; set; }
        /// <summary>Opening intervals, at most 28, with multiple intervals per day allowed.</summary>
        public List<VenueScheduleEntry> OpeningHours { get; set; } = new List<VenueScheduleEntry>();
        /// <summary>Weekly sessions (the property names them through directory.sessionLabel), at most 100.</summary>
        public List<VenueScheduleEntry> Sessions { get; set; } = new List<VenueScheduleEntry>();
        /// <summary>
        /// Deprecated input alias of <see cref="Sessions"/> named classes, accepted from clients built before the rename.
        /// When it is sent and sessions is empty, its entries become the sessions. Never serialized.
        /// </summary>
        [JsonPropertyName("classes")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<VenueScheduleEntry>? LegacyClasses
        {
            get { return null; }
            set { if (value != null && value.Count > 0 && Sessions.Count == 0) Sessions = value; }
        }
        /// <summary>Additional schedule notes, at most 1000 characters.</summary>
        public string? Notes { get; set; }

        /// <summary>Independent copy for editing and moderation previews.</summary>
        public VenueSchedule Clone() => new VenueSchedule { TimeZone = TimeZone, Notes = Notes,
            OpeningHours = OpeningHours.Select(e => e.Clone()).ToList(), Sessions = Sessions.Select(e => e.Clone()).ToList() };
    }
}

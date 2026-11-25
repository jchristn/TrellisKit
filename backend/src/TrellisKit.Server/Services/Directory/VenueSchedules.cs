namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using TrellisKit.Core.Models;

    /// <summary>Validation and display of bounded weekly venue schedules.</summary>
    public static class VenueSchedules
    {
        /// <summary>Validate and normalize a schedule without changing the caller's object.</summary>
        public static VenueSchedule? Validate(VenueSchedule? schedule, Dictionary<string, string> errors)
        {
            ArgumentNullException.ThrowIfNull(errors);
            if (schedule == null) return null;
            VenueSchedule result = new VenueSchedule
            {
                TimeZone = DirectoryText.TimeZone(schedule.TimeZone, "schedule.timeZone", errors),
                Notes = DirectoryText.Text(schedule.Notes, 1000, "schedule.notes", errors)
            };
            List<VenueScheduleEntry> Entries(List<VenueScheduleEntry>? source, string field, int max, bool sessions)
            {
                if (source == null) { errors[field] = "Use an array."; return new List<VenueScheduleEntry>(); }
                if (source.Count > max) errors[field] = "At most " + max + " entries.";
                List<VenueScheduleEntry> entries = new List<VenueScheduleEntry>();
                foreach (VenueScheduleEntry? row in source.Take(max))
                {
                    string key = field + "[" + entries.Count + "]";
                    if (row == null) { errors[key] = "An entry is required."; continue; }
                    VenueScheduleEntry entry = new VenueScheduleEntry { DayOfWeek = row.DayOfWeek, StartTime = row.StartTime, EndTime = row.EndTime, EndsNextDay = row.EndsNextDay,
                        Name = DirectoryText.Text(row.Name, 120, key + ".name", errors), Instructor = DirectoryText.Text(row.Instructor, 120, key + ".instructor", errors) };
                    if (entry.DayOfWeek < 0 || entry.DayOfWeek > 6) errors[key + ".dayOfWeek"] = "Use 0 (Monday) through 6 (Sunday).";
                    bool startValid = TimeOnly.TryParseExact(entry.StartTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out TimeOnly start);
                    bool endValid = TimeOnly.TryParseExact(entry.EndTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out TimeOnly end);
                    if (!startValid) errors[key + ".startTime"] = "Use HH:mm, 00:00 through 23:59.";
                    if (!endValid) errors[key + ".endTime"] = "Use HH:mm, 00:00 through 23:59.";
                    if (startValid && endValid)
                    {
                        double duration = (end.ToTimeSpan() - start.ToTimeSpan()).TotalMinutes + (entry.EndsNextDay ? 1440 : 0);
                        if (duration <= 0 || duration > 1440) errors[key + ".endTime"] = "The interval must be longer than zero and at most 24 hours; mark overnight intervals as ending the next day.";
                    }
                    if (sessions && entry.Name == null) errors[key + ".name"] = "A session name is required.";
                    if (!sessions && (entry.Name != null || entry.Instructor != null)) errors[key] = "Opening hours cannot have a session name or instructor.";
                    entries.Add(entry);
                }
                return entries;
            }
            result.OpeningHours = Entries(schedule.OpeningHours, "schedule.openingHours", 28, false);
            result.Sessions = Entries(schedule.Sessions, "schedule.sessions", 100, true);
            if ((result.OpeningHours.Count > 0 || result.Sessions.Count > 0) && result.TimeZone == null) errors["schedule.timeZone"] = "A time zone is required when hours or sessions are supplied.";
            return result;
        }

        /// <summary>Plain text for public HTML, search and assistant grounding.</summary>
        public static string Text(VenueSchedule schedule)
        {
            ArgumentNullException.ThrowIfNull(schedule);
            string[] days = { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" };
            List<string> lines = new List<string>();
            if (schedule.TimeZone != null) lines.Add(schedule.TimeZone);
            foreach (VenueScheduleEntry row in schedule.OpeningHours.Concat(schedule.Sessions).OrderBy(e => e.DayOfWeek).ThenBy(e => e.StartTime))
                lines.Add(days[Math.Clamp(row.DayOfWeek, 0, 6)] + " " + row.StartTime + "–" + row.EndTime + (row.EndsNextDay ? " (next day)" : "") + (row.Name == null ? "" : " " + row.Name) + (row.Instructor == null ? "" : " - " + row.Instructor));
            if (schedule.Notes != null) lines.Add(schedule.Notes);
            return String.Join("\n", lines);
        }
    }
}

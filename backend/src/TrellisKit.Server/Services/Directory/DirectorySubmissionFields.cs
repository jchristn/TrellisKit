namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text.Json;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;
    using TrellisKit.Server.Serialization;

    /// <summary>
    /// The structured field rows of directory submissions (docs/PHASE4_CONTRACT.md sections 2.4 and 3.7): the editable
    /// fields per target type, conversion between request bodies, stored rows (one row per value of a multi-value field,
    /// a single null row clears a field), and the current values of a venue or instructor. Thread safe (stateless).
    /// </summary>
    public static class DirectorySubmissionFields
    {
        #region Public-Members

        /// <summary>
        /// Editable venue fields, in display order.
        /// </summary>
        public static readonly IReadOnlyList<string> VenueFields = new List<string>
        {
            "name", "affiliation", "summary", "description", "address", "addressVisibility", "geo", "phone", "email", "websiteUrl", "socialLinks", "scheduleText", "schedule", "programs", "visitorPolicy", "visitorPolicyNote", "photoMediaIds"
        };

        /// <summary>
        /// Editable instructor fields, in display order.
        /// </summary>
        public static readonly IReadOnlyList<string> InstructorFields = new List<string>
        {
            "displayName", "bio", "photoMediaId", "rank", "rankLevel", "affiliation", "websiteUrl", "socialLinks", "venueIds"
        };

        /// <summary>
        /// The stored sub-fields of an address.
        /// </summary>
        public static readonly IReadOnlyList<string> AddressParts = new List<string> { "line1", "line2", "locality", "region", "postalCode", "countryCode" };

        /// <summary>
        /// Fields stored as one row per value.
        /// </summary>
        public static readonly IReadOnlyCollection<string> MultiValue = new HashSet<string>(StringComparer.Ordinal) { "socialLinks", "programs", "photoMediaIds", "venueIds" };

        /// <summary>Structured schedule row keys; entry fields use matching positions.</summary>
        public static readonly IReadOnlyList<string> ScheduleParts = new[] { "set", "timeZone", "notes", "openingHours.dayOfWeek", "openingHours.startTime", "openingHours.endTime", "openingHours.endsNextDay", "sessions.dayOfWeek", "sessions.startTime", "sessions.endTime", "sessions.endsNextDay", "sessions.name", "sessions.instructor" };

        private static bool IsMulti(string field) => MultiValue.Contains(field) || field.StartsWith("schedule.openingHours.", StringComparison.Ordinal) || field.StartsWith("schedule.sessions.", StringComparison.Ordinal);

        #endregion

        #region Public-Methods

        /// <summary>
        /// The editable fields of a target type.
        /// </summary>
        /// <param name="targetType">Venue or Instructor.</param>
        /// <returns>Fields.</returns>
        public static IReadOnlyList<string> FieldsOf(TargetTypeEnum targetType)
        {
            return targetType == TargetTypeEnum.Instructor ? InstructorFields : VenueFields;
        }

        /// <summary>
        /// Validate the field names of a body and convert it to a venue request.
        /// </summary>
        /// <param name="fields">Body fields.</param>
        /// <param name="present">Fields present in the body.</param>
        /// <returns>Request.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed for unknown fields or malformed values.</exception>
        public static VenueRequest Venue(Dictionary<string, JsonElement>? fields, out HashSet<string> present)
        {
            present = Present(fields, VenueFields);
            return Convert<VenueRequest>(fields);
        }

        /// <summary>
        /// Validate the field names of a body and convert it to an instructor request.
        /// </summary>
        /// <param name="fields">Body fields.</param>
        /// <param name="present">Fields present in the body.</param>
        /// <returns>Request.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed for unknown fields or malformed values.</exception>
        public static InstructorRequest Instructor(Dictionary<string, JsonElement>? fields, out HashSet<string> present)
        {
            present = Present(fields, InstructorFields);
            return Convert<InstructorRequest>(fields);
        }

        /// <summary>
        /// Field rows of the present fields of a venue (the proposed values).
        /// </summary>
        /// <param name="venue">The venue as it would be after the change.</param>
        /// <param name="present">Fields to include.</param>
        /// <returns>Rows.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public static List<DirectorySubmissionField> Rows(Venue venue, IEnumerable<string> present)
        {
            ArgumentNullException.ThrowIfNull(venue);
            ArgumentNullException.ThrowIfNull(present);
            Dictionary<string, List<string?>> values = Values(venue);
            return ToRows(values, present.SelectMany(Expand));
        }

        /// <summary>
        /// Field rows of the present fields of an instructor (the proposed values).
        /// </summary>
        /// <param name="instructor">The instructor as it would be after the change.</param>
        /// <param name="venueIds">Venues to link.</param>
        /// <param name="present">Fields to include.</param>
        /// <returns>Rows.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public static List<DirectorySubmissionField> Rows(Instructor instructor, List<string> venueIds, IEnumerable<string> present)
        {
            ArgumentNullException.ThrowIfNull(instructor);
            ArgumentNullException.ThrowIfNull(venueIds);
            ArgumentNullException.ThrowIfNull(present);
            Dictionary<string, List<string?>> values = Values(instructor);
            values["venueIds"] = venueIds.Cast<string?>().ToList();
            return ToRows(values, present);
        }

        /// <summary>
        /// The stored values of a venue by row field name.
        /// </summary>
        /// <param name="venue">Venue.</param>
        /// <returns>Values.</returns>
        /// <exception cref="ArgumentNullException">The venue is null.</exception>
        public static Dictionary<string, List<string?>> Values(Venue venue)
        {
            ArgumentNullException.ThrowIfNull(venue);
            Dictionary<string, List<string?>> result = new Dictionary<string, List<string?>>(StringComparer.Ordinal)
            {
                { "name", One(venue.Name) },
                { "affiliation", One(venue.Affiliation) },
                { "summary", One(venue.Summary) },
                { "description", One(venue.Description) },
                { "address.line1", One(venue.Line1) },
                { "address.line2", One(venue.Line2) },
                { "address.locality", One(venue.Locality) },
                { "address.region", One(venue.Region) },
                { "address.postalCode", One(venue.PostalCode) },
                { "address.countryCode", One(venue.CountryCode) },
                { "addressVisibility", One(venue.AddressVisibility.ToString()) },
                { "phone", One(venue.Phone) },
                { "email", One(venue.Email) },
                { "websiteUrl", One(venue.WebsiteUrl) },
                { "socialLinks", venue.SocialLinks.OrderBy(l => l.Position).Select(l => (string?)(l.Kind + " " + l.Url)).ToList() },
                { "scheduleText", One(venue.ScheduleText) },
                { "programs", venue.Programs.Cast<string?>().ToList() },
                { "visitorPolicy", One(venue.VisitorPolicy.ToString()) },
                { "visitorPolicyNote", One(venue.VisitorPolicyNote) },
                { "photoMediaIds", venue.PhotoMediaIds.Cast<string?>().ToList() }
            };
            result["schedule.set"] = One(venue.Schedule == null ? "false" : "true");
            result["schedule.timeZone"] = One(venue.Schedule?.TimeZone);
            result["schedule.notes"] = One(venue.Schedule?.Notes);
            void Entries(string kind, List<VenueScheduleEntry> entries)
            {
                result["schedule." + kind + ".dayOfWeek"] = entries.Select(e => (string?)e.DayOfWeek.ToString(CultureInfo.InvariantCulture)).ToList();
                result["schedule." + kind + ".startTime"] = entries.Select(e => (string?)e.StartTime).ToList();
                result["schedule." + kind + ".endTime"] = entries.Select(e => (string?)e.EndTime).ToList();
                result["schedule." + kind + ".endsNextDay"] = entries.Select(e => (string?)(e.EndsNextDay ? "true" : "false")).ToList();
                if (kind == "sessions")
                {
                    result["schedule.sessions.name"] = entries.Select(e => e.Name).ToList();
                    result["schedule.sessions.instructor"] = entries.Select(e => e.Instructor).ToList();
                }
            }
            Entries("openingHours", venue.Schedule?.OpeningHours ?? new List<VenueScheduleEntry>());
            Entries("sessions", venue.Schedule?.Sessions ?? new List<VenueScheduleEntry>());
            return result;
        }

        /// <summary>
        /// The stored values of an instructor by row field name.
        /// </summary>
        /// <param name="instructor">Instructor.</param>
        /// <returns>Values (venueIds empty).</returns>
        /// <exception cref="ArgumentNullException">The instructor is null.</exception>
        public static Dictionary<string, List<string?>> Values(Instructor instructor)
        {
            ArgumentNullException.ThrowIfNull(instructor);
            return new Dictionary<string, List<string?>>(StringComparer.Ordinal)
            {
                { "displayName", One(instructor.DisplayName) },
                { "bio", One(instructor.Bio) },
                { "photoMediaId", One(instructor.PhotoMediaId) },
                { "rank", One(instructor.RankKey) },
                { "rankLevel", One(instructor.RankLevel?.ToString(CultureInfo.InvariantCulture)) },
                { "affiliation", One(instructor.Affiliation) },
                { "websiteUrl", One(instructor.WebsiteUrl) },
                { "socialLinks", instructor.SocialLinks.OrderBy(l => l.Position).Select(l => (string?)(l.Kind + " " + l.Url)).ToList() },
                { "venueIds", new List<string?>() }
            };
        }

        /// <summary>
        /// Rebuild a venue request from rows (the request field names present are returned).
        /// </summary>
        /// <param name="rows">Rows.</param>
        /// <param name="present">Request fields present.</param>
        /// <returns>Request.</returns>
        /// <exception cref="ArgumentNullException">The rows are null.</exception>
        public static VenueRequest VenueRequestOf(List<DirectorySubmissionField> rows, out HashSet<string> present)
        {
            ArgumentNullException.ThrowIfNull(rows);
            Dictionary<string, List<string?>> v = Group(rows);
            present = new HashSet<string>(v.Keys.Select(k => k.StartsWith("address.", StringComparison.Ordinal) ? "address" : k.StartsWith("schedule.", StringComparison.Ordinal) ? "schedule" : k), StringComparer.Ordinal);
            VenueRequest r = new VenueRequest
            {
                Name = First(v, "name"),
                Affiliation = First(v, "affiliation"),
                Summary = First(v, "summary"),
                Description = First(v, "description"),
                Phone = First(v, "phone"),
                Email = First(v, "email"),
                WebsiteUrl = First(v, "websiteUrl"),
                ScheduleText = First(v, "scheduleText"),
                Schedule = ScheduleOf(v),
                VisitorPolicyNote = First(v, "visitorPolicyNote"),
                Programs = v.ContainsKey("programs") ? NonNull(v["programs"]) : null,
                PhotoMediaIds = v.ContainsKey("photoMediaIds") ? NonNull(v["photoMediaIds"]) : null,
                SocialLinks = v.ContainsKey("socialLinks") ? Links(v["socialLinks"]) : null
            };
            if (present.Contains("address"))
                r.Address = new AddressRequest { Line1 = First(v, "address.line1"), Line2 = First(v, "address.line2"), Locality = First(v, "address.locality"), Region = First(v, "address.region"), PostalCode = First(v, "address.postalCode"), CountryCode = First(v, "address.countryCode") };
            if (Enum.TryParse(First(v, "addressVisibility"), true, out AddressVisibilityEnum visibility)) r.AddressVisibility = visibility;
            if (Enum.TryParse(First(v, "visitorPolicy"), true, out VisitorPolicyEnum policy)) r.VisitorPolicy = policy;
            return r;
        }

        /// <summary>
        /// Rebuild an instructor request from rows.
        /// </summary>
        /// <param name="rows">Rows.</param>
        /// <param name="present">Request fields present.</param>
        /// <returns>Request.</returns>
        /// <exception cref="ArgumentNullException">The rows are null.</exception>
        public static InstructorRequest InstructorRequestOf(List<DirectorySubmissionField> rows, out HashSet<string> present)
        {
            ArgumentNullException.ThrowIfNull(rows);
            Dictionary<string, List<string?>> v = Group(rows);
            present = new HashSet<string>(v.Keys, StringComparer.Ordinal);
            InstructorRequest r = new InstructorRequest
            {
                DisplayName = First(v, "displayName"),
                Bio = First(v, "bio"),
                PhotoMediaId = First(v, "photoMediaId"),
                Rank = First(v, "rank"),
                Affiliation = First(v, "affiliation"),
                WebsiteUrl = First(v, "websiteUrl"),
                SocialLinks = v.ContainsKey("socialLinks") ? Links(v["socialLinks"]) : null,
                VenueIds = v.ContainsKey("venueIds") ? NonNull(v["venueIds"]) : null
            };
            if (Int32.TryParse(First(v, "rankLevel"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int level)) r.RankLevel = level;
            return r;
        }

        /// <summary>
        /// The change list of a submission: one entry per field (address parts separately), current from the rows (or
        /// the given fresh values), proposed from the rows; multi-value fields as lists.
        /// </summary>
        /// <param name="submission">Submission.</param>
        /// <returns>Changes in field order.</returns>
        /// <exception cref="ArgumentNullException">The submission is null.</exception>
        public static List<SubmissionChangeView> Changes(DirectorySubmission submission)
        {
            ArgumentNullException.ThrowIfNull(submission);
            List<SubmissionChangeView> result = new List<SubmissionChangeView>();
            List<string> order = FieldsOf(submission.TargetType).SelectMany(Expand).ToList();
            foreach (IGrouping<string, DirectorySubmissionField> group in submission.Fields.GroupBy(f => f.Field).OrderBy(g => Index(order, g.Key)))
            {
                List<DirectorySubmissionField> rows = group.OrderBy(f => f.Position).ToList();
                bool multi = IsMulti(group.Key);
                List<string?> proposed = rows.Select(r => r.ProposedValue).Where(p => p != null).ToList();
                List<string?> current = rows.Select(r => r.CurrentValue).Where(c => c != null).ToList();
                result.Add(new SubmissionChangeView
                {
                    Field = group.Key,
                    Proposed = multi ? proposed : proposed.FirstOrDefault(),
                    Current = submission.Type == DirectorySubmissionTypeEnum.NewVenue || submission.Type == DirectorySubmissionTypeEnum.NewInstructor ? null : multi ? CurrentList(rows) : current.FirstOrDefault(),
                    IsOverride = rows.Any(r => r.IsOverride)
                });
            }
            return result.Take(60).ToList();
        }

        /// <summary>
        /// Fill the current values of rows from the target's values (multi-value fields keep the whole current list on
        /// the first row of the field, encoded one value per line).
        /// </summary>
        /// <param name="rows">Rows.</param>
        /// <param name="current">Current values by row field.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public static void FillCurrent(List<DirectorySubmissionField> rows, Dictionary<string, List<string?>> current)
        {
            ArgumentNullException.ThrowIfNull(rows);
            ArgumentNullException.ThrowIfNull(current);
            foreach (IGrouping<string, DirectorySubmissionField> group in rows.GroupBy(r => r.Field))
            {
                List<string?> values = current.TryGetValue(group.Key, out List<string?>? list) ? list : new List<string?>();
                List<DirectorySubmissionField> ordered = group.OrderBy(r => r.Position).ToList();
                if (IsMulti(group.Key))
                {
                    for (int i = 0; i < ordered.Count; i++) ordered[i].CurrentValue = i == 0 ? (values.Count == 0 ? null : String.Join("\n", values)) : null;
                }
                else
                {
                    foreach (DirectorySubmissionField row in ordered) row.CurrentValue = values.FirstOrDefault();
                }
            }
        }

        /// <summary>
        /// The text fields of a body for the word filter.
        /// </summary>
        /// <param name="rows">Rows.</param>
        /// <returns>Fields.</returns>
        /// <exception cref="ArgumentNullException">The rows are null.</exception>
        public static Dictionary<string, string?> FilterText(List<DirectorySubmissionField> rows)
        {
            ArgumentNullException.ThrowIfNull(rows);
            Dictionary<string, string?> result = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (DirectorySubmissionField row in rows)
            {
                if (row.ProposedValue == null || row.Field == "photoMediaIds" || row.Field == "photoMediaId" || row.Field == "venueIds" || row.Field == "programs") continue;
                string key = row.Field + (IsMulti(row.Field) ? "[" + row.Position + "]" : String.Empty);
                result[key] = row.ProposedValue;
            }
            return result;
        }

        /// <summary>
        /// Take a placed pin (fields.geo: { lat, lng }) out of a venue body: it is stored as the submission's proposed
        /// pin (confirmed by the reviewer on approval), never as a field row.
        /// </summary>
        /// <param name="fields">Converted request.</param>
        /// <param name="present">Present fields ("geo" is removed).</param>
        /// <returns>The pin, or null when the body has none.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">400 InvalidGeo (latitude -90 to 90, longitude -180 to 180, both required).</exception>
        public static GeoRequest? TakePin(VenueRequest fields, HashSet<string> present)
        {
            ArgumentNullException.ThrowIfNull(fields);
            ArgumentNullException.ThrowIfNull(present);
            if (!present.Remove("geo")) return null;
            GeoRequest? pin = fields.Geo;
            fields.Geo = null;
            if (pin == null || (pin.Lat == null && pin.Lng == null)) return null;
            if (pin.Lat == null || pin.Lat < -90 || pin.Lat > 90) throw DirectoryFilters.InvalidGeo("fields.geo.lat", "Latitude must be -90 to 90.");
            if (pin.Lng == null || pin.Lng < -180 || pin.Lng > 180) throw DirectoryFilters.InvalidGeo("fields.geo.lng", "Longitude must be -180 to 180.");
            return pin;
        }

        #endregion

        #region Private-Methods

        private static HashSet<string> Present(Dictionary<string, JsonElement>? fields, IReadOnlyList<string> allowed)
        {
            HashSet<string> present = new HashSet<string>(StringComparer.Ordinal);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            foreach (string key in (fields ?? new Dictionary<string, JsonElement>()).Keys)
            {
                string? match = allowed.FirstOrDefault(a => String.Equals(a, key, StringComparison.OrdinalIgnoreCase));
                if (match == null) errors["fields." + key] = "Unknown field; use " + String.Join(", ", allowed) + ".";
                else present.Add(match);
            }
            if (errors.Count > 0) throw ApiErrorException.Validation(errors, "One or more fields are not editable here.");
            return present;
        }

        private static T Convert<T>(Dictionary<string, JsonElement>? fields) where T : class, new()
        {
            if (fields == null || fields.Count == 0) return new T();
            try
            {
                string json = JsonSerializer.Serialize(fields, ApiJson.Api);
                return JsonSerializer.Deserialize<T>(json, ApiJson.Api) ?? new T();
            }
            catch (JsonException e)
            {
                throw ApiErrorException.Validation(new Dictionary<string, string> { { "fields", "A field has the wrong type (" + (e.Path ?? "value") + ")." } });
            }
        }

        private static IEnumerable<string> Expand(string field)
        {
            if (field == "address") return AddressParts.Select(p => "address." + p);
            if (field == "schedule") return ScheduleParts.Select(p => "schedule." + p);
            return new[] { field };
        }

        private static List<DirectorySubmissionField> ToRows(Dictionary<string, List<string?>> values, IEnumerable<string> fields)
        {
            List<DirectorySubmissionField> rows = new List<DirectorySubmissionField>();
            foreach (string field in fields.Distinct(StringComparer.Ordinal))
            {
                List<string?> list = values.TryGetValue(field, out List<string?>? found) ? found : new List<string?>();
                if (list.Count == 0) rows.Add(new DirectorySubmissionField { Field = field, ProposedValue = null, Position = 0 });
                else for (int i = 0; i < list.Count; i++) rows.Add(new DirectorySubmissionField { Field = field, ProposedValue = list[i], Position = i });
            }
            return rows;
        }

        private static Dictionary<string, List<string?>> Group(List<DirectorySubmissionField> rows)
        {
            return rows.GroupBy(r => r.Field, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.OrderBy(r => r.Position).Select(r => r.ProposedValue).ToList(), StringComparer.Ordinal);
        }

        private static string? First(Dictionary<string, List<string?>> values, string field)
        {
            return values.TryGetValue(field, out List<string?>? list) ? list.FirstOrDefault() : null;
        }

        private static VenueSchedule? ScheduleOf(Dictionary<string, List<string?>> values)
        {
            if (First(values, "schedule.set") != "true") return null;
            List<VenueScheduleEntry> Entries(string kind)
            {
                string prefix = "schedule." + kind + ".";
                if (!values.TryGetValue(prefix + "dayOfWeek", out List<string?>? days)) return new List<VenueScheduleEntry>();
                string? At(string key, int index) => values.TryGetValue(prefix + key, out List<string?>? rows) && index < rows.Count ? rows[index] : null;
                return days.Select((day, i) => day == null ? null : new VenueScheduleEntry { DayOfWeek = Int32.Parse(day, CultureInfo.InvariantCulture), StartTime = At("startTime", i) ?? "", EndTime = At("endTime", i) ?? "", EndsNextDay = At("endsNextDay", i) == "true", Name = At("name", i), Instructor = At("instructor", i) }).Where(e => e != null).Cast<VenueScheduleEntry>().ToList();
            }
            return new VenueSchedule { TimeZone = First(values, "schedule.timeZone"), Notes = First(values, "schedule.notes"), OpeningHours = Entries("openingHours"), Sessions = Entries("sessions") };
        }

        private static List<string> NonNull(List<string?> values)
        {
            return values.Where(v => !String.IsNullOrWhiteSpace(v)).Select(v => v!).ToList();
        }

        private static List<SocialLinkRequest> Links(List<string?> values)
        {
            List<SocialLinkRequest> result = new List<SocialLinkRequest>();
            foreach (string? value in values)
            {
                if (String.IsNullOrWhiteSpace(value)) continue;
                int space = value.IndexOf(' ');
                string kind = space > 0 ? value.Substring(0, space) : "Other";
                string url = space > 0 ? value.Substring(space + 1) : value;
                result.Add(new SocialLinkRequest { Kind = Enum.TryParse(kind, true, out SocialLinkKindEnum k) ? k : SocialLinkKindEnum.Other, Url = url });
            }
            return result;
        }

        private static List<string?> One(string? value)
        {
            return value == null ? new List<string?>() : new List<string?> { value };
        }

        private static List<string> CurrentList(List<DirectorySubmissionField> rows)
        {
            string? first = rows.Select(r => r.CurrentValue).FirstOrDefault(c => c != null);
            return first == null ? new List<string>() : first.Split('\n').ToList();
        }

        private static int Index(List<string> order, string field)
        {
            int i = order.IndexOf(field);
            return i < 0 ? Int32.MaxValue : i;
        }

        #endregion
    }
}

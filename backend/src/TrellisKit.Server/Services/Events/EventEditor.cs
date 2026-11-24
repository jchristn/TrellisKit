namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Settings;

    /// <summary>
    /// Validates an event create or edit (docs/PHASE4_CONTRACT.md sections 2.5, 4.6, 4.7) and produces the record to
    /// store: local wall times in an IANA zone converted to UTC (gaps rejected, ambiguous times resolved to the first
    /// occurrence), formats and locations (a directory venue's address is copied), online details, capacity and RSVP
    /// options, hosts, instructors, reminders, audience, and the word filter. On edits, omitted fields stay and hosts
    /// may change only the description, cover, labels, and tags. Thread safety: stateless apart from the shared services.
    /// </summary>
    public class EventEditor
    {
        #region Public-Members

        /// <summary>
        /// Most directory instructors on an event (fixed bound).
        /// </summary>
        public const int MaxInstructors = 10;

        /// <summary>
        /// Most reminder offsets (fixed bound).
        /// </summary>
        public const int MaxReminders = 3;

        #endregion

        #region Private-Members

        private static readonly HashSet<string> _HostFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "description", "coverMediaId", "labels", "tags", "slug", "scope", "notifyAttendees" };
        private static readonly HashSet<string> _CanceledFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "description", "summary", "labels", "tags", "scope", "notifyAttendees" };
        private static readonly HashSet<string> _Visibilities = new HashSet<string>(StringComparer.Ordinal) { "Exact", "Approximate", "LocalityOnly" };
        private readonly ApplicationServices _Services;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public EventEditor(ApplicationServices services)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The fields present in a create request (every non-null field).
        /// </summary>
        /// <param name="r">Request.</param>
        /// <returns>Field names.</returns>
        public static HashSet<string> PresentOf(EventRequest r)
        {
            ArgumentNullException.ThrowIfNull(r);
            HashSet<string> p = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (System.Reflection.PropertyInfo property in typeof(EventRequest).GetProperties())
            {
                if (property.GetValue(r) != null) p.Add(Char.ToLowerInvariant(property.Name[0]) + property.Name.Substring(1));
            }
            return p;
        }

        /// <summary>
        /// Validate a create (existing null) or an edit and build the record.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="existing">Event being edited, or null for a create.</param>
        /// <param name="r">Request.</param>
        /// <param name="present">Fields present in the request.</param>
        /// <param name="proposal">Whether this is a member proposal (staff-only fields refused).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed, InvalidSchedule, InvalidGeo, UnknownOption, ContentFiltered; 403 Forbidden; 409 EventCanceled.</exception>
        public async Task<EventEditResult> ApplyAsync(ContentActor actor, EventRecord? existing, EventRequest r, HashSet<string> present, bool proposal, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(r);
            ArgumentNullException.ThrowIfNull(present);
            bool create = existing == null;
            bool staff = actor.IsEventStaff;
            if (!create && !staff)
            {
                List<string> refused = present.Where(f => !_HostFields.Contains(f)).ToList();
                if (refused.Count > 0) throw ApiErrorException.Forbidden("Hosts may edit only the description, cover, slug, labels, and tags (" + String.Join(", ", refused) + " refused).");
            }
            if (!create && existing!.Status == EventStatusEnum.Canceled && present.Any(f => !_CanceledFields.Contains(f)))
                throw new ApiErrorException(409, "EventCanceled", "This event was canceled; only labels, tags, the summary, and the description can change.");

            EventSettings s = _Services.Settings.Events;
            DateTime now = DateTime.UtcNow;
            Dictionary<string, string> errors = new Dictionary<string, string>();
            EventRecord e = create ? NewRecord(actor, now) : Copy(existing!);
            EventEditResult result = new EventEditResult { Record = e };
            bool Has(string name) => create || present.Contains(name);

            if (Has("title"))
            {
                string title = MarkdownNormalizer.NormalizeSingleLine(r.Title);
                if (title.Length < 3 || title.Length > 140) errors["title"] = "Use 3 to 140 characters.";
                e.Title = title;
            }
            if (Has("summary"))
            {
                string summary = MarkdownNormalizer.NormalizeSingleLine(r.Summary);
                if (summary.Length > 300) errors["summary"] = "Use at most 300 characters.";
                e.Summary = summary.Length == 0 ? null : summary;
            }
            if (Has("description"))
            {
                string description = MarkdownNormalizer.Normalize(r.Description);
                if (MarkdownNormalizer.Length(description) > s.MaxDescriptionLength) errors["description"] = "Use at most " + s.MaxDescriptionLength.ToString(CultureInfo.InvariantCulture) + " characters.";
                e.Description = description.Length == 0 ? null : description;
            }
            if (Has("category"))
            {
                string? key = String.IsNullOrWhiteSpace(r.Category) ? null : r.Category.Trim();
                if (key != null)
                {
                    await _Services.Events.Categories.EnsureSeededAsync(token).ConfigureAwait(false);
                    List<EventCategory> categories = await _Services.Database.Events.Categories.ListAllAsync(token).ConfigureAwait(false);
                    EventCategory? found = categories.FirstOrDefault(c => String.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase));
                    bool unchanged = !create && String.Equals(existing!.CategoryKey, key, StringComparison.OrdinalIgnoreCase);
                    if (found == null || (found.IsRetired && !unchanged)) throw new ApiErrorException(400, "UnknownOption", "Unknown event category.", new Dictionary<string, string> { { "category", "Not a configured category." } });
                    key = found.Key;
                }
                e.CategoryKey = key;
            }
            EventFormatEnum oldFormat = e.Format;
            if (Has("format"))
            {
                if (r.Format == null) errors["format"] = "Use InPerson, Online, or Hybrid.";
                else e.Format = r.Format.Value;
            }
            if (!create && oldFormat != e.Format) result.Changes.Format = true;

            await ApplyLocationAsync(e, existing, r, present, create, result, errors, token).ConfigureAwait(false);
            ApplyOnline(e, r, present, create, errors);
            ApplySchedule(e, existing, r, present, create, result, errors, s);

            if (Has("capacity"))
            {
                if (r.Capacity != null && (r.Capacity.Value < 1 || r.Capacity.Value > 100000)) errors["capacity"] = "Use 1 to 100000, or null for unlimited.";
                else
                {
                    if (!create && (r.Capacity == null ? existing!.Capacity != null : existing!.Capacity != null && r.Capacity.Value > existing.Capacity.Value)) result.Changes.MoreSpace = true;
                    e.Capacity = r.Capacity;
                }
            }
            if (Has("maxGuestsPerRsvp") || create)
            {
                int guests = r.MaxGuestsPerRsvp ?? (create ? s.DefaultMaxGuestsPerRsvp : e.MaxGuestsPerRsvp);
                if (guests < 0 || guests > 10) errors["maxGuestsPerRsvp"] = "Use 0 to 10.";
                else e.MaxGuestsPerRsvp = guests;
            }
            if (present.Contains("waitlistEnabled") || (create && r.WaitlistEnabled != null))
            {
                bool waitlist = r.WaitlistEnabled ?? true;
                if (!create && waitlist && !existing!.WaitlistEnabled) result.Changes.MoreSpace = true;
                e.WaitlistEnabled = waitlist;
            }
            if (Has("audience") || create)
            {
                ContentAudienceEnum audience = r.Audience ?? (create ? _Services.Access.Current.DefaultAudienceFor(FeatureKeyEnum.Events) : e.Audience);
                if (audience == ContentAudienceEnum.Public && !actor.IsSystemAdmin && (create || existing!.Audience != ContentAudienceEnum.Public))
                    errors["audience"] = "Only a SystemAdmin may make an event Public.";
                else e.Audience = audience;
            }
            if (Has("rsvpAudience") || create)
            {
                ContentAudienceEnum rsvp = r.RsvpAudience ?? (create ? (e.Audience == ContentAudienceEnum.Members ? ContentAudienceEnum.Members : ContentAudienceEnum.SignedIn) : e.RsvpAudience);
                if (rsvp == ContentAudienceEnum.Public) errors["rsvpAudience"] = "Use SignedIn or Members.";
                else e.RsvpAudience = rsvp;
            }
            if (Has("attendeeVisibility") || create) e.AttendeeVisibility = r.AttendeeVisibility ?? (create ? EventAttendeeVisibilityEnum.Attendees : e.AttendeeVisibility);
            if (Has("externalUrl"))
            {
                string? url = String.IsNullOrWhiteSpace(r.ExternalUrl) ? null : r.ExternalUrl.Trim();
                if (url != null && !IsHttps(url)) errors["externalUrl"] = "Use an https link.";
                e.ExternalUrl = url;
            }
            if (Has("commentsEnabled") || create) e.CommentsEnabled = r.CommentsEnabled ?? (create || e.CommentsEnabled);
            if (Has("rsvpOpensUtc")) e.RsvpOpensUtc = Utc(r.RsvpOpensUtc);
            if (Has("rsvpClosesUtc")) e.RsvpClosesUtc = Utc(r.RsvpClosesUtc);
            if (e.RsvpOpensUtc != null && e.RsvpOpensUtc.Value >= e.StartUtc) errors["rsvpOpensUtc"] = "RSVPs must open before the start.";
            if (e.RsvpClosesUtc != null && e.RsvpClosesUtc.Value > e.StartUtc) errors["rsvpClosesUtc"] = "RSVPs close at the start at the latest.";
            if (e.RsvpOpensUtc != null && e.RsvpClosesUtc != null && e.RsvpClosesUtc.Value <= e.RsvpOpensUtc.Value) errors["rsvpClosesUtc"] = "The RSVP window must close after it opens.";
            bool windowError = errors.ContainsKey("rsvpOpensUtc") || errors.ContainsKey("rsvpClosesUtc");

            if (Has("hostUserIds") || create)
            {
                List<string> hosts = (r.HostUserIds ?? (create && proposal ? new List<string> { actor.UserId ?? String.Empty } : create ? new List<string>() : e.HostUserIds))
                    .Where(h => !String.IsNullOrWhiteSpace(h)).Select(h => h.Trim()).Distinct(StringComparer.Ordinal).ToList();
                if (proposal && r.HostUserIds != null) errors["hostUserIds"] = "The proposer becomes the host on approval.";
                if (hosts.Count > s.MaxHosts) errors["hostUserIds"] = "At most " + s.MaxHosts.ToString(CultureInfo.InvariantCulture) + " hosts.";
                else if (hosts.Count > 0 && !proposal)
                {
                    List<User> users = await _Services.Database.Users.ReadManyAsync(hosts, token).ConfigureAwait(false);
                    HashSet<string> alive = new HashSet<string>(users.Where(u => !u.IsDeleted && !u.IsBanned).Select(u => u.Id), StringComparer.Ordinal);
                    List<string> unknown = hosts.Where(h => !alive.Contains(h)).ToList();
                    if (unknown.Count > 0) errors["hostUserIds"] = "Unknown member: " + String.Join(", ", unknown) + ".";
                }
                if (proposal) hosts = new List<string>();
                if (!e.HostUserIds.SequenceEqual(hosts, StringComparer.Ordinal)) result.ChildrenChanged = true;
                e.HostUserIds = hosts;
            }
            if (Has("instructorIds"))
            {
                List<string> instructors = (r.InstructorIds ?? new List<string>()).Where(h => !String.IsNullOrWhiteSpace(h)).Select(h => h.Trim()).Distinct(StringComparer.Ordinal).ToList();
                if (instructors.Count > MaxInstructors) errors["instructorIds"] = "At most " + MaxInstructors + " instructors.";
                else if (instructors.Count > 0)
                {
                    Dictionary<string, EventInstructorSnapshot> found = await _Services.Database.Events.Records.ReadInstructorsAsync(instructors, token).ConfigureAwait(false);
                    List<string> unknown = instructors.Where(i => !found.TryGetValue(i, out EventInstructorSnapshot? x) || !x.IsListed).ToList();
                    if (unknown.Count > 0) errors["instructorIds"] = "Unknown instructor: " + String.Join(", ", unknown) + ".";
                }
                if (!e.InstructorIds.SequenceEqual(instructors, StringComparer.Ordinal)) result.ChildrenChanged = true;
                e.InstructorIds = instructors;
            }
            if (Has("reminderOffsetsMinutes") || create)
            {
                List<int> offsets = r.ReminderOffsetsMinutes ?? (create ? ParseOffsets(s.DefaultReminderOffsetsMinutes) : e.ReminderOffsetsMinutes);
                offsets = offsets.Distinct().OrderByDescending(o => o).ToList();
                if (offsets.Count > MaxReminders) throw EventTimeZones.Invalid("reminderOffsetsMinutes", "At most 3 reminder offsets.");
                if (offsets.Any(o => o < 5 || o > 20160)) errors["reminderOffsetsMinutes"] = "Each offset is 5 to 20160 minutes.";
                if (!e.ReminderOffsetsMinutes.SequenceEqual(offsets)) result.ChildrenChanged = true;
                e.ReminderOffsetsMinutes = offsets;
            }
            if (Has("coverMediaId"))
            {
                string? cover = String.IsNullOrWhiteSpace(r.CoverMediaId) ? null : r.CoverMediaId.Trim();
                if (cover != null && !String.Equals(cover, existing?.CoverMediaId, StringComparison.Ordinal))
                {
                    MediaAttachment? media = await _Services.Database.Media.ReadAsync(cover, token).ConfigureAwait(false);
                    if (media == null || media.IsDeleted || media.Kind != MediaKindEnum.Image) errors["coverMediaId"] = "Use an uploaded image.";
                    else if (!actor.IsSystemAdmin && !String.Equals(media.OwnerUserId, actor.UserId, StringComparison.Ordinal)) errors["coverMediaId"] = "Not your media.";
                    else if (media.Status == MediaStatusEnum.Uploading || media.Status == MediaStatusEnum.Failed) errors["coverMediaId"] = "The upload is not ready.";
                }
                if (!String.Equals(cover, e.CoverMediaId, StringComparison.Ordinal)) result.CoverChanged = true;
                e.CoverMediaId = cover;
            }
            if (present.Contains("recurrence") && r.Recurrence != null)
            {
                if (proposal) errors["recurrence"] = "Proposals are single events.";
                else
                {
                    TimeZoneInfo? zone = EventTimeZones.Find(e.TimeZone);
                    DateTime local = zone == null ? DateTime.MinValue : EventTimeZones.ToLocal(e.StartUtc, zone);
                    result.Rule = RecurrenceService.Parse(r.Recurrence, local, s, errors);
                }
            }
            if (proposal && !String.IsNullOrEmpty(r.ProposalNote))
            {
                string note = MarkdownNormalizer.NormalizeSingleLine(r.ProposalNote);
                if (note.Length > 1000) errors["proposalNote"] = "Use at most 1000 characters.";
                e.ProposalNote = note;
            }
            if (windowError && errors.Count == errors.Count(x => x.Key.StartsWith("rsvp", StringComparison.Ordinal)))
                throw new ApiErrorException(400, "InvalidSchedule", "The RSVP window must be inside the event.", errors);
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);

            Dictionary<string, string?> text = new Dictionary<string, string?>(StringComparer.Ordinal);
            if (Has("title")) text["title"] = e.Title;
            if (Has("summary")) text["summary"] = e.Summary;
            if (Has("description")) text["description"] = e.Description;
            if (Has("location")) text["location.name"] = e.LocationName;
            if (text.Count > 0) await _Services.WordFilter.CheckAsync(actor, TargetTypeEnum.Event, text, token).ConfigureAwait(false);
            if (Has("labels") || Has("tags")) await _Services.Metadata.ValidateAsync(actor, r.Labels, r.Tags, actor.IsStaffFor(TargetTypeEnum.Event), token, TargetTypeEnum.Event).ConfigureAwait(false);

            // Public page slug rules (docs/PHASE4_CONTRACT.md section 18.1). Without a slug the default is the title plus the
            // locality and region; a changed slug keeps the previous one as a redirect.
            string? requested = present.Contains("slug") || create ? r.Slug?.Trim().ToLowerInvariant() : null;
            if (!String.IsNullOrEmpty(requested) && (create || !String.Equals(requested, existing!.Slug, StringComparison.OrdinalIgnoreCase)))
            {
                if (!PublicSlugRules.IsValid(requested)) throw ApiErrorException.Validation(new Dictionary<string, string> { { "slug", PublicSlugRules.Message } });
                if (await _Services.Database.PublicPages.SlugTakenAsync(PublicPageKindEnum.Event, requested, existing?.Id, token).ConfigureAwait(false))
                    throw new ApiErrorException(409, "SlugInUse", "That slug is already used by another event.", new Dictionary<string, string> { { "slug", "In use." } });
                e.Slug = requested;
            }
            else if (create)
            {
                e.Slug = await FreeSlugAsync(PublicSlugRules.Derive("event", e.Title, e.Locality, e.Region), null, token).ConfigureAwait(false);
            }
            if (!create)
            {
                result.Changes.Any = result.Changes.Material || result.ChildrenChanged || result.CoverChanged || !SameFields(existing!, e) || present.Contains("labels") || present.Contains("tags");
                if (result.Changes.Material) e.IcsSequence = existing!.IcsSequence + 1;
            }
            TimeZoneInfo z = EventTimeZones.Find(e.TimeZone) ?? TimeZoneInfo.Utc;
            result.StartLocal = EventTimeZones.ToLocal(e.StartUtc, z);
            return result;
        }

        /// <summary>
        /// A slug from text: lower case letters and digits joined by hyphens, at most 80 characters.
        /// </summary>
        /// <param name="text">Text.</param>
        /// <returns>Slug (event when nothing usable remains).</returns>
        public static string Slugify(string? text)
        {
            StringBuilder b = new StringBuilder();
            bool dash = false;
            foreach (char c in (text ?? String.Empty).Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD))
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
                {
                    b.Append(c);
                    dash = false;
                }
                else if (!dash && b.Length > 0)
                {
                    b.Append('-');
                    dash = true;
                }
                if (b.Length >= 80) break;
            }
            string slug = b.ToString().Trim('-');
            return slug.Length == 0 ? "event" : slug;
        }

        /// <summary>
        /// A slug not used by another undeleted event or as another event's redirect, from a title (suffixes -2, -3, ... or a
        /// random suffix).
        /// </summary>
        /// <param name="text">Title.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Slug.</returns>
        public Task<string> UniqueSlugAsync(string text, CancellationToken token = default)
        {
            return FreeSlugAsync(PublicSlugRules.Derive("event", text), null, token);
        }

        /// <summary>
        /// The first free slug of slug, slug-2, ... slug-10 (or slug plus a random number) for an event.
        /// </summary>
        /// <param name="slug">Valid slug.</param>
        /// <param name="exceptId">The event being edited, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Slug.</returns>
        public async Task<string> FreeSlugAsync(string slug, string? exceptId, CancellationToken token = default)
        {
            for (int i = 1; i <= 10; i++)
            {
                string candidate = i == 1 ? slug : PublicSlugRules.WithSuffix(slug, i);
                if (!await _Services.Database.PublicPages.SlugTakenAsync(PublicPageKindEnum.Event, candidate, exceptId, token).ConfigureAwait(false)) return candidate;
            }
            return PublicSlugRules.WithSuffix(slug.Length > 70 ? slug.Substring(0, 70).TrimEnd('-') : slug, 1000 + Random.Shared.Next(9000));
        }

        /// <summary>
        /// Parse comma-separated reminder offsets (invalid entries ignored).
        /// </summary>
        /// <param name="text">Text, for example 1440,120.</param>
        /// <returns>Offsets.</returns>
        public static List<int> ParseOffsets(string? text)
        {
            List<int> offsets = new List<int>();
            foreach (string part in (text ?? String.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (Int32.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) && value >= 5 && value <= 20160 && !offsets.Contains(value)) offsets.Add(value);
            }
            return offsets.OrderByDescending(o => o).Take(MaxReminders).ToList();
        }

        /// <summary>
        /// A field-by-field copy of an event row (children copied too).
        /// </summary>
        /// <param name="e">Event.</param>
        /// <returns>Copy.</returns>
        public static EventRecord Copy(EventRecord e)
        {
            ArgumentNullException.ThrowIfNull(e);
            return e.Clone();
        }

        #endregion

        #region Private-Methods

        private static EventRecord NewRecord(ContentActor actor, DateTime now)
        {
            return new EventRecord
            {
                Id = IdGenerator.Generate(EventIdPrefixes.Event),
                Status = EventStatusEnum.Draft,
                CreatedByUserId = actor.UserId,
                CreatedUtc = now,
                UpdatedUtc = now,
                WaitlistEnabled = true,
                CommentsEnabled = true
            };
        }

        private async Task ApplyLocationAsync(EventRecord e, EventRecord? existing, EventRequest r, HashSet<string> present, bool create, EventEditResult result, Dictionary<string, string> errors, CancellationToken token)
        {
            bool needs = e.Format != EventFormatEnum.Online;
            if (!create && !present.Contains("location"))
            {
                if (needs && e.VenueId == null && e.LocationName == null && e.Line1 == null && e.Locality == null) errors["location"] = "InPerson and Hybrid events need a venue or a place.";
                if (!needs) ClearLocation(e, existing, result);
                return;
            }
            if (!needs)
            {
                ClearLocation(e, existing, result);
                return;
            }
            EventLocationRequest? l = r.Location;
            if (l == null)
            {
                errors["location"] = "InPerson and Hybrid events need a venue or a place.";
                return;
            }
            string? before = existing == null ? null : LocationKey(existing);
            string visibility = String.IsNullOrWhiteSpace(l.AddressVisibility) ? "Exact" : l.AddressVisibility.Trim();
            if (!_Visibilities.Contains(visibility)) errors["location.addressVisibility"] = "Use Exact, Approximate, or LocalityOnly.";
            if (!String.IsNullOrWhiteSpace(l.VenueId))
            {
                EventVenueSnapshot? venue = await _Services.Database.Events.Records.ReadVenueAsync(l.VenueId.Trim(), token).ConfigureAwait(false);
                if (venue == null || !venue.IsListed)
                {
                    errors["location.venueId"] = "Unknown venue.";
                    return;
                }
                e.VenueId = venue.Id;
                e.LocationName = String.IsNullOrWhiteSpace(l.Name) ? venue.Name : MarkdownNormalizer.NormalizeSingleLine(l.Name);
                e.Line1 = venue.Line1;
                e.Line2 = venue.Line2;
                e.Locality = venue.Locality;
                e.Region = venue.Region;
                e.PostalCode = venue.PostalCode;
                e.CountryCode = venue.CountryCode;
                e.AddressVisibility = String.IsNullOrWhiteSpace(l.AddressVisibility) ? venue.AddressVisibility : visibility;
                e.Lat = venue.Lat;
                e.Lng = venue.Lng;
                if (String.IsNullOrWhiteSpace(r.TimeZone) && !String.IsNullOrWhiteSpace(venue.TimeZone) && (create || present.Contains("startLocal"))) e.TimeZone = venue.TimeZone!;
            }
            else
            {
                e.VenueId = null;
                string name = MarkdownNormalizer.NormalizeSingleLine(l.Name);
                if (name.Length > 120) errors["location.name"] = "Use at most 120 characters.";
                e.LocationName = name.Length == 0 ? null : name;
                EventAddressRequest a = l.Address ?? new EventAddressRequest();
                e.Line1 = Field(a.Line1, 200, "location.address.line1", errors);
                e.Line2 = Field(a.Line2, 200, "location.address.line2", errors);
                e.Locality = Field(a.Locality, 100, "location.address.locality", errors);
                e.Region = Field(a.Region, 100, "location.address.region", errors);
                e.PostalCode = Field(a.PostalCode, 20, "location.address.postalCode", errors);
                string? country = String.IsNullOrWhiteSpace(a.CountryCode) ? null : a.CountryCode.Trim().ToUpperInvariant();
                bool any = e.Line1 != null || e.Line2 != null || e.Locality != null || e.Region != null || e.PostalCode != null;
                if (country != null && (country.Length != 2 || !country.All(Char.IsLetter))) errors["location.address.countryCode"] = "Use an ISO 3166-1 alpha-2 code.";
                else if (any && country == null) errors["location.address.countryCode"] = "Required when an address is given.";
                e.CountryCode = country;
                if (e.LocationName == null && !any) errors["location"] = "Give a venue, or a place name or address.";
                e.AddressVisibility = visibility;
                e.Lat = null;
                e.Lng = null;
            }
            if (l.Geo != null && (l.Geo.Lat != null || l.Geo.Lng != null))
            {
                if (l.Geo.Lat == null || l.Geo.Lng == null || l.Geo.Lat.Value < -90 || l.Geo.Lat.Value > 90 || l.Geo.Lng.Value < -180 || l.Geo.Lng.Value > 180)
                    throw new ApiErrorException(400, "InvalidGeo", "Latitude is -90 to 90 and longitude -180 to 180.", new Dictionary<string, string> { { "location.geo", "Out of range." } });
                e.Lat = l.Geo.Lat;
                e.Lng = l.Geo.Lng;
            }
            if (before != null && !String.Equals(before, LocationKey(e), StringComparison.Ordinal)) result.Changes.Location = true;
        }

        private static void ClearLocation(EventRecord e, EventRecord? existing, EventEditResult result)
        {
            bool had = e.VenueId != null || e.LocationName != null || e.Line1 != null || e.Locality != null;
            e.VenueId = null;
            e.LocationName = null;
            e.Line1 = null;
            e.Line2 = null;
            e.Locality = null;
            e.Region = null;
            e.PostalCode = null;
            e.CountryCode = null;
            e.Lat = null;
            e.Lng = null;
            if (existing != null && had) result.Changes.Location = true;
        }

        private static void ApplyOnline(EventRecord e, EventRequest r, HashSet<string> present, bool create, Dictionary<string, string> errors)
        {
            bool needs = e.Format != EventFormatEnum.InPerson;
            if (!needs)
            {
                e.OnlinePlatformLabel = null;
                e.OnlineJoinUrl = null;
                return;
            }
            if (create || present.Contains("online"))
            {
                EventOnlineRequest? o = r.Online;
                string? url = o == null || String.IsNullOrWhiteSpace(o.JoinUrl) ? null : o.JoinUrl.Trim();
                string label = MarkdownNormalizer.NormalizeSingleLine(o?.PlatformLabel);
                if (url == null) errors["online.joinUrl"] = "Online and Hybrid events need a join link.";
                else if (!IsHttps(url) || url.Length > 2000) errors["online.joinUrl"] = "Use an https link.";
                if (label.Length > 60) errors["online.platformLabel"] = "Use at most 60 characters.";
                e.OnlineJoinUrl = url;
                e.OnlinePlatformLabel = label.Length == 0 ? null : label;
            }
            else if (e.OnlineJoinUrl == null) errors["online.joinUrl"] = "Online and Hybrid events need a join link.";
        }

        private static void ApplySchedule(EventRecord e, EventRecord? existing, EventRequest r, HashSet<string> present, bool create, EventEditResult result, Dictionary<string, string> errors, EventSettings s)
        {
            bool touches = create || present.Contains("startLocal") || present.Contains("endLocal") || present.Contains("timeZone") || present.Contains("isAllDay");
            if (!touches) return;
            string zoneId = !String.IsNullOrWhiteSpace(r.TimeZone) ? r.TimeZone.Trim() : e.TimeZone;
            if (create && String.IsNullOrWhiteSpace(r.TimeZone) && e.VenueId == null) throw EventTimeZones.Invalid("timeZone", "Give the event's IANA time zone, for example America/New_York.");
            TimeZoneInfo zone = EventTimeZones.Require(zoneId);
            bool allDay = r.IsAllDay ?? (!create && e.IsAllDay);
            DateTime startLocal;
            DateTime endLocal;
            if (present.Contains("startLocal") || create)
            {
                if (!EventTimeZones.TryParseLocal(r.StartLocal, out startLocal)) throw EventTimeZones.Invalid("startLocal", "Use yyyy-MM-ddTHH:mm in the event's time zone.");
            }
            else startLocal = EventTimeZones.ToLocal(e.StartUtc, EventTimeZones.Find(e.TimeZone) ?? zone);
            if (present.Contains("endLocal") || create)
            {
                if (!EventTimeZones.TryParseLocal(r.EndLocal, out endLocal)) throw EventTimeZones.Invalid("endLocal", "Use yyyy-MM-ddTHH:mm in the event's time zone.");
            }
            else
            {
                TimeSpan duration = e.EndUtc - e.StartUtc;
                endLocal = allDay && e.IsAllDay ? EventTimeZones.ToLocal(e.EndUtc, EventTimeZones.Find(e.TimeZone) ?? zone).AddDays(-1) : startLocal + (allDay ? TimeSpan.Zero : duration);
            }
            DateTime startUtc;
            DateTime endUtc;
            if (allDay)
            {
                startUtc = EventTimeZones.ToUtcLenient(startLocal.Date, zone);
                endUtc = EventTimeZones.ToUtcLenient(endLocal.Date.AddDays(1), zone);
            }
            else
            {
                startUtc = EventTimeZones.ToUtcStrict(startLocal, zone, "startLocal");
                endUtc = EventTimeZones.ToUtcStrict(endLocal, zone, "endLocal");
            }
            if (endUtc <= startUtc) throw EventTimeZones.Invalid("endLocal", "The end must be after the start.");
            if ((endUtc - startUtc).TotalDays > s.MaxDurationDays) throw EventTimeZones.Invalid("endLocal", "An event lasts at most " + s.MaxDurationDays.ToString(CultureInfo.InvariantCulture) + " days.");
            if (existing != null && (existing.StartUtc != startUtc || existing.EndUtc != endUtc || existing.TimeZone != zone.Id || existing.IsAllDay != allDay)) result.Changes.Time = true;
            e.TimeZone = zone.Id;
            e.IsAllDay = allDay;
            e.StartUtc = startUtc;
            e.EndUtc = endUtc;
        }

        private static string? Field(string? value, int max, string name, Dictionary<string, string> errors)
        {
            string text = MarkdownNormalizer.NormalizeSingleLine(value);
            if (text.Length > max) errors[name] = "Use at most " + max.ToString(CultureInfo.InvariantCulture) + " characters.";
            return text.Length == 0 ? null : text;
        }

        private static string LocationKey(EventRecord e)
        {
            return String.Join("|", e.VenueId, e.LocationName, e.Line1, e.Line2, e.Locality, e.Region, e.PostalCode, e.CountryCode,
                e.Lat?.ToString("R", CultureInfo.InvariantCulture), e.Lng?.ToString("R", CultureInfo.InvariantCulture));
        }

        private static bool SameFields(EventRecord a, EventRecord b)
        {
            return a.Title == b.Title && a.Summary == b.Summary && a.Description == b.Description && a.CategoryKey == b.CategoryKey && a.Capacity == b.Capacity &&
                a.MaxGuestsPerRsvp == b.MaxGuestsPerRsvp && a.WaitlistEnabled == b.WaitlistEnabled && a.RsvpAudience == b.RsvpAudience && a.RsvpOpensUtc == b.RsvpOpensUtc &&
                a.RsvpClosesUtc == b.RsvpClosesUtc && a.AttendeeVisibility == b.AttendeeVisibility && a.ExternalUrl == b.ExternalUrl && a.CommentsEnabled == b.CommentsEnabled &&
                a.Audience == b.Audience && a.OnlineJoinUrl == b.OnlineJoinUrl && a.OnlinePlatformLabel == b.OnlinePlatformLabel && a.AddressVisibility == b.AddressVisibility && a.Slug == b.Slug;
        }

        private static bool IsHttps(string url)
        {
            return Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) && uri.Scheme == Uri.UriSchemeHttps && !String.IsNullOrEmpty(uri.Host);
        }

        private static DateTime? Utc(DateTime? value)
        {
            if (value == null) return null;
            DateTime v = value.Value;
            return v.Kind == DateTimeKind.Utc ? v : v.Kind == DateTimeKind.Local ? v.ToUniversalTime() : DateTime.SpecifyKind(v, DateTimeKind.Utc);
        }

        #endregion
    }
}

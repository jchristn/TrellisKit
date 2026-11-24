# Phase 4 Contract: Directory, Events, and Library

This is the binding contract between the backend, the website, and the mobile app (phone and tablet) for Phase 4. Every team codes against it exactly. It is an engine contract: TrellisKit implements it for every property (`docs/TRELLISKIT.md`). Property-specific values (what a venue or an instructor is called, program names, event categories, lesson levels, map and geocoding providers, domains) come from the property configuration and the admin console; examples use the neutral sample property (Example Community, `example.test`). When the implementation is complete, `REST_API.md` and the Postman collection are updated to match, and this file stays as the design record. Plan references: the property plan of record, sections 4.3, 4.5, 4.8, 4.9, 4.10, 4.11, 6.3, 7.1, 11.

Status: implemented. The open questions were answered in "Resolved decisions (property owner, 2026-11-12)" at the end, which override the recommendations; the routes as built (including recurrence, member event proposals, staff viewing history, and the document text pipeline, which the resolved decisions added) are documented in `REST_API.md`.

Phase 4 builds on, and does not repeat:
- `docs/PHASE1_CONTRACT.md` (identity, roles including `DirectoryModerator` and `EventManager`, flags, audit).
- `docs/PHASE2_CONTRACT.md` (body format, mentions, media pipeline, reactions, threaded comments, notifications, push, reports, moderation actions, word filter, blocks, rate limit conventions, data write rules).
- `docs/PHASE3_CONTRACT.md` (the "one conversation" `commentTarget` rule, promotion to the feed, follows, global search through `ISearchIndex` with Verbex, `LeasedPassWorker` jobs).
- `docs/ENUMERATION_AND_METADATA.md` (binding: `EnumerationQuery` in and `EnumerationResult<T>` out for every list, labels and tags on every object, no unbounded lists, label and tag filters in every client search surface). Where any example in this file looks like a plain list, the enumeration specification wins.
- `docs/ACCESS_MODELS_CONTRACT.md` (binding route declarations, caller tiers, audience, locked stubs, denial bodies), `docs/MULTIPROPERTY_CONTRACT.md` (property-bound data access, `propertyid` on every row, the isolation suite), `docs/LIVE_UPDATES_CONTRACT.md` section 13 (kill switches), `docs/AI_CONTRACT.md` sections 7 and 8 (knowledge sources and tools), and `docs/TRELLISKIT.md` sections 7, 9 to 13.

Conventions are unchanged: routes under `/api/v1`, camelCase JSON, string enums, ISO 8601 UTC timestamps, errors as `{ "error", "message", "fieldErrors" }`, `X-Client` and `X-Client-Version` on every app request, `X-TK-Property` and `X-TK-App` from mobile and tablet builds, access tokens as `Authorization: Bearer <accessToken>`, `429` always with `Retry-After`. PostgreSQL only.

---

## 0. New conventions in Phase 4

### 0.1 Target types and identifier prefixes

Phase 2 reserved `Lesson` and `Event`. Phase 4 activates them and adds the rest:

| `targetType` | Id prefix | URL segment | Notes |
|---|---|---|---|
| `Venue` | `ven_` | `venues` | A place in the directory (the property names it, for example "Club" in the sample). Reports, labels, tags; no follows in Phase 4 |
| `VenueReview` | `vrv_` | | A member review of a venue with the optional instructor reply (section 3.12, as built). Reports, labels, tags |
| `VenueMedia` | `vmd_` | | A photo or video a member added to a venue (section 3.13, as built). Reports, labels, tags |
| `Instructor` | `ins_` | `instructors` | A person listed in the directory, optionally linked to a member account (claimed) |
| `VenueInstructor` | `vin_` | | Link between a venue and an instructor (many to many) |
| `DirectoryProgram` | `dpg_` | | Admin-defined program option a venue offers (seeded from `property.json`) |
| `DirectorySubmission` | `dsb_` | `directory/submissions` | Member proposal: new, edit, claim, closure |
| `GeocodeResult` | `gcr_` | | Cached geocoder answer (staff only) |
| `Event` | `evt_` | `events` | Comments, reactions, reports, reminders |
| `EventCategory` | `ecg_` | | Admin-defined category (seeded from `property.json`) |
| `EventRsvp` | `rsv_` | | One per member per event |
| `EventReminder` | `erm_` | | Scheduled reminder offset per event (internal; staff visible) |
| `CalendarFeed` | `cfd_` | | A member's private calendar subscription token |
| `Series` | `ser_` | `series` | A course of ordered lessons |
| `Lesson` | `les_` | `lessons` | Comments, reactions, reports, progress, promotion |
| `LessonResource` | `lrs_` | | Document, link, or reference attached to a lesson |
| `LessonMarker` | `lmk_` | | A chapter marker inside a lesson video |
| `LessonProgress` | `lpr_` | | One per member per lesson |
| `SeriesFollow` | `sfw_` | | A member following a series |
| `LibraryLevel` | `llv_` | | Admin-defined level when `library.levelSource` is `Custom` |
| `LessonDownload` | `ldl_` | | A mobile offline download license |

Comments on events and lessons are Phase 2 `Comment` rows with `targetType: "Event"` or `"Lesson"`, through `CommentHost` (section 15). Clients keep tolerating unknown target types (Phase 2 section 0.3).

### 0.2 Terminology from configuration

The engine never hard-codes what a property calls its directory entries, programs, categories, or levels. `property.json` gains three optional sections (schema version 2, absent means the engine defaults shown), seeded into the database on first load and editable in the admin console afterwards (the profile field rule: in `SingleProperty` the folder wins when the section changes on disk):

```json
"directory": {
  "venueLabel": "Club", "venueLabelPlural": "Clubs",
  "instructorLabel": "Coach", "instructorLabelPlural": "Coaches",
  "programs": [ { "key": "beginners", "label": "Beginners" }, { "key": "advanced", "label": "Advanced" }, { "key": "youth", "label": "Youth" } ],
  "instructorRankFieldKey": "rank",
  "submissions": true,
  "claims": true
},
"events": {
  "categories": [ { "key": "meetup", "label": "Meetup", "color": "#1F5FBF" }, { "key": "workshop", "label": "Workshop", "color": "#2E7D32" }, { "key": "online", "label": "Online session", "color": "#6A1B9A" } ]
},
"library": {
  "levelSource": "ProfileField",
  "levelFieldKey": "rank",
  "levels": []
}
```

| Key | Rule | Engine default |
|---|---|---|
| `directory.venueLabel`, `...Plural`, `instructorLabel`, `...Plural` | 1 to 40 characters; used by clients in every directory string through `{venue}`, `{venues}`, `{instructor}`, `{instructors}` placeholders | "Venue", "Venues", "Instructor", "Instructors" |
| `directory.programs` | 0 to `directory.maxPrograms` (50) options `{ key, label }`; keys stable, labels free | `[]` |
| `directory.instructorRankFieldKey` | Key of a profile field of type `Rank` (`docs/TRELLISKIT.md` section 11); instructors then carry `rank` and `rankLevel` rendered with that field's options and colors. Null hides rank on instructors | null |
| `directory.submissions`, `directory.claims` | Member submission queue and instructor claims on or off | true, true |
| `events.categories` | 0 to `events.maxCategories` (50) options `{ key, label, color? }` | `[]` |
| `library.levelSource` | `None`, `ProfileField` (the options of a `Rank` or `SingleSelect` profile field become the levels, in their order), `Custom` (the `library.levels` list) | `None` |
| `library.levelFieldKey`, `library.levels` | Used by `ProfileField` and `Custom` respectively; `levels` items `{ key, label, color? }`, at most 50 | null, `[]` |

`GET /api/v1/property` returns these sections (labels, options, and the effective map configuration of section 3.6) so clients render from them. Keys are what the database and the API store.

### 0.3 Error codes added in Phase 4

| Status | `error` | When |
|---|---|---|
| 400 | `InvalidGeo` | Latitude outside -90 to 90, longitude outside -180 to 180, radius above `directory.maxRadiusKm`, a malformed bounding box, or `near` combined with `bbox` |
| 400 | `GeocodeFailed` | The geocoder found no usable result for an address and no manual position was given (`fieldErrors.address`) |
| 400 | `InvalidSchedule` | Event `endUtc` not after `startUtc`, longer than `events.maxDurationDays`, unknown IANA `timeZone`, RSVP window outside the event, or more than 3 reminder offsets |
| 400 | `UnknownOption` | A program, category, or level key that is not configured (`fieldErrors` names the field) |
| 400 | `LessonVideoRequired` | Publishing a lesson whose `kind` is `Video` without a Ready video |
| 403 | `SubmissionsDisabled` | `directory.submissions` (or `directory.claims` for a claim) is off |
| 403 | `RsvpNotAllowed` | The caller cannot RSVP (event audience, gating, or the event's `rsvpAudience`) |
| 403 | `DownloadsNotAllowed` | Downloads are off for the property, the lesson, the platform, or the caller |
| 404 | `NotFound` | Unknown, deleted, draft, or not visible to the caller (never 403, so ids do not leak) |
| 409 | `EventFull` | Going requested on a full event with the waitlist off |
| 409 | `RsvpClosed` | RSVP outside the RSVP window, on a canceled event, or after the event started |
| 409 | `EventCanceled` | Edit or RSVP on a canceled event (except staff edits of `labels`, `tags`, and notes) |
| 409 | `SubmissionLimitReached` | The member has `directory.maxPendingSubmissionsPerMember` pending submissions |
| 409 | `SubmissionStale` | Approving an edit whose target changed after the submission was made (the reviewer must refresh the diff) |
| 409 | `AlreadyClaimed` | Claiming an instructor that is linked to an account, or a second pending claim by the same member |
| 409 | `SlugInUse` | Series or lesson slug not unique in the property (Phase 3 code) |
| 409 | `DownloadLimitReached` | The member holds `library.downloads.maxActivePerMember` active download licenses |
| 409 | `SeriesNotEmpty` | Delete a series that still has lessons (archive it instead) |
| 503 | `FeatureDisabled` | The `directory`, `events`, `library`, or `libraryDownloads` kill switch is on for this caller |
| 503 | `IntegrationNotConfigured` | A geocoder or map tile proxy is selected but not configured |

Phase 2 and 3 codes (`Muted`, `PostingNotAllowed`, `MediaNotAllowed`, `ContentFiltered`, `InteractionBlocked`, `CommentsDisabled`, `MediaNotAttachable`, `RateLimited`, `PromotionNotAllowed`, `AlreadyPromoted`, `UseCommentTarget`) apply unchanged.

### 0.4 Access model, feature keys, and kill switches

Phase 4 owns three existing feature keys. Each is a `property.json` `features.*` switch (absent means true), a gatable key in `access.gatedFeatures`, and a kill switch already seeded by `KillSwitchCatalog` (`docs/LIVE_UPDATES_CONTRACT.md` section 13.1). Phase 4 adds one kill switch key, `libraryDownloads`, to the catalog (seeded with no rules, not killed).

| Feature key | Routes | Feature off (`features.<key>: false`) | Kill switch |
|---|---|---|---|
| `directory` | `/directory/*`, `/venues*`, `/instructors*`, `/public/venues*`, and (already) `/members/directory*` | Every route `404 NotAvailable`; clients hide the Directory and Map entry points and admin pages | `directory`: `503 FeatureDisabled` on every member directory route; admin routes stay open |
| `events` | `/events*`, `/calendar/*`, `/me/calendar-feed*`, `/public/events*` | `404 NotAvailable`; Events entries hidden | `events` |
| `library` | `/series*`, `/lessons*`, `/me/library/*`, `/public/lessons*` | `404 NotAvailable`; Library tab hidden | `library`; `libraryDownloads` blocks only the download routes and stops background downloads on mobile |

The member directory of profiles (`/members/directory*`, shipped with the multi-property milestone) and the venue and instructor directory share the `directory` key: one switch, one gate, one kill switch. Open question 1 asks whether to split them.

**Audience.** Every `Venue`, `Instructor`, `Event`, `Series`, and `Lesson` has `audience` (`Public`, `SignedIn`, `Members`). Defaults follow `ACCESS_MODELS_CONTRACT.md` section 3.3: `Members` when the feature is gated in `PartiallyGated`, otherwise `access.defaultAudience`; `Public` may be set only by `SystemAdmin`. Rules per type:

| Type | Audience rule |
|---|---|
| Venue, Instructor | Independent per object. An instructor shown on a venue the caller can read but whose own audience the caller does not meet is omitted from that venue's instructor list |
| Event | Independent. `rsvpAudience` (`SignedIn` or `Members`, default the event's audience narrowed to at least `SignedIn`) may be narrower than the event's audience, so a `Public` event can be visible to visitors while only members RSVP |
| Series | Independent; it is the default audience of new lessons in the series |
| Lesson | May be broader than its series (a free sample lesson inside a members-only series), never narrower than `SignedIn` for comments and progress. A caller who cannot read the series still sees it as a locked stub that lists only the lessons they may open |

**Gating behavior per feature** (refines `ACCESS_MODELS_CONTRACT.md` section 3.2 for Phase 4; the route table in each section declares feature, action, and signed-out eligibility for `AccessRoutes.cs`):

| Feature, caller | `FullyGated` | `PartiallyGated`, feature gated | `PartiallyGated`, not gated | `Open` |
|---|---|---|---|---|
| `directory` reads and lists | Member | Member (route gate, as forums: reads need a membership; signed-out `401` even with anonymous browsing) | SignedIn; Anonymous for `Public` objects when `anonymousBrowsing` | SignedIn; Anonymous for `Public` objects when `anonymousBrowsing` |
| `directory` submissions and claims | Member | Member | SignedIn | SignedIn |
| `events` lists and detail | Member | SignedIn, with `Members` events as locked stubs; Anonymous for `Public` events when `anonymousBrowsing` | SignedIn; Anonymous for `Public` | same as not gated |
| `events` RSVP, comments, reactions | Member | Member | The higher of `rsvpAudience` and SignedIn | SignedIn |
| `library` catalog lists and detail | Member | SignedIn, with `Members` series and lessons as locked stubs; Anonymous for `Public` | SignedIn; Anonymous for `Public` | same as not gated |
| `library` playback grant, progress, downloads, comments | Member | Member, except lessons whose audience is `Public` or `SignedIn` (samples), which need only SignedIn (playback of a `Public` lesson needs no account) | SignedIn | SignedIn |

Events and the library are deliberately less strict than forums when gated (catalogs visible to free accounts as locked stubs, so they see what membership unlocks); the directory is as strict as forums because a venue list has no "sample" value without its details. Open question 2 covers this choice.

`FullyGated` properties expose `Public` objects only through the anonymous `/api/v1/public/*` routes (sections 3.11, 4.9, 5.12), like Phase 2 public sample posts and Phase 3 public threads, and only while the matching setting is on.

---

## 1. Roles, permissions, and capability matrix

### 1.1 Roles

| Holder | Scope in Phase 4 |
|---|---|
| `SystemAdmin` (and a delegated `PlatformAdmin` session) | Everything |
| `DirectoryModerator` (`resourceType: "Directory"`, `resourceId: "*"`) | Create, edit, verify, archive, and delete venues and instructors; review submissions and claims; moderate reports on venues and instructors; regeocode. Cannot change directory configuration (programs, labels, map, geocoder) |
| `EventManager` (`resourceType: "Events"`, `resourceId: "*"`) | Create, edit, publish, cancel, and duplicate any event; manage RSVPs, waitlists, and check-in; moderate comments on events; cannot change categories or event settings |
| Event host (any member listed in an event's `hosts`) | Edit that event's description, cover, and labels; see its RSVP list and check people in. Hosts are assigned by an `EventManager` or `SystemAdmin` |
| `Moderator` with `resourceId: "*"` and `FeedModerator` | No Phase 4 rights except: comments and reactions on lessons and events promoted to the feed are moderated through their target (section 6.3) |

The library has no dedicated role in Phase 4: `SystemAdmin` manages series and lessons. Open question 3 asks whether to add a `LibraryEditor` role. `isStaff` on `UserSummary` becomes true for `DirectoryModerator` and `EventManager` holders too (members cannot block them, Phase 2 decision 9).

### 1.2 Capability matrix

"Directory staff" is `SystemAdmin` or `DirectoryModerator`; "event staff" is `SystemAdmin` or `EventManager`. Staff content protection from Phase 2 decision 12 carries over: a non-admin role never hides or removes content authored by a `SystemAdmin`.

| Action | Who |
|---|---|
| List and read venues, instructors, events, series, lessons | Anyone the access model and `audience` allow |
| Use the map, "near me", and place search | Anyone who can list venues |
| Submit a new venue or instructor, an edit, or a closure | `directory.submissions` on, `canPost`, not muted, under the pending limit |
| Claim an instructor as yourself | `directory.claims` on, `canPost`, not muted, the instructor is unclaimed |
| Withdraw your own pending submission | Submitter |
| Approve, reject, or edit a submission before approval | Directory staff |
| Create, edit, verify, archive, delete venues and instructors; link and unlink | Directory staff |
| Edit directory configuration (labels, programs, geocoder, map) | `SystemAdmin` |
| RSVP (Going, Interested), cancel RSVP | Callers meeting `rsvpAudience`, not muted, event published and not canceled, inside the RSVP window |
| Comment, reply, react on events and lessons | Phase 2 rules on the `commentTarget`, plus the gating table of 0.4 and `commentsEnabled` |
| See an event's attendee list | Per the event's `attendeeVisibility`: `Everyone` (anyone who can read the event), `Attendees` (members with a Going RSVP), `Staff` (event staff and hosts) |
| Create, edit, publish, cancel, duplicate events; manage RSVPs; check in | Event staff; hosts for their events (edit description, cover, labels, check in only) |
| Edit event categories and settings | `SystemAdmin` |
| Create, edit, order, publish, archive series and lessons; upload lesson media | `SystemAdmin` |
| Record and reset own progress | Callers who may play the lesson |
| Download a lesson for offline viewing (mobile) | Section 5.9 |
| Follow a series | Signed-in callers who can read the series |
| Promote a lesson or an event to the feed | `SystemAdmin`, or a member with `canPromoteToFeed` who can read it (Phase 3 section 8 rules; the audience only narrows) |
| Report a venue, instructor, event, lesson, or their comments | Any signed-in caller who can read it |

### 1.3 Viewer capabilities

Every detail and list payload carries `viewer`, computed for the caller; clients drive menus from it and never re-derive permissions.

| Type | `viewer` |
|---|---|
| Venue | `{ canEdit, canSuggestEdit, canReportClosed, canReport, canModerate }` |
| Instructor | `{ canEdit, canSuggestEdit, canClaim, canReport, canModerate }` |
| Event | `{ canRsvp, canRsvpGoing, canJoinWaitlist, canComment, canReact, canReport, canSeeAttendees, canEdit, canCancel, canCheckIn, canPromote, canOpen, requires }` |
| Series | `{ canFollow, canEdit, canOpen, requires }` |
| Lesson | `{ canPlay, canDownload, canComment, canReact, canReport, canEdit, canPromote, canOpen, requires }` |

`canOpen` and `requires` follow the locked stub rules (`ACCESS_MODELS_CONTRACT.md` section 3.5): `requires` is `"SignIn"`, `"Membership"`, or null.

---

## 2. Shared shapes

### 2.1 Address and geo

```json
{
  "address": {
    "line1": "100 Example Street", "line2": null,
    "locality": "Exampleville", "region": "EX", "postalCode": "00000", "countryCode": "US",
    "formatted": "100 Example Street, Exampleville, EX 00000"
  },
  "addressVisibility": "Exact",
  "geo": { "lat": 37.7749, "lng": -122.4194, "precision": "Exact", "source": "Geocoder", "geocodedUtc": "2026-11-20T18:00:00Z" }
}
```

| Field | Rule |
|---|---|
| `address.*` | `line1` 0 to 200, `line2` 0 to 200, `locality` 0 to 100, `region` 0 to 100, `postalCode` 0 to 20, `countryCode` ISO 3166-1 alpha-2 (required when any address field is set), `formatted` server-built for display |
| `addressVisibility` | `Exact` (full address and exact pin), `Approximate` (locality, region, country, and a pin rounded to 2 decimal places, about 1 km), `LocalityOnly` (locality, region, country; no pin, not on the map). Staff always see the exact values |
| `geo.lat`, `geo.lng` | WGS 84 decimal degrees, stored as `DOUBLE PRECISION`; returned rounded to 5 decimal places (about 1 m) for `Exact`, 2 for `Approximate`, null for `LocalityOnly` |
| `geo.precision` | What the caller receives: `Exact`, `Approximate`, `None` |
| `geo.source` | `Geocoder`, `Manual` (staff placed the pin), `None` (staff only) |

### 2.2 Venue

```json
{
  "id": "ven_...",
  "name": "Example Club Downtown",
  "slug": "example-club-downtown",
  "affiliation": "Example Network",
  "summary": "Friendly club, all levels welcome.",
  "description": "Markdown subset of Phase 2 section 3.",
  "address": { "...": "2.1" },
  "addressVisibility": "Exact",
  "geo": { "...": "2.1" },
  "distanceKm": 2.4,
  "timeZone": "America/Los_Angeles",
  "phone": "+1 555 0100",
  "email": "hello@example.test",
  "websiteUrl": "https://example.test",
  "socialLinks": [ { "kind": "Instagram", "url": "https://..." } ],
  "scheduleText": "Mon to Fri 6 pm; Sat 10 am",
  "programs": [ { "key": "beginners", "label": "Beginners" } ],
  "visitorPolicy": "Welcome",
  "visitorPolicyNote": "Message ahead for weekend visits.",
  "photos": [ { "...": "Media" } ],
  "instructors": { "objects": [ { "...": "InstructorSummary" } ], "totalRecords": 4, "enumerate": "/api/v1/venues/ven_.../instructors/enumerate" },
  "isVerified": true,
  "verifiedUtc": "2026-11-20T18:00:00Z",
  "status": "Active",
  "audience": "Members",
  "labels": [], "tags": {},
  "viewer": { "...": "1.3" },
  "createdUtc": "...", "updatedUtc": "..."
}
```

| Field | Rule |
|---|---|
| `name` | 2 to 120; `slug` derived, unique per property, changeable (redirects like forums) |
| `affiliation` | 0 to 120 free text (a network, association, or parent organization) |
| `summary` 0 to 300; `description` 0 to `directory.maxDescriptionLength` (5000) | Word filtered |
| `distanceKm` | Present only when the request carried `near`; one decimal place |
| `timeZone` | IANA id; defaults from the geocoder when it returns one, else null |
| `phone` | E.164 or free text up to 40; `email` validated; `websiteUrl` and `socialLinks[].url` `https` only, at most `directory.maxSocialLinks` (8); `kind` is a fixed list (`Website`, `Instagram`, `Facebook`, `YouTube`, `TikTok`, `X`, `Other`) |
| `programs` | Keys from the configured programs (fixed bound 50) |
| `visitorPolicy` | `Welcome`, `ByArrangement`, `MembersOnly`, `Unknown`; `visitorPolicyNote` 0 to 300 |
| `photos` | Up to `directory.maxPhotos` (10) Ready images (fixed bound) |
| `instructors` | Bounded preview, at most 10, with the enumeration pointer |
| `status` | `Active`, `Closed` (permanently closed; kept for history, hidden from lists unless `filters.includeClosed`), `Archived` (staff only) |

`VenueSummary` (list item) is the Venue without `description`, `socialLinks`, `scheduleText`, `instructors` (replaced by `instructorCount`), and `photos` (replaced by `coverPhoto`: the first photo or null).

### 2.3 Instructor

```json
{
  "id": "ins_...",
  "displayName": "Example Coach",
  "slug": "example-coach",
  "bio": "Markdown subset.",
  "photo": { "...": "Media or null" },
  "rank": "Gold", "rankLevel": 2,
  "affiliation": "Example Network",
  "linkedUser": { "...": "UserSummary or null" },
  "venues": { "objects": [ { "...": "VenueSummary" } ], "totalRecords": 2, "enumerate": "/api/v1/instructors/ins_.../venues/enumerate" },
  "websiteUrl": null, "socialLinks": [],
  "status": "Active",
  "audience": "Members",
  "labels": [], "tags": {},
  "viewer": { "...": "1.3" },
  "createdUtc": "...", "updatedUtc": "..."
}
```

`rank` and `rankLevel` exist only when `directory.instructorRankFieldKey` is set; they use that field's option keys and `maxLevel`. When `linkedUser` is set and the linked member's own rank field is visible to the caller, clients show the member's value and staff see a "differs from profile" hint if they disagree. A linked instructor's `UserSummary` link lets members open the profile; the profile shows "Listed as {instructor} at" with a bounded preview of venues. `InstructorSummary` omits `bio`, `venues` (replaced by `venueCount`), and `socialLinks`.

### 2.4 DirectorySubmission

```json
{
  "id": "dsb_...",
  "type": "EditVenue",
  "status": "Pending",
  "target": { "type": "Venue", "id": "ven_...", "name": "Example Club Downtown" },
  "submitter": { "...": "UserSummary" },
  "note": "They moved across the street in October.",
  "changes": [
    { "field": "address.line1", "current": "100 Example Street", "proposed": "105 Example Street" },
    { "field": "programs", "current": ["beginners"], "proposed": ["beginners", "youth"] }
  ],
  "possibleDuplicates": { "objects": [ { "...": "VenueSummary" } ], "totalRecords": 1, "enumerate": null },
  "reviewer": null, "reviewNote": null, "decidedUtc": null,
  "labels": [], "tags": {},
  "createdUtc": "...", "updatedUtc": "..."
}
```

| Field | Rule |
|---|---|
| `type` | `NewVenue`, `EditVenue`, `NewInstructor`, `EditInstructor`, `ClaimInstructor`, `ReportClosed` |
| `status` | `Pending`, `Approved`, `Rejected`, `Withdrawn`, `Superseded` (another approved change made this one stale and the reviewer closed it) |
| `changes` | Structured field and value pairs (stored as child rows, not JSON); bounded by the field list of the target type (at most 40). For new entries `current` is null. Media changes name media ids |
| `possibleDuplicates` | Computed at submit time for `NewVenue` and `NewInstructor` (name similarity above `directory.duplicateNameSimilarity` 0.8 and, for venues, within `directory.duplicateDistanceKm` 0.5), at most 5; shown to the submitter before confirming and to reviewers |
| `note` | 0 to 1000, word filtered; `reviewNote` 0 to 1000, shown to the submitter |

### 2.5 Event, RSVP

```json
{
  "id": "evt_...",
  "title": "Autumn meetup",
  "slug": "autumn-meetup",
  "summary": "Open session and social.",
  "description": "Markdown subset.",
  "category": { "key": "meetup", "label": "Meetup", "color": "#1F5FBF" },
  "format": "InPerson",
  "status": "Published",
  "startUtc": "2026-12-05T18:00:00Z",
  "endUtc": "2026-12-05T21:00:00Z",
  "timeZone": "America/Los_Angeles",
  "isAllDay": false,
  "location": {
    "venue": { "...": "VenueSummary or null" },
    "name": null,
    "address": { "...": "2.1 or null" },
    "addressVisibility": "Exact",
    "geo": { "...": "2.1 or null" },
    "distanceKm": null
  },
  "online": { "platformLabel": "Video call", "joinUrl": null, "joinUrlAvailableUtc": "2026-12-05T17:45:00Z" },
  "capacity": 40,
  "goingCount": 37, "interestedCount": 12, "waitlistCount": 0, "spotsLeft": 3,
  "waitlistEnabled": true,
  "rsvpAudience": "Members",
  "rsvpOpensUtc": null, "rsvpClosesUtc": "2026-12-05T16:00:00Z",
  "attendeeVisibility": "Attendees",
  "attendeePreview": { "objects": [ { "...": "UserSummary" } ], "totalRecords": 37, "enumerate": "/api/v1/events/evt_.../attendees/enumerate" },
  "externalUrl": null,
  "cover": { "...": "Media or null" },
  "hosts": [ { "...": "UserSummary" } ],
  "instructors": [ { "...": "InstructorSummary" } ],
  "myRsvp": { "id": "rsv_...", "status": "Going", "guestCount": 0, "position": null, "checkedInUtc": null, "updatedUtc": "..." },
  "reminderOffsetsMinutes": [1440, 120],
  "commentsEnabled": true,
  "commentTarget": { "type": "Event", "id": "evt_..." },
  "commentCount": 4, "reactionCounts": {}, "reactionTotal": 0, "myReactions": [],
  "promotion": null,
  "audience": "Members",
  "canceledUtc": null, "cancelReason": null,
  "locked": false,
  "labels": [], "tags": {},
  "viewer": { "...": "1.3" },
  "publishedUtc": "...", "createdUtc": "...", "updatedUtc": "..."
}
```

| Field | Rule |
|---|---|
| `title` 3 to 140; `summary` 0 to 300; `description` 0 to `events.maxDescriptionLength` (10000) | Word filtered; mentions in `description` notify on publish (Phase 2 section 3.3) |
| `format` | `InPerson`, `Online`, `Hybrid`. `InPerson` and `Hybrid` need `location` (a venue, or a name and address); `Online` and `Hybrid` need `online` |
| `status` | `Draft` (staff and hosts only), `Published`, `Canceled`. "Past" is derived (`endUtc` before now) and filterable, not a status |
| `startUtc`, `endUtc`, `timeZone` | Stored in UTC with the IANA zone of the event's location; clients display in the event's zone with the viewer's zone in a secondary line when they differ. DST is handled by recomputing UTC from the local wall time when staff edit the local time (section 4.6) |
| `isAllDay` | Start and end are local dates (`startUtc` is local midnight in `timeZone`) |
| `location.venue` | A directory venue the caller can read (or null and `location.name` 0 to 120 plus an address). A venue the caller cannot read is replaced by its locality only |
| `online.joinUrl` | `https` only. Returned only to callers with a Going RSVP and event staff and hosts, and only from `joinUrlAvailableUtc` (start minus `events.joinUrlRevealMinutes`, 15); null otherwise |
| `capacity` | 1 to 100000, or null for unlimited; Going RSVPs plus their guests count against it |
| `spotsLeft` | `capacity - going - guests`, or null; never negative |
| `rsvpOpensUtc`, `rsvpClosesUtc` | Optional window; RSVPs always close at `startUtc` |
| `attendeePreview` | Bounded preview (at most 10) when `viewer.canSeeAttendees`; otherwise null. Only Going members, excluding members who chose `hideFromAttendeeLists` (section 4.3) |
| `externalUrl` | Optional `https` link for external ticketing or details (paid ticketing stays out of scope) |
| `hosts` | At most `events.maxHosts` (10) members (fixed bound); `instructors` at most 10 directory instructors |
| `myRsvp.position` | Waitlist position (1 based) when `Waitlisted`, else null |
| `locked` | Locked stub per `ACCESS_MODELS_CONTRACT.md` section 3.5: `description`, `online`, `attendeePreview`, `hosts`, and `myRsvp` are empty or null |

`EventSummary` (list item) omits `description`, `online.joinUrl`, `attendeePreview`, `hosts` (replaced by `hostCount`), and `instructors`.

### 2.6 Series, Lesson, LessonProgress

```json
{
  "id": "ser_...",
  "slug": "getting-started",
  "title": "Getting started",
  "summary": "Ten short lessons for your first month.",
  "description": "Markdown subset.",
  "cover": { "...": "Media or null" },
  "levels": [ { "key": "Bronze", "label": "Bronze", "color": "#A0662D" } ],
  "status": "Published",
  "sortOrder": 10,
  "lessonCount": 10,
  "totalDurationSeconds": 3420,
  "myProgress": { "completedLessons": 3, "percent": 30, "lastLessonId": "les_...", "lastWatchedUtc": "..." },
  "isFollowed": false,
  "audience": "Members",
  "locked": false,
  "labels": [], "tags": {},
  "viewer": { "...": "1.3" },
  "publishedUtc": "...", "createdUtc": "...", "updatedUtc": "..."
}
```

```json
{
  "id": "les_...",
  "series": { "id": "ser_...", "slug": "getting-started", "title": "Getting started" },
  "position": 4,
  "slug": "first-steps",
  "title": "First steps",
  "summary": "What to focus on in week one.",
  "description": "Markdown subset.",
  "kind": "Video",
  "video": { "...": "Media (Video) or null" },
  "durationSeconds": 342,
  "markers": [ { "id": "lmk_...", "seconds": 0, "label": "Introduction" }, { "id": "lmk_...", "seconds": 95, "label": "Common mistakes" } ],
  "resources": [
    { "id": "lrs_...", "kind": "Document", "title": "Lesson notes", "media": { "...": "Media (Document)" }, "url": null, "text": null },
    { "id": "lrs_...", "kind": "Link", "title": "Further reading", "media": null, "url": "https://example.test/reading", "text": null },
    { "id": "lrs_...", "kind": "Reference", "title": "Handbook", "media": null, "url": null, "text": "Chapter 3, pages 40 to 52" }
  ],
  "captions": [ { "language": "en", "label": "English", "media": { "...": "Media (Document, text/vtt)" } } ],
  "levels": [ { "key": "Bronze", "label": "Bronze", "color": "#A0662D" } ],
  "status": "Published",
  "publishAtUtc": null,
  "audience": "Members",
  "downloadable": true,
  "commentsEnabled": true,
  "commentTarget": { "type": "Lesson", "id": "les_..." },
  "commentCount": 9, "reactionCounts": { "like": 14 }, "reactionTotal": 14, "myReactions": [],
  "myProgress": { "...": "LessonProgress or null" },
  "previous": { "id": "les_...", "title": "...", "canOpen": true },
  "next": { "id": "les_...", "title": "...", "canOpen": false },
  "promotion": null,
  "locked": false,
  "labels": [], "tags": {},
  "viewer": { "...": "1.3" },
  "publishedUtc": "...", "createdUtc": "...", "updatedUtc": "..."
}
```

| Field | Rule |
|---|---|
| `kind` | `Video` (requires `video`), `Article` (text only; `description` up to `library.maxArticleLength` 50000) |
| `markers` | At most `library.maxMarkersPerLesson` (50), sorted by `seconds`, label 1 to 80 (fixed bound) |
| `resources` | At most `library.maxResourcesPerLesson` (10) (fixed bound). `Document` points at a Ready `Document` media (section 5.4); `Link` is an `https` URL; `Reference` is plain text up to 300 (for example a chapter of a printed book) |
| `captions` | At most 10 WebVTT documents (fixed bound), `language` BCP 47 |
| `levels` | Keys from the configured levels (section 0.2), at most 10 |
| `status` | `Draft`, `Scheduled` (`publishAtUtc` set), `Processing` (the video is not Ready; publishes when Ready if due), `Published`, `Archived` (hidden from lists, still playable by members who have progress, so "Continue watching" never breaks) |
| `downloadable` | Staff switch per lesson; the effective `viewer.canDownload` also needs section 5.9 |
| `previous`, `next` | Neighbors in the series by `position`, with `canOpen` for the caller |
| `locked` | Stub: `description`, `video`, `markers`, `resources`, `captions` empty or null |

`LessonProgress`:

```json
{ "id": "lpr_...", "lessonId": "les_...", "positionSeconds": 151, "maxPositionSeconds": 151, "watchedSeconds": 160, "percent": 44, "completed": false, "completedUtc": null, "lastWatchedUtc": "2026-11-20T18:02:00Z", "labels": [], "tags": {} }
```

`SeriesSummary` and `LessonSummary` (list items) omit `description`, `markers`, `resources`, `captions`, `previous`, `next`, and replace `video` with `{ durationSeconds, posterThumbUrl, blurhash }`.

---

## 3. Directory: venues and instructors

### 3.1 Member routes

| Method and path | Feature, action, signed-out | Body | Success | Errors |
|---|---|---|---|---|
| `POST /api/v1/venues/enumerate` (and `GET /api/v1/venues`) | directory, List, yes | `EnumerationQuery`; section 3.2 | `200 EnumerationResult<VenueSummary>` | `400 ValidationFailed`, `400 InvalidGeo` |
| `GET /api/v1/venues/{idOrSlug}` | directory, Read, yes | | `200 Venue` | `404` |
| `POST /api/v1/venues/{id}/instructors/enumerate` | directory, List, yes | `EnumerationQuery` (`NameAscending` default) | `200 EnumerationResult<InstructorSummary>` | |
| `POST /api/v1/instructors/enumerate` (and `GET`) | directory, List, yes | `EnumerationQuery`; filters `venueId`, `rank` (keys), `claimed`, `linkedUserId`, `audience` | `200 EnumerationResult<InstructorSummary>` | |
| `GET /api/v1/instructors/{idOrSlug}` | directory, Read, yes | | `200 Instructor` | `404` |
| `POST /api/v1/instructors/{id}/venues/enumerate` | directory, List, yes | `EnumerationQuery` | `200 EnumerationResult<VenueSummary>` | |
| `POST /api/v1/directory/map/enumerate` | directory, List, yes | Section 3.3 | `200 EnumerationResult<MapFeature>` | `400 InvalidGeo` |
| `GET /api/v1/directory/geocode`, `POST .../geocode/enumerate` | Signed in | `EnumerationQuery` with `filters.query` (2 to 200 characters); GET uses `query=Exampleville` | `200 EnumerationResult<GeocodeCandidateView>` with `objects` (`id`, `rank`, `label`, `lat`, `lng`, `bbox`, empty `labels`/`tags`). RankAscending default. Pages enumerate the finite top-five provider candidates; totals do not count all possible places. Search, epoch creation windows, metadata and query-scoped continuation apply | `429`, `503 IntegrationNotConfigured` |
| `POST /api/v1/directory/submissions` | directory, Write, no | Section 3.7 | `201 DirectorySubmission` | `403 SubmissionsDisabled`, `409 SubmissionLimitReached`, `409 AlreadyClaimed`, `400 ContentFiltered` |
| `POST /api/v1/me/directory/submissions/enumerate` | directory, List, no | `EnumerationQuery`; filters `status`, `type` | `200 EnumerationResult<DirectorySubmission>` (own) | |
| `POST /api/v1/directory/submissions/{id}/withdraw` | directory, Write, no | | `200 DirectorySubmission` | `409` when not Pending |
| `POST /api/v1/directory/programs/enumerate` | directory, List, yes | `EnumerationQuery` (`SortOrderAscending`) | `200 EnumerationResult<DirectoryProgram>` | |

Media for submissions (venue photos, instructor photo) is uploaded with the Phase 2 media routes and referenced by id; it stays unattached (owner visible only) until approval attaches it to the venue or instructor, and is deleted by `MediaCleanupWorker` if the submission is rejected or withdrawn.

### 3.2 Venue enumeration

Orderings: `NameAscending` (default without `near`), `DistanceAscending` (default with `near`; requires `near`), `CreatedDescending`, `CreatedAscending`, `UpdatedDescending`, `Relevance` (only with `search`).

| Filter | Values |
|---|---|
| `near` | `{ "lat": 37.77, "lng": -122.42, "radiusKm": 25 }`; `radiusKm` 0.1 to `directory.maxRadiusKm` (500), default `directory.defaultRadiusKm` (50) |
| `bbox` | `{ "south", "west", "north", "east" }`; `west > east` means the box crosses the antimeridian. Not combinable with `near` |
| `programs` | One or more program keys (any of) |
| `countryCode`, `region`, `locality` | Exact match (case-insensitive) |
| `visitorPolicy` | One or more values |
| `verifiedOnly`, `includeClosed` | Booleans (default false) |
| `instructorId` | Venues linked to that instructor |
| `audience` | One or more values |

`search` runs through `ISearchIndex` (section 7) over name, affiliation, summary, locality, region, and linked instructor names, then joins visibility before ranking, bounded by `maxResults`. Venues with `addressVisibility: LocalityOnly` match `near` and `bbox` by their stored position but return no `geo` and no `distanceKm` finer than whole kilometers.

### 3.3 Map

`POST /api/v1/directory/map/enumerate`:

```json
{ "bbox": { "south": 37.6, "west": -122.6, "north": 37.9, "east": -122.2 }, "zoom": 11, "maxResults": 300, "labels": [], "tags": {}, "filters": { "programs": ["youth"] } }
```

Returns `EnumerationResult<MapFeature>` where each object is either a point or a cluster:

```json
{ "kind": "Point", "venue": { "id": "ven_...", "name": "...", "geo": { "lat": 37.77, "lng": -122.41, "precision": "Exact" }, "programs": ["youth"], "isVerified": true, "coverThumbUrl": "..." } }
{ "kind": "Cluster", "count": 42, "lat": 37.71, "lng": -122.45, "bbox": { "south": 37.6, "west": -122.6, "north": 37.8, "east": -122.3 } }
```

- The configured feature budget (`directory.map.maxFeatures` or `events.mapMaxFeatures`) controls clustering independently of transport paging. The bounded generated feature set is paged through the shared normalizer and stable scoped keysets, with standard maxResults/skip/continuationToken. `totalRecords` and `recordsRemaining` count features; scalar `matchedRecords` counts represented source venues/events. The viewport, zoom, feature budget and caller visibility are bound into cursors. Changing any of these requires a new enumeration. Tapping a cluster zooms to its bbox.
- `LocalityOnly` venues are never points; `Approximate` venues are points at their rounded position.
- Results are filtered by visibility, blocks do not apply to venues, and the same filters as 3.2 apply.

Clients also keep client-side clustering for the points they already hold (MapLibre GeoJSON source clustering on web and mobile), so panning within a loaded area does not refetch.

### 3.4 Spatial queries (implementation)

No PostGIS in Phase 4 (open question 6). `venues` stores `lat` and `lng` as `DOUBLE PRECISION` with the index `(propertyid, lat, lng) WHERE status = 'Active' AND deletedutc IS NULL`. A `near` query prefilters by the bounding box of the radius (latitude span `radiusKm / 111.32`; longitude span divided by `cos(lat)`, clamped near the poles, split into two boxes across the antimeridian), then computes the Haversine distance in SQL (`6371.0088 * 2 * asin(sqrt(...))`), filters by radius, and orders by distance then `id` (keyset continuation over `(distance, id)`). Map clustering uses the same index with the viewport box. These run on any PostgreSQL 17 with no extension; at the expected directory size (thousands of venues) the bounding box index keeps each query under a few milliseconds.

### 3.5 Geocoding

Geocoding is an engine integration behind `IGeocoder` (`TrellisKit.Core/Integrations/Interfaces`), selected per property in the admin console (Settings, Directory, Geocoding), with secrets stored encrypted through the per-property settings store (`MULTIPROPERTY_CONTRACT.md` section 5).

| Provider (`directory.geocoder.provider`) | Configuration | Notes |
|---|---|---|
| `None` (default) | none | No automatic geocoding. Staff place pins manually on the map (drag a marker; `geo.source: Manual`); member submissions arrive without a position and the reviewer places it |
| `Nominatim` | `baseUrl` (self-hosted, or a provider's hosted Nominatim-compatible endpoint), optional `apiKey`, `email` contact parameter when the provider's usage policy requires it, `requestsPerSecond` (default 1) | OpenStreetMap data. The engine sends a generic `User-Agent: TrellisKit-Geocoder/<engine version>` and never sends member data. Using the public openstreetmap.org instance is allowed only within its usage policy (low volume, attribution); production properties should self-host or use a paid compatible host |
| `AwsLocation` | `placeIndexName`, `region`; credentials from the task role (no secret stored) | Amazon Location Service Places. Infra adds a place index per property when this provider is chosen |
| `Http` | `baseUrl`, `apiKey` (secret), `authHeaderName` | A provider exposing the Pelias or Photon style `GET /search?text=` GeoJSON API (several commercial and self-hosted options), mapped by one adapter |

Rules:
- **When:** geocoding runs on staff create and edit of an address, on approval of a submission with an address change, and on `POST /api/v1/admin/directory/regeocode`. Never on reads. Member submissions are geocoded at submit time only to compute `possibleDuplicates` and a proposed pin; the reviewer confirms or moves it.
- **Cache:** results are cached per property in `geocoderesults` keyed by the SHA-256 of the normalized address (or query) for `directory.geocoder.cacheDays` (90), so repeated edits and regeocoding do not repeat calls.
- **Manual override:** a staff-placed pin (`geo.source: Manual`) is never replaced by regeocoding unless the address changes.
- **Place search (`POST /api/v1/directory/geocode/enumerate`, GET `/api/v1/directory/geocode`):** forward geocoding of a typed place name, so a member can search "near Exampleville". The backend proxies the call so the provider key stays server-side, enumerates a finite shortlist of at most 5 candidates in standard `objects` pages, does not store the query text (only a counter), and redacts the body in request history.
- **Failure:** a provider error leaves the address saved with `geo.source: None` and a staff task in the directory admin page ("needs a pin"); it is not a save failure. `GeocodeFailed` is returned only when staff explicitly request geocoding and nothing matched.

**Privacy:**
- "Near me" uses the device location on the client only after the OS permission prompt (web Geolocation API, mobile `expo-location` foreground permission). Clients round the position to 2 decimal places (about 1 km) before sending it. The server never stores a caller's position, never writes it to logs, and request history replaces `near.lat` and `near.lng` with `[redacted]`. The map works without location permission (default center from the map configuration, place search instead).
- Venue addresses are public business information by default, but a submitted address may be a private home. `addressVisibility` defaults to `Exact` for staff-created venues and to `Approximate` for member submissions (`directory.submissionDefaultAddressVisibility`), and the submission form explains the choice. `LocalityOnly` keeps a venue off the map entirely.
- Instructor entries never carry an address. Linking an instructor to a member account requires the member's own claim (section 3.8) or a staff link that the member is notified about and can remove.
- Geocoder providers receive only the venue address or the typed place query, never a member identifier.

### 3.6 Map provider configuration (no lock-in)

The map is a client concern configured per property; the engine stores only addresses and plain coordinates, so switching providers needs no data migration.

| Setting (per property) | Values | Default |
|---|---|---|
| `directory.map.provider` | `None` (list view only), `MapLibre` (web and mobile render a MapLibre style), `PlatformNative` (mobile uses the platform map through `react-native-maps`: Apple Maps on iOS, Google Maps on Android; web falls back to `MapLibre` when a style is set, else list view) | `None` |
| `directory.map.styleUrl`, `directory.map.darkStyleUrl` | A MapLibre style JSON URL (any vector or raster tile source: a hosted tile provider, Amazon Location Service maps, or self-hosted tiles such as a PMTiles archive in the property's media bucket) | null |
| `directory.map.publicKey` | A client-visible tile key restricted at the provider by referrer and app id. Not a secret by nature; still editable only by `SystemAdmin` and never logged | null |
| `directory.map.tileProxy` | Boolean. When true, the style and tile requests go through `GET /api/v1/directory/tiles/{*path}` (directory, Read), which adds a secret key server-side (`directory.map.proxyKey`, encrypted) and caches tiles in memory and with `Cache-Control: public, max-age=86400`. Use it when the provider cannot restrict keys | false |
| `directory.map.attribution` | Text or Markdown link shown on the map (required by most tile and data licenses) | null |
| `directory.map.defaultCenter`, `directory.map.defaultZoom`, `directory.map.maxZoom` | `{ lat, lng }`, 0 to 22 | `{ 0, 0 }`, 2, 18 |
| `directory.map.androidGoogleMapsKey` | Build-time value for `PlatformNative` on Android, read from the property's mobile configuration (`property.json` `mobile.googleMapsApiKey`), never from server settings | none |

`GET /api/v1/property` returns `directory.map` without secrets (`provider`, style URLs or the proxied style URL, `publicKey`, `attribution`, center, zoom). Clients: website uses MapLibre GL JS (`maplibre-gl`); mobile uses `@maplibre/maplibre-react-native` for `MapLibre` and `react-native-maps` for `PlatformNative`. Both apps cluster markers, show the attribution, support dark mode through `darkStyleUrl`, and fall back to the list view when the provider is `None` or the style fails to load.

### 3.7 Submissions

`POST /api/v1/directory/submissions`:

```json
{
  "type": "NewVenue",
  "targetId": null,
  "fields": {
    "name": "Example Club Uptown",
    "address": { "line1": "200 Example Avenue", "locality": "Exampleville", "region": "EX", "postalCode": "00001", "countryCode": "US" },
    "addressVisibility": "Approximate",
    "programs": ["beginners"],
    "websiteUrl": "https://uptown.example.test",
    "scheduleText": "Tue and Thu 7 pm",
    "photoMediaIds": ["med_..."]
  },
  "note": "Opened last month.",
  "confirmNotDuplicate": false,
  "labels": [], "tags": {}
}
```

| Rule | Value |
|---|---|
| `fields` | The editable fields of the target type (Venue: name, affiliation, summary, description, address, addressVisibility, phone, email, websiteUrl, socialLinks, scheduleText, programs, visitorPolicy, visitorPolicyNote, photoMediaIds; Instructor: displayName, bio, photoMediaId, rank, rankLevel, affiliation, websiteUrl, socialLinks, venueIds). For `Edit*` only changed fields are sent; unknown fields `400 ValidationFailed` |
| `ClaimInstructor` | `targetId` is the instructor, `fields` may be empty, `note` explains the claim (required, 10 to 1000) |
| `ReportClosed` | `targetId` is the venue, `note` optional |
| Duplicates | For `NewVenue` and `NewInstructor`, when `possibleDuplicates` is not empty and `confirmNotDuplicate` is false, the response is `200` with `status: "Draft"` and the duplicates, and nothing is queued; the client asks the member and resubmits with `confirmNotDuplicate: true` (`201`) |
| Word filter | Every text field and `note` (`400 ContentFiltered`) |
| Limit | `directory.maxPendingSubmissionsPerMember` (5) pending at a time |

Review (directory staff):

| Method and path | Body | Success | Errors |
|---|---|---|---|
| `POST /api/v1/admin/directory/submissions/enumerate` | `EnumerationQuery`; filters `status` (default `Pending`), `type`, `submitterUserId`, `targetId`; orderings `CreatedAscending` (default, oldest first), `CreatedDescending` | `200 EnumerationResult<DirectorySubmission>` | |
| `GET /api/v1/admin/directory/submissions/{id}` | | `200 DirectorySubmission` with a fresh `changes` diff against the current target | |
| `PUT /api/v1/admin/directory/submissions/{id}` | `{ "fields": { ... } }` reviewer overrides before approval (for example fix the pin or the name) | `200 DirectorySubmission` | |
| `POST /api/v1/admin/directory/submissions/{id}/approve` | `{ "reviewNote"?, "notifySubmitter": true, "geo"?: { lat, lng } }` | `200 { submission, target: Venue \| Instructor }` | `409 SubmissionStale` (target changed since the diff was loaded; reload), `400 GeocodeFailed` |
| `POST /api/v1/admin/directory/submissions/{id}/reject` | `{ "reviewNote": "..." (required 3 to 1000), "notifySubmitter": true }` | `200 DirectorySubmission` | |

Approving applies the changes in one transaction (create the entry or update the changed fields, attach approved media, link venues, set `linkedUserId` for a claim, set `status: Closed` for `ReportClosed`), audits `directory.submission.approve` with the target, and sends `SubmissionDecision` to the submitter (section 8). Other pending submissions on the same target are left pending and show `SubmissionStale` on approval until reloaded. A staff email `staff_directory_submission` goes to the configured recipients on each new submission (section 9). The review target is `directory.reviewTargetHours` (72): overdue pending submissions appear first and send `SubmissionOverdue` to directory staff once.

### 3.8 Instructor claims

A claim links an instructor entry to the claiming member's account after review. Staff may also link directly (`POST /api/v1/admin/instructors/{id}/link` with `{ userId }`), which notifies the member (`InstructorLinked`) with an "Unlink me" action (`POST /api/v1/me/instructor-links/{instructorId}/unlink`, always allowed, audited). A linked member edits their entry only through `EditInstructor` submissions in Phase 4 (open question 9).

### 3.9 Admin routes (directory staff unless noted)

| Method and path | Body | Success |
|---|---|---|
| `POST /api/v1/admin/venues/enumerate` | `EnumerationQuery` (admin maximum); member filters plus `status` (including `Archived`), `geoSource` (`None` finds "needs a pin"), `verified` | `200 EnumerationResult<AdminVenue>` (Venue plus `openReportCount`, `pendingSubmissionCount`, `geocodedUtc`, exact address) |
| `POST /api/v1/admin/venues` | Venue fields plus `geo?` (manual pin), `labels`, `tags` | `201 Venue` |
| `GET`, `PUT`, `DELETE /api/v1/admin/venues/{id}` | | `200`, `200`, `204` (soft delete; links removed; events keep a frozen location copy) |
| `POST /api/v1/admin/venues/{id}/verify`, `.../unverify` | `{ note? }` | `200 Venue` |
| `POST /api/v1/admin/venues/{id}/close`, `.../reopen`, `.../archive`, `.../unarchive` | `{ reason? }` | `200 Venue` |
| `PUT /api/v1/admin/venues/{id}/instructors/{instructorId}`, `DELETE` same | `{ "role"?: "Head" \| "Regular" \| "Guest" }` | `200`, `204` |
| `POST /api/v1/admin/instructors/enumerate`, `POST /api/v1/admin/instructors`, `GET`, `PUT`, `DELETE .../{id}`, `POST .../{id}/link`, `POST .../{id}/unlink` | | As venues |
| `POST /api/v1/admin/directory/import` | `{ "rows": [ { ...venue fields } ] }` (at most 1000 rows), `dryRun` (default true) | `200 { created, updated, skipped, errors: { objects (max 100), totalRecords } }`; geocoding runs in the background |
| `POST /api/v1/admin/directory/regeocode` | `{ "venueIds"?: [...] (max 500), "onlyMissing": true }` | `202 { queued }` (a `GeocodeWorker` job) |
| `GET /api/v1/admin/directory/export.csv` | | CSV of venues and instructors, streamed, staff only |
| `POST /api/v1/admin/directory/programs`, `PUT`, `DELETE .../{id}`, `PUT /api/v1/admin/directory/programs/order` (`SystemAdmin`) | | Program CRUD; deleting a program in use retires it (hidden from forms and filters, values kept) |
| `GET /api/v1/admin/directory/settings`, `POST .../settings/enumerate` (`SystemAdmin`) | `EnumerationQuery` over directory integration fields; immutable dates use Unix epoch, metadata is empty | `200 EnumerationResult<PropertySettingField>` with `objects` plus `config` (labels, submission/claim switches) and `geocoderConfigured` context |
| `PUT /api/v1/admin/directory/settings` (`SystemAdmin`) | Edit labels, switches, geocoder and map values | `200 DirectorySettings` (`settings`, `config`, `geocoderConfigured`) |

Audit actions: `venue.create`, `venue.update`, `venue.delete`, `venue.verify`, `venue.close`, `venue.archive`, `instructor.create`, `instructor.update`, `instructor.delete`, `instructor.link`, `instructor.unlink`, `directory.submission.approve`, `directory.submission.reject`, `directory.import`, `directory.settings.update`.

### 3.10 Reports and moderation

`POST /api/v1/reports` accepts `targetType: "Venue"` and `"Instructor"`, with the Phase 2 reasons plus two new ones: `Inaccurate` and `PermanentlyClosed` (a `PermanentlyClosed` report also creates a `ReportClosed` submission). Reports are stored with `scopetype: "Directory"`; `DirectoryModerator` sees them in the moderation queue (`filters.scopeType: "Directory"`). Moderation actions `Hide`, `Unhide`, `Remove`, `Restore` apply to venues and instructors through an `IModerationTargetExtension` (section 15). Auto-hide does not apply to directory entries (a competitor could hide a listing); reports only queue.

### 3.11 Public directory (anonymous)

`POST /api/v1/public/venues/enumerate` and `GET /api/v1/public/venues/{id}`, plus `POST /api/v1/public/directory/map/enumerate`, return only `Public`, `Active` venues when `directory.publicListingsEnabled` is true (default false), in every model, with `Cache-Control: public, max-age=300` and rate limited per IP. They omit `email`, `phone` (unless `directory.publicShowsContact`), instructors' linked users, and `viewer`. When `anonymousBrowsing` is true outside `FullyGated`, anonymous callers may instead use the member routes for `Public` objects.

### 3.12 Venue reviews (as built)

Members rate a venue with 1 to 5 stars and a written review: one live review per member per venue, which the author may edit or delete (and then write again). The venue's linked instructors (instructor entries linked to a member account and linked to the venue) may post one public reply per review. Reviews are signed-in content: the review routes need a signed-in caller (and a membership where the directory is gated), anonymous and public routes never list them, and only the aggregates (`ratingAverage`, `ratingCount`) appear on public payloads.

| Method and path | Feature, action, signed-out | Body | Success | Errors |
|---|---|---|---|---|
| `POST /api/v1/venues/{id}/reviews/enumerate` | directory, List, no | `EnumerationQuery`; orderings `CreatedDescending` (default, newest), `CreatedAscending`, `RatingDescending` (highest, newest first within a rating), `RatingAscending` (lowest, newest first within a rating); filters `rating` (1 to 5, one or a list), `authorUserId`, `hasReply` | `200 EnumerationResult<VenueReview>` | `404` |
| `GET /api/v1/venues/{id}/reviews/{reviewId}` | directory, Read, no | | `200 VenueReview` | `404` |
| `POST /api/v1/venues/{id}/reviews` | directory, Write, no | `{ rating, body, labels?, tags? }` | `201 VenueReview` | `400 ValidationFailed`, `400 ContentFiltered`, `403 ReviewsDisabled`, `403 Muted`, `403 PostingNotAllowed`, `409 AlreadyReviewed`, `429` |
| `PUT /api/v1/venues/{id}/reviews/{reviewId}` | directory, Write, no | `{ rating?, body?, labels?, tags? }` (author) | `200 VenueReview` | as create, `403` (not the author), `404` |
| `DELETE /api/v1/venues/{id}/reviews/{reviewId}` | directory, Write, no | (author) | `204` | `404` |
| `PUT /api/v1/venues/{id}/reviews/{reviewId}/reply` | directory, Write, no | `{ body }` (a linked instructor of the venue; creates or edits the one reply) | `200 VenueReview` | `400`, `403 Forbidden` (not a linked instructor, or replying to your own review), `404` (also when a block in either direction hides the review) |
| `DELETE /api/v1/venues/{id}/reviews/{reviewId}/reply` | directory, Write, no | (the reply's author, or directory staff: audited `venue.review.reply.remove`) | `200 VenueReview` | `403`, `404` |

`VenueReview`:

```json
{
  "id": "vrv_...", "venueId": "ven_...", "author": { "...": "UserSummary" }, "rating": 4, "body": "Friendly and well organized.",
  "editedUtc": null,
  "reply": { "body": "Thank you!", "author": { "...": "UserSummary" }, "instructorId": "ins_...", "instructorName": "Example Coach", "createdUtc": "...", "editedUtc": null },
  "moderationState": "None", "labels": [], "tags": {},
  "viewer": { "isAuthor": false, "canEdit": false, "canDelete": false, "canReply": true, "canDeleteReply": false, "canReport": true, "canModerate": false },
  "createdUtc": "...", "updatedUtc": "..."
}
```

Rules:
- **Text:** `rating` is a whole number 1 to 5; `body` is plain text of `directory.reviewMinLength` (10) to `directory.reviewMaxLength` (2000) characters; a reply is 1 to `directory.reviewReplyMaxLength` (2000). Every text runs through the word filter (Block: `400 ContentFiltered`; Flag: a system report in the `Directory` scope).
- **Who may review:** signed-in callers who may make directory writes (the access model's write tier, `canPost`, not muted; banned accounts have no session), while `directory.reviewsEnabled` (per property, default true) is on; otherwise `403 ReviewsDisabled`. Staff and SystemAdmin may review like anyone else.
- **Aggregates:** `venues.ratingcount` and `venues.ratingtotal` are recomputed from source rows in the same transaction as every write and moderation change, counting only live reviews (not deleted, moderation state `None`). `VenueSummary` (and so `Venue`, list rows, and map list rows) carries `ratingAverage` (one decimal, null without ratings) and `ratingCount`. The venue detail adds `reviews { enabled, averageRating, count, distribution { "1" to "5" }, mine (the caller's own review or null), enumerate }`.
- **Visibility:** reviews follow their venue (`404` when the caller cannot read it). Hidden and removed reviews are listed only for directory staff (with `moderationState`); the author still sees their own hidden review as `mine`. Reviews by members the caller blocked, or who blocked the caller, are left out of the list, and a reply by such a member is omitted from the review.
- **Replies:** `canReply` is true for a member linked to an instructor entry that is linked to the venue (not on their own review); the reply records the instructor entry. The reviewer receives `VenueReviewReply`; replying across a block is `404` (the blocked review is hidden from that instructor).
- **Notifications:** a new review sends `VenueReview` (category `Replies`) to every linked instructor of the venue, except the author and across blocks; `path` is `/venues/{venueId}?review={reviewId}`.
- **Rate limits:** `directory.reviewsPerDay` (10) and `directory.newAccountReviewsPerDay` (2, accounts younger than `comments.newAccountHours`) for creates; edits and replies share `rateLimits.editsPerHour`.
- **Reports and moderation:** `POST /api/v1/reports` accepts `targetType: "VenueReview"` (Phase 2 reasons and `Inaccurate`). Reports queue in the `Directory` scope, so directory staff and SystemAdmin see them in the moderation queue next to venue reports; reports only queue (no auto-hide). `POST /api/v1/admin/moderation/actions` applies `Hide`, `Unhide`, `Remove`, and `Restore` (audited `moderation.<action>`, with the author notified per the Phase 2 rules); a non-admin never acts on a SystemAdmin's review.
- **Account deletion** deletes the member's reviews and replies (aggregates recomputed).

### 3.13 Venue member media (as built)

Any member who may make directory writes adds photos and videos to a venue's Media section while `directory.memberMediaEnabled` (per property, default true) is on. Uploads use the Phase 2 media routes (`POST /api/v1/media/uploads`, purpose `Content`, the member upload limits, image variants, HLS video) and are then attached to the venue; they appear at once. Staff-curated `venue.photos` stay first in the gallery (clients show them before the member media list).

| Method and path | Feature, action, signed-out | Body | Success | Errors |
|---|---|---|---|---|
| `POST /api/v1/venues/{id}/media/enumerate` | directory, List, no | `EnumerationQuery`; orderings `CreatedDescending` (default), `CreatedAscending`; filters `kind` (`Image`, `Video`), `uploaderUserId` | `200 EnumerationResult<VenueMediaItem>` | `404` |
| `POST /api/v1/venues/{id}/media` | directory, Write, no | `{ mediaIds (1 to 10, the caller's unattached uploads), labels?, tags? }` | `201 { objects: VenueMediaItem[] }` | `403 MemberMediaDisabled`, `403 MediaNotAllowed`, `403 VideoUploadNotAllowed`, `403 Muted`, `400 ValidationFailed`, `409 MediaNotAttachable` |
| `DELETE /api/v1/venues/{id}/media/{itemId}` | directory, Write, no | `{ reason? }` | `204` | `403`, `404` |

`VenueMediaItem`: `{ id: "vmd_...", venueId, media: Media, uploader: UserSummary, moderationState, labels, tags, viewer { isUploader, canRemove, canReport }, createdUtc, updatedUtc }`.

Rules:
- **Removal:** the uploader deletes their own item (the stored objects are purged). Directory staff and the venue's linked instructors remove any item (moderation state `Removed`, served objects moved to the retained bucket, audited `venue.media.remove`); a non-admin never removes a SystemAdmin's upload.
- **Visibility:** items follow their venue; removed and hidden items are listed only for directory staff; uploads by blocked members are left out both ways. Media of an item is readable through `GET /api/v1/media/{id}` and the media grant exactly when its item is.
- **Reports and moderation:** `targetType: "VenueMedia"` in `POST /api/v1/reports` and the moderation actions, `Directory` scope, no auto-hide.
- `VenueSummary` and `Venue` carry `mediaCount` (live member items). The venue `viewer` adds `canReview`, `canAddMedia`, `canManageMedia` (staff or a linked instructor), and `canReplyToReviews`.
- **Account deletion** deletes the member's items and purges their media.

---

## 4. Events

### 4.1 Member routes

| Method and path | Feature, action, signed-out | Body | Success | Errors |
|---|---|---|---|---|
| `POST /api/v1/events/enumerate` (and `GET /api/v1/events`) | events, List, yes | Section 4.2 | `200 EnumerationResult<EventSummary>` | `400 ValidationFailed`, `400 InvalidGeo` |
| `POST /api/v1/events/map/enumerate` | events, List, yes | `MapEnumerationQuery` (section 4.2.1) | `200 EnumerationResult<MapFeature>` | `400 ValidationFailed`, `400 InvalidGeo`, `429 RateLimited` |
| `GET /api/v1/events/{idOrSlug}` | events, Read, yes | | `200 Event` | `404` |
| `PUT /api/v1/events/{id}/rsvp` | events, Participate, no | `{ "status": "Going" \| "Interested", "guestCount"?: 0, "hideFromAttendeeLists"?: false }` | `200 Event` (with `myRsvp`; status may be `Waitlisted`) | `403 RsvpNotAllowed`, `409 EventFull`, `409 RsvpClosed`, `409 EventCanceled`, `403 Muted`, `429` |
| `DELETE /api/v1/events/{id}/rsvp` | events, Participate, no | | `200 Event` (`myRsvp: null`) | `409 RsvpClosed` (after start) |
| `POST /api/v1/events/{id}/attendees/enumerate` | events, List, no | `EnumerationQuery`; filters `status` (`Going` default; staff and hosts also `Interested`, `Waitlisted`, `CheckedIn`) | `200 EnumerationResult<{ user: UserSummary, status, guestCount, position, checkedInUtc, createdUtc }>` | `404` when `viewer.canSeeAttendees` is false |
| `GET /api/v1/events/{id}/ics` | events, Read, no | | `200 text/calendar` (section 4.5) | |
| `POST /api/v1/events/{id}/comments/enumerate`, `POST /api/v1/events/{id}/comments`, `PUT`/`DELETE /api/v1/events/{id}/reactions/{key}`, `POST /api/v1/events/{id}/reactions/enumerate` | events, List / Participate / Read | Phase 2 shapes against `commentTarget` | Phase 2 | Phase 2, plus `409 EventCanceled` for new comments on canceled events (reactions and reports still allowed) |
| `POST /api/v1/me/events/enumerate` | events, List, no | `EnumerationQuery`; filters `rsvpStatus`, `upcoming`, `past` | `200 EnumerationResult<EventSummary>` (events the caller RSVPed to or hosts) | |
| `GET /api/v1/me/calendar-feed` | events, Read, no | | `200 { "url": "https://api.example.test/api/v1/calendar/cfd_...<token>.ics", "createdUtc" }` (creates one on first call) | |
| `POST /api/v1/me/calendar-feed/rotate`, `DELETE /api/v1/me/calendar-feed` | events, Write, no | | `200`, `204` | |
| `GET /api/v1/calendar/{token}.ics` | Anonymous (token verified) | | `200 text/calendar` | `404` for an unknown, rotated, or disabled token |
| `POST /api/v1/events/categories/enumerate` | events, List, yes | `EnumerationQuery` | `200 EnumerationResult<EventCategory>` | |

### 4.2 Event enumeration

Orderings: `StartAscending` (default), `StartDescending`, `CreatedDescending`, `CreatedAscending`, `DistanceAscending` (requires `near`), `Relevance` (only with `search`).

| Filter | Values |
|---|---|
| `when` | `Upcoming` (default: `endUtc` after now), `Past`, `All` |
| `startsAfterUtc`, `startsBeforeUtc` | Window on `startUtc` (in addition to the standard created window) |
| `category` | One or more category keys |
| `format` | One or more of `InPerson`, `Online`, `Hybrid` |
| `venueId`, `hostUserId`, `instructorId` | Ids |
| `near` | As venues (section 3.2); online-only events are excluded when `near` is present unless `includeOnline` is true |
| `bbox` | As venues (section 3.2: `{ south, west, north, east }` or `"s,w,n,e"`, west greater than east crosses the antimeridian, not with `near`); matches the stored event position; online-only events are excluded unless `includeOnline` is true |
| `rsvpStatus` | The caller's: `Going`, `Interested`, `Waitlisted`, `None` |
| `hasSpots` | `true`: capacity null or `spotsLeft` above 0 |
| `includeCanceled` | Default false; canceled events the caller RSVPed to always appear in `/me/events` |
| `audience`, `status` (staff: `Draft`) | |

Blocks: events hosted only by members the caller blocked are omitted (staff-hosted events are always shown, Phase 2 decision 22 spirit).

#### 4.2.1 Events map

`POST /api/v1/events/map/enumerate` mirrors the directory map (section 3.3) for events: body `{ bbox (required), zoom (0 to 22, default 2), maxResults (transport page size), continuationToken, skip, ordering, labels, labelMatch, tags, search, filters }` with the event filters `when`, `startsAfterUtc`, `startsBeforeUtc`, `category`, `format`, `venueId`, `hostUserId`, `instructorId`, `rsvpStatus`, `hasSpots`, `includeCanceled`, and `audience` (no `near`, `bbox`, or `includeOnline` in `filters`). It covers the events the caller may list (audiences, locked stubs, blocked hosts, as above) whose position lies in the viewport. While they fit in the independent `events.mapMaxFeatures` feature budget each is `{ kind: "Point", event { id, slug, title, startUtc, endUtc, timeZone, startLocal, isAllDay, format, status, category, locationName, locality, region, countryCode, venue, geo { lat, lng, precision }, spotsLeft, locked, coverThumbUrl } }`; otherwise the server groups them on the directory map grid (one cell size from the zoom) into `{ kind: "Cluster", count, lat, lng, bbox }` (one-event cells stay points). The resulting features are paged using standard continuation tokens; `totalRecords` counts generated features and scalar `matchedRecords` counts matching source events. Every feature carries a stable viewport-scoped `id`, `createdUtc` (source creation for points, Unix epoch for clusters), and empty labels/tags. A smaller transport page never changes point-versus-cluster generation.

Online events are never on the map (clients list them behind an "Include online" control with `includeOnline` on the list). A position is never more precise than the event and its venue allow: the effective visibility is the stricter of the event's `addressVisibility` and its venue's, `LocalityOnly` (or a venue the caller may not read) keeps the event off the map, and `Approximate` rounds to 2 decimals (cluster bounds too). Event staff see exact positions. Rate limited by `events.mapPerMinute` (120). Metrics `trelliskit.events.map.queries` (`mode`) and `trelliskit.events.map.duration`.

### 4.3 RSVP rules

| Rule | Value |
|---|---|
| Statuses | `Going`, `Interested`, `Waitlisted`. Deleting the RSVP is "Not going". A canceled event keeps RSVPs for history with `eventCanceled: true` |
| Capacity | Going RSVPs plus their `guestCount` must not exceed `capacity`. `guestCount` 0 to the event's `maxGuestsPerRsvp` (0 to 10; default `events.defaultMaxGuestsPerRsvp` 0) |
| Full event | `PUT` with `Going` on a full event returns `200` with `status: "Waitlisted"` and a `position` when `waitlistEnabled`, else `409 EventFull`. `Interested` never counts against capacity and never waitlists |
| Concurrency | Every RSVP write runs in one transaction that locks the event row (`SELECT ... FOR UPDATE`), recounts Going plus guests from `eventrsvps`, decides the status, writes the RSVP and the counters, and promotes from the waitlist when space opened |
| Waitlist promotion | When space opens (a Going RSVP is deleted or downgraded, guests reduced, capacity raised), the oldest `Waitlisted` RSVPs (by `waitlistedutc`, then `id`) whose party fits are promoted to `Going` in the same transaction; each promoted member gets `EventWaitlistPromoted` (in-app, push, email). Promotion stops at `startUtc` minus `events.waitlistCutoffMinutes` (60): after that, a freed spot stays open for anyone |
| Window | RSVPs are accepted from `rsvpOpensUtc` (or publish) to `rsvpClosesUtc` (or `startUtc`); outside it `409 RsvpClosed`. Deleting an RSVP is allowed until `startUtc` |
| Who | The caller must read the event and meet `rsvpAudience` (`403 RsvpNotAllowed`); muted members `403 Muted`; blocks do not prevent RSVPs |
| Privacy | `hideFromAttendeeLists` hides the member from attendee lists and previews for everyone except event staff and hosts |
| Check-in | Event staff and hosts: `POST /api/v1/admin/events/{id}/rsvps/{rsvpId}/check-in` and `.../undo-check-in`; members cannot check themselves in in Phase 4 |
| Limits | A member may hold at most `events.maxActiveRsvpsPerMember` (100) RSVPs on upcoming events |

### 4.4 Reminders and change notices

- Each event has 0 to 3 reminder offsets (`reminderOffsetsMinutes`, default `events.defaultReminderOffsetsMinutes` `[1440, 120]`, each 5 to 20160 minutes), stored as `eventreminders` rows with `dueutc = startutc - offset` and `sentutc`.
- `EventReminderWorker` (`LeasedPassWorker`, every `events.reminderIntervalSeconds` 60) claims due reminders per property (fair claims, `MULTIPROPERTY_CONTRACT.md` section 4.4) and starts a fan-out job keyed `(EventReminder, evt_x:<offset>)` (idempotent) to Going members, plus Interested members when `events.remindInterested` (false). Waitlisted members receive the first reminder only with a "you are on the waitlist" title. A reminder whose due time passed by more than `events.reminderGraceMinutes` (30) without being sent (for example the worker was down) is skipped, not sent late.
- Delivery: an `EventReminder` notification (category `Events`), push (subject to preferences, notification hours, and pause, like every push), and, for the largest offset only, an email (`event_reminder`, Optional, governed by the `Events` email preference) with the time in the event's zone, the location or a "join link available from" note, and an "Add to calendar" link.
- Editing `startUtc` recomputes the due times and clears `sentutc` for reminders not yet due. Changing time, location, or format sends `EventUpdated` to Going, Waitlisted, and Interested members (in-app, push, and email `event_updated`) unless the editor sets `notifyAttendees: false`; cosmetic edits (description, cover, labels) never notify.
- Canceling (`POST /api/v1/admin/events/{id}/cancel` with `{ reason (required 3 to 500), notifyAttendees: true }`) sets `status: Canceled`, stops reminders, keeps RSVPs, and sends `EventCanceled` (in-app, push, email `event_canceled`, Essential to RSVPed members because they planned around it).
- Publishing with `notifyMembers: true` (default false for events) runs a `NewEvent` fan-out to every member who may read the event and has the `Events` category on, in batches of `notifications.fanoutBatchSize`.

### 4.5 Calendar

- **Single event:** `GET /api/v1/events/{id}/ics` returns an RFC 5545 `VCALENDAR` with one `VEVENT`: `UID: evt_...@<api domain>`, `SEQUENCE` incremented on each time or location change, `DTSTART`/`DTEND` with `TZID` and an embedded `VTIMEZONE` for the event's zone (or `VALUE=DATE` for all-day), `SUMMARY`, `DESCRIPTION` (plain text excerpt plus the event URL), `LOCATION` (formatted address respecting `addressVisibility`), `URL`, `STATUS:CANCELLED` for canceled events, and `VALARM` blocks for the reminder offsets. The join URL is included only for Going callers after it becomes available. `Content-Disposition: attachment; filename="<slug>.ics"`.
- **Mobile:** "Add to calendar" uses `expo-calendar` (after the calendar permission prompt) to create the event in the device calendar from the same fields, storing the device event id locally so a later update can be applied with one tap.
- **Personal feed:** `GET /api/v1/calendar/{token}.ics` (anonymous; the token is `cfd_<id>.<secret>`, the secret stored hashed, bound to the property and the member) lists the member's Going and Waitlisted events from 30 days ago to `events.calendarFeedDays` (365) ahead, at most `events.calendarFeedMaxEvents` (500, fixed bound), with `Cache-Control: private, max-age=900`. It never includes join URLs or attendee data. Rotating or deleting invalidates the old URL; account deletion, ban, and losing access to events (gating) disable it. Open question 13 asks whether to build the feed in Phase 4.

### 4.6 Time zones

Staff enter local wall time plus the zone (defaulting to the venue's `timeZone`, else the editor's device zone). The backend converts with the IANA database in .NET (`TimeZoneInfo.FindSystemTimeZoneById` with IANA ids), rejects nonexistent local times in a spring-forward gap (`400 InvalidSchedule` with a hint), and resolves ambiguous fall-back times to the first occurrence. Payloads carry UTC plus `timeZone`; clients format with `Intl.DateTimeFormat` in the event's zone. `isAllDay` events render as dates without times.

### 4.7 Admin routes (event staff unless noted)

| Method and path | Body | Success |
|---|---|---|
| `POST /api/v1/admin/events/enumerate` | `EnumerationQuery` (admin maximum); member filters plus `status` including `Draft`, `hostUserId` | `200 EnumerationResult<AdminEvent>` (Event plus `openReportCount`, `checkedInCount`, `reminderStatus`) |
| `POST /api/v1/admin/events` | Event fields (`title`, `summary`, `description`, `category`, `format`, `startLocal`, `endLocal`, `timeZone`, `isAllDay`, `location { venueId \| name, address, addressVisibility, geo? }`, `online { platformLabel, joinUrl }`, `capacity`, `maxGuestsPerRsvp`, `waitlistEnabled`, `rsvpAudience`, `rsvpOpensUtc`, `rsvpClosesUtc`, `attendeeVisibility`, `externalUrl`, `coverMediaId`, `hostUserIds`, `instructorIds`, `reminderOffsetsMinutes`, `commentsEnabled`, `audience`, `labels`, `tags`) | `201 Event` (`Draft`) |
| `GET`, `PUT /api/v1/admin/events/{id}` | `PUT` adds `notifyAttendees` | `200 Event` |
| `POST /api/v1/admin/events/{id}/publish` | `{ "notifyMembers": false }` | `200 Event` |
| `POST /api/v1/admin/events/{id}/cancel` | Section 4.4 | `200 Event` |
| `POST /api/v1/admin/events/{id}/duplicate` | `{ "startLocal", "timeZone"? }` (copies everything except RSVPs, comments, and reactions, as a Draft) | `201 Event` |
| `DELETE /api/v1/admin/events/{id}` | | `204` (Drafts only; published events are canceled, never deleted, so RSVP history stays) |
| `POST /api/v1/admin/events/{id}/rsvps/enumerate` | `EnumerationQuery`; filters `status`, `checkedIn` | `200 EnumerationResult<AdminRsvp>` |
| `POST /api/v1/admin/events/{id}/rsvps/{rsvpId}/promote`, `.../move-to-waitlist`, `.../remove`, `.../check-in`, `.../undo-check-in` | `{ reason?, notifyMember: true }` | `200 AdminRsvp` |
| `GET /api/v1/admin/events/{id}/rsvps/export.csv` | | CSV (display name, handle, status, guests, check-in) |
| `POST /api/v1/admin/events/categories` and CRUD, `PUT .../order` (`SystemAdmin`) | | Category management; deleting a category in use retires it |
| `GET /api/v1/admin/metrics/events?days=30` | | `{ eventsByDay, rsvpsByDay, checkInRate, noShowRate, byCategory: { objects (max 20), totalRecords, enumerate } }` |

Hosts use the same `PUT` with the fields section 1.1 allows (others `403 Forbidden`) and the check-in routes for their events. Audit actions: `event.create`, `event.update`, `event.publish`, `event.cancel`, `event.duplicate`, `event.delete`, `event.rsvp.promote`, `event.rsvp.remove`, `event.checkIn`, `event.category.*`.

### 4.8 Reports and moderation

`targetType: "Event"` reports and comments on events are stored with `scopetype: "Events"`; `EventManager` sees them in the queue and may hide, unhide, remove, and restore comments on events (never events or comments authored by a `SystemAdmin`). Events themselves can be hidden or removed by `SystemAdmin` and `EventManager` through moderation actions; a removed event is canceled and its RSVPed members get `EventCanceled` without the moderation reason.

### 4.9 Public events (anonymous)

`POST /api/v1/public/events/enumerate` (upcoming, `Public`, Published, `StartAscending`, `maxResults` at most 20) and `GET /api/v1/public/events/{id}` return `PublicEvent` (EventSummary plus `description`, without `myRsvp`, `viewer`, attendee data, or join URLs) while `events.publicListingsEnabled` (false) is on, in every model, cacheable for 60 seconds and rate limited per IP. They power a marketing page "Upcoming events" with a "Join to RSVP" call to action.

---

## 5. Library

### 5.1 Member routes

| Method and path | Feature, action, signed-out | Body | Success | Errors |
|---|---|---|---|---|
| `POST /api/v1/series/enumerate` (and `GET /api/v1/series`) | library, List, yes | `EnumerationQuery`; orderings `SortOrderAscending` (default), `TitleAscending`, `PublishedDescending`, `Relevance`; filters `levels`, `followedOnly`, `progress` (`NotStarted`, `InProgress`, `Completed`), `audience` | `200 EnumerationResult<SeriesSummary>` | |
| `GET /api/v1/series/{idOrSlug}` | library, Read, yes | | `200 Series` | `404` |
| `POST /api/v1/series/{id}/lessons/enumerate` | library, List, yes | `EnumerationQuery`; ordering `PositionAscending` (default) | `200 EnumerationResult<LessonSummary>` (locked stubs per 0.4) | |
| `POST /api/v1/lessons/enumerate` (and `GET`) | library, List, yes | `EnumerationQuery`; orderings `PublishedDescending` (default), `TitleAscending`, `DurationAscending`, `Relevance`; filters `seriesIds` (max 50), `levels`, `kind`, `hasResources`, `minDurationSeconds`, `maxDurationSeconds`, `progress`, `audience` | `200 EnumerationResult<LessonSummary>` | |
| `GET /api/v1/lessons/{idOrSlug}` | library, Read, yes | | `200 Lesson` | `404` |
| `PUT /api/v1/lessons/{id}/progress` | library, Participate, no | Section 5.5 | `200 LessonProgress` | `403`, `404`, `429` |
| `DELETE /api/v1/lessons/{id}/progress` | library, Participate, no | | `204` | |
| `POST /api/v1/lessons/{id}/complete`, `.../uncomplete` | library, Participate, no | | `200 LessonProgress` | |
| `POST /api/v1/me/library/continue/enumerate` | library, List, no | `EnumerationQuery` (`LastWatchedDescending` only; default `maxResults` 10) | `200 EnumerationResult<{ lesson: LessonSummary, progress: LessonProgress }>` (in progress, not completed, lesson still openable) | |
| `POST /api/v1/me/library/progress/enumerate` | library, List, no | `EnumerationQuery`; filters `completed`, `seriesId` | `200 EnumerationResult<LessonProgress>` | |
| `PUT`, `DELETE /api/v1/series/{id}/follow` | library, Participate, no | | `200 { isFollowed }` | |
| `POST /api/v1/lessons/{id}/downloads` | library, Participate, no | Section 5.9 | `201 LessonDownload` | `403 DownloadsNotAllowed`, `409 DownloadLimitReached` |
| `POST /api/v1/me/library/downloads/enumerate`, `POST /api/v1/me/library/downloads/{id}/renew`, `DELETE /api/v1/me/library/downloads/{id}` | library, List / Participate | | `200`, `200 LessonDownload`, `204` | |
| Lesson comments and reactions: `POST /api/v1/lessons/{id}/comments/enumerate`, `POST /api/v1/lessons/{id}/comments`, `PUT`/`DELETE /api/v1/lessons/{id}/reactions/{key}`, `POST .../reactions/enumerate` | library, List / Participate / Read | Phase 2 shapes | Phase 2 | Phase 2 plus gating of 0.4 |
| `GET /api/v1/library/levels`, `POST /api/v1/library/levels/enumerate` | library, List, yes | `EnumerationQuery`; `SortOrderAscending` (default), `LabelAscending`, created orderings; search, dates, labels (`All`/`Any`), tags and continuation. Admin counterpart supports `filters.includeRetired` | `200 EnumerationResult<LibraryLevelView>` from the configured source. Custom dates are the level timestamps; profile-derived dates are the source field timestamps. At most 50 custom levels or 200 field options; member/admin page bounds and count settings apply | |

### 5.2 Series and lesson rules

| Rule | Value |
|---|---|
| Ordering | Series by `sortOrder` (admin drag to reorder, assigned 10, 20, 30, ...); lessons by `position` inside a series (admin reorder). A lesson belongs to exactly one series in Phase 4 (open question 15) |
| Limits | At most `library.maxSeries` (500) series and `library.maxLessonsPerSeries` (200) lessons per series, so per-series lesson enumerations stay small; clients still follow continuation tokens |
| Slugs | 2 to 80 of `a-z 0-9 -`, unique per property for series and per series for lessons; changeable with redirects |
| Publishing | A lesson is listed when it and its series are `Published`. `Scheduled` lessons publish at `publishAtUtc` through `LibraryPublishWorker` (every minute) once their video is Ready; publishing sends `NewLessonInSeries` to the series' followers (section 8) and indexes the lesson for search and assistants |
| Audience | Section 0.4. Narrowing a series' audience offers (default yes) to narrow its lessons that were equal to the old audience |
| Comments | Per lesson `commentsEnabled` (default `library.commentsEnabledByDefault` true); Phase 2 comment features (one attachment, edit history, mentions) apply |
| Archive | Archived series and lessons leave every list and search; members with progress keep "Continue watching" and direct links until `library.archivedPlaybackDays` (365) |

### 5.3 Video through the existing media pipeline

Lesson video uses the Phase 2 pipeline with no new transcoding path:

1. `SystemAdmin` creates an upload session (`POST /api/v1/media/uploads`, `kind: Video`; admin limits, so `media.maxAdminVideoSeconds` 0 means uncapped), uploads directly to the video source bucket (multipart above the threshold, resumable with `upload-urls`), and completes.
2. `MediaProcessingWorker` submits the MediaConvert job (AWS) or runs ffmpeg (Docker), producing the HLS ladder, poster, and blurhash exactly as for feed video. Originals are kept (Phase 2 decision 8).
3. The admin attaches the `mediaId` to the lesson (`PUT /api/v1/admin/lessons/{id}` with `videoMediaId`); the lesson shows `Processing` until the media is Ready. `durationSeconds` comes from the media.
4. Playback uses the media grant cookie (`POST /api/v1/media/grant`), issued only to callers who may play at least one lesson; the lesson's media inherits the lesson's audience. A `Public` lesson's video is copied to the public paths (`/public/video/{id}/`, Phase 2 section 5.10) so visitors can play it without a cookie. Web plays with `hls.js` (`withCredentials`), Safari natively; mobile with `expo-video` and the cookie headers; markers render on the scrubber and as a chapter list; captions load as WebVTT text tracks.
5. Replacing a lesson's video keeps the old media for `moderation.retainRemovedDays` (restorable), resets nobody's progress, and clamps positions past the new duration.

**New media kind `Document`.** Phase 4 extends `MediaKindEnum` with `Document` for lesson resources and captions: content types `application/pdf` (magic bytes `%PDF-`) and `text/vtt` (must start with `WEBVTT`; parsed and size-checked), up to `media.maxDocumentBytes` (25 MB), `SystemAdmin` only in Phase 4 (members cannot upload documents), stored in the source bucket and served unchanged from the images bucket path `media/{id}/file.pdf` or `.vtt` after validation (PDF metadata is not stripped; staff are told so in the upload dialog). PDFs are served with `Content-Disposition: attachment`, `Content-Type: application/pdf`, `X-Content-Type-Options: nosniff`, and `Content-Security-Policy: sandbox` so a PDF can never run script in the media origin. Access follows the media grant cookie like images. A `Public` lesson's documents are copied to the public paths.

### 5.4 Resources and captions

Admin routes manage `resources` and `captions` as part of the lesson (`PUT` replaces the bounded lists). `Link` URLs are `https`, checked against the link preview SSRF policy only when a preview is requested; `Reference` text supports a linked printed work without the engine naming any product. Resources are included in search and in assistant grounding by title only (document bodies are not parsed in Phase 4; open question 18).

### 5.5 Progress tracking

`PUT /api/v1/lessons/{id}/progress`:

```json
{ "positionSeconds": 151.2, "playedSeconds": 14.8, "clientUpdatedUtc": "2026-11-20T18:02:00Z", "sessionId": "c6f1..." }
```

| Rule | Value |
|---|---|
| Cadence | Clients send every `library.progressIntervalSeconds` (15) while playing, on pause, seek end, exit, app background, and when playback ends |
| `positionSeconds` | 0 to the lesson duration (clamped); becomes `positionSeconds` (resume point). A position within the last `library.resumeTailSeconds` (10) of the video resumes from 0 next time |
| `maxPositionSeconds` | The furthest position reached (monotonic) |
| `playedSeconds` | Seconds actually played since the last update (0 to 2 times the interval; larger values are clamped, so skipping does not count); summed into `watchedSeconds`, capped at 3 times the duration |
| `percent` | `round(100 * min(maxPositionSeconds, watchedSeconds) / duration)`, so seeking to the end does not complete a lesson |
| Completion | `completed` becomes true when `percent` reaches `library.completionThresholdPercent` (90), or by `POST .../complete` (for articles, or members who want to mark it). Completion is sticky: later progress updates never clear it; `uncomplete` does |
| Ordering | Updates with `clientUpdatedUtc` older than the stored value are ignored (`200` with the stored progress), so a mobile offline queue replayed out of order cannot move the resume point backwards; `maxPositionSeconds`, `watchedSeconds`, and completion merge monotonically regardless |
| Offline | Mobile queues progress while offline (downloads, section 5.9) and replays it on reconnect with the original `clientUpdatedUtc` (at most 7 days old; older entries are dropped) |
| Series progress | `completedLessons` and `percent` (completed published lessons divided by published lessons) are computed in the series read from `lessonprogress` in one grouped query; not stored |
| Privacy | Progress is visible only to the member and to staff in aggregate (completion rates per lesson); no staff route shows an individual member's watch history except the member's own export (open question 17) |

### 5.6 Browse and search

The library home on every client shows "Continue watching" (5.1), followed series, and series by level. Search over the library uses `ISearchIndex` (section 7): series title, summary, description; lesson title, summary, description (articles in full), marker labels, resource titles, caption text when present (open question 18); results are filtered as the caller and ordered by `Relevance`. Label and tag filters, level chips, and duration filters are offered on every library search surface (enumeration specification section 3).

### 5.7 Following series

`PUT /api/v1/series/{id}/follow` follows; a member automatically follows a series when they complete its first lesson if `library.autoFollowOnProgress` (true). Followers receive `NewLessonInSeries` (aggregated per series while unread). Follows are bounded by `library.maxSeries`.

### 5.8 Promote to the feed

`POST /api/v1/lessons/{id}/promote` and `POST /api/v1/events/{id}/promote` reuse Phase 3 `PromotionService` exactly: the feed card has `kind: "PromotedLesson"` or `"PromotedEvent"`, `source: { type, id, title }`, `commentTarget` the lesson or event (one conversation), the audience only narrows, `notifyMembers` default true for lessons and false for events, `409 AlreadyPromoted`, unpromote through `POST .../unpromote`. Hiding or removing the source hides the card (Phase 3 section 8.4 coupling).

### 5.9 Downloads on mobile

Offline viewing is off by default and is a property decision (open question 16).

| Rule | Value |
|---|---|
| Switches | `library.downloads.enabled` (false), per lesson `downloadable` (true), kill switch `libraryDownloads`, platform availability (iOS and Android apps only; the website never offers downloads) |
| Who | Callers who may play the lesson; in gated models only members (sample lessons are streamed, not downloaded) |
| Rendition | When downloads are enabled, the media pipeline adds one progressive MP4 rendition per lesson video (`download_720p.mp4`, H.264 and AAC, at most 720p, from the same MediaConvert job template or ffmpeg run) so the app does not need offline HLS support |
| License | `POST /api/v1/lessons/{id}/downloads` with `{ "deviceId": "...", "platform": "Ios" \| "Android" }` returns `201 { id: "ldl_...", lessonId, url, urlExpiresUtc, licenseExpiresUtc, sizeBytes, sha256 }`. `url` is a short-lived signed URL (`library.downloads.urlMinutes`, 30) for the rendition; the license lasts `library.downloads.licenseDays` (30) and the app renews it online (`.../renew`) when it is less than 7 days from expiry and the member still qualifies |
| Storage on device | The app stores the file in its private sandbox (`expo-file-system` document directory, excluded from iCloud backup), never in the photo library or shared storage, and indexes it by license id |
| Enforcement | The app deletes downloads when the license expires without renewal, on sign-out, on account deletion, and when renewal returns `403` (membership ended, lesson unpublished or archived, kill switch). At most `library.downloads.maxActivePerMember` (25) licenses and `library.downloads.maxDevicesPerMember` (3) devices |
| Honest limits | Files are not DRM protected. A determined user with a jailbroken or rooted device can copy a downloaded file. The admin console says so where downloads are enabled, and the property's terms of use should forbid redistribution |
| Telemetry | Downloads issued, renewed, expired, and revoked per platform (no per-member data in metrics) |

### 5.10 Admin routes (`SystemAdmin`)

| Method and path | Success |
|---|---|
| `POST /api/v1/admin/series/enumerate`, `POST /api/v1/admin/series`, `GET`, `PUT`, `DELETE /api/v1/admin/series/{id}` (delete only when empty: `409 SeriesNotEmpty`), `POST .../{id}/publish`, `.../archive`, `.../unarchive`, `PUT /api/v1/admin/series/order` | Series management |
| `POST /api/v1/admin/lessons/enumerate` (filters `seriesId`, `status` including `Draft`, `Scheduled`, `Processing`, `Archived`, `hasVideo`, `mediaStatus`), `POST /api/v1/admin/series/{id}/lessons`, `GET`, `PUT`, `DELETE /api/v1/admin/lessons/{id}` (soft delete; progress kept for export until purge), `POST .../{id}/publish`, `.../schedule` (`{ publishAtUtc }`), `.../archive`, `.../unarchive`, `.../move` (`{ seriesId, position }`), `PUT /api/v1/admin/series/{id}/lessons/order` (`{ lessonIds }`, bounded by `library.maxLessonsPerSeries`) | Lesson management |
| `GET /api/v1/admin/metrics/library?days=30` | `{ playsByDay, completionsByDay, activeLearners, topLessons: { objects (max 20), totalRecords, enumerate }, completionRateBySeries: { objects (max 20), totalRecords, enumerate } }` (aggregates only) |
| `GET`, `PUT /api/v1/admin/library/settings` | Level source, downloads, completion threshold |
| `POST /api/v1/admin/library/levels` and CRUD (`levelSource: Custom` only) | Level management |

The admin Library page shows upload progress and media processing status per lesson (polling `POST /api/v1/media/enumerate` with `filters.ids`, collecting `objects` across continuation pages as composers do), a drag-ordered lesson list per series, a scheduler, the marker editor (pick a frame, type a label), resources and captions editors, and audience controls limited by the access model. Audit actions: `series.*`, `lesson.*` (`create`, `update`, `publish`, `schedule`, `archive`, `delete`, `move`, `order`).

### 5.11 Assistant knowledge and tools

- `AI_CONTRACT.md` section 7.1 already lists `Lesson` and `Event` as knowledge `targetType` values "when Phase 4 lands". Phase 4 makes them available and adds `Series` and `Venue`. What is indexed: series and lesson title, summary, description (articles in full), marker labels, resource titles and `Reference` text, caption text when present; events: title, summary, description, category label, start in the event zone, locality (never join URLs or attendee data); venues: name, affiliation, summary, locality, programs, visitor policy (never contact details for non-`Public` venues). `KnowledgeIndexWorker` stores each chunk with the object's audience; retrieval re-checks every hit as the chatting member (section 7.3 of the AI contract), so a member never receives grounding from a lesson they cannot open. Progress, RSVPs, downloads, and submissions are never knowledge.
- The tools `searchLibrary`, `findEvents`, and the directory tool become available. The AI contract names the directory tool `findGyms` with a `programs` argument; Phase 4 implements it under the neutral name `findVenues` (`query`?, `near`? (lat, lng, radiusKm), `programs`?, `maxResults`?) and keeps `findGyms` as an accepted alias in `tools.allowed` for configurations already saved. Every tool runs as the member and returns bounded results with app paths (`/venues/{id}`, `/events/{id}`, `/lessons/{id}`).

### 5.12 Public sample lessons (anonymous)

`POST /api/v1/public/lessons/enumerate` (`Public` Published lessons and the series they belong to, `PublishedDescending`, `maxResults` at most 20) and `GET /api/v1/public/lessons/{id}` return `PublicLesson` (LessonSummary plus `description`, `markers`, captions, and `Public` media URLs; no `viewer`, progress, reactions of the caller, comments, resources, neighbors, staff fields, or the media `ownerUserId`; every public route omits the uploader of media) while `library.publicSamplesEnabled` (true) is on, in every model, cacheable for 60 seconds and rate limited per IP. In `FullyGated` this is the only way visitors see lessons.

---

## 6. Comments, reactions, and the one-conversation rule

### 6.1 Hosts

`CommentHost` gains `Event` and `Lesson`: `AllowMedia` and `AllowVideo` from the feature's settings (`events.commentMedia`, `library.commentMedia`, default true), `ModeratorInScope` from `EventManager` (events) or `SystemAdmin` (lessons), `CommentCount` from the target, and the author for notifications (the event's first host, or none for lessons, which are authored by the property). Comment routes under the `events` and `lessons` segments use the enumeration forms of Phase 2 (`POST .../comments/enumerate`) and the target-independent `/api/v1/comments/{id}` routes.

### 6.2 Rules

- `PostComment` notifications for events go to hosts; for lessons, to nobody (no member author) unless the lesson has `notifyStaffOnComment` (staff receive `PostComment` in the `Library` category).
- Comments on canceled events: new comments `409 EventCanceled`; existing ones stay readable.
- A lesson's comment tree is the conversation for its promoted feed card too.

### 6.3 Moderation scope

Reports on comments resolve to their target's scope: `Events` (moderated by `EventManager` and `SystemAdmin`) or `Library` (moderated by `SystemAdmin`, and by `Moderator` holders with `resourceId: "*"` when `library.forumModeratorsModerateComments` is true, default true, so properties without a large staff can reuse their forum moderators). Promoted cards resolve to the source, as in Phase 3.

---

## 7. Global search additions

`SearchService.Types` gains `Venue`, `Instructor`, `Event`, `Series`, and `Lesson`. Writes enqueue index updates through the existing background indexer (`LeasedPassWorker` job `search_index`), with documents keyed `<type>:<id>` in the property's Verbex index, carrying the object's labels and tags so enumeration label and tag filters map to Verbex filters. Global search results (`POST /api/v1/search/enumerate`) add these `type` values with `path` (`/venues/{id}`, `/instructors/{id}`, `/events/{id}`, `/series/{id}`, `/lessons/{id}`) and every hit passes the object's own visibility check as the caller (locked stubs are not search results). Search stays its own feature (`search`); gating `library` keeps members-only lessons out of a non-member's results without gating search. Event results carry `event { startUtc, timeZone, isCanceled, isPast, recurrenceId, recurrenceSummary, occurrenceCount }`: the occurrences of one recurring series collapse into one result that opens the next upcoming occurrence that is not canceled (else the next canceled one, else the latest past one), and canceled events stay in the results marked `isCanceled` so clients label them.

---

## 8. Notifications

### 8.1 Types

| `type` | `category` | Trigger | Recipient | Aggregated | `title` example | `path` |
|---|---|---|---|---|---|---|
| `SubmissionDecision` | `Account` | A directory submission was approved or rejected | Submitter | No | "Your suggested edit to Example Club Downtown was approved" | `/venues/{id}` or `/me/directory/submissions` |
| `SubmissionOverdue` | `Moderation` | A pending submission passed `directory.reviewTargetHours` | Directory staff | No | "A directory submission is past the 72 hour review target" | admin path |
| `InstructorLinked` | `Account` | Staff linked you to an instructor entry, or your claim was approved | Member | No | "You are now listed as a {instructor} at Example Club Downtown" | `/instructors/{id}` |
| `VenueReview` (as built) | `Replies` | A member reviewed a venue you are a linked instructor of | Linked instructors | No | "New 5-star review of Example Club Downtown" | `/venues/{id}?review={reviewId}` |
| `VenueReviewReply` (as built) | `Replies` | A linked instructor replied to your review | Reviewer | No | "Example Coach replied to your review of Example Club Downtown" | `/venues/{id}?review={reviewId}` |
| `EventReminder` | `Events` | A reminder offset came due | Going (and optionally Interested) members | No | "Autumn meetup starts in 2 hours" | `/events/{id}` |
| `EventUpdated` | `Events` | Time, location, or format changed | Going, Waitlisted, Interested | Yes, per event while unread | "Autumn meetup moved to 7 pm" | `/events/{id}` |
| `EventCanceled` | `Events` | Event canceled | RSVPed members | No | "Autumn meetup was canceled" | `/events/{id}` |
| `EventWaitlistPromoted` | `Events` | Promoted from the waitlist | Member | No | "A spot opened: you are going to Autumn meetup" | `/events/{id}` |
| `NewEvent` | `Events` | Event published with `notifyMembers` | Members who may read it | No | "New event: Autumn meetup" | `/events/{id}` |
| `NewLessonInSeries` | `Library` | A lesson published in a followed series | Followers | Yes, per series while unread | "2 new lessons in Getting started" | `/series/{id}` (single: `/lessons/{id}`) |

`EventReminder`, `SubmissionDecision` were reserved by Phase 2. Existing types extend: `PostComment`, `CommentReply`, `Mention`, `Reaction` on event and lesson comments; `ModerationAction` for directory entries, events, and comments. No notifications across blocks, never to the actor. Notification `context` adds `venueId`, `instructorId`, `submissionId`, `eventId`, `seriesId`, `lessonId`.

### 8.2 New preference categories

| `category` | inApp | push | email | Editable |
|---|---|---|---|---|
| `Events` | true | true | true (reminders, changes, cancellations) | inApp, push, email |
| `Library` | true | true | false | inApp, push, email (opt-in, delivered as a weekly digest of new lessons in followed series, `library_digest`, reusing the Phase 3 digest pattern) |

`EventCanceled` and `EventWaitlistPromoted` are sent by email even when the `Events` email preference is off, because they change plans the member made (Essential, open question 12).

---

## 9. Administrative email

New templates in `EmailTemplateCatalog` (HtmlPageBuilder classes under `Services/Email/Templates`, admin-overridable subject, preheader, heading, and paragraphs with token validation, previews, and test sends, delivered by `EmailDeliveryWorker` with idempotency keys):

| Template key | Category | Recipient | Tokens |
|---|---|---|---|
| `event_reminder` | Optional (`Events`) | Member | `eventTitle`, `startLocal`, `timeZone`, `locationText`, `joinNote`, `eventLink`, `calendarLink`, `unsubscribeLink` |
| `event_updated` | Optional (`Events`) | Member | `eventTitle`, `changeSummary`, `startLocal`, `locationText`, `eventLink`, `calendarLink`, `unsubscribeLink` |
| `event_canceled` | Essential | Member | `eventTitle`, `startLocal`, `reason`, `eventLink` |
| `event_waitlist_promoted` | Essential | Member | `eventTitle`, `startLocal`, `locationText`, `eventLink`, `calendarLink` |
| `directory_submission_decision` | Essential | Submitter | `targetName`, `decision`, `reviewNote`, `link` |
| `library_digest` | Optional (`Library`) | Member | `count`, `items`, `link`, `unsubscribeLink` |
| `staff_directory_submission` | Staff | Configured recipients (`DirectoryModerator` holders by default) | `submissionType`, `targetName`, `submitterName`, `adminLink` |
| `staff_event_rsvp_digest` | Staff | Event staff and hosts who opt in | `eventTitle`, `going`, `waitlisted`, `changes`, `adminLink` (at most one per event per `events.staffRsvpDigestHours`, 24) |

All carry the property brand from configuration, render at 360 px in light and dark clients, escape member values, and Optional ones carry `List-Unsubscribe` and one-click unsubscribe for their category.

---

## 10. Account lifecycle

| Event | Phase 4 effect |
|---|---|
| Account deletion | RSVPs deleted (counters recomputed, waitlist promotion runs); calendar feed disabled; progress, follows, and download licenses deleted (mobile deletes files on next contact); submissions anonymized (submitter shown as "Deleted member") and pending ones withdrawn; instructor links removed (the instructor entry stays); comments follow Phase 2 erasure; events they host lose them as a host (events stay) |
| Export (`GET /api/v1/me/export`) | Adds `rsvps`, `lessonProgress`, `seriesFollows`, `downloads` (metadata), `directorySubmissions`, `instructorLinks`, `calendarFeed` (created date only) |
| Ban | Sessions revoked; RSVPs on upcoming events removed (waitlist promotion runs); calendar feed disabled; downloads revoked at next renewal |
| Mute (global) | `403 Muted` on RSVP, submissions, comments; progress keeps working |
| Membership lapse (gated models) | Reads become locked stubs; RSVPs on upcoming members-only events stay but the member sees "Membership required to attend" and reminders stop; downloads stop at next renewal |

---

## 11. Rate limits

Per account unless noted; existing limiter keyed `<propertyId>:<scope>:<caller>`; `SystemAdmin` exempt.

| Route group | Limit | Setting |
|---|---|---|
| Directory submissions | 10 per day; new accounts 2 per day | `directory.submissionsPerDay`, `directory.newAccountSubmissionsPerDay` |
| Place search (geocode) | 30 per minute, 300 per day | `directory.geocodePerMinute`, `directory.geocodePerDay` |
| Map enumerate | 120 per minute | `directory.mapPerMinute` |
| Events map enumerate | 120 per minute | `events.mapPerMinute` |
| Tile proxy | 1200 per minute per account, 600 per minute per IP signed out | `directory.tilesPerMinute` |
| RSVP writes | 30 per minute, 200 per day | `events.rsvpsPerMinute`, `events.rsvpsPerDay` |
| ICS and calendar feed | 60 per hour per token or account | `events.icsPerHour` |
| Progress updates | 12 per minute per lesson, 600 per hour | `library.progressPerMinute`, `library.progressPerHour` |
| Download licenses | 20 per day | `library.downloads.perDay` |
| Comments | Phase 2 comment limits (shared budget) | `comments.*` |
| Reads | Phase 2 `rateLimits.memberReadsPerMinute` | |
| Public routes (anonymous) | 120 per minute per IP | `rateLimits.publicFeedPerMinute` |
| Public landing pages, sitemap, robots.txt (section 18) | 600 per minute per IP | `rateLimits.publicPagesPerMinute` |

---

## 12. Settings

All are added to `SettingsRegistry.cs`; community behavior settings are `Overridable` per property (`MULTIPROPERTY_CONTRACT.md` section 5.3); integration settings with secrets are `Property` scope and encrypted. "Live" applies on save.

| Key | Type | Default | Apply | Scope |
|---|---|---|---|---|
| `directory.maxPrograms` | Integer 1 to 200 | 50 | Live | Overridable |
| `directory.maxDescriptionLength` | Integer 100 to 50000 | 5000 | Live | Overridable |
| `directory.maxPhotos` | Integer 0 to 30 | 10 | Live | Overridable |
| `directory.maxSocialLinks` | Integer 0 to 20 | 8 | Live | Overridable |
| `directory.defaultRadiusKm` | Decimal 0.1 to 500 | 50 | Live | Overridable |
| `directory.maxRadiusKm` | Decimal 1 to 2000 | 500 | Live | Overridable |
| `directory.maxPendingSubmissionsPerMember` | Integer 1 to 100 | 5 | Live | Overridable |
| `directory.reviewTargetHours` | Integer 1 to 720 | 72 | Live | Overridable |
| `directory.duplicateNameSimilarity` | Decimal 0.5 to 1 | 0.8 | Live | Overridable |
| `directory.duplicateDistanceKm` | Decimal 0 to 10 | 0.5 | Live | Overridable |
| `directory.submissionDefaultAddressVisibility` | Enum `Exact`, `Approximate`, `LocalityOnly` | `Approximate` | Live | Overridable |
| `directory.publicListingsEnabled`, `directory.publicShowsContact` | Boolean | false, false | Live | Property |
| `directory.reviewsEnabled`, `directory.memberMediaEnabled` (as built, sections 3.12 and 3.13) | Boolean | true, true | Live | Property |
| `directory.reviewMinLength`, `directory.reviewMaxLength`, `directory.reviewReplyMaxLength` | Integer 0 to 1000, 100 to 10000, 100 to 10000 | 10, 2000, 2000 | Live | Overridable |
| `directory.reviewsPerDay`, `directory.newAccountReviewsPerDay` | Integer 1 to 1000 | 10, 2 | Live | Platform |
| `directory.geocoder.provider` | Enum `None`, `Nominatim`, `AwsLocation`, `Http` | `None` | Live | Property |
| `directory.geocoder.baseUrl`, `.apiKey` (secret), `.authHeaderName`, `.email`, `.placeIndexName`, `.region`, `.requestsPerSecond` | Strings, Integer 1 to 50 | empty, 1 | Live | Property |
| `directory.geocoder.cacheDays` | Integer 1 to 3650 | 90 | Live | Property |
| `directory.map.provider` | Enum `None`, `MapLibre`, `PlatformNative` | `None` | Live | Property |
| `directory.map.styleUrl`, `.darkStyleUrl`, `.publicKey`, `.attribution` | String | empty | Live | Property |
| `directory.map.tileProxy` | Boolean | false | Live | Property |
| `directory.map.proxyKey` | Secret | empty | Live | Property |
| `directory.map.defaultCenterLat`, `.defaultCenterLng`, `.defaultZoom`, `.maxZoom`, `.maxFeatures` | Decimal, Integer 0 to 22, Integer 50 to 1000 | 0, 0, 2, 18, 500 | Live | Property |
| `directory.submissionsPerDay`, `.newAccountSubmissionsPerDay`, `.geocodePerMinute`, `.geocodePerDay`, `.mapPerMinute`, `.tilesPerMinute` | Integer | 10, 2, 30, 300, 120, 1200 | Live | Platform |
| `events.maxCategories` | Integer 1 to 200 | 50 | Live | Overridable |
| `events.maxDescriptionLength` | Integer 100 to 50000 | 10000 | Live | Overridable |
| `events.maxDurationDays` | Integer 1 to 60 | 14 | Live | Overridable |
| `events.maxHosts` | Integer 1 to 20 | 10 | Live | Overridable |
| `events.defaultMaxGuestsPerRsvp` | Integer 0 to 10 | 0 | Live | Overridable |
| `events.defaultReminderOffsetsMinutes` | String list of integers (at most 3) | `1440,120` | Live | Overridable |
| `events.remindInterested` | Boolean | false | Live | Overridable |
| `events.reminderIntervalSeconds` | Integer 15 to 600 | 60 | Live | Platform |
| `events.reminderGraceMinutes` | Integer 5 to 240 | 30 | Live | Overridable |
| `events.waitlistCutoffMinutes` | Integer 0 to 1440 | 60 | Live | Overridable |
| `events.joinUrlRevealMinutes` | Integer 0 to 1440 | 15 | Live | Overridable |
| `events.maxActiveRsvpsPerMember` | Integer 1 to 1000 | 100 | Live | Overridable |
| `events.calendarFeedEnabled` | Boolean | true | Live | Overridable |
| `events.calendarFeedDays`, `events.calendarFeedMaxEvents` | Integer 30 to 730, 50 to 1000 | 365, 500 | Live | Overridable |
| `events.publicListingsEnabled` | Boolean | false | Live | Property |
| `events.staffRsvpDigestHours` | Integer 1 to 168 | 24 | Live | Overridable |
| `events.commentMedia` | Boolean | true | Live | Overridable |
| `events.rsvpsPerMinute`, `events.rsvpsPerDay`, `events.icsPerHour` | Integer | 30, 200, 60 | Live | Platform |
| `events.mapPerMinute`, `events.mapMaxFeatures` | Integer 1 to 10000, 50 to 1000 | 120, 500 | Live | Platform |
| `library.maxSeries`, `library.maxLessonsPerSeries` | Integer 1 to 5000, 1 to 1000 | 500, 200 | Live | Overridable |
| `library.maxMarkersPerLesson`, `library.maxResourcesPerLesson` | Integer 0 to 200, 0 to 50 | 50, 10 | Live | Overridable |
| `library.maxArticleLength` | Integer 1000 to 200000 | 50000 | Live | Overridable |
| `library.completionThresholdPercent` | Integer 50 to 100 | 90 | Live | Overridable |
| `library.progressIntervalSeconds` | Integer 5 to 120 | 15 | Live | Overridable |
| `library.resumeTailSeconds` | Integer 0 to 120 | 10 | Live | Overridable |
| `library.autoFollowOnProgress` | Boolean | true | Live | Overridable |
| `library.commentsEnabledByDefault`, `library.commentMedia`, `library.forumModeratorsModerateComments` | Boolean | true, true, true | Live | Overridable |
| `library.archivedPlaybackDays` | Integer 0 to 3650 | 365 | Live | Overridable |
| `library.publicSamplesEnabled` | Boolean | true | Live | Property |
| `library.downloads.enabled` | Boolean | false | Live | Property |
| `library.downloads.licenseDays`, `.urlMinutes`, `.maxActivePerMember`, `.maxDevicesPerMember`, `.perDay` | Integer | 30, 30, 25, 3, 20 | Live | Overridable |
| `library.progressPerMinute`, `library.progressPerHour` | Integer | 12, 600 | Live | Platform |
| `media.maxDocumentBytes` | Integer 1048576 to 104857600 | 26214400 | Live | Platform |

---

## 13. Telemetry

New instruments on the `TrellisKit` meter (low-cardinality labels only; never ids, names, or coordinates), all with the `trelliskit.property` label:

| Instrument | Type | Labels |
|---|---|---|
| `trelliskit.directory.venues` | Counter | `action` (create, update, delete, verify, close, archive), `outcome` |
| `trelliskit.directory.reviews` (as built) | Counter | `action` (create, update, delete, reply, reply_delete), `outcome` (ok, rejected, conflict, filtered, limited) |
| `trelliskit.directory.venue_media` (as built) | Counter | `action` (add, delete, remove), `kind` (image, video), `outcome` |
| `trelliskit.directory.submissions` | Counter | `type`, `action` (submit, approve, reject, withdraw), `outcome` |
| `trelliskit.directory.submissions.pending` | Gauge | |
| `trelliskit.directory.geocode` | Counter | `provider`, `outcome` (hit, cache_hit, miss, error), `purpose` (write, search) |
| `trelliskit.directory.geocode.duration` | Histogram (s) | `provider` |
| `trelliskit.directory.map.queries` | Counter | `mode` (points, clusters) |
| `trelliskit.directory.query.duration` | Histogram (s) | `kind` (near, bbox, map, search) |
| `trelliskit.directory.tiles` | Counter | `outcome` (proxied, cache_hit, error) |
| `trelliskit.events.events` | Counter | `action` (create, publish, update, cancel, duplicate, delete) |
| `trelliskit.events.rsvps` | Counter | `action` (going, interested, waitlisted, remove, promote, check_in), `outcome` |
| `trelliskit.events.reminders` | Counter | `outcome` (sent, skipped_late), `channel` |
| `trelliskit.events.calendar` | Counter | `kind` (ics, feed), `outcome` |
| `trelliskit.events.map.queries` | Counter | `mode` (points, clusters) |
| `trelliskit.events.map.duration` | Histogram (s) | |
| `trelliskit.library.plays` | Counter | `platform` (web, ios, android), `kind` (video, article) |
| `trelliskit.library.progress` | Counter | `outcome` (accepted, stale, clamped) |
| `trelliskit.library.completions` | Counter | `source` (threshold, manual) |
| `trelliskit.library.downloads` | Counter | `action` (issue, renew, revoke, expire), `platform` |
| `trelliskit.library.lessons.published` | Counter | `kind` |
| `trelliskit.public_pages.requests` (section 18) | Counter | `kind` (Venue, Event, Profile, Sitemap, Robots, None), `outcome` (Ok, Redirect, NotFound, PassThrough) |
| `trelliskit.public_pages.slug_changes` (section 18) | Counter | `kind` |

Existing counters gain values: `trelliskit.comments`, `trelliskit.reactions`, `trelliskit.reports`, `trelliskit.moderation.actions` (`target_type` event, lesson, venue, instructor), `trelliskit.notifications.created` (new types), `trelliskit.email.*` (new templates), `trelliskit.media.*` (`kind` document). Spans: `directory.geocode`, `directory.map`, `directory.submission.approve`, `events.rsvp`, `events.reminders`, `library.progress`, `job EventReminderWorker`, `job LibraryPublishWorker`, `job GeocodeWorker`, with ids allowed as span attributes. Grafana: Directory, Events, and Library rows in the Community folder (submissions backlog, geocoder errors and latency, RSVPs per day, reminder outcomes, plays and completions per day, download licenses).

---

## 14. Data model

Migrations 50 to 59 are reserved for Phase 4 in `PostgresqlSchema.cs` (PostgreSQL only): **50 "Directory"**, **51 "Events"**, **52 "Library"**, **53 "Document media and download renditions"**, 54 to 59 held for fixes during the phase. Every new table has `propertyid TEXT NOT NULL` as its first column and an entry in `PropertyTables`; every index and unique constraint starts with `propertyid` (implied below); all SQL goes through the property-bound driver (`DatabaseDriverBase.ForProperty`) so `PropertyScopeGuard` accepts it. No JSON columns: every list is a child table. Labels and tags use the shared `labels` and `tags` tables with the target types of section 0.1.

### Tables

| Table | Key columns |
|---|---|
| `directoryconfig` | PK (`propertyid`), `venuelabel`, `venuelabelplural`, `instructorlabel`, `instructorlabelplural`, `instructorrankfieldkey`, `submissionsenabled`, `claimsenabled`, `sourcehash`, `updatedutc` |
| `directoryprograms` | `id` PK, `key`, `label`, `sortorder`, `isretired`, `createdutc`, `updatedutc` |
| `venues` | `id` PK, `slug`, `name`, `affiliation`, `summary`, `description`, `line1`, `line2`, `locality`, `region`, `postalcode`, `countrycode`, `addressvisibility`, `lat` NULL, `lng` NULL, `geosource`, `geocodedutc`, `timezone`, `phone`, `email`, `websiteurl`, `scheduletext`, `visitorpolicy`, `visitorpolicynote`, `isverified`, `verifiedutc`, `verifiedbyuserid`, `status`, `audience`, `instructorcount`, `openreportcount`, `moderationstate`, `createdbyuserid`, `deletedutc`, `createdutc`, `updatedutc` |
| `venueprograms` | PK (`propertyid`, `venueid`, `programkey`) |
| `venuesociallinks` | `id` PK, `venueid`, `kind`, `url`, `position` |
| `venuephotos` | PK (`propertyid`, `venueid`, `mediaid`), `position` |
| `instructors` | `id` PK, `slug`, `displayname`, `bio`, `photomediaid`, `rankkey`, `ranklevel`, `affiliation`, `linkeduserid` NULL, `linkedutc`, `websiteurl`, `status`, `audience`, `venuecount`, `moderationstate`, `deletedutc`, `createdutc`, `updatedutc` |
| `instructorsociallinks` | `id` PK, `instructorid`, `kind`, `url`, `position` |
| `venueinstructors` | `id` PK, `venueid`, `instructorid`, `role`, `createdutc` |
| `directorysubmissions` | `id` PK, `type`, `status`, `targettype`, `targetid` NULL, `targetupdatedutc` (stale check), `submitteruserid`, `note`, `reviewernote`, `revieweruserid`, `decidedutc`, `dueutc`, `overduenotifiedutc`, `createdutc`, `updatedutc` |
| `directorysubmissionfields` | `id` PK, `submissionid`, `field`, `proposedvalue`, `currentvalue`, `position` (multi-value fields as one row per value with `position`) |
| `geocoderesults` | `id` PK, `queryhash`, `provider`, `lat`, `lng`, `timezone`, `formatted`, `expiresutc`, `createdutc` |
| `eventconfig` | PK (`propertyid`), `sourcehash`, `updatedutc` |
| `eventcategories` | `id` PK, `key`, `label`, `color`, `sortorder`, `isretired`, `createdutc`, `updatedutc` |
| `events` | `id` PK, `slug`, `title`, `summary`, `description`, `categorykey`, `format`, `status`, `startutc`, `endutc`, `timezone`, `isallday`, `venueid` NULL, `locationname`, `line1`, `line2`, `locality`, `region`, `postalcode`, `countrycode`, `addressvisibility`, `lat`, `lng`, `onlineplatformlabel`, `onlinejoinurl`, `capacity` NULL, `maxguestsperrsvp`, `waitlistenabled`, `rsvpaudience`, `rsvpopensutc`, `rsvpclosesutc`, `attendeevisibility`, `externalurl`, `covermediaid`, `commentsenabled`, `goingcount`, `guestcount`, `interestedcount`, `waitlistcount`, `checkedincount`, `commentcount`, `reactiontotal`, `openreportcount`, `icssequence`, `audience`, `moderationstate`, `canceledutc`, `cancelreason`, `canceledbyuserid`, `publishedutc`, `createdbyuserid`, `deletedutc`, `createdutc`, `updatedutc` |
| `eventhosts` | PK (`propertyid`, `eventid`, `userid`), `position` |
| `eventinstructors` | PK (`propertyid`, `eventid`, `instructorid`), `position` |
| `eventrsvps` | `id` PK, `eventid`, `userid`, `status`, `guestcount`, `hidefromattendeelists`, `waitlistedutc`, `promotedutc`, `checkedinutc`, `checkedinbyuserid`, `createdutc`, `updatedutc` |
| `eventreminders` | `id` PK, `eventid`, `offsetminutes`, `dueutc`, `sentutc` NULL, `skippedutc` NULL |
| `calendarfeeds` | `id` PK, `userid`, `secrethash`, `isenabled`, `lastfetchedutc`, `createdutc`, `rotatedutc` |
| `libraryconfig` | PK (`propertyid`), `levelsource`, `levelfieldkey`, `sourcehash`, `updatedutc` |
| `librarylevels` | `id` PK, `key`, `label`, `color`, `sortorder`, `isretired` |
| `series` | `id` PK, `slug`, `title`, `summary`, `description`, `covermediaid`, `status`, `sortorder`, `audience`, `lessoncount`, `totaldurationseconds`, `publishedutc`, `archivedutc`, `deletedutc`, `createdbyuserid`, `createdutc`, `updatedutc` |
| `serieslevels` | PK (`propertyid`, `seriesid`, `levelkey`) |
| `lessons` | `id` PK, `seriesid`, `position`, `slug`, `title`, `summary`, `description`, `kind`, `videomediaid` NULL, `durationseconds`, `status`, `publishatutc`, `audience`, `downloadable`, `commentsenabled`, `notifystaffoncomment`, `commentcount`, `reactiontotal`, `openreportcount`, `moderationstate`, `publishedutc`, `archivedutc`, `deletedutc`, `createdbyuserid`, `createdutc`, `updatedutc` |
| `lessonlevels` | PK (`propertyid`, `lessonid`, `levelkey`) |
| `lessonmarkers` | `id` PK, `lessonid`, `seconds`, `label` |
| `lessonresources` | `id` PK, `lessonid`, `kind`, `title`, `mediaid` NULL, `url` NULL, `text` NULL, `position` |
| `lessoncaptions` | `id` PK, `lessonid`, `language`, `label`, `mediaid`, `position` |
| `lessonprogress` | `id` PK, `lessonid`, `userid`, `positionseconds`, `maxpositionseconds`, `watchedseconds`, `completed`, `completedutc`, `completionsource`, `lastwatchedutc`, `clientupdatedutc`, `createdutc`, `updatedutc` |
| `seriesfollows` | `id` PK, `seriesid`, `userid`, `source` (`Explicit`, `Progress`), `isoptedout`, `createdutc` |
| `lessondownloads` | `id` PK, `lessonid`, `userid`, `deviceid`, `platform`, `licenseexpiresutc`, `revokedutc`, `revokereason`, `lastrenewedutc`, `createdutc` |
| `venuereviews` (migration 93, as built) | `id` PK, `venueid`, `authoruserid`, `rating`, `body`, `replybody`, `replyuserid`, `replyinstructorid`, `replyutc`, `replyeditedutc`, `editedutc`, `openreportcount`, `moderationstate`, `moderatedutc`, `moderatedbyuserid`, `moderationreason`, `deletedutc`, `createdutc`, `updatedutc`; `ux_venuereviews_author (venueid, authoruserid) WHERE deletedutc IS NULL` UNIQUE |
| `venuemedia` (migration 93, as built) | `id` PK, `venueid`, `mediaid` (unique), `uploaderuserid`, `kind`, `openreportcount`, `moderationstate`, `moderatedutc`, `moderatedbyuserid`, `moderationreason`, `deletedutc`, `createdutc`, `updatedutc`; `venues` gains `ratingcount`, `ratingtotal`, `mediacount` (recomputed in the write transaction) |

Changes to existing tables: `mediaattachments.kind` accepts `Document`; `mediavariants` gains the `download_720p` rendition; `reports.scopetype` gains `Directory`, `Events`, `Library`; `feedposts.kind` gains `PromotedLesson`, `PromotedEvent`; `comments.targettype` and `reactions.targettype` accept `Event` and `Lesson`; `TargetTypeEnum` gains the types of section 0.1.

### Indexes and unique constraints (all prefixed by `propertyid`)

| Table | Index | Purpose |
|---|---|---|
| `directoryprograms`, `eventcategories`, `librarylevels` | `ux_* (lower(key))` UNIQUE | Stable keys |
| `venues` | `ux_venues_slug (lower(slug)) WHERE deletedutc IS NULL` UNIQUE; `idx_venues_geo (lat, lng) WHERE status = 'Active' AND deletedutc IS NULL AND moderationstate = 'None'`; `idx_venues_name (lower(name), id)`; `idx_venues_locality (countrycode, lower(region), lower(locality))`; `idx_venues_needspin (geosource) WHERE geosource = 'None'` | Slugs, spatial prefilter, name order, place filters, admin task |
| `venueprograms` | `idx_venueprograms_program (programkey, venueid)` | Program filter |
| `instructors` | `ux_instructors_slug` UNIQUE; `ux_instructors_linkeduser (linkeduserid) WHERE linkeduserid IS NOT NULL AND deletedutc IS NULL` UNIQUE; `idx_instructors_name (lower(displayname), id)` | One entry per member |
| `venueinstructors` | `ux_venueinstructors (venueid, instructorid)` UNIQUE; `idx_venueinstructors_instructor (instructorid)` | Links |
| `directorysubmissions` | `idx_submissions_queue (status, createdutc, id)`; `idx_submissions_submitter (submitteruserid, status)`; `ux_submissions_claim (targetid, submitteruserid) WHERE type = 'ClaimInstructor' AND status = 'Pending'` UNIQUE | Queue, limits, one claim |
| `geocoderesults` | `ux_geocoderesults (provider, queryhash)` UNIQUE; deployment-wide `idx_geocoderesults_expires (expiresutc)` (prune only) | Cache |
| `events` | `ux_events_slug` UNIQUE; `idx_events_upcoming (startutc, id) WHERE status = 'Published' AND deletedutc IS NULL AND moderationstate = 'None'`; `idx_events_venue (venueid, startutc)`; `idx_events_geo (lat, lng, startutc)`; `idx_events_category (categorykey, startutc)` | Lists, filters |
| `eventrsvps` | `ux_eventrsvps (eventid, userid)` UNIQUE; `idx_eventrsvps_waitlist (eventid, waitlistedutc, id) WHERE status = 'Waitlisted'`; `idx_eventrsvps_user (userid, status)` | One per member, promotion, "my events" |
| `eventreminders` | `ux_eventreminders (eventid, offsetminutes)` UNIQUE; `idx_eventreminders_due (dueutc) WHERE sentutc IS NULL AND skippedutc IS NULL` | Worker claims |
| `calendarfeeds` | `ux_calendarfeeds_user (userid)` UNIQUE | One per member |
| `series` | `ux_series_slug` UNIQUE; `idx_series_order (status, sortorder, id)` | |
| `lessons` | `ux_lessons_slug (seriesid, lower(slug)) WHERE deletedutc IS NULL` UNIQUE; `idx_lessons_position (seriesid, position, id)`; `idx_lessons_published (publishedutc DESC, id DESC) WHERE status = 'Published' AND deletedutc IS NULL AND moderationstate = 'None'`; `idx_lessons_schedule (publishatutc) WHERE status IN ('Scheduled', 'Processing')`; `idx_lessons_media (videomediaid)` | Order, lists, publish worker, media completion |
| `lessonprogress` | `ux_lessonprogress (userid, lessonid)` UNIQUE; `idx_lessonprogress_continue (userid, lastwatchedutc DESC, id DESC) WHERE completed = false` | Progress, "Continue watching" |
| `seriesfollows` | `ux_seriesfollows (seriesid, userid)` UNIQUE; `idx_seriesfollows_user (userid)` | Fan-out |
| `lessondownloads` | `idx_lessondownloads_user (userid, revokedutc, licenseexpiresutc)`; `ux_lessondownloads (userid, deviceid, lessonid) WHERE revokedutc IS NULL` UNIQUE | Limits |

### Write rules

Phase 2 section 15 write rules apply: counters (`goingcount`, `guestcount`, `waitlistcount`, `interestedcount`, `instructorcount`, `venuecount`, `lessoncount`, `totaldurationseconds`, `commentcount`, `reactiontotal`) are recomputed from source rows in the same transaction; RSVP writes lock the event row (`FOR UPDATE`); worker claims use the conditional `UPDATE` claim pattern and `jobleases`; search index updates are enqueued in the same transaction as the write.

---

## 15. Backend structure

**Reused building blocks (no reimplementation):**

| Existing component | Phase 4 use |
|---|---|
| `MediaService`, `MediaPipeline`, `MediaStorageService`, `MediaUrlBuilder`, `IObjectStore`, `IVideoTranscoder`, `IImageProcessor`, `MediaProcessingWorker`, `MediaCleanupWorker`, MediaConvert webhook | Venue and instructor photos, event covers, series covers, lesson video (HLS), the new `Document` kind, the download rendition, public sample copies, retained bucket on removal |
| `IMediaDeliverySigner` and `POST /media/grant` | Playback cookies; short-lived signed URLs for download renditions |
| `CommentService` with `CommentHost`, `ReactionService` with `ReactionTarget`, `MentionService`, `MarkdownNormalizer` | Comments, reactions, and mentions on events and lessons; descriptions |
| `NotificationService`, `NotificationCatalog`, `NotificationPreferenceService`, `NotificationHoursRule`, `NotificationFanoutWorker`, `PushDeliveryWorker`, `IPushSender` | New types and categories, reminder and new-lesson fan-outs, push |
| `ReportService`, `ModerationService`, `IModerationTargetExtension`, `ModerationDueWorker`, `ContentPurgeWorker` | Reports and actions on venues, instructors, events, lessons, and their comments; new scopes |
| `WordFilterService`, `BlockService` | Every member-written field; blocks on hosts, comments, attendee lists |
| `MetadataService`, `EnumerationService` | Labels, tags, and enumeration on every new object and list |
| `SearchService`, `ISearchIndex`, `VerbexSearchIndex`, `LeasedPassWorker` (`search_index`) | Global search types and library and directory search |
| `AccessDecider`, `AccessRoutes`, `ContentAudienceRule`, `ContentAccessService`, `AccessPolicyService` | Route declarations, audience, locked stubs, denials |
| `KillSwitchCatalog`, `FeatureFlagEvaluator` | `directory`, `events`, `library` switches (existing) and `libraryDownloads` (new) |
| `ProfileFieldService`, `ProfileValueService`, `UserSummaryService` | Instructor rank from a `Rank` field, library levels from a profile field, `isStaff` |
| `PromotionService` (Phase 3) | Promote lessons and events to the feed |
| `EmailService`, `EmailTemplateCatalog`, `EmailRenderer`, `PropertyEmailSender`, `EmailDeliveryWorker`, `ForumDigestService` pattern | New templates and the library digest |
| `PropertySettingsService`, `SecretBoxProvider` | Geocoder and tile proxy secrets per property |
| `ILinkPreviewAddressPolicy` | SSRF policy for the geocoder `Http` and `Nominatim` base URLs and lesson links |
| `AuditService`, `RateLimitService`, `SlidingWindowRateLimiter`, `CsvWriter`, `AccountExportWorker` | Audit, limits, CSV exports, account export additions |
| `KnowledgeIndexWorker`, assistant tool registry (AI) | Library, event, and venue knowledge and tools |
| `PublicFeedRoutes` and `PublicThreadRoutes` patterns | Public sample lessons, events, venues |

**New:** services `DirectoryService`, `VenueService`, `InstructorService`, `DirectorySubmissionService`, `GeoQueryService` (bounding box, Haversine, clustering SQL), `GeocodingService` over `IGeocoder` (`NoneGeocoder`, `NominatimGeocoder`, `AwsLocationGeocoder`, `HttpGeocoder` in `Core/Integrations/Implementations`), `MapTileProxyService`, `EventService`, `RsvpService`, `EventReminderService`, `CalendarService` (ICS writer), `SeriesService`, `LessonService`, `LessonProgressService`, `LessonDownloadService`, `LibraryConfigService`. Route registrars: `VenueRoutes`, `InstructorRoutes`, `DirectoryMapRoutes`, `DirectorySubmissionRoutes`, `AdminDirectoryRoutes`, `PublicDirectoryRoutes`, `EventRoutes`, `CalendarRoutes`, `AdminEventRoutes`, `PublicEventRoutes`, `SeriesRoutes`, `LessonRoutes`, `MyLibraryRoutes`, `AdminLibraryRoutes`, `PublicLessonRoutes`; `CommentRoutes`, `ReactionRoutes`, `ReportRoutes`, `ModerationRoutes` gain the new targets. Workers on `LeasedPassWorker`: `EventReminderWorker`, `LibraryPublishWorker`, `GeocodeWorker`, `GeocodeCachePruneWorker`, `SubmissionDueWorker`, `LibraryDigestWorker`, `DownloadLicensePruneWorker`. Every route is declared in `AccessRoutes.cs` and covered by the cross-property isolation suite.

Touchstone suites: `DirectorySuite` (CRUD, slugs, programs, verify, close), `GeoQuerySuite` (radius, bbox, antimeridian, poles, clustering bounds, visibility of `Approximate` and `LocalityOnly`), `GeocoderSuite` (fake providers, cache, manual override, failure path, SSRF), `SubmissionSuite` (types, duplicates, review edits, stale approval, claims, limits), `EventSuite` (schedule validation, DST gaps, formats, publish, cancel, duplicate), `RsvpSuite` (capacity with guests, concurrency with parallel requests, waitlist promotion order, cutoff, window), `EventReminderSuite` (fixed clock, offsets, reschedule, grace skip, idempotent fan-out), `CalendarSuite` (ICS fields, sequence, feed token rotation), `LibrarySuite` (series and lesson lifecycle, scheduling with Processing media, ordering, archive), `LessonProgressSuite` (monotonic merge, stale updates, completion threshold, skip resistance), `LessonDownloadSuite`, `DocumentMediaSuite` (PDF and VTT validation, headers), `Phase4AccessSuite` (every model, gated and not, locked stubs, samples, public routes), `Phase4SearchSuite`, `Phase4NotificationSuite`.

---

## 16. Client notes

**Shared (`shared/src`):** `ApiClient` methods for every route above (for example `enumerateVenues`, `getVenue`, `enumerateDirectoryMap`, `enumerateEventMap`, `geocodePlace`, `createDirectorySubmission`, `enumerateMySubmissions`, `withdrawSubmission`, `enumerateInstructors`, `getInstructor`, `enumerateEvents`, `getEvent`, `rsvpEvent`, `cancelRsvp`, `enumerateAttendees`, `getEventIcsUrl`, `getCalendarFeed`, `rotateCalendarFeed`, `enumerateSeries`, `getSeries`, `enumerateSeriesLessons`, `enumerateLessons`, `getLesson`, `putLessonProgress`, `completeLesson`, `enumerateContinueWatching`, `followSeries`, `createLessonDownload`, `renewLessonDownload`, plus admin and public methods). Helpers: `directoryTerms(property)` for the configured labels, `formatEventTime(event, viewerZone)`, `roundCoordinate(value, places)` (used before sending `near`), a progress reporter that throttles to `progressIntervalSeconds` and computes `playedSeconds`, level and program lookups from `GET /api/v1/property`. App paths: `/directory`, `/venues/{id}`, `/instructors/{id}`, `/events`, `/events/{id}`, `/library`, `/series/{id}`, `/lessons/{id}` (website prefixes `/app`).

**Website:** Directory page with list and map side by side (MapLibre GL JS from the property's map configuration; list only when the provider is `None`), filters (programs, visitor policy, verified, labels, tags, search) in the URL, place search box, "Use my location" (rounded), cluster zoom, venue and instructor detail pages, "Suggest an edit", "Add a {venue}", and "This is me" (claim) flows with the duplicate check step. Events: upcoming list and calendar month view, filters, detail with RSVP control (going, interested, waitlist position), attendees, add to calendar (`.ics` link), calendar feed settings, comments. Library: home with "Continue watching", series grid by level, series page with lesson list and progress, lesson player (hls.js with credentials, markers, captions, resume, keyboard shortcuts, playback speed), resources, comments, next lesson. Admin pages: Directory (venues, instructors, submissions queue with diff view and pin editor, programs, import, settings), Events (table, editor with local time and zone, RSVPs, check-in, categories, metrics), Library (series, lessons, uploads, ordering, scheduler, markers, resources, captions, settings, metrics). Marketing pages may show public events, venues, and sample lessons when enabled.

**Mobile and tablet:** the Map tab (plan section 7.1) uses `@maplibre/maplibre-react-native` or `react-native-maps` per configuration, with a list toggle, filter bottom sheet (programs, labels, tags), "near me" behind the `expo-location` foreground permission with a pre-prompt explanation, cluster zoom, venue and instructor screens, submission and claim screens with `expo-image-picker` photos. Events screens with RSVP, `expo-calendar` add to calendar, reminders arriving as pushes (deep links through Expo Router). Library tab: "Continue watching" rail, series and lesson screens, `expo-video` with grant cookie headers, markers, captions, background progress reporting with an offline queue, Downloads screen (when enabled) with storage usage, license expiry, and delete; downloads pause on the `libraryDownloads` kill switch. Tablet: `SplitView` with list and map side by side for the directory, series list and lesson detail for the library, event list and detail for events. Every list offers label and tag filters (enumeration specification section 3).

**As built: map-first search.** Venue search and event search open on the map whenever the property configures a map style (website) or a map provider (mobile) and it loads; otherwise the list views above stay. The map starts at the last viewport (website: the URL `at=lat,lng,zoom`, then the last viewport kept in the browser, then `directory.map.defaultCenter` and `defaultZoom`), shows server points and clusters (`POST /api/v1/directory/map/enumerate` or `POST /api/v1/events/map/enumerate`; the website refreshes them for the visible area as the map moves, mobile for the searched area so the sheet and the markers always agree; tapping a cluster zooms in), and offers a floating "Search this area" control once the map moves, which re-queries the results list with `filters.bbox` (website URL `bbox=s,w,n,e`, so links and back and forward work). The results list sits next to the map (a bottom sheet on narrow screens and phones, draggable on mobile) and stays in sync with the markers (hover or select a row to highlight its marker, and the reverse). An advanced search pane holds text search, place lookup (geocode, which moves the map and searches there), "Use my location" (rounded to 2 places), and every filter: venues programs, visitor policy, verified, include closed, labels, and tags; events when, date range, category, format, has spots, RSVP status, labels, tags, and "Include online" (online-only events have no position, so they appear only in the list, with `includeOnline`). The Map tab keeps its Places and Events segment.

**As built: public listing images.** Images of content signed-out visitors may see are copied to the public media paths (`/public/media/{id}/`, the same copies public sample posts use, served without a media grant) and withdrawn when the content stops being public; media of members-only, draft, archived, or moderated content never reaches them. Event covers are public while some event using the cover is `Public`, not moderated, `Published` or `Canceled`, and not deleted (decided per cover, because the occurrences of a recurring series share one), and are synced on create, edit, publish, delete, and moderation. Series covers follow `Public` and `Published`; venue and instructor photos follow `Public` and live (not deleted, archived, or moderated), synced on every directory change; lesson media and public sample threads keep their existing rules. Media that finishes processing after its content was published is copied once it is Ready. Signed-out callers (the public routes and anonymous browsing of the member routes) get the public URLs; members keep the member paths. Media dropped from public content leaves the public paths at once.

---

## 17. Delivery order

1. Migrations 50 to 53, enums, target types, configuration sections and seeding, data access with `propertyid` scoping, `AccessRoutes` declarations, kill switch key `libraryDownloads`.
2. Library: series and lessons (admin first), video attachment through the media pipeline, `Document` media, member reads, progress, "Continue watching", comments and reactions, admin Library page; website and mobile library screens.
3. Events: CRUD, publish, RSVPs with capacity and waitlist, reminders worker, change and cancel notices, ICS, admin Events page; client events screens.
4. Directory: venues and instructors CRUD, spatial queries, map enumeration and clustering, geocoder providers and cache, map configuration, submissions and claims, admin Directory page; client directory and map screens.
5. Notifications, preferences, email templates, digests.
6. Search types, assistant knowledge sources and tools (`searchLibrary`, `findEvents`, `findVenues`).
7. Promotion of lessons and events to the feed; public sample routes; calendar feed; downloads (if enabled by decision).
8. Metrics, Grafana, `REST_API.md`, Postman, isolation suite coverage, simulated user testing pass.

The library goes first because it reuses the most existing machinery (media pipeline, comments) and is the most visible member value; the directory goes last because it depends on provider choices (open questions 4 to 8). Backend steps 1 and 2 unblock both clients, which build against this contract in parallel using the Postman collection and the shared `ApiClient` fakes.

---

## 18. Public landing pages (as built)

Server-rendered, crawlable pages for venues, events, and opt-in member profiles, at `/<path word>/<slug>` on the website origin, plus `/sitemap.xml` and `/robots.txt`. The path words come from the property's `publicPages` section (`docs/TRELLISKIT.md` section 2.2; engine defaults `venues`, `events`, `profiles`). Migration 94.

### 18.1 Slugs

- One rule for all three kinds (`PublicSlugRules`, mirrored by `shared/src/publicPages.js`): lowercase letters and digits in groups joined by single hyphens, 3 to 80 characters, not a reserved word (`about`, `account`, `add`, `admin`, `all`, `api`, `app`, `assets`, `auth`, `contact`, `create`, `delete`, `edit`, `embed`, `help`, `index`, `join`, `login`, `logout`, `map`, `me`, `new`, `null`, `platform`, `privacy`, `register`, `search`, `settings`, `sign-in`, `sign-out`, `sign-up`, `signin`, `signup`, `sitemap`, `support`, `terms`, `undefined`). A breach is `400 ValidationFailed` with `fieldErrors.slug`.
- Unique per property per kind, counting the previous slugs of other objects of the kind (`409 SlugInUse` with `fieldErrors.slug`). A venue and a profile may share a slug.
- Default: the name plus the locality and region (`Example Club` in `Exampleville, EX` is `example-club-exampleville-ex`), with `-2`, `-3`, ... when taken. Venues use the venue address, events the event location, profiles the display name and only the city and region a signed-out visitor may see. Recurring occurrences keep `<title>-yyyyMMdd`. Existing slugs that predate the rule stay valid until changed.
- Redirects: a changed slug keeps the previous one (`venueslugs`, `eventslugs`, `profileslugs`), the API finds the object by it, and the page answers `301` to the current slug (also for a different letter case).
- Who sets them: directory staff for venues (`PUT /api/v1/admin/venues/{id}`; linked instructors do not edit venues in the current permission model); event staff and the event's hosts for events (`PUT /api/v1/admin/events/{id}`, `slug` is now a host field); the member for the profile (`PUT /api/v1/me/public-page`); administrators for any (`PUT /api/v1/admin/members/{id}/public-page`).
- `POST /api/v1/public-pages/slugs/check` `{ kind, slug, id? }` gives `{ kind, slug, valid, available, problem, path, url }` for live validation (`problem`: `required`, `length`, `format`, `reserved`, `taken`).

### 18.2 Which objects have a page

| Kind | Has a page | In the sitemap |
|---|---|---|
| Venue | Every listed venue: not deleted, `Active`, moderation `None`, any audience (the page shows business information only) | Same |
| Event | `Public` audience, `Published` or `Canceled`, moderation `None`, not deleted. Members and SignedIn events never get one | `Published` and not yet ended. Ended events keep their page but leave the sitemap |
| Profile | The member turned it on (`profilepages.enabled`, off by default) and has a slug, and is neither banned nor deleted | Same |

There is no "hide from search" setting in the engine today; turning the page off is the member's control. Payloads carry `publicUrl` (absolute, or null): `Venue`, `VenueSummary`, `AdminVenue`, `Event`, `EventSummary`, `AdminEvent`, `PublicEvent`, the public profile (`GET /api/v1/users/{id}`), and `MeResponse.publicPage { enabled, slug, path, url }`.

### 18.3 What a page shows

- Venue: name, affiliation, summary, description, address by `addressVisibility` (Exact: street, locality, region, postal code, and 5-decimal coordinates; Approximate: locality and region with 2-decimal coordinates; LocalityOnly: locality and region, no coordinates), programs, schedule text, visitor policy and note, website and social links, phone only when `directory.publicShowsContact`, linked live instructors (name and rank, at most 20), rating average and count, and the staff photos (the venue's own media, published to the public paths when the venue has a page). Never member reviews, member media, email, or instructors' linked members.
- Event: title, category, local date and time with the zone, canceled or ended notice, summary, description, format and platform label, location by the stricter of the event's and its venue's visibility (linking the venue page when it has one), spots left, and the cover image. Never join URLs, attendees, or hosts.
- Profile: display name, avatar, rank and affiliation, city, region, and country, bio, and the profile fields shown on profiles whose visibility is `Public` (never `Members`, `Staff`, or `Private` fields, never email or phone fields, images, or history lists).
- Every page: the property's name, logo, and light and dark colors; `<title>`, meta description, canonical URL (`web.publicBaseUrl`), Open Graph and Twitter card tags (image: the first venue photo, the event cover, or the avatar, else the property logo), JSON-LD (`SportsActivityLocation`, `Event` with `eventStatus`, `eventAttendanceMode`, and `location`, `ProfilePage` with a `Person`), a call to action into the member app (`/app/venues/{id}`, `/app/events/{id}`, `/app/members/{id}`) and to join (`/join` when purchases are on, else `/sign-up`) or sign in. Every value is HTML-encoded; JSON-LD escapes `<`, `>`, and `&`. No scripts; a strict `Content-Security-Policy`. Unknown slugs get a `404` page with `noindex`.

### 18.4 Routes and the website proxy

| Method and path | Result |
|---|---|
| `GET /api/v1/public/pages/resolve/{path}/{slug}` | `200` HTML, `301` to the current slug, `404` HTML; a `path` that is not a page path answers `404` with `X-Accel-Redirect: /spa.html` |
| `GET /api/v1/public/pages/sitemap.xml` | Sitemap index: `/sitemap-pages.xml` (written by the website build) and `/sitemap-{venues,events,profiles}-{n}.xml` |
| `GET /api/v1/public/pages/sitemaps/{kind}-{n}.xml` | `urlset` with `loc`, `lastmod`, `changefreq`, `priority`; 10,000 URLs per file, at most 100 files per kind; each file is one bounded, ordered query (`LIMIT`/`OFFSET`, never more than 50,000) |
| `GET /api/v1/public/pages/robots.txt` | Allows `/`, the public media, avatar, and brand image paths; disallows `/app`, `/admin`, `/api`; names the sitemap |
| `GET`, `PUT /api/v1/me/public-page` | The member's page settings (`{ enabled?, slug? }`); turning it on without a slug uses `suggestedSlug` |
| `POST /api/v1/public-pages/slugs/check` | Live slug validation |
| `GET`, `PUT /api/v1/admin/members/{id}/public-page` | The same for administrators (audited `profile.public_page`) |

The website's nginx (`docker/www/nginx.conf`, used by the website image and the combined server image) maps `/sitemap.xml`, `/sitemap-*.xml`, and `/robots.txt` to the backend, and sends every two-segment path that is not a file (other than `/app`, `/admin`, `/api`, `/assets`, `/embed`, `/platform`) to `resolve`. The backend renders the page when the first segment is one of the property's path words and otherwise lets nginx serve the website shell through `X-Accel-Redirect`, so the website's own routes keep working whatever words a property picks. With the backend down, nginx serves the shell, the static `/sitemap-pages.xml`, and the website's `robots.txt`. The public pages are rate limited per IP (`rateLimits.publicPagesPerMinute`, default 600) and cached `public, max-age=300` (sitemaps and robots.txt `max-age=3600`). The website has no SPA venue or event landing pages, so there is no duplicate content; member app views link to the public page when `publicUrl` is set.

### 18.5 Data

| Table | Columns |
|---|---|
| `profilepages` | `propertyid`, `userid` (PK with propertyid), `enabled`, `slug` NULL (unique per property, case-insensitive), `createdutc`, `updatedutc` |
| `profileslugs` | `propertyid`, `slug` (PK with propertyid), `userid`, `createdutc` |

`eventslugs` (migration 51) now records previous event slugs. Account purges remove both tables with the property's data; deleted and banned members never get a page.

---

## Property notes (appendix)

This appendix maps the property plan of record onto configuration only. No engine code or test reads it.

| Plan intent | Expressed as |
|---|---|
| Directory and map are members only, no public map | The property's `access.model` is `FullyGated` with `directory` gated; `directory.publicListingsEnabled` stays false |
| Venues and instructors carry the property's own names for a training location and a teacher | `property.json` `directory.venueLabel`, `venueLabelPlural`, `instructorLabel`, `instructorLabelPlural` |
| The plan's list of programs offered at a venue | `property.json` `directory.programs` (keys and labels from the plan) |
| Instructors show the same ranking scheme as member profiles, and their rank history | `directory.instructorRankFieldKey` set to the property's `Rank` profile field key; rank history comes from the linked member's `History` profile field when the instructor is claimed |
| Admin-seeded directory plus member submissions through an approval queue | `directory.submissions: true`, `directory.claims: true`, staff hold `DirectoryModerator` |
| Geocoding and map tiles from the cloud provider named in the plan | `directory.geocoder.provider: AwsLocation` with a place index, and `directory.map.provider: MapLibre` with that provider's style URL (or `PlatformNative` on mobile, which the plan's mobile library choice implies) |
| Event types listed in the plan | `property.json` `events.categories`; the plan's "online live" type is `format: Online` plus a category |
| Reminders 24 hours and 2 hours before | `events.defaultReminderOffsetsMinutes: 1440,120` (the engine default) |
| Paid ticketing out of scope | `externalUrl` only |
| Lessons tagged by position, technique, and ranking level | Labels and tags for positions and techniques; `library.levelSource: ProfileField` with `library.levelFieldKey` set to the property's `Rank` field |
| A lesson may link to a chapter of the owner's printed book | A `Reference` resource (text) or a `Link` resource; the engine names no product |
| Downloadable notes | `Document` resources (PDF) |
| Lessons can be promoted to the feed | `POST /lessons/{id}/promote` |

---

## Open questions (with recommended answers)

| # | Question | Recommendation | Rationale |
|---|---|---|---|
| 1 | Should the venue and instructor directory share the `directory` feature key (switch, gate, kill switch) with the existing member profile directory, or get its own key (for example `places`)? | Share `directory` in Phase 4 | The key already exists in `features`, `gatedFeatures`, and the kill switch catalog; splitting needs a config migration and doubles admin toggles for a distinction no property has asked for. Revisit if a property wants a public venue map with a private member directory |
| 2 | When a feature is gated in `PartiallyGated`, should events and library catalogs be visible to free accounts as locked stubs (as specified) while the directory requires a membership even to list? | Yes, as specified | Locked lesson and event cards sell the membership; a list of venue names without details does not, and the directory is the easiest feature to scrape |
| 3 | Add a `LibraryEditor` role, or keep the library `SystemAdmin` only? | Keep `SystemAdmin` only in Phase 4 | The plan has one content owner; a role is cheap to add later and costs nothing to defer |
| 4 | Default geocoder for a new property? | `None` (manual pins), with `Nominatim`, `AwsLocation`, and `Http` available | No vendor lock-in or surprise bill by default; small directories work with manual pins; the property picks a provider when it has volume |
| 5 | Default map provider? | `None` (list view) until the property configures a style; recommend `MapLibre` on all clients when configured | One renderer and one style for web and mobile keeps the look consistent and the provider swappable; `PlatformNative` remains for properties that prefer native maps on phones |
| 6 | Spatial queries with plain SQL (bounding box plus Haversine) or PostGIS? | Plain SQL in Phase 4 | Fast enough for thousands of venues, needs no extension in local Docker, CI, or RDS; PostGIS can replace the query behind `GeoQueryService` later without API changes |
| 7 | Default address visibility for member-submitted venues? | `Approximate`, with reviewers able to set `Exact` | A submitted address may be someone's home; staff verify business addresses before exposing exact pins |
| 8 | Should the tile proxy be offered, given its bandwidth cost through the API? | Offer it, off by default | Some providers cannot restrict client keys; proxying is the only way to keep their key secret. Most properties will use a referrer-restricted public key |
| 9 | May a member linked to an instructor entry edit it directly? | No: edits by linked members go through `EditInstructor` submissions in Phase 4 | Self-managed listings are out of scope in the plan; review keeps the directory trustworthy. Fast-track their submissions in the queue (a "Linked member" badge) |
| 10 | Should members be able to propose events (through a queue like the directory)? | No in Phase 4; event staff and hosts create events | Moderation load and liability for member-organized meetups; revisit with real demand |
| 11 | Recurring events (weekly sessions)? | No recurrence rules in Phase 4; offer "Duplicate" and a bulk "duplicate weekly for N weeks" admin action (N at most 26) | RRULE support touches reminders, RSVPs per occurrence, ICS exceptions, and edits of one versus all occurrences; duplication covers the plan's needs |
| 12 | Should cancellation and waitlist promotion emails ignore the member's `Events` email preference (Essential)? | Yes | They change plans the member made; missing them causes real harm, and they are rare |
| 13 | Build the personal calendar subscription feed in Phase 4, or only per-event `.ics` and device calendar add? | Build it | Small effort on top of the ICS writer, and members who live in their calendars stop missing events; tokens are revocable and carry no join links |
| 14 | Do event reminders respect notification hours (suppressed outside the window) like every other push? | Yes, and send the earliest reminder by email as well | Consistency with Phase 2; the 24 hour reminder plus email covers members whose window hides the 2 hour push |
| 15 | May a lesson belong to more than one series? | No in Phase 4: one series per lesson; use "Duplicate lesson" when needed | Many-to-many complicates ordering, progress per series, and navigation; the plan does not ask for it |
| 16 | Offer offline downloads on mobile, given files are not DRM protected? | Build the capability, off by default (`library.downloads.enabled: false`); the property decides | Downloads help members train without connectivity, but true DRM (FairPlay, Widevine) is a large separate project; licensed, sandboxed, expiring files are a reasonable middle ground the owner should consciously enable |
| 17 | Should staff see individual members' viewing history? | No: aggregates only; the member sees their own in the app and export | Viewing history is personal data; aggregates answer every content question; less to disclose in the privacy policy |
| 18 | Index caption text and PDF resource text for search and assistants? | Captions yes (they are uploaded by staff and are plain text); PDF text no in Phase 4 | Captions make video lessons searchable and groundable at little cost; PDF extraction adds a parser and a security surface for modest gain |
| 19 | Completion threshold for "watched"? | 90 percent of the lesson by furthest position and actual watched time | Credits end credits and outros skipped, while seeking to the end does not count |
| 20 | Public sample lessons on by default (`library.publicSamplesEnabled: true`)? | Yes; a lesson is public only when a `SystemAdmin` sets `audience: Public`, so the default exposes nothing | Mirrors public sample posts and threads, which the marketing site uses |
| 21 | Should members be able to hide themselves from event attendee lists? | Yes (`hideFromAttendeeLists`), default visible, with the event's `attendeeVisibility` default `Attendees` | Lets members RSVP to sensitive events without broadcasting it; attendees-only lists keep plans private from the whole community |
| 22 | Reserve migrations 50 to 59 for Phase 4 as numbered (50 Directory, 51 Events, 52 Library, 53 Document media)? | Yes | Matches the per-phase ranges (Phase 3 used 20, live updates 30, AI 40 and 41) and leaves room for fixes |

---

## Resolved decisions (property owner, 2026-11-12)

These answers override the recommendations in the open questions section wherever they differ.

| # | Decision | Notes |
|---|---|---|
| 1 | Venues and instructors share the `directory` feature key | As recommended |
| 2 | **Lock all three the same way** in PartiallyGated: events, library, and the venue directory all show locked stubs to free accounts | Overrides the recommendation (directory no longer hidden entirely) |
| 3 | **Add a `LibraryEditor` role** that manages series, lessons, and media without full admin rights | Overrides the recommendation |
| 4 | Default geocoder `None` (manual pins); Nominatim, AwsLocation, and Http available | As recommended |
| 5 | Default map display is a list until a style is configured; MapLibre on all clients when configured | As recommended |
| 6 | Plain SQL spatial queries (bounding box plus Haversine), no PostGIS | As recommended |
| 7 | **Member-submitted venue addresses are exact, but nothing is published until an admin or moderator approves the submission** | Overrides the recommendation (no Approximate default) |
| 8 | Tile proxy offered, off by default | As recommended |
| 9 | Linked members edit instructor entries through reviewed submissions (fast-tracked with a "Linked member" badge) | As recommended |
| 10 | **Members may propose events through a review queue** (like directory submissions); staff approve before publication | Overrides the recommendation |
| 11 | **Full recurrence rules** (RRULE-style repeating events with per-occurrence RSVPs, reminders, ICS exceptions, and edit one or all occurrences) | Overrides the recommendation |
| 12 | Cancellation and waitlist promotion emails are essential (sent regardless of the Events email preference) | As recommended |
| 13 | Build the personal calendar subscription feed | As recommended |
| 14 | Reminder pushes respect notification hours; the earliest reminder is also emailed | As recommended |
| 15 | One series per lesson | As recommended |
| 16 | Offline downloads built, off by default | As recommended |
| 17 | **Staff can see individual members' lesson viewing history** (admin member page and per-lesson views, audited); must be disclosed in the privacy policy | Overrides the recommendation |
| 18 | **Index caption text and PDF resource text** for search and assistants, using **DocumentAtom** (github.com/documentatom/documentatom) for extracting and breaking apart content objects and **Partio** (partio.ai) for chunking and summarization; both may be part of the stack | Overrides the recommendation; verify both packages' APIs before use |
| 19 | Completion at 90 percent by furthest position and actual watched time | As recommended |
| 20 | Public sample lessons allowed (only when a staff member sets `audience: Public`) | As recommended |
| 21 | Members may hide themselves from attendee lists; default visibility `Attendees` | As recommended |
| 22 | Migrations 50 to 59 reserved for Phase 4 | As recommended |

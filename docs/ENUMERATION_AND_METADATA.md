# Enumeration and Metadata (labels and tags)

Status: binding engine specification (2026-11-11). It overrides any conflicting paging, sorting, or list shape in `PHASE1_CONTRACT.md`, `PHASE2_CONTRACT.md`, `REST_API.md`, or the plan. Every TrellisKit route, every client, and every future phase follows it.

Two rules, zero tolerance:

1. **No unbounded retrieval. Ever.** No route returns a list whose length grows with data unless it is an enumeration with an enforced maximum page size. This applies to top-level responses and to lists nested inside other payloads.
2. **Everything carries labels and tags, and every enumeration filters on them from day one.**

---

## 1. Labels and tags on every object

Every persistent object the engine exposes (users and profiles, feed posts, comments, post and comment revisions, media assets, reactions, notifications, reports, sign-up codes, redemptions, subscriptions, entitlements and comps, sessions, audit records, request history entries, and every object added in later phases: forums, threads, polls, events, gyms, trainers, library series and lessons, conversations, messages, tickets, ideas) carries:

```json
{
  "labels": ["fundamentals", "beginners", "staff:featured"],
  "tags": { "topic": "getting started", "level": "beginner", "staff:review": "pending" }
}
```

| Rule | Value |
|---|---|
| `labels` | List of strings. Order is not significant. Duplicates (case-insensitive) are removed. Maximum 50 per object. Each 1 to 64 characters, letters, digits, spaces, and `_ - . : /`, trimmed; compared case-insensitively, stored with the casing first used |
| `tags` | Dictionary of string to string. Maximum 50 keys per object. Key 1 to 64 characters (same character set as labels), compared case-insensitively. Value 0 to 512 characters |
| Staff-only metadata | Any label or tag key starting with `staff:` is visible to and editable by staff only (SystemAdmin, and moderators within their scope). It is stripped from every payload served to non-staff and ignored if a non-staff caller sends it |
| Who may edit public metadata | The owner of the object (author of a post, comment, or media; the member for their own profile) and staff. Objects without a member owner (sign-up codes, subscriptions, sessions, audit, request history) are staff-only |
| Always present | Payloads always include `labels` (possibly `[]`) and `tags` (possibly `{}`); never omitted, never null |
| Word filter | Labels and tag values from members pass through the same word filter as content |
| Revisions | Post and comment revisions (`FeedPostRevision`, `CommentRevision`) are persistent objects and carry real labels and tags: the labels and tags their post or comment had in that version, copied when the revision is stored. They are immutable snapshots, so the generic metadata routes do not edit them (`404`), but they are returned on every revision payload (staff entries only for staff) and every revision enumeration filters on them. Purging the content deletes them |

### Writing metadata

- On create and update routes of each object: optional `labels` and `tags` fields. On update, a provided `labels` replaces the whole list and a provided `tags` replaces the whole dictionary; omitted means unchanged.
- Generic routes, for objects whose update route does not exist or for bulk work:
  - `GET /api/v1/metadata/{targetType}/{targetId}` returns `{ targetType, targetId, labels, tags }` (bounded by the limits above).
  - `PUT /api/v1/metadata/{targetType}/{targetId}` with `{ labels?, tags? }` (replace semantics as above).
  - `PATCH /api/v1/metadata/{targetType}/{targetId}` with `{ addLabels?, removeLabels?, setTags?, removeTagKeys? }`.
  - Admin bulk: `POST /api/v1/admin/metadata/bulk` with `{ targetType, targetIds (max 500), addLabels?, removeLabels?, setTags?, removeTagKeys? }`.
- `targetType` values are the engine's object type names (`User`, `FeedPost`, `Comment`, `Media`, `SignupCode`, ...), the same names used for polymorphic targets elsewhere. The generic routes cover the Phase 4 and 5 types too (`Event`, `Lesson`, `Series`, `Venue`, `Instructor`, `Idea`, RSVPs, progress, submissions, and the rest listed in REST_API.md "Generic metadata routes"), with each type's owner (for example the idea author or the event proposer) and its staff role (`EventManager`, `LibraryEditor`, `DirectoryModerator`, `SupportAgent`, `Communicator`, or `SystemAdmin`).

### Discovering labels and tags (for search UX autocomplete)

All bounded enumerations (section 2):
- `POST /api/v1/labels/enumerate` with an `EnumerationQuery` plus `targetType` (optional) and `prefix` (optional): returns `{ label, count }` objects, most used first. Non-staff never see `staff:` labels, and counts only include objects they can see.
- `POST /api/v1/tags/keys/enumerate` (same, returns `{ key, count }`) and `POST /api/v1/tags/values/enumerate` (adds required `key`, returns `{ value, count }`).

### Storage

Two engine tables shared by every object type, in both providers:

- `labels`: `id`, `targettype`, `targetid`, `label`, `labelnormalized`, `createdutc`. Unique (`targettype`, `targetid`, `labelnormalized`). Index (`targettype`, `labelnormalized`, `targetid`).
- `tags`: `id`, `targettype`, `targetid`, `tagkey`, `tagkeynormalized`, `tagvalue`, `createdutc`, `updatedutc`. Unique (`targettype`, `targetid`, `tagkeynormalized`). Index (`targettype`, `tagkeynormalized`, `tagvalue`, `targetid`).

Deleting an object deletes its labels and tags in the same transaction. Enumeration filters use `EXISTS` subqueries against these tables (section 2.3), so filtering never loads unbounded sets into memory.

---

## 2. Enumeration

Every retrieval of more than one object uses `EnumerationQuery` in and `EnumerationResult<T>` out.

### 2.1 Routes

Each collection exposes:
- `POST /api/v1/{collection}/enumerate` with an `EnumerationQuery` JSON body (canonical; the only way to send labels and tags filters as structured JSON), and
- `GET /api/v1/{collection}` accepting the same fields as query parameters (convenience; same semantics, same result shape): `maxResults`, `continuationToken`, `skip`, `ordering`, `search`, `createdAfterUtc`, `createdBeforeUtc`, `labels` (repeatable: `labels=a&labels=b`), `labelMatch`, `tag.<key>=<value>` (repeatable per key), and the collection's own filters.

Nested collections follow the same pattern (`POST /api/v1/feed/{id}/comments/enumerate`, `POST /api/v1/admin/members/{id}/sessions/enumerate`, and so on).

### 2.2 `EnumerationQuery`

```json
{
  "maxResults": 25,
  "continuationToken": null,
  "skip": 0,
  "ordering": "CreatedDescending",
  "search": "welcome",
  "createdAfterUtc": null,
  "createdBeforeUtc": null,
  "labels": ["guard"],
  "labelMatch": "All",
  "tags": { "level": "beginner" },
  "filters": { "status": "Active" }
}
```

| Field | Rule |
|---|---|
| `maxResults` | Default 25. Minimum 1. Maximum 100 for member routes and 500 for admin routes (setting `enumeration.maxResultsMember` and `enumeration.maxResultsAdmin`, hard-capped at 1000 in code). Values above the maximum are clamped, never rejected and never ignored |
| `continuationToken` | Opaque string from the previous result. When present, `skip` and `ordering` from the token win. Tokens are stable under concurrent inserts (keyset over the ordering column plus id) |
| `skip` | For admin tables that jump to a page: number of records to skip (maximum 100000). Ignored when `continuationToken` is present |
| `ordering` | One of the collection's allowed values. Every collection supports `CreatedDescending` (default) and `CreatedAscending`; collections document additional values (for example `UpdatedDescending`, `NameAscending`, `Top`, `DisplayNameAscending`). Unknown values return `400 ValidationFailed` |
| `search` | Optional free text (1 to 200 characters) over the collection's documented searchable fields |
| `createdAfterUtc`, `createdBeforeUtc` | Optional time window on the collection's created column, accepted by every collection without exception (label and tag discovery: when the label or tag was applied). `createdBeforeUtc` must be after `createdAfterUtc`. Collection-specific windows, such as the feeds' `publishedAfterUtc` and `publishedBeforeUtc` (with the admin feed's aliases `fromUtc` and `toUtc`), are `filters` and apply in addition |
| `labels` | Optional. Objects must carry these labels. Maximum 20 labels in a query |
| `labelMatch` | `All` (default: every listed label) or `Any` (at least one) |
| `tags` | Optional dictionary. Objects must carry every listed key with exactly that value (case-insensitive key, case-sensitive value). A value of `*` matches any value for that key (key present). Maximum 20 tags in a query |
| `filters` | Optional collection-specific typed filters (documented per collection; unknown keys return `400 ValidationFailed`) |

Non-staff callers filtering on `staff:` labels or tag keys get `400 ValidationFailed`.

### 2.3 `EnumerationResult<T>`

```json
{
  "success": true,
  "maxResults": 25,
  "skip": 0,
  "continuationToken": "opaque-or-null",
  "endOfResults": false,
  "totalRecords": 1342,
  "recordsRemaining": 1317,
  "timestampUtc": "2026-11-11T23:00:00Z",
  "objects": [ ]
}
```

| Field | Rule |
|---|---|
| `objects` | At most `maxResults` items |
| `continuationToken` | Null when `endOfResults` is true |
| `totalRecords`, `recordsRemaining` | Exact for admin routes. For member feeds and large collections they may be omitted (null) when `enumeration.countMemberTotals` is false (default true); clients must not depend on them for paging. They are always either exact or null, never the page size: a single ranked page (member search) reports exact totals only when it holds every match and null otherwise |

### 2.4 No nested unbounded lists

Payloads never embed a list that grows with data. Detail payloads either embed a bounded preview with an explicit cap and a pointer to the full enumeration, or omit the list:

```json
"recentSessions": { "objects": [ ... at most 5 ... ], "totalRecords": 12, "enumerate": "/api/v1/admin/members/usr_x/sessions/enumerate" }
```

Single-object attribute exceptions have fixed, documented upper bounds: configuration inside `property.json` (reactions, ranks, goals), an object's own labels (max 50) and tags (max 50), and comment tree pages (bounded by the tree page limits in the Phase 2 contract and by at most 1000 comments per response across every level, with `moreChildren` on every parent whose replies were left out). A collection endpoint never qualifies for this exception merely because its source is a finite catalog. Settings, reaction catalogs, notification categories, AI catalogs, job definitions, metric definitions and pages all use section 2. Aggregate collections enumerate actual observations or groups, not a one-element wrapper around the entire collection.

### 2.5 Enforcement

- A backend test enumerates the OpenAPI document and fails if any GET route that returns an array, or an object containing an array, is not either an `EnumerationResult` or on a reviewed allow-list of the fixed-bound exceptions in 2.4.
- A backend test posts the standard created window to every `POST .../enumerate` route in the OpenAPI document and fails when a route rejects it, still returns objects for a future window, or accepts an inverted window.
- Runtime enforcement checks successful GET responses for raw arrays and verifies declared collections honor `maxResults=1` and include the full standard envelope.
- `CatalogEnumeration` applies the same validation and scoped keyset rules to independently bounded sources. Immutable declarations use `1970-01-01T00:00:00Z`; stored records use their real creation time. Growing sources page in SQL.
- A shared backend helper applies `maxResults` clamping, continuation tokens, ordering validation, label and tag `EXISTS` filters, and staff-metadata stripping, so collections cannot implement paging differently.

### 2.6 Saves collections

| Collection | Routes | Maximum | Orderings | Filters | Metadata target type |
|---|---|---|---|---|---|
| `me-saves` | `GET /api/v1/me/saves`, `POST /api/v1/me/saves/enumerate` | member maximum (`enumeration.maxResultsMember`, 100) | `CreatedDescending` (default), `CreatedAscending`, `UpdatedDescending` | `targetTypes`, `collectionId`; search covers notes | `Favorite` |
| `me-save-collections` | `GET /api/v1/me/save-collections`, `POST /api/v1/me/save-collections/enumerate` | member maximum (100) | `PositionAscending` (default), `NameAscending`, `CreatedDescending`, `CreatedAscending` | search covers names | `FavoriteCollection` |

`POST /api/v1/me/saves/state` is a fixed-bound lookup, not a collection: its `items` answer exactly the request's 1 to 100 targets. A save's `collectionIds` is bounded by `saves.maxCollectionsPerMember` (at most 500). Labels and tags of saves and collections are discoverable only by their owner (`POST /api/v1/labels/enumerate` with `targetType` `Favorite` or `FavoriteCollection`).

---

## 3. Client UX (web, mobile, tablet; member app and admin console)

Every surface that lists or searches anything provides:

- **Label filter:** a chip input with autocomplete from `POST /api/v1/labels/enumerate` (scoped to the collection's `targetType`), with an All/Any toggle.
- **Tag filter:** add `key = value` rows; key autocomplete from `tags/keys/enumerate`, value autocomplete from `tags/values/enumerate`, and a "has key" option (`*`).
- **Search box** for `search` and the collection's ordering choices.
- **Active filters visible** as removable chips, with a "Clear all".
- **Shareable state:** on the web the filters live in the URL (`?labels=guard&tag.level=beginner`); on mobile and tablet the filters persist per screen for the session.
- **Editing metadata:** wherever a member or staff member can edit an object, a labels and tags editor is available (staff see and edit `staff:` metadata; members never see it).
- **Infinite scroll or "Load more"** driven by `continuationToken`; admin tables may use page numbers via `skip`. No client ever requests "all".

On tablets, filters sit in a side panel next to results; on phones, in a bottom sheet opened from a filter button that shows the active filter count.

### 2.6 Ranks, curricula, and attendance collections

| Collection | Routes | Maximum page | Filters |
|---|---|---|---|
| `rank-awards` (`RankPromotion`) | `GET /api/v1/admin/ranks/awards`, `POST .../enumerate` | Admin (500) | `userId`, `fieldKey`, `optionKey`, `actorId`, `action`, `status`, `fromUtc`, `toUtc`, labels, tags |
| `rank-eligibility` (members, `User` labels and tags) | `GET /api/v1/admin/ranks/eligibility`, `POST .../enumerate` | Admin (500) | `fieldKey`, `optionKey`, `eligibleOnly` (pages may hold fewer than `maxResults` eligible members while `endOfResults` is false) |
| `rank-requirements` (`RankRequirement`) | `GET /api/v1/admin/ranks/requirements`, `POST .../enumerate` | Admin (500; at most 200 rows exist per Rank field) | `fieldKey`, `optionKey`, labels, tags |
| `curricula` (`Curriculum`) | `GET /api/v1/curricula`, `POST .../enumerate` | Member (100) | `fieldKey`, labels, tags |
| `admin-curricula` (`Curriculum`) | `GET /api/v1/admin/curricula`, `POST .../enumerate` | Admin (500) | `fieldKey`, `status`, `visibility`, labels, tags |
| `attendance` (`Attendance`) | `GET /api/v1/admin/attendance`, `POST .../enumerate` | Admin (500) | `targetType`, `targetId`, `occurrenceDate`, `userId`, labels, tags |
| `my-attendance` (`Attendance`) | `GET /api/v1/me/attendance`, `POST .../enumerate` | Member (100) | `targetType`, `targetId`, `occurrenceDate`, labels, tags |

Fixed-bound GET exceptions (section 2.4, on the enforcement allow-list): `GET /api/v1/me/rank` (at most 10 Rank fields, a 20-row history preview with `historyTotal`, at most 100 lineage entries), `GET /api/v1/members/{id}/rank` (at most 10 Rank fields and 100 lineage entries), and the curriculum details `GET /api/v1/curricula/{id}`, `GET /api/v1/admin/curricula/{id}`, and `GET /api/v1/admin/curricula/{id}/progress/{userId}` (at most 500 items per curriculum). Every rank, promotion, requirement, lineage entry, curriculum, item, progress row, and check-in carries labels and tags.

---

## 4. Migration of existing routes

Phase 0 and Phase 1 routes that predate this specification (request history list, admin members, subscriptions, sign-up codes, redemptions, audit, membership history, member detail nested lists, and any other list) are converted to sections 1 and 2 with their clients. Phase 2 routes are built to this specification from the start. Until conversion is complete the plan tracks it as an open item; nothing is marked done while an unbounded route remains.

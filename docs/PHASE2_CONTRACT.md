# Phase 2 Contract: Feed and Engagement

> **Enumeration contract:** every collection follows `docs/ENUMERATION_AND_METADATA.md`: `EnumerationQuery`, `EnumerationResult`, bounded pages, standard ordering and filters, and labels/tags on collection objects. That specification and the implemented route details in `REST_API.md` are authoritative.


This is the binding contract between the backend, the website, and the mobile app for Phase 2. Every team codes against it exactly. It is an engine contract: TrellisKit implements it for every property (`docs/TRELLISKIT.md`). Property-specific values (reactions, names, domains, profile labels) come from the property's `property.json`; examples use the neutral sample property (Example Community, `example.test`). The property owner's Phase 2 decisions are recorded in section 19 and are already applied throughout. When the implementation is complete, `REST_API.md` and the Postman collection are updated to match, and this file stays as the design record. Plan references: the property's plan of record, sections 4.5, 4.6, 4.11 (notifications, moderation), 4.12, 4.13, 5, 6.2, 6.3, 7, 11. Phase 2 builds on `docs/PHASE1_CONTRACT.md`; nothing in Phase 1 changes unless this document says so explicitly.

Conventions are unchanged: routes under `/api/v1`, camelCase JSON, string enums, ISO 8601 UTC timestamps, errors as `{ "error", "message", "fieldErrors" }`, `X-Client` and `X-Client-Version` on every app request, access tokens as `Authorization: Bearer <accessToken>`.

---

## 0. New conventions in Phase 2

### 0.1 Standard enumeration

Member and admin collections accept `EnumerationQuery` and return `EnumerationResult<T>`. Use the canonical `POST /{collection}/enumerate` with a JSON query; the corresponding GET route accepts the same fields as query parameters. Collection-specific filters are nested under `filters` in POST bodies and sent as query parameters in GET requests. Admin jump navigation uses `skip`; mobile scrolling follows opaque `continuationToken` values.

| Parameter | Rule |
|---|---|
| `continuationToken` | Opaque token from the preceding page. Omit for the first page; a token overrides `skip`. Keep the query's ordering and filters unchanged |
| `maxResults` | Page size, clamped to the collection's configured limit and hard cap. Each route states any lower default or cap |
| `skip` | Optional initial offset, 0 to 100000, for table navigation |
| `ordering`, `search`, `createdAfterUtc`, `createdBeforeUtc`, `labels`, `labelMatch`, `tags`, `filters` | Standard query fields; accepted ordering and filter names are declared by the collection |

Example terminal `EnumerationResult`:

```json
{ "success": true, "maxResults": 20, "skip": 0, "continuationToken": null, "endOfResults": true, "totalRecords": 0, "recordsRemaining": 0, "timestampUtc": "2026-11-23T00:00:00Z", "objects": [] }
```

A terminal page has a null continuation token. Tokens are scoped to the collection, property, ordering, filters and relevant actor or parent; an invalid or mismatched token returns `400 InvalidContinuationToken`. Totals describe matching records, never merely the current page size. Mutable rankings can move rows between requests, so clients de-duplicate by `id`. See `REST_API.md` for collection-specific ranking and bounded-preview semantics.

### 0.2 Rate limits

Every `429 RateLimited` response in Phase 2 (and from now on in Phase 1 routes too) carries a `Retry-After` header in whole seconds. Limits are listed in section 12. The CORS layer adds `Access-Control-Expose-Headers: ETag, Retry-After` so browsers can read both.

### 0.3 Target types

Polymorphic features (reactions, comments, reports, notifications, moderation) name their subject with a `targetType` and `targetId`.

| `targetType` | Id prefix | URL segment | Phase |
|---|---|---|---|
| `FeedPost` | `fpo_` | `feed` | 2 |
| `Comment` | `cmt_` | `comments` | 2 |
| `User` | `usr_` | `users` | 2 (reports only) |
| `Thread`, `Lesson`, `Event`, `Message` | `thr_`, `les_`, `evt_`, `msg_` | `threads`, `lessons`, `events`, `messages` | Reserved (3 to 5) |

Clients must tolerate unknown `targetType` values in notifications and moderation payloads (render a generic row and skip deep links).

### 0.4 New identifier prefixes

| Entity | Prefix |
|---|---|
| Feed post | `fpo_` |
| Feed post revision | `fpr_` |
| Media | `med_` |
| Comment | `cmt_` |
| Comment revision | `cmr_` |
| Reaction | `rxn_` |
| Mention | `mtn_` |
| Notification | `ntf_` |
| Push token | `ptk_` |
| Push delivery | `pdl_` |
| Notification fan-out job | `fnj_` |
| Report | `rpt_` |
| Moderation action | `mac_` |

---

## 1. Route groups and permissions

Phase 1 route groups stay. Phase 2 adds one group and classifies the new prefixes:

| Group | Requirement | Phase 2 prefixes |
|---|---|---|
| Anonymous | none (the route verifies its own signature or cookie, or serves public data) | `/api/v1/webhooks/mediaconvert`, `/api/v1/media/files/*`, `/api/v1/media/local-uploads/*`, `/api/v1/public/feed*`, `/api/v1/email/unsubscribe` |
| Signed in | valid session | `/api/v1/me/notification-preferences`, `/api/v1/me/push-tokens`, `/api/v1/me/blocks`, `/api/v1/me/feed-posts` (all covered by the existing `/me` prefix) |
| Member | `ageAttested` and an active membership (`SystemAdmin` passes) | `/api/v1/feed*`, `/api/v1/comments*`, `/api/v1/media*` (except the two anonymous prefixes), `/api/v1/notifications*`, `/api/v1/reports`, `/api/v1/users/{id}/block`, `/api/v1/members/search`, `/api/v1/link-previews`, `/api/v1/reactions/set` |
| **Moderator** (new) | `SystemAdmin`, or any `Moderator` or `FeedModerator` role assignment. Per-item scope is checked in the service | `/api/v1/admin/moderation/*` |
| Admin | `SystemAdmin` or break-glass token | `/api/v1/admin/feed*`, `/api/v1/admin/media*`, `/api/v1/admin/metrics/engagement`, `/api/v1/admin/moderators*`, `/api/v1/admin/moderation/word-filter*` (checked before the Moderator rule) |

`AuthorizationService.Classify` checks `/api/v1/admin/moderation/word-filter` (Admin), then `/api/v1/admin/moderation` (Moderator), before the generic `/api/v1/admin` rule. A signed-in user without a moderator role gets `403 Forbidden` from the Moderator group.

Push tokens live under `/me` (not `/api/v1/push-tokens` as the plan sketched) on purpose: a member whose membership lapsed still receives `MembershipExpiring` and moderation notices, and a Signed-in route keeps registration working for them.

### 1.1 Capability matrix

| Action | Who |
|---|---|
| Read feed, comments, reactions | Any member |
| Create a feed post | `SystemAdmin`, or a member with `flags.canPostToFeed` (granted per user by an admin) and `flags.canPost` |
| Edit own feed post | Author, any time, in any state (revision history kept, section 5.5) |
| Edit any feed post, delete any feed post, pin and unpin, mark a post Public | `SystemAdmin` |
| Delete own feed post | Author, any time |
| Comment, reply | Any member with `flags.canPost` who is not muted, on a Published post with `commentsEnabled` |
| React | Any member who is not muted |
| Edit own comment | Author, any time (revision history kept, section 7.6) |
| Delete own comment | Author, any time |
| Attach images (posts and comments) | `flags.canPost` and `flags.canPostMedia` and not muted, or `SystemAdmin` |
| Attach video (posts and comments) | The image rule plus `flags.canUploadVideo`, or `SystemAdmin` |
| Search members (mention autocomplete) | Any member |
| Report, block | Any member, including muted and restricted members. Staff cannot be blocked |
| Hide, unhide, remove, restore feed comments | `SystemAdmin` (any), `FeedModerator` (`resourceType: Feed`; comments not authored by a `SystemAdmin`) |
| Hide, unhide, remove, restore feed posts | `SystemAdmin` (any post), `FeedModerator` (posts not authored by a `SystemAdmin`) |
| Moderation queue | `SystemAdmin` (everything), `FeedModerator` (targets in the Feed) |
| Grant and revoke `FeedModerator` | `SystemAdmin` (section 10.4) |
| Word filter list | `SystemAdmin` (section 9.4) |

**Per-user posting permissions.** Phase 2 adds two flags to the Phase 1 `flags` object (`PUT /api/v1/admin/members/{id}/flags`, audited as `member.flags`), next to the existing `canPostToFeed` and `canUploadVideo`:

| Flag | Default | When false |
|---|---|---|
| `canPostToFeed` (Phase 1) | false | Cannot create feed posts. Granted per user to the owner, admins' delegates, and trusted members |
| `canPost` (new) | true | Blocked from posting entirely: no feed posts, comments, or replies, and no edits; `403 PostingNotAllowed`. Reacting, reporting, blocking, deleting their own content, and notifications keep working |
| `canPostMedia` (new) | true | Text only: `403 MediaNotAllowed` on upload sessions and on attaching media to posts or comments |
| `canUploadVideo` (Phase 1) | true | Images only: `403 VideoUploadNotAllowed` for video |
| `isMuted`, `mutedUntilUtc` (Phase 1) | false | Temporary: see below |

`SystemAdmin` ignores the four permission flags. The admin member detail shows them as a "Posting" group with the toggles and the mute control, and every change is audited.

**Muted members** (`isMuted`, `mutedUntilUtc` in the future) get `403 Muted` from every create, edit, react, and upload route, and cannot change their avatar, bio, or display name until the mute ends (decision 21; other profile fields and unchanged values still save). The error body adds `mutedUntilUtc` (the end of the mute, or null for an indefinite mute) beside `error`, `message`, and `fieldErrors`; the message is plain text without a timestamp, so clients format the end time. Reading, reporting, blocking, deleting their own content, and notification routes keep working. Mute is the temporary, time-boxed control; `canPost` and `canPostMedia` are standing restrictions that stay until an admin changes them.

### 1.2 New error codes

| Status | `error` | When |
|---|---|---|
| 400 | `InvalidContinuationToken` | Cursor is malformed, expired, or from a different route, sort, or filter |
| 400 | `UnknownReaction` | Reaction key is not in the property's `reactions` |
| 400 | `MediaLimitExceeded` | Too many attachments, or more than one video on a post |
| 400 | `VideoTooLong` | Declared duration exceeds the cap (the same code is used as a media `failureReason`) |
| 400 | `CannotBlockSelf`, `CannotBlockStaff`, `CannotReportSelf` | Self-targeted or protected actions (staff: `SystemAdmin`, `Moderator`, `FeedModerator`) |
| 400 | `ContentFiltered` | Text matched a `Block` term of the word filter (section 9.4); `fieldErrors` names the field (`body`, `title`, `altText`, `displayName`, `bio`) without echoing the term |
| 400 | `LinkPreviewUnavailable` | `POST /api/v1/link-previews` for a URL that is not allowed or could not be fetched (section 5.9) |
| 403 | `Muted` | Muted member tried to write, or to change their avatar, bio, or display name; the body carries `mutedUntilUtc` |
| 403 | `InteractionBlocked` | The content's author has blocked the caller (reply, react, mention notification) |
| 403 | `VideoUploadNotAllowed` | `canUploadVideo` is false |
| 403 | `PostingNotAllowed` | `canPost` is false |
| 403 | `MediaNotAllowed` | `canPostMedia` is false |
| 403 | `InvalidSignature` | Local upload URL or media cookie failed verification |
| 404 | `NotFound` | Unknown id, deleted content, or content the caller may not see (never 403, so ids do not leak) |
| 409 | `PinLimitReached` | More than `feed.maxPinned` pinned posts |
| 409 | `MediaNotAttachable` | Media is not the caller's, is already attached elsewhere, is Uploading, or Failed |
| 409 | `UploadIncomplete` | `complete` called before every part (or the single object) arrived, or size mismatch |
| 409 | `ParentUnavailable` | Reply to a comment that is deleted or removed |
| 409 | `CommentsDisabled` | Comment on a post with `commentsEnabled: false` |
| 410 | `UploadExpired` | Upload session older than `media.uploadExpiryHours` |
| 413 | `TooLarge` | Declared or received size exceeds the limit (same code as Phase 1 avatars) |
| 415 | `UnsupportedMediaType` | Content type not in the allowed list, or sniffed bytes do not match it |
| 429 | `RateLimited` | Section 12; always with `Retry-After` |
| 503 | `IntegrationNotConfigured` | S3 mode without buckets, MediaConvert without a role, and similar |

---

## 2. Shared shapes

### UserSummary

Embedded wherever a person appears (authors, actors, reactors).

```json
{
  "id": "usr_...",
  "displayName": "Example Owner",
  "handle": "owner",
  "avatarUrl": "/api/v1/media/avatars/usr_..._1731350000.jpg",
  "rank": "Gold",
  "rankLevel": 2,
  "isStaff": true,
  "isDeleted": false
}
```

`isStaff` is true for `SystemAdmin`, `Moderator`, and `FeedModerator` holders (clients show a small badge). `rank` is a key from the property's `profile.rank.values` (null when unset or when `profile.rank.enabled` is false) and `rankLevel` is 0 to `profile.rank.maxLevel`; clients render labels and colors from the property (for example a grade and a level). For a deleted account: `id`, `handle`, `avatarUrl`, and `rank` are null, `rankLevel` is 0, `displayName` is `"Deleted member"`, and `isDeleted` is true.

### Mention

```json
{ "userId": "usr_...", "handle": "alexchen", "displayName": "Alex Chen" }
```

`handle` is the text exactly as written in the body (case preserved), so a later handle change does not break rendering. Clients link `@<handle>` occurrences that match an entry; unmatched `@words` render as plain text.

### Reaction fields (on every reactable payload)

```json
{
  "reactionCounts": { "like": 12, "fire": 3 },
  "reactionTotal": 15,
  "myReactions": ["like"]
}
```

`reactionCounts` only lists keys with a count above zero and only keys in the property's current `reactions`. `myReactions` lists the caller's keys. The configured display order comes from the property document (section 6); standalone catalog reads use their requested enumeration ordering, not from the object.

### Viewer capabilities

Every FeedPost and Comment carries a `viewer` object computed for the caller. Clients drive menus from it and never re-derive permissions. The server still enforces everything.

```json
{ "canEdit": false, "canDelete": false, "canPin": false, "canComment": true, "canReact": true, "canReport": true, "canModerate": false, "canAttachMedia": true, "canAttachVideo": true }
```

Comments use the same object with `canReply` in place of `canComment` and without `canPin`. `canAttachMedia` and `canAttachVideo` reflect `canPostMedia` and `canUploadVideo` for the caller, so composers can hide the attach buttons.

### Media

```json
{
  "id": "med_...",
  "kind": "Image",
  "status": "Ready",
  "failureReason": null,
  "ownerUserId": "usr_...",
  "contentType": "image/jpeg",
  "sizeBytes": 4210331,
  "width": 4032,
  "height": 3024,
  "durationSeconds": null,
  "altText": "The owner demonstrating a technique",
  "blurhash": "LKO2?U%2Tw=w]~RBVZRi};RPxuwH",
  "image": {
    "variants": {
      "thumb":  { "width": 320,  "height": 240,  "webpUrl": "https://media.example.test/media/med_.../thumb.webp",  "jpegUrl": "https://media.example.test/media/med_.../thumb.jpg" },
      "medium": { "width": 1080, "height": 810,  "webpUrl": ".../medium.webp", "jpegUrl": ".../medium.jpg" },
      "large":  { "width": 2048, "height": 1536, "webpUrl": ".../large.webp",  "jpegUrl": ".../large.jpg" }
    }
  },
  "video": null,
  "createdUtc": "2026-11-12T18:00:00Z",
  "readyUtc": "2026-11-12T18:00:04Z"
}
```

A Ready video:

```json
{
  "id": "med_...", "kind": "Video", "status": "Ready", "failureReason": null,
  "contentType": "video/quicktime", "sizeBytes": 188004112, "width": 1920, "height": 1080, "durationSeconds": 142.6,
  "altText": null, "blurhash": "L6PZfSi_.AyE_3t7t7R**0o#DgR4",
  "image": null,
  "video": {
    "hlsUrl": "https://media.example.test/video/med_.../master.m3u8",
    "mp4Url": null,
    "posterUrl": "https://media.example.test/media/med_.../poster.jpg",
    "posterThumbUrl": "https://media.example.test/media/med_.../poster_thumb.jpg"
  }
}
```

| Field | Rule |
|---|---|
| `kind` | `Image` or `Video` |
| `status` | `Uploading` (session created, bytes not confirmed), `Processing` (complete called, worker running), `Ready`, `Failed` |
| `failureReason` | Only when Failed: `UnsupportedFormat`, `Corrupt`, `TooLarge`, `VideoTooLong`, `ImageTooLarge` (pixel count), `ProcessingError`, `UploadExpired` |
| `width`, `height` | Display dimensions after EXIF orientation is applied (so a portrait phone photo reports portrait). Null until Ready |
| `durationSeconds` | Videos only, from the container header, null until known |
| `blurhash` | Placeholder string (4x3 components) for images and video posters; null until Ready |
| Image variants | Long edge 320 (`thumb`), 1080 (`medium`), 2048 (`large`). Never upscaled: a smaller original yields variants at the original size. Every variant exists in WebP and JPEG |
| `video.hlsUrl` | HLS master playlist (H.264 and AAC, 6 second segments) in the transcoded bucket, served under `/video/{id}/`. Ladder 360p, 540p, 720p, 1080p in production; 360p and 720p in local ffmpeg mode; rungs above the source height are skipped |
| `video.mp4Url` | Only in local `Passthrough` mode (section 17); null in production. Clients play `hlsUrl` when present, else `mp4Url` |
| URLs | Images, posters, and link preview images come from the images bucket under `/media/{id}/`; HLS from the transcoded bucket under `/video/{id}/`; media of Public posts under `/public/media/{id}/` and `/public/video/{id}/` (section 5.10). Absolute when `media.publicBaseUrl` is set (production). Root-relative (`/api/v1/media/files/med_.../thumb.webp`) when it is empty (local); clients resolve them against their API origin, exactly like Phase 1 `avatarUrl` |

URLs are never signed per request. Access is controlled by the media grant cookie (section 4.7), so URLs are cacheable and identical for every member.

---

## 3. Body format, mentions, and sanitization

### 3.1 Markdown subset

Feed posts and comments store Markdown source in `body`. There is one parser, in `shared/src/markdown.js`, that turns a body into an AST; the website maps the AST to React DOM elements and the mobile app maps it to React Native `Text` and `View` elements. **No client ever renders a body as HTML** (no `dangerouslySetInnerHTML`, no WebView), so there is nothing to inject.

| Syntax | Supported in | Renders as |
|---|---|---|
| Paragraphs (blank line) and line breaks (single newline) | posts, comments | paragraph, line break |
| `**bold**`, `*italic*` or `_italic_`, `~~strike~~` | posts, comments | emphasis |
| `- item` or `* item`, `1. item` (one nesting level) | posts, comments | list |
| `> quote` | posts, comments | block quote |
| `# Heading`, `## Heading`, `### Heading` | posts only | heading (comments render the line as plain text) |
| `` `inline code` `` (any run of backticks, closed by the same run) | posts, comments | monospaced code; no markup, links, or mentions inside |
| Fenced code blocks: ```` ``` ```` or `~~~` with an optional language, closed by the same fence (or the end of the body) | posts, comments | monospaced block that scrolls sideways; content literal |
| `---`, `***`, or `___` on a line of its own | posts, comments | horizontal rule |
| Pipe tables: a header row, a separator row (`|---|`, with `:` for left, center, or right alignment), and body rows (at most 12 columns and 200 rows) | posts, comments | table (scrolls sideways when wide); cells hold inline Markdown |
| `[text](https://...)` and bare `https://...` or `http://...` URLs | posts, comments | link (opens externally; `rel="noopener noreferrer nofollow"` on web) |
| `@handle` | posts, comments | mention link when the handle is in `mentions` |
| `\` escapes for `* _ ~ [ ] ( ) # > - @ \ ` |` | posts, comments | literal character |

Everything else (HTML tags, images, footnotes, other link schemes) is shown as the literal text the member typed. `@handle` inside code is not a mention (the server does not notify it). Link targets other than `http` and `https` are rendered as plain text.

### 3.2 Server normalization (on create and edit)

1. Reject bodies that are not valid UTF-8 (`400 ValidationFailed`).
2. Normalize to Unicode NFC, convert `\r\n` and `\r` to `\n`, remove control characters other than `\n` and `\t`, remove zero-width and bidirectional override characters (U+200B to U+200F, U+202A to U+202E, U+2066 to U+2069).
3. Trim leading and trailing whitespace; collapse runs of three or more newlines to two.
4. Enforce lengths (below) on the normalized text, counted in Unicode scalar values.
5. Extract mentions (3.3, ignoring code) and compute `excerpt`: the body rendered to plain text (markup removed, code text and table cells kept, rules dropped, mentions kept as `@handle`, whitespace collapsed), cut at a word boundary to 200 characters with `...` when truncated. Notifications, push messages, and admin tables use `excerpt`.

The stored `body` is the normalized source. The server does not rewrite Markdown.

### 3.3 Mentions

- Pattern: `@` followed by 3 to 30 of `A-Z a-z 0-9 _ .`, not preceded by a letter, digit, `_`, or `.` (so `member@example.test` is not a mention). Trailing `.` is not part of the handle.
- Resolved case-insensitively against active, non-deleted, non-banned accounts at write time. Unresolved handles are plain text.
- At most `mentions.maxNotifiedPerItem` (default 10) distinct users per item are stored and notified; the rest render as plain text.
- Self-mentions are stored (for rendering) but never notify.
- On edit, only newly added users are notified; removed users stay notified (notifications are not retracted).
- No notification when either side has blocked the other.
- A scheduled post notifies its mentions when it publishes, not when it is saved.

### 3.4 Member search (mention autocomplete)

Composers on both apps suggest members as soon as the member types `@` and one character.

`GET /api/v1/members/search?search=al&maxResults=8` or `POST /api/v1/members/enumerate` (Member) returns `200 EnumerationResult<UserSummary>`:
- `search` (`q` is a GET alias): 1 to 30 characters after an optional leading `@`; matched case-insensitively as a prefix of `handle` or of any word of `displayName`. `maxResults` default 8, max 20.
- Default `Relevance` order: exact handle match, then people the caller recently interacted with on the current thread (`contextType` and `contextId` optional parameters, for example `contextType=FeedPost&contextId=fpo_...`, rank the post author and commenters first), then handle prefix matches, then display name matches, alphabetical within each.
- Pages support `skip` and scoped SQL continuation tokens; `CreatedAscending` and `CreatedDescending` are also supported. Staff receive exact totals; members follow `enumeration.countMemberTotals`. Standard metadata filters and visible labels/tags apply.
- Excludes the caller, deleted, banned, and age-unattested accounts, members without an active membership, and accounts on either side of a block with the caller.
- Uses an index on `lower(handle)` and a `displaynamewords` child table (lowercased words) so prefix queries stay indexed on both providers.
- Errors: `400 ValidationFailed`, `429 RateLimited` (section 12). The same route will back the Phase 4 directory search.

The composer inserts `@handle` and the server resolves it on save (3.3).

### 3.5 Lengths

| Field | Min | Max |
|---|---|---|
| Feed post `title` | 0 (optional) | 140 |
| Feed post `body` | 1, or 0 when at least one attachment is present | `feed.maxBodyLength` (default 10000) |
| Comment `body` | 1, or 0 when an attachment is present | `comments.maxBodyLength` (default 5000) |
| Media `altText` | 0 | 1000 |
| Report `details` | 0 (10 when `reason` is `Other`) | 1000 |
| Moderation `reason` and `note` | 3 for `reason` | 500 each |

---

## 4. Media pipeline

Media bytes never pass through the API process in production. Clients ask the backend for an upload session, send the bytes straight to storage, tell the backend they are done, then poll until the media is Ready. Local development runs the identical client flow against the backend itself (section 4.8).

### 4.0 Storage locations (one bucket per purpose)

Every purpose has its own bucket (S3 mode) or folder (Local mode), so access, lifecycle, and cost are managed separately. Bucket names are settings (section 14) editable in the admin console (Settings, Media storage); the infrastructure creates the buckets and passes their names as environment variables (`infra/README.md`).

| Purpose | Setting (environment variable) | Local folder under `storage.directory` | Contents | Served by the media CDN |
|---|---|---|---|---|
| Source | `media.sourceBucket` (`TK_MEDIA_SOURCE_BUCKET`) | `source/` | Original image uploads, as received (`in/{id}/original`) | No (private) |
| Video source | `media.videoSourceBucket` (`TK_MEDIA_VIDEO_SOURCE_BUCKET`) | `video-source/` | Original video uploads, as received | No (private; MediaConvert reads it) |
| Images | `media.imagesBucket` (`TK_MEDIA_IMAGES_BUCKET`) | `images/` | Image variants, video posters, link preview images (`media/{id}/...`, `link-previews/{hash}/...`, `public/media/{id}/...`) | Yes (default path) |
| Transcoded | `media.transcodedBucket` (`TK_MEDIA_TRANSCODED_BUCKET`) | `transcoded/` | HLS playlists and segments (`video/{id}/...`, `public/video/{id}/...`) | Yes (`/video/*`, `/public/video/*`) |
| Retained | `media.retainedBucket` (`TK_MEDIA_RETAINED_BUCKET`) | `retained/` | Derivatives of moderator-removed content (`removed/{id}/...`), kept for `moderation.retainRemovedDays` | No |

**All original uploads are kept.** Originals are never deleted by processing and never expire by lifecycle; they move to cheaper storage classes as they age (Standard-IA, then Glacier Instant Retrieval). They are private: never served, never linked, and only read by the processors and by `reprocess`. Originals are deleted only when the member erases them: deleting the content they are attached to (after `moderation.retainRemovedDays`), deleting unattached media, or deleting the account (section 11).

### 4.1 Limits and accepted types

| | Images | Member video | Admin video (`SystemAdmin`) |
|---|---|---|---|
| Content types | `image/jpeg`, `image/png`, `image/webp` | `video/mp4`, `video/quicktime` | same |
| Max size | `media.maxImageBytes` (20 MB) | `media.maxMemberVideoMegabytes` (2048) | `media.maxAdminVideoMegabytes` (0 = uncapped) |
| Max pixels or duration | `media.maxImagePixels` (40 megapixels) | `media.maxMemberVideoSeconds` (300) | `media.maxAdminVideoSeconds` (0 = uncapped) |
| Upload mode | Single PUT | Single PUT up to `media.multipartThresholdMegabytes` (32), multipart above | same |
| Daily quota | Shared quota `media.memberDailyUploadMegabytes` (5120) per member per UTC day | | none |

Mobile converts HEIC and HEIF photos to JPEG before uploading (`expo-image-picker` with the compatible representation, or `expo-image-manipulator`); the server does not accept HEIC. Animated GIFs are not accepted in Phase 2. "Admin" here means `SystemAdmin` only; `canPostToFeed` members get member limits.

### 4.2 Create an upload session

`POST /api/v1/media/uploads` (Member)

```json
{
  "kind": "Video",
  "contentType": "video/quicktime",
  "sizeBytes": 188004112,
  "fileName": "IMG_4411.MOV",
  "durationSeconds": 142.6
}
```

| Field | Rule |
|---|---|
| `kind` | Required, `Image` or `Video` |
| `contentType` | Required, from the table above, and consistent with `kind` |
| `sizeBytes` | Required, 1 or more, within the limit |
| `fileName` | Optional, up to 255 characters, stored for the admin library only (never served) |
| `durationSeconds` | Required for video. Checked against the cap now (`400 VideoTooLong`) and re-checked from the file itself at complete |

Success `201`:

```json
{
  "media": { "...": "Media with status Uploading" },
  "upload": {
    "mode": "Multipart",
    "expiresUtc": "2026-11-12T19:00:00Z",
    "single": null,
    "multipart": {
      "partSizeBytes": 16777216,
      "partCount": 12,
      "parts": [
        { "partNumber": 1, "method": "PUT", "url": "https://example-prod-media-video-source-222222222222.s3.us-west-2.amazonaws.com/in/med_.../original?partNumber=1&uploadId=...&X-Amz-Signature=..." }
      ]
    },
    "uploadedParts": []
  }
}
```

For `mode: "Single"`, `multipart` is null and `single` is:

```json
{ "method": "PUT", "url": "https://...", "headers": { "Content-Type": "image/jpeg" } }
```

Rules:
- The client sends exactly the `headers` listed with the single PUT and nothing else (no `Authorization`). Part PUTs send no extra headers.
- Part size is `max(media.multipartPartMegabytes, ceil(sizeBytes / 1000))` rounded up to a whole MiB, so a session never exceeds 1000 parts. Every part except the last is exactly `partSizeBytes`.
- Every PUT response carries an `ETag` header. The client records it verbatim (including quotes) for `complete`.
- Images go to the source bucket and videos to the video source bucket.
- Errors: `400 ValidationFailed`, `400 VideoTooLong`, `403 Muted`, `403 PostingNotAllowed`, `403 MediaNotAllowed`, `403 VideoUploadNotAllowed`, `413 TooLarge`, `415 UnsupportedMediaType`, `429 RateLimited` (including the daily quota), `503 IntegrationNotConfigured`.

### 4.3 Refresh upload URLs and resume

`POST /api/v1/media/{id}/upload-urls` (owner, status Uploading) returns `200 { upload }` with fresh URLs and `uploadedParts: [{ partNumber, etag, sizeBytes }]` listing parts storage already has (S3 `ListParts`, or the local part files). A client that was interrupted (app backgrounded, network lost, URLs expired) calls this and uploads only the missing parts. `410 UploadExpired` after `media.uploadExpiryHours`.

### 4.4 Complete

`POST /api/v1/media/{id}/complete` (owner)

```json
{ "parts": [ { "partNumber": 1, "etag": "\"9b2cf535f27731c974343645a3985328\"" } ] }
```

`parts` is required for multipart and omitted for single. The server completes the multipart upload, confirms the object size equals `sizeBytes`, reads the first bytes to confirm the real format matches `contentType` (magic bytes: JPEG `FF D8 FF`, PNG signature, WebP `RIFF....WEBP`, ISO BMFF `ftyp` box for MP4 and QuickTime), and for video reads the duration from the `mvhd` box of the `moov` atom using ranged reads. Then the media moves to `Processing` and the worker is signalled.

Success `202 Media` (status `Processing`). Calling `complete` again on a Processing or Ready item returns `200 Media` unchanged (idempotent).

Errors: `409 UploadIncomplete` (missing parts or size mismatch; the session stays Uploading), `415 UnsupportedMediaType` (sniff mismatch; media becomes Failed with `UnsupportedFormat`), `400 VideoTooLong` (media becomes Failed with `VideoTooLong`), `410 UploadExpired`.

### 4.5 Status, metadata, delete

| Method and path | Who | Notes |
|---|---|---|
| `GET /api/v1/media/{id}` | Owner, `SystemAdmin`, or any member when the media is attached to content they can see | `200 Media`. Unattached media is visible to its owner and admins only |
| `GET /api/v1/media?ids=med_a,med_b`, `POST /api/v1/media/enumerate` | Same, per item | `200 EnumerationResult<Media>` with `objects`; POST uses `filters.ids` (CSV or string array), at most 20 requested ids. Invisible ids are omitted. Standard search, creation dates, metadata and continuation apply; composers collect pages on each poll |
| `PUT /api/v1/media/{id}` | Owner or `SystemAdmin` | `{ "altText": "..." }` returns `200 Media`. Allowed in any status and after attachment |
| `DELETE /api/v1/media/{id}` | Owner (unattached only) or `SystemAdmin` | `204`. Aborts a multipart upload, deletes every stored object including the original. Attached media is removed by editing the post or comment |

Polling: images every 2 seconds for up to 60 seconds; video every 5 seconds while the composer is open. Processing normally takes 2 to 10 seconds for an image and roughly the video's duration for video in production.

### 4.6 Processing

**Images** (in-process worker in every mode, SkiaSharp):
1. Decode; reject above `media.maxImagePixels` (`ImageTooLarge`) or undecodable (`Corrupt`).
2. Apply EXIF orientation to the pixels.
3. Re-encode every variant from pixels only. EXIF (including GPS), XMP, IPTC, and maker notes are not copied; color is converted to sRGB.
4. Write `thumb`, `medium`, `large` as `.webp` (quality 82) and `.jpg` (quality 85, progressive) under `media/{id}/` in the images bucket, compute `blurhash`, set `Ready`.
5. Keep the original in the source bucket (4.0). It may contain location data the member did not mean to publish, which is why it is never served; every served variant is re-encoded without metadata.

**Video:**
1. Production (`media.videoProcessor = MediaConvert`): submit a job from template `media.mediaConvertJobTemplate` with input clipping at the duration cap as a cost backstop, `userMetadata.mediaId`, the input in the video source bucket, HLS output to `video/{id}/` in the transcoded bucket (`master.m3u8` plus rendition playlists and segments), and frame capture to `media/{id}/` in the images bucket (`poster.jpg` 1280 wide, `poster_thumb.jpg` 320 wide). Source metadata is not passed through. Completion arrives at `POST /api/v1/webhooks/mediaconvert` (4.9); the worker also polls `GetJob` for any job older than 15 minutes without a webhook.
2. The worker reads the poster, computes `blurhash`, and sets `width`, `height`, and `Ready`. The original stays in the video source bucket (4.0), so any video can be re-encoded later (`reprocess`).
3. Any job error sets `Failed` with `ProcessingError` and deletes partial outputs.

File names are flat (no subfolders) in every mode, for example `master.m3u8`, `hls_720p.m3u8`, `hls_720p_00001.ts`, so playlists use relative names and the local file route needs one path segment per file.

### 4.7 Delivery and access (signed cookies)

Media is paywalled. Members get access through a short-lived grant that the browser or app sends as cookies.

`POST /api/v1/media/grant` (Member), no body. Success `200`:

```json
{
  "mode": "CloudFront",
  "expiresUtc": "2026-11-12T04:00:00Z",
  "cookies": [
    { "name": "CloudFront-Policy", "value": "eyJTdGF0ZW1lbnQiOlt7...", "domain": "example.test", "path": "/", "expiresUtc": "2026-11-12T04:00:00Z" },
    { "name": "CloudFront-Signature", "value": "...", "domain": "example.test", "path": "/", "expiresUtc": "..." },
    { "name": "CloudFront-Key-Pair-Id", "value": "K2JCJMDEHXQW5F", "domain": "example.test", "path": "/", "expiresUtc": "..." }
  ]
}
```

The same response sets those cookies with `Set-Cookie` (`Secure; HttpOnly; SameSite=Lax`, `Domain` from `media.cookieDomain`).

| Mode | When | Cookies | What is protected |
|---|---|---|---|
| `CloudFront` | `media.storageMode = S3` | `CloudFront-Policy`, `CloudFront-Signature`, `CloudFront-Key-Pair-Id`; custom policy for `https://<media host>/*` (images under `/media/`, HLS under `/video/`) ending at `expiresUtc` | CloudFront rejects requests without valid cookies with 403 |
| `Local` | `media.storageMode = Local` | `tk_media`: AES-256-GCM protected `{ userId, expiresUtc }` (same key as access tokens), `Path=/api/v1/media/files`, no `Domain`, `Secure` except on `http://127.0.0.1` | The backend's file route (4.8) |

Client rules:
- **Web:** call the grant after sign-in, on app load when signed in, and whenever `expiresUtc` is less than 10 minutes away (set a timer). The website calls the API same-origin (`/api/*` through nginx locally, CloudFront in production), so the `Set-Cookie` applies to the web domain (`media.cookieDomain`, for example `example.test`) and covers the media host (`media.example.test`). `<img>` and Safari native HLS send same-site cookies automatically. **hls.js must set `xhrSetup: (xhr) => { xhr.withCredentials = true; }`**; the media distribution answers CORS for the website origins with `Access-Control-Allow-Credentials: true`.
- **Mobile:** call the grant at the same moments, keep `cookies` in memory, and attach them as a `Cookie` header (`name=value; name=value`) on every media request: `expo-image` `source={{ uri, headers }}` and `expo-video` `VideoSource { uri, headers }` (headers apply to the playlist and every segment request).
- On a 403 from a media URL, refresh the grant once and retry.
- `media.grantMinutes` (default 360) bounds how long a lapsed member keeps media access after their membership ends.

On a moderation `Remove` (section 9.3) the backend moves the target's served objects (image variants, posters, HLS) to `removed/{id}/` in the retained bucket, which no distribution serves, and moves them back on `Restore`. The retained bucket's lifecycle deletes them after `moderation.retainRemovedDays` (365) unless they were restored; originals stay in the source buckets for the same period and are deleted with the content when it is purged. Deleting content deletes its media (served objects and originals) after `moderation.retainRemovedDays`.

### 4.8 Local mode (no AWS)

`media.storageMode = Local` (the default) makes the backend play both roles, storage and CDN, behind the same client contract.

| Concern | Local behavior |
|---|---|
| Upload URLs | `PUT {origin}/api/v1/media/local-uploads/{id}?exp=<unix>&sig=<hex>` for single, `PUT .../local-uploads/{id}/parts/{partNumber}?exp=&sig=` for parts. `sig` is HMAC-SHA256 over `PUT\n<path>\n<exp>` with a key derived from `auth.tokenEncryptionKey` (HKDF, info `tk-media-upload`). No bearer token is sent, exactly like S3 |
| Upload handling | Body streamed to `{storage.directory}/source/in/{id}/` (video: `video-source/in/{id}/`) (never buffered whole in memory). Enforces the part size, the declared total, and the declared `Content-Type` for single uploads. Responds `200` with `ETag: "<md5 hex>"`. Bad or expired signature: `403 InvalidSignature` |
| Processed files | `{storage.directory}/images/media/{id}/<file>` and `{storage.directory}/transcoded/video/{id}/<file>`, with the same flat names as S3; removed content under `{storage.directory}/retained/` |
| Delivery | `GET /api/v1/media/files/{id}/{file}` (images bucket folder, then transcoded). Accepts a valid `tk_media` cookie or an `Authorization: Bearer` access token whose session has a membership (so the API Explorer and tests work). Supports `Range` (`206`, `Accept-Ranges: bytes`), which Safari needs for video. `Cache-Control: private, max-age=31536000, immutable`, `X-Content-Type-Options: nosniff`, correct `Content-Type` (`application/vnd.apple.mpegurl` for playlists, `video/mp2t` for segments) |
| Images | Same SkiaSharp pipeline as production |
| Video | `media.videoProcessor = Ffmpeg` in the Docker stack: ffmpeg in the server container transcodes to HLS (360p and 720p, `-preset veryfast`, `-map_metadata -1`), grabs the poster at 1 second, `media.videoConcurrency` jobs at a time. `Passthrough` (the code default, and what tests use) copies the original to `transcoded/video/{id}/original.mp4`, sets `video.mp4Url`, leaves `hlsUrl` and posters null, and logs a warning at startup that video metadata is not stripped |

**Decision: ffmpeg HLS in the Docker stack, passthrough only as a fallback.** The bugs this phase is most likely to ship are in HLS playback (hls.js setup, `withCredentials`, `expo-video` header handling on segments, Safari range requests) and in poster and duration handling. Passthrough would hide all of them behind a progressive MP4 that every player handles. Running ffmpeg inside the server container (a `local` build target of `docker/server/Dockerfile` that adds the `ffmpeg` package) rather than a separate worker container keeps one binary and one queue: the media worker already claims jobs from the database and already writes to `storage.directory`, so a second container would need a second run mode and a shared volume for no benefit in development. CPU contention is acceptable at `videoConcurrency = 1`. Production never uses ffmpeg. Passthrough stays for `dotnet run` without ffmpeg installed and for the test suites.

### 4.9 MediaConvert webhook

`POST /api/v1/webhooks/mediaconvert` (anonymous; called by SNS, never by clients). EventBridge forwards "MediaConvert Job State Change" events to an SNS topic with an HTTPS subscription to this route.

- Verifies the SNS signature (`SignatureVersion` 2, SHA256), requires `SigningCertURL` on `https://sns.<region>.amazonaws.com/`, and requires `TopicArn` to equal `media.snsTopicArn`. Failure: `400`.
- `SubscriptionConfirmation`: fetches `SubscribeURL` (only after the checks above). `Notification`: reads `detail.status` (`COMPLETE`, `ERROR`, `CANCELED`), `detail.jobId`, `detail.userMetadata.mediaId`.
- Idempotent on `jobId` plus `status`. Always `200` once verified, so SNS does not retry processed messages.

---

## 5. Feed posts

### 5.1 FeedPost

```json
{
  "id": "fpo_...",
  "kind": "Original",
  "author": { "...": "UserSummary" },
  "title": "Welcome to the community",
  "body": "Start here: introduce yourself below. Thanks @alexchen for the photos. https://example.test/about",
  "excerpt": "Start here: introduce yourself below. Thanks @alexchen for the photos. https://example.test/about",
  "mentions": [ { "userId": "usr_...", "handle": "alexchen", "displayName": "Alex Chen" } ],
  "attachments": [ { "...": "Media" } ],
  "linkPreview": { "...": "LinkPreview (section 5.9) or null" },
  "visibility": "Members",
  "state": "Published",
  "commentsEnabled": true,
  "isPinned": true,
  "pinnedUtc": "2026-11-12T18:05:00Z",
  "publishAtUtc": null,
  "publishedUtc": "2026-11-12T18:00:00Z",
  "createdUtc": "2026-11-12T17:58:00Z",
  "updatedUtc": "2026-11-12T18:05:00Z",
  "editedUtc": null,
  "revisionCount": 0,
  "commentCount": 14,
  "reactionCounts": { "like": 31, "fire": 4 },
  "reactionTotal": 35,
  "myReactions": [],
  "commentTarget": { "type": "FeedPost", "id": "fpo_..." },
  "source": null,
  "moderation": null,
  "viewer": { "canEdit": false, "canDelete": false, "canPin": false, "canComment": true, "canReact": true, "canReport": true, "canModerate": false }
}
```

| Field | Rule |
|---|---|
| `kind` | `Original` in Phase 2. Phase 3 adds `PromotedThread` (a forum thread promoted to the feed, with `source: { type: "Thread", id, forumId, forumName, title }`). Clients skip items whose `kind` they do not know |
| `commentTarget` | Where this card's comments and reactions live. Clients always load and post comments, and react, against `commentTarget`, never against `id` directly. In Phase 2 it is the post itself; for a promoted thread it will be the thread, so there is one conversation |
| `visibility` | `Members` (every active member) or `Public` (a public sample post: also shown to anonymous visitors through the public feed, section 5.10). Only `SystemAdmin` may set `Public` |
| `state` | `Scheduled` (`publishAtUtc` in the future), `Processing` (an attachment is still Processing), `Published`, `Hidden` (a moderator or the auto-hide rule hid it pending review), `Removed` (a moderator removed it). Deleted posts return `404` |
| `publishedUtc` | Set once, when the post first becomes visible. Feed order uses it |
| `editedUtc` | Last edit made more than `content.editGraceSeconds` (120) after publishing; null otherwise. Clients show "edited" when set, and tapping it opens the edit history (5.5) |
| `revisionCount` | Number of stored revisions (0 when never edited after the grace period) |
| `linkPreview` | Preview card for the first `http` or `https` URL in the body, or null (section 5.9) |
| `commentCount` | Visible comments (not deleted, not removed, not hidden) |
| `moderation` | Null for ordinary viewers. For the author and moderators when Hidden or Removed: `{ state, reason, actedUtc }` |

Who sees non-Published posts: the author (all states), `SystemAdmin` (all), `FeedModerator` (Hidden and Removed). Everyone else gets `404`.

A post with an attachment in `Processing` is saved in state `Processing` and publishes automatically (5.6) once every attachment is Ready. If an attachment fails, the post stays in `Processing`, the author receives no fan-out, and the author's view shows the failed Media so they can edit the post to remove or replace it.

### 5.2 List the feed

`GET /api/v1/feed` and `POST /api/v1/feed/enumerate` (Member) accept `EnumerationQuery`; `maxResults` defaults to 20 and is capped at 50.

`200 EnumerationResult<FeedPost>`. Default `Feed` ordering places pinned Published posts first by `pinnedUtc` descending, then unpinned Published posts by `publishedUtc` descending and `id` descending. All posts share one SQL keyset and count toward `maxResults`; pins can span pages, and no page contains an extra pinned prefix. `totalRecords` includes all matching pinned and unpinned posts. `PublishedDescending` uses publication order without pin priority. Posts by authors the caller blocked are omitted. Clients refresh by refetching the first page.

`GET /api/v1/me/feed-posts` and `POST /api/v1/me/feed-posts/enumerate` (Signed in) enumerate the caller's own posts. Use `filters.state` for `Scheduled`, `Processing`, `Published`, `Hidden`, or `Removed`; default ordering is `CreatedDescending`.

### 5.3 Read

`GET /api/v1/feed/{id}` (Member) returns `200 FeedPost` or `404`.

### 5.4 Create

`POST /api/v1/feed` (Member; `SystemAdmin`, or `canPostToFeed` and `canPost`)

```json
{
  "title": "Welcome to the community",
  "body": "Start here: introduce yourself below. Thanks @alexchen for the photos. https://example.test/about",
  "mediaIds": ["med_a", "med_b"],
  "visibility": "Members",
  "commentsEnabled": true,
  "publishAtUtc": null,
  "pin": false,
  "showLinkPreview": true
}
```

| Field | Rule |
|---|---|
| `title` | Optional, up to 140 |
| `body` | Section 3. Required unless `mediaIds` is non-empty |
| `mediaIds` | Ordered; the order is the display order. Up to `feed.maxAttachments` (10) items and at most `feed.maxVideosPerPost` (1) video. Each must belong to the caller (or, for `SystemAdmin`, any unattached media), be unattached, and be `Processing` or `Ready` |
| `visibility` | Default `Members`. `Public` only for `SystemAdmin` (`403 Forbidden` otherwise) |
| `showLinkPreview` | Default true. False stores the post without a preview card even when the body has a URL |
| `commentsEnabled` | Default true |
| `publishAtUtc` | Null publishes now. Otherwise at least 1 minute and at most 365 days in the future (`fieldErrors.publishAtUtc`) |
| `pin` | `SystemAdmin` only; ignored for others. Subject to `feed.maxPinned` |

The word filter (9.4) runs on `title`, `body`, and attachment `altText`. Success `201 FeedPost` (state `Scheduled`, `Processing`, or `Published`). Errors: `400 ValidationFailed`, `400 MediaLimitExceeded`, `400 ContentFiltered`, `403 Forbidden` (no posting rights, or `Public` without `SystemAdmin`), `403 PostingNotAllowed`, `403 MediaNotAllowed`, `403 VideoUploadNotAllowed`, `403 Muted`, `409 MediaNotAttachable` (with `fieldErrors["mediaIds[1]"]`), `409 PinLimitReached`, `429 RateLimited`.

### 5.5 Edit and history

`PUT /api/v1/feed/{id}` with any of `title`, `body`, `mediaIds` (replaces the list; removed media is detached and deleted by cleanup), `visibility`, `commentsEnabled`, `publishAtUtc` (only while Scheduled; null publishes now), `showLinkPreview`. Returns `200 FeedPost`.

- **Posts are always editable** by their author (who must still have `canPost`; `SystemAdmin` any post), in every state and with no time limit. There is no edit window.
- Edits within `content.editGraceSeconds` (120) of publishing are silent: no revision, no `editedUtc`.
- Every later edit that changes `title`, `body`, or attachments first copies the previous `title` and `body` into a revision (`fpr_`, numbered from 1), then sets `editedUtc`. Attachment changes are recorded in the revision's `attachmentSummary` text (for example "Removed 1 image, added 1 video").
- **Edit history is visible to everyone who can see the post**, so unlimited editing cannot silently rewrite what people replied to: `GET /api/v1/feed/{id}/revisions?continuationToken=&maxResults=` (Member) returns `200 EnumerationResult<{ id, revision, title, body, attachmentSummary, editedBy: UserSummary, createdUtc }>`, newest first (default 20, max 50). The current version is the post itself. Revisions of Hidden or Removed posts follow the post's visibility rules.
- The word filter (9.4) runs on every edit. Mentions added by an edit notify (3.3). A changed first URL refreshes `linkPreview`.

### 5.6 Scheduling and publishing

A `FeedPublishWorker` runs every `feed.publishIntervalSeconds` (30). It publishes posts whose `publishAtUtc` has passed and whose attachments are all Ready, and it is also signalled when a media item becomes Ready. Publishing sets `publishedUtc` (to the later of `publishAtUtc` and the time all media became Ready), writes the `feedPost.publish` audit record, starts the `NewFeedPost` fan-out (section 8) including the feed post emails (8.9), and sends mention notifications. `POST /api/v1/admin/feed/{id}/publish-now` publishes a Scheduled post immediately (still waiting for media).

### 5.7 Delete

`DELETE /api/v1/feed/{id}` (author or `SystemAdmin`) returns `204`. The post is soft-deleted (`deletedUtc`): it disappears from every list, `GET` returns `404`, its comments become unreachable, its pin is cleared, and its media (served objects and originals) is deleted after `moderation.retainRemovedDays` (365). Audited as `feedPost.delete`.

### 5.8 Pin and unpin

`POST /api/v1/feed/{id}/pin` and `POST /api/v1/feed/{id}/unpin` (`SystemAdmin`) return `200 FeedPost`. Only Published posts can be pinned. Pinning beyond `feed.maxPinned` (3) returns `409 PinLimitReached`. Both are idempotent and audited (`feedPost.pin`, `feedPost.unpin`).

### 5.9 Link previews

The server builds a preview card for the first `http` or `https` URL in a post or comment body (`showLinkPreview` true), and composers ask for one while the member types.

`POST /api/v1/link-previews` (Member) `{ "url": "https://example.test/about" }` returns `200 LinkPreview` (from cache when fresh) or `400 LinkPreviewUnavailable`:

```json
{
  "url": "https://example.test/about",
  "canonicalUrl": "https://example.test/about",
  "siteName": "Example Community",
  "title": "About us",
  "description": "Who we are and how the community works.",
  "imageUrl": "https://media.example.test/link-previews/3f9a.../card.jpg",
  "imageWidth": 1200,
  "imageHeight": 630,
  "fetchedUtc": "2026-11-12T18:00:00Z"
}
```

Fetching rules (server side only; clients never fetch the target page for a preview):
- **SSRF protection.** Only `http` and `https` on ports 80 and 443, no credentials in the URL. The host is resolved once and every resolved address is checked: loopback, private (RFC 1918), carrier-grade NAT, link-local (including `169.254.169.254` and other metadata addresses), multicast, broadcast, reserved, documentation, unique-local IPv6 (`fc00::/7`), IPv4-mapped and NAT64 forms of those, and `0.0.0.0/8` are refused. The connection is made to the checked address (pinned, so DNS rebinding cannot swap it) with the original host name for TLS SNI and the `Host` header. Redirects are followed manually, at most `linkPreviews.maxRedirects` (3), and every hop is checked again. Domains in `linkPreviews.blockedDomains` are refused; the property's own domains are allowed but fetched like any other site.
- **Limits.** Connect and total timeout `linkPreviews.timeoutSeconds` (5); only `text/html` responses; at most `linkPreviews.maxHtmlBytes` (1 MB) read; no cookies sent or stored; a fixed `User-Agent` (`linkPreviews.userAgent`, default `TrellisKitLinkPreview/1.0`). Robots rules are not consulted (one fetch per URL per cache period).
- **Parsing.** Open Graph (`og:title`, `og:description`, `og:image`, `og:site_name`, `og:url`), then Twitter card tags, then `<title>` and `<meta name="description">`. Text is decoded, stripped of markup, normalized like bodies (3.2), and cut to 200 (title) and 300 (description) characters.
- **Images.** The `og:image` is fetched under the same rules (at most `linkPreviews.maxImageBytes`, 5 MB; JPEG, PNG, or WebP), re-encoded by the image pipeline (metadata stripped, long edge at most 1200) and stored in the images bucket under `link-previews/{urlHash}/`. Clients never load third-party images, so member IP addresses are not exposed to linked sites.
- **Cache.** Keyed by the normalized URL (lowercase scheme and host, default port removed, fragment removed). Successful previews are reused for `linkPreviews.cacheHours` (168); failures for `linkPreviews.failureCacheMinutes` (60). A post or comment stores a reference to the cached preview at write time, so later cache refreshes do not change published cards until the content is edited.
- **Moderation.** The word filter (9.4) runs on the preview title and description; a `Block` match drops the preview (the content is still saved). `linkPreviews.enabled = false` turns the feature off (`linkPreview` is always null).

### 5.10 Public sample posts

A post with `visibility: "Public"` (set by `SystemAdmin` on create or edit) is a teaser for the marketing site. It stays in the members' feed as usual and is also readable without signing in:

| Method and path | Group | Notes |
|---|---|---|
| `GET /api/v1/public/feed?continuationToken=&maxResults=` | Anonymous | Published, not Hidden or Removed, `Public` posts, newest `publishedUtc` first; `maxResults` default 10, max 20. The response is `EnumerationResult<PublicFeedPost>` |
| `GET /api/v1/public/feed/{id}` | Anonymous | One `PublicFeedPost`, or `404` |

`PublicFeedPost` is the FeedPost without `myReactions`, `viewer`, `moderation`, `mentions` user ids (handles render as plain text), and comments (the card shows `commentCount` and invites the visitor to join). Media of a Public post is copied to the public paths (`/public/media/{id}/` in the images bucket, `/public/video/{id}/` in the transcoded bucket), which the media distribution serves without signed cookies, and removed from them when the post stops being Public, is hidden, removed, or deleted. Originals are never public. Responses carry `Cache-Control: public, max-age=60`. The website's marketing pages render these posts; anonymous reads are rate limited per IP (section 12).

---

## 6. Reactions

### 6.1 The set

The set comes from the property: the `reactions` array of `property.json` (`docs/TRELLISKIT.md` section 2), in its order. There is no reactions setting. `GET /api/v1/reactions/set` and `POST /api/v1/reactions/set/enumerate` (Member) return a standard enumeration with `likeKey` context; clients follow continuation tokens to load the full set. The catalog supports key/created ordering, search, dates, labels and tags. Immutable declarations use Unix epoch and empty metadata. `GET /api/v1/property` carries the original property order for clients that already loaded the property. Example standalone catalog page:

```json
{ "success": true, "maxResults": 1, "skip": 0, "objects": [ { "key": "fire", "emoji": "🔥", "label": "Fire", "labels": [], "tags": {} } ], "endOfResults": false, "continuationToken": "opaque-cursor", "totalRecords": 4, "recordsRemaining": 3, "timestampUtc": "2026-11-23T00:00:00Z", "likeKey": "like" }
```

Each property defines its own reactions and their order in `property.json` (the sample property's set is an example).

Reactions are stored and addressed by **key**, not by emoji. Keys are short ASCII words (`^[a-z][a-z0-9]{1,19}$`, unique; validated with the rest of `property.json` at startup), so URLs need no percent-encoded emoji and a property can change the glyph or label for a key without touching stored data. `likeKey` is the one-tap shortcut: the first key in the property's list. Removing a key from the property hides its counts everywhere but keeps the rows, so restoring the key restores the counts. Clients cache the set for the session. The pickers show every reaction with its `label` as the accessible name.

### 6.2 React and unreact

| Method and path | Notes |
|---|---|
| `PUT /api/v1/feed/{id}/reactions/{key}` | Adds the caller's reaction. Idempotent |
| `DELETE /api/v1/feed/{id}/reactions/{key}` | Removes it. Idempotent |
| `PUT /api/v1/comments/{id}/reactions/{key}` and `DELETE ...` | Same for comments |

Both return `200 { "reactionCounts": {...}, "reactionTotal": 16, "myReactions": ["like", "fire"] }` so clients replace their copy. A member may hold several different keys on one target (one row per key). Reacting to your own content is allowed and does not notify.

Errors: `400 UnknownReaction`, `403 Muted`, `403 InteractionBlocked` (the author blocked the caller), `404 NotFound` (target not visible or not Published), `429 RateLimited`. React on a post with `commentsEnabled: false` is allowed.

### 6.3 Who reacted

`GET /api/v1/feed/{id}/reactions?key=&continuationToken=&maxResults=` and `GET /api/v1/comments/{id}/reactions?...` (Member). `key` optional (all keys when omitted). `maxResults` default 50, max 100. Newest first.

```json
{ "success": true, "maxResults": 50, "skip": 0, "objects": [ { "user": { "...": "UserSummary" }, "key": "fire", "createdUtc": "2026-11-12T18:10:00Z", "labels": [], "tags": {} } ], "continuationToken": null, "endOfResults": true, "totalRecords": 1, "recordsRemaining": 0, "timestampUtc": "2026-11-12T18:10:00Z" }
```

Users the caller blocked are omitted.

---

## 7. Threaded comments

### 7.1 Model

Comments attach to a target (`FeedPost` in Phase 2; `Thread`, `Lesson`, `Event` later, same routes under their own segment). Replies form a Reddit-style tree:

| Column | Meaning |
|---|---|
| `parentCommentId` | Null for a top-level comment |
| `rootCommentId` | The top-level ancestor (itself for a top-level comment) |
| `depth` | 0 for top-level, parent depth plus 1 for replies |
| `path` | Materialized path: ancestor ids and its own id joined with `/` and ending in `/` (for example `cmt_A/cmt_B/cmt_C/`). Ids are fixed-length and time-sortable, so ordering by `path` yields depth-first, oldest-first order and a subtree is the range `path >= 'cmt_A/cmt_B/' AND path < 'cmt_A/cmt_B0'` (`0` sorts right after `/`), which uses an ordinary index on both providers. In PostgreSQL the column is declared `TEXT COLLATE "C"` so comparisons are byte-wise (locale collations ignore `/`); SQLite's default `BINARY` collation already is |
| `childCount`, `descendantCount` | Visible direct replies and visible replies at any depth, maintained on write |
| `score` | `reactionTotal + 2 * childCount`, maintained on write; drives `sort=top` |

**Max depth:** `comments.maxDepth` (default 8). A reply to a comment already at `maxDepth` is stored as a sibling of that comment (same parent, same depth) with `replyToCommentId` set to the comment the member actually replied to, and the response and UI show "replying to @handle". Members can always reply; the tree never grows past the limit.

### 7.2 Comment

```json
{
  "id": "cmt_...",
  "targetType": "FeedPost",
  "targetId": "fpo_...",
  "parentCommentId": "cmt_...",
  "replyToCommentId": null,
  "replyToUser": null,
  "depth": 1,
  "state": "Visible",
  "author": { "...": "UserSummary" },
  "body": "Does this work for beginners too?",
  "mentions": [],
  "attachments": [],
  "linkPreview": null,
  "createdUtc": "2026-11-12T18:20:00Z",
  "editedUtc": null,
  "revisionCount": 0,
  "childCount": 2,
  "descendantCount": 5,
  "reactionCounts": { "like": 3 },
  "reactionTotal": 3,
  "myReactions": [],
  "viewer": { "canEdit": false, "canDelete": false, "canReply": true, "canReact": true, "canReport": true, "canModerate": false },
  "children": [],
  "moreChildren": { "remainingCount": 2, "cursor": null }
}
```

`state` and placeholder rendering:

| `state` | Who sees it this way | Payload | UI text |
|---|---|---|---|
| `Visible` | Everyone | Full | |
| `Deleted` | Everyone (the author deleted it, or the account was deleted) | `author`, `body`, `mentions`, `attachments`, `linkPreview` null or empty; reactions zeroed; revisions not available; every `viewer` flag false (placeholders cannot be replied to) | "[deleted]" |
| `Removed` | Everyone except moderators | Same as Deleted | "[removed by a moderator]" |
| `Hidden` | Everyone except the author and moderators | Same as Deleted | "[under review]" |
| `BlockedAuthor` | The member who blocked the author | Same as Deleted | "Comment from a member you blocked" |

A placeholder appears only when it has at least one visible descendant (so the tree keeps its shape); placeholders with no visible descendants are omitted from listings entirely. The author of a Hidden or Removed comment sees it with full content plus `moderation: { state, reason, actedUtc }`. Moderators see full content with the same `moderation` object.

### 7.3 List a tree page

`GET /api/v1/feed/{id}/comments` and `POST /api/v1/feed/{id}/comments/enumerate` (Member) accept `EnumerationQuery` and return `EnumerationResult<Comment>` with scalar `commentCount` context.

| Parameter | Default | Range | Meaning |
|---|---|---|---|
| `ordering` | `Top` | `Top`, `CreatedDescending`, `CreatedAscending` | Sibling order at every level. `Top`: score descending, then creation ascending and id |
| `maxResults` | 20 | 1 to 50 | Top-level comments in this page |
| `filters.depth` | 3 | 1 to 5 | Levels returned, counting the top level as 1 |
| `filters.childLimit` | 5 | 1 to 20 | Bounded reply preview under each returned comment |

Example response:

```json
{
  "success": true, "maxResults": 20, "skip": 0,
  "continuationToken": "opaque-cursor", "endOfResults": false,
  "totalRecords": 31, "recordsRemaining": 11, "timestampUtc": "2026-11-23T00:00:00Z",
  "commentCount": 154,
  "objects": [ { "...": "Comment with bounded children nested up to depth" } ]
}
```

`moreChildren` appears when visible replies exceed the bounded `children` preview:
- `remainingCount`: replies not shown at that level.
- `continuationToken`: continue after the last returned reply, or null to start from the first reply when depth ran out. `cursor` is a compatibility alias of the same token.
- `enumerate`: `POST /api/v1/comments/{id}/children/enumerate` for that comment.

Load more replies with `GET /api/v1/comments/{id}/children` or its canonical POST `/enumerate` route. The response is `EnumerationResult<Comment>` rooted at the direct replies of `{id}`. Send `continuationToken`, the same ordering, and the same filters; a token for a different parent or query returns `400 InvalidContinuationToken`.

The server builds a page with one query per level (window function `ROW_NUMBER() OVER (PARTITION BY parentcommentid ORDER BY <sort>)` capped at `childLimit + 1`), so a page costs at most `depth` queries regardless of thread size. SQLite 3.25 and later and PostgreSQL both support this.

### 7.4 Read one comment (permalink)

`GET /api/v1/comments/{id}?depth=&childLimit=&sort=` (Member) returns:

```json
{
  "target": { "type": "FeedPost", "id": "fpo_..." },
  "ancestors": [ { "...": "Comment without children, root first" } ],
  "comment": { "...": "Comment with children" }
}
```

Notification deep links (`?comment=cmt_...`) use this to render the comment in context with a "View all comments" link back to the full tree.

### 7.5 Create

`POST /api/v1/feed/{id}/comments` (Member)

```json
{ "body": "Does this work for beginners too?", "parentCommentId": "cmt_...", "mediaIds": [] }
```

| Field | Rule |
|---|---|
| `body` | Section 3. Headings are not supported in comments |
| `parentCommentId` | Optional; must belong to the same target. Deleted or removed parent: `409 ParentUnavailable`. Parent at max depth: flattened as in 7.1 |
| `mediaIds` | Up to `comments.maxAttachments` (default 1: one image or one video; 0 disables comment media). Same ownership rules as posts; needs `canPostMedia`, and video needs `canUploadVideo` |
| `showLinkPreview` | Default true (section 5.9) |

The word filter (9.4) runs on `body` and attachment `altText`. Success `201 Comment` (with `children: []`). Errors: `400 ValidationFailed`, `400 ContentFiltered`, `403 PostingNotAllowed`, `403 MediaNotAllowed`, `403 VideoUploadNotAllowed`, `403 Muted`, `403 InteractionBlocked` (parent author blocked the caller), `404 NotFound`, `409 CommentsDisabled`, `409 ParentUnavailable`, `409 MediaNotAttachable`, `429 RateLimited`.

A comment with Processing media is visible immediately; the attachment renders its Processing state until Ready (comments are conversational; holding them back would break the thread).

### 7.6 Edit and delete

`PUT /api/v1/comments/{id}` `{ body?, mediaIds?, showLinkPreview? }` (author only, who must still have `canPost`): `200 Comment`. **Comments are always editable**; there is no edit window. Edits within `content.editGraceSeconds` (120) are silent; later edits store a revision (`cmr_`, previous `body` and an `attachmentSummary`) and set `editedUtc`.

**Edit history is visible to everyone who can see the comment:** `GET /api/v1/comments/{id}/revisions?continuationToken=&maxResults=` (Member) returns `200 EnumerationResult<{ id, revision, body, attachmentSummary, createdUtc }>`, newest first (default 20, max 50). Clients show "edited" on the comment and open the history from it. Moderators do not edit other members' words; they hide or remove.

`DELETE /api/v1/comments/{id}` (author or `SystemAdmin`): `204`. Soft delete; counts on the target and ancestors are decremented; the node becomes a `Deleted` placeholder if it has visible replies, and its revisions are deleted with it. Attached media is deleted after `moderation.retainRemovedDays`.

---

## 8. Notifications

### 8.1 Notification

```json
{
  "id": "ntf_...",
  "type": "CommentReply",
  "category": "Replies",
  "title": "Alex Chen replied to your comment",
  "body": "Does this work for beginners too?",
  "actor": { "...": "UserSummary" },
  "actors": [ { "...": "UserSummary" } ],
  "actorCount": 1,
  "target": { "type": "Comment", "id": "cmt_..." },
  "context": { "feedPostId": "fpo_...", "commentId": "cmt_...", "parentCommentId": "cmt_..." },
  "path": "/feed/fpo_...?comment=cmt_...",
  "isRead": false,
  "readUtc": null,
  "createdUtc": "2026-11-12T18:20:00Z",
  "updatedUtc": "2026-11-12T18:20:00Z"
}
```

| Field | Rule |
|---|---|
| `title` | Server-rendered English sentence. Clients display it as-is (bold the actor name if they like) |
| `body` | Excerpt of the related content, or null |
| `actors`, `actorCount` | Up to the 3 most recent actors and the total, for aggregated types |
| `context` | Ids a client needs to deep link; keys not relevant to the type are omitted |
| `path` | App-relative route. The website prefixes `/app` (`/app/feed/fpo_...?comment=...`); the mobile app passes it to Expo Router unchanged. Paths used in Phase 2: `/feed/{postId}`, `/feed/{postId}?comment={commentId}`, `/notifications`, `/account/membership` |

Content that later disappears (deleted, removed, blocked) leaves the notification in place; opening it shows "This content is no longer available" after the `404`.

### 8.2 Types

| `type` | `category` | Trigger | Recipient | Aggregated | `title` example |
|---|---|---|---|---|---|
| `PostComment` | `Replies` | Top-level comment on a post | Post author | No | "Alex Chen commented on your post" |
| `CommentReply` | `Replies` | Reply to a comment (including flattened replies via `replyToCommentId`) | Author of the replied-to comment | No | "Alex Chen replied to your comment" |
| `Mention` | `Mentions` | `@handle` in a published post or a comment | Mentioned member | No | "Alex Chen mentioned you in a comment" |
| `Reaction` | `Reactions` | Reaction added to your post or comment | Content author | Yes, per target while unread | "Alex Chen and 4 others reacted to your comment" |
| `NewFeedPost` | `FeedPosts` | Feed post published | Every active member except the author (also emailed, 8.9) | No | "Example Owner posted: Welcome to the community" |
| `ModerationAction` | `Moderation` | Your content was hidden, removed, or restored by a moderator (not auto-hide) | Content author | No | "A moderator removed your comment (Spam)" |
| `ModerationOverdue` | `Moderation` | An Open report passed `moderation.reviewTargetHours` (9.1) | Every moderator in scope | No | "A report on a comment is past the 12 hour review target" |
| `MembershipExpiring` | `Account` | Code membership ends in 14 or 3 days (the Phase 1 reminder worker now also writes this) | Member | No | "Your free membership ends on March 1, 2027" |

Reserved for later phases (clients render a generic row): `ThreadReply`, `DirectMessage`, `EventReminder`, `TicketUpdate`, `SubmissionDecision`, `Broadcast`.

Rules:
- One notification per recipient per source item. When a reply also mentions the parent's author, only `CommentReply` is created. Priority: `CommentReply`, then `PostComment`, then `Mention`.
- Never notify the actor about their own action, and never across a block in either direction.
- `Reaction` aggregation uses a group key (`Reaction:<targetType>:<targetId>`): while the recipient's notification for that target is unread, new reactions update it (`actorCount`, `actors`, `updatedUtc`) instead of creating a new one. Removing a reaction does not retract anything.
- `NewFeedPost` fan-out runs in a background job in batches of `notifications.fanoutBatchSize` (500), so publishing returns immediately.

### 8.3 Notification center routes (Member)

| Method and path | Body | Success |
|---|---|---|
| `GET /api/v1/notifications?continuationToken=&maxResults=&unreadOnly=` | | `200 EnumerationResult<Notification>` with scalar `unreadCount` context. Ordered by `updatedUtc` desc, then `id` desc. `maxResults` default 30, max 100 |
| `GET /api/v1/notifications/unread-count` | | `200 { "unreadCount": 7, "latestUpdatedUtc": "..." }` |
| `POST /api/v1/notifications/read` | `{ "ids": ["ntf_..."] }` (1 to 100) | `200 { unreadCount }`. Unknown or foreign ids are ignored |
| `POST /api/v1/notifications/unread` | `{ "ids": [...] }` | `200 { unreadCount }` |
| `POST /api/v1/notifications/read-all` | `{ "beforeUtc"?: "..." }` (defaults to now) | `200 { unreadCount }` |

Opening a notification marks it read (the client calls `read` with its id, then navigates to `path`). Clients display counts above 99 as "99+".

Retention: read notifications are deleted after `notifications.retentionDays` (180); unread ones after twice that.

### 8.4 Unread counts: polling in Phase 2

**Decision: polling, no WebSocket in Phase 2.** The WebSocket endpoint is planned for direct messages in Phase 5, and doing it properly means authentication on the upgrade request, fan-out across ECS tasks (PostgreSQL `LISTEN/NOTIFY`), ALB idle timeouts, and reconnection logic in two apps. A badge number does not justify building that early, and the polling cost is small: the count is one indexed query, and 1,000 members with the app open at once produce about 17 requests per second. Phase 5 adds a `notification.created` WebSocket event and keeps this route as the fallback.

| Client | When it calls `GET /api/v1/notifications/unread-count` |
|---|---|
| Web | On sign-in, every 60 seconds while the tab is visible (`document.visibilityState === "visible"`), on tab focus, and after opening the notification center |
| Mobile | On app foreground, every 60 seconds while foregrounded, and when a push arrives in the foreground |

Mobile also gets the app icon badge from each push's `badge` value.

### 8.5 Preferences (the notification settings section)

Both apps have a full notification settings section (website `/app/settings/notifications`, mobile Account > Notifications) built from this document: one row per category with an in-app, push, and email toggle, a short description of what the category covers, the notification hours control (8.6), a "Pause all push notifications" switch, and, on mobile, the device permission state (8.10). **Every notification is on by default**, in every channel the category supports.

`GET /api/v1/me/notification-preferences` and `POST /api/v1/me/notification-preferences/enumerate` (Signed in) use `EnumerationQuery` and return `EnumerationResult<CategoryPreferenceView>`. Categories live in `objects`; the envelope also carries `pushPaused`, `autoFollowOnComment` and `notificationHours`. Follow continuation tokens before presenting the full preference matrix. Search matches the category name; immutable categories use Unix epoch and empty metadata. Example page (other categories follow the cursor):

```json
{
  "success": true, "maxResults": 1, "skip": 0,
  "objects": [{ "category": "Replies", "inApp": true, "push": true, "email": false, "editable": { "inApp": true, "push": true, "email": false }, "labels": [], "tags": {} }],
  "continuationToken": "opaque-cursor", "endOfResults": false,
  "totalRecords": 6, "recordsRemaining": 5, "timestampUtc": "2026-11-23T00:00:00Z",
  "pushPaused": false, "autoFollowOnComment": true,
  "notificationHours": { "enabled": true, "start": "07:00", "end": "22:00", "timeZone": "America/Los_Angeles" }
}
```

The values shown are the defaults for a new account. Email is sent in Phase 2 for `FeedPosts` (new feed posts, opt-out, 8.9) and `Account` (the existing membership emails, which stay on). Email cells marked not editable are reserved for later phases (for example a digest). `Moderation` and `Account` in-app notifications cannot be turned off, because they carry notices the member must see.

`PUT /api/v1/me/notification-preferences` with any subset:

```json
{ "categories": [ { "category": "Reactions", "push": false } ], "notificationHours": { "start": "08:00" }, "pushPaused": false }
```

Returns `200` with the full document. Changing a non-editable cell, an unknown category, a malformed `HH:mm`, or a time zone that is not an IANA id returns `400 ValidationFailed` (for example `fieldErrors["categories[0].inApp"]`). Turning `inApp` off for a category also stops push for it (push requires an in-app notification). `pushPaused` stops every push without changing the per-category choices. Clients send the device time zone (`Intl.DateTimeFormat().resolvedOptions().timeZone`) the first time they show the screen, and the mobile app updates it on foreground when it changes.

### 8.6 Notification hours (per user)

Each member chooses the hours during which pushes may arrive: `notificationHours.start` to `end` in their `timeZone` (default **7:00 to 22:00 local**, enabled). The window may cross midnight; `start` equal to `end` with `enabled` true means all day; `enabled: false` also means all day. Outside the window, pushes are **suppressed, not delayed**: the in-app notification is still created and the badge catches up on the next foreground. A burst of delayed pushes at the start of the window is worse than none. No category is exempt in Phase 2. Notification hours do not apply to email (8.9) or to in-app notifications.

### 8.7 Push tokens (Signed in)

| Method and path | Body | Success | Errors |
|---|---|---|---|
| `POST /api/v1/me/push-tokens` | `{ "token": "ExponentPushToken[xxxx]", "platform": "Ios" \| "Android", "deviceId"?, "deviceName"?, "appVersion"? }` | `201 { id, platform, deviceName, appVersion, createdUtc, lastSeenUtc }` when new, `200` when the token already exists (updates `lastSeenUtc`, and moves it to the caller if another account had it) | `400 ValidationFailed` (token must match `^Expo(nent)?PushToken\[[A-Za-z0-9_-]+\]$`) |
| `DELETE /api/v1/me/push-tokens` | `{ "token": "..." }` | `204` (also when unknown) | |
| `GET /api/v1/me/push-tokens` | | `200 EnumerationResult<PushTokenView>` with device metadata in `objects` (token values are never returned) | |

- Each token is bound to the session that registered it. Revoking that session (sign-out, password change, admin session revoke, ban, account deletion) deletes its tokens, so a signed-out device never receives pushes even if the client failed to unregister.
- At most 10 tokens per member; registering an eleventh deletes the least recently seen.
- Mobile registers after the notification permission prompt (`Notifications.getExpoPushTokenAsync({ projectId })`) and on every app start, and calls `DELETE` before sign-out. Android uses channel id `default` (created by the app at startup).

### 8.8 What triggers a push

A push is sent for a notification when all of these hold: the category's `push` preference is on, `pushPaused` is false, the current time is inside the recipient's notification hours, the recipient has at least one token, `push.enabled` is true, and the recipient has received fewer than `push.maxPerUserPerHour` (10) pushes in the last hour. `Reaction` pushes are sent at most once per target per hour. Updating an aggregated notification does not push again within that hour.

Push message (Expo push API, batches of 100):

```json
{
  "to": "ExponentPushToken[xxxx]",
  "title": "Alex Chen replied to your comment",
  "body": "Does this work for beginners too?",
  "data": { "notificationId": "ntf_...", "type": "CommentReply", "path": "/feed/fpo_...?comment=cmt_..." },
  "sound": "default",
  "badge": 7,
  "channelId": "default",
  "priority": "high"
}
```

`badge` is the recipient's unread count after this notification. The delivery worker stores each Expo ticket, checks receipts after `push.receiptDelayMinutes` (15), deletes tokens that report `DeviceNotRegistered`, and counts other errors per token (disabled after 5 consecutive failures). Tapping a push opens `data.path` and marks the notification read.

### 8.9 Email for new feed posts (opt-out)

When a feed post publishes, every active member whose `FeedPosts` email preference is on (the default) receives one email, queued by the fan-out job through the administrative email system (template `feed_post`, `Optional`; tokens `headline`, `authorName`, `postTitle`, `excerpt`, `link`, `unsubscribeLink`: the property's brand layout and template override, the queue with retries, suppressions, the email log, and an idempotency key per post and member; Mailpit locally):

- Subject: the post title, or "New post from <author displayName>" when there is none. Body: author, title, `excerpt`, the first image variant (`medium`, through a short-lived signed URL or omitted when signing is unavailable), and a button to the post (`{web.publicBaseUrl}/app/feed/{id}`). Text and HTML parts; the property `name` and brand color come from `property.json`.
- Every message carries a one-click unsubscribe link and `List-Unsubscribe` plus `List-Unsubscribe-Post: List-Unsubscribe=One-Click` headers (RFC 8058). `POST /api/v1/email/unsubscribe?token=` (Anonymous) and the `GET` page behind the link turn `FeedPosts` email off for that member; the token is an HMAC over `{ userId, category }` with a key derived from `auth.tokenEncryptionKey` and needs no session.
- Not sent to the author, to members who blocked the author, to banned or deleted accounts, or to members without a verified email. At most one feed post email per member per `email.feedPostMinIntervalMinutes` (0, no limit by default); posts in between are skipped for that member (they still get the in-app notification and push). `email.feedPostEmailsEnabled = false` stops all of them.
- Scheduled posts email when they publish. Edits never email again.

### 8.10 Device permission (mobile)

The mobile app asks for the OS notification permission during onboarding (the existing `allow-notifications` setup step) with a short explanation first, and registers its push token only after the permission is granted. The notification settings section shows the permission state: when it is not granted, a banner explains that pushes are off on this device and offers "Turn on notifications" (asks again when the OS still allows it, otherwise opens the app's system settings with `Linking.openSettings()`). Turning a category's push toggle on while the permission is missing triggers the same prompt. The app re-checks the permission on every foreground and registers or deletes its token to match. The website has no browser push in Phase 2; its settings section edits the same preferences (the push column applies to the member's mobile devices and says so).

---

## 9. Moderation hooks

### 9.1 Reports (Member)

`POST /api/v1/reports`

```json
{ "targetType": "Comment", "targetId": "cmt_...", "reason": "Harassment", "details": "Keeps going after another member." }
```

| Field | Rule |
|---|---|
| `targetType` | `FeedPost`, `Comment`, or `User` in Phase 2 |
| `reason` | `Spam`, `Harassment`, `HateSpeech`, `SexualContent`, `Violence`, `SelfHarm`, `Impersonation`, `IntellectualProperty`, `Other` |
| `details` | Optional, up to 1000; at least 10 characters when `reason` is `Other` |

Success `201 { "id": "rpt_...", "status": "Open", "createdUtc": "..." }`. If the caller already has an Open report on the same target, `200` with that report (no duplicate). Errors: `400 ValidationFailed`, `400 CannotReportSelf`, `404 NotFound`, `429 RateLimited`.

After reporting, clients thank the member ("Thanks. A moderator will review this.") and offer "Block @handle" for content and profiles.

**Auto-hide:** when the number of distinct reporters with Open reports on a FeedPost or Comment reaches `moderation.autoHideReportThreshold` (3; 0 disables), the content becomes `Hidden` with a system moderation action (`AutoHide`, no moderator, author not notified). A moderator then unhides or removes it. Content by `SystemAdmin` authors is never auto-hidden.

Reports are queued for moderators (section 10.2). The review target is `moderation.reviewTargetHours` (12): every queue row carries `dueUtc` (first report plus the target) and `isOverdue`, the queue sorts overdue rows first by default, the admin Home page shows open and overdue counts, and an overdue report sends a `ModerationOverdue` in-app notification and push to every moderator in scope once (category `Moderation`). This stays well inside the prompt action App Store Guideline 1.2 expects.

### 9.2 Blocking (Member and Signed in)

| Method and path | Success | Errors |
|---|---|---|
| `POST /api/v1/users/{id}/block` | `200 { "blocked": true }` (idempotent) | `400 CannotBlockSelf`, `400 CannotBlockStaff` (members cannot block `SystemAdmin`, `Moderator`, or `FeedModerator` holders; decision 9), `404 NotFound`, `429 RateLimited` |
| `DELETE /api/v1/users/{id}/block` | `200 { "blocked": false }` | |
| `GET /api/v1/me/blocks?continuationToken=&maxResults=` | `200 EnumerationResult<BlockView>` with `user` and `createdUtc` per object (default 50, max 100) | |

`GET /api/v1/users/{id}` (Phase 1) gains `"isBlockedByMe": true|false`.

Effects, where A blocks B:

| Surface | What A experiences | What B experiences |
|---|---|---|
| Feed | B's posts are omitted from A's feed | Unchanged |
| Comments | B's comments render as `BlockedAuthor` placeholders (or are omitted when they have no visible replies) | B gets `403 InteractionBlocked` replying to A's comments |
| Reactions | B is omitted from A's "who reacted" lists | B gets `403 InteractionBlocked` reacting to A's content |
| Mentions | A is not notified when B mentions A | B's mention renders but notifies nobody |
| Notifications | A receives nothing caused by B | B receives nothing caused by A |
| Profile | A can still open B's profile (shows "Unblock") | Unchanged |

Blocks are private: B is never told. A member may block up to 1000 accounts. When a blocked member later becomes staff, the existing block is kept but stops hiding their posts and moderator notices (staff content is always shown).

### 9.3 Moderation actions (Moderator group)

`POST /api/v1/admin/moderation/actions`

```json
{ "targetType": "Comment", "targetId": "cmt_...", "action": "Remove", "reason": "Spam", "note": "Third link drop today.", "notifyAuthor": true, "resolveReports": true }
```

| `action` | Effect | Reversible by |
|---|---|---|
| `Hide` | Content becomes `Hidden` (pending review). Media unaffected | `Unhide` |
| `Unhide` | Back to visible | |
| `Remove` | Content becomes `Removed`; served media moves to the retained bucket (4.7); counts update | `Restore`, within `moderation.retainRemovedDays` |
| `Restore` | Undo a Remove | |

- `targetType` is `FeedPost` or `Comment`. `FeedModerator` may hide, unhide, remove, and restore member posts and comments in the Feed, but never content authored by a `SystemAdmin` (decision 12); `SystemAdmin` may act on anything. Otherwise `403 Forbidden`.
- `reason` required (3 to 500): free text, or a report reason code.
- `notifyAuthor` (default true for `Remove` and `Restore`, false for `Hide` and `Unhide`) sends a `ModerationAction` notification.
- `resolveReports` (default true) marks every Open report on the target `ActionTaken` with this action.
- Returns `200 { action: ModerationActionView, target: FeedPost | Comment }`. Every action writes a `moderationactions` row and an audit record (`moderation.hide`, `moderation.unhide`, `moderation.remove`, `moderation.restore`).

Removed and deleted content is purged (bodies and revisions cleared, media and originals deleted) after `moderation.retainRemovedDays` (365, one year) by the cleanup worker, unless an Open report still references it. Removed content is restorable for that whole year.

### 9.4 Word filter (profanity and slurs)

An admin-editable list screens member text on write. It is engine behavior with an empty default list; each property's admins maintain their own terms.

| Method and path | Group | Notes |
|---|---|---|
| `GET /api/v1/admin/moderation/word-filter?search=&action=&sort=term` | Admin | Standard enumeration of `{ id, term, matchType, action, note, createdByName, createdUtc, updatedUtc, hitCount30d }` |
| `POST /api/v1/admin/moderation/word-filter` | Admin | `{ term, matchType: "Word" \| "Substring", action: "Block" \| "Flag", note? }` returns `201`. Duplicate term: `409 Conflict` |
| `PUT /api/v1/admin/moderation/word-filter/{id}` | Admin | Any field; `200` |
| `DELETE /api/v1/admin/moderation/word-filter/{id}` | Admin | `204` |
| `POST /api/v1/admin/moderation/word-filter/import` | Admin | `{ "terms": ["..."], matchType, action }` adds many (up to 1000), skipping duplicates; `200 { added, skipped }` |
| `POST /api/v1/admin/moderation/word-filter/test` | Admin | `{ text }` returns `200 { matches: [{ termId, term, action, start, length }] }` so admins can check a list before saving |

Matching: text and terms are compared after Unicode NFKC, case folding, removal of zero-width characters and diacritics, and a small substitution map (`0`=o, `1`=i, `3`=e, `4`=a, `5`=s, `7`=t, `@`=a, `$`=s); runs of the same letter longer than two are collapsed on both sides. `Word` terms match whole words (letters and digits on neither side); `Substring` terms match anywhere. Applied to feed post titles and bodies, comment bodies, media alt text, link preview text, and display names and bios (at registration, on profile edits, and on staff edits of a member's profile, decision 20), on create and on every edit.

| `action` | Effect |
|---|---|
| `Block` | The write is rejected with `400 ContentFiltered` and `fieldErrors.<field>` = "This contains a word that is not allowed here." The term is not echoed back |
| `Flag` | The write succeeds and the content gets a system report (`reason: "Filter"`, no reporter; for a display name or bio the report targets the member, `targetType: "User"`), so it appears in the moderation queue. Flags do not count toward auto-hide |

`SystemAdmin` authors are not filtered. A display name or bio is checked against the profile's owner, so a staff edit of a member's profile is screened as the member's words (a `SystemAdmin`'s own profile is not). Filter changes apply to new writes only; existing content is not rescanned. Every list change is audited (`moderation.wordFilter.create`, `.update`, `.delete`, `.import`). `moderation.wordFilterEnabled` (true) switches the filter off without deleting the list.

---

## 10. Admin console

### 10.1 Feed management (Admin)

| Method and path | Notes |
|---|---|
| `GET /api/v1/admin/feed?state=&authorUserId=&pinned=true&search=&fromUtc=&toUtc=&sort=-publishedUtc` | Standard `EnumerationQuery` / `EnumerationResult` paging. `state` any FeedPost state or `Deleted`. `search` matches title and body. `fromUtc`/`toUtc` filter `publishedUtc` (or `createdUtc` for unpublished states). Sort fields: `publishedUtc`, `createdUtc`, `publishAtUtc`, `commentCount`, `reactionTotal`. Items: `{ id, kind, title, excerpt, author: UserSummary, state, visibility, isPinned, publishAtUtc, publishedUtc, createdUtc, editedUtc, attachmentCount, hasVideo, commentCount, reactionTotal, openReportCount }` |
| `GET /api/v1/admin/feed/{id}` | FeedPost (any state, including Deleted with `deletedUtc`) plus bounded `revisions` and `moderationActions` previews (`objects`, `totalRecords`, `enumerate`, at most five each), `reportSummary: { open, total }` |
| `POST /api/v1/admin/feed` | Body of 5.4 plus optional `authorUserId` (must have `canPostToFeed` or `SystemAdmin`; default the caller). Requires a member session (the break-glass token cannot author, `403`). `201 FeedPost` |
| `PUT /api/v1/admin/feed/{id}` | Body of 5.5, no edit window. `200 FeedPost` |
| `DELETE /api/v1/admin/feed/{id}` | `204` |
| `POST /api/v1/admin/feed/{id}/pin`, `.../unpin` | `200 FeedPost` |
| `POST /api/v1/admin/feed/{id}/publish-now` | Scheduled only; `200 FeedPost`; `409` for other states |
| `GET /api/v1/admin/metrics/engagement?days=30` | `EnumerationResult<LegacyMetricRow>`: daily observations in `objects` (`series`, `date`, `value`), scalar `activeMembers`, `openReports`, and `scheduledPosts` context. POST `/api/v1/admin/metrics/engagement/enumerate` uses `filters.days` (7 to 365). Feeds the Home activity charts |

Audit actions added: `feedPost.create`, `feedPost.update`, `feedPost.delete`, `feedPost.pin`, `feedPost.unpin`, `feedPost.publish`, `media.update`, `media.delete`, `media.reprocess`, `moderation.hide`, `moderation.unhide`, `moderation.remove`, `moderation.restore`, `moderation.dismiss`.

Console page (`/admin/feed`): table with the filters above, row actions View, Edit, View JSON, Pin or Unpin, Publish now (Scheduled), Duplicate (opens Create prefilled, without media), Delete (confirm). Create and Edit use a drawer with the Markdown editor, a live preview from the shared renderer, the media picker with upload progress and processing state, alt-text fields, a schedule picker in the admin's local time zone (sent as UTC), and comments on or off. Detail shows revisions and moderation history.

### 10.2 Moderation queue (Moderator)

| Method and path | Notes |
|---|---|
| `GET /api/v1/admin/moderation/queue?status=Open\|Resolved&targetType=&reason=&overdue=true&sort=dueUtc` | Standard `EnumerationQuery` / `EnumerationResult` paging, one row per reported target, filtered to the caller's scope. Items: `{ targetType, targetId, targetState, preview: { title, excerpt, hasMedia }, author: UserSummary, reportCount, reporterCount, reasons: { "Spam": 2, "Harassment": 1 }, firstReportedUtc, lastReportedUtc, dueUtc, isOverdue, status }`. Sort fields: `dueUtc` (default, overdue first), `lastReportedUtc`, `firstReportedUtc`, `reportCount` |
| `GET /api/v1/admin/moderation/queue/{targetType}/{targetId}` | `{ target: FeedPost \| Comment \| UserSummary (full content, any state), context: { feedPostId, parentCommentId }, revisions, reports, actions (bounded previews with objects, totalRecords, enumerate), authorHistory: { removedCount90d, hiddenCount90d, reportsAgainst90d, isMuted, mutedUntilUtc } }` |
| `POST /api/v1/admin/moderation/queue/{targetType}/{targetId}/dismiss` | `{ note? }` marks Open reports `Dismissed`; does not change content (use an action to unhide auto-hidden content). Returns `200` with the detail |
| `GET /api/v1/admin/moderation/actions?moderatorUserId=&targetType=&action=&fromUtc=&toUtc=&sort=-createdUtc` | Standard enumeration of `ModerationActionView`: `{ id, moderator: UserSummary \| null (system), targetType, targetId, targetAuthor: UserSummary, action, reason, note, notifiedAuthor, createdUtc }` |
| `GET /api/v1/admin/moderation/content?authorUserId=&targetType=&state=&search=&sort=-createdUtc` | Standard enumeration of a member's posts and comments in any state (to review an author's history). Items: `{ targetType, targetId, state, excerpt, createdUtc, reactionTotal, reportCount }` |
| `POST /api/v1/admin/moderation/actions` | Section 9.3 |

Reports on `User` targets show the profile; the available response is a member action in the Members section (mute, ban; `SystemAdmin` only), and dismissing.

**Console access (changes Phase 1 section 8):** members holding `Moderator` or `FeedModerator` (several at once; the role is granted and revoked per user, section 10.4) may sign in to the console. They see only Home (moderation tiles) and Moderation; every other section is hidden and its routes return `403`. The console decides from `Me.roles`.

### 10.3 Media library (Admin)

| Method and path | Notes |
|---|---|
| `GET /api/v1/admin/media?kind=&status=&ownerUserId=&attached=true\|false&fromUtc=&toUtc=&sort=-createdUtc` | Standard `EnumerationQuery` / `EnumerationResult` paging. Items: `{ media: Media, owner: UserSummary, attachedTo: { targetType, targetId } \| null, storedBytes, fileName, createdUtc }`. Sort fields: `createdUtc`, `sizeBytes` |
| `GET /api/v1/admin/media/summary` | `{ totalCount, totalStoredBytes, byKind: { Image, Video }, byStatus: { Uploading, Processing, Ready, Failed }, failed24h }` |
| `GET /api/v1/admin/media/{id}` | Item fields plus `processor`, `processorJobId`, `processingStartedUtc`, `attempts`, `variants: [{ name, format, width, height, sizeBytes }]` |
| `PUT /api/v1/admin/media/{id}` | `{ altText }` |
| `POST /api/v1/admin/media/{id}/reprocess` | Failed items whose original still exists; back to Processing. `409` otherwise |
| `DELETE /api/v1/admin/media/{id}` | Detaches from its target (the post or comment keeps its other attachments) and deletes every object. `204` |

Console page (`/admin/media`): grid and table toggle, thumbnails (the console calls the grant like the member app), filters above, a detail drawer with playback, and the row actions View, View JSON, Edit alt text, Reprocess, Open target, Delete.

### 10.4 Moderators (Admin)

Moderation is staffed by several people. The moderator role is per user and can be granted or revoked at any time; it takes effect on the member's next request (the session cache is invalidated).

| Method and path | Notes |
|---|---|
| `GET /api/v1/admin/moderators` | `{ items: [ { user: UserSummary, roles: [{ role, resourceType, resourceId }], grantedUtc, grantedByName, openInScope, actions30d, medianResponseMinutes30d, overdueHandled30d } ] }` |
| `POST /api/v1/admin/moderators` | `{ userId, resourceType: "Feed" }` grants `FeedModerator` for that scope (`resourceId: "*"`). Idempotent; `200` with the row. Audited as `member.roles` |
| `DELETE /api/v1/admin/moderators/{userId}?resourceType=Feed` | Revokes it; `204`. Their past actions stay attributed to them |

These are shortcuts over the Phase 1 `PUT /api/v1/admin/members/{id}/roles`, which keeps working. The console's Moderation section has a Moderators tab with this table and Grant and Revoke actions, and the member detail shows the role with the same toggle.

### 10.5 Media storage settings (Admin)

The console's Settings page shows a "Media storage" group with the five bucket settings of section 4.0 (`media.sourceBucket`, `media.videoSourceBucket`, `media.imagesBucket`, `media.transcodedBucket`, `media.retainedBucket`), the region, and `media.storageMode`, generated from the settings registry like every other group. They are Restart settings; changing one does not move existing objects (the console warns and links to the infrastructure docs).

---

## 11. Account lifecycle integration

| Phase 1 event | Phase 2 effect |
|---|---|
| Account deletion (`DELETE /api/v1/me`) | All the member's comments are erased and become `Deleted` placeholders shown as "[deleted]" (bodies and revisions cleared immediately, so replies stay readable), feed posts are soft-deleted, reactions, mentions of them, notifications, push tokens, blocks (both directions), notification preferences and hours, and unattached media are deleted; attached media, including originals, follows its content. Reports they filed stay (reporter shown as Deleted member) |
| Export (`GET /api/v1/me/export`) | Adds `feedPosts` and `comments` (with their revisions), `reactions`, `notificationPreferences`, `pushTokens` (platform, device name, dates; no token values), `blocks`, `reports` (filed by the member), and `media` (metadata only) |
| Ban | Sessions are revoked, so push tokens go with them. Content stays; moderators remove it separately |
| Mute | `403 Muted` (with `mutedUntilUtc`) on writes and on avatar, bio, and display name changes (section 1.1) |
| Session revoke, password change, sign-out | Push tokens bound to the revoked sessions are deleted |

---

## 12. Rate limits

Limits are per account unless noted, enforced by the existing in-process `SlidingWindowRateLimiter`. With two ECS tasks a member can reach up to twice a limit; acceptable at this scale and recorded as a known gap. "New account" means `createdUtc` within `comments.newAccountHours` (24).

| Route group | Limit | Setting |
|---|---|---|
| Member reads (`GET` on feed, comments, reactions, media, notifications) | 600 per minute | `rateLimits.memberReadsPerMinute` |
| Unread count | 120 per minute | `rateLimits.unreadCountPerMinute` |
| Create feed post (member and admin routes) | 30 per hour | `feed.postsPerHour` |
| Create comment | 10 per minute and 300 per day; new accounts 3 per minute and 30 per day | `comments.perMinute`, `comments.perDay`, `comments.newAccountPerMinute`, `comments.newAccountPerDay` |
| Edit post or comment | 60 per hour | `rateLimits.editsPerHour` |
| Reactions (PUT and DELETE) | 120 per minute | `reactions.perMinute` |
| Upload sessions | 60 per hour, plus the daily byte quota (4.1) | `media.uploadsPerHour`, `media.memberDailyUploadMegabytes` |
| Media grant | 60 per hour | `rateLimits.mediaGrantsPerHour` |
| Local upload PUTs | none (signature-bound; size-bound) | |
| Reports | 20 per hour | `moderation.reportsPerHour` |
| Blocks and unblocks | 30 per hour | `moderation.blocksPerHour` |
| Push token registration | 20 per hour | `push.registrationsPerHour` |
| Notification preference updates, read and unread marks | 120 per minute | `rateLimits.notificationWritesPerMinute` |
| Member search (`GET /api/v1/members/search`) | 120 per minute | `rateLimits.memberSearchPerMinute` |
| Link previews (`POST /api/v1/link-previews`) | 60 per hour; outbound fetches also limited to 10 per host per minute across all members | `linkPreviews.perHour`, `linkPreviews.perHostPerMinute` |
| Public feed (anonymous) | 120 per minute per IP | `rateLimits.publicFeedPerMinute` |
| Email unsubscribe (anonymous) | 30 per hour per IP | `rateLimits.unsubscribePerHour` |
| Moderator and Admin groups | none | |
| `SystemAdmin` | exempt from every member limit | |

---

## 13. Telemetry

New instruments on the `TrellisKit` meter (Prometheus names in parentheses). Labels are low-cardinality; never ids, handles, or free text.

| Instrument | Type | Labels |
|---|---|---|
| `trelliskit.feed.posts` (`trelliskit_feed_posts_total`) | Counter | `action` (create, update, delete, publish, pin, unpin), `outcome` |
| `trelliskit.comments` | Counter | `action` (create, update, delete), `outcome`, `depth_bucket` (0, 1-3, 4+) |
| `trelliskit.reactions` | Counter | `action` (add, remove), `target_type`, `key` (bounded by the configured set) |
| `trelliskit.media.uploads` | Counter | `kind`, `mode` (single, multipart), `outcome` (created, completed, rejected, expired) |
| `trelliskit.media.upload.bytes` | Counter | `kind` |
| `trelliskit.media.processing` | Counter | `kind`, `processor` (skia, mediaconvert, ffmpeg, passthrough), `outcome`, `failure_reason` |
| `trelliskit.media.stage.duration` | Histogram (s) | `kind`, `stage`: `queued`, `verify`, `decode`, `resize`, `encode`, `store`, `transcode_submit`, `transcode_wait`, `transcode` (ffmpeg), `poster`, `cleanup` |
| `trelliskit.media.grants` | Counter | `mode`, `outcome` |
| `trelliskit.notifications.created` | Counter | `type`, `aggregated` (true, false) |
| `trelliskit.notifications.fanout.duration` | Histogram (s) | `type` |
| `trelliskit.push.sends` | Counter | `outcome`: `sent`, `error`, `suppressed_preference`, `suppressed_paused`, `suppressed_hours`, `suppressed_rate`, `no_token` |
| `trelliskit.push.receipts` | Counter | `status` (ok, device_not_registered, message_too_big, message_rate_exceeded, other) |
| `trelliskit.push.tokens` | Counter | `action` (registered, refreshed, deleted, pruned) |
| `trelliskit.reports` | Counter | `target_type`, `reason` |
| `trelliskit.moderation.actions` | Counter | `action` (including auto_hide), `target_type` |
| `trelliskit.blocks` | Counter | `action` (block, unblock) |
| `trelliskit.emails.feed_posts` | Counter | `outcome` (sent, skipped_preference, skipped_interval, error), and `trelliskit.emails.unsubscribes` by `category` |
| `trelliskit.link_previews` | Counter | `outcome` (cache_hit, fetched, blocked_address, blocked_domain, timeout, too_large, not_html, error) |
| `trelliskit.word_filter.matches` | Counter | `action` (block, flag), `field` |
| `trelliskit.member_search` | Counter | `outcome` |
| `trelliskit.moderation.overdue` | Gauge | open reports past `moderation.reviewTargetHours` |
| `trelliskit.integration.calls` | Counter | `service` (s3, mediaconvert, sns, expo, http_preview), `operation`, `outcome` |
| `trelliskit.integration.duration` | Histogram (s) | `service`, `operation` |

Spans: `media.process <kind>` root per job with `stage:<name>` children; `<service> <operation>` for every integration call (`s3 PutObject`, `mediaconvert CreateJob`, `expo push.send`, `expo push.getReceipts`); `feed.publish`, `notifications.fanout`, and `job <name>` from `IntervalWorker` for every new worker. Span attributes: `media.kind`, `media.id` (ids are fine on spans, not on metrics), `job.batch_size`.

All metrics also carry the resource attribute `trelliskit.property` (the property id). Grafana: new dashboards in `assets/grafana/` (queries use `trelliskit_*`) under the Community (posts, comments, reactions, reports, moderation actions), Media (uploads, stage latency p50/p95, failures by reason, MediaConvert wait), and Notifications (created by type, push outcomes, receipt errors, token churn) folders.

---

## 14. Settings

All are added to `SettingsRegistry.cs`. "Live" applies on save; "Restart" applies after a restart. Integers are 32-bit, which is why large sizes are in megabytes.

| Key | Type | Default | Apply | Environment variable |
|---|---|---|---|---|
| `media.storageMode` | Enum `Local`, `S3` | `Local` | Restart | `TK_MEDIA_STORAGE_MODE` |
| `media.publicBaseUrl` | String (empty or absolute https URL) | empty (root-relative local URLs) | Live | `TK_MEDIA_PUBLIC_BASE_URL` |
| `media.s3Region` | String | `us-west-2` | Restart | `TK_MEDIA_S3_REGION` |
| `media.sourceBucket` | String | empty | Restart | `TK_MEDIA_SOURCE_BUCKET` |
| `media.videoSourceBucket` | String | empty | Restart | `TK_MEDIA_VIDEO_SOURCE_BUCKET` |
| `media.imagesBucket` | String | empty | Restart | `TK_MEDIA_IMAGES_BUCKET` |
| `media.transcodedBucket` | String | empty | Restart | `TK_MEDIA_TRANSCODED_BUCKET` |
| `media.retainedBucket` | String | empty | Restart | `TK_MEDIA_RETAINED_BUCKET` |
| `media.videoProcessor` | Enum `MediaConvert`, `Ffmpeg`, `Passthrough` | `Passthrough` | Restart | `TK_MEDIA_VIDEO_PROCESSOR` |
| `media.ffmpegPath` | String | `ffmpeg` | Restart | `TK_FFMPEG_PATH` |
| `media.mediaConvertRoleArn` | String | empty | Live | `TK_MEDIACONVERT_ROLE_ARN` |
| `media.mediaConvertQueueArn` | String | empty (default queue) | Live | `TK_MEDIACONVERT_QUEUE_ARN` |
| `media.mediaConvertJobTemplate` | String | `trelliskit-hls` | Live | `TK_MEDIACONVERT_JOB_TEMPLATE` |
| `media.snsTopicArn` | String | empty | Live | `TK_MEDIA_SNS_TOPIC_ARN` |
| `media.cloudFrontKeyPairId` | String | empty | Live | `TK_MEDIA_SIGNING_KEY_PAIR_ID` |
| `media.cloudFrontPrivateKeyPem` | Secret | empty | Live | `TK_MEDIA_SIGNING_PRIVATE_KEY` |
| `media.cookieDomain` | String | empty (host-only) | Live | `TK_MEDIA_COOKIE_DOMAIN` |
| `media.grantMinutes` | Integer 15 to 1440 | 360 | Live | |
| `media.uploadUrlMinutes` | Integer 5 to 720 | 60 | Live | |
| `media.uploadExpiryHours` | Integer 1 to 168 | 24 | Live | |
| `media.unattachedRetentionHours` | Integer 1 to 720 | 168 | Live | |
| `media.maxImageBytes` | Integer 102400 to 104857600 | 20971520 | Live | |
| `media.maxImagePixels` | Integer 1000000 to 100000000 | 40000000 | Live | |
| `media.maxMemberVideoMegabytes` | Integer 1 to 20480 | 2048 | Live | |
| `media.maxMemberVideoSeconds` | Integer 5 to 3600 | 300 | Live | |
| `media.maxAdminVideoMegabytes` | Integer 0 to 1048576 (0 = uncapped) | 0 | Live | |
| `media.maxAdminVideoSeconds` | Integer 0 to 86400 (0 = uncapped) | 0 | Live | |
| `media.multipartThresholdMegabytes` | Integer 5 to 5120 | 32 | Live | |
| `media.multipartPartMegabytes` | Integer 5 to 512 | 16 | Live | |
| `media.memberDailyUploadMegabytes` | Integer 0 to 1048576 (0 = no quota) | 5120 | Live | |
| `media.uploadsPerHour` | Integer 1 to 10000 | 60 | Live | |
| `media.imageConcurrency` | Integer 1 to 16 | 2 | Restart | |
| `media.videoConcurrency` | Integer 1 to 8 | 1 | Restart | |
| `media.processingIntervalSeconds` | Integer 1 to 300 | 5 | Live | |
| `feed.maxBodyLength` | Integer 100 to 50000 | 10000 | Live | |
| `feed.maxAttachments` | Integer 0 to 20 | 10 | Live | |
| `feed.maxVideosPerPost` | Integer 0 to 10 | 1 | Live | |
| `feed.maxPinned` | Integer 1 to 10 | 3 | Live | |
| `feed.publishIntervalSeconds` | Integer 5 to 600 | 30 | Live | |
| `feed.postsPerHour` | Integer 1 to 1000 | 30 | Live | |
| `content.editGraceSeconds` | Integer 0 to 3600 | 120 | Live | |
| `comments.maxBodyLength` | Integer 100 to 20000 | 5000 | Live | |
| `comments.maxDepth` | Integer 1 to 20 | 8 | Live | |
| `comments.maxAttachments` | Integer 0 to 4 | 1 | Live | |
| `comments.perMinute` | Integer 1 to 1000 | 10 | Live | |
| `comments.perDay` | Integer 1 to 100000 | 300 | Live | |
| `comments.newAccountHours` | Integer 0 to 720 | 24 | Live | |
| `comments.newAccountPerMinute` | Integer 1 to 1000 | 3 | Live | |
| `comments.newAccountPerDay` | Integer 1 to 100000 | 30 | Live | |
| `reactions.perMinute` | Integer 1 to 10000 | 120 | Live | |
| `mentions.maxNotifiedPerItem` | Integer 0 to 50 | 10 | Live | |
| `notifications.retentionDays` | Integer 7 to 3650 | 180 | Live | |
| `notifications.fanoutBatchSize` | Integer 50 to 5000 | 500 | Live | |
| `notifications.fanoutIntervalSeconds` | Integer 1 to 3600 | 15 | Live | |
| `account.exportsPerDay` | Integer 1 to 100 | 3 | Live | |
| `account.exportDownloadLinkMinutes` | Integer 1 to 1440 | 15 | Live | |
| `push.enabled` | Boolean | true | Live | `TK_PUSH_ENABLED` |
| `push.expoAccessToken` | Secret | empty (unauthenticated Expo push) | Live | `TK_EXPO_ACCESS_TOKEN` |
| `push.maxPerUserPerHour` | Integer 1 to 1000 | 10 | Live | |
| `push.receiptDelayMinutes` | Integer 1 to 1440 | 15 | Live | |
| `push.sendIntervalSeconds` | Integer 1 to 300 | 5 | Live | |
| `push.registrationsPerHour` | Integer 1 to 1000 | 20 | Live | |
| `moderation.autoHideReportThreshold` | Integer 0 to 100 (0 = off) | 3 | Live | |
| `moderation.retainRemovedDays` | Integer 30 to 3650 | 365 | Live | |
| `moderation.reviewTargetHours` | Integer 1 to 168 | 12 | Live | |
| `moderation.wordFilterEnabled` | Boolean | true | Live | |
| `linkPreviews.enabled` | Boolean | true | Live | `TK_LINK_PREVIEWS_ENABLED` |
| `linkPreviews.timeoutSeconds` | Integer 1 to 30 | 5 | Live | |
| `linkPreviews.maxRedirects` | Integer 0 to 10 | 3 | Live | |
| `linkPreviews.maxHtmlBytes` | Integer 65536 to 10485760 | 1048576 | Live | |
| `linkPreviews.maxImageBytes` | Integer 65536 to 20971520 | 5242880 | Live | |
| `linkPreviews.cacheHours` | Integer 1 to 8760 | 168 | Live | |
| `linkPreviews.failureCacheMinutes` | Integer 1 to 10080 | 60 | Live | |
| `linkPreviews.blockedDomains` | String (comma-separated host suffixes) | empty | Live | |
| `linkPreviews.userAgent` | String | `TrellisKitLinkPreview/1.0` | Live | |
| `linkPreviews.perHour` | Integer 1 to 10000 | 60 | Live | |
| `linkPreviews.perHostPerMinute` | Integer 1 to 1000 | 10 | Live | |
| `email.feedPostEmailsEnabled` | Boolean | true | Live | `TK_EMAIL_FEED_POSTS_ENABLED` |
| `email.feedPostMinIntervalMinutes` | Integer 0 to 10080 (0 = every post) | 0 | Live | |
| `email.feedPostBatchSize` | Integer 10 to 1000 | 100 | Live | |
| `notifications.defaultHoursStart` | String `HH:mm` | `07:00` | Live | |
| `notifications.defaultHoursEnd` | String `HH:mm` | `22:00` | Live | |
| `rateLimits.memberSearchPerMinute` | Integer 1 to 10000 | 120 | Live | |
| `rateLimits.publicFeedPerMinute` | Integer 1 to 100000 | 120 | Live | |
| `rateLimits.unsubscribePerHour` | Integer 1 to 10000 | 30 | Live | |
| `moderation.reportsPerHour` | Integer 1 to 1000 | 20 | Live | |
| `moderation.blocksPerHour` | Integer 1 to 1000 | 30 | Live | |
| `rateLimits.memberReadsPerMinute` | Integer 10 to 100000 | 600 | Live | |
| `rateLimits.unreadCountPerMinute` | Integer 1 to 10000 | 120 | Live | |
| `rateLimits.editsPerHour` | Integer 1 to 10000 | 60 | Live | |
| `rateLimits.mediaGrantsPerHour` | Integer 1 to 10000 | 60 | Live | |
| `rateLimits.notificationWritesPerMinute` | Integer 1 to 10000 | 120 | Live | |

Existing `storage.directory` (Restart, `TK_STORAGE_DIRECTORY`) is reused as the local media root (`source/`, `video-source/`, `images/`, `transcoded/`, and `retained/` subfolders next to `avatars/`, section 4.0). AWS credentials come from the default credential chain (the ECS task role in production); there are no access-key settings. In the Docker stack, the property's `server/settings.json` (seeded into the `server-config` volume) or the compose environment sets `media.videoProcessor` to `Ffmpeg`; production sets `S3`, `MediaConvert`, the five buckets (from the `TK_MEDIA_*_BUCKET` variables the infrastructure passes), `media.publicBaseUrl = https://<domains.media>`, and `media.cookieDomain = <domains.web>` from the property.

Reactions are not a setting: they come from the property's `reactions` (section 6.1), validated at startup with the rest of `property.json`. The default notification hours apply to new accounts only; members change theirs in the notification settings section.

---

## 15. Data model

Migration version 3, "Feed and engagement", in both `SqliteSchema.cs` and `PostgresqlSchema.cs`. Type mapping follows Phase 1: ids and text are `TEXT`; booleans are `BOOLEAN` (PostgreSQL) and `INTEGER` 0 or 1 (SQLite); timestamps are `TIMESTAMPTZ` and ISO 8601 `TEXT`; counts are `INTEGER`; byte sizes are `BIGINT` and `INTEGER` (SQLite integers are 64-bit). Partial indexes (`WHERE ...`) are supported by both. Case-insensitive uniqueness uses `lower(...)` as in Phase 1. No JSON columns: every list is a child table.

### Tables

| Table | Key columns |
|---|---|
| `feedposts` | `id` PK, `authoruserid` FK users, `kind`, `sourcetype` NULL, `sourceid` NULL (Phase 3), `title` NULL, `body`, `excerpt`, `visibility` (`Members`, `Public`), `linkpreviewid` NULL, `showlinkpreview`, `commentsenabled`, `publishatutc` NULL, `publishedutc` NULL, `ispinned`, `pinnedutc` NULL, `pinnedbyuserid` NULL, `editedutc` NULL, `revisioncount`, `attachmentcount`, `hasvideo`, `commentcount`, `reactiontotal`, `openreportcount`, `moderationstate` (`None`, `Hidden`, `Removed`), `moderatedutc`, `moderatedbyuserid`, `moderationreason`, `deletedutc` NULL, `deletedbyuserid` NULL, `purgedutc` NULL, `createdutc`, `updatedutc` |
| `feedpostrevisions` | `id` PK, `feedpostid` FK, `revision`, `title`, `body`, `attachmentsummary`, `editedbyuserid`, `createdutc` (readable by members, 5.5) |
| `mediaattachments` | `id` PK, `owneruserid` FK, `kind`, `status`, `failurereason`, `contenttype`, `originalfilename`, `sizebytes`, `declareddurationseconds`, `durationseconds`, `width`, `height`, `alttext`, `blurhash`, `storagemode`, `sourcebucket`, `sourcekey` (the original, kept), `multipartuploadid`, `partsizebytes`, `partcount`, `outputprefix`, `hlsmanifestfile`, `mp4file`, `posterfile`, `posterthumbfile`, `isretained` (served objects moved to the retained bucket), `ispublic` (copied to the public paths), `processor`, `processorjobid`, `attempts`, `claimedby`, `claimuntilutc`, `processingstartedutc`, `readyutc`, `uploadexpiresutc`, `attachedtargettype`, `attachedtargetid`, `attachedposition`, `attachedutc`, `deletedutc`, `createdutc`, `updatedutc` |
| `mediavariants` | PK (`mediaid`, `name`, `format`), `width`, `height`, `sizebytes`, `filename` |
| `comments` | `id` PK, `targettype`, `targetid`, `parentcommentid` NULL, `rootcommentid`, `replytocommentid` NULL, `path`, `depth`, `authoruserid`, `body` NULL, `excerpt` NULL, `linkpreviewid` NULL, `showlinkpreview`, `attachmentcount`, `childcount`, `descendantcount`, `reactiontotal`, `score`, `editedutc`, `revisioncount`, `moderationstate`, `moderatedutc`, `moderatedbyuserid`, `moderationreason`, `deletedutc`, `deletedreason` (`Author`, `Admin`, `AccountDeleted`), `purgedutc`, `createdutc`, `updatedutc` |
| `commentrevisions` | `id` PK, `commentid` FK, `revision`, `body`, `attachmentsummary`, `createdutc` (readable by members, 7.6) |
| `reactions` | `id` PK, `targettype`, `targetid`, `userid`, `reactionkey`, `createdutc` |
| `reactioncounts` | PK (`targettype`, `targetid`, `reactionkey`), `count` |
| `mentions` | `id` PK, `sourcetype`, `sourceid`, `mentioneduserid`, `handle`, `authoruserid`, `createdutc` |
| `notifications` | `id` PK, `recipientuserid`, `type`, `category`, `actoruserid` NULL, `actorcount`, `targettype`, `targetid`, `feedpostid` NULL, `commentid` NULL, `parentcommentid` NULL, `title`, `body` NULL, `path`, `groupkey` NULL, `readutc` NULL, `pushstate` (`None`, `Pending`, `Sent`, `Suppressed`, `Failed`), `lastpushedutc` NULL, `createdutc`, `updatedutc` |
| `notificationactors` | PK (`notificationid`, `userid`), `createdutc` |
| `notificationpreferences` | PK (`userid`, `category`), `inapp`, `push`, `email`, `updatedutc`. Rows exist only for changed cells' categories; defaults come from code |
| `notificationhours` | `userid` PK, `enabled`, `startminute` (0 to 1439, default 420), `endminute` (default 1320), `timezone`, `pushpaused`, `updatedutc`. A missing row means the defaults |
| `pushtokens` | `id` PK, `userid`, `sessionid` FK authsessions, `token`, `platform`, `deviceid`, `devicename`, `appversion`, `failurecount`, `disabledutc`, `lastsuccessutc`, `createdutc`, `lastseenutc` |
| `pushdeliveries` | `id` PK, `notificationid`, `pushtokenid`, `ticketid` NULL, `status` (`Queued`, `Sent`, `Ok`, `Error`), `error` NULL, `createdutc`, `receiptcheckedutc` NULL |
| `notificationfanouts` | `id` PK, `type`, `sourceid`, `lastuserid` (keyset position), `status` (`Pending`, `Running`, `Done`), `createdcount`, `claimedby`, `claimuntilutc`, `createdutc`, `completedutc` |
| `reports` | `id` PK, `reporteruserid` NULL (system reports from the word filter), `targettype`, `targetid`, `targetauthoruserid`, `scopetype` (`Feed` in Phase 2; `Forum` in Phase 3), `scopeid`, `reason`, `details`, `status` (`Open`, `ActionTaken`, `Dismissed`), `resolvedbyuserid`, `resolvedutc`, `resolutionnote`, `moderationactionid`, `dueutc`, `overduenotifiedutc` NULL, `createdutc` |
| `moderationactions` | `id` PK, `moderatoruserid` NULL (system), `targettype`, `targetid`, `targetauthoruserid`, `action` (`Hide`, `Unhide`, `Remove`, `Restore`, `AutoHide`, `Dismiss`), `reason`, `note`, `notifiedauthor`, `createdutc` |
| `blocks` | PK (`blockeruserid`, `blockeduserid`), `createdutc` |
| `userpermissions` | `userid` PK, `canpost` (default true), `canpostmedia` (default true), `updatedutc`, `updatedbyuserid`. Extends the Phase 1 flags; a missing row means both true |
| `linkpreviews` | `id` PK, `urlhash` UNIQUE, `url`, `canonicalurl`, `sitename`, `title`, `description`, `imagekey` NULL, `imagewidth`, `imageheight`, `status` (`Ok`, `Failed`), `failurereason`, `fetchedutc`, `expiresutc` |
| `wordfilterterms` | `id` PK, `term`, `normalizedterm` UNIQUE, `matchtype`, `action`, `note`, `createdbyuserid`, `createdutc`, `updatedutc` |
| `wordfilterhits` | `id` PK, `termid`, `targettype`, `targetid` NULL (null for blocked writes), `field`, `action`, `createdutc` (pruned after 90 days) |
| `displaynamewords` | PK (`userid`, `word`), lowercased words of `displayname` for member search |
| `emailsends` | `id` PK, `userid`, `category`, `sourceid`, `status` (`Sent`, `Skipped`, `Error`), `createdutc`. Idempotency for feed post emails (unique on `userid`, `category`, `sourceid`) |
| `jobleases` | `jobname` PK, `holder`, `leaseuntilutc`, `updatedutc` |

### Indexes and unique constraints

| Table | Index | Purpose |
|---|---|---|
| `feedposts` | `idx_feedposts_feed (publishedutc DESC, id DESC) WHERE deletedutc IS NULL AND moderationstate = 'None' AND publishedutc IS NOT NULL` | Feed keyset |
| `feedposts` | `idx_feedposts_pinned (ispinned, pinnedutc)` | Pinned set |
| `feedposts` | `idx_feedposts_schedule (publishatutc) WHERE publishedutc IS NULL AND deletedutc IS NULL` | Publish worker |
| `feedposts` | `idx_feedposts_author (authoruserid, createdutc)` | My posts, admin filter |
| `feedpostrevisions` | `ux_feedpostrevisions (feedpostid, revision)` UNIQUE | |
| `mediaattachments` | `idx_media_owner (owneruserid, createdutc)` | Library, quota |
| `mediaattachments` | `idx_media_status (status, claimuntilutc)` | Worker claims |
| `mediaattachments` | `idx_media_target (attachedtargettype, attachedtargetid, attachedposition)` | Load attachments |
| `mediaattachments` | `idx_media_cleanup (status, uploadexpiresutc)`, `idx_media_unattached (attachedtargetid, createdutc) WHERE attachedtargetid IS NULL` | Cleanup |
| `mediaattachments` | `ux_media_jobid (processorjobid) WHERE processorjobid IS NOT NULL` UNIQUE | Webhook lookup |
| `comments` | `idx_comments_top (targettype, targetid, parentcommentid, score DESC, createdutc, id)` | `sort=top` |
| `comments` | `idx_comments_time (targettype, targetid, parentcommentid, createdutc, id)` | `new` and `old` |
| `comments` | `idx_comments_parent (parentcommentid, createdutc, id)` | Children pages |
| `comments` | `idx_comments_path (path)` | Subtree ranges |
| `comments` | `idx_comments_author (authoruserid, createdutc)` | Moderation history, deletion |
| `commentrevisions` | `ux_commentrevisions (commentid, revision)` UNIQUE | |
| `reactions` | `ux_reactions (targettype, targetid, userid, reactionkey)` UNIQUE | One per key per user per target |
| `reactions` | `idx_reactions_list (targettype, targetid, reactionkey, createdutc)`, `idx_reactions_user (userid, createdutc)` | Who reacted, export |
| `mentions` | `ux_mentions (sourcetype, sourceid, mentioneduserid)` UNIQUE, `idx_mentions_user (mentioneduserid, createdutc)` | |
| `notifications` | `idx_notifications_list (recipientuserid, updatedutc DESC, id DESC)` | Center |
| `notifications` | `idx_notifications_unread (recipientuserid) WHERE readutc IS NULL` | Unread count |
| `notifications` | `ux_notifications_group (recipientuserid, groupkey) WHERE readutc IS NULL AND groupkey IS NOT NULL` UNIQUE | Aggregation |
| `notifications` | `idx_notifications_push (pushstate, createdutc) WHERE pushstate = 'Pending'`, `idx_notifications_prune (readutc)` | Push worker, retention |
| `pushtokens` | `ux_pushtokens_token (token)` UNIQUE, `idx_pushtokens_user (userid, lastseenutc)`, `idx_pushtokens_session (sessionid)` | |
| `pushdeliveries` | `idx_pushdeliveries_receipts (status, createdutc)` | Receipt checks |
| `reports` | `ux_reports_open (reporteruserid, targettype, targetid) WHERE status = 'Open'` UNIQUE | One open report per reporter per target |
| `reports` | `idx_reports_queue (status, scopetype, createdutc)`, `idx_reports_target (targettype, targetid, status)` | Queue |
| `moderationactions` | `idx_modactions_target (targettype, targetid, createdutc)`, `idx_modactions_moderator (moderatoruserid, createdutc)`, `idx_modactions_author (targetauthoruserid, createdutc)` | |
| `blocks` | `idx_blocks_blocked (blockeduserid)` | Reverse checks |
| `feedposts` | `idx_feedposts_public (publishedutc DESC, id DESC) WHERE visibility = 'Public' AND deletedutc IS NULL AND moderationstate = 'None'` | Public feed |
| `users` | `idx_users_handle_prefix (lower(handle))`; `displaynamewords` PK serves word prefixes | Member search |
| `reports` | `idx_reports_due (status, dueutc)` | Overdue queue and notifications |
| `emailsends` | `ux_emailsends (userid, category, sourceid)` UNIQUE | One email per member per post |

### Write rules

- The driver API runs a batch of statements as one transaction (`ExecuteQueriesAsync(..., isTransaction: true)`); there are no interactive transactions. Counters are therefore **recomputed from source rows inside the same batch** rather than incremented conditionally. Example for a reaction: `INSERT ... ON CONFLICT DO NOTHING` (SQLite `INSERT OR IGNORE`), then `UPDATE reactioncounts SET count = (SELECT COUNT(*) FROM reactions WHERE ...)` (upserting the row), then refresh the target's `reactiontotal` and, for comments, `score`. Every statement is idempotent, so a retry cannot drift a count.
- Comment `childcount` and `descendantcount` for the parent and every ancestor (ids are in `path`) are recomputed in the batch that creates, deletes, removes, or restores a comment.
- Queue claims (media jobs, fan-out jobs, pending pushes) use a conditional `UPDATE ... SET claimedby = <instance>, claimuntilutc = <now + lease> WHERE id IN (SELECT ... LIMIT n) AND (claimuntilutc IS NULL OR claimuntilutc < <now>)`, then read back rows with `claimedby = <instance>`. Singleton workers (cleanup, prune, receipts, and the existing Phase 1 workers) take a row in `jobleases` with the same conditional update. Both work identically on SQLite and PostgreSQL, so no advisory locks are needed.

---

## 16. Backend structure (for the backend team)

New seams in `TrellisKit.Core/Integrations` (interfaces at real boundaries, fakes in `Test.Shared/Fakes`):

| Interface | Implementations | Fake for tests |
|---|---|---|
| `IObjectStore` (presign single and multipart, list parts, complete, head, ranged read, put, copy, delete, delete prefix) | `S3ObjectStore` (AWSSDK.S3), `LocalObjectStore` | `LocalObjectStore` on a temp directory |
| `IImageProcessor` | `SkiaImageProcessor` (SkiaSharp, MIT; `SkiaSharp.NativeAssets.Linux.NoDependencies` in the container) | Real implementation with small fixtures |
| `IVideoTranscoder` | `MediaConvertTranscoder` (AWSSDK.MediaConvert), `FfmpegTranscoder`, `PassthroughTranscoder` | `PassthroughTranscoder` |
| `IMediaDeliverySigner` | `CloudFrontCookieSigner`, `LocalMediaCookieSigner` | Local |
| `ISnsMessageVerifier` | `SnsMessageVerifier` | Fixture-signed messages |
| `IPushSender` | `ExpoPushSender` | `InMemoryPushSender` (records messages, scripted receipts) |
| `ILinkPreviewFetcher` (resolve and check addresses, pinned connection, manual redirects, size and type limits) | `HttpLinkPreviewFetcher` | Scripted fetcher with fixture pages, plus resolver tests for every blocked range |

New services: `MediaService`, `FeedService`, `CommentService`, `ReactionService` (set from the property), `MentionService`, `MemberSearchService`, `LinkPreviewService`, `WordFilterService`, `NotificationService`, `PushService`, `FeedPostEmailService`, `ModerationService`, `BlockService`, `MarkdownNormalizer` (server half of section 3). New route registrars: `FeedRoutes`, `PublicFeedRoutes`, `CommentRoutes`, `ReactionRoutes`, `MediaRoutes` (extended), `MemberSearchRoutes`, `LinkPreviewRoutes`, `NotificationRoutes`, `EmailUnsubscribeRoutes`, `ReportRoutes`, `BlockRoutes`, `AdminFeedRoutes`, `AdminMediaRoutes`, `AdminModeratorRoutes`, `WordFilterRoutes`, `ModerationRoutes`, and the MediaConvert handler in `WebhookRoutes`. New workers (all on `IntervalWorker`): `MediaProcessingWorker`, `MediaCleanupWorker`, `FeedPublishWorker`, `NotificationFanoutWorker` (in-app, push queueing, and feed post emails), `PushDeliveryWorker` (send and receipts), `NotificationPruneWorker`, `ModerationDueWorker` (overdue notices), `ContentPurgeWorker`, `LinkPreviewPruneWorker`.

Local upload and file routes are PreAuthentication routes (they verify their own signature or cookie) and must stream bodies to disk instead of reading `DataAsBytes`.

Touchstone suites to add in `Test.Shared`: `MediaSuite` (sessions, local PUT signatures, multipart complete and resume, sniffing, `mvhd` duration, EXIF removal verified by re-reading output bytes, grant cookie), `FeedSuite`, `CommentSuite` (tree pages, flattening at max depth, placeholders, cursors), `ReactionSuite` (idempotency, counts under concurrent PUTs), `NotificationSuite` (types, dedupe, aggregation, preferences, notification hours with a fixed clock, pause), `PushSuite`, `ModerationSuite` (reports, auto-hide, actions, scope, staff content protection, review target), `BlockSuite` (including `CannotBlockStaff`), `PermissionSuite` (`canPost`, `canPostMedia`, `canUploadVideo`, mute), `RevisionSuite` (unlimited edits, history visibility), `MemberSearchSuite`, `LinkPreviewSuite` (every blocked address range, DNS rebinding, redirects, limits, cache), `WordFilterSuite` (normalization, word and substring, block and flag), `PublicFeedSuite`, `FeedPostEmailSuite` (opt-out, unsubscribe token, idempotency). Tests use the sample property's reactions. All run on both providers.

---

## 17. Local development

| Concern | Local default |
|---|---|
| Storage | `media.storageMode = Local` under `storage.directory`, one subfolder per purpose (4.0) (Docker: the `server-data` volume at `/app/data` so media survives restarts) |
| Images | SkiaSharp in-process |
| Video | Docker stack: `Ffmpeg` (server image built from the `local` Dockerfile target, which adds `ffmpeg`). `dotnet run`: `Passthrough` unless `media.videoProcessor` is set and ffmpeg is on PATH |
| Delivery | `POST /api/v1/media/grant` returns `mode: "Local"` and sets `tk_media`; files from `/api/v1/media/files/...` (through nginx on 8080, Vite on 5173, or direct on 8000) |
| Upload size through nginx | Parts are 16 MB and images at most 20 MB, both under the existing `client_max_body_size 50m`; video above 32 MB always goes multipart, so no proxy change is needed |
| Push | Real Expo push from a dev build works with no access token; with `push.enabled = false` notifications are still created and pushes are counted as `suppressed_preference` |
| MediaConvert webhook | Not used locally |
| Link previews | Real outbound fetches from the server container (the blocked ranges include the Docker networks, so previews of local services are refused by design) |
| Feed post emails | Delivered to Mailpit (http://127.0.0.1:8025), with working unsubscribe links |
| Mobile on a device | Set `EXPO_PUBLIC_API_URL` to the machine's LAN address; root-relative media URLs resolve against it |

---

## 18. Client notes

**Shared (`shared/src`):** add `markdown.js` (section 3 parser to AST, plus `toPlainText` for excerpts in previews), `mentions.js` (the 3.3 pattern, used by composers for autocomplete triggers), the reaction-set cache, cursor-page helpers, and `ApiClient` methods: `listFeed`, `getFeedPost`, `createFeedPost`, `updateFeedPost`, `deleteFeedPost`, `pinFeedPost`, `unpinFeedPost`, `listMyFeedPosts`, `getReactionSet`, `putReaction`, `deleteReaction`, `listReactions`, `listComments`, `listCommentChildren`, `getComment`, `createComment`, `updateComment`, `deleteComment`, `createUpload`, `refreshUploadUrls`, `completeUpload`, `getMedia`, `getMediaBatch`, `updateMedia`, `deleteMedia`, `getMediaGrant`, `listNotifications`, `getUnreadCount`, `markNotificationsRead`, `markNotificationsUnread`, `markAllNotificationsRead`, `getNotificationPreferences`, `updateNotificationPreferences`, `listPushTokens`, `registerPushToken`, `unregisterPushToken`, `createReport`, `blockUser`, `unblockUser`, `listBlocks`, `searchMembers`, `getLinkPreview`, `listFeedPostRevisions`, `listCommentRevisions`, `listPublicFeed`, `getPublicFeedPost`; admin: `listModerators`, `grantModerator`, `revokeModerator`, `listWordFilter`, `createWordFilterTerm`, `updateWordFilterTerm`, `deleteWordFilterTerm`, `importWordFilter`, `testWordFilter`. Mention autocomplete calls `searchMembers` (debounced 150 ms, with the post as context) after `@` plus one character. The reaction picker and labels come from the property (`GET /api/v1/property` or `getReactionSet`), never from engine constants.

**Uploads:** web uploads with `fetch` PUTs of `Blob.slice()` parts, up to 3 parts in parallel, reading `ETag` from each response. Mobile reads parts with `expo-file-system` (new dependency) and PUTs them the same way, and resumes through `upload-urls` after the app returns to the foreground. Both show per-file progress and then the processing state from polling.

**Website (`/app/feed`, `/app/feed/:id`, `/app/notifications`, `/app/settings/notifications`, `/app/settings/blocked`):** feed list with pinned section, post detail with the comment tree, composer for posters (and a comment composer with one image or video when allowed), "edited" labels that open the edit history, link preview cards (with a remove control in composers), mention autocomplete, notification center and bell badge in `MemberLayout`, the full notification settings section (8.5, 8.6), blocked members. Marketing pages show public sample posts from the public feed. Admin: Moderators tab, Word filter page, Posting permissions on member detail, Media storage settings group. Video with hls.js (`withCredentials`), native HLS on Safari.

**Mobile:** Feed tab (`@shopify/flash-list`), `feed/[id]` with the comment tree, composer for posters, comment composer with one attachment, edit history sheets, link preview cards, mention autocomplete, Notifications screen (the existing `notifications.jsx`), the full notification settings section and blocked members under Account, device permission prompts and the permission banner (8.10), push registration in the existing `allow-notifications` setup step, push tap handling through Expo Router, `expo-video` for playback with cookie headers. Tablet: feed list and post detail in the existing `SplitView`.

---

## 19. Decisions (resolved; formerly open questions)

All Phase 2 questions are resolved. The property owner's answers override the earlier recommendations, and the contract above already implements them as engine behavior with settings (or property configuration) where a property could choose differently.

| # | Question | Decision | Where |
|---|---|---|---|
| 1 | Who may create feed posts? | **The owner, admins, and flagged members**, per user: `SystemAdmin`, or `flags.canPostToFeed` granted by an admin on the member's page | 1.1, 5.4 |
| 2 | Media in comments, and per-user restrictions | **Comments may carry one image or one video** (`comments.maxAttachments` 1). Per-user permissions let an admin restrict a member to text only (`canPostMedia: false`) or block posting entirely (`canPost: false`), alongside `canPostToFeed`, `canUploadVideo`, and mute | 1.1, 7.5 |
| 3 | Comment edit window | **Always editable, with edit history** visible to everyone who can see the comment | 7.6 |
| 4 | Feed post edit window | **Always editable, with edit history** visible to everyone who can see the post | 5.5 |
| 5 | Notification settings and defaults | **Full notification customization section on web and mobile; every notification on by default; the app prompts for the device permission** | 8.5, 8.10 |
| 6 | Quiet hours | **Notification hours are a per-user setting**, default allowed window **7:00 to 22:00 local** | 8.6 |
| 7 | Auto-hide threshold | **3 distinct reporters; never admin content** | 9.1 |
| 8 | Keep original uploads? | **Keep all original uploads**, and use **separate storage buckets per purpose** (source, video source, images, transcoded, retained), each a setting in the admin console | 4.0, 4.6, 10.5, `infra/` |
| 9 | Can members block staff? | **No** (`400 CannotBlockStaff`) | 9.2 |
| 10 | Reaction set | **Like, dislike, heart, laugh, fire, star, mind-blown, thanks, middle finger, hug, light bulb**, defined in the property's `property.json` (keys `like`, `dislike`, `heart`, `laugh`, `fire`, `star`, `mindblown`, `thanks`, `middlefinger`, `hug`, `idea`) | 6.1 |
| 11 | Who staffs moderation, and the response time? | **Several moderators with a 12 hour review target**; the moderator role is per user, granted and revoked at will | 9.1, 10.4 |
| 12 | May FeedModerators hide and remove feed posts? | **Yes for member posts, never for admin posts** (nor admin comments) | 1.1, 9.3 |
| 13 | Member search for mention autocomplete | **In Phase 2** (`GET /api/v1/members/search`) | 3.4 |
| 14 | Link preview cards | **In Phase 2**, fetched server side with SSRF protection and caching | 5.9 |
| 15 | A deleted member's comments | **Erased and shown as "[deleted]"** | 7.2, 11 |
| 16 | Removed-content retention | **1 year** (`moderation.retainRemovedDays` 365; retained bucket lifecycle 365 days) | 4.7, 9.3 |
| 17 | Profanity and slur filter | **In Phase 2**, with an admin-editable list | 9.4 |
| 18 | Public sample (teaser) posts | **In Phase 2**, through `visibility: "Public"` set by `SystemAdmin` and the public feed | 5.10 |
| 19 | Email members about new feed posts | **Yes, opt-out** (on by default, one-click unsubscribe) | 8.9 |
| 20 | Word filter on display names and bios (2026-11-12) | **Yes**: display names and bios are screened like posts and comments, including staff edits of a member's profile (checked against the member's words) | 9.4 |
| 21 | Muted members and their profile (2026-11-12) | **Frozen**: while muted, a member cannot change avatar, bio, or display name (`403 Muted` with `mutedUntilUtc`) | 9 |
| 22 | Staff feed posts and blocks (2026-11-12) | **Blocks respected**: `NewFeedPost` fan-out skips members on either side of a block even when the author is staff; the post stays visible in the feed | 9.2 |
| 23 | Navigation to features whose backend has not shipped (2026-11-12) | **Shown as coming soon**, driven by `features.*` capabilities on `GET /api/v1/property` (missing means false) | 18 |

Interpretation notes: "keep all original uploads" means processing and lifecycle never delete originals; a member's own deletion of content or of their account still erases its originals (after the removed-content retention for content, immediately for account deletion), as the privacy policy requires. "Admin content" for decisions 7 and 12 means content authored by a `SystemAdmin`.

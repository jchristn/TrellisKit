# TrellisKit REST API

This document covers the implemented TrellisKit engine API surface: Phase 0 platform routes, Phase 1 identity and membership, the property routes, and the Phase 2 routes (media, feed posts, the public feed, comments, reactions, reports, moderation, admin feed management, moderators, blocks, word filter, member search, link previews, notifications, push tokens, email unsubscribe, account export jobs, and the engine-wide labels, tags, and enumeration routes). Everything brand-specific (name, domains, membership product and price, profile taxonomy, reactions) comes from the active property's `property.json` (see `docs/TRELLISKIT.md`); examples below use the engine's sample property `samples/example` (Example Community, domain `example.test`, opaque property id `app_mwczw7mg_F8RsDcNidltPWZS1seB`, cosmetic slug `example`). A deployment runs `SingleProperty` (default) or `MultiProperty`; see [Properties and deployment modes](#properties-and-deployment-modes) and [Platform (MultiProperty)](#platform-multiproperty). It is updated with every change to the routes, together with the Postman collection in `assets/postman/`. `docs/PHASE1_CONTRACT.md` and `docs/PHASE2_CONTRACT.md` are the design records; `docs/ENUMERATION_AND_METADATA.md` is the binding specification for every list, every label, and every tag, and wins wherever an older document disagrees. Where the code and a contract differ, this document describes the code.

## Conventions

| Topic | Rule |
|---|---|
| Base URL | Local: `http://127.0.0.1:8000`. Through the website (nginx locally, CloudFront in production) the same routes are served same-origin, for example `http://127.0.0.1:8080/api/v1/health`. |
| Versioning | All application routes are under `/api/v1`. |
| Format | JSON request and response bodies, `Content-Type: application/json`. Property names are camelCase. Enums are strings. Timestamps are ISO 8601 UTC. |
| Clients | Apps send `X-Client: web` or `X-Client: mobile` and `X-Client-Version: <version>`; the mobile app also sends `X-Client-Platform: ios` or `android` (document acceptances record it). `X-TK-Client` (`web`, `mobile`, or `api`) optionally names the client for the content changelog and wins over `X-Client`. A missing `X-Client` is treated like mobile (refresh token in the body). |
| Property | Every request resolves to exactly one property ([Properties and deployment modes](#properties-and-deployment-modes)). Mobile and tablet apps send `X-TK-Property: <property id>` and `X-TK-App: <bundle id or package>` on every request; the website sends `X-TK-Property` (or relies on its host). Sessions, tokens, continuation tokens, and every object are bound to their property. |
| Authentication | `Authorization: Bearer <accessToken>` for member routes. Admin routes accept a `SystemAdmin` member's access token or the break-glass admin token. See [Tokens](#tokens). |
| Errors | Every non-2xx response has the body `{ "error": "<Code>", "message": "<text>", "fieldErrors": { "<field>": "<text>" } \| null }`. Field names in `fieldErrors` are camelCase body or query names; enumeration errors use `filters.<name>`, `labels[<n>]`, and `tags.<key>`. |
| Lists | Every list is an enumeration: `POST <collection>/enumerate` with an `EnumerationQuery` body, or `GET <collection>` with the same fields as query parameters, both returning `EnumerationResult<T>`. See [Enumeration and metadata](#enumeration-and-metadata). Page-number paging (`pageNumber`, `pageSize`, `sort`) is gone. |
| Labels and tags | Every object payload carries `labels` (a list, possibly `[]`) and `tags` (a dictionary, possibly `{}`); they are never omitted and never null. See [Labels and tags](#labels-and-tags). |
| Rate limits | Every `429 RateLimited` response (Phase 1 routes included) carries a `Retry-After` header in whole seconds. `SystemAdmin` callers are exempt from member limits. |
| Identifiers | Prefixed, K-sortable strings: `app_` property (opaque, never renamed), `pdm_` property domain, `ppa_` property app, `pad_` platform administrator, `pss_` platform session, `usr_` member, `ses_` session, `sub_` subscription, `sev_` subscription event, `ent_` entitlement (comps use it as their id), `suc_` sign-up code, `scr_` redemption, `scb_` batch, `lin_` rank history entry, `aud_` audit record, `req_` request history, `aex_` account export, `med_` media, `ntf_` notification, `ptk_` push token, `wft_` word filter term, `lpv_` link preview, `fpo_` feed post, `fpr_` feed post revision, `cmt_` comment, `cmr_` comment revision, `rxn_` reaction, `mtn_` mention, `rpt_` report, `mac_` moderation action. |
| CORS | Browsers may call the API only from origins in `cors.allowedOrigins`; credentials are allowed for those origins. Allowed methods: `GET, POST, PUT, PATCH, DELETE, OPTIONS, HEAD`. Allowed request headers: `Content-Type, Authorization, X-Api-Key, X-Client, X-Client-Version, X-TK-Property, X-TK-App`. In `MultiProperty`, `https://<host>` of every `Web` and `Alias` domain of every enabled property is also allowed. Exposed response headers: `ETag, Retry-After, X-Continuation-Token, X-Total-Records`. |
| Request capture | Every request except CORS preflights is recorded in request history. Secret-bearing headers (Authorization, Cookie, Set-Cookie, ...) are redacted, and JSON fields named `password`, `newPassword`, `currentPassword`, `token`, `idToken`, `identityToken`, `refreshToken`, `accessToken`, `signedTransaction`, `purchaseToken`, `nonce`, and `dateOfBirth` are replaced with `***redacted***` in request and response bodies. Live cookie values in bodies (the `value` of each entry in a `cookies` array, as in the media grant) are redacted too, and URL signature parameters (`sig`, `signature`, `token`, `X-Amz-Signature`, `X-Amz-Security-Token`, `X-Amz-Credential`) are redacted in the stored URL, headers, and bodies (other parameters such as `exp` stay readable). Binary bodies (avatars, media uploads) are not stored. |
| Client IP | Rate limits, sessions, audit records, and request history use the client IP: the socket peer, or, when the peer is a trusted proxy (`rest.trustedProxies`, default the private and loopback ranges, plus `rest.additionalTrustedProxies`, for example a CDN's origin-facing ranges), the CDN viewer address header (`rest.viewerAddressHeader`, default `CloudFront-Viewer-Address`, `ip:port`, IPv6 possible) when it holds a valid address, else the right-most `X-Forwarded-For` address that is not itself a trusted proxy. A client cannot spoof its address with its own headers. |
| Request headers | Responses never echo request headers (no `Host`, `Accept`, or `Cookie` on responses). |
| HEAD | `HEAD` is not served: on a path with a `GET` route it is `405` with `Allow: GET` and no body; elsewhere it behaves like `GET` on an unknown path. |

### Route groups (the paywall)

One authorization stage runs for every authenticated route, so no route can skip it. Routes that verify their own signature or serve public data run before authentication and are listed as Anonymous.

| Group | Routes | Requirement |
|---|---|---|
| Anonymous | `/api/v1/health*`, `/openapi.json`, `/swagger`, `/api/v1/auth/*` (except `/auth/me` and `/auth/email/resend`), `/api/v1/webhooks/*` (including `mediaconvert`), `/api/v1/media/avatars/*`, `/api/v1/media/files/*`, `/api/v1/media/local-uploads/*`, `/api/v1/property*`, `/api/v1/public/*` (the public feed), `/api/v1/email/unsubscribe`, `/api/v1/me/export/{id}/download` | None (the route checks its own signature, cookie, or token where it has one) |
| Signed in | `/api/v1/auth/me`, `/api/v1/auth/email/resend`, `/api/v1/me*` (profile, export jobs, `blocks`, `feed-posts`, `notification-preferences`, `push-tokens`), `/api/v1/handles/*`, `/api/v1/membership*`, `/api/v1/signup-codes/*` | A member access token. `POST /api/v1/membership/*` and `/api/v1/signup-codes/*` also need the age attestation (`/api/v1/membership/history*` does not). |
| Member | Everything else: `/api/v1/feed*`, `/api/v1/comments*`, `/api/v1/reactions/set`, `/api/v1/reports`, `/api/v1/users/*` (profiles, block), `/api/v1/members*`, `/api/v1/media*` (except the anonymous prefixes), `/api/v1/notifications*`, `/api/v1/link-previews`, `/api/v1/metadata/*`, `/api/v1/labels/*`, `/api/v1/tags/*` | Age attested, then the property's access model ([Access models](#access-models)): `FullyGated` needs an active membership; `PartiallyGated` and `Open` need the tier of the route's feature and action, and anonymous browsing opens content reads signed out. `SystemAdmin` members pass. |
| Moderator | `/api/v1/admin/moderation/*` except the word filter ([Moderation](#moderation-moderator)) | `SystemAdmin`, the break-glass admin token, or any `Moderator` or `FeedModerator` role assignment. Per-item scope is checked in the service. Others get `403 Forbidden`. |
| Admin | `/api/v1/admin/*` (including `/api/v1/admin/feed*`, `/api/v1/admin/metrics/engagement`, `/api/v1/admin/usage*`, `/api/v1/admin/moderators*`, `/api/v1/admin/moderation/word-filter*`, `/api/v1/admin/media*`, and `/api/v1/admin/metadata/*`), `/api/v1/settings*`, `/api/v1/request-history*` | `SystemAdmin`, or the break-glass admin token |

In the `Open` access model the membership, purchase, and sign-up code routes and the admin writes that grant entitlements return `404 NotAvailable` for every caller ([Access models](#access-models)).

The classifier checks `/api/v1/admin/moderation/word-filter` (Admin) before `/api/v1/admin/moderation` (Moderator), before the generic `/api/v1/admin` rule. The break-glass token passes every group, but routes that act on "me" return `403 Forbidden` for it because it has no member.

### Error codes

| Status | `error` | When |
|---|---|---|
| 400 | `ValidationFailed` | Invalid body fields or query parameters, including enumeration fields (unknown `ordering`, unknown filter, `staff:` filters from non-staff), and well-formed JSON whose value has the wrong type for its field (for example `{"rankLevel":"two"}` gives `fieldErrors.rankLevel` "Must be a whole number."; an unknown enum value lists the allowed values). `fieldErrors` names each problem. |
| 400 | `InvalidJson` | The body is not well-formed JSON, or is not a JSON object. NUL characters in strings are removed on read (storage cannot hold them), so responses echo exactly what is stored. |
| 400 | `PropertyMismatch` | `X-TK-Property` names one property and the request host is a domain of another. |
| 400 | `InvalidContinuationToken` | A `continuationToken` is malformed, or was issued for a different list, scope, or filter set. Start again without it. |
| 400 | `InvalidToken` | An emailed verification, reset, or unsubscribe token is unknown, used, or expired. |
| 400 | `InvalidCode` | A sign-up code cannot be redeemed (same message for every reason). |
| 400 | `InvalidImage` | An avatar is not a JPEG, PNG, or WebP matching its Content-Type. |
| 400 | `InvalidTransaction`, `InvalidPurchase` | An App Store transaction or Google Play purchase did not verify. |
| 400 | `InvalidSignature`, `InvalidNotification` | A webhook signature or body is invalid (MediaConvert and Stripe/Apple webhooks use 400; Google Play uses 401). |
| 400 | `ContentFiltered` | Text (post and comment text, alt text, display names and bios, labels, tag values from members) matched a `Block` term of the word filter; `fieldErrors` names the field without echoing the term. |
| 400 | `VideoTooLong` | A declared or measured video duration exceeds the cap. |
| 400 | `MediaLimitExceeded` | Too many attachments, or too many videos, on one item. |
| 400 | `CannotBlockSelf`, `CannotBlockStaff` | Blocking yourself, or a `SystemAdmin`, `Moderator`, or `FeedModerator` holder. |
| 400 | `LinkPreviewUnavailable` | The URL is not allowed or could not be fetched. |
| 400 | `UnknownReaction` | A reaction key (path or `key` filter) is not in the property's current reaction set. |
| 400 | `CannotReportSelf` | Reporting your own post, comment, or profile. |
| 401 | `Unauthorized` | No token, or an invalid, expired, or revoked access token. Clients refresh once and retry. |
| 401 | `InvalidCredentials` | Wrong email or password (one message for both), or a wrong current password. |
| 401 | `InvalidIdentityToken` | A provider token did not verify. |
| 401 | `InvalidRefreshToken` | The refresh token is unknown, expired, already used (the session is then revoked, except within `auth.refreshReuseGraceSeconds` of the rotation, when a concurrent refresh won and its new tokens stay valid), or its session is gone. Sign in again. |
| 402 | `MembershipRequired` | A member route without an active membership (`FullyGated`), or in `PartiallyGated` a gated feature write or a `Members` object. Access model denials add `required: "Membership"`, `feature`, and (per object) `targetType` and `targetId`. A `401` from the access model adds `required: "SignIn"`. |
| 404 | `NotAvailable` | The route does not exist in the property's access model (`Open`: membership, purchase, and sign-up code routes) or mode (platform routes in `SingleProperty`). |
| 403 | `Forbidden` | Signed in but lacks the role (for example a non-admin on an admin route, a non-moderator on a Moderator route, or a non-owner changing labels and tags). |
| 403 | `AppNotRegistered` | `X-TK-App` is not a registered app of the resolved property, or (`MultiProperty`, `deployment.requireAppHeader`) a request with `X-Client: mobile` has no `X-TK-App`. |
| 403 | `DocumentAcceptanceRequired` | The member has not accepted the required version of one or more property documents ([Property documents](#property-documents)). The body adds `"documents": ["<key>", ...]`; accept with `POST /api/v1/me/documents/accept`. Returned on member, staff, purchase, and sign-up code routes after the age attestation; never to the admin token or a delegated platform session. |
| 403 | `AgeAttestationRequired` | The member has not attested their age (`POST /api/v1/me/age-attestation`). The message quotes the property's minimum age (`membership.minimumAge` in `property.json`), for example "Confirm you are 16 or older to continue." for the sample property. |
| 403 | `AgeRequirementNotMet` | Under the property minimum age. Registration stores nothing; attestation closes the account. |
| 403 | `AccountBanned` | The account is banned. |
| 403 | `Muted` | A muted member tried to write (post, comment, edit, react, upload, edit alt text, labels, or tags) or to change their avatar, bio, or display name. The body adds `mutedUntilUtc` (the end of the mute, or null for an indefinite mute): `{ "error": "Muted", "message": "...", "fieldErrors": null, "mutedUntilUtc": "2026-11-14T18:00:00Z" }`. The message is plain text without a timestamp. |
| 403 | `PostingNotAllowed`, `MediaNotAllowed`, `VideoUploadNotAllowed` | `flags.canPost`, `flags.canPostMedia`, or `flags.canUploadVideo` is false. |
| 403 | `InteractionBlocked` | The author of the post or comment you reply to or react to has blocked you. |
| 403 | `InvalidSignature` | A local upload URL, a media file request without a valid grant, or an export download link failed verification. |
| 404 | `NotFound` | Unknown resource, content the caller may not see (never 403, so ids do not leak; this includes unpublished, hidden, removed, and deleted feed content), the public feed when `features.publicSamples` is false, or no route matches (for authenticated callers). |
| 404 | `PropertyNotFound` | The request resolved to no property, an unknown property, or one that is disabled or being deleted (also every non-platform route on a platform host, and platform routes on any other host). |
| 404 | `NotAvailable` | A route that does not exist in the deployment mode: `/api/v1/platform/*` in `SingleProperty`, the single-property Stripe webhook URL in `MultiProperty`. |
| 404 | `NoStripeCustomer` | The member has no Stripe customer for the portal. |
| 409 | `EmailInUse`, `AccountLinkRequired`, `IdentityInUse`, `LastSignInMethod`, `AlreadyMember`, `TransactionBelongsToAnotherAccount`, `AlreadyRedeemed`, `CodeInUse`, `CodeLocked`, `CannotRemoveOwnAdmin`, `CannotBanSelf` | Phase 1 conflicts, described with each route. |
| 409 | `PropertyLimitReached`, `DomainInUse`, `AppIdInUse`, `PropertyIdInUse`, `PropertyNotReady`, `PropertyNotRestorable`, `LastPlatformAdmin` | Platform conflicts, described with each [platform route](#platform-multiproperty). |
| 409 | `ExportInProgress` | An account export is already Pending or Running. The body adds `exportId` (the export in progress): `{ "error": "ExportInProgress", "message": "...", "fieldErrors": null, "exportId": "aex_..." }`. |
| 409 | `UploadIncomplete` | `complete` before every part arrived, a size mismatch, or a local upload to a session that is no longer Uploading. |
| 409 | `MediaNotAttachable` | Media is not the caller's, is already attached elsewhere, is Uploading, or Failed. |
| 409 | `NotReprocessable` | Reprocess of media that is not Failed or whose original is gone. |
| 409 | `BlockLimitReached` | More than 1000 blocks. |
| 409 | `PinLimitReached` | Pinning (or creating with `pin: true`) beyond `feed.maxPinned` pinned posts. |
| 409 | `NotPublished` | Pinning a post that is not Published. |
| 409 | `NotScheduled` | `publish-now` on a post that is not Scheduled. |
| 409 | `CommentsDisabled` | Commenting on a post with `commentsEnabled: false`. |
| 409 | `ParentUnavailable` | Replying to a comment that is deleted, removed, or no longer visible to you. |
| 409 | `InvalidModerationState` | A moderation action that does not fit the content's state (Hide on hidden, Unhide on visible, Restore on content that is not Removed or was purged, and so on). |
| 409 | `Conflict` | A word filter term with the same normalized form exists. |
| 410 | `ExportMoved` | `GET /api/v1/me/export` (replaced by the asynchronous export job). |
| 410 | `UploadExpired` | Upload session older than `media.uploadExpiryHours`. |
| 411 | `LengthRequired` | A local upload sent with chunked transfer encoding instead of a `Content-Length`. |
| 413 | `TooLarge` | An avatar over `storage.maxAvatarBytes`, or a declared or received media size over its limit. |
| 415 | `UnsupportedMediaType` | A media content type is not allowed, or the sniffed bytes do not match it. |
| 416 | `RangeNotSatisfiable` | A media file `Range` header outside the file (`Content-Range: bytes */<length>`). |
| 423 | `AccountLocked` | Too many failed password sign-ins; `message` says when to retry. |
| 429 | `RateLimited` | Too many requests for the route's limit; always with `Retry-After`. |
| 500 | `InternalError`, `WebhookFailed` | Unexpected error (logged, details never returned); a webhook that failed and will be retried by the store. |
| 502 | `StoreError` | Stripe failed to create a checkout or portal session. |
| 503 | `IntegrationNotConfigured` | Stripe, Apple, Google, Facebook, App Store, Google Play, S3 media storage, or CloudFront signing is not configured; the message names the missing settings. |

Unauthenticated calls to unknown paths return `401`, not `404`, so the API does not reveal which routes exist.

---

## Enumeration and metadata

Binding specification: `docs/ENUMERATION_AND_METADATA.md`. No route returns a list that grows with data unless it is an enumeration with an enforced maximum page size, at the top level or nested in another payload.

### Routes

Each collection exposes two routes with identical semantics and the same result shape:

- `POST <collection>/enumerate` with an `EnumerationQuery` JSON body (canonical; the only form that sends `tags` and `filters` as structured JSON).
- `GET <collection>` with the same fields as query parameters (convenience).

Nested collections follow the same pattern, for example `POST /api/v1/admin/members/{id}/sessions/enumerate` and `GET /api/v1/admin/members/{id}/sessions`.

### `EnumerationQuery`

```json
{
  "maxResults": 25,
  "continuationToken": null,
  "skip": 0,
  "ordering": "CreatedDescending",
  "search": "seminar",
  "createdAfterUtc": "2026-11-01T00:00:00Z",
  "createdBeforeUtc": null,
  "labels": ["fundamentals"],
  "labelMatch": "All",
  "tags": { "level": "beginner", "cohort": "*" },
  "filters": { "status": "Active" }
}
```

| Field | Rule |
|---|---|
| `maxResults` | Default 25 (or the collection's own default). Minimum 1. Maximum `enumeration.maxResultsMember` (default 100) for member collections and `enumeration.maxResultsAdmin` (default 500) for admin collections; both settings are hard-capped at 1000 in code. Values outside the range are clamped, never rejected. Some collections cap lower (member search: 20). |
| `continuationToken` | Opaque string from the previous result. When present it carries the ordering and position (keyset over the ordering column plus id, stable under concurrent inserts); `skip` is ignored. A token from another list, scope, or filter set is `400 InvalidContinuationToken`. |
| `skip` | 0 to 100000. Records to skip, for admin tables that jump to a page. Ignored with a `continuationToken`. |
| `ordering` | One of the collection's orderings (listed with each collection). Every collection supports `CreatedDescending` and `CreatedAscending`; the default is `CreatedDescending` unless the collection says otherwise. Unknown values are `400 ValidationFailed` naming the allowed ones. |
| `search` | Optional, trimmed, 1 to 200 characters, over the collection's documented searchable fields. |
| `createdAfterUtc`, `createdBeforeUtc` | Optional time window on the collection's created column. `createdBeforeUtc` must be after `createdAfterUtc`. |
| `labels` | Optional, at most 20. Objects must carry these labels. |
| `labelMatch` | `All` (default: every listed label) or `Any` (at least one). |
| `tags` | Optional dictionary, at most 20 entries. Objects must carry every key with exactly that value (key case-insensitive, value case-sensitive). The value `*` matches any value (the key is present). |
| `filters` | Optional collection-specific filters (listed with each collection). Unknown keys are `400 ValidationFailed` (`fieldErrors["filters.<name>"]`). |

Label and tag filters on a collection without metadata (subscription events, discovery) are `400 ValidationFailed`. Non-staff callers filtering on a `staff:` label or tag key get `400 ValidationFailed`.

### GET form parameters

| Parameter | Maps to |
|---|---|
| `maxResults`, `continuationToken`, `skip`, `ordering`, `search`, `createdAfterUtc`, `createdBeforeUtc`, `labelMatch` | The same fields |
| `labels` | Repeatable (`labels=a&labels=b`); comma-separated values are also split (`labels=a,b`) |
| `tag.<key>=<value>` | One `tags` entry per key, for example `tag.level=beginner`; `tag.cohort=*` means "has the key" |
| Any other name | A collection filter, for example `status=Active` is `filters.status`. An unknown name is therefore `400 ValidationFailed` with `fieldErrors["filters.<name>"]` |

Path parameters (such as `{id}`) are never read as filters. Values are URL-decoded and trimmed.

```bash
curl -H "Authorization: Bearer $TK_ADMIN_TOKEN" \
  "http://127.0.0.1:8000/api/v1/admin/signup-codes?maxResults=2&status=Active&labels=seminar&tag.cohort=*&ordering=NameAscending"
```

### `EnumerationResult<T>`

```json
{
  "success": true,
  "maxResults": 25,
  "skip": 0,
  "continuationToken": "eyJ2IjoxLCJmIjoiNGE...",
  "endOfResults": false,
  "totalRecords": 1342,
  "recordsRemaining": 1317,
  "timestampUtc": "2026-11-11T23:00:00Z",
  "objects": [ ]
}
```

| Field | Rule |
|---|---|
| `maxResults` | The clamped page size actually used |
| `skip` | The skip actually used (0 when a token was sent) |
| `objects` | At most `maxResults` items |
| `continuationToken` | Send it back unchanged for the next page; null when `endOfResults` is true |
| `endOfResults` | True on the last page |
| `totalRecords`, `recordsRemaining` | Exact for admin collections. For member collections they are null when `enumeration.countMemberTotals` is false (default true). Clients never page with them |
| `timestampUtc` | When the page was read |

Clients drive infinite scroll or "Load more" from `continuationToken`; admin tables may jump with `skip`. No client ever asks for "all". Lists ordered by a value that can change (for example notifications by `updatedUtc`) can repeat an item across pages; clients de-duplicate by `id`.

### Bounded previews

Detail payloads never embed a growing list. They embed a preview of at most 5 objects with the total and the path of the full enumeration:

```json
"sessions": { "objects": [ { "id": "ses_...", "...": "..." } ], "totalRecords": 12, "enumerate": "/api/v1/admin/members/usr_.../sessions/enumerate" }
```

Single-object documents may retain bounded attributes: property configuration, an object's own labels (max 50) and tags (max 50), `rankHistory` (max `profile.rankHistory.maxEntries`), `roles` (max 20 assignments), and media variants. Every collection retrieval, including fixed catalogs, uses the standard enumeration envelope.

### Labels and tags

```json
{
  "labels": ["fundamentals", "staff:featured"],
  "tags": { "level": "beginner", "staff:review": "pending" }
}
```

| Rule | Value |
|---|---|
| `labels` | List of strings, order not significant, case-insensitive duplicates removed, at most 50 public labels per object (plus at most 50 `staff:` labels, counted separately so a member's limit never reveals hidden staff metadata). Each 1 to 64 characters of letters, digits, spaces, and `_ - . : /`, trimmed; compared case-insensitively, stored with the casing first used |
| `tags` | Dictionary of string to string, at most 50 public keys per object (plus at most 50 `staff:` keys, counted separately). Keys use the label character set (1 to 64, compared case-insensitively); values 0 to 512 characters |
| `staff:` entries | Labels and tag keys starting with `staff:` are visible to and editable by staff only: `SystemAdmin` for every type, and `FeedModerator` holders for feed content (feed posts, comments, media, reactions, reports, moderation actions). They are stripped from every payload served to non-staff and ignored when a non-staff caller sends them |
| Who may edit | The owner (the member for their own profile; the uploader for media) and staff. Objects without a member owner (sign-up codes, batches, subscriptions, entitlements, sessions, audit records, request history, word filter terms) are staff-only |
| Replace semantics | On create and update routes that accept them, a provided `labels` replaces the whole list and a provided `tags` replaces the whole dictionary; omitted means unchanged |
| Word filter | Labels and tag values from members pass through the word filter (`400 ContentFiltered`) |

`targetType` values (the engine's object type names): `User`, `FeedPost`, `Comment`, `Media`, `Reaction`, `Notification`, `Report`, `ModerationAction`, `WordFilterTerm`, `PushToken`, `SignupCode`, `SignupCodeRedemption`, `SignupCodeBatch`, `Subscription`, `Entitlement`, `Session`, `AuditRecord`, `RequestHistory`, `FeedPostRevision`, `CommentRevision`, `Thread`, `Message`, and the Phase 4 and 5 types below. An unknown value is `400 ValidationFailed`. Revisions are read-only: their labels and tags are copied from the post or comment when the revision is stored, they filter the revision enumerations, and the generic metadata routes answer `404` for revision types.

Routes that accept `labels` and `tags` in their body: `PUT /api/v1/me/profile`, `POST /api/v1/admin/members/{id}/comp` (stored on the comp entitlement), `POST /api/v1/admin/signup-codes`, `PUT /api/v1/admin/signup-codes/{id}`, `POST /api/v1/admin/signup-codes/batches` (on the batch and every code), `POST /api/v1/media/uploads`, `PUT /api/v1/media/{id}`, `PUT /api/v1/admin/media/{id}`, `POST /api/v1/feed`, `PUT /api/v1/feed/{id}`, `POST /api/v1/admin/feed`, `PUT /api/v1/admin/feed/{id}`, `POST /api/v1/feed/{id}/comments`, `PUT /api/v1/comments/{id}`, `POST /api/v1/admin/moderation/word-filter`, `PUT /api/v1/admin/moderation/word-filter/{id}`. Every other object is labeled through the generic routes below.

### Generic metadata routes (Member)

| Method and path | Body | Success | Errors |
|---|---|---|---|
| `GET /api/v1/metadata/{targetType}/{targetId}` | | `200 Metadata` | `400 ValidationFailed` (unknown type), `404 NotFound` |
| `PUT /api/v1/metadata/{targetType}/{targetId}` | `{ labels?, tags? }` (replace semantics) | `200 Metadata` | `400 ValidationFailed`, `400 ContentFiltered`, `403 Forbidden` (not owner or staff), `403 Muted`, `404 NotFound` |
| `PATCH /api/v1/metadata/{targetType}/{targetId}` | `{ addLabels?, removeLabels?, setTags?, removeTagKeys? }` | `200 Metadata` | same as `PUT` |

Read access: the owner, staff, or (for `User`, `FeedPost`, `Comment`, `Media`, and `Reaction`) any member; otherwise `404`. Staff entries are only returned to staff.

Phase 4 and 5 types on the generic routes, with their owner and staff (staff edit `staff:` entries too; owners edit public entries; anyone else gets `404` on reads and `403` on writes):

| Types | Owner | Staff |
|---|---|---|
| `Event` | The member who proposed it (`proposedByUserId`) | `EventManager` |
| `EventRsvp`, `CalendarFeed` | The member | `EventManager` (RSVPs) |
| `EventCategory`, `EventRecurrence`, `EventReminder` | None | `EventManager` |
| `Series`, `Lesson`, `LessonResource`, `LessonMarker`, `LessonCaption`, `LibraryLevel` | None | `LibraryEditor` |
| `LessonProgress`, `LessonDownload`, `SeriesFollow` | The member | `SystemAdmin` |
| `Venue`, `Instructor`, `VenueInstructor`, `DirectoryProgram` | None | `DirectoryModerator` |
| `DirectorySubmission` | The submitter | `DirectoryModerator` |
| `Idea`, `IdeaVote` | The author or voter | `SupportAgent` |
| `IdeaStatus`, `TicketMessage` (author owns) | None (`TicketMessage`: its author) | `SupportAgent` |
| `Conversation` | None (members label their own `ConversationMember` row) | `SystemAdmin` |
| `Broadcast`, `BroadcastSegment`, `BroadcastDelivery` (the recipient owns a delivery) | None | `Communicator` |
| `PropertyDocumentVersion`, `DocumentAcceptance` (the member owns an acceptance), `AssistantConversation` (the member owns it) | As noted | `SystemAdmin` |

`SystemAdmin` is staff for every type. Deleted objects answer `404`.

```json
{ "targetType": "Media", "targetId": "med_...", "labels": ["fundamentals"], "tags": { "level": "beginner" } }
```

```bash
curl -X PATCH -H "Authorization: Bearer $ACCESS_TOKEN" -H "Content-Type: application/json" \
  -d '{"addLabels":["fundamentals"],"setTags":{"level":"beginner"},"removeTagKeys":["draft"]}' \
  http://127.0.0.1:8000/api/v1/metadata/Media/med_...
```

### Admin bulk metadata (Admin)

`POST /api/v1/admin/metadata/bulk` applies one incremental change to up to 500 objects of one type.

```json
{ "targetType": "SignupCode", "targetIds": ["suc_a", "suc_b"], "addLabels": ["seminar"], "removeLabels": [], "setTags": { "cohort": "spring" }, "removeTagKeys": [] }
```

Success `200 { "updated": 2, "notFound": [] }` (`notFound` lists requested ids that do not exist; it holds at most the 500 requested ids). Errors: `400 ValidationFailed` (`fieldErrors.targetIds` for 0 or more than 500 ids, or a change that would exceed 50 labels or 50 tags of one kind, public or `staff:`, on an object), `403 Forbidden`.

### Discovery enumerations (Member)

For label and tag autocomplete. POST only, with an `EnumerationQuery` body plus the fields below. Orderings: `CountDescending` (default; most used first), `CreatedDescending`, and `CreatedAscending`. Label and tag filters and `filters` are not accepted: a label filter is `400 ValidationFailed` under `fieldErrors.labels`, a tag filter under `fieldErrors.tags`.

| Method and path | Extra body fields | Objects |
|---|---|---|
| `POST /api/v1/labels/enumerate` | `targetType?`, `prefix?` | `{ label, count }` |
| `POST /api/v1/tags/keys/enumerate` | `targetType?`, `prefix?` | `{ key, count }` |
| `POST /api/v1/tags/values/enumerate` | `key` (required), `targetType?`, `prefix?` | `{ value, count }` |

Non-staff callers never see `staff:` entries, and counts only include objects they can see. Without `targetType`, only `SystemAdmin` counts as staff. Errors: `400 ValidationFailed` (unknown `targetType`, missing `key`, a `staff:` key from non-staff).

```bash
curl -X POST -H "Authorization: Bearer $ACCESS_TOKEN" -H "Content-Type: application/json" \
  -d '{"targetType":"Media","prefix":"fun","maxResults":10}' http://127.0.0.1:8000/api/v1/labels/enumerate
```

```json
{ "success": true, "maxResults": 10, "skip": 0, "continuationToken": null, "endOfResults": true, "totalRecords": 1, "recordsRemaining": 0, "timestampUtc": "2026-11-11T23:00:00Z", "objects": [ { "label": "fundamentals", "count": 14 } ] }
```

---

## Tokens

| | Web (`X-Client: web`) | Mobile and other clients |
|---|---|---|
| Access token | In the body; keep it in memory; send `Authorization: Bearer <accessToken>` | Same |
| Refresh token | HttpOnly cookie `tk_refresh`, `Path=/api/v1/auth`, `SameSite=Lax`, `Secure` except on plain HTTP to a loopback host; `refreshToken` is `null` in the body | In the body (store it in secure storage) |
| Refresh | `POST /api/v1/auth/refresh` with no body (`credentials: 'include'`) | `POST /api/v1/auth/refresh` with `{ "refreshToken": "..." }` |

- Access tokens are opaque AES-256-GCM tokens (session id, member id, issued, expiry, nonce), valid `auth.accessTokenMinutes` (default 60). `accessTokenExpiresUtc` says when.
- Sessions last `auth.refreshTokenDays` (default 90). Refresh tokens rotate on every refresh and are stored only as SHA-256 hashes. Presenting a refresh token that was already rotated revokes the whole session, unless it was rotated less than `auth.refreshReuseGraceSeconds` (default 10) ago: of several concurrent refreshes with one token (mobile body or web cookie), exactly one succeeds, the others get `401 InvalidRefreshToken`, and the winner's new tokens stay valid.
- Validated sessions are cached for `auth.sessionCacheSeconds` (default 30). Revocations on the same server instance apply immediately.

### AuthResponse

```json
{
  "accessToken": "opaque",
  "accessTokenExpiresUtc": "2026-11-11T21:00:00Z",
  "refreshToken": "opaque, or null for web",
  "isNewAccount": false,
  "user": { "...": "Me" }
}
```

### Me

```json
{
  "id": "usr_...", "email": "pat@example.com", "emailVerified": true,
  "displayName": "Pat Example", "handle": "pat", "avatarUrl": "/api/v1/media/avatars/usr_..._1731350000000.jpg",
  "bio": "Advanced member...", "rank": "Advanced", "rankLevel": 2, "affiliation": "Example Club",
  "city": "San Jose", "region": "CA", "country": "US",
  "goals": ["Learning", "Teaching"], "goalsOther": null,
  "rankHistory": [ { "id": "lin_...", "rank": "Advanced", "awardedByName": "Coach X", "awardedByUserId": null, "affiliation": "Example Club", "dateAwarded": "2015-06-01" } ],
  "profile": {
    "rank": { "key": "Advanced", "level": 2 }, "affiliation": "Example Club", "city": "San Jose", "region": "CA", "country": "US",
    "goals": { "keys": ["Learning", "Teaching"], "otherText": null },
    "rankHistory": [ { "id": "lin_...", "values": { "rank": { "key": "Advanced" }, "awardedBy": { "name": "Coach X", "userId": null }, "affiliation": "Example Club", "dateAwarded": "2015-06-01" } } ]
  },
  "profileMissing": [],
  "ageAttested": true, "profileComplete": true, "signInMethods": ["Email", "Google"],
  "roles": [ { "role": "Moderator", "resourceType": "Forum", "resourceId": "*" } ],
  "flags": { "canPostToFeed": false, "canPromoteToFeed": false, "canUploadVideo": true, "canPost": true, "canPostMedia": true, "canSendDirectMessages": true, "isMuted": false, "mutedUntilUtc": null },
  "membership": { "isActive": true, "source": "Subscription", "store": "Stripe", "expiresUtc": "2027-11-11T00:00:00Z", "autoRenew": true, "willConvertToPaid": false },
  "accessTier": "Member", "membershipRequired": false,
  "pendingDocuments": [],
  "createdUtc": "2026-11-11T20:00:00Z",
  "labels": ["fundamentals"], "tags": { "level": "beginner" }
}
```

| Field | Values |
|---|---|
| `profile` | Configurable profile fields ([Profile fields](#profile-fields)): every enabled field visible to the member (`Public`, `Members`, `Private`), every key present, `null` when unset. Value shapes per type are in [Value shapes](#value-shapes) |
| `profileMissing` | The keys that keep `profileComplete` false, built-ins first (`displayName`, `handle`), then profile order |
| `rank`, `rankLevel`, `affiliation`, `city`, `region`, `country`, `goals`, `goalsOther`, `rankHistory` | **Deprecated** (compatibility window, setting `profileFields.legacyFieldsMode`): mirrors of the seeded fields with the same keys (`profile.rank.key`, `profile.rank.level`, `profile.goals.keys`, `profile.goals.otherText`, `profile.rankHistory[]` as `{ id, rank, awardedByName, awardedByUserId, affiliation, dateAwarded }`). Null, `0`, or empty when the field is missing, disabled, not visible, or the mode is `Off` |
| `profileComplete` | `displayName` and `handle` set, `ageAttested` true, and every enabled field with `collectAt: SignUp` and `validation.required` has a value |
| `roles[].role` | `SystemAdmin`, `Moderator`, `FeedModerator`, `DirectoryModerator`, `EventManager` (`User` is implicit) |
| `flags` | `canPostToFeed` (default false) allows feed posts; `canPost` (default true) false blocks all posting (`403 PostingNotAllowed`); `canPostMedia` (default true) false makes the member text-only (`403 MediaNotAllowed`); `canUploadVideo` (default true) false allows images only (`403 VideoUploadNotAllowed`); `isMuted` and `mutedUntilUtc` give `403 Muted` on writes. `SystemAdmin` ignores the permission flags |
| `labels`, `tags` | The member's own `User` metadata (staff see `staff:` entries too) |
| `accessTier` | `SignedIn`, `Member` (an active membership outside `Open`), or `Staff` (SystemAdmin) under the property's access model |
| `membershipRequired` | True only in `FullyGated` for a non-member: show the paywall step after sign-up and onboarding only when true |
| `pendingDocuments` | `DocumentSummary[]` ([Property documents](#property-documents)): gating documents the member has not accepted at the required version (bounded by `property.json`, at most 30). Show the review-and-accept step while non-empty. Each carries `previouslyAccepted`: `true` when the member accepted an earlier version this one replaces (word the prompt as an updated document), `false` when they never accepted it ("Please review and accept") |
| `membership` | In `Open`, always inactive with nulls on member surfaces (entitlements are kept). Otherwise computed from every entitlement: active when any is active; `expiresUtc` is the end of continuous coverage (the latest end wins, null means lifetime or none); `source` is the primary source (a paid subscription first); `willConvertToPaid` is true when a renewing paid subscription takes over after a free period |

---

## Authentication

| Method and path | Body | Success | Errors |
|---|---|---|---|
| `GET /api/v1/auth/providers` | | `200` availability and public client ids (below) | |
| `POST /api/v1/auth/register` | `{ email, password, displayName, dateOfBirth: "YYYY-MM-DD", acceptTerms: true, acceptedDocuments: [{ key, version }] }` (every `RequiredAtSignup` document with `separateCheckbox`, at its current version) | `201 AuthResponse` (`isNewAccount: true`); verification email sent; acceptances recorded (method `Signup`) | `400 ValidationFailed` (`fieldErrors["acceptedDocuments.<key>"]` for a missing or stale document; nothing stored), `403 AgeRequirementNotMet` (nothing stored), `409 EmailInUse` |
| `POST /api/v1/auth/login` | `{ email, password }` | `200 AuthResponse` | `401 InvalidCredentials`, `423 AccountLocked` (`auth.lockoutThreshold` failures, default 10, within `auth.lockoutMinutes`, default 15), `403 AccountBanned` |
| `POST /api/v1/auth/apple` | `{ identityToken, nonce (raw), givenName?, familyName? }` | `200 AuthResponse` | `401 InvalidIdentityToken`, `409 AccountLinkRequired`, `403 AccountBanned`, `503 IntegrationNotConfigured` |
| `POST /api/v1/auth/google` | `{ idToken }` | `200 AuthResponse` | same as Apple |
| `POST /api/v1/auth/facebook` | `{ token, tokenType: "Jwt" \| "AccessToken", nonce? }` (`nonce` is the raw nonce for iOS Limited Login JWTs) | `200 AuthResponse` | same as Apple |
| `POST /api/v1/auth/refresh` | mobile `{ refreshToken }`; web none (cookie) | `200 AuthResponse` | `401 InvalidRefreshToken` (a web cookie is cleared) |
| `POST /api/v1/auth/logout` | mobile `{ refreshToken }`; web none | `204` (web cookie cleared) | none |
| `POST /api/v1/auth/password/forgot` | `{ email }` | `202` always | `429 RateLimited` (`auth.passwordResetRequestsPerHour` per email, four times that per IP) |
| `POST /api/v1/auth/password/reset` | `{ token, newPassword }` | `204`; every session revoked | `400 InvalidToken`, `400 ValidationFailed` |
| `POST /api/v1/auth/email/verify` | `{ token }` | `204` | `400 InvalidToken` |
| `GET /api/v1/auth/me` (signed in) | | `200 Me` | `401` |
| `POST /api/v1/auth/email/resend` (signed in) | | `202` | `429 RateLimited` (`auth.verificationResendsPerHour`) |

- **Passwords:** 10 to 128 characters, not equal to the email. Hashed with PBKDF2-HMAC-SHA256.
- **Social sign-up:** new accounts start with `ageAttested: false`; member routes, purchases, and codes return `403 AgeAttestationRequired` until `POST /api/v1/me/age-attestation`, and then `403 DocumentAcceptanceRequired` until the gating documents are accepted (in the attestation body or with `POST /api/v1/me/documents/accept`). Provider names fill `displayName` (Apple's `givenName` and `familyName` on first sign-in).
- **`409 AccountLinkRequired`:** the provider's email belongs to an account that does not use this provider. The body adds `"existingMethods": ["Email", "Google"]`. Sign in with an existing method and link from `POST /api/v1/me/identities/{provider}`.
- **Bootstrap admin:** the member whose verified email equals `auth.bootstrapAdminEmail` receives `SystemAdmin` (on verification, password reset, provider sign-in with a verified email, or the next sign-in). When `auth.bootstrapAdminPassword` is also set and the primary property has no members, startup creates that account (display name `Administrator`, email verified, `SystemAdmin`, the property's current documents recorded as accepted) with that password, so it can sign in at once. The sign-up password length rule does not apply to this configured password and the email needs only one `@` (local addresses work); a password under 10 characters is logged as a warning. Once any member exists nothing is created.
- **Minimum age:** registration and age attestation require the property `membership.minimumAge` (the message names it).
- **Emails:** `{web.publicBaseUrl}` is the setting when set, otherwise `https://` plus the property `domains.web`. The sender defaults to the property `contact.emailFromName` and `contact.emailFromAddress` unless `email.fromName` or `email.fromAddress` is set. Verification links go to `{web.publicBaseUrl}/verify-email?token=...` (single use, 48 hours) and resets to `{web.publicBaseUrl}/reset-password?token=...` (single use, 1 hour). With no SMTP host configured, the server logs each message's subject and link instead of sending it.

`GET /api/v1/auth/providers`:

```json
{
  "email": true,
  "apple": { "enabled": false, "webClientId": null },
  "google": { "enabled": false, "webClientId": null, "iosClientId": null, "androidClientId": null },
  "facebook": { "enabled": false, "appId": null },
  "stripe": { "enabled": false },
  "appleIap": { "enabled": false, "productId": "example.membership.annual" },
  "googlePlay": { "enabled": false, "productId": "example.membership.annual" }
}
```

```bash
curl -X POST http://127.0.0.1:8000/api/v1/auth/login -H "Content-Type: application/json" -H "X-Client: mobile" \
  -d '{"email":"me@example.com","password":"a long local password"}'
```

---

## Account (signed in)

| Method and path | Body | Success | Errors |
|---|---|---|---|
| `POST /api/v1/me/age-attestation` | `{ dateOfBirth: "YYYY-MM-DD", acceptTerms?, acceptedDocuments?: [{ key, version }] }` (date never stored; documents recorded after a successful attestation) | `200 Me` | `400 ValidationFailed` (also `acceptedDocuments.<key>` for unknown or stale documents), `403 AgeRequirementNotMet` (account closed and signed out) |
| `PUT /api/v1/me/profile` | any of `displayName, handle, bio, profile: { <key>: <value> \| null }, labels, tags`, plus the deprecated `rank, rankLevel, affiliation, city, region, country, goals, goalsOther, rankHistory` | `200 Me` | `400 ValidationFailed` (`fieldErrors.handle` is "That handle is taken." when not unique), `400 ContentFiltered` (display name, bio, labels, or tag values; `fieldErrors` names `displayName`, `bio`, `labels[i]`, or `tags.key`), `403 Muted` (a changed display name or bio, labels, or tags while muted) |
| `GET /api/v1/handles/{handle}/availability` | | `200 { "available": true }` (your own handle counts as available) | `400 ValidationFailed` |
| `PUT /api/v1/me/avatar` | raw image, `Content-Type: image/jpeg \| image/png \| image/webp`, at most 5 MB | `200 Me` | `400 InvalidImage` (type checked by magic bytes), `403 Muted`, `413 TooLarge` |
| `DELETE /api/v1/me/avatar` | | `200 Me` | `403 Muted` |
| `PUT /api/v1/me/password` | `{ currentPassword?, newPassword }` | `204`; other sessions revoked | `400 ValidationFailed` (`currentPassword` required when one is set), `401 InvalidCredentials` |
| `POST /api/v1/me/identities/{provider}` | same body as `/auth/{provider}`; `provider` is `apple`, `google`, or `facebook` | `200 Me` | `401 InvalidIdentityToken`, `409 IdentityInUse`, `503 IntegrationNotConfigured` |
| `DELETE /api/v1/me/identities/{provider}` | | `200 Me` | `409 LastSignInMethod` |
| `POST /api/v1/me/export` | none | `202 { exportId, status }` | `409 ExportInProgress` (with the `exportId` in progress), `429 RateLimited` |
| `GET /api/v1/me/export/{id}` | | `200 AccountExport` | `404 NotFound` |
| `GET /api/v1/me/export/{id}/download?exp=&sig=` (anonymous) | | `200` the export file | `403 InvalidSignature`, `404 NotFound` |
| `GET /api/v1/me/export` | | never succeeds | `410 ExportMoved` (use the export job) |
| `DELETE /api/v1/me` | `{ "confirm": "DELETE" }` | `202 { message, storeSubscriptionNote }` | `400 ValidationFailed` without the confirmation |
| `GET /api/v1/me/appearance` | | `200 AppearanceView { palettePreset (stored: a preset id, or null for the property palette), effectivePreset (palettePreset while the property still offers it, else null), presetsAvailable }` | `404 NotFound` |
| `PUT /api/v1/me/appearance` | `{ palettePreset: <id from GET /api/v1/property brand.presets> \| null }` (null or empty: the property palette) | `200 AppearanceView`; stored on the account (`users.palettepreset`), so it follows the member to every client. A preset the administrator later stops offering is kept but not applied (`effectivePreset` null) | `400 ValidationFailed` (`fieldErrors.palettePreset`: unknown preset, or one the property does not offer), `404 NotFound` |

Profile rules: only properties present in the body change, and `null` clears an optional field. `profile` is merged key by key (a provided key replaces that field's value, for `History` and `MultiSelect` the whole list; `null` clears it; an omitted key is unchanged); the rules per key and the per-type validation are in [Writing values](#writing-values). Sending a deprecated field together with its `profile` key is `400 ValidationFailed` ("Send either rank or profile.rank, not both."); with `profileFields.legacyFieldsMode` `ReadOnly` or `Off` a deprecated field is `400` ("This field moved to profile.<key>."). Every profile edit is audited (`profile.update` for the member's own edits, `member.profile` for staff edits; key names only). Deprecated fields: `displayName` 2 to 60 characters; `handle` 3 to 30 of `A-Z a-z 0-9 _ .`, unique case-insensitively; `bio` up to 1000; `affiliation`, `city`, `region` up to 100; `country` ISO 3166-1 alpha-2 (upper-cased); `rank` a key from `profile.rank.values` (matched case-insensitively, stored as the canonical key); `rankLevel` 0 to `profile.rank.maxLevel`; `goals` keys from `profile.goals.values`; `goalsOther` up to 200; `rankHistory` replaces the whole list, up to `profile.rankHistory.maxEntries` entries, each `{ id?, rank (required), awardedByName?, awardedByUserId?, affiliation?, dateAwarded? (YYYY-MM-DD, not in the future) }`; entries with an existing `id` keep it. Rank history errors use keys such as `rankHistory[0].rank`. A value for a section the property disables (`profile.rank`, `profile.rankHistory`, `profile.goals`, `profile.affiliation`) is `400 ValidationFailed` with "The <field> field is not enabled for this community."; `null`, `0`, and empty lists are accepted and clear the field. `labels` and `tags` are the member's own public metadata (replace semantics; `staff:` entries from the member are ignored). A changed `displayName` or `bio` passes through the word filter: a `Block` term is `400 ContentFiltered` naming the field, a `Flag` term saves and files one system report (reason `Filter`) on the member. The same screening applies to the display name at registration and to staff edits of a member's profile (the member's words are checked even though the caller is staff). While muted, a member cannot change their display name, bio, or avatar (`403 Muted`); other fields, and unchanged values, still save.

Deletion anonymizes the account at once (email, handle, profile, avatar, goals, rank history, identities, and password are removed), revokes every session, and cancels a renewing Stripe subscription at period end. Content: every comment the member wrote becomes a `Deleted` placeholder shown as "[deleted]" (body, revisions, mentions, labels, and tags cleared at once, so replies by others stay readable; comment counts are recomputed), feed posts are soft-deleted, every reaction the member gave is deleted (reaction counts and scores are recomputed), mentions of the member are deleted, and media attached to their posts and comments is purged. Notifications: the member's own notifications are deleted; in other members' notifications the member's name and erased text disappear (a notification the member was the only actor of is deleted, an aggregated one is re-rendered from the remaining actors, for example "Alex Chen and 2 others reacted to your post", and a moderation notice keeps its text without the actor). Reports the member filed stay, with the reporter shown as the Deleted member. `storeSubscriptionNote` is "Cancel your App Store subscription in iPhone Settings." or "Cancel your Google Play subscription in the Play Store app." when one is renewing, otherwise null. The email can be registered again.

### Account export (asynchronous)

`POST /api/v1/me/export` queues an export of everything stored about the member. One export runs at a time per member (`409 ExportInProgress` while one is Pending or Running) and a member may request at most `account.exportsPerDay` (default 3) per rolling day (`429 RateLimited` with `Retry-After`). Export files are stored in the media object store (the private retained location: the retained bucket in AWS, `storage.directory/retained` locally) under `{propertyId}/exports/{exportId}.json`, so any server instance serves the download and replacing an instance loses nothing.

```json
{ "exportId": "aex_mwd1k2b9_Q3n8...", "status": "Pending" }
```

`GET /api/v1/me/export/{id}` (owner only; anyone else gets `404`):

```json
{
  "id": "aex_mwd1k2b9_Q3n8...",
  "status": "Ready",
  "createdUtc": "2026-11-11T20:00:00Z",
  "completedUtc": "2026-11-11T20:00:03Z",
  "expiresUtc": "2026-11-13T20:00:03Z",
  "downloadUrl": "/api/v1/me/export/aex_mwd1k2b9_Q3n8.../download?exp=1762891200&sig=9f2c...",
  "sizeBytes": 48211
}
```

| Field | Rule |
|---|---|
| `status` | `Pending`, `Running`, `Ready`, `Failed`, or `Expired`. A request wakes the export worker on the instance that took it, so generation starts at once (the worker also runs every 15 seconds for retries and expiry) |
| `downloadUrl`, `sizeBytes` | Only when `Ready`. The link is root-relative, needs no `Authorization` header, and is valid for `account.exportDownloadLinkMinutes` (default 15); read the export again for a fresh link |
| `expiresUtc` | When the stored file is deleted from the object store by the export worker (`account.exportRetentionHours` after completion) |

The download (`GET .../download?exp=&sig=`, anonymous, signature-verified) streams `application/json` with `Content-Disposition: attachment; filename="example-export-aex_....json"` and `Cache-Control: private, no-store`. A bad or expired link is `403 InvalidSignature`; an export that is not Ready, expired, or deleted is `404`. The file is one JSON object: `exportId`, `exportedUtc`, then one property per section: `profile`, `identities`, `sessions`, `subscriptions`, `entitlements`, `redemptions`, `auditRecords`, `metadata`, `feedPosts`, `comments` (with their revisions), `reactions`, `notificationPreferences`, `pushTokens` (no token values), `blocks`, `reports` (filed by the member), `media` (metadata only), `assistantConversations`, `threads` (with revisions), `pollVotes`, `follows`, and `forumMutes`, each written in bounded batches. `assistantConversations` holds every conversation the member owns (`{ id, assistantId, assistantTitle, title, status, isArchived, surface, messageCount, labels, tags, createdUtc, lastMessageUtc, messages }`) with its messages streamed oldest first (`{ id, role, content, status, isSuperseded, citations, feedback: { rating, note, reasons, createdUtc } | null, toolCalls: [ { id, toolName, argumentsJson, status, resultCount, round, results, createdUtc } ], createdUtc, completedUtc }`, no model telemetry and no tool output), read 50 conversations and 200 messages at a time.

### Public profiles and media

| Method and path | Access | Success |
|---|---|---|
| `GET /api/v1/users/{id}` | Member | `200 { id, displayName, handle, avatarUrl, bio, profile, rank, rankLevel, affiliation, city, region, country, rankHistory, createdUtc, isStaff, isBlockedByMe, labels, tags }`; `404` for unknown, banned, or deleted accounts. `profile` holds the fields visible to the caller (Self sees `Private`, staff see everything) with `showOnProfile` and a value (unset keys omitted); `bio` is null when the bio field's visibility hides it. The deprecated `rank` ... `rankHistory` follow the same visibility. `rankHistory` holds at most its field's `validation.maxEntries` entries |
| `GET /api/v1/media/avatars/{file}` | Anonymous (a bearer token is accepted and ignored) | Image bytes, `Cache-Control: public, max-age=31536000, immutable` (names change on every upload), `X-Content-Type-Options: nosniff`; `404` otherwise |

---

## Property (anonymous)

| Method and path | Success | Errors |
|---|---|---|
| `GET /api/v1/property` | `200` the public property configuration: everything in `property.json` except `web` and the seeding-only `profileFields`, with absolute brand image URLs, plus `slug`, `profileFields` (client definitions), the generated deprecated `profile`, `minimumAge`, and `access` ([Access models](#access-models)); `membership` is null in the `Open` model. `brand.colors` is the property's effective brand palette (every key in both schemes, `onAccentSoft` included; see [Admin: branding and the AI switch](#admin-branding-and-the-ai-switch-systemadmin)) and `brand.paletteVersion` its version; `brand.presets` lists the engine palette presets members may choose (`[{ id, name, description, light, dark }]`, catalog order, empty when none; see `PUT /api/v1/admin/property/branding/member-presets`); `features.ai` is the property's AI switch and `ai.enabled`. `mobile.navigation` carries the optional validated drawer order and up to four bottom shortcuts (see [mobile navigation](docs/TRELLISKIT.md#23-mobile-navigation-mobilenavigation)); null means the client retains its bundled/default navigation. `Cache-Control: public, max-age=300`, `ETag: "v<document>-f<profile field revision>-a<model><access revision>-u<check interval>-i<AI placement or off>-b<palette version>m<member preset mode and revision>"` (`304` for a matching `If-None-Match`; a palette edit, a member preset change, or an AI switch changes it), `Vary: Host, X-TK-Property`. | |
| `GET /api/v1/property/assets/{file}` | Image bytes of a file directly under the property `brand/` folder (`.png`, `.jpg`, `.jpeg`, `.webp`, `.gif`, `.ico`, `.svg`), with `Cache-Control: public, max-age=3600`, `ETag`, and `304` for a matching `If-None-Match` | `404 NotFound` for anything else (nested paths, `..`, hidden files, other types, missing files) |

The server loads the property at startup from `property.directory` (`TK_PROPERTY_DIR`; default the engine sample `samples/example`, found by searching upward from the working directory, then `/app/property`) and refuses to start when `property.json` is missing or invalid, naming each problem (schema version, required fields, unique keys, color format, positive price, brand files).

```json
{
  "schemaVersion": 1, "id": "app_mwczw7mg_F8RsDcNidltPWZS1seB", "slug": "example", "name": "Example Community", "shortName": "Example",
  "tagline": "A sample TrellisKit property", "description": "...", "owner": { "name": "Example Owner" }, "locale": "en-US",
  "domains": { "web": "example.test", "api": "api.example.test", "media": "media.example.test", "redirects": [] },
  "contact": { "supportEmail": "support@example.test", "emailFromName": "Example Community", "emailFromAddress": "no-reply@example.test" },
  "brand": {
    "logo": "brand/logo.png", "icon": "brand/icon.png", "iconIco": "brand/icon.ico",
    "logoUrl": "http://127.0.0.1:8000/api/v1/property/assets/logo.png",
    "iconUrl": "http://127.0.0.1:8000/api/v1/property/assets/icon.png",
    "iconIcoUrl": "http://127.0.0.1:8000/api/v1/property/assets/icon.ico",
    "colors": { "light": { "primary": "#1F5FBF", "onAccentSoft": "#1F5FBF", "...": "..." }, "dark": { "primary": "#8DB4F0", "onAccentSoft": "#8DB4F0", "...": "..." } },
    "paletteVersion": 1
  },
  "mobile": { "bundleId": "test.example.trelliskit", "androidPackage": "test.example.trelliskit", "scheme": "trelliskitexample", "easProjectId": "" },
  "membership": { "productName": "Example Membership", "priceUsdCents": 999, "interval": "Year", "storeProductId": "example.membership.annual", "minimumAge": 16, "trialDays": 0 },
  "profile": {
    "affiliation": { "enabled": true, "label": "Club" },
    "rank": { "enabled": true, "label": "Level", "levelLabel": "Stars", "maxLevel": 3, "values": [ { "key": "Beginner", "label": "Beginner", "color": "#9CA3AF" } ] },
    "rankHistory": { "enabled": true, "label": "History", "awardedByLabel": "Awarded by", "maxEntries": 12 },
    "goals": { "enabled": true, "label": "Interests", "values": [ { "key": "Learning", "label": "Learning", "freeText": false } ] }
  },
  "profileFields": [
    { "key": "displayName", "builtIn": true, "label": "Name", "type": "Text", "required": true, "collectAt": "SignUp", "visibility": "Public", "...": "..." },
    { "key": "rank", "builtIn": false, "label": "Level", "helpText": "", "placeholder": "", "type": "Rank",
      "options": [ { "id": "pfo_...", "key": "Beginner", "label": "Beginner", "color": "#9CA3AF", "maxLevel": null, "freeText": false, "retired": false } ],
      "rank": { "levelLabel": "Stars", "maxLevel": 3 }, "subFields": [], "validation": { "required": true, "...": null }, "required": true,
      "collectAt": "SignUp", "editableBy": "Member", "visibility": "Public", "showOnProfile": true, "profileOrder": 10, "showOnCard": true, "searchable": false, "filterable": true }
  ],
  "reactions": [ { "key": "like", "emoji": "...", "label": "Like" } ],
  "features": { "signupCodes": true, "directory": true, "events": true, "library": true, "messaging": true, "publicSamples": true, "forums": true, "search": true, "ai": false, "aiPlacement": "Menu" },
  "terminology": { "member": "member", "members": "members", "community": "community" }
}
```

Asset URLs come from configured hosts only, because the document is cacheable: `X-Forwarded-Host` is ignored, the `Host` header is used only when it is one of the property's domains (`domains.api`, `domains.web`, `domains.media`, or the host of `web.publicBaseUrl`; always `https://`) or a loopback host (local development; `http://` unless the server terminates TLS), and anything else gets `https://` plus `domains.api`.

`features` carries the property's switches from `property.json` (`signupCodes`, `directory`, `events`, `library`, `messaging`, `publicSamples`, `forums`, and `search` default true when absent; `ai` defaults false) as effective capability flags: `search` is true only when `features.search` and the deployment setting `search.enabled` are both on, `ai` only when `features.ai` and `ai.enabled` are, and `aiPlacement` (`Menu` or `Tab`) comes from `ai.mobilePlacement`. Clients hide entry points for features whose flag is false.

`profileFields` lists the enabled fields whose visibility is not `Staff`, built-ins included, in profile order (at most 104; definitions only: no `id`, counts, or staff metadata). Clients render onboarding, the profile editor, profile pages, member cards, and member filters from it and skip types they do not know. `profile` (deprecated) is generated from the fields with keys `rank`, `affiliation`, `goals`, and `rankHistory` (labels, non-retired options, enabled) and is `null` when `profileFields.legacyFieldsMode` is `Off`.

---

## Membership and purchases (signed in)

| Method and path | Body | Success | Errors |
|---|---|---|---|
| `GET /api/v1/membership` | | `200 { membership, webOffer: { priceUsd, interval, productName }, storeProductId, storeAccountToken, history }` where `history` is a bounded preview (at most 5, latest start first) | |
| `GET /api/v1/membership/history`, `POST /api/v1/membership/history/enumerate` | `EnumerationQuery` | `200 EnumerationResult<MembershipHistoryItem>` | `400 ValidationFailed`, `400 InvalidContinuationToken` |
| `POST /api/v1/membership/stripe/checkout` | none, or `{ code }` (a discount sign-up code) | `200 { url }`; success returns to `{web.publicBaseUrl}/app/membership?checkout=success`, cancel to `?checkout=cancelled`. With a code the session applies the code's Stripe coupon | `400 InvalidCode` (unknown, inactive, not a discount, or already used; same message), `409 AlreadyMember` (an active paid subscription), `429 RateLimited`, `503 IntegrationNotConfigured` |
| `POST /api/v1/membership/stripe/portal` | none | `200 { url }` | `404 NoStripeCustomer`, `503` |
| `POST /api/v1/membership/apple/verify` | `{ signedTransaction }` | `200 { membership }` | `400 InvalidTransaction`, `409 TransactionBelongsToAnotherAccount`, `503` |
| `POST /api/v1/membership/google/verify` | `{ purchaseToken, productId }` | `200 { membership }` (the purchase is acknowledged) | `400 InvalidPurchase`, `409 TransactionBelongsToAnotherAccount`, `503` |

```json
{
  "membership": { "isActive": true, "source": "Subscription", "store": "Stripe", "expiresUtc": "2027-11-11T00:00:00Z", "autoRenew": true, "willConvertToPaid": false },
  "webOffer": { "priceUsd": 9.99, "interval": "Year", "productName": "Example Membership" },
  "storeProductId": "example.membership.annual",
  "storeAccountToken": "6f1c2a9e-3c55-4d7a-9a61-0d2f5b1e8c44",
  "history": {
    "objects": [ { "id": "ent_...", "source": "Subscription", "store": "Stripe", "startsUtc": "2026-11-11T00:00:00Z", "endsUtc": "2027-11-11T00:00:00Z", "status": "Active", "labels": [], "tags": {} } ],
    "totalRecords": 1,
    "enumerate": "/api/v1/membership/history/enumerate"
  }
}
```

**Membership history collection** (member, scoped to the caller; the age attestation is not required): items `{ id, source, store, startsUtc, endsUtc, status, labels, tags }`.

| | |
|---|---|
| Orderings | `StartsDescending` (default), `StartsAscending`, `CreatedDescending`, `CreatedAscending` |
| Filters | `source` (`Subscription`, `SignupCode`, `AdminComp`), `status` (`Active`, `Scheduled`, `Expired`, `Revoked`) |
| Search | none |
| Labels and tags | Entitlement metadata |

- `storeAccountToken` is a UUID, stable per member. Pass it as `appAccountToken` (StoreKit 2) and `obfuscatedAccountId` (Play Billing). A transaction whose token belongs to someone else, or whose subscription already belongs to another member, is `409`.
- History `status` is `Active`, `Scheduled`, `Expired`, or `Revoked`.
- `webOffer` comes from the property `membership` section (`priceUsdCents / 100`, `interval`, `productName`). `storeProductId` is `appStore.productId` when set, otherwise the property `membership.storeProductId`.
- After Stripe Checkout the website polls `GET /api/v1/membership` until `isActive` is true.

### Webhooks (anonymous; not called by clients)

| Route | Verification | Idempotency |
|---|---|---|
| `POST /api/v1/webhooks/stripe/{propertyId}` | `Stripe-Signature` over the raw body (that property's `stripe.webhookSecret`). Each property's Stripe account posts to its own URL | Stripe event id |
| `POST /api/v1/webhooks/stripe` | Same, for the one property. `SingleProperty` only; `404 NotAvailable` in `MultiProperty` | Stripe event id |
| `POST /api/v1/webhooks/apple` | `{ "signedPayload": "..." }`, JWS verified to Apple's root | `notificationUUID` |
| `POST /api/v1/webhooks/google` | Pub/Sub push OIDC bearer token (`googlePlay.pubSubAudience`, `googlePlay.pubSubServiceAccountEmail`) | Pub/Sub message id |
| `POST /api/v1/webhooks/mediaconvert` | Amazon SNS signature (SignatureVersion 2, SHA256), `SigningCertURL` on `https://sns.<region>.amazonaws.com/`, `TopicArn` equal to `media.snsTopicArn` | MediaConvert `jobId` plus status |

Webhooks never read `X-TK-Property`, `X-TK-App`, or the host. Apple notifications resolve their property from `data.bundleId` (iOS apps), Google notifications from `packageName` (Android apps), and MediaConvert from the media row that owns the job id; the property's own verifier then checks the signature. A notification that matches no property returns `200 { "message": "Ignored." }` and stores nothing. Webhooks for a property being deleted are ignored.

Each store event is stored with its raw payload before processing. A redelivered event that was processed or ignored returns `200 { "message": "Duplicate." }`; one that failed is processed again. Unmatched events (no known member) are recorded as `Ignored`. Processing failures return `500 WebhookFailed` so the store retries. Status mapping: Stripe `active`/`trialing` is `Active`, `past_due` is `InGrace`, `unpaid`/`incomplete` is `InBillingRetry`, `paused` is `Paused`, otherwise `Expired`; Apple `REFUND` is `Refunded`, `REVOKE` is `Revoked`, `EXPIRED` and `GRACE_PERIOD_EXPIRED` are `Expired`, `DID_FAIL_TO_RENEW` is `InGrace` (grace period subtype) or `InBillingRetry`; Google maps `subscriptionsv2` states the same way (a cancelled subscription stays `Active` until its period ends). Members are entitled while a subscription is `Active` or `InGrace` through its period end.

**MediaConvert.** EventBridge forwards "MediaConvert Job State Change" events to an SNS topic with an HTTPS subscription to this route. A failed verification is `400 InvalidSignature`. Once verified the route always answers `200 { "message": "<outcome>" }` so SNS does not retry: `subscribed` (a `SubscriptionConfirmation` was confirmed by fetching `SubscribeURL`), `processed` (a `Notification` with `detail.status` `COMPLETE`, `ERROR`, or `CANCELED`, `detail.jobId`, and `detail.userMetadata.mediaId` changed the media), `duplicate` (already applied), `ignored` (any other message), or `error` (logged; the media worker also polls `GetJob` for jobs older than 15 minutes without a webhook).

A reconciliation worker re-queries every subscription that is not `Expired`, `Revoked`, or `Refunded` every `membership.reconcileIntervalMinutes` (default nightly).

---

## Sign-up codes, member side (signed in, age attested; website only)

| Method and path | Body | Success | Errors |
|---|---|---|---|
| `POST /api/v1/signup-codes/validate` | `{ code }` | `200 { valid: true, kind: "FreeMembership", name, description, grantSummary, membershipEndsUtc, note? }`, `200 { valid: true, kind: "Discount", name, description, grantSummary, discountSummary }`, or `200 { valid: false, message: "This code isn't valid." }` | `429 RateLimited` |
| `POST /api/v1/signup-codes/redeem` | `{ code }` | `200 { membership, grantSummary, membershipEndsUtc }` | `400 InvalidCode` (same message for every reason), `400 DiscountCodeAtCheckout` (use it with checkout), `409 AlreadyRedeemed`, `429 RateLimited` |

- Codes are case-insensitive. Validate and redeem together are limited to `signupCodes.attemptsPerHour` (default 10) per IP and per account.
- With an active paid subscription the free period starts when the paid one ends, and validate adds `note: "Your free membership starts when your current membership ends on <date>."`.
- Redemption runs in one transaction that re-checks the code and increments its count, so a single-use code is redeemed at most once under concurrency. A new account can redeem right after registering (email verification is not required).
- Discount codes (`grantType: "Discount"`): percent off (`discountPercent`, 1 to 100) or amount off (`discountAmountCents`, USD) for the first year (`discountDuration: "Once"`) or every renewal (`"Forever"`), used with `POST /membership/stripe/checkout { code }`. One Stripe coupon is created per code on its first checkout (`stripeCouponId`). The redemption is recorded when the `checkout.session.completed` webhook arrives (once per member, counted toward `maxRedemptions`); it has no entitlement or reminders, and revoking it only marks the record. Discounts never apply to App Store or Google Play purchases.
- Code memberships are reminded by email 14 and 3 days before they end (`signupCodes.firstReminderDays`, `signupCodes.finalReminderDays`), unless another membership continues past the code.

---

## Admin: members (SystemAdmin or admin token)

| Method and path | Notes |
|---|---|
| `GET /api/v1/admin/members`, `POST /api/v1/admin/members/enumerate` | Members collection (below). Deleted accounts are excluded. |
| `GET /api/v1/admin/members/{id}` | Member detail (below). `404 NotFound` |
| `GET /api/v1/admin/members/{id}/subscriptions`, `POST .../subscriptions/enumerate` | The member's subscriptions (subscriptions collection) |
| `GET /api/v1/admin/members/{id}/redemptions`, `POST .../redemptions/enumerate` | The member's sign-up code redemptions (redemptions collection) |
| `GET /api/v1/admin/members/{id}/comps`, `POST .../comps/enumerate` | The member's `AdminComp` entitlements (entitlements collection) |
| `GET /api/v1/admin/members/{id}/sessions`, `POST .../sessions/enumerate` | The member's active sessions; `includeInactive=true` adds revoked and expired ones (sessions collection) |
| `GET /api/v1/admin/members/{id}/membership-history`, `POST .../membership-history/enumerate` | Every entitlement of the member (entitlements collection) |
| `PUT /api/v1/admin/members/{id}/flags` | `{ canPostToFeed?, canPromoteToFeed?, canUploadVideo?, canPost?, canPostMedia?, canSendDirectMessages? }`; only present flags change |
| `PUT /api/v1/admin/members/{id}/roles` | `{ roles: [{ role, resourceType?, resourceId? }] }` (at most 20) replaces non-implicit roles (`resourceType` defaults by role, `resourceId` to `*`). `409 CannotRemoveOwnAdmin` |
| `POST /api/v1/admin/members/{id}/comp` | `{ grantType: "DurationMonths" \| "FixedEndDate" \| "Lifetime", months?, endUtc?, reason, labels?, tags? }`; labels and tags are stored on the comp entitlement |
| `DELETE /api/v1/admin/members/{id}/comp/{compId}` | `{ reason }` ends the comp |
| `DELETE /api/v1/admin/members/{id}` | `{ confirm: "DELETE", reason }` (reason required, at most 500 characters): deletes and anonymizes the account exactly as `DELETE /api/v1/me` (sessions revoked, sign-in methods, profile, and personal data removed, Stripe canceled at period end; posts stay under "Deleted member"), audited `member.delete` with the reason, and emails the member (`account_deleted`). `202 { message, storeSubscriptionNote }`. `400 ValidationFailed` (confirm or reason), `404` (unknown or already deleted), `409 CannotDeleteSelf`, `409 CannotDeleteAdmin` (remove the SystemAdmin role first). The member detail of a deleted account still reads, with `isDeleted: true` |
| `POST /api/v1/admin/members/{id}/ban` | `{ reason }`; revokes every session. `409 CannotBanSelf` |
| `POST /api/v1/admin/members/{id}/unban` | |
| `POST /api/v1/admin/members/{id}/mute` | `{ untilUtc (null for indefinite), reason }` |
| `POST /api/v1/admin/members/{id}/unmute` | |
| `DELETE /api/v1/admin/members/{id}/sessions` | Revokes every session |
| `PUT /api/v1/admin/members/{id}/profile` | `{ profile: { key: value \| null } }`: staff write any configurable profile field ([Profile fields](#profile-fields)); audited `member.profile` |

Every mutation returns `200` with the member detail and writes an audit record. Errors: `400 ValidationFailed`, `403 Forbidden`, `404 NotFound`. Nested enumerations return `404 NotFound` for an unknown member.

**Members collection.** Items `{ id, displayName, handle, email, avatarUrl, profile, rank, rankLevel, membership, roles, isBanned, isMuted, createdUtc, lastSeenUtc, labels, tags }` (staff metadata included; `profile` is the card map as staff see it; `rank` and `rankLevel` are deprecated).

| | |
|---|---|
| Orderings | `CreatedDescending` (default), `CreatedAscending`, `DisplayNameAscending`, `DisplayNameDescending`, `LastSeenDescending` (members who were never seen come last) |
| Filters | `membership` (`Active`, `Inactive`, `Paid`, `Free`), `profile` (`filters.profile.<key>.<operator>` on every enabled, filterable field; [Member filters](#member-filters)), `rank` (deprecated: maps to `filters.profile.rank.in = [<key>]`; an unknown key is `400`), `role` (a role name), `banned` (`true` or `false`) |
| Search | `displayName`, `handle`, `email` |
| Labels and tags | `User` metadata |

```bash
curl -X POST -H "Authorization: Bearer $TK_ADMIN_TOKEN" -H "Content-Type: application/json" \
  -d '{"maxResults":50,"ordering":"LastSeenDescending","search":"pat","labels":["staff:vip"],"filters":{"membership":"Paid","banned":false}}' \
  http://127.0.0.1:8000/api/v1/admin/members/enumerate
```

**Member detail.** `Me` fields (with staff labels and tags; `profile` holds every enabled field, `Staff` and `AdminOnly` included, every key present) plus `isBanned`, `bannedReason`, `isDeleted`, `lastSeenUtc`, and five bounded previews (at most 5 objects each, newest first) with `totalRecords` and the `enumerate` path:

```json
{
  "id": "usr_...", "displayName": "Pat Example", "...": "Me fields",
  "isBanned": false, "bannedReason": null, "isDeleted": false, "lastSeenUtc": "2026-11-11T20:30:00Z",
  "subscriptions": { "objects": [ { "id": "sub_...", "...": "subscription item" } ], "totalRecords": 1, "enumerate": "/api/v1/admin/members/usr_.../subscriptions/enumerate" },
  "redemptions": { "objects": [], "totalRecords": 0, "enumerate": "/api/v1/admin/members/usr_.../redemptions/enumerate" },
  "comps": { "objects": [ { "id": "ent_...", "grantType": "DurationMonths", "grantSummary": "Free membership for 3 months", "startsUtc": "2026-11-11T00:00:00Z", "endsUtc": "2027-02-11T00:00:00Z", "reason": "Instructor", "status": "Active", "grantedByUserId": "usr_...", "revokedUtc": null, "revokeReason": null, "createdUtc": "2026-11-11T00:00:00Z", "labels": [], "tags": {} } ], "totalRecords": 1, "enumerate": "/api/v1/admin/members/usr_.../comps/enumerate" },
  "sessions": { "objects": [ { "id": "ses_...", "client": "Web", "createdUtc": "...", "lastUsedUtc": "...", "expiresUtc": "...", "sourceIp": "127.0.0.1", "userAgent": "...", "labels": [], "tags": {} } ], "totalRecords": 2, "enumerate": "/api/v1/admin/members/usr_.../sessions/enumerate" },
  "membershipHistory": { "objects": [ { "id": "ent_...", "source": "Subscription", "store": "Stripe", "startsUtc": "...", "endsUtc": "...", "status": "Active", "labels": [], "tags": {} } ], "totalRecords": 2, "enumerate": "/api/v1/admin/members/usr_.../membership-history/enumerate" }
}
```

**Nested collections.**

| Collection | Orderings | Filters | Search | Labels and tags |
|---|---|---|---|---|
| Subscriptions | `CreatedDescending` (default), `CreatedAscending`, `CurrentPeriodEndDescending`, `LastEventDescending` | `store`, `status` | store subscription id, member name, email | `Subscription` |
| Redemptions | `CreatedDescending` (default; by redemption time), `CreatedAscending`, `RedeemedDescending`, `MembershipEndsDescending`, `MembershipEndsAscending` | `status` (`Active`, `Expired`, `Revoked`) | member name, email | `SignupCodeRedemption` |
| Comps and membership history (entitlements) | `CreatedDescending` (default), `CreatedAscending`, `StartsDescending`, `StartsAscending` | `source` (`Subscription`, `SignupCode`, `AdminComp`), `status` (`Active`, `Scheduled`, `Expired`, `Revoked`) | comp reason | `Entitlement` |
| Sessions | `CreatedDescending` (default), `CreatedAscending`, `LastUsedDescending` | `includeInactive` (`true` or `false`) | user agent, source IP | `Session` |

## Admin: subscriptions and metrics

| Method and path | Notes |
|---|---|
| `GET /api/v1/admin/subscriptions`, `POST /api/v1/admin/subscriptions/enumerate` | Subscriptions collection: items `{ id, userId, memberDisplayName, memberEmail, store, productId, status, currentPeriodStartUtc, currentPeriodEndUtc, autoRenew, environment, createdUtc, lastEventUtc, labels, tags }` |
| `GET /api/v1/admin/subscriptions/{id}` | Item fields plus `storeSubscriptionId` and `events`, a bounded preview of the newest 5 events with `totalRecords` and `enumerate` (`/api/v1/admin/subscriptions/sub_.../events/enumerate`). `404 NotFound` |
| `GET /api/v1/admin/subscriptions/{id}/events`, `POST .../events/enumerate` | Subscription events collection: items `{ id, source: "Webhook" \| "Verify" \| "Reconcile", type, receivedUtc, processedUtc, outcome, rawPayload }`. `404 NotFound` |
| `GET /api/v1/admin/metrics/membership` | `{ totalMembers, activeMembers, paidActive, freeActive, byStore: { Apple, Google, Stripe }, bySource: { Subscription, SignupCode, AdminComp }, newMembers30d, estimatedArrUsd, lapsed30d }` |

| Collection | Orderings | Filters | Search | Labels and tags |
|---|---|---|---|---|
| Subscriptions | `CreatedDescending` (default), `CreatedAscending`, `CurrentPeriodEndDescending`, `LastEventDescending` | `store` (`Apple`, `Google`, `Stripe`), `status` (`Active`, `InGrace`, `InBillingRetry`, `Expired`, `Revoked`, `Refunded`, `Paused`), `userId` | member name, email, store subscription id | `Subscription` |
| Subscription events | `CreatedDescending` (default; by `receivedUtc`), `CreatedAscending` | `outcome` | event type | Not supported (`400 ValidationFailed`) |

```bash
curl -H "Authorization: Bearer $TK_ADMIN_TOKEN" \
  "http://127.0.0.1:8000/api/v1/admin/subscriptions?store=Stripe&status=Active&ordering=CurrentPeriodEndDescending&maxResults=50"
```

## Admin: sign-up codes

| Method and path | Notes |
|---|---|
| `GET /api/v1/admin/signup-codes`, `POST /api/v1/admin/signup-codes/enumerate` | Sign-up codes collection; items are `SignupCode`. The old `tag` query parameter is the standard `labels` filter |
| `POST /api/v1/admin/signup-codes` | `{ code? (omit to generate), name, description?, notes?, grantType (DurationMonths, FixedEndDate, Lifetime, or Discount), grantMonths?, grantEndUtc?, discountPercent? or discountAmountCents? (Discount: exactly one), discountDuration? (Once or Forever), maxRedemptions? (null = unlimited), startsUtc?, expiresUtc?, labels?, tags? }` returns `201 SignupCode` (adds `discountPercent`, `discountAmountCents`, `discountDuration`, `stripeCouponId`). `400 ValidationFailed`, `409 CodeInUse` |
| `GET /api/v1/admin/signup-codes/summary` | `{ activeCodes, totalRedemptions, redemptions30d, activeFreeMembers, convertedToPaid, conversionRate (0 to 1) }` |
| `GET /api/v1/admin/signup-codes/export.csv` | One CSV page of codes (below) |
| `POST /api/v1/admin/signup-codes/batches` | `{ name, quantity (1 to 1000), grantType, grantMonths?, grantEndUtc?, discountPercent?, discountAmountCents?, discountDuration?, expiresUtc?, labels?, tags? }` returns `201 { batchId, codes, labels, tags }` (single-use codes; labels and tags go on the batch and on every code) |
| `GET /api/v1/admin/signup-codes/{id}` | `SignupCode` plus `redemptionsByDay: [{ date, count }]` (exactly 90 days, oldest first, every day included) and `outcomes: { active, convertedToPaid, lapsed, revoked }`. `404 NotFound` |
| `PUT /api/v1/admin/signup-codes/{id}` | any of `name, description, notes, maxRedemptions, startsUtc, expiresUtc, isActive, labels, tags`; `code`, grant, and discount fields only while `redemptionCount` is 0 (`409 CodeLocked`); `maxRedemptions` cannot go below `redemptionCount`. `409 CodeInUse` |
| `POST /api/v1/admin/signup-codes/{id}/deactivate` | Sets `isActive: false`; existing memberships continue. Returns `SignupCode` |
| `DELETE /api/v1/admin/signup-codes/{id}` | Deletes a code that has never been redeemed, with its labels and tags (audit `signupCode.delete`). Returns `204`. `404 NotFound`; `409 CodeRedeemed` when it has redemptions (deactivate it instead) |
| `GET /api/v1/admin/signup-codes/{id}/redemptions`, `POST .../redemptions/enumerate` | Redemptions collection of the code: items `{ id, signupCodeId, code, userId, memberDisplayName, memberEmail, redeemedUtc, membershipEndsUtc, isNewAccount, status, convertedToPaid, revokedUtc, revokeReason, labels, tags }`. `404 NotFound` |
| `GET /api/v1/admin/signup-codes/{id}/redemptions/export.csv` | One CSV page of the code's redemptions (below). `404 NotFound` |
| `POST /api/v1/admin/signup-codes/{id}/revoke-all` | `{ reason }` returns `{ revokedCount }` |
| `POST /api/v1/admin/signup-code-redemptions/{id}/revoke` | `{ reason }` ends that free membership, emails the member, returns the redemption |

| Collection | Orderings | Filters | Search | Labels and tags |
|---|---|---|---|---|
| Sign-up codes | `CreatedDescending` (default), `CreatedAscending`, `CodeAscending`, `NameAscending`, `RedemptionCountDescending`, `LastRedeemedDescending`, `ExpiresAscending` | `status` (`Active`, `Scheduled`, `Expired`, `Exhausted`, `Inactive`), `batchId` | code, name | `SignupCode` |
| Redemptions | `CreatedDescending` (default; by redemption time), `CreatedAscending`, `RedeemedDescending`, `MembershipEndsDescending`, `MembershipEndsAscending` | `status` (`Active`, `Expired`, `Revoked`) | member name, email | `SignupCodeRedemption` |

**CSV exports are bounded.** Both `export.csv` routes accept the collection's GET form parameters (orderings, filters, `search`, `labels`, `tag.<key>`, window) and return one page of at most `enumeration.maxResultsAdmin` rows (also the default page size) as `text/csv; charset=utf-8` with a header row and `Content-Disposition: attachment`. When more rows remain, the `X-Continuation-Token` response header carries the token for the next page (send it back as `continuationToken` with the same filters); `X-Total-Records` has the total. Tokens from the JSON list do not work for the CSV route and vice versa (`400 InvalidContinuationToken`). CSV values that a spreadsheet would read as formulas are prefixed with an apostrophe.

```bash
curl -i -H "Authorization: Bearer $TK_ADMIN_TOKEN" \
  "http://127.0.0.1:8000/api/v1/admin/signup-codes/export.csv?status=Active&maxResults=500"
# HTTP/1.1 200 OK
# Content-Type: text/csv; charset=utf-8
# X-Continuation-Token: eyJ2IjoxLC...
# X-Total-Records: 1342
```

`SignupCode`:

```json
{
  "id": "suc_...", "code": "SEMINAR2026", "name": "Spring seminar 2026", "description": "...", "notes": "...",
  "grantType": "DurationMonths", "grantMonths": 12, "grantEndUtc": null, "grantSummary": "Free membership for 12 months",
  "maxRedemptions": 50, "redemptionCount": 37, "startsUtc": null, "expiresUtc": "2026-12-31T23:59:59Z",
  "isActive": true, "status": "Active", "batchId": null,
  "joinUrl": "http://127.0.0.1:8080/join?code=SEMINAR2026",
  "lastRedeemedUtc": "2026-11-10T18:00:00Z", "createdByUserId": "usr_...", "createdUtc": "...", "updatedUtc": "...",
  "labels": ["seminar"], "tags": { "cohort": "spring", "staff:owner": "events team" }
}
```

The Phase 1 `tags` string list on codes became `labels`; `tags` is now the key and value dictionary. Codes are 6 to 40 of `A-Z 0-9 -` (stored upper case); generated codes use an alphabet without `0 O 1 I L` and `signupCodes.generatedLength` characters (default 10). `status` is derived in this order: `Inactive`, `Scheduled` (before `startsUtc`), `Expired` (after `expiresUtc`), `Exhausted` (`redemptionCount >= maxRedemptions`), otherwise `Active`.

## Admin: audit

`GET /api/v1/admin/audit` and `POST /api/v1/admin/audit/enumerate` return audit records `{ id, actorUserId, actorName, action, targetType, targetId, summary, sourceIp, createdUtc, labels, tags }`.

| | |
|---|---|
| Orderings | `CreatedDescending` (default), `CreatedAscending` |
| Filters | `actorUserId`, `targetType`, `targetId`, `action` (exact); the time window is `createdAfterUtc` and `createdBeforeUtc` (formerly `fromUtc` and `toUtc`) |
| Search | action, actor name, summary |
| Labels and tags | `AuditRecord` |

Actions: `member.flags`, `member.roles`, `member.comp`, `member.ban`, `member.delete`, `member.mute`, `member.sessions.revoke`, `signupCode.create`, `signupCode.update`, `signupCode.deactivate`, `signupCode.batch`, `signupCode.redeem`, `signupCode.revoke`, `settings.update`, `server.restart`, `account.delete`, `account.close.underage`, `account.password.change`, `account.password.reset`, `account.identity.link`, `account.identity.unlink`, `moderation.wordFilter.create`, `moderation.wordFilter.update`, `moderation.wordFilter.delete`, `moderation.wordFilter.import`. The admin token appears as `actorName: "admin token"`, the system as `"system"`.

```bash
curl -H "Authorization: Bearer $TK_ADMIN_TOKEN" \
  "http://127.0.0.1:8000/api/v1/admin/audit?action=member.ban&createdAfterUtc=2026-11-01T00:00:00Z&maxResults=100"
```

---

## Media (Member)

Media bytes never pass through the API process in production: clients create an upload session, send the bytes straight to storage, call `complete`, then poll until the media is `Ready`. Local mode (`media.storageMode = Local`, the default) runs the identical client flow against the backend itself. Design record: `docs/PHASE2_CONTRACT.md` section 4.

| Method and path | Group | Body | Success | Errors |
|---|---|---|---|---|
| `POST /api/v1/media/uploads` | Member | `{ kind, contentType, sizeBytes, fileName?, durationSeconds? (video, required), labels?, tags? }` | `201 UploadSession` | `400 ValidationFailed`, `400 VideoTooLong`, `403 Muted`, `403 PostingNotAllowed`, `403 MediaNotAllowed`, `403 VideoUploadNotAllowed`, `413 TooLarge`, `415 UnsupportedMediaType`, `429 RateLimited` (`media.uploadsPerHour` or the daily byte quota), `503 IntegrationNotConfigured` |
| `POST /api/v1/media/{id}/upload-urls` | Member (owner, status `Uploading`) | none | `200 { upload }` with fresh URLs and `uploadedParts` | `404 NotFound`, `409 UploadIncomplete` (no longer Uploading), `410 UploadExpired` |
| `POST /api/v1/media/{id}/complete` | Member (owner) | `{ parts: [{ partNumber, etag }] }` (multipart only; omit for single) | `202 Media` (now `Processing`); `200 Media` unchanged when already Processing, Ready, or Failed | `400 ValidationFailed`, `400 VideoTooLong` (media becomes Failed), `404 NotFound`, `409 UploadIncomplete` (missing parts or size mismatch; stays Uploading), `410 UploadExpired`, `415 UnsupportedMediaType` (sniff mismatch; media becomes Failed) |
| `GET /api/v1/media/{id}` | Member (owner, `SystemAdmin`, or a member who can see the content it is attached to) | | `200 Media` | `404 NotFound`, `429 RateLimited` |
| `GET /api/v1/media?ids=med_a,med_b` | Member (same rule per item) | | `200 EnumerationResult<MediaView>`; at most 20 requested ids; ids the caller cannot see are omitted. POST `/api/v1/media/enumerate` uses `filters.ids` | `400 ValidationFailed`, `429 RateLimited` |
| `PUT /api/v1/media/{id}` | Member (owner or `SystemAdmin`) | `{ altText?, labels?, tags? }`; allowed in any status and after attachment | `200 Media` | `400 ValidationFailed`, `400 ContentFiltered`, `403 Muted`, `404 NotFound` |
| `DELETE /api/v1/media/{id}` | Member (owner of unattached media, or `SystemAdmin`, which detaches it first) | | `204`; aborts a multipart upload and deletes every stored object including the original | `403 Forbidden` (attached; edit the post or comment instead), `404 NotFound` |
| `POST /api/v1/media/grant` | Member | none | `200 MediaGrant` plus `Set-Cookie` headers and `Cache-Control: no-store` | `429 RateLimited` (`rateLimits.mediaGrantsPerHour`), `503 IntegrationNotConfigured` |
| `PUT /api/v1/media/local-uploads/{id}?exp=&sig=` | Anonymous (signature) | raw bytes with the declared `Content-Type` | `200`, `ETag: "<md5 hex>"` | `403 InvalidSignature`, `404 NotFound`, `409 UploadIncomplete` (session closed), `410 UploadExpired`, `411 LengthRequired`, `413 TooLarge`, `415 UnsupportedMediaType` |
| `PUT /api/v1/media/local-uploads/{id}/parts/{partNumber}?exp=&sig=` | Anonymous (signature) | raw bytes of one part | `200`, `ETag` | `403 InvalidSignature`, `404 NotFound`, `410 UploadExpired`, `411 LengthRequired`, `413 TooLarge` |
| `GET /api/v1/media/files/{id}/{file}` | Anonymous (grant cookie or bearer) | | `200` or `206` bytes | `403 InvalidSignature` (no valid grant), `404 NotFound`, `416 RangeNotSatisfiable` |
| `GET /api/v1/media/files/public/{id}/{file}` | Anonymous | | `200` or `206` bytes of media on Public posts | `404 NotFound`, `416 RangeNotSatisfiable` |
| `GET /api/v1/media/files/link-previews/{hash}/{file}` | Anonymous | | `200 image/jpeg` (re-encoded link preview card) | `404 NotFound` |

The local upload and file routes exist in Local mode only; production serves files from the media CDN.

### Limits and accepted types

| | Images | Member video | Admin video (`SystemAdmin`) |
|---|---|---|---|
| Content types | `image/jpeg`, `image/png`, `image/webp` | `video/mp4`, `video/quicktime` | same |
| Max size | `media.maxImageBytes` (20 MB) | `media.maxMemberVideoMegabytes` (2048) | `media.maxAdminVideoMegabytes` (0 = uncapped) |
| Max pixels or duration | `media.maxImagePixels` (40 megapixels) | `media.maxMemberVideoSeconds` (300) | `media.maxAdminVideoSeconds` (0 = uncapped) |
| Upload mode | Single PUT | Single PUT up to `media.multipartThresholdMegabytes` (32), multipart above | same |
| Daily quota | `media.memberDailyUploadMegabytes` (5120) per member per UTC day, shared | | none |

HEIC, HEIF, and animated GIFs are not accepted (mobile converts HEIC to JPEG first). `fileName` (up to 255 characters) is stored for the admin library only and never served.

### Create an upload session

```bash
curl -X POST -H "Authorization: Bearer $ACCESS_TOKEN" -H "Content-Type: application/json" \
  -d '{"kind":"Image","contentType":"image/jpeg","sizeBytes":4210331,"fileName":"IMG_0001.jpg","labels":["fundamentals"]}' \
  http://127.0.0.1:8000/api/v1/media/uploads
```

`201 UploadSession` (Local mode, single PUT):

```json
{
  "media": { "id": "med_mwd2a1c3_7Hk2...", "kind": "Image", "status": "Uploading", "...": "Media" },
  "upload": {
    "mode": "Single",
    "expiresUtc": "2026-11-12T19:00:00Z",
    "single": { "method": "PUT", "url": "http://127.0.0.1:8000/api/v1/media/local-uploads/med_mwd2a1c3_7Hk2...?exp=1762974000&sig=4b1f...", "headers": { "Content-Type": "image/jpeg" } },
    "multipart": null,
    "uploadedParts": []
  }
}
```

For `mode: "Multipart"`, `single` is null and `multipart` is `{ partSizeBytes, partCount, parts: [{ partNumber, method: "PUT", url }] }`. In S3 mode the URLs are presigned S3 URLs and the object keys start with the property id (`example/in/med_.../original` in the source or video source bucket).

Rules:
- Send exactly the `headers` listed with the single PUT and nothing else (no `Authorization`). Part PUTs send no extra headers.
- Part size is `max(media.multipartPartMegabytes, ceil(sizeBytes / 1000))` rounded up to a whole MiB, so a session never exceeds 1000 parts. Every part except the last is exactly `partSizeBytes`.
- Every PUT response carries an `ETag` header. Record it verbatim (including quotes) for `complete`.
- Interrupted? Call `upload-urls`: `uploadedParts: [{ partNumber, etag, sizeBytes }]` lists what storage already has, so only the missing parts are sent.
- Local upload URLs are signed with HMAC-SHA256 over `PUT\n<path>\n<exp>`; the body must have a `Content-Length` (chunked bodies are `411`).

### Complete

```json
{ "parts": [ { "partNumber": 1, "etag": "\"9b2cf535f27731c974343645a3985328\"" } ] }
```

The server completes the multipart upload, confirms the size equals `sizeBytes`, sniffs the format (magic bytes), reads the video duration from the container, moves the media to `Processing`, and signals the worker. Poll `GET /api/v1/media/{id}` (or the batch form) until `Ready` or `Failed`: images every 2 seconds for up to 60 seconds, video every 5 seconds while the composer is open.

### `Media`

```json
{
  "id": "med_...", "kind": "Image", "status": "Ready", "failureReason": null, "ownerUserId": "usr_...",
  "contentType": "image/jpeg", "sizeBytes": 4210331, "width": 4032, "height": 3024, "durationSeconds": null,
  "altText": "A member demonstrating a technique", "blurhash": "LKO2?U%2Tw=w]~RBVZRi};RPxuwH",
  "image": {
    "variants": {
      "thumb":  { "width": 320,  "height": 240,  "webpUrl": "https://media.example.test/example/media/med_.../thumb.webp",  "jpegUrl": "https://media.example.test/example/media/med_.../thumb.jpg" },
      "medium": { "width": 1080, "height": 810,  "webpUrl": ".../medium.webp", "jpegUrl": ".../medium.jpg" },
      "large":  { "width": 2048, "height": 1536, "webpUrl": ".../large.webp",  "jpegUrl": ".../large.jpg" }
    }
  },
  "video": null,
  "createdUtc": "2026-11-12T18:00:00Z", "readyUtc": "2026-11-12T18:00:04Z",
  "labels": ["fundamentals"], "tags": { "level": "beginner" }
}
```

| Field | Rule |
|---|---|
| `kind` | `Image` or `Video` |
| `status` | `Uploading`, `Processing`, `Ready`, `Failed` |
| `ownerUserId` | The uploading member. Omitted on anonymous callers and on every public route (`/public/*` feed posts, threads, lessons, events, venues, and instructors), which never name the uploader; `failureReason` is null there |
| `failureReason` | Only when Failed: `UnsupportedFormat`, `Corrupt`, `TooLarge`, `VideoTooLong`, `ImageTooLarge`, `ProcessingError`, `UploadExpired` |
| `width`, `height`, `blurhash` | Null until Ready; dimensions after EXIF orientation |
| `durationSeconds` | Videos only |
| `image.variants` | `thumb` (long edge 320), `medium` (1080), `large` (2048), never upscaled, each in WebP and JPEG; null until Ready |
| `video` | `{ hlsUrl, mp4Url, posterUrl, posterThumbUrl }` when Ready. `mp4Url` only in local `Passthrough` mode; play `hlsUrl` when present, else `mp4Url` |
| URLs | With `media.publicBaseUrl` set (production) they are absolute CDN URLs whose path starts with the property id: `/{propertyId}/media/{id}/` for images and posters, `/{propertyId}/video/{id}/` for HLS, `/{propertyId}/public/media/{id}/` and `/{propertyId}/public/video/{id}/` for Public posts, `/{propertyId}/link-previews/{hash}/` for link preview images. With it empty (local) they are root-relative, `/api/v1/media/files/med_.../thumb.webp` (or `/api/v1/media/files/public/...`, `/api/v1/media/files/link-previews/...`), resolved against the API origin like `avatarUrl` |

URLs are never signed per request; access is controlled by the media grant.

### Media grant and file delivery

`POST /api/v1/media/grant` (no body):

```json
{
  "mode": "Local",
  "expiresUtc": "2026-11-12T04:00:00Z",
  "cookies": [ { "name": "tk_media", "value": "opaque", "domain": null, "path": "/api/v1/media/files", "expiresUtc": "2026-11-12T04:00:00Z", "secure": false } ]
}
```

| Mode | When | Cookies |
|---|---|---|
| `CloudFront` | `media.storageMode = S3` | `CloudFront-Policy`, `CloudFront-Signature`, `CloudFront-Key-Pair-Id` for the media host, `Domain` from `media.cookieDomain` (for example `example.test`) |
| `Local` | `media.storageMode = Local` | `tk_media` (AES-256-GCM protected `{ userId, expiresUtc }`), `Path=/api/v1/media/files`, no `Domain`, `Secure` except on plain HTTP to a loopback host |

The response sets each cookie with `Set-Cookie: <name>=<value>; Path=...; Expires=...; Max-Age=...; HttpOnly; SameSite=Lax` (plus `Secure` and `Domain` when they apply). Web clients rely on the cookie (hls.js needs `xhr.withCredentials = true`); mobile clients keep `cookies` in memory and send them as a plain `Cookie: name=value; name=value` header on every media request. Refresh the grant when `expiresUtc` is less than 10 minutes away, and once on a 403. `media.grantMinutes` (default 360) bounds access after a membership lapses.

`GET /api/v1/media/files/{id}/{file}` (Local mode) accepts the `tk_media` cookie (browser cookie or a plain `Cookie` header) or an `Authorization: Bearer` access token whose session has a membership. It looks in the images folder, then the transcoded folder, and answers with `Accept-Ranges: bytes`, `Cache-Control: private, max-age=31536000, immutable`, `X-Content-Type-Options: nosniff`, and the right `Content-Type` (`application/vnd.apple.mpegurl` for playlists, `video/mp2t` for segments). A `Range: bytes=start-end` (or `bytes=-suffix`) header returns `206` with `Content-Range`; an unsatisfiable range returns `416` with `Content-Range: bytes */<length>`. File names are flat (for example `master.m3u8`, `hls_720p_00001.ts`, `poster.jpg`).

```bash
curl -H "Cookie: tk_media=$TK_MEDIA" -H "Range: bytes=0-1023" -o part.bin \
  http://127.0.0.1:8000/api/v1/media/files/med_.../hls_720p_00001.ts
```

## Admin: media library (Admin)

| Method and path | Notes |
|---|---|
| `GET /api/v1/admin/media`, `POST /api/v1/admin/media/enumerate` | Media library collection (below) |
| `GET /api/v1/admin/media/summary` | `{ totalCount, totalStoredBytes, byKind: { Image, Video }, byStatus: { Uploading, Processing, Ready, Failed }, failed24h }` |
| `GET /api/v1/admin/media/{id}` | Item fields plus `processor`, `processorJobId`, `processingStartedUtc`, `attempts`, `variants: [{ name, format, width, height, sizeBytes }]`. `404 NotFound` |
| `PUT /api/v1/admin/media/{id}` | `{ altText?, labels?, tags? }` (staff metadata allowed) returns the detail. `400 ValidationFailed`, `404 NotFound` |
| `POST /api/v1/admin/media/{id}/reprocess` | Failed items whose original still exists go back to `Processing`; returns the detail. `404 NotFound`, `409 NotReprocessable` |
| `DELETE /api/v1/admin/media/{id}` | Detaches it from its target (the content keeps its other attachments) and deletes every object; `204`. `404 NotFound` |

**Media library collection.** Items `{ media: Media, owner: UserSummary, attachedTo: { targetType, targetId } | null, storedBytes, fileName, createdUtc }` (labels and tags are on `media`).

| | |
|---|---|
| Orderings | `CreatedDescending` (default), `CreatedAscending`, `SizeDescending`, `SizeAscending` |
| Filters | `kind` (`Image`, `Video`), `status` (`Uploading`, `Processing`, `Ready`, `Failed`), `ownerUserId`, `attached` (`true` or `false`) |
| Search | original file name, alt text |
| Labels and tags | `Media` |

```bash
curl -H "Authorization: Bearer $TK_ADMIN_TOKEN" \
  "http://127.0.0.1:8000/api/v1/admin/media?kind=Video&status=Failed&ordering=SizeDescending&maxResults=100"
```

## Blocks (Member and Signed in)

| Method and path | Group | Success | Errors |
|---|---|---|---|
| `POST /api/v1/users/{id}/block` | Member | `200 { "blocked": true }` (idempotent) | `400 CannotBlockSelf`, `400 CannotBlockStaff`, `404 NotFound`, `409 BlockLimitReached` (more than 1000), `429 RateLimited` (`moderation.blocksPerHour`, shared with unblock) |
| `DELETE /api/v1/users/{id}/block` | Member | `200 { "blocked": false }` (idempotent) | `429 RateLimited` |
| `GET /api/v1/me/blocks`, `POST /api/v1/me/blocks/enumerate` | Signed in | `200 EnumerationResult<{ user: UserSummary, createdUtc, labels, tags }>` | `400 ValidationFailed`, `400 InvalidContinuationToken` |

Blocks are private (the blocked member is never told). Muted and restricted members may block. `GET /api/v1/users/{id}` carries `isBlockedByMe`. Blocks collection: orderings `CreatedDescending` (default, by block time) and `CreatedAscending`; no filters; search over the blocked member's display name and handle; label and tag filters apply to the blocked member's `User` metadata.

```json
{ "success": true, "maxResults": 25, "skip": 0, "continuationToken": null, "endOfResults": true, "totalRecords": 1, "recordsRemaining": 0, "timestampUtc": "2026-11-12T18:00:00Z",
  "objects": [ { "user": { "id": "usr_...", "displayName": "Alex Example", "handle": "alex", "avatarUrl": null, "rank": "Beginner", "rankLevel": 0, "isStaff": false, "isDeleted": false }, "createdUtc": "2026-11-12T17:00:00Z", "labels": [], "tags": {} } ] }
```

## Admin: word filter (Admin)

The word filter screens member text on write (media alt text, member labels and tag values, feed post titles and bodies, comment bodies, and display names and bios at registration, on profile edits, and on staff edits of a member's profile). A `Block` term is `400 ContentFiltered` with `fieldErrors` naming the field (`displayName`, `bio`, `body`, ...); a `Flag` term saves and files one system report (reason `Filter`, no reporter) on the target (the member for a profile). When that report is new, the SystemAdmins (at most `contentReview.maxNotifiedAdmins`) get an in-app and push notification (type `ContentFlagged`, the matched terms in the title, the start of the flagged text as the body, `path` `/admin/moderation?target=<TargetType>%3A<id>`, which opens the item in the moderation queue) and the `staff_new_report` email with the same link. The list starts empty; `POST .../starter` loads the engine's starter list. `moderation.wordFilterEnabled` switches screening off.

| Method and path | Body | Success | Errors |
|---|---|---|---|
| `GET /api/v1/admin/moderation/word-filter`, `POST /api/v1/admin/moderation/word-filter/enumerate` | `EnumerationQuery` | `200 EnumerationResult<WordFilterTerm>` | `400 ValidationFailed`, `400 InvalidContinuationToken` |
| `POST /api/v1/admin/moderation/word-filter` | `{ term, matchType?: "Word" \| "Substring" (default Word), action?: "Block" \| "Flag" (default Block), note?, labels?, tags? }` | `201 WordFilterTerm` | `400 ValidationFailed`, `409 Conflict` (same normalized term) |
| `PUT /api/v1/admin/moderation/word-filter/{id}` | any field; absent fields unchanged | `200 WordFilterTerm` | `400 ValidationFailed`, `404 NotFound`, `409 Conflict` |
| `DELETE /api/v1/admin/moderation/word-filter/{id}` | | `204` | `404 NotFound` |
| `POST /api/v1/admin/moderation/word-filter/import` | `{ terms: ["..."] (up to 1000), matchType?, action? }` | `200 { added, skipped }` (duplicates and invalid entries skipped) | `400 ValidationFailed` |
| `POST /api/v1/admin/moderation/word-filter/starter` | none | `200 { added, skipped }`: the engine's starter list of common English profanity, sexual vulgarity and slurs, added with action `Flag` (whole-word terms with common inflections, plus a few `Substring` roots that do not occur inside ordinary words); terms already on the list are skipped | none beyond authentication |
| `POST /api/v1/admin/moderation/word-filter/test` | `{ text }` | `200 { matches: [{ termId, term, action, start, length }] }` (`start` and `length` are UTF-16 positions in the original text) | `400 ValidationFailed` |

`WordFilterTerm`: `{ id, term, matchType, action, note, createdByName, createdUtc, updatedUtc, hitCount30d, labels, tags }` (`hitCount30d` counts matches in the last 30 days).

| | |
|---|---|
| Orderings | `CreatedDescending` (default), `CreatedAscending`, `TermAscending`, `UpdatedDescending`, `HitsDescending` |
| Filters | `action` (`Block`, `Flag`), `matchType` (`Word`, `Substring`) |
| Search | term, note |
| Labels and tags | `WordFilterTerm` |

Matching compares text and terms after Unicode NFKC, case folding, removal of zero-width characters and diacritics, and a small substitution map (`0`=o, `1`=i, `3`=e, `4`=a, `5`=s, `7`=t, `@`=a, `$`=s), with runs of the same letter longer than two collapsed. `Word` terms match whole words; `Substring` terms match anywhere. `Block` rejects the write with `400 ContentFiltered` (the term is never echoed); `Flag` lets it through and files a system report. `SystemAdmin` authors are not filtered. Every list change is audited.

## Member search (Member)

Mention autocomplete (and later the directory search).

| Method and path | Notes |
|---|---|
| `GET /api/v1/members?search=al` (also `q=al`) | GET form |
| `GET /api/v1/members/search?q=al` | Alias of `GET /api/v1/members` |
| `POST /api/v1/members/enumerate` | `{ search, maxResults?, filters: { contextType?, contextId? } }` |

- `search` (alias `q` in the GET form): 1 to 30 characters after an optional leading `@`, matched case-insensitively as a prefix of the handle or of any display name word.
- Default `Relevance` ranking: exact handle, then the context post's author and commenters (`contextType=FeedPost&contextId=fpo_...`; each requires the other, and `FeedPost` is the only accepted `contextType`: any other value is `400 ValidationFailed` under `filters.contextType`), then handle prefix, then display name.
- Standard SQL keyset pages: `maxResults` defaults to 8 and is capped at 20; `skip` and continuation tokens are supported. Orderings are `Relevance` (default), `CreatedAscending`, and `CreatedDescending`. Tokens bind property, normalized prefix, caller and context. Totals are exact for staff and follow `enumeration.countMemberTotals` for members. `objects` are `UserSummary` values with visible labels/tags; staff metadata stays hidden from ordinary members.
- Excludes the caller, deleted, banned, and age-unattested accounts, members without an active membership, and accounts on either side of a block with the caller.
- Errors: `400 ValidationFailed` (`fieldErrors.search`, `filters.contextType`, `filters.contextId`), `429 RateLimited` (`rateLimits.memberSearchPerMinute`).

```bash
curl -H "Authorization: Bearer $ACCESS_TOKEN" "http://127.0.0.1:8000/api/v1/members?q=%40al&maxResults=8"
```

```json
{ "success": true, "maxResults": 8, "skip": 0, "continuationToken": null, "endOfResults": true, "totalRecords": 1, "recordsRemaining": 0, "timestampUtc": "2026-11-12T18:00:00Z",
  "objects": [ { "id": "usr_...", "displayName": "Alex Example", "handle": "alex", "avatarUrl": "/api/v1/media/avatars/usr_..._1731350000000.jpg", "rank": "Intermediate", "rankLevel": 1, "isStaff": false, "isDeleted": false } ] }
```

## Link previews (Member)

`POST /api/v1/link-previews` with `{ "url": "https://example.test/about" }` returns `200 LinkPreview` (from cache when fresh):

```json
{
  "url": "https://example.test/about",
  "canonicalUrl": "https://example.test/about",
  "siteName": "Example Community",
  "title": "About us",
  "description": "Who we are and how the community works.",
  "imageUrl": "/api/v1/media/files/link-previews/3f9a.../card.jpg",
  "imageWidth": 1200,
  "imageHeight": 630,
  "fetchedUtc": "2026-11-12T18:00:00Z"
}
```

`imageUrl` is the re-encoded copy (never the third-party image): root-relative in Local mode, `https://media.example.test/example/link-previews/3f9a.../card.jpg` in production. Errors: `400 ValidationFailed`, `400 LinkPreviewUnavailable` (not allowed or could not be fetched; failures are cached for `linkPreviews.failureCacheMinutes`), `429 RateLimited` (`linkPreviews.perHour` per member, `linkPreviews.perHostPerMinute` per host). The fetch is server side with SSRF protection (http and https on ports 80 and 443 only, every resolved address and every redirect hop checked, connection pinned to the checked address), `linkPreviews.timeoutSeconds`, `linkPreviews.maxHtmlBytes`, `linkPreviews.maxRedirects`, and `linkPreviews.blockedDomains`. Successful previews are cached for `linkPreviews.cacheHours`. `linkPreviews.enabled = false` turns the feature off.

## Notifications (Member)

| Method and path | Body | Success | Errors |
|---|---|---|---|
| `GET /api/v1/notifications`, `POST /api/v1/notifications/enumerate` | `EnumerationQuery` | `200 EnumerationResult<Notification>` plus `unreadCount` | `400 ValidationFailed`, `400 InvalidContinuationToken`, `429 RateLimited` (`rateLimits.memberReadsPerMinute`) |
| `GET /api/v1/notifications/unread-count` | | `200 { "unreadCount": 7, "latestUpdatedUtc": "2026-11-12T18:20:00Z" }` | `429 RateLimited` (`rateLimits.unreadCountPerMinute`) |
| `POST /api/v1/notifications/read` | `{ "ids": ["ntf_..."] }` (1 to 100) | `200 { unreadCount }`; unknown or foreign ids are ignored | `400 ValidationFailed`, `429 RateLimited` (`rateLimits.notificationWritesPerMinute`) |
| `POST /api/v1/notifications/unread` | `{ "ids": [...] }` (1 to 100) | `200 { unreadCount }` | `400 ValidationFailed`, `429 RateLimited` |
| `POST /api/v1/notifications/read-all` | `{ "beforeUtc"?: "..." }` (default now; marks notifications last updated at or before it) | `200 { unreadCount }` | `429 RateLimited` |

**Notifications collection** (the caller's own):

| | |
|---|---|
| Orderings | `UpdatedDescending` (default), `CreatedDescending`, `CreatedAscending` |
| Filters | `unreadOnly` (`true` or `false`), `type` (a notification type), `category` (`Replies`, `Mentions`, `Reactions`, `FeedPosts`, `Moderation`, `Account`) |
| Search | title, body |
| Labels and tags | `Notification` |

```bash
curl -H "Authorization: Bearer $ACCESS_TOKEN" "http://127.0.0.1:8000/api/v1/notifications?unreadOnly=true&maxResults=30"
```

```json
{
  "success": true, "maxResults": 30, "skip": 0, "continuationToken": null, "endOfResults": true, "totalRecords": 1, "recordsRemaining": 0, "timestampUtc": "2026-11-12T18:21:00Z",
  "unreadCount": 1,
  "objects": [
    {
      "id": "ntf_...", "type": "MembershipExpiring", "category": "Account",
      "title": "Your free membership ends on March 1, 2027", "body": null,
      "actor": null, "actors": [], "actorCount": 0,
      "target": { "type": "User", "id": "usr_..." },
      "context": {}, "path": "/account/membership",
      "isRead": false, "readUtc": null, "createdUtc": "2026-11-12T18:20:00Z", "updatedUtc": "2026-11-12T18:20:00Z",
      "labels": [], "tags": {}
    }
  ]
}
```

`title` is a server-rendered sentence; `actors` holds up to the 3 most recent actors with `actorCount` the total (aggregated types); `context` holds deep-link ids; `path` is app-relative (the website prefixes `/app`). Clients poll `unread-count` every 60 seconds while visible (no WebSocket in Phase 2) and show counts above 99 as "99+". Read notifications are deleted after `notifications.retentionDays` (180), unread ones after twice that. Types and triggers: `docs/PHASE2_CONTRACT.md` section 8.2; clients render unknown types as a generic row.

### Notification preferences (Signed in)

`GET /api/v1/me/notification-preferences` and `POST /api/v1/me/notification-preferences/enumerate` take `EnumerationQuery` and return `EnumerationResult<CategoryPreferenceView>`. Category rows are in `objects`; scalar preference context remains alongside the page. Example first page:

```json
{
  "pushPaused": false,
  "success": true, "maxResults": 25, "skip": 0, "continuationToken": null, "endOfResults": true, "totalRecords": 8, "recordsRemaining": 0, "timestampUtc": "2026-11-23T00:00:00Z",
  "objects": [
    { "category": "Replies",    "inApp": true, "push": true, "email": false, "editable": { "inApp": true,  "push": true, "email": false } },
    { "category": "Mentions",   "inApp": true, "push": true, "email": false, "editable": { "inApp": true,  "push": true, "email": false } },
    { "category": "Reactions",  "inApp": true, "push": true, "email": false, "editable": { "inApp": true,  "push": true, "email": false } },
    { "category": "FeedPosts",  "inApp": true, "push": true, "email": true,  "editable": { "inApp": true,  "push": true, "email": true } },
    { "category": "Moderation", "inApp": true, "push": true, "email": false, "editable": { "inApp": false, "push": true, "email": false } },
    { "category": "Account",    "inApp": true, "push": true, "email": true,  "editable": { "inApp": false, "push": true, "email": false } },
    { "category": "Following",  "inApp": true, "push": true, "email": false, "editable": { "inApp": true,  "push": true, "email": true } },
    { "category": "Forums",     "inApp": true, "push": true, "email": false, "editable": { "inApp": true,  "push": true, "email": true } }
  ],
  "notificationHours": { "enabled": true, "start": "07:00", "end": "22:00", "timeZone": "America/Los_Angeles" }
}
```

`PUT /api/v1/me/notification-preferences` with any subset returns `200` with the full document:

```json
{ "categories": [ { "category": "Reactions", "push": false } ], "notificationHours": { "start": "08:00", "timeZone": "America/Los_Angeles" }, "pushPaused": false }
```

`Following` and `Forums` email is opt-in (off by default) and arrives as a digest, not per event: every `forums.emailDigestIntervalHours` (168, weekly) a member with the category's email on and a verified address gets one email per category listing their unread notifications of that category updated since the previous digest (at most `forums.emailDigestMaxItems`, 10, the rest counted; nothing is sent when there are none). The first digest arrives one interval after the member turns email on. Digests use the administrative email system (template `forum_digest`, `Optional`: the property's brand and template override, the queue with retries, and suppressions) and carry one-click unsubscribe for their category.

Changing a non-editable cell, an unknown category, a malformed `HH:mm`, or a time zone that is not an IANA id is `400 ValidationFailed` (for example `fieldErrors["categories[0].inApp"]`). Turning `inApp` off also stops push for that category; `pushPaused` stops every push. Outside the notification hours pushes are suppressed, not delayed. Both routes are rate limited (`429 RateLimited`).

### Push tokens (Signed in)

| Method and path | Body | Success | Errors |
|---|---|---|---|
| `POST /api/v1/me/push-tokens` | `{ "token": "ExponentPushToken[xxxx]", "platform": "Ios" \| "Android", "deviceId"?, "deviceName"?, "appVersion"? }` | `201 PushToken` when new; `200 PushToken` when the token exists (updates `lastSeenUtc` and moves it to the caller if another account had it; `deviceId`, `deviceName`, and `appVersion` the request omits keep their stored values) | `400 ValidationFailed` (token must match `^Expo(nent)?PushToken\[[A-Za-z0-9_-]+\]$`), `429 RateLimited` (`push.registrationsPerHour`) |
| `DELETE /api/v1/me/push-tokens` | `{ "token": "..." }` | `204` (also when unknown) | `429 RateLimited` |
| `GET /api/v1/me/push-tokens`, `POST /api/v1/me/push-tokens/enumerate` | `EnumerationQuery` | `200 EnumerationResult<PushToken>` | `400 ValidationFailed`, `400 InvalidContinuationToken` |

`PushToken`: `{ id, platform, deviceName, appVersion, createdUtc, lastSeenUtc, labels, tags }` (token values are never returned). Collection orderings: `LastSeenDescending` (default), `CreatedDescending`, `CreatedAscending`; no filters; search over device name; `PushToken` labels and tags. Each token is bound to the registering session; revoking that session (sign-out, password change, admin revoke, ban, deletion) deletes its tokens. At most 10 tokens per member; the eleventh deletes the least recently seen.

### Email unsubscribe (Anonymous)

Feed post emails and the `Following` and `Forums` digests carry `List-Unsubscribe` and `List-Unsubscribe-Post: List-Unsubscribe=One-Click` (RFC 8058). The token is an HMAC over `{ userId, category }` and needs no session.

| Method and path | Success | Errors |
|---|---|---|
| `POST /api/v1/email/unsubscribe?token=` | `200 { "unsubscribed": true, "category": "FeedPosts" }` (or `Following`, `Forums`); turns that category's email off | `400 InvalidToken`, `429 RateLimited` (`rateLimits.unsubscribePerHour` per IP) |
| `GET /api/v1/email/unsubscribe?token=` | `200 text/html` confirmation page (the link in the email); same effect | `400 text/html` "This unsubscribe link is not valid" page, `429 RateLimited` |

## Feed posts (Member)

Design record: `docs/PHASE2_CONTRACT.md` sections 2, 3, and 5, with every list converted to `EnumerationQuery` and `EnumerationResult<T>` and every object carrying `labels` and `tags`. Bodies are the Markdown subset of contract section 3.1; the server normalizes them (NFC, newlines, control and bidirectional characters removed, trimmed, three or more newlines collapsed) and counts lengths in Unicode scalar values. Mentions (`@handle`) are resolved on save and notify on publish.

| Method and path | Group | Body | Success | Errors |
|---|---|---|---|---|
| `GET /api/v1/feed`, `POST /api/v1/feed/enumerate` | Member | `EnumerationQuery` | `200 EnumerationResult<FeedPost>` | `400 ValidationFailed`, `400 InvalidContinuationToken`, `429 RateLimited` (`rateLimits.memberReadsPerMinute`) |
| `POST /api/v1/feed` | Member (`SystemAdmin`, or `flags.canPostToFeed` and `flags.canPost`) | `FeedPostRequest` | `201 FeedPost` (state `Scheduled`, `Processing`, or `Published`) | `400 ValidationFailed`, `400 MediaLimitExceeded`, `400 ContentFiltered`, `403 Forbidden` (no `canPostToFeed`, `Public` without `SystemAdmin`, or the admin token), `403 PostingNotAllowed`, `403 MediaNotAllowed`, `403 VideoUploadNotAllowed`, `403 Muted`, `409 MediaNotAttachable`, `409 PinLimitReached`, `429 RateLimited` (`feed.postsPerHour`) |
| `GET /api/v1/feed/{id}` | Member | | `200 FeedPost` | `404 NotFound`, `429 RateLimited` |
| `PUT /api/v1/feed/{id}` | Member (the author with `canPost`, in any state; or `SystemAdmin`) | `FeedPostRequest`; omitted fields are unchanged | `200 FeedPost` | `400 ValidationFailed` (including `fieldErrors.publishAtUtc` when the post is not Scheduled), `400 MediaLimitExceeded`, `400 ContentFiltered`, `403 Forbidden` (you can see the post but it is not yours), `403 PostingNotAllowed`, `403 MediaNotAllowed`, `403 VideoUploadNotAllowed`, `403 Muted`, `404 NotFound`, `409 MediaNotAttachable`, `429 RateLimited` (`rateLimits.editsPerHour`) |
| `DELETE /api/v1/feed/{id}` | Member (author or `SystemAdmin`) | | `204` | `403 Forbidden`, `404 NotFound` |
| `POST /api/v1/feed/{id}/pin` | Member (`SystemAdmin` only) | none | `200 FeedPost` (idempotent) | `403 Forbidden`, `404 NotFound`, `409 NotPublished`, `409 PinLimitReached` |
| `POST /api/v1/feed/{id}/unpin` | Member (`SystemAdmin` only) | none | `200 FeedPost` (idempotent) | `403 Forbidden`, `404 NotFound` |
| `GET /api/v1/feed/{id}/revisions`, `POST /api/v1/feed/{id}/revisions/enumerate` | Member (anyone who can see the post) | `EnumerationQuery` | `200 EnumerationResult<FeedPostRevision>` | `400 ValidationFailed`, `400 InvalidContinuationToken`, `404 NotFound`, `429 RateLimited` |
| `GET /api/v1/me/feed-posts`, `POST /api/v1/me/feed-posts/enumerate` | Signed in | `EnumerationQuery` | `200 EnumerationResult<FeedPost>` | `400 ValidationFailed`, `400 InvalidContinuationToken`, `403 Forbidden` (admin token), `429 RateLimited` |

Who sees which states: the author sees their posts in every state, `SystemAdmin` sees every state, and `FeedModerator` holders also see `Hidden` and `Removed` posts. Everyone else gets `404` for anything not `Published`, and deleted posts are `404` for everyone on member routes.

Audience (`docs/TRELLISKIT.md` section 7) is checked per post for every route that serves a post or anything that follows it: the feed and `me/feed-posts` lists, the detail, comments, reactions, revisions, reports, attached media, and notifications. `Public` is open to anyone (signed out only through the public feed), `SignedIn` needs a signed-in account that attested the minimum age, and `Members` needs an active membership; the author, `SystemAdmin`, and feed moderators always pass. A post whose audience the caller does not meet is `404` and never listed. Until the access-model milestone the engine runs `FullyGated`: member routes already require a membership, so `SignedIn` posts behave like `Members` posts there. Notifications about a post or its comments (except moderation notices) go only to the post's author or to recipients who hold `SystemAdmin` or an active membership and meet the audience; the new post fan-out uses the same rule.

### `FeedPostRequest` (create and edit)

```json
{
  "title": "Welcome to the community",
  "body": "Start here: introduce yourself below. Thanks @alex for the photos. https://example.test/about",
  "mediaIds": ["med_a", "med_b"],
  "visibility": "Members",
  "audience": "Members",
  "commentsEnabled": true,
  "publishAtUtc": null,
  "pin": false,
  "showLinkPreview": true,
  "labels": ["welcome"],
  "tags": { "series": "onboarding" }
}
```

| Field | Rule |
|---|---|
| `title` | Optional, single line, at most 140 characters; empty becomes null |
| `body` | At most `feed.maxBodyLength` (default 10000). Required unless `mediaIds` is non-empty (`fieldErrors.body` "Write something or attach media.") |
| `mediaIds` | Ordered display list, at most `feed.maxAttachments` (10) and `feed.maxVideosPerPost` (1) videos (`400 MediaLimitExceeded`). Each must be the caller's, unattached, and `Processing` or `Ready` (`409 MediaNotAttachable`). On edit it replaces the list; removed media is detached and cleaned up |
| `visibility` | `Members` (default) or `Public` (a public sample post, [Public feed](#public-feed-anonymous)). `Public` needs `SystemAdmin` (`403 Forbidden`) |
| `audience` | `Public`, `SignedIn`, or `Members` (default). `Public` audience and `Public` visibility go together (sending only one sets the other; sending a mismatched pair is `400` with `fieldErrors.audience`) |
| `commentsEnabled` | Default true. False makes `POST .../comments` return `409 CommentsDisabled`; reactions still work |
| `publishAtUtc` | Null publishes now. Otherwise 1 minute to 365 days in the future (`fieldErrors.publishAtUtc`); the post is `Scheduled` until then. On edit only while Scheduled; `null` publishes now |
| `pin` | Create only, `SystemAdmin` only (ignored for others); subject to `feed.maxPinned` (`409 PinLimitReached`) |
| `showLinkPreview` | Default true. Builds a preview card for the first `http` or `https` URL in the body ([Link previews](#link-previews-member)) |
| `labels`, `tags` | Optional; replace semantics on edit ([Labels and tags](#labels-and-tags)); member values pass through the word filter |

The word filter runs on `title`, `body`, and attachment alt text (`400 ContentFiltered` for a Block term; Flag terms file a system report). A post with an attachment still `Processing` is saved as `Processing` and publishes when every attachment is Ready. Edits within `content.editGraceSeconds` (120) of publishing are silent; later edits that change `title`, `body`, or attachments store the previous version as a revision and set `editedUtc`. Delete is a soft delete: the post leaves every list, its pin is cleared, its comments become unreachable, and its media is purged after `moderation.retainRemovedDays`. Create, update, delete, pin, unpin, and publish are audited (`feedPost.create`, `feedPost.update`, `feedPost.delete`, `feedPost.pin`, `feedPost.unpin`, `feedPost.publish`).

```bash
curl -X POST -H "Authorization: Bearer $ACCESS_TOKEN" -H "Content-Type: application/json" \
  -d '{"title":"Welcome to the community","body":"Start here: introduce yourself below.","labels":["welcome"],"tags":{"series":"onboarding"}}' \
  http://127.0.0.1:8000/api/v1/feed
```

### `FeedPost`

```json
{
  "id": "fpo_...",
  "kind": "Original",
  "author": { "id": "usr_...", "displayName": "Example Owner", "handle": "owner", "avatarUrl": "/api/v1/media/avatars/usr_..._1731350000000.jpg", "rank": "Advanced", "rankLevel": 2, "isStaff": true, "isDeleted": false },
  "title": "Welcome to the community",
  "body": "Start here: introduce yourself below. Thanks @alex for the photos. https://example.test/about",
  "excerpt": "Start here: introduce yourself below. Thanks @alex for the photos. https://example.test/about",
  "mentions": [ { "userId": "usr_...", "handle": "alex", "displayName": "Alex Example" } ],
  "attachments": [ { "id": "med_...", "kind": "Image", "status": "Ready", "...": "Media" } ],
  "linkPreview": { "url": "https://example.test/about", "siteName": "Example Community", "title": "About us", "...": "LinkPreview" },
  "visibility": "Members",
  "audience": "Members",
  "state": "Published",
  "commentsEnabled": true,
  "showLinkPreview": true,
  "isPinned": true,
  "pinnedUtc": "2026-11-12T18:05:00Z",
  "publishAtUtc": null,
  "publishedUtc": "2026-11-12T18:00:00Z",
  "createdUtc": "2026-11-12T17:58:00Z",
  "updatedUtc": "2026-11-12T18:05:00Z",
  "editedUtc": null,
  "revisionCount": 0,
  "commentCount": 14,
  "reactionCounts": { "like": 31, "heart": 4 },
  "reactionTotal": 35,
  "myReactions": ["like"],
  "commentTarget": { "type": "FeedPost", "id": "fpo_..." },
  "source": null,
  "moderation": null,
  "viewer": { "canEdit": false, "canDelete": false, "canPin": false, "canComment": true, "canReact": true, "canReport": true, "canModerate": false, "canAttachMedia": true, "canAttachVideo": true },
  "labels": ["welcome"],
  "tags": { "series": "onboarding" }
}
```

| Field | Rule |
|---|---|
| `kind` | `Original` (Phase 2). `PromotedThread` is reserved; clients skip kinds they do not know |
| `state` | `Scheduled`, `Processing`, `Published`, `Hidden` (a moderator or the auto-hide rule), `Removed` (a moderator) |
| `visibility`, `audience` | As in the request. `Public` posts also appear on the public feed |
| `publishedUtc` | Set once, when the post first becomes visible; feed order uses it |
| `editedUtc`, `revisionCount` | Set by edits after the grace period; clients show "edited" and open the revisions |
| `commentCount` | Visible comments (not deleted, removed, or hidden) |
| `reactionCounts`, `reactionTotal`, `myReactions` | Counts only for keys in the property's current reaction set with a count above zero; `myReactions` are the caller's keys. Display order comes from `GET /api/v1/reactions/set` |
| `commentTarget` | Where this card's comments and reactions live. Clients always comment and react against `commentTarget` (the post itself in Phase 2) |
| `moderation` | Null for ordinary viewers. For the author and moderators when Hidden or Removed: `{ state, reason, actedUtc }` |
| `viewer` | The caller's capabilities; clients drive menus from it and the server still enforces every rule |

### Feed collection (`GET /api/v1/feed`)

| | |
|---|---|
| Orderings | `Feed` (default), `PublishedDescending`, `CreatedDescending`, `CreatedAscending` |
| Filters | `publishedAfterUtc`, `publishedBeforeUtc` (on `publishedUtc`; `publishedAfterUtc` at or after `publishedBeforeUtc` is `400 ValidationFailed` under `filters.publishedBeforeUtc`, here and on the public and admin feeds), `audience` (`Public`, `SignedIn`, `Members`) |
| Search | title, body |
| Labels and tags | `FeedPost` |
| Page size | `maxResults` default 20, at most 50 |

`Feed` ordering puts pinned Published posts first (`pinnedUtc` descending), then unpinned posts by newest `publishedUtc`. Pinned and unpinned posts share one SQL keyset and count toward `maxResults`; no prefix exceeds the page cap. Pins may occupy several pages when the requested size is small. `totalRecords` counts all matching posts on every page. `PublishedDescending` follows publication order throughout. Posts by members the caller blocked are omitted (staff authors remain visible). Clients refresh by fetching the first page.

```bash
curl -H "Authorization: Bearer $ACCESS_TOKEN" \
  "http://127.0.0.1:8000/api/v1/feed?maxResults=20&publishedAfterUtc=2026-11-01T00:00:00Z&labels=welcome"
```

```json
{
  "success": true, "maxResults": 20, "skip": 0, "continuationToken": "eyJ2IjoxLC...", "endOfResults": false,
  "totalRecords": 42, "recordsRemaining": 22, "timestampUtc": "2026-11-12T18:30:00Z",
  "objects": [ { "id": "fpo_...", "isPinned": true, "...": "FeedPost" }, { "id": "fpo_...", "isPinned": false, "...": "FeedPost" } ]
}
```

`GET /api/v1/me/feed-posts` (the caller's own posts in every state except deleted, so `canPostToFeed` members can find scheduled and processing posts): orderings `CreatedDescending` (default) and `CreatedAscending`; filters `state` (`Scheduled`, `Processing`, `Published`, `Hidden`, `Removed`; `Deleted` is `400`) and `audience`; search over title and body; `FeedPost` labels and tags; default 20, at most 50.

### Revisions

`GET /api/v1/feed/{id}/revisions` lists the stored previous versions, newest first (orderings `CreatedDescending` default and `CreatedAscending`; search over title and body; default 20, at most 50; no typed filters; the standard `createdAfterUtc` and `createdBeforeUtc` window; labels and tags of type `FeedPostRevision`). The current version is the post itself. Each revision carries the labels and tags the post had in that version (copied when the revision is stored, read-only afterwards; `staff:` entries only for staff), so label and tag filters find the versions that carried them.

```json
{ "id": "fpr_...", "revision": 1, "title": "Welcome", "body": "Start here.", "attachmentSummary": "Removed 1 image, added 1 video",
  "editedBy": { "id": "usr_...", "displayName": "Example Owner", "...": "UserSummary" }, "editedByUserId": "usr_...", "editedByName": "Example Owner",
  "createdUtc": "2026-11-12T19:00:00Z", "labels": [], "tags": {} }
```

## Public feed (Anonymous)

Public sample posts for the marketing site (contract section 5.10). Registered before authentication; no token needed.

| Method and path | Group | Success | Errors |
|---|---|---|---|
| `GET /api/v1/public/feed`, `POST /api/v1/public/feed/enumerate` | Anonymous | `200 EnumerationResult<PublicFeedPost>` | `400 ValidationFailed`, `400 InvalidContinuationToken`, `404 NotFound` (public samples disabled), `429 RateLimited` (`rateLimits.publicFeedPerMinute` per IP) |
| `GET /api/v1/public/feed/{id}` | Anonymous | `200 PublicFeedPost` | `404 NotFound` (unknown, not public, or public samples disabled), `429 RateLimited` |

- Only `Published`, unmoderated, undeleted posts with `visibility` and `audience` both `Public`.
- Every successful response carries `Cache-Control: public, max-age=60`.
- Gated by the property's `features.publicSamples` (default true): when false, every route returns `404 NotFound`.
- Orderings `PublishedDescending` (default, newest `publishedUtc` first), `Feed` (accepted, same order, no pinned prefix), `CreatedDescending`, `CreatedAscending`; filters `publishedAfterUtc`, `publishedBeforeUtc`, `audience` (only `Public` matches anything here); search over title and body; `FeedPost` labels and tags (non-staff view, so no `staff:` filters). `maxResults` default 10, at most 20.
- `PublicFeedPost` is the `FeedPost` without `mentions`, `myReactions`, `viewer`, `moderation`, `pinnedUtc`, `publishAtUtc`, `updatedUtc`, `showLinkPreview`, and `source`; attachments use the public media paths (`/public/media/{id}/`, `/public/video/{id}/`), which need no media grant. Comments are not exposed; the card shows `commentCount`.

```bash
curl -i "http://127.0.0.1:8000/api/v1/public/feed?maxResults=10"
```

```json
{
  "success": true, "maxResults": 10, "skip": 0, "continuationToken": null, "endOfResults": true, "totalRecords": 1, "recordsRemaining": 0, "timestampUtc": "2026-11-12T18:30:00Z",
  "objects": [ {
    "id": "fpo_...", "kind": "Original", "author": { "id": "usr_...", "displayName": "Example Owner", "...": "UserSummary" },
    "title": "What a class looks like", "body": "A short sample from this week.", "excerpt": "A short sample from this week.",
    "attachments": [ { "id": "med_...", "kind": "Image", "...": "Media with public paths" } ], "linkPreview": null,
    "visibility": "Public", "audience": "Public", "state": "Published", "commentsEnabled": true, "isPinned": false,
    "publishedUtc": "2026-11-12T18:00:00Z", "createdUtc": "2026-11-12T17:58:00Z", "editedUtc": null, "revisionCount": 0,
    "commentCount": 3, "reactionCounts": { "like": 9 }, "reactionTotal": 9,
    "commentTarget": { "type": "FeedPost", "id": "fpo_..." }, "labels": [], "tags": {}
  } ]
}
```

## Comments (Member)

Threaded, Reddit-style comments on feed posts (contract section 7). A reply to a comment already at `comments.maxDepth` (default 8) is stored as that comment's sibling with `replyToCommentId` and `replyToUser` set ("replying to @handle"), so members can always reply.

| Method and path | Group | Body | Success | Errors |
|---|---|---|---|---|
| `GET /api/v1/feed/{id}/comments`, `POST /api/v1/feed/{id}/comments/enumerate` | Member | `EnumerationQuery` plus filters `depth`, `childLimit` | `200 CommentTreePage` | `400 ValidationFailed`, `400 InvalidContinuationToken`, `404 NotFound`, `429 RateLimited` |
| `POST /api/v1/feed/{id}/comments` | Member (`canPost`, not muted) | `CommentRequest` | `201 Comment` (with `children: []`) | `400 ValidationFailed`, `400 MediaLimitExceeded`, `400 ContentFiltered`, `403 PostingNotAllowed`, `403 MediaNotAllowed`, `403 VideoUploadNotAllowed`, `403 Muted`, `403 InteractionBlocked`, `403 Forbidden` (admin token), `404 NotFound` (post not visible or not Published, or parent on another post), `409 CommentsDisabled`, `409 ParentUnavailable`, `409 MediaNotAttachable`, `429 RateLimited` (comments per minute and per day) |
| `GET /api/v1/comments/{id}` | Member | query `depth`, `childLimit`, `ordering` (alias `sort`) | `200 CommentPermalink` | `400 ValidationFailed`, `404 NotFound`, `429 RateLimited` |
| `GET /api/v1/comments/{id}/children`, `POST /api/v1/comments/{id}/children/enumerate` | Member | `EnumerationQuery` plus `cursor` and filters `depth`, `childLimit` | `200 CommentTreePage` (the direct replies of `{id}`) | `400 ValidationFailed`, `400 InvalidContinuationToken`, `404 NotFound`, `429 RateLimited` |
| `PUT /api/v1/comments/{id}` | Member (author only, with `canPost`) | `CommentRequest` without `parentCommentId`; omitted fields unchanged | `200 Comment` | `400 ValidationFailed`, `400 MediaLimitExceeded`, `400 ContentFiltered`, `403 Forbidden` (not the author), `403 PostingNotAllowed`, `403 MediaNotAllowed`, `403 VideoUploadNotAllowed`, `403 Muted`, `404 NotFound`, `409 MediaNotAttachable`, `429 RateLimited` (`rateLimits.editsPerHour`) |
| `DELETE /api/v1/comments/{id}` | Member (author or `SystemAdmin`) | | `204` | `403 Forbidden`, `404 NotFound` |
| `GET /api/v1/comments/{id}/revisions`, `POST /api/v1/comments/{id}/revisions/enumerate` | Member (anyone who can see the comment) | `EnumerationQuery` | `200 EnumerationResult<CommentRevision>` | `400 ValidationFailed`, `400 InvalidContinuationToken`, `404 NotFound`, `429 RateLimited` |

Comment rate limits: `comments.perMinute` (10) and `comments.perDay` (300); accounts younger than `comments.newAccountHours` (24) get `comments.newAccountPerMinute` (3) and `comments.newAccountPerDay` (30). Delete is a soft delete: counts on the post and ancestors drop, the comment stays as a `Deleted` placeholder while it has visible replies, and attached media is purged after `moderation.retainRemovedDays`.

### `CommentRequest`

```json
{ "body": "Does this work for beginners too?", "parentCommentId": "cmt_...", "mediaIds": [], "showLinkPreview": true, "labels": ["question"], "tags": { "level": "beginner" } }
```

| Field | Rule |
|---|---|
| `body` | At most `comments.maxBodyLength` (5000); required unless an attachment is present. Headings render as plain text in comments |
| `parentCommentId` | Create only, optional; must be on the same post (`404 NotFound` otherwise). A deleted, removed, or invisible parent is `409 ParentUnavailable`. The author of the parent (or of the post, for a top-level comment) having blocked the caller is `403 InteractionBlocked` |
| `mediaIds` | At most `comments.maxAttachments` (default 1; 0 disables comment media), at most one video; same ownership rules as posts. Processing media is shown immediately with its Processing state |
| `showLinkPreview` | Default true |
| `labels`, `tags` | Optional; replace semantics on edit |

The word filter runs on `body` and attachment alt text. Edits within `content.editGraceSeconds` are silent; later edits store a revision and set `editedUtc`.

### `Comment`

```json
{
  "id": "cmt_...",
  "targetType": "FeedPost",
  "targetId": "fpo_...",
  "parentCommentId": null,
  "replyToCommentId": null,
  "replyToUser": null,
  "depth": 0,
  "state": "Visible",
  "author": { "id": "usr_...", "displayName": "Alex Example", "handle": "alex", "...": "UserSummary" },
  "body": "Does this work for beginners too?",
  "mentions": [],
  "attachments": [],
  "linkPreview": null,
  "createdUtc": "2026-11-12T18:20:00Z",
  "editedUtc": null,
  "revisionCount": 0,
  "childCount": 7,
  "descendantCount": 9,
  "reactionCounts": { "idea": 3 },
  "reactionTotal": 3,
  "myReactions": [],
  "moderation": null,
  "viewer": { "canEdit": false, "canDelete": false, "canReply": true, "canReact": true, "canReport": true, "canModerate": false, "canAttachMedia": true, "canAttachVideo": true },
  "children": [ { "id": "cmt_...", "depth": 1, "...": "Comment" } ],
  "moreChildren": { "remainingCount": 2, "cursor": "eyJ2IjoxLC...", "continuationToken": "eyJ2IjoxLC...", "enumerate": "/api/v1/comments/cmt_.../children/enumerate" },
  "labels": ["question"],
  "tags": { "level": "beginner" }
}
```

| `state` | Who sees it this way | Payload |
|---|---|---|
| `Visible` | Everyone | Full |
| `Deleted` | Everyone | `author`, `body`, `mentions`, `attachments`, `linkPreview` null or empty; reactions zeroed; every `viewer` flag false |
| `Removed` | Everyone except moderators | Same as Deleted |
| `Hidden` | Everyone except the author and moderators | Same as Deleted |
| `BlockedAuthor` | A member who blocked the author | Same as Deleted |

Placeholders (`Deleted`, `Removed`, `Hidden`, `BlockedAuthor`) appear only while they have visible descendants, so the tree keeps its shape; otherwise they are omitted. The author and moderators see Hidden and Removed comments in full with `moderation: { state, reason, actedUtc }`. `childCount` and `descendantCount` count visible replies; `depth` is 0 for top-level comments.

### Comment trees (`GET /api/v1/feed/{id}/comments`)

A tree page is an `EnumerationResult` of top-level comments, each with `children` nested to the requested depth, plus the post's `commentCount`:

| | |
|---|---|
| Orderings | `Top` (default: `score` descending, then `createdUtc` ascending, then id; `score` is `reactionTotal + 2 * childCount`), `CreatedDescending`, `CreatedAscending`. The ordering applies at every level |
| Filters | `depth` (1 to 5, default 3; levels returned, counting the top level), `childLimit` (1 to 20, default 5; replies under each returned comment) |
| Tree size | At most 1000 comments per response across every level (`CommentService.MaxTreeNodes`), including a permalink's subtree. Replies are filled level by level in page order; a parent whose replies were left out carries `moreChildren` (`remainingCount`, `enumerate`, and a `cursor` when some replies were returned), so clients continue on the children route as they do when `depth` or `childLimit` runs out |
| Search | body |
| Labels and tags | `Comment` (applied to the top-level comments) |
| Page size | `maxResults` default 20, at most 50 (top-level comments) |

Typed filters (`depth`, `childLimit` here, and `key`, `state`, `publishedAfterUtc`, `publishedBeforeUtc` on the other collections) may be sent as GET query parameters (`?depth=2&childLimit=3`) or in the POST body's `filters` (`"filters": { "depth": 2, "childLimit": 3 }`); out-of-range values are `400 ValidationFailed` with `fieldErrors["filters.depth"]` or `fieldErrors["filters.childLimit"]`.

```bash
curl -X POST -H "Authorization: Bearer $ACCESS_TOKEN" -H "Content-Type: application/json" \
  -d '{"maxResults":20,"ordering":"Top","filters":{"depth":3,"childLimit":5}}' \
  http://127.0.0.1:8000/api/v1/feed/fpo_.../comments/enumerate
```

```json
{
  "success": true, "maxResults": 20, "skip": 0, "continuationToken": "eyJ2IjoxLC...", "endOfResults": false,
  "totalRecords": 31, "recordsRemaining": 11, "timestampUtc": "2026-11-12T18:30:00Z",
  "commentCount": 154,
  "objects": [ {
    "id": "cmt_A", "depth": 0, "childCount": 7, "...": "Comment",
    "children": [ { "id": "cmt_B", "depth": 1, "children": [], "moreChildren": { "remainingCount": 4, "cursor": null, "continuationToken": null, "enumerate": "/api/v1/comments/cmt_B/children/enumerate" }, "...": "Comment" } ],
    "moreChildren": { "remainingCount": 2, "cursor": "eyJ2IjoxLC...", "continuationToken": "eyJ2IjoxLC...", "enumerate": "/api/v1/comments/cmt_A/children/enumerate" }
  } ]
}
```

`moreChildren` appears on a comment that has visible replies not included in `children`:

| Field | Meaning |
|---|---|
| `remainingCount` | Replies not shown at that level. When the page's depth ran out (`children` is empty) it is the comment's visible reply count |
| `cursor`, `continuationToken` | The same token: continue after the last reply shown. Null when `children` is empty (start from the first reply) |
| `enumerate` | `POST /api/v1/comments/{id}/children/enumerate` for this comment |

Load more replies or "continue this thread" with the children route, sending the token as `continuationToken` or `cursor` (alias) and the ordering the token was issued for (a token from another ordering, comment, or filter set is `400 InvalidContinuationToken`):

```json
{ "cursor": "eyJ2IjoxLC...", "ordering": "Top", "maxResults": 20, "filters": { "depth": 2, "childLimit": 5 } }
```

The children route returns a `CommentTreePage` of the direct replies of `{id}` with the same nesting, orderings, filters, and page size (`maxResults` default 20, at most 50; `commentCount` is the post's).

### Permalink (`GET /api/v1/comments/{id}`)

Renders a comment in context for notification deep links. Query `depth` (1 to 5, default 3, counting the comment), `childLimit` (1 to 20, default 5), `ordering` (or `sort`: `Top`, `CreatedDescending`, `CreatedAscending`). A placeholder without visible replies is `404`, and so is a comment by a member the caller blocked (whatever its replies; a reply's permalink still works and shows that ancestor as a `BlockedAuthor` placeholder).

```json
{
  "target": { "type": "FeedPost", "id": "fpo_..." },
  "ancestors": [ { "id": "cmt_A", "depth": 0, "children": [], "...": "Comment" } ],
  "comment": { "id": "cmt_B", "depth": 1, "children": [ { "...": "Comment" } ], "...": "Comment" }
}
```

`ancestors` are root first, without children, and bounded by `comments.maxDepth` (at most 20).

### Comment revisions

`GET /api/v1/comments/{id}/revisions`: newest first (orderings `CreatedDescending` default and `CreatedAscending`; search over body; default 20, at most 50; no typed filters; the standard created window; labels and tags of type `CommentRevision`). Objects: `{ id, revision, body, attachmentSummary, createdUtc, labels, tags }` (`cmr_` ids), where `labels` and `tags` are the comment's metadata in that version (read-only).

## Reactions (Member)

The reaction set comes from the property's `reactions` (`property.json`), addressed by key. The sample property defines `like`, `heart`, and `idea`.

| Method and path | Group | Success | Errors |
|---|---|---|---|
| `GET /api/v1/reactions/set`, `POST /api/v1/reactions/set/enumerate` | Member | `200 EnumerationResult<ReactionSetItem>`; context: likeKey | |
| `PUT /api/v1/feed/{id}/reactions/{key}` | Member (not muted) | `200 ReactionState` (idempotent; several keys per member) | `400 UnknownReaction`, `403 Muted`, `403 InteractionBlocked`, `403 Forbidden` (admin token), `404 NotFound` (not visible or not Published), `429 RateLimited` (`reactions.perMinute`) |
| `DELETE /api/v1/feed/{id}/reactions/{key}` | Member (not muted) | `200 ReactionState` (idempotent) | same as `PUT` |
| `PUT /api/v1/comments/{id}/reactions/{key}`, `DELETE /api/v1/comments/{id}/reactions/{key}` | Member (not muted) | `200 ReactionState` | same as `PUT` (`404` also for placeholders, moderated comments, and comments on unpublished posts) |
| `GET /api/v1/feed/{id}/reactions`, `POST /api/v1/feed/{id}/reactions/enumerate` | Member | `200 EnumerationResult<Reaction>` | `400 ValidationFailed`, `400 UnknownReaction` (`key` filter), `400 InvalidContinuationToken`, `404 NotFound`, `429 RateLimited` |
| `GET /api/v1/comments/{id}/reactions`, `POST /api/v1/comments/{id}/reactions/enumerate` | Member | `200 EnumerationResult<Reaction>` | same |

```json
{ "success": true, "maxResults": 25, "skip": 0, "continuationToken": null, "endOfResults": true, "totalRecords": 3, "recordsRemaining": 0, "timestampUtc": "2026-11-23T00:00:00Z", "objects": [ { "key": "like", "emoji": "👍", "label": "Like" }, { "key": "heart", "emoji": "❤️", "label": "Love" }, { "key": "idea", "emoji": "💡", "label": "Idea" } ], "likeKey": "like" }
```

Reaction catalog pages use `EnumerationResult<ReactionSetItem>` with `objects` and scalar `likeKey` (the one-tap shortcut). The key remains the configured first reaction even when paging or filtering. `ReactionState`, returned by `PUT` and `DELETE` so clients replace their copy:

```json
{ "reactionCounts": { "like": 12, "idea": 3 }, "reactionTotal": 15, "myReactions": ["like", "idea"] }
```

Reacting to your own content is allowed and does not notify; reacting on a post with `commentsEnabled: false` is allowed. Removing a key from the property hides its counts but keeps the rows.

Who reacted (Reaction collection): orderings `CreatedDescending` (default) and `CreatedAscending`; filter `key` (a current reaction key; others are `400 UnknownReaction`); no search; `Reaction` labels and tags; `maxResults` default 50, at most 100. Members the caller blocked are omitted.

```json
{ "success": true, "maxResults": 50, "skip": 0, "continuationToken": null, "endOfResults": true, "totalRecords": 1, "recordsRemaining": 0, "timestampUtc": "2026-11-12T18:30:00Z",
  "objects": [ { "id": "rxn_...", "user": { "id": "usr_...", "displayName": "Alex Example", "...": "UserSummary" }, "key": "heart", "createdUtc": "2026-11-12T18:10:00Z", "labels": [], "tags": {} } ] }
```

## Reports (Member)

`POST /api/v1/reports` (Member; muted and restricted members may report; the admin token gets `403 Forbidden`):

```json
{ "targetType": "Comment", "targetId": "cmt_...", "reason": "Harassment", "details": "Keeps going after another member." }
```

| Field | Rule |
|---|---|
| `targetType` | `FeedPost`, `Comment`, `Thread`, `User`, or `AssistantMessage` |
| `targetId` | Required; the target must exist and be visible to the caller (`404 NotFound` otherwise). An `AssistantMessage` must be an assistant reply in a conversation the caller owns (AI must be on; `404 NotAvailable` otherwise) |
| `reason` | `Spam`, `Harassment`, `HateSpeech`, `SexualContent`, `Violence`, `SelfHarm`, `Impersonation`, `IntellectualProperty`, `Other`. `Filter` is the reason of system reports filed by word filter `Flag` terms (no reporter); members cannot send it (`400 ValidationFailed`), but it appears in report views, queue `reasons`, and the `reason` filters |
| `details` | Optional, at most 1000; at least 10 characters when `reason` is `Other` |
| `labels`, `tags` | Optional metadata for the new report ([Labels and tags](#labels-and-tags)). `staff:` entries from non-staff are ignored and member values pass through the word filter; invalid values are `400 ValidationFailed` before anything is stored. Applied only when a new report is created (the `200` existing-report response leaves its metadata unchanged); later edits are staff-only |

Success `201 ReportCreated` for a new report; `200` with the caller's existing Open report on the same target (no duplicate, and no rate limit charge):

```json
{ "id": "rpt_...", "status": "Open", "createdUtc": "2026-11-12T18:40:00Z", "labels": [], "tags": {} }
```

Errors: `400 ValidationFailed`, `400 CannotReportSelf` (your own content or profile), `403 Forbidden` (admin token), `404 NotFound`, `429 RateLimited` (`moderation.reportsPerHour`). The request accepts `labels` and `tags` (above); staff edit them later with the generic metadata routes. Auto-hide: when the distinct reporters with Open reports on a post or comment reach `moderation.autoHideReportThreshold` (3; 0 disables), the content becomes `Hidden` with a system `AutoHide` action (never for `SystemAdmin` authors). Each report is due `moderation.reviewTargetHours` (12) after it is filed.

**Assistant replies** (docs/AI_CONTRACT.md section 12.3): reports on an `AssistantMessage` are filed in scope `Assistant` (`scopeId` the assistant) with no target author, are reviewed by `SystemAdmin` only, and never auto-hide. The first report on a reply stores a moderation snapshot: a copy of the conversation up to and including the reply (at most 1000 messages), its endpoint, model, and citations, kept for `moderation.retainRemovedDays` even if the member deletes the conversation. The server files a system report (reason `Filter`, no reporter, same scope) with a snapshot when output moderation withholds a reply (`OutputBlocked`; the snapshot keeps the withheld text, the stored reply only the notice), flags a reply or a member message (`OutputFlagged`, `InputFlagged`, in moderation mode `Block`), or the word filter flags a member message.

## Admin: feed management (Admin)

Admin group (`SystemAdmin` or the break-glass token). Design record: contract section 10.1.

| Method and path | Body | Success | Errors |
|---|---|---|---|
| `GET /api/v1/admin/feed`, `POST /api/v1/admin/feed/enumerate` | `EnumerationQuery` | `200 EnumerationResult<AdminFeedItem>` (exact totals) | `400 ValidationFailed`, `400 InvalidContinuationToken` |
| `POST /api/v1/admin/feed` | `FeedPostRequest` plus `authorUserId` (default the caller; must have `canPostToFeed` or be `SystemAdmin`, else `fieldErrors.authorUserId`) | `201 FeedPost` | `400 ValidationFailed`, `400 MediaLimitExceeded`, `400 ContentFiltered`, `403 Forbidden` (the break-glass token cannot author; a member session is required), `409 MediaNotAttachable`, `409 PinLimitReached`, `429 RateLimited` (`feed.postsPerHour`) |
| `GET /api/v1/admin/feed/{id}` | | `200 AdminFeedPostDetail` (any state, including deleted) | `404 NotFound` |
| `PUT /api/v1/admin/feed/{id}` | `FeedPostRequest` (no edit window) | `200 FeedPost` | `400 ValidationFailed`, `400 MediaLimitExceeded`, `400 ContentFiltered`, `404 NotFound`, `409 MediaNotAttachable` |
| `DELETE /api/v1/admin/feed/{id}` | | `204` (soft delete) | `404 NotFound` |
| `POST /api/v1/admin/feed/{id}/pin`, `POST /api/v1/admin/feed/{id}/unpin` | none | `200 FeedPost` (idempotent) | `404 NotFound`, `409 NotPublished`, `409 PinLimitReached` (pin) |
| `POST /api/v1/admin/feed/{id}/publish-now` | none | `200 FeedPost` (still `Processing` until every attachment is Ready) | `404 NotFound`, `409 NotScheduled` |
| `GET /api/v1/admin/metrics/engagement?days=30` | | `200 EngagementMetrics` | `400 ValidationFailed` (`fieldErrors.days`: 7 to 365) |

**Admin feed collection**:

| | |
|---|---|
| Orderings | `PublishedDescending` (default), `PublishedAscending`, `CreatedDescending`, `CreatedAscending`, `PublishAtAscending`, `CommentCountDescending`, `ReactionTotalDescending` |
| Time windows | The standard `createdAfterUtc` and `createdBeforeUtc` (on `createdUtc`, like every collection), and the published window `publishedAfterUtc` and `publishedBeforeUtc` (filters; on `publishedUtc`, or `createdUtc` for unpublished posts) with the aliases `fromUtc` and `toUtc`. Both windows apply together; sending a name and its alias with different values is `400 ValidationFailed` |
| Filters | `state` (any state or `Deleted`), `authorUserId`, `pinned` (`true` or `false`), `audience` (`Public`, `SignedIn`, `Members`), `publishedAfterUtc`, `publishedBeforeUtc`, `fromUtc`, `toUtc` |
| Search | title, body |
| Labels and tags | `FeedPost` (staff view) |

```bash
curl -H "Authorization: Bearer $TK_ADMIN_TOKEN" \
  "http://127.0.0.1:8000/api/v1/admin/feed?state=Scheduled&ordering=PublishAtAscending&maxResults=50"
```

`AdminFeedItem`:

```json
{ "id": "fpo_...", "kind": "Original", "title": "Meetup this Saturday", "excerpt": "Bring a friend.", "author": { "id": "usr_...", "...": "UserSummary" },
  "state": "Scheduled", "visibility": "Members", "audience": "Members", "isPinned": false, "publishAtUtc": "2026-11-14T16:00:00Z", "publishedUtc": null,
  "createdUtc": "2026-11-12T18:00:00Z", "editedUtc": null, "deletedUtc": null, "attachmentCount": 1, "hasVideo": false,
  "commentCount": 0, "reactionTotal": 0, "openReportCount": 0, "labels": ["events"], "tags": { "staff:review": "done" } }
```

`AdminFeedPostDetail` is the `FeedPost` plus `deletedUtc`, `deletedByUserId`, `openReportCount`, `reportSummary: { open, total }`, and two bounded previews (at most 5 objects each): `revisions` (enumerate `/api/v1/feed/{id}/revisions/enumerate`) and `moderationActions` (`ModerationActionView`; enumerate `/api/v1/admin/moderation/queue/FeedPost/{id}/actions/enumerate`):

```json
{ "id": "fpo_...", "...": "FeedPost fields", "deletedUtc": null, "deletedByUserId": null, "openReportCount": 1,
  "revisions": { "objects": [ { "id": "fpr_...", "revision": 1, "...": "FeedPostRevision" } ], "totalRecords": 1, "enumerate": "/api/v1/feed/fpo_.../revisions/enumerate" },
  "moderationActions": { "objects": [], "totalRecords": 0, "enumerate": "/api/v1/admin/moderation/queue/FeedPost/fpo_.../actions/enumerate" },
  "reportSummary": { "open": 1, "total": 2 } }
```

`EnumerationResult<LegacyMetricRow>` (one entry per day, at most 365; `activeMembers` commented or reacted in the window; `commentsByDay` counts every comment, `threadCommentsByDay` only comments on forum threads; the forums series `threadsByDay` and `threadCommentsByDay` are always present, all zero when forums are unused):

```json
{ "days": 30, "postsByDay": [ { "date": "2026-10-14", "count": 2 } ], "commentsByDay": [ { "date": "2026-10-14", "count": 19 } ], "reactionsByDay": [ { "date": "2026-10-14", "count": 64 } ],
  "threadsByDay": [ { "date": "2026-10-14", "count": 4 } ], "threadCommentsByDay": [ { "date": "2026-10-14", "count": 11 } ],
  "activeMembers": 87, "openReports": 3, "scheduledPosts": 2 }
```

## Moderation (Moderator)

Moderator group: `SystemAdmin`, the break-glass token, or any `Moderator` or `FeedModerator` role assignment; others get `403 Forbidden`. Per-item scope is checked in the service: `FeedModerator` sees reports and acts on content (`FeedPost` and `Comment`) in the Feed scope, never on content authored by a `SystemAdmin`. Reports on `User` targets and on `SystemAdmin` content are handled by `SystemAdmin` only: they are absent from a `FeedModerator`'s queue, counts, and summary, and the detail, reports, and dismiss routes for them are `403 Forbidden`. Moderation by the break-glass token is recorded with the actor name "break-glass token" (distinct from "system") on the action (`moderatorName`), on reports it resolves (`resolvedByName`), and in the audit record (`actorName`). Design record: contract sections 9.1, 9.3, and 10.2.

| Method and path | Body | Success | Errors |
|---|---|---|---|
| `POST /api/v1/admin/moderation/actions` | `ModerationActionRequest` | `200 ModerationActionResult` | `400 ValidationFailed`, `403 Forbidden`, `404 NotFound`, `409 InvalidModerationState` |
| `GET /api/v1/admin/moderation/actions`, `POST /api/v1/admin/moderation/actions/enumerate` | `EnumerationQuery` | `200 EnumerationResult<ModerationActionView>` | `400 ValidationFailed`, `400 InvalidContinuationToken`, `403 Forbidden` |
| `GET /api/v1/admin/moderation/queue`, `POST /api/v1/admin/moderation/queue/enumerate` | `EnumerationQuery` | `200 EnumerationResult<ModerationQueueItem>` | `400 ValidationFailed`, `400 InvalidContinuationToken`, `403 Forbidden` |
| `GET /api/v1/admin/moderation/queue/{targetType}/{targetId}` | | `200 ModerationQueueDetail` | `400 ValidationFailed` (`targetType` not `FeedPost`, `Comment`, or `User`), `403 Forbidden`, `404 NotFound` |
| `POST /api/v1/admin/moderation/queue/{targetType}/{targetId}/dismiss` | `{ "note"?: "...", "labels"?: [], "tags"?: {} }` (note at most 500; labels and tags are stored on the Dismiss action when one is recorded) | `200 ModerationQueueDetail` | `400 ValidationFailed`, `403 Forbidden`, `404 NotFound` |
| `GET /api/v1/admin/moderation/queue/{targetType}/{targetId}/reports`, `POST .../reports/enumerate` | `EnumerationQuery` | `200 EnumerationResult<ReportView>` | `400 ValidationFailed`, `400 InvalidContinuationToken`, `403 Forbidden`, `404 NotFound` |
| `GET /api/v1/admin/moderation/queue/{targetType}/{targetId}/actions`, `POST .../actions/enumerate` | `EnumerationQuery` | `200 EnumerationResult<ModerationActionView>` | `400 ValidationFailed`, `400 InvalidContinuationToken`, `403 Forbidden`, `404 NotFound` |
| `GET /api/v1/admin/moderation/queue/{targetType}/{targetId}/snapshot` | targetType must be AssistantMessage. The retained copy of the conversation up to and including the reported or withheld message (docs/AI_CONTRACT.md section 12.3), oldest first; it outlives the conversation until moderation.retainRemovedDays after the report. SystemAdmin. | `200` List a reported assistant message's snapshot | `400` |
| `POST /api/v1/admin/moderation/queue/{targetType}/{targetId}/snapshot/enumerate` | targetType must be AssistantMessage. The retained copy of the conversation up to and including the reported or withheld message (docs/AI_CONTRACT.md section 12.3), oldest first; it outlives the conversation until moderation.retainRemovedDays after the report. SystemAdmin. | `200` List a reported assistant message's snapshot | `400` |
| `GET /api/v1/admin/moderation/content`, `POST /api/v1/admin/moderation/content/enumerate` | `EnumerationQuery` (filter `authorUserId` required) | `200 EnumerationResult<ModerationContentItem>` | `400 ValidationFailed`, `400 InvalidContinuationToken`, `403 Forbidden` |
| `GET /api/v1/admin/moderation/summary` | | `200 ModerationSummary` | `403 Forbidden` |

The word filter (`/api/v1/admin/moderation/word-filter*`) is Admin, not Moderator ([Admin: word filter](#admin-word-filter-admin)).

### Moderation actions

```json
{ "targetType": "Comment", "targetId": "cmt_...", "action": "Remove", "reason": "Spam", "note": "Third link drop today.", "notifyAuthor": true, "resolveReports": true }
```

| Field | Rule |
|---|---|
| `targetType` | `FeedPost` or `Comment` |
| `targetId` | Required; deleted content is `404` |
| `action` | `Hide` (visible content becomes `Hidden`), `Unhide` (needs Hidden), `Remove` (visible or hidden content becomes `Removed`; served media moves to the retained bucket), `Restore` (needs Removed and not yet purged; media moves back). Any other transition is `409 InvalidModerationState`. `AutoHide` and `Dismiss` are recorded by the system and cannot be sent |
| `reason` | Required, 3 to 500 characters (free text or a report reason) |
| `note` | Optional, at most 500 |
| `notifyAuthor` | Default true for `Remove` and `Restore`, false for `Hide` and `Unhide`; sends a `ModerationAction` notification |
| `resolveReports` | Default true: marks the target's Open reports in the caller's scope `ActionTaken` with this action |
| `labels`, `tags` | Optional metadata for the new action (moderators may set `staff:` entries). Invalid values are `400 ValidationFailed` before the target changes |

Labels and tags sent with the request are stored on the action (type `ModerationAction`) and filter the actions collection; the generic metadata routes edit them later. Every action writes an audit record (`moderation.hide`, `moderation.unhide`, `moderation.remove`, `moderation.restore`).

`ModerationActionResult`:

```json
{
  "action": { "id": "mac_...", "moderator": { "id": "usr_...", "displayName": "Example Owner", "...": "UserSummary" }, "targetType": "Comment", "targetId": "cmt_...",
    "targetAuthor": { "id": "usr_...", "displayName": "Alex Example", "...": "UserSummary" }, "action": "Remove", "reason": "Spam", "note": "Third link drop today.",
    "notifiedAuthor": true, "createdUtc": "2026-11-12T19:00:00Z", "labels": [], "tags": {} },
  "target": { "id": "cmt_...", "state": "Removed", "moderation": { "state": "Removed", "reason": "Spam", "actedUtc": "2026-11-12T19:00:00Z" }, "...": "Comment (or FeedPost) as a moderator sees it" }
}
```

`ModerationActionView`: `{ id, moderator: UserSummary | null (null for system actions such as AutoHide and for the break-glass token), moderatorName, targetType, targetId, targetAuthor, action, reason, note, notifiedAuthor, createdUtc, labels, tags }`. `moderatorName` is the moderator's display name, "break-glass token", or "system".

**Actions collection** (`/api/v1/admin/moderation/actions` and the per-target `.../queue/{targetType}/{targetId}/actions`):

| | |
|---|---|
| Orderings | `CreatedDescending` (default), `CreatedAscending` |
| Filters | `moderatorUserId` (`system` for system actions; break-glass token actions are not included), `targetType`, `targetId`, `targetAuthorUserId`, `action` (`Hide`, `Unhide`, `Remove`, `Restore`, `AutoHide`, `Dismiss`), `fromUtc` and `toUtc` (aliases of the created window). The per-target route fixes the target |
| Search | reason, note |
| Labels and tags | `ModerationAction` |

### Moderation queue

One row per reported target in the caller's scope.

| | |
|---|---|
| Orderings | `DueAscending` (default; overdue first), `LastReportedDescending`, `FirstReportedAscending`, `ReportCountDescending`, `CreatedDescending`, `CreatedAscending` (by first report) |
| Filters | `status` (`Open`, the default, or `Resolved`), `targetType` (`FeedPost`, `Thread`, `Comment`, `User`, `AssistantMessage`), `reason` (a report reason), `overdue` (`true` or `false`), `scopeType` (`Feed`, `Forum`, or `Assistant`), `forumIds` |
| Search | none |
| Labels and tags | The target's own metadata (`FeedPost`, `Comment`, or `User`) |
| Totals | Exact |

```bash
curl -H "Authorization: Bearer $TK_ADMIN_TOKEN" \
  "http://127.0.0.1:8000/api/v1/admin/moderation/queue?status=Open&overdue=true&targetType=Comment&maxResults=50"
```

`ModerationQueueItem`:

```json
{ "targetType": "Comment", "targetId": "cmt_...", "targetState": "Hidden",
  "preview": { "title": null, "excerpt": "Buy cheap gear at ...", "hasMedia": false },
  "author": { "id": "usr_...", "displayName": "Sam Sample", "...": "UserSummary" },
  "reportCount": 3, "reporterCount": 3, "reasons": { "Spam": 2, "Harassment": 1 },
  "firstReportedUtc": "2026-11-12T06:00:00Z", "lastReportedUtc": "2026-11-12T09:00:00Z", "dueUtc": "2026-11-12T18:00:00Z", "isOverdue": true,
  "status": "Open", "scope": { "type": "Feed", "id": "*", "name": null }, "labels": [], "tags": {} }
```

`scope.name` is the forum name (`Forum`) or the assistant name (`Assistant`). Rows in scope `Assistant` are visible to `SystemAdmin` only; their `preview` is `{ title: assistant name, excerpt: the reported text }`, `targetState` the message status, and `author` the Deleted member summary (assistant replies have no member author).

`ModerationQueueDetail` (`GET .../queue/{targetType}/{targetId}`; the dismiss route returns it too):

```json
{
  "targetType": "Comment", "targetId": "cmt_...",
  "target": { "id": "cmt_...", "state": "Hidden", "...": "Comment, FeedPost, or UserSummary (full content, any state)" },
  "context": { "feedPostId": "fpo_...", "parentCommentId": null },
  "revisions": { "objects": [], "totalRecords": 0, "enumerate": "/api/v1/comments/cmt_.../revisions/enumerate" },
  "reports": { "objects": [ { "id": "rpt_...", "...": "ReportView" } ], "totalRecords": 3, "enumerate": "/api/v1/admin/moderation/queue/Comment/cmt_.../reports/enumerate" },
  "actions": { "objects": [ { "id": "mac_...", "action": "AutoHide", "moderator": null, "...": "ModerationActionView" } ], "totalRecords": 1, "enumerate": "/api/v1/admin/moderation/queue/Comment/cmt_.../actions/enumerate" },
  "authorHistory": { "removedCount90d": 1, "hiddenCount90d": 2, "reportsAgainst90d": 5, "isMuted": false, "mutedUntilUtc": null },
  "openReports": 3,
  "labels": [], "tags": {}
}
```

For an `AssistantMessage` (SystemAdmin only; anyone else `403 Forbidden`) `target` is the moderation snapshot:

```json
{ "id": "ams_...", "messageId": "acm_...", "reportId": "rpt_...", "reason": "Report", "conversationId": "acv_...", "conversationDeleted": true,
  "assistant": { "id": "ast_...", "name": "Helper", "deleted": false }, "owner": { "id": "usr_...", "...": "UserSummary" },
  "reportedMessage": { "id": "acm_...", "position": 1, "role": "Assistant", "content": "...", "status": "Completed", "isReported": true, "createdUtc": "...", "labels": [], "tags": {} },
  "model": { "endpointId": "mep_...", "endpointName": "Hosted chat", "model": "example-chat-model", "usedFallback": false },
  "citations": [ { "index": 1, "targetType": "FeedPost", "targetId": "fpo_...", "title": "Schedule", "path": "/feed/fpo_..." } ],
  "toolCalls": { "objects": [ { "id": "atc_...", "toolName": "searchFeed", "label": "Searching the feed", "completedLabel": "Searched the feed", "status": "Succeeded", "resultCount": 1, "durationMs": 42, "round": 1,
    "results": [ { "targetType": "FeedPost", "targetId": "fpo_...", "title": "Schedule", "path": "/feed/fpo_..." } ], "argumentsJson": "{\"query\":\"schedule\"}", "resultSummary": "- Schedule ...", "resultCharacters": 180, "error": null, "createdUtc": "..." } ], "totalRecords": 1 },
  "messages": { "objects": [ { "...": "AssistantSnapshotMessage" } ], "totalRecords": 2, "enumerate": "/api/v1/admin/moderation/queue/AssistantMessage/acm_.../snapshot/enumerate" },
  "createdUtc": "...", "expiresUtc": "...", "labels": [], "tags": {} }
```

`reason` is `Report`, `OutputBlocked`, `OutputFlagged`, or `InputFlagged`. `toolCalls` are the reported reply's tool calls copied into the snapshot (at most 12, with the administrator fields), so they stay readable after the member deletes the conversation. `GET .../queue/AssistantMessage/{messageId}/snapshot` and `POST .../snapshot/enumerate` (SystemAdmin) page the snapshot's messages oldest first (`CreatedAscending` default, `CreatedDescending`): `EnumerationResult<AssistantSnapshotMessage { id, position, role, content, status, isReported, createdUtc, labels, tags }>`. Only `Dismiss` applies to assistant messages (act on the assistant's configuration instead); `authorHistory` is all zeros. `AiMaintenanceWorker` deletes expired snapshots.

`revisions`, `reports`, and `actions` are bounded previews of at most 5 objects. Dismiss marks the target's Open reports in the caller's scope `Dismissed` without changing the content (unhide auto-hidden content with an action), records a `Dismiss` action, and writes `moderation.dismiss`.

**Reports collection** (`.../queue/{targetType}/{targetId}/reports`): orderings `CreatedDescending` (default) and `CreatedAscending`; filters `status` (`Open`, `ActionTaken`, `Dismissed`) and `reason`; search over details; `Report` labels and tags. `ReportView`:

```json
{ "id": "rpt_...", "reporter": { "id": "usr_...", "displayName": "Alex Example", "...": "UserSummary" }, "targetType": "Comment", "targetId": "cmt_...",
  "reason": "Spam", "details": "Link drop.", "status": "Open", "createdUtc": "2026-11-12T06:00:00Z", "dueUtc": "2026-11-12T18:00:00Z",
  "resolvedUtc": null, "resolvedByName": null, "resolutionNote": null, "moderationActionId": null, "labels": [], "tags": {} }
```

`reporter` is null for system reports (word filter Flag terms) and the Deleted member summary for deleted accounts.

**Member content collection** (`/api/v1/admin/moderation/content`, an author's history): orderings `CreatedDescending` (default), `CreatedAscending`, `ReactionTotalDescending`, `ReportCountDescending`; filters `authorUserId` (required, `fieldErrors["filters.authorUserId"]`), `targetType` (`FeedPost` or `Comment`), `state` (`Visible`, `Hidden`, `Removed`, `Deleted`); search over title and body; labels and tags filter each item's own metadata.

```json
{ "targetType": "FeedPost", "targetId": "fpo_...", "state": "Visible", "feedPostId": "fpo_...", "excerpt": "Meetup this Saturday.", "createdUtc": "2026-11-12T18:00:00Z", "reactionTotal": 12, "reportCount": 0, "labels": [], "tags": {} }
```

### Summary

`GET /api/v1/admin/moderation/summary` returns the admin Home tiles for the caller's scope: Open reports, Open reports past `moderation.reviewTargetHours`, and targets with Open reports.

```json
{ "open": 4, "overdue": 1, "openTargets": 3 }
```

## Admin: moderators (Admin)

Shortcuts over `PUT /api/v1/admin/members/{id}/roles` for the `FeedModerator` role (contract section 10.4). Changes take effect on the member's next request and are audited as `member.roles`.

| Method and path | Body | Success | Errors |
|---|---|---|---|
| `GET /api/v1/admin/moderators`, `POST /api/v1/admin/moderators/enumerate` | `EnumerationQuery` | `200 EnumerationResult<ModeratorListItem>` | `400 ValidationFailed`, `400 InvalidContinuationToken` |
| `POST /api/v1/admin/moderators` | `{ "userId": "usr_...", "resourceType": "Feed" }` (`resourceType` defaults to `Feed`, the only accepted value) | `200 ModeratorListItem` (idempotent) | `400 ValidationFailed`, `403 Forbidden`, `404 NotFound` (unknown or deleted member) |
| `DELETE /api/v1/admin/moderators/{userId}?resourceType=Feed` | | `204` (idempotent; past actions stay attributed) | `400 ValidationFailed`, `403 Forbidden`, `404 NotFound` |

Moderators collection: members holding `Moderator` or `FeedModerator` (not deleted). Orderings `DisplayNameAscending` (default), `CreatedDescending`, `CreatedAscending`; filters `role` (`Moderator`, `FeedModerator`) and `resourceType`; search over display name and handle; `User` labels and tags; exact totals.

```json
{ "user": { "id": "usr_...", "displayName": "Morgan Moderator", "handle": "morgan", "...": "UserSummary" },
  "roles": [ { "role": "FeedModerator", "resourceType": "Feed", "resourceId": "*" } ],
  "grantedUtc": "2026-11-01T12:00:00Z", "grantedByName": "Example Owner",
  "openInScope": 4, "actions30d": 18, "medianResponseMinutes30d": 42.5, "overdueHandled30d": 1, "labels": [], "tags": {} }
```

`roles` is fixed-bound (at most 20 assignments). Statistics cover the last 30 days; `medianResponseMinutes30d` is null without handled reports.

---

## Profile fields

Each property defines its own profile fields (`docs/MULTIPROPERTY_CONTRACT.md` section 9; `docs/TRELLISKIT.md` section 11). On first load the property's `property.json` `profileFields` list is seeded (or, without it, its version 1 `profile` section is converted: `rank`, `affiliation`, `city`, `region`, `country`, `goals`, `rankHistory`), together with four built-in fields (`displayName`, `handle`, `avatar`, `bio`), and existing values are backfilled from the Phase 1 columns. Afterwards the database is the source of truth; in `SingleProperty` a change of the folder's profile field section is merged again on the next start (new keys created, allowed attributes updated, nothing deleted; the folder wins on change).

### `ProfileField`

```json
{
  "id": "pfd_...", "key": "rank", "builtIn": false, "label": "Level", "helpText": "", "placeholder": "", "type": "Rank",
  "options": [ { "id": "pfo_...", "key": "Beginner", "label": "Beginner", "color": "#9CA3AF", "maxLevel": null, "freeText": false, "retired": false } ],
  "rank": { "levelLabel": "Stars", "maxLevel": 3 }, "subFields": [],
  "validation": { "required": true, "minLength": null, "maxLength": null, "minValue": null, "maxValue": null, "integerOnly": false, "pattern": null, "patternMessage": null, "minEntries": null, "maxEntries": null, "minDate": null, "maxDate": null, "notInFuture": false },
  "collectAt": "SignUp", "editableBy": "Member", "visibility": "Public", "showOnProfile": true, "profileOrder": 10, "showOnCard": true,
  "searchable": false, "filterable": true, "enabled": true, "valueCount": 1180, "labels": [], "tags": {},
  "createdUtc": "2026-11-11T20:00:00Z", "updatedUtc": "2026-11-11T20:00:00Z"
}
```

| Attribute | Rule |
|---|---|
| `key` | `^[a-z][A-Za-z0-9]{0,39}$`, unique per property case-insensitively; reserved: `id`, `email`, `displayName`, `handle`, `avatar`, `avatarUrl`, `bio`, `roles`, `flags`, `membership`, `profile`, `labels`, `tags`, `createdUtc`, `updatedUtc`, `isStaff`, `isDeleted`. Immutable while `valueCount > 0` |
| `type` | `Text`, `LongText`, `Number`, `Date`, `Boolean`, `Url`, `Email`, `Phone`, `Country`, `SingleSelect`, `MultiSelect`, `Rank`, `History` (`Image` is the built-in avatar only). Changeable only while `valueCount` is 0 |
| `options` | Select and rank types: 1 to 200; option `key` `^[A-Za-z0-9_-]{1,64}$`; `color` required for `Rank`; `maxLevel` (`Rank`, 0 to 20); `freeText` (select types); `retired` hides it from pickers while existing values stay valid |
| `subFields` | `History`: 1 to 10 `{ id, key, label, type: Text \| Number \| Date \| Boolean \| Url \| SingleSelect \| RankValue \| Member, required, maxLength, minValue, maxValue, notInFuture, options, rankFieldKey, withLevel, position }` (`pfs_` ids) |
| `collectAt`, `editableBy` | `SignUp` or `Profile` with `Member`; `AdminOnly` with `Staff`. `validation.required` only with `SignUp` or `Profile` |
| `visibility` | `Public`, `Members`, `Private` (the member and staff), `Staff` (requires `AdminOnly`) |
| `showOnCard` | At most `profileFields.maxCardFields` (3) enabled fields |
| `searchable` | `Text` and `LongText`; `filterable` not for `LongText`, `Url`, `Phone` |
| Limits | At most `profileFields.maxFields` (50, hard maximum 100) non-built-in fields (`409 FieldLimitReached`) |

### Routes (SystemAdmin or the admin token)

| Method and path | Body | Success | Errors |
|---|---|---|---|
| `GET /api/v1/admin/profile-fields`, `POST /api/v1/admin/profile-fields/enumerate` | `EnumerationQuery`; filters `type`, `collectAt`, `visibility`, `enabled`, `builtIn`, `filterable`, `showOnCard`; search over key and label; orderings `CreatedDescending`, `CreatedAscending`, `ProfileOrderAscending`, `LabelAscending` | `200 EnumerationResult<ProfileField>` | |
| `POST /api/v1/admin/profile-fields` | `ProfileField` without `id`, `builtIn`, `valueCount`, timestamps | `201 ProfileField` (appended to profile order unless `profileOrder` is given) | `400 ValidationFailed`, `409 FieldKeyInUse`, `409 FieldLimitReached` |
| `GET /api/v1/admin/profile-fields/{id}` | | `200 ProfileField` | `404` |
| `PUT /api/v1/admin/profile-fields/{id}` | any attributes; `options` and `subFields` replace their lists (entries with an existing `id` are updated); `validation` and `rank` replace as whole objects. Built-ins accept `label`, `helpText`, `placeholder` (avatar and bio also `collectAt`, `enabled`; bio also `visibility` Public, Members, or Private, `showOnProfile`, `profileOrder`) | `200 ProfileField` | `400`, `404`, `409 FieldHasValues` (key or type), `409 OptionInUse` (retire it instead; `fieldErrors` has `options.<key>` for each option in use), `409 BuiltInField`, `409 FieldKeyInUse` |
| `DELETE /api/v1/admin/profile-fields/{id}?deleteValues=true` | `{ "confirm": "<key>" }` (or `?confirm=`) when values exist | `202` (values deleted) or `204` (none) | `409 FieldHasValues` without `deleteValues`, `400 ValidationFailed` (confirmation, or a history field still references this rank field), `409 BuiltInField` |
| `POST /api/v1/admin/profile-fields/reorder` | `{ "order": ["pfd_...", ...] }`: every field id exactly once (at most 104) | `200 { order }`; `profileOrder` becomes index times 10 | `400` |
| `POST /api/v1/admin/profile-fields/preview` | `{ target: Onboarding \| ProfilePage \| Card \| Editor, viewer: Anonymous \| SignedIn \| Member \| Self \| Staff, fields?: [drafts], values?: { key: value } }`; drafts merge into the saved fields by key (a draft replaces the saved field with its key, new keys are added, other saved fields stay) | `200 { target, viewer, items: [{ key, builtIn, label, type, required, helpText, placeholder, control, display, value, formattedValue, options }] }` | `400` (invalid drafts) |
| `GET /api/v1/admin/profile-fields/{id}/stats`, `POST /api/v1/admin/profile-fields/{id}/stats/enumerate` | `200 EnumerationResult<ProfileOptionCount>`; context: valueCount, fillRate, updated30d | `404` |
| `PUT /api/v1/admin/members/{id}/profile` | `{ profile: { <key>: <value> \| null } }`: any field, `AdminOnly` and `Staff` included | `200` admin member detail | `400 ValidationFailed`, `400 ContentFiltered`, `404` |

Audit actions: `profileField.create`, `profileField.update`, `profileField.delete`, `profileField.reorder`, `member.profile` (staff edits) and `profile.update` (members' own edits); key names only, never values.

### Value shapes

| Type | JSON value | Validation |
|---|---|---|
| `Text` | string | trimmed; `minLength` to `maxLength` (default 200, cap 1000); optional `pattern` (`patternMessage`); word filter |
| `LongText` | string | `maxLength` default 2000, cap 10000; word filter |
| `Number` | number | finite; `minValue`, `maxValue`; `integerOnly`; at most 4 decimals |
| `Date` | `"YYYY-MM-DD"` | `minDate`, `maxDate`, `notInFuture` (UTC) |
| `Boolean` | `true` or `false` | |
| `Url` | string | absolute http or https, at most 2048 |
| `Email` | string | at most 254, one `@`, a dot in the domain |
| `Phone` | string | E.164 `^\+[1-9][0-9]{6,14}$` |
| `Country` | `"US"` | ISO 3166-1 alpha-2 (upper-cased) |
| `SingleSelect` | `{ "key": "Other", "otherText": "..." \| null }` (a bare key string is accepted) | a non-retired option (a retired key only when unchanged); `otherText` only for a `freeText` option, 1 to 100 |
| `MultiSelect` | `{ "keys": [...], "otherText": null }` | distinct non-retired keys; `minEntries`, `maxEntries` |
| `Rank` | `{ "key": "Intermediate", "level": 2 }` | a non-retired option; `level` 0 to the option's `maxLevel` (else `rank.maxLevel`) |
| `History` | `[ { "id": "phe_..." \| null, "values": { "<subKey>": <value> } } ]` | `maxEntries` (default 12); sub-fields validated (`RankValue` `{ key, level? }` against the referenced rank field; `Member` `{ name, userId? }` with `userId` a member of the same property); ids must be the member's own entries |

`null` clears a value; empty strings count as `null`. Errors are keyed `profile.<key>` or `profile.<key>[<index>].<subKey>` with member-facing messages.

### Writing values

In `PUT /api/v1/me/profile` (`profile` map) the per-key errors are: "Unknown field." (unknown key, or a `Staff` field), "This field is not available." (disabled), "This field can only be changed by staff." (`editableBy: Staff`), "This field is required." (clearing a required field). Staff writes through `PUT /api/v1/admin/members/{id}/profile` skip the `editableBy` and `Staff` checks. Writes replace the member's rows of the written fields in one transaction and recompute `valueCount`; while `profileFields.legacyFieldsMode` is not `Off` they also update the deprecated columns (dual writes).

### Payloads and visibility

| `visibility` | Anonymous | SignedIn | Member | Self | Staff |
|---|---|---|---|---|---|
| `Public` | yes | yes | yes | yes | yes |
| `Members` | no | no | yes | yes | yes |
| `Private` | no | no | no | yes | yes |
| `Staff` | no | no | no | no | yes |

| Payload | `profile` |
|---|---|
| `Me` | every enabled field visible to Self, every key present (`null` when unset) |
| `GET /api/v1/users/{id}` | fields visible to the caller with `showOnProfile` and a value |
| `UserSummary` (authors, mentions, reactions, notifications, member search, directory) | fields visible to the viewer with `showOnCard` and a value, at most `profileFields.maxCardFields` (signed-out surfaces use the Anonymous viewer; member surfaces the Member viewer) |
| Admin member list | as `UserSummary` with the staff viewer; admin member detail: every enabled field, every key |
| Account export | every value, disabled fields included, except `Staff` fields |

### Member filters

`POST /api/v1/members/directory/enumerate` (and `GET /api/v1/members/directory`; Member) returns `EnumerationResult<UserSummary>` with `labels` and `tags` for active accounts (not banned or deleted) that finished their profile (a handle is set, so unfinished sign-ups are never listed); when the access model requires a membership to use the directory (`FullyGated`, or `PartiallyGated` with `directory` or `members` gated) it lists only accounts with an active membership or a staff role ([ACCESS_MODELS_CONTRACT.md](docs/ACCESS_MODELS_CONTRACT.md)); orderings `CreatedDescending`, `CreatedAscending`, `DisplayNameAscending`; `search` matches display names and handles (contains) and word prefixes of `searchable` fields the caller can see. `POST /api/v1/admin/members/enumerate` accepts the same `filters.profile` on every enabled, filterable field.

```json
{ "maxResults": 25, "search": "pat", "labels": ["coaches"],
  "filters": { "profile": { "rank": { "minRank": "Intermediate", "minLevel": 1 }, "country": { "in": ["US", "CA"] }, "affiliation": { "prefix": "example" }, "rankHistory": { "hasValue": true } } } }
```

GET form: `profile.<key>.<operator>=<value>` (repeatable for `in`, `any`, `all`), for example `?profile.country.in=US&profile.country.in=CA`.

| Operator | Types | Meaning |
|---|---|---|
| `eq` | `Text`, `Email` (case-insensitive), `Number`, `Boolean`; `Country`, `SingleSelect` (one key, same as a one-value `in`) | equals |
| `prefix` | `Text` | normalized value starts with (1 to 100 characters) |
| `gte`, `lte` | `Number`, `Date` | inclusive bounds |
| `in` | `Country`, `SingleSelect`, `Rank` | one of up to 20 keys |
| `any`, `all` | `MultiSelect` | holds at least one, or all, of up to 20 keys |
| `minRank`, `maxRank` | `Rank` | at or above (or below) an option's position |
| `minLevel` | `Rank` | level at least |
| `hasValue` | every type | has (or has no) value |

At most `profileFields.maxFiltersPerQuery` (10) filters. An unknown, disabled, non-filterable, or hidden field, or an operator its type does not allow, is `400 ValidationFailed` with `fieldErrors["filters.profile.<key>"] = "Unknown filter."` (the same message for every reason).

---

## Administrative email

TrellisKit sends a complete set of administrative emails per property (`docs/TRELLISKIT.md` section 12). Each message is a template rendered with HtmlPageBuilder into a complete, inline-styled HTML document (light and dark mail clients, fluid to 600 px) plus a plain-text version, in the property's brand (logo, primary color, name, support email, postal address, privacy and email preference links); every token value is HTML-encoded. Messages are queued as `EmailMessage` records (`eml_`), sent at once and retried with backoff by the delivery worker (`email.deliveryMaxAttempts`, `email.deliveryIntervalSeconds`), idempotent per event, and never sent to a suppressed address. Bodies are not stored: the recipient tokens are kept encrypted (engine secrets key) only until the message is final.

**Provider per property** (`email.provider`, default `Auto`): Mailgun (SendWithMailgun) when the property's Mailgun domain and API key are set, else the property's own SMTP server, else (`SingleProperty` only) the deployment's SMTP settings (Mailpit locally), else the log. In `MultiProperty` no property uses the deployment's account. Per-property Mailgun credentials are set only through the property settings routes below (encrypted with `TK_SECRETS_ENCRYPTION_KEY`), never from per-property environment variables. SendWithMailgun sends the HTML body only (it has no plain-text alternative, custom header, or reply-to parameter), so `List-Unsubscribe` headers and reply-to apply to SMTP delivery only.

**Catalog** (template keys): member essential `welcome`, `verify_email`, `password_reset`, `password_changed`, `sign_in_method_linked`, `sign_in_method_removed`, `email_changed`, `membership_started`, `renewal_succeeded`, `payment_failed`, `subscription_canceled`, `membership_expired`, `refund_revoked`, `free_membership_granted`, `code_expiry_reminder`, `code_revoked`, `account_restricted`, `restriction_lifted`, `content_removed`, `account_deletion_requested`, `account_deleted`, `export_ready`, `test`; staff `staff_new_member`, `staff_new_subscription`, `staff_cancellation`, `staff_payment_failure`, `staff_refund`, `staff_integration_failure` (rate-limited by `email.integrationAlertMinutes`); moderators `staff_new_report`, `staff_content_auto_hidden`, `staff_moderation_overdue` (their `{targetType}` and `{reason}` tokens are human labels such as `feed post`, `forum thread`, `member profile`, `AI assistant reply`, and `Hate speech`, never enum names); platform `platform_admin_invite`. Optional, governed by notification preferences: `feed_post` (new feed post), `forum_digest`, and the other digests and notices. New feed post emails are queued through this system like every other template (overridable, logged, retried, suppressed).

### Property settings (SystemAdmin)

| Method and path | Body | Success | Errors |
|---|---|---|---|
| `GET /api/v1/admin/property/settings`, `POST /api/v1/admin/property/settings/enumerate` | `200 EnumerationResult<PropertySettingField>`; context: propertyId | |
| `PUT /api/v1/admin/property/settings` | `{ values: { key: value }, clear: [keys] }`; all or nothing; null or empty clears | `200 { propertyId, fields }`; audited `property.settings.update` (key names only) | `400 ValidationFailed` (`fieldErrors["values.<key>"]`) |
| `POST /api/v1/admin/property/integrations/{name}/test` | `{ to? }`; `name`: `email` | `200 { ok, message }` (sends the `test` template to the caller or `to`); other names report `ok: false` | `400` |

Keys (section Email): `email.provider` (`Auto`, `Mailgun`, `Smtp`, `Log`), `email.fromName`, `email.fromAddress` (empty uses `property.json` `contact`), `email.replyTo` (empty uses the support email), `email.postalAddress` (footer; empty uses `email/layout.json` `postalAddress`), `email.trackOpens` and `email.trackClicks` (Boolean, default `false`; Mailgun open and click tracking for Optional mail only, never Essential or Staff mail), `email.mailgunDomain`, `email.mailgunApiKey` (Secret), `email.mailgunRegion` (`Us`, `Eu`), `email.mailgunWebhookSigningKey` (Secret), `email.validationEnabled` (Mailgun address validation at sign-up: high risk or undeliverable addresses get `400 ValidationFailed` on `email`; unknown results and errors allow), `email.mailgunValidationApiKey` (Secret; empty uses the sending key), `email.smtpHost`, `email.smtpPort`, `email.smtpUsername`, `email.smtpPassword` (Secret), `email.smtpUseTls`.

Keys (section Usage): `usage.timeZone` (an IANA time zone id, default `UTC`; validated) and `usage.weekStartsOn` (`Monday` default, any day name) set the property's usage calendar (docs/USAGE_METERING_CONTRACT.md section 3.1). Deployment settings (section Usage of `/api/v1/settings`): `usage.enabled` (`TK_USAGE_ENABLED`), `usage.rollupIntervalMinutes`, `usage.closeAfterHours`, `usage.hourlyRetentionDays`, `usage.dailyRetentionDays` (0 keeps daily rows forever), `usage.activityRetentionDays`, `usage.backfillDaysPerPass`, `usage.storageBatchSize`, `usage.storageReconcileDays`, `usage.recordRequests` (`TK_USAGE_RECORD_REQUESTS`).

### Email admin (SystemAdmin)

| Method and path | Body | Success | Errors |
|---|---|---|---|
| `GET /api/v1/admin/email/templates`, `POST /api/v1/admin/email/templates/enumerate` | `200 EnumerationResult<EmailTemplateView>` | |
| `GET /api/v1/admin/email/templates/{key}` | | `200` the template | `404` |
| `PUT /api/v1/admin/email/templates/{key}` | `{ subject?, preheader?, heading?, body? }` with `{token}` placeholders from `tokens`; subject, preheader, heading one line of at most 200; body paragraphs separated by blank lines, at most 5000; all null resets | `200` the template; audited `email.template.update` | `400 ValidationFailed` (unknown tokens named), `404` |
| `DELETE /api/v1/admin/email/templates/{key}` | | `204` (engine default again) | `404` |
| `POST /api/v1/admin/email/templates/{key}/preview` | `{ subject?, preheader?, heading?, body?, tokens? }` | `200 { subject, preheader, html, text }` with sample data | `400`, `404` |
| `POST /api/v1/admin/email/test` | `{ template?, to? }` | `200 { ok, message }`; audited `email.test` | `400` |
| `POST /api/v1/admin/email/test/catalog` | `{ to? }` (default the caller's address) | `200 { to, provider, sent, failed, results: [{ template, ok, message }] }`: every template the portal lists, sent once with sample data through the property's provider (the local stack delivers to Mailpit, http://127.0.0.1:8025); bounded by the catalog; audited once `email.test_catalog` | `400` |
| `GET /api/v1/admin/email/staff-events`, `POST /api/v1/admin/email/staff-events/enumerate` | `200 EnumerationResult<EmailStaffEventView>` | |
| `PUT /api/v1/admin/email/staff-events/{template}` | `{ enabled?, recipients? (at most 10; empty uses the defaults: SystemAdmins, or SystemAdmins and feed moderators) }` | `200` the event; audited `email.staff_event.update` | `400`, `404` |
| `GET /api/v1/admin/email/messages`, `POST .../messages/enumerate` | `EnumerationQuery`; filters `status` (`Queued`, `Sent`, `Delivered`, `Failed`, `Suppressed`), `template`, `category` (`Essential`, `Optional`, `Staff`), `recipient`, `userId`; search over recipient and subject; orderings `CreatedDescending`, `CreatedAscending`, `UpdatedDescending` | `200 EnumerationResult<EmailMessage { id, template, category, recipient, userId, subject, status, provider, providerMessageId, attempts, nextAttemptUtc, lastError, createdUtc, sentUtc, deliveredUtc, failedUtc, updatedUtc, labels, tags }>` | |
| `GET /api/v1/admin/email/messages/{id}` | | `200 EmailMessage` | `404` |
| `GET /api/v1/admin/email/suppressions`, `POST .../suppressions/enumerate` | `EnumerationQuery`; filter `reason` (`Bounce`, `Complaint`, `Unsubscribed`, `Manual`); search over email and note; orderings add `EmailAscending` | `200 EnumerationResult<EmailSuppression { id, email, reason, source, note, createdUtc, labels, tags }>` | |
| `POST /api/v1/admin/email/suppressions` | `{ email, note?, labels?, tags? }` | `201 EmailSuppression`; audited `email.suppression.add` | `400` |
| `DELETE /api/v1/admin/email/suppressions/{id}` | | `204`; audited `email.suppression.remove` | `404` |

### Mailgun webhook (anonymous; not called by clients)

`POST /api/v1/webhooks/mailgun/{propertyId}` receives `delivered`, `failed`, `complained`, and `unsubscribed` events. The signature (`signature.timestamp`, `signature.token`, `signature.signature`: HMAC-SHA256 of timestamp plus token with the property's `email.mailgunWebhookSigningKey`) is verified, the timestamp must be within 15 minutes, and a token is accepted once. The message record is found by `event-data.message.headers.message-id`, else by the `tk-message-id` user variable the engine sends with every message (only when `event-data.recipient` matches the record). `delivered` marks the record `Delivered`; a permanent `failed`, `complained`, and `unsubscribed` suppress the address (a permanent failure also marks the record `Failed`). `200 { message: ok | ignored | unknown_message }`; `400 InvalidSignature` or `InvalidJson`; `404 PropertyNotFound`; `503 IntegrationNotConfigured` without a signing key.

---

## Admin: property domains (SystemAdmin)

Property administrators manage their own domains and app ids in both modes (resolved decision 6). Hosts and app ids are unique across the deployment. A domain a property administrator adds is not routed until it passes a DNS TXT ownership check; domains registered by platform administrators or the property folder are verified at once.

| Method and path | Body | Success | Errors |
|---|---|---|---|
| `GET /api/v1/admin/property/domains`, `POST /api/v1/admin/property/domains/enumerate` | `200 EnumerationResult<PlatformDomainResponse>`; context: propertyId | |
| `PUT /api/v1/admin/property/domains` | `{ domains?: [{ host, kind }], apps?: [{ platform, appId, storeAppId? }] }`; a provided list replaces the stored one; unchanged hosts keep their state, new hosts start unverified | `200` the `GET` body; audited `property.domains.update` (and in the platform audit) | `400 ValidationFailed`, `409 DomainInUse` (`fieldErrors["domains[i].host"]` per offending row), `409 AppIdInUse` (`fieldErrors["apps[i].appId"]`) |
| `POST /api/v1/admin/property/domains/{domainId}/verify` | | `200` the domain: `verifiedUtc` set when the TXT record `verification.recordName` (`_trelliskit-verify.<host>`) holds `verification.recordValue` (`trelliskit-verify=<token>`); otherwise `verification.error` explains; audited `property.domain.verify` | `404` |

TXT records are read through the DNS-over-HTTPS endpoint `deployment.domainVerificationResolverUrl` (default Cloudflare). In `SingleProperty`, a change of `property.json` `domains` on disk replaces the list again (the folder wins on change).

---

## Admin: branding and the AI switch (SystemAdmin)

The brand palette and the AI switch are stored per property, seeded from `property.json` (`brand.colors`, `features.ai`), and edited here (spec: `docs/TRELLISKIT.md` sections 2.1 and 9.5). SystemAdmin, the admin token, or a delegated platform session; the platform routes below edit any property. Every change applies to `GET /api/v1/property` at once (its `ETag` changes; other instances within `deployment.propertyCacheSeconds`), and no restart or rebuild is needed. In `SingleProperty` a changed `property.json` section (`brand.colors` or `features.ai`) replaces the stored value on the next start (the folder wins on change; audited `property.branding.sync` and `property.ai.sync`); otherwise admin edits stand.

| Method and path | Body | Success | Errors |
|---|---|---|---|
| `GET /api/v1/admin/property/branding` | | `200 BrandingView` | |
| `PUT /api/v1/admin/property/branding` | `{ light?: { <key>: "#RRGGBB" }, dark?: { ... }, contrastOverrides?: [{ scheme: light \| dark, pair, reason? }], note? }`; keys left out keep their value; `#RGB` is expanded | `200 BrandingView` with a new `version` (source `Edit`); audited `property.branding.update`; each accepted override is audited `property.branding.contrastOverride` with a warning naming the pair and its ratio. A body that changes nothing stores nothing | `400 ValidationFailed`: `fieldErrors["light.<key>"]` (not a hex color, unknown key), `fieldErrors["contrast.<scheme>.<pair>"]` for every pair below WCAG 2.1 AA that is not overridden (the message names both colors, the ratio, and the minimum), `contrastOverrides[i].scheme` / `.pair` / `.reason`, `note` (over 200 characters); `409 Conflict` (concurrent edit) |
| `GET /api/v1/admin/property/branding/versions`, `POST /api/v1/admin/property/branding/versions/enumerate` | `EnumerationQuery`; orderings `VersionDescending` (default), `VersionAscending`, `CreatedDescending`, `CreatedAscending`; filter `source`; search over note and source. Label and tag filters are `400 ValidationFailed` (versions carry none) | `200 EnumerationResult<BrandingVersion { version, source, note, light, dark, failingPairs, overrideCount, isCurrent, createdByUserId, createdUtc, labels, tags }>` (at most `property.maxPaletteVersions` retained; `labels` and `tags` always empty) | `400 ValidationFailed`, `400 InvalidContinuationToken` |
| `PUT /api/v1/admin/property/branding/member-presets` | `{ mode: All \| None \| Selected, presetIds?: [<preset id>] }` (`presetIds` at least one catalog id with `Selected`, ignored otherwise; duplicates removed; stored in catalog order) | `200 BrandingView` (its `memberPresets` changed); audited `property.branding.memberPresets` when it changes; applies to `GET /api/v1/property` `brand.presets` at once (its `ETag` changes). The property palette is always the members' default | `400 ValidationFailed` (`mode`, `presetIds`, `presetIds[i]` unknown preset) |
| `POST /api/v1/admin/property/branding/revert` | `{ to: Previous \| Seed \| Version, version?, note? }` (`version` required with `Version`: any retained version) | `200 BrandingView` with a new version (source `RevertPrevious`, with the restored version's overrides, `RevertVersion` for a named version, also with its overrides, or `RevertSeed`); audited `property.branding.revert` (pairs below AA are listed as a warning) | `400 ValidationFailed` (`to`, `version`), `404 NotFound` (the version is not retained), `409 NoPreviousVersion`, `409 AlreadyCurrent` (the named version is the current one) |
| `GET /api/v1/admin/property/ai` | | `200 PropertyAiView` | |
| `PUT /api/v1/admin/property/ai` | `{ enabled: bool }` | `200 PropertyAiView`; audited `property.ai.update` when it changes. Off keeps endpoints, assistants, and conversations; AI routes answer `404 NotAvailable` and AI workers pause until it is on | `400 ValidationFailed` (`enabled` missing) |

Brand color keys (both schemes): `primary`, `primaryHover`, `onPrimary`, `accent`, `onAccent`, `accentSoft`, `onAccentSoft` (text and icons on `accentSoft`: selected chips, active navigation, avatar initials; defaults to `primary` when `property.json` leaves it out), `themeColor`. Contrast pairs, checked in each scheme (`background` is the engine page background, light `#FFFFFF`, dark `#0E141D`):

| Pair | Level | Minimum | Used for |
|---|---|---|---|
| `onPrimary/primary` | Text | 4.5:1 | Button labels |
| `onPrimary/primaryHover` | Text | 4.5:1 | Button labels on hover |
| `onAccent/accent` | Text | 4.5:1 | Text on accent surfaces |
| `onAccentSoft/accentSoft` | Text | 4.5:1 | Selected chips, active navigation, avatar initials |
| `primary/background` | Text | 4.5:1 | Links and primary text on the page |
| `primary/accentSoft` | Component | 3:1 | Selected chip borders on the soft accent |
| `accent/background` | Component | 3:1 | Accent surfaces against the page |

Ratios are WCAG 2.1 contrast ratios, shown rounded to two decimals but never rounded up to a minimum they miss. Seeded palettes (from `property.json`, on first start, a folder sync, or a revert to the seed) are not refused: failing pairs are logged as warnings and shown failing on the Branding page. At most `property.maxPaletteVersions` versions are kept (default 20; the current and the previous version always).

```json
{
  "propertyId": "app_mwczw7mg_F8RsDcNidltPWZS1seB", "version": 3, "source": "Edit", "note": "Darker blue",
  "light": { "primary": "#1A4FA0", "primaryHover": "#194D9C", "onPrimary": "#FFFFFF", "accent": "#1F2933", "onAccent": "#FFFFFF", "accentSoft": "#E6EEF9", "onAccentSoft": "#1F5FBF", "themeColor": "#1F2933" },
  "dark": { "primary": "#8DB4F0", "...": "..." },
  "keys": ["primary", "primaryHover", "onPrimary", "accent", "onAccent", "accentSoft", "onAccentSoft", "themeColor"],
  "backgrounds": { "light": "#FFFFFF", "dark": "#0E141D" },
  "contrast": [ { "scheme": "light", "pair": "onPrimary/primary", "foreground": "onPrimary", "background": "primary", "foregroundColor": "#FFFFFF", "backgroundColor": "#1A4FA0", "ratio": 7.61, "required": 4.5, "level": "Text", "passes": true, "overridden": false, "usage": "Button labels" } ],
  "overrides": [ { "scheme": "light", "pair": "primary/accentSoft", "ratio": 2.8, "required": 3, "reason": "Brand review pending" } ],
  "seed": { "version": null, "source": null, "createdUtc": null, "light": { "...": "..." }, "dark": { "...": "..." } },
  "previous": { "version": 2, "source": "Edit", "createdUtc": "2026-11-12T10:00:00Z", "light": { "...": "..." }, "dark": { "...": "..." } },
  "presets": [ { "id": "ocean", "name": "Ocean", "description": "Clear ocean blue with a deep cyan accent. Fresh and open.", "light": { "primary": "#0B5FC4", "...": "..." }, "dark": { "...": "..." } } ],
  "memberPresets": { "mode": "Selected", "presetIds": ["graphite", "ocean"], "allowedPresetIds": ["graphite", "ocean"], "revision": 2, "updatedByUserId": "usr_...", "updatedUtc": "2026-11-13T09:00:00Z" },
  "syncsFromFolder": true, "createdByUserId": "usr_...", "createdUtc": "2026-11-12T11:00:00Z"
}
```

Palette presets: `presets` is the engine catalog (at most 32; every preset passes every pair above in both schemes without an override), defined once in `backend/src/TrellisKit.Core/Assets/palette-presets.json`. Applying one on the Branding page is an ordinary `PUT .../branding` of its colors (a new version; revert and history work as for any edit). `memberPresets` says which presets members may choose in Settings, Appearance (`All` by default, `None`, or `Selected`); members always have the property palette as their default, and their choice is `PUT /api/v1/me/appearance` ([Account](#account-signed-in)).

`PropertyAiView`: `{ propertyId, enabled, deploymentEnabled (ai.enabled), available (both; what features.ai reports), seedEnabled (property.json), syncsFromFolder, revision, updatedByUserId, updatedUtc }`.

## Property documents

A property declares its documents (terms, privacy policy, guidelines, waivers, refund and subscription terms, informational pages) in `property.json` `documents` (spec: `docs/TRELLISKIT.md` section 14). The engine seeds a version of each from its Markdown file, serves it, and records members' acceptances. Gating documents (`acceptance` `RequiredAtSignup` or `AcceptedWithTerms`) are required at sign-up; when a version that requires re-acceptance is published, signed-in members get `403 DocumentAcceptanceRequired` until they accept it.

| Method and path | Group | Body | Success | Errors |
|---|---|---|---|---|
| `GET /api/v1/public/documents/{key}` | Anonymous | | `200 Document` (current version; `Cache-Control: public, max-age=60`) | `404 NotFound` |
| `POST /api/v1/me/documents/accept` | Signed in | `{ acceptTerms?, documents: [{ key, version }] }` | `200 Me`; method `Reacceptance` when an earlier version was accepted, otherwise `Signup`; audited `document.accept` | `400 ValidationFailed` (`fieldErrors["documents.<key>"]`: unknown or stale) |
| `GET /api/v1/me/documents/acceptances`, `POST .../enumerate` | Signed in | `EnumerationQuery` (orderings `AcceptedDescending` default, `AcceptedAscending`, `CreatedDescending`, `CreatedAscending`; filter `documentKey`) | `200 EnumerationResult<DocumentAcceptance>` (the caller's own) | `400 ValidationFailed` |
| `GET /api/v1/admin/documents`, `POST /api/v1/admin/documents/enumerate` | Admin | `200 EnumerationResult<AdminDocumentView>`; context: syncsFromFolder | `403 Forbidden` |
| `GET /api/v1/admin/documents/{key}` | Admin | | `200 AdminDocument` with `content` | `404 NotFound` |
| `GET /api/v1/admin/documents/{key}/versions`, `POST .../enumerate` | Admin | `EnumerationQuery` (orderings `SequenceDescending` default, `CreatedDescending`, `CreatedAscending`; search over version, title, change summary) | `200 EnumerationResult<DocumentVersion>` (content omitted) | `404 NotFound` |
| `GET /api/v1/admin/documents/{key}/versions/{versionId}` | Admin | | `200 DocumentVersion` with `content` | `404 NotFound` |
| `POST /api/v1/admin/documents/{key}/versions` | Admin | `{ version (1 to 32, new for the document), content (1 to property.maxDocumentCharacters), changeSummary (1 to 500), title? (1 to 120), requireReacceptance? (gating documents; default reacceptOnChange) }` | `201 AdminDocument` (source `Edit`); audited `document.publish` | `400 ValidationFailed`, `404 NotFound`, `409 VersionExists`, `409 Conflict` |
| `GET /api/v1/admin/documents/acceptances`, `POST .../enumerate` | Admin | `EnumerationQuery` (orderings as above; filters `documentKey`, `userId`, `version`, `method`, `client`; search over member display name, email, and key) | `200 EnumerationResult<DocumentAcceptance>` with `user` | `400 ValidationFailed` |
| `GET /api/v1/platform/properties/{propertyId}/documents` (and `/{key}`, `/{key}/versions[/enumerate]`, `/{key}/versions/{versionId}`, `POST /{key}/versions`, `/acceptances[/enumerate]`) | Platform | Same as the admin routes | Same, for any property; a publish is audited in both audits | `401 Unauthorized`, `404 NotFound` |
| `GET /api/v1/platform/properties/{propertyId}/documents/acceptances` | Same as /api/v1/admin/documents/acceptances, for any property. Platform administrator token (Authorization: Bearer tkp_...) or the break-glass admin token; platform host only. | `200` List a property's document acceptances | `400` |  |
| `GET /api/v1/platform/properties/{propertyId}/documents/{key}/versions/{versionId}` | Same as GET /api/v1/admin/documents/{key}/versions/{versionId}, for any property. Platform administrator token (Authorization: Bearer tkp_...) or the break-glass admin token; platform host only. | `200` Read a property document version | `401`, `404` |  |
| `GET /api/v1/platform/properties/{propertyId}/documents/{key}/versions` | Same as /api/v1/admin/documents/{key}/versions, for any property. Platform administrator token (Authorization: Bearer tkp_...) or the break-glass admin token; platform host only. | `200` List a property document's versions | `400` |  |
| `GET /api/v1/platform/properties/{propertyId}/documents/{key}` | Same as GET /api/v1/admin/documents/{key}, for any property. Platform administrator token (Authorization: Bearer tkp_...) or the break-glass admin token; platform host only. | `200` Read a property's document | `401`, `404` |  |
| `POST /api/v1/platform/properties/{propertyId}/documents/acceptances/enumerate` | Same as /api/v1/admin/documents/acceptances, for any property. Platform administrator token (Authorization: Bearer tkp_...) or the break-glass admin token; platform host only. | `200` List a property's document acceptances | `400` |  |
| `POST /api/v1/platform/properties/{propertyId}/documents/{key}/versions/enumerate` | Same as /api/v1/admin/documents/{key}/versions, for any property. Platform administrator token (Authorization: Bearer tkp_...) or the break-glass admin token; platform host only. | `200` List a property document's versions | `400` |  |
| `POST /api/v1/platform/properties/{propertyId}/documents/{key}/versions` | Same as POST /api/v1/admin/documents/{key}/versions, for any property. Body { version (1 to 32 characters, new for the document), content (Markdown, 1 to property.maxDocumentCharacters), changeSummary (1 to 500), title? (1 to 120), requireReacceptance? (gating documents only; default the document's reacceptOnChange) }. Stores a new current version (source Edit); with requireReacceptance, signed-in members who accepted an earlier version get 403 DocumentAcceptanceRequired until they accept it. In SingleProperty a later change to the document on disk replaces it again (the folder wins on change). Audited document.publish. Platform administrator token (Authorization: Bearer tkp_...) or the break-glass admin token; platform host only. | `201` Publish a property document version | `400`, `401`, `404`, `409` |  |

Admin routes: SystemAdmin, the admin token, or a delegated platform session. `GET /api/v1/property` adds `documents: DocumentSummary[]` with the live current versions (its `ETag` changes on publish). The account export adds `documentAcceptances`. Acceptances are kept after account deletion.

```json
{
  "key": "participation-agreement", "title": "Participation Agreement", "kind": "Waiver", "acceptance": "RequiredAtSignup",
  "separateCheckbox": true, "reacceptOnChange": true, "slug": "participation-agreement", "footer": true, "description": null,
  "version": "2.0", "sequence": 2, "updatedUtc": "2026-11-12T12:00:00Z", "labels": [], "tags": {},
  "content": "# participation agreement ...", "changeSummary": "Clarified section 2."
}
```

| Shape | Fields |
|---|---|
| `DocumentSummary` | `key`, `title`, `kind` (`Terms`, `Privacy`, `Guidelines`, `Refund`, `Subscription`, `Waiver`, `AccountDeletion`, `Other`), `acceptance` (`RequiredAtSignup`, `AcceptedWithTerms`, `ShownAtPurchase`, `InformationOnly`), `separateCheckbox`, `reacceptOnChange`, `slug`, `footer`, `description`, `version`, `sequence`, `updatedUtc`, `labels`, `tags` |
| `Document` | `DocumentSummary` plus `content` (Markdown) and `changeSummary` |
| `AdminDocument` | `DocumentSummary` plus `source` (`Seed`, `Edit`, `DirectorySync`, or `None` before a first version), `versionId`, `versionCount`, `acceptedCurrentCount`, `acceptedAnyCount` (distinct members), `requiredSequence` (the lowest sequence a member must have accepted; 0 for documents that are not accepted), `syncsFromFolder`, `createdByUserId`, `changeSummary`, and `content` on single reads |
| `DocumentVersion` | `id` (`dcv_`), `documentKey`, `version`, `sequence`, `title`, `changeSummary`, `requiresReacceptance`, `source`, `contentLength`, `createdByUserId`, `createdUtc`, `acceptanceCount`, `content` (single reads), `labels`, `tags` |
| `DocumentAcceptance` | `id` (`dac_`), `documentKey`, `documentTitle`, `version`, `sequence`, `versionId`, `userId`, `user` (staff views), `method` (`Signup`, `Reacceptance`), `client` (`Web`, `Ios`, `Android`, `Other`, from `X-Client` and `X-Client-Platform`), `acceptedUtc`, `createdUtc`, `labels`, `tags` |

---

## Access models

Each property runs `Open` (no payments), `PartiallyGated` (optional membership, gated features, per-object audience), or `FullyGated` (required membership). The binding rules, the route-by-model table, and the client changes are in `docs/ACCESS_MODELS_CONTRACT.md`; this section lists the shapes.

| Method and path | Body | Success | Errors |
|---|---|---|---|
| `GET /api/v1/admin/property/access` | | `200 AccessPolicy` | |
| `PUT /api/v1/admin/property/access` | `{ model?: Open \| PartiallyGated \| FullyGated, anonymousBrowsing?: bool, gatedFeatures?: [feed \| members \| forums \| library \| events \| directory \| messaging \| assistants \| search], defaultAudience?: Public \| SignedIn \| Members \| null }`; omitted fields stay | `200 AccessPolicy`; applies at once (other instances within `deployment.propertyCacheSeconds`); audited `property.access.update`; subscriptions and entitlements never change | `400 ValidationFailed` (`fieldErrors.model`: a paid model without a membership product; `gatedFeatures`: unknown or duplicate keys; `defaultAudience`: `Members` sent in `Open`). A default audience the model cannot use is coerced: `FullyGated` stores none (always `Members`), and switching to `Open` drops a stored `Members` |

SystemAdmin, the admin token, or a delegated platform session.

```json
{ "model": "PartiallyGated", "anonymousBrowsing": true, "gatedFeatures": ["library", "messaging"], "defaultAudience": "SignedIn", "purchasesEnabled": true, "signupCodesEnabled": true, "revision": 3, "configuredDefaultAudience": "SignedIn", "updatedByUserId": "usr_...", "updatedUtc": "2026-11-11T20:00:00Z" }
```

`GET /api/v1/property` carries the same object as `access` (with the effective `anonymousBrowsing`, `gatedFeatures` empty outside `PartiallyGated`, and the bookkeeping fields null). The stored configuration is seeded from `property.json` `access` (absent means `FullyGated`); in `SingleProperty` a changed folder section wins on the next start (audited `property.access.sync`).

| Behaviour | `FullyGated` | `PartiallyGated` | `Open` |
|---|---|---|---|
| Content reads and lists | Member | Signed in (signed out for feed reads when `anonymousBrowsing`), then the object's `audience` | Same as PartiallyGated |
| Content writes and participation | Member | Member in a gated feature, else signed in | Signed in |
| `/users/{id}`, mention search (`/members`, `/members/search`, `/members/enumerate`) | Member | Member when `members` is gated, else signed in | Signed in |
| Member directory (`/members/directory*`) | Member | Member when `directory` or `members` is gated, else signed in | Signed in; `404 NotAvailable` when `features.directory` is false |
| Global search (`/search`, `/search/enumerate`) | Member | Member when `search` is gated (independently of `forums`), else signed in; results still follow each object's rules (threads of gated forums stay hidden from non-members) | Signed in; `404 NotAvailable` in every model when `features.search` is false |
| Notifications, reports, blocks, link previews, labels, tags, metadata, media uploads | Member | Signed in | Signed in |
| `Members` objects for a signed-in non-member | n/a | Locked stub in lists (`locked: true`), `402` on direct reads | Readable (treated as `SignedIn`) |
| Membership, purchases, sign-up codes, comps, new codes | Available | Available | `404 NotAvailable`; webhooks still processed |
| New post notifications, push, email | Members | Attested accounts that can read the post | Every attested account |

`FeedPost` adds `locked` and `viewer.canOpen`, `viewer.requires`. A locked stub has empty `body`, `excerpt`, `attachments`, `mentions`, reactions, and null `linkPreview`.

---

## Forums (Member)

Phase 3 (docs/PHASE3_CONTRACT.md). Forums hold threads of four types: `Discussion`, `Question` (an accepted answer), `Poll`, and `Feedback` (a video review request). Every list is an `EnumerationQuery` in and `EnumerationResult<T>` out with labels and tags. All routes answer `404 NotAvailable` when the property turns forums off (`property.json` `features.forums`; `GET /api/v1/property` reports `features.forums` and `features.search`). Rate limits are the `forums.*`, `polls.*`, and `search.*` settings below. A property with `forums.seedDefaultForum` (default true) and no forums gets one forum, "General discussion" (`general-discussion`), on first use.

| Method and path | Body | Result | Errors |
|---|---|---|---|
| `GET /api/v1/forums`, `POST /api/v1/forums/enumerate` | `EnumerationQuery` (filters `section`, `includeArchived`, `followedOnly`, `audience`; orderings `SortOrderAscending` default, `NameAscending`, `ActivityDescending`, Created) | `EnumerationResult<Forum>`: each forum carries `threadCount`, `commentCount`, `lastActivityUtc`, `lastThread { id, title, author, lastActivityUtc }`, `isFollowed`, `unreadThreadCount` (capped at 99; null signed out), and `viewer { canCreateThread, canFollow, canModerate, allowedThreadTypes }` | `400` |
| `GET /api/v1/forums/{idOrSlug}` | | `Forum` | `404` |
| `PUT /api/v1/forums/{id}/follow` | `{ "level": "NewThreads" }` (the only level) | `Forum` | `400`, `404` |
| `DELETE /api/v1/forums/{id}/follow` | | `Forum` | `404` |
| `POST /api/v1/forums/{id}/read` | | `204` | `404` |
| `GET /api/v1/forums/{id}/threads`, `POST .../threads/enumerate` | `EnumerationQuery` (orderings `Active` default, `Hot`, `CreatedDescending`, `CreatedAscending`, `Top`, `Relevance`; filters `type`, `answered`, `unanswered`, `feedbackStatus`, `pollOpen`, `authorUserId`, `followedOnly`, `unreadOnly`, `hasMedia`, `hasVideo`, `audience`, `includePinned`, `includeComments`) | `EnumerationResult<ThreadSummary>`; `Active`/`Hot` without search use one pinned-first keyset when `includePinned` is true; pins and ordinary threads share `maxResults`, so pins can span pages | `400`, `404` |
| `POST /api/v1/forums/{id}/threads` | `ThreadRequest` | `201 Thread` (`Published`, or `Processing` until its media is ready) | `400 ValidationFailed`, `PollInvalid`, `FeedbackVideoRequired`, `ContentFiltered`; `403 PostingNotAllowed`, `ThreadCreationRestricted`, `ThreadTypeNotAllowed`, `MediaNotAllowed`, `Muted`, `ForumMuted`; `409 ForumArchived`, `FeedbackLimitReached`; `429` |
| `GET /api/v1/threads`, `POST /api/v1/threads/enumerate` | as the forum list plus `forumIds` | `EnumerationResult<ThreadSummary>` (no pinned prefix) | `400` |
| `GET /api/v1/threads/{id}` | | `Thread`; records the caller's read position | `404` |
| `PUT /api/v1/threads/{id}` | `ThreadRequest` (omitted fields stay) | `Thread`; edits after `content.editGraceSeconds` store a revision | `400`, `403`, `404`, `409 ThreadLocked`, `PollOptionsLocked` |
| `DELETE /api/v1/threads/{id}` | | `204` (author, moderator in scope, or SystemAdmin) | `403`, `404` |
| `GET /api/v1/threads/{id}/revisions`, `POST .../revisions/enumerate` | `EnumerationQuery` | `EnumerationResult<ThreadRevision>` | `404` |
| `POST /api/v1/threads/{id}/pin`, `.../unpin` | | `Thread` (moderator in scope; at most `forums.maxPinnedPerForum`) | `403`, `404`, `409 PinLimitReached`, `NotPublished` |
| `POST /api/v1/threads/{id}/lock` | `{ "reason": "..." }` (optional) | `Thread`; locked threads take no comments, replies, or votes (`409 ThreadLocked`) | `403`, `404` |
| `POST /api/v1/threads/{id}/unlock` | | `Thread` | `403`, `404` |
| `POST /api/v1/threads/{id}/move` | `{ "forumId", "reason", "notifyAuthor": true }` | `Thread` (moderator in scope of both forums; audience narrows to the target forum) | `400`, `403`, `404`, `409 ForumArchived`, `ThreadTypeNotAllowed` |
| `POST /api/v1/threads/{id}/accept-answer` | `{ "commentId" }` | `Thread` with `acceptedAnswer` (author or moderator in scope; any visible comment at any depth; concurrent accepts serialize) | `400 InvalidAcceptedAnswer`, `403` |
| `DELETE /api/v1/threads/{id}/accept-answer` | | `Thread` (`acceptedAnswer: null`) | `403` |
| `PUT /api/v1/threads/{id}/feedback` | `{ "status": "Open" \| "InReview" \| "Reviewed" \| "Closed", "reviewCommentId"? }` | `Thread` (moderators in scope for every transition; the author may set `Closed`; `Reviewed` notifies the author) | `400`, `403`, `404` |
| `PUT /api/v1/threads/{id}/follow`, `DELETE ...` | | `{ "isFollowed" }`; an unfollow sticks against later automatic follows; beyond `forums.maxFollowsPerMember` the least recently active followed thread is unfollowed | `404` |
| `POST /api/v1/threads/{id}/promote` | `PromoteRequest { title?, excerpt?, pin?, audience?, notifyMembers? }` | `201 FeedPost` (kind `PromotedThread`; `commentTarget` is the thread) | `403 PromotionNotAllowed`, `409 AlreadyPromoted`, `NotPublished` |
| `POST /api/v1/threads/{id}/unpromote` | | `Thread` | `403`, `404` |
| `POST /api/v1/feed/{id}/discuss` | `{ "forumId", "title"?, "labels"?, "tags"? }` | `201 Thread` (kind `LinkedFeedPost`; `commentTarget` is the feed post) | `403`, `404`, `409 AlreadyPromoted`, `ForumArchived` |
| `POST /api/v1/feed/{id}/undiscuss` | | `FeedPost` | `403`, `404` |
| `GET /api/v1/me/follows`, `POST /api/v1/me/follows/enumerate` | `EnumerationQuery` (filter `targetType`: `Forum` or `Thread`) | `EnumerationResult<FollowItem>` | `400` |

Thread list items (`ThreadSummary`) carry `feedback: { status, reviewer }` on `Feedback` threads, where `reviewer` is the `UserSummary` of the moderator who started or finished the review (null while `Open` or when the account is gone); `PublicThread` carries the same.

### One conversation per thread (`commentTarget`)

Every `Thread`, `ThreadSummary`, and promoted `FeedPost` carries `commentTarget { type: FeedPost | Thread, id }`. Comments and reactions always go to that target:

- An `Original` thread's conversation is the thread: `GET|POST /api/v1/threads/{id}/comments`, `POST /api/v1/threads/{id}/comments/enumerate`, and `PUT|DELETE /api/v1/threads/{id}/reactions/{key}`, `GET /api/v1/threads/{id}/reactions`, `POST .../reactions/enumerate`. Trees have the feed rules (section "Comment trees") and add `acceptedAnswerId`; comments carry `isAcceptedAnswer` and `isFeedbackReview`.
- A `LinkedFeedPost` thread's conversation is its feed post; the thread comment and reaction routes answer `409 UseCommentTarget`.
- A `PromotedThread` feed card's conversation is its thread; the feed comment and reaction routes on the card answer `409 UseCommentTarget`.

Thread comments follow the forum's `allowMedia` and `allowVideo`, refuse locked or archived threads (`409 ThreadLocked`, `ForumArchived`), and refuse members muted in the forum (`403 ForumMuted`). The commenter follows the thread when their `autoFollowOnComment` preference (`GET|PUT /api/v1/me/notification-preferences`, default `forums.autoFollowOnComment`) is on.

### `ThreadRequest`

| Field | Notes |
|---|---|
| `type` | `Discussion`, `Question`, `Poll`, `Feedback` (create only; limited to the forum's `allowedThreadTypes`) |
| `title` | 3 to 200 characters |
| `body` | Markdown, at most `forums.maxBodyLength` |
| `mediaIds` | At most `forums.maxAttachments`; at most `forums.maxVideosPerThread` videos; `Feedback` needs at least one video |
| `showLinkPreview`, `audience`, `labels`, `tags` | As feed posts; `audience` never wider than the forum's |
| `poll` | `Poll` threads: `{ options: [{ label }] (2 to polls.maxOptions), multipleChoice, maxChoices (multiple choice only), allowVoteChange, resultsVisibility: Always \| AfterVote \| AfterClose, voterVisibility: Anonymous \| Visible, closesUtc (at most polls.maxDurationDays) }`; options are fixed after the first vote |
| `feedback` | `Feedback` threads: `{ focus }` |

### Polls

| Method and path | Body | Result | Errors |
|---|---|---|---|
| `PUT /api/v1/polls/{id}/votes` | `{ "optionIds": [...] }` | `Poll` (counts exact under concurrent votes) | `400 ValidationFailed`, `403 ForumMuted`, `409 PollClosed`, `PollVoteLocked`, `ThreadLocked`, `429` |
| `DELETE /api/v1/polls/{id}/votes` | | `Poll` | `409 PollClosed`, `PollVoteLocked` |
| `POST /api/v1/polls/{id}/close` | | `Poll` (author or moderator in scope; voters get `PollClosed`) | `403` |
| `GET /api/v1/polls/{id}/voters`, `POST .../voters/enumerate` | `EnumerationQuery` (filter `optionId`) | `EnumerationResult<{ user, optionIds, createdUtc }>`, only when `voterVisibility` is `Visible` and the poll's results are visible to the caller (`resultsVisible`) | `403` (anonymous poll, or results not yet visible), `404` |

`Poll`: `{ id, threadId, multipleChoice, maxChoices, resultsVisibility, voterVisibility, allowVoteChange, closesUtc, closedUtc, isClosed, totalVoters, resultsVisible, myVote, options: [{ id, label, position, voteCount, percent }] }`. When `resultsVisible` is false, `voteCount` and `percent` are null. A worker closes due polls every `polls.closeIntervalSeconds`.

### Search

`GET /api/v1/search`, `POST /api/v1/search/enumerate`: one search across feed posts, threads, comments, and members the caller may see. `search` is required (1 to 200 characters); `filters.types` narrows (`FeedPost`, `Thread`, `Comment`, `User`); ordering `Relevance` (default) or Created. Items: `SearchResult { type, id, title, snippet { text, highlights [{ start, length }] }, path, author, forum, createdUtc, labels, tags }`. Terms are lemmatized and stop words removed. The index is updated by a background indexer (eventually consistent, normally within `search.indexIntervalSeconds`).

Global search is its own feature (`docs/ACCESS_MODELS_CONTRACT.md` section 2): `404 NotAvailable` when `property.json` `features.search` is false (default true); in `PartiallyGated` with `search` in `access.gatedFeatures` non-members get `402 MembershipRequired` (`feature: "search"`), independently of whether `forums` is gated; the page is empty while the deployment setting `search.enabled` is off. `GET /api/v1/property` reports the effective `features.search` (both switches on). Thread search through `POST /api/v1/threads/enumerate` belongs to the forums feature.

### Notifications

New types `ThreadReply`, `NewThreadInForum`, `AnswerAccepted`, `FeedbackReviewed`, `PollClosed`, `ThreadPromoted`, `ThreadMoved` in the new categories `Following` and `Forums` (in-app and push on; email off, opt-in, and delivered as a weekly digest: see [Notification preferences](#notification-preferences-signed-in)). Notifications carry `threadId`, `forumId`, and `pollId`. Forum mutes notify the member as `ModerationAction`.

### Account export and deletion

The export adds the sections `threads` (with revisions), `pollVotes`, `follows`, and `forumMutes` (without the moderator). Deleting an account soft-deletes the member's threads, deletes their poll votes (counts recomputed), follows, read state, and forum mutes.

## Public threads (Anonymous)

| Method and path | Result |
|---|---|
| `GET /api/v1/public/threads`, `POST /api/v1/public/threads/enumerate` | `EnumerationResult<PublicThread>`: Published, unmoderated `Public` threads in `Public` forums, newest first, `maxResults` default 10, max 20 |
| `GET /api/v1/public/threads/{id}` | `PublicThread` |

`Cache-Control: public, max-age=60`. `404` when forums are off or the property disables public samples (`features.publicSamples`).

## Admin: forums (Admin)

| Method and path | Body | Result | Errors |
|---|---|---|---|
| `GET /api/v1/admin/forums`, `POST .../enumerate` | `EnumerationQuery` | `EnumerationResult<AdminForum>` (adds `openReportCount`, `moderatorCount`, `pinnedCount`, `threadsLast30d`) | `400`, `403` |
| `POST /api/v1/admin/forums` | `ForumRequest { name, slug?, description, guidelines, icon { emoji, mediaId } (one grapheme, or a Ready image owned by an admin; responses add `imageUrl`), color, section, audience, threadCreation: Members \| Moderators \| Admins, allowedThreadTypes, defaultOrdering, allowMedia, allowVideo, maxOpenFeedbackPerMember, labels, tags }` | `201 Forum` (at most `forums.maxForums`) | `400`, `409 SlugInUse`, `ForumLimitReached` |
| `GET /api/v1/admin/forums/{id}` | | `AdminForumDetail` (moderators preview of at most 10 with `totalRecords` and `enumerate`) | `404` |
| `PUT /api/v1/admin/forums/{id}` | `ForumRequest` (omitted fields stay; narrowing the audience narrows every thread) | `Forum` | `400`, `404`, `409 SlugInUse` |
| `PUT /api/v1/admin/forums/order` | `{ "forumIds": [...] }` (every non-archived forum once) | `{ "updated": 12 }` | `400` |
| `POST /api/v1/admin/forums/{id}/archive`, `.../unarchive` | `{ "reason"? }` | `Forum` | `404` |
| `DELETE /api/v1/admin/forums/{id}` | | `204` (deleted threads do not block; Published, Processing, Hidden, and Removed threads do) | `409 ForumNotEmpty` |
| `GET /api/v1/admin/forums/{id}/moderators`, `POST .../enumerate` | `EnumerationQuery` | `EnumerationResult<ForumModerator>` (`scope`: `Forum` or `AllForums`, `forumId`) | `404` |
| `GET /api/v1/admin/metrics/forums?days=30` | | `EnumerationResult<LegacyMetricRow>` (filters.days 7 to 365, optional filters.series), scalar totals and an explicit bounded forum preview | `400` |
| `POST /api/v1/admin/search/rebuild` | | `202 { queued, requestedUtc }` | `404 NotAvailable` |

Forum moderators: `POST /api/v1/admin/moderators` with `{ userId, resourceType: "Forum", resourceId: "<forum id>" | "*" }` grants `Moderator` for one forum or every forum and returns the row with `scope` and `forumId`; `DELETE /api/v1/admin/moderators/{userId}?resourceType=Forum&resourceId=...` revokes it. Moderators act on threads, comments, polls, feedback, mutes, and reports only in their forums.

## Forum moderation (Moderator)

| Method and path | Body | Result | Errors |
|---|---|---|---|
| `GET /api/v1/admin/threads`, `POST .../enumerate` | `EnumerationQuery` (filters `state` incl. `Deleted`, `forumIds`, `type`, `feedbackStatus`, `authorUserId`, `pinned`, `locked`, `promoted`, `audience`) | `EnumerationResult<ThreadSummary>` in the caller's scope | `403` |
| `GET /api/v1/admin/trending`, `POST .../enumerate` | `EnumerationQuery` (ordering `ScoreDescending`; filters `forumIds`, `type`, `promoted`, `minScore`) | `EnumerationResult<{ thread, breakdown: { reactions, comments, uniqueCommenters, ageHours, numerator, denominator, score }, rank, computedUtc, promotion }>`; `breakdown.score` is the live score from the current counts and weights, `thread.trendingScore` (which orders the list) is the stored score as of `computedUtc` | `403` |
| `POST /api/v1/admin/moderation/forum-mutes` | `{ userId, forumId, untilUtc?, reason, notifyMember? }` | `201 ForumMute` (replaces an active mute in that forum; `forums.maxForumMuteDays` caps `untilUtc` when above 0) | `400`, `403`, `404` |
| `GET /api/v1/admin/moderation/forum-mutes`, `POST .../enumerate` | `EnumerationQuery` (filters `forumId`, `userId`, `activeOnly` default true) | `EnumerationResult<ForumMute>` | `403` |
| `DELETE /api/v1/admin/moderation/forum-mutes/{id}` | `{ "reason"? }` | `204` | `403`, `404` |

Reports accept `targetType: "Thread"` and file thread and thread-comment reports in `Forum` scope (`scopeId` the forum). The moderation queue adds filters `scopeType` (`Feed` or `Forum`) and `forumIds` and a `scope { type, id, name }` on each row. `POST /api/v1/admin/moderation/actions` accepts `targetType: "Thread"` (`Hide`, `Unhide`, `Remove`, `Restore`); pin, lock, move, metadata edits, and forum mutes are recorded as moderation actions `Pin`, `Unpin`, `Lock`, `Unlock`, `Move`, `EditMetadata`, `ForumMute`, `ForumUnmute`. `/admin/threads` and `/admin/trending` are Moderator routes; the rest of `/admin/forums` is Admin.

### Trending

`score = (trending.reactionWeight * reactions + trending.commentWeight * comments + trending.commenterWeight * uniqueCommenters) / (ageHours + trending.ageOffsetHours) ^ trending.gravity`, where `uniqueCommenters` excludes the thread author. A singleton worker recomputes threads active within `trending.windowDays` every `trending.intervalMinutes` in batches of `trending.batchSize` and zeroes the rest. Ordering `Hot` sorts by this score.

### Forum, poll, trending, and search settings

| Key | Default | Notes |
|---|---|---|
| `forums.maxForums`, `forums.maxBodyLength`, `forums.maxAttachments`, `forums.maxVideosPerThread`, `forums.maxPinnedPerForum` | see `GET /api/v1/settings` | Limits |
| `forums.maxOpenFeedbackPerMember` | 3 | 0 means unlimited |
| `forums.maxForumMuteDays` | 0 | 0 means no cap |
| `forums.maxFollowsPerMember`, `forums.readStateRetentionDays`, `forums.unreadWindowDays` | | Follows and read state; the hourly prune deletes thread and forum read markers older than `forums.readStateRetentionDays` |
| `forums.emailDigestIntervalHours` | 168 | Hours between `Following` and `Forums` email digests (24 to 720) |
| `forums.emailDigestMaxItems` | 10 | Notifications listed per digest (1 to 50) |
| `forums.autoFollowOnComment` | true | Default of the member preference |
| `forums.threadsPerHour`, `forums.threadsPerDay`, `forums.newAccountThreadsPerHour`, `forums.newAccountThreadsPerDay`, `forums.followsPerMinute`, `forums.searchPerMinute`, `forums.promotionsPerHour` | | Rate limits |
| `forums.seedDefaultForum` | true | Seed "General discussion" |
| `polls.maxOptions`, `polls.maxDurationDays`, `polls.votesPerMinute`, `polls.closeIntervalSeconds` | | Polls |
| `trending.enabled` (`TK_TRENDING_ENABLED`), `trending.intervalMinutes`, `trending.windowDays`, `trending.batchSize`, `trending.reactionWeight`, `trending.commentWeight`, `trending.commenterWeight`, `trending.ageOffsetHours`, `trending.gravity` | | Trending |
| `search.enabled` (`TK_SEARCH_ENABLED`), `search.schema` (`TK_SEARCH_SCHEMA`, restart), `search.lemmatize`, `search.removeStopWords`, `search.indexIntervalSeconds`, `search.indexBatchSize`, `search.candidateLimit`, `search.perMinute` | | Search; the index lives in the PostgreSQL schema `search.schema` (default `<database schema>_search`, or `search`) |

Background workers (singleton through job leases, per property): `forum_thread_publish`, `forum_trending`, `forum_poll_close`, `forum_read_prune`, `forum_email_digest` (every 30 minutes, at most 500 member and category pairs per pass), `search_index`.

## AI assistants (Member, Visitor)

Specification: [docs/AI_CONTRACT.md](docs/AI_CONTRACT.md). Every AI route answers `404 NotAvailable` unless the property feature `features.ai` is on and the setting `ai.enabled` (`TK_AI_ENABLED`) is true; `GET /api/v1/property` reports `features.ai` (both true) and `features.aiPlacement` (`Menu` or `Tab`, from `ai.mobilePlacement`). Administrators can read every conversation; clients show that disclosure (the privacy notice on every chat surface).

**Callers.** A member sends the usual bearer token or session cookie (assistants with audience `SignedIn` or `Members`, and `Public`). A visitor (signed out, `Public` assistants only) first calls `POST /api/v1/assistant-visitors` and sends the returned token in `X-TK-Visitor-Token` on every assistant request; the header is allowed and exposed by CORS. Members-only assistants answer `402 MembershipRequired` without an active membership.

**Visitor tokens.** Only two routes issue a token: `POST /api/v1/assistant-visitors` (in the body and the `X-TK-Visitor-Token` response header) and conversation start, `POST /api/v1/assistants/{id}/conversations`, which creates a new visitor and returns its token in the `X-TK-Visitor-Token` response header when the request has no token or its token no longer resolves (expired key, another property, or the visitor was deleted after `ai.visitorRetentionDays`). Every other visitor route (listing and reading assistants, conversations, messages, feedback) answers `401 Unauthorized` when the token is missing or no longer resolves; clients then start a new conversation (or call `POST /api/v1/assistant-visitors`) and replace the stored token. A new visitor never sees the old visitor's conversations.

| Method and path | Body | Success | Errors |
|---|---|---|---|
| `POST /api/v1/assistant-visitors` | `{}` | `201 { "visitorId": "avs_...", "visitorToken": "..." }` | `404 NotAvailable`, `429 RateLimited` (`rateLimits.aiNewVisitorsPerIpPerHour`) |
| `POST /api/v1/assistants/enumerate` | `EnumerationQuery` (search over name, title, description) | `200 EnumerationResult<AssistantSummary>` | `400 ValidationFailed`, `400 InvalidContinuationToken` |
| `GET /api/v1/assistants/{id}` | | `200 AssistantSummary` (with `limits.remainingToday`, `privacy`, and, for embeddable `Public` assistants, `embed { launcherLabel, position, allowedOrigins }`) | `401 Unauthorized`, `402 MembershipRequired`, `404 NotFound` |
| `POST /api/v1/assistants/{id}/conversations` | `{}` or `{ "message": "...", "clientMessageId": "uuid" }` | without a message `201 Conversation`; with one, a reply exchange (below) | as for messages |
| `POST /api/v1/assistants/{id}/conversations/enumerate` | `EnumerationQuery` (`filters.archived`) | `200 EnumerationResult<Conversation>` | `400 ValidationFailed` |
| `GET /api/v1/assistants` | Enabled assistants the caller may use (audience Public for visitors; SignedIn and Members by membership). A member bearer token, or X-TK-Visitor-Token for Public assistants (401 Unauthorized when it is missing or no longer resolves; only POST /api/v1/assistant-visitors and conversation start issue a token); 404 NotAvailable when AI is off. | `200` List assistants | `401`, `404` |
| `POST /api/v1/assistant-conversations/enumerate` | `EnumerationQuery` (`filters.assistantId`, `filters.archived`; search over title and last message) | `200 EnumerationResult<Conversation>` (`lastMessagePreview` is a plain-text excerpt; Markdown is removed) | `400 ValidationFailed` |
| `GET /api/v1/assistant-conversations/{id}` | | `200 Conversation` with `recentMessages` | `404 NotFound` (not the caller's) |
| `PUT /api/v1/assistant-conversations/{id}` | `{ "title"?, "isArchived"?, "labels"?, "tags"? }` | `200 Conversation` | `400 ValidationFailed`, `404 NotFound` |
| `DELETE /api/v1/assistant-conversations/{id}` | | `204` | `404 NotFound` |
| `POST /api/v1/assistant-conversations/{id}/messages` | `{ "content": "...", "clientMessageId": "uuid" }` | reply exchange | `400 MessageTooLong`, `400 ContentFiltered`, `400 MessageBlocked`, `400 OutOfScope`, `403 Muted`, `409 ReplyInProgress`, `409 ConversationFull`, `429 AssistantLimitReached` (with `Retry-After`), `429 RateLimited`, `502 ModelError`, `503 ModelBusy`, `503 ModelUnavailable`, `503 BudgetExceeded`, `504 ModelTimeout` |
| `POST /api/v1/assistant-conversations/{id}/messages/enumerate` | `EnumerationQuery` (oldest first by default) | `200 EnumerationResult<Message>` | `400 ValidationFailed` |
| `GET /api/v1/assistant-conversations/{id}/messages/{messageId}` | | `200 Message` (poll after a dropped stream) | `404 NotFound` |
| `POST /api/v1/assistant-conversations/{id}/messages/{messageId}/cancel` | `{}` | `200 Message` (`Cancelled`) | `404 NotFound` |
| `POST /api/v1/assistant-conversations/{id}/messages/{messageId}/regenerate` | `{}` | reply exchange (the previous reply is marked `isSuperseded`) | as for messages |
| `PUT /api/v1/assistant-conversations/{id}/messages/{messageId}/feedback` | `{ "rating": "Up" \| "Down", "note"?, "reasons"?: ["Incorrect", "Unhelpful", "Unsafe", "Other"] }` | `200 MessageFeedback` | `400 ValidationFailed`, `404 NotFound`, `429 RateLimited` |
| `DELETE /api/v1/assistant-conversations/{id}/messages/{messageId}/feedback` | | `204` | `404 NotFound` |

**Reply exchange.** With `Accept: text/event-stream` (and `streaming` on for the assistant) the response is a Server-Sent Events stream; otherwise `201 { "userMessage": Message, "reply": Message, "conversation": Conversation, "remainingToday": n }`. Sending the same `clientMessageId` again returns the stored exchange instead of a second reply. Stream events, in order:

| Event | Data |
|---|---|
| `message.created` | `{ "userMessage": Message, "replyMessageId": "acm_...", "conversation": Conversation }` |
| `reply.started` | `{ "replyMessageId", "assistantTitle" }` |
| `reply.reasoning` | `{ "text" }` (only when the assistant shows reasoning) |
| `tool.started` | `{ "toolCallId", "toolName", "label", "round" }` (only when the assistant has tools and `showToolActivity`; may arrive before and between deltas) |
| `tool.completed` | `{ "toolCallId", "toolName", "label", "completedLabel", "status", "resultCount", "durationMs", "round", "results": [ { "targetType", "targetId", "title", "path" } ] }` (`status` `Succeeded`, `Failed`, `TimedOut`, `Rejected`) |
| `reply.delta` | `{ "text" }` (append; text from several tool rounds is joined by a blank line and still concatenates to `content`) |
| `reply.citations` | `{ "citations": [ { "index", "targetType", "targetId", "title", "path" } ] }` |
| `reply.completed` | `{ "reply": Message, "remainingToday": n }` |
| `reply.blocked` | `{ "reply": Message }` (output moderation withheld the reply) |
| `error` | `{ "replyMessageId", "error": "ModelTimeout", "message" }` (the stream then ends) |

Comment heartbeats are sent every `ai.sseHeartbeatSeconds`. A client whose stream drops polls `GET .../messages/{messageId}` until the status is final (`Completed`, `Failed`, `Cancelled`, `Blocked`).

```json
{ "id": "acm_...", "conversationId": "acv_...", "role": "Assistant", "content": "The meetup is on Saturday [1].", "status": "Completed",
  "isAiGenerated": true, "reasoning": null, "citations": [ { "index": 1, "targetType": "FeedPost", "targetId": "fpo_...", "title": "Schedule", "path": "/feed/fpo_..." } ],
  "feedback": null, "model": null, "usage": null, "latencyMs": null, "timeToFirstTokenMs": null, "finishReason": "stop", "errorCode": null,
  "isSuperseded": false, "clientMessageId": null, "labels": [], "createdUtc": "2026-11-11T12:00:00Z", "completedUtc": "2026-11-11T12:00:02Z" }
```

`model`, `usage`, and timings are filled for members only when `ai.showModelDetailsToMembers` is true; the admin routes always return them plus `telemetry`.

**Tool calls.** A reply's `toolCalls` is `{ "objects": [ { "id": "atc_...", "toolName": "searchLibrary", "label": "Searching the library", "completedLabel": "Searched the library", "status": "Succeeded", "resultCount": 3, "durationMs": 120, "round": 1, "results": [ { "targetType": "Lesson", "targetId": "les_...", "title": "...", "path": "/lessons/les_..." } ], "createdUtc": "..." } ], "totalRecords": 1 }` (at most 12). Tools run as the caller with the caller's access (visitors get only tools that work signed out, over `Public` content), so results never include anything the caller could not open in the app. Admin views add `argumentsJson`, `resultSummary` (first 500 characters of the output), `resultCharacters`, and `error`. Cited tool results appear in `citations` like knowledge items. See `docs/AI_CONTRACT.md` section 8.3.

**Reporting a reply.** `POST /api/v1/reports` with `{ "targetType": "AssistantMessage", "targetId": "acm_...", "reason": "..." }` reports an assistant reply in the caller's own conversation (see [Reports](#reports-member)).

**Configured origins.** An assistant's `embed.allowedOrigins` (admin create and update, at most 20) accepts `https://host[:port]` origins, and plain `http` only for the local development hosts `localhost` and `127.0.0.1` on any port (for example `http://localhost:5500`). Entries are trimmed, lowercased, and stripped of a trailing slash; duplicates are dropped. Anything else (http on another host, paths, wildcards, other schemes) is `400 ValidationFailed` with `fieldErrors["embed.allowedOrigins[<index>]"]` naming the entry, plus `fieldErrors["embed.allowedOrigins"]`, and nothing is changed. The embed page's origin check and `postMessage` targets follow the list, local origins included; framing itself is also governed by the website's `frame-ancestors` (see `docs/AI_CONTRACT.md` section 13.3).

**Widget origins.** `embed.allowedOrigins` on an embeddable assistant's summary lists the exact origins (`scheme://host[:port]`, lowercase) the widget may be framed by and exchange `postMessage` events with: the assistant's configured `embed.allowedOrigins` plus the property's own web origins. The embed page posts `tk-assistant:*` events only to the embedding page's origin when it is in this list (never to `"*"`), and the loader accepts events only from the iframe's origin.

---

## Admin: AI model endpoints (Admin)

Connections to model providers. `apiType`: `Ollama`, `OpenAI`, `OpenAICompatible`, `Gemini`, `AzureOpenAI`, `Anthropic`, `Bedrock`, `VertexAI`, `OllamaCloud`, `XAI`, `Cohere`, `VoyageAI`, `Tei` (limited by `ai.enabledApiTypes`); `kind`: `Completion`, `Embedding`, `Rerank` (each API supports a subset; others are `400 ValidationFailed`). `authType`: `None`, `ApiKey` (the provider's native key), `Bearer`, `Header` (`authHeaderName`), `QueryParameter` (`authQueryParameter`), `Basic` (`authUsername` plus the password as `authValue`); a type the API cannot use is `400 AuthTypeNotSupported`.

**Secrets are write-only.** `authValue` and `extraHeaders[].value` are encrypted with AES-256-GCM under `secrets.encryptionKey` (`TK_SECRETS_ENCRYPTION_KEY`, 32 bytes base64; older keys in `secrets.previousEncryptionKeys` still decrypt) and are never returned: responses carry `hasAuthValue` and `extraHeaders[].hasValue`. On update, omit `authValue` to keep it, send a value to replace it, or `""` to clear it; an extra header sent without `value` keeps the stored value of that name. Request history never stores the bodies of these routes. Without an encryption key, storing a secret is `400 ValidationFailed`.

| Method and path | Body | Success | Errors |
|---|---|---|---|
| `GET /api/v1/admin/ai/endpoints`, `POST /api/v1/admin/ai/endpoints/enumerate` | `EnumerationQuery` (filters `kind`, `apiType`, `authType`, `scope`, `enabled`, `healthStatus`; search over name, model, URL; orderings `NameAscending` (default), `CreatedDescending`, `CreatedAscending`) | `200 EnumerationResult<ModelEndpoint>` | `400 ValidationFailed` |
| `POST /api/v1/admin/ai/endpoints` | `ModelEndpointRequest` | `201 ModelEndpoint` | `400 ValidationFailed`, `400 AuthTypeNotSupported` |
| `GET /api/v1/admin/ai/endpoints/{id}` | | `200 ModelEndpoint` | `404 NotFound` |
| `PUT /api/v1/admin/ai/endpoints/{id}` | partial `ModelEndpointRequest` | `200 ModelEndpoint` | `400 ValidationFailed`, `404 NotFound` |
| `DELETE /api/v1/admin/ai/endpoints/{id}` | | `204` (usage and health history stay for analytics) | `409 EndpointInUse` (an enabled assistant uses it) |
| `POST /api/v1/admin/ai/endpoints/validate` | `ModelEndpointRequest` (not stored) | `200 EndpointTestResult` | `400 ValidationFailed` |
| `POST /api/v1/admin/ai/endpoints/{id}/test` | Completion `{ "prompt"?, "maxTokens"? }` (`maxTokens` default 1024, clamped to 1 through 8192); Embedding `{ "input"? }`; Rerank `{ "query"?, "documents"? }` | `200 { "success", "kind", "latencyMs", "timeToFirstTokenMs", "statusCode", "model", "output", "reasoning", "finishReason", "maxTokens", "truncated", "hint" (ReasoningConsumedBudget, EmptyOutput, Truncated, or null), "usage": { "promptTokens", "completionTokens", "reasoningTokens", "totalTokens" }, "embedding": { "dimensions", "preview" }, "rerank", "error" }` | `404 NotFound`, `429 RateLimited` |
| `POST /api/v1/admin/ai/endpoints/{id}/models/enumerate` | `EnumerationQuery` | `200 EnumerationResult<ProviderModelView>` (name, displayName, description, ownedBy, createdUtc, sizeBytes, token limits, supportsTools, labels, tags) (cached `ai.modelListCacheMinutes`, at most `ai.maxListedModels`) | `502 ModelError` |
| `GET /api/v1/admin/ai/endpoints/{id}/health` | | `200 { "status", "lastCheckedUtc", "lastLatencyMs", "lastError", "consecutiveSuccesses", "consecutiveFailures", "lastHealthyUtc", "lastUnhealthyUtc", "uptimePercentage", "uptimeWindowHours" }` | `404 NotFound` |
| `POST /api/v1/admin/ai/endpoints/{id}/health/enumerate` | `EnumerationQuery` (newest first) | `200 EnumerationResult<ModelEndpointHealthCheck>` | `400 ValidationFailed` |
| `POST /api/v1/admin/ai/endpoints/{id}/health/check` | `{}` | `200 ModelEndpointHealthCheck` | `404 NotFound` |
| `POST /api/v1/admin/ai/endpoints/{id}/duplicate` | `{ "name"?, "includeSecrets"?: false }` | `201 ModelEndpoint` (disabled) | `404 NotFound` |
| `POST /api/v1/admin/ai/endpoints/{id}/reveal` | `{ "field": "authValue" \| "extraHeader", "headerName"?, "reason": "10 to 500 characters" }` | `200 { "value" }` (audited `ai.endpoint.reveal`; never stored in request history) | `403 RevealNotAllowed` (`ai.allowSecretReveal` off, or not an administrator signed in with their own account within `ai.revealRecentSignInMinutes`), `429 RateLimited` |
| `POST /api/v1/admin/ai/endpoints/{id}/usage/enumerate` | `EnumerationQuery` (newest first) | `200 EnumerationResult<AiUsageRecord>` | `400 ValidationFailed` |
| `POST /api/v1/admin/ai/endpoints/reencrypt` | `{}` | `200 { "endpoints", "secrets", "keyId" }` (re-encrypts every secret under the current key) | `400 ValidationFailed` (no key) |

`ModelEndpointRequest`:

```json
{ "name": "Local llama", "description": null, "enabled": true, "kind": "Completion", "apiType": "Ollama", "model": "llama3.1:8b",
  "endpointUrl": "http://ollama.internal:11434", "authType": "Bearer", "authValue": "write-only",
  "extraHeaders": [ { "name": "X-Org", "value": "write-only" } ],
  "parameters": { "temperature": 0.7, "topP": null, "maxTokens": null, "contextWindow": 8192, "streaming": true, "timeoutMs": 120000, "firstResponseTimeoutMs": 20000, "dimensions": null, "reasoningEffort": null, "toolCalling": "Native" },
  "limits": { "maxConcurrentRequests": 2, "maxQueueDepth": 20, "maxRetries": 2 },
  "healthCheck": { "enabled": true, "intervalMs": 60000, "timeoutMs": 10000, "mode": "Auto" },
  "pricing": { "inputPerMillionTokens": 0.15, "outputPerMillionTokens": 0.6, "cacheReadPerMillionTokens": null, "cacheWritePerMillionTokens": null }, "labels": [], "tags": {} }
```

`pricing` (US dollars per million tokens, each 0 to 1000000 or null; every price is optional, and an endpoint without prices records a cost of 0): `inputPerMillionTokens`, `outputPerMillionTokens`, and the optional `cacheReadPerMillionTokens` and `cacheWritePerMillionTokens` for tokens read from and written to the provider's prompt cache, which fall back to the input price ([docs/AI_TELEMETRY.md](docs/AI_TELEMETRY.md), "Cost").

`endpointUrl` is an absolute `http` or `https` URL without credentials, a query string, or a fragment. Plain `http` and a path prefix are both accepted for any host: an `Ollama` endpoint at `http://gateway.example:8900/v1.0/api/model-a/` is called at `http://gateway.example:8900/v1.0/api/model-a/api/chat`. Model endpoints are configured only by a SystemAdmin, so the outbound address policy that guards member-supplied URLs (link previews, discovery crawls) does not apply to them. Over plain `http` the credential travels unencrypted; the admin console says so in the form.

`providerSettings` (`{ region, project, apiVersion, deployment, accessKeyId }`, all strings of `A-Z a-z 0-9 . _ : -`, at most 200; a present field replaces the stored value, and every field the provider does not use must be null): `AzureOpenAI` needs `deployment` (the deployment name is sent as the model; `model` is informational); `apiVersion` is accepted and stored but no longer sent (PolyPrompt 4.0 uses the Azure v1 API, `/openai/v1/responses`); `endpointUrl` is the resource endpoint (`https://<resource>.openai.azure.com`); auth `ApiKey` (sent as `api-key`) or `Bearer` (an Entra token). `VertexAI` needs `project` and `region`; auth `ApiKey` (the service account key JSON in `authValue`, exchanged for OAuth tokens that are cached with the endpoint) or `None` (application default credentials); `endpointUrl` optional (default `https://{region}-aiplatform.googleapis.com`). `Bedrock` needs `region` and, with `ApiKey`, `accessKeyId` (`authValue` is the secret access key; `None` uses the environment or task role credentials); requests are SigV4 signed; `endpointUrl` optionally overrides the runtime host (model listing always uses the regional control plane). `OpenAI` endpoints use OpenAI's Responses API (`/v1/responses`); servers that only speak Chat Completions (vLLM, LM Studio, Ollama `/v1`, gateways) use `OpenAICompatible`. `XAI` has no `Embedding` kind (PolyPrompt 4.0 removed xAI embeddings), and `Tei` needs `endpointUrl`. Kinds: `AzureOpenAI` and `VertexAI` Completion and Embedding; `Bedrock` Completion, Embedding, and Rerank. Model listing: `AzureOpenAI` and `Bedrock` (none for `VertexAI`; type the model). Header, query, and Basic auth are `400 AuthTypeNotSupported` for these three.

`parameters.toolCalling` (Completion endpoints): `Native` (default; provider function calling through PolyPrompt, with an automatic one-time fallback to the prompt protocol when the model answers HTTP 400 naming tools), `Prompt` (a JSON tool request protocol in the system prompt, for models without tool support), or `Off` (assistants never send this endpoint tools).

Bounds: `timeoutMs` 1000 to 600000, `maxConcurrentRequests` 1 to 256, `maxQueueDepth` 0 to 10000, `maxRetries` 0 to 5, `temperature` 0 to 2, `topP` 0 to 1, at most `ai.maxExtraHeaders` extra headers. Health checks run on `ModelEndpointHealthWorker`; history is kept `ai.healthHistoryDays` (at most `ai.healthHistoryMaxRecords` per endpoint).

---

## Admin: AI assistants and knowledge (Admin)

| Method and path | Body | Success | Errors |
|---|---|---|---|
| `GET /api/v1/admin/ai/assistants`, `POST /api/v1/admin/ai/assistants/enumerate` | `EnumerationQuery` (filters `enabled`, `audience`, `endpointId`, `embedEnabled`; search over name, title, description) | `200 EnumerationResult<Assistant>` (with 30-day `stats` and month-to-date `budget`) | `400 ValidationFailed` |
| `POST /api/v1/admin/ai/assistants` | `AssistantRequest` | `201 Assistant` | `400 ValidationFailed`, `400 EndpointKindMismatch` |
| `GET /api/v1/admin/ai/assistants/{id}` | | `200 Assistant` | `404 NotFound` |
| `PUT /api/v1/admin/ai/assistants/{id}` | partial `AssistantRequest` (a model set to `null` is cleared) | `200 Assistant` | `400 ValidationFailed`, `400 EndpointKindMismatch`, `404 NotFound` |
| `DELETE /api/v1/admin/ai/assistants/{id}` | | `204` (conversations are deleted after `ai.deletedAssistantRetentionDays`) | `404 NotFound` |
| `POST /api/v1/admin/ai/assistants/{id}/duplicate` | `{ "name"? }` | `201 Assistant` (disabled) | `404 NotFound` |
| `POST /api/v1/admin/ai/assistants/{id}/test` | `{ "messages": [ { "role": "user" \| "assistant", "content" } ], "useDraft"?: AssistantRequest }` | a reply exchange plus `debug` events (system prompt sent, retrieval query, retrieved items, and `{ "toolCall": { "round", "toolCallId", "toolName", "argumentsJson", "status", "resultCount", "durationMs", "error", "output" } }` per tool call, output truncated); tools run as the administrator; nothing is stored and usage is excluded from analytics | `400 ValidationFailed`, `429 RateLimited` (`rateLimits.aiAssistantTestsPerHour`) |
| `GET /api/v1/admin/ai/assistants/{id}/knowledge/preview`, `POST .../knowledge/preview/enumerate` | `EnumerationQuery`; required `filters.query`, optional `filters.asUserId` (GET uses `query` and `asUserId`) | `200 EnumerationResult<KnowledgePreviewItem>` with `objects` (`id`, `rank`, `targetType`, `targetId`, `title`, `path`, `score`, `excerpt`, `labels`, `tags`), scalar `totalCandidates` and `mode`. Default `RankAscending`; the finite shortlist is capped by `maxContextItems` (at most 50), not the whole corpus. Creation windows use Unix epoch; source metadata filters and scoped continuations apply | `400 ValidationFailed`, `400 InvalidContinuationToken` |
| `GET /api/v1/admin/ai/assistants/{id}/knowledge/status` | | `200 AiKnowledgeIndex` (`status` `NotConfigured`, `Idle`, `Running`, `Failed`; `itemCount`, `chunkCount`, `lastRunUtc`, `lastSuccessUtc`, `lastError`) | `404 NotFound` |
| `POST /api/v1/admin/ai/assistants/{id}/knowledge/reindex` | `{}` | `202 AiKnowledgeIndex` (`reindexRequested`) | `404 NotFound` |
| `POST /api/v1/admin/ai/assistants/{id}/knowledge/items/enumerate` | `EnumerationQuery` (filters `status`, `targetType`) | `200 EnumerationResult<AiKnowledgeItem>` | `400 ValidationFailed` |
| `POST /api/v1/admin/ai/tools/enumerate` | `EnumerationQuery` (search over name, description, label, aliases; filter `disabled`; tools carry no labels or tags) | `200 EnumerationResult<AssistantTool>`: `{ "name", "description", "label", "completedLabel", "aliases", "worksSignedOut", "parametersSchema", "disabled", "disabledReason", "disabledByUserId", "disabledUtc", "labels": [], "tags": {} }` | `400 ValidationFailed`, `404 NotAvailable` |
| `PUT /api/v1/admin/ai/tools/{name}` | `{ "disabled": true, "reason"? }` (aliases accepted, for example `findGyms`) | `200 AssistantTool`; the property-wide kill switch of the tool (never offered to any assistant while disabled; calls are rejected); audited `ai.tool.disable` or `ai.tool.enable` | `400 ValidationFailed`, `404 NotFound` |

`AssistantRequest`:

```json
{ "name": "Helper", "title": "Community helper", "description": null, "enabled": true, "audience": "Members",
  "avatar": { "emoji": "🤖", "mediaId": null }, "systemPrompt": "You help members of {name}. Today is {date}.",
  "models": { "chat": "mep_...", "chatFallback": null, "titleGeneration": null, "queryRewrite": null, "embedding": null, "rerank": null, "moderation": null, "toolRouting": null },
  "generation": { "temperature": null, "topP": null },
  "tools": { "enabled": true, "allowed": ["searchLibrary", "findEvents", "findGyms"], "maxToolIterations": 4, "maxToolCallsPerTurn": 6, "toolCallTimeoutMs": 15000,
    "maxToolOutputCharacters": 8000, "maxTokensPerTurn": 32000, "showToolActivity": true },
  "knowledge": { "enabled": true, "mode": "Keyword", "maxContextItems": 6, "maxContextCharacters": 6000, "citations": true,
    "sources": [ { "targetType": "FeedPost", "labels": ["guides"], "labelMatch": "All", "tags": {}, "maxItems": 200 } ] },
  "limits": { "messagesPerMemberPerDay": 50, "messagesPerVisitorPerDay": 10, "maxMessagesPerConversation": 100, "maxTokensPerReply": 800, "maxMessageCharacters": 4000, "conversationRetentionDays": 365 },
  "streaming": true, "showReasoning": false, "starterPrompts": ["What is on this week?"], "disclaimer": null, "moderationMode": "Off",
  "budget": { "monthlyUsd": 20, "hardStop": false },
  "embed": { "enabled": false, "allowedOrigins": [], "launcherLabel": null, "position": "BottomRight" }, "labels": [], "tags": {} }
```

`models.chat` is required and must be a `Completion` endpoint; `embedding` must be `Embedding` and `rerank` `Rerank` (`400 EndpointKindMismatch`). System prompt tokens: `{name}`, `{shortName}`, `{memberDisplayName}`, `{date}`, `{assistantTitle}` (up to `ai.maxSystemPromptCharacters`). Knowledge `mode`: `Keyword` (the feed's search, as the member), `Vector` (RecallDB, `ai.recallDbEndpoint`; needs an embedding model), or `Hybrid` (reciprocal rank fusion); every retrieved item is re-checked against what the caller may read. Only `FeedPost` sources are available. `moderationMode`: `Off`, `LogOnly`, `Block` (needs a moderation model; failures fail open). `tools.allowed` lists registered tool names (aliases such as `findGyms` are saved as their canonical name; unknown names and `enabled` with an empty list are `400 ValidationFailed` with `fieldErrors["tools.allowed"]`) and replaces the list on update; limits: `maxToolIterations` 1 to 10, `maxToolCallsPerTurn` 1 to 12, `toolCallTimeoutMs` 100 to 60000, `maxToolOutputCharacters` 200 to 100000, `maxTokensPerTurn` 0 (no extra cap) or 1000 to 1000000. `models.toolRouting` (a `Completion` endpoint) decides tool calls before the chat model answers. `embed` applies only to `Public` assistants and enables the widget (`/assistant-widget.js` on the web domain, `data-assistant="ast_..."`). A spent `budget.monthlyUsd` with `hardStop` (or the property budget `ai.monthlyBudgetUsd` with `ai.budgetHardStop`) answers `503 BudgetExceeded`; alerts go to `ai.budgetAlertEmail` at `ai.budgetAlertPercent`.

---

## Admin: AI analytics, conversations, and feedback (Admin)

| Method and path | Body or query | Success | Errors |
|---|---|---|---|
| `GET /api/v1/admin/ai/analytics/overview` | `?days=30&assistantId=&endpointId=` (days 1 to 365) | `200 AiOverview` | `400 ValidationFailed` |
| `GET /api/v1/admin/ai/analytics/timeseries` | `?days=30&metric=requests&granularity=Day&assistantId=&endpointId=` (metric `requests`, `errors`, `promptTokens`, `completionTokens`, `tokens`, `cost`, `latencyAvgMs`, `latencyP95Ms`; granularity `Hour` (days at most 7) or `Day`; bounded by the window) | `200 { "metric", "granularity", "points": [ { "timestampUtc", "value" } ] }` | `400 ValidationFailed` |
| `POST /api/v1/admin/ai/analytics/endpoints/enumerate` | `EnumerationQuery` (`filters.windowDays`, default 30) | `200 EnumerationResult<{ "endpoint", "requests", "errors", "errorRate", "latencyP95Ms", "promptTokens", "completionTokens", "costUsd", "purposes" }>` | `400 ValidationFailed` |
| `POST /api/v1/admin/ai/analytics/assistants/enumerate` | `EnumerationQuery` (`filters.windowDays`) | `200 EnumerationResult<{ "assistant", "conversations", "messages", "thumbsUpRate", "requests", "errorRate", "latencyP95Ms", "promptTokens", "completionTokens", "costUsd", "monthlyBudgetUsd", "monthToDateCostUsd" }>` | `400 ValidationFailed` |
| `POST /api/v1/admin/ai/analytics/members/enumerate` | `EnumerationQuery` (filters `windowDays`, `assistantId`, `subjectType` `User` or `Visitor`; cost descending) | `200 EnumerationResult<{ "subjectType", "subjectId", "member", "messages", "promptTokens", "completionTokens", "costUsd", "lastActiveUtc" }>` | `400 ValidationFailed` |
| `POST /api/v1/admin/ai/conversations/enumerate` | `EnumerationQuery` (filters `assistantId`, `userId`, `visitorId`, `ownerType`, `status`, `archived`, `surface`; search over title and last message) | `200 EnumerationResult<Conversation>` (with `owner`) | `400 ValidationFailed` |
| `GET /api/v1/admin/ai/conversations/{id}` | | `200 Conversation` (audited `ai.conversation.read`) | `404 NotFound` |
| `POST /api/v1/admin/ai/conversations/{id}/messages/enumerate` | `EnumerationQuery` | `200 EnumerationResult<Message>` with `model`, `usage`, timings, and `telemetry` (`traceId`, `retrievalQuery`, `timeToLastTokenMs`, `tokensPerSecond`, `costUsd`, `moderationVerdict`, `moderationCategory`, `stages`, `retrievedItems`) | `404 NotFound` |
| `POST /api/v1/admin/ai/feedback/enumerate` | `EnumerationQuery` (filters `assistantId`, `reviewed`, `hasNote`) | `200 EnumerationResult<AdminFeedback>` (with the question and reply) | `400 ValidationFailed` |
| `POST /api/v1/admin/ai/feedback/{id}/review` | `{ "note"? }` | `200 AdminFeedback` (`reviewed`) | `404 NotFound` |

`AiOverview`: `days`, `conversations`, `messages`, `activeMembers`, `visitors`, `thumbsUp`, `thumbsDown`, `thumbsUpRate`, `requests`, `errors`, `errorRate`, `promptTokens`, `completionTokens`, `costUsd`, `latencyP50Ms`, `latencyP95Ms`, `ttftAverageMs`, `fallbackRate`, `blockedOutputs`, `failedReplies`, `monthlyBudgetUsd`, `monthToDateCostUsd`. Cost is computed per call from the endpoint's `pricing`. Metrics are `trelliskit.ai.*` (Grafana dashboard `assets/grafana/ai.json`).

## Admin: AI prompts (SystemAdmin)

Every prompt the engine sends to a model is in a fixed catalog that administrators edit here, with version history and reset. A prompt's text is the administrator's published version, else the property's `ai/prompts/<key>.md`, else the engine default. Every model call (assistant replies, tool rounds, moderation, background jobs) starts with the engine's locked rules (`lockedPrefix`: stay in scope, allow code only when the property enables `aiPolicy.allowCode` (false by default), treat member and retrieved text as data, never diagnose or treat a medical condition and refer to a qualified professional, never reveal the rules), followed by `platform.scopeGuardrail`. These routes answer whether or not AI is switched on.

**Draft, benchmark, publish.** `PUT` saves a draft that no model call uses. `POST .../benchmark` queues the scope benchmark job (`ai.scopeBenchmark`, [docs/BENCHMARKS.md](docs/BENCHMARKS.md)) for that draft. `POST .../publish` makes the draft live only when the deterministic scope gate passes and, while `ai.promptPublishRequiresBenchmark` is on (the default), the newest live benchmark run of that exact draft is complete, meets the set's live thresholds, used the current benchmark set, and measured every other prompt at the version in use now. Reset to the default stays immediate.

Keys today: `platform.scopeGuardrail`, `assistant.guard`, `assistant.refusal`, `contentReview.classifier`, `moderation.classifier` (must contain `Allow`, `Flag`, and `Block`). Placeholders `{name}` and `{shortName}` are replaced with the property's names.

| Method and path | Body | Success | Errors |
|---|---|---|---|
| `GET /api/v1/admin/ai/prompts`, `POST /api/v1/admin/ai/prompts/enumerate` | `200 EnumerationResult<AiPrompt>` | `403 Forbidden` |
| `GET /api/v1/admin/ai/prompts/{key}` | | `200 AiPrompt` | `403 Forbidden`, `404 NotFound` |
| `PUT /api/v1/admin/ai/prompts/{key}` | `{ "content": "..." }` (within `minLength` to `maxLength`, containing `requiredWords`, no code unless `aiPolicy.allowCode` is enabled) | `200 AiPrompt` (a new draft version in `draft`; the live text is unchanged; audited `ai.prompt.update`) | `400 ValidationFailed`, `403 Forbidden`, `404 NotFound` |
| `POST /api/v1/admin/ai/prompts/{key}/benchmark` | `{ "dryRun"?, "assistantId"?, "replyRole"? }` (`replyRole` a completion task role, default `CommunityAssistant`; `assistantId` classifies input with that assistant's moderation model when the `Moderation` role has no executors) | `201 JobRun` (`Queued`; measures the pending draft, or the prompts in use when there is none; audited `job.run`) | `400 ValidationFailed`, `403 Forbidden`, `404 NotFound`, `409 RunActive`, `409 BudgetExceeded` |
| `POST /api/v1/admin/ai/prompts/{key}/publish` | `{ "versionId"? }` (the draft reviewed) | `200 AiPrompt` (the draft is live; audited `ai.prompt.publish`) | `403 Forbidden`, `404 NotFound`, `409 NoDraft`, `409 DraftChanged`, `409 ScopeGateFailed`, `409 BenchmarkRequired` (no live run of this draft, or only a dry run), `409 BenchmarkRunning`, `409 BenchmarkFailed`, `409 BenchmarkStale` (the benchmark set or another prompt changed after the run) |
| `POST /api/v1/admin/ai/prompts/{key}/reset` | `{}` | `200 AiPrompt` (default in use; a reset version is recorded; audited `ai.prompt.reset`) | `403 Forbidden`, `404 NotFound` |
| `GET /api/v1/admin/ai/prompts/{key}/versions` and `POST .../versions/enumerate` | `EnumerationQuery` (newest first) | `200 EnumerationResult<AiPromptVersion>` | `400 ValidationFailed`, `403 Forbidden`, `404 NotFound` |

`AiPrompt`: `key`, `title`, `description`, `content` (the live text), `defaultContent`, `defaultSource` (`Engine` or `Property`), `isDefault`, `version` (the live version; 0 while the default is in use), `draft` (`AiPromptVersion` of the pending draft, or null), `draftBenchmark` (the newest `JobRun` of `ai.scopeBenchmark` for the draft, or null), `requiresBenchmark`, `minLength`, `maxLength`, `variables`, `requiredWords`, `lockedPrefix` (scope guardrail only), `updatedByUserId`, `updatedUtc`. `AiPromptVersion`: `id` (`apv_...`), `key`, `version`, `content`, `isReset`, `isDraft` (the pending draft), `createdByUserId`, `createdUtc`, `publishedUtc` (null for a draft or a draft replaced before it was published), `labels`, `tags`.

**Scope enforcement on chat.** When the property disallows code, a member message that asks for code (or contains it) answers `400 OutOfScope` with the `assistant.refusal` text and never reaches a model. A reply that violates the code policy or quotes the engine's locked rules is retried once, without streaming, with a reminder; a clean retry is delivered (the `reply.completed` reply replaces any streamed text, and the message stages include `chat.retry`). When the retry is rejected too, the reply is withheld whatever the assistant's moderation mode: it is stored as `Blocked` with `errorCode` `OutputOutOfScope`, members see the refusal text, and a system report keeps the withheld text for moderators. The moderation classifier gained the `OffScope` category. An assistant without a moderation model classifies through the `Moderation` task role; when that role has no executors either, the skip is recorded as a `moderation.input` stage on the reply and as `verdict=skipped` on `trelliskit.ai.moderation`.

## Admin: AI task roles and call ledger (SystemAdmin)

Features and jobs call a **task role**, never a model. Each role has an ordered executor hierarchy (at most 8), a reasoning effort, an availability policy (`FallThrough` or `Wait`), and an exhaustion policy (`Fail`, `Defer`, or `Wait`). Executor runtimes: `ApiEndpoint` (a model endpoint from `/admin/ai/endpoints`), and the CLI agent harnesses `ClaudeCode`, `Codex`, `GeminiCli`, `CursorCli`, `Mux`, `OpenCode`, `Pi`. Harnesses are allowed only on agentic background roles (`Research` or a property-defined background completion role that allows harnesses), never on interactive roles, and run only on instances with `jobs.runHarnesses` and only inside job items (never in request paths, so `POST .../test` always passes harness executors over and reports why in `escalations`). Harnesses hand results back through a job-scoped MCP server on 127.0.0.1 ([docs/HARNESSES.md](docs/HARNESSES.md)); no route serves it. Every model call (role calls, harness calls, assistant chat, tool rounds, moderation, titles and rewrites, embeddings, rerank, an administrator's endpoint tests and validations, and health checks that call the model) adds a row to the call ledger with its full telemetry ([docs/AI_TELEMETRY.md](docs/AI_TELEMETRY.md)); harness rows carry the harness `runtime`, the reported `model`, tokens, and the reported cost. Inside a job, the row names the job, run, and item, and the call's cost is added to the item. Anthropic requests never carry temperature or top_p.

Built-in roles: `Research`, `Extraction`, `Moderation`, `Classification`, `SearchParse`, `CommunityAssistant`, `ContentAssistant`, and `Embedding` (embedding endpoints). Properties can append definitions in `ai/roles.json`; built-in keys cannot be replaced. The catalog is bounded at 64 roles and the file at 64 KiB. Each role needs a unique PascalCase key (2 to 64 characters), a title (1 to 120 characters), a description (at most 2000), and optional suggested guidance (at most 1000). Only non-interactive completion roles can allow harnesses. Executor assignments remain administrator-managed per property.

| Method and path | Body | Success | Errors |
|---|---|---|---|
| `GET /api/v1/admin/ai/roles`, `POST /api/v1/admin/ai/roles/enumerate` | `200 EnumerationResult<AiTaskRole>` | `403 Forbidden` |
| `GET /api/v1/admin/ai/roles/{role}` | | `200 AiTaskRole` | `403 Forbidden`, `404 NotFound` |
| `PUT /api/v1/admin/ai/roles/{role}` | `{ "reasoningEffort"?, "availabilityPolicy"?, "exhaustionPolicy"?, "executors": [ { "runtime", "endpointId"?, "model"?, "enabled"? } ] }` | `200 AiTaskRole` (audited `ai.role.update`) | `400 ValidationFailed`, `403 Forbidden`, `404 NotFound` |
| `POST /api/v1/admin/ai/roles/{role}/test` | `{ "prompt" }` (1 to 4000 characters) | `200 AiRoleTestResult` (recorded as test traffic) | `400 ValidationFailed`, `400 NotTestable` (Embedding), `403 Forbidden`, `404 NotFound`, `404 NotAvailable` |
| `GET /api/v1/admin/ai/calls` and `POST .../calls/enumerate` | `EnumerationQuery` (filters `role`, `purpose`, `outcome`, `endpointId`, `jobKey`, `jobRunId`, `assistantId`; newest first) | `200 EnumerationResult<AiCall>` | `400 ValidationFailed`, `403 Forbidden` |

`AiTaskRole`: `role`, `title`, `description`, `kind` (`Completion` or `Embedding`), `allowsHarness`, `interactive`, `suggested` (configuration guidance), `reasoningEffort`, `availabilityPolicy`, `exhaustionPolicy`, `executors` (`position`, `runtime`, `endpointId`, `endpointName`, `model`, `health`, `enabled`, `problem`), `ready`, `updatedByUserId`, `updatedUtc`. `AiRoleTestResult`: `success`, `text`, `executorPosition`, `endpointId`, `model`, `escalations`, `errorCode` (`NoExecutors`, `ExecutorsExhausted`), `latencyMs`, `costUsd`. `AiCall`: `id` (`aic_...`), `createdUtc`, `purpose` (`Chat`, `ChatFallback`, `TitleGeneration`, `QueryRewrite`, `Embedding`, `Rerank`, `Moderation`, `ToolRouting`, `Task`, `EndpointTest`, `HealthCheck`), `role`, `assistantId`, `endpointId`, `runtime`, `model`, `executorPosition`, `escalationReason`, `outcome`, `errorCode`, `isFallback`, `isTest`, `promptTokens`, `completionTokens`, `reasoningTokens`, `latencyMs`, `costUsd`, `jobKey`, `jobRunId`, `jobItemId`, `labels`, `tags`, and the telemetry below. Rows are kept `ai.callRetentionDays` (default 90) and pruned daily by the built-in job `ai.callsPrune`.

`AiCall` telemetry (every field is null when the provider or the call path does not report it; [docs/AI_TELEMETRY.md](docs/AI_TELEMETRY.md) says which provider reports what):

| Field | Meaning |
|---|---|
| `inputTokens`, `outputTokens` | `promptTokens` and `completionTokens` under provider-neutral names (always present) |
| `totalTokens` | The provider's total, else prompt plus completion plus reasoning tokens |
| `cachedReadTokens`, `cacheCreationTokens` | Input tokens read from and written to the provider's prompt cache. Inside `promptTokens` on OpenAI, Azure OpenAI, OpenAI compatible, Gemini, Vertex AI, and Cohere; additional to it on Anthropic and Bedrock |
| `inputTextTokens`, `inputImageTokens`, `inputAudioTokens`, `inputVideoTokens` | Input tokens by modality (Gemini, Vertex AI) |
| `timeToFirstTokenMs` | Time to the first streamed text or reasoning token; null for a call that does not stream |
| `timeToLastTokenMs` | Time to the last token (for a call that does not stream, until the whole response arrived) |
| `streamingMs` | Total streaming time, last token minus first token |
| `latencyMs` | Overall time of the call (always present) |
| `queueMs` | Wait for the endpoint's concurrency slot |
| `tokensPerSecond` | Output tokens per second |
| `loadMs`, `promptEvalMs`, `evalMs` | Model load, prompt evaluation, and generation time (Ollama) |
| `toolCalls`, `toolExecMs` | Tool calls this model call requested, and the time the engine spent running them (attributed to the call that asked for the tools; `latencyMs` never includes it) |
| `retries` | Earlier attempts of this call on the same endpoint (each attempt has its own row) |
| `statusCode`, `finishReason` | HTTP status and the provider's finish reason |
| `streamed`, `chunks` | Whether the call streamed, and how many chunks arrived |
| `requestBytes`, `responseBytes` | Request and response body sizes; `responseBytes` is null for a streamed response, which declares no length |
| `responseId`, `providerModel` | The provider's response id (a harness's session id) and the model it reported |
| `reasoningEffort` | The reasoning value actually sent on the wire |
| `exitCode` | Exit code of a harness process |
| `costUsd` | From the endpoint's `pricing` (0 without prices), or the cost a harness reports |

### Harness profiles

A harness profile is the administrator's configuration of one CLI agent harness runtime ([docs/HARNESSES.md](docs/HARNESSES.md), "Harness profiles"): the default model, the permission mode, the timeout, the job tool call limit, whether web research is allowed, a base URL, and the credential. One profile per runtime; a runtime without a saved profile uses the worker's environment and the `jobs.harness*` settings. A profile changes what a harness call receives, never where harnesses run (instances with `jobs.runHarnesses`, inside a job item).

| Method and path | Body | Success | Errors |
|---|---|---|---|
| `GET /api/v1/admin/ai/harnesses`, `POST /api/v1/admin/ai/harnesses/enumerate` | `200 EnumerationResult<AiHarnessProfile>` | `403 Forbidden` |
| `GET /api/v1/admin/ai/harnesses/{runtime}` | | `200 AiHarnessProfile` | `403 Forbidden`, `404 NotFound` |
| `PUT /api/v1/admin/ai/harnesses/{runtime}` | `{ "model"?, "permissionMode"?, "timeoutSeconds"?, "maxToolCalls"?, "allowWebResearch"?, "baseUrl"?, "credentialType"?, "credential"? }` | `200 AiHarnessProfile` (audited `ai.harness.update`, without the credential) | `400 ValidationFailed`, `403 Forbidden`, `404 NotFound` |
| `POST /api/v1/admin/ai/harnesses/{runtime}/reveal` | `{ "reason" }` (10 to 500 characters) | `200 { "field": "credential", "headerName": null, "value" }` (`Cache-Control: no-store`; audited `ai.harness.reveal` without the value) | `400 ValidationFailed`, `403 RevealNotAllowed`, `404 NotFound` (no stored credential), `429 RateLimited` |

`PUT` replaces the profile: an omitted or null field returns to its default. Fields: `model` (at most 200 characters; used when a role executor names no model), `permissionMode` (`Restricted` or `ReadOnly`, or null for the mode each job asks for; `WorkspaceWrite` and `Unrestricted` are refused), `timeoutSeconds` (30 to 3600, or null for `jobs.harnessTimeoutSeconds`), `maxToolCalls` (1 to 500, or null for `jobs.harnessMaxToolCalls`), `allowWebResearch` (default true; false turns web search and fetch off for the runtime even when a job asks for them), `baseUrl` (absolute `http` or `https`, for runtimes that take one), `credentialType` (`Environment`, the default, or `ApiKey`, `AuthToken`, `OAuthToken` as the runtime accepts), and `credential` (at most 16384 characters).

`credential` is write-only, like an endpoint's `authValue`: omit it to keep the stored value, send a value to replace it. It is stored encrypted with `secrets.encryptionKey`, rewritten by key rotation and by `POST /api/v1/admin/ai/endpoints/reencrypt`, and never returned by a read (`hasCredential` says whether one is stored). Choosing `Environment` deletes it. The reveal route follows the endpoint reveal rules: a SystemAdmin member session signed in within `ai.revealRecentSignInMinutes`, never the admin token, with a reason, off when `ai.allowSecretReveal` is false.

| Runtime | Credential types | Stored credential is passed as | Base URL is passed as |
|---|---|---|---|
| `ClaudeCode` | `Environment`, `ApiKey`, `OAuthToken`, `AuthToken` (needs `baseUrl`) | `ANTHROPIC_API_KEY`, `CLAUDE_CODE_OAUTH_TOKEN`, or `ANTHROPIC_AUTH_TOKEN` | `ANTHROPIC_BASE_URL` |
| `Codex` | `Environment`, `ApiKey` | `CODEX_API_KEY` and `OPENAI_API_KEY` | `OPENAI_BASE_URL` |
| `GeminiCli` | `Environment`, `ApiKey` | `GEMINI_API_KEY` | `GOOGLE_GEMINI_BASE_URL` |
| `CursorCli` | `Environment`, `ApiKey` | `CURSOR_API_KEY` | (none) |
| `Mux`, `OpenCode`, `Pi` | `Environment` | (their own provider configuration) | (none) |

A stored credential replaces every vendor credential variable the worker's environment holds for that runtime, so an inherited key can never shadow it. A stored credential that is missing or cannot be decrypted fails the call (`HarnessCredentialUnreadable`) instead of falling back to the worker's key.

`AiHarnessProfile`: `runtime`, `supportsJobTools`, `credentialTypes`, `baseUrlVariable`, `credentialVariables` (the variables the stored credential is passed in), `credentialVariablesByType` (the same for each stored type the runtime accepts), `environmentVariables` (the vendor variables kept from the worker's environment with the `Environment` type), `model`, `permissionMode`, `timeoutSeconds`, `maxToolCalls`, `allowWebResearch`, `baseUrl`, `credentialType`, `hasCredential`, `defaultTimeoutSeconds`, `defaultMaxToolCalls`, `runsOnThisInstance`, `saved`, `updatedByUserId`, `updatedUtc`.

## Admin: jobs (SystemAdmin)

The job engine (INTEGRATION.md section 31, [docs/JOBS.md](docs/JOBS.md)). Each registered job handler has one definition with a schedule, per-run concurrency, attempts per item, and an optional run budget. A run plans items; items retry with exponential backoff and jitter (`jobs.retryBaseSeconds`, `jobs.retryMaxSeconds`) and move to `Dead` after their last attempt. Every job of a property shares the monthly cap `jobs.monthlyBudgetUsd` (default $2,000): at the cap, run now answers `409 BudgetExceeded`, scheduled runs are recorded as `Skipped`, and running runs stop claiming items. Items that need a CLI agent harness are claimed only by instances with `jobs.runHarnesses`. Built-in jobs: `jobs.prune` (daily, deletes finished runs older than `jobs.retentionDays`), `ai.callsPrune`, and the content review jobs `content.review`, `content.reviewDigest`, and `content.changesPrune` (see Admin: content review), and `ai.scopeBenchmark` (manual, the scope benchmark of [Admin: AI prompts](#admin-ai-prompts-systemadmin); one item per case, a summary on the run, dry runs make no model calls).

| Method and path | Body | Success | Errors |
|---|---|---|---|
| `GET /api/v1/admin/jobs`, `POST /api/v1/admin/jobs/enumerate` | `200 EnumerationResult<JobDefinition>` | `403 Forbidden` |
| `GET /api/v1/admin/jobs/overview` | | `200 JobOverview` | `403 Forbidden` |
| `GET /api/v1/admin/jobs/{key}` | | `200 JobDefinition` | `403 Forbidden`, `404 NotFound` |
| `PUT /api/v1/admin/jobs/{key}` | `{ "enabled"?, "intervalType"?, "intervalValue"?, "timeOfDayMinutesUtc"?, "daysOfWeek"?, "maxConcurrency"?, "maxAttempts"?, "budgetUsdPerRun"? }` | `200 JobDefinition` (next run recomputed; audited `job.update`) | `400 ValidationFailed`, `403 Forbidden`, `404 NotFound` |
| `POST /api/v1/admin/jobs/{key}/run` | `{ "dryRun"?, "parameters"? }` (`parameters`: a JSON object encoded as a string, at most 16384 characters, validated by the handler) | `201 JobRun` (`Queued`; audited `job.run`) | `400 DryRunNotSupported`, `400 InvalidJobParameters`, `403 Forbidden`, `404 NotFound`, `409 RunActive`, `409 BudgetExceeded`, `409 HandlerMissing` |
| `GET /api/v1/admin/jobs/runs` and `POST .../runs/enumerate` | `EnumerationQuery` (filters `key`, `status`, `trigger`; newest first) | `200 EnumerationResult<JobRun>` | `400 ValidationFailed`, `403 Forbidden` |
| `GET /api/v1/admin/jobs/runs/{id}` | | `200 JobRun` (live counts) | `403 Forbidden`, `404 NotFound` |
| `POST /api/v1/admin/jobs/runs/{id}/cancel` | `{}` | `200 JobRun` (`Cancelled`; pending items skipped; audited `job.cancel`) | `403 Forbidden`, `404 NotFound`, `409 RunFinished` |
| `GET /api/v1/admin/jobs/runs/{id}/items` and `POST .../items/enumerate` | `EnumerationQuery` (filters `status`, `key`) | `200 EnumerationResult<JobItem>` | `400 ValidationFailed`, `403 Forbidden`, `404 NotFound` |
| `GET /api/v1/admin/jobs/items` and `POST .../items/enumerate` | `EnumerationQuery` (filters `runId`, `key`, `status`; `status: ["Dead"]` is the dead-letter view; search over target key and error) | `200 EnumerationResult<JobItem>` | `400 ValidationFailed`, `403 Forbidden` |
| `POST /api/v1/admin/jobs/items/{id}/retry` | `{}` | `200 JobItem` (`Pending`, attempts reset, run reopened; audited `job.item.retry`) | `403 Forbidden`, `404 NotFound`, `409 NotRetryable` |

Interval types and values: `EveryNMinutes` (5 to 1440), `EveryNHours` (1 to 168), `EveryNDays` (1 to 90, at `timeOfDayMinutesUtc`), `DaysOfWeek` (`daysOfWeek` names, at `timeOfDayMinutesUtc`), `Manual`. `timeOfDayMinutesUtc` 0 to 1439; `maxConcurrency` 1 to 64; `maxAttempts` 1 to 10; `budgetUsdPerRun` 0 (no cap) to 100000.

`JobDefinition`: `id` (`jbd_...`), `key`, `name`, `description`, `enabled`, `intervalType`, `intervalValue`, `timeOfDayMinutesUtc`, `daysOfWeek`, `maxConcurrency`, `maxAttempts`, `budgetUsdPerRun`, `requiresHarness`, `supportsDryRun`, `handlerAvailable`, `nextRunUtc`, `lastRunUtc`, `latestRun`, `updatedUtc`. `JobRun`: `id` (`jbr_...`), `key`, `trigger` (`Schedule`, `Manual`, `DryRun`), `status` (`Queued`, `Running`, `Succeeded`, `PartiallySucceeded`, `Failed`, `Cancelled`, `Skipped`), `isDryRun`, `skippedReason`, `error`, `requestedByUserId`, `createdUtc`, `startedUtc`, `completedUtc`, `itemsTotal`, `itemsOpen`, `itemsSucceeded`, `itemsFailed`, `itemsDead`, `itemsSkipped`, `costUsd`, `parameters` (handler parameters JSON, or null), `summary` (the handler's summary JSON when the run finished, or null; for `ai.scopeBenchmark`, the pass rate per category, the prompt versions, and `gatePassed`), `labels`, `tags`. `JobItem`: `id` (`jbi_...`), `runId`, `key`, `targetKey`, `payload`, `status` (`Pending`, `Running`, `Succeeded`, `Failed`, `Dead`, `Skipped`), `attempts`, `maxAttempts`, `nextAttemptUtc`, `requiresHarness`, `result`, `error`, `costUsd`, `createdUtc`, `startedUtc`, `completedUtc`, `labels`, `tags`. `JobOverview`: `enabled`, `runsHarnesses`, `monthStartUtc`, `monthCostUsd`, `monthlyBudgetUsd`, `remainingUsd`, `openRuns`, `definitions`, `enabledDefinitions`.

Metrics: `trelliskit.jobs.runs` (key, outcome), `trelliskit.jobs.items` (key, outcome), `trelliskit.jobs.item.duration`, `trelliskit.jobs.cost`.

## Admin: content review (SystemAdmin)

The content changelog and its Terms of Service review . Database triggers write one `contentchanges` row for every create, edit, and delete of a member contribution, in the same transaction as the change, so a rolled-back change leaves no row and nothing escapes review. Watched tables: feed posts, comments, forum threads and poll options, direct messages, venue reviews and their replies, profiles (`users`) and profile field values, media uploads, directory submissions and their fields, venue media, events, lessons, ideas, support tickets and replies, and assistant messages. Edits are recorded only when the text or media changes (counters, moderation state, and reads write nothing); staff promotion cards and generated event occurrences are not member contributions and are skipped.

- **Actor and client.** The signed-in member and the client of the request: `X-TK-Client` (`web`, `mobile`, or `api`) when sent, else `X-Client` (the apps send `web` and `mobile`), else the User-Agent (browsers are `Web`, native stacks `Mobile`), else `Api`. Changes made outside a request (workers, jobs, seeds) are attributed to the row's author with client `System`.
- **Review.** The built-in job `content.review` (every 5 minutes, enabled by default) reviews `Pending` rows oldest first (`contentReview.batchSize`, 200) through the `Moderation` task role with the admin-editable prompt `contentReview.classifier`; the reply is one JSON object (`verdict` `Clean` or `Flagged`, `categories`, `severity`, `note`). While AI is off or the role has no executors, the job plans nothing and rows wait in `Pending` (the summary says why). Model errors and unreadable verdicts count toward `contentReview.maxAttempts` (5), after which the row is flagged for an administrator. Deletions and rows without text are marked `Clean` by the engine; media references are recorded (the reviewer reads text today). Each review is in the call ledger under the job.
- **Warnings.** Every flagged row sends one `ContentFlagged` notification (in-app and push, category `Moderation`) to each SystemAdmin (at most `contentReview.maxNotifiedAdmins`, 25), linking to `/admin/content-review?id=<id>`. The daily job `content.reviewDigest` (15:00 UTC) emails `staff_content_review_digest` to the SystemAdmins (or the template's staff recipients) when anything was flagged in the last 24 hours, once per day.
- **Retention.** `content.changesPrune` (daily) deletes `Clean`, `Dismissed`, and `Actioned` rows older than `contentReview.retentionDays` (400). Account deletion clears the text and media of the member's rows and dismisses their `Pending` rows.

| Method and path | Body | Success | Errors |
|---|---|---|---|
| `GET /api/v1/admin/content-review/summary` | | `200 ContentReviewSummary` | `403 Forbidden` |
| `GET /api/v1/admin/content-review` and `POST .../content-review/enumerate` | `EnumerationQuery`: filters `status`, `category`, `severity`, `targetType`, `client`, `action` (arrays or comma-separated), `minimumSeverity`, `targetId`, `actorUserId`, `authorUserId`; orderings `CreatedDescending` (default), `CreatedAscending`, `SeverityDescending`, `ReviewedDescending`; search over the snapshot, the reviewer note, and the target id; labels and tags (`ContentChange`); exact totals | `200 EnumerationResult<ContentChange>` | `400 ValidationFailed`, `403 Forbidden` |
| `GET /api/v1/admin/content-review/{id}` | | `200 ContentChange` | `403 Forbidden`, `404 NotFound` |
| `POST /api/v1/admin/content-review/{id}/action` | `{ "action": "Dismiss" \| "Hide" \| "Remove" \| "Warn" \| "Mute" \| "Ban", "reason" (3 to 500; optional for Dismiss), "note"? (at most 2000), "untilUtc"? (Mute; null is indefinite), "notifyAuthor"? (Hide and Remove) }` | `200 ContentReviewActionResult` (audited `contentReview.<action>`) | `400 ValidationFailed`, `403 Forbidden`, `404 NotFound`, `409 NotModeratable` (Hide and Remove on a type without them), `409 NoAuthor`, and the errors of the routed action (`409 InvalidModerationState`, `409 CannotBanSelf`) |

Actions run through the existing systems: `Hide` and `Remove` call `POST /api/v1/admin/moderation/actions` on the content (the row's `moderationTargetType`; a `VenueReviewReply` acts on its `VenueReview`), `Warn` records a `Warn` moderation action and sends the author a `ModerationAction` notification ("A moderator sent you a warning about your post"), and `Mute` and `Ban` call the member actions of `/api/v1/admin/members/{id}` on the author. `Dismiss` marks the row `Dismissed`; every other action marks it `Actioned` and keeps the moderation action id.

`ContentChange`: `id` (`chg_...`), `targetType` (`FeedPost`, `Comment`, `Thread`, `PollOption`, `Message`, `VenueReview`, `VenueReviewReply`, `User`, `Media`, `DirectorySubmission`, `VenueMedia`, `Event`, `Lesson`, `Idea`, `Ticket`, `TicketMessage`, `AssistantMessage`), `targetId`, `contextType` and `contextId` (for example the `FeedPost` of a comment, the `Conversation` of a message, the `ProfileField` of a profile value), `action` (`Create`, `Update`, `Delete`), `actor` and `author` (`UserSummary` or null), `client` (`Web`, `Mobile`, `Api`, `System`), `snapshot` ("column: value" lines, at most 20000 characters; empty for deletions), `mediaRefs` (at most 50), `reviewStatus` (`Pending`, `Clean`, `Flagged`, `Dismissed`, `Actioned`), `reviewedUtc`, `reviewedBy` (`model:<model>`, `user:<id>`, `admin-token`, or `system`), `reviewerKind` (`Model`, `Administrator`, `System`), `categories` (`SexualContent`, `HarassmentAndHate`, `Violence`, `IllegalActivity`, `SelfHarm`, `Spam`, `Scam`, `PersonalData`, `Profanity`), `severity` (`None`, `Low`, `Medium`, `High`, `Critical`), `reviewerNote`, `reviewAttempts`, `lastError`, `notifiedUtc`, `moderationActionId`, `moderationTargetType` (null when Hide and Remove do not apply), `createdUtc`, `updatedUtc`, `labels`, `tags`. `ContentReviewSummary`: `pending`, `flagged`, `flaggedLast24Hours`, `failing`, `oldestPendingUtc`, `reviewerReady`, `reviewerProblem` (`AiOff`, `NoExecutors`, `JobsOff`, `JobDisabled`, or null), `reviewerMessage`, `lastRunStatus`, `lastRunUtc`, `nextRunUtc`. `ContentReviewActionResult`: `change`, `action`, `moderationActionId`.

Settings: `contentReview.batchSize`, `contentReview.maxReviewCharacters` (6000), `contentReview.maxOutputTokens` (300), `contentReview.maxAttempts`, `contentReview.retentionDays`, `contentReview.maxNotifiedAdmins`.
## Admin: usage metering (Admin)

Per-property usage over time for the admin console Usage page and future usage-based billing (binding spec `docs/USAGE_METERING_CONTRACT.md`; no prices or invoices). A per-property `UsageWorker` rolls usage up every `usage.rollupIntervalMinutes` (5): hourly rows (kept `usage.hourlyRetentionDays`, 90) and daily rows per local day in the property's `usage.timeZone` (property setting, default `UTC`; weeks start on `usage.weekStartsOn`, default `Monday`), kept forever by default. Daily rows close `usage.closeAfterHours` (48) after their day ends and never change again. The first passes backfill history from existing rows.

| Method and path | Body or query | Success | Errors |
|---|---|---|---|
| `GET /api/v1/admin/usage/summary` | `?period=LastMonth&from=&to=&bucket=&meters=`. `period`: `LastDay` (trailing 24 hours, hourly), `LastWeek`, `LastMonth`, `Last90Days` (trailing 7, 30, and 90 local days including today), `PreviousQuarter` (the whole previous calendar quarter), `ThisQuarter` (calendar quarter to date), `LastYear` (365 days, weekly), `ThisWeek` (weeks start on `usage.weekStartsOn`, Monday by default), `ThisMonth`, `ThisYear`, `Range` (`from` and `to`: local dates `YYYY-MM-DD` with `to` inclusive, or ISO 8601 date-times with an offset; at most 1830 days; `to` clamps to now). `bucket` overrides the default (Hour up to 2 days, Day up to 93 days, Week up to 400 days, else Month; at most 400 buckets; Hour at most 14 days inside the hourly retention). `meters`: comma-separated meter ids or groups (`users`, `content`, `interactions`, `media`, `storage`, `platform`, `ai`) | `200 EnumerationResult<UsageObservationRow>` | `400 ValidationFailed` (`fieldErrors.period`, `from`, `to`, `bucket`, `meters`) |
| `GET /api/v1/admin/usage`, `POST /api/v1/admin/usage/enumerate` | `EnumerationQuery`; filters `meter` (an id or a group), `fromDay`, `toDay` (local dates, inclusive), `closed`; orderings `CreatedDescending` (default), `CreatedAscending` (both on the period start, which is also what `createdAfterUtc` and `createdBeforeUtc` filter), `MeterAscending`, `ValueDescending`; search over meter; labels and tags (`UsageRecord`) | `200 EnumerationResult<UsageRecord>` (exact totals) | `400 ValidationFailed`, `400 InvalidContinuationToken` |
| `GET /api/v1/admin/usage/export` | `?period=&from=&to=&meters=&format=json\|csv&granularity=Day\|Hour` (Hour: at most 31 days inside the hourly retention) | `200` attachment `usage-<propertyId>-<fromDay>-<toDay>.json` or `.csv` | `400 ValidationFailed` |

`UsageObservationRow`: `{ id, meter, group, kind, unit, total, previousTotal, change, exact, startUtc, endUtc, label, value, labels, tags }`. The response is `EnumerationResult<UsageObservationRow>` plus scalar calendar/context fields `propertyId`, `period`, `timeZone`, `weekStartsOn`, `fromUtc`, `toUtc`, `previousFromUtc`, `previousToUtc`, `bucket`, `dataThroughUtc`, `backfillComplete`, `storageReconciliation`, `generatedUtc`. One row is one meter in one bucket. The selected source window has at most 400 buckets; transport pages follow normal admin limits. Period totals, previous totals, percentage changes and meter exactness retain the usage contract semantics. POST `/api/v1/admin/usage/summary/enumerate` places period/from/to/bucket/meters in `filters`.

`UsageRecord`: `{ id (usd_<yyyymmdd>_<meter>), propertyId, meter, group, kind, unit, day, timeZone, periodStartUtc, periodEndUtc, value, closed, computedUtc, createdUtc, labels, tags }`.

Meters (kind; unit `count` unless noted): `users.total` (Gauge), `users.new`, `users.deleted`, `users.active` (Distinct: signed in or made an authenticated request), `users.activeWeekly`, `users.activeMonthly` (Distinct, daily rows only: WAU and MAU as of the day), `posts.new` (feed posts plus forum threads created), `posts.total` (Gauge), `comments.new`, `comments.total` (Gauge), `interactions.new` (the sum of `reactions.new`, `pollVotes.new`, `follows.new` (explicit forum and thread follows), and `assistantMessages.new`), `images.new`, `images.bytes` (bytes), `images.total` (Gauge), `videos.new`, `videos.bytes` (bytes), `videos.total` (Gauge), `storage.total`, `storage.originals`, `storage.imageVariants`, `storage.videoRenditions` (measured from the object store), `storage.updateAssets`, `storage.exports` (Gauge, bytes), `api.requests`, `api.errors` (5xx; counted on write), `push.sent`, `email.sent`, `ai.requests`, `ai.promptTokens`, `ai.completionTokens` (tokens), `ai.costMicros` (usdMicros, non-test AI calls).

Export JSON (`schemaVersion: 1`): `{ schemaVersion, propertyId, propertyName, period, fromUtc, toUtc, fromDay, toDay, timeZone, granularity, generatedUtc, complete, closedThroughDay, totals: [ { meter, kind, unit, value, exact } ], rows: [ { meter, day, periodStartUtc, periodEndUtc, value, closed, timeZone } ] }` (hourly rows: `{ meter, periodStartUtc, periodEndUtc, value }`); `complete` is true when every daily row of the range is closed (bill only complete ranges). CSV columns: `propertyId, meter, kind, unit, granularity, day, periodStartUtc, periodEndUtc, value, closed, timeZone`.

---

## Library (Member, object gated)

Design record: `docs/PHASE4_CONTRACT.md` section 5 with resolved decisions 3, 16, 17, and 18. The library is the `library` feature: `features.library: false` answers `404 NotAvailable` on every member, me, public, and admin library route; the `library` kill switch answers `503 FeatureDisabled` on member, me, and public routes (staff routes stay open); `libraryDownloads` blocks only the download routes. Member routes need a session (anonymous-eligible reads also work signed out when `access.anonymousBrowsing` is on); each object then decides:

| Caller | Sees in full | Sees as locked stub | Takes part (play, progress, comments, reactions) |
|---|---|---|---|
| Library staff (SystemAdmin, LibraryEditor) | Everything in any status | | Everything |
| Member | Every published audience | | Yes |
| Free account, PartiallyGated | `Public`, `SignedIn` | `Members` series and lessons (`locked: true`, `viewer.requires: "Membership"`) | Lessons whose audience is `Public` or `SignedIn` (samples) |
| Free account, Open | Everything (`Members` is `SignedIn` content) | | Yes |
| Visitor | `Public` only (no stubs) | | No (`401`); `Public` lessons still play |

A series the caller cannot read is still listed (locked) when it holds lessons the caller may open, and its lesson list then shows only those lessons. Locked stubs keep the summary fields; `description`, `video`, `markers`, `resources`, and `captions` are empty. Taking part on a locked lesson answers `402 MembershipRequired` (`401 Unauthorized` signed out) with the access denial body (`required`, `feature: "library"`, `targetType`, `targetId`).

### Shapes

- `SeriesSummary`: `{ id, slug, title, summary, cover: Media | null, levels [{ key, label, color }], status, sortOrder, lessonCount, totalDurationSeconds, myProgress: { completedLessons, percent, lastLessonId, lastWatchedUtc } | null, isFollowed, audience, locked, labels, tags, viewer { canFollow, canEdit, canOpen, requires }, publishedUtc, archivedUtc, createdUtc, updatedUtc }`. `Series` adds `description`.
- `LessonSummary`: `{ id, series { id, slug, title }, position, slug, title, summary, kind: Video | Article, video { durationSeconds, posterThumbUrl, blurhash } | null, durationSeconds, hasResources, levels, status, publishAtUtc, audience, downloadable, commentsEnabled, commentCount, reactionTotal, myProgress: LessonProgress | null, locked, labels, tags, viewer { canPlay, canDownload, canComment, canReact, canReport, canEdit, canPromote, canOpen, requires }, publishedUtc, archivedUtc, createdUtc, updatedUtc }`.
- `Lesson`: `LessonSummary` plus `description`, `video: Media | null`, `markers [{ id, seconds, label }]`, `resources [{ id, kind: Document | Link | Reference, title, media: Media | null, url, text }]`, `captions [{ language, label, media: Media }]`, `commentTarget { type: "Lesson", id }`, `reactionCounts`, `myReactions`, `previous` and `next` `{ id, title, canOpen } | null`, `promotion`, and for staff `mediaStatus`, `moderationState`, `notifyStaffOnComment`.
- `LessonProgress`: `{ id, lessonId, positionSeconds, maxPositionSeconds, watchedSeconds, percent, completed, completedUtc, lastWatchedUtc, labels, tags }`.
- Document media (`kind: "Document"`, PDF resources and WebVTT captions) carry `media.document { url, fileName }`.

### Routes

| Method and path | Body | Success | Errors |
|---|---|---|---|
| `GET /api/v1/series`, `POST /api/v1/series/enumerate` | `EnumerationQuery`; orderings `SortOrderAscending` (default), `TitleAscending`, `PublishedDescending`, `Relevance`; filters `levels`, `followedOnly`, `progress` (`NotStarted`, `InProgress`, `Completed`), `audience` | `200 EnumerationResult<SeriesSummary>` | `400 ValidationFailed` |
| `GET /api/v1/series/{idOrSlug}` | | `200 Series` (old slugs redirect) | `404 NotFound` |
| `GET`, `POST /api/v1/series/{id}/lessons[/enumerate]` | `EnumerationQuery`; ordering `PositionAscending` | `200 EnumerationResult<LessonSummary>` | `404 NotFound` |
| `PUT`, `DELETE /api/v1/series/{id}/follow` | | `200 { isFollowed }` | `401`, `402`, `404`, `409 FollowLimitReached` |
| `GET /api/v1/series/{id}/lessons` | Published lessons in position order (PositionAscending). A series the caller sees as a locked stub lists only the lessons the caller may open; otherwise Members lessons appear as locked stubs to free accounts. The route needs only a session (or none for anonymous-eligible reads); each object decides: callers who cannot read an object in full get it as a locked stub (PartiallyGated free accounts) or not at all. 404 NotAvailable when features.library is false; 503 FeatureDisabled under the library kill switch. | `200` List a series' lessons | `400` |
| `POST /api/v1/series/{id}/lessons/enumerate` | Published lessons in position order (PositionAscending). A series the caller sees as a locked stub lists only the lessons the caller may open; otherwise Members lessons appear as locked stubs to free accounts. The route needs only a session (or none for anonymous-eligible reads); each object decides: callers who cannot read an object in full get it as a locked stub (PartiallyGated free accounts) or not at all. 404 NotAvailable when features.library is false; 503 FeatureDisabled under the library kill switch. | `200` List a series' lessons | `400` |
| `GET /api/v1/lessons`, `POST /api/v1/lessons/enumerate` | `EnumerationQuery`; orderings `PublishedDescending` (default), `TitleAscending`, `DurationAscending`, `Relevance`; filters `seriesIds` (max 50), `levels`, `kind`, `hasResources`, `minDurationSeconds`, `maxDurationSeconds`, `progress`, `audience` | `200 EnumerationResult<LessonSummary>` | `400 ValidationFailed` |
| `GET /api/v1/lessons/{idOrSlug}` | | `200 Lesson` | `404 NotFound` |
| `PUT /api/v1/lessons/{id}/progress` | `{ positionSeconds, playedSeconds, clientUpdatedUtc, sessionId }` | `200 LessonProgress` | `400 ValidationFailed`, `401`, `402`, `404`, `429 RateLimited` (`library.progressPerMinute` per lesson, `library.progressPerHour`) |
| `DELETE /api/v1/lessons/{id}/progress` | | `204` | `404` |
| `POST /api/v1/lessons/{id}/complete`, `.../uncomplete` | | `200 LessonProgress` | `401`, `402`, `404` |
| `GET`, `POST /api/v1/lessons/{id}/comments[/enumerate]`, `POST /api/v1/lessons/{id}/comments`, `PUT`/`DELETE /api/v1/lessons/{id}/reactions/{key}`, `GET`/`POST .../reactions[/enumerate]` | Phase 2 shapes | Phase 2 | Phase 2, `402`, `403 CommentsDisabled` |
| `POST /api/v1/lessons/{id}/promote`, `.../unpromote` | Phase 3 promotion body | `PromotedLesson` card | Phase 3 (`409 AlreadyPromoted`) |
| `POST /api/v1/lessons/{id}/downloads` | `{ deviceId, platform: Ios \| Android }` | `201 LessonDownload` | `400`, `403 DownloadsNotAllowed`, `404`, `409 DownloadLimitReached`, `429 RateLimited` (`library.downloads.perDay`), `503 FeatureDisabled` |
| `GET /api/v1/me/library/continue`, `POST .../continue/enumerate` | `EnumerationQuery` (`LastWatchedDescending`, default `maxResults` 10) | `200 EnumerationResult<{ lesson: LessonSummary, progress: LessonProgress }>` | |
| `GET /api/v1/me/library/progress`, `POST .../progress/enumerate` | filters `completed`, `seriesId` | `200 EnumerationResult<LessonProgress>` | |
| `GET /api/v1/me/library/downloads`, `POST .../downloads/enumerate` | filters `activeOnly`, `deviceId` | `200 EnumerationResult<LessonDownload>` (no `url`) | |
| `POST /api/v1/me/library/downloads/{id}/renew` | | `200 LessonDownload` (fresh `url`) | `403 DownloadsNotAllowed` (the license is revoked; delete the file), `404` |
| `DELETE /api/v1/me/library/downloads/{id}` | | `204` | `404` |
| `GET /api/v1/library/levels`, `POST /api/v1/library/levels/enumerate` | | `200 EnumerationResult<{ key, label, color, labels, tags }>` from the configured source | |

### Progress rules

`positionSeconds` is clamped to the duration; `playedSeconds` is clamped to 2 times `library.progressIntervalSeconds` (15), so skipping does not count, and summed into `watchedSeconds` (capped at 3 times the duration). `percent` is `round(100 * min(maxPositionSeconds, watchedSeconds) / duration)`; completion at `library.completionThresholdPercent` (90) is sticky (only `uncomplete` clears it). A position within the last `library.resumeTailSeconds` (10) resumes from 0. An update older than the stored `clientUpdatedUtc` keeps the resume point while furthest position, watched time, and completion still merge; an update with the same `clientUpdatedUtc` is a replay and changes nothing; offline entries older than `library.offlineReplayDays` (7) are dropped (`200` with the stored progress). Each merge is one atomic statement, so concurrent updates are exact. Every update records a playback session (`sessionId`; platform from `X-Client-Platform`). Completing a lesson follows its series (`library.autoFollowOnProgress`) unless the member unfollowed it.

### Downloads

Off by default (`library.downloads.enabled`, per property in the library settings). A license needs a downloadable `Video` lesson whose video has the `download_720p` rendition (produced by ffmpeg when downloads are on; the passthrough processor copies the original; MediaConvert writes it when the job template has the opt-in download output group, infra `MediaSettings.DownloadRendition`, and `media.mediaConvertDownloadRendition` is true so every job sets that group's destination `s3://<transcoded>/<propertyId>/video/<id>/download`). Without the rendition, the license and the signed URL serve the original upload instead (`sizeBytes` is the original's size, `sha256` null, the original content type and file name); the rendition is preferred as soon as it exists and, in gated models, a membership. `LessonDownload`: `{ id, lessonId, deviceId, platform, url, urlExpiresUtc, licenseExpiresUtc, sizeBytes, sha256, revokedUtc, revokeReason, lastRenewedUtc, createdUtc, labels, tags }`. `url` is a signed, short-lived (`library.downloads.urlMinutes`, 30) link to `GET /api/v1/public/lessons/downloads/{id}?exp=&sig=` (no Authorization header; Range supported). Limits: `library.downloads.maxActivePerMember` (25) licenses and `library.downloads.maxDevicesPerMember` (3) devices; the same device and lesson reuse their license. Files are not DRM protected.

## Public sample lessons (Anonymous)

| Method and path | Success | Errors |
|---|---|---|
| `GET /api/v1/public/lessons`, `POST /api/v1/public/lessons/enumerate` | `200 EnumerationResult<PublicLesson>` (`Public` published lessons of published series, `PublishedDescending`, `maxResults` at most 20) | `404 NotAvailable` (`features.library` or `library.publicSamplesEnabled` off), `429 RateLimited` (`rateLimits.publicFeedPerMinute` per IP) |
| `GET /api/v1/public/lessons/{idOrSlug}` | `200 PublicLesson` | `404 NotFound` or `NotAvailable`, `429` |
| `GET /api/v1/public/lessons/downloads/{id}?exp=&sig=` | `200 video/mp4` (signed download rendition) | `403 InvalidSignature` or `DownloadsNotAllowed`, `404`, `503 FeatureDisabled` |

`PublicLesson` is `{ id, series { id, slug, title }, position, slug, title, summary, description, kind, durationSeconds, levels, audience, video, markers, captions, commentCount, reactionTotal, labels, tags, publishedUtc }`: the lesson summary plus description, markers, and captions, with the video and caption media from the public paths (playable without an account or media grant) and without `ownerUserId`. It never carries `viewer`, `myProgress`, `myReactions`, `reactionCounts`, `commentTarget`, comments, resources, neighbors, `promotion`, or staff fields (`moderationState`, `mediaStatus`, `notifyStaffOnComment`, `status`, `publishAtUtc`, `archivedUtc`). `Cache-Control: public, max-age=60`. In `FullyGated` these are the only lesson routes visitors reach; the website shows a public lesson at `/lessons/{slug}` for signed-out visitors. With anonymous browsing (not `FullyGated`), `GET /api/v1/lessons/{idOrSlug}` of a `Public` lesson for a signed-out guest also serves the public media paths, so guests play it without a media grant.

## Library administration (library staff: SystemAdmin or LibraryEditor)

The route group needs a session; the service answers `403 Forbidden` to everyone else. Grant `LibraryEditor` with `POST /api/v1/admin/moderators { userId, resourceType: "Library", resourceId: "*" }`. Library settings and levels are SystemAdmin only. Audit actions: `series.create|update|publish|archive|unarchive|delete|order`, `lesson.create|update|publish|schedule|archive|unarchive|delete|move|order`, `library.settings.update`, `library.level.create|update|delete|order`, `library.viewingHistory.read`.

| Method and path | Body | Success | Errors |
|---|---|---|---|
| `GET /api/v1/admin/series`, `POST .../enumerate` | filters `levels`, `status`, `audience` | `200 EnumerationResult<Series>` | |
| `POST /api/v1/admin/series` | `{ title, slug?, summary, description, coverMediaId, levels, audience, labels, tags }` | `201 Series` (Draft, last in order) | `400 ValidationFailed` or `UnknownOption`, `409 SlugInUse` or `SeriesLimitReached` |
| `GET`, `PUT`, `DELETE /api/v1/admin/series/{id}` | PUT: any create field plus `narrowLessons` (default true) | `200 Series`, `204` | `404`, `409 SlugInUse`, `409 SeriesNotEmpty` (delete; archive instead) |
| `POST /api/v1/admin/series/{id}/publish`, `.../archive`, `.../unarchive` | | `200 Series` | `404` |
| `PUT /api/v1/admin/series/order` | `{ seriesIds }` (every non-archived series once) | `200 { updated }` | `400 ValidationFailed` |
| `POST /api/v1/admin/series/{id}/lessons` | `{ title, slug?, summary, description, kind, videoMediaId, levels, audience, downloadable, commentsEnabled, notifyStaffOnComment, markers [{ seconds, label }], resources [{ kind, title, mediaId, url, text }], captions [{ language, label, mediaId }], labels, tags }` | `201 Lesson` (Draft, last in the series) | `400`, `404`, `409 SlugInUse`, `LessonLimitReached`, or `MediaNotAttachable` |
| `PUT /api/v1/admin/series/{id}/lessons/order` | `{ lessonIds }` | `200 { updated }` | `400`, `404` |
| `POST /api/v1/admin/series/{id}/unarchive` | Audited series.unarchive. Library staff (SystemAdmin or LibraryEditor); 403 Forbidden otherwise. | `200` Unarchive a series | `403`, `404` |
| `GET /api/v1/admin/lessons`, `POST .../enumerate` | filters `seriesId`, `status`, `hasVideo`, `mediaStatus`, `kind`, `levels`, `audience` | `200 EnumerationResult<LessonSummary>` | |
| `GET`, `PUT`, `DELETE /api/v1/admin/lessons/{id}` | PUT: any create field; lists replace | `200 Lesson`, `204` (soft delete; downloads revoked) | `400`, `404`, `409` |
| `POST /api/v1/admin/lessons/{id}/publish` | | `200 Lesson` (`Published`, or `Processing` until the video is Ready) | `400 LessonVideoRequired` |
| `POST /api/v1/admin/lessons/{id}/schedule` | `{ publishAtUtc }` | `200 Lesson` (`Scheduled`) | `400`, `409 AlreadyPublished` |
| `POST /api/v1/admin/lessons/{id}/archive`, `.../unarchive` | | `200 Lesson` | `404` |
| `POST /api/v1/admin/lessons/{id}/move` | `{ seriesId, position? }` | `200 Lesson` | `400`, `404`, `409 SlugInUse` or `LessonLimitReached` |
| `GET`, `POST /api/v1/admin/lessons/{id}/viewers[/enumerate]` | filter `completed` | `200 EnumerationResult<{ user, progress, plays, labels, tags }>` (audited) | `404` |
| `GET /api/v1/admin/lessons/{id}/viewers` | Members with progress on the lesson, LastWatchedDescending. Filter completed. Every call is audited as library.viewingHistory.read (resolved decision 17; disclose in the privacy policy). Library staff (SystemAdmin or LibraryEditor); 403 Forbidden otherwise. | `200` List a lesson's viewers | `400` |
| `POST /api/v1/admin/lessons/{id}/viewers/enumerate` | Members with progress on the lesson, LastWatchedDescending. Filter completed. Every call is audited as library.viewingHistory.read (resolved decision 17; disclose in the privacy policy). Library staff (SystemAdmin or LibraryEditor); 403 Forbidden otherwise. | `200` List a lesson's viewers | `400` |
| `GET`, `POST /api/v1/admin/library/members/{userId}/progress[/enumerate]` | filters `completed`, `seriesId` | `200 EnumerationResult<{ lesson, progress, plays, playedSeconds }>` (audited) | `404` |
| `GET /api/v1/admin/library/members/{userId}/progress` | The member's lesson progress with playback sessions, LastWatchedDescending. Filters completed, seriesId. Every call is audited as library.viewingHistory.read (resolved decision 17; disclose in the privacy policy). Library staff (SystemAdmin or LibraryEditor); 403 Forbidden otherwise. | `200` List a member's viewing history | `400` |
| `POST /api/v1/admin/library/members/{userId}/progress/enumerate` | The member's lesson progress with playback sessions, LastWatchedDescending. Filters completed, seriesId. Every call is audited as library.viewingHistory.read (resolved decision 17; disclose in the privacy policy). Library staff (SystemAdmin or LibraryEditor); 403 Forbidden otherwise. | `200` List a member's viewing history | `400` |
| `GET /api/v1/admin/metrics/library?days=30` | days 7 to 365 | `200 { days, playsByDay, completionsByDay, activeLearners, topLessons, completionRateBySeries }` (aggregates only) | `400` |
| `GET /api/v1/admin/library/settings` | | `200 { levelSource, levelFieldKey, downloadsEnabled, completionThresholdPercent, publicSamplesEnabled, autoFollowOnProgress, commentsEnabledByDefault, downloadsNotice, updatedUtc }` | |
| `PUT /api/v1/admin/library/settings` (SystemAdmin) | any of the fields above (null clears a property override) | `200` settings | `400 ValidationFailed` |
| `GET`, `POST /api/v1/admin/library/levels[/enumerate]` (SystemAdmin) | filter `includeRetired` | `200 EnumerationResult<LibraryLevel>` | |
| `POST /api/v1/admin/library/levels` (SystemAdmin) | `{ key, label, color, sortOrder }` | `201 LibraryLevel` | `400`, `409 Conflict`, `LevelLimitReached`, or `LevelSourceNotCustom` |
| `PUT /api/v1/admin/library/levels/order` (SystemAdmin) | `{ levelIds }` | `200 { updated }` | `400`, `409 LevelSourceNotCustom` |
| `PUT`, `DELETE /api/v1/admin/library/levels/{id}` (SystemAdmin) | `{ label, color, sortOrder, isRetired }` | `200 LibraryLevel`, `204` | `404`, `409 LevelSourceNotCustom` |
| `GET /api/v1/admin/library/levels` | The configured levels; filter includeRetired. SystemAdmin only; 403 Forbidden otherwise. | `200` List library levels (admin) | `400` |
| `POST /api/v1/admin/library/levels/enumerate` | The configured levels; filter includeRetired. SystemAdmin only; 403 Forbidden otherwise. | `200` List library levels (admin) | `400` |

Lesson media: video (`kind: Video` media; lessons stay `Processing` until it is Ready; replacing it keeps progress and clamps positions), PDF resources and WebVTT captions (`kind: Document` media, `POST /api/v1/media/uploads` with `application/pdf` or `text/vtt`, library staff only, at most `media.maxDocumentBytes`; PDFs must start with `%PDF-`, WebVTT with `WEBVTT` and well-formed timings; served as `media/{id}/file.pdf` or `file.vtt` with `Content-Disposition: attachment`, `X-Content-Type-Options: nosniff`, and for PDFs `Content-Security-Policy: sandbox`). LibraryEditor uploads use the admin video limits. Caption and PDF text is extracted in the background (DocumentAtom for PDF, chunked by Partio when `documents.partio.enabled`, else the local chunker) and included in lesson search and assistant knowledge. Levels come from `library.levelSource`: `None`, `ProfileField` (the options of a `Rank` or `SingleSelect` profile field), or `Custom`; `property.json` section `library` seeds them (in SingleProperty the folder wins when the section changes on disk), and `GET /api/v1/property` returns the database copy (`library: { levelSource, levelFieldKey, levels, downloadsEnabled, progressIntervalSeconds, completionThresholdPercent, resumeTailSeconds, publicSamplesEnabled }`; clients skip the public lesson routes while `publicSamplesEnabled` is false).

## Ranks, curricula, and attendance

Verified ranks on top of the property's `Rank` profile fields, self-reported lineage, curricula built from the library, and attendance at events and venue schedule sessions (`docs/TRELLISKIT.md` section 16). Every route answers `404 NotAvailable` while the server setting `ranks.enabled` is off (the default); `GET /api/v1/property` reports it as `features.ranks` and `ranks.showVerificationLabels` as `features.rankVerificationLabels`. Feature key `ranks` (gateable in `PartiallyGated`). Staff routes need a member session; the service checks the role: rank staff are SystemAdmin and `RankAwarder` (grant with `POST /api/v1/admin/moderators { userId, resourceType: "Ranks", resourceId: "*" }` or the member roles editor, role `RankAwarder`, resource type `Ranks`); curriculum editors are SystemAdmin and `LibraryEditor`. Nobody awards, revokes, verifies, or checks in themselves.

**Awarding rules** (`rank.awarding` on the Rank field; SystemAdmin is exempt): the awarder needs a `Verified` rank in the field; with `optionAwarderMinimum` set it must be at or above that option; the awarded option must be strictly below the awarder's, or the awarder's own option when `allowEqualOption` (default false), or a level within the awarder's own option for a member who already holds it when `allowEqualOptionLevels` (default true); never above the awarder's own level. Refusals are `403 AwardAboveOwnRank`. Levels run 0 to the option's `maxLevel` (else `rank.maxLevel`); others are `400 LevelOutOfRange`.

**Profile lock:** while a verified rank stands, `PUT /api/v1/me/profile` changing that Rank field is `409 RankVerified` (`fieldErrors["profile.<key>"]`); unchanged values pass. An award sets the stored profile value; a revoke makes the field self-reported and editable again.

Objects: `EffectiveRankView { fieldKey, fieldLabel, optionKey, optionLabel, color, level, maxLevel, levelLabel, rankLabel, verified, source (Verified, SelfReported, None), status, awardedBy, awarder, awardedUtc }`; `RankNextView { optionKey, optionLabel, level, rankLabel, requirement, daysInGrade, attendance, daysInGradeMet, attendanceMet, curriculum, curriculumMet, eligible }`; `RankPromotionView { id, userId, user, fieldKey, optionKey, optionLabel, level, rankLabel, action (Award, Revoke), actorId, actor, note, eventId, reason, awardId, revokedUtc, createdUtc, labels, tags }`; `LineageEntryView { id, userId, fieldKey, optionKey, optionLabel, promotedByUserId, promotedBy, promotedByName, promotedUtc, verified, verifiedBy, verifiedUtc, position, createdUtc, labels, tags }`; `RankRequirement { id, fieldKey, optionKey, level, minimumDaysInGrade, minimumAttendance, notes, curriculumId, createdUtc, updatedUtc, labels, tags }`; `CurriculumView { id, name, description, fieldKey, visibility (Members, Public, Hidden), status (Draft, Published, Archived), position, itemCount, progress { curriculumId, name, optionKey, total, required, completed, completedRequired, percent }, items, userId, publishedUtc, createdUtc, updatedUtc, labels, tags }`; `CurriculumItemView { id, curriculumId, optionKey, optionLabel, itemType (Lesson, Series, CatalogEntry, Custom), targetId, title, targetAvailable, customText, position, required, selfCompletable, state (Incomplete, Completed, SelfComplete, Verified), satisfied, verifiedBy, verifiedUtc, labels, tags }`; `AttendanceView { id, userId, user, targetType (Event, VenueSession), targetId, occurrenceDate, checkedInUtc, method (QR, Staff, SelfWithCode), checkedInBy, createdUtc, labels, tags }`.

| Method and path | Role | Body or filters | Success | Errors |
|---|---|---|---|---|
| `GET /api/v1/me/rank` | Member | | `200 { showVerificationLabels, fields: [{ rank: EffectiveRankView, next: RankNextView or null }], history: [RankPromotionView] (20 newest), historyTotal, lineage: [LineageEntryView] }` (fields at most 10, lineage at most 100) | `403` (admin token), `404 NotAvailable` |
| `GET /api/v1/members/{id}/rank` | Member (profile visibility rules) | | `200 { userId, showVerificationLabels, ranks: [EffectiveRankView], lineage: [LineageEntryView] }` (only Rank fields whose visibility admits the caller) | `403`, `404 NotFound` (unknown, deleted, banned, or blocked), `404 NotAvailable` |
| `PUT /api/v1/me/lineage` | Member | `{ fieldKey?, entries: [{ optionKey, promotedByUserId?, promotedByName?, promotedUtc? }] }` (at most 50; a member id or a name up to 120 characters, not both; unchanged entries keep their verification) | `200 { fieldKey, entries: [LineageEntryView] }`; a new entry naming a member sends them `LineageVerificationRequested`; audited `lineage.update` | `400 ValidationFailed` or `ContentFiltered`, `403 Muted`, `404 NotAvailable` |
| `POST /api/v1/admin/lineage/{id}/verify` | The named promoter or SystemAdmin | | `200 LineageEntryView`; audited `lineage.verify` | `403 Forbidden`, `404` |
| `POST /api/v1/admin/ranks/awards` | Rank staff | `{ userId, fieldKey?, optionKey, level?, note?, eventId?, labels?, tags? }` (note at most 500) | `201 RankPromotionView`; sets the profile value and locks the field; `RankPromoted` in-app and push plus the `rank_promoted` email; audited `rank.award` | `400 ValidationFailed`, `UnknownRankOption`, `LevelOutOfRange`, `ContentFiltered`; `403 Forbidden`, `AwardAboveOwnRank`, `SelfAward`; `404` (member), `404 NotAvailable` |
| `POST /api/v1/admin/ranks/awards/{id}/revoke` | SystemAdmin, the original awarder, or a RankAwarder the rules allow | `{ reason }` (1 to 500) | `200 RankPromotionView` (the revoke row); audited `rank.revoke` | `400 ValidationFailed`, `403 Forbidden` or `SelfAward`, `404`, `409 AlreadyRevoked` |
| `GET /api/v1/admin/ranks/awards` and `POST /api/v1/admin/ranks/awards/enumerate` | Rank staff | filters `userId`, `fieldKey`, `optionKey`, `actorId`, `action` (Award, Revoke), `status` (Active, Revoked award rows), `fromUtc`, `toUtc`, labels, tags; search notes and reasons | `200 EnumerationResult<RankPromotionView>` (newest first) | `400`, `403`, `404 NotAvailable` |
| `GET /api/v1/admin/ranks/eligibility` and `POST /api/v1/admin/ranks/eligibility/enumerate` | Rank staff | filters `fieldKey` (default the first Rank field), `optionKey` (current option), `eligibleOnly` (default true), labels and tags of the member; search names | `200 EnumerationResult<RankEligibilityView { userId, user, current: EffectiveRankView, next: RankNextView, eligible, labels, tags }>`; with `eligibleOnly` a page may hold fewer than `maxResults` objects while `endOfResults` is false | `400`, `403`, `404 NotAvailable` |
| `GET /api/v1/admin/ranks/requirements` and `POST /api/v1/admin/ranks/requirements/enumerate` | Rank staff | filters `fieldKey`, `optionKey`, labels, tags | `200 EnumerationResult<RankRequirement>` (position order) | `400`, `403`, `404 NotAvailable` |
| `PUT /api/v1/admin/ranks/requirements` | SystemAdmin | `{ fieldKey?, requirements: [{ optionKey, level?, minimumDaysInGrade, minimumAttendance, notes?, curriculumId?, labels?, tags? }] }` (at most 200; one per option and level; days 0 to 3650; attendance 0 to 10000; notes at most 1000) | `200 { fieldKey, updated, requirements }`; replaces the field's requirements; audited `rank.requirements` | `400 ValidationFailed`, `403 Forbidden`, `404 NotAvailable` |
| `GET /api/v1/curricula` and `POST /api/v1/curricula/enumerate` | Member | filters `fieldKey`, labels, tags; orderings PositionAscending (default), NameAscending, UpdatedDescending, CreatedDescending, CreatedAscending | `200 EnumerationResult<CurriculumView>` (published, visibility Public, and Members for members; with the caller's `progress`) | `400`, `404 NotAvailable` |
| `GET /api/v1/curricula/{id}` | Member (visibility; Hidden by identifier) | | `200 CurriculumView` with `items` (at most 500) and the caller's state | `404` (drafts and archived for members), `404 NotAvailable` |
| `PUT /api/v1/me/curricula/{id}/items/{itemId}/complete` | Member | | `200 CurriculumItemView` (`SelfComplete`; optional items count, required items wait for verification) | `404`, `409 NotSelfCompletable` (Lesson and Series items) |
| `DELETE /api/v1/me/curricula/{id}/items/{itemId}/complete` | Member | | `200 CurriculumItemView` | `404`, `409 NotSelfCompletable` or `AlreadyVerified` |
| `GET /api/v1/admin/curricula` and `POST /api/v1/admin/curricula/enumerate` | Curriculum editors and rank staff | filters `fieldKey`, `status`, `visibility`, labels, tags | `200 EnumerationResult<CurriculumView>` | `400`, `403`, `404 NotAvailable` |
| `POST /api/v1/admin/curricula` | Curriculum editors | `{ name, description?, fieldKey?, visibility?, position?, labels?, tags? }` (name 1 to 120; description at most 2000) | `201 CurriculumView` (Draft); audited `curriculum.create` | `400 ValidationFailed`, `403`, `404 NotAvailable` |
| `GET /api/v1/admin/curricula/{id}` | Curriculum editors and rank staff | | `200 CurriculumView` with items | `403`, `404` |
| `PUT /api/v1/admin/curricula/{id}` | Curriculum editors | any create field (omitted fields stay) | `200 CurriculumView`; audited `curriculum.update` | `400` (also when items are grouped by options the new field lacks), `403`, `404` |
| `PUT /api/v1/admin/curricula/{id}/items` | Curriculum editors | `{ items: [{ id?, optionKey?, itemType, targetId?, customText?, required?, labels?, tags? }] }` (at most 500; Lesson and Series need an existing target; CatalogEntry a text identifier of 1 to 100 letters, digits, and `_ : . -`, no catalog lookup; Custom 1 to 300 characters; listed ids keep member progress, unlisted items are deleted with it) | `200 CurriculumView`; audited `curriculum.items` | `400 ValidationFailed`, `403`, `404` |
| `POST /api/v1/admin/curricula/{id}/publish` | Curriculum editors | | `200 CurriculumView`; audited `curriculum.publish` | `403`, `404`, `409 InvalidStatus` or `CurriculumEmpty` |
| `POST /api/v1/admin/curricula/{id}/archive` | Curriculum editors | | `200 CurriculumView` (hidden from members; progress kept); audited `curriculum.archive` | `403`, `404`, `409 InvalidStatus` |
| `GET /api/v1/admin/curricula/{id}/progress/{userId}` | Curriculum editors and rank staff | | `200 CurriculumView` with the member's state per item | `403`, `404` |
| `POST /api/v1/admin/curricula/{id}/progress/{userId}/items/{itemId}/verify` | Rank staff | | `200 CurriculumItemView` (`Verified`); audited `curriculum.verify` | `403`, `404` |
| `POST /api/v1/attendance/check-in` | Member with the target's code, or rank staff with `userId` | `{ targetType (Event, VenueSession), targetId, occurrenceDate?, code?, userId?, method? (QR, SelfWithCode), labels?, tags? }`. Event: `occurrenceDate` optional and must be the event's start date in its time zone. VenueSession: `targetId` `{venueId}:{sessionIndex}` (zero-based position in `schedule.sessions`), `occurrenceDate` (yyyy-MM-dd) required and on the session's weekday | `201 AttendanceView`; audited `attendance.checkIn`. Members check in between `attendance.checkInWindowBeforeMinutes` before and `attendance.checkInWindowAfterMinutes` after the start (the session start on the date in the venue's time zone); staff (method Staff, no code) any time after the window opens | `400 ValidationFailed` or `InvalidCode`, `403`, `404`, `409 AlreadyCheckedIn`, `422 OutsideCheckInWindow` |
| `GET /api/v1/attendance/check-in-code` | Rank staff | query `targetType`, `targetId`, `occurrenceDate` (VenueSession) | `200 { targetType, targetId, occurrenceDate, code, expiresUtc, rotationSeconds, qrPayload, windowOpensUtc, windowClosesUtc }` (six digits, rotates every `attendance.codeRotationSeconds`, the previous code is accepted for one more period; `qrPayload` is `checkin?targetType=...&targetId=...&occurrenceDate=...&code=...`) | `400`, `403`, `404` |
| `GET /api/v1/admin/attendance` and `POST /api/v1/admin/attendance/enumerate` | Rank staff | filters `targetType`, `targetId`, `occurrenceDate`, `userId`, labels, tags; orderings CheckedInDescending (default), CheckedInAscending | `200 EnumerationResult<AttendanceView>` (with member summaries) | `400`, `403`, `404 NotAvailable` |
| `DELETE /api/v1/admin/attendance/{id}` | Rank staff | | `204`; audited `attendance.delete` | `403`, `404` |
| `GET /api/v1/me/attendance` and `POST /api/v1/me/attendance/enumerate` | Member | filters `targetType`, `targetId`, `occurrenceDate`, labels, tags | `200 EnumerationResult<AttendanceView>` | `400`, `404 NotAvailable` |

## Saves (signed in)

Generic saves and named private collections. A member saves a `Lesson`, `Series`, `CatalogEntry`, `Venue`, `Instructor`, `Event`, `Post` (feed post), or `Thread`; each type follows its own feature switch and read rules (`features.library`, `features.directory`, `features.events`, `features.forums`, `features.catalog`; `CatalogEntry` resolves through the catalog's content target provider: published entries are available, and deleting an entry removes every save of it). The whole area is the `saves` feature: `features.saves: false` answers `404 NotAvailable` on every route below, and the `saves` kill switch answers `503 FeatureDisabled`. Saves are independent of any navigation or hub switch. The routes need a session (route group SignedIn). Writes (`PUT /me/saves/...`, creating a collection) need the write tier of the `saves` feature: a membership in `FullyGated`, or when `saves` is listed in `access.gatedFeatures` (`402 MembershipRequired` with `feature: "saves"`); reads and deletes stay open so a lapsed member can still see and remove saves.

Saved lists never leak content the member can no longer open: when a target is archived, unpublished, moderated, behind a turned-off feature, or no longer readable by the member, its `target.available` is `false` and `title`, `imageUrl`, and `href` are `null`. Deleting a target (lesson, series, catalog entry, venue, instructor, event, post, thread, including purges) deletes every member's saves of it. Account deletion deletes the member's saves and collections; account exports carry the sections `saves` (with notes, `collectionIds`, labels, tags) and `saveCollections`.

Notes are visible to the member and to moderators (never call them private in a client): they pass the word filter (`400 ContentFiltered`) and every create, edit, and clear writes a `contentchanges` row (target type `Favorite`, context the saved target) for content review. Labels and tags use the shared metadata tables (target types `Favorite` and `FavoriteCollection`, owner-editable through the generic metadata routes, discoverable only by their owner).

### Shapes

- `FavoriteView`: `{ id (fav_), targetType, targetId, note, collectionIds, target { available, title, imageUrl, href }, labels, tags, createdUtc, lastUpdateUtc }`. `collectionIds` follow the member's collection order.
- `FavoriteCollection`: `{ id (fvc_), userId, name, position, itemCount, labels, tags, createdUtc, lastUpdateUtc }`.
- `FavoriteRequest` (every field optional; omitted keeps the stored value): `{ collectionIds (at most 20 of your collections; replaces the set; [] removes it from every collection), note (at most 500 characters; null or empty clears it), labels, tags }`.

### Routes

| Method and path | Body | Success | Errors |
|---|---|---|---|
| `PUT /api/v1/me/saves/{targetType}/{targetId}` | `FavoriteRequest` | `200 FavoriteView` (idempotent: created or updated) | `400 ValidationFailed` (unknown type, note over 500, more than 20 collections, labels or tags), `400 ContentFiltered`, `401`, `402 MembershipRequired`, `403 Forbidden` (you cannot open the target), `404 NotFound` (target or collection), `404 NotAvailable` (saves off, or the target type unavailable), `409 LimitReached` (`saves.maxPerMember`) |
| `DELETE /api/v1/me/saves/{targetType}/{targetId}` | | `204` (idempotent) | `400 ValidationFailed` (unknown type), `404 NotAvailable` |
| `GET /api/v1/me/saves`, `POST /api/v1/me/saves/enumerate` | `EnumerationQuery`; orderings `CreatedDescending` (default), `CreatedAscending`, `UpdatedDescending`; filters `targetTypes` (array or comma list), `collectionId`; `labels`, `labelMatch`, `tags`, `search` (notes), `createdAfterUtc`, `createdBeforeUtc` | `200 EnumerationResult<FavoriteView>` (member maximum 100) | `400 InvalidContinuationToken`, `400 ValidationFailed`, `404 NotFound` (`collectionId` not yours), `404 NotAvailable` |
| `POST /api/v1/me/saves/state` | `{ targets: [{ targetType, targetId }] }` (1 to 100) | `200 { items: [{ targetType, targetId, saved, favoriteId }] }` in request order | `400 ValidationFailed` (empty, over 100 targets, unknown type, missing id), `404 NotAvailable` |
| `GET /api/v1/me/save-collections`, `POST /api/v1/me/save-collections/enumerate` | `EnumerationQuery`; orderings `PositionAscending` (default), `NameAscending`, `CreatedDescending`, `CreatedAscending`; `search` (names), labels and tags | `200 EnumerationResult<FavoriteCollection>` | `400 InvalidContinuationToken`, `400 ValidationFailed`, `404 NotAvailable` |
| `POST /api/v1/me/save-collections` | `{ name (1 to 60, unique per member, case-insensitive), labels, tags }` | `201 FavoriteCollection` (last in your order) | `400 ValidationFailed`, `400 ContentFiltered`, `402 MembershipRequired`, `404 NotAvailable`, `409 DuplicateName`, `409 LimitReached` (`saves.maxCollectionsPerMember`) |
| `PUT /api/v1/me/save-collections/{id}` | `{ name, labels, tags }` (each optional) | `200 FavoriteCollection` | `400 ValidationFailed`, `400 ContentFiltered`, `404 NotFound`, `404 NotAvailable`, `409 DuplicateName` |
| `PUT /api/v1/me/save-collections/order` | `{ ids }`: every one of your collections exactly once (at most `saves.maxCollectionsPerMember`) | `204` | `400 ValidationFailed`, `404 NotAvailable` |
| `DELETE /api/v1/me/save-collections/{id}` | | `204` (the saves stay; only the grouping is removed) | `404 NotFound`, `404 NotAvailable` |

A collection membership always links a save and a collection of the same member in the same property; the database refuses any other link and the route answers `404 NotFound`.

Settings: `saves.maxPerMember` (default 2000, 100 to 20000), `saves.maxCollectionsPerMember` (default 50, 1 to 500). Metrics: `trelliskit.saves.created` and `trelliskit.saves.removed`, labeled `target_type` (`Any` for account deletion).

## Training hub (signed in)

`GET /api/v1/me/training` returns the signed-in member's Training overview in one round trip (docs/TRELLISKIT.md section 15). It is session only: signed-out clients never call it and show the public Library (the library and catalog routes) instead. The route is in the Member group with a `training` declaration: `FullyGated` needs a membership, `PartiallyGated` needs one only when `training` is listed in `access.gatedFeatures`, `Open` needs a session. `features.training: false` answers `404 NotAvailable`; the `training` kill switch answers `503 FeatureDisabled` on this route only (the library, catalog, events, and saves routes keep their own switches).

### Shape

```json
{
  "sections": [
    { "key": "Overview", "available": true, "reason": null, "segments": [] },
    { "key": "Library", "available": true, "reason": null, "segments": ["Courses", "Techniques", "Saved"] },
    { "key": "Schedule", "available": false, "reason": "NoData", "segments": [] },
    { "key": "Programs", "available": false, "reason": "FeatureOff", "segments": [] },
    { "key": "Progress", "available": true, "reason": null, "segments": [] }
  ],
  "upNext": { "objects": [{ "kind": "Event", "id": "evt_...", "title": "...", "startUtc": "...", "endUtc": "...", "timeZone": "Etc/UTC", "locationSummary": "...", "path": "/events/evt_...", "labels": [], "tags": {} }], "...": "EnumerationResult" },
  "continue": "EnumerationResult<ContinueWatchingItem>",
  "recentSaves": "EnumerationResult<FavoriteView>",
  "newForYou": "EnumerationResult<LessonSummary>",
  "rank": { "rank": "EffectiveRankView", "next": "RankNextView | null", "curriculum": "CurriculumView | null", "showVerificationLabels": true },
  "coach": { "available": false, "placement": "Menu" },
  "firstRun": false,
  "labels": [],
  "tags": {}
}
```

- `sections`: always the five keys in this order. `reason` (`FeatureOff`, `NoAccess`, `NoData`, `SignedOut`) is set only when `available` is false; clients show only available sections and ignore unknown keys. `segments` (at most 3) is set on an available Library only.
- Library: available when any segment is: `Courses` (`features.library`), `Techniques` (`features.catalog` and at least one published entry), `Saved` (`features.saves`).
- Schedule: available when the member has an upcoming RSVP (Going, Interested, or Waitlisted) to a published event, or (with `ranks.enabled`) at least one check-in. `FeatureOff` when events and ranks are both off.
- Programs: available when `ranks.enabled` is on and a published curriculum is visible to the member.
- Progress: available when the member has any lesson progress, holds a rank, has a recorded promotion, or has a check-in.
- Overview: available when any other section is.
- `upNext`: upcoming published events the member RSVPed Going to, by start (at most `training.upNextLimit`). `continue`: lessons in progress, most recently watched first (`training.continueLimit`). `recentSaves`: newest saves (`training.recentSavesLimit`, empty while `features.saves` is off). `newForYou`: lessons published within `training.newForYouDays` in series the member follows (not opted out) and has not started, newest first, readable by the member (`training.newForYouLimit`). Each is an `EnumerationResult`; an empty card is an empty, terminal page.
- `rank`: the first Rank profile field (profile order): the effective rank (`source` `None` when the member holds none), the next step with requirements, the next requirement's curriculum (or the first published curriculum visible to the member for the field) with progress, and `ranks.showVerificationLabels`. `null` while `ranks.enabled` is off or the property has no Rank field.
- `coach`: `available` when AI is available for the property (the `features.ai` rule: the property AI switch and `ai.enabled`) and at least one assistant serves the member; `placement` is `features.aiPlacement` (`ai.mobilePlacement`).
- `firstRun`: true when the member has no lesson progress, saves, or RSVPs (and every provider answered). The overview then carries no sample content; clients build their first-run card from real featured series and the events or directory entry.

A section provider that fails is logged, counted (`trelliskit.training.provider.errors`, label `provider`), and reports its sections `available: false` with `reason: NoData`; the request still succeeds. `404 NotAvailable` is answered only when no section is available and no provider failed.

| Method and path | Body | Success | Errors |
|---|---|---|---|
| `GET /api/v1/me/training` | | `200 TrainingOverview` (sections exactly 5; cards bounded by the `training.*` settings, maxima 10, 25, 20, 20) | `401 Unauthorized`, `402 MembershipRequired`, `404 NotAvailable` (`features.training` off or no section available), `429 RateLimited`, `503 FeatureDisabled` (training kill switch) |

Settings (live): `training.upNextLimit` (default 3, 1 to 10), `training.continueLimit` (default 10, 1 to 25), `training.recentSavesLimit` (default 6, 1 to 20), `training.newForYouLimit` (default 6, 1 to 20), `training.newForYouDays` (default 14, 1 to 90). Telemetry: span `training.overview`; histogram `trelliskit.training.overview.duration` and counter `trelliskit.training.overview.requests`, labeled `outcome` (`success`, `partial`, `not_available`, `error`). Product events `training_opened`, `training_section_viewed`, and `training_card_opened` are client events a property declares in `metrics.events` (opt in).

## Library search (member, object gated)

`POST /api/v1/library/search` searches courses (series), lessons, and techniques (published catalog entries) at once. Each kind runs through its own member enumeration with the same search text, so access rules, locked stubs (PartiallyGated free accounts), and continuation are exactly those of `POST /api/v1/series/enumerate`, `POST /api/v1/lessons/enumerate`, and `POST /api/v1/catalog/entries/enumerate`: continue a kind by sending its `continuationToken` there with the same `search`. Signed out works where anonymous browsing is on (signed-out readers see Public lessons unlocked). Techniques are searched only while `features.catalog` is on (otherwise the page is empty). Rate limited by the shared search limiter (`search.perMinute`, bucket `search.global`; per IP when signed out). The series and lesson search columns carry trigram GIN indexes when the `pg_trgm` extension is installed (migration 135). Counter `trelliskit.library.search.requests` labeled `outcome`.

| Method and path | Body | Success | Errors |
|---|---|---|---|
| `POST /api/v1/library/search` | `{ query (2 to 100 characters after trimming), kinds (Series, Lesson, CatalogEntry; case-insensitive; default all; at most 3), maxResultsPerKind (1 to 25, default 10), labels, tags }` | `200 { series: EnumerationResult<SeriesSummary>, lessons: EnumerationResult<LessonSummary>, techniques: EnumerationResult<CatalogEntry> }`; kinds not requested are empty, terminal pages | `400 ValidationFailed`, `401 Unauthorized` (signed out without anonymous browsing), `402 MembershipRequired`, `404 NotAvailable` (`features.library` off), `429 RateLimited`, `503 FeatureDisabled` (library kill switch) |

## Lesson techniques (features.catalog)

Links between lessons and the catalog entries they teach (table `lessoncatalogentries`, ids `lce_`, migration 135): "Techniques in this lesson" and "Lessons that teach this". Courses and techniques stay separate data sets; a link joins them without changing either. Every route answers `404 NotAvailable` while `features.catalog` or `features.library` is off. A lesson carries at most 50 links, each entry once; only published entries can be linked. Archived and unpublished lessons never appear in "Lessons that teach this"; an entry that is later unpublished leaves "Techniques in this lesson" for members, while library staff still see it with `warning: "EntryNotPublished"`. Deleting a lesson or a catalog entry deletes its links (with their labels and tags). Link labels and tags use the shared metadata tables (target type `LessonCatalogEntry`).

`LessonCatalogEntryView`: the caller's `CatalogEntry` view plus `{ linkId (lce_), lessonId, position (1 to n), startSeconds (0 to 86400 or null), source (Editor or Suggested), warning (EntryNotPublished for staff, otherwise null), linkLabels, linkTags }`.

| Method and path | Role | Body | Success | Errors |
|---|---|---|---|---|
| `PUT /api/v1/admin/lessons/{id}/catalog-entries` | SystemAdmin or LibraryEditor | `{ items: [{ catalogEntryId, startSeconds (optional), position (1 to 50, optional; ties keep list order), source (Editor default, or Suggested), labels, tags }] }`, at most 50; `[]` removes every link | `200 EnumerationResult<LessonCatalogEntryView>` (the stored links in position order, renumbered 1 to n; audited `lesson.catalogEntries`) | `400 ValidationFailed` (missing items, over 50, duplicate entry, bad position, offset, labels, or tags), `403 Forbidden`, `404 NotFound` (lesson or entry), `404 NotAvailable`, `409 CatalogEntryNotPublished` |
| `GET /api/v1/lessons/{id}/catalog-entries`, `POST /api/v1/lessons/{id}/catalog-entries/enumerate` | member who can see the lesson (a locked stub included; signed out under anonymous browsing) | `EnumerationQuery`; orderings `PositionAscending` (default), `NameAscending`, `CreatedDescending`, `CreatedAscending`; `search` (entry name and summary); `labels`, `labelMatch`, `tags` filter the links | `200 EnumerationResult<LessonCatalogEntryView>` (at most 50 per page) | `400 InvalidContinuationToken`, `400 ValidationFailed`, `404 NotFound` (lesson), `404 NotAvailable`, `429 RateLimited` |
| `GET /api/v1/catalog/entries/{id}/lessons`, `POST /api/v1/catalog/entries/{id}/lessons/enumerate` | member (signed out under anonymous browsing) | `EnumerationQuery`; orderings `PublishedDescending` (default), `TitleAscending`, `CreatedDescending`, `CreatedAscending`; `search` (title and summary); `labels`, `labelMatch`, `tags` filter the lessons | `200 EnumerationResult<LessonSummary>` under your lesson access: locked stubs where your access model shows them (at most 50 per page) | `400 InvalidContinuationToken`, `400 ValidationFailed`, `404 NotFound` (entry missing or not published), `404 NotAvailable`, `429 RateLimited` |
| `GET /api/v1/public/catalog/entries/{id}/lessons`, `POST /api/v1/public/catalog/entries/{id}/lessons/enumerate` | anonymous | `EnumerationQuery` (as above) | `200 EnumerationResult<LessonSummary>`: Public, published lessons only, public media paths, no viewer state (at most 50 per page) | `400 ValidationFailed`, `404 NotFound`, `404 NotAvailable`, `429 RateLimited` (public catalog limit per IP) |

## Events (Phase 4)

Events, recurring events, RSVPs, reminders, member proposals, and calendars (docs/PHASE4_CONTRACT.md section 4; resolved decisions 10, 11, 12, 13, 14, 21). Feature key `events`: `features.events: false` answers `404 NotAvailable` on every route below (member, me, public, calendar, and admin); the `events` kill switch answers `503 FeatureDisabled` on the member, me, public, and calendar routes (admin routes stay open). Member routes are declared object gated: the route needs a signed-in caller (or none for anonymous-eligible reads under anonymous browsing) and the event service decides per event (locked stubs, `rsvpAudience`). In `FullyGated` every event route needs a membership.

Event staff is `SystemAdmin` or `EventManager` (grant with `POST /api/v1/admin/moderators` `{ userId, resourceType: "Events", resourceId: "*" }`). Hosts (members listed in `hostUserIds`) edit the description, cover, labels, and tags of their events, list RSVPs, and check people in.

### Shapes

`EventSummary` (lists):

```json
{
  "id": "evt_...", "title": "Autumn meetup", "slug": "autumn-meetup", "summary": "Open session and social.",
  "category": { "key": "meetup", "label": "Meetup", "color": "#1F5FBF" },
  "format": "InPerson", "status": "Published",
  "startUtc": "2026-12-05T23:00:00Z", "endUtc": "2026-12-06T02:00:00Z", "timeZone": "America/New_York", "isAllDay": false,
  "startLocal": "2026-12-05T18:00", "endLocal": "2026-12-05T21:00", "isPast": false,
  "location": { "venue": null, "name": "Example Hall", "address": { "line1": "100 Example Street", "line2": null, "locality": "Exampleville", "region": "EX", "postalCode": "00000", "countryCode": "US", "formatted": "100 Example Street, Exampleville, EX 00000" }, "addressVisibility": "Exact", "geo": null, "distanceKm": null },
  "online": null,
  "capacity": 40, "goingCount": 37, "interestedCount": 12, "waitlistCount": 0, "spotsLeft": 3, "waitlistEnabled": true,
  "rsvpAudience": "SignedIn", "rsvpOpensUtc": null, "rsvpClosesUtc": null, "attendeeVisibility": "Attendees",
  "externalUrl": null, "cover": null, "hostCount": 1,
  "myRsvp": { "id": "rsv_...", "status": "Going", "guestCount": 0, "position": null, "hideFromAttendeeLists": false, "checkedInUtc": null, "eventCanceled": false, "updatedUtc": "..." },
  "reminderOffsetsMinutes": [1440, 120], "commentsEnabled": true, "commentTarget": { "type": "Event", "id": "evt_..." },
  "commentCount": 4, "reactionCounts": {}, "reactionTotal": 0, "myReactions": [], "promotion": null,
  "audience": "SignedIn", "canceledUtc": null, "cancelReason": null, "locked": false,
  "recurrence": { "id": "erc_...", "rule": { "frequency": "Weekly", "interval": 1, "weekdays": ["Tue", "Thu"], "monthDay": null, "setPosition": null, "count": 12, "untilDate": null }, "summary": "Every week on Tuesday and Thursday, 12 times", "occurrenceIndex": 3, "isException": false, "originalStartUtc": "..." },
  "labels": [], "tags": {},
  "viewer": { "canRsvp": true, "canRsvpGoing": true, "canJoinWaitlist": false, "canComment": true, "canReact": true, "canReport": true, "canSeeAttendees": true, "canEdit": false, "canCancel": false, "canCheckIn": false, "canPromote": false, "canOpen": true, "requires": null },
  "publishedUtc": "...", "createdUtc": "...", "updatedUtc": "..."
}
```

`Event` (detail) adds `description`, `maxGuestsPerRsvp`, `hosts` (UserSummary), `instructors` (`{ id, slug, displayName, photoMediaId }`, directory instructors the caller can read), `attendeePreview` (`{ objects (at most 10 Going members), totalRecords, enumerate }` when `viewer.canSeeAttendees`, else null), and `proposalNote` (member proposals; event staff and the proposer only). `online.joinUrl` is returned only to Going members, event staff, and hosts, and only from `online.joinUrlAvailableUtc` (start minus `events.joinUrlRevealMinutes`); summaries never carry it. `recurrence` is null for single events.

A locked stub (`PartiallyGated`, a signed-in caller without the membership the audience needs) has `locked: true`, `description: null`, `online: null`, `attendeePreview: null`, empty `hosts`, `myRsvp: null`, and `viewer { canOpen: false, requires: "Membership" }`. `viewer.requires` is `"SignIn"` for visitors and `"Membership"` when the caller's tier cannot RSVP.

Location rules: `addressVisibility` `Exact` (full address, pin to 5 decimals), `Approximate` (locality, region, country, pin to 2 decimals), `LocalityOnly` (no pin). A directory venue the caller cannot read is replaced by its locality only. Event staff and hosts always see the exact values.

`AdminEvent` (staff routes) is `Event` plus `openReportCount`, `checkedInCount`, `guestCount`, `reminderStatus` (`[{ offsetMinutes, dueUtc, sentUtc, skippedUtc }]`), `onlineJoinUrl` (always), `venueId`, `hostUserIds`, `instructorIds`, `proposedBy` (UserSummary or null), `reviewNote`, `reviewedUtc`, `moderationState`, and `isHost` (the caller is a host, not staff).

`PublicEvent` (anonymous): `{ id, title, slug, summary, description, category, format, startUtc, endUtc, timeZone, isAllDay, location, onlinePlatformLabel, capacity, goingCount, spotsLeft, cover, audience, recurrence, labels, tags, publishedUtc }` (no `myRsvp`, `viewer`, attendee data, or join URLs).

### Local time and zones

Staff send local wall time (`startLocal`, `endLocal` as `yyyy-MM-ddTHH:mm`) plus an IANA `timeZone` (default the venue's zone). A time inside a spring-forward gap is `400 InvalidSchedule` with a hint naming the next valid time (`fieldErrors.startLocal`); an ambiguous fall-back time resolves to the first occurrence (the daylight offset). All-day events send dates (`yyyy-MM-dd`; the end date is inclusive) and store local midnight. Changing only `timeZone` keeps the wall time and recomputes UTC. `400 InvalidSchedule` also covers end not after start, longer than `events.maxDurationDays`, unknown zones, an RSVP window outside the event, and more than 3 reminder offsets.

### Recurring events

Create (or edit with scope `Following` or `All`) with `recurrence`:

```json
{ "frequency": "Daily | Weekly | Monthly | Yearly", "interval": 1, "weekdays": ["Tue", "Thu"], "monthDay": null, "setPosition": null, "count": 12, "untilDate": null }
```

| Field | Rule |
|---|---|
| `frequency` | Required |
| `interval` | 1 to 99 (default 1) |
| `weekdays` | `Mon` to `Sun`. Weekly: one or more (default the start's weekday). Monthly: exactly one, with `setPosition` |
| `monthDay` | Monthly by date: 1 to 31, or -1 for the last day. Months without the day are skipped (RFC 5545); use -1 for "last day" |
| `setPosition` | Monthly by weekday: 1 to 4, or -1 for the last ("second Tuesday", "last Friday") |
| `count` | Total occurrences including the first, 1 to `events.maxOccurrences` |
| `untilDate` | Last local date (inclusive). Exactly one of `count` and `untilDate`, or neither (until the horizon) |

The first occurrence is `startLocal` and must fall on the rule (`400 InvalidSchedule` otherwise). Occurrences keep the local wall time in the event zone, so their UTC instants shift across DST; a wall time that does not exist on a transition day moves forward by the gap. Yearly rules on February 29 skip non-leap years.

Each occurrence is an ordinary event (its own `evt_` id, slug `<title>-yyyyMMdd`, RSVPs, capacity, reminders, comments), linked by `recurrence.id`, `originalStartUtc`, and `occurrenceIndex`. Occurrences are materialized up to `events.recurrenceHorizonDays` ahead (default 180) and at most `events.maxOccurrences` per recurrence (default 200); the `event_recurrence` worker (every `events.recurrenceIntervalSeconds`) extends the horizon as time passes. A hidden series template row (status `Template`, never listed or readable) holds the fields new occurrences copy. Publishing any occurrence of a draft series publishes the series (later occurrences are created published).

Edit scopes on `PUT /api/v1/admin/events/{id}` and `POST /api/v1/admin/events/{id}/cancel` (`scope`, default `One`):

| Scope | Effect |
|---|---|
| `One` | Only this occurrence changes; it becomes an exception (`recurrence.isException: true`, an `Edited` or `Canceled` exception row keyed by `originalStartUtc`) that later series edits never touch |
| `Following` | The old rule ends before this occurrence (its `count` becomes this occurrence's index); a new recurrence starts here with the edits and an optional new `recurrence` rule (a count carries over as the remaining count). Occurrences from here on move to the new recurrence and keep their rows and RSVPs |
| `All` | Every not-yet-started occurrence that is not an exception (and the series template) takes the edits. A time or rule change regenerates occurrences: rows are matched by local date (shifted with the series when the date moves), so RSVPs stay on their occurrence; dates no longer on the rule are canceled (`EventCanceled` to their RSVPs, one notice per member). Moving the series to other days needs a new `recurrence` rule |

Material changes (time, location, format) send one aggregated `EventUpdated` per member for a series edit. RSVP windows (`rsvpOpensUtc`, `rsvpClosesUtc`) apply to the edited occurrence only. Deleting a draft occurrence records a `Deleted` exception, so it is never materialized again; `DELETE ...?scope=All` deletes a whole draft series. Proposals are single events (`recurrence` is refused).

### Member routes

| Method and path | Body | Result | Errors |
|---|---|---|---|
| `GET /api/v1/events`, `POST /api/v1/events/enumerate` | `EnumerationQuery`. Orderings `StartAscending` (default), `StartDescending`, `CreatedDescending`, `CreatedAscending`, `DistanceAscending` (needs `near`), `Relevance` (needs `search`; start order among matches). Filters `when` (`Upcoming` default, `Past`, `All`), `startsAfterUtc`, `startsBeforeUtc`, `category`, `format`, `venueId`, `hostUserId`, `instructorId`, `near` (`{ lat, lng, radiusKm }` or `"lat,lng,radiusKm"`, radius default 50, at most 2000; online-only events excluded unless `includeOnline`), `bbox` (`{ south, west, north, east }` or `"south,west,north,east"`; west greater than east crosses the antimeridian; not with `near`; events whose stored position is in the box, online-only events excluded unless `includeOnline`), `rsvpStatus` (`Going`, `Interested`, `Waitlisted`, `None`), `hasSpots`, `includeCanceled`, `audience`, `status`, `recurrenceId` | `EnumerationResult<EventSummary>` (locked stubs in PartiallyGated; events hosted only by members the caller blocked are omitted) | `400 ValidationFailed`, `400 InvalidGeo` |
| `POST /api/v1/events/map/enumerate` | `{ bbox (required), zoom (0 to 22, default 2), maxResults (standard member page cap), continuationToken, skip, labels, labelMatch, tags, search, filters (when, startsAfterUtc, startsBeforeUtc, category, format, venueId, hostUserId, instructorId, rsvpStatus, hasSpots, includeCanceled, audience) }` | `EnumerationResult<MapFeature>`: the events the caller may list (as the event list: audiences, locked stubs, blocked hosts) placed in the viewport. `{ kind: "Point", event { id, slug, title, startUtc, endUtc, timeZone, startLocal, isAllDay, format, status, category, locationName, locality, region, countryCode, venue, geo { lat, lng, precision }, spotsLeft, locked, coverThumbUrl } }` while they fit the configured feature budget, otherwise grid clusters `{ kind: "Cluster", count, lat, lng, bbox }` (the directory map clustering; one-event cells stay points); standard continuation pages; totalRecords counts features and matchedRecords counts represented source records. Online events never appear. Positions follow the stricter of the event's and its venue's address visibility: LocalityOnly events and events at venues the caller may not read are left out, Approximate ones sit at 2 decimals; event staff see exact positions | `400 ValidationFailed`, `400 InvalidGeo`, `429` (`events.mapPerMinute`) |
| `GET /api/v1/events/{idOrSlug}` | | `Event` | `404` |
| `PUT /api/v1/events/{id}/rsvp` | `{ "status": "Going" \| "Interested", "guestCount": 0, "hideFromAttendeeLists": false }` | `Event` (`myRsvp.status` may be `Waitlisted` with a `position`) | `400`, `403 RsvpNotAllowed`, `403 Muted`, `404`, `409 EventFull`, `409 RsvpClosed`, `409 EventCanceled`, `409 RsvpLimitReached`, `429` |
| `DELETE /api/v1/events/{id}/rsvp` | | `Event` (`myRsvp: null`) | `409 RsvpClosed` (after the start) |
| `GET /api/v1/events/{id}/attendees`, `POST .../attendees/enumerate` | Filter `status`: `Going` (default); staff and hosts also `Interested`, `Waitlisted`, `CheckedIn` | `EnumerationResult<{ id, user, status, guestCount, position, checkedInUtc, createdUtc, labels, tags }>` | `400`, `404` when `viewer.canSeeAttendees` is false |
| `GET /api/v1/events/{id}/occurrences`, `POST .../occurrences/enumerate` | Filters `when` (default `All`), `startsAfterUtc`, `startsBeforeUtc`, `includeCanceled` (default true), `rsvpStatus` | `EnumerationResult<EventSummary>` of the same recurrence (a single event lists itself) | `404` |
| `GET /api/v1/events/{id}/ics` | Query `scope=Series` for the whole series | `text/calendar` (see Calendars) | `404`, `429` |
| `GET /api/v1/events/categories`, `POST .../categories/enumerate` | Orderings `SortOrderAscending` (default), `LabelAscending` | `EnumerationResult<EventCategory>` (`{ id, key, label, color, sortOrder, isRetired, labels, tags, createdUtc, updatedUtc }`; not retired) | |
| `GET /api/v1/events/{id}/comments`, `POST .../comments/enumerate`, `POST .../comments`, `PUT`/`DELETE .../reactions/{key}`, `GET .../reactions`, `POST .../reactions/enumerate` | Phase 2 shapes against `commentTarget` | Phase 2 | `403 CommentsDisabled`, `409 EventCanceled` (new comments on canceled events; reactions and reports still work), Phase 2 codes |
| `POST /api/v1/events/{id}/promote`, `POST .../unpromote` | Phase 3 promote body | `PromotedEvent` feed card (conversation stays on the event) | Phase 3 codes |
| `POST /api/v1/events/proposals` | Event fields (as the admin create, without `hostUserIds` and `recurrence`; `audience` not `Public`) plus `proposalNote` (0 to 1000) | `201 EventProposal` `{ id, title, status, startUtc, timeZone, proposalNote, reviewNote, reviewedUtc, createdUtc, labels, tags }` | `400`, `403 ProposalsDisabled`, `403 PostingNotAllowed`, `403 Muted`, `409 ProposalLimitReached`, `429` |
| `POST /api/v1/events/proposals/{id}/withdraw` | | `EventProposal` (`Withdrawn`) | `404`, `409 NotPending` |
| `GET /api/v1/me/events`, `POST /api/v1/me/events/enumerate` | Filters `rsvpStatus`, `upcoming`, `past`, `category`, `format` | `EnumerationResult<EventSummary>`: events the caller RSVPed to or hosts (canceled included; hosted drafts too) | |
| `GET /api/v1/me/events/proposals`, `POST .../proposals/enumerate` | Filter `status` | `EnumerationResult<EventProposal>` | |
| `GET /api/v1/me/calendar-feed` | | `{ url, createdUtc, rotatedUtc, lastFetchedUtc }` (created on first call; the same URL afterwards) | `404 NotAvailable` (`events.calendarFeedEnabled` off) |
| `GET /api/v1/calendar/{file}` | GET /api/v1/calendar/{token}.ics with the token from /api/v1/me/calendar-feed (cfd_id.secret). The member's Going and Waitlisted events from 30 days ago to events.calendarFeedDays ahead (at most events.calendarFeedMaxEvents), one VEVENT per occurrence (UID per occurrence), never join URLs or attendee data. Cache-Control: private, max-age=900. 404 for an unknown, rotated, or disabled token (a ban, account deletion, or lost access disables it). Rate limited per token (events.icsPerHour). Anonymous. | `200` Read a personal calendar feed | `404`, `429` |
| `POST /api/v1/me/calendar-feed/rotate` | | New URL; the old one answers 404 | `404 NotAvailable` |
| `DELETE /api/v1/me/calendar-feed` | | `204` | |

RSVP rules (section 4.3): every write is one transaction that locks the event row, recounts Going plus guests from the RSVP rows, decides, writes, recomputes the counters, and promotes the oldest Waitlisted parties (by `waitlistedutc`, then id) that fit, so parallel requests never overbook and waitlist positions are exact. `Interested` never counts against capacity. A Going member who asks for more guests than fit gets `409 EventFull` and keeps the RSVP as it was. Promotion stops `events.waitlistCutoffMinutes` before the start (freed spots then stay open for anyone) and runs on cancellation, removal, a capacity raise, or the waitlist turning on; promoted members get `EventWaitlistPromoted` (in-app, push, and the Essential email `event_waitlist_promoted`). The RSVP window is `rsvpOpensUtc` (or publish) to `rsvpClosesUtc` (or the start). The caller must read the event and meet `rsvpAudience` (the higher of the event's participation tier and `rsvpAudience`); at most `events.maxActiveRsvpsPerMember` RSVPs on upcoming events. `hideFromAttendeeLists` hides the member from attendee lists and previews for everyone except event staff and hosts.

### Admin routes

| Method and path | Who | Body | Result | Errors |
|---|---|---|---|---|
| `GET /api/v1/admin/events`, `POST .../enumerate` | Event staff | Member filters plus `status` (default Draft, Published, Canceled), `when` default `All` | `EnumerationResult<AdminEvent>` | `403` |
| `POST /api/v1/admin/events` | Event staff | `{ title, slug, summary, description, category, format, startLocal, endLocal, timeZone, isAllDay, location { venueId \| name, address { line1, line2, locality, region, postalCode, countryCode }, addressVisibility, geo { lat, lng } }, online { platformLabel, joinUrl }, capacity, maxGuestsPerRsvp, waitlistEnabled, rsvpAudience, rsvpOpensUtc, rsvpClosesUtc, attendeeVisibility, externalUrl, coverMediaId, hostUserIds, instructorIds, reminderOffsetsMinutes, commentsEnabled, audience, labels, tags, recurrence }` | `201 AdminEvent` (`Draft`; the first occurrence of a series) | `400 ValidationFailed`, `InvalidSchedule`, `InvalidGeo`, `UnknownOption`, `ContentFiltered`; `403`; `409 SlugInUse`, `MediaNotAttachable` |
| `GET /api/v1/admin/events/{id}` | Event staff, hosts | | `AdminEvent` | `403`, `404` |
| `PUT /api/v1/admin/events/{id}` | Event staff; hosts for `description`, `coverMediaId`, `labels`, `tags` | Create fields plus `notifyAttendees` (default true), `recurrence`, `scope` (omitted fields stay) | `AdminEvent` | `400`, `403`, `404`, `409 EventCanceled` (a canceled event takes only labels, tags, summary, and description), `409 SlugInUse` |
| `DELETE /api/v1/admin/events/{id}` | Event staff | Query `scope` (`One`, `All`) | `204` (Drafts only) | `409 NotDraft` |
| `POST /api/v1/admin/events/{id}/publish` | Event staff | `{ "notifyMembers": false }` | `AdminEvent` | `409 EventCanceled`, `NotDraft` |
| `POST /api/v1/admin/events/{id}/cancel` | Event staff | `{ "reason": "3 to 500", "notifyAttendees": true, "scope": "One" }` | `AdminEvent` | `400`, `409 EventCanceled` |
| `POST /api/v1/admin/events/{id}/duplicate` | Event staff | `{ "startLocal", "timeZone"? }` | `201 AdminEvent` (Draft copy with labels and tags; no RSVPs, comments, reactions, or recurrence) | `400 InvalidSchedule` |
| `POST /api/v1/admin/events/{id}/approve` | Event staff | `{ "reviewNote"?, "publish": true, "notifyMembers": false }` | `AdminEvent` (the proposer becomes the host) | `409 NotPending` |
| `POST /api/v1/admin/events/{id}/reject` | Event staff | `{ "reviewNote": "3 to 1000" }` | `AdminEvent` (`Rejected`) | `400`, `409 NotPending` |
| `GET /api/v1/admin/events/proposals`, `POST .../proposals/enumerate` | Event staff | Filters `status` (default `Proposed`), `proposedByUserId`; oldest first | `EnumerationResult<AdminEvent>` | `403` |
| `GET /api/v1/admin/events/{id}/rsvps`, `POST .../rsvps/enumerate` | Event staff, hosts | Filters `status`, `checkedIn`; orderings `CreatedAscending` (default), `CreatedDescending`, `DisplayNameAscending`, `WaitlistAscending`, `UpdatedDescending` | `EnumerationResult<AdminRsvp>` (`{ id, eventId, user, status, guestCount, position, hideFromAttendeeLists, waitlistedUtc, promotedUtc, checkedInUtc, checkedInByUserId, createdUtc, updatedUtc, labels, tags }`) | `403`, `404` |
| `POST /api/v1/admin/events/{id}/rsvps/{rsvpId}/promote`, `.../move-to-waitlist`, `.../remove` | Event staff | `{ "reason"?, "notifyMember": true }` | `AdminRsvp` (promote ignores capacity; move-to-waitlist puts the RSVP at the end of the queue) | `403`, `404` |
| `POST /api/v1/admin/events/{id}/rsvps/{rsvpId}/check-in`, `.../undo-check-in` | Event staff, hosts | | `AdminRsvp` | `404`, `409 NotGoing` |
| `POST /api/v1/admin/events/{id}/rsvps/{rsvpId}/remove` | Body { reason, notifyMember } (both optional). Event staff delete the RSVP; the waitlist is promoted. Audited as event.rsvp.remove. | `200` Remove an RSVP | `403`, `404`, `409` |  |
| `GET /api/v1/admin/events/{id}/rsvps/export.csv` | Event staff, hosts | Query `continuationToken` | CSV `displayName,handle,status,guests,position,checkedInUtc,createdUtc`, up to 1000 rows per page; `X-Continuation-Token`, `X-Total-Records` | `403`, `404` |
| `GET /api/v1/admin/events/categories`, `POST .../categories/enumerate` | Event staff | Filter `includeRetired` (default true) | `EnumerationResult<EventCategory>` | `403` |
| `POST /api/v1/admin/events/categories` | SystemAdmin | `{ key, label, color, labels, tags }` | `201 EventCategory` | `400`, `409 KeyInUse`, `CategoryLimitReached` |
| `PUT /api/v1/admin/events/categories/{id}` | SystemAdmin | `{ label, color, labels, tags }` (restores a retired category) | `EventCategory` | `400`, `404` |
| `DELETE /api/v1/admin/events/categories/{id}` | SystemAdmin | | `{ "retired": true \| false }` (in use: retired; else deleted) | `404` |
| `PUT /api/v1/admin/events/categories/order` | SystemAdmin | `{ "categoryIds": [...] }` (every id once) | `{ "updated" }` | `400` |
| `GET /api/v1/admin/metrics/events?days=30` | Event staff | `days` 1 to 365 | `{ days, eventsByDay, rsvpsByDay, checkInRate, noShowRate, byCategory { objects (at most 20), totalRecords, enumerate } }` | `403` |

Audit actions: `event.create`, `event.update`, `event.publish`, `event.cancel`, `event.duplicate`, `event.delete`, `event.propose`, `event.approve`, `event.reject`, `event.withdraw`, `event.rsvp.promote`, `event.rsvp.moveToWaitlist`, `event.rsvp.remove`, `event.checkIn`, `event.undoCheckIn`, `event.category.create`, `event.category.update`, `event.category.delete`, `event.category.retire`, `event.category.order`.

Categories are seeded from `property.json` `events.categories` on first load (in SingleProperty the folder wins when the section changes on disk). `GET /api/v1/property` returns `events: { categories (not retired, in order), proposals (events.proposalsEnabled and property.json events.proposals), calendarFeedEnabled, defaultReminderOffsetsMinutes, publicListingsEnabled (events.publicListingsEnabled; clients skip the public event routes while false) }` from the database copy.

### Public routes (anonymous)

| Method and path | Result | Errors |
|---|---|---|
| `GET /api/v1/public/events`, `POST /api/v1/public/events/enumerate` | Upcoming Published events with audience `Public`, `StartAscending`, `maxResults` at most 20; filters `category`, `format`, `startsAfterUtc`, `startsBeforeUtc`; `EnumerationResult<PublicEvent>`; `Cache-Control: public, max-age=60`; rate limited per IP (`rateLimits.publicFeedPerMinute`) | `404 NotAvailable` (`events.publicListingsEnabled` off), `429` |
| `GET /api/v1/public/events/{idOrSlug}` | `PublicEvent` | `404` |

### Calendars

- `GET /api/v1/events/{id}/ics`: RFC 5545 VCALENDAR with one VEVENT: `UID` (`evt_...@<api domain>`, or the series UID `erc_...@<api domain>` plus `RECURRENCE-ID` for an occurrence), `DTSTAMP`, `SEQUENCE` (incremented on each time, location, format, or status change), `DTSTART`/`DTEND` with `TZID` and an embedded `VTIMEZONE` (one observance per real transition in the window), or `VALUE=DATE` for all-day events (exclusive end date), `SUMMARY`, `DESCRIPTION` (plain text excerpt plus the event link; the join URL only for Going callers after it becomes available), `LOCATION` (respecting `addressVisibility`), `URL`, `STATUS:CONFIRMED` or `STATUS:CANCELLED`, `CREATED`, `LAST-MODIFIED`, and a `VALARM` per reminder offset. TEXT values are escaped and lines folded at 75 octets. `Content-Disposition: attachment; filename="<slug>.ics"`.
- `GET /api/v1/events/{id}/ics?scope=Series`: the master VEVENT with `RRULE` (`FREQ`, `INTERVAL`, `BYDAY` with an ordinal for monthly weekday rules, `BYMONTHDAY`, `COUNT` or `UNTIL` in UTC; an open-ended rule carries `COUNT=events.maxOccurrences`), `EXDATE` for canceled or deleted occurrences on the rule, and an override VEVENT with `RECURRENCE-ID` for each edited occurrence.
- `GET /api/v1/calendar/{token}.ics` (anonymous; token `cfd_<id>.<secret>`, the secret stored as a SHA-256 hash and derived from `auth.tokenEncryptionKey` so `GET /me/calendar-feed` can return the same URL): the member's Going and Waitlisted events from 30 days ago to `events.calendarFeedDays` ahead, at most `events.calendarFeedMaxEvents`, one VEVENT per occurrence (UID per occurrence), never join URLs or attendee data; `Cache-Control: private, max-age=900`; rate limited per token (`events.icsPerHour`). Unknown, rotated, deleted, or disabled tokens answer 404. A ban, account deletion, or lost access (FullyGated without a membership) disables the feed.

### Reminders and notices

- Each event has 0 to 3 reminder offsets (default `events.defaultReminderOffsetsMinutes`, each 5 to 20160 minutes) stored as reminder rows due at start minus the offset. Editing the start recomputes the due times and clears `sentutc` for reminders not yet due.
- The `event_reminders` worker (every `events.reminderIntervalSeconds`) claims due reminders, skips those later than `events.reminderGraceMinutes` and those of events no longer published, and starts one idempotent fan-out per reminder (`EventReminder`, source `evt_x:<offset>:<dueTicks>`) to Going members (plus Interested with `events.remindInterested`; Waitlisted members get the first reminder only, titled "You are on the waitlist: ..."). Recipients who no longer meet the event's audience or `rsvpAudience` are skipped. The largest offset also sends the email `event_reminder` (Events email preference, one-click unsubscribe). Pushes follow the normal push rules (preferences, notification hours, pause).
- `EventUpdated` (time, location, or format changed; `notifyAttendees: false` suppresses): Going, Waitlisted, and Interested members; one unread notice per event (a later change refreshes it); email `event_updated` when Events email is on.
- `EventCanceled`: RSVPed members, in-app, push, and the Essential email `event_canceled` (sent even with Events email off). A moderation removal cancels the event and sends `EventCanceled` without the moderation reason.
- `NewEvent` (`publish` with `notifyMembers: true`): every member who may read the event and has Events notifications on.
- `EventProposalDecision` (category Account) and the email `event_proposal_decision` to the proposer; `staff_event_proposal` to event staff (the configured staff recipients plus EventManager holders); `staff_event_rsvp_digest` at most once per event per `events.staffRsvpDigestHours` for upcoming events with RSVP changes (hourly `event_rsvp_digest` worker).

### Settings (`events.*`)

`maxCategories` (50), `maxDescriptionLength` (10000), `maxDurationDays` (14), `maxHosts` (10), `defaultMaxGuestsPerRsvp` (0), `defaultReminderOffsetsMinutes` ("1440,120"), `remindInterested` (false), `reminderIntervalSeconds` (60), `reminderGraceMinutes` (30), `waitlistCutoffMinutes` (60), `joinUrlRevealMinutes` (15), `maxActiveRsvpsPerMember` (100), `calendarFeedEnabled` (true), `calendarFeedDays` (365), `calendarFeedMaxEvents` (500), `publicListingsEnabled` (false), `staffRsvpDigestHours` (24), `commentMedia` (true), `rsvpsPerMinute` (30), `rsvpsPerDay` (200), `icsPerHour` (60), `mapPerMinute` (120), `mapMaxFeatures` (500, 50 to 1000), `recurrenceHorizonDays` (180, 30 to 730), `maxOccurrences` (200, 1 to 1000), `recurrenceIntervalSeconds` (3600), `proposalsEnabled` (true), `maxPendingProposalsPerMember` (3), `proposalsPerDay` (5).

### Account lifecycle and export

Account deletion deletes the member's RSVPs (upcoming ones one by one so the waitlist promotes), the calendar feed, and host rows (events stay), and withdraws pending proposals. A ban removes RSVPs on upcoming events and disables the calendar feed. `GET /api/v1/me/export` adds `rsvps` (`[{ id, eventId, eventTitle, eventStartUtc, eventStatus, status, guestCount, hideFromAttendeeLists, checkedInUtc, createdUtc, updatedUtc }]`) and `calendarFeed` (`[{ createdUtc, rotatedUtc, isEnabled }]`, never the secret).

## Directory: venues and instructors (Member)

Phase 4 (docs/PHASE4_CONTRACT.md section 3). The property names venues and instructors (`GET /api/v1/property` `directory.venueLabel`, `venueLabelPlural`, `instructorLabel`, `instructorLabelPlural`). Every list is an `EnumerationQuery` in and `EnumerationResult<T>` out with labels and tags. The routes are object gated: a signed-in caller (or an anonymous one for `Public` entries under anonymous browsing) reaches them, and each entry decides by its `audience`; in `PartiallyGated` with `directory` gated, free accounts get `Members` entries as locked stubs (`locked: true`, `viewer { canOpen: false, requires: "Membership" }`, no details, no position). Every route answers `404 NotAvailable` when `features.directory` is off and `503 FeatureDisabled` under the `directory` kill switch. Admin routes of directory staff (SystemAdmin or DirectoryModerator) are listed under Directory (admin).

`GET /api/v1/property` `directory` is the stored configuration: `{ venueLabel, venueLabelPlural, instructorLabel, instructorLabelPlural, sessionLabel, sessionLabelPlural, programs [{ key, label }] (active), instructorRankFieldKey, submissions, claims, map { provider, styleUrl, darkStyleUrl, publicKey, attribution, defaultCenter { lat, lng }, defaultZoom, maxZoom, maxFeatures }, geocoderConfigured, publicListingsEnabled (features.directory and directory.publicListingsEnabled; clients skip the public venue routes while false) }`. With `directory.map.tileProxy` on, the style URLs point at the tile proxy. It is seeded from the `property.json` `directory` section on first use; in `SingleProperty` a changed section on disk is merged again (new program keys created, labels updated, nothing deleted).

**Weekly schedule.** Venue detail responses and create/edit requests accept `schedule: { timeZone, openingHours, sessions, notes }`. `timeZone` is an IANA zone and is required when entries exist. Each entry uses `dayOfWeek` (0 Monday through 6 Sunday), `startTime` and `endTime` (`HH:mm`, 00:00 to 23:59), and `endsNextDay` (boolean). Opening hours allow up to 28 intervals; `sessions` allows up to 100 entries, each with a required `name` and optional `instructor` (120 characters each). Clients label sessions with the property's `directory.sessionLabel` and `sessionLabelPlural` (default "Session", "Sessions"). Intervals must be longer than zero and at most 24 hours; overnight and full-day intervals set `endsNextDay: true`. Multiple sessions per day are supported. Clients built before the rename may send the deprecated input name `classes`; when `sessions` is empty its entries are used as the sessions, and responses only ever return `sessions`. Missing days mean unknown hours, not closed. `notes` allows 1000 characters. Invalid input returns `400 ValidationFailed` with schedule field errors. Omit `schedule` on edit to retain it; send `null` to clear it. Legacy `scheduleText` is retained and displayed only when no structured schedule exists. Member proposals and reviewer overrides use `fields.schedule` with the same shape. Schedules are omitted from summaries and hidden on locked stubs.

`GET /api/v1/property` also returns optional property-owned `directory.introduction` and `directory.listingNotice`, used by web and mobile directory pages.

**Address and position.** `address { line1, line2, locality, region, postalCode, countryCode, formatted }`, `addressVisibility` (`Exact`, `Approximate`, `LocalityOnly`), `geo { lat, lng, precision, source, geocodedUtc }`. Members get `Exact` venues with 5 decimal pins and the full address, `Approximate` venues with a 2 decimal pin and locality, region, and country only, and `LocalityOnly` venues with no pin (not on the map; `distanceKm` in whole kilometers). `geo.source` and `geocodedUtc` are staff only; staff see exact values everywhere.

| Method and path | Body | Result | Errors |
|---|---|---|---|
| `GET /api/v1/venues`, `POST /api/v1/venues/enumerate` | `EnumerationQuery`; orderings `NameAscending` (default without near), `DistanceAscending` (default with near; needs near), `CreatedDescending`, `CreatedAscending`, `UpdatedDescending`, `Relevance` (needs search); filters `near { lat, lng, radiusKm }` (radius 0.1 to `directory.maxRadiusKm`, default `directory.defaultRadiusKm`; GET form `near=lat,lng,radiusKm`), `bbox { south, west, north, east }` (west greater than east crosses the antimeridian; GET form `south,west,north,east`; not with near), `programs`, `countryCode`, `region`, `locality`, `visitorPolicy`, `verifiedOnly`, `includeClosed`, `instructorId`, `audience`; search covers name, affiliation, summary, locality, region, and linked instructor names | `EnumerationResult<VenueSummary>` (`distanceKm` on near queries; keyset continuation over distance and id) | `400 ValidationFailed`, `InvalidGeo` |
| `GET /api/v1/venues/{idOrSlug}` | | `Venue` (a previous slug finds the venue; adds `description`, `socialLinks`, `scheduleText`, `schedule`, `photos`, and `instructors` (at most 10 with `totalRecords` and `enumerate`; instructors the caller may not read are omitted)); a locked stub when the caller needs a membership | `404` |
| `GET /api/v1/venues/{id}/instructors`, `POST .../instructors/enumerate` | `EnumerationQuery` (`NameAscending` default) | `EnumerationResult<InstructorSummary>` with `role` (`Head`, `Regular`, `Guest`) | `404` |
| `GET /api/v1/instructors`, `POST /api/v1/instructors/enumerate` | `EnumerationQuery`; filters `venueId`, `rank` (option keys), `claimed`, `linkedUserId`, `audience` | `EnumerationResult<InstructorSummary>` | `400` |
| `GET /api/v1/instructors/{idOrSlug}` | | `Instructor` (`rank`, `rankLabel`, `rankColor`, `rankLevel` only when `directory.instructorRankFieldKey` names a `Rank` profile field; `linkedUser`; `venues` preview of at most 10; staff see `rankDiffersFromProfile`) | `404` |
| `GET /api/v1/instructors/{id}/venues`, `POST .../venues/enumerate` | `EnumerationQuery` (filter `includeClosed`) | `EnumerationResult<VenueSummary>` | `404` |
| `POST /api/v1/directory/map/enumerate` | `{ bbox (required), zoom (0 to 22, default 2), maxResults (standard member page cap), continuationToken, skip, labels, tags, filters (programs, countryCode, region, locality, visitorPolicy, verifiedOnly, instructorId, audience) }` | `EnumerationResult<MapFeature>`: `{ kind: "Point", venue { id, name, slug, geo, programs, isVerified, coverThumbUrl } }` while the viewport fits the configured feature budget, otherwise grid clusters `{ kind: "Cluster", count, lat, lng, bbox }` (one-venue cells stay points); standard continuation pages; totalRecords counts features and matchedRecords counts represented source records. LocalityOnly venues never appear; Approximate venues sit at their rounded position; maps show no locked stubs | `400 InvalidGeo`, `429` (`directory.mapPerMinute`) |
| `GET /api/v1/directory/programs`, `POST /api/v1/directory/programs/enumerate` | `EnumerationQuery` (`SortOrderAscending` default, `LabelAscending`; directory staff may set `filters.includeRetired`) | `EnumerationResult<DirectoryProgram { id, key, label, sortOrder, isRetired, labels, tags, createdUtc, updatedUtc }>` | |
| `GET /api/v1/directory/geocode`, `POST .../geocode/enumerate` | `EnumerationQuery`; required `filters.query` (2 to 200 characters), or GET `query=Exampleville` | `200 EnumerationResult<GeocodeCandidateView>` with `objects` (`id`, `rank`, `label`, `lat`, `lng`, `bbox`, empty `labels`/`tags`). `RankAscending` default; standard pages enumerate the finite top-five provider shortlist. Totals describe that shortlist. Search, epoch creation windows, metadata filters and query/actor-scoped continuation apply. Text is not stored or logged; request history drops the body | `400`, `429` (`directory.geocodePerMinute`, `directory.geocodePerDay`), `502 GeocoderUnavailable`, `503 IntegrationNotConfigured` |
| `GET /api/v1/directory/tiles/{*path}` | | The tile, style, or TileJSON from the configured tile origin with `directory.map.proxyKey` added server-side; documents are rewritten to point back at the proxy and never carry the key; `Cache-Control: public, max-age=86400`; cached in memory. Registered for 1 to 6 path segments | `400`, `404` (proxy off or upstream 404), `429` (`directory.tilesPerMinute`; half per IP signed out), `502 TileUnavailable`, `503 IntegrationNotConfigured` |
| `POST /api/v1/directory/submissions` | `{ type: NewVenue \| EditVenue \| NewInstructor \| EditInstructor \| ClaimInstructor \| ReportClosed, targetId?, fields?, note?, confirmNotDuplicate?, labels?, tags? }`. Venue fields: `name`, `affiliation`, `summary`, `description`, `address`, `addressVisibility` (default `directory.submissionDefaultAddressVisibility`, `Exact`), `geo` (`{ lat, lng }`, a pin the member placed on the map: latitude -90 to 90 and longitude -180 to 180, both required, else `400 InvalidGeo`; stored as the submission's `proposedGeo`, never as a field row, winning over a geocoded pin; an `EditVenue` may send only `geo`; applied on approval unless the reviewer sends another `geo`), `phone`, `email`, `websiteUrl`, `socialLinks`, `scheduleText`, `programs`, `visitorPolicy`, `visitorPolicyNote`, `photoMediaIds` (your own Ready images). Instructor fields: `displayName`, `bio`, `photoMediaId`, `rank`, `rankLevel`, `affiliation`, `websiteUrl`, `socialLinks`, `venueIds`. Edits send only changed fields; a claim needs a note (10 to 1000) | `201 DirectorySubmission`; `200` with `status: "Draft"` and `possibleDuplicates` when a new entry looks like an existing one (name similarity `directory.duplicateNameSimilarity`, and for venues within `directory.duplicateDistanceKm` or the same locality) and `confirmNotDuplicate` is false (nothing queued). Nothing is published until directory staff approve it | `400 ValidationFailed`, `ContentFiltered`, `UnknownOption`, `InvalidGeo`; `402` (gated directory); `403 SubmissionsDisabled`, `PostingNotAllowed`, `Muted`; `404`; `409 SubmissionLimitReached` (`directory.maxPendingSubmissionsPerMember`), `AlreadyClaimed`, `AlreadyClosed`; `429` (`directory.submissionsPerDay`, `directory.newAccountSubmissionsPerDay`) |
| `GET /api/v1/me/directory/submissions`, `POST .../enumerate` | `EnumerationQuery` (filters `status`, `type`) | `EnumerationResult<DirectorySubmission>` (your own) | `400` |
| `POST /api/v1/directory/submissions/{id}/withdraw` | | `DirectorySubmission` (`Withdrawn`; unattached media deleted) | `404`, `409 NotPending` |
| `POST /api/v1/me/instructor-links/{instructorId}/unlink` | | `204`; the linked member removes the link (always allowed, audited as `instructor.unlink`) | `404` |

`DirectorySubmission`: `{ id, type, status (Pending, Approved, Rejected, Withdrawn, Superseded; Draft in a duplicate answer), target { type, id, name }, submitter (a deleted member after account deletion), note, changes [{ field, current, proposed, isOverride }] (address parts as address.line1 and so on; list fields as lists), proposedGeo, possibleDuplicates { objects, totalRecords }, isLinkedMember (fast-track badge on EditInstructor by the linked member), isOverdue, dueUtc, reviewer (staff), reviewNote, decidedUtc, labels, tags, createdUtc, updatedUtc }`.

Reports: `POST /api/v1/reports` takes `targetType: "Venue"` and `"Instructor"` with the Phase 2 reasons plus `Inaccurate`, and `PermanentlyClosed` for venues (which also creates a `ReportClosed` submission). Directory reports have scope `Directory`, are moderated by directory staff, and never auto-hide.

### Venue reviews and member media

docs/PHASE4_CONTRACT.md sections 3.12 and 3.13. Signed-in member content: the routes need a signed-in caller (and a membership where the directory is gated; no locked stubs), follow the venue's visibility (`404` otherwise), and never appear on the public routes. `VenueSummary` (list and map list rows) and `Venue` carry `ratingAverage` (one decimal, null without reviews), `ratingCount`, and `mediaCount`. The venue detail adds `reviews { enabled, averageRating, count, distribution { "1" to "5" }, mine (your own review or null), enumerate }`, and `viewer` adds `canReview`, `canAddMedia`, `canManageMedia` (directory staff or a linked instructor of the venue), and `canReplyToReviews` (a linked instructor: an instructor entry linked to your account and to the venue). `GET /api/v1/property` `directory` adds `reviews { enabled, minLength, maxLength, replyMaxLength }` and `memberMedia`.

| Method and path | Body | Result | Errors |
|---|---|---|---|
| `GET /api/v1/venues/{id}/reviews`, `POST .../reviews/enumerate` | `EnumerationQuery`; orderings `CreatedDescending` (default, newest), `CreatedAscending`, `RatingDescending` (highest, newest first within a rating), `RatingAscending` (lowest, newest first within a rating); filters `rating` (1 to 5, one or a list), `authorUserId`, `hasReply`; search covers the review and reply text | `EnumerationResult<VenueReview>`; hidden and removed reviews only for directory staff; reviews by members you blocked or who blocked you are left out | `400`, `401`, `403 ReviewsDisabled`, `404` |
| `GET /api/v1/venues/{id}/reviews/{reviewId}` | | `VenueReview` | `403 ReviewsDisabled`, `404` |
| `POST /api/v1/venues/{id}/reviews` | `{ rating (1 to 5), body (directory.reviewMinLength 10 to directory.reviewMaxLength 2000), labels?, tags? }` | `201 VenueReview`; one live review per member per venue; the venue's linked instructors get `VenueReview` | `400 ValidationFailed`, `ContentFiltered`; `403 ReviewsDisabled`, `Muted`, `PostingNotAllowed`, `Forbidden`; `404`; `409 AlreadyReviewed`; `429` (`directory.reviewsPerDay` 10, `directory.newAccountReviewsPerDay` 2) |
| `PUT /api/v1/venues/{id}/reviews/{reviewId}` | `{ rating?, body?, labels?, tags? }` (the author; omitted fields stay) | `VenueReview` (`editedUtc` set when the rating or text changed) | as create, `403 Forbidden` (not the author), `429` (`rateLimits.editsPerHour`) |
| `DELETE /api/v1/venues/{id}/reviews/{reviewId}` | | `204` (the author; you may review again); staff remove reviews through the moderation actions | `403`, `404` |
| `PUT /api/v1/venues/{id}/reviews/{reviewId}/reply` | `{ body }` (1 to `directory.reviewReplyMaxLength` 2000) | `VenueReview`; a linked instructor writes or edits the one reply; the first reply notifies the reviewer (`VenueReviewReply`) | `400`, `403 Forbidden` (not a linked instructor, your own review, or another instructor's reply), `Muted`; `404` (also when a block in either direction hides the review); `429` |
| `DELETE /api/v1/venues/{id}/reviews/{reviewId}/reply` | | `VenueReview` (the reply's author, or directory staff, audited as `venue.review.reply.remove`) | `403`, `404` |
| `GET /api/v1/venues/{id}/media`, `POST .../media/enumerate` | `EnumerationQuery`; orderings `CreatedDescending` (default), `CreatedAscending`; filters `kind` (`Image`, `Video`), `uploaderUserId` | `EnumerationResult<VenueMediaItem>` (staff-curated `photos` stay on the venue and clients show them first); removed and hidden items only for directory staff; uploads by blocked members are left out | `400`, `401`, `404` |
| `POST /api/v1/venues/{id}/media` | `{ mediaIds (1 to 10 of your unattached uploads from POST /api/v1/media/uploads), labels?, tags? }` | `201 { objects: VenueMediaItem[] }`, shown at once | `400 ValidationFailed`, `MediaLimitExceeded`; `403 MemberMediaDisabled`, `MediaNotAllowed`, `VideoUploadNotAllowed`, `Muted`; `404`; `409 MediaNotAttachable` |
| `DELETE /api/v1/venues/{id}/media/{itemId}` | `{ reason? }` (at most 500) | `204`: the uploader deletes (media purged); directory staff and linked instructors remove (served objects retained, open reports resolved, audited as `venue.media.remove`; never a SystemAdmin's upload unless you are one) | `400`, `403`, `404` |

`VenueReview`: `{ id, venueId, author, rating, body, editedUtc, reply { body, author, instructorId, instructorName, createdUtc, editedUtc } | null, moderationState, labels, tags, viewer { isAuthor, canEdit, canDelete, canReply, canDeleteReply, canReport, canModerate }, createdUtc, updatedUtc }`. `VenueMediaItem`: `{ id, venueId, media, uploader, moderationState, labels, tags, viewer { isUploader, canRemove, canReport }, createdUtc, updatedUtc }`.

Reports and moderation: `POST /api/v1/reports` takes `targetType: "VenueReview"` and `"VenueMedia"` (Phase 2 reasons and `Inaccurate`); they queue in the `Directory` scope (directory staff and SystemAdmin) without auto-hide, and `POST /api/v1/admin/moderation/actions` applies `Hide`, `Unhide`, `Remove`, `Restore` (a hidden or removed review leaves the rating). Per-property switches `directory.reviewsEnabled` and `directory.memberMediaEnabled` (default true) are set through `PUT /api/v1/admin/directory/settings` `values`. Notifications `VenueReview` and `VenueReviewReply` (category `Replies`) link `/venues/{venueId}?review={reviewId}`. Account deletion deletes the member's reviews, replies, and media items.

## Directory (public)

Anonymous, in every access model, only while `directory.publicListingsEnabled` is on (else `404 NotAvailable`); only `Public`, `Active` venues; no `email`, no `viewer`, no linked users, and no `phone` unless `directory.publicShowsContact`. `Cache-Control: public, max-age=300`; rate limited per IP (`rateLimits.publicFeedPerMinute`).

| Method and path | Body | Result | Errors |
|---|---|---|---|
| `GET /api/v1/public/venues`, `POST /api/v1/public/venues/enumerate` | `EnumerationQuery` (filters `near`, `bbox`, `programs`, `countryCode`, `region`, `locality`, `visitorPolicy`, `verifiedOnly`) | `EnumerationResult<VenueSummary>` | `400`, `404 NotAvailable`, `429` |
| `GET /api/v1/public/venues/{id}` | | `Venue` | `404`, `429` |
| `POST /api/v1/public/directory/map/enumerate` | as the member map | `EnumerationResult<MapFeature>` | `400`, `404`, `429` |

## Profile multimedia

Profile galleries use the existing image/video upload pipeline (`POST /api/v1/media/uploads`, upload bytes, complete), then attach the uploaded IDs. Only the profile owner may attach or delete. The normal members access policy, posting/media/video permissions, mutes, blocks, and property isolation apply. Documents and another member's uploads are refused. Attachment is atomic, including concurrent requests for the same upload.

| Route | Body / query | Result |
|---|---|---|
| `POST /api/v1/users/{id}/media/enumerate` | `EnumerationQuery`; `filters.kind`: `Image` or `Video`, labels and tags | `EnumerationResult<ProfileMediaItem>`; bounded pages, newest first |
| `GET /api/v1/users/{id}/media` | Same query in GET form | Same result |
| `POST /api/v1/users/{id}/media` | `{ mediaIds: [1..10 distinct own uploads], labels?, tags? }` | `201 { objects: ProfileMediaItem[] }`; `403` permissions, `409 MediaNotAttachable` |
| `DELETE /api/v1/users/{id}/media/{itemId}` | Owner only | `204`; purges the media; `403` other owner, `404` absent |
| `GET /api/v1/media/gallery/{id}/{file}` | Anonymous; a processed image, poster, MP4, HLS playlist or segment | Bytes, `206` for Range; `404` unless the item and its parent page are currently public; `Cache-Control: no-store` |

`ProfileMediaItem`: `{ id, profileUserId, media, uploader, moderationState, labels, tags, viewer: { isUploader, canRemove, canReport }, createdUtc, updatedUtc }`. IDs use `pmd_`. Report with `POST /api/v1/reports` and `{ targetType: "ProfileMedia", targetId, reason, details? }`. Reports enter the Feed moderation scope and support the existing auto-hide, Hide, Unhide, Remove, Restore and Dismiss actions. Feed moderators may see moderated gallery items; other readers see live items. Account exports include `profileMedia`; account closure purges these uploads.

Venue and profile public pages include up to 24 live uploaded photos/videos and per-item report links into the signed-in app. The profile gallery is public only while its owner's public profile page is enabled and the account is active. Venue galleries follow the venue public-page rules and member-media setting. Public file requests recheck the page, account and item's visibility each time, including HLS segments; no permanent public copies or originals are exposed. Local and S3 storage use the same checked route. HLS playback uses native support or the bundled web player. Public pages provide a persisted light/dark switch using the same `tk.theme` preference as the website.

## Public landing pages

Server-rendered, crawlable pages for venues, events, opt-in member profiles, and published catalog entries (design record: `docs/PHASE4_CONTRACT.md` section 18). The website origin serves them at `/<path word>/<slug>`, where the path words come from the property's `publicPages` section (`docs/TRELLISKIT.md` section 2.2; engine defaults `venues`, `events`, `profiles`, `catalog`; `GET /api/v1/property` returns `publicPages { enabled, venuesPath, eventsPath, profilesPath, catalogPath }`). The website's nginx maps the public paths onto the fixed backend prefix `/api/v1/public/pages`: `/sitemap.xml`, `/sitemap-{venues|events|profiles|catalog}-{n}.xml`, `/robots.txt`, and every two-segment path that is not a file and does not start with `/app`, `/admin`, `/api`, `/assets`, `/embed`, or `/platform` (the backend answers `X-Accel-Redirect: /spa.html` for paths that are not page paths, so the website keeps its own routes).

**Slugs** (venues, events, profiles alike): lowercase letters and digits with single hyphens, 3 to 80 characters, not reserved (`admin`, `api`, `app`, `new`, `search`, `sign-in`, and the rest of the list in the contract); unique per property per kind, counting previous slugs. The default is the name plus the locality and region (`example-club-exampleville-ex`), suffixed `-2`, `-3`, ... when taken. A changed slug keeps the old one: the API finds the object by it and the page answers `301` to the current slug. Errors: `400 ValidationFailed` (`fieldErrors.slug`), `409 SlugInUse` (`fieldErrors.slug`). `PUT /api/v1/admin/events/{id}` now accepts `slug` from the event's hosts as well as event staff.

**`publicUrl`** (absolute, or null when there is no page) is on `Venue`, `VenueSummary`, `AdminVenue` (every listed venue: not deleted, `Active`, moderation `None`, any audience), `Event`, `EventSummary`, `AdminEvent`, `PublicEvent` (only `Public`, `Published` or `Canceled` events), and the public profile of `GET /api/v1/users/{id}` (only when the member turned the page on). `MeResponse.publicPage` is `{ enabled, slug, suggestedSlug: null, path, url }`.

| Method and path | Auth | Body | Result | Errors |
|---|---|---|---|---|
| `GET /api/v1/public/pages/resolve/{path}/{slug}` | Anonymous | | `200` HTML page (title, meta description, canonical from `web.publicBaseUrl`, Open Graph, Twitter card, JSON-LD `SportsActivityLocation`, `Event`, or `ProfilePage`), `301` to the current slug (previous slug or other letter case), `404` HTML not found page (`noindex`). A `path` that is not one of the property's page paths (or pages off) answers `404` with `X-Accel-Redirect: /spa.html` | `429` (`rateLimits.publicPagesPerMinute`, default 600) |
| `GET /api/v1/public/pages/sitemap.xml` | Anonymous | | Sitemap index: `<site>/sitemap-pages.xml` (the website build's marketing pages) and `<site>/sitemap-{kind}-{n}.xml` for each kind with pages (10,000 URLs per child, at most 100 per kind). `Cache-Control: public, max-age=3600` | `429` |
| `GET /api/v1/public/pages/sitemaps/{kind}-{n}.xml` | Anonymous | | `urlset` with `loc`, `lastmod`, `changefreq`, `priority`: live Active venues; Published Public events that have not ended; enabled profile pages of members who are neither banned nor deleted | `404` (unknown kind or past the last page), `429` |
| `GET /api/v1/public/pages/well-known/apple-app-site-association` | Anonymous | | Served at `/.well-known/apple-app-site-association`: `{ "applinks": { "apps": [], "details": [{ "appID", "appIDs": [...], "paths": ["/app/check-in"], "components": [{ "/": "/app/check-in" }] }] }, "webcredentials": { "apps": ["<mobile.appleTeamId>.<mobile.bundleId>"] } }` (`application/json`, `Cache-Control: public, max-age=3600`), so iOS offers to save and autofill the app's passwords for the website domain and opens check-in QR links in the app; no other website path opens the app (the app declares `webcredentials:<domains.web>` and `applinks:<domains.web>`) | `404` when `mobile.appleTeamId` or `mobile.bundleId` is empty |
| `GET /api/v1/public/pages/well-known/assetlinks.json` | Anonymous | | Served at `/.well-known/assetlinks.json`: one statement with the relations `delegate_permission/common.get_login_creds` and `delegate_permission/common.handle_all_urls` for `mobile.androidPackage` with `mobile.androidCertFingerprints` (upper-cased, deduplicated); the app's only verified link is `https://<domains.web>/app/check-in` | `404` when either is empty |
| `GET /api/v1/public/pages/sitemaps/{name}` | Served at /sitemap-{name}: venues-{n}.xml, events-{n}.xml, profiles-{n}.xml, or catalog-{n}.xml (loc, lastmod, changefreq, priority). Events leave the sitemap once they end. Catalog entries appear only while Published, unmoderated, and undeleted with `features.catalog` on; catalog sitemaps require revalidation. Anonymous. | `200` Child sitemap | `404`, `429` |  |
| `GET /api/v1/public/pages/robots.txt` | Anonymous | | `User-agent: *`, `Allow: /` and the public media, avatar, and brand image paths, `Disallow: /app`, `/admin`, `/api`, and `Sitemap: <site>/sitemap.xml` | `429` |
| `GET /api/v1/me/public-page` | Signed in | | `ProfilePageView { enabled (default false), slug, suggestedSlug, path, url }` (`suggestedSlug` uses the display name and only the city and region signed-out visitors may see) | `404 NotAvailable` (pages off) |
| `PUT /api/v1/me/public-page` | Signed in | `{ enabled?, slug? }` (omitted fields stay; turning it on without a slug uses `suggestedSlug`) | `ProfilePageView`; audited `profile.public_page` | `400 ValidationFailed`, `404 NotAvailable`, `409 SlugInUse` |
| `POST /api/v1/public-pages/slugs/check` | Signed in | `{ kind: Venue \| Event \| Profile \| Catalog, slug, id? }` (`id`: the venue, event, or catalog entry being edited; a profile check excludes the caller) | `{ kind, slug, valid, available, problem: null \| required \| length \| format \| reserved \| taken, path, url }` | `429` |
| `GET`, `PUT /api/v1/admin/members/{id}/public-page` | Admin | as the member routes | `ProfilePageView`; audited `profile.public_page` | `400`, `404`, `409 SlugInUse` |

Catalog pages default to `/catalog/{slug}` (`publicPages.catalogPath`), require both `publicPages.enabled` and `features.catalog`, and render only Published, unmoderated, undeleted entries. An unpublished identifier or slug returns the same noindex 404 as a missing entry; a published identifier redirects to its current canonical slug. Only Published relationship targets appear (at most 50). Ordered instructions, structured details, and public source and license attribution are escaped; private review notes, author and reviewer ids, source model metadata, and lesson ids are omitted. Catalog pages and their child sitemaps use `Cache-Control: public, max-age=0, must-revalidate`. Sitemap `lastmod` follows the entry's `updatedUtc`. `POST /api/v1/public-pages/slugs/check` accepts `kind: Catalog`. See [catalog public pages](docs/CATALOG_PUBLIC_PAGES.md).

What each page shows: a venue's name, affiliation, summary, description, address by `addressVisibility` (Exact: full address and coordinates; Approximate: locality and region, coordinates at 2 decimals; LocalityOnly: locality and region, no coordinates), programs, schedule, visitor policy, website and social links, phone only with `directory.publicShowsContact`, linked instructors (name and rank), rating average and count, and the staff photos (never member reviews or member media). An event's title, category, local time and zone, canceled or ended notice, summary, description, format, location (stricter of the event's and venue's visibility), spots left, and cover (never join URLs or attendees). A profile's display name, avatar, rank and affiliation, city, region, and country, bio, and the profile fields shown on profiles with `Public` visibility (never members-only, staff, or private fields, email, or phone). Every page links into the member app (`/app/venues/{id}`, `/app/events/{id}`, `/app/members/{id}`) and to join or sign in, uses the property's logo and light and dark colors, encodes every value, and sends a strict `Content-Security-Policy`.

## Directory (admin)

Directory staff (SystemAdmin or DirectoryModerator; grant with `POST /api/v1/admin/moderators` `resourceType: "Directory"`) unless noted; programs and settings are SystemAdmin only. Member sessions reach these routes and get `403 Forbidden` without the role.

| Method and path | Body | Result | Errors |
|---|---|---|---|
| `GET /api/v1/admin/venues`, `POST .../enumerate` | member filters plus `status` (`Active`, `Closed`, `Archived`), `geoSource` (`None` finds venues that need a pin), `verified` | `EnumerationResult<AdminVenue>` (Venue plus `openReportCount`, `pendingSubmissionCount`, `geoSource`, `geocodedUtc`, `needsPin`, `moderationState`, `closedReason`, `closedUtc`; exact address and pin) | `400`, `403` |
| `POST /api/v1/admin/venues` | `{ name, slug?, affiliation, summary, description, address { line1, line2, locality, region, postalCode, countryCode }, addressVisibility (default Exact), geo? { lat, lng } (a manual pin), geocode? (true: geocode now), timeZone, phone, email, websiteUrl, socialLinks [{ kind, url }], scheduleText, programs, visitorPolicy, visitorPolicyNote, photoMediaIds, audience (Public only by SystemAdmin), labels, tags }` | `201 AdminVenue`; without a pin the address is geocoded when a geocoder is configured; a provider failure leaves `geo.source: None` (needs a pin) | `400 ValidationFailed`, `InvalidGeo`, `UnknownOption`, `ContentFiltered`, `GeocodeFailed`; `403`; `409 SlugInUse`, `MediaNotAttachable`; `503 IntegrationNotConfigured` (geocode requested) |
| `GET`, `PUT`, `DELETE /api/v1/admin/venues/{id}` | `PUT`: any create field (omitted fields stay; a new slug keeps the old one as a redirect; an address change geocodes again and replaces a manual pin unless `geo` is sent) | `AdminVenue`, `AdminVenue`, `204` (soft delete; links removed) | `400`, `403`, `404`, `409 SlugInUse` |
| `POST /api/v1/admin/venues/{id}/verify`, `.../unverify` | `{ note? }` | `AdminVenue` | `403`, `404` |
| `POST /api/v1/admin/venues/{id}/close`, `.../reopen`, `.../archive`, `.../unarchive` | `{ reason? }` | `AdminVenue` | `403`, `404` |
| `PUT /api/v1/admin/venues/{id}/instructors/{instructorId}`, `DELETE` same | `{ role?: Head \| Regular \| Guest }` | `InstructorSummary` with `role`, `204` | `403`, `404` |
| `POST /api/v1/admin/venues/{id}/archive` | Body { reason? }. Archived venues are staff only. Audited as venue.archive. Directory staff (SystemAdmin or DirectoryModerator). | `200` Archive a venue | `400`, `403`, `404` |  |  |
| `POST /api/v1/admin/venues/{id}/unarchive` | Body { reason? }. Audited as venue.archive (unarchived). Directory staff (SystemAdmin or DirectoryModerator). | `200` Unarchive a venue | `400`, `403`, `404` |  |  |
| `GET /api/v1/admin/instructors`, `POST .../enumerate` | member filters plus `status` (`Active`, `Archived`) | `EnumerationResult<AdminInstructor>` | `400`, `403` |
| `POST /api/v1/admin/instructors` | `{ displayName, slug?, bio, photoMediaId, rank, rankLevel, affiliation, websiteUrl, socialLinks, venueIds (linked as Regular), audience, labels, tags }` | `201 AdminInstructor` | `400 ValidationFailed`, `UnknownOption`, `ContentFiltered`; `403`; `404` (a venue); `409 SlugInUse` |
| `GET`, `PUT`, `DELETE /api/v1/admin/instructors/{id}` | | `AdminInstructor`, `AdminInstructor`, `204` | `400`, `403`, `404`, `409` |
| `POST /api/v1/admin/instructors/{id}/link` | `{ userId }` | `AdminInstructor`; the member gets `InstructorLinked` and may unlink themselves | `400`, `403`, `404`, `409 AlreadyClaimed` |
| `POST /api/v1/admin/instructors/{id}/unlink`, `.../archive`, `.../unarchive` | | `AdminInstructor` | `403`, `404` |
| `POST /api/v1/admin/instructors/{id}/unarchive` | Audited as instructor.update. Directory staff (SystemAdmin or DirectoryModerator). | `200` Unarchive an instructor | `403`, `404` |
| `GET /api/v1/admin/directory/submissions`, `POST .../enumerate` | filters `status` (default `Pending`), `type`, `submitterUserId`, `targetId`; orderings `CreatedAscending` (default, oldest first) and `CreatedDescending` | `EnumerationResult<DirectorySubmission>` | `400`, `403` |
| `GET /api/v1/admin/directory/submissions/{id}` | | `DirectorySubmission` with a fresh diff against the current target (this is what the approval stale check compares against) and the possible duplicates of new entries | `403`, `404` |
| `PUT /api/v1/admin/directory/submissions/{id}` | `{ fields { ... }, geo? { lat, lng } }` reviewer overrides | `DirectorySubmission` | `400`, `403`, `404`, `409 NotPending` |
| `POST /api/v1/admin/directory/submissions/{id}/approve` | `{ reviewNote?, notifySubmitter (default true), geo? }` | `{ submission, target }`; applied in one transaction (create or update the entry, attach media, link venues, link the claimant, close the venue); `SubmissionDecision` notification and `directory_submission_decision` email | `400`, `403`, `404`, `409 SubmissionStale` (reload and review again), `NotPending`, `AlreadyClaimed`, `SlugInUse` |
| `POST /api/v1/admin/directory/submissions/{id}/reject` | `{ reviewNote (3 to 1000), notifySubmitter (default true) }` | `DirectorySubmission` (unattached submission media deleted) | `400`, `403`, `404`, `409 NotPending` |
| `POST /api/v1/admin/directory/import` | `{ rows [VenueRequest] (at most 1000; a slug that exists updates that venue), dryRun (default true) }` | `{ dryRun, created, updated, skipped, errors { objects [{ row, field, message }] (at most 100), totalRecords } }`; geocoding runs in the background | `400`, `403` |
| `POST /api/v1/admin/directory/regeocode` | `{ venueIds? (at most 500), onlyMissing (default true) }` | `202 { queued }` (GeocodeWorker; manual pins kept) | `400`, `403`, `503 IntegrationNotConfigured` |
| `GET /api/v1/admin/directory/export.csv` | | CSV of every live venue and instructor, streamed | `403` |
| `GET /api/v1/admin/directory/metrics?days=30` | `days` 7 to 365 (default 30) | `{ activeVenues, closedVenues, archivedVenues, verifiedVenues, needsPin, instructors, claimedInstructors, pendingSubmissions, overdueSubmissions, submissionsLast30Days, approvedLast30Days, days, submissionsByDay, approvalsByDay, venuesCreatedByDay }`; each daily series has exactly one `{ date, count }` per day (submissions created, submissions approved by decision time, venues created) | `400` (`fieldErrors.days`), `403` |
| `GET /api/v1/admin/directory/programs`, `POST .../programs/enumerate` | `EnumerationQuery` (retired programs included unless `filters.includeRetired` is false) | `EnumerationResult<DirectoryProgram>` | `403` |
| `POST /api/v1/admin/directory/programs` (SystemAdmin) | `{ key, label, labels?, tags? }` | `201 DirectoryProgram` | `400`, `403`, `409 ProgramKeyInUse`, `ProgramLimitReached` |
| `PUT /api/v1/admin/directory/programs/{id}` (SystemAdmin) | `{ label?, labels?, tags? }` (keys never change; a label restores a retired program) | `DirectoryProgram` | `400`, `403`, `404` |
| `DELETE /api/v1/admin/directory/programs/{id}` (SystemAdmin) | | `{ retired }`: a program in use is retired (hidden from forms and filters, values kept); an unused one is deleted | `403`, `404` |
| `PUT /api/v1/admin/directory/programs/order` (SystemAdmin) | `{ programIds }` (every non-retired program once) | `{ updated }` | `400`, `403` |
| `GET /api/v1/admin/directory/settings` (SystemAdmin) | | `{ config { venueLabel, venueLabelPlural, instructorLabel, instructorLabelPlural, sessionLabel, sessionLabelPlural, instructorRankFieldKey, submissions, claims, updatedUtc }, settings [PropertySettingField of every directory.* property key; secrets report isSet], geocoderConfigured }` | `403` |
| `PUT /api/v1/admin/directory/settings` (SystemAdmin) | `{ venueLabel?, venueLabelPlural?, instructorLabel?, instructorLabelPlural?, sessionLabel?, sessionLabelPlural? (1 to 40), instructorRankFieldKey? (a Rank field; empty clears), submissions?, claims?, values { "directory.*": value }, clear ["directory.*"] }` | the settings | `400`, `403` |

Audit actions: `venue.create`, `venue.update`, `venue.delete`, `venue.verify`, `venue.unverify`, `venue.close`, `venue.archive`, `instructor.create`, `instructor.update`, `instructor.delete`, `instructor.link`, `instructor.unlink`, `directory.submission.approve`, `directory.submission.reject`, `directory.import`, `directory.settings.update`.

**Per-property settings** (`/api/v1/admin/property/settings` or the directory settings route; secrets stored encrypted): `directory.publicListingsEnabled`, `directory.publicShowsContact`; `directory.reviewsEnabled`, `directory.memberMediaEnabled` (default true); `directory.geocoder.provider` (`None` default, `Nominatim`, `AwsLocation`, `Http`), `.baseUrl` (must pass the outbound address policy: public internet addresses, ports 80 and 443), `.apiKey` (secret), `.accessKeyId`, `.authHeaderName`, `.email`, `.placeIndexName`, `.region`, `.requestsPerSecond` (1 to 50, default 1), `.cacheDays` (1 to 3650, default 90); `directory.map.provider` (`None` default, `MapLibre`, `PlatformNative`), `.styleUrl`, `.darkStyleUrl`, `.publicKey`, `.attribution`, `.tileProxy`, `.proxyKey` (secret), `.proxyKeyParameter` (default `key`), `.tileBaseUrl` (the only origin the proxy fetches; empty uses the style URL's origin), `.defaultCenterLat`, `.defaultCenterLng`, `.defaultZoom`, `.maxZoom`, `.maxFeatures`.

For `AwsLocation`, set `.accessKeyId` and the secret access key in `.apiKey` to use property-specific AWS credentials. When the complete pair is absent, the adapter uses the default AWS SDK credential chain. `.region` and `.placeIndexName` select the AWS Location place index; secrets remain redacted in settings responses, and changing credentials rebuilds the cached adapter.

**Deployment settings** (`/api/v1/settings`): `directory.maxPrograms`, `maxDescriptionLength`, `maxPhotos`, `maxSocialLinks`, `defaultRadiusKm`, `maxRadiusKm`, `maxPendingSubmissionsPerMember`, `reviewTargetHours`, `duplicateNameSimilarity`, `duplicateDistanceKm`, `submissionDefaultAddressVisibility` (default `Exact`), `submissionsPerDay`, `newAccountSubmissionsPerDay`, `geocodePerMinute`, `geocodePerDay`, `mapPerMinute`, `tilesPerMinute`, `geocodeIntervalSeconds`, `geocodeBatchSize`, `tileCacheEntries`, `tileTimeoutSeconds`, `geocodeTimeoutSeconds`.

Workers: `GeocodeWorker` (queued import and regeocode venues; `directory.geocodeIntervalSeconds`), `SubmissionDueWorker` (sends `SubmissionOverdue` to directory staff once per submission past `directory.reviewTargetHours`), `GeocodeCachePruneWorker` (every 6 hours).

## Phase 4 engagement: comments, reactions, reports, moderation, search, and promotion on Phase 4 types

The Phase 4 content types (`Venue`, `Instructor`, `Event`, `Series`, `Lesson`) plug into the Phase 2 and Phase 3 engagement services through content target providers. Each feature decides visibility, write rules, and moderation state for its own objects; the shared routes below behave the same for every type.

### Conversations on events and lessons

Events and lessons host one conversation each (`commentTarget` is the object itself). Comment edits, deletes, children, permalinks, revisions, and comment reactions use the target-independent `/api/v1/comments/{id}` routes.

| Method and path | Body | Success | Errors |
|---|---|---|---|
| `GET /api/v1/events/{id}/comments`, `POST .../comments/enumerate` (same for `/api/v1/lessons/{id}`) | `EnumerationQuery` (filters `depth` 1 to 5, `childLimit` 1 to 20; orderings `Top`, `CreatedDescending`, `CreatedAscending`) | `CommentTreePage` with `commentCount`; moderators of the scope see hidden and removed comments | `400`, `402 MembershipRequired`, `404` (the caller cannot read the object), `429` |
| `POST /api/v1/events/{id}/comments`, `POST /api/v1/lessons/{id}/comments` | `CommentRequest { body, parentCommentId?, mediaIds?, showLinkPreview?, labels?, tags? }` | `201 Comment`; mentions notify with the object's path (`/events/{id}?comment=...`) | `400`, `402`, `403 CommentsDisabled`, `PostingNotAllowed`, `MediaNotAllowed`, `VideoUploadNotAllowed`, `Muted`, `InteractionBlocked`, `404`, `409 EventCanceled`, `ParentUnavailable`, `429` |
| `PUT`, `DELETE /api/v1/events/{id}/reactions/{key}` (and lessons) | | `{ reactionCounts, reactionTotal, myReactions }` | `400 UnknownReaction`, `402`, `403 Muted`, `InteractionBlocked`, `404`, `429` |
| `GET /api/v1/events/{id}/reactions`, `POST .../reactions/enumerate` (and lessons) | `EnumerationQuery` (filter `key`) | `EnumerationResult<ReactionListItem>` | `400`, `404`, `429` |

`PUT`, `DELETE /api/v1/comments/{id}` and `PUT`, `DELETE /api/v1/comments/{id}/reactions/{key}` work for comments under events and lessons; `GET /api/v1/comments/{id}` and `.../children` answer `404` when the caller cannot read the host object.

### Promote to the feed

| Method and path | Body | Success | Errors |
|---|---|---|---|
| `POST /api/v1/lessons/{id}/promote`, `POST /api/v1/events/{id}/promote` | `PromoteRequest { title?, excerpt?, pin? (SystemAdmin only), audience? (only narrows), notifyMembers? (default true for lessons, false for events) }` | `201 FeedPost` with `kind` `PromotedLesson` or `PromotedEvent`, `source { type, id, title }`, `commentTarget` the lesson or event | `400 ValidationFailed` (not published, broader audience), `403 PromotionNotAllowed`, `404`, `409 AlreadyPromoted`, `PinLimitReached`, `429` |
| `POST /api/v1/lessons/{id}/unpromote`, `POST /api/v1/events/{id}/unpromote` | | `{ targetType, targetId, unpromoted, feedPostId }` (idempotent) | `403` (SystemAdmin, the member the card is attributed to, or a moderator of the scope), `404`, `429` |
| `GET /api/v1/lessons/{id}/comments` | Top-level comments on a lesson with replies nested up to depth levels (filter depth 1 to 5, default 3) and childLimit replies each (filter childLimit 1 to 20, default 5); the result adds commentCount. Orderings Top (default), CreatedDescending, CreatedAscending. moreChildren continues through POST /api/v1/comments/{id}/children/enumerate. Moderators of the scope see hidden and removed comments. maxResults default 20, max 50. 404 when the caller cannot read the Lesson. | `200` List the comment tree of a lesson | `400`, `402`, `404`, `429` |
| `GET /api/v1/lessons/{id}/reactions` | Newest first; filter key; members you blocked are omitted. maxResults default 50, max 100. | `200` Who reacted to a lesson | `400`, `404`, `429` |
| `POST /api/v1/lessons/{id}/comments/enumerate` | EnumerationQuery body (filters.depth, filters.childLimit); same result as the GET form. Top-level comments on a lesson with replies nested up to depth levels (filter depth 1 to 5, default 3) and childLimit replies each (filter childLimit 1 to 20, default 5); the result adds commentCount. Orderings Top (default), CreatedDescending, CreatedAscending. moreChildren continues through POST /api/v1/comments/{id}/children/enumerate. Moderators of the scope see hidden and removed comments. maxResults default 20, max 50. 404 when the caller cannot read the Lesson. | `200` Enumerate the comment tree of a lesson | `400`, `402`, `404`, `429` |
| `POST /api/v1/lessons/{id}/reactions/enumerate` | EnumerationQuery body (filters.key); same result as the GET form. | `200` Enumerate who reacted to a lesson | `400`, `404`, `429` |

Comments and reactions on a `PromotedLesson` or `PromotedEvent` card answer `409 UseCommentTarget` (use its `commentTarget`). Reports on the card resolve to the source. Hiding, removing, unpublishing, or canceling the source takes the card out of the feed (`GET /api/v1/feed/{id}` answers `404` for members) until the source is live again; deleting the source deletes the card.

### Reports

`POST /api/v1/reports` accepts `targetType` `Venue`, `Instructor`, `Event`, `Series`, and `Lesson` (while the feature is on) and comments under events and lessons. The caller must be able to read the object (`404` otherwise). Reports are stored with the scope of the type: `Directory` (venues, instructors), `Events` (events and their comments), `Library` (series, lessons, and their comments).

New reasons: `Inaccurate` (Phase 4 objects) and `PermanentlyClosed` (venues only; it also creates a `ReportClosed` directory submission). Using either on another target answers `400 ValidationFailed` (`fieldErrors.reason`). Directory entries are never auto-hidden; other types follow their feature.

### Moderation

`DirectoryModerator`, `EventManager`, and `LibraryEditor` holders reach `/api/v1/admin/moderation/*` and see and act only in their scope (`Directory`, `Events`, `Library`). When `library.forumModeratorsModerateComments` is on, `Moderator` holders with `resourceId: "*"` also moderate comments under lessons (not the lessons). A non-admin role never acts on content authored by a SystemAdmin, and such reports never appear in a scoped queue.

- `POST /api/v1/admin/moderation/actions` accepts `targetType` `Venue`, `Instructor`, `Event`, `Series`, `Lesson` (actions `Hide`, `Unhide`, `Remove`, `Restore`); outside the caller's scope `403 Forbidden`.
- `GET /api/v1/admin/moderation/queue`, `POST .../queue/enumerate`: `filters.scopeType` also accepts `Directory`, `Events`, `Library`; `filters.targetType` accepts the Phase 4 types; rows carry `preview { title, excerpt, hasMedia }` and `targetState` (`Visible`, `Hidden`, `Removed`, `Deleted`) from the feature, and `scope { type, id }`.
- `GET /api/v1/admin/moderation/queue/{targetType}/{targetId}` (and `/reports/enumerate`, `/actions/enumerate`, `/dismiss`): Phase 4 types; `target` is the feature's moderation view.
- Overdue notices for `Directory`, `Events`, and `Library` reports go to SystemAdmins and the holders of the scope's role.

### Global search

`POST /api/v1/search/enumerate`: `filters.types` also accepts `Venue`, `Instructor`, `Event`, `Series`, `Lesson` (searched while the feature is wired). Results carry `path` (`/venues/{id}`, `/instructors/{id}`, `/events/{id}`, `/series/{id}`, `/lessons/{id}`), and every hit passes the object's own visibility check as the caller (locked stubs are not results). Event results add `event { startUtc, timeZone, isCanceled, isPast, recurrenceId, recurrenceSummary, occurrenceCount }` (null on other types). The occurrences of one recurring series collapse into a single result, in the rank position of its first hit, that opens the next upcoming occurrence that is not canceled (else the next upcoming canceled one, else the latest past one), with `occurrenceCount` matching occurrences and the plain-language `recurrenceSummary`. Canceled events stay in the results with `isCanceled: true`; clients mark them.

### Staff grants

`POST /api/v1/admin/moderators` with `resourceType` `Directory`, `Events`, `Library`, or `Ranks` grants `DirectoryModerator`, `EventManager`, `LibraryEditor`, or `RankAwarder` (`resourceId: "*"`); the moderators list shows them with `roles`; `DELETE /api/v1/admin/moderators/{userId}?resourceType=Library` (or `Directory`, `Events`, `Ranks`) revokes. Admin only.

## Properties and deployment modes

A property is the tenant: its own members (the same email can register independently in two properties), sessions, content, media, labels and tags, settings-backed integrations, and audit. Every property-owned row carries `propertyid` and every query is scoped by it.

| Mode (`deployment.mode`) | Behaviour |
|---|---|
| `SingleProperty` (default) | One backend serves the property in `property.directory` (`TK_PROPERTY_DIR`). It is registered in the database on first start (domains from `property.json` `domains`, apps from `mobile.bundleId` and `mobile.androidPackage`) and re-synchronized when its `property.json` changes on disk. The server refuses to start when the database holds another property or more than one. |
| `MultiProperty` | One backend serves up to `deployment.maxProperties` (25) properties registered by platform administrators ([Platform](#platform-multiproperty)), plus the folder property on first start. Requires `platform.hosts`. |

**Property ids** are opaque, generated, and never renamed: `app_{k-sortable}_{random}` (for example `app_mwczw7mg_F8RsDcNidltPWZS1seB`), written into `property.json` `id`. The optional `slug` (lowercase letters, digits, hyphens) is cosmetic only (folder names, image tags, infrastructure names) and never identifies the property in data, storage keys, headers, or URLs. Every storage key begins with the property id (`<propertyId>/media/...`, avatars under `{storage.directory}/<propertyId>/avatars`).

**Resolution** (every request, first match wins):

| Step | Rule |
|---|---|
| 1 | Special routes need no property: `/api/v1/health*`, `/openapi.json`, `/swagger`, CORS preflight, and `/api/v1/webhooks/*` (they resolve from their own content). |
| 2 | `/api/v1/platform/*` resolves to the platform realm, only on a platform host (`platform.hosts`) and only in `MultiProperty`; elsewhere `404 PropertyNotFound` (`404 NotAvailable` in `SingleProperty`). |
| 3 | `X-TK-Property: <id>`: that property; unknown, disabled, or being deleted is `404 PropertyNotFound` (in `SingleProperty`, any id but the one property). When the host is a domain of another property: `400 PropertyMismatch`. |
| 4 | The host (`X-Forwarded-Host` when `deployment.trustForwardedHost`, else `Host`; lowercased, port removed) matches a property domain: that property. |
| 5 | `SingleProperty`: the one property (so `127.0.0.1` and `localhost` work unchanged). |
| 6 | Otherwise `404 PropertyNotFound`. |

After a property resolves, `X-TK-App` (when sent) must be one of its enabled apps, else `403 AppNotRegistered`; in `MultiProperty` a request with `X-Client: mobile` must send it (`deployment.requireAppHeader`).

**Binding.** Access tokens carry the property they were issued for (payload version 2): presented to another property they are `401 Unauthorized`. Refresh, verification, reset, and unsubscribe tokens are looked up within the property, so another property's token is unknown (`401 InvalidRefreshToken`, `400 InvalidToken`). Continuation tokens from another property are `400 InvalidContinuationToken`. A request naming an object of another property (a path id, a media id to attach, a report target, a sign-up code) gets the route's not-found answer.

---

## Platform (MultiProperty)

Platform routes exist only in `MultiProperty` (`404 NotAvailable` in `SingleProperty`) and only on a platform host (`platform.hosts`; elsewhere `404 PropertyNotFound`). Platform administrators are separate from every property's members: they have their own accounts (`pad_`) and sessions (`pss_`) and sign in with `POST /api/v1/platform/auth/login`, which returns a bearer token `tkp_...` valid for `platform.sessionMinutes`. The break-glass admin token also passes platform routes. Member access tokens never do (`401 Unauthorized`). Every platform action is audited under the platform realm (`POST /api/v1/platform/audit/enumerate`).

Bootstrap: when `platform.bootstrapAdminEmail` is set and no platform administrator exists, startup creates one. With `platform.bootstrapAdminPassword` (from the environment) it can sign in at once; otherwise an invitation token is logged once at warning level.

`PropertyRecord`:

```json
{
  "id": "app_mwd1j5o5_FlHVU4CoaxDMHyBFZoJ",
  "slug": "second",
  "name": "Second Example",
  "status": "Enabled",
  "domains": [ { "id": "pdm_...", "host": "second.test", "kind": "Web", "verifiedUtc": "2026-11-11T20:00:00Z", "source": "Platform", "verification": null } ],
  "apps": [ { "id": "ppa_...", "platform": "Ios", "appId": "test.second.trelliskit", "storeAppId": null, "enabled": true } ],
  "memberCount": 0,
  "createdUtc": "2026-11-11T20:00:00Z", "updatedUtc": "2026-11-11T20:01:00Z",
  "enabledUtc": "2026-11-11T20:01:00Z", "disabledUtc": null, "disabledReason": null,
  "deleteScheduledUtc": null, "purgeStartedUtc": null,
  "labels": [], "tags": {}
}
```

`status`: `Disabled` (new properties start here), `Enabled`, `Deleting` (restorable until the purge starts), `Deleted` (purged tombstone; the id is never reused). `domains` (at most 20; kinds `Web`, `Api`, `Media`, `Redirect`, `Alias`) and `apps` (at most 10; platforms `Ios`, `Android`) are fixed-bound lists; hosts and app ids are unique across the deployment.

| Method and path | Body | Success | Errors |
|---|---|---|---|
| `POST /api/v1/platform/auth/login` | `{ email, password }` | `200 { accessToken, expiresUtc, admin }` | `400 ValidationFailed`, `401 InvalidCredentials`, `423 AccountLocked` (5 failures, 15 minutes) |
| `POST /api/v1/platform/auth/accept-invite` | `{ token, password }` | `204` | `400 InvalidToken`, `400 ValidationFailed` |
| `POST /api/v1/platform/auth/logout` | | `204` | `401` |
| `GET /api/v1/platform/auth/me` | | `200 PlatformAdmin { id, email, displayName, invitationPending, lastLoginUtc, createdUtc, labels, tags }` | `401`, `403` (break-glass token) |
| `GET /api/v1/platform/admins`, `POST /api/v1/platform/admins/enumerate` | `EnumerationQuery`; search over email and display name; orderings `CreatedDescending`, `CreatedAscending`, `EmailAscending` | `200 EnumerationResult<PlatformAdmin>` | |
| `POST /api/v1/platform/admins` | `{ email, displayName }` | `201 { admin, inviteToken, inviteDelivery, inviteExpiresUtc }`: the invitation is emailed (template `platform_admin_invite`, link `https://<first platform host>/platform/accept-invite?token=...`) through the deployment's email settings (`email.provider`, `email.mailgunDomain` and `email.mailgunApiKey`, or `email.smtpHost`); then `inviteDelivery` is `Email` and `inviteToken` is null. Without a deployment provider `inviteDelivery` is `Log` and the token is returned once (and logged) | `400`, `409 EmailInUse` |
| `DELETE /api/v1/platform/admins/{adminId}` | | `204`; sessions revoked | `404`, `409 LastPlatformAdmin`, `409 CannotRemoveOwnAdmin` |
| `GET /api/v1/platform/properties`, `POST /api/v1/platform/properties/enumerate` | `EnumerationQuery`; search over id, slug, name; filters `status`, `host`; orderings `CreatedDescending`, `CreatedAscending`, `NameAscending`, `UpdatedDescending` | `200 EnumerationResult<PropertyRecord>` (purged tombstones only with `filters.status = Deleted`) | |
| `POST /api/v1/platform/properties` | `{ name?, slug?, document? (a property.json object), cloneFromPropertyId?, domains?, apps?, labels?, tags? }`. The id is always generated; `document` or `cloneFromPropertyId` is required; omitted `domains` and `apps` come from the document | `201 PropertyRecord` (`Disabled`) | `400 ValidationFailed`, `404` (clone source), `409 PropertyLimitReached`, `DomainInUse`, `AppIdInUse`, `409 SlugInUse` (`fieldErrors.slug`; the slug, given or from the document, is used by another property; never auto-suffixed) |
| `GET /api/v1/platform/properties/{propertyId}` | | `200 PropertyRecord` | `404` |
| `PUT /api/v1/platform/properties/{propertyId}` | any of `name`, `domains` (replaces), `apps` (replaces), `labels`, `tags` | `200 PropertyRecord` | `400`, `404`, `409 DomainInUse`, `AppIdInUse` (both with `fieldErrors` naming the row: `domains[i].host`, `apps[i].appId`) |
| `POST /api/v1/platform/properties/{propertyId}/domains/{domainId}/verify` | | `200` the domain, after the same DNS TXT check as `POST /api/v1/admin/property/domains/{domainId}/verify`; audited `property.domain.verify` (property audit) and `platform.domain.verify` (platform audit) | `404` |
| `POST /api/v1/platform/properties/{propertyId}/enable` | | `200 PropertyRecord` | `409 PropertyNotReady` (`fieldErrors`: `document`, `domains` (at least one `Web` domain)) |
| `POST /api/v1/platform/properties/{propertyId}/disable` | `{ reason? }` | `200 PropertyRecord`; requests return `404 PropertyNotFound` within `deployment.propertyCacheSeconds`; sessions are kept | |
| `DELETE /api/v1/platform/properties/{propertyId}` | `{ "confirm": "<propertyId>" }` | `202 PropertyRecord` (`Deleting`, disabled at once, `deleteScheduledUtc` = now + `platform.propertyPurgeDelayHours`, 30 days); then every row and stored object of the property is purged and the record becomes `Deleted` | `400` without the confirmation, `409 PropertyNotReady` (the folder property) |
| `POST /api/v1/platform/properties/{propertyId}/restore` | | `200 PropertyRecord` (`Disabled`) | `409 PropertyNotRestorable` (not `Deleting`, or the purge started) |
| `GET /api/v1/platform/properties/{propertyId}/admins`, `POST .../admins/enumerate` | `EnumerationQuery` | `200 EnumerationResult<UserSummary>`: the property's members annotated as `SystemAdmin` | `404` |
| `POST /api/v1/platform/properties/{propertyId}/admins` | `{ userId }` (a member of that property) | `200 UserSummary`; audited in the platform and property audits | `404` (unknown property, or a member of another property) |
| `DELETE /api/v1/platform/properties/{propertyId}/admins/{userId}` | | `200 UserSummary` | `404` |
| `POST /api/v1/platform/properties/{propertyId}/admin-session` | | `200 { accessToken, accessTokenExpiresUtc, propertyId }`: a delegated session for `platform.delegatedSessionMinutes` (60), not refreshable, bound to that property (send `X-TK-Property`); the caller acts as `SystemAdmin` and "me" routes return `403`; audited as `platform.delegate` in both audits, and the platform administrator is the actor of every action | `403` (break-glass token), `404` |
| `GET /api/v1/platform/audit`, `POST /api/v1/platform/audit/enumerate` | `EnumerationQuery`; filters `propertyId` (default the platform realm), `actorUserId`, `action`, `targetType`, `targetId` | `200 EnumerationResult<AuditRecord>` | `404` (unknown property) |
| `GET /api/v1/platform/settings`, `PUT /api/v1/platform/settings`, `POST /api/v1/platform/settings/restart` | Same bodies as `/api/v1/settings` | Same | |
| `GET /api/v1/platform/properties/{propertyId}/settings`, `PUT ...` | Same bodies as `/api/v1/admin/property/settings` | Same; a platform write is audited `property.settings.update` in both audits | `404` |
| `PUT /api/v1/platform/properties/{propertyId}/settings` | Body { values, clear }. Same body as PUT /api/v1/admin/property/settings. Audited property.settings.update in the platform and property audits. Platform administrator token (Authorization: Bearer tkp_...) or the break-glass admin token; platform host only; MultiProperty only. | `200` Update a property's integration settings | `400`, `404` |
| `GET /api/v1/platform/properties/{propertyId}/branding`, `PUT ...`, `POST .../branding/revert`, `PUT .../branding/member-presets` | Same bodies as [`/api/v1/admin/property/branding`](#admin-branding-and-the-ai-switch-systemadmin) | Same; a platform write is audited `property.branding.update`, `property.branding.revert`, or `property.branding.memberPresets` in both audits | `400`, `404`, `409` |
| `GET /api/v1/platform/properties/{propertyId}/branding/versions` | Same as GET /api/v1/admin/property/branding/versions, for any property. Retained palette versions (at most property.maxPaletteVersions), newest first: BrandingVersion { version, source, note, light, dark, failingPairs, overrideCount, isCurrent, createdByUserId, createdUtc, labels, tags }. Filter source; orderings VersionDescending (default), VersionAscending, CreatedDescending, CreatedAscending. Versions carry no labels or tags (always empty lists), so label and tag filters are 400 ValidationFailed. Restore one with POST .../branding/revert { to: Version, version }. Platform administrator token (Authorization: Bearer tkp_...) or the break-glass admin token; platform host only. | `200` List a property's brand palette versions | `401`, `404`, `400` |
| `POST /api/v1/platform/properties/{propertyId}/branding/versions/enumerate` | Same as GET /api/v1/admin/property/branding/versions, for any property. Retained palette versions (at most property.maxPaletteVersions), newest first: BrandingVersion { version, source, note, light, dark, failingPairs, overrideCount, isCurrent, createdByUserId, createdUtc, labels, tags }. Filter source; orderings VersionDescending (default), VersionAscending, CreatedDescending, CreatedAscending. Versions carry no labels or tags (always empty lists), so label and tag filters are 400 ValidationFailed. Restore one with POST .../branding/revert { to: Version, version }. Platform administrator token (Authorization: Bearer tkp_...) or the break-glass admin token; platform host only. | `200` List a property's brand palette versions | `401`, `404`, `400` |
| `PUT /api/v1/platform/properties/{propertyId}/branding/member-presets` | Same as PUT /api/v1/admin/property/branding/member-presets, for any property. Body { mode: All \| None \| Selected, presetIds? }: which engine palette presets (BrandingView presets[].id) members may choose in Settings, Appearance; presetIds lists at least one catalog id with Selected and is ignored otherwise. The property's brand palette is always the members' default option. A member whose preset is no longer allowed sees the brand palette (the preference is kept). Applies to GET /api/v1/property brand.presets immediately (its ETag changes). Audited property.branding.memberPresets; a body that changes nothing stores nothing. Returns the BrandingView. Also audited in the platform audit. Platform administrator token (Authorization: Bearer tkp_...) or the break-glass admin token; platform host only. | `200` Choose the presets a property's members may pick | `400`, `404` |
| `PUT /api/v1/platform/properties/{propertyId}/branding` | Same as PUT /api/v1/admin/property/branding, for any property. Body { light? (map of color key to #RRGGBB), dark? (same), contrastOverrides?: [{ scheme, pair, reason? }], note? }; keys left out keep their value. Every value must be #RRGGBB or #RGB. Every pair must meet WCAG 2.1 AA (4.5:1 normal text, 3:1 components): a failing pair is 400 ValidationFailed with fieldErrors["contrast.<scheme>.<pair>"] naming the colors and the ratio, unless contrastOverrides lists it, in which case the version is saved and a warning is audited as property.branding.contrastOverride. Stores a new version (audited property.branding.update) and applies to GET /api/v1/property immediately (its ETag changes); a body that changes nothing stores nothing. In SingleProperty a later change of property.json brand.colors replaces the palette on the next start. Also audited in the platform audit. Platform administrator token (Authorization: Bearer tkp_...) or the break-glass admin token; platform host only. | `200` Change a property's brand palette | `400`, `404`, `409` |
| `GET /api/v1/platform/properties/{propertyId}/ai`, `PUT ...` | Same bodies as `/api/v1/admin/property/ai` | Same; a platform change is audited `property.ai.update` in both audits | `400`, `404` |
| `PUT /api/v1/platform/properties/{propertyId}/ai` | Same as PUT /api/v1/admin/property/ai, for any property. Body { enabled }. Applies to the next request without a restart (other instances within deployment.propertyCacheSeconds) and changes the GET /api/v1/property ETag. Turning AI off keeps endpoints, assistants, and conversations; AI routes answer 404 NotAvailable until it is on. Audited property.ai.update. Also audited in the platform audit. Platform administrator token (Authorization: Bearer tkp_...) or the break-glass admin token; platform host only. | `200` Switch AI on or off for a property | `400`, `404` |
| `GET /api/v1/platform/request-history`, `POST /api/v1/platform/request-history/enumerate` | `EnumerationQuery`; filters `propertyId` (a property id, `platform` (default), or `unresolved`), `method`, `statusCode`, `pathContains`, `userId`; search over path and principal name | `200 EnumerationResult<RequestHistoryListItem>` (one property per call) | `404` (unknown property) |
| `GET /api/v1/platform/request-history/{id}?propertyId=` | `propertyId`: a property id, `platform` (default), or `unresolved` | `200 RequestHistoryEntry` with headers and bodies redacted at capture exactly as in the property request history (credential headers, cookies, tokens, passwords, secret fields, URL signatures), bodies possibly truncated, plus labels and tags | `404` (unknown property or entry) |
| `GET /api/v1/platform/metrics/properties`, `POST /api/v1/platform/metrics/properties/enumerate` | `EnumerationQuery`; filter `status`; search over id, slug, name; orderings `CreatedDescending`, `CreatedAscending`, `NameAscending`, and the computed `MembersDescending`, `ErrorsDescending`, `StorageDescending` (sorted over at most 500 matching registry rows, paged with standard continuation tokens or `skip`; larger matching sets must be narrowed) | `200 EnumerationResult<{ propertyId, slug, name, status, members, activeMembers (seen in 30 days), requests24h, errors24h (status 500 or above), storageBytes, labels, tags }>` (at most `deployment.maxProperties` rows) | |
| `GET /api/v1/platform/properties/{propertyId}/usage/summary`, `GET /api/v1/platform/properties/{propertyId}/usage`, `POST .../usage/enumerate`, `GET .../usage/export` | As [Admin: usage metering](#admin-usage-metering-admin), for any registered property in its own time zone | Same bodies | `404` |
| `GET /api/v1/platform/properties/{propertyId}/usage/export` | Same file as GET /api/v1/admin/usage/export, for any property: the stable usage export a billing engine reads (bill a range only when complete is true). Platform administrator token (Authorization: Bearer tkp_...) or the break-glass admin token; platform host only; MultiProperty only. | `200` Export a property's usage | `400`, `401`, `404` |
| `GET /api/v1/platform/usage/properties`, `POST /api/v1/platform/usage/properties/enumerate` | `EnumerationQuery`; filters `period` (default `LastMonth`), `from`, `to`, `status`; search over id, slug, name; orderings `NameAscending`, `CreatedDescending`, `CreatedAscending`, and the computed `UsersDescending`, `ActiveUsersDescending`, `PostsDescending`, `CommentsDescending`, `InteractionsDescending`, `ImagesDescending`, `VideosDescending`, `StorageDescending`, `ApiRequestsDescending`, `PushDescending`, `EmailDescending`, `AiTokensDescending`, `AiCostDescending` (sorted over at most 500 matching registry rows, paged with standard continuation tokens or `skip`; larger matching sets must be narrowed) | `200 EnumerationResult<PropertyUsageRow { propertyId, slug, name, status, period, fromUtc, toUtc, usersTotal, usersNew, activeUsers, posts, comments, interactions, images, imageBytes, videos, videoBytes, storageBytes, apiRequests, pushSent, emailSent, aiTokens, aiCostMicros, dataThroughUtc, labels, tags }>` | `400 ValidationFailed` (`fieldErrors.filters.period`) |
| `POST /api/v1/platform/labels/enumerate`, `POST /api/v1/platform/tags/keys/enumerate`, `POST /api/v1/platform/tags/values/enumerate` | `MetadataDiscoveryQuery { targetType? (default Property; or PlatformAdmin), prefix?, key (tag values) }` | `200 EnumerationResult<{ label \| key \| value, count }>` for console autocomplete | `400` |

In `MultiProperty`, `/api/v1/settings*` returns `403 Forbidden` to property administrators (only the break-glass token passes); deployment settings are edited at `/api/v1/platform/settings`.

Platform audit actions: `platform.login`, `platform.admin.bootstrap`, `platform.admin.invite`, `platform.admin.accept`, `platform.admin.remove`, `platform.property.create`, `.update`, `.enable`, `.disable`, `.delete`, `.restore`, `.purge`, `platform.property.admin.grant`, `platform.property.admin.revoke`, `platform.delegate`, `platform.settings.update`, `platform.server.restart`.

---

## Live updates (code updates)

Self-hosted Expo Updates protocol v1 (docs/LIVE_UPDATES_CONTRACT.md, with its resolved decisions and client implementation notes). Update records are property-owned; every route resolves the property from `X-TK-Property` and `X-TK-App` (apps) or the host.

### `GET /api/v1/updates/manifest` (Anonymous)

Request headers: `expo-protocol-version` (1; absent is 0), `expo-platform` (`ios` or `android`), `expo-runtime-version`, `expo-channel-name` (absent is `production`), `expo-current-update-id`, `expo-embedded-update-id`, `expo-expect-signature` (structured dictionary; its `keyid` names the certificate the binary embeds), `expo-recent-failed-update-ids` (structured list), `X-TK-Install-Id` (the install id used for rollout buckets; empty means none; `expo-extra-params: installid="..."` is also read), `X-Client-Version`.

Selection: Published (and, for installs already on it, Paused) updates for exactly the platform, runtime version, and channel, newest `createdAt` first; updates the install recently failed to launch are skipped (and remembered per install); the first update whose rollout contains the install wins (`bucket = first 4 bytes of SHA-256("<updateGroupId>:<installId>") mod 10000 < rolloutPercentage * 100`; no install id is inside only 100 percent rollouts).

Protocol 1 responses are `200 multipart/mixed` with `expo-protocol-version: 1`, `expo-sfv-version: 0`, `expo-manifest-filters: channel="<channel>"`, and `cache-control: private, max-age=0`:

| Case | Parts |
|---|---|
| Update available | `manifest` (signed, `extra.critical`, `extra.criticalMessage`, `extra.expoClient`, `extra.trelliskit`, `metadata.channel`, `metadata.critical`), `extensions` (`{ "assetRequestHeaders": {} }`), and `certificate_chain` after a key rotation |
| App is current, nothing applies, or not in the rollout | `directive` `{ "type": "noUpdateAvailable" }` (signed) |
| Rollback to the built-in bundle | `directive` `{ "type": "rollBackToEmbedded", "parameters": { "commitTime" } }` (signed); `noUpdateAvailable` when the app already runs its embedded update |

Signatures: `expo-signature: sig="<base64 RSASSA-PKCS1-v1_5 SHA-256>", keyid="<signing key id>", alg="rsa-v1_5-sha256"` over the exact part bytes. The server signs with the property's Active key. When the binary embeds another certificate (its `keyid`), the response adds a `certificate_chain` part (`application/x-pem-file`) with the certificates from the signing key up to, not including, the embedded certificate (expo-updates certificate-chain mode); when no chain can be built it signs with the embedded key itself if available, else `503 SigningUnavailable`. A retired or unknown embedded key id is `503`.

Protocol 0 (no `expo-protocol-version`): `200 application/expo+json` with the manifest and an `expo-signature` response header, or `204` when nothing applies.

Errors are `text/plain`: `400 MissingProtocolHeader`, `400 UnsupportedPlatform`, `400 UnknownChannel`, `429 RateLimited` (`rateLimits.updateChecksPerInstallPerHour`, `rateLimits.updateChecksPerIpPerHour`), `503 SigningUnavailable`. Each request upserts the install's adoption row (install id stored only as an HMAC).

### `GET /api/v1/updates/assets/{propertyId}/{sha256Hex}` (Anonymous)

Local delivery of content-addressed assets: `Cache-Control: public, max-age=31536000, immutable`, `ETag: "<sha256>"`, `X-Content-Type-Options: nosniff`, `Range` (`206`, `416`), `304` on `If-None-Match`, `404` for unknown hashes or another property. With `updates.publicBaseUrl` set (AWS) manifests point at `{publicBaseUrl}/assets/{propertyId}/{sha256}` on the CDN instead. Source maps are never served.

### `POST /api/v1/updates/events` (Anonymous, `X-TK-Install-Id` required)

`{ "events": [{ "type", "updateId"?, "errorCode"?, "occurredUtc", "launchDurationMs"?, "message"?, "stackTrace"? }] }`, 1 to 20 events; types `CheckFailed`, `Downloaded`, `DownloadFailed`, `Applied`, `LaunchFailed`, `RolledBackToEmbedded`, `CriticalPromptShown`, `CriticalRestarted`; error codes `Network`, `SignatureInvalid`, `HashMismatch`, `Storage`, `Timeout`, `Server`, `Unknown`. Events older than 7 days or more than 5 minutes ahead are dropped. `202 { accepted, dropped }`. `LaunchFailed` also counts toward the automatic halt. On `LaunchFailed`, `DownloadFailed`, and `CheckFailed` the optional `message` (kept up to 500 characters) and `stackTrace` (kept up to 16384 characters) are stored privately for `updates.eventRetentionDays` and used only by the symbolication routes; other event types drop them.

### Upload: `POST /api/v1/admin/updates` (Admin or deploy key)

`multipart/form-data`, one request per platform, exactly as `mobile/scripts/publish-update.js` sends it: an `update` part (JSON descriptor), then one part per asset named by its `part` (the asset's MD5 `key`), then one `sourcemap-<md5>` part per source map. Descriptor:

```json
{
  "platform": "ios", "runtimeVersion": "<fingerprint>", "channel": "preview", "message": "Fix composer",
  "critical": false, "criticalMessage": null, "rolloutPercentage": 100, "gitCommit": "<sha>", "gitDirty": false,
  "createdUtc": "2026-12-01T17:00:00.000Z", "updateGroupId": "<uuid shared by the platforms of one run>", "supersede": false,
  "expoConfig": { },
  "launchAsset": { "part", "path", "key", "hash", "contentType", "fileExtension", "size" },
  "assets": [ { "part", "path", "key", "hash", "contentType", "fileExtension", "size" } ],
  "sourceMaps": [ { "part": "sourcemap-<md5>", "path", "key", "hash", "contentType", "size", "bundleKey" } ],
  "labels": [], "tags": {}
}
```

Every part is streamed to disk while its SHA-256 (base64url, the manifest `hash`) and MD5 (the `key`) are computed; a mismatch is `400 AssetMismatch` naming the file. Assets already stored may be omitted (check with `HEAD`); anything neither uploaded nor stored is `409 AssetsMissing`. Platforms sharing `updateGroupId` join one group (without it, uploads with the same channel, commit, and message within 10 minutes join). `createdAt` is the descriptor's `createdUtc` unless that is not later than the target's newest update (then previous plus 1 ms). `rolloutPercentage` 0 keeps a Draft; omitted is 100 (10 on production). Another update mid-rollout on the same target is `409 RolloutConflict` unless `supersede: true`. Without an active signing key the upload is kept as a Draft and the response is `503 SigningUnavailable`. Success `201 UpdateGroup`. Body limit `updates.maxUploadMegabytes` (`413 TooLarge`).

`HEAD /api/v1/admin/updates/assets/{sha256Hex}`: `200` stored, `404` missing.

Update and app configuration admin routes accept `SystemAdmin`, the admin token, or a member holding the global `ReleaseManager` role, except the signing key and deploy key routes (`/api/v1/admin/updates/signing-keys/**`, `/api/v1/admin/updates/deploy-keys/**`), which stay `SystemAdmin` only. Assign the role with `PUT /api/v1/admin/members/{id}/roles` (`{ "roles": [{ "role": "ReleaseManager" }] }`, resource type `Global`).

### Update groups (Admin or ReleaseManager)

| Method and path | Body | Notes |
|---|---|---|
| `POST /api/v1/admin/update-groups/enumerate` (and `GET`) | EnumerationQuery | Filters `platform`, `channel`, `runtimeVersion`, `status`, `kind`, `critical`, `gitCommit`; search over message and commit; labels and tags; orderings `CreatedDescending`, `CreatedAscending`, `UpdatedDescending` |
| `GET /api/v1/admin/update-groups/{id}` | | `UpdateGroup` with `updates` (at most 2, with `sourceMapCount`), `rolloutPlan`, `autoHalt` (with `failures`, `successes`, `failureRatePercent`), `adoption`, and `assets` (preview of 20 with `enumerate`) |
| `GET /api/v1/admin/update-groups/{id}/adoption?days=14` | | `EnumerationResult<UpdateAdoptionDay>` with scalar `days`/`byPlatform`; binary versions have a separate enumeration |
| `POST /api/v1/admin/updates/{updateId}/assets/enumerate` | EnumerationQuery | Ordering `PositionAscending` |
| `POST .../{id}/publish` | `{ rolloutPercentage?, supersede? }` | Drafts only; deploy keys may publish on their channels |
| `POST .../{id}/rollout` | `{ rolloutPercentage, platform? }` | Manual override at any time (`409 UpdateNotPublished` unless Published or Paused) |
| `PUT .../{id}/rollout-plan` | `{ stages?: [{ percentage, startsUtc? or hoursAfterPublish? }], useTemplate?, autoHaltEnabled?, autoHaltFailurePercent?, autoHaltMinInstalls? }` | At most 10 increasing stages in time order; the rollout worker applies each at its time; the automatic halt pauses the group when the share of installs that failed to launch it exceeds the threshold (after the minimum installs) |
| `POST .../{id}/pause`, `.../resume` | `{ reason? }` | Resume clears an automatic halt |
| `POST .../{id}/critical` | `{ critical, criticalMessage? }` | Up to 200 characters |
| `POST .../{id}/rollback` | `{ targetUpdateGroupId, rolloutPercentage?, message? }` | Republishes the target (same channel) with new ids and a newer `createdAt`; this group becomes `RolledBack`; `201` |
| `POST .../{id}/promote` | `{ channel, rolloutPercentage?, message?, supersede? }` | Republishes on another channel (never from or to `development`: `400 PromotionNotAllowed`); `promotedFrom` records the source; `201` |
| `POST /api/v1/admin/update-groups/rollback-to-embedded` | `{ platform: ios, android, or all, runtimeVersion, channel, rolloutPercentage?, message? }` | `201`; not for deploy keys |
| `DELETE /api/v1/admin/update-groups/{id}` | | Drafts only (`409 UpdateNotDraft`) |
| `GET /api/v1/admin/update-groups` | Search covers message and git commit. SystemAdmin, ReleaseManager, or the admin token. | `200` List update groups; errors `400` |
| `POST /api/v1/admin/update-groups/{id}/critical` | { critical, criticalMessage? (200 characters) }: applies to installs that have not downloaded it. SystemAdmin, ReleaseManager, or the admin token. | `200` Mark an update critical; errors `404`, `400` |
| `POST /api/v1/admin/update-groups/{id}/pause` | { reason? }: installs not running it stop receiving it. SystemAdmin, ReleaseManager, or the admin token. | `200` Pause a rollout; errors `404`, `400` |
| `POST /api/v1/admin/update-groups/{id}/promote` | { channel, rolloutPercentage?, message?, supersede? }: republishes the group on the channel (never from or to development); promotedFrom records the source. SystemAdmin, ReleaseManager, or the admin token. | `201` Promote to another channel; errors `404`, `400` |
| `POST /api/v1/admin/update-groups/{id}/publish` | { rolloutPercentage?, supersede? }. Deploy keys may publish groups on their channels. SystemAdmin, ReleaseManager, or the admin token. | `200` Publish a draft group; errors `404`, `400` |
| `POST /api/v1/admin/update-groups/{id}/resume` | { reason? }: also clears an automatic halt. SystemAdmin, ReleaseManager, or the admin token. | `200` Resume a rollout; errors `404`, `400` |
| `POST /api/v1/admin/update-groups/{id}/rollback` | { targetUpdateGroupId, rolloutPercentage?, message? }: republishes the target's content with new ids and createdAt; this group becomes RolledBack. SystemAdmin, ReleaseManager, or the admin token. | `201` Roll back to an earlier group; errors `404`, `400` |
| `POST /api/v1/admin/update-groups/{id}/rollout` | { rolloutPercentage, platform? }: a manual override at any time; stages of a rollout plan still apply at their time. SystemAdmin, ReleaseManager, or the admin token. | `200` Change the rollout percentage; errors `404`, `400` |
| `PUT /api/v1/admin/update-groups/{id}/rollout-plan` | Body { stages, useTemplate, autoHaltEnabled, autoHaltFailurePercent, autoHaltMinInstalls }. { stages?: [{ percentage, startsUtc? \| hoursAfterPublish? }] (at most 10, increasing), useTemplate?, autoHaltEnabled?, autoHaltFailurePercent?, autoHaltMinInstalls? }. The worker applies each stage at its time; the automatic halt pauses the group when the launch failure rate exceeds the threshold. SystemAdmin, ReleaseManager, or the admin token. | `200` Set the rollout plan; errors `400`, `404`, `409` |
| `POST /api/v1/admin/updates/{updateId}/failures/enumerate` | EnumerationQuery | Reported failures that carried a message or stack trace: `{ id (uev_), type, errorCode, message, hasStackTrace, occurredUtc, receivedUtc, labels, tags }`; filter `type`; newest first; the stack trace itself is never listed |
| `POST /api/v1/admin/updates/{updateId}/symbolicate` | `{ stackTrace }` (at most 65536 characters) or `{ eventId }` | Server-side symbolication with the update's private source map (the one whose `bundleKey` is the launch asset): `{ updateId, sourceMapAvailable, sourceMap { key, bundleKey, sizeBytes }, frames (at most 200: raw, function, file, line, column, original { source, line, column, name } or null), text, truncated }`. Parses Hermes (`at fn (address at file:line:col)`), V8, and JavaScriptCore frames; lines and columns are 1-based. `400 ValidationFailed`, `404 NotFound` (update or event), `413 TooLarge` (map above `updates.maxSourceMapMegabytes`), `422 SourceMapInvalid` (a map that cannot be read; index maps with embedded `sections` are decoded section by section, `url` sections are refused). Source maps are never served |

`UpdateGroup`: `{ id, kind (Publish, Republish, RollBackToEmbedded), channel, message, status (Draft, Published, Paused, RolledBack, Superseded), rolloutPercentage, critical, criticalMessage, gitCommit, gitDirty, republishOf, promotedFrom, updateGroupUuid, platforms, updates, createdBy { type, id, name }, publishedUtc, pausedUtc, rolledBackUtc, supersededUtc, rolloutPlan, autoHalt, adoption, assets, labels, tags, createdUtc, updatedUtc }`. A group becomes `Superseded` when a newer group reaches 100 percent on every target it covers. Audited as `updates.create`, `updates.upload`, `updates.publish`, `updates.rollout`, `updates.rolloutPlan`, `updates.pause`, `updates.resume`, `updates.critical`, `updates.rollback`, `updates.promote`, `updates.rollbackToEmbedded`, `updates.supersede`, `updates.autoHalt`, `updates.delete`.

### Channels, runtime versions, keys, and health (Admin; channels, runtime versions, and health also ReleaseManager)

| Method and path | Notes |
|---|---|
| `POST /api/v1/admin/updates/channels/enumerate` | `{ name, builtIn, latestByPlatform, activeInstalls30d, promotable }`; filter `builtIn` |
| `POST /api/v1/admin/updates/channels` | `{ name }` (`^[a-z][a-z0-9-]{1,31}$`, at most `updates.maxChannels` custom) |
| `DELETE /api/v1/admin/updates/channels/{name}` | Custom channels without live groups |
| `POST /api/v1/admin/updates/runtime-versions/enumerate` | Per platform and runtime version (the branches installs report): `firstSeenUtc`, `lastSeenUtc`, `activeInstalls30d`, `latestUpdate`, `binaryVersions` (at most 10); filters `platform`, `channel` |
| `POST /api/v1/admin/updates/deploy-keys/enumerate`, `POST .../deploy-keys`, `POST .../deploy-keys/{id}/revoke` | `{ name, channels (never production), expiresUtc?, labels?, tags? }`; the `tkdk_` secret is returned once; a deploy key reaches only the upload, asset `HEAD`, group publish, and health routes, for its channels |
| `POST /api/v1/admin/updates/deploy-keys/{id}/revoke` | Revoke a deploy key. SystemAdmin or the admin token. |
| `POST /api/v1/admin/updates/deploy-keys` | Create a deploy key. Body { name, channels, expiresUtc?, labels?, tags? }. { name, channels (never production), expiresUtc?, labels?, tags? }. The tkdk_ secret is returned once. A deploy key may only upload, check assets, and publish on its channels. SystemAdmin or the admin token. |
| `POST /api/v1/admin/updates/signing-keys/enumerate` | `{ id, keyId, algorithm, keyBits, source (Environment, Stored), privateKeyAvailable, certificateSha256, certificateNotAfterUtc, certificateSubject, issuedByKeyId, isCertificateAuthority, status (Active, Standby, Retired), lastUsedUtc, labels, tags }`; never private material |
| `POST /api/v1/admin/updates/signing-keys` | `{ keyId, certificatePem, privateKeyPem?, status? }`; a private key (member session only) is stored AES-256-GCM encrypted with `secrets.encryptionKey` (`503 IntegrationNotConfigured` when unset) and never returned; it must match the certificate |
| `POST /api/v1/admin/updates/signing-keys/{id}/activate` | Member session; switch signing (rotation) |
| `POST /api/v1/admin/updates/signing-keys/{id}/retire` | The key never signs again |
| `GET /api/v1/admin/updates/signing-keys/installs`, `POST /api/v1/admin/updates/signing-keys/installs/enumerate` | `EnumerationResult<UpdateCountRow>` of active installs grouped by embedded signing key; exact counts and SQL paging |
| `GET /api/v1/admin/updates/health` | `{ enabled, signing (Active or Missing), activeKeyId, signingProblem, storage, storageMode, rolloutPlanTemplate }`; newest channel releases are paged separately using `GET /api/v1/admin/updates/channels` / `POST /api/v1/admin/updates/channels/enumerate` |

`UpdateAssetGcWorker` (per property, under the `update_asset_gc` job lease, every `updates.assetGcIntervalHours`, default 168) deletes update assets that no update references and that were last referenced more than `updates.assetGcRetentionDays` (30) ago, in batches of `updates.assetGcBatchSize` (200, at most 50 batches per pass): each batch removes the rows in one statement that re-checks references, then the stored objects. Metrics `trelliskit.updates.gc.deleted`, `trelliskit.updates.gc.bytes` (label `kind`), and `trelliskit.updates.gc.runs` (`outcome`: completed, failed, skipped).

Key sources: the property folder's committed `mobile/updates/certificate.pem` (key id `main`, or `signing.json` `activeKeyId`) and `standby-certificate.pem` (`standby`) are registered on first use; in `SingleProperty` their private keys come from `TK_UPDATES_SIGNING_KEY` / `TK_UPDATES_SIGNING_KEY_FILE` (id `updates.signingKeyId`) and `TK_UPDATES_STANDBY_SIGNING_KEY` / `TK_UPDATES_STANDBY_SIGNING_KEY_FILE` (id `updates.standbySigningKeyId`); keys registered with a private key are stored encrypted per property (both modes). Private keys never come from the repository.

## App configuration (feature flags, kill switches, content manifest)

### `GET /api/v1/app/manifest?since=<contentVersion>` (Anonymous or signed in)

Headers `X-Client` (`web`, `mobile`), `X-Client-Version`, `X-Client-Update-Id`, `X-Client-Platform`, `X-Client-Channel`, `X-TK-Install-Id` or `X-TK-Visitor-Id`, optional bearer token. Returns `{ contentVersion, publishedUtc, fullRefresh, since, evaluatedFor, flags, killSwitches, strings, banners, assets, binary, checkIntervalMinutes, updatesCheckIntervalMinutes, property }`. `flags` holds every published, non-archived flag for the client type, resolved on the server for this caller; `killSwitches` maps every engine feature key to `{ killed, message }`; `binary` (mobile) is `{ minimumVersion, latestVersion, updateRequired, updateRecommended, storeUrl }` from `mobile.minimumBinaryVersion`, `mobile.latestBinaryVersion`, `mobile.storeUrls`, and `mobile.appStoreId`; `property` is the public property document when `since` is absent. `strings` is `{ upserts: { key: value }, deletes: [key] }` for the caller's client (`web.*` or `mobile.*`, plus `shared.*`); `banners` is `{ objects: [ClientBanner], deletes: [id] }`, the complete list of banners active for this caller (published, inside their schedule, for the client, targeting rules matched on the server; at most `appConfig.maxActiveBanners`, highest priority first) plus the ids changed since `since` that are not active; `assets` is `{ upserts: [ClientAsset], deletes: [id] }`. Without `since` strings and assets are full snapshots; with `since` they are the changes after it. `fullRefresh: true` (with `strings`, `banners`, and `assets` null) when `since` is older than the retained change log, ahead of the current version, or more than `appConfig.maxDeltaItems` items changed; the client then re-enumerates. `ClientBanner`: `{ id, title, body, style, placement, dismissible, priority, startsUtc, endsUtc, link { label, path, url } or null, imageAssetId, contentHash, contentVersion, labels, tags }` (`contentHash` covers title and body: a dismissal holds while it is unchanged). `ClientAsset`: `{ id, key, contentType, sizeBytes, width, height, sha256, url, altText, darkVariant { sha256, url, width, height, sizeBytes } or null, contentVersion, labels, tags }`. Targeting reads the install row of `X-TK-Install-Id` for `runtimeVersion` (and `channel` when `X-Client-Channel` is absent) and the signed-in member's filterable profile fields for `profileField`. `ETag` and `If-None-Match` (`304`); `Cache-Control: private, max-age=0`; rate limits `rateLimits.appManifestPerClientPerHour`, `rateLimits.appManifestPerIpPerHour`.

`GET /api/v1/property` now also returns `mobile.minimumBinaryVersion`, `mobile.latestBinaryVersion` (a version string or `{ ios, android }`), `mobile.storeUrls`, `mobile.appStoreId`, and `updates.checkIntervalMinutes` (property value, else the setting).

Re-enumeration (Anonymous, the manifest headers, rate limit `rateLimits.appContentEnumeratePerHour` per install or visitor id), each an EnumerationResult plus top-level `contentVersion`:

| Method and path | Notes |
|---|---|
| `POST /api/v1/app/strings/enumerate` | Published overrides `{ key, value, contentVersion }`; filter `client` (default the `X-Client` header) |
| `POST /api/v1/app/banners/enumerate` | Standard pages of the finite active banner set for this caller (at most `appConfig.maxActiveBanners` total), with exact totals, `PriorityDescending`/created ordering, search, dates, metadata filters, and caller/client/content-version scoped continuation |
| `POST /api/v1/app/assets/enumerate` | Published asset descriptors with content |
| `GET /api/v1/app/content-assets/{propertyId}/{sha256Hex}` | Local delivery of remote asset images (WebP, content addressed): `Cache-Control: public, max-age=31536000, immutable`, `ETag`, `304`, a single byte `Range` (`206` with `Content-Range`, `416 RangeNotSatisfiable` outside the asset; `Accept-Ranges: bytes`), `404` for hashes no remote asset references. With `updates.contentAssetBaseUrl` set (AWS) descriptors point at `{contentAssetBaseUrl}/{propertyId}/{sha256}` instead |

A `503 FeatureDisabled` body from any route adds `feature` (the engine feature key) next to `error` and `message`, so clients update the cached kill switch at once.

### Feature flags (Admin or ReleaseManager)

| Method and path | Notes |
|---|---|
| `POST /api/v1/admin/app/flags/enumerate` | The draft when pending, else the published revision; filters `kind`, `state`, `hasDraftChanges`, `client`; orderings `KeyAscending` (default), `UpdatedDescending`, `CreatedDescending`, `CreatedAscending`; search over key, name, description |
| `POST /api/v1/admin/app/flags` | `{ key, name, description?, kind (Boolean, Variant, Value), valueType?, variants?, defaultValue?, offValue?, enabled?, clients? (mobile, web, server; default mobile and web), rules?, labels?, tags? }` as a draft; `409 FlagKeyInUse`; `400 InvalidTargetingRule` |
| `GET /api/v1/admin/app/flags/{id}` | `{ flag, published, history { objects (max 10), totalRecords, enumerate } }` and an `ETag` header |
| `PUT /api/v1/admin/app/flags/{id}` | `If-Match` required (`412 PreconditionFailed`); absent fields keep their values, `rules` replaces the list; the key is immutable after the first publish |
| `POST /api/v1/admin/app/flags/{id}/archive`, `.../restore` | Draft changes |
| `DELETE /api/v1/admin/app/flags/{id}` | Never-published flags only |
| `POST /api/v1/admin/app/flags/{id}/evaluate` | `{ as: { platform, client, appVersion, updateId, runtimeVersion, channel, signedIn, userId, installId, country }, useDraft }` returns `{ value, matchedRuleId, reason (Disabled, Rule, RuleOutsideRollout, Default), bucket }` |
| `POST /api/v1/admin/app/flags/{id}/history/enumerate` | Published revisions, newest first |

Rules: `{ id?, description?, conditions: [{ attribute, operator, argument?, values }], value, rolloutPercentage? }`. Attributes and operators: `platform`, `client`, `updateId`, `runtimeVersion`, `channel`, `country` (`In`, `NotIn`); `appVersion` (`SemverRange`, `In`, `NotIn`); `signedIn` (`Is`); `membership` (`In`); `userRole` (`In`, `NotIn`); `userLabels` (`HasAny`, `HasAll`, `HasNone`, staff labels included, never sent to clients); `userTag` (`Equals` with `key=value`, `HasKey`); `userId` (`In`); `profileField` (`Equals`, `In`: the field key is `argument`, else `values[0]`; matches the signed-in member's option keys or text of a filterable profile field, case-insensitive; unknown or non-filterable fields are `400 InvalidTargetingRule`); `date` (`Before`, `After`). `userRole` values include `ReleaseManager`. The first rule whose conditions all match decides; partial rules use `bucket = SHA-256("<key>:<ruleId>:<subject>") mod 10000`.

### Remote strings (Admin or ReleaseManager)

The engine string catalog (`shared/src/strings/catalog.json`, regenerated with `npm run strings:catalog --workspace shared` from the website's and the mobile app's strings modules and embedded in the server) lists every key (`web.<path>`, `mobile.<path>`) with its default, `{tokens}`, and `maxLength`.

| Method and path | Notes |
|---|---|
| `POST /api/v1/admin/app/strings/enumerate` | Catalog keys joined with overrides: `{ key, client, defaultValue, tokens, maxLength, id (rst_) or null, value, publishedValue, notes, overridden, pendingDelete, state, hasDraftChanges, publishedInVersion, eTag, labels, tags, updatedUtc }`; filters `client`, `overridden`, `hasDraftChanges`; search over key, default, and override value; ordering `KeyAscending`; labels and tags attach to the key |
| `GET /api/v1/admin/app/strings/{key}` | `{ string, history { objects (max 10), totalRecords, enumerate } }` |
| `PUT /api/v1/admin/app/strings/{key}` | `{ value, notes?, labels?, tags? }` draft upsert: `400 UnknownStringKey`, `400 InvalidStringTokens` (tokens the key does not declare), `400 ValidationFailed` (1 to `maxLength`, word filter, `appConfig.maxStringOverrides`). Audited `appConfig.string.update` |
| `DELETE /api/v1/admin/app/strings/{key}` | Discards the draft, or drafts the removal of a published override (`pendingDelete`); `404` without an override. Audited `appConfig.string.delete` |
| `POST /api/v1/admin/app/strings/{key}/history/enumerate` | Published revisions, newest first |

### Banners (Admin or ReleaseManager)

| Method and path | Notes |
|---|---|
| `POST /api/v1/admin/app/banners/enumerate` | The draft when pending, else the published revision; filters `active`, `placement`, `style`, `state`, `hasDraftChanges`; search over title and body; orderings `PriorityDescending` (default), `UpdatedDescending`, `CreatedDescending`, `CreatedAscending` |
| `POST /api/v1/admin/app/banners` | `{ title (1 to 80), body? (0 to 500, Markdown), style? (Info, Success, Warning, Critical), placement? (Global, Home, Feed, Forums, Library, Events, Directory, Messages, SignIn, Account), dismissible?, priority? (-1000 to 1000), startsUtc?, endsUtc?, link? { label (1 to 40), path (app route starting with /) or url (https) }, imageAssetId?, clients? (mobile, web; default both), rules? (flag rule shape; value ignored, no rules means everyone), labels?, tags? }` as a draft; `Critical` is never dismissible; at most `appConfig.maxBanners`. `201 Banner` |
| `GET /api/v1/admin/app/banners/{id}` | `{ banner, published, history }` and an `ETag` header |
| `PUT /api/v1/admin/app/banners/{id}` | Partial; `If-Match` required (`412 PreconditionFailed`) |
| `POST /api/v1/admin/app/banners/{id}/archive`, `.../restore` | Draft changes (published banners only, `409` otherwise) |
| `DELETE /api/v1/admin/app/banners/{id}` | Never-published banners only (`204`; `409 Conflict` otherwise) |
| `POST /api/v1/admin/app/banners/{id}/history/enumerate` | Published revisions |

`Banner`: `{ id, title, body, style, placement, dismissible, priority, startsUtc, endsUtc, link, imageAssetId, clients, rules, contentHash, state, hasDraftChanges, publishedInVersion, eTag, labels, tags, createdUtc, updatedUtc }`. Audited `appConfig.banner.create`, `.update`, `.archive`, `.restore`, `.delete`.

### Remote assets (Admin or ReleaseManager)

| Method and path | Notes |
|---|---|
| `POST /api/v1/admin/app/assets/enumerate` | Filters `state`, `hasDraftChanges`; search over key and alt text; orderings `KeyAscending` (default), `UpdatedDescending`, `CreatedDescending`, `CreatedAscending` |
| `POST /api/v1/admin/app/assets` | `{ key, altText?, labels?, tags? }` draft (key `^[a-z][a-zA-Z0-9]*(\.[a-z][a-zA-Z0-9]*){0,3}$`, unique: `409 Conflict`); at most `appConfig.maxRemoteAssets` |
| `GET /api/v1/admin/app/assets/{id}` | `{ asset, published, history }` and an `ETag` header |
| `PUT /api/v1/admin/app/assets/{id}` | `{ key?, altText?, labels?, tags? }`, `If-Match`; the key is immutable after the first publish |
| `PUT /api/v1/admin/app/assets/{id}/content`, `PUT .../dark-content` | Raw body `image/png`, `image/jpeg`, or `image/webp`, at most `appConfig.maxAssetMegabytes` (`413 TooLarge`); decoded, re-encoded to WebP without metadata (long edge at most 4096), stored content addressed under `app-content/{propertyId}/{sha256}` |
| `POST /api/v1/admin/app/assets/{id}/archive`, `.../restore`, `DELETE /api/v1/admin/app/assets/{id}`, `POST /api/v1/admin/app/assets/{id}/history/enumerate` | As banners |

`RemoteAsset`: `{ id, key, contentType, sizeBytes, width, height, sha256, url, altText, darkVariant, state, hasDraftChanges, publishedInVersion, eTag, labels, tags, createdUtc, updatedUtc }` (`sha256`, `url`, `width`, `height` null before upload). Audited `appConfig.asset.create`, `.update`, `.content`, `.archive`, `.restore`, `.delete`.

### Content publishing (Admin or ReleaseManager)

| Method and path | Notes |
|---|---|
| `GET /api/v1/admin/app/drafts`, `POST /api/v1/admin/app/drafts/enumerate` | `EnumerationResult<AppConfigDraft> plus contentVersion and exact counts { flags, strings, banners, assets }` |
| `POST /api/v1/admin/app/drafts/enumerate`, `POST /api/v1/admin/app/drafts/discard` | `{ objectIds? }`: `ffl_`, `rst_` (or a string key), `ban_`, and `ras_` ids; every draft when omitted |
| `POST /api/v1/admin/app/publish` | `{ message, expectedVersion }`: every flag, string, banner, and asset draft becomes version `expectedVersion + 1` atomically (`changeCounts { flags, strings, banners, assets }`); an asset draft without content is `400 ValidationFailed` (`fieldErrors["assets.<id>"]`); `409 ContentVersionConflict`; `201 ContentVersion` |
| `POST /api/v1/admin/app/versions/enumerate`, `GET /api/v1/admin/app/versions/{version}`, `POST .../versions/{version}/changes/enumerate` | Version history and its flag changes |
| `POST /api/v1/admin/app/versions/{version}/restore` | `{ message? }`: publishes a new version whose flags, strings, banners, and assets equal their state at that version (kill switches untouched) |

### Kill switches (Admin or ReleaseManager)

| Method and path | Notes |
|---|---|
| `POST /api/v1/admin/app/kill-switches/enumerate` | One per engine feature key (`signups`, `purchases`, `signupCodes`, `feed`, `feedComposer`, `comments`, `reactions`, `mediaUpload`, `videoUpload`, `linkPreviews`, `forums`, `polls`, `directory`, `events`, `library`, `messaging`, `assistants`, `notificationsPush`, `search`): `{ featureKey, killed, killedForEveryone, message, blocksServer, rules, revision, lastChangedBy, lastChangedReason, lastChangedUtc }`; filter `killed` |
| `PUT /api/v1/admin/app/kill-switches/{featureKey}` | `{ killed, message?, rules?, blocksServer?, reason }`: live (no content publish), audited `appConfig.killSwitch` |

Enforcement: while killed for the caller, the feature's API routes answer `503 FeatureDisabled` with the message and `Retry-After: 60` (admin and moderation routes are never blocked; SystemAdmin callers are exempt unless a rule targets them by role or user id). With `mobile.enforceMinimumOnApi` on, mobile callers below the minimum binary version get `426 ClientUpdateRequired` outside the auth, me, membership, property, update, and app routes.

## Health

### `GET /api/v1/health`

Liveness. Anonymous. Returns `200` when the database is reachable, otherwise `503` with `status: "Unhealthy"`.

```bash
curl http://127.0.0.1:8000/api/v1/health
```

```json
{
  "status": "Healthy",
  "version": "1.0.0",
  "startedUtc": "2026-11-11T19:45:17.786Z",
  "uptimeSeconds": 50.5,
  "database": "Postgresql"
}
```

| Field | Type | Notes |
|---|---|---|
| `status` | `Healthy` \| `Degraded` \| `Unhealthy` | |
| `version` | string | Server version. |
| `startedUtc` | timestamp | Changes after every restart (the admin console uses this to detect a completed restart). |
| `uptimeSeconds` | number | |
| `database` | `Postgresql` | Active provider (PostgreSQL is the only provider). |

### `GET /api/v1/health/ready`

Readiness. Anonymous. Same body. Returns `200` only when the database is reachable and every schema migration has been applied; otherwise `503` (`Degraded` while migrations are pending, `Unhealthy` when the database is unreachable).

---

## Request history (admin)

Backs the admin console's Home and Request History views. When capture is disabled (`requestHistory.enabled = false`) the routes still respond, with empty results.

### `GET /api/v1/request-history` and `POST /api/v1/request-history/enumerate`

Request history collection. List items omit headers and bodies.

| | |
|---|---|
| Orderings | `CreatedDescending` (default), `CreatedAscending` |
| Filters | `method` (exact HTTP method, case-insensitive), `statusCode` (exact, a whole number), `pathContains` (case-sensitive substring; `%` and `_` are literal), `userId` (exact), `routeTemplate`, `statusClass` (`1xx`–`5xx`), `errorCode`, `clientApp`, `clientPlatform`, `clientVersion` (exact retained metric dimensions) |
| Time window | `createdAfterUtc`, `createdBeforeUtc` (formerly `fromUtc` and `toUtc` on this route) |
| Search | path, principal name |
| Labels and tags | `RequestHistory` |

Metric chart drilldowns use the request-history filters above, keeping bucket start inclusive and bucket end exclusive. The web request-history URL uses `fromUtc`/`toUtc`; its enumeration builder converts these to `createdAfterUtc`/`createdBeforeUtc`. Thread creation and moderation-action charts use their respective source lists and URL namespaces. Historical snapshot gauges, unsupported groupings, suppressed points, and the combined Other group do not create misleading source links. Retained metric rollups may outlive the individual request records.

```bash
curl -H "Authorization: Bearer $TK_ADMIN_TOKEN" \
  "http://127.0.0.1:8000/api/v1/request-history?method=PUT&maxResults=2"
```

```json
{
  "success": true,
  "maxResults": 2,
  "skip": 0,
  "continuationToken": "eyJ2IjoxLCJmIjoiYTFk...",
  "endOfResults": false,
  "totalRecords": 9,
  "recordsRemaining": 7,
  "timestampUtc": "2026-11-11T19:40:00Z",
  "objects": [
    {
      "id": "req_mwciqpxf_M2nsKggk6if50GznwMf",
      "userId": null,
      "principalName": "admin",
      "method": "PUT",
      "path": "/api/v1/settings",
      "url": "/api/v1/settings",
      "statusCode": 200,
      "durationMs": 1.64,
      "sourceIp": "127.0.0.1",
      "requestBodyBytes": 46,
      "responseBodyBytes": 7125,
      "createdUtc": "2026-11-11T19:35:18.721Z",
      "completedUtc": "2026-11-11T19:35:18.723Z",
      "labels": [],
      "tags": {}
    }
  ]
}
```

`requestBodyBytes` and `responseBodyBytes` are the original sizes before any truncation. Malformed filters return `400 ValidationFailed`, for example `{ "filters.statusCode": "Must be a whole number." }`.

### Summary and bulk delete filter parameters

`GET /api/v1/request-history/summary` and `DELETE /api/v1/request-history` still take these query parameters (all optional unless stated):

| Parameter | Type | Notes |
|---|---|---|
| `method` | string | Exact HTTP method, case-insensitive. |
| `statusCode` | integer | Exact status code. |
| `pathContains` | string | Case-sensitive substring of the path. `%` and `_` are literal. |
| `routeTemplate` | string | Exact normalized route template from the request rollup (`unknown` for an empty recorded route), up to 300 characters. |
| `statusClass` | string | One of `1xx`, `2xx`, `3xx`, `4xx`, `5xx`. |
| `errorCode` | string | Exact captured error code, or the numeric status as text when no error code was captured. Up to 100 characters. |
| `clientApp`, `clientPlatform`, `clientVersion` | string | Exact retained client dimensions, up to 100 characters each. |
| `userId` | string | Exact user identifier. |
| `fromUtc` | timestamp | Inclusive lower bound on request start time. |
| `toUtc` | timestamp | Exclusive upper bound. Must be later than `fromUtc`. |

Malformed values return `400 ValidationFailed` with `fieldErrors`, for example `{ "statusCode": "Must be a whole number." }`.

### `GET /api/v1/request-history/summary`

Time-bucketed counts for charts, paged with `EnumerationQuery`. Supply both `fromUtc` and `toUtc`, or omit both for the preceding 24 closed UTC hours. POST `/api/v1/request-history/summary/enumerate` puts these selections in `filters`. Buckets start exactly at `fromUtc`, are `bucketMinutes` wide (the last bucket ends at `toUtc`), and every bucket in the selected range is reachable through continuation pages, including empty ones. Failure means status 400 or above.

| Parameter | Default | Range |
|---|---|---|
| `bucketMinutes` | 15 | 1 to 1440; anything else is `400 ValidationFailed` (`fieldErrors.bucketMinutes`) |

A range that would produce more than 2000 buckets returns `400 ValidationFailed` (the bucket list is bounded at 2000).

```bash
curl -H "Authorization: Bearer $TK_ADMIN_TOKEN" \
  "http://127.0.0.1:8000/api/v1/request-history/summary?fromUtc=2026-11-11T18:00:00Z&toUtc=2026-11-11T19:00:00Z&bucketMinutes=15"
```

```json
{
  "totalCount": 10,
  "totalSuccess": 6,
  "totalFailure": 4,
  "averageDurationMs": 4.27,
  "success": true, "maxResults": 2, "skip": 0, "continuationToken": "opaque-next-page", "endOfResults": false, "totalRecords": 4, "recordsRemaining": 2, "timestampUtc": "2026-11-11T19:00:00Z",
  "objects": [
    { "bucketStartUtc": "2026-11-11T18:00:00Z", "bucketEndUtc": "2026-11-11T18:15:00Z", "successCount": 0, "failureCount": 0, "averageDurationMs": 0 },
    { "bucketStartUtc": "2026-11-11T18:15:00Z", "bucketEndUtc": "2026-11-11T18:30:00Z", "successCount": 6, "failureCount": 4, "averageDurationMs": 4.27 }
  ]
}
```

### `GET /api/v1/request-history/{id}`

Full entry: every list field plus `requestHeaders`, `requestBody`, `requestBodyTruncated`, `responseHeaders`, `responseBody`, `responseBodyTruncated`, `labels`, and `tags`. Header values for `Authorization`, `Proxy-Authorization`, `Cookie`, `Set-Cookie`, and any header whose name contains `api-key`, `apikey`, `token`, `password`, or `secret` are replaced with `***redacted***`. Bodies longer than `requestHistory.maxRequestBodyBytes` or `requestHistory.maxResponseBodyBytes` are truncated and flagged. Returns `404` if the entry does not exist.

### `DELETE /api/v1/request-history/{id}`

Deletes one entry (and its labels and tags). `204` on success, `404` if it does not exist.

### `DELETE /api/v1/request-history`

Deletes every entry matching the filter parameters (and their labels and tags). With no filter, deletes all entries.

```json
{ "deletedCount": 12 }
```

---

## Settings (admin)

Backs the admin console's Settings view. Reads reflect the settings file plus environment variable overrides. Secret values are never returned.

### `GET /api/v1/settings` and `POST /api/v1/settings/enumerate`


The example uses illustrative totals. Both retrieval forms accept the standard query; continue through `continuationToken` to inspect every setting. Mutations retain their existing `{ fields }` response.
```json
{
  "success": true, "maxResults": 2, "skip": 0, "continuationToken": "opaque-next-page", "endOfResults": false, "totalRecords": 200, "recordsRemaining": 198, "timestampUtc": "2026-11-23T00:00:00Z",
  "objects": [
    {
      "key": "requestHistory.retentionDays",
      "section": "Request history",
      "label": "Retention (days)",
      "description": "Captured requests older than this are deleted.",
      "type": "Integer",
      "value": 30,
      "options": null,
      "min": 1,
      "max": 3650,
      "applyMode": "Live",
      "overriddenByEnvironment": false,
      "environmentVariable": null,
      "isSet": null
    },
    {
      "key": "auth.adminToken",
      "section": "Security",
      "label": "Admin token",
      "description": "Break-glass bearer token for the admin console and automation. Changing it signs out every admin session.",
      "type": "Secret",
      "value": null,
      "options": null,
      "min": null,
      "max": null,
      "applyMode": "Live",
      "overriddenByEnvironment": false,
      "environmentVariable": "TK_ADMIN_TOKEN",
      "isSet": true
    }
  ]
}
```

| Field | Notes |
|---|---|
| `type` | `Boolean`, `Integer`, `String`, `Secret`, or `Enum`. |
| `value` | Configured value; always `null` for `Secret`. |
| `options` | Allowed values for `Enum`, otherwise `null`. |
| `min`, `max` | Bounds for `Integer`, otherwise `null`. |
| `applyMode` | `Live` (applied on save) or `Restart` (saved, applied after restart). |
| `overriddenByEnvironment` | `true` when `environmentVariable` is set in the server's environment; the field cannot be changed through the API. |
| `isSet` | For `Secret` only: whether a value is configured. |

Current keys:

| Key | Type | Apply | Environment variable |
|---|---|---|---|
| `rest.hostname` | String | Restart | `TK_REST_HOSTNAME` |
| `rest.port` | Integer 1 to 65535 | Restart | `TK_REST_PORT` |
| `cors.allowedOrigins` | String (comma-separated) | Live | `TK_CORS_ORIGINS` |
| `deployment.mode` | Enum `SingleProperty`, `MultiProperty` (default `SingleProperty`) | Restart | `TK_DEPLOYMENT_MODE` |
| `deployment.name` | String, 1 to 64 (default `trelliskit`) | Restart | `TK_DEPLOYMENT_NAME` |
| `deployment.trustForwardedHost` | Boolean (default true) | Restart | `TK_TRUST_FORWARDED_HOST` |
| `deployment.requireAppHeader` | Boolean (default true) | Live | |
| `deployment.propertyCacheSeconds` | Integer 1 to 600 (default 30) | Live | |
| `deployment.maxProperties` | Integer 1 to 25 (default 25) | Live | |
| `platform.hosts` | String (comma-separated host names) | Live | `TK_PLATFORM_HOSTS` |
| `platform.bootstrapAdminEmail` | String | Live | `TK_PLATFORM_BOOTSTRAP_ADMIN_EMAIL` |
| `platform.bootstrapAdminPassword` | Secret | Live | `TK_PLATFORM_BOOTSTRAP_ADMIN_PASSWORD` |
| `platform.sessionMinutes` | Integer 5 to 1440 (default 120) | Live | |
| `platform.delegatedSessionMinutes` | Integer 5 to 480 (default 60) | Live | |
| `platform.propertyPurgeDelayHours` | Integer 1 to 720 (default 720, 30 days) | Live | |
| `platform.inviteDays` | Integer 1 to 30 (default 7) | Live | |
| `database.type` | Enum `Postgresql` | Restart | `TK_DB_TYPE` |
| `database.hostname` | String | Restart | `TK_DB_HOSTNAME` |
| `database.port` | Integer 1 to 65535 | Restart | `TK_DB_PORT` |
| `database.databaseName` | String | Restart | `TK_DB_DATABASE` |
| `database.username` | String | Restart | `TK_DB_USERNAME` |
| `database.maxPoolSize` | Integer 1 to 1000 | Restart | `TK_DB_MAX_POOL_SIZE` |
| `database.password` | Secret | Restart | `TK_DB_PASSWORD` |
| `requestHistory.enabled` | Boolean | Live | |
| `requestHistory.retentionDays` | Integer 1 to 3650 | Live | |
| `requestHistory.maxRequestBodyBytes` | Integer 0 to 1048576 | Live | |
| `requestHistory.maxResponseBodyBytes` | Integer 0 to 1048576 | Live | |
| `logging.logRequests` | Boolean | Live | |
| `logging.fileLogging` | Boolean | Restart | |
| `logging.logDirectory` | String | Restart | |
| `telemetry.enabled` | Boolean | Restart | `TK_TELEMETRY_ENABLED` |
| `telemetry.otlpEndpoint` | String (absolute http(s) URI) | Restart | `TK_OTLP_ENDPOINT` |
| `telemetry.otlpProtocol` | Enum `Grpc`, `HttpProtobuf` | Restart | `TK_OTLP_PROTOCOL` |
| `telemetry.serviceName` | String | Restart | `TK_TELEMETRY_SERVICE_NAME` |
| `auth.adminToken` | Secret (32 characters minimum) | Live | `TK_ADMIN_TOKEN` |
| `auth.bootstrapAdminEmail` | String (default empty) | Live | `TK_BOOTSTRAP_ADMIN_EMAIL` |
| `auth.bootstrapAdminPassword` | Secret (default empty; with the email, creates that account at startup when the property has no members) | Live (read at startup) | `TK_BOOTSTRAP_ADMIN_PASSWORD` |
| `rest.trustedProxies` | String (comma-separated IP addresses and CIDR ranges; default `10.0.0.0/8,172.16.0.0/12,192.168.0.0/16,127.0.0.0/8,::1,fc00::/7`; empty ignores `X-Forwarded-For`) | Live | `TK_TRUSTED_PROXIES` |
| `rest.additionalTrustedProxies` | String (comma-separated addresses and CIDR ranges trusted in addition, for example CloudFront origin-facing ranges; default empty) | Live | `TK_ADDITIONAL_TRUSTED_PROXIES` |
| `rest.viewerAddressHeader` | String (default `CloudFront-Viewer-Address`; empty disables) | Live | `TK_VIEWER_ADDRESS_HEADER` |
| `deployment.domainVerificationResolverUrl` | String (DNS-over-HTTPS JSON endpoint; default `https://cloudflare-dns.com/dns-query`) | Live | `TK_DOMAIN_VERIFICATION_RESOLVER_URL` |
| `secrets.encryptionKey` | Secret (base64 of 32 bytes, the same on every instance). Required: startup fails when it is missing or invalid unless `secrets.allowGeneratedKey` | Restart | `TK_SECRETS_ENCRYPTION_KEY` |
| `secrets.previousEncryptionKeys` | Secret (comma-separated base64 keys that still decrypt after a rotation; an invalid entry stops startup) | Restart | `TK_SECRETS_PREVIOUS_ENCRYPTION_KEYS` |
| `secrets.allowGeneratedKey` | Boolean (default false). Local development only: generate the key when empty and save it to the settings file (the Docker stack sets it) | Restart | `TK_SECRETS_ALLOW_GENERATED_KEY` |
| `secrets.reencryptIntervalSeconds` | Integer 30 to 86400 (default 300): seconds between re-encryption passes per property | Live | |
| `secrets.reencryptBatchSize` | Integer 1 to 1000 (default 100): most values one pass rewrites per property | Live | |
| `email.provider` | Enum `Auto`, `Mailgun`, `Smtp`, `Log` (deployment mail and the `SingleProperty` default) | Live | `TK_EMAIL_PROVIDER` |
| `email.mailgunDomain`, `email.mailgunApiKey` (Secret), `email.mailgunBaseUrl` | Deployment-level Mailgun for platform administrator mail only (default base `https://api.mailgun.net/v3/`) | Live | `TK_MAILGUN_DOMAIN`, `TK_MAILGUN_API_KEY`, `TK_MAILGUN_BASE_URL` |
| `email.deliveryMaxAttempts` | Integer 1 to 20 (default 5) | Live | |
| `email.deliveryIntervalSeconds` | Integer 5 to 3600 (default 30) | Restart | |
| `email.integrationAlertMinutes` | Integer 1 to 1440 (default 60) | Live | |
| `email.messageRetentionDays` | Integer 1 to 3650 (default 90) | Live | |
| `profileFields.maxFields` | Integer 1 to 100 (default 50) | Live | |
| `profileFields.maxCardFields` | Integer 0 to 5 (default 3) | Live | |
| `profileFields.maxFiltersPerQuery` | Integer 1 to 20 (default 10) | Live | |
| `profileFields.legacyFieldsMode` | Enum `ReadWrite` (default), `ReadOnly`, `Off` | Live | |
| `auth.refreshReuseGraceSeconds` | Integer 0 to 120 (default 10): a rotated refresh token presented again within this window gets 401 without revoking the session | Live | |
| `property.directory` | String (absolute, or relative to the working directory or a parent; empty uses `samples/example`) | Restart | `TK_PROPERTY_DIR` |
| `property.maxPaletteVersions` | Integer 2 to 200 (default 20): brand palette versions kept per property | Live | |
| `web.publicBaseUrl` | String (http(s) URL, or empty for `https://` plus the property `domains.web`) | Live | `TK_WEB_PUBLIC_BASE_URL` |
| `email.fromAddress` | String (empty uses the property `contact.emailFromAddress`) | Live | `TK_SMTP_FROM_ADDRESS` |
| `email.fromName` | String (empty uses the property `contact.emailFromName`) | Live | `TK_SMTP_FROM_NAME` |
| `appStore.productId`, `googlePlay.productId` | String (empty uses the property `membership.storeProductId`) | Live | `TK_APPSTORE_PRODUCT_ID`, `TK_GOOGLEPLAY_PRODUCT_ID` |
| `enumeration.maxResultsMember` | Integer 1 to 1000 (default 100) | Live | |
| `enumeration.maxResultsAdmin` | Integer 1 to 1000 (default 500) | Live | |
| `enumeration.countMemberTotals` | Boolean (default true; admin lists always count) | Live | |
| `account.exportRetentionHours` | Integer 1 to 168 | Live | `TK_ACCOUNT_EXPORT_RETENTION_HOURS` |
| `account.exportsPerDay` | Integer 1 to 100 (default 3): account exports a member may request per rolling day | Live | |
| `account.exportDownloadLinkMinutes` | Integer 1 to 1440 (default 15): lifetime of a signed export download link | Live | |
| `notifications.fanoutIntervalSeconds` | Integer 1 to 3600 (default 15): seconds between notification fan-out passes (new posts also start a pass at once) | Live | |

Phase 2 adds the `media.*`, `linkPreviews.*`, `notifications.*`, `push.*`, `moderation.*`, `rateLimits.*`, `feed.*`, `comments.*`, `reactions.*`, `mentions.*`, and `content.*` groups (named with each route above; full list and ranges in `GET /api/v1/settings` and `docs/PHASE2_CONTRACT.md` section 14). The membership product name and price are no longer settings (`membership.productName` and `membership.priceCents` were removed); they come from the property `membership` section. The full key list is in `GET /api/v1/settings`.
### `PUT /api/v1/settings`

Send only changed keys. The update is all or nothing: if any value is invalid, nothing is saved. Live fields apply immediately; restart fields are saved and apply after a restart. Returns the same body as `GET`.

```bash
curl -X PUT -H "Authorization: Bearer $TK_ADMIN_TOKEN" -H "Content-Type: application/json" \
  -d '{"values":{"requestHistory.retentionDays":14}}' \
  http://127.0.0.1:8000/api/v1/settings
```

Validation failure (`400`):

```json
{
  "error": "ValidationFailed",
  "message": "One or more settings are invalid.",
  "fieldErrors": {
    "requestHistory.retentionDays": "Must be between 1 and 3650.",
    "rest.port": "Set by environment variable TK_REST_PORT; change it there.",
    "database.type": "Must be one of: Postgresql.",
    "nope": "Unknown setting."
  }
}
```

### `POST /api/v1/settings/restart`

Responds `202 { "message": "Restarting." }`, then exits the process with code 75 so the container runtime (`restart: unless-stopped` locally, ECS in production) relaunches it with the saved settings. Poll `GET /api/v1/health` until `startedUtc` changes.


### Background workers (feed and moderation)

These run inside the server process; each pass holds a database job lease, so only one instance runs it at a time. They have no routes; their effects show up in the routes above.

| Worker | Interval | What it does |
|---|---|---|
| `FeedPublishWorker` | `feed.publishIntervalSeconds` (default 30, 5 to 600, read live), and immediately when media becomes Ready or Failed | Publishes Scheduled posts whose `publishAtUtc` has passed and Processing posts whose attachments are all Ready: sets `publishedUtc` once, writes `feedPost.publish`, starts the `NewFeedPost` fan-out and feed post emails, sends pending mention notifications, and refreshes public media copies |
| `ModerationDueWorker` | Every minute | Open reports past `moderation.reviewTargetHours` send one `ModerationOverdue` notification (in-app and push) per target to every moderator in scope, once |
| `ContentPurgeWorker` | Every 60 minutes | Purges feed posts, forum threads, ideas, and comments removed or deleted more than `moderation.retainRemovedDays` ago with no Open report: deletes media and originals, clears bodies and revisions (ideas: title, body, and status note), deletes labels, tags, and mentions, and sets `purgedutc` (ideas gained the column in migration 85). Purged content can no longer be restored (`409 InvalidModerationState`) || `SecretRotationWorker` | `secrets.reencryptIntervalSeconds` (default 300) per property | See [Secrets key](#secrets-key) |

### Secrets key

The engine secrets key (`secrets.encryptionKey`, `TK_SECRETS_ENCRYPTION_KEY`) encrypts per-property integration secrets and queued email payloads (AES-256-GCM, bound to the property id).

- **Startup check:** the server refuses to start (exit code 1, the message names the variable) when the key is missing, unless `secrets.allowGeneratedKey` (`TK_SECRETS_ALLOW_GENERATED_KEY=true`, local development and the Docker stack only) lets it generate one and save it to the settings file. An invalid key or an invalid entry in `secrets.previousEncryptionKeys` always stops startup. Every instance of a deployment must run with the same key.
- **Rotation:** set the new key as `TK_SECRETS_ENCRYPTION_KEY`, move the old one to `TK_SECRETS_PREVIOUS_ENCRYPTION_KEYS` (comma-separated), and restart. Values keep decrypting with the previous key, and the `SecretRotationWorker` of each property rewrites them with the current key: at most `secrets.reencryptBatchSize` values per pass (keyset order, wrapping around), each rewrite conditional on the row still holding the ciphertext it read (idempotent, safe on several instances). A pass that rewrote values writes the audit record `secrets.reencrypt` (target `Property`, summary with counts); values that do not decrypt are left unchanged, logged, and counted in `trelliskit.secrets.decrypt_failures`. The gauge `trelliskit.secrets.reencrypt_remaining` (label `trelliskit.property`) reports the values still using a previous key; when it is 0 for every property, remove the previous key and restart.

---

## OpenAPI

### `GET /openapi.json` and `GET /swagger`

Anonymous. OpenAPI 3.0 document of every route above (used by the admin console's API Explorer), and a Swagger UI.

---

## Ideas board (Phase 5)

Phase 5 (docs/PHASE5_CONTRACT.md sections 2.4, 7, 9 to 16). Members vote on ideas; staff (`SystemAdmin`, `SupportAgent`) set statuses, merge duplicates, and publish ideas from Enhancement support tickets. Every list is an `EnumerationQuery` in and `EnumerationResult<T>` out with labels and tags filters. Feature `ideas` (`property.json` `features.ideas`, default true): off answers `404 NotAvailable` on every idea route; the `ideas` kill switch answers `503 FeatureDisabled` on `/ideas*` and `/public/ideas*`. In `PartiallyGated` with `ideas` in `access.gatedFeatures`, reads need a membership too (`402 MembershipRequired`, `feature: "ideas"`).

### `Idea`

```json
{
  "id": "ida_...",
  "title": "Dark mode for the website",
  "body": "Markdown subset.",
  "excerpt": "Markdown subset.",
  "author": { "...": "UserSummary, Deleted member, or null when the author is hidden" },
  "status": { "key": "Planned", "label": "Planned", "color": "#1565C0", "isClosed": false },
  "statusNote": "Scheduled for the next release.",
  "statusUtc": "2026-11-20T18:02:00Z",
  "voteCount": 57,
  "hasVoted": true,
  "isFollowed": true,
  "mergedInto": null,
  "commentTarget": { "type": "Idea", "id": "ida_..." },
  "commentCount": 12, "reactionCounts": {}, "reactionTotal": 0, "myReactions": [],
  "audience": "Members",
  "locked": false,
  "moderation": null,
  "sourceTicketId": null,
  "voterCounts": null,
  "editedUtc": null,
  "labels": [], "tags": {},
  "viewer": { "canVote": true, "canComment": true, "canReact": true, "canReport": true, "canEdit": false, "canSetStatus": false, "canMerge": false, "canOpen": true, "requires": null },
  "createdUtc": "...", "updatedUtc": "..."
}
```

| Field | Rule |
|---|---|
| `body` | Full text on single reads; `null` in lists (`IdeaSummary`); empty on a locked stub |
| `author` | `null` when the idea hides its author (`showAuthor` false, for example a ticket requester), except for staff; `Deleted member` after the author deleted their account |
| `voteCount` | Public. Voters are never listed. Staff single reads (`GET /api/v1/admin/ideas/{id}`) add `voterCounts { total, members, nonMembers }` |
| `mergedInto` | `{ id, title }` of the target when merged (title empty when the caller cannot read the target) |
| `audience` | `Public`, `SignedIn`, `Members`. New ideas default to `Members` when ideas is gated, else the property default; `Public` only by ideas staff |
| `locked` | `true` for a `Members` idea shown to a signed-in non-member in `PartiallyGated` (stub: body and excerpt empty, `viewer { canOpen: false, requires: "Membership" }`) |
| `moderation`, `sourceTicketId` | Staff (and the author for `moderation`) only |

### Member routes

| Method and path | Body | Result | Errors |
|---|---|---|---|
| `GET /api/v1/ideas`, `POST /api/v1/ideas/enumerate` | `EnumerationQuery`: orderings `Top` (votes, default), `Trending` (votes in the last `ideas.trendingDays`), `CreatedDescending`, `CreatedAscending`, `UpdatedDescending`, `Relevance` (with `search`: title matches first, then votes); filters `status` (keys), `open` (true: not a closed status; false: closed only), `votedByMe`, `authorUserId`, `audience`, `includeMerged` (default false); search over title and body | `EnumerationResult<IdeaSummary>` | `400` |
| `GET /api/v1/ideas/statuses`, `POST /api/v1/ideas/statuses/enumerate` | `EnumerationQuery` | `EnumerationResult<IdeaStatus>` (active statuses in display order, at most 50) | |
| `GET /api/v1/ideas/{id}` | | `Idea` (a merged idea returns its own payload with `mergedInto`) | `401` (signed out, non-Public), `402 MembershipRequired` (locked stub), `404` |
| `POST /api/v1/ideas` | `{ title (3 to 140), body (Markdown subset, at most 10000), audience?, labels?, tags? }` | `201 Idea` with the author's vote and follow | `400 ValidationFailed`, `ContentFiltered`; `403 Forbidden` (`ideas.memberSubmissions` is `StaffOnly`), `PostingNotAllowed`, `Muted`; `429` (`ideas.ideasPerDay`) |
| `PUT /api/v1/ideas/{id}` | `{ title?, body?, labels?, tags? }` (omitted fields stay) | `Idea` (the author while the status is the initial status; staff any time) | `400`, `403`, `404`, `409 IdeaNotEditable` |
| `PUT /api/v1/ideas/{id}/vote`, `DELETE ...` | | `Idea` (idempotent; voting follows the idea) | `401`, `402`, `403 Muted`, `404`, `409 IdeaMerged` (body adds `mergedInto { id, title }`), `409 IdeaClosed` (a closed status unless `ideas.voteOnClosed`), `429` (`ideas.votesPerMinute`) |
| `PUT /api/v1/ideas/{id}/follow`, `DELETE ...` | | `{ "isFollowed" }` (idempotent) | `402`, `404` |
| `GET /api/v1/ideas/{id}/comments`, `POST .../comments/enumerate` | Phase 2 comment tree query (`depth`, `childLimit`; orderings `Top`, `CreatedDescending`, `CreatedAscending`) | `CommentTreePage` (comments of a merged idea stay readable) | `404` |
| `POST /api/v1/ideas/{id}/comments` | Phase 2 `CommentRequest` | `201 Comment`; notifies the idea's author (`PostComment`), the replied-to author (`CommentReply`), and mentioned members | Phase 2 errors, `402`, `409 IdeaMerged` |
| `PUT /api/v1/ideas/{id}/reactions/{key}`, `DELETE ...`; `GET /api/v1/ideas/{id}/reactions`, `POST .../reactions/enumerate` | Phase 2 | Phase 2 (`Reaction` notifications point to `/ideas/{id}`) | `402`, `404` |
| `DELETE /api/v1/ideas/{id}/reactions/{key}` | Removes the caller's reaction key. Idempotent. | `200` Remove a reaction from an idea | `400`, `404`, `429` |

Idea comments use the generic comment routes as well (`/api/v1/comments/{id}`, children, edits, reactions). Reports: `POST /api/v1/reports` with `targetType: "Idea"` (or `Comment` for an idea comment).

### Public board (anonymous)

`GET /api/v1/public/ideas`, `POST /api/v1/public/ideas/enumerate` (orderings as the board; filters `status`, `open`; at most 50 per page), and `GET /api/v1/public/ideas/{id}`: `Public`, unmoderated, unmerged ideas when `ideas.publicBoardEnabled` (else `404 NotAvailable`). `Cache-Control: public, max-age=60`; rate limited per IP (`rateLimits.publicFeedPerMinute`, `429` with `Retry-After`); no votes by viewer, no comments, `viewer` allows nothing.

### Staff routes (`SystemAdmin`, `SupportAgent`)

| Method and path | Body | Result | Errors |
|---|---|---|---|
| `GET /api/v1/admin/ideas`, `POST /api/v1/admin/ideas/enumerate` | `EnumerationQuery` (default `CreatedDescending`; filters as the board plus `hidden`, `merged`) | `EnumerationResult<Idea>` | `400`, `403` |
| `POST /api/v1/admin/ideas` | `{ title, body, audience?, status? (key, default the initial status), showAuthor? (default true), labels?, tags? }` | `201 Idea` (no automatic vote) | `400`, `403` |
| `GET /api/v1/admin/ideas/{id}` | | `Idea` with `voterCounts` | `403`, `404` |
| `PUT /api/v1/admin/ideas/{id}` | `{ title?, body?, audience?, showAuthor?, labels?, tags? }` | `Idea` | `400`, `403`, `404` |
| `PUT /api/v1/admin/ideas/{id}/status` | `{ status, statusNote (0 to 1000), notifyVoters (default true) }` | `Idea` | `400`, `403`, `404` |
| `POST /api/v1/admin/ideas/{id}/merge` | `{ intoIdeaId }` | `Idea` (the merged idea, with `mergedInto`) | `400`, `403`, `404`, `409 AlreadyMerged`, `409 IdeaMerged` (target merged) |
| `POST /api/v1/admin/ideas/{id}/unmerge` | | `Idea` | `403`, `404`, `409 NotMerged` |
| `DELETE /api/v1/admin/ideas/{id}` | | `204` (soft delete) | `403`, `404` |
| `POST /api/v1/admin/tickets/{id}/publish-idea` | `{ title?, body?, showRequester (default false), audience? }` | `201 Idea` (title and body default to the ticket's; the ticket's idea is set; the requester votes and follows) | `400 NotAnEnhancement` (only `Enhancement` tickets), `403`, `404`, `409 TicketHasIdea` |
| `POST /api/v1/admin/tickets/{id}/link-idea` | `{ ideaId }` | `{ ticketId, ideaId }` (a merged idea links to its target; the requester votes) | `400`, `403`, `404` |

Merging copies the merged idea's votes to the target (one per member) and makes its followers follow the target; the merged idea keeps its own votes and comments (read-only), leaves the board, and answers `409 IdeaMerged` to votes and new comments. Unmerge lists it again; votes copied to the target stay there. Audit: `idea.create`, `idea.update`, `idea.status`, `idea.merge` (merge and unmerge), `idea.delete`, and `idea.status.create`, `.update`, `.retire`, `.order`.

### Idea statuses (`IdeaStatus`)

`{ id: "ids_...", key, label, color, isClosed, isInitial, sortOrder, isRetired, labels, tags, createdUtc, updatedUtc }`. Seeded once from `property.json` `ideas.statuses` (`[{ key, label, color?, isClosed?, isInitial? }]`, exactly one initial, validated at startup) or the engine defaults `UnderReview` (initial), `Planned`, `InProgress`, `Shipped` (closed), `Declined` (closed). The status key `Shipped` sends the `idea_shipped` email.

| Method and path | Body | Result | Errors |
|---|---|---|---|
| `GET /api/v1/admin/ideas/statuses`, `POST .../statuses/enumerate` | `EnumerationQuery` (filter `includeRetired`) | `EnumerationResult<IdeaStatus>` (staff) | `403` |
| `POST /api/v1/admin/ideas/statuses` | `{ key, label, color?, isClosed?, isInitial? }` (an initial status clears the flag on the others; at most 50) | `201 IdeaStatus` (SystemAdmin) | `400`, `403`, `409 StatusKeyInUse` |
| `GET /api/v1/admin/ideas/statuses/{id}` | | `IdeaStatus` (staff) | `403`, `404` |
| `PUT /api/v1/admin/ideas/statuses/{id}` | `{ key?, label?, color?, isClosed?, isInitial? }` (renaming the key moves every idea) | `IdeaStatus` (SystemAdmin) | `400`, `403`, `404`, `409 StatusKeyInUse` |
| `PUT /api/v1/admin/ideas/statuses/order` | `{ ideaStatusIds }` (every active status once) | `EnumerationResult<IdeaStatus>` (SystemAdmin) | `400`, `403` |
| `DELETE /api/v1/admin/ideas/statuses/{id}` | | `204`: retired (ideas keep it; it is no longer offered) (SystemAdmin) | `403`, `404`, `409 StatusInUse` (the initial status) |

### Notifications and email

- `IdeaStatusChanged` (category `Ideas`; defaults in-app on, push on, email off; all three editable): a status change notifies every voter and follower who may read the idea, through the notification fan-out worker in keyset batches of `notifications.fanoutBatchSize`. One unread notification per idea and member: a later change refreshes it (new title, body, time, push queued again). Title "Status update on {idea title}", body "Now {status label}: {note}", path `/ideas/{id}`. `notifyVoters: false` sends nothing. Never to the actor.
- `idea_shipped` (Optional, category `Ideas`): when the new status key is `Shipped`, voters with Ideas email on and a verified address get the email (tokens `ideaTitle`, `statusNote`, `link`, `unsubscribeLink`; one-click `List-Unsubscribe`).
- Idea comments send `PostComment` (to the idea's author, "commented on your idea"), `CommentReply`, and `Mention`; idea reactions send `Reaction`.

### Moderation

Reports on ideas and idea comments file in scope `Ideas` (scope id `Idea` or `Comment`). `SystemAdmin` and `SupportAgent` handle both (SupportAgent reaches `/api/v1/admin/moderation*` for this scope only); forum moderators with resource id `*` handle idea comments when `ideas.forumModeratorsModerate` (default true); feed moderators do not. Content by a SystemAdmin is protected. Auto-hide follows `moderation.autoHideReportThreshold`. Hidden or removed ideas disappear for members (the author and staff still see them).

### Account lifecycle and search

Account deletion deletes the member's votes (counts recomputed) and follows; ideas they authored stay with "Deleted member" unless still in the initial status, which are deleted. The account export adds `ideaVotes` (`{ id, ideaId, ideaTitle, createdUtc }`) and `ideas` (`{ id, title, body, status, audience, voteCount, createdUtc, editedUtc }`). Ideas and idea comments join global search (`filters.types` accepts `Idea`) and obey the idea's audience; merged, hidden, and deleted ideas are not indexed.

### Property document

`GET /api/v1/property` adds `ideas: { memberSubmissions: "Direct" | "StaffOnly", publicBoardEnabled, voteOnClosed }` from the live settings (part of the `ETag`), so clients show or hide the submit form and the public board.

### Settings and worker

| Key | Value | Apply |
|---|---|---|
| `ideas.memberSubmissions` | `Direct` or `StaffOnly` (default) | Live |
| `ideas.voteOnClosed` | Boolean (default false) | Live |
| `ideas.trendingDays` | Integer 1 to 90 (default 14) | Live |
| `ideas.publicBoardEnabled` | Boolean (default false) | Live |
| `ideas.forumModeratorsModerate` | Boolean (default true) | Live |
| `ideas.ideasPerDay`, `ideas.votesPerMinute` | Integer (defaults 5, 60) | Live |
| `ideas.trendingIntervalMinutes` | Integer 5 to 1440 (default 60) | Live |

`IdeaTrendingWorker` (leased pass `idea_trending`, every `ideas.trendingIntervalMinutes`) recomputes each idea's recent vote count from the votes of the last `ideas.trendingDays` (the `Trending` ordering). Metric `trelliskit.ideas.actions` (label `action`: create, update, vote, unvote, follow, unfollow, status, merge, unmerge, delete).

---

## Support tickets and private media (Phase 5)

Design record: `docs/PHASE5_CONTRACT.md` sections 1, 2.3, 6, 9, 10, 12 to 16 and as-built note C. Support is an account feature: every route under `/api/v1/tickets` and `/api/v1/support` is in the SignedIn group, so any signed-in account reaches it in every access model (a lapsed or never-paid member in `FullyGated` can always ask for billing help). There is no anonymous ticket API. `features.support` off: `404 NotAvailable` on every member and staff support route (clients show the property's support email). The `support` kill switch: `503 FeatureDisabled` on `/tickets*` and `/support/*` (staff routes stay open). Tickets are never assistant knowledge and never in global search.

### Shapes

`Ticket`: `{ id (tkt_), number, type { key, label }, title, body, status (Open, InProgress, WaitingOnMember, Resolved, Closed), priority (Low, Normal, High, Urgent), requester (UserSummary; Deleted member after account deletion), assignee, device { client, clientVersion, platform, osVersion, deviceModel, updateId, locale, timeZone } or null, membership (staff only: snapshot at creation { isActive, source, store, expiresUtc }), currentMembership (staff ticket detail only), idea { id, title } or null, messages { objects (newest 20, oldest first), totalRecords, enumerate } (detail only), firstResponseDueUtc, firstResponseUtc, isOverdue, lastActivityUtc, resolvedUtc, closedUtc, labels, tags, viewer { canReply, canClose, canReopen, canAttach, canNote, canAssign, canSetStatus (staff fields null for members) }, createdUtc, updatedUtc }`. Lists (`TicketSummary`, `AdminTicketSummary`) carry the same object without `messages`.

`TicketMessage`: `{ id (tkm_), ticketId, kind (Reply, Note, System), author, authorRole (Member, Staff, System), authorName, body, statusChange, attachments (Media with per-object URLs), labels, tags, createdUtc }`. Members see `Reply` messages only; staff also see internal notes (`Note`) and the staff timeline (`System` lines written for every status, priority, type, and assignment change; `statusChange` names the new status). Staff replies show members `authorName` = "{agent}, {name} Support" when `support.showAgentNames` (default true), else the team name with `author` null.

`TicketType`: `{ id (tty_), key, label, description, helpText, staffRecipients (SystemAdmin only), sortOrder, isRetired, labels, tags, createdUtc, updatedUtc }`.

### Member routes

| Method and path | Notes |
|---|---|
| `POST /api/v1/tickets` | `{ type, title, body, mediaIds?, includeDiagnostics?, labels?, tags? }`. Title 3 to 140 characters, body 10 to `support.maxBodyLength` (Markdown subset), at most `support.maxAttachments` uploads with purpose `Ticket` (one video only when `support.allowVideo`). With `includeDiagnostics` the `device` block is read from `X-Client`, `X-Client-Version`, `X-Client-Update-Id`, `X-Client-Platform`, `X-Client-Os-Version`, `X-Client-Device-Model`, `X-Client-Locale` (else the first `Accept-Language` entry), and `X-Client-Time-Zone` (each trimmed to 200 characters). The membership snapshot is stored for staff. Word filter `Block` terms reject (`400 ContentFiltered`); `Flag` terms never report tickets. `201 Ticket`; `400 ValidationFailed`; `409 TicketLimitReached` (more than `support.maxOpenTicketsPerMember` not Closed); `409 MediaNotAttachable`; `429` (`support.ticketsPerDay`) |
| `POST /api/v1/tickets/enumerate`, `GET /api/v1/tickets` | Own tickets. Orderings `LastActivityDescending` (default), `CreatedDescending`; filters `status` (a name, a comma list, or an array) and `type`; search covers the title |
| `GET /api/v1/tickets/{id}` | Own ticket with the newest 20 replies; any other ticket is `404` |
| `POST /api/v1/tickets/{id}/messages/enumerate`, `GET .../messages` | Replies only, `CreatedAscending` default |
| `POST /api/v1/tickets/{id}/messages` | `{ body, mediaIds? }` (1 to `support.maxBodyLength`). `WaitingOnMember` and `Resolved` move to `Open`; a `Closed` ticket reopens within `support.reopenDays` (14) and is `409 TicketClosed` after. Notifies the assignee (`TicketMemberReplied`). `429` (`support.repliesPerHour`) |
| `GET /api/v1/tickets/{id}/messages` | List my ticket's replies. Replies only (CreatedAscending default); attachments carry per-object URLs valid messaging.mediaUrlMinutes. Any signed-in account (every access model). |
| `POST /api/v1/tickets/{id}/close` | Idempotent; `200 Ticket` |
| `POST /api/v1/tickets/{id}/reopen` | `Resolved`, or `Closed` within `support.reopenDays`, moves to `Open`; `Closed` after the window is `409 TicketClosed`; other statuses unchanged |
| `POST /api/v1/support/ticket-types/enumerate`, `GET /api/v1/support/ticket-types` | Enabled types in order with description and help text |
| `POST /api/v1/tickets/uploads` | The upload session of `POST /api/v1/media/uploads` with purpose `Ticket` forced, in the SignedIn group (so lapsed and never-paid accounts can attach screenshots). Body `{ kind, contentType, sizeBytes, fileName?, durationSeconds?, scopeId?, labels?, tags? }`: `scopeId` is an own ticket (`404` otherwise) or omitted for a new ticket. Posting permissions (mute, `canPostMedia`) do not apply to ticket attachments. Video only when `support.allowVideo` (`403 VideoUploadNotAllowed`) |
| `POST /api/v1/tickets/uploads/{id}/upload-urls`, `POST /api/v1/tickets/uploads/{id}/complete` | As the media routes, for an own `Ticket` upload only (`404` for other media) |

### Staff routes (SystemAdmin and SupportAgent)

| Method and path | Notes |
|---|---|
| `POST /api/v1/admin/tickets/enumerate`, `GET /api/v1/admin/tickets` | Orderings `LastActivityDescending` (default), `CreatedAscending`, `CreatedDescending`, `DueAscending`, `PriorityDescending`. Filters `status` (default every status except `Closed`), `type`, `priority`, `assigneeUserId`, `unassigned`, `requesterUserId`, `overdue`, `hasIdea`, plus labels and tags. `search` covers the number, title, body, and the requester's display name and email (PostgreSQL text matching on this property's own rows only). Exact totals |
| `GET /api/v1/admin/tickets/{id}` | Every message kind in the newest 20, `membership` and `currentMembership` |
| `POST /api/v1/admin/tickets/{id}/messages/enumerate`, `GET .../messages` | Replies, notes, and system lines; filter `kind`; orderings `CreatedAscending` (default) and `CreatedDescending`; pages with `continuationToken` through the full history (the ticket detail carries only the newest 20) |
| `POST /api/v1/admin/tickets/{id}/messages` | `{ kind: Reply (default) or Note, body, mediaIds?, setStatus? }`. A `Reply` is visible to the requester, sets `WaitingOnMember` unless `setStatus` says otherwise, records the first response, notifies (`TicketUpdate`), emails `ticket_reply`, and publishes `ticket.updated`. A `Note` changes nothing and is never returned to members or emailed |
| `GET /api/v1/admin/tickets/{id}/messages` | List a ticket's messages (staff). Replies, internal notes, and system timeline lines (CreatedAscending default); filter kind. SystemAdmin or SupportAgent. |
| `PUT /api/v1/admin/tickets/{id}` | `{ status?, priority?, type?, assigneeUserId?, labels?, tags? }`; omitted fields stay; `assigneeUserId: null` unassigns; the assignee must hold `SupportAgent` or `SystemAdmin` (`400`). Each change writes a `System` line and the audit record `ticket.update`. Members hear about status changes only (`TicketUpdate`, `ticket_resolved` for `Resolved` and `Closed`, `ticket.updated`) |
| `DELETE /api/v1/admin/tickets/{id}` | `Closed` tickets only (`409 TicketNotClosed`). Deletes the ticket, its messages, their attachments (every stored object), and the labels and tags. Audited `ticket.delete`. `204` |
| `GET /api/v1/admin/tickets/export.csv` | One bounded CSV page (at most `enumeration.maxResultsAdmin` rows) with the inbox filters as query parameters; `X-Continuation-Token` and `X-Total-Records` headers. Metadata only: `id, number, type, status, priority, title, requesterUserId, assigneeUserId, ideaId, isOverdue, firstResponseDueUtc, firstResponseUtc, lastActivityUtc, resolvedUtc, closedUtc, createdUtc, labels, tags` (never bodies or messages) |
| `GET /api/v1/admin/metrics/support?days=30` | `days` 1 to 365. `{ days, openedByDay [{ date, count }], resolvedByDay, firstResponseMedianHours, overdueNow, openNow, byType { objects [{ key, label, count }] (max 20), totalRecords, enumerate } }` |
| `POST /api/v1/admin/support/ticket-types/enumerate`, `GET .../ticket-types` | SystemAdmin. Every type with recipients; `filters.includeRetired` (default true) |
| `POST /api/v1/admin/support/ticket-types` | SystemAdmin. `{ key, label, description?, helpText?, staffRecipients?, isRetired?, labels?, tags? }`: key 1 to 64 letters, digits, underscores, or hyphens, unique case-insensitively (`409 KeyInUse`), label 1 to 80, at most 10 recipients, at most 50 types (`409 TicketTypeLimitReached`). Audited `ticket_type.create` |
| `GET`, `PUT /api/v1/admin/support/ticket-types/{id}` | SystemAdmin. PUT keeps omitted fields; `isRetired: false` restores. Audited `ticket_type.update` |
| `DELETE /api/v1/admin/support/ticket-types/{id}` | SystemAdmin. Retires the type (hidden from the form; existing tickets keep it); idempotent; `204`. Audited `ticket_type.retire` |
| `PUT /api/v1/admin/support/ticket-types/order` | SystemAdmin. `{ ticketTypeIds }`: the listed types first, the others after them; `{ updated, objects }`. Audited `ticket_type.order` |
| `GET /api/v1/admin/support/ticket-types` | List ticket types (admin). Every ticket type in order, with staff recipients. filters.includeRetired (default true). SystemAdmin. |

`POST /api/v1/admin/tickets/{id}/publish-idea` and `.../link-idea` belong to the ideas board section. A `SupportAgent` reaches only `/admin/tickets*`, `/admin/support*`, `/admin/ideas*`, and `/admin/metrics/support`; ticket type administration is SystemAdmin only (`403`). `SupportAgent` counts as staff (`isStaff`).

### Ticket types and seeding

Ticket types are seeded once per property, the first time a support route needs them, from `property.json` `support.ticketTypes` (`[{ key, label, description?, helpText?, staffRecipients? }]`, at most 50, validated at startup), or the engine defaults `Support`, `Bug`, `Enhancement`, `Billing` when the section is absent. Later `property.json` changes do not resync; admins edit types in the console.

### Workflow, notifications, and email

- `firstResponseDueUtc` = creation + `support.firstResponseTargetHours` (24). `TicketDueWorker` (every minute, job lease `support_ticket_due`) marks unanswered, non-Closed tickets past the target overdue once and sends `TicketOverdue` plus `staff_ticket_overdue` to the assignee, or to every support staff member when unassigned (email only when the staff member keeps Support email on). It also refreshes the gauges `trelliskit.support.tickets.open` and `.overdue`.
- `TicketAutoCloseWorker` (every 15 minutes, job lease `support_ticket_autoclose`) closes `Resolved` tickets with no member reply for `support.autoCloseDays` (7), with a System line, `TicketUpdate`, `ticket_resolved`, and `ticket.updated`.
- Notifications: `TicketUpdate` (category `Account`, push on) to the requester; `TicketOpened` (to every support staff member), `TicketMemberReplied` (to the assignee), `TicketOverdue` (category `Support`). Each groups into one unread row per ticket and type (group key `Ticket:<type>:<id>`, the latest event's title replaces the row). The `Support` preference category (in-app, push, email on; all editable) is shown and editable only for `SupportAgent` and `SystemAdmin`; other accounts never see it and get `400` when they send it.
- Emails (in the template catalog with preview and test sends): `ticket_created`, `ticket_reply` (the reply as text, HTML-encoded), `ticket_resolved` (Essential, to the requester), `staff_ticket_opened` (to the ticket type's `staffRecipients`), `staff_ticket_overdue`. All queued with idempotency keys. There is no reply-by-email.
- Live events: after every staff reply and status change the server publishes `ticket.updated` (`TargetId` = ticket id, `State` = status, recipients = the requester) on the realtime bus.

### Account lifecycle

Account deletion leaves tickets exactly as they are (the requester renders as Deleted member); support staff close and delete them. The account export adds `tickets`: `[{ id, number, type, title, body, status, priority, device, createdUtc, resolvedUtc, closedUtc, replies [{ id, authorRole, body, createdUtc }] }]` (never internal notes or system lines).

### Private media (ticket and direct message attachments)

Media uploads take `purpose` (`Content` default, `Ticket`, `Message`) and `scopeId` (the ticket or conversation id; omitted means pending, bound by the first attachment). Rules:

- Private media attaches only to a target in its purpose and scope, by its owner, once (`409 MediaNotAttachable`); content routes refuse private media and ticket routes refuse content media.
- Private media is never listed by `GET /api/v1/media` or `GET /api/v1/media/{id}` for anyone but its owner, SystemAdmin, and viewers its scope allows.
- After processing settles, the served outputs move out of every location the media CDN and the grant cookie serve, into `<propertyId>/private/<mediaId>/media/` and `.../video/` of the source location (never served directly). The local file route (`/api/v1/media/files/...`) answers `404` for private media whatever the cookie or token.
- Payloads carry per-object URLs `GET /api/v1/media/private/{token}/{file}` valid `messaging.mediaUrlMinutes` (60). The token is the AES-GCM protection (the integrations token protector) of `{ typ: "pmedia", pid, mid, pur, scp, uid, exp }`: one media item, its purpose and scope, the viewer the URL was issued to, and the expiry. The route is anonymous (token-verified); it refuses a forged, foreign-property, or expired token (`403 InvalidSignature`), a token whose media, purpose, or scope no longer match (`404`), and re-checks on every request that the viewer may still see the scope through the resolver registered for the purpose (`MediaService.RegisterPrivateScope`; tickets: the requester and support staff; direct messages: conversation members, registered by the messaging module), so a viewer removed from the scope gets `403` even with an unexpired URL. It streams from the object store with `Range` (`206`), `Cache-Control: private, max-age` up to the expiry, and `Referrer-Policy: no-referrer`. HLS playlists reference segments by relative name, so segments resolve under the same token path. Clients refresh URLs by re-reading the ticket or message when a URL returns `403`. Request history redacts the token path segment.

### Settings

`support.maxBodyLength`, `support.maxAttachments`, `support.allowVideo`, `support.maxOpenTicketsPerMember`, `support.firstResponseTargetHours`, `support.autoCloseDays`, `support.reopenDays`, `support.showAgentNames`, `support.ticketsPerDay`, `support.repliesPerHour` (contract section 14), and `messaging.mediaUrlMinutes` for private media URLs.

---

## Broadcasts and announcements (Phase 5)

Staff announcements over in-app, push, and email (`docs/PHASE5_CONTRACT.md` section 8, resolved decisions 20 and 22, as-built note D). The admin routes are for `SystemAdmin` (and the admin token) and the global `Communicator` role, which may use `/api/v1/admin/broadcasts*` and nothing else in the admin console; only `SystemAdmin` may create, edit, schedule, or send an `Essential` broadcast. `features.broadcasts` false makes every route below `404 NotAvailable`. The `broadcasts` kill switch pauses the send worker (the routes stay open). Identifier prefixes: `bct_` broadcast, `bsg_` saved segment, `bdl_` delivery.

### Segments

A segment is a list of filters `{ field, operator, value }` combined with AND (at most 20; a list value has at most 20 strings). Filters are stored one row per value (`broadcastsegmentfilters`, never JSON) and returned as sent.

| `field` | `operator` | `value` |
|---|---|---|
| `membership` | `eq` | `Active`, `NotActive`, `Any` |
| `membershipSource` | `in` or `eq` | `Subscription`, `SignupCode`, `AdminComp` (an active entitlement from one of them) |
| `store` | `in` or `eq` | `Apple`, `Google`, `Stripe` (an active subscription entitlement in one of them) |
| `profile.<fieldKey>` | a member directory operator (`eq`, `in`, `any`, `all`, `prefix`, `gte`, `lte`, `hasValue`, `minRank`, `maxRank`, `minLevel`) | as in `filters.profile` of the member directory (staff visibility) |
| `labels` | `any` or `all` | a label or a list (member labels, `staff:` labels included) |
| `tags.<key>` | `eq` (a string value) or `exists` (`true`) | |
| `signedUpAfterUtc`, `signedUpBeforeUtc` | `eq` | ISO 8601 UTC time |
| `activeWithinDays`, `inactiveForDays` | `eq` | whole days, 1 to 3650 (`inactiveForDays` includes never seen) |
| `hasPushToken`, `hasVerifiedEmail` | `eq` | `true` or `false` |
| `role` | `in` or `eq` | staff roles (`User` is rejected) |
| `eventRsvp` | `going`, `interested`, `waitlisted`, or `any` | event id (an existing event; needs `features.events`) |
| `seriesFollowers` | `eq` | series id (an existing series; opted-out followers excluded; needs `features.library`) |

An unknown field, option, or operator, a bad value, or `eventRsvp` and `seriesFollowers` with an unknown event or series or with their feature off is `400 SegmentInvalid` with `fieldErrors` naming the filter (`filters[i]`, or `segment.filters[i]` inside a broadcast or preview). Recipients are always limited to accounts that are age attested, not banned, and not deleted, and in `FullyGated` to accounts with an active membership or a staff role (`docs/ACCESS_MODELS_CONTRACT.md` section 7). The audience query is one bounded query over users (an aggregate count, a sample of 10, or a keyset batch by user id).

| Method and path | Body | Success | Errors |
|---|---|---|---|
| `POST /api/v1/admin/broadcasts/segments/preview` | `{ segmentId }` or `{ segment: { filters } }`, `category?` (`Essential` ignores the Announcements preferences in the channel counts) | `200 { count, byChannel: { inApp, push, email }, sample: { objects (at most 10 UserSummary), totalRecords } }` | `400 SegmentInvalid`, `404` |
| `POST /api/v1/admin/broadcasts/segments` | `{ name (1 to 100), description?, filters, labels?, tags? }` | `201 BroadcastSegment` (audited `broadcast.segment.create`) | `400 ValidationFailed`, `400 SegmentInvalid` |
| `POST /api/v1/admin/broadcasts/segments/enumerate` (and `GET /api/v1/admin/broadcasts/segments`) | `EnumerationQuery`; orderings `CreatedDescending` (default), `CreatedAscending`, `NameAscending`; search over name and description; labels and tags | `200 EnumerationResult<BroadcastSegment>` | |
| `GET`, `PUT`, `DELETE /api/v1/admin/broadcasts/segments/{id}` | `PUT`: any create field; omitted fields stay; `filters` replaces | `200`, `200`, `204` | `404` |

`BroadcastSegment` is `{ id, name, description, filters, createdBy, labels, tags, createdUtc, updatedUtc }`. `byChannel.push` counts recipients with an active push token and the Announcements push preference on; `byChannel.email` counts verified addresses with the Announcements email preference on that are not suppressed (`Essential`: preferences ignored, suppressions never).

### Broadcasts

`Broadcast` (contract section 2.5): `{ id, title, channels: { inApp, push, email }, inApp: { title, body, path }, push: { title, body }, email: { subject, preheader, body, ctaLabel, ctaPath }, segment: { id, name, filters }, category, essentialReason, status, sendAtUtc, stats: { targeted, inAppCreated, pushSent, pushFailed, emailQueued, emailSent, emailDelivered, emailFailed, emailSuppressed, skippedPreferences }, createdBy, labels, tags, createdUtc, updatedUtc, startedUtc, sentUtc, canceledUtc }`. `status` is `Draft`, `Scheduled`, `Sending`, `Sent`, or `Canceled`. A saved segment chosen with `segmentId` is copied into the broadcast (`segment.id` names it), so later segment edits never change a scheduled audience. Bodies are the Phase 2 Markdown subset. Push rides on the in-app notification, so `channels.push` needs `channels.inApp`. `email.ctaPath` is a path on the website (default `/announcements/{id}`).

| Method and path | Body | Success | Errors |
|---|---|---|---|
| `POST /api/v1/admin/broadcasts/enumerate` (and `GET /api/v1/admin/broadcasts`) | `EnumerationQuery`; orderings `CreatedDescending` (default), `CreatedAscending`, `UpdatedDescending`, `SendAtAscending`; filters `status` (one or a list), `channel` (`inApp`, `push`, `email`); search over the title; labels and tags | `200 EnumerationResult<Broadcast>` (test copies are never listed) | `400 ValidationFailed` |
| `POST /api/v1/admin/broadcasts` | `{ title, channels?, inApp, push?, email?, segmentId or segment, category?, essentialReason?, labels?, tags? }` | `201 Broadcast` (`Draft`; audited `broadcast.create`, and `broadcast.essential`) | `400 ValidationFailed`, `400 SegmentInvalid`, `403` (Essential by a Communicator), `404` (segmentId) |
| `GET /api/v1/admin/broadcasts/{id}` | | `200 Broadcast` | `404` |
| `PUT /api/v1/admin/broadcasts/{id}` | any create field; omitted fields stay | `200 Broadcast` (Drafts and Scheduled only) | `409 BroadcastNotEditable`, `403` (Essential), `400` |
| `DELETE /api/v1/admin/broadcasts/{id}` | | `204` (Drafts only) | `409 BroadcastNotEditable` |
| `POST /api/v1/admin/broadcasts/{id}/test` | `{ channels?: ["inApp", "push", "email"] }` (default the broadcast's channels; push needs inApp) | `202 { accepted, channels, announcementId }`: a hidden copy marked `[Test]` delivered to the caller only (Announcements preferences do not apply; suppressions do). Not counted toward `broadcasts.maxPerDay`. Audited `broadcast.test` | `400 ValidationFailed`, `403` (the admin token has no member to send to) |
| `POST /api/v1/admin/broadcasts/{id}/schedule` | `{ sendAtUtc }` at least 5 minutes and at most 90 days ahead | `200 Broadcast` (`Scheduled`; audited `broadcast.schedule`) | `400 ValidationFailed` (`fieldErrors.sendAtUtc`, or `segment` over `broadcasts.maxRecipients`), `409 BroadcastNotEditable`, `409 BroadcastLimitReached` (that UTC day is full) |
| `POST /api/v1/admin/broadcasts/{id}/send` | `{ confirmCount }`: within 5 percent of the current preview count | `200 Broadcast` (`Sending`; audited `broadcast.send`, and `broadcast.essential`) | `400 ValidationFailed` (`fieldErrors.confirmCount` with the current count, or `segment` over `broadcasts.maxRecipients`), `409 BroadcastNotEditable`, `409 BroadcastLimitReached` |
| `POST /api/v1/admin/broadcasts/{id}/cancel` | | `200 Broadcast` (`Canceled`; remaining batches stop; audited `broadcast.cancel`) | `409 BroadcastNotEditable` (already sent or canceled) |
| `POST /api/v1/admin/broadcasts/{id}/deliveries/enumerate` (and `GET .../deliveries`) | `EnumerationQuery`; filters `channel`, `status` (with `channel` it applies to that channel, else to any channel) | `200 EnumerationResult<BroadcastDelivery>`: `{ id, broadcastId, user, inAppStatus, pushStatus, emailStatus, labels, tags, createdUtc, updatedUtc }` | `404` |
| `GET /api/v1/admin/broadcasts/{id}/deliveries` | One per recipient with a status per channel, newest first. Filters channel (inApp, push, email) and status (Pending, Created, Queued, Sent, Delivered, Failed, Suppressed, SkippedPreference, NoToken, NoAddress, NotRequested). SystemAdmin or Communicator. | `200` List a broadcast's deliveries | `400` |

`broadcasts.maxPerDay` counts broadcasts (not test copies) that started on a UTC day or are scheduled for it; it applies to `SystemAdmin` too. Delivery statuses: `NotRequested`, `Pending`, `Created` (in-app), `Queued`, `Sent`, `Delivered` (email webhook), `Failed`, `Suppressed` (address suppressed, or push paused or outside notification hours), `SkippedPreference`, `NoToken`, `NoAddress`.

### Delivery (`BroadcastSendWorker`)

Every 15 seconds and at once after a send, under the `broadcast_send` job lease (one instance sends at a time): due Scheduled broadcasts start, and each Sending broadcast is expanded in keyset batches of `broadcasts.batchSize` over user ids from its cursor, at most `broadcasts.maxRecipients` in total. Each batch writes the delivery rows (unique per broadcast and member, so a repeated batch after a crash writes nothing twice) and, per recipient: a `Broadcast` notification (category `Announcements`, path `/announcements/{id}`) when in-app; a push through the push pipeline when push and the Announcements push preference allows (pause, notification hours, tokens, and rate limits still apply); an email (template `broadcast`) when email and the Announcements email preference allows, never to a suppressed address, claimed before sending and keyed `broadcast:{id}:{userId}` so it goes at most once, with `List-Unsubscribe` and one-click unsubscribe for Announcements. `Essential` broadcasts ignore the Announcements preferences and carry no unsubscribe header. Emails are throttled to `broadcasts.emailsPerMinute` per property (the batch resumes next pass). The cursor advances only after a batch completes; cancel stops the remaining batches. Nothing is published on the realtime bus per recipient (clients refresh unread counts lazily). Counters come from the delivery rows, the push states and receipts of the notifications, and the email statuses that the Mailgun webhooks update (refreshed for 7 days after a broadcast finishes). No open or click tracking.

### Announcements (any signed-in account)

| Method and path | Body | Success | Errors |
|---|---|---|---|
| `POST /api/v1/announcements/enumerate` (and `GET /api/v1/announcements`) | `EnumerationQuery`; newest first; search over the title; labels and tags | `200 EnumerationResult<Announcement>`: the broadcasts the caller received | |
| `GET /api/v1/announcements/{id}` | | `200 Announcement` `{ id, title, body, sentUtc, labels, tags }` | `404` for anyone without a delivery |

### Notifications and email

The `Announcements` preference category: in-app on and not editable, push on, email on (push and email editable; the email unsubscribe link turns email off). The `broadcast` email template (Optional; previews and test sends like the other templates) has tokens `subject`, `preheader`, `heading`, `body` (the Markdown subset rendered to safe HTML: paragraphs, bold, italic, strike, lists, quotes, level 2 and 3 headings, http and https links; everything else is literal text), `ctaLabel`, `ctaLink`, `unsubscribeLink`, and `footerNote`.

### Settings

| Key | Range (default) | Apply |
|---|---|---|
| `broadcasts.maxPerDay` | 1 to 50 (3): broadcasts per property per UTC day | Live |
| `broadcasts.maxRecipients` | 1 to 1000000 (100000): largest audience of one broadcast | Live |
| `broadcasts.batchSize` | 50 to 5000 (500): recipients per send batch | Live |
| `broadcasts.emailsPerMinute` | 10 to 10000 (600): broadcast emails queued per property per minute | Live |

Account deletion deletes the member's deliveries. Telemetry: `trelliskit.broadcasts.sent` (labels `channel`, `category`) and `trelliskit.broadcasts.deliveries` (labels `channel`, `outcome`); spans `job BroadcastSendWorker` and `broadcast.batch`.

---

## Realtime (WebSocket)

One WebSocket per running app instance carries every live event (`docs/PHASE5_CONTRACT.md` section 4 and as-built note A). The socket is an optimization: REST stays the source of truth, delivery is at most once, and clients run the REST sync after every reconnect.

### `GET /api/v1/ws`

Watson WebSocket route (RFC 6455, HTTP/1.1 upgrade, text frames only). Anonymous at the HTTP level; the session is authenticated by the `hello` frame. The route is not in the OpenAPI document (WebSocket routes have no OpenAPI form).

**Upgrade request.** The property resolves exactly as for REST (`X-TK-Property` and `X-TK-App` from apps, else the host). Request **no subprotocol** (Watson never echoes `Sec-WebSocket-Protocol`); the protocol version travels in `hello.protocol`. No credentials on the URL.

Pre-upgrade checks (a refused upgrade gets a normal JSON error, never a handshake):

| Status | `error` | When |
|---|---|---|
| `426` | `UpgradeRequired` | Plain `GET` without `Upgrade: websocket` (response carries `Upgrade: websocket`) |
| `404` | `PropertyNotFound` | The property does not resolve or is disabled |
| `403` | `OriginNotAllowed` | An `Origin` header is present and is not `https://` plus the property's `domains.web` or a verified domain, nor a `cors.allowedOrigins` entry. Upgrades without `Origin` (native apps) skip this check |
| `503` | `FeatureDisabled` | The `realtime` kill switch is on for the caller, or `realtime.enabled` is false (`Retry-After: 300`); clients poll (section 5.1) |
| `503` | `ServiceUnavailable` | The task is draining (`Retry-After: 5`) or holds `realtime.maxConnectionsPerTask` sockets (`Retry-After: 30`) |
| `429` | `RateLimited` | More than `realtime.anonymousUpgradesPerMinute` upgrades from the client address in a minute, or `realtime.maxPendingHellos` sockets from the address still waiting for `hello` (`Retry-After` always) |
| `400` | `SubprotocolNotSupported` | The upgrade carries `Sec-WebSocket-Protocol` |

**Frame envelope.** `{ "type", "id", "ts", "data" }`, UTF-8 JSON. Client `id` (string, at most 64 characters) is echoed in `error.frameId`; server `id` is `s-<n>`, increasing per connection; `ts` is server time. Inbound frames at most `realtime.maxInboundFrameBytes` (16 KB; larger closes `1009`); outbound frames at most 64 KB.

**`hello`** (first frame, within `realtime.authTimeoutSeconds`, else close `4408`):

```json
{ "type": "hello", "id": "c1", "data": { "accessToken": "...", "propertyId": "app_...", "client": "Mobile", "clientVersion": "1.4.0", "installId": "...", "appState": "Foreground", "protocol": "tk.v1", "resume": { "syncToken": null }, "capabilities": ["messaging", "notifications", "tickets"] } }
```

The access token is validated exactly like a bearer token (active session, member principal, bound to the resolved property, age attested); failure sends `error` `Unauthorized` and closes `4401`. `propertyId`, when present, must name the resolved property (else `error` `PropertyMismatch`, close `4404`). `protocol`, when present, must be `tk.v1` (else close `4400`). More than `realtime.connectsPerMinute` hellos per member per minute send `error` `RateLimited` with `retryAfterMs` and close `4429`. `capabilities` defaults to all three; the granted set is the requested set intersected with the allowed set: `notifications` and `tickets` always, `messaging` when the property's `messaging` feature is on, the `messaging` kill switch is off for the caller, and the session's tier meets the messaging tier of the access model (Member in `FullyGated` or with `messaging` gated, else SignedIn). A request whose granted set is empty sends `error` `MembershipRequired` and closes `4402`. Beyond `realtime.maxConnectionsPerUser` the member's oldest socket closes `4409`.

```json
{ "type": "welcome", "id": "s-1", "ts": "2026-11-20T18:02:00.123Z", "data": { "connectionId": "rtc_...", "userId": "usr_...", "serverTime": "2026-11-20T18:02:00.123Z", "heartbeatSeconds": 25, "maxInboundFrameBytes": 16384, "capabilities": ["messaging", "notifications", "tickets"], "accessTier": "Member", "resume": { "accepted": false, "syncRequired": true } } }
```

`resume.accepted` is always false and `syncRequired` always true in Phase 5 (no server replay buffer).

**Client frames after `hello`.**

| `type` | `data` | Server behavior |
|---|---|---|
| `ping` | `{}` | `pong {}`. Send one every `welcome.heartbeatSeconds` whatever else arrives; a socket with no inbound frame for two heartbeats plus 5 seconds is closed `1000` (`Heartbeat timeout.`) |
| `auth.refresh` | `{ accessToken }` | Replaces the token (no reply). Another session or an invalid token: `error` `Unauthorized`, close `4401` |
| `state` | `{ appState: "Foreground" \| "Background", activeConversationId?: "cnv_..." \| null }` | Updates the connection registry (push decision). Invalid values get `error` `ValidationFailed` |
| `typing`, `ack`, `read` | section 4.4 | Handled by the messaging module (frames need the `messaging` capability, else `error` `CapabilityRequired`); at most one `typing` `Start` per conversation per `realtime.typingMinIntervalMs`, extra Starts are dropped silently. Ignored while no messaging handler is registered |
| anything else | | `error` `UnknownFrame`; the socket stays open |

A frame before `hello`, a second `hello`, malformed JSON, or a frame without `type` closes `4400`; a binary frame closes `1003`. More than `realtime.maxInboundFramesPerSecond` frames per second (burst twice that) get `error` `RateLimited` with `retryAfterMs`; `realtime.maxViolations` violations within a minute close `4429`.

**Server frames** (engine events; the messaging module adds `message.*`, `conversation.*`, `receipt.*`, `typing`):

| `type` | `data` | When |
|---|---|---|
| `notification.created` | `{ notification: Notification, unreadCount }` | A notification was stored or aggregated for the member (`notifications` capability). Batches of more than 20 notifications (bulk fan-outs) are not sent live |
| `unread.counts` | `{ notifications, conversations?, messages?, requests? }` | Published by features after reads on another device |
| `ticket.updated` | `{ ticketId, status, lastActivityUtc }` | Staff replied to or changed the member's ticket (`tickets` capability; a hint, fetch the ticket) |
| `access.changed` | `{ capabilities, accessTier }` | Membership granted or lapsed, roles or `canSendDirectMessages` changed, access model edited, or the messaging kill switch turned off again |
| `feature.disabled` | `{ feature: "messaging", message }` | The messaging kill switch turned on; messaging events stop |
| `auth.expiring` | `{ expiresUtc }` | 5 minutes before the access token expires; refresh and send `auth.refresh`, or the socket closes `4401` at expiry |
| `resync.required` | `{ reason: "Overflow" \| "BusReconnected" }` | The outbound queue overflowed, or the task's bus listener reconnected (notifications may have been missed); run the REST sync |
| `shutdown` | `{ reconnectAfterMs }` | The task is draining (0 to 5000 ms jitter); a `1012` close follows |
| `error` | `{ code, message, frameId, retryAfterMs? }` | Bad frame, rate limit, unknown type |

**Revocation.** Sign-out, session revocation, password change (other sessions), ban, account deletion, and property disable or delete close the affected sockets with `4401` within `realtime.revocationSeconds` on every task (the receiving task re-validates the session itself). A disabled property's sockets also close on the 1 second sweep of each task. The `realtime` kill switch closes open sockets `4503` within 2 seconds of a task seeing it.

**Backpressure.** Each connection queues at most `realtime.maxQueuedFrames` frames or `realtime.maxQueuedBytes` bytes; on overflow queued `typing` and `receipt.*` frames are dropped first, then the queue is cleared and one `resync.required` `Overflow` is queued. A write blocked longer than `realtime.slowConsumerSeconds` closes the socket `1013` (best effort: a close frame cannot pass a blocked write, so the socket is aborted after the close handshake timeout). Frames of one conversation are written in `seq` order per connection.

**Draining.** On SIGTERM (`TrellisKitServer.Stop`) the task stops accepting upgrades (`GET /api/v1/health` reports `status: "Draining"` and `/api/v1/health/ready` returns `503`), sends `shutdown`, closes every socket `1012` spread over `realtime.drainSeconds`, and deletes its connection registry rows; Watson then stops.

**Scaling.** Events cross tasks on PostgreSQL `LISTEN`/`NOTIFY` (`realtime.bus` `Postgres`, default) on the channel `tk_realtime` (suffixed `_<schema>` when `database.schema` is set), payloads of at most 1 KB with references only; `InProcess` serves a single task. The `realtimeconnections` registry (upserted on `hello` and `state`, touched every 60 seconds, deleted on disconnect, rows not seen for 5 minutes pruned by `RealtimeRegistryPruneWorker` every minute) lets a write on one task see a member's foreground socket on another.

**Settings** (Platform, deployment-wide; `docs/PHASE5_CONTRACT.md` section 14): `realtime.enabled` (`TK_REALTIME_ENABLED`, Restart), `realtime.bus` (`TK_REALTIME_BUS`, Restart), `realtime.heartbeatSeconds` (25), `realtime.authTimeoutSeconds` (10), `realtime.revocationSeconds` (5), `realtime.maxInboundFrameBytes` (16384, Restart), `realtime.maxInboundFramesPerSecond` (20), `realtime.maxViolations` (5), `realtime.maxQueuedFrames` (256), `realtime.maxQueuedBytes` (1048576), `realtime.slowConsumerSeconds` (30), `realtime.maxConnectionsPerUser` (10), `realtime.maxConnectionsPerTask` (10000, Restart for the Watson request limit), `realtime.connectsPerMinute` (30), `realtime.anonymousUpgradesPerMinute` (60), `realtime.maxPendingHellos` (50), `realtime.typingMinIntervalMs` (3000), `realtime.drainSeconds` (20).

**Telemetry.** `trelliskit.realtime.connections` (`client`), `.connects` (`outcome`: ok, auth_failed, timeout, rejected_origin, over_capacity, rate_limited, disabled, protocol), `.closes` (`code`), `.frames` (`direction`, `type`), `.dropped` (`reason`), `.resyncs` (`reason`), `.bus.events` (`kind`, `outcome`), `.bus.lag` (s), `.bus.reconnects`, `.bus.queue_usage` (fraction, alarm at 0.5); Watson's WebSocket metrics; spans `realtime.hello`, `realtime.dispatch`, `realtime.bus.receive` (ids only).

---

## Direct messages (Member, Signed in, Moderator)

Phase 5 one-to-one direct messages over REST (`docs/PHASE5_CONTRACT.md` sections 3, 4.6, 4.7, 4.9, 5.2 and as-built note B). Messaging is never available signed out. In `FullyGated`, and in `PartiallyGated` with `messaging` in `gatedFeatures`, every messaging route needs an active membership (`402 MembershipRequired`, `feature: "messaging"`), reads included; elsewhere any signed-in, age attested account may message. `features.messaging` off: `404 NotAvailable` on every route below. The `messaging` kill switch: `503 FeatureDisabled`. A member without messaging access (lapsed, banned, deleted) cannot receive messages: senders get `403 RecipientUnavailable`. Conversation ids never leak: a conversation you do not belong to (or deleted for yourself) is `404 NotFound`. No staff route lists, searches, or opens a conversation.

### Conversations

| Method and path | Group | Body | Success | Errors |
|---|---|---|---|---|
| `GET /api/v1/conversations`, `POST /api/v1/conversations/enumerate` | Member | `EnumerationQuery` | `200 EnumerationResult<Conversation>` | `400 ValidationFailed`, `400 InvalidContinuationToken` |
| `POST /api/v1/conversations` | Member | `{ recipientUserId, message?: { clientMessageId, body, mediaIds?, replyToMessageId?, showLinkPreview? } }` | `201 { conversation, message }` when new, `200` with the existing conversation (restored for you if you deleted it; the message appended) | `400 ValidationFailed`, `400 CannotMessageSelf`, `400 MessageEmpty`, `400 ContentFiltered`, `403 MessagingNotAllowed`, `403 RecipientUnavailable`, `403 MessagingRestricted`, `403 RequestLimitReached`, `404 NotFound`, `429 RateLimited` |
| `GET /api/v1/conversations/{id}` | Member | | `200 Conversation` | `404 NotFound` |
| `POST /api/v1/conversations/{id}/read` | Member | `{ seq }` | `200 { myLastReadSeq, unreadCount }` (monotonic: a lower `seq` is ignored) | `400 ValidationFailed`, `404 NotFound` |
| `POST /api/v1/conversations/{id}/delivered` | Member | `{ seq }` | `204` (monotonic; REST twin of the WebSocket `ack`) | `400 ValidationFailed`, `404 NotFound` |
| `POST /api/v1/conversations/{id}/accept` | Member | | `200 Conversation`: accepts a pending request, or undoes a decline | `404 NotFound`, `409 NotPendingRequest` |
| `POST /api/v1/conversations/{id}/decline` | Member | | `200 Conversation` (the sender is not told) | `404 NotFound`, `409 NotPendingRequest` |
| `PUT /api/v1/conversations/{id}/folder` | Member | `{ folder: "Inbox" \| "Archived" }` | `200 Conversation` | `400 ValidationFailed`, `404 NotFound`, `409 RequestPending` |
| `PUT /api/v1/conversations/{id}/mute`, `DELETE` same | Member | `{ untilUtc? }` (null mutes indefinitely; must be in the future) | `200 Conversation` | `400 ValidationFailed`, `404 NotFound` |
| `DELETE /api/v1/conversations/{id}` | Member | | `204`: hides every message up to now for you only; a new message from the other member brings it back with only newer messages | `404 NotFound` |
| `POST /api/v1/conversations/changes/enumerate` | Member | `EnumerationQuery` (`syncToken` starts synchronization; `continuationToken` advances within its pages) | `200 EnumerationResult<{ conversation: Conversation, change: "Updated" \| "Removed" }>` | `400 InvalidContinuationToken`, `429 RateLimited` |

Conversation list collection: orderings `LastMessageDescending` (default, your last visible activity), `CreatedDescending`, `CreatedAscending`; filters `folder` (`Inbox` default, `Requests` (pending requests to you), `Archived`, `All`), `unreadOnly`, `withUserId`; search over the other member's display name and handle; labels and tags are your own private metadata on your `ConversationMember` row (`memberId`, `cvm_`; write it with `PUT /api/v1/metadata/ConversationMember/{memberId}`; the other member never sees it). Conversations with a member you blocked are hidden from your lists and return when you unblock.

```json
{
  "id": "cnv_...", "kind": "Direct",
  "members": [ { "user": { "...": "UserSummary" }, "role": "Member", "joinedUtc": "2026-11-20T18:00:00Z" } ],
  "otherMember": { "...": "UserSummary" },
  "folder": "Inbox", "requestState": null,
  "lastMessage": { "id": "msg_...", "seq": 418, "authorUserId": "usr_...", "excerpt": "See you Thursday?", "hasAttachments": false, "state": "Visible", "createdUtc": "..." },
  "lastSeq": 418, "myLastReadSeq": 410, "myLastDeliveredSeq": 418, "otherLastReadSeq": 418, "unreadCount": 8,
  "isMuted": false, "mutedUntilUtc": null, "memberId": "cvm_...", "labels": [], "tags": {},
  "viewer": { "canSend": true, "canAttachMedia": true, "canAttachVideo": true, "canReport": true, "canBlock": true, "canAccept": false, "canDecline": false, "cannotSendReason": null },
  "createdUtc": "...", "updatedUtc": "..."
}
```

- `requestState`: `Pending` for the recipient of a request, `AwaitingAcceptance` for its sender, `Declined` for a recipient who declined (visible only in the declined list and by id), else null.
- `otherLastReadSeq` is null when either member turned `readReceipts` off, while a request is open, or across a block.
- `unreadCount`: visible messages by others above your read position, capped at 999.
- `viewer.cannotSendReason`: `Blocked` (only to the member who blocked), `RecipientUnavailable`, `RequestPending` (you reached the request message limit), `MessagingRestricted` (muted, or `canSendDirectMessages` false), `Unavailable` (every reason that must not be revealed, such as a block by the other member or a declined request), or null.

**Starting and requests.** One direct conversation per pair (`directkey`, unique per property, also under concurrent starts). Eligibility, in order: your messaging access and `flags.canSendDirectMessages`; not muted (`403 MessagingRestricted`, except replies inside a conversation with staff); the recipient exists and has messaging access (`403 RecipientUnavailable`); no block in either direction; not declined; the recipient's `dmPolicy` (`Everyone`; `Known`: only members the recipient messaged or accepted; `Nobody`); staff always pass the policy and never create requests. Every refusal of blocks, policy, declines, and `canSendDirectMessages` is the same `403 MessagingNotAllowed`. A conversation from a non-contact lands in the recipient's `Requests` with a quiet in-app `MessageRequest` notification (one aggregated row while unread, never pushed); until the recipient accepts or replies (replying accepts), the sender may send `messaging.requestMessageLimit` (3) messages (`403 RequestLimitReached`), attachments are shown to the recipient as placeholders (`isHidden: true`, `placeholder: "Attachment hidden until you accept"`, `media: null`), and typing and receipts are not exchanged. A member may have at most `messaging.maxPendingRequests` (20) requests awaiting acceptance (`403 RequestLimitReached`). Accounts younger than `messaging.newAccountHours` (72) may start `messaging.newAccountConversationsPerDay` (3) conversations a day (`403 MessagingRestricted` beyond); everyone else `messaging.newConversationsPerDay` (20) (`429 RateLimited`).

**Sync.** Ordered by change time then member row id, at most 100 per page. A null token returns every conversation of yours (first sync). Nonterminal pages carry a `continuationToken`; the final page has `continuationToken: null` and a separate `syncToken` for the next synchronization (its position never trails real time by more than a minute). `Removed` reports a conversation you deleted for yourself, declined, or whose other member you blocked. Tokens whose position is older than `messaging.syncTokenDays` (30), malformed tokens, and tokens of another caller are `400 InvalidContinuationToken`.

### Messages

| Method and path | Group | Body | Success | Errors |
|---|---|---|---|---|
| `GET /api/v1/conversations/{id}/messages`, `POST /api/v1/conversations/{id}/messages/enumerate` | Member | `EnumerationQuery` | `200 EnumerationResult<Message>` | `400 ValidationFailed`, `400 InvalidContinuationToken`, `404 NotFound`, `429 RateLimited` |
| `POST /api/v1/conversations/{id}/messages` | Member | `{ clientMessageId, body, mediaIds?, replyToMessageId?, showLinkPreview? }` | `201 Message`, or `200` with the stored message for a repeated `clientMessageId` | `400 ValidationFailed`, `400 MessageEmpty`, `400 ContentFiltered`, `400 MediaLimitExceeded`, `403 MessagingNotAllowed`, `403 RecipientUnavailable`, `403 RequestLimitReached`, `403 MessagingRestricted`, `403 MediaNotAllowed`, `404 NotFound`, `409 MediaNotAttachable`, `429 RateLimited` |
| `PUT /api/v1/messages/{id}` | Member | `{ body }` | `200 Message` | `400 ValidationFailed`, `400 MessageEmpty`, `400 ContentFiltered`, `403 Forbidden` (not the author), `404 NotFound`, `409 EditWindowClosed`, `429 RateLimited` |
| `DELETE /api/v1/messages/{id}` | Member | | `200 Message` (tombstone; idempotent) | `403 Forbidden` (not the author), `404 NotFound`, `429 RateLimited` |
| `PUT`, `DELETE /api/v1/messages/{id}/reactions/{key}` | Member | | `200 { reactionCounts, reactionTotal, myReactions }` | `400 UnknownReaction`, `403 MessagingNotAllowed` (across blocks), `403 Muted`, `404 NotFound`, `429 RateLimited` |
| `POST /api/v1/messages/{id}/reactions/enumerate` | Member | `EnumerationQuery` (filter `key`) | `200 EnumerationResult<{ id, user, key, createdUtc, labels, tags }>` | `400 ValidationFailed`, `404 NotFound` |
| `POST /api/v1/messages/search/enumerate` | Member | `EnumerationQuery` with `search` (2 to 200 characters, required) | `200 EnumerationResult<{ message: Message, conversationId, otherMember, headline }>` | `400 ValidationFailed`, `400 InvalidContinuationToken`, `429 RateLimited` |

```json
{
  "id": "msg_...", "conversationId": "cnv_...", "seq": 418, "clientMessageId": "6f0c1e2a-...",
  "author": { "...": "UserSummary" }, "body": "See you Friday?", "originalBody": "See you Thursday?", "isEdited": true, "editedUtc": "...",
  "attachments": [ { "mediaId": "med_...", "kind": "Image", "isHidden": false, "placeholder": null, "media": { "...": "Media" } } ],
  "linkPreview": { "url": "https://example.test/schedule", "siteName": "Example", "title": "Schedule", "...": "LinkPreview" },
  "replyTo": { "id": "msg_...", "seq": 402, "excerpt": "Are you coming...", "authorDisplayName": "Alex Chen", "isDeleted": false, "state": "Visible" },
  "state": "Visible", "reactionCounts": { "like": 1 }, "reactionTotal": 1, "myReactions": [], "labels": [], "tags": {},
  "viewer": { "canEdit": false, "canDelete": true, "canReact": true, "canReport": false },
  "createdUtc": "...", "updatedUtc": "..."
}
```

- **Link previews.** With `showLinkPreview` (default true) and `linkPreviews.enabled`, the server fetches a preview of the first `http` or `https` URL in the body through the same service, SSRF protection, cache, and limits as feed posts and comments ([Link previews](#link-previews-member)), so the link is fetched by the service, never by the other member's device. `linkPreview` is the same `LinkPreview` shape as on comments and feed posts, null when there is no link, the fetch failed, `showLinkPreview` is false, or the message is a tombstone or removed. An edit re-resolves the preview from the new body.
- **Send.** `seq` is assigned in one transaction that locks the conversation row, so it is strictly increasing by 1 with no gaps, in commit order, under any concurrency. `clientMessageId` (1 to 100 characters, a UUID per composed message, reused on every retry) is unique per conversation and author: a repeat returns `200` with the stored message and does nothing else, also when retries race. `clientMessageId` is returned to the author only. Body: plain text after normalization (line endings, invisible characters), 0 to `messaging.maxBodyLength` (4000) characters; no Markdown; clients auto-link URLs. `400 MessageEmpty` without text and attachments. The word filter rejects `Block` terms (`400 ContentFiltered`); `Flag` terms never apply to direct messages. Attachments: up to `messaging.maxAttachments` (4) images or one video, when `messaging.allowMedia` (`403 MediaNotAllowed`) and your media flags allow; media must be yours and unattached (`409 MediaNotAttachable`). `replyToMessageId` must be a message of the same conversation you can see (`400 ValidationFailed`); the quote follows the quoted message's current state. Sends are rate limited by `messaging.messagesPerMinute` (30) and `messaging.messagesPerDay` (1000) with `Retry-After`.
- **Edit.** The author may edit once, within `messaging.editWindowMinutes` (15; 0 disables edits); `originalBody` keeps the text before the edit and is shown to both members as edit history. A second or late edit is `409 EditWindowClosed`. Attachments cannot be edited.
- **Delete for everyone.** The author may delete any time. Both members see a tombstone (`state: Deleted`, empty `body`, null `originalBody`, empty `attachments`); the content is kept internally for moderation and no member route returns it. Removed messages (`state: Removed`, by a moderator) look the same.
- **History.** Orderings `SeqDescending` (default) and `SeqAscending`; keyset continuation over `seq`; `maxResults` default 50, maximum 100. Filters `beforeSeq` and `afterSeq` (exclusive; `afterSeq` with `SeqAscending` is the gap fill and reconnect query) and `aroundSeq` (a neighborhood of up to 50 messages at or before and 50 after it, independently bounded at 100; standard page size, ordering, skip and continuation apply within that neighborhood; cannot combine with beforeSeq or afterSeq). `search` (2 to 200 characters) runs PostgreSQL full text search over Visible messages of the conversation (ordered `SeqDescending`; not combinable with `aroundSeq`). Messages you hid by deleting the conversation are excluded; tombstones are included so `seq` stays contiguous. Reading history moves your delivery position to the newest message returned. Reads are rate limited by `messaging.readsPerMinute` (120).
- **Search.** Full text (`to_tsvector('simple', body)`) over the conversations you belong to and still see: never other members' conversations, messages you hid, tombstones, removed messages, declined requests, or conversations with members you blocked. Orderings `Relevance` (default), `CreatedDescending`, `CreatedAscending`; filters `conversationId`, `withUserId`; at most `messaging.searchMaxResults` per page; `headline` wraps matches in `**`. Direct messages are never in global search and never assistant knowledge.
- **Reactions** use the property's reaction set, never notify, and are refused across blocks. `message.updated` carries the new counts.

### Messaging account routes (Signed in)

| Method and path | Body | Success | Errors |
|---|---|---|---|
| `GET /api/v1/me/messaging/settings` | | `200 { dmPolicy, readReceipts, typingIndicators, pushPreview, exemptFromNotificationHours, updatedUtc }` | `404 NotAvailable` |
| `PUT /api/v1/me/messaging/settings` | `{ dmPolicy?: "Everyone" \| "Known" \| "Nobody", readReceipts?, typingIndicators?, pushPreview?: "SenderOnly" \| "Full" \| "Hidden", exemptFromNotificationHours? }` (partial) | `200` settings | `400 ValidationFailed` |
| `GET /api/v1/me/messaging/unread-count` | | `200 { conversations, messages, requests }` | `402 MembershipRequired` |
| `GET /api/v1/me/messaging/declined`, `POST /api/v1/me/messaging/declined/enumerate` | `EnumerationQuery` | `200 EnumerationResult<Conversation>` (newest decline first; undo with `POST /api/v1/conversations/{id}/accept`) | `402 MembershipRequired`, `400 ValidationFailed`, `400 InvalidContinuationToken` |

Defaults: `dmPolicy` `messaging.defaultDmPolicy` (`Everyone`), `readReceipts` and `typingIndicators` true (both reciprocal), `pushPreview` `messaging.defaultPushPreview` (`SenderOnly`), `exemptFromNotificationHours` false (when true, direct message pushes arrive outside your notification hours). The settings routes stay reachable for signed-in accounts without a membership; unread counts and the declined list need messaging access.

### Reports and Messaging moderation (Moderator: SystemAdmin and Messaging moderators)

`POST /api/v1/reports` accepts `targetType: "Message"` (a message you can see in a conversation you belong to; not your own, `400 CannotReportSelf`) and `"Conversation"`. These reports are stored with scope `Messaging`, never auto-hide, and write a frozen snapshot: the reported message (for a conversation report, the latest message) and up to `messaging.reportContextMessages` (10) earlier messages the reporter could see, tombstones as placeholders, attachment references. Later edits and deletes never change it. Snapshots of resolved reports are purged after `moderation.retainRemovedDays`.

| Method and path | Body | Success | Errors |
|---|---|---|---|
| `POST /api/v1/admin/moderation/messaging/reports/enumerate` | `EnumerationQuery`; filters `status` (`Open` default, `ActionTaken`, `Dismissed`), `targetType` (`Message`, `Conversation`) | `200 EnumerationResult<{ reportId, targetType, targetId, reason, details, status, reporter, targetAuthor, snapshotId, labels, tags, createdUtc }>` (no message text) | `400 ValidationFailed`, `403 Forbidden` |
| `GET /api/v1/admin/moderation/messaging/reports/{reportId}/snapshot` | | `200 { id, reportId, conversationId, reportedMessageId, reporter, capturedUtc, messages: [ { id, messageId, seq, author, body, originalBody, state, isReported, attachmentSummary, editedUtc, createdUtc } ] }` (at most `messaging.reportContextMessages` + 1); every view writes the audit record `message.snapshot.view` | `403 Forbidden`, `404 NotFound` |
| `POST /api/v1/admin/moderation/messaging/messages/{id}/remove` | `{ reason? }` | `200 { targetType, targetId, author, snapshotBody, snapshotState, currentState, capturedSeq }`: the message becomes `Removed` for both members (`message.updated`), its open reports resolve as `ActionTaken`; audited `moderation.message.remove` | `403 Forbidden`, `404 NotFound` (no snapshot), `409 NotChanged` |
| `POST /api/v1/admin/moderation/messaging/messages/{id}/restore` | `{ reason? }` | `200` as above; `Visible` again; audited `moderation.message.restore` | `403 Forbidden`, `404 NotFound`, `409 NotChanged` |
| `POST /api/v1/admin/moderation/messaging/members/{userId}/revoke-messaging` | `{ reason? }` | `200 { userId, canSendDirectMessages: false }` (RevokeMessaging; the member can still read, delete their own messages, report, and block); audited `messaging.revoke` | `403 Forbidden` (also for a SystemAdmin target unless the caller is SystemAdmin), `404 NotFound` |
| `POST /api/v1/admin/moderation/messaging/members/{userId}/restore-messaging` | `{ reason? }` | `200 { userId, canSendDirectMessages: true }`; audited `messaging.restore` | `403 Forbidden`, `404 NotFound` |

Messaging moderators are `Moderator` holders with resource type `Messaging` (or `Global`). Moderators only ever see snapshots; no route opens a conversation. The moderation extension `MessageModerationTarget` (Message and Conversation targets) loads targets from snapshots and applies Remove and Restore to the live message.

### Push, email, realtime, privacy

- **Push** (no in-app row for `DirectMessage`): skipped while the recipient has a foreground realtime connection; otherwise sent when the `Messages` push preference is on, push is not paused, the time is inside the recipient's notification hours (unless `exemptFromNotificationHours`), the conversation is not muted, and it is not a pending request. At most one push per conversation per `messaging.pushCoalesceSeconds` (60) while unread. `data`: `{ type: "DirectMessage", conversationId, seq, path: "/messages/{conversationId}", collapseId, threadId }` (collapse and thread ids are the conversation id). Title and body by `pushPreview`: `SenderOnly` ("Alex Chen sent you a message"), `Full` (sender and a 100 character excerpt), `Hidden` ("New message"). `badge` is unread notifications plus unread conversations.
- **Email**: `messages_unread` (Optional, `Messages` email preference, default off) when messages stay unread longer than `messaging.emailAfterMinutes` (60), at most one per `messaging.emailMinIntervalHours` (12), about messages newer than the previous email; tokens `count`, `senderNames` (up to 3 names, then "and N others"), `link`, `unsubscribeLink`; never message text.
- **Notifications**: category `Messages` (in-app not editable; push on and editable; email off and editable). `MessageRequest` (in-app only, aggregated "Alex Chen and 2 others sent you message requests", path `/messages/requests`).
- **Realtime events** published on the bus (references only): `message.created`, `message.updated` (edit, reactions, moderation), `message.deleted`, `conversation.created`, `conversation.updated` (requests, folders, mutes, deletes, blocks), `receipt.read`, `receipt.delivered`, `typing`.
- **Request history** stores `[redacted]` instead of the request and response bodies of `/api/v1/conversations*`, `/api/v1/messages*`, `/api/v1/me/messaging*`, and `/api/v1/admin/moderation/messaging*`. The server never logs message text.

### Lifecycle and workers

| Worker (job lease) | Interval | What it does |
|---|---|---|
| `message_email` (MessageEmailWorker) | 5 minutes | Queues `messages_unread` (above) |
| `message_retention` (MessageRetentionWorker) | Hourly | When `messaging.retentionDays` is above 0 (default 0, forever), messages older than that become tombstones (text and media purged); prunes report snapshots of resolved reports after `moderation.retainRemovedDays` |
| `conversation_purge` (ConversationPurgeWorker) | Hourly | A conversation every member deleted (or left by account deletion) is purged 30 days later with its messages, tombstones, media, reactions, and metadata |
| `messaging_lapse` (MessagingLapseWorker) | Hourly | Gated models only: records when members of conversations lose messaging access; after `messaging.lapsedRetentionDays` (90) without rejoining, removes their conversations for them (as delete for me) and clears their contacts. The other member's history is untouched; rejoining before then restores everything |

Account deletion turns the member's messages into tombstones ("Deleted member", bodies and media purged), removes their memberships, settings, contacts, and messaging state; the other member keeps their own messages. Account export adds `conversations` (one row per message you can still see in your conversations, yours and the other member's: `{ id, conversationId, seq, authorUserId, isMine, body, originalBody, state, attachmentCount, createdUtc, editedUtc }`; tombstones and removed messages without content) and `messagingSettings`.

### Messaging settings

| Key | Default | Range | Meaning |
|---|---|---|---|
| `messaging.maxBodyLength` | 4000 | 100 to 20000 | Longest message |
| `messaging.allowMedia`, `messaging.maxAttachments` | true, 4 | 0 to 10 | Attachments (images; or one video) |
| `messaging.editWindowMinutes` | 15 | 0 to 1440 | One edit inside the window (0: no edits) |
| `messaging.defaultDmPolicy`, `messaging.defaultPushPreview` | `Everyone`, `SenderOnly` | | Member setting defaults |
| `messaging.requestMessageLimit` | 3 | 1 to 20 | Messages per pending request |
| `messaging.reportContextMessages` | 10 | 0 to 50 | Earlier messages in a report snapshot |
| `messaging.retentionDays` | 0 | 0 to 3650 | Automatic expiry (0: forever) |
| `messaging.lapsedRetentionDays` | 90 | | Days before a lapsed member's conversations are removed for them |
| `messaging.pushCoalesceSeconds` | 60 | 0 to 3600 | One push per conversation per window while unread |
| `messaging.emailAfterMinutes`, `messaging.emailMinIntervalHours` | 60, 12 | | Unread email delay and interval |
| `messaging.syncTokenDays` | 30 | 1 to 365 | Sync token lifetime |
| `messaging.newAccountHours` | 72 | 0 to 720 | New account period |
| `messaging.messagesPerMinute`, `.messagesPerDay`, `.newConversationsPerDay`, `.newAccountConversationsPerDay`, `.maxPendingRequests`, `.editsPerMinute`, `.readsPerMinute`, `.searchMaxResults` | 30, 1000, 20, 3, 20, 60, 120, 50 | | Limits |

## Product events (opt-in)

`GET /api/v1/property` exposes `productEventsEnabled`, true only when the property's `metrics.enabled` and `metrics.productEventsEnabled` are both on. The latter defaults to false. Clients must wait for the live property response before emitting events; the build-time property is not consent to collect.

| Method and path | Body | Success | Errors |
|---|---|---|---|
| `POST /api/v1/metrics/events` | `{ name, dimensions }`, at most 4096 bytes | `204` recorded, or collection disabled | `400 ValidationFailed`, `401 Unauthorized`, `413 TooLarge`, `429 RateLimited` |

Anonymous callers may emit only `signup.started` with exactly one dimension, `source` (`web`, `mobile`, or `other`). Authenticated members may emit property-declared custom events from `metrics.events`; names and dimension values must match finite configuration allowlists. Server lifecycle events cannot be emitted through this endpoint. Limits are 30 requests per IP per minute and 60 per member per minute. No client-supplied member ID or content is stored.

Server events are `signup.registered`, `signup.verified`, `profile.completed`, `member.firstAction`, and `notification.opened`. The first action is the first successful feed post, forum thread, or comment. Milestones count once per member; notification opens count a successful individual unread-to-read transition, excluding bulk marking. Aggregates keep only hourly counts, event names, and finite dimensions. Minimal member milestone keys support deduplication and are erased on account closure. Custom events use `events.custom.<name>` metrics. Collection errors do not change the result of a successful member action.

## Platform: metric comparisons (PlatformAdmin)

These routes require the platform host and a platform administrator token. Property administrator tokens cannot query another property's metrics. Reads use property-bound aggregate stores and can include retained history for a disabled property. Deleted and deleting properties are excluded.

| Method and path | Body | Success | Errors |
|---|---|---|---|
| `POST /api/v1/platform/metrics/comparisons/enumerate` | `PlatformMetricQuery`: `EnumerationQuery` plus `query` (`MetricQuery`) and `includeSeries` (false) | `200 EnumerationResult<PropertyMetricComparison>`; at most 500 matching properties scanned and 20 returned; standard keyset continuation or skip | `400 ValidationFailed`, `400 InvalidContinuationToken`, `401 Unauthorized` |
| `POST /api/v1/platform/properties/{propertyId}/metrics/query` | `MetricEnumerationQuery` (standard query fields plus nested `query: MetricQuery`) | `200 EnumerationResult<MetricObservationRow>` with metric/unit/calendar/watermark context | `400 ValidationFailed`, `401 Unauthorized`, `404 NotFound` |

Comparison filters include property `status`, labels, tags, and search. Ordering is `ValueDescending` (default) or `ValueAscending`; standard continuation tokens are supported; `query.groupBy` is refused because the properties are the groups. Counts rank by sum, gauges by the last bucket, and distributions by the worst bucket percentile. Percentiles are never averaged. Unknown metrics on an individual property produce `unavailableReason: MetricNotRegistered`; absent or suppressed values remain null. `includeSeries` includes an initial enumerated observation page per property. Continue it through the per-property metric query endpoint with the same selection. Other query bounds match the property-admin metrics query.

`PropertyMetricComparison`: `propertyId`, `name`, `metric`, `unit`, `aggregation` (`Sum`, `Last`, `WorstBucket`), nullable `value`, `complete`, `completeThroughUtc`, nullable `unavailableReason`, optional `data`, `labels`, and `tags`.

## Registered metrics and dashboards

These property-scoped routes require `SystemAdmin` or the property admin token. Legacy `admin/metrics/*` summary endpoints also enumerate their daily observations, retaining scalar totals alongside the page. New dashboards read registered aggregates; they never scan request history or member content on a page request. See [Metrics contract](docs/METRICS_CONTRACT.md) for source definitions, calendars, privacy, retention, and property extensions.

| Method | Path | Request and response |
|---|---|---|
| GET / POST | `/api/v1/admin/metrics` / `/api/v1/admin/metrics/enumerate` | `EnumerationQuery` -> `EnumerationResult<MetricDefinition>`; filters kind/source/feature, search keys/titles, immutable creation anchor. Source at most 256 definitions. Each declares key, title, unit, kind, source, allowed groupings/buckets, retention, role, sensitivity, feature and drilldown. |
| GET / POST | `/api/v1/admin/metrics/pages` / `/api/v1/admin/metrics/pages/enumerate` | `EnumerationQuery` -> `EnumerationResult<MetricPageDeclaration>`; six engine pages plus at most 20 property pages, at most 24 chart declarations per page. |
| GET / POST | `/api/v1/admin/metrics/workers` / `/api/v1/admin/metrics/workers/enumerate` | `EnumerationQuery` -> `EnumerationResult<WorkerPassRecord>`; filters worker/outcome/from/to; actual start timestamps, SQL keyset paging across retained history. No 2,000-row truncation. |
| GET / POST | `/api/v1/admin/metrics/annotations` / `/api/v1/admin/metrics/annotations/enumerate` | `EnumerationQuery` -> `EnumerationResult<MetricAnnotation>`; filters kind/from/to, SQL keyset paging. |
| POST | `/api/v1/admin/metrics/query`, `POST /api/v1/admin/metrics/query/enumerate` | `MetricEnumerationQuery` -> `EnumerationResult<MetricObservationRow>` with scalar metric/unit/timeZone/completeThroughUtc context. |

Metric queries page actual current/comparison observations. Each object has `id`, `period` (`Current` or `Previous`), `seriesKey`, `startUtc`, `endUtc`, nullable `value`, `complete`, `suppressed`, `samples`, `labels`, and `tags`. Standard `filters.period` and `filters.seriesKey` select rows. The nested `query` selects the metric and source window; its dimension filters are separate from enumeration filters. Cursors are bound to the entire selection and property. Annotations are a separate enumeration. Queries are capped at 2,000 buckets, 20 leading groups plus `Other`, eight exact dimension filters and 1,830 days; each transport page obeys the normal admin cap. Clients follow cursors for the selected chart window before reconstructing rendering series.

```json
{
  "maxResults": 25,
  "continuationToken": null,
  "query": {
    "metric": "requests.latency",
    "from": "2026-11-01T00:00:00Z",
    "to": "2026-11-08T00:00:00Z",
    "bucket": "Day",
    "groupBy": "route",
    "filters": {},
    "compare": true,
    "percentile": 0.95
  }
}
```

Missing observations remain null; they are not fabricated zeros. Member-sensitive grouped points with fewer than five members are suppressed with null value and zero samples. Operational series use UTC; business days/weeks use the property's usage calendar. Hour buckets are UTC instants, so repeated daylight-saving hours stay distinct. Distribution queries merge histogram counts before selecting the percentile; they never average precomputed percentiles. Previous series cover the immediately preceding equal-duration interval.

## Admin: daily summary (SystemAdmin)

The daily administrator summary email and its report. Recipients, the send hour, and extra lines are deployment settings (`AdminSummary` in the settings JSON file; see [Admin summary](docs/ADMIN_SUMMARY.md)). Both routes require `SystemAdmin` or the property admin token.

| Method and path | Body or query | Success | Errors |
|---|---|---|---|
| `GET /api/v1/admin/summary` | `date` (`yyyy-MM-dd`, default yesterday in `usage.timeZone`) | `200 AdminSummaryReport` | `400 ValidationFailed` (format, future date, more than 1,800 days ago), `403 Forbidden` |
| `POST /api/v1/admin/summary/send` | `{ date?, to? }` | `200 AdminSummarySendResult { date, queued, alreadySent }` | `400 ValidationFailed` (date, invalid `to`, or no `to` and no configured recipients), `403 Forbidden` |

`AdminSummaryReport`: `propertyId`, `propertyName`, `date`, `timeZone`, `fromUtc`, `toUtc` (the local day, exclusive end), `generatedUtc`, `sections` (`[{ title, rows }]`), and `skipped` (configured items that could not be reported, with the reason). A row has `metric`, `label`, nullable `group`, `unit`, nullable `value`, `previousValue`, `sevenDayAverage`, and `change` (relative to the day before), `complete`, `suppressed`, `timeZone` (the bucket calendar: `usage.timeZone`, or `UTC` for operational metrics), and `display`, the formatted line the email shows. Missing data stays null and displays as `No data`; member-sensitive groups smaller than five are withheld. The report is bounded by the engine default items plus at most 60 configured items, each with at most 20 groups plus a total and `Other`.

`send` with `to` always queues one copy to that address. Without `to` it queues one message per configured recipient that has not received that date's summary; the daily worker shares the same once-per-date-and-recipient key, so a manual send never causes a duplicate. Audited `admin_summary.send`.

```json
{
  "propertyId": "app_...",
  "propertyName": "Example Community",
  "date": "2026-11-23",
  "timeZone": "America/Los_Angeles",
  "sections": [
    {
      "title": "Members",
      "rows": [
        { "metric": "users.new", "label": "New sign-ups", "group": null, "unit": "count", "value": 12, "previousValue": 9, "sevenDayAverage": 10.43, "change": 0.3333, "complete": true, "suppressed": false, "timeZone": "America/Los_Angeles", "display": "12 (day before 9, +33%; 7-day average 10.43)" }
      ]
    }
  ],
  "skipped": []
}
```

## Reviewed exercise and technique catalog (features.catalog)

The catalog is gated by `features.catalog` (default off; every route below returns `404 NotAvailable` when it is off, and `503` while the `catalog` kill switch is on). Its demonstrations reference existing library `lessonIds` and the library's media and audience rules. Entries remain Draft until a library editor submits them and a SystemAdmin explicitly approves the content and source licensing. The engine seeds no entries and publishes nothing automatically. Ordinary member and anonymous routes return Published entries only. Draft, InReview, Rejected, Archived and deleted entries are hidden. Editing content or relationships returns the source to Draft. Private review notes and author/reviewer IDs are omitted from public responses.

Anonymous catalog/configuration reads share an IP bucket governed by `rateLimits.publicFeedPerMinute`; authenticated catalog requests use the member read allowance. Exhaustion returns `429 RateLimited` with Retry-After. Public responses require cache revalidation on each request. Author text in sources, structured descriptors and relationship notes also follows the word filter (`400 ContentFiltered` for blocked terms); flagged text creates a Library-scoped moderation report and source attribution is recorded transactionally in the content changelog.


| Route | Request and behavior | Result / errors |
|---|---|---|
| `GET /api/v1/catalog/config` | Anonymous; property programs, `library.catalogMuscles` and `library.catalogEquipment` options, per-program `library.catalogDescriptorProfiles`, kinds, difficulties and relationshipKinds. | `200 {programs,muscles,equipment,kinds,difficulties,relationshipKinds,descriptorProfiles}`; `404 NotAvailable` |
| `GET /api/v1/catalog/entries`, `POST /api/v1/catalog/entries/enumerate` | Published member reads. Bounded enumeration with maxResults (1-100), continuationToken, ordering, search, labels, tags; filters kind, program, difficulty, muscle, equipment, familyKey, nameLanguage, attireKey, levelKey, notationSystemKey. | `200 EnumerationResult<CatalogEntry>`; `400 ValidationFailed` |
| `GET /api/v1/catalog/entries/{idOrSlug}` | Published member reads. Lookup by identifier or current slug. | `200 CatalogEntry`; `404 NotFound` |
| `GET /api/v1/catalog/entries/{id}/relationships`, `POST /api/v1/catalog/entries/{id}/relationships/enumerate` | Bounded directed outgoing edges; filter kind, search target name/note, labels/tags. Public results require both endpoints published. | `200 EnumerationResult<CatalogRelationship>`; `400`, `404` |
| `GET /api/v1/public/catalog/entries`, `POST /api/v1/public/catalog/entries/enumerate` | Anonymous published reads. Bounded enumeration with maxResults (1-100), continuationToken, ordering, search, labels, tags; filters kind, program, difficulty, muscle, equipment, familyKey, nameLanguage, attireKey, levelKey, notationSystemKey. | `200 EnumerationResult<CatalogEntry>`; `400 ValidationFailed` |
| `GET /api/v1/public/catalog/entries/{idOrSlug}` | Anonymous published reads. Lookup by identifier or current slug. | `200 CatalogEntry`; `404 NotFound` |
| `GET /api/v1/public/catalog/entries/{id}/relationships`, `POST /api/v1/public/catalog/entries/{id}/relationships/enumerate` | Bounded directed outgoing edges; filter kind, search target name/note, labels/tags. Public results require both endpoints published. | `200 EnumerationResult<CatalogRelationship>`; `400`, `404` |
| `GET /api/v1/admin/catalog/entries`, `POST /api/v1/admin/catalog/entries/enumerate` | LibraryEditor or SystemAdmin; includes draft/review/archive states. Bounded enumeration with maxResults (1-100), continuationToken, ordering, search, labels, tags; filters kind, program, difficulty, muscle, equipment, familyKey, nameLanguage, attireKey, levelKey, notationSystemKey, status. | `200 EnumerationResult<CatalogEntry>`; `400 ValidationFailed` |
| `GET /api/v1/admin/catalog/entries/{idOrSlug}` | LibraryEditor or SystemAdmin; includes draft/review/archive states. Lookup by identifier or current slug. | `200 CatalogEntry`; `404 NotFound` |
| `GET /api/v1/admin/catalog/entries/{id}/relationships`, `POST /api/v1/admin/catalog/entries/{id}/relationships/enumerate` | Bounded directed outgoing edges; filter kind, search target name/note, labels/tags. Public results require both endpoints published. | `200 EnumerationResult<CatalogRelationship>`; `400`, `404` |
| `POST /api/v1/admin/catalog/entries` | Library staff create Draft; full editable CatalogEntry fields. | `201 CatalogEntry`; `400 ValidationFailed`, `403 Forbidden`, `409 SlugInUse` |
| `PUT /api/v1/admin/catalog/entries/{id}` | Full editable aggregate replacement and required expectedRevision; preserves outgoing edges, returns Draft. | `200 CatalogEntry`; `400`, `403`, `404`, `409 CatalogChanged or SlugInUse` |
| `DELETE /api/v1/admin/catalog/entries/{id}` | Library staff soft deletion, required expectedRevision query parameter. | `204`; `400`, `403`, `404`, `409 CatalogChanged` |
| `POST /api/v1/admin/catalog/entries/{id}/submit` | `{expectedRevision,note?}`; Draft/Rejected to InReview; validates publication readiness. | `200 CatalogEntry`; `400`, `403`, `404`, `409 CatalogChanged or InvalidTransition` |
| `POST /api/v1/admin/catalog/entries/{id}/approve` | `{expectedRevision,note?}`; SystemAdmin only, InReview to Published. Explicit review of licensing/content. | `200 CatalogEntry`; `400`, `403`, `404`, `409` |
| `POST /api/v1/admin/catalog/entries/{id}/reject` | `{expectedRevision,note?}`; SystemAdmin only, InReview to Rejected. | `200 CatalogEntry`; `400`, `403`, `404`, `409` |
| `POST /api/v1/admin/catalog/entries/{id}/archive` | `{expectedRevision,note?}`; library staff withdraw public visibility. | `200 CatalogEntry`; `400`, `403`, `404`, `409` |
| `PUT /api/v1/admin/catalog/entries/{id}/relationships` | `{expectedRevision,relationships:[{targetId,kind,note,labels,tags}]}` atomically replaces at most 100 outgoing edges and returns Draft. Same-property targets, no self references or duplicate kind/target pairs. | `200 CatalogEntry`; `400`, `403`, `404`, `409 CatalogChanged` |

`CatalogEntry` editable fields are kind (`Exercise`/`Technique`), name (2-160), slug (3-80 lowercase URL words separated by single hyphens, excluding reserved public-page words; surrounding whitespace and uppercase are normalized consistently with `POST /api/v1/public-pages/slugs/check`), summary (at most 2000), program (configured key), difficulty (`Beginner`/`Intermediate`/`Advanced`/`AllLevels`), movementPattern, plane, laterality, techniqueType, fromPosition, toPosition (optional descriptors at most 100 each). Ordered arrays: aliases (at most 30 values, at most 100 each); primaryMuscles, secondaryMuscles and equipment (configured keys, at most 30 each); instructions (at most 50), cues, commonErrors, regressions, progressions, substitutions, contraindications (at most 30 each, nonempty values at most 1000); lessonIds (at most 20 same-property lesson IDs). Response also includes id, status, monotonic revision, createdUtc, updatedUtc, publishedUtc, reviewedUtc, reviewNote and viewer `{canEdit,canSubmit,canReview}`. Labels/tags follow the common metadata limits and staff namespace rules. Public lesson references are rechecked against current publication and audience before returning IDs.

Sources are bounded to 20 `{url,title,license,attribution,method,model,retrievedUtc}` records. Title (1-200) and license identifier (1-80) are required; supplied URLs must be HTTPS (at most 2000, no credentials); attribution at most 2000. Method is Manual, Import or Model; model at most 200. Drafts may contain unverified licenses but submission/publication requires an explicit permissive identifier (case insensitive): CC0-1.0, CC-BY-4.0, PublicDomain, MIT, Unlicense or Original. CC-BY-4.0 requires attribution; all except Original require a source URL. Submission also requires instructions, a configured program and, for Exercise entries, a primary muscle. Techniques may have no muscle fields. These checks do not replace the administrator's source review.

`CatalogRelationship` response includes id, sourceId, targetId, targetName, targetSlug, targetKind, kind, note, labels, tags and createdUtc. Supported kinds: Regression, Progression, Substitution, Transition, Prerequisite, Counterpose. Notes at most 1000. Target withdrawal immediately hides its public relationships. All aggregate writes and relationship replacement use transactional revision comparisons; stale writers receive `409 CatalogChanged` without partial changes.


`modalityDetails` adds optional typed descriptors to the existing entry body and response:

```json
{"localizedNames":[{"languageTag":"en","text":"Source-reviewed example name"}],"familyKey":"example-family","attireKey":null,"levelKey":null,"notationSystemKey":null,"notationText":null}
```

The example requires matching configured options for the entry's program. Reads always return a group, with an empty names array and null family, attire, level and notation fields for legacy entries. Omitted/null `modalityDetails` preserves existing values on an update; an explicit object replaces the whole group, and `{"localizedNames":[],"familyKey":null}` clears it. This is the only partial-preservation exception to the existing aggregate update. The required revision, Draft reset, moderation hold and source review rules remain unchanged.

Names are bounded to 10, each with 1..160 characters after NFC normalization and trimming. Distinct names may share a language; duplicate normalized language/text pairs are rejected case-insensitively. `languageTag` must be a configured option for the current program and follow `language[-Script][-REGION]`: 2..8 ASCII letters for language, optional four-letter script, optional two-letter or three-digit region, maximum 35 characters. Tags are case-normalized; unsupported variants/extensions/private-use tags are rejected. Family is null or a configured key of 1..64 letters, digits, underscores or hyphens. Invalid descriptors return `400 ValidationFailed` with field errors; blocked text returns `400 ContentFiltered`. Changing programs requires explicitly replacing/clearing incompatible preserved descriptors. Optional descriptors do not become publication prerequisites.

Configuration `descriptorProfiles` contains at most 50 per-program objects: `{programKey,familyLabel,families:[{key,label}],nameLanguages:[{key,label}],attireLabel,attires:[{key,label}],levelLabel,levels:[{key,label}],notationSystems:[{key,label}]}`. Each program is unique and configured. Families, attires, levels and notation systems each accept at most 100 options; languages at most 20. Arrays are nonnull and default empty; labels are 1 to 80 characters. AttireLabel defaults to `Attire`, levelLabel to `Level`. The engine supplies no taxonomy or automatic translation. Existing content remains readable after option removal; edits and publication must use current options or clear invalid values.

Migration 130 adds optional `attireKey`, `levelKey`, `notationSystemKey` and `notationText` to `modalityDetails`. Keys must match configured options for the current program. Instructional content level is not a person's rank, credential or verified status. Notation text is trimmed, NFC-normalized and bounded to 1 to 500 characters; notation system and nonempty text must both be present or both absent. Blank values normalize to null. Notation passes the existing word filter and transactional content-review attribution. Public rendering includes the system label and escapes both configured labels and text.

Compatibility: an explicit older-shaped group without the new fields clears those fields, because explicit groups are complete replacements. Editors preserving only part of a group must carry forward its fetched members. Omission/null of the whole group still preserves all fields. Earlier import fingerprints remain stable when the new fields are absent or null; a source change to populated descriptors remains subject to source review, Draft state, moderation and revision guards.

Enumeration search includes localized text. `familyKey`, `attireKey`, `levelKey` and `notationSystemKey` are exact and `nameLanguage` normalizes tags; these filters combine with existing program/metadata filters and participate in continuation-token scope. The 100-record page cap remains. Reviewed version 1 imports accept the same group and omission-preservation rule, with existing source-license approval, untouched-draft and revision guards. The model draft generator does not emit these descriptors. See [Catalog modality descriptors](docs/CATALOG_MODALITIES.md).


`apparatusSetup` is a separate optional entry request group and a nonnull response object (migration 131):

```json
{"apparatusKey":null,"manufacturer":null,"model":null,"setupText":null,"springSetupText":null}
```

A nonnull `apparatusKey` must be an existing `library.catalogEquipment` key and occur in the entry's selected `equipment` array. No separate apparatus taxonomy is introduced. Manufacturer/model are trimmed NFC plain text, at most 120 characters each; setupText/springSetupText are trimmed NFC plain text, at most 500 each. Whitespace-only fields become null. Any nonempty descriptive field requires apparatusKey; model requires manufacturer; springSetupText requires apparatusKey, manufacturer and model together. General setup may omit manufacturer/model. Literal source notation and internal line breaks are preserved, without resistance conversions or inferred identity. Invalid context or bounds returns `400 ValidationFailed`; blocked text returns `400 ContentFiltered`.

Omitting/nulling the whole apparatusSetup group preserves the stored group on update or import. An explicit object replaces the entire group; `{}` clears it. This group is independent of modalityDetails, so an existing client that replaces modalityDetails and omits apparatusSetup retains the apparatus context. Removing selected equipment while preserving an incompatible setup fails validation without changes. Removal from property vocabulary leaves existing values readable; later writes/publication must correct or clear them. Stale `expectedRevision` returns `409 CatalogChanged` atomically.

Public/member/staff reads expose the same typed setup according to the existing entry visibility rules. The existing equipment filter finds relevant entries; no new unbounded list or apparatus filter is added. Public HTML escapes the apparatus label, manufacturer, model and literal setup text, retaining source-specific context beside spring text. All fields participate in text moderation, transactional contribution tracking, Draft reset and human publication review. Reviewed imports use the same validation and revision/source guards. Empty/absent apparatus groups retain earlier import fingerprints, including populated modality groups. The closed model draft generator rejects a supplied nonnull apparatusSetup, including an empty object.

### Reviewed catalog source imports

All source-management routes require `SystemAdmin` and `features.catalog`. The source object is `{id,name,url,license,attribution,enabled,maxEntries,revision,reviewedUtc,reviewedByUserId,lastAttemptUtc,createdUtc,updatedUtc,labels,tags}`. Names are 2..200 characters; URLs are HTTPS, at most 2000 characters, without credentials, query strings or fragments. Licenses are `CC0-1.0`, `CC-BY-4.0`, `PublicDomain`, or `Unlicense`, compared case-insensitively and stored canonically. Attribution is at most 2000 characters and required for `CC-BY-4.0`. `maxEntries` is 1..100, default 20. Sources begin unreviewed, even when enabled.

| Method | Path | Contract |
|---|---|---|
| GET | `/api/v1/admin/catalog/import-sources` | Standard bounded enumeration (maximum 100), search name/URL, filters `enabled` and `reviewed` (`true`/`false`), labels/tags, continuation and ordering `NameAscending`/`CreatedAscending`/`CreatedDescending`. Returns `EnumerationResult<CatalogImportSource>` |
| POST | `/api/v1/admin/catalog/import-sources/enumerate` | Same enumeration with `EnumerationQuery` JSON body |
| POST | `/api/v1/admin/catalog/import-sources` | `{name,url,license,attribution?,enabled?,maxEntries?,labels?,tags?}`; 201 source. 400 `ValidationFailed`; 409 `SourceUrlInUse` |
| GET | `/api/v1/admin/catalog/import-sources/{id}` | 200 source; 404 `NotFound` |
| PUT | `/api/v1/admin/catalog/import-sources/{id}` | Full editable source replacement plus positive `expectedRevision`; every edit revokes review. Set `enabled:false` to withdraw. 200 source; 400 `ValidationFailed`; 404 `NotFound`; 409 `CatalogSourceChanged` or `SourceUrlInUse` |
| POST | `/api/v1/admin/catalog/import-sources/{id}/review` | `{expectedRevision,licenseReviewed:true}` explicitly acknowledges permission to reuse every record under the configured license. 200 source with advanced revision and review evidence. 400 `ValidationFailed`; 404 `NotFound`; 409 `CatalogSourceChanged` |

All routes also return 401 for missing authentication, 403 `Forbidden` for non-administrators, and 404 `NotAvailable` when `features.catalog` is off. Source mutations are audited. Review authorizes ingestion only; existing catalog submit/approve routes remain the sole publication path.

The existing Jobs API exposes `catalog.importSources`, initially disabled, with a seven-day schedule, one concurrent item and three attempts. Run parameters are an optional JSON **string** containing `{sourceIds:[...]}` with at most 20 distinct identifiers. Empty selection chooses up to 20 reviewed enabled sources ordered by oldest real fetch attempt, so later sources do not starve behind the first window. An explicit selection must resolve entirely to eligible sources in the current property. Jobs capture source revisions when planned; changed, disabled or review-revoked sources cannot write from stale plans. Job reports and their items use the existing bounded Jobs enumerations.

The manifest contract (version 1, closed shape, record keys, bounds, outcomes, fetch policy, robots.txt, and redirect rules) is in [Catalog imports](docs/CATALOG_IMPORTS.md). Invalid `sourceIds` parameters return `400 InvalidJobParameters` when the run is queued.

### Catalog draft generation job

`POST /api/v1/admin/jobs/catalog.generateDrafts/run` uses the standard SystemAdmin job run contract, returning `201 JobRunView`. The request contains `dryRun` and `parameters` as a JSON object encoded in a string. Decoded parameters: `topics` (1 to 20 distinct names, 2 to 160 characters each), `kind` (`Exercise` or `Technique`), optional configured `program`, `sourceText` (40 to 10000 characters), `source` (`title`, HTTPS `url`, `license`, optional `attribution`), required `sourceReviewed: true`, and `maxOutputTokens` (256 to 12000, default 4000). Licenses accepted for generation are `CC0-1.0`, `CC-BY-4.0` with attribution, and `PublicDomain`. The whole parameter string is at most 16384 characters. Invalid or missing parameters return `400 InvalidJobParameters`; normal job authorization, active-run and budget errors apply.

The job is manual and disabled by default, requires `features.catalog`, and real runs require `features.ai` and a configured `CatalogAuthoring` role. A preview calls no model and writes no entries. Real runs use `CatalogAuthoring` and produce Draft entries only. Item result JSON includes `topic`, `outcome` (`Planned`, `CreatedDraft`, `Existing`, `Unsupported`), nullable `entryId`, deterministic `slug`, `sourceHash`, `dryRun`, and `humanReviewRequired: true`. Invalid model responses fail the item; normal catalog submit/approve actions are required for publication. See [catalog generation](docs/CATALOG_GENERATION.md) for provenance, review and idempotency limits.

## Collection contract migration (2026-11-23)

Fixed catalogs and aggregate collections follow the same `EnumerationQuery` / `EnumerationResult<T>` contract as persisted record lists. GET fields and POST bodies have the same semantics. Clients consume `objects`, obey the effective `maxResults`, and follow opaque cursors until `endOfResults`; they never infer completeness from the page length. All caps are enforced by the shared normalizer. Counts are exact when supplied. Immutable catalog entries use the Unix epoch as their creation anchor; document and persisted-record collections use their documented stored timestamps. Empty catalog metadata is returned as `labels: []`, `tags: {}` and nonmatching metadata filters return no entries.

Mutation responses remain their documented resource shapes. Single-object documents may retain fixed configuration attributes and explicit bounded previews. These are distinct from collection retrieval endpoints.

| GET collection | Canonical POST | Objects | Additional context / selection |
|---|---|---|---|
| `GET /api/v1/settings` | `POST /api/v1/settings/enumerate` | `SettingField` | None |
| `GET /api/v1/platform/settings` | `POST /api/v1/platform/settings/enumerate` | `SettingField` | None |
| `GET /api/v1/admin/property/settings` | `POST /api/v1/admin/property/settings/enumerate` | `PropertySettingField` | propertyId |
| `GET /api/v1/platform/properties/{propertyId}/settings` | `POST /api/v1/platform/properties/{propertyId}/settings/enumerate` | `PropertySettingField` | propertyId |
| `GET /api/v1/admin/email/templates` | `POST /api/v1/admin/email/templates/enumerate` | `EmailTemplateView` | None |
| `GET /api/v1/admin/email/staff-events` | `POST /api/v1/admin/email/staff-events/enumerate` | `EmailStaffEventView` | None |
| `GET /api/v1/me/notification-preferences` | `POST /api/v1/me/notification-preferences/enumerate` | `CategoryPreferenceView` | pushPaused, autoFollowOnComment, notificationHours |
| `GET /api/v1/reactions/set` | `POST /api/v1/reactions/set/enumerate` | `ReactionSetItem` | likeKey |
| `GET /api/v1/media` | `POST /api/v1/media/enumerate` | `MediaView` | filters.ids: at most 20 requested identifiers |
| `GET /api/v1/admin/documents` | `POST /api/v1/admin/documents/enumerate` | `AdminDocumentView` | syncsFromFolder |
| `GET /api/v1/platform/properties/{propertyId}/documents` | `POST /api/v1/platform/properties/{propertyId}/documents/enumerate` | `AdminDocumentView` | syncsFromFolder |
| `GET /api/v1/admin/property/domains` | `POST /api/v1/admin/property/domains/enumerate` | `PlatformDomainResponse` | propertyId |
| `GET /api/v1/admin/property/apps` | `POST /api/v1/admin/property/apps/enumerate` | `PlatformAppResponse` | propertyId |
| `GET /api/v1/admin/profile-fields/{id}/stats` | `POST /api/v1/admin/profile-fields/{id}/stats/enumerate` | `ProfileOptionCount` | valueCount, fillRate, updated30d |
| `GET /api/v1/admin/updates/signing-keys/installs` | `POST /api/v1/admin/updates/signing-keys/installs/enumerate` | `UpdateCountRow` | None |
| `GET /api/v1/admin/app/drafts` | `POST /api/v1/admin/app/drafts/enumerate` | `AppConfigDraft` | None |
| `GET /api/v1/admin/jobs` | `POST /api/v1/admin/jobs/enumerate` | `JobDefinition` | None |
| `GET /api/v1/admin/ai/roles` | `POST /api/v1/admin/ai/roles/enumerate` | `AiTaskRole` | None |
| `GET /api/v1/admin/ai/harnesses` | `POST /api/v1/admin/ai/harnesses/enumerate` | `AiHarnessProfile` | None |
| `GET /api/v1/admin/ai/prompts` | `POST /api/v1/admin/ai/prompts/enumerate` | `AiPrompt` | None |
| `GET /api/v1/admin/update-groups/{id}/adoption` | `POST /api/v1/admin/update-groups/{id}/adoption/enumerate` | `UpdateAdoptionDay` | days, byPlatform; filters.days is 1 to 90, default 14 |
| `GET /api/v1/admin/update-groups/{id}/adoption/binary-versions` | `POST /api/v1/admin/update-groups/{id}/adoption/binary-versions/enumerate` | `UpdateCountRow` | None |

Jobs enumerate in SQL and remain reachable beyond 500 definitions. Latest-run lookup is restricted to the returned page. Job definitions support persistent metadata through `/api/v1/metadata/JobDefinition/{id}`. AI catalog dates represent immutable declarations, independent of edits to the associated configuration. Adoption binary-version and signing-key install counts enumerate SQL groups, with exact distinct-group totals and no top-ten truncation.

| `GET /api/v1/admin/metrics` | `POST /api/v1/admin/metrics/enumerate` | `EnumerationResult<MetricDefinition>` | Standard query and collection filters |
| `GET /api/v1/admin/metrics/pages` | `POST /api/v1/admin/metrics/pages/enumerate` | `EnumerationResult<MetricPageDeclaration>` | Standard query and collection filters |
| `GET /api/v1/admin/metrics/workers` | `POST /api/v1/admin/metrics/workers/enumerate` | `EnumerationResult<WorkerPassRecord>` | Standard query and collection filters |
| `GET /api/v1/admin/metrics/annotations` | `POST /api/v1/admin/metrics/annotations/enumerate` | `EnumerationResult<MetricAnnotation>` | Standard query and collection filters |
| `GET /api/v1/admin/metrics/engagement` | `POST /api/v1/admin/metrics/engagement/enumerate` | `EnumerationResult<LegacyMetricRow>` | Standard query and collection filters |
| `GET /api/v1/admin/metrics/forums` | `POST /api/v1/admin/metrics/forums/enumerate` | `EnumerationResult<LegacyMetricRow>` | Standard query and collection filters |
| `GET /api/v1/admin/directory/metrics` | `POST /api/v1/admin/directory/metrics/enumerate` | `EnumerationResult<LegacyMetricRow>` | Standard query and collection filters |
| `GET /api/v1/admin/metrics/support` | `POST /api/v1/admin/metrics/support/enumerate` | `EnumerationResult<LegacyMetricRow>` | Standard query and collection filters |
| `GET /api/v1/request-history/summary` | `POST /api/v1/request-history/summary/enumerate` | `EnumerationResult<RequestHistoryBucket>` | Standard query and collection filters |
| `GET /api/v1/admin/ai/analytics/timeseries` | `POST /api/v1/admin/ai/analytics/timeseries/enumerate` | `EnumerationResult<AiTimeseriesPoint>` | Standard query and collection filters |
| `GET /api/v1/admin/usage/summary` | `POST /api/v1/admin/usage/summary/enumerate` | `EnumerationResult<UsageObservationRow>` | Standard query and collection filters |
| `GET /api/v1/platform/properties/{propertyId}/usage/summary` | `POST /api/v1/platform/properties/{propertyId}/usage/summary/enumerate` | `EnumerationResult<UsageObservationRow>` | Standard query and collection filters |

Daily engagement/forum/directory/support collections return `{ id, series, date, value, labels, tags }` observations; `series` identifies the former daily property (for example `postsByDay`). Scalar totals remain at the root. Request-history summary returns buckets in `objects` plus `totalCount`, `totalSuccess`, `totalFailure`, `averageDurationMs`; AI timeseries returns timestamp/value observations plus metric/granularity. Usage summaries return meter/bucket observation rows, with calendar, reconciliation and completion context at the root. No top-level series arrays bypass paging.

| GET collection | Canonical POST | Objects | Context |
|---|---|---|---|
| `GET /api/v1/admin/metrics/events` | `POST /api/v1/admin/metrics/events/enumerate` | `LegacyMetricRow` | Scalar totals and bounded category preview |
| `GET /api/v1/admin/metrics/library` | `POST /api/v1/admin/metrics/library/enumerate` | `LegacyMetricRow` | Scalar totals |
| `GET /api/v1/admin/directory/settings` | `POST /api/v1/admin/directory/settings/enumerate` | `PropertySettingField` | config, geocoderConfigured |

| GET collection | Canonical POST | Objects | Selection |
|---|---|---|---|
| `GET /api/v1/directory/geocode` | `POST /api/v1/directory/geocode/enumerate` | `GeocodeCandidateView` | filters.query; finite top-five provider shortlist, not a global place count |
| `GET /api/v1/admin/ai/assistants/{id}/knowledge/preview` | `POST /api/v1/admin/ai/assistants/{id}/knowledge/preview/enumerate` | `KnowledgePreviewItem` | filters.query, optional filters.asUserId; finite context shortlist with totalCandidates/mode context |

### Optional lifecycle settings

`support.deleteOnAccountDeletion` (boolean, default false, live): remove requester tickets on account deletion, including private notes and attached media, except staff-only `staff:retain-legal` and `staff:retain-billing` holds. Operators maintain and review the legal/billing basis of each hold.

`signupCodes.cancelQueuedOnRefund` (boolean, default false, live): when purchase processing receives normalized status `Refunded`, revoke that member's future free code redemptions and entitlements. Active independent grants and discount redemptions remain. Refunds handled outside this status flow require the operator to revoke queued benefits through the existing redemption revoke route. Both settings are available through the existing settings API and admin settings screen.

# Phase 3 Contract: Forums

This is the binding contract between the backend, the website, and the mobile app (phone and tablet) for Phase 3. Every team codes against it exactly. It is an engine contract: TrellisKit implements it for every property (`docs/TRELLISKIT.md`). Property-specific values (forum names, reactions, profile labels, domains) come from the property configuration and the admin console; examples use the neutral sample property (Example Community, `example.test`). When the implementation is complete, `REST_API.md` and the Postman collection are updated to match, and this file stays as the design record. Plan references: the property's plan of record, sections 4.3, 4.5, 4.6, 4.11, 6.3, 7.1, 11.

Phase 3 builds on, and does not repeat:
- `docs/PHASE1_CONTRACT.md` (identity, roles, flags, audit).
- `docs/PHASE2_CONTRACT.md` (body format, mentions, media pipeline, reactions, threaded comments, notifications, push, reports, moderation actions, word filter, blocks, rate limit conventions, data write rules). Its section 19 decisions apply to forums unless this document says otherwise.
- `docs/ENUMERATION_AND_METADATA.md` (binding: `EnumerationQuery` in and `EnumerationResult<T>` out for every list, labels and tags on every object, no unbounded lists, label and tag filters in every client search surface). Where any example in this file looks like a plain list, the enumeration specification wins.
- `docs/TRELLISKIT.md` section 7 (access models and `audience`), section 10 (multi-property: every table has `propertyid`, the property is resolved per request), and section 11 (profile fields on cards).

Phase 3 is built after the multi-property, profile field, and access model milestones, so everything here is property-scoped and audience-aware from the first line of code.

Conventions are unchanged: routes under `/api/v1`, camelCase JSON, string enums, ISO 8601 UTC timestamps, errors as `{ "error", "message", "fieldErrors" }`, `X-Client` and `X-Client-Version` on every app request, `X-TK-Property` and `X-TK-App` from mobile and tablet builds, access tokens as `Authorization: Bearer <accessToken>`, `429` always with `Retry-After`.

---

## 0. New conventions in Phase 3

### 0.1 Target types

Phase 2 reserved `Thread`. Phase 3 activates it and adds the rest:

| `targetType` | Id prefix | URL segment | Notes |
|---|---|---|---|
| `Forum` | `frm_` | `forums` | Labels, tags, follows, moderator scope |
| `Thread` | `thr_` | `threads` | Reactions, comments, reports, moderation, notifications, follows |
| `ThreadRevision` | `thv_` | | Edit history entries |
| `Poll` | `pol_` | `polls` | One per Poll thread |
| `PollOption` | `pop_` | | Child of a poll |
| `PollVote` | `pov_` | | One row per voter per chosen option |
| `ForumFollow` | `ffw_` | | A member following a forum |
| `ThreadFollow` | `tfw_` | | A member following a thread |
| `ForumMute` | `fmu_` | | A moderator's time-boxed mute in one forum |

Comments on threads are Phase 2 `Comment` rows with `targetType: "Thread"`. Clients keep tolerating unknown target types (Phase 2 section 0.3).

### 0.2 The "one conversation" rule (comment target)

Every feed card and every thread card has a `commentTarget`. Clients always load comments, post comments, react, and report the conversation against `commentTarget`, never against the card's own id. This is what lets a promoted thread and its feed card, or a feed post and its forum discussion thread, share exactly one comment tree and one reaction set (section 8).

### 0.3 Error codes added in Phase 3

| Status | `error` | When |
|---|---|---|
| 400 | `ThreadTypeNotAllowed` | The forum's `allowedThreadTypes` does not include the requested type (create or move) |
| 400 | `PollInvalid` | Poll payload invalid (option count, duplicate labels, `maxChoices`, close date); `fieldErrors` names the field |
| 400 | `FeedbackVideoRequired` | A `Feedback` thread without a video attachment |
| 400 | `InvalidAcceptedAnswer` | The comment is not a visible comment of this thread |
| 403 | `ForumMuted` | The caller is muted in this forum (`message` includes the end time) |
| 403 | `ThreadCreationRestricted` | The forum's `threadCreation` excludes the caller |
| 403 | `PromotionNotAllowed` | Caller lacks `SystemAdmin` and `flags.canPromoteToFeed` |
| 404 | `NotFound` | Unknown, deleted, or not visible to the caller (never 403, so ids do not leak), including forums whose `audience` the caller does not meet |
| 409 | `ThreadLocked` | Comment, reply, or vote on a locked thread |
| 409 | `ForumArchived` | Create a thread in, or move a thread into, an archived forum |
| 409 | `ForumNotEmpty` | Delete a forum that still has threads that are not deleted (archive it instead) |
| 409 | `PinLimitReached` | More than `forums.maxPinnedPerForum` pinned threads in a forum (same code as the feed) |
| 409 | `AlreadyPromoted` | The thread already has an active feed card, or the feed post already has a discussion thread |
| 409 | `PollClosed` | Vote on a closed poll |
| 409 | `PollVoteLocked` | Change or remove a vote when `allowVoteChange` is false |
| 409 | `PollOptionsLocked` | Edit poll options after the first vote |
| 409 | `FeedbackLimitReached` | The member already has `forums.maxOpenFeedbackPerMember` open Feedback threads |
| 409 | `SlugInUse` | Forum slug is not unique in the property |
| 503 | `FeatureDisabled` | The `forums` kill switch is on for this caller (`LIVE_UPDATES_CONTRACT.md` section 13) |

Phase 2 codes (`Muted`, `PostingNotAllowed`, `MediaNotAllowed`, `VideoUploadNotAllowed`, `ContentFiltered`, `InteractionBlocked`, `CommentsDisabled`, `ParentUnavailable`, `MediaNotAttachable`, `MediaLimitExceeded`, `RateLimited`) apply unchanged to forum writes.

### 0.4 Access model and feature gating

- The property feature key is `forums`. `property.json` `features.forums` (absent means `true`) turns the whole phase on or off; when off every forum route returns `404 NotAvailable` and clients hide the Forums tab and admin pages.
- In `PartiallyGated` properties, `forums` may be listed in `access.gatedFeatures`. Then reading and writing forums requires an active membership; otherwise any signed-in account may read and write, subject to each object's `audience`.
- Every `Forum` and `Thread` has `audience` (`Public`, `SignedIn`, `Members`). A thread's audience can never be broader than its forum's (`400 ValidationFailed` with `fieldErrors.audience`); moving a thread into a narrower forum narrows the thread. New forums default to `access.defaultAudience`; new threads default to their forum's audience.
- `Public` forums and threads are readable signed out only when `access.anonymousBrowsing` is true (all models) or, in `FullyGated`, as public samples (section 10). Anonymous callers never see `viewer` write capabilities, poll voting, or follow controls.
- One engine authorization component evaluates (model, feature `forums`, forum audience, thread audience, caller state) for every route below. Routes in this document state the role or flag requirement on top of that.

---

## 1. Roles, permissions, and capability matrix

### 1.1 Roles

Phase 1 already defines the `Moderator` role with `resourceType: "Forum"` and `resourceId` (a `frm_` id or `*` for every forum). Phase 3 gives it meaning:

| Holder | Scope |
|---|---|
| `SystemAdmin` (and `PlatformAdmin` in multi-property mode) | Every forum, thread, and comment |
| `Moderator` with `resourceId: "*"` | Every forum, including forums created later |
| `Moderator` with `resourceId: "frm_..."` | That forum only; a member may hold several assignments |
| `FeedModerator` | No forum rights. Promoted feed cards are moderated through their thread (section 8.4) |

`isStaff` on `UserSummary` (Phase 2 section 2) is true for every `Moderator` holder, whatever the scope. Members cannot block staff (`400 CannotBlockStaff`, unchanged).

### 1.2 Per-user flags

Unchanged from Phases 1 and 2, now applied to forums: `canPost`, `canPostMedia`, `canUploadVideo`, `isMuted`/`mutedUntilUtc`. `canPromoteToFeed` (Phase 1, default false) is the right to promote threads to the feed and to open feed posts for discussion (section 8). `canPostToFeed` has no forum meaning.

A **forum mute** (section 11.3) is narrower than the global mute: the member reads everything but cannot create threads, comment, react, or vote in that forum (`403 ForumMuted`).

### 1.3 Capability matrix

"Moderator in scope" means `SystemAdmin`, or a `Moderator` whose assignment covers the thread's forum. Staff content protection from Phase 2 decision 12 carries over: a Moderator never hides, removes, locks, moves, or edits the metadata of content authored by a `SystemAdmin`; `SystemAdmin` may act on anything.

| Action | Who |
|---|---|
| List and read forums, threads, comments, polls | Anyone the access model and `audience` allow |
| Create a thread | `canPost`, not muted or forum-muted, forum not archived, and the forum's `threadCreation` allows the caller (`Members`: any member; `Moderators`: moderators in scope; `Admins`: `SystemAdmin`) |
| Create a Feedback thread | Same, plus `canUploadVideo`, a video attachment, and under `forums.maxOpenFeedbackPerMember` open Feedback threads |
| Edit own thread (title, body, attachments, labels, tags) | Author with `canPost`, any time, any state (revision history kept, section 4.6). Poll options only before the first vote |
| Delete own thread | Author, any time |
| Edit another member's thread | Nobody edits other members' words. Moderators in scope may change `title` (for clarity), `labels`, `tags`, and `type` between `Discussion` and `Question`; every change is a revision and an audited moderation action |
| Pin, unpin, lock, unlock | Moderator in scope |
| Move | Moderator in scope of both the source and the destination forum |
| Accept an answer (Question) | Thread author, or moderator in scope |
| Set Feedback review status, choose the review comment | Moderator in scope (the reviewers) |
| Close a poll early | Thread author, or moderator in scope |
| Vote | Any member who can read the thread, not muted or forum-muted, thread not locked, poll open |
| Comment, reply, react, report, block | Phase 2 rules, plus `409 ThreadLocked` on locked threads (reactions and reports still allowed on locked threads) |
| Hide, unhide, remove, restore threads and thread comments | Moderator in scope |
| Forum mute and unmute | Moderator in scope (mute duration at most `forums.maxForumMuteDays`) |
| Promote a thread to the feed, unpromote | `SystemAdmin`, or a member with `canPromoteToFeed` who can read the thread |
| Open a feed post for discussion in a forum (cross-post) | `SystemAdmin`, or a member with `canPromoteToFeed`; the destination forum must allow `Discussion` |
| Follow and unfollow forums and threads | Any signed-in caller who can read them |
| Create, edit, order, archive, delete forums; assign forum moderators | `SystemAdmin` |
| Trending queue, forum metrics | `SystemAdmin`; Moderators see the trending queue filtered to their scope |

### 1.4 Viewer capabilities

Every `Thread` payload carries `viewer`, computed for the caller (clients drive menus from it and never re-derive permissions):

```json
{
  "canEdit": true, "canDelete": true, "canComment": true, "canReact": true, "canReport": false,
  "canModerate": false, "canPin": false, "canLock": false, "canMove": false,
  "canAcceptAnswer": true, "canVote": true, "canClosePoll": true, "canSetFeedbackStatus": false,
  "canPromote": false, "canFollow": true, "canAttachMedia": true, "canAttachVideo": true
}
```

`Forum` payloads carry `viewer: { canCreateThread, canFollow, canModerate, allowedThreadTypes }` where `allowedThreadTypes` is the forum's list filtered by the caller's rights (for example, `Feedback` is dropped when the caller lacks `canUploadVideo`).

---

## 2. Shared shapes

### 2.1 Forum

```json
{
  "id": "frm_...",
  "slug": "general",
  "name": "General",
  "description": "Anything about the community.",
  "guidelines": "Be kind. Search before asking.",
  "icon": { "emoji": "💬", "mediaId": null, "imageUrl": null },
  "color": "#1F5FBF",
  "section": "Community",
  "sortOrder": 10,
  "audience": "Members",
  "threadCreation": "Members",
  "allowedThreadTypes": ["Discussion", "Question", "Poll", "Feedback"],
  "defaultOrdering": "Active",
  "allowMedia": true,
  "allowVideo": true,
  "isArchived": false,
  "threadCount": 412,
  "commentCount": 5210,
  "lastActivityUtc": "2026-11-20T18:02:00Z",
  "lastThread": { "id": "thr_...", "title": "Weekly open thread", "author": { "...": "UserSummary" }, "lastActivityUtc": "2026-11-20T18:02:00Z" },
  "isFollowed": false,
  "unreadThreadCount": 3,
  "labels": [],
  "tags": {},
  "viewer": { "canCreateThread": true, "canFollow": true, "canModerate": false, "allowedThreadTypes": ["Discussion", "Question", "Poll", "Feedback"] },
  "createdUtc": "2026-11-15T00:00:00Z",
  "updatedUtc": "2026-11-15T00:00:00Z"
}
```

| Field | Rule |
|---|---|
| `slug` | 2 to 60 of `a-z 0-9 -`, unique per property case-insensitively; changeable (clients link by id; slugs are for readable web URLs and redirect on change) |
| `name` | 2 to 60 characters; `description` up to 500; `guidelines` up to 5000 (Markdown subset of Phase 2 section 3 for posts) |
| `icon` | Either an `emoji` (1 grapheme) or an image `mediaId` (Ready image owned by an admin); `imageUrl` is the `thumb` variant |
| `color` | `#RRGGBB` or null |
| `section` | Optional grouping header (0 to 60 characters). Clients group forums by `section` in `sortOrder` order |
| `threadCreation` | `Members`, `Moderators`, `Admins` |
| `allowedThreadTypes` | Non-empty subset of `Discussion`, `Question`, `Poll`, `Feedback` (fixed bound of 4) |
| `defaultOrdering` | One of the thread orderings in section 4.2; clients use it when the member has not chosen one |
| `allowMedia`, `allowVideo` | Forum-level media switches on top of the member's own flags |
| `unreadThreadCount` | Threads with activity since the caller last opened the forum (section 9.4), capped at 99; null for anonymous callers |
| `lastThread` | Bounded single preview |

### 2.2 Thread

```json
{
  "id": "thr_...",
  "forum": { "id": "frm_...", "slug": "general", "name": "General" },
  "kind": "Original",
  "type": "Question",
  "author": { "...": "UserSummary" },
  "title": "How do I reset my profile photo?",
  "body": "I uploaded the wrong image. Thanks @alexchen for the tip earlier.",
  "excerpt": "I uploaded the wrong image. Thanks @alexchen for the tip earlier.",
  "mentions": [ { "userId": "usr_...", "handle": "alexchen", "displayName": "Alex Chen" } ],
  "attachments": [ { "...": "Media" } ],
  "linkPreview": null,
  "audience": "Members",
  "state": "Published",
  "isPinned": false,
  "pinnedUtc": null,
  "isLocked": false,
  "lockedUtc": null,
  "lockReason": null,
  "acceptedAnswer": { "...": "Comment without children, or null" },
  "poll": null,
  "feedback": null,
  "commentTarget": { "type": "Thread", "id": "thr_..." },
  "source": null,
  "promotion": null,
  "commentCount": 6,
  "uniqueCommenterCount": 4,
  "reactionCounts": { "like": 3 },
  "reactionTotal": 3,
  "myReactions": [],
  "trendingScore": 1.84,
  "lastActivityUtc": "2026-11-20T18:02:00Z",
  "lastCommentUtc": "2026-11-20T18:02:00Z",
  "isFollowed": true,
  "isUnread": false,
  "editedUtc": null,
  "revisionCount": 0,
  "moderation": null,
  "labels": ["account"],
  "tags": { "platform": "ios" },
  "viewer": { "...": "section 1.4" },
  "publishedUtc": "2026-11-20T17:30:00Z",
  "createdUtc": "2026-11-20T17:30:00Z",
  "updatedUtc": "2026-11-20T18:02:00Z"
}
```

| Field | Rule |
|---|---|
| `kind` | `Original` (authored in the forum) or `LinkedFeedPost` (a discussion thread opened from a feed post, section 8.2). Clients skip kinds they do not know |
| `type` | `Discussion`, `Question`, `Poll`, `Feedback` |
| `title` | Required, 3 to 140 characters after normalization |
| `body` | Phase 2 section 3 Markdown subset with post features (headings allowed). Required for `Discussion` and `Question` (1 to `forums.maxBodyLength`), optional for `Poll` and `Feedback` when an attachment or poll is present |
| `attachments` | Ordered Media, up to `forums.maxAttachments` (10), at most `forums.maxVideosPerThread` (1) video |
| `state` | `Processing` (an attachment is still Processing; publishes automatically when every attachment is Ready, as feed posts do), `Published`, `Hidden`, `Removed`. Deleted threads return `404`. There is no scheduling in forums |
| `acceptedAnswer` | `Question` only: the accepted comment (bounded single object) or null |
| `poll` | `Poll` only: section 6 |
| `feedback` | `Feedback` only: section 7 |
| `commentTarget` | `{ type: "Thread", id }` for `Original`; `{ type: "FeedPost", id }` for `LinkedFeedPost` |
| `source` | For `LinkedFeedPost`: `{ type: "FeedPost", id, title, author: UserSummary }`, else null |
| `promotion` | When an active feed card exists: `{ feedPostId, promotedBy: UserSummary, promotedUtc }`, else null |
| `commentCount`, `uniqueCommenterCount`, `reactionCounts`, `reactionTotal`, `myReactions` | Of the `commentTarget` (so a linked thread shows the feed post's numbers) |
| `trendingScore` | Section 5; returned to everyone (it drives the visible Hot ordering) |
| `lastActivityUtc` | Max of `publishedUtc`, the last visible comment, the last poll vote (votes count only once per voter per hour), and the last accepted-answer or feedback status change |
| `isUnread` | True when `lastActivityUtc` is after the caller's last read of this thread (section 9.4); false for anonymous callers |
| `editedUtc`, `revisionCount` | Phase 2 rules: silent edits within `content.editGraceSeconds`, revisions after |
| `moderation` | Phase 2 shape, for the author and moderators in scope when Hidden or Removed; null otherwise |

Who sees non-Published threads: the author (all states), moderators in scope (Hidden, Removed), `SystemAdmin` (all). Everyone else gets `404`.

### 2.3 ThreadSummary (list item)

Thread list enumerations return `ThreadSummary`: the Thread without `body`, `mentions`, `linkPreview`, `acceptedAnswer` (replaced by `hasAcceptedAnswer: true|false`), and `attachments` (replaced by `attachmentPreview`: at most the first 4 Media and `attachmentCount`), and with `poll` reduced to `{ id, isClosed, closesUtc, totalVoters, hasVoted }` and `feedback` to `{ status, reviewer }`, where `reviewer` is the `UserSummary` of the moderator who started or finished the review (`InReview`, `Reviewed`, or a `Closed` request that was reviewed), or null while nobody has picked the request up or when the reviewer's account is gone. `PublicThread` carries the same `feedback`. Search results add `searchSnippet` (section 9.1).

---

## 3. Forums

### 3.1 Member routes

| Method and path | Body | Success | Errors |
|---|---|---|---|
| `POST /api/v1/forums/enumerate` (and `GET /api/v1/forums`) | `EnumerationQuery`. Orderings: `SortOrderAscending` (default), `NameAscending`, `ActivityDescending`, `CreatedDescending`, `CreatedAscending`. `search` over name and description. `filters`: `section`, `followedOnly` (bool), `includeArchived` (bool, default false), `audience` | `200 EnumerationResult<Forum>` | `400 ValidationFailed` |
| `GET /api/v1/forums/{idOrSlug}` | | `200 Forum` | `404 NotFound` |
| `PUT /api/v1/forums/{id}/follow` | `{ "level": "NewThreads" }` (the only level in Phase 3) | `200 Forum` (`isFollowed: true`) | `404` |
| `DELETE /api/v1/forums/{id}/follow` | | `200 Forum` | |
| `POST /api/v1/forums/{id}/read` | none | `204`; marks every thread in the forum read for the caller (section 9.4) | |

A property has at most `forums.maxForums` (200) forums, so the forum enumeration's default page size of 100 normally returns them all in one page; clients still follow `continuationToken`.

### 3.2 Admin routes (`SystemAdmin`)

| Method and path | Body | Success | Errors |
|---|---|---|---|
| `POST /api/v1/admin/forums/enumerate` | `EnumerationQuery` (admin maximum); same filters plus `isArchived` | `200 EnumerationResult<AdminForum>` (Forum plus `openReportCount`, `moderatorCount`, `pinnedCount`, `threadsLast30d`) | |
| `POST /api/v1/admin/forums` | `{ name, slug?, description?, guidelines?, icon?, color?, section?, audience?, threadCreation?, allowedThreadTypes?, defaultOrdering?, allowMedia?, allowVideo?, labels?, tags? }`. Omitted `slug` is derived from `name`; the new forum goes last in `sortOrder` | `201 Forum` | `400 ValidationFailed`, `409 SlugInUse`, `409 ForumLimitReached` |
| `GET /api/v1/admin/forums/{id}` | | `200 AdminForum` plus `moderators: { objects (max 10), totalRecords, enumerate }` | |
| `PUT /api/v1/admin/forums/{id}` | Any create field. Narrowing `audience` narrows every thread in the forum in the same batch. Removing a type from `allowedThreadTypes` does not change existing threads | `200 Forum` | `400`, `409 SlugInUse` |
| `PUT /api/v1/admin/forums/order` | `{ "forumIds": ["frm_a", "frm_b"] }`: every non-archived forum id exactly once (bounded by `forums.maxForums`); assigns `sortOrder` 10, 20, 30, ... | `200 { "updated": 12 }` | `400 ValidationFailed` (missing, duplicate, or unknown ids) |
| `POST /api/v1/admin/forums/{id}/archive`, `.../unarchive` | `{ reason? }` | `200 Forum`. Archived forums are read-only: no new threads, comments, votes, or moves in; reactions and reports still work | |
| `DELETE /api/v1/admin/forums/{id}` | | `204` (only when it has no threads that are not deleted: Published, Processing, Hidden, and Removed threads block it; deleted threads, purged or not, do not, and the purge worker still erases them on its normal schedule) | `409 ForumNotEmpty` |
| `POST /api/v1/admin/forums/{id}/moderators/enumerate` | `EnumerationQuery` | `200 EnumerationResult<ModeratorRow>` (Phase 2 section 10.4 row shape, filtered to assignments covering this forum, with `scope: "Forum" \| "AllForums"`) | |

Audit actions: `forum.create`, `forum.update`, `forum.order`, `forum.archive`, `forum.unarchive`, `forum.delete`.

---

## 4. Threads

### 4.1 Read

| Method and path | Success |
|---|---|
| `GET /api/v1/threads/{id}` | `200 Thread`. Also records the caller's read position (section 9.4) |
| `POST /api/v1/threads/{id}/revisions/enumerate` | `200 EnumerationResult<{ id, revision, title, body, type, attachmentSummary, editedBy: UserSummary, editedByRole: "Author" \| "Moderator", createdUtc }>`, newest first (member maximum). Visibility follows the thread |

### 4.2 List threads in a forum

`POST /api/v1/forums/{id}/threads/enumerate` (and `GET /api/v1/forums/{id}/threads`) with an `EnumerationQuery`. Returns `200 EnumerationResult<ThreadSummary>`.

| Ordering | Sort | Notes |
|---|---|---|
| `Active` (typical default) | `lastActivityUtc` desc, `id` desc | |
| `Hot` | `trendingScore` desc, `lastActivityUtc` desc, `id` desc | Section 5. Keyset over (`trendingscore`, `lastactivityutc`, `id`); because scores change every recompute, an item can repeat across pages and clients de-duplicate by `id` (Phase 2 rule for mutable sorts) |
| `CreatedDescending`, `CreatedAscending` | `publishedUtc` | |
| `Top` | `reactionTotal + 2 * commentCount` desc | Over the `createdAfterUtc` window when given |
| `Relevance` | Text rank | Only valid with `search` (section 9.1) |

`filters` (unknown keys return `400 ValidationFailed`):

| Filter | Values |
|---|---|
| `type` | One or more of `Discussion`, `Question`, `Poll`, `Feedback` |
| `answered` | `true` (Questions with an accepted answer), `false` (Questions without one) |
| `unanswered` | `true`: Questions with no accepted answer and zero visible comments |
| `feedbackStatus` | One or more of `Open`, `InReview`, `Reviewed`, `Closed` |
| `pollOpen` | `true` or `false` |
| `authorUserId` | `usr_...` |
| `followedOnly`, `unreadOnly` | `true` |
| `hasMedia`, `hasVideo` | `true` |
| `audience` | One or more audience values |
| `includePinned` | Default `true`: with no `search` and ordering `Active` or `Hot`, pinned Published threads sort first by `pinnedUtc` descending, followed by the normally ordered unpinned threads. Both groups share one keyset and count toward `maxResults`; pins can occupy several pages. No page exceeds its effective cap. Disabling pin priority uses the ordinary ordering |

Threads by authors the caller blocked are omitted. Labels, tags, `search`, and the time window filters apply per the enumeration specification.

### 4.3 List threads across forums

`POST /api/v1/threads/enumerate` (and `GET /api/v1/threads`) takes the same query plus `filters.forumIds` (up to 50). It powers the "All forums" view, the member's own threads (`authorUserId` = self, where the author also sees `Processing`, `Hidden`, and `Removed` threads with `filters.state`), "Following" (`followedOnly`), and search (section 9.1). Pinned threads are not prepended here.

### 4.4 Create

`POST /api/v1/forums/{id}/threads`

```json
{
  "type": "Poll",
  "title": "Which night works for the next meetup?",
  "body": "Vote below. Results show after you vote.",
  "mediaIds": [],
  "showLinkPreview": true,
  "audience": null,
  "poll": {
    "options": [ { "label": "Tuesday" }, { "label": "Thursday" }, { "label": "Saturday" } ],
    "multipleChoice": false,
    "maxChoices": null,
    "closesUtc": "2026-12-01T00:00:00Z",
    "resultsVisibility": "AfterVote",
    "allowVoteChange": true
  },
  "feedback": null,
  "labels": ["meetup"],
  "tags": {}
}
```

| Field | Rule |
|---|---|
| `type` | Required; must be in the forum's `allowedThreadTypes` (`400 ThreadTypeNotAllowed`) |
| `title`, `body` | Section 2.2 |
| `mediaIds` | Phase 2 ownership and state rules; also the forum's `allowMedia` and `allowVideo` (`403 MediaNotAllowed` or `403 VideoUploadNotAllowed`) |
| `audience` | Optional, not broader than the forum's |
| `poll` | Required for `Poll`, rejected for other types (section 6.1) |
| `feedback` | Optional for `Feedback`: `{ "focus": "Text up to 500 characters" }` (section 7). `Feedback` requires at least one video in `mediaIds` (`400 FeedbackVideoRequired`) |
| `labels`, `tags` | Enumeration specification rules; members cannot send `staff:` metadata |

The word filter runs on `title`, `body`, poll option labels, `feedback.focus`, attachment `altText`, and member labels and tag values. Mentions resolve and notify on publish (Phase 2 section 3.3). The author automatically follows the new thread.

Success `201 Thread` (state `Processing` or `Published`). Errors: `400 ValidationFailed`, `400 ThreadTypeNotAllowed`, `400 PollInvalid`, `400 FeedbackVideoRequired`, `400 MediaLimitExceeded`, `400 ContentFiltered`, `403 ThreadCreationRestricted`, `403 PostingNotAllowed`, `403 MediaNotAllowed`, `403 VideoUploadNotAllowed`, `403 Muted`, `403 ForumMuted`, `404 NotFound`, `409 ForumArchived`, `409 FeedbackLimitReached`, `409 MediaNotAttachable`, `429 RateLimited`.

Publishing a thread (immediately, or when its last attachment becomes Ready) sets `publishedUtc` and `lastActivityUtc`, writes `thread.publish` to the audit log, sends mention notifications, and starts the `NewThreadInForum` fan-out to the forum's followers (section 9.3).

### 4.5 Edit

`PUT /api/v1/threads/{id}` with any of `title`, `body`, `mediaIds` (replaces the list), `showLinkPreview`, `audience`, `poll` (section 6.4), `feedback.focus`, `labels`, `tags`, and, for moderators in scope only, `type` (`Discussion` and `Question` swap; other type changes are not allowed). Returns `200 Thread`. Phase 2 edit rules apply: always editable by the author with `canPost`, silent within the grace period, revisions after, word filter on every edit, newly added mentions notify. Moderator edits are limited to section 1.3 and are recorded as a revision with `editedByRole: "Moderator"` plus a moderation action `EditMetadata`. Errors as create, plus `403 Forbidden` for fields the caller may not change.

### 4.6 Delete

`DELETE /api/v1/threads/{id}` (author or `SystemAdmin`) returns `204`. Soft delete: the thread disappears from every list, its comments become unreachable, its pin is cleared, an active feed promotion is ended (the feed card disappears), follows are deleted, and media is deleted after `moderation.retainRemovedDays`. A `LinkedFeedPost` thread can be deleted without affecting the feed post or its comments. Audited as `thread.delete`.

### 4.7 Pin, lock, move

| Method and path | Body | Success | Errors |
|---|---|---|---|
| `POST /api/v1/threads/{id}/pin` | none | `200 Thread`. Published threads only; idempotent | `409 PinLimitReached` |
| `POST /api/v1/threads/{id}/unpin` | none | `200 Thread` | |
| `POST /api/v1/threads/{id}/lock` | `{ "reason": "Resolved; see the accepted answer." }` (0 to 500) | `200 Thread`. Blocks new comments, replies, and votes (`409 ThreadLocked`); edits by the author, reactions, reports, follows, and moderation keep working. Idempotent | |
| `POST /api/v1/threads/{id}/unlock` | none | `200 Thread` | |
| `POST /api/v1/threads/{id}/move` | `{ "forumId": "frm_...", "reason": "Better fit", "notifyAuthor": true }` | `200 Thread` | `400 ThreadTypeNotAllowed`, `403 Forbidden` (no scope on the destination), `409 ForumArchived` |

Moving updates the thread's forum, narrows its audience when needed, unpins it, recomputes both forums' counters, moves Open reports' scope to the destination forum (so the destination's moderators see them), and keeps comments, reactions, follows, promotion, and the id (thread links are id based, so nothing breaks). `ThreadMoved` notifies the author when `notifyAuthor` (default true). Pin, unpin, lock, unlock, and move each write a `moderationactions` row (actions `Pin`, `Unpin`, `Lock`, `Unlock`, `Move` with `fromForumId` and `toForumId`) and an audit record (`thread.pin`, `thread.unpin`, `thread.lock`, `thread.unlock`, `thread.move`).

### 4.8 Accepted answer (Question)

| Method and path | Body | Success | Errors |
|---|---|---|---|
| `POST /api/v1/threads/{id}/accept-answer` | `{ "commentId": "cmt_..." }` | `200 Thread` with `acceptedAnswer` | `400 InvalidAcceptedAnswer` (not this thread's comment, not Visible, or the thread is not a Question), `403 Forbidden` |
| `DELETE /api/v1/threads/{id}/accept-answer` | | `200 Thread` (`acceptedAnswer: null`) | |

Any visible comment of the thread at any depth may be accepted, including the author's own (self-answers are allowed and do not notify). Accepting replaces a previous choice. The accepted comment carries `isAcceptedAnswer: true` in comment payloads; clients show it pinned above the comment tree with an "Accepted answer" badge and still render it in place in the tree. If the accepted comment is later deleted, hidden, or removed, `acceptedAnswer` becomes null and `hasAcceptedAnswer` false until it is restored. `AnswerAccepted` notifies the comment author. Audited as `thread.acceptAnswer` and `thread.clearAnswer`.

### 4.9 Reactions on threads

`PUT` and `DELETE /api/v1/threads/{id}/reactions/{key}` and `POST /api/v1/threads/{id}/reactions/enumerate` behave exactly like the feed routes (Phase 2 section 6) against the thread's `commentTarget`. For a `LinkedFeedPost` thread they act on the feed post. Reactions on a locked thread are allowed; on an archived forum's threads, allowed.

### 4.10 Comments on threads

The Phase 2 comment routes under the `threads` segment, in the enumeration form the Phase 2 implementation uses: `POST /api/v1/threads/{id}/comments/enumerate` (tree page; `sort`, `depth`, `childLimit` as in Phase 2 section 7.3), `POST /api/v1/threads/{id}/comments` (create), and the target-independent `/api/v1/comments/{id}...` routes. Differences for threads:

- Create fails with `409 ThreadLocked` on a locked thread, `409 ForumArchived` in an archived forum, and `403 ForumMuted` for a forum-muted caller.
- Comment media also obeys the forum's `allowMedia` and `allowVideo`.
- Tree pages of a Question include `acceptedAnswerId` at the top level of the response; every comment carries `isAcceptedAnswer`. In a Feedback thread the review comment carries `isFeedbackReview: true`.
- For a `LinkedFeedPost` thread, clients call the feed post's comment routes (`commentTarget`); the thread routes return `409` with `error: "UseCommentTarget"` and `message` naming the target, so a client bug cannot fork the conversation.
- Comment notifications follow section 9.2.

---

## 5. Trending and the Hot ordering

### 5.1 Score

```
score = (wR * reactions + wC * comments + wU * uniqueCommenters) / (ageHours + ageOffsetHours) ^ gravity
```

| Term | Meaning | Setting (default) |
|---|---|---|
| `reactions` | `reactionTotal` of the thread's `commentTarget` | `trending.reactionWeight` (1) |
| `comments` | Visible comments | `trending.commentWeight` (2) |
| `uniqueCommenters` | Distinct authors of visible comments, excluding the thread author | `trending.commenterWeight` (3) |
| `ageHours` | Hours since `publishedUtc` | |
| `ageOffsetHours` | Keeps brand new threads from dividing by zero | `trending.ageOffsetHours` (2) |
| `gravity` | How fast scores decay | `trending.gravity` (1.5) |

Only Published threads in non-archived forums score. Hidden, Removed, and deleted threads score 0. Comments and reactions by blocked or banned members still count (the score is global, not per viewer); content removed by moderators does not.

### 5.2 Recompute

`TrendingWorker` (singleton through `jobleases`) runs every `trending.intervalMinutes` (10). Per property, it recomputes threads with `lastActivityUtc` within `trending.windowDays` (7) in batches of `trending.batchSize` (500) by keyset over `id`, and sets every other non-zero score to 0 in one statement. Scores are stored as `DOUBLE PRECISION` (PostgreSQL) and `REAL` (SQLite) with `trendingcomputedutc`. Create, comment, and reaction writes do not recompute inline; new threads get an initial score at publish so they can appear in Hot immediately.

### 5.3 Trending queue (admin console)

`POST /api/v1/admin/trending/enumerate` (`SystemAdmin`; Moderators see threads in their scope) with `EnumerationQuery` (ordering `ScoreDescending` default, `filters`: `forumIds`, `type`, `promoted` true or false, `minScore`). Items: `{ thread: ThreadSummary, breakdown: { reactions, comments, uniqueCommenters, ageHours, numerator, denominator, score }, rank, computedUtc, promotion: { feedPostId, promotedUtc } | null }`. `breakdown.score` is the live score (numerator over denominator now); `thread.trendingScore` is the stored score the worker wrote at `computedUtc` and orders the list. The console page shows the table with the live score and its breakdown, the stored score labeled with its computed time ("Not computed yet" before the first pass), a "Promote" and "Unpromote" row action (section 8.1), and the current weights with a link to the Trending settings group.

---

## 6. Polls

### 6.1 Poll payload on create

| Field | Rule |
|---|---|
| `options` | 2 to `polls.maxOptions` (20) items of `{ label }`, each 1 to 140 characters, unique case-insensitively after normalization; order is display order |
| `multipleChoice` | Default false (single choice) |
| `maxChoices` | Multiple choice only: 2 to the option count, or null for "any number" |
| `closesUtc` | Optional; at least 5 minutes and at most `polls.maxDurationDays` (365) days in the future |
| `resultsVisibility` | `Always`, `AfterVote` (default), `AfterClose` |
| `allowVoteChange` | Default true |

The poll's question is the thread title; the body is optional context. Votes are anonymous to members (counts only); staff can see who voted only through the database export for abuse investigations, never in the API.

### 6.2 Poll shape

```json
{
  "id": "pol_...",
  "threadId": "thr_...",
  "multipleChoice": false,
  "maxChoices": null,
  "resultsVisibility": "AfterVote",
  "allowVoteChange": true,
  "closesUtc": "2026-12-01T00:00:00Z",
  "closedUtc": null,
  "isClosed": false,
  "totalVoters": 38,
  "resultsVisible": true,
  "myVote": ["pop_b"],
  "options": [
    { "id": "pop_a", "label": "Tuesday", "position": 1, "voteCount": 12, "percent": 31.6, "labels": [], "tags": {} },
    { "id": "pop_b", "label": "Thursday", "position": 2, "voteCount": 20, "percent": 52.6, "labels": [], "tags": {} },
    { "id": "pop_c", "label": "Saturday", "position": 3, "voteCount": 6, "percent": 15.8, "labels": [], "tags": {} }
  ],
  "labels": [],
  "tags": {}
}
```

`options` is bounded by `polls.maxOptions` (fixed bound, allowed by the enumeration specification section 2.4). When `resultsVisible` is false, `voteCount` and `percent` are null and `totalVoters` is still shown. `resultsVisible` is true for staff in scope and the thread author always; otherwise by `resultsVisibility` (`AfterVote`: after the caller voted or the poll closed). `percent` is of `totalVoters` (so multiple choice percentages can sum above 100), one decimal place.

### 6.3 Vote

| Method and path | Body | Success | Errors |
|---|---|---|---|
| `PUT /api/v1/polls/{id}/votes` | `{ "optionIds": ["pop_b"] }` | `200 Poll`. Replaces the caller's vote (idempotent for the same set) | `400 ValidationFailed` (unknown option, more than one for single choice, above `maxChoices`, empty), `403 Muted`, `403 ForumMuted`, `409 PollClosed`, `409 ThreadLocked`, `409 PollVoteLocked` (changing when `allowVoteChange` is false), `429 RateLimited` |
| `DELETE /api/v1/polls/{id}/votes` | | `200 Poll` | `409 PollClosed`, `409 PollVoteLocked` |
| `POST /api/v1/polls/{id}/close` | none | `200 Poll` (author or moderator in scope; idempotent) | `403 Forbidden` |

Counts are recomputed from `pollvotes` in the vote batch (Phase 2 write rules). A `PollWorker` (every minute) closes polls whose `closesUtc` passed, sets `closedUtc`, and sends `PollClosed` (section 9.3). Closed polls never reopen.

### 6.4 Editing a poll

Before the first vote the author may replace `options`, `multipleChoice`, `maxChoices`, `resultsVisibility`, and `allowVoteChange` through `PUT /api/v1/threads/{id}` with `poll`. After the first vote only `closesUtc` may change (later, or earlier but not in the past); other poll fields return `409 PollOptionsLocked`.

---

## 7. Feedback threads (video review requests)

A `Feedback` thread is a member's request for review of a video (for example a recorded performance). Any member who can read the thread may comment; moderators in scope act as the reviewers.

| Field | Values |
|---|---|
| `feedback.status` | `Open` (new), `InReview` (a reviewer picked it up), `Reviewed` (a review comment was chosen), `Closed` (withdrawn by the author, or closed by a reviewer) |
| `feedback.focus` | What the member wants reviewed, 0 to 500 characters |
| `feedback.reviewer` | `UserSummary` of the reviewer who set `InReview` or `Reviewed`, or null |
| `feedback.reviewCommentId` | The comment chosen as the review, or null |
| `feedback.statusUtc` | When the status last changed |

| Method and path | Body | Who | Success | Errors |
|---|---|---|---|---|
| `PUT /api/v1/threads/{id}/feedback` | `{ "status": "InReview" \| "Reviewed" \| "Closed" \| "Open", "reviewCommentId"? }` | Moderators in scope for every transition; the author may only set `Closed` (withdraw) | `200 Thread` | `400 ValidationFailed` (`Reviewed` requires a visible `reviewCommentId` of this thread), `403 Forbidden` |

Rules: a member may have at most `forums.maxOpenFeedbackPerMember` (3; 0 means unlimited) Feedback threads in `Open` or `InReview` across the property (`409 FeedbackLimitReached`). `Reviewed` notifies the author (`FeedbackReviewed`). The review comment is shown above the tree with a "Review" badge, like an accepted answer. Feedback threads appear in the moderation console's "Review requests" tab (section 11.5), ordered oldest `Open` first, so reviewers work a queue.

---

## 8. Forum to feed portability

### 8.1 Promote a thread to the feed

`POST /api/v1/threads/{id}/promote` (`SystemAdmin` or `canPromoteToFeed`)

```json
{ "title": null, "excerpt": null, "pin": false, "audience": null, "notifyMembers": true }
```

| Field | Rule |
|---|---|
| `title`, `excerpt` | Optional overrides for the card (title up to 140, excerpt up to 300); default to the thread's |
| `pin` | `SystemAdmin` only; subject to `feed.maxPinned` |
| `audience` | Default the thread's audience; may be narrower, never broader |
| `notifyMembers` | Default true: run the normal `NewFeedPost` fan-out and feed post email (Phase 2 sections 8.2 and 8.9) |

Success `201 FeedPost` with `kind: "PromotedThread"`, `source: { type: "Thread", id, forumId, forumName, title }`, `commentTarget: { type: "Thread", id: "thr_..." }`, and `promotion` set on the thread. Errors: `403 PromotionNotAllowed`, `404 NotFound`, `409 AlreadyPromoted`, `409 PinLimitReached`, `400 ValidationFailed` (the thread is not Published). `ThreadPromoted` notifies the thread author. Audited as `thread.promote`.

The feed card renders the thread title (or override), excerpt, the first attachments, a "From the {forumName} forum" badge linking to the thread, and live counts from the thread. Comments and reactions are not duplicated: the card's comment and reaction routes are the thread's.

### 8.2 Open a feed post for discussion in a forum (cross-post)

`POST /api/v1/feed/{id}/discuss` (`SystemAdmin` or `canPromoteToFeed`)

```json
{ "forumId": "frm_...", "title": null, "labels": [], "tags": {} }
```

Creates a `Thread` with `kind: "LinkedFeedPost"`, `type: "Discussion"`, `source: { type: "FeedPost", id }`, `commentTarget: { type: "FeedPost", id }`, author = the feed post's author, title = the override or the post title (or the first 140 characters of its excerpt), and no body of its own: thread payloads of this kind return the feed post's current `body`, `mentions`, `attachments`, and `linkPreview` (so edits to the post show in the forum). The feed post gains `discussion: { threadId, forumId, forumName }`. One discussion thread per feed post (`409 AlreadyPromoted`). Errors: `400 ThreadTypeNotAllowed` (the forum does not allow `Discussion`), `409 ForumArchived`, `403 Forbidden`. Audited as `feedPost.discuss`. The forum's followers receive `NewThreadInForum`.

### 8.3 Unpromote and unlink

| Method and path | Who | Effect |
|---|---|---|
| `POST /api/v1/threads/{id}/unpromote` | `SystemAdmin`, the member who promoted it, or a moderator in scope | Soft-deletes the promoted feed card (`feedPost.delete` semantics without touching the thread); the thread, its comments, and reactions are untouched. `200 Thread`. Audited as `thread.unpromote` |
| `POST /api/v1/feed/{id}/undiscuss` | `SystemAdmin` or the member who linked it | Deletes the linked thread; the feed post and its comments are untouched. `200 FeedPost`. Audited as `feedPost.undiscuss` |

### 8.4 State coupling

- The comment owner is always the original object. Hiding, removing, or deleting a thread hides the promoted card from the feed (the card returns with the thread on Unhide or Restore while the promotion is active). Hiding, removing, or deleting a feed post makes its linked thread return `404` to non-staff.
- Feed post lists skip promoted cards whose thread the caller cannot read (audience or blocks), so audience is never widened by promotion.
- Reports on a promoted card are filed against `commentTarget` (the thread), scoped to the forum; `FeedModerator` does not see them, forum moderators do. Reports on a linked thread's content are filed against the feed post and scoped to the Feed.
- Moving a promoted thread keeps the promotion and updates `source.forumId` and `forumName`.
- Notifications about comments always point at the original object's path; promoted cards deep link to `/threads/{id}`.

---

## 9. Search, following, read state, notifications

### 9.1 Forum search

`POST /api/v1/threads/enumerate` with `search` (1 to 200 characters) searches thread titles (weighted highest), bodies, poll option labels, and, when `filters.includeComments` is true (default true), visible comment bodies on threads. Results are threads (one row per thread even when several comments match), ordered by `Relevance` by default when `search` is present (any other ordering is allowed). Each item adds:

```json
"searchSnippet": { "field": "Comment", "commentId": "cmt_...", "text": "...try the reset option under Account...", "highlights": [ { "start": 7, "length": 5 } ] }
```

`text` is plain text (never HTML) cut to 200 characters around the first match; `highlights` are offsets into `text`. The same filters as 4.2 apply, plus `forumIds`.

Implementation: PostgreSQL `tsvector` columns (`searchvector` on `forumthreads`, maintained on write with `setweight(to_tsvector('simple', title), 'A') || setweight(to_tsvector('simple', body), 'B')`, and on `comments` for `targettype = 'Thread'`) with GIN indexes and `websearch_to_tsquery('simple', ...)`; SQLite FTS5 virtual tables `forumthreads_fts(title, body, content='')` and `threadcomments_fts(body)` maintained in the same write batches, queried with `MATCH` and `bm25()`. Prefix matching on the last term (`term:*` and `term*`). The `simple` configuration keeps the engine language-neutral; a property setting `forums.searchConfiguration` (PostgreSQL text search configuration name, default `simple`) can change it. Search respects audience, blocks, and state like every list; the query joins visibility before ranking and is bounded by `maxResults`.

The global `GET /api/v1/search?q=&types=` of the plan is not built in Phase 3; clients use the thread enumeration.

### 9.2 Following

| Object | How a member follows | What it sends |
|---|---|---|
| Thread | Explicitly (`PUT /api/v1/threads/{id}/follow`), automatically when they create it, and automatically on their first comment when `forums.autoFollowOnComment` (true) | `ThreadReply` for new visible comments by others |
| Forum | Explicitly (`PUT /api/v1/forums/{id}/follow`) | `NewThreadInForum` for new threads by others |

| Method and path | Success |
|---|---|
| `PUT /api/v1/threads/{id}/follow` | `200 { "isFollowed": true }` (idempotent). Following a linked thread follows the feed post's conversation |
| `DELETE /api/v1/threads/{id}/follow` | `200 { "isFollowed": false }`. Unfollowing your own thread is allowed and sticks (a later comment by you does not re-follow it) |
| `POST /api/v1/me/follows/enumerate` | `200 EnumerationResult<{ id, targetType: "Forum" \| "Thread", target: ForumSummary \| ThreadSummary, createdUtc, labels, tags }>`; `filters.targetType` |

A member may follow at most `forums.maxFollowsPerMember` (5000) threads; following beyond that removes the least recently active followed thread. Forum follows are bounded by the forum count.

### 9.3 Notification types and categories

New `type` values (the Phase 2 `ThreadReply` reservation is now active):

| `type` | `category` | Trigger | Recipient | Aggregated | `title` example | `path` |
|---|---|---|---|---|---|---|
| `ThreadReply` | `Following` | A visible comment on a thread you follow (including your own threads) | Followers except the comment author | Yes, per thread while unread (group key `ThreadReply:Thread:<id>`) | "Alex Chen and 3 others commented on How do I reset my profile photo?" | `/threads/{id}?comment={commentId}` |
| `NewThreadInForum` | `Forums` | A thread published in a forum you follow | Followers except the author | Yes, per forum while unread (`NewThreadInForum:Forum:<id>`) | "3 new threads in General" | `/forums/{forumId}` (single: `/threads/{id}`) |
| `AnswerAccepted` | `Forums` | Your comment was accepted | Comment author | No | "Your answer was accepted on How do I reset my profile photo?" | `/threads/{id}?comment={commentId}` |
| `FeedbackReviewed` | `Forums` | Your Feedback thread moved to `Reviewed` | Thread author | No | "Example Owner reviewed your video" | `/threads/{id}?comment={reviewCommentId}` |
| `PollClosed` | `Forums` | A poll you created or voted in closed | Author and voters (fan-out job) | No | "Poll closed: Which night works for the next meetup?" | `/threads/{id}` |
| `ThreadPromoted` | `Forums` | Your thread was promoted to the feed | Thread author | No | "Your thread was featured in the feed" | `/feed/{feedPostId}` |
| `ThreadMoved` | `Moderation` | A moderator moved your thread | Thread author | No | "A moderator moved your thread to Help" | `/threads/{id}` |

Existing types extend to threads: `PostComment` (top-level comment on your thread: title "commented on your thread"), `CommentReply`, `Mention`, `Reaction` (on your thread or thread comment), `ModerationAction` (your thread hidden, removed, restored, or locked), `ModerationOverdue` (to moderators in the forum's scope).

De-duplication (one notification per recipient per source event), in priority order: `CommentReply`, `PostComment`, `Mention`, `ThreadReply`. So the author of a thread receives `PostComment` for a top-level comment and never also `ThreadReply` for it; a follower who is mentioned receives `Mention` only. No notifications across blocks, and never to the actor.

New preference categories (added to `objects` from `GET /api/v1/me/notification-preferences` or `POST .../enumerate`; every cell on by default, Phase 2 decision 5):

| `category` | inApp | push | email | Editable |
|---|---|---|---|---|
| `Following` | true | true | false | inApp, push, email (opt-in, delivered as a digest) |
| `Forums` | true | true | false | inApp, push, email (opt-in, delivered as a digest) |

**Email for `Following` and `Forums` (resolved decision 9, as built).** Email is an opt-in channel per category, delivered as a digest rather than per event (the open question 9 recommendation: per-event email for aggregated reply and new-thread notifications would flood inboxes). For each member with the category's email turned on, a verified address, and an account that is neither deleted nor banned, the `forum_email_digest` worker (per property, under its job lease, every 30 minutes, at most 500 member and category pairs per pass, oldest check first) sends one email per category every `forums.emailDigestIntervalHours` (168, weekly) listing the member's unread notifications of that category updated since the previous digest, newest first, at most `forums.emailDigestMaxItems` (10) with the rest counted, each with its `/app` link. Nothing is sent for an empty window, and a member's first check only starts the schedule (the first digest arrives one interval after opting in). State lives in `forumemaildigests (propertyid, userid, category, lastsentutc, lastcheckedutc)`. Mail goes through the administrative email system: template `forum_digest` (category `Optional`; tokens `count`, `digestTitle`, `items`, `link`, `unsubscribeLink`), the property's brand and template override, the queue with retries, suppressions, the email log, and an idempotency key per member, category, and window; it carries `List-Unsubscribe` and one-click unsubscribe for its category. Notification hours and push pause do not apply to digests.

Fan-outs (`NewThreadInForum`, `PollClosed`) run in the Phase 2 fan-out job in batches of `notifications.fanoutBatchSize`, keyed by `(type, sourceid)` for idempotency. `ThreadReply` fan-out to followers uses the same job when a thread has more than `notifications.fanoutBatchSize` followers, otherwise inline in the comment write's post-commit step. Pushes obey notification hours, pause, preferences, and `push.maxPerUserPerHour`; aggregated `ThreadReply` and `NewThreadInForum` push at most once per group per hour (the `Reaction` rule).

Notification `context` adds `threadId`, `forumId`, `pollId` where relevant. Website paths are prefixed with `/app` as in Phase 2.

### 9.4 Read state (unread markers)

`threadreads (propertyid, userid, threadid, lastreadutc)` is written when a member opens a thread (`GET /api/v1/threads/{id}`) and `forumreads (propertyid, userid, forumid, lastreadutc)` when they open a forum's first page or call `POST /api/v1/forums/{id}/read`. A thread is unread when its `lastActivityUtc` is after `max(threadreads.lastreadutc, forumreads.lastreadutc)`. Rows older than `forums.readStateRetentionDays` (90) are pruned from both tables by the hourly `forum_read_prune` worker (one statement per pass, at most 5000 rows per table); a missing row means "unread only if active within the last `forums.unreadWindowDays` (14) days", so new members are not shown every thread as unread.

---

## 10. Public sample threads

In `FullyGated` properties (and any model with `anonymousBrowsing: false`) a `SystemAdmin` may set `audience: "Public"` on a thread in a forum whose audience is `Public` to make it a teaser, mirroring Phase 2 public sample posts:

| Method and path | Group | Notes |
|---|---|---|
| `POST /api/v1/public/threads/enumerate` (and `GET`) | Anonymous | Published `Public` threads in `Public` forums, `CreatedDescending` only, `maxResults` at most 20. Items are `PublicThread` |
| `GET /api/v1/public/threads/{id}` | Anonymous | One `PublicThread` or `404` |

`PublicThread` is `ThreadSummary` plus `body`, without `viewer`, `myReactions`, `isFollowed`, `isUnread`, poll `myVote`, and mention user ids. Comments are not included (the card shows `commentCount` and invites the visitor to join). Media follows Phase 2 section 5.10 (copied to the public paths). Responses carry `Cache-Control: public, max-age=60` and are rate limited per IP with `rateLimits.publicFeedPerMinute`.

When `anonymousBrowsing` is true, anonymous callers instead use the normal member routes for `Public` objects.

---

## 11. Moderation

### 11.1 Reports

`POST /api/v1/reports` accepts `targetType: "Thread"` (and `Comment` on threads, `PollOption` is not reportable; report the thread). Reports on threads and thread comments are stored with `scopetype: "Forum"` and `scopeid` = the forum id (comments resolve through their thread; reports on a promoted card resolve to the thread; on a linked thread to the feed post with `scopetype: "Feed"`). Auto-hide (`moderation.autoHideReportThreshold`) applies to threads exactly as to feed posts; `SystemAdmin` content is never auto-hidden. The review target (`moderation.reviewTargetHours`) and `ModerationOverdue` go to every moderator whose scope covers the forum.

### 11.2 Actions

`POST /api/v1/admin/moderation/actions` (Phase 2 section 9.3) accepts `targetType: "Thread"` with actions `Hide`, `Unhide`, `Remove`, `Restore`. Thread state rules mirror feed posts: Removed threads move their served media to the retained bucket; their comment trees become unreachable to non-staff; an active promotion's card disappears from the feed. Lock, unlock, pin, unpin, move, metadata edits, accepted answer changes by moderators, and forum mutes also write `moderationactions` rows, so one action history shows everything. The `action` enumeration adds `Pin`, `Unpin`, `Lock`, `Unlock`, `Move`, `EditMetadata`, `ForumMute`, `ForumUnmute`.

### 11.3 Forum mutes

| Method and path | Body | Success | Errors |
|---|---|---|---|
| `POST /api/v1/admin/moderation/forum-mutes` | `{ "userId", "forumId", "untilUtc", "reason", "notifyMember": true }` | `201 ForumMute` `{ id, user: UserSummary, forum: { id, name }, untilUtc, reason, mutedBy: UserSummary, createdUtc, labels, tags }` | `400 ValidationFailed` (`untilUtc` more than `forums.maxForumMuteDays` (30) ahead, or in the past), `403 Forbidden` (no scope, or the member is staff) |
| `POST /api/v1/admin/moderation/forum-mutes/enumerate` | `EnumerationQuery`; `filters`: `forumId`, `userId`, `activeOnly` (default true) | `200 EnumerationResult<ForumMute>` (filtered to the caller's scope) | |
| `DELETE /api/v1/admin/moderation/forum-mutes/{id}` | `{ "reason"? }` | `204` | |

A forum mute blocks thread creation, comments, reactions, votes, and follows' write side in that forum (`403 ForumMuted`); reading, reporting, blocking, and deleting their own content keep working. One active mute per member per forum: a new mute replaces the old one. `notifyMember` sends a `ModerationAction` notification ("A moderator muted you in General until ..."). Global mutes and bans stay `SystemAdmin` actions on the member page.

### 11.4 Queue scope

The moderation queue (`POST /api/v1/admin/moderation/queue/enumerate`) gains `filters.scopeType` (`Feed`, `Forum`) and `filters.forumIds`, and every row adds `scope: { type, id, name }`. Moderators see only rows in their scope; `SystemAdmin` sees everything. The queue detail for a thread target returns the Thread (any state), its forum, revisions, reports, actions, and author history (Phase 2 section 10.2 shape).

### 11.5 Moderator console access

Members holding any forum `Moderator` assignment may sign in to the admin console (Phase 2 section 10.2 rule) and see Home (moderation tiles for their scope), Moderation (queue, actions, forum mutes, review requests), Trending (their scope), and Threads (their scope). Every other section is hidden and its routes return `403`.

### 11.6 Moderator management

`POST /api/v1/admin/moderators` (Phase 2 section 10.4) accepts `{ userId, resourceType: "Forum", resourceId: "frm_..." | "*" }` and grants `Moderator` for that scope; `DELETE /api/v1/admin/moderators/{userId}?resourceType=Forum&resourceId=frm_...` revokes one assignment. The moderators enumeration adds `filters.resourceType` and `filters.forumId`, and each row lists its forum scopes (bounded by `forums.maxForums`). Audited as `member.roles`.

---

## 12. Admin console

| Page | Contents |
|---|---|
| Forums (`/admin/forums`) | Enumerated table (filters: section, archived, audience, labels, tags; search) with drag-to-reorder (saves `PUT /api/v1/admin/forums/order`), Create and Edit drawer (all forum fields, Markdown guidelines with live preview, icon picker for emoji or upload, allowed types checkboxes, audience select limited by the access model), row actions View, Edit, View JSON, Archive or Unarchive, Delete (only when empty), Moderators (drawer listing scoped moderators with Grant and Revoke) |
| Threads (`/admin/threads`) | Enumerated table across forums (filters: forum, type, state including Deleted, author, pinned, locked, promoted, feedback status, labels, tags, time window; search). Row actions View, View JSON, Pin or Unpin, Lock or Unlock, Move, Promote or Unpromote, Hide, Remove, Restore, Edit metadata. Detail drawer with revisions, moderation history, reports summary, poll results with totals, and the accepted answer or review |
| Trending (`/admin/trending`) | Section 5.3 |
| Moderation | Phase 2 page plus the Forum scope filter, a Forum mutes tab, and a Review requests tab (Feedback threads by status, oldest Open first, with "Start review" and "Mark reviewed") |
| Members (detail) | Forum roles in the roles editor, forum mutes list, `canPromoteToFeed` toggle in the Posting group |
| Home | Tiles: threads and comments per day (forums series added to `GET /api/v1/admin/metrics/engagement`), open forum reports, overdue reports, open review requests |
| Settings | New groups: Forums, Polls, Trending (section 15) |

`GET /api/v1/admin/metrics/forums?days=30` (`days` 7 to 365) returns `{ threadsByDay: [{ date, count }], commentsByDay, votesByDay, activeThreads, openReviewRequests, unansweredQuestions, byForum: { objects (max 20, by threads in window), totalRecords, enumerate: "/api/v1/admin/forums/enumerate" } }`.

---

## 13. Account lifecycle integration

| Event | Phase 3 effect |
|---|---|
| Account deletion | Threads are soft-deleted (Phase 2 rule for feed posts); their comment trees go with them. Comments elsewhere become "[deleted]" placeholders. Poll votes are deleted and counts recomputed. Follows, read state, and forum mutes are deleted. Accepted answers by the member are cleared by the comment erasure |
| Export (`GET /api/v1/me/export`) | Adds `threads` (with revisions), `pollVotes` (option labels and dates), `follows`, `forumMutes` (received) |
| Ban | Sessions revoked; content stays for moderators to act on |
| Mute (global) | `403 Muted` on every forum write |

---

## 14. Rate limits

Per account unless noted; existing limiter; `SystemAdmin` exempt; "new account" from Phase 2 (`comments.newAccountHours`).

| Route group | Limit | Setting |
|---|---|---|
| Create thread | 10 per hour, 50 per day; new accounts 2 per hour and 5 per day | `forums.threadsPerHour`, `forums.threadsPerDay`, `forums.newAccountThreadsPerHour`, `forums.newAccountThreadsPerDay` |
| Comments on threads | Phase 2 comment limits (shared budget with feed comments) | `comments.*` |
| Votes (PUT and DELETE) | 60 per minute | `polls.votesPerMinute` |
| Follow and unfollow | 120 per minute | `forums.followsPerMinute` |
| Thread search (`search` present) | 60 per minute | `forums.searchPerMinute` |
| Promote, discuss, unpromote | 30 per hour | `forums.promotionsPerHour` |
| Reads (forum and thread enumerations and GETs) | Phase 2 `rateLimits.memberReadsPerMinute` | |
| Public threads (anonymous) | 120 per minute per IP | `rateLimits.publicFeedPerMinute` |
| Moderator actions | none | |

---

## 15. Settings

All are added to `SettingsRegistry.cs`, property-scoped in multi-property mode. "Live" applies on save; "Restart" after a restart.

| Key | Type | Default | Apply | Environment variable |
|---|---|---|---|---|
| `forums.maxForums` | Integer 1 to 1000 | 200 | Live | |
| `forums.maxBodyLength` | Integer 100 to 50000 | 10000 | Live | |
| `forums.maxAttachments` | Integer 0 to 20 | 10 | Live | |
| `forums.maxVideosPerThread` | Integer 0 to 10 | 1 | Live | |
| `forums.maxPinnedPerForum` | Integer 1 to 20 | 5 | Live | |
| `forums.maxOpenFeedbackPerMember` | Integer 0 to 100 (0 = unlimited) | 3 | Live | |
| `forums.maxForumMuteDays` | Integer 1 to 365 | 30 | Live | |
| `forums.maxFollowsPerMember` | Integer 100 to 100000 | 5000 | Live | |
| `forums.autoFollowOnComment` | Boolean | true | Live | |
| `forums.readStateRetentionDays` | Integer 7 to 3650 | 90 | Live | |
| `forums.unreadWindowDays` | Integer 1 to 365 | 14 | Live | |
| `forums.emailDigestIntervalHours` | Integer 24 to 720 | 168 | Live | |
| `forums.emailDigestMaxItems` | Integer 1 to 50 | 10 | Live | |
| `forums.searchConfiguration` | String (PostgreSQL text search configuration) | `simple` | Restart | `TK_FORUMS_SEARCH_CONFIGURATION` |
| `forums.threadsPerHour` | Integer 1 to 1000 | 10 | Live | |
| `forums.threadsPerDay` | Integer 1 to 10000 | 50 | Live | |
| `forums.newAccountThreadsPerHour` | Integer 1 to 1000 | 2 | Live | |
| `forums.newAccountThreadsPerDay` | Integer 1 to 10000 | 5 | Live | |
| `forums.followsPerMinute` | Integer 1 to 10000 | 120 | Live | |
| `forums.searchPerMinute` | Integer 1 to 10000 | 60 | Live | |
| `forums.promotionsPerHour` | Integer 1 to 1000 | 30 | Live | |
| `polls.maxOptions` | Integer 2 to 50 | 20 | Live | |
| `polls.maxDurationDays` | Integer 1 to 3650 | 365 | Live | |
| `polls.votesPerMinute` | Integer 1 to 10000 | 60 | Live | |
| `polls.closeIntervalSeconds` | Integer 10 to 3600 | 60 | Live | |
| `trending.enabled` | Boolean | true | Live | `TK_TRENDING_ENABLED` |
| `trending.intervalMinutes` | Integer 1 to 1440 | 10 | Live | |
| `trending.windowDays` | Integer 1 to 90 | 7 | Live | |
| `trending.batchSize` | Integer 50 to 5000 | 500 | Live | |
| `trending.reactionWeight` | Decimal 0 to 100 | 1 | Live | |
| `trending.commentWeight` | Decimal 0 to 100 | 2 | Live | |
| `trending.commenterWeight` | Decimal 0 to 100 | 3 | Live | |
| `trending.ageOffsetHours` | Decimal 0.1 to 48 | 2 | Live | |
| `trending.gravity` | Decimal 0.1 to 5 | 1.5 | Live | |

The settings registry gains a `Decimal` type (double, invariant culture, with min and max) for the trending weights. Forum names, sections, and the initial forum set are not settings; admins create forums in the console. A property may seed initial forums through an optional `forums` array in `property.json` (`[{ slug, name, description, section, allowedThreadTypes }]`, applied only when the property has no forums yet); the sample property seeds "General" and "Help" so the engine's default exercises the feature.

---

## 16. Telemetry

New instruments on the `TrellisKit` meter (low-cardinality labels only; never ids, slugs, or text). All carry the resource attribute `trelliskit.property` and the `trelliskit.property` metric label from the multi-property milestone.

| Instrument | Type | Labels |
|---|---|---|
| `trelliskit.forums.threads` | Counter | `action` (create, update, delete, publish, pin, unpin, lock, unlock, move), `type`, `outcome` |
| `trelliskit.forums.polls.votes` | Counter | `action` (vote, change, remove), `outcome` |
| `trelliskit.forums.polls.closed` | Counter | `reason` (scheduled, manual) |
| `trelliskit.forums.answers` | Counter | `action` (accept, clear) |
| `trelliskit.forums.feedback` | Counter | `status` (transition target) |
| `trelliskit.forums.promotions` | Counter | `action` (promote, unpromote, discuss, undiscuss) |
| `trelliskit.forums.follows` | Counter | `target_type` (forum, thread), `action` (follow, unfollow, auto) |
| `trelliskit.forums.search` | Counter | `outcome`, `include_comments` (true, false) |
| `trelliskit.forums.search.duration` | Histogram (s) | `provider` (sqlite, postgresql) |
| `trelliskit.trending.recompute.duration` | Histogram (s) | |
| `trelliskit.trending.threads_scored` | Gauge | |
| `trelliskit.forums.review_requests.open` | Gauge | |
| `trelliskit.forums.mutes` | Counter | `action` (mute, unmute, expire) |

Existing counters gain values: `trelliskit.comments` and `trelliskit.reactions` `target_type` includes `thread`; `trelliskit.reports` and `trelliskit.moderation.actions` include `thread` and the new actions; `trelliskit.notifications.created` includes the new types. Spans: `forums.publish`, `forums.move`, `forums.search`, `trending.recompute`, `job PollWorker`, `job TrendingWorker`, with attributes `forum.id`, `thread.id`, `thread.type` (ids allowed on spans). Grafana: a Forums row in the Community folder (threads and comments by day, votes, search latency p50 and p95, trending recompute duration, open review requests, forum report backlog).

---

## 17. Data model

Migration "Forums", the next free version after the multi-property, profile field, and access model migrations (the number is assigned at implementation), in both `SqliteSchema.cs` and `PostgresqlSchema.cs`. Type mapping follows Phase 2 (`TEXT` ids and text, `BOOLEAN`/`INTEGER` 0 or 1, `TIMESTAMPTZ`/ISO `TEXT`, `DOUBLE PRECISION`/`REAL` for scores and weights). No JSON columns: every list is a child table. **Every table has `propertyid TEXT NOT NULL` as its first column, and every index and unique constraint below starts with `propertyid`** (written once here, implied in each row). Labels and tags use the shared `labels` and `tags` tables with the target types of section 0.1.

### Tables

| Table | Key columns |
|---|---|
| `forums` | `id` PK, `slug`, `name`, `description`, `guidelines`, `iconemoji`, `iconmediaid`, `color`, `section`, `sortorder`, `audience`, `threadcreation`, `defaultordering`, `allowmedia`, `allowvideo`, `isarchived`, `archivedutc`, `threadcount`, `commentcount`, `lastactivityutc`, `lastthreadid`, `createdbyuserid`, `deletedutc`, `createdutc`, `updatedutc` |
| `forumthreadtypes` | PK (`propertyid`, `forumid`, `threadtype`) | Allowed types per forum |
| `forumthreads` | `id` PK, `forumid` FK forums, `kind` (`Original`, `LinkedFeedPost`), `type`, `authoruserid`, `title`, `body` NULL (null for `LinkedFeedPost`), `excerpt`, `linkpreviewid`, `showlinkpreview`, `audience`, `sourcetype` NULL, `sourceid` NULL, `committargettype`, `committargetid`, `promotedfeedpostid` NULL, `promotedbyuserid` NULL, `promotedutc` NULL, `ispinned`, `pinnedutc`, `pinnedbyuserid`, `islocked`, `lockedutc`, `lockedbyuserid`, `lockreason`, `acceptedcommentid` NULL, `feedbackstatus` NULL, `feedbackfocus` NULL, `feedbackreviewerid` NULL, `feedbackreviewcommentid` NULL, `feedbackstatusutc` NULL, `attachmentcount`, `hasvideo`, `commentcount`, `uniquecommentercount`, `reactiontotal`, `openreportcount`, `trendingscore` (default 0), `trendingcomputedutc`, `lastactivityutc`, `lastcommentutc`, `editedutc`, `revisioncount`, `moderationstate`, `moderatedutc`, `moderatedbyuserid`, `moderationreason`, `publishedutc` NULL, `deletedutc` NULL, `deletedbyuserid`, `purgedutc`, `createdutc`, `updatedutc`; PostgreSQL adds `searchvector TSVECTOR` |
| `forumthreadrevisions` | `id` PK, `threadid` FK, `revision`, `title`, `body`, `type`, `attachmentsummary`, `editedbyuserid`, `editedbyrole`, `createdutc` |
| `polls` | `id` PK, `threadid` FK UNIQUE, `multiplechoice`, `maxchoices` NULL, `resultsvisibility`, `allowvotechange`, `closesutc` NULL, `closedutc` NULL, `closedbyuserid` NULL, `totalvoters`, `firstvoteutc` NULL, `createdutc`, `updatedutc` |
| `polloptions` | `id` PK, `pollid` FK, `label`, `labelnormalized`, `position`, `votecount`, `createdutc` |
| `pollvotes` | `id` PK, `pollid` FK, `optionid` FK, `userid`, `createdutc` |
| `forumfollows` | `id` PK, `forumid`, `userid`, `level`, `createdutc` |
| `threadfollows` | `id` PK, `threadid`, `userid`, `source` (`Explicit`, `Author`, `Comment`), `isoptedout` (an explicit unfollow that blocks auto-follow), `createdutc` |
| `threadreads` | PK (`propertyid`, `userid`, `threadid`), `lastreadutc` |
| `forumreads` | PK (`propertyid`, `userid`, `forumid`), `lastreadutc` |
| `forummutes` | `id` PK, `forumid`, `userid`, `untilutc`, `reason`, `mutedbyuserid`, `endedutc` NULL, `endedbyuserid` NULL, `createdutc` |
| SQLite only: `forumthreads_fts`, `threadcomments_fts` | FTS5 virtual tables (contentless, `rowid` mapped through `forumthreadsftsmap (propertyid, threadid, ftsrowid)` and `threadcommentsftsmap`), maintained in write batches |

Changes to existing tables (same migration): `feedposts` gains `discussionthreadid` NULL; `comments` gains `isacceptedanswer` (default false), `isfeedbackreview` (default false), and PostgreSQL `searchvector TSVECTOR` (maintained for `targettype = 'Thread'` only); `moderationactions` gains `fromforumid` NULL, `toforumid` NULL; `reports.scopetype` gains the value `Forum`.

### Indexes and unique constraints (all prefixed by `propertyid`)

| Table | Index | Purpose |
|---|---|---|
| `forums` | `ux_forums_slug (lower(slug)) WHERE deletedutc IS NULL` UNIQUE; `idx_forums_order (isarchived, sortorder, id)` | Slugs, forum list |
| `forumthreads` | `idx_threads_active (forumid, lastactivityutc DESC, id DESC) WHERE deletedutc IS NULL AND moderationstate = 'None' AND publishedutc IS NOT NULL` | `Active` |
| `forumthreads` | `idx_threads_hot (forumid, trendingscore DESC, lastactivityutc DESC, id DESC) WHERE ...same` | `Hot` |
| `forumthreads` | `idx_threads_created (forumid, publishedutc DESC, id DESC) WHERE ...same` | Created orderings |
| `forumthreads` | `idx_threads_all_active (lastactivityutc DESC, id DESC) WHERE ...same` | Cross-forum lists |
| `forumthreads` | `idx_threads_pinned (forumid, ispinned, pinnedutc)` | Pinned set |
| `forumthreads` | `idx_threads_author (authoruserid, createdutc)` | Own threads, moderation history |
| `forumthreads` | `idx_threads_scored (id) WHERE trendingscore > 0` | Worker reset of scores outside the window (the window itself uses `idx_threads_all_active`) |
| `forumthreads` | `idx_threads_feedback (feedbackstatus, createdutc) WHERE type = 'Feedback'` | Review queue, per-member limit |
| `forumthreads` | `ux_threads_source (sourcetype, sourceid) WHERE sourceid IS NOT NULL AND deletedutc IS NULL` UNIQUE | One discussion thread per feed post |
| `forumthreads` | PostgreSQL `idx_threads_search USING GIN (searchvector)` | Search |
| `forumthreadrevisions` | `ux_threadrevisions (threadid, revision)` UNIQUE | |
| `polloptions` | `ux_polloptions_label (pollid, labelnormalized)` UNIQUE; `idx_polloptions_order (pollid, position)` | |
| `pollvotes` | `ux_pollvotes (pollid, userid, optionid)` UNIQUE; `idx_pollvotes_user (userid, createdutc)` | One row per voter per option; export |
| `polls` | `idx_polls_closing (closesutc) WHERE closedutc IS NULL AND closesutc IS NOT NULL` | Close worker |
| `forumfollows` | `ux_forumfollows (forumid, userid)` UNIQUE; `idx_forumfollows_user (userid)` | |
| `threadfollows` | `ux_threadfollows (threadid, userid)` UNIQUE; `idx_threadfollows_user (userid, createdutc)` | Fan-out, "Following" |
| `forummutes` | `idx_forummutes_active (userid, forumid, untilutc) WHERE endedutc IS NULL` | Write checks |
| `comments` | PostgreSQL `idx_comments_search USING GIN (searchvector) WHERE targettype = 'Thread'` | Search |
| `feedposts` | `idx_feedposts_source (sourcetype, sourceid) WHERE sourceid IS NOT NULL` | Promotion lookup |

### Write rules

Phase 2 section 15 write rules apply: counters (`commentcount`, `uniquecommentercount`, `reactiontotal`, poll `votecount` and `totalvoters`, forum `threadcount` and `commentcount`) are recomputed from source rows inside the same batch; queue claims and singleton workers use the conditional `UPDATE` claim pattern and `jobleases`. Comment and reaction writes on a thread also refresh `lastactivityutc`, `lastcommentutc`, and the forum's `lastactivityutc` and `lastthreadid` in the same batch. FTS maintenance is part of the same batch (SQLite delete and insert into the FTS table; PostgreSQL `searchvector` set in the `UPDATE`).

---

## 18. Backend structure

New services: `ForumService`, `ThreadService`, `PollService`, `FeedbackService`, `PromotionService` (promote, discuss, coupling rules of 8.4), `ForumSearchService` (provider-specific SQL behind the database interfaces), `FollowService`, `ReadStateService`, `TrendingService`, `ForumModerationService` (scope checks, forum mutes; extends `ModerationService`). Route registrars: `ForumRoutes`, `ThreadRoutes`, `PollRoutes`, `PublicThreadRoutes`, `AdminForumRoutes`, `AdminThreadRoutes`, `AdminTrendingRoutes`, `ForumMuteRoutes`; `CommentRoutes`, `ReactionRoutes`, `ReportRoutes`, `ModerationRoutes`, `AdminModeratorRoutes`, and `NotificationRoutes` gain the `Thread` target. Workers (on `IntervalWorker`): `TrendingWorker`, `PollWorker`, `ReadStatePruneWorker`; `ContentPurgeWorker`, `NotificationFanoutWorker`, and `MediaProcessingWorker` (publishing Processing threads) are extended. `AuthorizationService` gains forum scope resolution (one cached lookup of the caller's forum assignments per request). Enums: `ThreadKindEnum`, `ThreadTypeEnum`, `ThreadStateEnum` (reuse `FeedPostStateEnum` values), `ThreadOrderingEnum`, `ForumOrderingEnum`, `ThreadCreationEnum`, `PollResultsVisibilityEnum`, `FeedbackStatusEnum`, `FollowSourceEnum`; `TargetTypeEnum`, `NotificationTypeEnum`, `NotificationCategoryEnum`, `ModerationActionTypeEnum` extended.

Touchstone suites (both providers, every route also run under the cross-property isolation suite of `TRELLISKIT.md` section 10.2): `ForumSuite` (CRUD, ordering, archive, audience narrowing), `ThreadSuite` (types, states, Processing publish, edit history, moderator metadata edits, delete), `ThreadListSuite` (orderings, bounded pinned-first paging, filters, de-duplication under Hot, continuation tokens), `PollSuite` (single and multi choice, `maxChoices`, results visibility per caller, vote change lock, close worker with a fixed clock, option lock after first vote), `QuestionSuite` (accept, replace, clear, accepted comment hidden), `FeedbackSuite` (limits, transitions, reviewer rights), `PromotionSuite` (promote, discuss, one conversation, coupling of hide, remove, delete, move, audience), `ForumSearchSuite` (title weighting, comments, prefix, snippets, visibility), `FollowSuite` and `ForumNotificationSuite` (types, de-duplication priority, aggregation, fan-out idempotency, blocks), `TrendingSuite` (formula with fixed clock and weights, window), `ForumModerationSuite` (scope per forum and `*`, staff content protection, forum mutes, move scope checks, report scope moves), `ForumAccessSuite` (`Open`, `PartiallyGated` with `forums` gated and not, `FullyGated` public samples, anonymous browsing).

---

## 19. Client notes

**Shared (`shared/src`):** `ApiClient` methods: `enumerateForums`, `getForum`, `followForum`, `unfollowForum`, `markForumRead`, `enumerateForumThreads`, `enumerateThreads`, `getThread`, `createThread`, `updateThread`, `deleteThread`, `enumerateThreadRevisions`, `pinThread`, `unpinThread`, `lockThread`, `unlockThread`, `moveThread`, `acceptAnswer`, `clearAcceptedAnswer`, `putThreadReaction`, `deleteThreadReaction`, `enumerateThreadReactions`, `enumerateThreadComments`, `createThreadComment`, `votePoll`, `removePollVote`, `closePoll`, `setFeedbackStatus`, `promoteThread`, `unpromoteThread`, `discussFeedPost`, `undiscussFeedPost`, `followThread`, `unfollowThread`, `enumerateMyFollows`, `enumeratePublicThreads`, `getPublicThread`; admin: `adminEnumerateForums`, `adminCreateForum`, `adminUpdateForum`, `adminOrderForums`, `adminArchiveForum`, `adminUnarchiveForum`, `adminDeleteForum`, `adminEnumerateForumModerators`, `adminEnumerateThreads`, `adminEnumerateTrending`, `adminForumMetrics`, `createForumMute`, `enumerateForumMutes`, `endForumMute`. A `commentTarget` helper routes every comment and reaction call to the right segment (`feed` or `threads`), and a poll helper computes display state (`resultsVisible`, percentages) only from server fields. Paths: `/forums`, `/forums/{id}`, `/threads/{id}`, `/threads/{id}?comment={commentId}` (the website prefixes `/app`; web URLs may include the slug, `/app/forums/{slug}`, resolved through `getForum`).

**Website (`/app/forums`, `/app/forums/:slug`, `/app/threads/:id`, `/app/forums/following`):** forum index grouped by section with unread counts and follow toggles; forum page with ordering selector (Active, Hot, New, Top), type filter chips, label and tag filters, search (URL state per the enumeration spec), pinned section, and a "New thread" composer with a type picker limited to `viewer.allowedThreadTypes`; thread page with the body, attachments, poll card (vote, change, results bar chart with accessible text alternatives), accepted answer or review block, comment tree (Phase 2 component) against `commentTarget`, moderator menu from `viewer`, promote dialog, follow button, edit history. Search results show snippets with highlights. Marketing pages may show public sample threads. Admin pages per section 12.

**Mobile and tablet:** Forums tab (plan section 7.1): forum list (sections, unread badges), forum screen (`@shopify/flash-list`, ordering and filter bottom sheet with labels and tags, pull to refresh), thread screen (body, media, poll card, accepted answer, comment tree, composer), thread composer (type picker, poll builder, Feedback with a required video from `expo-image-picker`), search screen, Following list under the tab's header menu, moderator actions sheet when `viewer.canModerate`. Tablet: forum list, thread list, and thread detail in the existing `SplitView` (forums collapse into a drawer below 900 points of width). Push taps route through Expo Router paths of section 9.3.

---

## 20. Delivery order

1. Schema migration, enums, data access for forums, threads, revisions, polls, follows, read state, mutes (both providers), with `propertyid` scoping in the shared helpers.
2. Forums: member and admin routes, ordering, archive, moderator scope resolution, admin Forums page.
3. Threads: create (Discussion, Question), list with orderings and filters, read, edit history, delete, comments and reactions on the `Thread` target, mention and comment notifications; website and mobile forum and thread screens.
4. Moderation: reports scope `Forum`, actions on threads, pin, lock, move, metadata edits, forum mutes, queue scope, moderator console access.
5. Polls (create, vote, results visibility, close worker) and Questions (accepted answers).
6. Following, read state, the new notification types and categories, fan-outs, preference screens.
7. Trending worker, Hot ordering, admin Trending page.
8. Promote to feed and cross-post discussion with the coupling rules; feed card rendering on both apps.
9. Feedback threads and the review requests queue.
10. Search (PostgreSQL `tsvector`, SQLite FTS5) and search UX.
11. Public sample threads, metrics, Grafana, `REST_API.md`, Postman, simulated user testing pass.

Backend steps 1 to 3 unblock both clients; the website and mobile teams build against this contract in parallel from step 1, using the Postman collection and the shared `ApiClient` fakes.

---

## 21. Open questions (with recommended answers)

| # | Question | Recommendation |
|---|---|---|
| 1 | May forum moderators edit other members' thread titles? Phase 2 decided moderators never edit members' words | Allow title, labels, tags, and Discussion/Question type changes only, always as a visible revision marked "Moderator"; never the body (section 1.3) |
| 2 | Who reviews Feedback threads? | Moderators in the forum's scope (and `SystemAdmin`). A separate `Reviewer` role adds little until a property needs reviewers who are not moderators; revisit then |
| 3 | Open Feedback requests per member | 3 at a time (`forums.maxOpenFeedbackPerMember`), so reviewers are not flooded; properties can set 0 for unlimited |
| 4 | Should poll voters be visible ("who voted for what")? | No in Phase 3: anonymous counts only. Public voting changes how members vote and adds a privacy surface |
| 5 | Default poll results visibility | `AfterVote` (avoids bandwagon voting while still rewarding participation) |
| 6 | Should a promoted thread notify every member (`NewFeedPost` fan-out and email)? | Yes by default (`notifyMembers: true`), with the option to promote silently |
| 7 | Can a member with `canPromoteToFeed` promote threads they cannot moderate, from any forum? | Yes, from any forum they can read; the audience can only narrow, so promotion never widens access |
| 8 | Auto-follow threads you comment on | On (`forums.autoFollowOnComment: true`), with a one-tap unfollow that sticks |
| 9 | Email for `Following` and `Forums` categories | Not in Phase 3 (in-app and push only); a weekly digest is the right email surface later |
| 10 | Trending weights and decay | Keep the plan's formula and defaults (1, 2, 3, offset 2 hours, gravity 1.5), editable in Settings; tune after real data |
| 11 | Global `GET /api/v1/search` across feed, forums, library | Defer to Phase 4 when the library exists; Phase 3 ships forum search through the thread enumeration |
| 12 | Initial forums for a property | Admin-created in the console, with an optional `forums` seed in `property.json` for new properties; each property seeds the forum list from its own plan (plus a Help forum), never from engine defaults |
| 13 | Text search language | `simple` configuration (language-neutral, engine rule) with the `forums.searchConfiguration` override; a property in one language may set `english` for stemming |
| 14 | Should locked threads still accept reactions? | Yes; locking stops the conversation, not appreciation. Reports and moderation always work |
| 15 | Forum mute duration cap | 30 days for forum moderators; longer or global restrictions stay with `SystemAdmin` on the member page |


---

## Resolved decisions (property owner, 2026-11-11)

These answers override the recommendations in the open questions section wherever they differ.

| # | Question | Decision |
|---|---|---|
| 1 | Moderator powers on threads | Edit title, labels, tags, and type (Discussion or Question) as visible revisions, **and** hide, lock, move, and delete. No body rewrites |
| 2 | Who reviews Feedback threads | Forum moderators (and admins) |
| 3 | Open Feedback requests per member | 3, configurable per forum |
| 4 | Poll vote visibility | **The poll author chooses** per poll: anonymous counts or visible voters |
| 5 | Poll results visibility | **The poll creator chooses** per poll (always, after voting, or after close); no engine-imposed default beyond "after voting" as the initial form value |
| 6 | Notify on promote to feed | Yes, through the new-feed-post category, with opt-out |
| 7 | Promote scope | Any forum the promoter can read; the audience can only narrow |
| 8 | Auto-follow on comment | On by default (member setting to turn off) |
| 9 | Delivery for new forum categories | In-app and push by default, **plus email as an opt-in channel** per category (built as a per-category digest, section 9.3) |
| 10 | Trending weights | Plan defaults, editable in admin settings |
| 11 | Global search | **Included in Phase 3** (a feature of its own for access models: `features.search` and the `search` gated feature key, docs/ACCESS_MODELS_CONTRACT.md section 2)**:** one search box across all content types that exist (feed posts, threads, comments, members, and later phases' objects as they land), with label, tag, type, and date filters |
| 12 | Initial forums | Optional seed list in `property.json`, **plus a default "General discussion" forum created automatically for every property** (renamable and deletable by admins) |
| 13 | Search engine and language processing | **Verbex** (NuGet `Verbex`, 0.2.1 or later; https://github.com/jchristn/verbex) replaces the PostgreSQL `tsvector` and SQLite FTS5 approach for all engine search: one inverted index per property (index names prefixed with the property id), lemmatization and stop-word removal enabled, Verbex label and tag filters mapped from enumeration label and tag filters, and results returned as `EnumerationResult` with snippets. Index updates happen on create, edit, hide, and delete through a background indexer with a rebuild command. Verbex is marked alpha upstream, so search goes through an engine interface (`ISearchIndex`) with Verbex as the implementation |
| 14 | Reactions on locked threads | **No:** a locked thread is fully frozen (no comments, no reactions, no votes) |
| 15 | Forum mute duration cap for moderators | **No cap** (moderators may mute within their forums indefinitely; admins can lift any mute) |


---

## Client assumptions the backend must match (from the built clients, 2026-11-12)

The website and mobile clients were built against this contract before the backend. Where the contract was silent, they assume the following; the backend implements these unless the contract is changed and the clients updated together.

1. **Global search:** `POST /api/v1/search/enumerate`, an `EnumerationQuery` with required `search` and optional `filters.types` (`FeedPost`, `Thread`, `Comment`, `User`, later types), labels, tags, and the created window. Items: `{ type, id, title, snippet: { text, highlights: [{ start, length }] }, path, author: UserSummary|null, forum?: { id, slug, name }, createdUtc, labels, tags }`; `path` is app-relative like notification paths.
2. **Poll voter visibility:** `poll.voterVisibility` is `Anonymous` (default) or `Visible`, accepted on create and returned on Poll. `POST /api/v1/polls/{id}/voters/enumerate` returns `EnumerationResult<{ user: UserSummary, optionIds, createdUtc }>` with `filters.optionId`.
3. **Feedback limit per forum:** Forum and AdminForum `maxOpenFeedbackPerMember` (integer; null uses the property setting; 0 is unlimited), accepted on admin create and update.
4. **Forum mutes:** `untilUtc: null` means muted until lifted; ForumMute carries `endedUtc` (null while active).
5. **Auto-follow:** `autoFollowOnComment` (boolean) on `GET` and `PUT /me/notification-preferences`; clients show the toggle only when the field is present.
6. **Locked threads (resolved decision 14 overrides sections 4.7 and 4.9):** `viewer.canReact` is false and reaction PUT/DELETE return `409 ThreadLocked`.
7. **Admin threads:** `POST /api/v1/admin/threads/enumerate` (admin page limits) returns ThreadSummary items whose `state` may be `Deleted`. Filters: `forumIds`, `type` (array), `state`, `authorUserId`, `pinned`, `locked`, `promoted`, `feedbackStatus` (array). Orderings: `CreatedDescending`, `CreatedAscending`, `LastActivityDescending`, `LastActivityAscending`. Moderators are scope-limited. The Review requests tab uses it with `type: ["Feedback"]`, `CreatedAscending`.
8. **Admin thread detail:** reuses `GET /api/v1/admin/moderation/queue/Thread/{id}` returning `{ target: Thread, revisions, reports, actions }`, each list a bounded preview `{ objects, totalRecords, enumerate }` per the enumeration spec section 2.4.
9. **Boolean filters** in POST bodies arrive as strings `"true"`/`"false"` (`answered`, `pollOpen`, `activeOnly`, `includeArchived`, `followedOnly`, `promoted`); the backend accepts both strings and JSON booleans.
10. **Admin forums:** enumerate sends `filters.includeArchived: "true"` unless filtering on `isArchived`; archive and unarchive send `{}` when there is no reason.
11. **Trending:** ordering `ScoreDescending`; filters `forumIds`, `type`, `promoted`, `minScore` (number); items carry `thread.trendingScore` and `thread.forum`. Trending weights are collected from `objects` returned by `POST /settings/enumerate` with `search: "trending."`, following continuation tokens; each field has a `key`.
12. **Moderation queue:** GET parameters `scopeType` and `forumIds` (single id); the forum moderators enumeration returns rows with `user` and `scope` (`Forum` or `AllForums`).
13. **Promote:** body `{ title, excerpt, audience, notifyMembers, pin? }` (`pin` SystemAdmin only), returns a FeedPost with `id` and `publishedUtc`. **Discuss** returns a Thread with `forum { id, name }`; FeedPost carries `discussion { threadId, forumId, forumName }`. "Open for discussion" visibility is computed client-side from `Me` (SystemAdmin or `flags.canPromoteToFeed`).
14. **Comments on threads:** served in both the GET and `POST .../enumerate` forms. Tree pages carry `commentCount` and `acceptedAnswerId`; each comment carries `isAcceptedAnswer` and `isFeedbackReview`. Reports on a promoted card target the thread; reports on a thread opened from a feed post target the feed post (section 8.4).
15. **Metadata target types:** `Forum`, `Thread`, `ForumFollow`, `ThreadFollow`; mention context `contextType: "Thread"`.

Public sample threads are on the website: the home page (or a `@public-threads` widget) lists them and each opens a public page at `/threads/{id}` (body, attachment preview, poll summary, counts, and a join or sign-in prompt; replies stay inside). Editing a poll before the first vote (section 6.4) is built in both clients (after the first vote only `closesUtc` is offered; `409 PollOptionsLocked` switches the form to that mode), and `/admin/metrics/engagement` carries the forums series `threadsByDay` and `threadCommentsByDay`, shown on the admin Home engagement tiles.

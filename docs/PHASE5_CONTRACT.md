# Phase 5 Contract: Messaging and Support

This is the binding contract between the backend, the website, and the mobile app (phone and tablet) for Phase 5. Every team codes against it exactly. It is an engine contract: TrellisKit implements it for every property (`docs/TRELLISKIT.md`). Property-specific values (ticket types, idea statuses shown to members, broadcast segments, domains) come from the property configuration and the admin console; examples use the neutral sample property (Example Community, `example.test`). When the implementation is complete, `REST_API.md` and the Postman collection are updated to match, and this file stays as the design record. Plan references: the property plan of record, sections 3, 4.3, 4.7, 4.11, 6.3, 7.1, 9, 11.

Status: draft for product owner review. Nothing here is built yet. The open questions at the end must be answered before implementation starts; the answers are recorded in this file after review.

Phase 5 builds on, and does not repeat:
- `docs/PHASE1_CONTRACT.md` (identity, sessions and refresh, roles, the `canSendDirectMessages` flag, audit).
- `docs/PHASE2_CONTRACT.md` (body format, media pipeline and grant cookies, reactions, notifications, push, preferences, notification hours, reports, moderation actions, word filter, blocks, link previews, rate limit conventions, section 8.4: "Phase 5 adds a `notification.created` WebSocket event and keeps this route as the fallback").
- `docs/PHASE3_CONTRACT.md` (comments as the one conversation, digests through the administrative email system, `LeasedPassWorker` jobs).
- `docs/PHASE4_CONTRACT.md` (the `Document` media kind, scoped staff roles pattern).
- `docs/ENUMERATION_AND_METADATA.md` (binding: `EnumerationQuery` and `EnumerationResult<T>` for every list, labels and tags on every object, no unbounded lists, label and tag filters on every search surface).
- `docs/ACCESS_MODELS_CONTRACT.md`, `docs/MULTIPROPERTY_CONTRACT.md` (property resolution, property-bound data access, `propertyid` on every row, the isolation suite), `docs/LIVE_UPDATES_CONTRACT.md` section 13 (kill switches), `docs/AI_CONTRACT.md` section 7.4 (direct messages and support tickets are never assistant knowledge), and `docs/TRELLISKIT.md` sections 7, 10, 12, 13.

Conventions are unchanged: routes under `/api/v1`, camelCase JSON, string enums, ISO 8601 UTC timestamps, errors as `{ "error", "message", "fieldErrors" }`, `X-Client` and `X-Client-Version` on every app request, `X-TK-Property` and `X-TK-App` from mobile and tablet builds, access tokens as `Authorization: Bearer <accessToken>`, `429` always with `Retry-After`. PostgreSQL only.

---

## 0. New conventions in Phase 5

### 0.1 Target types and identifier prefixes

Phase 2 reserved `Message`. Phase 5 activates it and adds the rest:

| `targetType` | Id prefix | URL segment | Notes |
|---|---|---|---|
| `Conversation` | `cnv_` | `conversations` | A direct conversation (1:1 at launch; the schema allows groups) |
| `ConversationMember` | `cvm_` | | A member's state in a conversation (folder, read and delivered positions, mute) |
| `Message` | `msg_` | `messages` | Reactions, reports |
| `MessageReportSnapshot` | `mrs_` | | Frozen copy of reported messages for moderators (section 3.9) |
| `RealtimeConnection` | `rtc_` | | A live WebSocket connection (internal, staff diagnostics only) |
| `Ticket` | `tkt_` | `tickets` | Support request |
| `TicketMessage` | `tkm_` | | Member-visible reply or staff-only internal note |
| `TicketType` | `tty_` | | Admin-defined ticket type (seeded) |
| `Idea` | `ida_` | `ideas` | Ideas board entry; comments, reactions, reports |
| `IdeaVote` | `idv_` | | One per member per idea |
| `IdeaStatus` | `ids_` | | Admin-defined idea status (seeded) |
| `Broadcast` | `bct_` | `broadcasts` | Staff announcement over in-app, push, and email |
| `BroadcastSegment` | `bsg_` | | A saved audience definition |
| `BroadcastDelivery` | `bdl_` | | Per recipient delivery record (bounded by the segment) |

The AI contract's `Conversation` (`acv_`) and `ConversationMessage` (`acm_`) are assistant chats and are unrelated; their target type names are `AssistantConversation` and `AssistantMessage`, so no names collide.

### 0.2 Feature keys and kill switches

| Feature key | Routes | Gatable (`access.gatedFeatures`) | `features.<key>` off | Kill switch |
|---|---|---|---|---|
| `messaging` (existing) | `/conversations*`, `/messages*`, `/me/messaging*`, messaging frames on `/ws` | Yes | `404 NotAvailable`; Messages entry hidden; WebSocket still serves notification events | `messaging` (existing): `503 FeatureDisabled` on REST; WebSocket sends `feature.disabled` and stops messaging events |
| `support` (new) | `/tickets*`, `/support/*` | **No**: support is an account route, reachable by every signed-in account in every model, so a lapsed or unpaid member can always reach billing help | `404 NotAvailable`; clients show the property's support email instead | `support` (new) |
| `ideas` (new) | `/ideas*`, `/public/ideas*` | Yes | `404 NotAvailable`; Ideas entry hidden | `ideas` (new) |
| `broadcasts` (new) | `/admin/broadcasts*`, and the in-app announcement surface | No (staff feature) | Admin pages hidden; no member impact | `broadcasts` (new): stops pending sends (admin routes stay open per section 13.2 of the live updates contract, but the sender worker pauses) |
| `realtime` (new, kill switch only) | `GET /api/v1/ws` | No | not a property feature | `realtime`: the upgrade returns `503 FeatureDisabled` and open sockets close with `4503`; clients fall back to polling (section 5) |

`property.json` `features` gains `support` (default true), `ideas` (default true), and `broadcasts` (default true); `messaging` already exists. `KillSwitchCatalog.FeatureKeys` gains `support`, `ideas`, `broadcasts`, `realtime`. `ACCESS_MODELS_CONTRACT.md` section 1 `gatedFeatures` accepts `ideas` (in addition to the existing nine keys); `support` and `broadcasts` are rejected there (`400 ValidationFailed`).

### 0.3 Access model behavior

| Feature, action | `FullyGated` | `PartiallyGated`, feature gated | `PartiallyGated`, not gated | `Open` |
|---|---|---|---|---|
| `messaging`: start conversations, send, react | Member, and the recipient must be a member | Member, and the recipient must be a member | SignedIn | SignedIn |
| `messaging`: read history, mark read, delete own messages, report, block | Member; a lapsed member keeps read-only access to their history for `messaging.lapsedReadOnlyDays` (90) | same | SignedIn | SignedIn |
| `support`: tickets | SignedIn (every model) | SignedIn | SignedIn | SignedIn |
| `ideas`: list and read | Member; `/public/ideas*` for `Public` ideas when `ideas.publicBoardEnabled` | Member (route gate, signed out `401`) | SignedIn; Anonymous for `Public` ideas when `anonymousBrowsing` | same as not gated |
| `ideas`: submit, vote, comment, react | Member | Member | SignedIn | SignedIn |
| WebSocket connection | SignedIn session (notification events for everyone; messaging events only where `messaging` allows) | same | same | same |

Messaging is never available signed out. A direct message never widens access: when either party loses access to `messaging`, the other party sees "This member can't receive messages right now" and sending returns `403 RecipientUnavailable`. Messages and tickets have no `audience` (they are private to their participants); ideas do (`Public`, `SignedIn`, `Members`, default per `ACCESS_MODELS_CONTRACT.md` section 3.3).

### 0.4 Error codes added in Phase 5

| Status | `error` | When |
|---|---|---|
| 400 | `CannotMessageSelf` | Starting a conversation with yourself |
| 400 | `MessageEmpty` | No body and no attachment |
| 400 | `SegmentInvalid` | A broadcast segment filter names an unknown field, option, or operator |
| 403 | `MessagingNotAllowed` | The sender cannot message this recipient: blocks in either direction, the recipient's DM policy, the sender's `canSendDirectMessages` false, or a declined request. One code for every reason, so a block is never revealed |
| 403 | `RecipientUnavailable` | The recipient lost messaging access (gating, ban, deletion) |
| 403 | `RequestLimitReached` | The sender reached `messaging.requestMessageLimit` unanswered messages in a pending request |
| 403 | `MessagingRestricted` | The sender is muted, or a new account over its first-contact limits |
| 404 | `NotFound` | Unknown, deleted, or not a participant (never 403 for conversations, so ids do not leak) |
| 409 | `EditWindowClosed` | Editing a message after `messaging.editWindowMinutes` |
| 409 | `TicketClosed` | Replying to a closed ticket after `support.reopenDays` |
| 409 | `IdeaMerged` | Voting or commenting on an idea merged into another (the body names the target) |
| 409 | `BroadcastNotEditable` | Editing a broadcast that is sending, sent, or canceled |
| 409 | `BroadcastLimitReached` | More than `broadcasts.maxPerDay` broadcasts sent today |
| 429 | `RateLimited` | Section 13, always with `Retry-After` |
| 503 | `FeatureDisabled` | A Phase 5 kill switch is on |

---

## 1. Roles, permissions, and capability matrix

### 1.1 Roles

| Holder | Scope in Phase 5 |
|---|---|
| `SystemAdmin` (and a delegated `PlatformAdmin` session) | Everything that staff may do (section 4); **cannot browse direct messages** |
| `SupportAgent` (new Phase 1 style role, `resourceType: "Global"`) | Ticket inbox, replies, internal notes, assignment, status, ideas board status and merges. No other admin pages. Sees a requester's membership state and recent sessions' app versions on a ticket; nothing else from the member record |
| `Moderator` with `resourceType: "Messaging"`, `resourceId: "*"` (new resource type) | The message report queue: snapshots of reported messages only (section 3.9), and the actions of 3.9. Cannot open conversations |
| `Moderator` with `resourceId: "*"` (forums) | Comments on ideas when `ideas.forumModeratorsModerate` (true) |
| `ReleaseManager` (live updates) | Phase 5 kill switches |

`isStaff` on `UserSummary` becomes true for `SupportAgent` and Messaging moderators (members cannot block staff, Phase 2 decision 9; staff can still be reported).

### 1.2 Per-user flags and settings

| Flag or setting | Default | Meaning |
|---|---|---|
| `flags.canSendDirectMessages` (Phase 1) | true | False: cannot start conversations or send messages (`403 MessagingNotAllowed`); can read, delete own, report, block |
| `isMuted` (Phase 1) | | Muted members cannot send (`403 MessagingRestricted`), except replies inside conversations with staff (so a moderator can talk to a muted member) |
| `me.messaging.dmPolicy` (member setting) | `messaging.defaultDmPolicy` (`Everyone`) | `Everyone` (anyone may send; strangers land in Requests), `Known` (only members the recipient has messaged before or accepted; others get `403 MessagingNotAllowed`), `Nobody` (only staff). Staff can always message members |
| `me.messaging.readReceipts` | true | Reciprocal: when off, the member neither sends nor sees read receipts |
| `me.messaging.typingIndicators` | true | Reciprocal, like read receipts |
| `me.messaging.pushPreview` | `messaging.defaultPushPreview` (`SenderOnly`) | `SenderOnly` ("Alex Chen sent you a message"), `Full` (sender and a 100 character excerpt), `Hidden` ("New message") |

### 1.3 Capability matrix

| Action | Who |
|---|---|
| Start a conversation, send, edit, react | Section 0.3 access, `canSendDirectMessages`, not muted (except with staff), recipient's `dmPolicy`, no block in either direction, request limits |
| Read a conversation | Its current members only. No staff role can read a conversation it is not a member of |
| Delete own message for everyone | Author, any time (tombstone) |
| Delete a conversation for yourself | Any member of it |
| Accept, decline a request; block from a request | The recipient |
| Report a message or a conversation | Any member of the conversation |
| See reported message snapshots, act on them | `SystemAdmin`, Messaging moderators |
| Open, read own, reply to own tickets | Any signed-in account (`support` feature) |
| Ticket inbox, staff replies, internal notes, assignment, status, priority | `SystemAdmin`, `SupportAgent` |
| Submit an idea | Members per 0.3 when `ideas.memberSubmissions` is `Direct`; otherwise staff publish ideas from tickets |
| Vote, comment, react on ideas | Members per 0.3, not muted |
| Set idea status, merge, edit any idea | `SystemAdmin`, `SupportAgent` |
| Create, schedule, send, cancel broadcasts | `SystemAdmin` (open question 20 covers a dedicated role) |
| Essential (non-optional) broadcast | `SystemAdmin` with a recorded reason |

### 1.4 Viewer capabilities

| Type | `viewer` |
|---|---|
| Conversation | `{ canSend, canAttachMedia, canAttachVideo, canReport, canBlock, canAccept, canDecline, cannotSendReason }` (`cannotSendReason` is `Blocked` only to the member who blocked, `RecipientUnavailable`, `RequestPending`, `MessagingRestricted`, or null; a blocked sender sees only `Unavailable`) |
| Message | `{ canEdit, canDelete, canReact, canReport }` |
| Ticket | `{ canReply, canClose, canReopen, canAttach }` (staff add `canNote`, `canAssign`, `canSetStatus`) |
| Idea | `{ canVote, canComment, canReact, canReport, canEdit, canSetStatus, canMerge, canOpen, requires }` |

---

## 2. Shared shapes

### 2.1 Conversation

```json
{
  "id": "cnv_...",
  "kind": "Direct",
  "members": [ { "user": { "...": "UserSummary" }, "role": "Member", "joinedUtc": "..." } ],
  "otherMember": { "...": "UserSummary" },
  "folder": "Inbox",
  "requestState": null,
  "lastMessage": { "...": "MessageSummary or null" },
  "lastSeq": 418,
  "myLastReadSeq": 410,
  "myLastDeliveredSeq": 418,
  "otherLastReadSeq": 418,
  "unreadCount": 8,
  "isMuted": false,
  "mutedUntilUtc": null,
  "labels": [], "tags": {},
  "viewer": { "...": "1.4" },
  "createdUtc": "...", "updatedUtc": "..."
}
```

| Field | Rule |
|---|---|
| `kind` | `Direct` (exactly two members). `Group` is reserved (schema ready, routes return `400 ValidationFailed` until enabled, open question 1) |
| `members` | Fixed bound: 2 for `Direct`, `messaging.maxGroupMembers` for a future `Group` |
| `otherMember` | For `Direct`, the member who is not the caller (convenience) |
| `folder` | The caller's folder: `Inbox`, `Requests` (a pending request sent to the caller), `Archived` |
| `requestState` | For the recipient of a request: `Pending`; for the sender of a pending request: `AwaitingAcceptance`; null once accepted or for known contacts |
| `lastSeq` | Highest message sequence number in the conversation (section 3.3) |
| `myLastReadSeq`, `myLastDeliveredSeq` | The caller's positions |
| `otherLastReadSeq` | The other member's read position, null when either member has `readReceipts` off |
| `unreadCount` | Messages by others with `seq` above `myLastReadSeq`, capped at 999 |
| `labels`, `tags` | The caller's own private metadata on the conversation (each member's metadata is separate and never shown to the other) |

### 2.2 Message

```json
{
  "id": "msg_...",
  "conversationId": "cnv_...",
  "seq": 418,
  "clientMessageId": "6f0c1e2a-...",
  "author": { "...": "UserSummary" },
  "body": "See you Thursday?",
  "attachments": [ { "...": "Media with conversation-scoped URLs (section 3.7)" } ],
  "linkPreview": null,
  "replyTo": { "id": "msg_...", "seq": 402, "excerpt": "Are you coming...", "authorDisplayName": "Alex Chen", "isDeleted": false },
  "state": "Visible",
  "editedUtc": null,
  "reactionCounts": { "like": 1 }, "reactionTotal": 1, "myReactions": [],
  "labels": [], "tags": {},
  "viewer": { "...": "1.4" },
  "createdUtc": "2026-11-20T18:02:00Z",
  "updatedUtc": "2026-11-20T18:02:00Z"
}
```

| Field | Rule |
|---|---|
| `seq` | Per-conversation sequence, strictly increasing by 1 with no gaps for created messages, assigned by the server (section 3.3) |
| `clientMessageId` | Client-generated UUID, required on send, unique per (conversation, author); makes sends idempotent |
| `body` | 0 to `messaging.maxBodyLength` (4000) characters after normalization; plain text with URLs auto-linked (no Markdown formatting, no mentions, no headings) |
| `attachments` | At most `messaging.maxAttachments` (4) images, or 1 video, when `messaging.allowMedia` (true) and the sender's media flags allow |
| `state` | `Visible`, `Deleted` (tombstone: `body` empty, attachments empty, "Message deleted"), `Removed` (by a moderator after a report: "Message removed") |
| `editedUtc` | Set when edited; only the latest text is kept (no edit history in DMs, open question 6) |
| `labels`, `tags` | Author-private metadata (each viewer's own); `staff:` metadata is never stored on messages |

`MessageSummary` (conversation list preview) is `{ id, seq, authorUserId, excerpt (at most 120, empty for deleted), hasAttachments, state, createdUtc }`.

### 2.3 Ticket and TicketMessage

```json
{
  "id": "tkt_...",
  "number": 1042,
  "type": { "key": "Billing", "label": "Billing" },
  "title": "Charged twice",
  "status": "WaitingOnMember",
  "priority": "Normal",
  "requester": { "...": "UserSummary" },
  "assignee": { "...": "UserSummary or null" },
  "device": { "client": "Mobile", "clientVersion": "1.4.0", "platform": "Ios", "osVersion": "18.1", "deviceModel": "iPhone15,2", "updateId": "...", "locale": "en-US", "timeZone": "America/Los_Angeles" },
  "membership": { "isActive": true, "source": "Subscription", "store": "Apple", "expiresUtc": "..." },
  "idea": null,
  "messages": { "objects": [ { "...": "TicketMessage (newest 20)" } ], "totalRecords": 3, "enumerate": "/api/v1/tickets/tkt_.../messages/enumerate" },
  "firstResponseDueUtc": "...", "firstResponseUtc": "...", "isOverdue": false,
  "lastActivityUtc": "...", "resolvedUtc": null, "closedUtc": null,
  "labels": [], "tags": {},
  "viewer": { "...": "1.4" },
  "createdUtc": "...", "updatedUtc": "..."
}
```

```json
{ "id": "tkm_...", "ticketId": "tkt_...", "kind": "Reply", "author": { "...": "UserSummary" }, "authorRole": "Member", "body": "Markdown subset.", "attachments": [ { "...": "Media" } ], "labels": [], "tags": {}, "createdUtc": "..." }
```

| Field | Rule |
|---|---|
| `number` | Per-property sequential number for humans ("Ticket #1042") |
| `type` | Key from the configured ticket types (seeded from `property.json` `support.ticketTypes`; engine defaults `Support`, `Bug`, `Enhancement`, `Billing`) |
| `status` | `Open`, `InProgress`, `WaitingOnMember`, `Resolved`, `Closed` |
| `priority` | `Low`, `Normal`, `High`, `Urgent` (staff set; members see it read-only) |
| `device` | Captured from the request headers at creation (`X-Client`, `X-Client-Version`, `X-Client-Update-Id`, and the optional `X-Client-Platform`, `X-Client-Os-Version`, `X-Client-Device-Model` the mobile app sends on ticket creation only). Shown to the requester ("We attached your app version") and staff |
| `membership` | Staff only: a snapshot of the requester's membership at creation and now |
| `kind` (message) | `Reply` (visible to the requester) or `Note` (staff only; never returned to members, never emailed) |
| `authorRole` | `Member` or `Staff` (members see staff replies as from the property's support team name plus the agent's display name, per `support.showAgentNames`, default true) |

### 2.4 Idea

```json
{
  "id": "ida_...",
  "title": "Dark mode for the website",
  "body": "Markdown subset.",
  "author": { "...": "UserSummary or null (staff-published ideas from tickets may hide the requester)" },
  "status": { "key": "Planned", "label": "Planned", "color": "#2E7D32", "isClosed": false },
  "statusNote": "Scheduled for the next release.",
  "statusUtc": "...",
  "voteCount": 57,
  "hasVoted": true,
  "mergedInto": null,
  "commentTarget": { "type": "Idea", "id": "ida_..." },
  "commentCount": 12, "reactionCounts": {}, "reactionTotal": 0, "myReactions": [],
  "audience": "Members",
  "locked": false,
  "labels": [], "tags": {},
  "viewer": { "...": "1.4" },
  "createdUtc": "...", "updatedUtc": "..."
}
```

Statuses are configurable (`ideas.statuses` in `property.json`, engine defaults `UnderReview`, `Planned`, `InProgress`, `Shipped`, `Declined`, with `isClosed` true for the last two). Voting on a closed status is allowed only when `ideas.voteOnClosed` (false).

### 2.5 Broadcast

```json
{
  "id": "bct_...",
  "title": "Holiday schedule",
  "channels": { "inApp": true, "push": true, "email": true },
  "inApp": { "title": "Holiday schedule", "body": "Markdown subset, shown in the announcement view.", "path": "/announcements/bct_..." },
  "push": { "title": "Holiday schedule", "body": "Opening hours change next week." },
  "email": { "subject": "Holiday schedule", "preheader": "What changes next week", "body": "Markdown subset", "ctaLabel": "Read more", "ctaPath": "/announcements/bct_..." },
  "segment": { "...": "BroadcastSegment (inline or saved id)" },
  "category": "Optional",
  "essentialReason": null,
  "status": "Scheduled",
  "sendAtUtc": "2026-12-20T17:00:00Z",
  "stats": { "targeted": 812, "inAppCreated": 0, "pushSent": 0, "pushFailed": 0, "emailQueued": 0, "emailSent": 0, "emailDelivered": 0, "emailFailed": 0, "emailSuppressed": 0, "skippedPreferences": 0 },
  "createdBy": { "...": "UserSummary" },
  "labels": [], "tags": {},
  "createdUtc": "...", "updatedUtc": "...", "sentUtc": null
}
```

---

## 3. Direct messages (REST)

### 3.1 Routes

| Method and path | Feature, action | Body | Success | Errors |
|---|---|---|---|---|
| `POST /api/v1/conversations/enumerate` (and `GET /api/v1/conversations`) | messaging, List | `EnumerationQuery`; ordering `LastMessageDescending` (default), `CreatedDescending`; filters `folder` (default `Inbox`), `unreadOnly`, `withUserId`; `search` over the other member's display name and handle only | `200 EnumerationResult<Conversation>` | |
| `POST /api/v1/conversations` | messaging, Write | `{ "recipientUserId": "usr_...", "message"?: { "clientMessageId", "body", "mediaIds" } }` | `201 { conversation, message? }` when new; `200` with the existing conversation (and the message appended) when one exists | `400 CannotMessageSelf`, `403 MessagingNotAllowed`, `403 RecipientUnavailable`, `403 MessagingRestricted`, `404`, `429` |
| `GET /api/v1/conversations/{id}` | messaging, Read | | `200 Conversation` | `404` |
| `POST /api/v1/conversations/{id}/messages/enumerate` (and `GET`) | messaging, List | Section 3.4 | `200 EnumerationResult<Message>` | `404` |
| `POST /api/v1/conversations/{id}/messages` | messaging, Participate | `{ "clientMessageId": "uuid", "body": "...", "mediaIds": [], "replyToMessageId"?: "msg_...", "showLinkPreview": true }` | `201 Message` (or `200` with the original for a repeated `clientMessageId`) | `400 MessageEmpty`, `400 ContentFiltered`, `400 MediaLimitExceeded`, `403 MessagingNotAllowed`, `403 RecipientUnavailable`, `403 RequestLimitReached`, `403 MessagingRestricted`, `403 MediaNotAllowed`, `409 MediaNotAttachable`, `429` |
| `PUT /api/v1/messages/{id}` | messaging, Participate | `{ "body": "..." }` | `200 Message` | `409 EditWindowClosed`, `403`, `400 ContentFiltered` |
| `DELETE /api/v1/messages/{id}` | messaging, Participate | | `200 Message` (tombstone) | `403` (not the author) |
| `POST /api/v1/conversations/{id}/read` | messaging, Read | `{ "seq": 418 }` | `200 { "myLastReadSeq": 418, "unreadCount": 0 }` (monotonic: a lower `seq` is ignored) | |
| `POST /api/v1/conversations/{id}/delivered` | messaging, Read | `{ "seq": 418 }` | `204` (REST twin of the WebSocket `ack`) | |
| `POST /api/v1/conversations/{id}/accept`, `.../decline` | messaging, Write | | `200 Conversation` | `409` when not a pending request to the caller |
| `PUT /api/v1/conversations/{id}/folder` | messaging, Write | `{ "folder": "Inbox" \| "Archived" }` | `200 Conversation` | |
| `PUT /api/v1/conversations/{id}/mute`, `DELETE` same | messaging, Write | `{ "untilUtc"?: null }` | `200 Conversation` | |
| `DELETE /api/v1/conversations/{id}` | messaging, Write | | `204`: removes the conversation from the caller's lists and hides every message before now for the caller only; a new message from the other member brings it back with only newer messages | |
| `PUT`, `DELETE /api/v1/messages/{id}/reactions/{key}`; `POST /api/v1/messages/{id}/reactions/enumerate` | messaging, Participate / Read | Phase 2 reaction shapes (the property reaction set) | Phase 2 | `403 MessagingNotAllowed` across blocks |
| `POST /api/v1/conversations/changes/enumerate` | messaging, List | Section 5.2 | `200 EnumerationResult<ConversationChange>` | `400 InvalidContinuationToken` |
| `GET /api/v1/me/messaging/unread-count` | messaging, Read | | `200 { "conversations": 3, "messages": 11, "requests": 1 }` | |
| `GET`, `PUT /api/v1/me/messaging/settings` | account (SignedIn) | `{ dmPolicy?, readReceipts?, typingIndicators?, pushPreview? }` | `200` settings | `400 ValidationFailed` |
| `POST /api/v1/reports` | account | `{ targetType: "Message" \| "Conversation", targetId, reason, details }` | Section 3.9 | |

Every route is declared in `AccessRoutes.cs` (feature `messaging`, the action shown, never signed-out eligible) and covered by the cross-property isolation suite.

### 3.2 Starting conversations and requests

- **One direct conversation per pair:** `conversations.directkey` is the two user ids sorted and joined; unique per property. `POST /conversations` returns the existing one (un-deleting it for the caller).
- **Eligibility** (checked in this order, all failures `403 MessagingNotAllowed` unless noted): sender has messaging access (0.3) and `canSendDirectMessages`; sender not muted (`403 MessagingRestricted`); recipient exists in the property and has messaging access (`403 RecipientUnavailable`); no block in either direction; recipient's `dmPolicy` allows the sender (staff always pass); new-account limits (section 13, `403 MessagingRestricted`).
- **Requests:** when the recipient has never sent a message to the sender and has not accepted them before ("known contact"), and the sender is not staff, the conversation lands in the recipient's `Requests` folder. The recipient gets a quiet in-app `MessageRequest` notification (no push, no email) and a requests badge count. Until the recipient accepts or replies, the sender may send at most `messaging.requestMessageLimit` (3) messages (`403 RequestLimitReached`), media is not delivered to the recipient (shown as "Attachment hidden until you accept"), link previews are not fetched, and typing and read receipts are not exchanged. Replying accepts implicitly.
- **Decline:** the conversation moves out of the recipient's lists; the sender is not told and still sees their messages as sent; further sends return `403 MessagingNotAllowed`. The recipient can undo from Settings, Messaging, Declined requests (bounded enumeration).
- **Blocks:** blocking (Phase 2 `POST /users/{id}/block`, also offered from the conversation and the request) stops sending in both directions immediately, hides the conversation from the blocker's lists, and ends typing and receipts. Unblocking restores the conversation (history kept). The blocked member is never told; their sends fail with `403 MessagingNotAllowed`.

### 3.3 Sending, ordering, and idempotency

1. The client generates `clientMessageId` (UUID v4) once per composed message and reuses it on every retry.
2. The server validates, runs the word filter (`Block` terms reject with `400 ContentFiltered`; `Flag` terms do not apply to direct messages, so private messages never generate system reports), and in one transaction: locks the conversation row (`SELECT ... FOR UPDATE`), increments `lastseq`, inserts the message with that `seq`, encrypts the body (section 3.8), attaches media, updates both members' `updatedutc` and the sender's `lastreadseq` and `lastdeliveredseq` to the new `seq`, and issues `pg_notify` for the realtime bus (section 4.11, delivered only on commit).
3. A repeated `(conversationId, authorUserId, clientMessageId)` returns `200` with the stored message and does nothing else. This makes REST retries after timeouts safe.
4. After commit: push decision (section 4.9), link preview fetch (when allowed, asynchronously; the message is updated and `message.updated` sent), media attachment checks.

Ordering guarantee: within a conversation, `seq` is a total order consistent with commit order; clients render by `seq`, never by `createdUtc` or arrival order. Across conversations there is no ordering guarantee.

### 3.4 History (bounded enumeration)

`POST /api/v1/conversations/{id}/messages/enumerate` with `EnumerationQuery`:

| Field | Rule |
|---|---|
| `ordering` | `SeqDescending` (default: newest first, for opening a conversation) or `SeqAscending` (for catching up) |
| `maxResults` | Default 50, maximum 100 (member maximum) |
| `filters.beforeSeq`, `filters.afterSeq` | Exclusive bounds; `afterSeq` with `SeqAscending` is the gap-fill and reconnect query |
| `filters.aroundSeq` | Enumerates an independently bounded neighborhood: at most 50 messages at or before the target and 50 after it. Standard `maxResults`, `skip`, ordering and continuation apply within that neighborhood; clients drain these pages before centering a replied-to message |
| `continuationToken` | Keyset over `seq` |
| `search` | Not supported in Phase 5 (`400 ValidationFailed`); open question 8 |

Messages hidden for the caller (deleted conversation for them) are excluded; tombstones are included (so `seq` stays contiguous for gap detection). Labels and tag filters apply to the caller's own metadata.

### 3.5 Edits, deletes, reactions, replies

- **Edit:** the author may edit within `messaging.editWindowMinutes` (15); the message shows "Edited"; only the latest text is stored. Attachments cannot be edited.
- **Delete for everyone:** the author may delete any time. The body and attachments are purged immediately (media deleted after the retention rule of 3.6), `state: Deleted`, `message.deleted` event to both members, push notifications already delivered cannot be recalled.
- **Reactions:** the property's reaction set, one per key per member per message, `message.updated` carries new counts. No reactions across blocks.
- **Replies:** `replyToMessageId` must be a message in the same conversation; the quoted excerpt follows the quoted message's current state.

### 3.6 Retention

| Data | Kept until |
|---|---|
| Messages | Deleted by their author, or the author's account is deleted, or `messaging.retentionDays` passes (0 = forever, the default; a property may choose an automatic retention period, applied by `MessageRetentionWorker`) |
| A conversation deleted by one member | Hidden for that member only; rows remain for the other member. When every member has deleted it (or deleted their account), the conversation and its messages are purged after 30 days |
| Tombstones | Kept as rows without body and attachments, so sequence numbers stay contiguous; purged with the conversation |
| DM media | Deleted 30 days after its message is deleted or purged (a short grace period for in-flight report snapshots); originals are not kept beyond that, unlike feed media (direct messages are private) |
| Report snapshots | `moderation.retainRemovedDays` (365) after the report is resolved, then purged |
| Realtime connection rows | Deleted on disconnect; stale rows pruned after 5 minutes |
| Push payloads | Never stored beyond the Phase 2 delivery record (title only, no body) |

Account deletion: the deleted member's messages become tombstones ("Deleted member" as author, body and media purged) in every conversation; the other member keeps their own messages.

### 3.7 Media in direct messages

Direct message media must not be readable by every member, so it does **not** use the member-wide media grant cookie:

- Uploads use the Phase 2 upload sessions with `purpose: "Message"`; storage keys are `p/<propertyId>/dm/<conversationId>/<mediaId>/...` in the images and transcoded buckets. The CloudFront signed cookie policy of `POST /media/grant` covers `p/<propertyId>/media/*` and `p/<propertyId>/video/*` only, never `dm/`.
- Message payloads carry per-object signed URLs (CloudFront signed URLs with a custom policy for `p/<propertyId>/dm/<conversationId>/<mediaId>/*`, valid `messaging.mediaUrlMinutes` (60)); for HLS the policy covers the playlist and segments. Locally, the media file route checks that the caller is a member of the conversation.
- Clients refresh URLs by re-reading the message (`GET` through the history enumeration) when a URL returns `403`.
- Images are re-encoded without EXIF as in Phase 2; video follows the member video caps.

### 3.8 Privacy and encryption (statement for the privacy policy)

The following is stated plainly so a property's privacy policy can describe it accurately:

1. **Not end-to-end encrypted.** Direct messages are encrypted in transit (TLS) and at rest, but the service operator holds the keys. A person with operator access to the production systems and keys could technically read messages.
2. **Encryption at rest, two layers.** The database and object storage are encrypted at rest by the hosting provider (for example RDS and S3 encryption with managed keys). In addition, message bodies are encrypted at the application level with AES-256-GCM under a per-property data key (`IMessageProtector`, the same envelope mechanism as `ISecretProtector`, keys derived from the engine secret key and rotated through `secrets.previousEncryptionKeys` with a background re-encryption pass), so database backups, snapshots, and query logs never contain readable message text. Attachments rely on storage encryption and private, per-object signed URLs.
3. **Staff access.** No staff tool lists, searches, or opens direct messages. Staff see a message only when a participant reports it, and then only the frozen report snapshot (the reported message and up to `messaging.reportContextMessages` (10) earlier messages of that conversation that the reporter could see), and every view of a snapshot is audited (`message.snapshot.view`). Platform and property administrators have the same restriction; delegated platform sessions do too.
4. **Legal requests.** Producing messages for a valid legal request requires an operator procedure outside the product (documented in the runbook, audited), not an admin console feature.
5. **Not used for AI or search.** Direct messages are never assistant knowledge, never indexed in search, and never used to train models (`AI_CONTRACT.md` section 7.4).
6. **Notifications.** Push notifications travel through the device platform providers (Apple and Google, via the Expo push service). By default they contain only the sender's name, not the message text (`pushPreview: SenderOnly`); members can choose `Full` or `Hidden`. Email notices about unread messages never contain message text.
7. **Link previews.** When a message contains a link, the server (not the recipient's device) may fetch the page to build a preview, so the linked site sees a request from the service, not from the member.
8. **Retention** is as section 3.6. Deleting a message deletes it for both members.
9. **Logs and request history.** Request capture stores no message bodies (bodies of `/conversations*` and `/messages*` requests and responses are replaced by `[redacted]`), and the server never logs message text.

### 3.9 Reports and moderation

- `POST /api/v1/reports` with `targetType: "Message"` (a message in a conversation the reporter belongs to) or `"Conversation"` (the whole conversation, for harassment patterns). Reasons are Phase 2's.
- Creating the report writes a `MessageReportSnapshot`: decrypted copies of the reported message (or, for a conversation report, the latest messages) and up to `messaging.reportContextMessages` (10) earlier messages the reporter could see, including tombstones as placeholders, plus attachment references, re-encrypted under the snapshot key. Later edits or deletes do not change the snapshot.
- The report is stored with `scopetype: "Messaging"`; only `SystemAdmin` and Messaging moderators see it in the queue, and the queue row shows the snapshot, not a link into the conversation.
- Actions (`POST /api/v1/admin/moderation/actions`): `Remove` (the message becomes `Removed` for both members, `message.updated`), `Restore`, plus member actions on the author: warn (a `ModerationAction` notification), `RevokeMessaging` (sets `canSendDirectMessages` false, audited), mute, ban (`SystemAdmin`). Auto-hide does not apply (in a two-person conversation the reporter can block).
- After reporting, clients offer "Block @handle" (Phase 2 rule) and the reporter's conversation stays intact.

---

## 4. Real-time protocol (WebSocket)

### 4.1 Endpoint and transport

- `GET /api/v1/ws` (Watson 7 WebSocket route, `WebserverBase.WebSocket`), upgrade to WebSocket (RFC 6455), subprotocol `tk.v1` (the server rejects other subprotocols with `400` before upgrade). One connection per running app instance carries every live event: messages, receipts, typing, notifications, unread counts, ticket updates.
- Production URL: `wss://<api domain>/api/v1/ws` (for example `wss://api.example.test/api/v1/ws`). The website connects to the API domain directly (CloudFront in front of the website also supports WebSockets, but a direct ALB connection avoids CloudFront's idle limits; open question 11).
- Text frames only, UTF-8 JSON. Binary frames close the connection with `1003`. Inbound frames at most `realtime.maxInboundFrameBytes` (16 KB, close `1009`); outbound frames at most 64 KB (larger events are sent as references and fetched over REST).
- Compression: `permessage-deflate` is not negotiated in Phase 5 (CPU cost and compression side-channel risk with mixed secret and attacker-controlled content).

### 4.2 Authentication

1. **Upgrade request.** The property is resolved exactly as for REST (`MULTIPROPERTY_CONTRACT.md` section 2): `X-TK-Property` and `X-TK-App` headers from mobile apps (React Native WebSocket accepts headers), or the `Host` header. Browsers cannot set headers on WebSocket upgrades, so the website additionally sends the `Origin` header, which must be one of the property's verified web domains (`403` before upgrade otherwise; this also defends against cross-site WebSocket hijacking). An unresolved or disabled property gets `404 PropertyNotFound` before upgrade. No credentials are sent on the upgrade URL, so tokens never appear in load balancer access logs.
2. **`hello` frame.** Within `realtime.authTimeoutSeconds` (10) of opening, the client sends:

```json
{ "type": "hello", "id": "c1", "data": { "accessToken": "...", "propertyId": "app_...", "client": "Mobile", "clientVersion": "1.4.0", "installId": "...", "appState": "Foreground", "resume": { "syncToken": "opaque-or-null" }, "capabilities": ["messaging", "notifications", "tickets"] } }
```

   The server validates the access token exactly like a bearer token (session active, property-bound: a token of another property is rejected, age attestation), checks `propertyId` against the resolved property (`4404` on mismatch), and replies `welcome`. Failure: `error` then close `4401`. No `hello` in time: close `4408`.
3. **Token refresh.** Access tokens live 60 minutes. The server sends `auth.expiring` 5 minutes before the token's expiry; the client obtains a new access token through the normal refresh route and sends `{ "type": "auth.refresh", "data": { "accessToken": "..." } }` (same session required). If the token expires without a refresh, the server closes `4401`.
4. **Revocation.** Sign-out, session revocation, password change, ban, account deletion, and property disable publish a `session.revoked` bus event; every task closes that session's sockets with `4401` within `realtime.revocationSeconds` (5).
5. **Access changes.** Membership changes (entitlement granted or lapsed), access model edits, and `canSendDirectMessages` changes publish `access.changed`; the server re-evaluates the connection's capabilities and sends `access.changed` with the new set, so clients update composers without reconnecting.

### 4.3 Frame envelope

```json
{ "type": "message.created", "id": "s-91822", "ts": "2026-11-20T18:02:00.123Z", "data": { } }
```

`type` is required; `id` is a client frame id (echoed in `ack` and `error` replies) or a server event id (unique per connection, increasing, for logging); `ts` is server time on server frames. Unknown `type` values from the server are ignored by clients (forward compatibility); unknown client types get `error` with `UnknownFrame`.

### 4.4 Client to server frames

| `type` | `data` | Notes |
|---|---|---|
| `hello` | 4.2 | First frame only |
| `auth.refresh` | `{ accessToken }` | 4.2 |
| `ping` | `{}` | Application keepalive; server replies `pong`. Clients send one when no frame was received for 30 seconds |
| `state` | `{ appState: "Foreground" \| "Background", activeConversationId?: "cnv_..." }` | Drives the push decision (4.9); `activeConversationId` suppresses pushes and in-app banners for the open conversation |
| `typing` | `{ conversationId, state: "Start" \| "Stop" }` | At most one `Start` per conversation per `realtime.typingMinIntervalMs` (3000); ignored for requests, blocks, or when either member disabled typing indicators |
| `ack` | `{ conversationId, seq }` | Delivery acknowledgement: the client has stored messages up to `seq`; updates `lastdeliveredseq` (monotonic, batched every second) |
| `read` | `{ conversationId, seq }` | Same as `POST .../read` |

Sending messages is REST only (section 3.1): it needs idempotency, rate limits, media checks, and a status code, and keeping one write path avoids two implementations. The socket is the delivery channel.

### 4.5 Server to client frames

| `type` | `data` | When |
|---|---|---|
| `welcome` | `{ connectionId, userId, serverTime, heartbeatSeconds, maxInboundFrameBytes, capabilities: ["messaging", ...], resume: { accepted, syncRequired } }` | After a valid `hello` |
| `pong` | `{}` | Reply to `ping` |
| `message.created` | `{ conversationId, message: Message }` | New message in a conversation the member belongs to (including their own, for other devices) |
| `message.updated` | `{ conversationId, message }` | Edit, reaction counts, link preview ready, moderation `Removed` |
| `message.deleted` | `{ conversationId, messageId, seq }` | Author deleted for everyone |
| `conversation.created`, `conversation.updated` | `{ conversation: Conversation }` | New conversation, request accepted or declined, folder or mute change on another device, member access change |
| `receipt.delivered`, `receipt.read` | `{ conversationId, userId, seq }` | Other member's positions (only when both members allow receipts; the member's own other devices always get their own `receipt.read` to sync unread state) |
| `typing` | `{ conversationId, userId, state, expiresInMs: 6000 }` | Other member typing; clients clear the indicator after `expiresInMs` without a refresh |
| `notification.created` | `{ notification: Notification, unreadCount }` | Phase 2 notification for this member (closes the Phase 2 section 8.4 promise) |
| `unread.counts` | `{ notifications, conversations, messages, requests }` | After reads on another device, after resync |
| `ticket.updated` | `{ ticketId, status, lastActivityUtc }` | Staff replied or changed status on the member's ticket (a hint; the client fetches the ticket) |
| `access.changed` | `{ capabilities, accessTier }` | 4.2 step 5 |
| `feature.disabled` | `{ feature, message }` | A kill switch for a feature carried on the socket |
| `auth.expiring` | `{ expiresUtc }` | 4.2 step 3 |
| `resync.required` | `{ reason: "Gap" \| "Overflow" \| "BusReconnected" \| "Resume" }` | The client must run the REST sync (5.2) |
| `error` | `{ code, message, frameId }` | Bad frame, rate limit, unknown type; the connection stays open unless followed by a close |
| `shutdown` | `{ reconnectAfterMs }` | Task draining (4.11); jittered 0 to 5000 ms |

### 4.6 Delivery semantics and ordering

- The socket is **at-most-once, best effort**; REST is the source of truth. Correctness comes from sequence numbers: each conversation's messages carry contiguous `seq` values, so a client that receives `seq` 420 after 418 knows it missed 419 and fills the gap with `messages/enumerate` (`afterSeq: 418`, `SeqAscending`).
- Within one connection, events for the same conversation are sent in `seq` order (the receiving task orders by `seq` per conversation before writing). Events of different conversations may interleave.
- Duplicates are possible (a gap fill racing a live event); clients de-duplicate by message `id` and replace by `id` on `message.updated`.
- Own messages: the sending device inserts an optimistic message keyed by `clientMessageId`, replaces it with the `201` response, and ignores the echoed `message.created` with the same `clientMessageId`.
- **Delivery receipts** (`lastdeliveredseq`): updated when any of the recipient's devices acks over the socket or fetches the messages over REST. **Read receipts** (`lastreadseq`): updated by `read` frames or `POST .../read` when the conversation is visible on screen. Both are monotonic per member and exposed per 2.1.

### 4.7 Typing

Ephemeral: never stored, never sent over push, not replayed on resume. The server relays a `typing` frame to the other member's connections only when both members allow typing indicators, the conversation is not a pending request, and there is no block. A `Stop` (or a sent message) clears it; otherwise it expires after 6 seconds.

### 4.8 Presence

No presence ("online now", "last seen") is shown to members in Phase 5 (open question 10). The connection registry (4.11) is used only internally to decide whether to push.

### 4.9 Live event versus push

For each new message, per recipient device set:

1. If the recipient has at least one connection in `Foreground` state (registry row updated within 90 seconds): send `message.created` over the socket; no push. If that connection's `activeConversationId` is this conversation, the client shows no banner either.
2. Otherwise: create no in-app notification row (the conversation list is the inbox), and send a push when the `Messages` category push preference is on, push is not paused, the time is inside the recipient's notification hours (open question 12), the conversation is not muted, and the conversation is not a pending request. Pushes carry `data: { type: "DirectMessage", conversationId, seq, path: "/messages/{conversationId}" }`, a per-conversation collapse key (`collapseId` and `threadId` so iOS and Android group them), and at most one push per conversation per `messaging.pushCoalesceSeconds` (60) while unread. The title and body follow the recipient's `pushPreview`. The push `badge` is notifications plus unread conversations.
3. Email: when a message stays unread for `messaging.emailAfterMinutes` (60) and the `Messages` email preference is on (default off, open question 13), `MessageEmailWorker` sends `messages_unread` (sender names and counts only, never text), at most one per member per `messaging.emailMinIntervalHours` (12).

### 4.10 Heartbeats, reconnects, close codes

- **Heartbeat:** the server sends WebSocket ping control frames every `realtime.heartbeatSeconds` (25); two missed pongs close the connection (`1001` is not used; the server simply drops and the client reconnects). Application `ping` frames cover clients whose platform hides control frames. The infrastructure sets the ALB idle timeout to 120 seconds (section 17).
- **Reconnect:** exponential backoff with full jitter, starting at 1 second, doubling to a 30 second cap, reset after a connection survives 60 seconds; immediate reconnect on network change and on app foreground. After reconnect the client sends `hello` with `resume.syncToken` from its last sync and runs the REST sync (5.2) when `welcome.resume.syncRequired` is true (always true in Phase 5: there is no server-side replay buffer, open question 9).
- **Mobile background:** the app closes the socket `1000` after 30 seconds in the background (iOS suspends sockets anyway) and relies on push; it reconnects on foreground.
- **Close codes:**

| Code | Meaning | Client action |
|---|---|---|
| `1000` | Normal | Reconnect only if still needed |
| `1003`, `1009` | Binary frame, frame too large | Fix the client; reconnect with backoff |
| `1011` | Server error | Reconnect with backoff |
| `1012` | Service restart (draining, after `shutdown`) | Reconnect after `reconnectAfterMs` |
| `1013` | Try again later (task over capacity, slow consumer) | Reconnect with backoff |
| `4400` | Protocol violation (frame before `hello`, malformed JSON) | Reconnect with backoff |
| `4401` | Unauthorized (bad, expired, revoked token) | Refresh the access token once and reconnect; if refresh fails, go to sign-in |
| `4402` | Membership required for every requested capability | Do not reconnect until access changes (the app reconnects for notifications without the `messaging` capability) |
| `4404` | Property not found or mismatch | Do not reconnect; report |
| `4408` | `hello` timeout | Reconnect with backoff |
| `4409` | Replaced: the member exceeded `realtime.maxConnectionsPerUser`; the oldest connection is closed | Do not reconnect from this instance unless the app is in the foreground |
| `4429` | Rate limited | Reconnect after the `retryAfterMs` sent in the preceding `error` |
| `4503` | Feature disabled (`realtime` kill switch) | Fall back to polling (section 5); retry the socket after 5 minutes |

### 4.11 Horizontal scaling across ECS tasks

Each API task (ECS Fargate, two or more in production) holds its own sockets. Events cross tasks through a bus behind `IRealtimeBus`:

- **Default implementation: PostgreSQL `LISTEN`/`NOTIFY`.** Each task opens one dedicated, deployment-level Npgsql connection (on the reviewed unbound allow-list, `MULTIPROPERTY_CONTRACT.md` section 4.3 rule 1) and runs `LISTEN tk_realtime`. Writers call `SELECT pg_notify('tk_realtime', $payload)` inside the same transaction as the write, so a notification is delivered only if the write commits and never before it.
- **Payload** (at most 1 KB, well under PostgreSQL's 8000 byte limit): references only, never message text or personal data: `{ "v": 1, "p": "<propertyId>", "k": "message.created", "c": "cnv_...", "s": 418, "m": "msg_...", "u": ["usr_a", "usr_b"], "e": "<eventId>" }`. `u` lists recipient user ids (bounded by the conversation size; fan-out kinds such as `session.revoked` carry one user or session id).
- **Receiving task:** filters to recipients with local sockets (an in-memory index by property and user); if none, drops the event. Otherwise it loads the referenced rows once through a property-bound driver (one batched read per event, cached for 2 seconds by id so concurrent events do not repeat reads), checks each recipient's access again, decrypts, renders the per-recipient payload (receipt visibility, request rules), and enqueues frames.
- **Missed notifications:** `NOTIFY` is not durable. When the listener connection drops and reconnects, the task sends `resync.required` (`BusReconnected`) to all its sockets, and clients run the REST sync. Sequence gaps catch anything else.
- **Throughput:** PostgreSQL's notification queue (8 GB by default) and one listener per task are ample for the expected load (thousands of concurrent members); `trelliskit.realtime.bus.lag` and queue usage (`pg_notification_queue_usage()`) are monitored with an alarm at 50 percent.
- **Alternatives:** `IRealtimeBus` has `InProcess` (single task, tests, local development without PostgreSQL listeners) and is the seam for a future Redis or SNS implementation (setting `realtime.bus`, Restart). No sticky sessions are needed at the load balancer.
- **Connection registry:** `realtimeconnections (propertyid, id, userid, sessionid, taskid, appstate, activeconversationid, connectedutc, lastseenutc)` is upserted on `hello` and `state` and touched every 60 seconds; rows not seen for 90 seconds count as offline and are pruned after 5 minutes by `RealtimeRegistryPruneWorker`. The push decision (4.9) reads it, so a member connected to task A suppresses pushes sent from a write on task B.
- **Draining:** on SIGTERM the task stops accepting upgrades (health check reports draining), sends `shutdown` with a jittered `reconnectAfterMs`, closes sockets with `1012` over up to 20 seconds, deletes its registry rows, and exits; the ECS deregistration delay is 30 seconds.

### 4.12 Backpressure and flow control

- **Outbound queue per connection:** at most `realtime.maxQueuedFrames` (256) frames or `realtime.maxQueuedBytes` (1 MB). On overflow the server drops queued `typing` and `receipt.*` frames first; if still over, it clears the queue, sends one `resync.required` (`Overflow`), and continues. A connection whose writes are blocked for more than `realtime.slowConsumerSeconds` (30) is closed with `1013`.
- **Inbound limits per connection:** `realtime.maxInboundFramesPerSecond` (20, burst 40); `typing` at most one per conversation per 3 seconds; violations receive `error` (`RateLimited`, with `retryAfterMs`) and, after `realtime.maxViolations` (5) within a minute, close `4429`.
- **Fan-out bound:** a single event is delivered to at most `realtime.maxRecipientsPerEvent` (Direct: 2 members and their devices); bulk notification fan-outs (feed posts, broadcasts) do not emit one bus event per recipient: they emit `unread.counts` lazily (the recipient's next interaction) to protect the bus.

### 4.13 Limits

| Limit | Value | Setting |
|---|---|---|
| Connections per member | 10 (oldest closed with `4409`) | `realtime.maxConnectionsPerUser` |
| Connections per task | 10000 (upgrades beyond return `503` with `Retry-After`) | `realtime.maxConnectionsPerTask` |
| New connections per member | 30 per minute | `realtime.connectsPerMinute` |
| Unauthenticated upgrades per IP | 60 per minute, 50 concurrent pending `hello` | `realtime.anonymousUpgradesPerMinute` |
| Inbound frame size, frames per second | 16 KB, 20 | as 4.12 |
| `hello` timeout, heartbeat | 10 s, 25 s | `realtime.authTimeoutSeconds`, `realtime.heartbeatSeconds` |

### 4.14 Local development

The Docker stack's nginx proxies `/api/v1/ws` with `proxy_http_version 1.1`, `Upgrade` and `Connection` headers, and `proxy_read_timeout 120s`; the website dev server proxies WebSockets the same way. With one server container the bus runs on PostgreSQL `LISTEN`/`NOTIFY` as in production (the default), so the multi-task path is exercised by running two server containers in a test profile (`compose.yaml` profile `realtime-scale`).

---

## 5. REST fallbacks

### 5.1 When the socket is unavailable

Clients treat the socket as an optimization. When it is closed with `4503`, fails to connect three times in a row, or the network blocks WebSockets:

| What | Fallback |
|---|---|
| Unread badges | `GET /api/v1/notifications/unread-count` and `GET /api/v1/me/messaging/unread-count` every 60 seconds while visible (Phase 2 section 8.4 cadence) |
| Open conversation | `POST .../messages/enumerate` with `afterSeq` every `messaging.pollSeconds` (5) while the screen is visible, backing off to 30 seconds after 2 minutes without new messages |
| Conversation list | Sync (5.2) every 30 seconds while visible |
| Typing, presence | Not available |
| Receipts | Read from the conversation payload on each poll |

### 5.2 Sync

`POST /api/v1/conversations/changes/enumerate`:

```json
{ "maxResults": 100, "syncToken": "<saved watermark or null>", "continuationToken": null }
```

Returns `EnumerationResult<ConversationChange>` where each item is `{ conversation: Conversation, change: "Updated" | "Removed" }` for every conversation of the caller whose state changed after the token (new messages, receipts, folder, mute, removal), ordered by change time then id, bounded by `maxResults` (at most 100). The final page has `continuationToken: null` and a separate `syncToken` the client stores and sends in `hello.resume`; nonterminal pages use ordinary continuation tokens. A null token returns the caller's most recent conversations (first sync). Tokens older than `messaging.syncTokenDays` (30) return `400 InvalidContinuationToken`, and the client starts fresh. For each changed conversation the client fetches messages with `afterSeq` its last known `seq`.

---

## 6. Support tickets

### 6.1 Member routes (`support` feature, account routes: SignedIn in every model)

| Method and path | Body | Success | Errors |
|---|---|---|---|
| `POST /api/v1/tickets` | `{ "type": "Billing", "title": "...", "body": "...", "mediaIds": [], "includeDiagnostics": true, "labels"?, "tags"? }` | `201 Ticket` | `400 ValidationFailed`, `400 ContentFiltered` (only `Block` terms; no `Flag` reports on tickets), `429` |
| `POST /api/v1/tickets/enumerate` (and `GET`) | `EnumerationQuery`; ordering `LastActivityDescending` (default), `CreatedDescending`; filters `status`, `type` | `200 EnumerationResult<TicketSummary>` (own tickets) | |
| `GET /api/v1/tickets/{id}` | | `200 Ticket` (own; notes excluded) | `404` |
| `POST /api/v1/tickets/{id}/messages/enumerate` | `EnumerationQuery` (`CreatedAscending` default) | `200 EnumerationResult<TicketMessage>` (replies only) | |
| `POST /api/v1/tickets/{id}/messages` | `{ "body": "...", "mediaIds": [] }` | `201 TicketMessage`; a reply on `WaitingOnMember` or `Resolved` moves the ticket to `Open` | `409 TicketClosed` |
| `POST /api/v1/tickets/{id}/close`, `.../reopen` | | `200 Ticket` (reopen within `support.reopenDays`, 14) | `409 TicketClosed` |
| `POST /api/v1/support/ticket-types/enumerate` | `EnumerationQuery` | `200 EnumerationResult<TicketType>` (enabled types with description and the help text shown above the form) | |

Rules:
- Title 3 to 140; body 10 to `support.maxBodyLength` (10000), Markdown subset; at most `support.maxAttachments` (5) images (and, when `support.allowVideo`, one video), uploaded with `purpose: "Ticket"` into `p/<propertyId>/support/<ticketId>/...` and served only through per-object signed URLs to the requester and support staff (never through the member-wide grant cookie, as in section 3.7).
- `includeDiagnostics` (default true on mobile, false on web) attaches the `device` block; members can turn it off.
- Mobile captures `X-Client-Platform`, `X-Client-Os-Version`, `X-Client-Device-Model` only on `POST /tickets`.
- A member may have at most `support.maxOpenTicketsPerMember` (10) tickets not `Closed`.
- Signed-out visitors (for example someone who cannot sign in) use the property's support email shown on the public support page; there is no anonymous ticket API in Phase 5 (open question 16).

### 6.2 Staff routes (`SystemAdmin`, `SupportAgent`)

| Method and path | Body | Success |
|---|---|---|
| `POST /api/v1/admin/tickets/enumerate` | `EnumerationQuery` (admin maximum); orderings `LastActivityDescending`, `CreatedAscending`, `DueAscending`, `PriorityDescending`; filters `status` (default not `Closed`), `type`, `priority`, `assigneeUserId`, `unassigned`, `requesterUserId`, `overdue`, `hasIdea`; `search` over number, title, body, requester name and email (staff-only index, never global search) | `200 EnumerationResult<AdminTicketSummary>` |
| `GET /api/v1/admin/tickets/{id}` | | `200 Ticket` with notes and the membership snapshot |
| `POST /api/v1/admin/tickets/{id}/messages` | `{ "kind": "Reply" \| "Note", "body", "mediaIds", "setStatus"?: "WaitingOnMember" }` (a staff `Reply` sets `WaitingOnMember` unless `setStatus` says otherwise; a `Note` changes nothing) | `201 TicketMessage` |
| `PUT /api/v1/admin/tickets/{id}` | `{ status?, priority?, type?, assigneeUserId?, labels?, tags? }` | `200 Ticket` |
| `POST /api/v1/admin/tickets/{id}/publish-idea` | `{ "title"?, "body"?, "showRequester": false, "audience"? }` (Enhancement tickets) | `201 Idea`; the ticket links the idea and the requester automatically votes |
| `POST /api/v1/admin/tickets/{id}/link-idea` | `{ "ideaId" }` | `200 Ticket`; the requester votes for the idea |
| `GET /api/v1/admin/tickets/export.csv` | filters as enumerate | CSV of ticket metadata (no bodies) |
| `GET /api/v1/admin/metrics/support?days=30` | | `{ openedByDay, resolvedByDay, firstResponseMedianHours, overdueNow, byType: { objects (max 20), totalRecords, enumerate } }` |
| `POST /api/v1/admin/support/ticket-types` and CRUD, `PUT .../order` (`SystemAdmin`) | | Ticket types; retiring keeps existing tickets |

Workflow rules:
- **First response target:** `support.firstResponseTargetHours` (24) sets `firstResponseDueUtc`; `TicketDueWorker` marks overdue tickets and sends `TicketOverdue` to the assignee (or every support agent when unassigned) once.
- **Auto close:** `Resolved` tickets with no member reply for `support.autoCloseDays` (7) become `Closed`.
- **Assignment:** manual in Phase 5 (open question 17 covers round-robin).
- Every status, priority, type, and assignment change writes an audit record (`ticket.update`) and a system line in the staff timeline; members see only status changes.

### 6.3 Notifications and email

| Event | Member | Staff |
|---|---|---|
| Ticket created | Confirmation email `ticket_created` (Essential) with the number | `staff_ticket_opened` email to configured recipients (per type), in-app `TicketOpened` to support staff (category `Support`) |
| Staff reply | `TicketUpdate` notification (category `Account`, push on) and email `ticket_reply` (Essential, catalog row "Support ticket replies"), with the reply text and a link to the app; no reply-by-email in Phase 5 (open question 18) | |
| Status change to `Resolved` or `Closed` | `TicketUpdate` and email `ticket_resolved` | |
| Member reply | | `TicketMemberReplied` to the assignee |
| Overdue | | `TicketOverdue` |

### 6.4 Privacy

Tickets are visible only to the requester and to `SystemAdmin` and `SupportAgent` holders. They are never assistant knowledge and never in global search. Account deletion closes the member's open tickets and anonymizes them (requester "Deleted member", bodies kept for `support.retainAfterDeletionDays` (90) for billing disputes, then purged; open question 19). Export includes the member's tickets and replies (not internal notes).

---

## 7. Ideas board

### 7.1 Member routes (`ideas` feature)

| Method and path | Feature, action, signed-out | Body | Success | Errors |
|---|---|---|---|---|
| `POST /api/v1/ideas/enumerate` (and `GET`) | ideas, List, yes | `EnumerationQuery`; orderings `Top` (votes, default), `Trending` (votes in the last `ideas.trendingDays` 14), `CreatedDescending`, `UpdatedDescending`, `Relevance`; filters `status` (keys), `open` (not closed status), `votedByMe`, `authorUserId`, `audience`; merged ideas excluded unless `includeMerged` | `200 EnumerationResult<IdeaSummary>` | |
| `GET /api/v1/ideas/{id}` | ideas, Read, yes | | `200 Idea` (a merged idea returns its own payload with `mergedInto`) | `404` |
| `POST /api/v1/ideas` | ideas, Write, no | `{ title, body, labels?, tags? }` (only when `ideas.memberSubmissions` is `Direct`) | `201 Idea` with the author's vote | `403 Forbidden` when submissions are staff only, `400 ContentFiltered`, `429` |
| `PUT /api/v1/ideas/{id}` | ideas, Write, no | Author: title, body, labels, tags while the status is the initial status; staff any time | `200 Idea` | |
| `PUT`, `DELETE /api/v1/ideas/{id}/vote` | ideas, Participate, no | | `200 Idea` (idempotent) | `409 IdeaMerged`, `409` on a closed status unless `ideas.voteOnClosed` |
| Idea comments and reactions: `POST /api/v1/ideas/{id}/comments/enumerate`, `POST .../comments`, `PUT`/`DELETE .../reactions/{key}` | ideas, List / Participate | Phase 2 shapes | Phase 2 | `409 IdeaMerged` for new comments |
| `PUT`, `DELETE /api/v1/ideas/{id}/follow` | ideas, Participate, no | | `200 { isFollowed }` (voting follows automatically) | |

Votes are one per member per idea, upvote only; counts are public, voters are not (staff see the count and, for planning, an export of voter count by membership state, never names). `Duplicate` handling: staff merge (`POST /api/v1/admin/ideas/{id}/merge` with `{ intoIdeaId }`): votes move to the target (de-duplicated), comments stay on the merged idea (read-only) with a banner, followers follow the target.

### 7.2 Staff routes (`SystemAdmin`, `SupportAgent`)

`POST /api/v1/admin/ideas/enumerate` (adds `hidden`, `merged` filters), `POST /api/v1/admin/ideas` (staff-created), `PUT /api/v1/admin/ideas/{id}`, `PUT /api/v1/admin/ideas/{id}/status` (`{ status, statusNote (0 to 1000), notifyVoters: true }`), `POST .../merge`, `POST .../unmerge`, `DELETE .../{id}` (soft delete), status CRUD and order (`SystemAdmin`). Audit: `idea.create`, `idea.update`, `idea.status`, `idea.merge`, `idea.delete`.

Status changes send `IdeaStatusChanged` to voters and followers (category `Ideas`, aggregated per idea while unread) and, for `Shipped`, an optional email `idea_shipped` (Optional).

### 7.3 Moderation and public board

Reports on ideas and their comments are scoped `Ideas` (handled by `SystemAdmin`, `SupportAgent` for ideas, and forum moderators with `*` for comments when `ideas.forumModeratorsModerate`). Auto-hide follows Phase 2. `POST /api/v1/public/ideas/enumerate` and `GET /api/v1/public/ideas/{id}` serve `Public` ideas when `ideas.publicBoardEnabled` (false), cacheable 60 seconds, rate limited per IP, without votes by viewer or comments.

---

## 8. Broadcasts

### 8.1 Segments

A segment selects recipients with structured filters (stored as `broadcastsegmentfilters` rows, never JSON), combined with AND:

| Filter | Values |
|---|---|
| `membership` | `Active`, `NotActive`, `Any` (only meaningful in paid models) |
| `membershipSource` | `Subscription`, `SignupCode`, `AdminComp`; `store`: `Apple`, `Google`, `Stripe` |
| `profile.<fieldKey>` | Any filterable profile field with the member directory operators (`MULTIPROPERTY_CONTRACT.md` section 9.12): for example a `Rank` field `in` some keys, `country` equals, `region` equals |
| `labels`, `tags` | Member labels and tags, including `staff:` metadata |
| `signedUpAfterUtc`, `signedUpBeforeUtc`, `activeWithinDays`, `inactiveForDays` | Windows |
| `hasPushToken`, `hasVerifiedEmail` | Booleans |
| `role` | Staff roles (for staff-only broadcasts) |
| `eventRsvp` | `{ eventId, status }` (message everyone going to an event, Phase 4) |
| `seriesFollowers` | `{ seriesId }` (Phase 4) |

Recipients are always further limited to accounts that are age attested, not banned, not deleted, and allowed to receive the content under the access model (`ACCESS_MODELS_CONTRACT.md` section 7). `POST /api/v1/admin/broadcasts/segments/preview` returns `{ count, byChannel: { inApp, push, email }, sample: { objects (max 10, UserSummary), totalRecords } }`. Saved segments: `POST /api/v1/admin/broadcasts/segments` and CRUD, enumerate.

### 8.2 Routes (`SystemAdmin`)

| Method and path | Body | Success | Errors |
|---|---|---|---|
| `POST /api/v1/admin/broadcasts/enumerate` | `EnumerationQuery`; filters `status`, `channel` | `200 EnumerationResult<Broadcast>` | |
| `POST /api/v1/admin/broadcasts` | Section 2.5 fields (`segmentId` or inline `segment`) | `201 Broadcast` (`Draft`) | `400 SegmentInvalid` |
| `GET`, `PUT`, `DELETE /api/v1/admin/broadcasts/{id}` | | `200`, `200`, `204` (Drafts only) | `409 BroadcastNotEditable` |
| `POST /api/v1/admin/broadcasts/{id}/test` | `{ "channels": [...] }` | `202`: sends to the caller only, marked "Test" | |
| `POST /api/v1/admin/broadcasts/{id}/schedule` | `{ "sendAtUtc" }` (at least 5 minutes ahead, at most 90 days) | `200 Broadcast` (`Scheduled`) | `409 BroadcastLimitReached` |
| `POST /api/v1/admin/broadcasts/{id}/send` | `{ "confirmCount": 812 }` (must equal the current preview count within 5 percent, to prevent sending to an unexpected audience) | `200 Broadcast` (`Sending`) | `409`, `400 ValidationFailed` |
| `POST /api/v1/admin/broadcasts/{id}/cancel` | | `200 Broadcast` (`Canceled`; remaining batches stop) | |
| `POST /api/v1/admin/broadcasts/{id}/deliveries/enumerate` | `EnumerationQuery`; filters `channel`, `status` | `200 EnumerationResult<BroadcastDelivery>` | |
| `GET /api/v1/announcements/{id}` | Member (any signed-in recipient of the broadcast) | `200 Announcement` (`{ id, title, body, sentUtc, labels, tags }`) | `404` for non-recipients |
| `POST /api/v1/announcements/enumerate` | Member | `200 EnumerationResult<Announcement>` (the caller's received broadcasts) | |

### 8.3 Delivery

- `BroadcastSendWorker` (`LeasedPassWorker`) expands the segment in keyset batches of `broadcasts.batchSize` (500) over user ids, writes `broadcastdeliveries` rows (idempotent per broadcast and user), and for each recipient: creates a `Broadcast` notification (category `Announcements`) when `inApp`; queues a push through `PushDeliveryWorker` when `push` and the recipient's `Announcements` push preference, pause, and notification hours allow; queues an `EmailMessage` (template `broadcast`) when `email` and the recipient's `Announcements` email preference allows (Optional category: suppressions respected, `List-Unsubscribe` and one-click unsubscribe for `Announcements`). Email sending is throttled by `broadcasts.emailsPerMinute` (600) per property.
- **Essential broadcasts:** a `SystemAdmin` may mark a broadcast `category: Essential` with a required `essentialReason` (for example a service or policy notice); it then ignores the `Announcements` preferences (never suppressions) and is audited `broadcast.essential`. Marketing content must not be essential (the console says so).
- No open or click tracking pixels in Phase 5 (privacy; open question 22). Stats come from delivery records, push receipts, and Mailgun delivery webhooks.
- Limits: `broadcasts.maxPerDay` (3) sent per property per UTC day; `broadcasts.maxRecipients` (100000) per broadcast.
- Every create, schedule, send, cancel, and test is audited (`broadcast.*`); the console shows the segment, count, and channels in a confirmation dialog before sending.

---

## 9. Notifications

### 9.1 Types

| `type` | `category` | Trigger | Recipient | Aggregated | `path` |
|---|---|---|---|---|---|
| `DirectMessage` | `Messages` | Push only (no in-app row), section 4.9 | Recipient | Per conversation via collapse keys | `/messages/{conversationId}` |
| `MessageRequest` | `Messages` | A new request | Recipient (in-app only, no push) | Yes, one row while unread | `/messages/requests` |
| `TicketUpdate` | `Account` | Staff reply or status change | Requester | Per ticket while unread | `/support/tickets/{id}` |
| `TicketOpened`, `TicketMemberReplied`, `TicketOverdue` | `Support` (staff) | Section 6.3 | Support staff | Per ticket while unread | admin path |
| `IdeaStatusChanged` | `Ideas` | Status change | Voters and followers | Per idea while unread | `/ideas/{id}` |
| `Broadcast` | `Announcements` | Broadcast sent | Segment | No | `/announcements/{id}` |

`DirectMessage`, `TicketUpdate`, and `Broadcast` were reserved by Phase 2. Existing types extend to idea comments (`PostComment` to the idea's author, `CommentReply`, `Mention`, `Reaction`) and to message moderation (`ModerationAction`). No notifications across blocks; never to the actor.

### 9.2 New preference categories

| `category` | inApp | push | email | Editable |
|---|---|---|---|---|
| `Messages` | n/a (the inbox is the in-app surface; `MessageRequest` rows always appear) | true | false | push, email (unread message digest) |
| `Ideas` | true | true | false | inApp, push, email |
| `Announcements` | true (not editable) | true | true | push, email |
| `Support` (staff only, shown to `SupportAgent` and `SystemAdmin`) | true | true | true | inApp, push, email |

---

## 10. Administrative email

New templates in `EmailTemplateCatalog` (HtmlPageBuilder, admin-overridable, previewed, test sends, queued with idempotency keys and retries through `EmailDeliveryWorker`, property brand from configuration):

| Template key | Category | Recipient | Tokens |
|---|---|---|---|
| `messages_unread` | Optional (`Messages`) | Member | `count`, `senderNames` (at most 3 plus "and N others"), `link`, `unsubscribeLink` (never message text) |
| `ticket_created` | Essential | Requester | `ticketNumber`, `title`, `link` |
| `ticket_reply` | Essential | Requester | `ticketNumber`, `title`, `replyText` (escaped Markdown rendered to safe HTML), `agentName`, `link` |
| `ticket_resolved` | Essential | Requester | `ticketNumber`, `title`, `reopenDays`, `link` |
| `idea_shipped` | Optional (`Ideas`) | Voters | `ideaTitle`, `statusNote`, `link`, `unsubscribeLink` |
| `broadcast` | Optional (`Announcements`) or Essential | Segment | `subject`, `preheader`, `heading`, `body` (rendered Markdown subset), `ctaLabel`, `ctaLink`, `unsubscribeLink` |
| `staff_ticket_opened` | Staff | Configured recipients per ticket type | `ticketNumber`, `type`, `title`, `requesterName`, `adminLink` |
| `staff_ticket_overdue` | Staff | Assignee or support staff | `ticketNumber`, `title`, `dueUtc`, `adminLink` |

---

## 11. Moderation summary

| Target | Scope | Who acts | Auto-hide |
|---|---|---|---|
| `Message`, `Conversation` | `Messaging` | `SystemAdmin`, Messaging moderators, through snapshots only | No |
| `Idea`, idea comments | `Ideas` | `SystemAdmin`, `SupportAgent` (ideas), forum moderators with `*` (comments, when enabled) | Phase 2 threshold |
| `Ticket` | not reportable (members close their own; staff handle abuse by restricting the account) | | |
| `Broadcast` | not reportable | | |

`IModerationTargetExtension` implementations: `MessageModerationTarget` (loads snapshots, applies `Remove` and `Restore` to the live message), `IdeaModerationTarget`.

---

## 12. Account lifecycle

| Event | Phase 5 effect |
|---|---|
| Account deletion | Messages become tombstones with "Deleted member" (bodies and media purged); conversation memberships removed; realtime sockets closed (`4401`); tickets closed and anonymized (6.4); votes removed (counts recomputed); ideas authored are kept with "Deleted member" (they belong to the community roadmap) unless still in the initial status, then deleted; broadcast deliveries deleted |
| Export | Adds `conversations` (the member's own messages and, for context, the other member's messages in conversations the member belongs to, per the property's privacy policy; open question 21), `tickets` (with replies, no notes), `ideaVotes`, `ideas`, `messagingSettings` |
| Ban | Sessions and sockets revoked; sending blocked; existing messages stay visible to the other members |
| Mute | Sending blocked except to staff (1.2) |
| Membership lapse (gated) | Messaging becomes read-only for `messaging.lapsedReadOnlyDays` (90), then hidden until membership returns (data kept); tickets unaffected |

---

## 13. Rate limits

Per account unless noted; keyed `<propertyId>:<scope>:<caller>`; `SystemAdmin` exempt except broadcasts.

| Route group | Limit | Setting |
|---|---|---|
| Messages sent | 30 per minute, 1000 per day | `messaging.messagesPerMinute`, `messaging.messagesPerDay` |
| New conversations (first contact) | 20 per day; accounts younger than `messaging.newAccountHours` (72): 3 per day | `messaging.newConversationsPerDay`, `messaging.newAccountConversationsPerDay` |
| Outstanding pending requests | 20 | `messaging.maxPendingRequests` |
| Edits and deletes | 60 per minute | `messaging.editsPerMinute` |
| History and sync reads | 120 per minute | `messaging.readsPerMinute` |
| Tickets created | 5 per day | `support.ticketsPerDay` |
| Ticket replies | 30 per hour | `support.repliesPerHour` |
| Ideas created | 5 per day | `ideas.ideasPerDay` |
| Idea votes | 60 per minute | `ideas.votesPerMinute` |
| Broadcasts | `broadcasts.maxPerDay` per property | |
| WebSocket | Section 4.13 | `realtime.*` |

---

## 14. Settings

Added to `SettingsRegistry.cs`; community behavior settings are `Overridable` per property; realtime transport settings are `Platform` (deployment-wide).

| Key | Type | Default | Apply | Scope |
|---|---|---|---|---|
| `messaging.maxBodyLength` | Integer 100 to 20000 | 4000 | Live | Overridable |
| `messaging.allowMedia` | Boolean | true | Live | Overridable |
| `messaging.maxAttachments` | Integer 0 to 10 | 4 | Live | Overridable |
| `messaging.editWindowMinutes` | Integer 0 to 1440 (0 = no edits) | 15 | Live | Overridable |
| `messaging.defaultDmPolicy` | Enum `Everyone`, `Known`, `Nobody` | `Everyone` | Live | Overridable |
| `messaging.defaultPushPreview` | Enum `SenderOnly`, `Full`, `Hidden` | `SenderOnly` | Live | Overridable |
| `messaging.requestMessageLimit` | Integer 1 to 20 | 3 | Live | Overridable |
| `messaging.reportContextMessages` | Integer 0 to 50 | 10 | Live | Overridable |
| `messaging.retentionDays` | Integer 0 to 3650 (0 = forever) | 0 | Live | Overridable |
| `messaging.lapsedReadOnlyDays` | Integer 0 to 3650 | 90 | Live | Overridable |
| `messaging.mediaUrlMinutes` | Integer 5 to 1440 | 60 | Live | Platform |
| `messaging.pushCoalesceSeconds` | Integer 0 to 3600 | 60 | Live | Overridable |
| `messaging.emailAfterMinutes`, `messaging.emailMinIntervalHours` | Integer 5 to 10080, 1 to 168 | 60, 12 | Live | Overridable |
| `messaging.pollSeconds` | Integer 2 to 60 | 5 | Live | Platform |
| `messaging.syncTokenDays` | Integer 1 to 365 | 30 | Live | Platform |
| `messaging.maxGroupMembers` | Integer 3 to 100 | 20 (reserved) | Live | Overridable |
| `messaging.newAccountHours` | Integer 0 to 720 | 72 | Live | Overridable |
| `messaging.messagesPerMinute`, `.messagesPerDay`, `.newConversationsPerDay`, `.newAccountConversationsPerDay`, `.maxPendingRequests`, `.editsPerMinute`, `.readsPerMinute` | Integer | 30, 1000, 20, 3, 20, 60, 120 | Live | Overridable |
| `realtime.enabled` | Boolean | true | Restart | Platform (`TK_REALTIME_ENABLED`) |
| `realtime.bus` | Enum `Postgres`, `InProcess` | `Postgres` | Restart | Platform (`TK_REALTIME_BUS`) |
| `realtime.heartbeatSeconds` | Integer 10 to 55 | 25 | Live | Platform |
| `realtime.authTimeoutSeconds` | Integer 3 to 60 | 10 | Live | Platform |
| `realtime.revocationSeconds` | Integer 1 to 60 | 5 | Live | Platform |
| `realtime.maxInboundFrameBytes` | Integer 1024 to 65536 | 16384 | Restart | Platform |
| `realtime.maxInboundFramesPerSecond`, `realtime.maxViolations` | Integer 1 to 200, 1 to 100 | 20, 5 | Live | Platform |
| `realtime.maxQueuedFrames`, `realtime.maxQueuedBytes`, `realtime.slowConsumerSeconds` | Integer | 256, 1048576, 30 | Live | Platform |
| `realtime.maxConnectionsPerUser`, `realtime.maxConnectionsPerTask`, `realtime.connectsPerMinute`, `realtime.anonymousUpgradesPerMinute` | Integer | 10, 10000, 30, 60 | Live | Platform |
| `realtime.typingMinIntervalMs` | Integer 500 to 10000 | 3000 | Live | Platform |
| `support.maxBodyLength`, `support.maxAttachments`, `support.allowVideo` | Integer, Integer, Boolean | 10000, 5, false | Live | Overridable |
| `support.maxOpenTicketsPerMember` | Integer 1 to 100 | 10 | Live | Overridable |
| `support.firstResponseTargetHours` | Integer 1 to 720 | 24 | Live | Overridable |
| `support.autoCloseDays`, `support.reopenDays` | Integer 1 to 365 | 7, 14 | Live | Overridable |
| `support.showAgentNames` | Boolean | true | Live | Overridable |
| `support.retainAfterDeletionDays` | Integer 0 to 3650 | 90 | Live | Overridable |
| `support.ticketsPerDay`, `support.repliesPerHour` | Integer | 5, 30 | Live | Overridable |
| `ideas.memberSubmissions` | Enum `Direct`, `StaffOnly` | `StaffOnly` | Live | Overridable |
| `ideas.voteOnClosed` | Boolean | false | Live | Overridable |
| `ideas.trendingDays` | Integer 1 to 90 | 14 | Live | Overridable |
| `ideas.publicBoardEnabled` | Boolean | false | Live | Property |
| `ideas.forumModeratorsModerate` | Boolean | true | Live | Overridable |
| `ideas.ideasPerDay`, `ideas.votesPerMinute` | Integer | 5, 60 | Live | Overridable |
| `broadcasts.maxPerDay` | Integer 1 to 50 | 3 | Live | Overridable |
| `broadcasts.maxRecipients` | Integer 1 to 1000000 | 100000 | Live | Platform |
| `broadcasts.batchSize` | Integer 50 to 5000 | 500 | Live | Platform |
| `broadcasts.emailsPerMinute` | Integer 10 to 10000 | 600 | Live | Property |

---

## 15. Telemetry

New instruments (low-cardinality labels; never ids, names, or text), all with `trelliskit.property`:

| Instrument | Type | Labels |
|---|---|---|
| `trelliskit.messaging.messages` | Counter | `action` (send, edit, delete, react), `outcome`, `has_media` |
| `trelliskit.messaging.conversations` | Counter | `action` (create, accept, decline, delete), `outcome` |
| `trelliskit.messaging.denied` | Counter | `reason` (policy, block_or_policy, restricted, unavailable, request_limit) |
| `trelliskit.messaging.send.duration` | Histogram (s) | |
| `trelliskit.messaging.delivery.latency` | Histogram (s) | `path` (socket, push): commit to frame written or push queued |
| `trelliskit.realtime.connections` | Gauge (UpDownCounter) | `client` (web, mobile) |
| `trelliskit.realtime.connects` | Counter | `outcome` (ok, auth_failed, timeout, rejected_origin, over_capacity) |
| `trelliskit.realtime.closes` | Counter | `code` |
| `trelliskit.realtime.frames` | Counter | `direction` (in, out), `type` |
| `trelliskit.realtime.dropped` | Counter | `reason` (overflow, slow_consumer, rate_limited) |
| `trelliskit.realtime.resyncs` | Counter | `reason` |
| `trelliskit.realtime.bus.events` | Counter | `kind`, `outcome` (delivered_local, no_local_recipients) |
| `trelliskit.realtime.bus.lag` | Histogram (s) | (notify to receive) |
| `trelliskit.realtime.bus.reconnects` | Counter | |
| `trelliskit.support.tickets` | Counter | `action` (create, reply, note, status, assign), `type` (configured key; bounded by the type list), `actor` (member, staff) |
| `trelliskit.support.first_response.hours` | Histogram | |
| `trelliskit.support.tickets.open`, `.overdue` | Gauge | |
| `trelliskit.ideas.actions` | Counter | `action` (create, vote, unvote, status, merge) |
| `trelliskit.broadcasts.sent` | Counter | `channel`, `category` |
| `trelliskit.broadcasts.deliveries` | Counter | `channel`, `outcome` |

Watson's built-in WebSocket metrics (`WebSocketSessionsActive`, `WebSocketSessionsTotal`, `WebSocketHandshakeFailures`) are enabled (`TelemetrySettings.CaptureWebSocketMetrics`). Spans: `messaging.send`, `realtime.hello`, `realtime.dispatch`, `realtime.bus.receive`, `support.ticket.create`, `broadcast.batch`, `job BroadcastSendWorker`, `job TicketDueWorker`, `job MessageEmailWorker`; span attributes may carry ids, never message text. Grafana: a Messaging row (messages per minute, delivery latency p50 and p95 by path, connections per task, closes by code, resyncs, bus lag) and a Support row (opened and resolved per day, first response median, overdue) in the Community folder; an alarm on bus queue usage and on resync spikes.

---

## 16. Data model

Migrations 60 to 69 are reserved for Phase 5 in `PostgresqlSchema.cs`: **60 "Direct messages"**, **61 "Realtime connections"**, **62 "Support tickets"**, **63 "Ideas board"**, **64 "Broadcasts"**, 65 to 69 held for fixes. Every table has `propertyid TEXT NOT NULL` first and an entry in `PropertyTables`; every index and unique constraint starts with `propertyid` (implied); access through the property-bound driver only. No JSON columns. Labels and tags use the shared tables (for conversations and messages, rows are keyed by the owning member: target id `cvm_` for a member's conversation metadata, and message metadata is author-only).

### Tables

| Table | Key columns |
|---|---|
| `conversations` | `id` PK, `kind`, `directkey` NULL, `lastseq` BIGINT, `lastmessageid`, `lastmessageutc`, `createdbyuserid`, `purgeafterutc` NULL, `createdutc`, `updatedutc` |
| `conversationmembers` | `id` PK, `conversationid`, `userid`, `role`, `folder`, `requeststate` NULL, `lastreadseq` BIGINT, `lastdeliveredseq` BIGINT, `hiddenbeforeseq` BIGINT (delete for me), `ismuted`, `muteduntilutc`, `joinedutc`, `leftutc` NULL, `deletedutc` NULL, `updatedutc` |
| `messages` | `id` PK, `conversationid`, `seq` BIGINT, `clientmessageid`, `authoruserid` NULL (null after account deletion), `bodyciphertext` BYTEA NULL, `bodynonce` BYTEA, `bodykeyversion`, `excerptciphertext` BYTEA NULL, `replytomessageid` NULL, `linkpreviewid` NULL, `attachmentcount`, `state`, `editedutc`, `deletedutc`, `moderatedutc`, `moderatedbyuserid`, `reactiontotal`, `createdutc`, `updatedutc` |
| `messageattachments` | PK (`propertyid`, `messageid`, `mediaid`), `position` |
| `messagereportsnapshots` | `id` PK, `reportid`, `conversationid`, `capturedutc`, `purgeafterutc` |
| `messagereportsnapshotitems` | `id` PK, `snapshotid`, `messageid`, `seq`, `authoruserid`, `bodyciphertext`, `bodynonce`, `keyversion`, `state`, `attachmentsummary`, `createdutc` |
| `messagingsettings` | PK (`propertyid`, `userid`), `dmpolicy`, `readreceipts`, `typingindicators`, `pushpreview`, `updatedutc` |
| `messagingcontacts` | PK (`propertyid`, `userid`, `contactuserid`), `source` (`Messaged`, `Accepted`), `createdutc` (the "known contact" relation, written when a member sends or accepts) |
| `messageemailstate` | PK (`propertyid`, `userid`), `lastsentutc`, `lastcheckedutc` |
| `realtimeconnections` | `id` PK, `userid`, `sessionid`, `taskid`, `client`, `appstate`, `activeconversationid` NULL, `connectedutc`, `lastseenutc` |
| `tickettypes` | `id` PK, `key`, `label`, `description`, `helptext`, `staffrecipients` (child table `tickettyperecipients`), `sortorder`, `isretired` |
| `tickettyperecipients` | PK (`propertyid`, `tickettypeid`, `email`) |
| `tickets` | `id` PK, `number` BIGINT, `typekey`, `title`, `body`, `status`, `priority`, `requesteruserid` NULL, `assigneeuserid` NULL, `ideaid` NULL, `deviceclient`, `deviceclientversion`, `deviceplatform`, `deviceosversion`, `devicemodel`, `deviceupdateid`, `devicelocale`, `devicetimezone`, `membershipactive`, `membershipsource`, `membershipstore`, `membershipexpiresutc`, `firstresponsedueutc`, `firstresponseutc`, `overduenotifiedutc`, `lastactivityutc`, `resolvedutc`, `closedutc`, `purgeafterutc`, `createdutc`, `updatedutc` |
| `ticketcounters` | PK (`propertyid`), `nextnumber` BIGINT |
| `ticketmessages` | `id` PK, `ticketid`, `kind`, `authoruserid` NULL, `authorrole`, `body`, `createdutc` |
| `ticketmessageattachments` | PK (`propertyid`, `ticketmessageid`, `mediaid`), `position` |
| `ideastatuses` | `id` PK, `key`, `label`, `color`, `isclosed`, `isinitial`, `sortorder`, `isretired` |
| `ideas` | `id` PK, `title`, `body`, `authoruserid` NULL, `showauthor`, `statuskey`, `statusnote`, `statusutc`, `votecount`, `recentvotecount`, `mergedintoid` NULL, `sourceticketid` NULL, `audience`, `commentcount`, `reactiontotal`, `openreportcount`, `moderationstate`, `deletedutc`, `createdutc`, `updatedutc` |
| `ideavotes` | `id` PK, `ideaid`, `userid`, `createdutc` |
| `ideafollows` | `id` PK, `ideaid`, `userid`, `source`, `createdutc` |
| `broadcastsegments` | `id` PK, `name`, `description`, `createdbyuserid`, `createdutc`, `updatedutc` |
| `broadcastsegmentfilters` | `id` PK, `segmentid` NULL, `broadcastid` NULL (inline), `field`, `operator`, `value`, `position` |
| `broadcasts` | `id` PK, `title`, `inapp`, `push`, `email`, `inapptitle`, `inappbody`, `pushtitle`, `pushbody`, `emailsubject`, `emailpreheader`, `emailbody`, `ctalabel`, `ctapath`, `segmentid` NULL, `category`, `essentialreason`, `status`, `sendatutc`, `startedutc`, `sentutc`, `canceledutc`, `targeted`, `cursoruserid` (keyset progress), stat counters, `createdbyuserid`, `createdutc`, `updatedutc` |
| `broadcastdeliveries` | `id` PK, `broadcastid`, `userid`, `inappstatus`, `pushstatus`, `emailstatus`, `emailmessageid` NULL, `createdutc`, `updatedutc` |

Changes to existing tables: `reports.scopetype` gains `Messaging`, `Ideas`; `reports.targettype` accepts `Message`, `Conversation`, `Idea`; `comments.targettype` and `reactions.targettype` accept `Idea` (and reactions `Message`); `mediaattachments` gains `purpose` (`Content`, `Message`, `Ticket`) and `scopeid` (conversation or ticket id) for key layout and access checks; `notifications` accepts the new types; `roleassignments.resourcetype` accepts `Messaging`; `RoleEnum` gains `SupportAgent`; `ResourceTypeEnum` gains `Messaging`.

### Indexes and unique constraints (all prefixed by `propertyid`)

| Table | Index | Purpose |
|---|---|---|
| `conversations` | `ux_conversations_direct (directkey) WHERE directkey IS NOT NULL` UNIQUE | One direct conversation per pair |
| `conversationmembers` | `ux_conversationmembers (conversationid, userid)` UNIQUE; `idx_conversationmembers_list (userid, folder, updatedutc DESC, id DESC) WHERE deletedutc IS NULL AND leftutc IS NULL`; `idx_conversationmembers_changes (userid, updatedutc, id)` | Membership, inbox list, sync |
| `messages` | `ux_messages_seq (conversationid, seq)` UNIQUE; `ux_messages_client (conversationid, authoruserid, clientmessageid)` UNIQUE; `idx_messages_author (authoruserid, createdutc)` (account deletion, export); `idx_messages_retention (createdutc) WHERE state = 'Visible'` | Ordering and gap fill, idempotency, lifecycle |
| `messagingcontacts` | PK above | Request rule |
| `realtimeconnections` | `idx_realtime_user (userid, appstate, lastseenutc)`; deployment-wide `idx_realtime_stale (lastseenutc)` (prune only) and `idx_realtime_task (taskid)` (drain) | Push decision, prune |
| `tickets` | `ux_tickets_number (number)` UNIQUE; `idx_tickets_inbox (status, lastactivityutc DESC, id DESC)`; `idx_tickets_assignee (assigneeuserid, status)`; `idx_tickets_requester (requesteruserid, lastactivityutc DESC)`; `idx_tickets_due (firstresponsedueutc) WHERE firstresponseutc IS NULL AND status <> 'Closed'` | Inbox, own list, due worker |
| `ticketmessages` | `idx_ticketmessages_ticket (ticketid, createdutc, id)` | Timeline |
| `ideas` | `idx_ideas_top (votecount DESC, id DESC) WHERE deletedutc IS NULL AND mergedintoid IS NULL AND moderationstate = 'None'`; `idx_ideas_trending (recentvotecount DESC, id DESC) WHERE ...same`; `idx_ideas_status (statuskey, votecount DESC)` | Orderings |
| `ideavotes` | `ux_ideavotes (ideaid, userid)` UNIQUE; `idx_ideavotes_recent (ideaid, createdutc)` | One vote, trending recompute |
| `broadcasts` | `idx_broadcasts_due (sendatutc) WHERE status = 'Scheduled'`; `idx_broadcasts_sending (status) WHERE status = 'Sending'` | Worker |
| `broadcastdeliveries` | `ux_broadcastdeliveries (broadcastid, userid)` UNIQUE; `idx_broadcastdeliveries_status (broadcastid, emailstatus)` | Idempotency, stats |

### Write rules

Phase 2 write rules apply (counters recomputed from source rows in the same transaction; claims through the conditional `UPDATE` pattern and `jobleases`). Message sends lock the conversation row to assign `seq`; `pg_notify` runs inside the writing transaction; message bodies are encrypted before the `INSERT` and decrypted only in the service layer (never in SQL). `recentvotecount` on ideas is recomputed hourly by `IdeaTrendingWorker`.

---

## 17. Infrastructure

| Item | Change |
|---|---|
| ALB | Idle timeout 120 seconds (from the 60 second default) so 25 second heartbeats keep sockets open; WebSockets need no listener change on ALB |
| ECS | Deregistration delay 30 seconds; stop timeout 30 seconds for draining; task CPU and memory sized for sockets (each idle socket costs about 20 to 50 KB) |
| CloudFront (website distribution) | `/api/*` forwarding already exists; the web app connects to the API domain for `/api/v1/ws` (CORS-free because WebSockets use the `Origin` check, not CORS) |
| RDS | `max_connections` headroom for one `LISTEN` connection per task; CloudWatch alarm on `pg_notification_queue_usage` via the telemetry exporter |
| S3 and CloudFront | Signed URL key group already used for signed cookies is reused for per-object signed URLs of `dm/` and `support/` paths; the signed cookie policy is narrowed to `media/` and `video/` paths |
| Secrets | None new (message encryption keys derive from the engine secret key) |
| Docker | nginx WebSocket proxy settings (4.14); an optional `realtime-scale` compose profile with two server containers |

---

## 18. Backend structure

**Reused building blocks (no reimplementation):**

| Existing component | Phase 5 use |
|---|---|
| Watson 7.3 WebSocket routes (`WebserverBase.WebSocket`, `WebSocketSettings`, session events, built-in WebSocket telemetry) | `GET /api/v1/ws` |
| `SessionService`, `TokenService`, `AuthenticationService`, `PropertyResolution`, `PropertyContext` | `hello` validation, property binding, revocation events |
| `AccessDecider`, `AccessRoutes`, `AccessPolicyService`, `MembershipService` | Messaging, support, and ideas route declarations; capability evaluation on the socket |
| `KillSwitchCatalog`, `FeatureFlagEvaluator` | `messaging`, new `support`, `ideas`, `broadcasts`, `realtime` |
| `MediaService`, `MediaPipeline`, `MediaStorageService`, `MediaUrlBuilder`, `IMediaDeliverySigner`, `IObjectStore`, `MediaProcessingWorker`, `MediaCleanupWorker` | DM and ticket attachments with new key prefixes and per-object signed URLs |
| `ISecretProtector`, `SecretBoxProvider`, `SecretRotationService`, `SecretRotationWorker` | The envelope pattern and rotation for `IMessageProtector` |
| `ReactionService`, `ReactionTarget` | Reactions on messages and ideas |
| `CommentService`, `CommentHost`, `MentionService` | Idea comments |
| `NotificationService`, `NotificationCatalog`, `NotificationPreferenceService`, `NotificationHoursRule`, `NotificationFanoutWorker`, `PushDeliveryWorker`, `IPushSender` | New types and categories, DM pushes with collapse keys, broadcast fan-out |
| `ReportService`, `ModerationService`, `IModerationTargetExtension`, `ModerationDueWorker`, `ContentPurgeWorker` | Message snapshots, idea moderation, purges |
| `BlockService`, `WordFilterService` | DM eligibility and filtering (`Block` terms only for DMs and tickets) |
| `LinkPreviewService`, `ILinkPreviewFetcher`, `ILinkPreviewAddressPolicy` | Previews in messages |
| `MetadataService`, `EnumerationService` | Labels, tags, and every enumeration |
| `MemberDirectoryService` filter engine, `ProfileFieldService` | Broadcast segment profile filters |
| `EmailService`, `EmailTemplateCatalog`, `EmailRenderer`, `PropertyEmailSender`, `EmailDeliveryWorker`, `ForumDigestService` pattern | Ticket, idea, broadcast, and unread message emails |
| `AuditService`, `RateLimitService`, `SlidingWindowRateLimiter`, `CsvWriter`, `AccountExportWorker`, `RequestHistoryCaptureService` (with body redaction for messaging routes) | Audit, limits, exports, privacy |
| `SearchService`, `ISearchIndex` | Ideas in global search; a separate staff-only index for tickets; never messages |
| `IntervalWorker`, `LeasedPassWorker`, `jobleases` | New workers |

**New:** `IRealtimeBus` with `PostgresRealtimeBus` (listener connection, reconnect, `pg_notify` helper usable inside driver transactions) and `InProcessRealtimeBus`; `RealtimeGateway` (socket lifecycle, `hello`, frame parsing and limits, outbound queues, heartbeats, draining), `RealtimeConnectionRegistry`, `RealtimeDispatcher` (bus event to per-recipient frames); services `ConversationService`, `MessageService`, `MessageProtector` (`IMessageProtector`), `MessagingPolicyService` (eligibility, requests, contacts), `MessagePushService`, `MessageSyncService`, `MessageReportService`, `TicketService`, `TicketWorkflowService`, `IdeaService`, `BroadcastService`, `BroadcastSegmentService`. Route registrars: `RealtimeRoutes`, `ConversationRoutes`, `MessageRoutes`, `MessagingSettingsRoutes`, `TicketRoutes`, `AdminTicketRoutes`, `IdeaRoutes`, `AdminIdeaRoutes`, `PublicIdeaRoutes`, `AdminBroadcastRoutes`, `AnnouncementRoutes`. Workers: `MessageEmailWorker`, `MessageRetentionWorker`, `ConversationPurgeWorker`, `RealtimeRegistryPruneWorker`, `TicketDueWorker`, `TicketAutoCloseWorker`, `IdeaTrendingWorker`, `BroadcastSendWorker`, `MessageKeyRotationWorker`.

Touchstone suites (every route also under the cross-property isolation suite; a dedicated case asserts that a socket of property A never receives an event of property B and that a bus payload from B is dropped by A's dispatcher): `ConversationSuite` (direct key uniqueness, requests, accept and decline, policies, blocks both ways, folders, delete for me), `MessageSuite` (sequence contiguity under parallel sends, idempotent retries, edits window, delete tombstones, reactions, replies), `MessageHistorySuite` (`beforeSeq`, `afterSeq`, `aroundSeq`, continuation, hidden ranges), `MessagePrivacySuite` (encryption at rest verified by reading raw rows, request history redaction, no staff route returns bodies, snapshot audit, DM media not covered by the grant cookie), `RealtimeProtocolSuite` (hello timeout, bad token, property mismatch, origin check, refresh, revocation close, close codes, frame limits, backpressure overflow to resync), `RealtimeScaleSuite` (two server instances against one PostgreSQL: cross-task delivery, listener reconnect triggers resync, registry-based push suppression, draining), `MessagePushSuite` (foreground suppression, coalescing, preview modes, notification hours), `SyncSuite`, `TicketSuite` (workflow, notes hidden from members, attachments access, due and auto close with a fixed clock, numbering under concurrency), `IdeaSuite` (votes, merge, status notifications, trending), `BroadcastSuite` (segment preview equals sent count, idempotent batches, preferences and suppressions, essential override, cancel mid-send, limits), `Phase5AccessSuite` (all models, lapsed read-only, support always reachable).

---

## 19. Client notes

**Shared (`shared/src`):** `ApiClient` methods for every route (`enumerateConversations`, `startConversation`, `getConversation`, `enumerateMessages`, `sendMessage`, `editMessage`, `deleteMessage`, `markConversationRead`, `acceptRequest`, `declineRequest`, `setConversationFolder`, `muteConversation`, `deleteConversation`, `syncConversations`, `getMessagingUnreadCount`, `getMessagingSettings`, `updateMessagingSettings`, ticket, idea, and announcement methods, and admin methods). A `RealtimeClient` shared by web and mobile: connect, `hello`, token refresh hook, heartbeat, backoff with jitter, close code handling (4.10), event emitter, gap detection per conversation (tracks the highest contiguous `seq` and triggers `afterSeq` fetches), resync orchestration (5.2), polling fallback (5.1), outbound `typing` throttling, and optimistic sends keyed by `clientMessageId` with retry. A message store that merges REST pages, socket events, and optimistic messages by `id` and `seq`.

**Website:** Messages page (`/app/messages`, `/app/messages/:id`, `/app/messages/requests`) with conversation list (Inbox, Requests, Archived; search by name; label and tag filters on the caller's own metadata), conversation view (virtualized list, load older on scroll, date separators, read receipts, typing indicator, replies, reactions, attachments via the Phase 2 uploader with `purpose: "Message"`, link previews, edit and delete menus from `viewer`), request banner (Accept, Decline, Block and report), "Message" button on profiles when `viewer` allows, messaging settings (DM policy, receipts, typing, push preview) under Settings. Notification badge updates from `notification.created` (polling stays as fallback). Support (`/app/support`): ticket list, new ticket form with types and help text, ticket timeline, close and reopen. Ideas (`/app/ideas`): board with orderings, status filter chips, label and tag filters, idea page with vote button and comments. Announcements view. Admin: Support inbox (filters, assignment, notes in a distinct color, macros not in Phase 5, publish to ideas), Ideas management, Broadcasts (segment builder with live count, channel editors with previews for push and email, test send, schedule, send confirmation with the count, delivery stats), message report queue showing snapshots only.

**Mobile and tablet:** Messages entry in the top bar (plan section 7.1) with an unread badge; conversation list and conversation screens (`@shopify/flash-list` inverted list, keyboard-aware composer, image and video attach with `expo-image-picker`, haptics on send), requests, messaging settings; the socket connects on foreground and closes after 30 seconds in background; pushes with `data.type: "DirectMessage"` deep link to the conversation and are suppressed while it is open; notification categories for Messages, Ideas, Announcements in the settings section. Support screens with device diagnostics toggle; Ideas screens; Announcements. Tablet: conversation list and conversation side by side in `SplitView`; ticket list and detail; ideas list and detail. Every list offers label and tag filters (enumeration specification section 3).

---

## 20. Delivery order

1. Migrations 60 to 64, enums, roles (`SupportAgent`, `Messaging` resource type), feature and kill switch keys, `AccessRoutes` declarations.
2. Direct messages over REST: conversations, requests, policies, blocks, send with sequence numbers and idempotency, history, read and delivered, edits, deletes, reactions, encryption at rest, DM media paths and signed URLs, reports with snapshots; website and mobile messaging screens with polling.
3. Realtime: `IRealtimeBus` on PostgreSQL, gateway with `hello`, heartbeats, close codes, limits, backpressure, registry, draining; `RealtimeClient` in shared; message, receipt, typing, and notification events; push decision; infrastructure changes; the two-instance scale suite.
4. Support tickets with workflow, attachments, notifications, emails, admin inbox.
5. Ideas board with votes, merges, statuses, notifications, admin page, ticket publishing.
6. Broadcasts with segments, preview, scheduling, delivery workers, stats, admin page; announcements view.
7. Unread message email, retention workers, key rotation, metrics, Grafana, `REST_API.md`, Postman, simulated user testing pass (including two members messaging across web and mobile while tasks restart).

REST messaging lands before the socket so both clients can ship a working inbox with polling while the realtime layer is built and load tested. Phase 5 can run in parallel with Phase 4 (plan section 11).

---

## Property notes (appendix)

This appendix maps the property plan of record onto configuration only. No engine code or test reads it.

| Plan intent | Expressed as |
|---|---|
| 1:1 direct messages at launch, groups later | `kind: Direct` only; `Group` reserved |
| WebSocket delivery on the API domain, push when not connected | Section 4 with the property's `domains.api`; section 4.9 |
| Message requests from people you have not interacted with | `dmPolicy: Everyone` default with the Requests folder (3.2) |
| Per-user DM switch | Phase 1 `flags.canSendDirectMessages` |
| Admins cannot read DMs except messages attached to a report | Section 3.8 and 3.9 (snapshots only, audited) |
| Ticket types listed in the plan | `property.json` `support.ticketTypes` (the plan's four types match the engine defaults) |
| Device and app version attached from mobile | `includeDiagnostics` default true on mobile |
| Enhancement requests optionally published to a member-visible ideas board with upvotes and admin statuses | `ideas.memberSubmissions: StaffOnly`, `publish-idea` from tickets, `property.json` `ideas.statuses` with the plan's four statuses |
| Admin broadcasts by push and email to all members or a segment by rank, location, or subscription store | Broadcast segments with `profile.<rankFieldKey>`, `profile.country`/`region`, and `store` filters |
| Fully paywalled membership | `FullyGated`: messaging requires both parties to be members; support stays reachable for every signed-in account |

---

## Open questions (with recommended answers)

| # | Question | Recommendation | Rationale |
|---|---|---|---|
| 1 | Group conversations in Phase 5? | No: 1:1 only, schema ready for groups | The plan defers groups; groups add membership management, admin roles, and fan-out costs |
| 2 | Default DM policy for new members? | `Everyone`, with strangers landing in Requests | Keeps the community open while the request folder, limits, and blocks protect members; members can switch to `Known` or `Nobody` |
| 3 | Messages per pending request before acceptance? | 3 | Enough for an introduction and a follow-up; low enough to stop spam campaigns |
| 4 | Encrypt message bodies at the application level in addition to storage encryption? | Yes (AES-256-GCM per property key) | Keeps backups, snapshots, and query logs free of readable text, at the cost of no SQL search over bodies (which we do not offer) |
| 5 | End-to-end encryption? | No in Phase 5; state plainly in the privacy policy that messages are not end-to-end encrypted | E2E across web and mobile needs key management, device verification, and breaks report snapshots and moderation; disproportionate for a community product |
| 6 | Message edits: window and history? | Edit within 15 minutes, latest text only, "Edited" marker | Unlike posts, DMs are private and short; a visible history adds little and stores more personal data |
| 7 | Can a member delete a message for everyone? | Yes, any time, leaving a tombstone | Members expect to retract what they sent; reports already captured keep their snapshot |
| 8 | Search inside direct messages? | Not in Phase 5 | Requires indexing private text (or client-side search over decrypted history); privacy and complexity outweigh the value at launch |
| 9 | Server-side replay buffer for missed socket events, or REST resync on every reconnect? | REST resync with sequence gap detection | Simpler and correct by construction; the sync query is bounded and cheap; a replay buffer can be added later behind the same `resume` field |
| 10 | Show presence ("online now", "last seen")? | No | Privacy expectations and a registry write load we do not otherwise need; receipts and typing give enough liveness |
| 11 | Website socket through the CloudFront website distribution (same origin) or directly to the API domain? | Directly to the API domain | Avoids CloudFront WebSocket idle limits and extra cost; the `Origin` check secures it; REST stays same origin |
| 12 | Should DM pushes respect notification hours? | Yes, like every category (Phase 2 rule), with a member toggle to exempt Messages | Consistency and fewer late-night buzzes; members who want DMs any time can opt out of the window for this category |
| 13 | Email digest of unread messages? | Build it, default off (opt-in per member) | Useful for members who rarely open the app; default off avoids inbox noise; emails never carry message text |
| 14 | Default push preview? | `SenderOnly` | Message text would otherwise pass through Apple, Google, and the Expo push service and show on lock screens |
| 15 | Read receipts default? | On, reciprocal | Matches member expectations from mainstream messengers; reciprocity keeps it fair |
| 16 | Anonymous (signed-out) support tickets? | No in Phase 5; the public support page shows the support email | An anonymous form attracts spam and needs captcha and email verification; people who cannot sign in can email |
| 17 | Ticket assignment: manual, or automatic round-robin? | Manual in Phase 5 | Small support teams; round-robin needs agent availability states |
| 18 | Reply to tickets by email (inbound parsing)? | Not in Phase 5; emails link back to the app | Inbound mail routing (Mailgun routes), quoting cleanup, and spoofing protection are a separate project |
| 19 | Keep ticket contents after account deletion? | 90 days anonymized, then purge | Billing disputes and refunds may need the record; anonymization respects the deletion request; must be disclosed in the privacy policy |
| 20 | Who sends broadcasts: `SystemAdmin` only, or a new `Communicator` role? | `SystemAdmin` only | Broadcasts reach everyone; limit to the highest trust level until a property needs delegation |
| 21 | Does the account export include the other party's messages in the member's conversations? | Yes, messages in conversations the member belongs to, because they were sent to the member | Matches common data access practice for messaging; flag for counsel review per property |
| 22 | Email open and click tracking for broadcasts? | No | Tracking pixels are personal data processing that needs disclosure and consent in some regions; delivery stats are enough at launch |
| 23 | Ideas: members submit directly, or staff publish from enhancement tickets? | Staff publish from tickets by default (`StaffOnly`), with `Direct` available | Matches the plan ("optionally published"), keeps the board curated, and avoids duplicate floods |
| 24 | Show idea voters' names? | No: counts only | Voting is a preference signal, not a public statement; avoids social pressure |
| 25 | Message retention period? | Keep until deleted (`retentionDays: 0`), with a per-property setting for automatic expiry | Members expect history to persist; properties with stricter policies can set a period |
| 26 | Lapsed members' access to their DM history (gated models)? | Read-only for 90 days, then hidden until they rejoin (data kept) | Members should not lose conversations the moment a payment fails, but messaging remains a membership benefit |
| 27 | Bus mechanism for multi-task fan-out? | PostgreSQL `LISTEN`/`NOTIFY` behind `IRealtimeBus` | No new infrastructure; transactional delivery on commit; ample capacity at expected scale; the interface leaves room for Redis or SNS later |
| 28 | Reserve migrations 60 to 69 for Phase 5 as numbered (60 Direct messages, 61 Realtime connections, 62 Support tickets, 63 Ideas board, 64 Broadcasts)? | Yes | Per-phase ranges keep parallel Phase 4 and Phase 5 work free of migration number conflicts |

---

## Resolved decisions (property owner, 2026-11-12)

These answers override the recommendations in the open questions section wherever they differ.

| # | Decision | Notes |
|---|---|---|
| 1 | One-to-one conversations only; schema ready for groups | As recommended |
| 2 | Default DM policy `Everyone`, strangers land in Requests | As recommended |
| 3 | 3 messages per pending request before acceptance | As recommended |
| 4 | **No application-level encryption of message bodies; rely on storage (disk) encryption only** | Overrides the recommendation; enables server-side search (decision 8) |
| 5 | Not end-to-end encrypted; stated plainly in the privacy policy | As recommended |
| 6 | **Edit within 15 minutes, at most one edit per message, and keep the original text as edit history** (viewable, "Edited" marker) | Overrides the recommendation |
| 7 | **Members can delete a message for everyone at any time (tombstone shown), but the deleted content is kept internally** (staff access follows the moderation rules; disclose retention in the privacy policy) | Overrides the recommendation |
| 8 | **Search inside direct messages: yes** (members search their own conversations; property-bound, bounded enumeration) | Overrides the recommendation |
| 9 | REST resync with sequence gap detection on reconnect | As recommended |
| 10 | No presence | As recommended |
| 11 | Website socket connects directly to the API domain with an Origin check | As recommended |
| 12 | DM pushes respect notification hours, with a member toggle to exempt Messages | As recommended |
| 13 | Unread messages email digest built, off by default, never contains message text | As recommended |
| 14 | Default push preview `SenderOnly` | As recommended |
| 15 | Read receipts on by default, reciprocal | As recommended |
| 16 | No anonymous tickets; the public support page shows the support email | As recommended |
| 17 | Manual ticket assignment | As recommended |
| 18 | No inbound email replies in Phase 5; emails link to the app | As recommended |
| 19 | **Tickets are kept as they are when a member deletes their account; support staff close and delete them** | Overrides the recommendation (no automatic anonymization or purge) |
| 20 | **Add a `Communicator` role that may send broadcasts** (SystemAdmin also may) | Overrides the recommendation |
| 21 | Export includes messages in the member's conversations, including the other party's (flag for counsel) | As recommended |
| 22 | No open or click tracking on broadcasts | As recommended |
| 23 | Staff publish ideas from enhancement tickets by default (`StaffOnly`), `Direct` available | As recommended |
| 24 | Idea vote counts only, voters private | As recommended |
| 25 | Messages kept until deleted, with a per-property automatic expiry setting | As recommended |
| 26 | **Lapsed members in gated models cannot access their message history; it is retained for 90 days and then purged unless they rejoin** | Overrides the recommendation (no read-only period) |
| 27 | PostgreSQL LISTEN/NOTIFY behind `IRealtimeBus` | As recommended |
| 28 | Migrations 60 to 69 reserved for Phase 5 | As recommended |

---

## As-built notes (implementation, 2026-11-12)

These notes record how the resolved decisions above were implemented and where the implementation differs from the draft sections. Where a note and an earlier section differ, the note wins.

### A. Transport (Watson 7.3.2)

Watson 7.3.2 hosts WebSockets in process (`Webserver.WebSocket(path, handler)`, RFC 6455, HTTP/1.1 upgrades, whole-message receive with a size cap, serialized sends, a session registry, and built-in telemetry), so `GET /api/v1/ws` is a Watson WebSocket route in the same process as the REST API. Two Watson behaviors shape the protocol:

1. **No subprotocol negotiation.** Watson's upgrade response never echoes `Sec-WebSocket-Protocol`, and browsers fail a connection that requested a subprotocol the server did not echo. Clients therefore request no subprotocol; the protocol version travels in the `hello` frame (`data.protocol`, `"tk.v1"`, optional; any other value closes `4400`). An upgrade that carries `Sec-WebSocket-Protocol` is refused with `400 SubprotocolNotSupported` before the upgrade.
2. **Keepalive.** Watson creates the socket with a fixed 30 second keepalive (control frames that keep load balancers and NAT mappings warm) and exposes no ping or pong events, so liveness is application level: clients send `ping` every `welcome.heartbeatSeconds` (25) whether or not other frames arrived, the server answers `pong`, and a connection with no inbound frame for two heartbeat intervals plus 5 seconds is dropped (the client reconnects).

All pre-upgrade checks (property resolution, `Origin`, the `realtime` kill switch, capacity, anonymous upgrade limits, subprotocol) run in the server's pre-routing hook, so a refused upgrade gets a normal HTTP error and never reaches Watson's handshake. An upgrade without an `Origin` header (native apps) skips the origin check; a present `Origin` must be one of the property's web origins (its `domains.web` origin, its verified domains, and the deployment's `cors.allowedOrigins`), else `403 OriginNotAllowed`.

The realtime bus channel is `tk_realtime`, suffixed with `_<schema>` when `database.schema` is set, so deployments (and parallel test schemas) that share one PostgreSQL database never receive each other's notifications.

### B. Direct messages

- **No application-level encryption** (decision 4). Bodies are stored as text and protected by storage encryption. `IMessageProtector`, the per-property data key, `MessageKeyRotationWorker`, and the ciphertext columns of section 16 are not built; `messages` has `body`, `originalbody`, and a generated `searchvector`. Section 3.8 point 2 is replaced by the privacy statement in note G.
- **Edits** (decision 6): one edit per message within `messaging.editWindowMinutes` (15). `Message` adds `isEdited` (bool), `editedUtc`, and `originalBody` (the text before the edit, null when not edited; shown to both members as edit history). A second edit returns `409 EditWindowClosed`; `viewer.canEdit` is false after the first edit.
- **Delete for everyone** (decision 7): the author may delete any time; members see a tombstone (`state: Deleted`, empty `body`, null `originalBody`, empty `attachments`). The text and attachment references are kept internally for moderation (report snapshots and the moderator flow of section 3.9); no member route returns them. Account deletion still purges the deleted member's bodies (section 12).
- **Search** (decision 8): PostgreSQL full text search (`to_tsvector('simple', body)` in a generated column with a GIN index), property bound like every query. `POST /api/v1/messages/search/enumerate` (`EnumerationQuery` with required `search` of 2 to 200 characters; orderings `Relevance` (default), `CreatedDescending`, `CreatedAscending`; filters `conversationId`, `withUserId`; at most 50 per page) returns `EnumerationResult<MessageSearchHit>` with `{ message, conversationId, otherMember, headline }`. Only conversations the caller belongs to and messages the caller can still see (not hidden by delete for me, not tombstones, not removed) are searched. The history enumeration (3.4) also accepts `search` (then ordered `SeqDescending`; `aroundSeq` is not allowed with it). Direct messages are never in global search and never assistant knowledge.
- **Lapsed members** (decision 26): in `FullyGated`, and in `PartiallyGated` with `messaging` gated, every messaging route (reads included) needs a membership (`402 MembershipRequired`, `feature: "messaging"`). `MessagingLapseWorker` records when a member lost access and, after `messaging.lapsedRetentionDays` (90) without rejoining, deletes their conversations for them (as delete for me) and clears their messaging contacts; the other member's history is untouched, and section 3.6 purges a conversation once every member has deleted it. Rejoining within the window restores full access. The setting `messaging.lapsedReadOnlyDays` is replaced by `messaging.lapsedRetentionDays`.

### C. Support

- **Account deletion** (decision 19): tickets stay exactly as they are; the requester shows as "Deleted member". Support staff close them and delete them with `DELETE /api/v1/admin/tickets/{id}` (only `Closed` tickets, else `409 TicketNotClosed`; deletes the ticket, its messages, metadata, and attachments; audited `ticket.delete`). `support.retainAfterDeletionDays` is not built.
- `/tickets*` and `/support/*` are in the SignedIn route group, so every signed-in account reaches them in every access model.

### D. Roles

`SupportAgent` (decision table 1.1) and `Communicator` (decision 20) are global staff roles granted like the other global roles; both count as staff for `isStaff`. `Communicator` may use `/api/v1/admin/broadcasts*` (segments, preview, create, test, schedule, send, cancel, deliveries) and nothing else in the admin console; `SystemAdmin` may too, and only `SystemAdmin` may send an `Essential` broadcast.

### D2. Private media

Ticket and direct message attachments are uploaded with `purpose` (`Ticket`, `Message`) and `scopeId` (ticket or conversation). After processing their variants move to a private prefix of the Source location (never served by CloudFront), and payloads carry per-object API URLs (`GET /api/v1/media/private/{token}/{file}`, token valid `messaging.mediaUrlMinutes`) that re-check the viewer's access to the scope on every request (ticket requester and support staff; current conversation members, and not the recipient of a pending request). This replaces the CloudFront signed URLs of section 3.7; no new infrastructure is needed, and the grant cookie never reaches these objects.

### E. Privacy policy statements

A property's privacy policy can describe direct messages as follows: messages are encrypted in transit and stored on encrypted storage, but they are not end-to-end encrypted and the service operator could technically read them; staff tools never list, search, or open conversations, and staff see messages only when a participant reports them (a frozen, audited snapshot); a message deleted for everyone disappears for both members, but its content is retained by the service for safety and moderation; members can search their own conversations and no one else can; lapsed members of a membership community lose access to their messages, which are kept for 90 days and then removed from their account unless they rejoin; push notifications show only the sender's name by default; unread message emails never contain message text; link previews are fetched by the service, not the member's device; support tickets are kept after an account is deleted until support staff close and delete them.

### Configurable account-deletion policy

The default continues to retain support tickets. With `support.deleteOnAccountDeletion=true`, account closure deletes requester tickets, replies, internal notes, metadata, and attached media in bounded pages. Tickets carrying the staff-only labels `staff:retain-legal` or `staff:retain-billing` are retained for an operator-verified obligation. Operators must minimize held records and review/delete them when the obligation ends; a Billing ticket type is not itself a hold.

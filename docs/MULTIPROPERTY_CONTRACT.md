# Multi-Property Contract: Deployments, Profile Fields, and Access Models

This is the binding contract between the backend, the website, and the mobile app for the milestones "Engine: multi-property deployments and configurable profile fields" and "Engine: access models". Every team codes against it exactly. It is an engine contract: nothing here names a property, a brand, a sport, a person, a domain, a price, or a store product id. Examples use the neutral sample property (`example`, Example Community, `example.test`) and a second test-local fixture property (`second`, Second Example, `second.test`) that exists only in engine tests. When the implementation is complete, `REST_API.md` and the Postman collection are updated to match, and this file stays as the design record.

It implements `docs/TRELLISKIT.md` sections 7 (access models), 10 (deployment modes and multi-tenancy), and 11 (configurable profile fields), and builds on `docs/PHASE1_CONTRACT.md` and `docs/PHASE2_CONTRACT.md`. `docs/ENUMERATION_AND_METADATA.md` is binding for every route in this document: every list is an `EnumerationQuery` in and `EnumerationResult<T>` out with label and tag filters, every new object carries `labels` and `tags`, and nothing returns an unbounded list. The tenant isolation rules of `~/Code/Agents/requirements/AUTHENTICATION.md` apply with the property as the tenant. Nothing in Phase 1 or Phase 2 changes unless this document says so explicitly.

Conventions are unchanged: routes under `/api/v1`, camelCase JSON, string enums, ISO 8601 UTC timestamps, errors as `{ "error", "message", "fieldErrors" }`, `X-Client` and `X-Client-Version` on every app request, access tokens as `Authorization: Bearer <accessToken>`.

Decisions already made (from `TRELLISKIT.md`, not reopened here): accounts are separate per property; `PlatformAdmin` exists only in multi-property mode; each property has its own integrations; the database is the source of truth for property configuration and profile fields after seeding, with `property.json` as the import and export format; both deployment modes run the same code.

---

## 0. Scope and conventions

### 0.1 What this contract delivers

| Part | Content | Sections |
|---|---|---|
| A. Multi-property | Deployment mode, property resolution, `propertyid` on every property-owned row, isolation rules and tests, per-property integrations and secrets, platform administration, property storage in the database, single-property mode on the same code | 1 to 8 |
| B. Profile fields | `ProfileField` definitions, values, payloads, rendering, member search filters, seeding, compatibility with the Phase 1 profile fields | 9 |
| C. Access models | `Open`, `PartiallyGated`, `FullyGated`, gated features, per-object audience, the central authorization decision, client behaviour, `GET /api/v1/property` additions | 10 |

Parts A and B ship together (one milestone); part C ships right after. The contract is written together so that teams can build in parallel (section 16).

### 0.2 Terms

| Term | Meaning |
|---|---|
| Property | A configured community (a tenant). Identified by its `id` from `property.json`: lowercase letters, digits, and hyphens, 1 to 63 characters (`^[a-z0-9][a-z0-9-]{0,62}$`, as validated today). Immutable forever |
| Reserved ids | `platform`, `unresolved`, `none`, `all`, `system`. No property may use them |
| Platform realm | The pseudo-property `platform` that holds platform administrator accounts, their sessions, and platform audit records. Exists only in `MultiProperty` mode. It has no domains, apps, members, content, or profile fields and is never returned as a property |
| Property-owned row | Any row whose data belongs to one property. Carries `propertyid` |
| Deployment-level row | A row that belongs to the running deployment, not to a property (`schema_migrations`, `jobleases`) |
| Resolved property | The property a request is bound to after section 2 runs |
| Caller tier | `Anonymous`, `SignedIn` (session and age attested), `Member` (signed in with an active entitlement), `Staff` (`SystemAdmin`, `Moderator`, `FeedModerator` within scope, a delegated platform admin, or the break-glass token). Ordered `Anonymous < SignedIn < Member < Staff` |

### 0.3 New request headers

| Header | Sent by | Value |
|---|---|---|
| `X-TK-Property` | Mobile and tablet apps on every request (baked in at build time from `property.json` `id`); the website on every API request (from its build property); the admin console when it manages a property from a platform host | Property id |
| `X-TK-App` | Mobile and tablet apps on every request, including the Expo Updates manifest request (`updates.requestHeaders` in `app.config.js`) | The app's iOS bundle id or Android package (`mobile.bundleId` or `mobile.androidPackage`) for the platform it runs on |

CORS adds `X-TK-Property` and `X-TK-App` to the allowed request headers. Both headers are recorded in request history (not secret).

### 0.4 New identifier prefixes

| Entity | Prefix |
|---|---|
| Property version | `prv_` |
| Property asset | `pas_` |
| Property domain | `pdm_` |
| Property app | `ppa_` |
| Profile field | `pfd_` |
| Profile field option | `pfo_` |
| Profile field sub-field | `pfs_` |
| Profile history entry | `phe_` (entries migrated from `rankhistoryentries` keep their `lin_` ids) |
| Property bundle job (import or export record) | `pbj_` |

Properties themselves use their slug id (`example`), not a prefixed id.

### 0.5 New error codes

| Status | `error` | When |
|---|---|---|
| 400 | `PropertyMismatch` | Two property hints in one request disagree (section 2.3) |
| 400 | `InvalidBundle` | A property bundle failed validation; `fieldErrors` is keyed by file path and JSON path (section 7.4) |
| 401 | `Unauthorized` | Also returned when an access token issued for one property is presented to another (same message as any invalid token) |
| 402 | `MembershipRequired` | Unchanged meaning; the body now also carries `required`, `feature`, `targetType`, `targetId` (section 10.5) |
| 403 | `AppNotRegistered` | `X-TK-App` is not a registered app of the resolved property |
| 404 | `PropertyNotFound` | No property resolved, an unknown id, or a property that is disabled or being deleted |
| 404 | `NotAvailable` | A route that does not exist in the current mode or access model (platform routes in `SingleProperty`, purchase and sign-up code routes in `Open`, platform-managed fields edited by a property admin in `MultiProperty`) |
| 409 | `PropertyIdInUse`, `DomainInUse`, `AppIdInUse` | Registration conflicts (ids, hosts, and app ids are unique across the deployment) |
| 409 | `PropertyLimitReached` | More than `deployment.maxProperties` properties |
| 409 | `PropertyNotDisabled` | Delete requested for an enabled property |
| 409 | `PropertyNotReady` | Enable requested while the property fails readiness checks; `fieldErrors` names each problem |
| 409 | `LastPlatformAdmin` | Removing the last platform admin |
| 409 | `NoDraft`, `DraftExists` | Publishing without a draft; creating a second draft |
| 409 | `FieldHasValues` | Changing the `key` or `type` of a profile field that has values, or deleting it without `deleteValues` |
| 409 | `FieldKeyInUse` | A profile field key already exists in the property |
| 409 | `FieldLimitReached` | More than `profileFields.maxFields` fields |
| 409 | `OptionInUse` | Removing an option that has values (retire it instead) |
| 409 | `BuiltInField` | Deleting a built-in field or changing a locked attribute of one |
| 413 | `TooLarge` | A bundle or a property file exceeds its limit (same code as avatars and media) |

---

## 1. Deployment mode

Setting `deployment.mode` (Enum `SingleProperty`, `MultiProperty`; default `SingleProperty`; Restart; environment variable `TK_DEPLOYMENT_MODE`).

| | `SingleProperty` | `MultiProperty` |
|---|---|---|
| Properties | Exactly one, loaded from `property.directory` (`TK_PROPERTY_DIR`) and stored in the database | Zero or more registered by platform admins (at most `deployment.maxProperties`), plus the platform realm |
| Resolution | Always the one property (headers are still validated, section 2) | Section 2 order |
| `PlatformAdmin`, platform routes, platform realm | Do not exist; `/api/v1/platform/*` returns `404 NotAvailable` | Exist |
| Highest property role | `SystemAdmin` has every right, including domains, apps, deployment settings, and property import | `SystemAdmin` manages its own property except platform-managed fields (domains, app ids) and deployment settings |
| Settings `GET/PUT /api/v1/settings` | `SystemAdmin` | `PlatformAdmin` only (platform realm) |
| Environment variables for per-property keys | Existing `TK_*` names apply to the one property | `TK_PROPERTY_<ID>_*` names (section 5.4) |

Startup checks (the server refuses to start with a message naming the problem):

- `SingleProperty` with more than one non-deleted property in the database: "Deployment mode is SingleProperty but N properties are registered; set deployment.mode to MultiProperty."
- `SingleProperty` where the database holds a property whose id differs from the `property.json` in `property.directory`: "property.directory holds property <a> but the database holds property <b>."
- `MultiProperty` while any media row still uses legacy unprefixed storage keys (section 4.4): "Media key migration is not complete (N objects remaining); run in SingleProperty mode until /api/v1/health/ready reports it done."
- `MultiProperty` with `platform.hosts` empty: "platform.hosts must name at least one host in MultiProperty mode."

Switching `SingleProperty` to `MultiProperty`: set `platform.hosts` and `platform.bootstrapAdminEmail`, set the mode, restart. The existing property keeps its data, accounts, and sessions. Switching back is allowed only when exactly one non-deleted property remains.

---

## 2. Property resolution

Resolution runs for every request in the PreRouting stage, before authentication. Its result is stored on `RequestContext` and nothing downstream may change it.

### 2.1 Inputs

| Input | Notes |
|---|---|
| `X-TK-Property` | Exact, case-sensitive match against registered property ids |
| `X-TK-App` | Case-insensitive match against the resolved property's registered apps (`propertyapps`) |
| Host | `X-Forwarded-Host` when `deployment.trustForwardedHost` is true (default; nginx and CloudFront set it), otherwise `Host`. Lowercased, port removed, trailing dot removed. Matched case-insensitively against `propertydomains.host` (every domain kind: `Web`, `Api`, `Media`, `Redirect`, `Alias`) |
| Access token | Carries the property it was issued for (section 3.2). A hint, never a selector |
| Platform hosts | `platform.hosts` (comma-separated hostnames) select the platform realm when no `X-TK-Property` is sent |

### 2.2 Order (first match wins)

1. **Special routes** resolve on their own and skip steps 2 to 5: webhooks (section 2.5), `/api/v1/health*`, `/openapi.json`, `/swagger`, and CORS preflight (`OPTIONS`). Health and OpenAPI are deployment-level and need no property.
2. **`X-TK-Property` present:** the named property. Unknown id, or a property whose status is not `Enabled`: `404 PropertyNotFound`. Then, if `X-TK-App` is present, it must be one of the property's enabled apps, else `403 AppNotRegistered`. In `MultiProperty`, when `X-Client` is `mobile` and `deployment.requireAppHeader` is true (default), a missing `X-TK-App` is also `403 AppNotRegistered`.
3. **Host matches a property domain:** that property (same status rule as step 2).
4. **Host is a platform host** (`MultiProperty` only): the platform realm. Only `/api/v1/platform/*`, `/api/v1/auth/login`, `/api/v1/auth/refresh`, `/api/v1/auth/logout`, `/api/v1/auth/me`, `/api/v1/auth/password/*`, and `/api/v1/me/password` are served in the realm; every other route returns `404 PropertyNotFound`.
5. **`SingleProperty` mode:** the one property.
6. Otherwise: `404 PropertyNotFound`.

In `SingleProperty` mode steps 2 and 3 still run: an `X-TK-Property` naming a different id is `404 PropertyNotFound` (it catches a mis-built app), and `X-TK-App` is validated against the apps registered from `property.json` `mobile`. A host that matches no domain is fine (step 5), so `127.0.0.1` and `localhost` work unchanged.

`/api/v1/platform/*` requests always resolve to the platform realm, regardless of `X-TK-Property` (which is then ignored), and only on a platform host; elsewhere they return `404 PropertyNotFound`.

### 2.3 Hint agreement

All hints present on a request must agree, checked in this order:

| Conflict | Result |
|---|---|
| `X-TK-Property` names property A and the host is a domain of property B | `400 PropertyMismatch` |
| The host is a platform host and `X-TK-Property` names property A | Allowed: resolves to A (the admin console on the platform host managing a property with a delegated session, section 6.2) |
| The access token was issued for a property other than the resolved one | `401 Unauthorized` ("A valid bearer token is required."), so the client refreshes, fails, and signs out |
| A refresh token, one-time token, unsubscribe token, local upload signature, or media cookie issued for another property | Treated exactly as an unknown token for its route (`401 InvalidRefreshToken`, `400 InvalidToken`, `403 InvalidSignature`) |

Caller-supplied headers never broaden a session: a token is only ever accepted for the property it was issued for.

### 2.4 Anonymous routes with their own verification

| Route | Property comes from |
|---|---|
| `/api/v1/media/files/*`, `/api/v1/media/avatars/*` | Host or header (section 2.2). The storage key itself begins with `p/<propertyId>/` and must match the resolved property, else `404` |
| `/api/v1/media/local-uploads/*` | Host or header; the HMAC covers the property id, so a signature from another property fails with `403 InvalidSignature` |
| `/api/v1/email/unsubscribe` | Host (links are built from the property's public base URL); the token's HMAC covers `{ propertyId, userId, category }` |
| `/api/v1/property*`, `/api/v1/public/*`, `/api/v1/auth/*` | Host or header like any other route |
| `/api/v1/updates/manifest` (live updates milestone) | `X-TK-Property` and `X-TK-App` |

### 2.5 Webhooks

Webhooks never read `X-TK-Property`, `X-TK-App`, or the host. Each verifies first and resolves from verified content, except where verification itself needs the property's secret, in which case the property comes from the URL and the signature then proves it.

| Webhook | Resolution | Verification |
|---|---|---|
| Stripe | `POST /api/v1/webhooks/stripe/{propertyId}` (new, canonical: each property's Stripe account is configured with its own URL). The legacy `POST /api/v1/webhooks/stripe` is accepted only in `SingleProperty` mode and resolves to the one property | Signature with that property's `stripe.webhookSecret`. After parsing, an event whose object carries metadata `tk_property` (set on every Checkout Session and subscription the engine creates) must name the same property, else the event is recorded as `Ignored` with outcome `PropertyMismatch` |
| Apple App Store Server Notifications V2 | `POST /api/v1/webhooks/apple` (unchanged URL). After the JWS verifies to Apple's root, `data.bundleId` is matched against iOS `propertyapps` | Apple chain (no per-property secret). Then `data.appAppleId` must equal the property's `appStore.appAppleId` when that is set |
| Google Play RTDN | `POST /api/v1/webhooks/google` (unchanged URL). After the Pub/Sub OIDC token verifies (Google signature and issuer), the decoded message `packageName` is matched against Android `propertyapps` | Then the token's audience and email must equal the resolved property's `googlePlay.pubSubAudience` and `googlePlay.pubSubServiceAccountEmail` |
| MediaConvert (SNS) | The job id is looked up through `ux_media_jobid` (deployment-wide, section 4.2); the media row's `propertyid` is the property. Jobs also carry user metadata `tk_property`, which must match | SNS signature (deployment-level topic) |

A webhook that verifies but matches no property (unknown bundle id, unknown package, unknown job) returns `200 { "message": "Ignored." }`, is logged with the store and the unmatched identifier, counts `trelliskit.property.resolutions` with `outcome=webhook_unmatched`, and stores nothing. Webhooks for a `Disabled` property are still processed (billing state must stay correct); webhooks for a property being deleted are ignored.

### 2.6 Request context

`RequestContext` gains:

| Member | Type | Notes |
|---|---|---|
| `PropertyId` | string | Never null after resolution; `platform` for the realm, `unresolved` only on the error path before a response |
| `Property` | `PropertyScope` | The resolved property: id, published configuration, effective settings, integration clients, access configuration, profile field definitions |
| `ResolutionSource` | `PropertyResolutionSourceEnum` | `Header`, `Host`, `Platform`, `Single`, `Webhook`, `Special` |
| `AppId` | string? | The matched `X-TK-App` |
| `PrincipalKind` | `PrincipalKindEnum` | `Member`, `PlatformAdmin`, `Delegated`, `BreakGlass`, `Anonymous` |

The resolved property id is stamped on request history (`propertyid` column), every log line (`[p:<id>]` prefix), every span (`trelliskit.property` attribute), and every engine metric (`trelliskit.property` label, section 11), and is passed explicitly to every service and database call.

### 2.7 Registry cache

The property registry (ids, status, domains, apps, current version) is held in memory and rebuilt when it changes. Each instance also checks `max(properties.updatedutc)` every `deployment.propertyCacheSeconds` (default 30) and reloads when it advanced, so a change made on one ECS task reaches the others within that interval. Disabling a property therefore takes effect everywhere within `deployment.propertyCacheSeconds`.

### 2.8 CORS

Allowed origins are the union of `cors.allowedOrigins` and `https://<host>` for every `Web` and `Alias` domain of every enabled property, recomputed with the registry cache.

---

## 3. Accounts, sessions, and tokens per property

### 3.1 Accounts

Accounts are separate per property (decided). Within a property everything in Phase 1 holds. Across properties:

- The same email can register in two properties independently: different passwords, verification, sign-in links, profiles, roles, memberships, notifications, and data.
- Apple, Google, and Facebook identities are unique per property (`externalidentities` unique on `propertyid, provider, subject`). Each property has its own OAuth client ids, so a provider token minted for property A's audience fails audience validation in property B (`401 InvalidIdentityToken`).
- `auth.bootstrapAdminEmail` is a per-property setting (section 5): that email becomes `SystemAdmin` of that property only.
- Lockout counters, password reset and verification rate limits, and sign-up code attempt limits are per property and per caller.

### 3.2 Sessions and tokens

- `authsessions`, `refreshtokens`, and `onetimetokens` carry `propertyid`. Every lookup includes it.
- Access token payload version 2 adds `Pid` (property id) and `Pk` (principal kind: `Member`, `PlatformAdmin`, `Delegated`). `AccessTokenPayload.V` becomes 2. Version 1 tokens (no `Pid`) are accepted in `SingleProperty` mode only and treated as the one property; in `MultiProperty` they are rejected (`401`) and the member signs in again (acceptable, as in the engine rename).
- Refresh tokens rotate as today; the refresh route resolves the property first and looks the token up within it.
- Web refresh cookie `tk_refresh` is unchanged. Cookies are host-scoped, and every property has its own domains, so cookies never cross properties.
- The session cache key is `<propertyId>:<sessionId>`.

---

## 4. Data isolation

### 4.1 Migration "Property scoping"

One migration, the next free version in both `SqliteSchema.cs` and `PostgresqlSchema.cs` (version 6 at the time of writing, unless another milestone lands a migration first; numbering follows whatever is next). `SchemaMigration` gains placeholder substitution: the driver replaces `{{propertyId}}` in statements with the escaped id of the property in `property.directory` (`Sanitizer.Str`), which is the property that owns every pre-existing row.

For each property-owned table, in this order inside the migration's single transaction:

| Step | SQLite | PostgreSQL |
|---|---|---|
| Add the column | `ALTER TABLE t ADD COLUMN propertyid TEXT NOT NULL DEFAULT '';` | `ALTER TABLE t ADD COLUMN IF NOT EXISTS propertyid TEXT NOT NULL DEFAULT {{propertyId}};` |
| Backfill | `UPDATE t SET propertyid = {{propertyId}};` | (done by the constant default, no table rewrite) |
| Remove the default | (SQLite cannot drop a default; the empty default is kept so a write that forgets the column produces `''`, which no query matches and the isolation suite detects) | `ALTER TABLE t ALTER COLUMN propertyid DROP DEFAULT;` |
| Rebuild indexes | `DROP INDEX IF EXISTS <name>;` then `CREATE [UNIQUE] INDEX IF NOT EXISTS <name> ON t (propertyid, <original columns>) [<original WHERE>];` | same |
| Property index | `CREATE INDEX IF NOT EXISTS idx_<t>_property ON t (propertyid);` on every table that has no other index starting with `propertyid` (used by property purge and export) | same |

No foreign keys to `properties` are added (SQLite cannot add them with `ALTER TABLE`, and both providers must behave the same); integrity is enforced by the data-access layer and the isolation suite.

Property-owned tables in the current schema (every one gets `propertyid`):

| Migration of origin | Tables |
|---|---|
| 1 Request history | `requesthistory` (also accepts the reserved values `platform` and `unresolved`) |
| 2 Identity and membership | `users`, `credentials`, `externalidentities`, `authsessions`, `refreshtokens`, `onetimetokens`, `roleassignments`, `rankhistoryentries`, `usergoals`, `subscriptions`, `subscriptionevents`, `entitlements`, `signupcodebatches`, `signupcodes`, `signupcodetags`, `signupcoderedemptions`, `auditrecords` (also accepts `platform`) |
| 4 Feed and engagement | `feedposts`, `feedpostrevisions`, `mediaattachments`, `mediavariants`, `comments`, `commentrevisions`, `reactions`, `reactioncounts`, `mentions`, `notifications`, `notificationactors`, `notificationpreferences`, `notificationhours`, `pushtokens`, `pushdeliveries`, `notificationfanouts`, `reports`, `moderationactions`, `blocks`, `userpermissions`, `linkpreviews`, `wordfilterterms`, `wordfilterhits`, `displaynamewords`, `emailsends` |
| 5 Labels, tags, and content audience | `labels`, `tags` |

Deployment-level tables (unchanged): `schema_migrations`, `jobleases`.

Tables added by milestones that land before this migration (live updates, content manifest, feature flags, AI endpoints, assistants, conversations) are property-owned and are added to this migration by the backend team with the same steps. Tables created after this migration include `propertyid` from their first `CREATE TABLE`, as the first column of every index and unique constraint.

`authsessions` also gains `principalkind TEXT NOT NULL DEFAULT 'Member'` (section 6.2).

### 4.2 Composite unique constraints and indexes

Every existing index and unique index is rebuilt with `propertyid` prepended, keeping its name, its other columns, and its `WHERE` clause, except the deployment-wide indexes listed below. Primary keys are not changed: every primary key is either a random 32-character prefixed id (globally unique) or a composite of such ids, so it cannot collide across properties.

Unique constraints after the migration (the ones that carry meaning per property):

| Table | Unique |
|---|---|
| `users` | `ux_users_email (propertyid, lower(email))`, `ux_users_handle (propertyid, lower(handle))`, `ux_users_storeaccounttoken (propertyid, lower(storeaccounttoken))` |
| `credentials` | `ux_credentials_userid (propertyid, userid)` |
| `externalidentities` | `ux_externalidentities_provider_subject (propertyid, provider, subject)`, `ux_externalidentities_userid_provider (propertyid, userid, provider)` |
| `refreshtokens`, `onetimetokens` | `ux_refreshtokens_tokenhash (propertyid, tokenhash)`, `ux_onetimetokens_tokenhash (propertyid, tokenhash)` |
| `roleassignments` | `ux_roleassignments_scope (propertyid, userid, role, resourcetype, resourceid)` |
| `subscriptions` | `ux_subscriptions_store_storeid (propertyid, store, storesubscriptionid)` |
| `subscriptionevents` | `ux_subscriptionevents_store_eventid (propertyid, store, storeeventid)` |
| `signupcodes` | `ux_signupcodes_code (propertyid, lower(code))` |
| `signupcoderedemptions` | `ux_signupcoderedemptions_code_user (propertyid, signupcodeid, userid)` |
| `feedpostrevisions`, `commentrevisions` | `(propertyid, feedpostid, revision)`, `(propertyid, commentid, revision)` |
| `reactions` | `ux_reactions (propertyid, targettype, targetid, userid, reactionkey)` |
| `mentions` | `ux_mentions (propertyid, sourcetype, sourceid, mentioneduserid)` |
| `notifications` | `ux_notifications_group (propertyid, recipientuserid, groupkey) WHERE readutc IS NULL AND groupkey IS NOT NULL` |
| `pushtokens` | `ux_pushtokens_token (propertyid, token)` |
| `notificationfanouts` | `ux_notificationfanouts_source (propertyid, type, sourceid)` |
| `reports` | `ux_reports_open (propertyid, reporteruserid, targettype, targetid) WHERE status = 'Open'`, `ux_reports_system_open (propertyid, targettype, targetid) WHERE status = 'Open' AND reporteruserid IS NULL` |
| `linkpreviews` | `ux_linkpreviews_urlhash (propertyid, urlhash)` (link preview cache is per property) |
| `wordfilterterms` | `ux_wordfilterterms_normalized (propertyid, normalizedterm)` |
| `emailsends` | `ux_emailsends (propertyid, userid, category, sourceid)` |
| `labels` | `ux_labels_target (propertyid, targettype, targetid, labelnormalized)` |
| `tags` | `ux_tags_target (propertyid, targettype, targetid, tagkeynormalized)` |

Indexes kept deployment-wide (not prefixed), because only deployment-wide prune, expiry, and webhook paths use them, and those paths never return data to a caller without first binding to the row's `propertyid`:

| Index | User |
|---|---|
| `idx_requesthistory_createdutc` | `RequestHistoryPruneWorker` |
| `idx_authsessions_expiresutc`, `idx_refreshtokens_expiresutc` | Session expiry cleanup |
| `idx_notifications_prune (readutc)`, `idx_notifications_created (createdutc)` | `NotificationPruneWorker` |
| `idx_linkpreviews_expires` | `LinkPreviewPruneWorker` |
| `idx_wordfilterhits_created` | Word filter hit pruning |
| `idx_media_cleanup (status, uploadexpiresutc)` | `MediaCleanupWorker` |
| `ux_media_jobid (processorjobid) WHERE processorjobid IS NOT NULL` | MediaConvert webhook (stays unique deployment-wide) |

Every other index named in migrations 1 to 5 (for example `idx_feedposts_feed`, `idx_comments_top`, `idx_notifications_list`, `idx_media_status`, `idx_reports_queue`, `idx_labels_lookup`, `idx_tags_lookup`, `idx_displaynamewords_word`) becomes `(propertyid, ...)`. Queue claim indexes (`idx_media_status`, `idx_notifications_push`, `idx_notificationfanouts_status`, `idx_feedposts_schedule`, `idx_pushdeliveries_receipts`) are property-prefixed because queue workers claim per property (section 4.4).

### 4.3 Data-access rules

These are the AUTHENTICATION.md "Tenant Data Isolation" query and write patterns, applied in the shared helpers so no service can bypass them.

1. **Binding (decided 2026-11-12, replaces explicit `propertyId` parameters).** Data access is bound to one property when it is created (`DatabaseDriverBase.ForProperty(propertyId)`); methods do not take `propertyId`. Every SQL builder adds the `propertyid` predicate (reads, joins, `EXISTS` subqueries, `UPDATE`, `DELETE`) and the `propertyid` value (`INSERT`) from the binding, overwriting whatever the model carries. `PropertyScopeGuard` refuses any statement on a property-owned table that lacks the `propertyid` predicate (string literals do not count). Services and workers exist once per property, so caches, rate limits, and job leases are per property. Unbound access throws, and tests assert every data accessor is bound. The only unbound accessors are the deployment-wide ones on a reviewed allow-list (registry lookups by host and app id, the prune and expiry deletions in 4.2, MediaConvert job lookup, job leases, platform tables); each returns rows that include `PropertyId`, and callers bind to that id before any further call. Rules 2 to 5 below describe what the bound builders and guard enforce.
2. **Reads.** Every `WHERE` on a property-owned table includes `propertyid = <id>` through the dialect helper `D.Scope(propertyId)` (or `D.Scope(alias, propertyId)` in joins). Every joined table is scoped too (`a.propertyid = <id> AND b.propertyid = <id>`), and every `EXISTS` subquery (labels, tags, profile filters, blocks) carries the same predicate. A lookup by id alone is a bug.
3. **Writes.** Every `INSERT` names `propertyid` and takes the value from the method's `propertyId` parameter, overwriting whatever the model carries. Every `UPDATE` and `DELETE` includes the scope predicate. Models gain `PropertyId` (string, never null after load).
4. **Guard.** `MethodsBase.RequireProperty(propertyId, allowPlatform)` runs first in every scoped method: it throws `ArgumentException` for null, empty, malformed, or reserved ids (`platform` is accepted only by user, credential, session, refresh token, one-time token, role assignment, and audit methods). A violation also increments `trelliskit.isolation.violations` and logs at error level.
5. **References across objects.** When a request names another object (a parent comment, a mention handle, a media id to attach, a user to block, a sign-up code), the referenced object is loaded with the same property id. A reference that does not resolve in the property is `404 NotFound` (or the route's own not-found error), never a cross-property read.
6. **Continuation tokens** include the property id in their fingerprint; a token from another property is `400 InvalidContinuationToken`.
7. **Exports and purges** are per property: `GET /api/v1/me/export` reads only the caller's property; property purge deletes `WHERE propertyid = <id>` in bounded batches table by table.
8. **Delegated sessions** (section 6.2) are the only rows that reference a user in another property (`authsessions.userid` points at a platform realm user while `authsessions.propertyid` is the managed property). The guard allows this only when `principalkind = 'Delegated'`.

### 4.4 Per-property caches, rate limits, workers, and storage

| Concern | Rule |
|---|---|
| In-memory caches | Keys are prefixed `<propertyId>:` (sessions, entitlements, compiled word filter, reaction set, profile field definitions, link previews, provider JWKS are per provider and may be shared) |
| Rate limits | Sliding windows are keyed `<propertyId>:<scope>:<caller>`; limit values come from settings (some overridable per property, section 5.3) |
| Queue workers (`MediaProcessingWorker`, `NotificationFanoutWorker`, `PushDeliveryWorker`, `FeedPublishWorker`, `ModerationDueWorker`, `SubscriptionReconciliationWorker`, `SignupCodeReminderWorker`, `FeedPostEmailService` batches, and later workers) | Each pass iterates enabled properties in id order (bounded by `deployment.maxProperties`) and claims at most `max(1, batchSize / enabledPropertyCount)` items per property, so one busy property cannot starve the others. Every item is processed with its row's `propertyid` and that property's settings and integrations. The singleton lease in `jobleases` stays per worker (deployment-level) |
| Prune and expiry workers | Run deployment-wide on the indexes in 4.2; they only delete |
| Disabled properties | Queue workers skip them except `SubscriptionReconciliationWorker` (billing state). Properties being deleted are skipped by all workers except `PropertyPurgeWorker` |
| Object store keys | Every key begins with `p/<propertyId>/`, for example `p/example/in/med_x/original`, `p/example/media/med_x/...`, `p/example/public/video/med_x/...`, `p/example/removed/med_x/...`, `p/example/avatars/usr_x_1731350000.jpg`, `p/example/property/<sha256>`. `MediaKeys` takes the property id. Local storage uses the same keys under `storage.directory` |
| Legacy keys | Rows created before the migration keep their unprefixed keys (`mediaattachments.keyprefix = ''`; new rows store `p/<id>/`). `MediaKeyMigrationWorker` (single instance, `jobleases`) copies each legacy object to its prefixed key, verifies size and SHA-256, updates the row, then deletes the old object. Legacy avatars are moved the same way. `/api/v1/health/ready` reports `Degraded` with the remaining count until it finishes, and `MultiProperty` refuses to start until it has (section 1) |
| Media delivery | CloudFront signed cookie policies cover `https://<property media domain>/p/<propertyId>/*` only; the local `tk_media` cookie HMAC covers the property id. A cookie for property A cannot read an object of property B even when both are served by one distribution and one bucket |
| Link preview images | Stored per property under `p/<propertyId>/` like other media |

### 4.5 Cross-property isolation test suite

`PropertyIsolationSuite` in `Test.Shared/Suites`, run by every test host on both providers (`Sqlite` and `Postgresql`). It starts the server in `MultiProperty` mode with two properties: `example` (the sample) and `second` (a test-local fixture folder under `Test.Shared`, with its own domains `second.test` and app ids `test.second.trelliskit`).

Fixture data, created identically in both properties through the API: members with the same email and the same handle in both; a `SystemAdmin`, a `FeedModerator`, and a plain member in each; Google and Apple identities with the same provider subject in both; feed posts (Members, SignedIn, and Public audience), comments with replies, reactions, mentions, media (image and video, attached and unattached), notifications, push tokens, reports, moderation actions, blocks, sign-up codes with redemptions, subscriptions with events (fake store clients), comps, labels and tags (with the same label strings), word filter terms, profile fields with the same keys but different options and values, and an audit trail. Each property's data also includes a marker string (`ISOLATION-A-...`, `ISOLATION-B-...`) in every free-text field.

Cases:

| # | Case | Expectation |
|---|---|---|
| 1 | **Route sweep.** The suite reads `/openapi.json` and, for every route, issues the request as each caller tier of property A, substituting every path id with the matching object id from property B (and sending B's ids in bodies where the route takes ids) | `404` (or the route's documented not-found error, or an empty `EnumerationResult`), never `2xx` with data. A test fails if any route in the OpenAPI document has no sweep entry (allow-list: health, OpenAPI, webhooks, platform routes, which have their own cases) |
| 2 | **Leak scan.** Every response body from case 1 and from normal A-side calls | Contains no id, handle, email, or marker string belonging to B |
| 3 | **No side effects.** A row-hash of every property-owned table filtered to `propertyid = 'second'`, before and after the sweep | Unchanged |
| 4 | Token binding | A's access token with `X-TK-Property: second` is `401`; A's refresh token on B's host is `401 InvalidRefreshToken`; A's verification, reset, and unsubscribe tokens in B are `400 InvalidToken` |
| 5 | Hints | `X-TK-Property: example` on host `second.test` is `400 PropertyMismatch`; `X-TK-App: test.second.trelliskit` with `X-TK-Property: example` is `403 AppNotRegistered`; a disabled property is `404 PropertyNotFound` |
| 6 | Separate accounts | Registering the same email in both succeeds; signing in to A with B's password fails; deleting the account in A leaves B's untouched; the same Google subject signs in to two different members |
| 7 | Enumeration and discovery | Labels, tag keys, and tag values discovery in A never count B's objects; member search in A never returns B's members; a continuation token from A used in B is `400 InvalidContinuationToken` |
| 8 | Workers | Publishing a post in A fans out notifications, pushes, and emails only to A's members; reconciliation and reminders touch only the row's property; claims are fair across properties (a backlog of 1000 items in A does not delay B's single item past one pass) |
| 9 | Webhooks | A Stripe event on `/webhooks/stripe/second` signed with A's secret is rejected; an Apple notification for B's bundle id updates only B; a Google RTDN for A's package with B's audience is rejected |
| 10 | Media | A's grant cookie cannot read B's object keys; a local upload URL signed for A fails in B; a media id from B cannot be attached in A |
| 11 | Platform | A property `SystemAdmin` cannot call `/api/v1/platform/*`; a delegated session for A cannot act in B; platform audit records every delegation |
| 12 | Static checks | Reflection: every public method of every `I*Methods` interface has `string propertyId` as its first parameter, except the reviewed allow-list. A recording dialect captures the SQL of every scoped method in `Shared/*MethodsBase` and asserts every statement touching a property-owned table contains `propertyid` in each `WHERE`, `JOIN`, and `INSERT` column list. After the suite, no property-owned row has `propertyid = ''` |

The suite is required for the milestone to be marked done and runs in CI on every pull request.

---

## 5. Per-property integration settings and secrets

### 5.1 Setting scopes

Every key in `SettingsRegistry.cs` gets a scope:

| Scope | Stored in | Edited by | Notes |
|---|---|---|---|
| `Platform` | `trelliskit.json` plus `TK_*` environment overrides (as today) | `SystemAdmin` in `SingleProperty`; `PlatformAdmin` in `MultiProperty` (`/api/v1/settings` or `/api/v1/platform/settings`) | Every existing key not listed in 5.2 or 5.3 |
| `Property` | `propertysettings` rows (secrets encrypted) | That property's `SystemAdmin` (`/api/v1/admin/property/settings`) and platform admins (`/api/v1/platform/properties/{id}/settings`) | No platform value; empty means "not configured" (or falls back to the property document where noted) |
| `Overridable` | Platform default in `trelliskit.json`; optional per-property override in `propertysettings` | Default: as `Platform`. Override: as `Property` | The effective value is the override when present, else the platform default |

### 5.2 `Property` scope keys (moved from global settings)

| Integration | Keys (type) | Single-mode environment variable (unchanged) |
|---|---|---|
| Stripe | `stripe.secretKey` (Secret), `stripe.webhookSecret` (Secret), `stripe.priceId` (String) | `TK_STRIPE_SECRET_KEY`, `TK_STRIPE_WEBHOOK_SECRET`, `TK_STRIPE_PRICE_ID` |
| App Store | `appStore.bundleId`, `appStore.appAppleId`, `appStore.issuerId`, `appStore.keyId` (String), `appStore.privateKeyPem` (Secret), `appStore.productId` (String, empty uses `membership.storeProductId`), `appStore.allowSandbox` (Boolean) | `TK_APPSTORE_BUNDLE_ID`, `TK_APPSTORE_APP_APPLE_ID`, `TK_APPSTORE_ISSUER_ID`, `TK_APPSTORE_KEY_ID`, `TK_APPSTORE_PRIVATE_KEY`, `TK_APPSTORE_PRODUCT_ID`, `TK_APPSTORE_ALLOW_SANDBOX` |
| Google Play | `googlePlay.packageName`, `googlePlay.productId`, `googlePlay.pubSubAudience`, `googlePlay.pubSubServiceAccountEmail` (String), `googlePlay.serviceAccountJson` (Secret) | `TK_GOOGLEPLAY_PACKAGE_NAME`, `TK_GOOGLEPLAY_PRODUCT_ID`, `TK_GOOGLEPLAY_PUBSUB_AUDIENCE`, `TK_GOOGLEPLAY_PUBSUB_SERVICE_ACCOUNT`, `TK_GOOGLEPLAY_SERVICE_ACCOUNT_JSON` |
| OAuth | `oauth.appleBundleId`, `oauth.appleServicesId`, `oauth.googleWebClientId`, `oauth.googleIosClientId`, `oauth.googleAndroidClientId`, `oauth.facebookAppId` (String), `oauth.facebookAppSecret` (Secret) | `TK_APPLE_BUNDLE_ID`, `TK_APPLE_SERVICES_ID`, `TK_GOOGLE_WEB_CLIENT_ID`, `TK_GOOGLE_IOS_CLIENT_ID`, `TK_GOOGLE_ANDROID_CLIENT_ID`, `TK_FACEBOOK_APP_ID`, `TK_FACEBOOK_APP_SECRET` |
| Email sender | `email.fromName`, `email.fromAddress` (String, empty uses the property `contact`) | `TK_SMTP_FROM_NAME`, `TK_SMTP_FROM_ADDRESS` |
| Push | `push.enabled` (Boolean, default true), `push.expoAccessToken` (Secret) | `TK_PUSH_ENABLED`, `TK_EXPO_ACCESS_TOKEN` |
| Web and media | `web.publicBaseUrl` (String, empty uses `https://` plus `domains.web`), `media.publicBaseUrl` (String, empty uses `https://` plus `domains.media` in S3 mode), `media.cookieDomain` (String) | `TK_WEB_PUBLIC_BASE_URL`, `TK_MEDIA_PUBLIC_BASE_URL`, `TK_MEDIA_COOKIE_DOMAIN` |
| Accounts | `auth.bootstrapAdminEmail` (String), `auth.bootstrapAdminPassword` (Secret; as built both are global settings and the startup account is created for the primary property only) | `TK_BOOTSTRAP_ADMIN_EMAIL`, `TK_BOOTSTRAP_ADMIN_PASSWORD` |
| Live updates (section 8 of `TRELLISKIT.md`) | The code signing key (`updates.signingKeyPem`, Secret), `updates.checkIntervalMinutes`, and the per-platform minimum binary versions (`mobile.minimumBinaryVersionIos`, `mobile.minimumBinaryVersionAndroid`) are `Property` scope. Update records and channels are property-owned rows. If the live updates contract names these keys differently, its names win and the scope rule here still applies | `TK_UPDATES_SIGNING_KEY` |
| AI (section 9 of `TRELLISKIT.md`) | Model endpoints, assistants, and conversations are property-owned rows; endpoint secrets are encrypted with `platform.secretsKey` (5.4). Any `ai.*` keys are `Property` scope | |

### 5.3 `Overridable` keys

`email.smtpHost`, `email.smtpPort`, `email.username`, `email.password`, `email.useTls` (a property may send through its own SMTP account); `feed.maxBodyLength`, `feed.maxAttachments`, `feed.maxVideosPerPost`, `feed.maxPinned`, `feed.postsPerHour`; `comments.maxBodyLength`, `comments.maxDepth`, `comments.maxAttachments`, `comments.perMinute`, `comments.perDay`, `comments.newAccountHours`, `comments.newAccountPerMinute`, `comments.newAccountPerDay`; `content.editGraceSeconds`; `mentions.maxNotifiedPerItem`; `notifications.defaultHoursStart`, `notifications.defaultHoursEnd`; `moderation.autoHideReportThreshold`, `moderation.reviewTargetHours`, `moderation.wordFilterEnabled`; `email.feedPostEmailsEnabled`, `email.feedPostMinIntervalMinutes`; `linkPreviews.enabled`; `media.maxMemberVideoMegabytes`, `media.maxMemberVideoSeconds`, `media.memberDailyUploadMegabytes`; `push.maxPerUserPerHour`; `signupCodes.firstReminderDays`, `signupCodes.finalReminderDays`.

An override must satisfy the key's type and bounds. Every other existing key (REST, database, logging, telemetry, request history, media storage mode, buckets, processors, CloudFront key pair, rate limits, enumeration limits, link preview fetch limits, worker intervals and concurrency) is `Platform` scope.

### 5.4 Environment variables in `MultiProperty`

`Property` and `Overridable` keys can be set per property from the environment with `TK_PROPERTY_<ID>_<SUFFIX>`, where `<ID>` is the property id uppercased with hyphens replaced by underscores and `<SUFFIX>` is the single-mode variable name without its `TK_` prefix. Example: `TK_PROPERTY_EXAMPLE_STRIPE_SECRET_KEY`. Such a value overrides the database value and the field reports `overriddenByEnvironment: true` and cannot be changed through the API, exactly like platform keys today. In `MultiProperty` the plain single-mode variables of `Property` keys (for example `TK_STRIPE_SECRET_KEY`) are ignored, and startup logs a warning naming each one that is set.

### 5.5 Secrets at rest

- New setting `platform.secretsKey` (Secret, base64 that decodes to 32 bytes, Restart, `TK_SECRETS_KEY`). When empty on first boot it is generated, saved to the settings file, and its presence (never its value) is logged, like the admin token. Production passes it from Secrets Manager.
- Every `Secret` value in `propertysettings` (and every secret on property-owned rows such as AI endpoint `authValue`) is stored as AES-256-GCM ciphertext with a random 12-byte nonce and the associated data `<propertyId>|<key>`, so a ciphertext copied to another property or key fails to decrypt. Columns: `ciphertext`, `nonce`, `keyid` (first 8 hex characters of SHA-256 of the key).
- Rotation: set `platform.secretsKeyPrevious` (Secret, Restart, `TK_SECRETS_KEY_PREVIOUS`) to the old key and `platform.secretsKey` to the new one; values encrypted with the previous `keyid` still decrypt, and `SecretRotationWorker` re-encrypts them. A decrypt failure counts `trelliskit.secrets.decrypt_failures`, makes that integration report not configured (`503 IntegrationNotConfigured` naming the key), and never logs the value.
- Secrets are write-only through every API: reads return `value: null` and `isSet`. Request history redacts the `values` of secret keys in settings bodies.

### 5.6 Moving values from global settings

On the first start after the migration (both modes), `PropertySettingsMigrator` runs once for the property that owned the pre-existing data, when its `properties.settingsmigratedutc` is null:

1. Copy every `Property` scope key that has a value in `trelliskit.json` into `propertysettings` for that property (secrets encrypted). Environment values are not copied; in `SingleProperty` they keep overriding.
2. Write a backup `trelliskit.json.bak-<yyyyMMddHHmmss>` next to the settings file, then remove the copied keys from `trelliskit.json`. `Overridable` keys stay in the file as platform defaults.
3. Set `settingsmigratedutc`, and write an audit record `property.settings.migrate` listing the key names only.

The step is idempotent and logged. `IntegrationSet` becomes `IIntegrationResolver.For(propertyId)`, which builds and caches each property's Stripe, App Store, Google Play, OAuth verifiers, email sender, and push sender from that property's effective settings, and rebuilds them when the property's settings change. `PropertyContext` becomes the per-request `PropertyScope` (section 2.6).

### 5.7 Routes

| Method and path | Who | Notes |
|---|---|---|
| `GET /api/v1/admin/property/settings`, `POST /api/v1/admin/property/settings/enumerate` | `SystemAdmin` of the property | `EnumerationResult<PropertySettingField>` (`objects`) with scalar `propertyId`, restricted to `Property` and `Overridable` keys, each field adding `scope` (`Property` or `Overridable`), `inherited` (true when an `Overridable` key has no override; `value` then shows the platform default), and `section` (`Stripe`, `App Store`, `Google Play`, `Sign-in providers`, `Email`, `Push`, `Web and media`, `Accounts`, `Updates`, `AI`, `Community limits`). Standard search, created windows, labels/tags, key ordering and continuation apply; immutable fields use Unix epoch and empty metadata |
| `PUT /api/v1/admin/property/settings` | same | `{ "values": { "<key>": <value> }, "clear": ["<key>"] }`. All or nothing, same validation errors as `PUT /api/v1/settings`. `clear` removes overrides (or empties a `Property` key). Audited `property.settings.update` (key names only). Returns the `GET` body |
| `POST /api/v1/admin/property/integrations/{name}/test` | same | `name`: `stripe`, `appStore`, `googlePlay`, `email`, `push`. Makes one harmless call with the property's settings (Stripe: retrieve the price; App Store: request a signed status for a test id; Google Play: fetch the app's subscriptions list with page size 1; email: send to the caller; push: validate the access token). `200 { "ok": true, "message": "..." }` or `200 { "ok": false, "message": "<reason>" }` |
| `GET/PUT /api/v1/platform/properties/{id}/settings` | `PlatformAdmin` | Same as the two property routes, for any property |
| `GET /api/v1/settings`, `PUT /api/v1/settings`, `POST /api/v1/settings/restart` | `SystemAdmin` (`SingleProperty`); `PlatformAdmin` on a platform host (`MultiProperty`) | `Platform` keys and `Overridable` defaults only. In `MultiProperty` these routes on a property host return `403 Forbidden` for property admins |

`GET /api/v1/auth/providers` is per property: it reports the resolved property's integrations and public client ids.

---

## 6. Platform administration

### 6.1 `PlatformAdmin` and the platform realm

- `RoleEnum` gains `PlatformAdmin`; `ResourceTypeEnum` gains `Platform`. A `PlatformAdmin` assignment is `{ role: "PlatformAdmin", resourceType: "Platform", resourceId: "*" }` and is valid only on users in the platform realm (`propertyid = 'platform'`).
- Platform realm users sign in with email and password only, on a platform host (`POST /api/v1/auth/login`). Registration and social sign-in in the realm return `404 NotAvailable`. They have no age attestation, membership, profile fields, notifications, or content.
- Bootstrap: when `platform.bootstrapAdminEmail` is set and the realm has no platform admin, startup creates that realm user with the role and emails an invitation (a one-time token with purpose `Invite`, 7 days, that sets the password through `POST /api/v1/auth/password/reset`). The invitation link is also logged once at warning level for first-run setups without email.
- More platform admins are invited with `POST /api/v1/platform/admins`. The last one cannot be removed (`409 LastPlatformAdmin`).
- The break-glass `auth.adminToken` in `MultiProperty`: on a platform host it passes the `Platform` group; on a property-resolved request it acts as that property's `SystemAdmin`. Every use is audited with `actorName: "break-glass"` in the property's audit and the platform audit.
- `AuthorizationService.Classify` gains `RouteGroupEnum.Platform` for `/api/v1/platform/*`, checked first. The rule: resolved to the platform realm, and the caller is a `PlatformAdmin` or the break-glass token; otherwise `403 Forbidden`. In `SingleProperty` the route returns `404 NotAvailable` before authorization.

### 6.2 Delegated property admin sessions

A platform admin acts inside a property through a delegated session, never with the realm token (tokens are bound to one property).

`POST /api/v1/platform/properties/{id}/admin-session` (no body) returns `200 { "accessToken", "accessTokenExpiresUtc", "propertyId", "adminConsoleUrl" }`:

- The session row has `propertyid = <id>`, `userid = <platform user id>`, `principalkind = 'Delegated'`; the token has `Pk = Delegated` and `Pid = <id>`.
- The caller acts as `SystemAdmin` of that property. Routes that act on "me" return `403 Forbidden` (as for the break-glass token). There is no refresh token; the session lasts `platform.delegatedSessionMinutes` (default 60) and a new one is requested when it expires.
- It is used on the property's admin host, or on the platform host with `X-TK-Property: <id>` (section 2.3).
- Audited as `platform.delegate` in the platform audit and in the property's audit, and every action taken with it records the platform admin's id and name as the actor.

### 6.3 Platform routes (`PlatformAdmin`, platform host, `MultiProperty` only)

`PropertyRecord`:

```json
{
  "id": "second",
  "name": "Second Example",
  "status": "Enabled",
  "currentVersion": 4,
  "draftVersion": null,
  "accessModel": "PartiallyGated",
  "domains": [ { "id": "pdm_...", "host": "second.test", "kind": "Web" }, { "id": "pdm_...", "host": "api.second.test", "kind": "Api" } ],
  "apps": [ { "id": "ppa_...", "platform": "Ios", "appId": "test.second.trelliskit", "storeAppId": null, "enabled": true } ],
  "memberCount": 1342,
  "createdUtc": "2026-11-11T20:00:00Z", "updatedUtc": "2026-11-11T21:00:00Z",
  "enabledUtc": "2026-11-11T20:30:00Z", "disabledUtc": null, "disabledReason": null, "deleteScheduledUtc": null,
  "labels": [], "tags": {}
}
```

`status`: `Disabled` (new properties start here), `Enabled`, `Deleting` (purge scheduled), `Deleted` (tombstone; the id stays reserved forever). `domains` (at most 20, kind `Web`, `Api`, `Media`, `Redirect`, `Alias`) and `apps` (at most 10, platform `Ios` or `Android`) are fixed-bound lists. `memberCount` counts non-deleted users.

| Method and path | Body | Success | Errors |
|---|---|---|---|
| `POST /api/v1/platform/properties/enumerate` (and `GET /api/v1/platform/properties`) | `EnumerationQuery`; `filters`: `status`, `accessModel`, `host` (exact domain); `search` over id and name; orderings `CreatedDescending`, `CreatedAscending`, `NameAscending`, `UpdatedDescending` | `200 EnumerationResult<PropertyRecord>` (the realm is never included) | |
| `POST /api/v1/platform/properties` | `{ id, name, source: "Sample" \| "Clone", cloneFromPropertyId?, domains: [{ host, kind }], apps: [{ platform, appId, storeAppId? }], labels?, tags? }`. `Sample` copies the engine's `samples/example` bundle with `id` and `name` replaced; `Clone` copies another property's current version, files, profile field definitions, and access configuration (never members or content) | `201 PropertyRecord` (status `Disabled`) | `400 ValidationFailed`, `409 PropertyIdInUse`, `DomainInUse`, `AppIdInUse`, `PropertyLimitReached` |
| `GET /api/v1/platform/properties/{id}` | | `200 PropertyRecord` | `404 NotFound` |
| `PUT /api/v1/platform/properties/{id}` | any of `name`, `domains` (replaces the list), `apps` (replaces the list), `labels`, `tags` | `200 PropertyRecord`; registry caches reload | `409 DomainInUse`, `AppIdInUse` |
| `POST /api/v1/platform/properties/{id}/enable` | | `200 PropertyRecord` | `409 PropertyNotReady` (readiness: a valid published version, at least one `Web` domain, at least one `Api` domain or an `Alias` the API is served on, and for `FullyGated` or `PartiallyGated` a `membership` section) |
| `POST /api/v1/platform/properties/{id}/disable` | `{ reason }` | `200 PropertyRecord`. Requests resolve to `404 PropertyNotFound` within `deployment.propertyCacheSeconds`; sessions are kept (not revoked) so re-enabling restores them | |
| `DELETE /api/v1/platform/properties/{id}` | `{ "confirm": "<id>" }` | `202 PropertyRecord` (status `Deleting`, `deleteScheduledUtc` = now plus `platform.propertyPurgeDelayHours`) | `409 PropertyNotDisabled`, `400 ValidationFailed` without the confirmation |
| `POST /api/v1/platform/properties/{id}/restore` | | `200 PropertyRecord` (back to `Disabled`) while `Deleting` | `409` after the purge started |
| `POST /api/v1/platform/properties/import` | Raw `application/zip` body (section 7.4). Query: `mode=Create\|NewVersion` (default `Create`), `propertyId` (optional override of the bundle's id), `dryRun` (default false), `publish` (default true), `includeProfileFields` (default true for `Create`, false for `NewVersion`) | `200 BundleImportResult` | `400 InvalidBundle`, `413 TooLarge`, `409 PropertyIdInUse` (`Create`), `404 NotFound` (`NewVersion` of an unknown id) |
| `GET /api/v1/platform/properties/{id}/export` | Query: `version` (default current), `includeProfileFields` (default true) | `200 application/zip`, `Content-Disposition: attachment; filename="<id>-v<version>.zip"` | `404 NotFound` |
| `POST /api/v1/platform/properties/{id}/versions/enumerate` | `EnumerationQuery`; `filters.status`, `filters.source` | `200 EnumerationResult<PropertyVersionSummary>` | |
| `GET /api/v1/platform/properties/{id}/versions/{version}` | | `200 PropertyVersion` | |
| `POST /api/v1/platform/properties/{id}/versions/{version}/publish` | `{ note? }` | `200 PropertyRecord` (publishes that version's content as a new version) | `400 InvalidBundle` if it no longer validates |
| `GET/PUT /api/v1/platform/properties/{id}/settings` | section 5.7 | | |
| `POST /api/v1/platform/properties/{id}/admin-session` | section 6.2 | | |
| `GET /api/v1/platform/settings`, `PUT /api/v1/platform/settings`, `POST /api/v1/platform/settings/restart` | Same bodies as `/api/v1/settings` | Same | |
| `POST /api/v1/platform/admins/enumerate` | `EnumerationQuery`; `search` over email and name | `200 EnumerationResult<{ id, email, displayName, lastSeenUtc, createdUtc, labels, tags }>` | |
| `POST /api/v1/platform/admins` | `{ email, displayName }` | `201` the admin; invitation emailed | `409 EmailInUse` |
| `DELETE /api/v1/platform/admins/{userId}` | | `204`; sessions revoked | `409 LastPlatformAdmin`, `409 CannotRemoveOwnAdmin` |
| `POST /api/v1/platform/audit/enumerate` | `EnumerationQuery`; `filters`: `propertyId` (default the realm), `actorUserId`, `action`, `targetType`, `targetId` | `200 EnumerationResult<AuditRecord>` | |
| `POST /api/v1/platform/request-history/enumerate` | the request history filters plus `filters.propertyId` (including `unresolved` and `platform`) | `200 EnumerationResult<RequestHistoryListItem>` | |
| `POST /api/v1/platform/metrics/properties/enumerate` | `EnumerationQuery` | `200 EnumerationResult<{ propertyId, members, activeMembers, requests24h, errors24h, storageBytes }>` | |

Platform audit actions: `platform.property.create`, `.update`, `.enable`, `.disable`, `.delete`, `.restore`, `.import`, `.export`, `.publish`, `platform.settings.update`, `platform.server.restart`, `platform.admin.invite`, `platform.admin.remove`, `platform.delegate`, `property.settings.update` (when done by a platform admin).

`PropertyPurgeWorker` purges a `Deleting` property after its scheduled time: every property-owned table `WHERE propertyid = <id>` in batches of 1000 rows, every object under `p/<id>/`, then marks the row `Deleted`. While `Deleting`, the property record adds `purgeProgress` `{ tablesDone, tablesTotal, objectsDeleted }` (null before the purge starts).

### 6.4 Property configuration routes (both modes, the property's `SystemAdmin`)

The property document is the `property.json` content (schema version 2, section 7.3) without `profileFields` (managed by section 9 routes).

| Method and path | Body | Success | Errors |
|---|---|---|---|
| `GET /api/v1/admin/property` | | `200 { "version": 4, "document": { ... }, "draft": { "version": 5, "document": { ... }, "updatedUtc", "updatedByUserId" } \| null, "platformManaged": ["id", "domains", "mobile.bundleId", "mobile.androidPackage"] }` (`platformManaged` is `["id"]` in `SingleProperty`) | |
| `PUT /api/v1/admin/property/draft` | `{ "document": { <any top-level sections> } }`; each provided section replaces that section of the draft (a draft is created from the current version when none exists) | `200` the `GET` body | `400 ValidationFailed` (`fieldErrors` keyed by JSON path, for example `brand.colors.light.primary`), `404 NotAvailable` for a platform-managed section in `MultiProperty` |
| `DELETE /api/v1/admin/property/draft` | | `204` | |
| `POST /api/v1/admin/property/draft/publish` | `{ note? }` | `200` the `GET` body; caches reload; audited `property.publish` | `409 NoDraft`, `400 InvalidBundle` |
| `POST /api/v1/admin/property/files/enumerate` | `EnumerationQuery`; `filters.version` (number, or `draft`), `filters.folder` (`brand`, `web`, `legal`, `mobile`); `search` over path | `200 EnumerationResult<{ path, contentType, sizeBytes, sha256, updatedUtc }>` | |
| `PUT /api/v1/admin/property/draft/files/{path}` | Raw body with `Content-Type`; limits in 7.4 | `200 { path, contentType, sizeBytes, sha256 }` | `400 InvalidBundle`, `413 TooLarge`, `415 UnsupportedMediaType` |
| `DELETE /api/v1/admin/property/draft/files/{path}` | | `204` | `409` when the document still references the file (`fieldErrors` names the JSON path) |
| `POST /api/v1/admin/property/versions/enumerate`, `GET /api/v1/admin/property/versions/{version}` | | as the platform routes | |
| `POST /api/v1/admin/property/versions/{version}/restore` | | `200` the `GET` body (creates a draft from that version) | `409 DraftExists` |
| `GET /api/v1/admin/property/export` | `includeProfileFields` | `200 application/zip` | |
| `POST /api/v1/admin/property/import` | Raw zip; `dryRun`, `publish`, `includeProfileFields` | `200 BundleImportResult` (always `NewVersion` of the resolved property; platform-managed sections are ignored with a warning in `MultiProperty`) | `400 InvalidBundle`, `413 TooLarge` |

Audit actions: `property.draft.update`, `property.file.put`, `property.file.delete`, `property.publish`, `property.restore`, `property.import`, `property.export`.

### 6.5 Admin portal sections

**Platform console** (website, served on a platform host, `MultiProperty` only; title "TrellisKit Platform"):

| Section | Content |
|---|---|
| Properties | Enumerated table (status, access model, members, domains, labels and tags filters, search), "New property" (from sample or clone), "Import bundle" (upload, dry-run report, then import) |
| Property detail | Tabs: Overview (status, enable, disable with reason, metrics), Domains and apps (editable lists with conflict errors), Integrations (section 5.7 settings, test buttons), Versions (enumerate, view diff of two versions as JSON paths, publish), Export, "Open admin console" (requests a delegated session and opens the property's admin console), Danger zone (delete with typed confirmation, restore during the window, purge progress) |
| Platform settings | The existing generated settings page for `Platform` keys and `Overridable` defaults |
| Platform admins | Enumerate, invite, remove |
| Audit and request history | Platform audit with `propertyId` filter; request history across properties |

**Property admin console** additions (both modes):

| Section | Content |
|---|---|
| Property | Identity (name, short name, tagline, description, owner, locale, contact), Brand (colors with light and dark preview, logo, icon, favicon uploads), Content and legal (Markdown editors for `web/content/*.md` and `legal/*.md`), Terminology, Reactions (ordered list editor), Features, Access model (section 10), Domains and apps (read-only in `MultiProperty`), Draft bar (unpublished changes, publish with note, discard), Versions (enumerate, restore), Export and import |
| Integrations | Stripe, App Store, Google Play, Sign-in providers, Email, Push, Updates, AI, Community limits (section 5.7) with test buttons |
| Members, then Profile fields | Section 9.5 (list in profile order with drag reorder, editor, preview, fill-rate stats) |

---

## 7. Property storage

### 7.1 Records and versions

- `properties` holds one row per property (and the realm row in `MultiProperty`): id, name, status, current version, timestamps, `settingsmigratedutc`, `lastdirectoryhash` (section 8).
- `propertyversions` holds every version: `version` (1, 2, 3, ... per property), `status` (`Draft`, `Published`, `Superseded`), `source` (`Seed`, `Import`, `Edit`, `Restore`, `DirectorySync`, `Clone`, `Sample`), `note`, `documentjson`, `documenthash` (SHA-256 of the canonical JSON), `createdbyuserid`, `publishedutc`.
- At most one `Draft` and exactly one `Published` version per property. Publishing validates the draft exactly as `PropertyValidator` validates `property.json` today (plus the schema version 2 rules), marks the previous `Published` version `Superseded`, sets `properties.currentversion`, and invalidates caches.
- `documentjson` is the second deliberate JSON column in the engine (after `subscriptionevents.rawpayloadjson`): it is the validated `property.json` document of that version, read whole and never queried inside. Everything that SQL must look up is structured: domains (`propertydomains`), apps (`propertyapps`), files (`propertyversionfiles`, `propertyassets`), settings (`propertysettings`), and profile fields (section 9.7).
- Retention: at most `property.maxVersions` (default 50) versions per property; older `Superseded` versions are pruned by `PropertyAssetCleanupWorker`. The current version and the draft are never pruned.
- Profile field definitions are not versioned with the document. They are live objects; every change is audited (`profileField.*`).

### 7.2 Files and brand assets

- Files are content-addressed. `propertyassets` stores one row per distinct file content per property (`sha256`, `contenttype`, `sizebytes`, `storagekey = p/<propertyId>/property/<sha256>`). `propertyversionfiles` maps `(versionid, path)` to an asset, so unchanged files are shared between versions.
- Storage location `property`: S3 bucket `media.propertyAssetsBucket` in `S3` mode, `storage.directory/property/` in `Local` mode (same keys). The infrastructure adds the bucket; until it is set, `S3` mode stores property files in the images bucket under the same keys.
- `GET /api/v1/property/assets/{file}` (unchanged route) now serves `brand/<file>` from the resolved property's current version: `ETag` is the SHA-256, `Cache-Control: public, max-age=3600`, `Vary: Host, X-TK-Property`.
- New anonymous route `GET /api/v1/property/pages/{group}/{name}` (`group`: `content` or `legal`; `name` without `.md`) returns `200 { "group", "name", "markdown", "version", "updatedUtc" }` from the current version, with the same caching headers, so the website and apps show edited marketing copy and legal text without a rebuild. `POST /api/v1/property/pages/enumerate` lists them (`EnumerationQuery`, `filters.group`).
- SVG files are served with `Content-Security-Policy: default-src 'none'; style-src 'unsafe-inline'`, `Content-Type: image/svg+xml`, and `X-Content-Type-Options: nosniff`.
- Unreferenced assets (no retained version references them) older than 7 days are deleted by `PropertyAssetCleanupWorker`.

### 7.3 `property.json` schema version 2

`PropertyValidator.SupportedSchemaVersion` accepts 1 and 2. Exports always write 2. Version 2 differs from 1 as follows:

| Section | Version 2 |
|---|---|
| `profileFields` | New: an array of profile field definitions in the create-body shape of section 9.5 (no ids, no counts), in profile order. Replaces `profile` |
| `profile` | Accepted only in version 1 and converted on seed (section 9.13). Not written in version 2 |
| `access` | New (section 10.1). Optional in version 1 (absent means `FullyGated`) |
| `membership` | Optional when `access.model` is `Open`; required otherwise |
| `features` | Gains `members` (default true) and `assistants` (default false; same as the existing `ai` flag of section 9 of `TRELLISKIT.md`, which is accepted as an alias) |
| Everything else | Unchanged |

Validation adds: unique profile field keys and option keys, every rule in section 9.4, `access` rules in 10.1, and every referenced file existing in the bundle.

### 7.4 Bundles (import and export)

A bundle is a zip of a property folder:

| Path | Rule |
|---|---|
| `property.json` | Required, at the root |
| `brand/`, `web/`, `legal/`, `mobile/` | Optional; any depth up to 4 folders |
| `server/`, `deploy/`, and anything else | Ignored on import with a warning (deployment values and secrets never travel in a bundle); never written on export |

Limits and checks: at most `platform.bundleMaxFiles` files (default 500) and `platform.bundleMaxMegabytes` uncompressed (default 50; `413 TooLarge` beyond); each file at most 10 MB; compression ratio at most 100 per entry (zip bomb guard); no absolute paths, `..` segments, backslashes, symlinks, or hidden files; path segments match `^[A-Za-z0-9][A-Za-z0-9._-]*$`, total path at most 200 characters; extensions `.json`, `.md`, `.css`, `.txt`, `.png`, `.jpg`, `.jpeg`, `.webp`, `.gif`, `.ico`, `.svg`, `.pem` (public certificates under `mobile/` only; a file containing `PRIVATE KEY` is rejected); content type is sniffed and must match the extension.

`BundleImportResult`:

```json
{
  "propertyId": "second",
  "mode": "Create",
  "dryRun": false,
  "version": 1,
  "published": true,
  "profileFields": { "created": 9, "updated": 0, "unchanged": 0 },
  "files": { "count": 23, "bytes": 1843220 },
  "warnings": [ "server/settings.json was ignored." ],
  "problems": []
}
```

`warnings` and `problems` are bounded (at most 100 each; a final entry says how many more were omitted). Any problem makes the import `400 InvalidBundle` with `fieldErrors` keyed like `property.json:membership.priceUsdCents` or `brand/logo.png`, and nothing is stored. `mode=Create` registers the bundle's `domains` and `mobile` ids as domains and apps (`409 DomainInUse`, `AppIdInUse`). `mode=NewVersion` never changes domains or apps; differences are reported as warnings. With `includeProfileFields`, definitions are merged (section 9.13); without it they are untouched.

Round trip: `import(export(p))` yields a document with the same `documenthash`, the same files, and the same profile field definitions. A test asserts it for the sample and the fixture property.

Each import and export writes a `pbj_` record in the audit trail (`property.import`, `property.export`) with the outcome, file count, bytes, and the actor.

---

## 8. Single-property mode on the same code

Single-property mode is a multi-property deployment with exactly one registered property and resolution that always picks it.

| Concern | `SingleProperty` | `MultiProperty` |
|---|---|---|
| Startup | Load `property.directory`; if no property is registered, import it (source `Seed`): registry row, version 1 published, domains and apps from the document, profile fields seeded (section 9.13), settings migrated (5.6) | If no property is registered and `property.directory` resolves, import it the same way (so a fresh multi-property stack serves the sample); create the realm; bootstrap the platform admin |
| Folder changes | `property.syncFromDirectory` (`OnChange` default, or `Never`). With `OnChange`, at startup the folder's content hash (all bundle files) is compared with `properties.lastdirectoryhash`; when it differs, the folder is imported as a new published version (source `DirectorySync`), keeping today's edit-the-folder workflow. Profile fields are never touched by a sync. With `Never`, the database is the only source after the first seed | Not used; folders arrive as bundles |
| Admin edits | Allowed (section 6.4). With `OnChange`, a later folder change publishes over them; the admin console shows a banner "This property syncs from its folder; edits are replaced when the folder changes" | Allowed |
| Resolution | Always the one property; headers validated | Section 2 |
| Domains and apps | Editable by `SystemAdmin` | Platform-managed |
| Settings | `/api/v1/settings` (platform keys) and `/api/v1/admin/property/settings` (property keys), both for `SystemAdmin`; single-mode env vars apply | Split between platform and property admins |
| Data, isolation, keys | Identical (`propertyid` on every row, `p/<id>/` keys, scoped queries) | Identical |
| Clients | Send `X-TK-Property` and `X-TK-App` exactly as in `MultiProperty` | Same |

Because the code path is identical, every engine test that does not need two properties runs in `SingleProperty` mode, and `PropertyIsolationSuite` runs in `MultiProperty`.

---

## 9. Configurable profile fields

### 9.1 `ProfileField`

```json
{
  "id": "pfd_...",
  "key": "rank",
  "builtIn": false,
  "label": "Level",
  "helpText": "Your current level.",
  "placeholder": null,
  "type": "Rank",
  "options": [
    { "id": "pfo_...", "key": "Beginner", "label": "Beginner", "color": "#9CA3AF", "maxLevel": null, "freeText": false, "retired": false },
    { "id": "pfo_...", "key": "Intermediate", "label": "Intermediate", "color": "#1F5FBF", "maxLevel": null, "freeText": false, "retired": false }
  ],
  "rank": { "levelLabel": "Stars", "maxLevel": 3 },
  "subFields": [],
  "validation": {
    "required": true,
    "minLength": null, "maxLength": null,
    "minValue": null, "maxValue": null, "integerOnly": false,
    "pattern": null, "patternMessage": null,
    "minEntries": null, "maxEntries": null,
    "minDate": null, "maxDate": null, "notInFuture": false
  },
  "collectAt": "SignUp",
  "editableBy": "Member",
  "visibility": "Public",
  "showOnProfile": true,
  "profileOrder": 10,
  "showOnCard": true,
  "searchable": false,
  "filterable": true,
  "enabled": true,
  "valueCount": 1180,
  "labels": [],
  "tags": {},
  "createdUtc": "2026-11-11T20:00:00Z",
  "updatedUtc": "2026-11-11T20:00:00Z"
}
```

| Attribute | Values and rules |
|---|---|
| `key` | `^[a-z][A-Za-z0-9]{0,39}$` (camelCase, because it is a JSON key). Unique per property, case-insensitively. Reserved: `id`, `email`, `displayName`, `handle`, `avatar`, `avatarUrl`, `bio`, `roles`, `flags`, `membership`, `profile`, `labels`, `tags`, `createdUtc`, `updatedUtc`, `isStaff`, `isDeleted`. Immutable once `valueCount > 0` (`409 FieldHasValues`) |
| `label` | 1 to 60 characters. `helpText` 0 to 300. `placeholder` 0 to 100 |
| `type` | `Text`, `LongText`, `Number`, `Date`, `Boolean`, `Url`, `Email`, `Phone`, `Country`, `SingleSelect`, `MultiSelect`, `Rank`, `History`. Changeable only while `valueCount == 0` |
| `options` | Select and rank types only: ordered list, 1 to 200 entries; option `key` `^[A-Za-z0-9_-]{1,64}$` unique per field case-insensitively; `label` 1 to 60; `color` `#RGB`, `#RRGGBB`, or `#RRGGBBAA` (required for `Rank`, optional otherwise); `maxLevel` (`Rank` only, 0 to 20, overrides `rank.maxLevel` for that option); `freeText` (select types: choosing it enables a free text value up to 100 characters); `retired` (hidden from pickers, existing values stay valid and are displayed) |
| `rank` | `Rank` only: `levelLabel` (0 to 30 characters; empty means no level), `maxLevel` (0 to 20) |
| `subFields` | `History` only: 1 to 10 sub-fields (9.2) |
| `validation` | Per type (9.6). `required` is allowed only with `collectAt` `SignUp` or `Profile` |
| `collectAt` | `SignUp` (asked during onboarding), `Profile` (optional at sign-up, editable later), `AdminOnly` (set by staff) |
| `editableBy` | `Member` (the member and staff) or `Staff`. `collectAt: AdminOnly` requires `editableBy: Staff`; `SignUp` and `Profile` require `Member` |
| `visibility` | `Public`, `Members`, `Staff`, `Private` (9.8). `Staff` requires `collectAt: AdminOnly` |
| `showOnProfile`, `profileOrder` | Shown on the profile page, in ascending `profileOrder` (integer 0 to 100000; ties by `createdUtc`). Also the order in onboarding and the editor |
| `showOnCard` | Shown on compact member cards (posts, comments, member lists). At most `profileFields.maxCardFields` (default 3) enabled fields may have it; card order is `profileOrder` |
| `searchable` | `Text` and `LongText` only: words are indexed for member search text |
| `filterable` | Offered as a filter (9.12). Not allowed for `LongText` |
| `enabled` | Disabling hides the field everywhere (payloads, forms, filters) without deleting values; re-enabling restores them |
| `valueCount` | Read-only: members with a value (recomputed in the write batch) |
| `labels`, `tags` | Per the enumeration and metadata spec (`targetType: ProfileField`) |

### 9.2 Sub-fields (`History`)

```json
{ "id": "pfs_...", "key": "dateAwarded", "label": "Date", "type": "Date", "required": false, "maxLength": null, "notInFuture": true, "options": [], "rankFieldKey": null, "withLevel": false, "position": 3 }
```

| Sub-field `type` | Value | Rules |
|---|---|---|
| `Text` | string | `maxLength` 1 to 500 (default 100) |
| `Number` | number | as `Number` (9.6) |
| `Date` | `YYYY-MM-DD` | `notInFuture` optional |
| `Boolean` | boolean | |
| `Url` | string | as `Url` |
| `SingleSelect` | option key | `options` 1 to 50 entries |
| `RankValue` | `{ "key": "Beginner", "level": 1 }` (or `{ "key" }` when `withLevel` is false) | `rankFieldKey` must name an existing `Rank` field of the property; the key must be one of its options (retired allowed only when unchanged), `level` 0 to the option's max level |
| `Member` | `{ "name": "Coach X", "userId": "usr_..." \| null }` | `name` 1 to 100 required; `userId` optional and must be a member of the same property (`400 ValidationFailed` otherwise) |

Sub-field `key` follows the field key pattern and is unique within the field.

### 9.3 Built-in fields

Every property has four built-in field rows (`builtIn: true`), created with the property and never deleted (`409 BuiltInField`). Their values stay top-level in payloads (`displayName`, `handle`, `avatarUrl`, `bio`), never in the `profile` map, and their validation is fixed by Phase 1.

| Key | Type | Editable attributes | Locked |
|---|---|---|---|
| `displayName` | `Text` | `label`, `helpText`, `placeholder` | `collectAt: SignUp`, required, `visibility: Public`, always on profile and card |
| `handle` | `Text` | `label`, `helpText`, `placeholder` | as `displayName` |
| `avatar` | `Image` (built-in only type) | `label`, `helpText`, `collectAt` (`SignUp` or `Profile`), `enabled` | `visibility: Public`, always on card when set |
| `bio` | `LongText` | `label`, `helpText`, `placeholder`, `collectAt` (`SignUp` or `Profile`), `visibility` (`Public`, `Members`, `Private`), `showOnProfile`, `profileOrder`, `enabled` (disabled means bio is not collected) | max 1000 characters, never on cards |

Built-ins do not count toward `profileFields.maxFields`.

### 9.4 Definition rules (summary)

The server rejects a definition with `400 ValidationFailed` (each problem in `fieldErrors`) unless all of these hold: the attribute rules in 9.1 and 9.2; `minLength <= maxLength`, `minValue <= maxValue`, `minDate <= maxDate`, `minEntries <= maxEntries`; `MultiSelect` `maxEntries` at most the number of non-retired options; `pattern` is a valid .NET regular expression of at most 200 characters (evaluated with a 100 ms timeout, `Text` only); the card limit; at most `profileFields.maxFields` (default 50, hard maximum 100) non-built-in fields (`409 FieldLimitReached`).

Changing a definition that has values:

| Change | Rule |
|---|---|
| `key`, `type` | `409 FieldHasValues` |
| Remove an option that has values | `409 OptionInUse`; set `retired: true` instead |
| Tighten validation (shorter `maxLength`, new `pattern`, lower `maxLevel`, fewer `maxEntries`) | Allowed. Existing values are kept and displayed; the new rule applies the next time that value is written |
| `collectAt` to `SignUp` with `required` | Allowed. Existing members without a value get `profileComplete: false` (9.10) |
| `visibility`, `showOnProfile`, `showOnCard`, `enabled`, labels | Allowed; takes effect on the next read (definition caches reload on change) |

### 9.5 Routes

| Method and path | Who | Body | Success | Errors |
|---|---|---|---|---|
| `POST /api/v1/admin/profile-fields/enumerate` (and `GET /api/v1/admin/profile-fields`) | `SystemAdmin` | `EnumerationQuery`; `filters`: `type`, `collectAt`, `visibility`, `enabled`, `builtIn`, `filterable`, `showOnCard`; `search` over key and label; orderings `CreatedDescending` (default), `CreatedAscending`, `ProfileOrderAscending`, `LabelAscending` | `200 EnumerationResult<ProfileField>` | |
| `POST /api/v1/admin/profile-fields` | `SystemAdmin` | `ProfileField` without `id`, `builtIn`, `valueCount`, timestamps; options and sub-fields without ids | `201 ProfileField` (appended at the end of profile order unless `profileOrder` is given) | `400 ValidationFailed`, `409 FieldKeyInUse`, `409 FieldLimitReached` |
| `GET /api/v1/admin/profile-fields/{id}` | `SystemAdmin` | | `200 ProfileField` | `404 NotFound` |
| `PUT /api/v1/admin/profile-fields/{id}` | `SystemAdmin` | any attributes; `options` and `subFields` replace their lists (entries with an existing `id` are updated, entries without one are new, missing entries are removed subject to 9.4) | `200 ProfileField` | `400`, `409 FieldHasValues`, `409 OptionInUse`, `409 BuiltInField` |
| `DELETE /api/v1/admin/profile-fields/{id}?deleteValues=true` | `SystemAdmin` | `{ "confirm": "<key>" }` when values exist | `202` (values are deleted in the background by `ProfileValuePurgeWorker`; the field disappears immediately) or `204` when it had no values | `409 FieldHasValues` without `deleteValues`, `409 BuiltInField` |
| `POST /api/v1/admin/profile-fields/reorder` | `SystemAdmin` | `{ "order": ["pfd_...", ...] }`: every field id of the property (built-ins included; their position only matters for `bio`), at most 104 | `200 { "order": [...] }`; `profileOrder` becomes `index * 10` | `400` when the list is not exactly the property's fields |
| `POST /api/v1/admin/profile-fields/preview` | `SystemAdmin` | `{ "target": "Onboarding" \| "ProfilePage" \| "Card" \| "Editor", "viewer": "Anonymous" \| "SignedIn" \| "Member" \| "Self" \| "Staff", "fields": [ProfileField drafts]?, "values": { "<key>": <value> }? }`. Without `fields`, the saved definitions are used; with them, unsaved edits are previewed | `200 { "target", "viewer", "items": [ { "key", "label", "type", "required", "helpText", "placeholder", "control", "display", "value", "options"? } ] }` (`control` and `display` per 9.11; bounded by the field count) | `400 ValidationFailed` for invalid drafts |
| `GET /api/v1/admin/profile-fields/{id}/stats`, `POST .../stats/enumerate` | `SystemAdmin` | `EnumerationQuery` | `200 EnumerationResult<ProfileOptionCount>`; `objects` contain `{ key, count, labels: [], tags: {} }`. Scalar context: `valueCount`, `fillRate` (of non-deleted members), `updated30d`. Option timestamps use Unix epoch; cursor is field-scoped | |
| `PUT /api/v1/admin/members/{id}/profile` | `SystemAdmin` | `{ "profile": { "<key>": <value> \| null } }` (any field, including `AdminOnly` and `Staff`) | `200` admin member detail | `400 ValidationFailed` |

Audit actions: `profileField.create`, `profileField.update`, `profileField.delete`, `profileField.reorder`, `member.profile` (staff edits of a member's values; key names only, never values).

Members read definitions from `GET /api/v1/property` (`profileFields`, section 10.8): every enabled field whose `visibility` is not `Staff`, with the attributes clients need (no `valueCount`, no staff metadata). Definitions are fixed-bound configuration (at most 104 rows), an allowed exception in `ENUMERATION_AND_METADATA.md` 2.4.

### 9.6 Value shapes and validation per type

| Type | Value in JSON | Stored in | Validation | Filter operators (9.12) |
|---|---|---|---|---|
| `Text` | string | `valuetext`, `valuenormalized` | Trimmed; `minLength` (default 0) to `maxLength` (default 200, cap 1000); optional `pattern`; word filter (`Block` is `400 ContentFiltered`) | `eq`, `prefix`, `hasValue` |
| `LongText` | string (plain text, line breaks kept) | `valuetext` | `maxLength` default 2000, cap 10000; word filter | `hasValue` |
| `Number` | number | `valuenumber` | Finite; `minValue`, `maxValue`; `integerOnly`; at most 4 decimal places | `eq`, `gte`, `lte`, `hasValue` |
| `Date` | `"YYYY-MM-DD"` | `valuedate` (TEXT in both providers, as `dateawarded` today) | Valid calendar date; `minDate`, `maxDate`, `notInFuture` (in UTC) | `gte`, `lte`, `hasValue` |
| `Boolean` | `true` or `false` | `valuebool` | | `eq` |
| `Url` | string | `valuetext` | Absolute `http` or `https`, at most 2048 characters, host required | `hasValue` |
| `Email` | string | `valuetext`, `valuenormalized` | At most 254 characters, one `@`, a dot in the domain | `eq`, `hasValue` |
| `Phone` | string | `valuetext` | E.164: `^\+[1-9][0-9]{6,14}$` | `hasValue` |
| `Country` | ISO 3166-1 alpha-2 string (`"US"`) | `valuetext` | One of the codes in the shared country list (`shared/src/countries.js` and its C# twin `Countries.cs`) | `in`, `eq` (one code, same as a one-value `in`), `hasValue` |
| `SingleSelect` | `{ "key": "Other", "otherText": "..." \| null }` | `valuetext` (key), `othertext` | Key is a non-retired option (a retired key is accepted only when unchanged); `otherText` only for a `freeText` option, 1 to 100 characters, word filtered | `in`, `eq` (one key, same as a one-value `in`), `hasValue` |
| `MultiSelect` | `{ "keys": ["Learning", "Other"], "otherText": "..." \| null }` | `profilevalueoptions` rows; `othertext` on `profilevalues` | Distinct non-retired keys (retired keys kept when unchanged); `minEntries`, `maxEntries`; `otherText` as above | `any`, `all`, `hasValue` |
| `Rank` | `{ "key": "Intermediate", "level": 2 }` | `valuetext` (key), `valuelevel` | Key is a non-retired option (retired kept when unchanged); `level` 0 to the option's `maxLevel` (else `rank.maxLevel`), default 0 | `in`, `minRank`, `maxRank`, `minLevel`, `hasValue` |
| `History` | `[ { "id": "phe_..." \| null, "values": { "<subKey>": <value> } } ]` in display order | `profilehistoryentries` and `profilehistoryvalues` | `maxEntries` 1 to 50 (default 12); each entry validated per sub-field (9.2); required sub-fields present; ids must belong to this member and field | `hasValue` |

`null` clears a value. Empty strings are treated as `null` for text-like types. Every rejected value is reported in `fieldErrors` as `profile.<key>` or `profile.<key>[<index>].<subKey>`, with messages written for members (for example "Must be 100 characters or fewer.", "Choose one of the listed options.", "That date is in the future.").

### 9.7 Value storage

Structured per property, user, and field; no JSON blobs (section 13 has full columns):

- `profilevalues`: one row per (property, user, field) for every type except `MultiSelect` options and `History` entries: `valuetext`, `valuenormalized` (lowercased, trimmed, accents folded), `valuenumber`, `valuedate`, `valuebool`, `valuelevel`, `othertext`, `updatedutc`, `updatedbyuserid`.
- `profilevalueoptions`: one row per selected `MultiSelect` key, with `position`.
- `profilehistoryentries` (`phe_`): one row per entry with `position`; `profilehistoryvalues`: one row per (entry, sub-field) with the same typed columns plus `valueuserid` (for `Member`).
- `profilesearchwords`: lowercased words of `searchable` field values, for member search prefix matching (like `displaynamewords`).

A write replaces the member's rows for the written fields inside one batch (delete then insert, recomputing `profilefields.valuecount` with `UPDATE ... SET valuecount = (SELECT COUNT(DISTINCT userid) ...)`), following the Phase 2 write rules. Deleting an account deletes its profile rows in the account deletion batch.

### 9.8 The `profile` map and visibility

Who sees a value (`visibility` against the viewer):

| `visibility` | Anonymous (Public content cards) | SignedIn, not a member | Member | The member (Self) | Staff |
|---|---|---|---|---|---|
| `Public` | Yes | Yes | Yes | Yes | Yes |
| `Members` | No | No (Yes in `Open`, where every signed-in account counts as a member for visibility) | Yes | Yes | Yes |
| `Private` | No | No | No | Yes | Yes |
| `Staff` | No | No | No | No | Yes |

"Anonymous" only applies where signed-out viewers can see member cards at all (Public content with `anonymousBrowsing`, section 10). Staff here means `SystemAdmin`, `Moderator`, `FeedModerator`, delegated platform admins, and the break-glass token.

Payloads (every one only ever contains enabled, non-built-in fields):

| Payload | `profile` contains |
|---|---|
| `Me` (`GET /api/v1/auth/me`, every route that returns `Me`) | Every field visible to Self (`Public`, `Members`, `Private`), every key present, `null` when unset. Plus `profileMissing` (section 9.10) |
| Public profile (`GET /api/v1/users/{id}`) | Fields visible to the viewer with `showOnProfile: true` and a value; unset keys omitted |
| `UserSummary` (authors on posts and comments, mentions, reactions lists, notifications, member search results) | Fields visible to the viewer with `showOnCard: true` and a value, at most `profileFields.maxCardFields`; unset keys omitted |
| Admin member list (`POST /api/v1/admin/members/enumerate`) | As `UserSummary` with the staff viewer |
| Admin member detail (`GET /api/v1/admin/members/{id}`) | Every field (all visibilities), every key present, `null` when unset |
| Account export (`GET /api/v1/me/export`) | Every value the member has, including disabled fields, except fields with `visibility: Staff` |

Example `Me` fragment:

```json
{
  "displayName": "Pat Example", "handle": "pat", "bio": "...",
  "profile": {
    "rank": { "key": "Intermediate", "level": 2 },
    "affiliation": "Example Club",
    "goals": { "keys": ["Learning"], "otherText": null },
    "rankHistory": [ { "id": "lin_...", "values": { "rank": { "key": "Beginner", "level": 0 }, "awardedBy": { "name": "Coach X", "userId": null }, "affiliation": "Example Club", "dateAwarded": "2015-06-01" } } ],
    "city": "San Jose", "region": "CA", "country": "US",
    "favoriteDrill": null
  },
  "profileComplete": true,
  "profileMissing": []
}
```

### 9.9 Writing values

`PUT /api/v1/me/profile` (Signed in) accepts `{ displayName?, handle?, bio?, profile?: { "<key>": <value> | null } }`, plus the legacy fields during the compatibility window (9.14). Keys in `profile` are merged one by one: a provided key replaces that field's value (for `History` and `MultiSelect`, the whole list), `null` clears it, an omitted key is unchanged. The update is all or nothing. Per-key errors:

| Situation | `fieldErrors["profile.<key>"]` |
|---|---|
| Unknown key, or a field with `visibility: Staff` | "Unknown field." |
| Disabled field | "This field is not available." |
| `editableBy: Staff` | "This field can only be changed by staff." |
| Clearing a `required` field | "This field is required." |
| Invalid value | The type's message (9.6) |

Staff write any field through `PUT /api/v1/admin/members/{id}/profile` with the same shape and rules except the `editableBy` and `Staff` visibility checks.

### 9.10 Onboarding and `profileComplete`

- `profileComplete` is true when `displayName` and `handle` are set, `ageAttested` is true, and every enabled field with `collectAt: SignUp` and `validation.required: true` has a value.
- `profileMissing` lists the keys that keep `profileComplete` false, in profile order (built-ins first: `displayName`, `handle`).
- As in Phase 1, the server does not block routes on `profileComplete`; clients route the member to onboarding (new accounts) or to a "Complete your profile" screen (existing accounts after a new required field is added) and do not let them continue past it until it is complete. Optional `SignUp` fields can be skipped.

### 9.11 Rendering rules (web, mobile, tablet)

Clients render onboarding, the profile editor, profile pages, member cards, and member filters entirely from `profileFields` in `GET /api/v1/property` (with the bundled copy as the offline fallback on mobile). They never hard-code field keys, labels, or option sets. A field whose `type` the client does not know is skipped everywhere (forward compatibility).

| Surface | Rule |
|---|---|
| Onboarding | After account creation and age attestation: built-ins with `collectAt: SignUp` (display name, handle, then avatar and bio when configured), then every enabled field with `collectAt: SignUp`, in profile order. Required fields are marked and must validate before "Continue"; optional ones show "Skip". Server `fieldErrors` are shown inline on the field they name |
| Profile editor | Built-ins, then every enabled field with `editableBy: Member` (`SignUp` and `Profile`), in profile order. `AdminOnly` fields visible to Self are shown read-only with "Set by staff". `helpText` under the label, `placeholder` in the input |
| Profile page | Header: avatar, display name, handle, card fields as compact chips. Body: bio (when shown), then fields from the profile payload in profile order, each with its label and formatted value. Unset fields are not shown. `History` renders as a list in stored order |
| Member card | Avatar, display name, handle, then up to `maxCardFields` card fields inline in profile order (`Rank` as a color bar plus short label) |
| Filters | Section 9.12 controls in the member search filter panel (side panel on tablets, bottom sheet on phones), next to the label and tag filters |
| Admin preview | Uses `POST /api/v1/admin/profile-fields/preview` so web and mobile previews match the server's ordering and visibility |

| Type | `control` | `display` |
|---|---|---|
| `Text` | Single-line input | Text |
| `LongText` | Multi-line input (plain text, no Markdown) | Text with line breaks |
| `Number` | Numeric input (integer keyboard when `integerOnly`) | Locale-formatted number |
| `Date` | Date picker | Locale date (`MMM d, yyyy`) |
| `Boolean` | Switch | "Yes" or "No" (engine strings) |
| `Url` | URL input | Link with `rel="nofollow ugc noopener"`, host shown |
| `Email` | Email input | `mailto:` link |
| `Phone` | Phone input with an E.164 hint | `tel:` link |
| `Country` | Searchable picker from the shared country list | Country name |
| `SingleSelect` | Radio list for up to 5 options, dropdown otherwise; a text input appears for a `freeText` option | Option label (or the free text) |
| `MultiSelect` | Checkbox list or chips; text input for a `freeText` option | Comma-separated labels |
| `Rank` | Option picker with color swatches, plus a level stepper from 0 to the option's max level when `levelLabel` is set | Shared `formatRank(field, value)`: "<option label>" for level 0, otherwise "<option label>, <level> <levelLabel>" (both from config, no hard-coded words), with the color swatch |
| `History` | List editor: add, remove, reorder, at most `maxEntries`; each entry is a small form of its sub-fields (`Member` sub-fields use member search with a free-name fallback; `RankValue` uses the referenced rank field's picker) | Entries in stored order, each line built from sub-fields in sub-field order |

`shared/src` gains `profileFields.js`: definition helpers (`visibleFields`, `cardFields`, `onboardingFields`, `editorFields`), client-side validation mirroring 9.6 (for instant feedback; the server is authoritative), formatters (`formatRank`, `formatValue`), and the country list.

### 9.12 Member search and filters

Routes that enumerate members accept `filters.profile.<key>`:

| Route | Who | Fields that may be filtered |
|---|---|---|
| `POST /api/v1/members/enumerate` (new; and `GET /api/v1/members`) | Per the access decision for feature `members` (section 10) | Enabled, `filterable` fields whose visibility the caller can see |
| `GET /api/v1/members/search` (Phase 2 mention autocomplete) | unchanged | same, as `profile.<key>` query parameters |
| `POST /api/v1/admin/members/enumerate` (and `GET /api/v1/admin/members`) | `SystemAdmin` | Every enabled, `filterable` field |

`POST /api/v1/members/enumerate` returns `EnumerationResult<UserSummary>` (with `labels` and `tags`); orderings `CreatedDescending` (default), `CreatedAscending`, `DisplayNameAscending`; `search` matches display name and handle word prefixes plus `searchable` fields the caller can see.

Filter shape (JSON):

```json
{
  "maxResults": 25,
  "search": "pat",
  "labels": ["coaches"],
  "filters": {
    "profile": {
      "rank": { "minRank": "Intermediate", "minLevel": 1 },
      "country": { "in": ["US", "CA"] },
      "goals": { "any": ["Learning", "Teaching"] },
      "affiliation": { "prefix": "example" },
      "rankHistory": { "hasValue": true }
    }
  }
}
```

GET form: `profile.<key>.<operator>=<value>` (repeatable for list operators), for example `?profile.country.in=US&profile.country.in=CA&profile.rank.minRank=Intermediate`.

| Operator | Value | Meaning |
|---|---|---|
| `eq` | scalar | Equals (case-insensitive for text, exact for numbers and booleans) |
| `prefix` | string, 1 to 100 | `valuenormalized` starts with the normalized value |
| `gte`, `lte` | number or date | Inclusive bounds |
| `in` | up to 20 keys | Value is one of them |
| `any`, `all` | up to 20 keys | `MultiSelect` holds at least one, or all |
| `minRank`, `maxRank` | option key | `Rank` option at or above (or below) that option's position in `options` |
| `minLevel` | integer | `Rank` level at least this |
| `hasValue` | boolean | The member has (or has no) value |

Rules: at most `profileFields.maxFiltersPerQuery` (default 10) profile filters per query; an unknown, disabled, non-filterable, or not-visible field, or an operator not listed for its type in 9.6, is `400 ValidationFailed` with `fieldErrors["filters.profile.<key>"] = "Unknown filter."` (the same message for every reason, so hidden fields are not revealed). Each filter compiles to an `EXISTS` subquery on `profilevalues` (or `profilevalueoptions`, `profilehistoryentries`) scoped by `propertyid` and `fieldid`, served by the indexes in section 13. Rank order filters translate to `valuetext IN (...)` with the option keys at or above the position, computed from the definition (at most 200). The legacy admin filter `rank=<key>` maps to `filters.profile.rank.in = [<key>]` during the compatibility window.

### 9.13 Seeding from `property.json`

When seeding runs:

| Trigger | Behaviour |
|---|---|
| Property created (sample, clone, or bundle import with `includeProfileFields`) | Create the four built-ins, then every definition from the bundle |
| First start after the profile fields migration, for the pre-existing property | When the property has no non-built-in fields: create definitions from its current document, then backfill values from the legacy columns (below) |
| Bundle import with `mode=NewVersion` and `includeProfileFields=true` | Merge: a key that does not exist is created; an existing key is updated only in attributes that 9.4 allows for its current values (a disallowed change is a warning, not applied); fields missing from the bundle are left alone (never deleted) |
| Folder sync in `SingleProperty` (section 8) | Never touches profile fields |

Schema version 2 bundles carry `profileFields` (the create-body shape). Schema version 1 bundles are converted from `profile`:

| Version 1 section | Seeded field |
|---|---|
| (always) | `city` (`Text`, max 100), `region` (`Text`, max 100), `country` (`Country`, filterable): `collectAt: Profile`, `visibility: Public`, `showOnProfile: true`, `showOnCard: false` (these were Phase 1 engine fields for every property) |
| `rank` | Key `rank`, type `Rank`, `label` from `rank.label`, options from `rank.values` (key, label, color), `rank.levelLabel` and `rank.maxLevel` from `levelLabel` and `maxLevel`; `collectAt: SignUp`, `required: true` (the Phase 1 `profileComplete` rule), `visibility: Public`, `showOnProfile: true`, `showOnCard: true` (as `UserSummary` showed it), `filterable: true`; `enabled` from `rank.enabled` |
| `affiliation` | Key `affiliation`, `Text` max 100, `label` from config, `collectAt: Profile`, `visibility: Public`, `showOnProfile: true`, `searchable: true`, `filterable: true`; `enabled` from config |
| `goals` | Key `goals`, `MultiSelect`, options from `goals.values` (`freeText` kept), `collectAt: Profile`, `visibility: Private` (Phase 1 public profiles did not include goals), `showOnProfile: true`, `filterable: true`; `enabled` from config |
| `rankHistory` | Key `rankHistory`, `History`, `label` from config, `maxEntries` from `maxEntries`, sub-fields `rank` (`RankValue`, `rankFieldKey: "rank"`, `withLevel: false`, required), `awardedBy` (`Member`, label from `awardedByLabel`), `affiliation` (`Text`, max 100, label from the `affiliation` label), `dateAwarded` (`Date`, `notInFuture`); `collectAt: Profile`, `visibility: Public`, `showOnProfile: true`; `enabled` from config |

Profile order: `rank` 10, `affiliation` 20, `city` 30, `region` 40, `country` 50, `goals` 60, `rankHistory` 70, `bio` 5.

Value backfill (idempotent; skips members that already have a value for that field): `users.rank` and `users.ranklevel` to `rank`; `users.affiliation`; `users.city`, `users.region`, `users.country`; `usergoals` rows and `users.goalsother` to `goals` (`otherText` goes with the `freeText` option); `rankhistoryentries` to `rankHistory` entries keeping their `lin_` ids, positions, and values (`rank`, `awardedbyname` and `awardedbyuserid` to `awardedBy`, `affiliation`, `dateawarded`). Progress is logged; the backfill runs in batches of 500 members under a job lease.

### 9.14 Compatibility mapping and deprecation

During the transition the Phase 1 to 2 profile fields keep working, mapped onto the seeded fields with the same keys:

| Legacy API field | Maps to | Read | Write |
|---|---|---|---|
| `rank`, `rankLevel` | `profile.rank.key`, `profile.rank.level` | `null` and `0` when the field is missing, disabled, not a `Rank`, or not visible to the viewer | Accepted in `PUT /api/v1/me/profile` |
| `affiliation`, `city`, `region` | `profile.<key>` (`Text`) | same rule | same |
| `country` | `profile.country` (`Country`) | same rule | same |
| `goals`, `goalsOther` | `profile.goals.keys`, `profile.goals.otherText` | Only in `Me` (as in Phase 1) | same |
| `rankHistory[]` `{ id, rank, awardedByName, awardedByUserId, affiliation, dateAwarded }` | `profile.rankHistory[]` entries (`awardedBy.name`, `awardedBy.userId`, `rank.key`) | same rule | same (whole-list replace, as in Phase 1) |
| Admin member filter `rank=` | `filters.profile.rank.in` | | |
| `GET /api/v1/property` `profile` section | Generated from the seeded fields (labels, options, enabled) | Present while the window is open | |

Rules: sending a legacy field and `profile.<same key>` in one request is `400 ValidationFailed` ("Send either rank or profile.rank, not both."). During the window, every write updates both the new tables and the legacy columns (`users.rank`, `users.ranklevel`, `users.affiliation`, `users.city`, `users.region`, `users.country`, `users.goalsother`, `usergoals`, `rankhistoryentries`), so a rollback of the backend keeps data correct. The legacy fields are marked deprecated in the OpenAPI document and `REST_API.md`, and `trelliskit.profile.legacy_fields` counts every read and write that uses them, by `X-Client` and `X-Client-Version`.

Timeline, controlled by setting `profileFields.legacyFieldsMode` (`ReadWrite`, `ReadOnly`, `Off`; Live):

| Stage | When | Mode | Effect |
|---|---|---|---|
| 1. Dual | This milestone ships. The website and the mobile app move to `profile` in the same milestone | `ReadWrite` | Legacy fields are served and accepted; dual writes |
| 2. Read only | At least 90 days after stage 1, once `trelliskit.profile.legacy_fields{direction="write"}` has been zero for 30 days and `mobile.minimumBinaryVersion*` excludes builds that write legacy fields | `ReadOnly` | Legacy writes return `400 ValidationFailed` ("This field moved to profile.<key>."); legacy reads continue; dual writes continue |
| 3. Off | At least 30 days after stage 2, when legacy reads from current client versions are zero | `Off` | Legacy fields are no longer returned; dual writes stop |
| 4. Removal | The first release after stage 3 | | A migration drops `users.rank`, `ranklevel`, `affiliation`, `city`, `region`, `country`, `goalsother`, `idx_users_rank`, and the tables `usergoals` and `rankhistoryentries`; `IUserGoalMethods` and `IRankHistoryMethods` are removed |

---

## 10. Access models

### 10.1 `access` configuration

In the property document (`property.json` and the admin portal Property section):

```json
"access": {
  "model": "PartiallyGated",
  "anonymousBrowsing": true,
  "gatedFeatures": ["library", "messaging"],
  "defaultAudience": "SignedIn"
}
```

| Field | Values | Default |
|---|---|---|
| `model` | `Open`, `PartiallyGated`, `FullyGated` | `FullyGated` (also when `access` is absent: today's behaviour) |
| `anonymousBrowsing` | Boolean. Signed-out callers may read `Public` objects through the normal content routes (not only `/api/v1/public/*`). Ignored in `FullyGated` | false |
| `gatedFeatures` | Subset of the feature keys in 10.2, no duplicates. Used only in `PartiallyGated` | `[]` |
| `defaultAudience` | `Public`, `SignedIn`, `Members`. `Members` is rejected when `model` is `Open` | `Members`, or `SignedIn` when `model` is `Open` |

Validation (publish and import): unknown feature keys, `defaultAudience: Members` with `Open`, and a missing `membership` section for a paid model are problems. Switching models is a normal publish; it takes effect immediately for every request (definitions caches reload). Existing entitlements are kept when switching to `Open`, so switching back restores members.

The sample property stays `FullyGated` until this part lands; at the end of it the sample switches to `PartiallyGated` (with `anonymousBrowsing: true`, `gatedFeatures: ["library", "messaging"]`, `defaultAudience: "SignedIn"`) so the engine's default exercises the most general case, as `TRELLISKIT.md` section 7 requires. Engine tests cover all three models with fixtures.

### 10.2 Features and routes

| Feature key | Routes today | Later |
|---|---|---|
| `feed` | `/api/v1/feed*`, comments, reactions, and media on feed posts | |
| `members` | `/api/v1/users/{id}`, `/api/v1/members/*` | Phase 4 directory of members |
| `forums`, `library`, `events`, `directory`, `messaging`, `assistants` | Reserved (accepted in config, no routes yet) | Phases 3 to 5 and AI; each new route declares its feature |

Comments, reactions, mentions, and media have no feature of their own: they inherit the feature and audience of their target (a comment on a feed post is `feed` with the post's audience; media inherits the object it is attached to; unattached media is readable only by its owner and staff). Notifications, reports, blocks, push tokens, and account routes are not features (section 10.4).

Every route registrar declares its feature and action (`Read`, `List`, `Write`, `Participate`) in one table in `AccessRoutes.cs`; a test fails if a route outside the Anonymous, SignedIn, Admin, Moderator, and Platform groups has no declaration.

### 10.3 `audience` on objects

- Every content object carries `audience`: `Public`, `SignedIn`, or `Members`. Feed posts already have the column (migration 5); later content tables add it at creation. It is always present in payloads and is an enumeration filter (`filters.audience`: one or more values) on every content collection.
- On create, `audience` defaults to `Members` when the object's feature is in `gatedFeatures` (`PartiallyGated`), otherwise `access.defaultAudience`. In `FullyGated` the default is `Members`.
- Who may set it: the author when creating or editing (`SignedIn` or `Members`; `Members` is not offered in `Open`); `Public` only by `SystemAdmin` (as Phase 2 public sample posts).
- Phase 2 `visibility` on feed posts maps onto `audience`: `visibility: Public` is `audience: Public`; `visibility: Members` is `audience` `SignedIn` or `Members`. Writes of `visibility` keep working (`Public` sets `Public`; `Members` sets the default non-public audience), reads return both, and `visibility` is deprecated on the same timeline as 9.14.
- A migration "Access models" backfills `feedposts.audience = 'Public'` where `visibility = 'Public'` and adds `idx_feedposts_audience (propertyid, audience, publishedutc DESC, id DESC) WHERE deletedutc IS NULL AND moderationstate = 'None' AND publishedutc IS NOT NULL`.
- In `Open`, a stored `Members` audience is treated as `SignedIn`. In `FullyGated`, `SignedIn` is treated as `Members`.

### 10.4 Central authorization decision

One component, `AccessDecider` (`TrellisKit.Server/Services`), decides every content access from (model, feature, action, object audience, caller tier, ownership). Route groups (`AuthorizationService.Classify`) still run first; `AccessDecider` replaces the fixed rule of the Member group.

Route group rule by model:

| Group | `FullyGated` | `PartiallyGated` and `Open` |
|---|---|---|
| Anonymous, SignedIn, Admin, Moderator, Platform | unchanged | unchanged |
| Member (content routes) | Age attested and an active entitlement (today) | Age attested session, or anonymous for routes declared `Read` or `List` when `anonymousBrowsing` is true. Then `AccessDecider` per object and action |
| Notifications, reports, blocks, link previews (in the Member group today) | Member | SignedIn |

Required tier for an action:

| Action | `FullyGated` | `PartiallyGated` | `Open` |
|---|---|---|---|
| Read an object with audience `Public` | Member on content routes; anyone through `/api/v1/public/*` | Anonymous when `anonymousBrowsing`, else SignedIn | Anonymous when `anonymousBrowsing`, else SignedIn |
| Read an object with audience `SignedIn` | Member | SignedIn | SignedIn |
| Read an object with audience `Members` | Member | Member | SignedIn |
| List a collection | Member | Anonymous when `anonymousBrowsing`, else SignedIn; results filtered (below) | same as `PartiallyGated` |
| Write in a feature (create, edit own, upload media to attach in it) | Member | Member when the feature is in `gatedFeatures`, else SignedIn | SignedIn |
| Participate on an object (comment, reply, react, mention notifications) | Member | The higher of the object's read tier and the feature's write tier | SignedIn |
| Read and use notifications, report, block, push tokens | Member (notifications, reports, blocks), SignedIn (push tokens, under `/me`) | SignedIn | SignedIn |
| View a member profile or search members (`members`) | Member | Member when `members` is gated, else SignedIn | SignedIn |

Overrides, in order: `Staff` passes every check; the author can always read their own objects; Phase 1 and 2 permission flags (`canPost`, `canPostMedia`, `canUploadVideo`, `canPostToFeed`, mute) and blocks still apply on top; moderation state (hidden, removed) and deletion still return `404 NotFound` as in Phase 2.

Outcome when the caller's tier is below the required tier:

| Caller | Required | Response |
|---|---|---|
| Anonymous | SignedIn or Member | `401 Unauthorized` with `required: "SignIn"` |
| Signed in, not age attested | anything above SignedIn group routes | `403 AgeAttestationRequired` (unchanged) |
| SignedIn | Member | `402 MembershipRequired` (never in `Open`) |
| Any, object not readable for another reason | | `404 NotFound` |

List filtering: a collection returns the objects the caller may read. In `PartiallyGated`, a `SignedIn` caller also receives objects that need `Member` as locked stubs (10.5), so free accounts can see what membership unlocks; anonymous callers never receive stubs, only `Public` objects. `Open` has no stubs. Fan-out follows the same rule: notifications, pushes, and new-post emails go only to recipients who can read the object.

### 10.5 Locked stubs and denial bodies

Every content payload carries `locked` (false normally). A locked stub keeps only discovery fields:

```json
{
  "id": "fpo_...",
  "locked": true,
  "audience": "Members",
  "kind": "Standard",
  "title": "Members-only session notes",
  "body": null,
  "excerpt": null,
  "attachments": [],
  "linkPreview": null,
  "author": { "id": "usr_...", "displayName": "Coach X", "handle": "coachx", "avatarUrl": "...", "profile": {}, "isStaff": true, "isDeleted": false },
  "publishedUtc": "2026-11-11T20:00:00Z",
  "isPinned": false,
  "commentCount": 12,
  "reactions": null,
  "labels": ["sessions"],
  "tags": {},
  "viewer": { "canOpen": false, "requires": "Membership" }
}
```

Reading a locked object directly (detail, comments, reactions, media) returns:

```json
{
  "error": "MembershipRequired",
  "message": "This is for members.",
  "fieldErrors": null,
  "required": "Membership",
  "feature": "feed",
  "targetType": "FeedPost",
  "targetId": "fpo_..."
}
```

`401 Unauthorized` from `AccessDecider` carries `required: "SignIn"` and the same `feature`, `targetType`, and `targetId`. Route-group `402` responses (in `FullyGated`) carry `required: "Membership"` and `feature` when the route declares one.

### 10.6 `Open` model specifics

In `Open` there are no payments and no sign-up codes. These routes return `404 NotAvailable`:

- `GET /api/v1/membership`, `POST /api/v1/membership/stripe/checkout`, `POST /api/v1/membership/stripe/portal`, `POST /api/v1/membership/apple/verify`, `POST /api/v1/membership/google/verify`
- `POST /api/v1/signup-codes/validate`, `POST /api/v1/signup-codes/redeem`
- Admin writes that create entitlements: `POST /api/v1/admin/members/{id}/comp`, `POST /api/v1/admin/signup-codes`, `POST /api/v1/admin/signup-codes/batches`, `PUT /api/v1/admin/signup-codes/{id}`

Admin reads of subscriptions, sign-up codes, redemptions, and membership metrics stay available (history). Store webhooks keep being processed and recorded. `GET /api/v1/auth/providers` reports `stripe`, `appleIap`, and `googlePlay` as disabled. `Me.membership` is still present with `isActive: false` and nulls; clients ignore it. The same sign-up code routes also return `404 NotAvailable` in any model when `features.signupCodes` is false.

### 10.7 What clients show and hide

`Me` gains `accessTier`: `SignedIn`, `Member`, or `Staff`.

| Element | `Open` | `PartiallyGated` | `FullyGated` |
|---|---|---|---|
| Prices, membership page, paywall screen, "Restore purchases" | Hidden | Shown (membership page in navigation; paywall when a locked item or gated action is chosen) | Shown (paywall right after sign-in for non-members, as today) |
| Sign-up codes (`/join?code=`, redeem UI) | Hidden; `/join` redirects to sign-up | Website only, when `features.signupCodes` | Website only, when `features.signupCodes` |
| Locked items | None exist | Shown to signed-in non-members with a lock badge and "Become a member"; hidden from signed-out visitors | Not shown (non-members cannot reach content) |
| Gated features in navigation | n/a | Shown with a lock badge for non-members; opening one shows its free samples and locked stubs | Hidden until membership |
| Write actions in gated features (composer, comment, react) | n/a | Replaced by an upgrade prompt for non-members | n/a |
| Signed-out browsing | When `anonymousBrowsing`: `Public` content readable with "Sign in to join in" prompts | Same | Marketing pages with public sample posts only (as today) |
| Membership section in Account settings and admin (subscriptions, codes, comps, membership metrics) | Hidden in Account; admin shows history read-only | Shown | Shown |
| Audience picker in composers | `SignedIn` default, plus `Public` for `SystemAdmin` | `SignedIn`, `Members`, plus `Public` for `SystemAdmin`; default per 10.3 | Hidden (always `Members`), plus `Public` for `SystemAdmin` |

Store rules always apply: the mobile apps never show sign-up codes or web pricing, and the upgrade prompt on iOS and Android opens the native in-app purchase only. Clients decide from `GET /api/v1/property` `access` and `Me.accessTier`, and treat server `401` and `402` bodies (10.5) as the source of truth.

### 10.8 `GET /api/v1/property` additions

The response gains (and keeps everything it returns today, except where noted):

```json
{
  "version": 7,
  "access": {
    "model": "PartiallyGated",
    "anonymousBrowsing": true,
    "gatedFeatures": ["library", "messaging"],
    "defaultAudience": "SignedIn",
    "purchasesEnabled": true,
    "signupCodesEnabled": true
  },
  "membership": { "productName": "Example Membership", "priceUsdCents": 999, "interval": "Year", "storeProductId": "example.membership.annual", "minimumAge": 16, "trialDays": 0 },
  "profileFields": [
    { "key": "displayName", "builtIn": true, "label": "Name", "type": "Text", "collectAt": "SignUp", "visibility": "Public", "required": true, "helpText": "", "placeholder": "" },
    { "key": "rank", "builtIn": false, "label": "Level", "type": "Rank", "options": [ { "key": "Beginner", "label": "Beginner", "color": "#9CA3AF", "maxLevel": null, "freeText": false, "retired": false } ], "rank": { "levelLabel": "Stars", "maxLevel": 3 }, "subFields": [], "validation": { "required": true }, "collectAt": "SignUp", "editableBy": "Member", "visibility": "Public", "showOnProfile": true, "profileOrder": 10, "showOnCard": true, "searchable": false, "filterable": true }
  ],
  "features": { "signupCodes": true, "directory": true, "events": true, "library": true, "messaging": true, "members": true, "assistants": false, "publicSamples": true }
}
```

| Field | Rule |
|---|---|
| `version` | The current published property version |
| `access` | As configured, plus derived `purchasesEnabled` (`model` is not `Open`) and `signupCodesEnabled` (`purchasesEnabled` and `features.signupCodes`) |
| `membership` | `null` in `Open` |
| `profileFields` | Enabled fields with `visibility` other than `Staff`, in profile order, built-ins included; definitions only (no counts, ids optional for clients) |
| `profile` | The legacy section, generated from the fields, while `profileFields.legacyFieldsMode` is not `Off` |
| Headers | `Cache-Control: public, max-age=300`, `ETag: "v<version>-f<profile field revision>"` (304 for a matching `If-None-Match`), `Vary: Host, X-TK-Property` (required: a shared CDN in front of several properties must not mix them) |

---

## 11. Telemetry

Every engine metric gains the label `trelliskit.property` (Prometheus `trelliskit_property`): the resolved property id, `platform`, or `unresolved`. It is low-cardinality by design (at most `deployment.maxProperties` values). The resource attribute `trelliskit.property` stays the property id in `SingleProperty`; in `MultiProperty` it is `multi` and the new resource attribute `trelliskit.deployment` carries `deployment.name`.

New instruments on the `TrellisKit` meter:

| Instrument | Type | Labels |
|---|---|---|
| `trelliskit.property.resolutions` | Counter | `source` (header, host, platform, single, webhook, special), `outcome` (resolved, not_found, disabled, mismatch, app_not_registered, webhook_unmatched) |
| `trelliskit.isolation.violations` | Counter | `kind` (missing_scope, reserved_id, cross_property_token, cross_property_reference). Must stay 0; alerting rule in Grafana |
| `trelliskit.properties` | Gauge | `status` |
| `trelliskit.platform.actions` | Counter | `action` (the platform audit actions without the prefix) |
| `trelliskit.property.bundles` | Counter | `operation` (import, export), `outcome` (ok, invalid, too_large, dry_run) |
| `trelliskit.property.publishes` | Counter | `source` |
| `trelliskit.property.settings.changes` | Counter | `scope` (property, overridable, platform) |
| `trelliskit.secrets.decrypt_failures` | Counter | `key` (setting key; bounded by the registry) |
| `trelliskit.media.key_migration` | Counter and Gauge (`remaining`) | `outcome` |
| `trelliskit.profile.updates` | Counter | `source` (member, staff), `outcome` |
| `trelliskit.profile.validation_failures` | Counter | `type`, `rule` (required, length, range, pattern, option, date, entries, filtered) |
| `trelliskit.profile.legacy_fields` | Counter | `direction` (read, write), `field`, `client` (web, mobile, other) |
| `trelliskit.profile.filters` | Counter | `type`, `operator` |
| `trelliskit.profile.definitions.changes` | Counter | `action` (create, update, delete, reorder) |
| `trelliskit.access.decisions` | Counter | `model`, `feature`, `action`, `outcome` (allow, unauthorized, membership_required, not_available, locked_stub) |

Spans: `property.resolve` (attributes `trelliskit.property`, `property.resolution_source`), `property.import`, `property.export`, `property.publish`, `property.purge`, `profile.validate`, `access.decide` (only on denials, to keep volume low), `job <name>` per worker pass with `trelliskit.property` per item batch. The `trelliskit.property` attribute is on every request span.

Logs: every line carries `[p:<propertyId>]`. Request history stores `propertyid` and the admin request history view filters by it.

Grafana (`assets/grafana/`, queries `trelliskit_*`): a Platform folder with "Properties" (request rate, error rate, p95 latency, and active members by property; resolution failures by outcome; isolation violations with an alert at any non-zero value; bundle and publish activity) and a property variable (`$property`) added to every existing dashboard.

---

## 12. Settings

All are added to `SettingsRegistry.cs` with their scope. "Live" applies on save; "Restart" applies after a restart.

| Key | Type | Default | Scope | Apply | Environment variable |
|---|---|---|---|---|---|
| `deployment.mode` | Enum `SingleProperty`, `MultiProperty` | `SingleProperty` | Platform | Restart | `TK_DEPLOYMENT_MODE` |
| `deployment.name` | String (1 to 64, telemetry resource attribute) | `trelliskit` | Platform | Restart | `TK_DEPLOYMENT_NAME` |
| `deployment.trustForwardedHost` | Boolean | true | Platform | Restart | `TK_TRUST_FORWARDED_HOST` |
| `deployment.requireAppHeader` | Boolean | true | Platform | Live | |
| `deployment.propertyCacheSeconds` | Integer 1 to 600 | 30 | Platform | Live | |
| `deployment.maxProperties` | Integer 1 to 500 | 50 | Platform | Live | |
| `platform.hosts` | String (comma-separated hostnames) | empty | Platform | Live | `TK_PLATFORM_HOSTS` |
| `platform.bootstrapAdminEmail` | String | empty | Platform | Live | `TK_PLATFORM_BOOTSTRAP_ADMIN_EMAIL` |
| `platform.secretsKey` | Secret (base64, 32 bytes) | generated on first boot | Platform | Restart | `TK_SECRETS_KEY` |
| `platform.secretsKeyPrevious` | Secret | empty | Platform | Restart | `TK_SECRETS_KEY_PREVIOUS` |
| `platform.delegatedSessionMinutes` | Integer 5 to 480 | 60 | Platform | Live | |
| `platform.bundleMaxMegabytes` | Integer 1 to 500 | 50 | Platform | Live | |
| `platform.bundleMaxFiles` | Integer 10 to 5000 | 500 | Platform | Live | |
| `platform.propertyPurgeDelayHours` | Integer 1 to 720 | 72 | Platform | Live | |
| `property.directory` | (existing) | | Platform | Restart | `TK_PROPERTY_DIR` |
| `property.syncFromDirectory` | Enum `OnChange`, `Never` | `OnChange` | Platform | Restart | `TK_PROPERTY_SYNC` |
| `property.maxVersions` | Integer 5 to 1000 | 50 | Platform | Live | |
| `media.propertyAssetsBucket` | String | empty (images bucket) | Platform | Restart | `TK_MEDIA_PROPERTY_ASSETS_BUCKET` |
| `media.keyMigrationBatchSize` | Integer 1 to 1000 | 100 | Platform | Live | |
| `profileFields.maxFields` | Integer 1 to 100 | 50 | Platform | Live | |
| `profileFields.maxCardFields` | Integer 0 to 5 | 3 | Overridable | Live | |
| `profileFields.maxFiltersPerQuery` | Integer 1 to 20 | 10 | Platform | Live | |
| `profileFields.legacyFieldsMode` | Enum `ReadWrite`, `ReadOnly`, `Off` | `ReadWrite` | Platform | Live | |

Existing keys change scope as listed in 5.2 (`Property`) and 5.3 (`Overridable`); their types, defaults, bounds, and apply modes are unchanged. Access configuration is property document data, not settings.

---

## 13. Data model

Migrations, in order, in both `SqliteSchema.cs` and `PostgresqlSchema.cs` (version numbers are the next free ones when they land):

1. **"Property registry and scoping"**: the registry tables below, then section 4.1 for every property-owned table, then `authsessions.principalkind` and `mediaattachments.keyprefix TEXT NOT NULL DEFAULT ''`.
2. **"Configurable profile fields"**: the profile tables below.
3. **"Access models"**: section 10.3 backfill and index.
4. **"Remove legacy profile columns"**: stage 4 of 9.14 (a later release).

Type mapping follows Phase 1 and 2: ids and text `TEXT`; booleans `BOOLEAN` (PostgreSQL) and `INTEGER` 0 or 1 (SQLite); timestamps `TIMESTAMPTZ` and ISO 8601 `TEXT`; integers `INTEGER`; numbers `DOUBLE PRECISION` and `REAL`; byte sizes `BIGINT` and `INTEGER`; dates `TEXT` `YYYY-MM-DD` in both. Case-insensitive uniqueness uses `lower(...)`. Every new table carries `propertyid` as the first column of every index and unique constraint.

### Tables

| Table | Columns |
|---|---|
| `properties` | `id` PK (slug), `kind` (`Property`, `Platform`), `name`, `status` (`Disabled`, `Enabled`, `Deleting`, `Deleted`), `currentversion` INTEGER NULL, `settingsmigratedutc` NULL, `lastdirectoryhash` NULL, `enabledutc` NULL, `disabledutc` NULL, `disabledreason` NULL, `deletescheduledutc` NULL, `purgestartedutc` NULL, `createdutc`, `updatedutc`. (Labels and tags on properties use `propertyid = 'platform'`, `targettype = 'Property'`) |
| `propertydomains` | `id` PK (`pdm_`), `propertyid`, `host` (lowercase), `kind` (`Web`, `Api`, `Media`, `Redirect`, `Alias`), `createdutc` |
| `propertyapps` | `id` PK (`ppa_`), `propertyid`, `platform` (`Ios`, `Android`), `appid`, `storeappid` NULL, `enabled`, `createdutc`, `updatedutc` |
| `propertyversions` | `id` PK (`prv_`), `propertyid`, `version`, `status` (`Draft`, `Published`, `Superseded`), `source`, `note` NULL, `documentjson`, `documenthash`, `schemaversion`, `createdbyuserid` NULL, `createdutc`, `updatedutc`, `publishedutc` NULL |
| `propertyassets` | `id` PK (`pas_`), `propertyid`, `sha256`, `contenttype`, `sizebytes`, `storagekey`, `createdutc` |
| `propertyversionfiles` | PK (`versionid`, `path`), `propertyid`, `assetid` |
| `propertysettings` | PK (`propertyid`, `settingkey`), `valuetext` NULL (non-secret values as their string form), `ciphertext` NULL, `nonce` NULL, `keyid` NULL, `updatedbyuserid` NULL, `updatedutc` |
| `profilefields` | `id` PK (`pfd_`), `propertyid`, `key`, `builtin`, `label`, `helptext`, `placeholder`, `type`, `collectat`, `editableby`, `visibility`, `showonprofile`, `profileorder`, `showoncard`, `searchable`, `filterable`, `enabled`, `required`, `minlength` NULL, `maxlength` NULL, `minvalue` NULL, `maxvalue` NULL, `integeronly`, `pattern` NULL, `patternmessage` NULL, `minentries` NULL, `maxentries` NULL, `mindate` NULL, `maxdate` NULL, `notinfuture`, `levellabel` NULL, `maxlevel` NULL, `valuecount` INTEGER DEFAULT 0, `revision` INTEGER (bumped on every change; feeds the property `ETag`), `createdbyuserid` NULL, `updatedbyuserid` NULL, `createdutc`, `updatedutc` |
| `profilefieldoptions` | `id` PK (`pfo_`), `propertyid`, `fieldid`, `subfieldid` NULL (options of a `SingleSelect` sub-field), `key`, `label`, `color` NULL, `maxlevel` NULL, `freetext`, `retired`, `position`, `createdutc`, `updatedutc` |
| `profilefieldsubfields` | `id` PK (`pfs_`), `propertyid`, `fieldid`, `key`, `label`, `type`, `required`, `maxlength` NULL, `minvalue` NULL, `maxvalue` NULL, `notinfuture`, `rankfieldkey` NULL, `withlevel`, `position`, `createdutc`, `updatedutc` |
| `profilevalues` | PK (`propertyid`, `userid`, `fieldid`), `valuetext` NULL, `valuenormalized` NULL, `valuenumber` NULL, `valuedate` NULL, `valuebool` NULL, `valuelevel` NULL, `othertext` NULL, `updatedbyuserid` NULL, `updatedutc` |
| `profilevalueoptions` | PK (`propertyid`, `userid`, `fieldid`, `optionkey`), `position` |
| `profilehistoryentries` | `id` PK (`phe_`, or migrated `lin_`), `propertyid`, `userid`, `fieldid`, `position`, `createdutc`, `updatedutc` |
| `profilehistoryvalues` | PK (`propertyid`, `entryid`, `subfieldkey`), `valuetext` NULL, `valuenormalized` NULL, `valuenumber` NULL, `valuedate` NULL, `valuebool` NULL, `valuelevel` NULL, `valueuserid` NULL |
| `profilesearchwords` | PK (`propertyid`, `userid`, `fieldid`, `word`) |

### Indexes and unique constraints

| Table | Index | Purpose |
|---|---|---|
| `propertydomains` | `ux_propertydomains_host (lower(host))` UNIQUE (deployment-wide: it is the resolution key), `idx_propertydomains_property (propertyid)` | Host resolution |
| `propertyapps` | `ux_propertyapps_app (platform, lower(appid))` UNIQUE (deployment-wide), `idx_propertyapps_property (propertyid)` | App and webhook resolution |
| `propertyversions` | `ux_propertyversions_version (propertyid, version)` UNIQUE, `ux_propertyversions_draft (propertyid) WHERE status = 'Draft'` UNIQUE, `ux_propertyversions_published (propertyid) WHERE status = 'Published'` UNIQUE, `idx_propertyversions_created (propertyid, createdutc)` | One draft, one published |
| `propertyassets` | `ux_propertyassets_sha (propertyid, sha256)` UNIQUE | Content addressing |
| `propertyversionfiles` | `idx_propertyversionfiles_asset (propertyid, assetid)` | Cleanup |
| `profilefields` | `ux_profilefields_key (propertyid, lower(key))` UNIQUE, `idx_profilefields_order (propertyid, enabled, profileorder)` | Definitions |
| `profilefieldoptions` | `ux_profilefieldoptions_key (propertyid, fieldid, subfieldid, lower(key))` UNIQUE, `idx_profilefieldoptions_order (propertyid, fieldid, position)` | |
| `profilefieldsubfields` | `ux_profilefieldsubfields_key (propertyid, fieldid, lower(key))` UNIQUE | |
| `profilevalues` | `idx_profilevalues_text (propertyid, fieldid, valuenormalized, userid)`, `idx_profilevalues_number (propertyid, fieldid, valuenumber, userid)`, `idx_profilevalues_date (propertyid, fieldid, valuedate, userid)`, `idx_profilevalues_key (propertyid, fieldid, valuetext, valuelevel, userid)`, `idx_profilevalues_user (propertyid, userid)` | Filters, rank filters, member reads |
| `profilevalueoptions` | `idx_profilevalueoptions_option (propertyid, fieldid, optionkey, userid)` | `any` and `all` filters |
| `profilehistoryentries` | `idx_profilehistory_user (propertyid, userid, fieldid, position)` | Member reads |
| `profilehistoryvalues` | `idx_profilehistoryvalues_user (propertyid, valueuserid) WHERE valueuserid IS NOT NULL` | Account deletion (clear references) |
| `profilesearchwords` | `idx_profilesearchwords_word (propertyid, word, userid)` | Search prefixes |

SQLite note: the `WHERE` partial unique indexes are supported (as in Phase 2). PostgreSQL note: `lower(key)` expression indexes as in Phase 1.

### Write rules

Phase 2 write rules apply unchanged (statement batches as transactions, counters recomputed from source rows inside the batch, conditional `UPDATE` claims). Publishing a version, replacing a member's profile values, reordering fields, and purging a property batch are each one batch.

---

## 14. Backend structure

| Area | New or changed |
|---|---|
| Resolution | `PropertyResolver` (PreRouting), `PropertyRegistry` (cache, section 2.7), `PropertyScope` (replaces `PropertyContext`), enums `DeploymentModeEnum`, `PropertyStatusEnum`, `PropertyResolutionSourceEnum`, `PrincipalKindEnum`, `DomainKindEnum`, `AppPlatformEnum` |
| Data | `IPropertyMethods`, `IPropertyDomainMethods`, `IPropertyAppMethods`, `IPropertyVersionMethods`, `IPropertyAssetMethods`, `IPropertySettingMethods`, `IProfileFieldMethods`, `IProfileValueMethods`; every existing `I*Methods` gains the `propertyId` first parameter; `MethodsBase.RequireProperty`; `SqlDialect.Scope` |
| Settings and secrets | `SettingScopeEnum`, scope on `SettingFieldDefinition`, `PropertySettingsService`, `PropertySettingsMigrator`, `SecretBox` (AES-256-GCM with associated data), `IIntegrationResolver` |
| Platform | `PlatformRoutes`, `PlatformService`, `PropertyBundleService` (zip read and write, validation), `PropertyVersionService`, `PropertyAdminRoutes` |
| Profile fields | `ProfileFieldRoutes`, `ProfileFieldService` (definitions, rules in 9.4), `ProfileValueService` (validation per type, writes, visibility filtering, compatibility mapping), `ProfileFieldSeeder`, `ProfileFilterCompiler`, `Countries` |
| Access | `AccessDecider`, `AccessRoutes` (feature and action declarations), `AccessTierEnum`, `FeatureKeyEnum` |
| Workers | `MediaKeyMigrationWorker`, `PropertyPurgeWorker`, `PropertyAssetCleanupWorker`, `ProfileValuePurgeWorker`, `ProfileBackfillWorker`, `SecretRotationWorker`; existing queue workers iterate properties (4.4) |
| Tests (`Test.Shared`) | `PropertyIsolationSuite` (4.5), `PropertyResolutionSuite` (every row of 2.2 and 2.3, both modes), `PlatformSuite`, `PropertyBundleSuite` (limits, zip bombs, round trip), `PropertySettingsSuite` (scopes, encryption, rotation, env patterns, migration from the settings file), `SingleModeSuite` (startup checks, folder sync), `ProfileFieldSuite` (definition rules), `ProfileValueSuite` (every type and rule in 9.6, visibility matrix, payload rules), `ProfileFilterSuite`, `ProfileCompatibilitySuite` (legacy mapping, dual writes, each legacy mode), `AccessModelSuite` (every cell of 10.4 for each model, stubs, `404 NotAvailable` routes, fan-out filtering). All run on both providers. The test-local `second` property folder lives in `Test.Shared/Fixtures/second/` |

---

## 15. Client notes

**Shared (`shared/src`):** `ApiClient` sends `X-TK-Property` (and, on mobile, `X-TK-App`) on every request from build configuration. New methods: platform (`listProperties`, `createProperty`, `getProperty`, `updateProperty`, `enableProperty`, `disableProperty`, `deleteProperty`, `restoreProperty`, `importPropertyBundle`, `exportPropertyBundle`, `listPropertyVersions`, `publishPropertyVersion`, `getPlatformSettings`, `updatePlatformSettings`, `listPlatformAdmins`, `invitePlatformAdmin`, `removePlatformAdmin`, `createDelegatedSession`, `listPlatformAudit`), property admin (`getPropertyAdmin`, `updatePropertyDraft`, `discardPropertyDraft`, `publishPropertyDraft`, `listPropertyFiles`, `putPropertyFile`, `deletePropertyFile`, `restorePropertyVersion`, `getPropertySettings`, `updatePropertySettings`, `testIntegration`), profile fields (`listProfileFields`, `createProfileField`, `updateProfileField`, `deleteProfileField`, `reorderProfileFields`, `previewProfileFields`, `getProfileFieldStats`, `updateMemberProfile`), members (`enumerateMembers`), pages (`getPropertyPage`, `listPropertyPages`). `profileFields.js` (9.11) and an `access.js` helper (`canRead`, `canWrite`, `isLocked`, `showsPurchases`) that mirrors 10.4 for display only.

**Website:** sends `X-TK-Property` from `TK_PROPERTY_DIR` at build time; reads marketing and legal pages from `GET /api/v1/property/pages/*` at runtime with the build-time copy as the fallback; profile editor, onboarding, profile page, member cards, and member search filters from `profileFields`; access behaviour per 10.7 (locked stubs, upgrade prompts, signed-out browsing routes when `anonymousBrowsing`); admin sections of 6.5; the platform console as a separate route tree (`/platform/*`) shown when the API reports the platform realm (`GET /api/v1/auth/me` returns `principalKind: "PlatformAdmin"`). In `MultiProperty` the website build for a property obtains its folder by exporting the property bundle and unzipping it into `TK_PROPERTY_DIR`.

**Mobile and tablet:** `app.config.js` sets `X-TK-Property` from the property id and `X-TK-App` from the bundle id or package for the platform, in `ApiClient` and in `updates.requestHeaders`; onboarding, profile editor, profile screen, member cards, and member search filters render from `profileFields` (bundled copy for first launch offline); access behaviour per 10.7 with native in-app purchase only; no sign-up codes and no web pricing ever.

---

## 16. Delivery order

Each step ends with every test green on both providers and `REST_API.md` plus the Postman collection updated for its routes.

| Step | Backend | Website | Mobile |
|---|---|---|---|
| 1. Scoping in single mode (no behaviour change) | Registry tables and migration 1 (4.1, 4.2), `PropertyResolver` in `SingleProperty`, `propertyId` on every data method with the guard, `p/<id>/` keys for new media and `MediaKeyMigrationWorker`, token payload v2, request history and telemetry property label, `PropertyIsolationSuite` skeleton (static checks, case 12) | Send `X-TK-Property` | Send `X-TK-Property` and `X-TK-App` (including update checks) |
| 2. Property storage and per-property settings | Versions, files, bundles, `/api/v1/admin/property*`, `/api/v1/property/pages/*`, setting scopes, `propertysettings`, secrets, `PropertySettingsMigrator`, `IIntegrationResolver`, folder sync | Property admin sections (Property, Integrations), runtime pages | none |
| 3. Profile fields (parallel with step 2 once step 1 is merged) | Migration 2, definitions and routes, values, payloads, filters, seeding and backfill, compatibility layer in `ReadWrite` | Onboarding, editor, profile page, cards, member search filters from definitions; admin Profile fields with preview | Same member surfaces from definitions |
| 4. Multi-property | `MultiProperty` mode, resolution order, platform realm, platform routes, delegated sessions, webhook resolution (Stripe per-property URL), fair worker claims, CORS, startup checks, full `PropertyIsolationSuite` | Platform console | none |
| 5. Access models | `AccessDecider`, route declarations, migration 3, stubs, `404 NotAvailable` routes, fan-out filtering, `GET /api/v1/property` additions, `Me.accessTier`; switch the sample to `PartiallyGated` last | Per-model UI (10.7), audience picker, signed-out browsing | Per-model UI (10.7) with native purchase only |
| 6. Deprecation | Stages 2 to 4 of 9.14 on their timeline, and the `visibility` field | Nothing left to change (moved in step 3) | Raise `mobile.minimumBinaryVersion*` when stage 2 starts |

Docker and infrastructure teams: the local stack keeps running `SingleProperty` by default; a `MultiProperty` compose profile adds `TK_DEPLOYMENT_MODE=MultiProperty`, `TK_PLATFORM_HOSTS`, and the `second`-style fixture only for engine tests. Infrastructure adds the `property` storage location (`media.propertyAssetsBucket`), `TK_SECRETS_KEY` in Secrets Manager, and, for a shared stack, one CloudFront alternate domain set and `TK_PROPERTY_<ID>_*` secrets per property.

---

## 17. Open questions (with recommended answers)

| # | Question | Recommended answer |
|---|---|---|
| 1 | Where do platform admin accounts live: a separate `admins` table (as AUTHENTICATION.md sketches) or users in a reserved platform realm? | **The platform realm** (section 6.1): it reuses users, credentials, sessions, refresh, password reset, and audit code unchanged, and the isolation rules treat it as one more property |
| 2 | How does a platform admin act inside a property: a cross-property token, or a delegated, property-bound session? | **Delegated sessions** (6.2): short-lived, property-bound, audited on both sides, so tokens never span properties |
| 3 | How are per-property secrets provided in production for a shared stack? | **Both** the admin API (encrypted in the database) and `TK_PROPERTY_<ID>_*` environment variables injected from Secrets Manager (5.4); environment wins. Revisit a direct Secrets Manager reference type only if the number of properties makes environment variables unwieldy |
| 4 | Stripe webhook resolution: per-property URL or payload metadata? | **Per-property URL** `/api/v1/webhooks/stripe/{propertyId}` (the signature needs the property's secret first), with `tk_property` metadata as a cross-check; the legacy URL only in `SingleProperty` |
| 5 | Should community behaviour settings (feed, comments, moderation thresholds, email cadence) be per property now? | **Yes, as `Overridable`** (5.3); infrastructure limits (rate limits, storage, workers) stay platform-wide |
| 6 | Who edits domains and app ids in `MultiProperty`? | **Platform admins only**: they route traffic and decide webhook ownership, so a property admin must not be able to claim another property's host or bundle id |
| 7 | Property deletion: purge or archive forever? | **Purge after a 72 hour delay** (`platform.propertyPurgeDelayHours`), restorable until then, with the id kept as a tombstone forever; export the bundle first (the console offers it) |
| 8 | Do `city`, `region`, and `country` become profile fields or stay built-in account fields? | **Seeded profile fields** for every property (9.13), so a property can disable or relabel them; they are on the same compatibility timeline |
| 9 | Legacy media keys: migrate objects, or allow unprefixed keys forever? | **Migrate** with `MediaKeyMigrationWorker` before `MultiProperty` is allowed (4.4): signed cookie policies can then be scoped to `p/<id>/*` with no exceptions |
| 10 | Link preview cache: shared across properties or per property? | **Per property**: a shared cache would reveal through timing that another property linked a URL, and the saving is small |
| 11 | Should signed-out visitors see locked stubs in `PartiallyGated`? | **No**: they see only `Public` objects (10.4). Titles of members-only content stay inside the community |
| 12 | Should member edits of their own profile be audited? | **No**, only staff edits (`member.profile`, key names only); member edits update `updatedutc` and `updatedbyuserid` on the value rows |
| 13 | Should the account export include `Staff` visibility fields? | **No**: they are staff notes about the member, not data the member provided. Revisit if legal review of a property's privacy policy says otherwise |
| 14 | Platform-shared integrations (one SMTP account or one AI endpoint used by several properties)? | **SMTP via the `Overridable` platform default** (already in 5.3); **AI endpoints stay per property** in this milestone, with shared endpoints a later platform feature |
| 15 | Can a property id ever be renamed? | **Never**: it is in every row, every storage key, every token, and every mobile build. A rename is a new property plus a data migration tool, out of scope |
| 16 | In `SingleProperty`, should admin portal edits or the folder win? | **The folder, by default** (`property.syncFromDirectory = OnChange`), to keep the current git-reviewed workflow; a property that wants portal-first editing sets `Never` |
| 17 | Is `deployment.maxProperties` 50 the right ceiling? | **Yes for now**: it keeps the `trelliskit.property` metric label low-cardinality and worker passes short. Raising it later only needs a cardinality review of dashboards |


---

## Resolved decisions (property owner, 2026-11-11)

These answers override the recommendations in the open questions section above wherever they differ.

| # | Question | Decision |
|---|---|---|
| 1 | Where platform admins live | A **separate platform admins table** (not a reserved pseudo-property). Member accounts are separately annotatable as admins within their own property (the per-property `SystemAdmin` role) |
| 2 | How a platform admin acts inside a property | Short-lived, audited session bound to that one property |
| 3 | How per-property secrets are supplied | **Admin portal only** (stored encrypted). No `TK_PROPERTY_<ID>_*` environment overrides. Deployment-wide secrets (database, token and signing keys) still come from the environment and Secrets Manager |
| 4 | Stripe webhooks | One URL per property (`/api/v1/webhooks/stripe/{propertyId}`), cross-checked against the payload; the legacy shared URL works only in single-property mode |
| 5 | Community settings | Per-property overrides of platform defaults; infrastructure limits stay platform-wide |
| 6 | Who changes domains and app ids | **Property admins too** (not only platform admins). Values must be unique across the deployment, and a new domain is activated only after ownership verification (DNS TXT record) |
| 7 | Deleting a property | Disabled immediately, restorable for **30 days**, then all data purged; the id is never reused |
| 8 | City, region, country | Seeded as standard profile fields for every property (admins may disable or hide them) |
| 9 | Storage key prefixes | **Every storage key is prefixed with the property id from the beginning, in single-property mode too.** No later migration |
| 10 | Link preview cache | Per property |
| 11 | Locked placeholders | Hidden from signed-out visitors; shown to signed-in non-members with an upgrade prompt |
| 12 | Audit of profile edits | **All profile edits are audited** (members' own edits and staff edits) |
| 13 | Account export | Excludes staff-only data |
| 14 | Shared integrations | **Fully separate:** every property configures every integration itself (no platform default email account either) |
| 15 | Configuration source in single-property mode | The property folder wins when `property.json` changes on disk; otherwise admin portal edits stand |
| 16 | Maximum properties per shared deployment | 25 (the load balancer certificate default) |
| 17 | Renaming a property id | **Never.** The property id is an opaque generated identifier, not a human-readable name: `app_{k-sortable}_{random}` from PrettyId (for example `app_mwcnf2ia_Yk3hhcUgbET5kqGV`), assigned when a property is created and written into its `property.json` `id`. A separate human-readable `slug` (lowercase letters, digits, hyphens) is used only for cosmetic purposes such as infrastructure resource names, image tags, and folder names, and does not identify the property in data, storage keys, headers, or URLs. The existing human-readable ids (for example `example`) are replaced with generated ids during the multi-property milestone (before any production data exists) |

---

## Implementation notes (part 2, backend)

These record how part 2 implements sections 5, 6, 9, and 11 and where it differs from the text above. REST_API.md is the reference for every shape.

| Topic | As built |
|---|---|
| Member directory route | `GET /api/v1/members` and `POST /api/v1/members/enumerate` were already the Phase 2 mention search, so the member enumeration of section 9.12 is `GET /api/v1/members/directory` and `POST /api/v1/members/directory/enumerate`. `POST /api/v1/admin/members/enumerate` accepts `filters.profile` as specified |
| Seeding source | `property.json` (schema version 1) gains an optional `profileFields` list in the create-body shape; when present it wins over the version 1 `profile` section, which stays for build-time clients. Fields are seeded once per property (`properties.profilefieldsseededutc`); in `SingleProperty` a change of the profile field section on disk (hash in `properties.profilefieldshash`) is merged on the next start: new keys are created, existing keys take the allowed attributes, options missing from the folder are retired, nothing is deleted (resolved decision 15: the folder wins on change) |
| Backfill | Set-based, scoped `INSERT ... SELECT` statements in one transaction at seeding (idempotent per member and field), instead of a batched worker |
| Deleting values | `DELETE /api/v1/admin/profile-fields/{id}?deleteValues=true` deletes the values in the same request (still `202`); there is no `ProfileValuePurgeWorker` |
| Normalized text | `valuenormalized` is trimmed and lowercased (invariant); accents are not folded |
| Bio visibility | The built-in bio's `visibility`, `showOnProfile`, and `enabled` apply to `bio` on public profiles |
| Card viewer | `UserSummary.profile` uses the Member viewer on member surfaces and the Anonymous viewer on the public feed; staff lists use the Staff viewer |
| Audits | Resolved decision 12: members' own edits are audited (`profile.update`) as well as staff edits (`member.profile`) |
| Secrets key | One engine key, setting `secrets.encryptionKey` (`TK_SECRETS_ENCRYPTION_KEY`, rotation through `secrets.previousEncryptionKeys`), as in the AI contract, instead of `platform.secretsKey`; generated on first start when empty. Per-property settings exist for the email integration only in this part; other integrations keep their deployment settings until they move |
| Platform request history | One property per call (`filters.propertyId`, default `platform`) |
| Domains | Property administrators edit domains and app ids in both modes (resolved decision 6) through `PUT /api/v1/admin/property/domains`; collection reads use `GET /api/v1/admin/property/domains` and `GET /api/v1/admin/property/apps`, each with a paired `POST .../enumerate`, standard `objects` pages and scalar `propertyId`. Domain and app declarations expose empty metadata and Unix epoch timestamps; new hosts start unverified with a DNS TXT challenge (`_trelliskit-verify.<host>` = `trelliskit-verify=<token>`, read through DNS-over-HTTPS) and are routed only after `POST .../verify` succeeds |
| Telemetry property label | Set when a request's property services are resolved and for each property's workers; requests that never resolve a property (and platform-host requests) report `unresolved` |
| Access models (section 10) | Built as `docs/ACCESS_MODELS_CONTRACT.md` (binding where it differs): stored in `propertyaccess` (migration 10) rather than in a published property version, edited at `/api/v1/admin/property/access`; `FullyGated` keeps the per-object `SignedIn` rule (the route gate already requires a membership); locked stub `body` and `excerpt` are empty strings; webhooks keep processing in `Open`; `Me` also carries `membershipRequired`. The platform `accessModel` filter on property enumeration is not built yet |

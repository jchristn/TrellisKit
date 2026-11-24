# Access Models Contract (as built)

This is the binding, as-built contract for the engine's access models: `Open`, `PartiallyGated`, and `FullyGated` (`docs/TRELLISKIT.md` section 7). It refines `docs/MULTIPROPERTY_CONTRACT.md` section 10, which stays as the design record; where they differ, this document wins. It is an engine contract: examples use the neutral sample property. `REST_API.md` has every shape; the Postman collection has every route.

## 1. Configuration (per property)

The access configuration is per property, stored in the database (tables `propertyaccess` and `propertyaccessfeatures`, migration 10 "Access models"), seeded from `property.json` `access`, and edited by property administrators.

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
| `model` | `Open`, `PartiallyGated`, `FullyGated` | `FullyGated` (also when `access` is absent) |
| `anonymousBrowsing` | Boolean. Signed-out callers may read `Public` objects on the content routes. Ignored in `FullyGated` | false |
| `gatedFeatures` | Feature keys: `feed`, `members`, `forums`, `library`, `events`, `directory`, `messaging`, `assistants`, `search`; no duplicates. Used only in `PartiallyGated`; kept when the model changes so switching back restores them | `[]` |
| `defaultAudience` | `Public`, `SignedIn`, `Members`, or null (model default). `Members` is rejected in `Open` when sent; a stored `Members` is dropped (back to null) when the model switches to `Open` without one. In `FullyGated` any value is coerced to null (the default there is always `Members`), and the admin view reports `configuredDefaultAudience: null` | null: `Members`, or `SignedIn` in `Open` |

- **Seeding:** on a property's first load the row is created from `property.json` `access`. In `SingleProperty`, when the folder's access section changes on disk (hash in `propertyaccess.sourcehash`), the folder wins on the next load and the change is audited `property.access.sync` (the same rule as profile fields, resolved decision 15). Otherwise admin edits stand.
- **Editing:** `GET` and `PUT /api/v1/admin/property/access` (section 6), SystemAdmin, the admin token, or a delegated platform session, both deployment modes. Audited `property.access.update` with a before and after summary. A change applies immediately on the instance that took it and on other instances within `deployment.propertyCacheSeconds` (the edit touches the property registry).
- **Validation:** unknown or duplicate feature keys, `defaultAudience: Members` with `Open`, and a paid model (`PartiallyGated`, `FullyGated`) when the property document has no sellable `membership` (productName, positive price, storeProductId) are `400 ValidationFailed` with `fieldErrors`. In `property.json`, the same problems stop startup, and `membership` is optional only in `Open` (its `minimumAge` still applies, default 18).
- **Minimum age:** always `membership.minimumAge` from the property (18 when the section is absent). Age attestation applies in every model.
- **Sample property:** `samples/example` is `PartiallyGated` (anonymous browsing, `library` and `messaging` gated, `SignedIn` default). The engine test fixture pins `FullyGated` for the Phase 1 and 2 suites; `AccessModelSuite` covers every model.

## 2. Features and route declarations

Every route of the Member route group has a declaration in `TrellisKit.Server/Services/AccessRoutes.cs`: a feature (or none), an action (`Read`, `List`, `Write`, `Participate`), whether it is an account route, and whether signed-out callers may reach it under anonymous browsing. `AccessModelSuite` fails when the OpenAPI document lists a Member group route without a declaration.

| Routes | Feature | Action | Signed-out eligible |
|---|---|---|---|
| `GET /feed`, `POST /feed/enumerate` | feed | List | yes |
| `POST /feed`, `PUT`/`DELETE /feed/{id}`, `POST /feed/{id}/pin`, `/unpin` | feed | Write | no |
| `GET /feed/{id}` | feed | Read | yes |
| `GET /feed/{id}/comments`, `POST .../comments/enumerate` | feed | List | yes |
| `POST /feed/{id}/comments`, `PUT`/`DELETE /feed/{id}/reactions/{key}` | feed | Participate | no |
| `GET /feed/{id}/reactions`, `/revisions` (and their enumerate forms) | feed | Read | no |
| `GET /comments/{id}`, `/comments/{id}/children` (and enumerate) | feed | Read | yes |
| other `/comments/*` reads (reactions, revisions) | feed | Read | no |
| `PUT`/`DELETE /comments/{id}`, comment reactions | feed | Participate | no |
| `GET /reactions/set` | none | Read | yes |
| `GET /media`, `GET /media/{id}`, `POST /media/grant` | none | Read | no |
| `POST /media/uploads`, `/media/{id}/upload-urls`, `/media/{id}/complete`, `PUT`/`DELETE /media/{id}` | none | Write | no |
| `GET /users/{id}`, `GET /members`, `/members/search`, `POST /members/enumerate` (mention search) | members | Read, List | no |
| `GET /members/directory`, `POST /members/directory/enumerate` (member directory) | directory | List | no |
| `/notifications*`, `POST /reports`, `/users/{id}/block`, `POST /link-previews`, `/labels/*`, `/tags/*`, `/metadata/*` | account | Read, Write | no |
| `GET /forums`, `POST /forums/enumerate`, `GET /forums/{idOrSlug}`, `GET /forums/{id}/threads` (and enumerate), `GET /threads` (and enumerate), `GET /threads/{id}`, `GET /threads/{id}/comments` (and enumerate) | forums | List, Read | yes |
| `POST /forums/{id}/read`, `/threads/{id}/revisions`, `GET /threads/{id}/reactions`, `/polls/{id}/voters` | forums | Read, List | no |
| `GET /search`, `POST /search/enumerate` (global search) | search | List | no |
| `POST /forums/{id}/threads`, `PUT`/`DELETE /threads/{id}`, thread moderation and type actions (pin, lock, move, accept-answer, feedback, promote), `POST /polls/{id}/close` | forums | Write | no |
| `POST /threads/{id}/comments`, thread reactions, `PUT`/`DELETE /forums/{id}/follow`, `/threads/{id}/follow`, `PUT`/`DELETE /polls/{id}/votes` | forums | Participate | no |

Comments, reactions, and media inherit the feature and audience of their target. **Forums** (docs/PHASE3_CONTRACT.md section 0.4): when `forums` is gated in `PartiallyGated`, reads need a membership as well as writes (the route gate answers `402` with `feature: "forums"`, signed-out callers get `401` even with anonymous browsing, and thread comments, reactions, media, and promoted feed cards are hidden from non-members on every other route); a forum's audience applies like a post's, and a thread's effective audience is the narrower of its own and its forum's. New forums default to `Members` when forums are gated, else to the property default audience. **Search** (global search across feed posts, threads, comments, and members; docs/PHASE3_CONTRACT.md resolved decision 11): its own feature, independent of `forums`. `property.json` `features.search` (default true) false makes the search routes `404 NotAvailable` in every model; in `PartiallyGated` with `search` gated, searching needs a membership (`402 MembershipRequired`, `feature: "search"`; signed-out callers `401`), since search has no writes to gate; otherwise searching needs a signed-in account. Results always follow each object's own rules, so gating `forums` keeps forum threads and thread comments out of a non-member's results without gating search, and gating `search` does not gate the forum thread enumeration (`POST /threads/enumerate` with `search` is a forums route). `GET /api/v1/property` reports the effective `features.search` (the property switch and the deployment setting `search.enabled`). **Assistants** (docs/AI_CONTRACT.md sections 0.2 and 13; the assistant routes resolve their caller themselves, so they are not Member group routes): a signed-in caller needs the `assistants` participation tier for the assistant's audience (a membership in `FullyGated`, in `PartiallyGated` when `assistants` is gated or the audience is `Members`; `Open` treats `Members` as `SignedIn`), denials carry `required` and `feature: "assistants"`; visitors reach only `Public` assistants, through anonymous browsing (outside `FullyGated`, `assistants` not gated) or the embeddable widget (every model); new assistants default to the property audience (`Members` when gated). A Member group route without a declaration needs a membership in `FullyGated` and `PartiallyGated` and a signed-in account in `Open`.

## 3. The decision

### 3.1 Caller tiers

`Anonymous` (signed out), `SignedIn` (signed in, no active membership), `Member` (active membership), `Staff` (SystemAdmin, the break-glass token, a delegated platform session: passes every check). A signed-in account without the age attestation gets `403 AgeAttestationRequired` on every Member group route before anything else.

### 3.2 Route-by-model behaviour

| Route group | `FullyGated` | `PartiallyGated` | `Open` |
|---|---|---|---|
| Anonymous (`/health`, `/auth/*` except `/auth/me`, `/public/*`, `/property*`, webhooks, media files) | unchanged | unchanged | unchanged |
| SignedIn (`/auth/me`, `/me/*`, `/handles/*`) | session | session | session |
| Membership and purchases (`/membership`, `/membership/stripe/*`, `/membership/apple/verify`, `/membership/google/verify`) | session (+ attestation for purchases) | same | `404 NotAvailable` |
| `/membership/history` | session | session | session (history stays readable) |
| Sign-up codes (`/signup-codes/*`) | session + attestation | same | `404 NotAvailable` (also any model when `features.signupCodes` is false) |
| Content reads and lists (declared Read/List) | Member | SignedIn; Anonymous when signed-out eligible and `anonymousBrowsing`; then per object (3.3) | same as PartiallyGated |
| Content writes and participation | Member | Member when the feature is gated, else SignedIn; then per object | SignedIn; then per object |
| Members feature (`/users/{id}`, mention search) | Member | Member when `members` is gated, else SignedIn | SignedIn |
| Member directory (`/members/directory*`) | Member | Member when `directory` or `members` is gated, else SignedIn | SignedIn; `404 NotAvailable` in every model when `features.directory` is false |
| Global search (`/search*`) | Member | Member when `search` is gated, else SignedIn | SignedIn; `404 NotAvailable` in every model when `features.search` is false |
| Account routes (notifications, reports, blocks, link previews, labels, tags, metadata) | Member | SignedIn | SignedIn |
| Media uploads and grants | Member | SignedIn | SignedIn |
| Moderator (`/admin/moderation/*`) | unchanged | unchanged | unchanged |
| Admin (`/admin/*`, `/settings*`, `/request-history*`) | unchanged | unchanged | unchanged, except `404 NotAvailable` for `POST /admin/members/{id}/comp`, `POST /admin/signup-codes`, `POST /admin/signup-codes/batches`, `PUT /admin/signup-codes/{id}` |
| Webhooks (Stripe, Apple, Google) | processed | processed | **still processed and recorded** (see 4) |

**Who the member directory lists.** In every model the directory lists only accounts that are not banned or deleted and have finished their profile (a handle is set), so unfinished sign-ups never appear. When the model requires a membership to use the directory (`FullyGated`, or `PartiallyGated` with `directory` or `members` gated), it also lists only accounts with an active membership or a staff role (any role other than `User`), so lapsed and never-paid accounts are not shown to members. In `Open`, and in `PartiallyGated` with neither gated, every finished account is listed.

### 3.3 Per-object audience (`ContentAudienceRule`, `AccessDecider`)

| Object audience | `FullyGated` | `PartiallyGated` | `Open` |
|---|---|---|---|
| `Public` | Member (route gate); anyone on `/public/*` | anyone who reached the route | anyone who reached the route |
| `SignedIn` | Member (route gate) | SignedIn | SignedIn |
| `Members` | Member | Member | SignedIn (treated as `SignedIn`) |

Overrides: staff see everything; authors always see their own objects; feed moderators see what they moderate; blocks, permission flags, mutes, moderation states, and deletion apply on top. Participation (comment, reply, react) needs the higher of the object's read tier (at least SignedIn) and the feature's write tier, and is never allowed signed out.

New objects default to `Members` in a gated feature (`PartiallyGated`), otherwise to the property's default audience (`Members` in `FullyGated`, never `Members` in `Open`); a `Public` default applies only to SystemAdmin authors. Authors may set `SignedIn` or `Members` (`Members` is `400 ValidationFailed` in `Open`); `Public` needs SystemAdmin. Phase 2 `visibility: Members` keeps a non-public audience or takes the non-public default.

### 3.4 Denials

| Caller | Situation | Response |
|---|---|---|
| Signed out | Member group route not opened by anonymous browsing | `401 Unauthorized`; outside `FullyGated` the body adds `required: "SignIn"` and `feature` |
| Signed out | `GET` of a Published object whose audience is not `Public` (anonymous browsing on) | `401 Unauthorized` with `required: "SignIn"`, `feature`, `targetType`, `targetId` |
| Signed in, no membership | Route gate (gated feature write, gated members) in `PartiallyGated` | `402 MembershipRequired` with `required: "Membership"` and `feature` |
| Signed in, no membership | Direct read of a `Members` object (detail, comments, reactions) in `PartiallyGated` | `402 MembershipRequired` with `required`, `feature`, `targetType`, `targetId` |
| Signed in, no membership | Any content route in `FullyGated` | `402 MembershipRequired` (unchanged body) |
| Anyone | Route not available in the model | `404 NotAvailable` |
| Anyone | Object not readable for another reason | `404 NotFound` |

```json
{ "error": "MembershipRequired", "message": "This is for members.", "fieldErrors": null, "required": "Membership", "feature": "feed", "targetType": "FeedPost", "targetId": "fpo_..." }
```

### 3.5 Lists and locked stubs

Lists return the objects the caller may read. In `PartiallyGated` a signed-in caller without a membership also receives `Members` objects as locked stubs. Signed-out callers never receive stubs (only `Public` objects); `Open` and `FullyGated` have none. Every `FeedPost` carries `locked` (false normally). A stub keeps `id`, `kind`, `author`, `title`, `audience`, `visibility`, `state`, dates, `isPinned`, `commentCount`, `labels`, and `tags`; `body` and `excerpt` are empty strings, `attachments`, `mentions`, `myReactions` are empty, `reactionCounts` is `{}`, `reactionTotal` 0, `linkPreview` and `moderation` null, and `viewer` is `{ canOpen: false, requires: "Membership" }` with every other flag false. `viewer.canOpen` is true and `viewer.requires` null on every other post. The `audience` filter narrows lists to audiences the caller may receive.

Signed-out callers (anonymous browsing) get the anonymous profile cards, the public media copies of `Public` posts (and of the other content signed-out visitors may see: event covers, series covers, venue and instructor photos, and public sample threads; see `docs/PHASE4_CONTRACT.md`, as-built public listing images), comments without attachments (comment media has no public copy), and `viewer` flags that allow nothing.

## 4. Payments in `Open`

- Purchase, membership, and sign-up code routes and the entitlement-granting admin writes return `404 NotAvailable` (3.2). `GET /api/v1/auth/providers` reports `stripe`, `appleIap`, and `googlePlay` disabled. `GET /api/v1/property` returns `membership: null` and `access.purchasesEnabled: false`.
- **Existing subscriptions are unaffected.** Nothing is cancelled, refunded, or revoked by a switch. Store webhooks keep being processed and recorded and the reconciliation worker keeps running, so billing state stays correct; switching back to a paid model restores every member whose entitlement is still active. Members keep paying the stores until they cancel there: an operator switching a paid property to `Open` should tell members how to cancel (Stripe portal links stop working through the API, so use the Stripe dashboard).
- Admin reads of subscriptions, codes, redemptions, comps, and membership metrics stay available (history). Sign-up code reminder emails pause in `Open` and resume after switching back.
- Member surfaces show `Me.membership` inactive (`isActive: false`, nulls) and `Me.accessTier` `SignedIn` or `Staff`; staff views (`/admin/members/{id}`) keep the real membership.

## 5. Client-visible shapes

`GET /api/v1/property` adds:

```json
{
  "access": { "model": "PartiallyGated", "anonymousBrowsing": true, "gatedFeatures": ["library", "messaging"], "defaultAudience": "SignedIn", "purchasesEnabled": true, "signupCodesEnabled": true, "revision": 3, "configuredDefaultAudience": null, "updatedByUserId": null, "updatedUtc": null },
  "membership": { "productName": "Example Membership", "priceUsdCents": 999, "interval": "Year", "storeProductId": "example.membership.annual", "minimumAge": 16, "trialDays": 0 },
  "minimumAge": 16
}
```

- `access.anonymousBrowsing` is the effective value (false in `FullyGated`); `gatedFeatures` is the effective list (empty outside `PartiallyGated`; gating `members` also lists `directory`); `defaultAudience` is the effective default; `signupCodesEnabled` is `purchasesEnabled` and `features.signupCodes`. `membership` is null in `Open`; `minimumAge` is always present. The `ETag` includes the access model and revision.
- `Me` (`GET /api/v1/auth/me`, and the `user` of every auth response) adds `accessTier` (`SignedIn`, `Member`, `Staff`) and `membershipRequired` (true only in `FullyGated` for a non-member): **show the paywall step after sign-up and onboarding only when `membershipRequired` is true.**
- `FeedPost` adds `locked`; `FeedPost.viewer` adds `canOpen` and `requires`. `canComment` and `canReact` already reflect the model.
- 401 and 402 bodies may add `required`, `feature`, `targetType`, `targetId` (3.4).

## 6. Admin routes

| Method and path | Body | Success | Errors |
|---|---|---|---|
| `GET /api/v1/admin/property/access` | | `200 AccessPolicy`: `{ model, anonymousBrowsing (as stored), gatedFeatures (as stored), defaultAudience (effective), configuredDefaultAudience, purchasesEnabled, signupCodesEnabled, revision, updatedByUserId, updatedUtc }` | |
| `PUT /api/v1/admin/property/access` | `{ model?, anonymousBrowsing?, gatedFeatures?, defaultAudience? }` (omitted fields stay; explicit null `defaultAudience` returns to the model default) | `200 AccessPolicy` | `400 ValidationFailed` |

## 7. Notifications, email, push, public pages

- New post fan-out, in-app notifications, pushes, and new-post emails go only to recipients who may open the post: in `FullyGated` members (and SystemAdmin); in `PartiallyGated` every age-attested account for `Public` and `SignedIn` posts and members for `Members` posts; in `Open` every age-attested account. The fan-out query pre-filters by membership only where the model needs it, and each recipient is checked again when notifications are created.
- The public feed (`/api/v1/public/feed*`, the marketing site and SEO feed) lists only `Public`, Published, unmoderated posts in every model, and only while `features.publicSamples` is on.

## 8. Client changes needed

1. Read `access` from `GET /api/v1/property` (refresh on `ETag` change) and drive navigation, paywalls, and composers from it; never assume `FullyGated`. The website requests it on every load and again when a tab regains focus or becomes visible (at most once a minute per tab), so an access-model or palette change reaches open tabs without a manual reload; mobile clients refresh it on return to the foreground.
2. After sign-up, social sign-in, and onboarding, show the paywall step only when `Me.membershipRequired` is true (never in `Open` or `PartiallyGated`).
3. When `access.purchasesEnabled` is false: hide prices, the membership page, paywall screens, "Restore purchases", store purchase flows, and the Account membership section; do not call `/membership*` or `/signup-codes/*` (they are `404 NotAvailable`); `/join` redirects to sign-up; admin shows subscriptions, codes, and comps read-only and hides Comp, New code, New batch, and Edit code.
4. Handle `membership: null` in `GET /api/v1/property`; read the age gate from `minimumAge`.
5. Render locked stubs (`locked: true`) with a lock badge and an upgrade prompt for signed-in non-members; opening one shows the paywall; never request detail, comments, or reactions of a locked post.
6. Treat `402` bodies with `required: "Membership"` as "show the upgrade prompt" and `401` with `required: "SignIn"` as "show sign in", using `feature` and `targetId` for context.
7. In `PartiallyGated`, show gated features (from `access.gatedFeatures`, including `directory` for the member directory entry and `search` for the global search box, which no longer follows `forums`) with a lock badge for non-members; hide the global search box when `features.search` is false; hide the directory entry when `features.directory` is false; replace composers and write actions there with the upgrade prompt; use `viewer.canComment` and `viewer.canReact`.
8. When `access.anonymousBrowsing` is true: offer signed-out browsing of the feed (`GET /api/v1/feed`, `/feed/{id}`, `/feed/{id}/comments`, `/comments/{id}`, `/reactions/set` without a token) with "Sign in to join in" prompts; signed-out callers only see `Public` posts.
9. Composer audience picker: `Open`: `SignedIn` (default) plus `Public` for SystemAdmin; `PartiallyGated`: `SignedIn` and `Members` (default `access.defaultAudience`, `Members` in gated features) plus `Public` for SystemAdmin; `FullyGated`: hidden (always `Members`) plus `Public` for SystemAdmin.
10. Admin portal: an Access model section (Property area) over `GET`/`PUT /api/v1/admin/property/access` with the model, anonymous browsing, gated feature checkboxes (all 9 keys, including `search`), and default audience; warn before switching a paid property to `Open` that subscriptions keep billing in the stores.
11. Mobile apps: the upgrade prompt opens native in-app purchase only; never show sign-up codes or web pricing in any model.

## 9. Infrastructure and deployment notes

- No new infrastructure. Migration 10 adds two small tables and an index.
- The Docker stack and infrastructure need nothing for access models. (Unrelated to access models but shipped alongside: `TK_SECRETS_ALLOW_GENERATED_KEY=true` is required in the local Docker stack unless it supplies `TK_SECRETS_ENCRYPTION_KEY`; see `REST_API.md`, "Secrets key".)

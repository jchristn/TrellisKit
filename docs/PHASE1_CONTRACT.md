# Phase 1 Contract: Identity and Membership

> **Superseded in part (2026-11-11):** paging, sorting, and every list shape in this contract are replaced by `docs/ENUMERATION_AND_METADATA.md` (EnumerationQuery and EnumerationResult, labels and tags on every object, no unbounded lists). Where this file disagrees, that specification wins.


> **Updated for TrellisKit (2026-11-11):** field, cookie, filter, and environment variable names in this record were updated to the engine-neutral names of `docs/TRELLISKIT.md` section 3 (`rank`, `rankLevel`, `affiliation`, `rankHistory` with `awardedByName` and `awardedByUserId`, the `rank=` admin filter, the `tk_refresh` cookie, and `TK_*` variables). Rank, level, and goal values, the membership product and price, and the minimum age come from the active property's `property.json`. `REST_API.md` is the current reference.

This is the binding contract between the backend, the website, and the mobile app for Phase 1. Every team codes against it exactly. When the implementation is complete, `REST_API.md` and the Postman collection are updated to match, and this file stays as the design record. Plan references: the property's plan of record, sections 4.2, 4.3, 4.4, 4.4.1, 6.2, 6.3, 7.

Conventions are unchanged from Phase 0 (`REST_API.md`): routes under `/api/v1`, camelCase JSON, string enums, ISO 8601 UTC timestamps, errors as `{ "error", "message", "fieldErrors" }`.

---

## 1. Clients and tokens

Every request from an app sends `X-Client: web` or `X-Client: mobile` (and `X-Client-Version: <app version>`).

| | Web (`X-Client: web`) | Mobile (`X-Client: mobile`) |
|---|---|---|
| Access token | Returned in the body; kept in memory only; sent as `Authorization: Bearer <accessToken>` | Same |
| Refresh token | Set by the server as an HttpOnly, Secure (except on http://127.0.0.1), SameSite=Lax cookie named `tk_refresh`, path `/api/v1/auth`; **`refreshToken` is null in the body** | Returned in the body; stored in `expo-secure-store` |
| Refresh call | `POST /api/v1/auth/refresh` with no body (cookie sent automatically, `credentials: 'include'`) | `POST /api/v1/auth/refresh` with `{ "refreshToken": "..." }` |

- Access tokens are opaque (AES-256-GCM encrypted), valid 60 minutes by default (`accessTokenExpiresUtc` tells the client when).
- Refresh tokens rotate on every refresh. Reusing an old refresh token revokes the whole session (the client must sign in again).
- On any `401` from a non-auth route, the client calls refresh once and retries; if refresh fails, it signs out.
- The break-glass admin token from Phase 0 keeps working for admin routes.

### AuthResponse

Returned by every sign-in, registration, and refresh call.

```json
{
  "accessToken": "opaque",
  "accessTokenExpiresUtc": "2026-11-11T21:00:00Z",
  "refreshToken": "opaque or null (web)",
  "isNewAccount": false,
  "user": { "...": "Me (section 3)" }
}
```

---

## 2. Authentication routes (anonymous unless noted)

| Method and path | Body | Success | Errors |
|---|---|---|---|
| `POST /api/v1/auth/register` | `{ email, password, displayName, dateOfBirth: "YYYY-MM-DD", acceptTerms: true }` | `201 AuthResponse` (`isNewAccount: true`); verification email sent | `400 ValidationFailed` (fieldErrors), `403 AgeRequirementNotMet` (under the property minimum age, nothing stored), `409 EmailInUse` |
| `POST /api/v1/auth/login` | `{ email, password }` | `200 AuthResponse` | `401 InvalidCredentials` (same message for unknown email and wrong password), `423 AccountLocked` (after 10 failures in 15 minutes; `message` says when to retry), `403 AccountBanned` |
| `POST /api/v1/auth/apple` | `{ identityToken, nonce, givenName?, familyName? }` | `200 AuthResponse` | `401 InvalidIdentityToken`, `409 AccountLinkRequired`, `403 AccountBanned` |
| `POST /api/v1/auth/google` | `{ idToken }` | `200 AuthResponse` | same as Apple |
| `POST /api/v1/auth/facebook` | `{ token, tokenType: "Jwt" \| "AccessToken" }` (iOS Limited Login gives a JWT; Android and web give an access token) | `200 AuthResponse` | same as Apple |
| `POST /api/v1/auth/refresh` | mobile: `{ refreshToken }`; web: none (cookie) | `200 AuthResponse` | `401 InvalidRefreshToken` |
| `POST /api/v1/auth/logout` | mobile: `{ refreshToken }`; web: none | `204` (web cookie cleared) | none (always 204) |
| `POST /api/v1/auth/password/forgot` | `{ email }` | `202` always (no account enumeration) | `429 RateLimited` |
| `POST /api/v1/auth/password/reset` | `{ token, newPassword }` | `204` (all sessions revoked) | `400 InvalidToken`, `400 ValidationFailed` |
| `POST /api/v1/auth/email/verify` | `{ token }` | `204` | `400 InvalidToken` |
| `POST /api/v1/auth/email/resend` (signed in) | none | `202` | `429 RateLimited` |

Rules:
- **Password policy:** 10 to 128 characters, not equal to the email. `fieldErrors.password` explains failures.
- **Social sign-in, new account:** the account is created with `ageAttested: false`. The client must then call `POST /api/v1/me/age-attestation` before anything else; every other member route returns `403 AgeAttestationRequired` until it does.
- **`409 AccountLinkRequired`:** the provider's verified email already belongs to an account that does not use this provider. The body adds `"existingMethods": ["Email", "Google"]`. The client tells the user to sign in with an existing method and link the new one from Account settings.
- **Bootstrap admin:** the email in setting `auth.bootstrapAdminEmail` receives the `SystemAdmin` role automatically once that email is verified (by password verification or a provider that verifies email).
- Emails (verification, reset) contain links to `{web.publicBaseUrl}/verify-email?token=...` and `{web.publicBaseUrl}/reset-password?token=...`. Those web pages call the routes above. Tokens are single-use: verification 48 hours, reset 1 hour.

---

## 3. The signed-in member ("Me")

`GET /api/v1/auth/me` returns `Me`. Profile and account routes all return `Me` so clients can replace their cached copy.

```json
{
  "id": "usr_...",
  "email": "pat@example.com",
  "emailVerified": true,
  "displayName": "Pat Example",
  "handle": "pat",
  "avatarUrl": "/api/v1/media/avatars/usr_..._1731350000.jpg",
  "bio": "Advanced member...",
  "rank": "Advanced",
  "rankLevel": 2,
  "affiliation": "Example Club",
  "city": "San Jose", "region": "CA", "country": "US",
  "goals": ["Learning", "Teaching"],
  "goalsOther": null,
  "rankHistory": [
    { "id": "lin_...", "rank": "Advanced", "awardedByName": "Coach X", "awardedByUserId": null, "affiliation": "Example Club", "dateAwarded": "2015-06-01" }
  ],
  "ageAttested": true,
  "profileComplete": true,
  "signInMethods": ["Email", "Google"],
  "roles": [ { "role": "Moderator", "resourceType": "Forum", "resourceId": "*" } ],
  "flags": { "canPostToFeed": false, "canPromoteToFeed": false, "canUploadVideo": true, "canSendDirectMessages": true, "isMuted": false, "mutedUntilUtc": null },
  "membership": {
    "isActive": true,
    "source": "Subscription",
    "store": "Stripe",
    "expiresUtc": "2027-11-11T00:00:00Z",
    "autoRenew": true,
    "willConvertToPaid": false
  },
  "createdUtc": "2026-11-11T20:00:00Z"
}
```

| Field | Values |
|---|---|
| `rank` | a key from the property `profile.rank.values`, or null |
| `rankLevel` | 0 to the property `profile.rank.maxLevel`, default 0 |
| `goals` | any keys from the property `profile.goals.values` (a goal with `freeText` enables `goalsOther` text) |
| `profileComplete` | true when `displayName` and `handle` are set, `ageAttested` is true, and `rank` is set when the property enables `profile.rank` |
| `signInMethods` | any of `Email`, `Apple`, `Google`, `Facebook` |
| `roles[].role` | `User` (implicit, not listed), `SystemAdmin`, `Moderator`, `FeedModerator`, `DirectoryModerator`, `EventManager` |
| `roles[].resourceType` | `Global`, `Forum`, `Feed`, `Directory`, `Events`; `resourceId` is an id or `*` |
| `membership.source` | `Subscription`, `SignupCode`, `AdminComp`, or null |
| `membership.store` | `Apple`, `Google`, `Stripe`, or null (only for `Subscription`) |
| `membership.expiresUtc` | null means lifetime (code or comp) or no membership |

### Account routes (signed in; no membership required)

| Method and path | Body | Success | Errors |
|---|---|---|---|
| `POST /api/v1/me/age-attestation` | `{ dateOfBirth: "YYYY-MM-DD" }` | `200 Me` | `403 AgeRequirementNotMet` (the account is closed and signed out) |
| `PUT /api/v1/me/profile` | any of `displayName, handle, bio, rank, rankLevel, affiliation, city, region, country, goals, goalsOther, rankHistory` (`rankHistory` replaces the whole list; entries without `id` are new) | `200 Me` | `400 ValidationFailed` (`fieldErrors.handle` = "That handle is taken." when not unique) |
| `GET /api/v1/handles/{handle}/availability` | | `200 { "available": true }` | `400` for an invalid handle |
| `PUT /api/v1/me/avatar` | raw image body, `Content-Type: image/jpeg \| image/png \| image/webp`, at most 5 MB | `200 Me` | `400 InvalidImage`, `413 TooLarge` |
| `DELETE /api/v1/me/avatar` | | `200 Me` | |
| `PUT /api/v1/me/password` | `{ currentPassword?, newPassword }` (`currentPassword` required when one is already set) | `204` (other sessions revoked) | `400 ValidationFailed`, `401 InvalidCredentials` |
| `POST /api/v1/me/identities/{provider}` (`apple`, `google`, `facebook`) | same body as the matching `/auth/{provider}` route | `200 Me` | `401 InvalidIdentityToken`, `409 IdentityInUse` |
| `DELETE /api/v1/me/identities/{provider}` | | `200 Me` | `409 LastSignInMethod` |
| `GET /api/v1/me/export` | | `200` JSON of everything stored about the member | |
| `DELETE /api/v1/me` | `{ "confirm": "DELETE" }` | `202 { "message": "...", "storeSubscriptionNote": "Cancel your App Store subscription in iPhone Settings." \| null }` | `400` without the confirmation |

Validation:
- `displayName` 2 to 60 characters. `handle` 3 to 30 of `A-Z a-z 0-9 _ .`, unique case-insensitively. `bio` up to 1000. `affiliation`, `city`, `region` up to 100. `country` is an ISO 3166-1 alpha-2 code. `rankHistory` up to the property `profile.rankHistory.maxEntries` entries, each `rank` required, `awardedByName` up to 100, `dateAwarded` optional `YYYY-MM-DD`, not in the future.
- Account deletion: the account is anonymized immediately, sessions are revoked, an active Stripe subscription is cancelled at period end, and the response tells the member to cancel an Apple or Google subscription themselves.

### Public member profiles (members only)

`GET /api/v1/users/{id}` returns `{ id, displayName, handle, avatarUrl, bio, rank, rankLevel, affiliation, city, region, country, rankHistory, createdUtc }` for an active, non-deleted account. Requires a membership (section 4 paywall).

Avatar images are served at `GET /api/v1/media/avatars/{file}` (members only in production; anonymous is acceptable in Phase 1).

---

## 4. Paywall

Route groups:

| Group | Requirement |
|---|---|
| Anonymous | `/api/v1/health*`, `/openapi.json`, `/api/v1/auth/*` (except `/auth/me` and `/auth/email/resend`), webhooks |
| Signed in | `/api/v1/auth/me`, `/api/v1/me/*`, `/api/v1/handles/*`, `/api/v1/membership*`, `/api/v1/signup-codes/*` |
| Member | everything else for members (Phase 1: `/api/v1/users/{id}`, avatars); requires `ageAttested` and an active membership |
| Admin | `/api/v1/admin/*`, `/api/v1/settings*`, `/api/v1/request-history*`; requires `SystemAdmin` (or the break-glass token) |

Errors: `401 Unauthorized` (no or bad token), `403 AgeAttestationRequired`, `402 MembershipRequired`, `403 Forbidden` (signed in but lacks the role). `SystemAdmin` users pass the membership check.

---

## 5. Membership and purchases (signed in)

| Method and path | Body | Success | Errors |
|---|---|---|---|
| `GET /api/v1/membership` | | `200 { "membership": {...as in Me}, "webOffer": { "priceUsd": <property membership.priceUsdCents / 100>, "interval": <property membership.interval>, "productName": <property membership.productName> }, "storeProductId": <property membership.storeProductId unless appStore.productId is set>, "history": { "objects": [ { "source", "store", "startsUtc", "endsUtc", "status" } ], "totalRecords", "enumerate": "/api/v1/membership/history/enumerate" } }` | |
| `POST /api/v1/membership/stripe/checkout` | none, or `{ "code": "SAVE40" }` (a discount sign-up code, section 6) | `200 { "url": "https://checkout.stripe.com/..." }`; the website redirects there. Success returns to `{web.publicBaseUrl}/app/membership?checkout=success`, cancel to `?checkout=cancelled`. With a code the session carries the code's Stripe coupon | `400 InvalidCode` (same message for every reason), `409 AlreadyMember`, `429 RateLimited` |
| `POST /api/v1/membership/stripe/portal` | none | `200 { "url": "https://billing.stripe.com/..." }` | `404 NoStripeCustomer` |
| `POST /api/v1/membership/apple/verify` | `{ "signedTransaction": "<JWS from StoreKit 2>" }` | `200 { membership }` | `400 InvalidTransaction`, `409 TransactionBelongsToAnotherAccount` |
| `POST /api/v1/membership/google/verify` | `{ "purchaseToken": "...", "productId": "example.membership.annual" }` | `200 { membership }` | `400 InvalidPurchase`, `409 TransactionBelongsToAnotherAccount` |

Mobile purchase flow: purchase with `expo-iap` setting `appAccountToken` (iOS) or `obfuscatedAccountId` (Android) to the member's `id` without the `usr_` prefix, as a UUID-shaped value the server returns in `GET /api/v1/membership` as `"storeAccountToken": "<uuid>"`. Then post the transaction to the verify route and call `finishTransaction` only after a `200`. "Restore purchases" re-posts the latest transaction.

After a Stripe checkout the website polls `GET /api/v1/membership` (every 2 seconds, up to 30 seconds) until `isActive` is true; the webhook may land a moment after the redirect.

Webhooks (anonymous, signature-verified, idempotent): `POST /api/v1/webhooks/stripe`, `POST /api/v1/webhooks/apple`, `POST /api/v1/webhooks/google`. Not called by clients.

---

## 6. Sign-up codes, member side (website only)

| Method and path | Body | Success | Errors |
|---|---|---|---|
| `POST /api/v1/signup-codes/validate` | `{ "code": "SEMINAR2026" }` | `200 { "valid": true, "kind": "FreeMembership", "name": "Spring seminar 2026", "description": "...", "grantSummary": "Free membership for 12 months", "membershipEndsUtc": "2027-11-11T00:00:00Z" \| null }`, for a discount code `200 { "valid": true, "kind": "Discount", "name", "description", "grantSummary": "40% off the first year", "discountSummary": "40% off the first year" }`, or `200 { "valid": false, "message": "This code isn't valid." }` | `429 RateLimited` |
| `POST /api/v1/signup-codes/redeem` | `{ "code": "SEMINAR2026" }` | `200 { "membership": {...}, "grantSummary": "...", "membershipEndsUtc": ... }` | `400 InvalidCode` ("This code isn't valid." for every failure reason), `400 DiscountCodeAtCheckout` (a discount code: send it with checkout instead), `409 AlreadyRedeemed`, `429 RateLimited` |

- Codes are case-insensitive; clients trim and upper-case before sending.
- `/join?code=X` on the website: if signed out, sign-up with the code prefilled, then redeem right after registration (or social sign-in plus age attestation) and skip Stripe. If signed in, go straight to the redeem confirmation.
- When the member already has an active paid subscription, the validate response adds `"note": "Your free membership starts when your current membership ends on <date>."`.
- **Discount codes** (`grantType: "Discount"`) give a discount on the web (Stripe) membership instead of a free membership: percent off (1 to 100) or an amount off in USD cents, for the first billing period (`discountDuration: "Once"`) or every renewal (`"Forever"`). The member validates the code, then sends it with `POST /membership/stripe/checkout`. The server creates one Stripe coupon per code on its first checkout (stored as `stripeCouponId`; a changed discount gets a new coupon) and puts `signupCodeId` in the session and subscription metadata. The redemption is recorded (and counts toward `maxRedemptions`) when the `checkout.session.completed` webhook arrives, once per member; it has no entitlement, no end date, and no reminder emails, and revoking it only marks the record (the Stripe discount stays). A new account whose pending code is a discount goes to the membership page, which offers the code at checkout. Discount codes never apply to App Store or Google Play purchases.
- The mobile apps never call these routes and never mention codes.

---

## 7. Admin routes (SystemAdmin or break-glass token)

Paging follows `docs/ENUMERATION_AND_METADATA.md`: `EnumerationQuery` (`maxResults`, `skip` or `continuationToken`, `ordering`, standard filters) and `EnumerationResult` (`objects`, `totalRecords`, `recordsRemaining`, `endOfResults`, `continuationToken`). GET collection routes and canonical POST `/enumerate` routes share the contract. Use declared ordering names, such as `CreatedDescending`; older sort expressions in the design tables below describe the intended field direction, not the current wire format.

The membership detail keeps at most five history rows in its bounded preview; `/api/v1/membership/history` and POST `/api/v1/membership/history/enumerate` provide the complete paged history.

### Members

| Method and path | Notes |
|---|---|
| `GET /api/v1/admin/members?search=&membership=Active\|Inactive\|Paid\|Free&rank=&role=&banned=true&sort=-createdUtc` | Items: `{ id, displayName, handle, email, avatarUrl, rank, rankLevel, membership: {isActive, source, store, expiresUtc}, roles, isBanned, isMuted, createdUtc, lastSeenUtc }`. `search` matches name, handle, and email. Sort fields: `createdUtc`, `displayName`, `lastSeenUtc` |
| `GET /api/v1/admin/members/{id}` | `Me` fields plus `isBanned`, `bannedReason`, `lastSeenUtc`, `subscriptions` (section below), `redemptions` (code redemptions), `comps`, `sessions: [{ id, client, createdUtc, lastUsedUtc, sourceIp }]` |
| `PUT /api/v1/admin/members/{id}/flags` | `{ canPostToFeed?, canPromoteToFeed?, canUploadVideo?, canSendDirectMessages? }` returns the detail |
| `PUT /api/v1/admin/members/{id}/roles` | `{ "roles": [ { role, resourceType, resourceId } ] }` replaces non-implicit roles; returns the detail. An admin cannot remove their own `SystemAdmin` role (`409 CannotRemoveOwnAdmin`) |
| `POST /api/v1/admin/members/{id}/comp` | `{ "grantType": "DurationMonths" \| "FixedEndDate" \| "Lifetime", "months"?, "endUtc"?, "reason" }` grants a free membership (`Discount` is for sign-up codes only) |
| `DELETE /api/v1/admin/members/{id}/comp/{compId}` | `{ "reason" }` ends a comp |
| `POST /api/v1/admin/members/{id}/ban` | `{ "reason" }` bans and revokes sessions; `POST .../unban` |
| `POST /api/v1/admin/members/{id}/mute` | `{ "untilUtc", "reason" }`; `POST .../unmute` |
| `DELETE /api/v1/admin/members/{id}/sessions` | revokes every session |

### Subscriptions

| Method and path | Notes |
|---|---|
| `GET /api/v1/admin/subscriptions?store=&status=&search=&sort=-currentPeriodEndUtc` | Items: `{ id, userId, memberDisplayName, memberEmail, store, productId, status, currentPeriodStartUtc, currentPeriodEndUtc, autoRenew, environment, createdUtc, lastEventUtc }`. `status`: `Active`, `InGrace`, `InBillingRetry`, `Expired`, `Revoked`, `Refunded`, `Paused` |
| `GET /api/v1/admin/subscriptions/{id}` | Item fields plus `storeSubscriptionId` and `events: [{ id, source: "Webhook" \| "Verify" \| "Reconcile", type, receivedUtc, processedUtc, outcome, rawPayload }]` |
| `GET /api/v1/admin/metrics/membership` | `{ totalMembers, activeMembers, paidActive, freeActive, byStore: { Apple, Google, Stripe }, bySource: { Subscription, SignupCode, AdminComp }, newMembers30d, estimatedArrUsd, lapsed30d }` |

### Sign-up codes

| Method and path | Notes |
|---|---|
| `GET /api/v1/admin/signup-codes?search=&status=Active\|Scheduled\|Expired\|Exhausted\|Inactive&tag=&sort=-createdUtc` | Items: `SignupCode` below |
| `POST /api/v1/admin/signup-codes` | `{ code? (omit to generate), name, description?, notes?, grantType ("DurationMonths" \| "FixedEndDate" \| "Lifetime" \| "Discount"), grantMonths?, grantEndUtc?, discountPercent? or discountAmountCents? (Discount: exactly one), discountDuration? ("Once" \| "Forever", Discount), maxRedemptions? (null = unlimited), startsUtc?, expiresUtc?, tags? }` returns `201 SignupCode`. `409 CodeInUse` |
| `GET /api/v1/admin/signup-codes/{id}` | `SignupCode` plus `redemptionsByDay: [{ date, count }]` (last 90 days) and `outcomes: { active, convertedToPaid, lapsed, revoked }` |
| `PUT /api/v1/admin/signup-codes/{id}` | any of `name, description, notes, maxRedemptions, startsUtc, expiresUtc, isActive, tags`; `code`, grant, and discount fields are accepted only while `redemptionCount` is 0 (`409 CodeLocked` otherwise). `maxRedemptions` cannot go below `redemptionCount` |
| `POST /api/v1/admin/signup-codes/{id}/deactivate` | sets `isActive: false` |
| `DELETE /api/v1/admin/signup-codes/{id}` | deletes a code with no redemptions; `204`, or `409 CodeRedeemed` when it has been redeemed (deactivate instead) |
| `POST /api/v1/admin/signup-codes/batches` | `{ name, quantity (1 to 1000), grantType, grantMonths?, grantEndUtc?, discountPercent?, discountAmountCents?, discountDuration?, expiresUtc?, tags? }` creates single-use codes; returns `201 { batchId, codes: [SignupCode] }` |
| `GET /api/v1/admin/signup-codes/{id}/redemptions?status=&sort=-redeemedUtc` | Items: `{ id, userId, memberDisplayName, memberEmail, redeemedUtc, membershipEndsUtc, isNewAccount, status: "Active" \| "Expired" \| "Revoked", convertedToPaid, revokedUtc, revokeReason }` |
| `POST /api/v1/admin/signup-code-redemptions/{id}/revoke` | `{ reason }` |
| `POST /api/v1/admin/signup-codes/{id}/revoke-all` | `{ reason }` returns `{ revokedCount }` |
| `GET /api/v1/admin/signup-codes/summary` | `{ activeCodes, totalRedemptions, redemptions30d, activeFreeMembers, convertedToPaid, conversionRate }` |
| `GET /api/v1/admin/signup-codes/export.csv` and `GET /api/v1/admin/signup-codes/{id}/redemptions/export.csv` | `text/csv` |

`SignupCode`:

```json
{
  "id": "suc_...", "code": "SEMINAR2026", "name": "Spring seminar 2026", "description": "...", "notes": "...",
  "grantType": "DurationMonths", "grantMonths": 12, "grantEndUtc": null, "grantSummary": "Free membership for 12 months",
  "discountPercent": null, "discountAmountCents": null, "discountDuration": null, "stripeCouponId": null,
  "maxRedemptions": 50, "redemptionCount": 37, "startsUtc": null, "expiresUtc": "2026-12-31T23:59:59Z",
  "isActive": true, "status": "Active", "batchId": null, "tags": ["seminar"],
  "joinUrl": "https://example.test/join?code=SEMINAR2026",
  "lastRedeemedUtc": "2026-11-10T18:00:00Z", "createdByUserId": "usr_...", "createdUtc": "...", "updatedUtc": "..."
}
```

`status` is derived: `Inactive` when not active, `Scheduled` before `startsUtc`, `Expired` after `expiresUtc`, `Exhausted` when `redemptionCount >= maxRedemptions`, otherwise `Active`.

### Audit

`GET /api/v1/admin/audit?actorUserId=&targetType=&targetId=&action=&fromUtc=&toUtc=&sort=-createdUtc` returns paged `{ id, actorUserId, actorName, action, targetType, targetId, summary, sourceIp, createdUtc }`. Actions include `member.flags`, `member.roles`, `member.comp`, `member.ban`, `member.mute`, `member.sessions.revoke`, `signupCode.create`, `signupCode.update`, `signupCode.deactivate`, `signupCode.batch`, `signupCode.redeem`, `signupCode.discount` (a discount code used at checkout), `signupCode.revoke`, `settings.update`, `server.restart`, `account.delete`.

---

## 8. Admin console sign-in

The admin console login accepts either email and password (for members with `SystemAdmin`) or the break-glass token (an "Use admin token" link). After password sign-in it uses the normal access and refresh tokens (`X-Client: web`). A signed-in non-admin gets `403 Forbidden` from admin routes and the console shows "This account is not an administrator."

---

## 9. Local development defaults

- Email is delivered to Mailpit in the Docker stack: SMTP `mailpit:1025`, inbox UI at http://127.0.0.1:8025.
- Stripe, Apple, Google, and Facebook are disabled until their settings are filled in. When disabled, the matching routes return `503 IntegrationNotConfigured` with a message naming the missing setting, and the clients hide that option.
- `GET /api/v1/auth/providers` (anonymous) tells clients what is available and gives the public identifiers the web SDKs need (never secrets):

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

  `apple.webClientId` is the Services ID used by Sign in with Apple JS (redirect URI `{web.publicBaseUrl}/auth/apple/callback`, `usePopup: true`). For Google, `webClientId` is the first configured client id whose purpose is Web; the backend learns each id's purpose from settings `oauth.googleWebClientId`, `oauth.googleIosClientId`, and `oauth.googleAndroidClientId`, and accepts all three as audiences.
- `auth.bootstrapAdminEmail` defaults to empty; each property's local Docker settings set it (for example `admin@example.test` for the sample).
- `auth.bootstrapAdminPassword` (Secret, `TK_BOOTSTRAP_ADMIN_PASSWORD`) defaults to empty. With both set, startup creates the bootstrap administrator (verified email, `SystemAdmin`, current documents accepted) when the property has no members; the configured password skips the sign-up length rule. Local development only for short passwords.

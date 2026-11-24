# Mobile

The TrellisKit engine's iOS and Android app (phone and tablet), built with React Native, Expo (SDK 58), and Expo Router. The app is engine code only: the property it runs as (name, brand, colors, icons, splash, domains, minimum age, store product id, profile taxonomy) comes from a property folder (see `docs/TRELLISKIT.md`).

## Navigation

The upper-left menu opens the complete side drawer. Members is a community destination; Settings contains account preferences. A property chooses up to four bottom shortcuts through `mobile.navigation.shortcuts`, while `mobile.navigation.drawerItems` sets the drawer order. The neutral sample uses Feed, Forums, Messages and Profile shortcuts. An empty shortcuts array selects drawer-only navigation.

The [navigation contract](../docs/TRELLISKIT.md#23-mobile-navigation-mobilenavigation) lists identifiers, defaults, validation and access behavior. Both surfaces share `resolveMobileNavigation` from `@trelliskit/shared`, including feature visibility, guest restrictions, lock badges and property terminology. The drawer preserves existing deep links and detail Back behavior. The app keeps its bundled navigation when an older server's public property response lacks the new field.

## Property

The active property is selected only by directory path, `TK_PROPERTY_DIR`: absolute, or relative to the engine root (the folder that contains `mobile/`). When it is unset the neutral sample property, `samples/example` (Example Community), is used. Nothing in `mobile/` names a specific property.

| What | Where it comes from |
|---|---|
| App name, slug, scheme, iOS bundle id, Android package, EAS project id | `property.json` (`name`, `id`, `mobile.*`), read by `app.config.js` through `scripts/property.js`. `EAS_PROJECT_ID` overrides `mobile.easProjectId`. |
| Icons, adaptive icon, splash (light and dark), favicon, in-app logo | `<property>/mobile/*.png`, generated from the property's `brand/` (see "Property assets") |
| Theme brand colors (`primary`, `onPrimary`, `accent`, `onAccent`, `accentSoft`) | `brand.colors.light` and `brand.colors.dark`; neutrals (backgrounds, text, status) stay in `src/theme/tokens.js` |
| Strings with the property's name, owner, minimum age, product name, billing interval | `{token}` placeholders in `src/strings.js` (`{name}`, `{owner}`, `{minimumAge}`, `{productName}`, `{interval}`, ...) filled from the property (`propertyTokens` in `@trelliskit/shared`) |
| Terms of Use and Privacy Policy links | `https://<domains.web>/terms` and `/privacy` |
| Paywall product id | `membership.storeProductId` (falls back to the `GET /auth/providers` store block, then `GET /membership`); the price shown is always the store's localized price |
| Profile editor and member display (rank picker with colors, level picker, rank history, affiliation, goals) | the `profile` section; disabled sections are hidden and not sent |
| API base URL in preview and production builds | `https://<domains.api>` unless `EXPO_PUBLIC_API_URL` is set |

At build time `app.config.js` bundles the public part of `property.json` (everything except `web`) as `extra.property`. At launch `PropertyProvider` (`src/property/`) fetches `GET /api/v1/property` and merges it over the bundled copy, so profile labels and taxonomy follow the server; offline or on the first launch the bundled copy is used. Strings and theme colors use the bundled copy (they are fixed per build).

App code imports property images as `tk-property/<file>` (for example `tk-property/logo.png`). `metro.config.js` resolves that to `<property>/mobile/<file>` and watches the property folder; `jest.config.js` maps it the same way. Restart Metro with `--clear` after changing `TK_PROPERTY_DIR`.

```bash
npx expo config --type public                                        # Example Community (sample)
TK_PROPERTY_DIR=/path/to/my-property npx expo config --type public   # another property
TK_PROPERTY_DIR=/path/to/my-property npx expo start --clear
```

For EAS cloud builds, set `TK_PROPERTY_DIR` (and `EAS_PROJECT_ID` if the property does not set `mobile.easProjectId`) as EAS environment variables of the property's deployment; they are property-owned and never committed in engine files.

Create a separate upload directory before building a property. A direct EAS archive of a larger checkout can include unrelated untracked files, deployment credentials, and design drafts. The staging helper copies mobile runtime sources, the shared package, `property.json`, public mobile images/fonts, and public update certificates. It excludes deployment files, generated native projects, dependency directories, hidden files, and drafts; symlinks and private keys in certificate files are rejected. `property.json` must contain public configuration only, as it is bundled into the app.

```sh
# Run from mobile/. The output directory must be empty and outside both source folders.
node scripts/stage-build.js --property /path/to/my-property --out /tmp/my-mobile-release
# Use the mobileDir, root and propertyPath printed by the helper below.
cd /tmp/my-mobile-release/trelliskit/mobile
npm ci
export EAS_NO_VCS=1
export EAS_PROJECT_ROOT=/tmp/my-mobile-release
export TK_PROPERTY_DIR=../my-property
npx eas-cli build:inspect --profile production --platform ios --stage archive --output /tmp/my-mobile-archive
npx eas-cli build --profile production --platform all --non-interactive --no-wait
```

The staged engine directory is always `trelliskit`; the property folder uses its source folder name in lowercase (`property` if that would collide with `trelliskit`). These stable names preserve relative asset paths when checkouts move or change casing. Use the helper's printed paths, which resolve filesystem aliases such as macOS `/tmp` to `/private/tmp`. The property's EAS production environment must also set the printed relative `TK_PROPERTY_DIR`, since a local absolute path does not exist on a cloud worker. Keep provider configuration and `GOOGLE_SERVICES_JSON` in that EAS environment. When `GOOGLE_SERVICES_JSON` is a secret EAS file variable, also set it in the local build shell to an existing private copy of the same file, outside the staging directory. EAS cannot download secret file variables while resolving the local fingerprint; omitting the local file makes Android fail its local-versus-cloud runtime check. The cloud worker continues to use its EAS-managed file. The helper preserves `eas.json`, lockfiles, certificates, and app versions; it does not create credentials, upload, submit, or modify a property. Recreate the stage after source changes. Export future self-hosted updates from this same staged layout with the same production provider environment and local Firebase file, using `npm run publish-update -- --channel production --platform ios --message "Release description and source commit"` (dry run first). Running an export directly from differently named or cased standalone checkouts can change native asset paths and the fingerprint. A stage has no Git checkout, so the publisher records a null `gitCommit`; include the source commit in the release message when staging. Verify the resolved runtime against the target binary before adding `--upload` with the private `TK_ADMIN_TOKEN` in the publishing environment.

### Property assets

`scripts/generate-property-assets.js` produces a property's app images from its logo (`brand.logo`, plus an optional `brand/logo-dark.png` for the dark splash) with `jimp-compact` (pure JavaScript, pinned as a dev dependency, no native toolchain):

```bash
npm run assets -- /path/to/property     # or: node scripts/generate-property-assets.js /path/to/property
npm run assets                          # TK_PROPERTY_DIR, else the sample
```

It writes into `<property>/mobile/`: `icon.png` (1024, opaque on white), `android-icon-foreground.png` (1024, logo inside the adaptive icon safe zone), `android-icon-background.png` (1024, white), `android-icon-monochrome.png` (1024, white silhouette, also the Android notification icon), `splash-icon.png` and `splash-icon-dark.png` (1024, transparent), `favicon.png` (48), and `logo.png` (512, shown on Welcome). Commit the output with the property. A new property only needs `property.json` and `brand/logo.png`, then this script.

Phase 1 (identity and membership, coded against `docs/PHASE1_CONTRACT.md`) adds sign-in with email, Apple, Google, and Facebook; registration with the property's minimum age (`membership.minimumAge`); age attestation; profile setup; the in-app purchase paywall; tab gating; and Account settings (edit profile, sign-in methods, password, email verification, membership, data export, account deletion). The Phase 0 tab placeholders remain until later phases fill them in.

### Flows

| Flow | Routes |
|---|---|
| Launch | `/` restores the session from the refresh token in secure storage (`POST /auth/refresh`), then redirects by state. Offline launches keep the token and offer Retry. |
| Signed out | `/welcome`, `/sign-in` (provider buttons from `GET /auth/providers`), `/email-sign-in`, `/register`, `/forgot-password` (request only; the reset happens on the emailed web link) |
| Setup (signed in) | `/age` (social accounts), `/profile-setup`, `/subscribe` (paywall, skipped when the membership is already active from any source), `/allow-notifications` (asks for push permission and registers the token; shown once per device), then `/feed` |
| Member tabs | Gated: signed in, age attested, and an active membership (or `SystemAdmin`). Everyone else is redirected to the step they still need. |
| Session revoked during use | Any request whose refresh is rejected (or that still answers 401 after a refresh) clears the session and lands on `/sign-in` with "You were signed out. Please sign in again." (or "This account has been suspended." for `AccountBanned`). |
| Account (signed in, no membership needed) | `/account`, `/account/edit-profile`, `/account/sign-in-methods`, `/account/password`, `/account/membership`, `/account/delete`. Reachable from Profile and from the paywall, so lapsed members can still delete their account. |

### App Store policy built in

- The app never shows, accepts, or mentions sign-up codes, web pricing, or web purchase. Members who joined on the website sign in and are let in because their membership is active. Non-store memberships show as "Annual subscription" or "Complimentary membership" with status and expiry only. `__tests__/policy.test.js` renders every screen and scans the strings module for code, redeem, promo, coupon, website, and Stripe wording.
- On iOS, Google and Facebook buttons appear only when Sign in with Apple is also shown (Guideline 4.8).
- In-app account deletion with a typed `DELETE` confirmation and the server's `storeSubscriptionNote` (Guideline 5.1.1(v)).
- The paywall shows the store's localized price, Restore purchases, auto-renewal terms, and Terms of Use and Privacy Policy links (opened in the in-app browser).

### Purchases (app-wide)

`src/billing/PurchaseProvider.jsx` (mounted in `app/_layout.jsx`) owns the expo-iap connection and the purchase update and error listeners whenever a member is signed in and the store is enabled. Every transaction, whether started on the paywall, approved later through Ask to Buy, delivered as a renewal at launch, or left over from an interrupted purchase, goes through `src/billing/processor.js`:

- deduplicated by transaction id (concurrent deliveries share one verify; a transaction finished this launch is skipped unless Restore forces it),
- posted to the verify route, with retries on network errors, timeouts, 429, and 5xx after 2, 5, 15, 30, and 60 seconds,
- finished exactly once, and only after a 200 (a transaction that verified but failed to finish is finished on its next delivery without a second verify),
- followed by `GET /auth/me`, so gating updates wherever the member is.

On connect it also sweeps unfinished transactions (`getPendingTransactionsIOS` on iOS, unacknowledged purchases on Android). The paywall (`usePaywall`) only loads the product, starts purchases and restores, and shows provider events; once the member is active the paywall redirects to the next step.

### Phase 2: feed and engagement

Coded against `docs/PHASE2_CONTRACT.md` (section 19 decisions applied) and `docs/ENUMERATION_AND_METADATA.md`, which overrides the contract's paging and list shapes.

| Area | What the app does | Code |
|---|---|---|
| Lists | Every list is `POST /api/v1/{collection}/enumerate` with an `EnumerationQuery` (`maxResults` always set, clamped to 100) paged by `continuationToken`; the app never asks for "all". Results are `EnumerationResult` (`objects`, `continuationToken`, `endOfResults`) merged de-duplicated by id | `src/content/enumeration.js`, `src/hooks/useCursorList.js`, `src/api/community.js` |
| Filters | Feed, comments, notifications, and blocked members offer search, ordering, label chips with autocomplete (`labels/enumerate`) and an All/Any toggle, `key = value` tag rows with key and value autocomplete and "has key" (`*`), active filter chips with Clear all, and a count on the Filters button. Phones use a bottom sheet; tablets a side panel (comments use the sheet inside the post pane). Filters persist per screen for the session and reset at sign-out | `src/components/filters/`, `src/hooks/useListFilters.js` |
| Labels and tags | Editors on the post composer, the comment composer, each post attachment, and the profile form. Members never see or send `staff:` entries; staff do. On edit, metadata is sent only when it changed (replace semantics) | `src/components/filters/MetadataEditor.jsx`, `src/content/metadata.js` |
| Feed tab | FlashList, pinned first, pull to refresh, infinite scroll, unknown `kind`s skipped. Post cards render the Markdown subset natively (never HTML or a WebView), link resolved mentions to `/members/{id}`, show link preview cards, image galleries (expo-image, blurhash, full screen viewer), video (expo-video, HLS), the "edited" marker with history, and the Report and Block actions. Tablets show the feed and the selected post side by side | `app/(tabs)/feed/`, `src/components/feed/` |
| Composer | `/compose` (and `?id=` to edit) for `SystemAdmin` or `canPostToFeed` with `canPost` (muted members see until when). Markdown with preview, @mention autocomplete (150 ms debounce), library and camera (HEIC converted to JPEG), upload sessions: single PUT with exactly the listed headers or multipart (3 parts in parallel, ETags kept verbatim), resume through `upload-urls` on foreground, then polling until Ready (images every 2 s up to 60 s, video every 5 s). Attach buttons follow `viewer.canAttachMedia` and `canAttachVideo`; limits 10 attachments and one video | `app/compose.jsx`, `src/media/upload.js`, `src/media/useUploads.js`, `src/media/pick.js` |
| Media access | `POST /media/grant` after sign-in, on foreground, and 10 minutes before expiry; cookies are sent as a `Cookie` header on expo-image and expo-video sources (the bearer token is added only for root-relative local URLs on the API origin). A failed load refreshes the grant once and retries | `src/media/MediaProvider.jsx` |
| Reactions | The property's set in order, optimistic toggle with a selection haptic and rollback on failure, picker, who-reacted sheet filtered by reaction | `src/components/feed/ReactionBar.jsx` |
| Comments | `/feed/{id}`: tree with collapse, "load N more replies", Top, Newest, Oldest, reply (flattened replies show "replying to"), edit with history, delete placeholders, one attachment, mentions; `?comment=` shows the comment in context with View all comments | `src/components/feed/PostDetail.jsx`, `src/content/comments.js` |
| Notifications | Center with All and Unread, read and unread per row, Mark all as read; the header bell shows the unread count (99+), polled on launch, on foreground, every 60 s while active, and on a foreground push; the app icon badge follows it. Push taps (including the one that cold-started the app) mark read and open `data.path`; unknown paths open the center | `app/notifications.jsx`, `src/notifications/NotificationsProvider.jsx`, `src/notifications/deepLinks.js` |
| Notification settings | Account > Notification settings: per category in-app, push, and email (all on by default; locked cells disabled), Pause all push notifications, notification hours (default 7:00 AM to 10:00 PM, 30 minute steps, device time zone sent when it differs), and the device permission banner (asks again, or opens Settings) | `app/account/notifications.jsx`, `src/notifications/preferences.js` |
| Push tokens | `POST /api/v1/me/push-tokens` with `{ token, platform: 'Ios' or 'Android', deviceName, appVersion }`; `DELETE /api/v1/me/push-tokens` with `{ token }` before sign-out and when the permission is turned off (re-checked on every foreground). A 404 defers registration to the next launch | `src/notifications/push.js` |
| Safety | Report posts, comments, and profiles (reason, details required for Other), then thanks and a block offer. Blocking hides the author's posts and turns their comments into placeholders. Staff cannot be blocked: the action is not offered and the app explains why (also for a `CannotBlockStaff` answer). Account > Blocked members lists and unblocks | `src/components/feed/ReportSheet.jsx`, `app/account/blocked.jsx` |
| Profiles | `/members/{id}`: avatar, name, staff badge, rank with the property's label and color, affiliation and rank history when enabled, bio, block or unblock, report | `app/members/[id].jsx` |

Libraries added (pinned with `npx expo install`): `@shopify/flash-list`, `expo-image`, `expo-video`, `expo-file-system`, `expo-haptics`. `app.config.js` adds the `expo-image` and `expo-video` plugins and camera and microphone permission text.

`@trelliskit/shared` now carries Phase 2 helpers (markdown, mentions, cursor paging, uploads), but they predate the enumeration specification (cursor shapes), so the app keeps local copies in `src/content/` and `src/media/` that follow it; they have no React Native dependency and can move to shared.

### Resilience and accessibility

- An offline banner (`expo-network`) shows at the top of every screen while the device has no connection.
- Every request times out after 15 seconds with its own message; store calls on the paywall time out after 20 seconds.
- Screens that load data show the same error card with Retry (`ErrorState`): sign-in options, the paywall, Membership, About, and the offline launch screen. Forms show the error next to the submit button.
- Every control has a label, is at least 44 points tall, and grows with the system text size (no capped or truncated text; date fields wrap at the largest sizes). Form field errors are part of the field's announcement. With Reduce Motion on, screen transitions cross-fade instead of sliding.

## Live updates and request identification

Implements the mobile side of `docs/TRELLISKIT.md` sections 8.1 (self-hosted Expo Updates) and 10.1 (property resolution). `expo-updates` (58.0.22, pinned) talks to TrellisKit's own Expo Updates protocol v1 endpoint; nothing uses Expo's hosted update service.

### Build configuration

`app.config.js` takes the updates block from `scripts/updates.js`:

| Setting | Value |
|---|---|
| `updates.url` | `https://<domains.api>/api/v1/updates/manifest`. `TK_UPDATES_URL` overrides it (full URL, for example `http://127.0.0.1:8000/api/v1/updates/manifest` for the local Docker backend); otherwise `EXPO_PUBLIC_API_URL` + the manifest path when that is set |
| `runtimeVersion` | `{ policy: 'fingerprint' }`. `fingerprint.config.js` skips the `extra` section (JavaScript-only: bundled property, API URL, build channel), so editing `property.json` does not change the runtime version, and neither does the channel: preview and production binaries of one commit share one runtime version (`__tests__/fingerprint.test.js` resolves it per channel) |
| Code signing | `updates.codeSigningCertificate` = `<property>/mobile/updates/certificate.pem`, `codeSigningMetadata` = `{ keyid: 'main', alg: 'rsa-v1_5-sha256' }` (the key id comes from `mobile/updates/signing.json` after a promotion). Mandatory: without the certificate the build gets `updates: { enabled: false }` and prints `[trelliskit] Warning: Live updates are disabled ...`. When the certificate is a CA, `plugins/withUpdatesCertificateChain.js` turns on expo-updates' manifest response certificate chain option so the standby key is trusted too (see Key rotation); the build warns when rotation is not ready |
| Channel | `production`, `preview`, or `development`, from `TK_UPDATES_CHANNEL` (set per profile in `eas.json`), else `APP_VARIANT`, else `development`. It goes only to `extra.updates.channel`; the app sends it as `expo-channel-name` at runtime |
| Request headers | Embedded, the same for every channel: `expo-channel-name: production` (placeholder), `X-TK-Property: <property id>`, `X-TK-App` (the iOS bundle id on iOS and the Android package on Android; `plugins/withUpdatesAppHeader.js` rewrites the native copies and is listed before `expo-updates` so its mods run last), and `X-TK-Install-Id: ''` (placeholder). Before its first check the app replaces all four with `Updates.setUpdateRequestHeadersOverride` (expo-updates only accepts override keys that are embedded, hence the placeholders) |
| Native checks | `checkAutomatically: 'ON_ERROR_RECOVERY'`, `fallbackToCacheTimeout: 0`: the app-level controller drives normal checks; native only checks while recovering from a crash |
| `extra.updates` | `{ enabled, channel, checkIntervalMinutes, url }` for the runtime |

```bash
npx expo config --type public                                   # sample: updates enabled
TK_PROPERTY_DIR=/path/to/my-property npx expo config --type public   # disabled with a warning until the certificate exists
TK_UPDATES_URL=http://127.0.0.1:8000/api/v1/updates/manifest npx expo run:ios --configuration Release   # against the local Docker backend
```

Updates never run in development (Metro, the dev client, Expo Go); test them with a release build.

### Code signing certificate

```bash
npm run updates-certificate -- /path/to/my-property      # or: node scripts/generate-updates-certificate.js <propertyDir>
npm run updates-certificate -- /path/to/my-property --key-out /secure/place/key.pem
```

The script generates two RSA 2048 key pairs: the active key (`main`) with a self-signed CA certificate (basicConstraints CA, key usage digitalSignature and keyCertSign, extended key usage codeSigning, 10 years by default, `--years`) at `<property>/mobile/updates/certificate.pem`, and the standby key (`standby`) with a certificate issued by the active key at `<property>/mobile/updates/standby-certificate.pem`. Commit both certificates with the property. The private keys go outside the repository only (default `~/.trelliskit/keys/<propertyId>/updates-private-key.pem` and `updates-standby-private-key.pem`, mode 0600, `--key-dir` to change; it refuses any path inside the engine, the property, or their Git work trees). Register both keys with the backend (admin portal signing keys, or AWS Secrets Manager / `TK_UPDATES_SIGNING_KEY` for the active one), then delete the local copies. It refuses to overwrite existing files without `--force`. The equivalent openssl commands are in the script header. The sample property's certificates are committed at `samples/example/mobile/updates/`. A real property generates its own the same way, from `mobile/`, for example `npm run updates-certificate -- ../<property folder>`.

```bash
npm run updates-certificate -- <propertyDir> --standby --active-key <main key>                     # add a standby to an existing CA certificate
npm run updates-certificate -- <propertyDir> --reissue-active --standby --active-key <main key>    # upgrade a certificate made before rotation support (same key)
npm run updates-certificate -- <propertyDir> --promote-standby --standby-key <standby key> [--new-standby-id <id>]
```

### Key rotation

expo-updates 58 embeds exactly one certificate and one `keyid` per build, and has no runtime API to change it; in its default mode a signature with another `keyid` is rejected. Its "include manifest response certificate chain" mode instead verifies the signature with the leaf of the `certificate_chain` part of the manifest response plus the embedded certificate (which must be a self-signed CA when a chain is sent) and does not compare key ids. So instead of two embedded certificates, a build embeds the active CA certificate and trusts every certificate it issued, which is how the standby key is trusted without being embedded:

| Manifest signed with | Response | Accepted by |
|---|---|---|
| Active key (`main`) | `expo-signature: ...keyid="main"`, no `certificate_chain` part | Every build embedding that certificate (also builds made before rotation support, which share the key) |
| Standby key (`standby`) | `expo-signature: ...keyid="standby"` plus a `certificate_chain` part containing `standby-certificate.pem` | Every build with the chain option (all builds from this version on) |

Procedure: (1) register the standby key and certificate with the backend in advance; (2) to rotate (planned, or because the active key leaked), switch the property's signing to the standby key: installed binaries keep receiving updates; (3) before the next store release run `--promote-standby`: the standby key becomes the active key of new binaries (its key id is written to `mobile/updates/signing.json` and sent in `expo-expect-signature`), and a new standby is created and registered; (4) retire the old key on the server. Old binaries keep `keyid: "main"` in `expo-expect-signature` and are served the new key's signature plus the chain of standby certificates back to their embedded certificate. A leaked active key can still sign for binaries that embed its certificate (true of any embedded-trust scheme); those binaries stop being a risk only when they are replaced through the store and the minimum binary version.

### Runtime

| Part | What the app does | Code |
|---|---|---|
| Request headers | Before the first check, `Updates.setUpdateRequestHeadersOverride` sets `expo-channel-name` (the build channel: taken from the embedded config on an embedded launch and saved per binary in secure storage, so a downloaded or promoted update never changes it), `X-TK-Property`, `X-TK-App`, and `X-TK-Install-Id`. The override is persisted natively. A binary built before these headers existed rejects the override and keeps its embedded headers (reported, never thrown) | `src/updates/requestHeaders.js` |
| Install id | A random UUID created once per install (`expo-crypto`), stored in secure storage as `tk.installId`, and sent only on update checks as `X-TK-Install-Id` for staged rollouts. It is not linked to the member: API requests never carry it, and sign-in and sign-out do not touch it | `src/updates/installId.js` |
| Updates controller | Checks on launch and whenever the app returns to the foreground, at most once per interval (property setting `updates.checkIntervalMinutes` from `GET /api/v1/property`, else the build value, default 15, minimum 1). Concurrent checks share one run. A new update downloads in the background and applies on the next launch; the same update is never downloaded twice; `rollBackToEmbedded` is handled the same way. Failures (offline, server errors, rejected signatures, failed reloads) are recorded on the state and retried at the next allowed check, never thrown | `src/updates/controller.js`, `src/updates/UpdatesProvider.jsx` |
| Critical updates | A manifest with `extra.critical: true` (or `metadata.critical: "true"`) shows "Update ready" with Later and "Restart to update" once per update, with `extra.criticalMessage` (up to 200 characters) as the text when present; Restart calls `Updates.reloadAsync()`, and if that fails the update still applies on the next launch | `src/updates/UpdatesProvider.jsx` |
| Update required | When the binary version (`Constants.nativeAppVersion`) is below `mobile.minimumBinaryVersion` for the platform (one string, or `{ "ios": "1.2.0", "android": "1.2.0" }`), the whole app is replaced by a blocking "Update required" screen with the store link: `mobile.storeUrls.ios` / `.android` (https only), else `https://apps.apple.com/app/id<mobile.appStoreId>` on iOS and the Play Store page for `mobile.androidPackage` on Android, else guidance text. The value comes from `GET /api/v1/property` merged over the bundled copy (the app manifest will feed the same property when it exists) | `src/updates/minimumVersion.js`, `src/updates/UpdateRequiredGate.jsx` |
| New version hint | When the binary version is below the optional `mobile.latestBinaryVersion` (same shape as the minimum), a dismissible bar at the top says "A new version of {name} is available." with Update (the store link) and Dismiss. A dismissal is remembered per version (`tk.updates.latestHintDismissed`). It never blocks, and binaries below the minimum get the blocking screen instead | `src/updates/NewVersionHint.jsx`, `app/_layout.jsx` |
| Identification headers | Every API request (all `MobileApiClient` paths, including refresh, push token registration and removal, store verification, avatar upload, and the media grant), every media source (expo-image and expo-video, through `mediaHeaders`), and remote avatars send `X-TK-Property` (bundled property id), `X-TK-App` (bundle id or package for the running platform), and `X-Client-Update-Id` (the running update id, when expo-updates reports one). Presigned storage uploads keep exactly the headers the server listed, because extra headers would break their signatures | `src/api/identity.js`, `src/api/MobileApiClient.js`, `src/media/MediaProvider.jsx`, `src/components/Avatar.jsx` |

### Publishing an update

```bash
npm run publish-update -- --channel production --message "Fix the feed header"                 # dry run
npm run publish-update -- --channel preview --message "..." --platform ios --critical-message "Fixes sign in" --rollout 10
npm run publish-update -- --channel preview --message "..." --platform android --update-group-id <uuid from the iOS run>
TK_ADMIN_TOKEN=... npm run publish-update -- --channel production --message "..." --upload      # once the backend route exists
```

`scripts/publish-update.js` sets `APP_VARIANT` and `TK_UPDATES_CHANNEL` to the channel (the API URL in the published config depends on it; the runtime version does not), runs `expo export --platform <p> --source-maps external` into `dist/updates/<p>` for iOS and Android, resolves the runtime version with `expo-updates runtimeversion:resolve`, and hashes every file (SHA-256 base64url as the protocol `hash`, MD5 hex as the `key`). Without `--upload` it prints the request it would send. With `--upload` it sends one `multipart/form-data` request per platform to `POST /api/v1/admin/updates` (origin `TK_UPDATES_ADMIN_URL`, default `https://<domains.api>`) with `Authorization: Bearer $TK_ADMIN_TOKEN`, `X-TK-Property`, and `X-TK-App`: an `update` JSON part (`platform`, `runtimeVersion`, `channel`, `message`, `critical`, `criticalMessage`, `rolloutPercentage`, `gitCommit`, `createdUtc`, `updateGroupId`, `expoConfig`, `launchAsset`, `assets`, `sourceMaps`) plus one file part per asset named by its key and one private part per source map named `sourcemap-<md5>` (each `sourceMaps` entry names its `bundleKey`; the server stores them for symbolicating error reports and never serves them). Every platform of one run carries the same `updateGroupId` (a new UUID, or `--update-group-id`). `expoConfig.extra.updates.channel` is removed before upload: the app's channel comes from the binary. This contract is provisional until the backend route lands. Publish with the same build environment variables as the EAS environment of the target binaries (`EXPO_PUBLIC_*`, `EAS_PROJECT_ID`, `GOOGLE_SERVICES_JSON`), or the fingerprint will not match.

## Prerequisites

- Node.js 24.3 or later and npm. Expo SDK 58 and React Native 0.88 also run on Node 22.13+, but the Jest suite needs Node 24: on Node 22, tests that combine fake timers with the fake fetch server hang, so CI runs Node 24
- For simulators: Xcode with an iOS simulator (macOS) and/or Android Studio with an emulator
- For sign-in with Google or Facebook and for purchases: a development build (see "Expo Go and the dev client")
- A running backend (see `backend/README.md`), listening on port 8000 by default

## Install

`mobile/` is a standalone Expo project with its own `package.json` and `package-lock.json`. It is not one of the root npm workspaces. Always install from this folder:

```bash
cd mobile
npm install
```

The shared package is linked with `"@trelliskit/shared": "file:../shared"`. `metro.config.js` adds `../shared` to Metro's watch folders and resolves packages from `mobile/node_modules`, so edits in `shared/src` hot reload in the app. Add new native or Expo packages with `npx expo install <package>` so versions match the SDK.

## Run

```bash
npx expo start          # then press i (iOS simulator), a (Android emulator), or scan the QR code with Expo Go
npm run ios             # same as expo start --ios
npm run android         # same as expo start --android
```

`expo-dev-client` is installed, so `npx expo start` serves a development build by default. Press `s` in the Metro terminal (or run `npx expo start --go`) to switch to Expo Go. The Phase 0 "Preview the app" bypass is gone; the tabs require a real member session. Sign in against a local backend, or use an account with `SystemAdmin`.

Step-by-step development builds for the iOS simulator, iPad, the Android emulator, and a physical iPhone, with the local limitations (push, store purchases, maps): [LOCAL_DEVICE_TESTING.md](LOCAL_DEVICE_TESTING.md).

## Pointing at a local backend

The API base URL comes from `EXPO_PUBLIC_API_URL` (read in `app.config.js` and exposed as `extra.apiUrl`). When it is not set, the app uses:

| Target | Default |
|---|---|
| iOS simulator | `http://127.0.0.1:8000` |
| Android emulator | `http://10.0.2.2:8000` (the emulator's alias for the host loopback) |

For a physical device, use your computer's LAN address:

```bash
EXPO_PUBLIC_API_URL=http://192.168.1.20:8000 npx expo start --clear
```

Use `--clear` after changing the variable so Metro rebuilds with the new value. Plain `http://` works in development builds and Expo Go; release builds should point at HTTPS. Profile > About this app shows the server address in use and the result of `GET /api/v1/health`.

## Tests and lint

```bash
npm test                          # Jest (jest-expo preset, @testing-library/react-native)
npm run lint                      # ESLint (eslint-config-expo, flat config)
npx expo-doctor                   # dependency and config checks
npx expo export --platform ios    # bundle build; proves Metro resolves @trelliskit/shared and the property images (output in dist/, git ignored)
```

Tests live in `__tests__/`. `jest.setup.js` mocks the native modules (expo-updates, secure store, Apple, Google, image picker and manipulator, web browser, crypto, notifications, device, network, expo-image, expo-video, expo-haptics, and an in-memory expo-file-system) and loads FlashList's jest layout setup and `test-utils/fakeServer.js` installs a fake backend as `global.fetch`, so the real `MobileApiClient`, `AuthProvider`, and Expo Router screens run in the tests. Coverage includes refresh-on-401 with one shared in-flight refresh, secure-store persistence, launch restore and routing by state, tab gating, registration and the property's minimum age, provider button visibility, Apple nonce handling, error copy (409 AccountLinkRequired, 423, AccountBanned, AgeRequirementNotMet), the paywall (purchase then verify then finish, no finish on failed verify, restore, cancel, pending, unavailable store), profile setup with live handle availability and rank history, avatar resizing and upload, account deletion confirmation, the app-wide purchase listener (deduplication, finish only after verify, backoff on server errors, launch-time sweep, renewals and Ask to Buy while the paywall is closed), push registration (permission denied, 404 deferral, token persisted, launch re-registration, sign-out removal), revoked sessions during requests, timeouts, the offline banner, Retry states, accessibility (labels and 44 point targets on every Phase 1 screen, Dynamic Type, Reduce Motion), the no-sign-up-code policy scan, Phase 2 (`feed`, `composer`, `comments`, `notifications`, `filters`, `safety`: enumeration paging with continuation tokens, pull to refresh, Markdown rendering and sanitizing, mentions, media cookies, tablet panes, posting permissions and upload states including multipart and resume, comment tree and permalinks, optimistic reactions with rollback, notification center, badge, push taps and deep links, settings and hours, the push token route and body, report and block, the filter sheet and side panel, the labels and tags editor, and accessibility of the new screens), and property-driven configuration (`property.test.js`, `profileConfig.test.js`: app.config.js per `TK_PROPERTY_DIR`, token substitution, `GET /api/v1/property` with the bundled fallback, profile sections shown and hidden from config, and a scan proving `app/` and `src/` name no property, sport, person, domain, price, or product id). Tests run against the active property (the sample by default) and derive expected text from it (`test-utils/property.js`); property-specific cases use the fixtures in `test-utils/fixtures/` (`plain` has no ranks, history, affiliation, or goals; `custom` has its own labels). Run the suite against another property with `TK_PROPERTY_DIR=... npm test`.

## Project layout

```text
mobile/
|-- app/                       # Expo Router routes (file based)
|   |-- _layout.jsx            # root stack, theme provider
|   |-- index.jsx              # session restore, then redirect by state
|   |-- (onboarding)/          # welcome, sign-in, email-sign-in, register, forgot-password
|   |-- (setup)/               # age, profile-setup, subscribe (paywall), allow-notifications
|   |-- (tabs)/                # gated: feed (stack with [id]), forums, library, map, profile (stack with about)
|   |-- account/               # account settings, edit profile, sign-in methods, password, membership, delete, notifications, blocked
|   |-- members/[id].jsx       # member profiles
|   |-- compose.jsx            # feed post composer
|   |-- messages.jsx, notifications.jsx
|-- src/
|   |-- strings.js             # every UI string (English only)
|   |-- config.js              # API base URL and app version
|   |-- api/                   # MobileApiClient (tokens, refresh), endpoints, secure token store
|   |-- auth/                  # AuthProvider (session context), routing rules, error copy
|   |-- billing/               # PurchaseProvider (app-wide listener), processor (verify, retry, finish), usePaywall
|   |-- notifications/         # push permission and tokens, unread count and push taps, deep links, preferences
|   |-- content/               # enumeration, Markdown subset, mentions, comment tree, reactions, metadata, permissions, errors
|   |-- native/                # defensive loaders for Apple, Google, Facebook, and IAP native modules
|   |-- media/                 # avatar, media grant, upload sessions, picking
|   |-- forms.js, links.js     # client validation; Terms, Privacy, and store subscription URLs
|   |-- theme/                 # engine neutral tokens plus the property's brand colors, light and dark
|   |-- property/              # bundled property, PropertyProvider (GET /api/v1/property), token filling
|   |-- components/            # Screen, ContentColumn, SplitView, Button, AppText, Card, ...
|   |-- hooks/                 # useLayout, useServerHealth, useNetworkStatus, useReducedMotion, useStackOptions
|   |-- utils/timeout.js       # withTimeout for store calls
|   |-- updates/               # live updates controller and provider, critical prompt, minimum binary version gate
|-- __tests__/, test-utils/, jest.setup.js
|-- plugins/                   # withUpdatesAppHeader.js (per-platform X-TK-App on update requests)
|-- scripts/                   # property.js, generate-property-assets.js, updates.js, generate-updates-certificate.js, publish-update.js
|-- app.config.js, fingerprint.config.js, react-native.config.js, eas.json, metro.config.js, jest.config.js, eslint.config.js
```

Branding comes from the property (see "Property"). Keep text colors at WCAG AA contrast; `__tests__/theme.test.js` checks the main pairs against the active property's palette.

Tablets (window width 768 or more) get a centered reading column (`ContentColumn`), a list plus detail layout (`SplitView` on Forums; the feed with the selected post beside it), and filter side panels.

## Builds (EAS)

Set `mobile.easProjectId` to the existing Expo project's ID. Optional `mobile.expoSlug` and `mobile.expoOwner` select its Expo slug and owning account; the slug defaults to the property's slug and the owner is omitted when unset. These public identifiers belong in the property's `property.json`, not in the engine. An Expo slug override does not rename the property's deployment resources or key folders.

`eas.json` defines these profiles:

| Profile | Purpose |
|---|---|
| `development` | Custom dev client (`expo-dev-client`), internal distribution, iOS simulator build |
| `development-device` | Same, for physical iPhones (needed for App Store sandbox purchases) |
| `preview` | Internal distribution build for testers |
| `production` | Store build; the API URL defaults to `https://<domains.api>` from the property |

Each profile reads EAS environment variables from the matching EAS environment (`development`, `preview`, `production`). Build numbers are managed by EAS (`appVersionSource: remote`) and the `production` profile auto-increments them. `eas submit` for Android uploads to the internal testing track as a draft. Store submission steps: `docs/APP_STORE_SUBMISSION.md`.

```bash
npx eas-cli@latest build --profile development --platform ios          # simulator dev client
npx eas-cli@latest build --profile development-device --platform ios   # device dev client
npx eas-cli@latest build --profile development --platform android
npx eas-cli@latest build --profile production --platform all
npx expo start --dev-client                                            # then open the installed dev client
```

Or build locally with `npx expo run:ios` / `npx expo run:android` (requires Xcode or Android Studio).

Native projects are generated at build time (Continuous Native Generation). Do not commit `ios/` or `android/`; if you run `npx expo prebuild` or `npx expo run:ios` locally, delete those folders or keep them out of Git.

### Native configuration (environment variables)

| Variable | Used for | When empty |
|---|---|---|
| `EXPO_PUBLIC_API_URL` | Backend origin | Simulator and emulator defaults |
| `EXPO_PUBLIC_GOOGLE_IOS_URL_SCHEME` | Google Sign-In config plugin on iOS (the reversed iOS client id, `com.googleusercontent.apps....`) | Plugin skipped and the module left unlinked on iOS; Google hidden there |
| `EXPO_PUBLIC_FACEBOOK_APP_ID`, `EXPO_PUBLIC_FACEBOOK_CLIENT_TOKEN` | Facebook SDK config plugin (Limited Login, auto events and advertiser id collection off) | Plugin skipped and the SDK left unlinked; Facebook hidden |
| `EAS_PROJECT_ID` | `extra.eas.projectId`, needed for Expo push tokens | No push token; the notification step still works |
| `GOOGLE_SERVICES_JSON` | Path to `google-services.json` (EAS file variable) for Android push through FCM | Android builds work but cannot receive pushes |
| `TK_UPDATES_CHANNEL` | Update channel (`production`, `preview`, `development`; set per profile in `eas.json`) | Follows `APP_VARIANT`, else `development` |
| `TK_UPDATES_URL` | Full update manifest URL (local backend) | `EXPO_PUBLIC_API_URL` + `/api/v1/updates/manifest`, else `https://<domains.api>/api/v1/updates/manifest` |
| `TK_UPDATES_DISABLED` | `1` builds without live updates | Updates on when the certificate exists |

Set them with `npx eas-cli@latest env:create` for cloud builds, or export them in your shell before `npx expo run:ios`. Every value defaults to empty, so `expo export`, `expo-doctor`, and Expo Go work without them. `react-native.config.js` drops the Google (iOS) and Facebook native modules from autolinking when their variable is empty, because those SDKs crash at runtime without their native configuration.

The Google web, iOS, and Android client ids used at runtime come from `GET /api/v1/auth/providers`, not from the build. Sign in with Apple uses `ios.usesAppleSignIn` (the `com.apple.developer.applesignin` entitlement) plus the `expo-apple-authentication` plugin. Enable Sign in with Apple and In-App Purchase on the App ID in the Apple Developer portal; EAS can sync capabilities during the build.

### Expo Go and the dev client

| Feature | Expo Go | Dev client or store build |
|---|---|---|
| Email sign-in, registration, password reset request | Yes | Yes |
| Sign in with Apple (iOS) | Yes (Expo Go bundle id, so the backend audience will not match; use a dev client for real tests) | Yes |
| Google sign-in | Hidden | Yes, with `EXPO_PUBLIC_GOOGLE_IOS_URL_SCHEME` on iOS and the Android SHA-1 registered in Google Cloud |
| Facebook sign-in | Hidden | Yes, with `EXPO_PUBLIC_FACEBOOK_APP_ID` |
| In-app purchase and Restore purchases (`expo-iap`) | "Purchases are not available in this build" | Yes. iOS simulator needs a StoreKit configuration file in the Xcode scheme or a sandbox account on a device; Android needs a Play internal testing track |
| Push permission and token | iOS: permission only (no token for the Expo Go project); Android: hidden | Yes, on a physical device with `EAS_PROJECT_ID` (Android also needs `GOOGLE_SERVICES_JSON` and FCM V1 credentials in EAS) |
| Profile photo, secure storage, in-app browser, account settings | Yes | Yes |

Native modules are loaded defensively (`src/native/`): when one is missing the matching button or purchase option is not shown, and the app keeps running.

### Paywall and purchases

The product id comes from `GET /auth/providers` (`appleIap.productId` or `googlePlay.productId`, falling back to `storeProductId` from `GET /membership`). The store is offered only when that provider block is `enabled`. Purchases pass `storeAccountToken` from `GET /membership` as `appAccountToken` (iOS) or `obfuscatedAccountId` (Android), post the transaction to `/membership/apple/verify` (StoreKit 2 JWS) or `/membership/google/verify` (purchase token and product id), and call `finishTransaction` only after a 200. Restore purchases asks the store to sync, then re-posts the newest transaction for the product.

App identifiers come from the property (`mobile.bundleId`, `mobile.androidPackage`, `mobile.scheme`); the version is `1.0.0`.

Collection wrappers return standard enumeration pages. `getPreferences(api, query)` posts to `/me/notification-preferences/enumerate`; notification settings collect every continuation page, read categories from `objects`, and preserve `pushPaused`, `autoFollowOnComment`, and `notificationHours`. `getBatch(api, ids, query)` posts `filters.ids` to `/media/enumerate`; upload polling collects visible `objects` across pages for the bounded requested ID set. Preferences updates continue sending `categories` in the PUT body.

# Live Updates Contract: Code Updates and Content/Config Updates

This is the binding contract between the backend, the website, the mobile app (phone and tablet), the publish tooling, and infrastructure for the "Engine: live updates" milestone (`docs/TRELLISKIT.md` section 8). Every team codes against it exactly. It is an engine feature available to every property; nothing here names a property, a domain, a store id, or a key. Examples use the neutral sample property (Example Community, `example.test`, API at `api.example.test`). When the implementation is complete, `REST_API.md` and the Postman collection are updated to match, and this file stays as the design record.

It builds on `docs/ENUMERATION_AND_METADATA.md` (binding: every list is an `EnumerationQuery` in and `EnumerationResult<T>` out, every object carries `labels` and `tags`, no unbounded lists), `docs/TRELLISKIT.md` sections 7 (access models), 8 (this feature's design), and 10 (multi-property: `propertyid` on every table, property resolved on every request), and the Phase 1 and 2 contracts (auth, audit, media pipeline signing, settings, telemetry conventions).

Two layers:

| Layer | What changes | Clients | Sections |
|---|---|---|---|
| Code updates | The mobile app's JavaScript bundle and assets, delivered over the Expo Updates protocol v1, signed, matched to the native runtime | Mobile and tablet (iOS, Android) | 1 to 10 |
| Content and configuration | Feature flags, kill switches, remote strings, banners, remote assets, binary version policy | Mobile, tablet, and web | 11 to 17 |

Conventions are unchanged: routes under `/api/v1`, camelCase JSON, string enums, ISO 8601 UTC timestamps, errors as `{ "error", "message", "fieldErrors" }`, `429` with `Retry-After`. The Expo manifest endpoint is the one exception to the JSON conventions: it speaks the Expo protocol's own headers and multipart format (section 3).

Store policy: Apple Guideline 3.3.1(b) and Google Play policy allow interpreted code and asset updates that do not change the app's primary purpose or add native capabilities. Native changes (new native modules, permissions, SDK upgrades, `app.config.js` changes that alter the fingerprint) always ship through the stores; the runtime version policy (section 5) makes it impossible to send JavaScript to an incompatible binary.

---

## 0. Identifiers, roles, feature gating

### 0.1 Identifier prefixes and target types

| Entity | Prefix | `targetType` |
|---|---|---|
| Update (one platform's update) | `upd_` (the Expo manifest `id` is the update's UUID, section 4.1) | `Update` |
| Update group (the iOS and Android updates of one publish) | `upg_` | `UpdateGroup` |
| Update deploy key | `udk_` | `UpdateDeployKey` |
| Update signing key | `usk_` | `UpdateSigningKey` |
| Install (a device's app installation, pseudonymous) | `uin_` | `UpdateInstall` |
| Update event (client-reported) | `uev_` | `UpdateEvent` |
| Content version | `cve_` | `ContentVersion` |
| Feature flag (including kill switches) | `ffl_` | `FeatureFlag` |
| Remote string override | `rst_` | `RemoteString` |
| Banner (announcement) | `ban_` | `Banner` |
| Remote asset | `ras_` | `RemoteAsset` |

Every object above carries `labels` and `tags` in payloads (always present, `[]` and `{}` when empty) and is filterable by them in its enumeration.

### 0.2 Who may do what

| Action | Who |
|---|---|
| Check for and download code updates, fetch the app manifest, report update events | Anonymous (the app calls before sign-in); property resolved from `X-TK-Property` and `X-TK-App` (section 10.1 of `TRELLISKIT.md`) |
| Publish, roll out, pause, roll back updates | `SystemAdmin` (or `PlatformAdmin`), the break-glass admin token, or an update deploy key with scope `updates:publish` (CI) |
| Manage deploy keys and signing keys | `SystemAdmin` (or `PlatformAdmin`); signing key import requires a member session, never a deploy key |
| Edit and publish flags, kill switches, strings, banners, assets, binary version policy | `SystemAdmin` (or `PlatformAdmin`). Kill switches may also be flipped by members holding the new `ReleaseManager` role (section 13.3) |

### 0.3 Error codes added

| Status | `error` | When |
|---|---|---|
| 400 | `MissingProtocolHeader` | Manifest request without `expo-platform` or `expo-runtime-version` (plain text body, section 3.4) |
| 400 | `UnsupportedPlatform` | `expo-platform` other than `ios` or `android` |
| 400 | `UnknownChannel` | `expo-channel-name` not one of the property's channels |
| 400 | `AssetMismatch` | An uploaded asset's size or SHA-256 differs from what was declared |
| 400 | `UnknownStringKey`, `InvalidStringTokens` | Remote string key not in the engine catalog, or it uses tokens the key does not declare |
| 400 | `InvalidTargetingRule` | Flag rule invalid (`fieldErrors` names the rule and condition) |
| 409 | `UpdateNotDraft`, `UpdateNotPublished` | Lifecycle action not valid for the update's status |
| 409 | `AssetsMissing` | Publish called before every declared asset arrived (`fieldErrors` lists hashes) |
| 409 | `RolloutConflict` | Another update for the same platform, runtime version, and channel is mid-rollout (section 7.2) |
| 409 | `ContentVersionConflict` | Publish of content drafts based on a stale version (`expectedVersion` mismatch) |
| 409 | `FlagKeyInUse` | Flag key not unique in the property |
| 412 | `PreconditionFailed` | `If-Match` on a draft edit does not match the current ETag |
| 426 | `ClientUpdateRequired` | The app binary is below the minimum version (section 15.3) |
| 503 | `SigningUnavailable` | No active signing key for the property; the manifest endpoint refuses to serve unsigned manifests |
| 503 | `FeatureDisabled` | A kill switch blocks this request (section 13) |

---

## 1. Code updates: overview

```text
mobile/  npm run publish-update -- --channel production --message "Fix comment composer"
   1. resolve runtime version per platform and channel (fingerprint)   npx expo-updates runtimeversion:resolve
   2. export bundles and assets                                        npx expo export
   3. POST /api/v1/admin/updates, one multipart/form-data request per platform:
      an "update" JSON part plus one file part per asset (named by its MD5 key);
      the server verifies every hash, stores assets content addressed, and publishes at the rollout %

app launch / foreground
   GET /api/v1/updates/manifest  (expo-* headers, X-TK-Property, X-TK-App, expo-extra-params installid)
   <- multipart/mixed: signed manifest + extensions, or a signed directive
   GET <asset urls>  (CDN, content addressed, immutable)
   verify signature (embedded certificate) and every asset hash; apply on next launch
   POST /api/v1/updates/events  (downloaded, applied, failed)
```

The app uses the `expo-updates` library (`mobile/package.json` already depends on it) with code signing configured; TrellisKit implements the server side of the protocol itself. There is no dependency on Expo's hosted update service.

---

## 2. App configuration (mobile build)

`mobile/app.config.js` sets, from the active property and build profile:

```js
updates: {
  url: `${apiBaseUrl}/api/v1/updates/manifest`,
  enabled: true,
  checkAutomatically: 'ON_LOAD',
  fallbackToCacheTimeout: 0,
  codeSigningCertificate: `${propertyDir}/mobile/updates/certificate.pem`,
  codeSigningMetadata: { keyid: 'main', alg: 'rsa-v1_5-sha256' },
  requestHeaders: {
    'expo-channel-name': '<channel for this build profile>',
    'X-TK-Property': '<property id>',
    'X-TK-App': '<bundle id or package>'
  }
},
runtimeVersion: { policy: 'fingerprint' }
```

| Item | Rule |
|---|---|
| `url` | The property's API origin (`https://<domains.api>` in production; the LAN API address for local release builds, section 18) |
| `checkAutomatically: 'ON_LOAD'` with `fallbackToCacheTimeout: 0` | The app never blocks launch on the network: it launches the newest update already on disk and downloads in the background. Updates apply on the next cold launch (or immediately for critical updates, section 8.3) |
| `codeSigningCertificate` | The property's committed certificate at `<property>/mobile/updates/certificate.pem` (section 6.2). `keyid` is `main` (the only key id until a rotation, section 6.3); the app sends it back in `expo-expect-signature` |
| `requestHeaders` | Static, baked at build time. `expo-channel-name` comes from the build profile: `production` (store builds), `preview` (TestFlight and internal testing), `development` (developer builds). `X-TK-App` is the iOS bundle id or the Android package |
| `runtimeVersion` | `fingerprint` policy (section 5). Because `requestHeaders` (including the channel) are part of the native configuration, the fingerprint differs per channel |
| Install id | On first launch the app creates a random UUID v4, stores it in `expo-secure-store` (`tk.installId`), and calls `Updates.setExtraParamAsync('installid', id)`, so every manifest request carries it in `expo-extra-params`. API calls send it as `X-TK-Install-Id`. A reinstall creates a new id. (Not yet in the built client; open question 13) |

The JavaScript side (`mobile/src/updates/`) owns: the foreground check (section 8.1), critical update prompts, the "Update required" screen (section 15.3), event reporting (section 9.2), and `X-Client-Update-Id` on every API and media request when updates are enabled (`Updates.updateId`, or the literal `embedded` when running the embedded bundle).

Configuration the app reads from `GET /api/v1/property` (property configuration, `property.json` in single-property mode and the property record in multi-property mode, editable in the admin console):

| Field | Shape | Use |
|---|---|---|
| `mobile.minimumBinaryVersion` | Semver string (both platforms) or `{ "ios": "1.2.0", "android": "1.2.0" }`, or absent | Section 15 |
| `mobile.latestBinaryVersion` | Same shape, optional (engine addition by this contract) | Section 15 |
| `mobile.storeUrls` | `{ "ios": "https://apps.apple.com/app/id...", "android": "https://play.google.com/store/apps/details?id=..." }` | Store links on the "Update required" screen |
| `mobile.appStoreId` | String (numeric Apple id) | Native App Store sheet on iOS |
| `updates.checkIntervalMinutes` | Integer 1 to 1440, default 15 | Foreground check throttle (section 8.1) |

---

## 3. Manifest endpoint (Expo Updates protocol v1)

`GET /api/v1/updates/manifest` (Anonymous). Not rate limited per member; limited per install id and per IP (section 19). Never cached by the CDN (the API distribution forwards it to the origin with caching disabled and forwards every request header listed below).

### 3.1 Request headers

| Header | Required | Use |
|---|---|---|
| `expo-protocol-version` | Expected `1` | Protocol version. Missing is treated as `0`: the server answers with the plain JSON manifest format (3.3) and never sends directives |
| `expo-platform` | Yes | `ios` or `android` (`400 UnsupportedPlatform` otherwise) |
| `expo-runtime-version` | Yes | The binary's fingerprint runtime version (section 5) |
| `expo-channel-name` | Yes in TrellisKit | `production`, `preview`, `development`, or a custom channel defined for the property (section 7.4). Missing means `production` and is logged as a client bug |
| `expo-current-update-id` | Sent by the library | UUID of the update the app is running (the embedded update's id on first launch) |
| `expo-embedded-update-id` | Sent by the library | UUID of the update embedded in the binary |
| `expo-recent-failed-update-ids` | When present | Structured field list of update UUIDs that recently failed to launch; the server never offers those again to this install and records a `LaunchFailed` event for each (section 9.2) |
| `expo-expect-signature` | When code signing is configured (always in TrellisKit) | Structured field dictionary, for example `sig, keyid="main", alg="rsa-v1_5-sha256"`. The server signs with the key whose id is `keyid` (section 6) |
| `expo-extra-params` | Expected (open question 13) | Structured field dictionary set by `Updates.setExtraParamAsync`, for example `installid="2f1c..."`. Keys used: `installid` |
| `accept` | Sent by the library | `multipart/mixed` preferred; `application/expo+json` or `application/json` accepted (protocol 0 style single JSON body) |
| `X-TK-Property`, `X-TK-App` | Yes | Property resolution (`TRELLISKIT.md` section 10.1). In single-property mode a missing header resolves to the only property |
| `X-Client-Version` | Optional | The native app version (`expo-application` `nativeApplicationVersion`), recorded on the install for adoption reports. The library does not send it; the app adds it to `requestHeaders` at build time from `app.config.js` `version` |

### 3.2 Selection algorithm

For the resolved property, `platform`, `runtimeVersion`, and `channel`:

1. Candidates are updates with status `Published` (or `Paused` for installs already on them, see 7.2) for exactly that platform, runtime version, and channel, ordered by `createdUtc` descending, then `id`.
2. Skip any update listed in `expo-recent-failed-update-ids` or recorded as failed for this install.
3. Take the first candidate for which the install is inside the rollout (`bucket(updateGroupId, installId) < rolloutPercentage * 100`, section 7.1). An install without an id is inside only 100 percent rollouts.
4. If the selected record is a **rollback to embedded** record (section 7.3): respond with a signed `rollBackToEmbedded` directive, unless the app is already running its embedded update (`expo-current-update-id` equals `expo-embedded-update-id`), in which case respond with `noUpdateAvailable`.
5. If the selected update's UUID equals `expo-current-update-id`: respond with a signed `noUpdateAvailable` directive.
6. If there is no candidate: respond with `noUpdateAvailable`.
7. Otherwise respond with the selected update's signed manifest.

Expo clients only launch an update whose `createdAt` is newer than the one they run (the default selection policy). That is why rollback republishes the previous content as a new update with a new id and a new `createdAt` (section 7.3) instead of serving an old record.

Every request upserts the install's adoption row (section 9.1) after the response is chosen.

### 3.3 Responses

**Protocol 1, update available** (`200`):

```http
HTTP/1.1 200 OK
expo-protocol-version: 1
expo-sfv-version: 0
expo-manifest-filters: channel="production"
expo-server-defined-headers:
cache-control: private, max-age=0
content-type: multipart/mixed; boundary=tk-7f3a9c

--tk-7f3a9c
content-type: application/json; charset=utf-8
content-disposition: form-data; name="manifest"
expo-signature: sig="MEUCIQ...base64...", keyid="main", alg="rsa-v1_5-sha256"

{ ...manifest JSON (section 4.1)... }
--tk-7f3a9c
content-type: application/json
content-disposition: form-data; name="extensions"

{ "assetRequestHeaders": {} }
--tk-7f3a9c--
```

**Protocol 1, directive** (`200`): the same envelope with a single part named `directive`, signed the same way:

```json
{ "type": "noUpdateAvailable" }
```

```json
{ "type": "rollBackToEmbedded", "parameters": { "commitTime": "2026-12-02T18:00:00.000Z" } }
```

`commitTime` is the rollback record's `createdUtc`; clients roll back only when it is newer than the update they run.

| Response header | Value |
|---|---|
| `expo-protocol-version` | `1` |
| `expo-sfv-version` | `0` |
| `expo-manifest-filters` | `channel="<channel>"`: the client discards stored updates whose `metadata.channel` differs, so switching a device between channels never launches another channel's update |
| `expo-server-defined-headers` | Empty dictionary (reserved; TrellisKit does not ask clients to echo headers) |
| `cache-control` | `private, max-age=0` |
| `content-type` | `multipart/mixed; boundary=<random>` |

The `manifest` and `directive` parts carry `expo-signature: sig="<base64 RSA signature>", keyid="<keyid>"`. The signature covers the exact bytes of that part's body (section 6.1). The `extensions` part is not signed and carries `assetRequestHeaders` (always empty in TrellisKit: assets are public, content addressed, and need no headers).

**Protocol 0 or a client that accepts only JSON** (`accept` without `multipart/mixed`): `200` with `content-type: application/expo+json` (or `application/json` when that is all the client accepts), the manifest JSON as the body, and `expo-signature` as a response header. When there is nothing newer, the server returns the current manifest (protocol 0 has no directives); rollback to embedded is not possible for protocol 0 clients and the server logs it.

Responses are generated per request (they depend on the install's rollout bucket) and never stored by intermediaries.

### 3.4 Errors

Errors are plain text (`content-type: text/plain`) because the library surfaces the body in its logs, with the same `error` codes as the JSON API in the first line, for example `400 MissingProtocolHeader: expo-runtime-version is required`. `404 PropertyNotFound` for an unknown or disabled property; `503 SigningUnavailable` when the property has no active signing key (the app keeps running its current update); `429 RateLimited` with `Retry-After`. All errors are counted (section 20).

---

## 4. Manifests and assets

### 4.1 Manifest JSON

```json
{
  "id": "6f0b6c1e-3a1d-4f3e-9a55-1b0d6a0a7c21",
  "createdAt": "2026-12-01T17:00:00.000Z",
  "runtimeVersion": "a4c7f2e9d1b3c5a7e9f1b3d5c7a9e1f3b5d7c9e1",
  "launchAsset": {
    "hash": "pYvP7fMk6hCq1kGx0s3Vq6m8bXo2c9aT1uW4yZ7dE0Q",
    "key": "3b1f0c4e9a7d2b6c8e0f1a3c5e7d9b2f",
    "contentType": "application/javascript",
    "fileExtension": ".bundle",
    "url": "https://api.example.test/updates/assets/example/a58bcfedf324e8...c3d74b1"
  },
  "assets": [
    {
      "hash": "Q2xn1Zb3...",
      "key": "9c2e4f6a8b0d1e3f5a7c9e1b3d5f7a9c",
      "contentType": "image/png",
      "fileExtension": ".png",
      "url": "https://api.example.test/updates/assets/example/436c67d5..."
    }
  ],
  "metadata": { "channel": "production", "updateGroup": "upg_...", "critical": "false" },
  "extra": {
    "expoClient": { "...": "public Expo config (the expoConfig captured at publish)" },
    "critical": false,
    "criticalMessage": null,
    "trelliskit": {
      "updateId": "upd_...",
      "updateGroupId": "upg_...",
      "message": "Fix comment composer",
      "gitCommit": "4e1d2c9",
      "publishedUtc": "2026-12-01T17:05:00.000Z"
    }
  }
}
```

| Field | Rule |
|---|---|
| `id` | UUID v4 generated at publish; stable for the update's life. The update's `upd_` id is TrellisKit's own handle and is carried in `extra.trelliskit.updateId` |
| `createdAt` | The update's `createdUtc` with millisecond precision. Strictly increasing per (property, platform, runtime version, channel): if two publishes collide, the later gets `previous + 1 ms` |
| `runtimeVersion` | Equals the request's `expo-runtime-version` |
| `launchAsset` | The JavaScript (Hermes bytecode) bundle. `contentType` is always `application/javascript` and `fileExtension` `.bundle` |
| `assets` | Every other asset of the export for this platform. Bounded by `updates.maxAssetsPerUpdate` (2000) |
| `hash` | Base64url (no padding) SHA-256 of the asset bytes; the library verifies it after download |
| `key` | The asset's MD5 hex as produced by `expo export` (the library's cache key) |
| `url` | `{updates.assetBaseUrl}/{propertyId}/{sha256 hex}`. Absolute |
| `metadata` | String to string only. `channel` drives `expo-manifest-filters`; `critical` (`"true"` or `"false"`) mirrors `extra.critical` for clients that read metadata |
| `extra.expoClient` | The public app config captured at publish, so `Constants.expoConfig` reflects the update |
| `extra.critical`, `extra.criticalMessage` | Critical update marker read by the app (section 8.3). The app treats the update as critical when `extra.critical` is `true` or `metadata.critical` is `"true"` |
| `extra.trelliskit` | TrellisKit fields read by the app (About screen) |

### 4.2 Asset delivery

`GET /api/v1/updates/assets/{propertyId}/{sha256Hex}` (Anonymous) serves an asset in local mode; in AWS the URL host is the CDN and the path is served from the `updates` bucket (section 17). Rules for both:

- Content addressed and immutable: `cache-control: public, max-age=31536000, immutable`, `ETag: "<sha256 hex>"`, the stored `content-type`, `x-content-type-options: nosniff`.
- Public: bundles and assets are not secret (anyone can extract them from a store binary). Nothing secret may be placed in a bundle; `EXPO_PUBLIC_*` values are public by definition.
- `Range` requests are supported (`206`), and gzip or brotli are applied by the CDN for `application/javascript`.
- Unknown hash: `404`.

---

## 5. Runtime version policy

- `runtimeVersion: { policy: 'fingerprint' }`: the runtime version is a hash of the native layer (native dependencies, config plugins, `app.config.js` native fields, native project files) computed by `@expo/fingerprint`.
- Builds compute it at build time; the publish tool computes it with `npx expo-updates runtimeversion:resolve --platform <ios|android>` (JSON output, `runtimeVersion` field) from the same commit, `TK_PROPERTY_DIR`, and channel, so a publish targets exactly the binaries built from a compatible native layer.
- The fingerprint includes the static `updates.requestHeaders`, so **it differs per channel**: `preview` and `production` binaries of the same commit have different runtime versions. An update is always exported and published per channel (section 7.4); the server never copies an update across channels.
- Because the property selects icons, bundle ids, and other native values, each property's binaries have their own runtime versions; updates never cross properties (they are also scoped by `propertyid`).
- The publish tool refuses to publish when the working tree has native changes not reflected in any store build (it compares the resolved runtime version against the `runtimeVersions` known to the server, section 7.5) unless `--allow-new-runtime` is passed (for a binary that is in review).
- The admin console shows, per runtime version, how many installs report it, so operators see when an old runtime version has drained and can stop publishing to it.

---

## 6. Code signing

### 6.1 Scheme

- Algorithm `rsa-v1_5-sha256` (RSASSA-PKCS1-v1_5 with SHA-256), RSA 2048 or 4096 bit keys.
- The server signs the exact UTF-8 bytes of the `manifest` or `directive` part body and sends `expo-signature: sig="<base64 signature>", keyid="<keyid>"` (structured field dictionary).
- The app embeds the certificate (`codeSigningCertificate`) and rejects any manifest or directive whose signature is missing, malformed, made with another key, or does not verify. It also rejects assets whose hash does not match the signed manifest, so the CDN and storage are not trusted.
- `keyid` selects the key: the server signs with the active key whose `keyId` equals the `keyid` in `expo-expect-signature`. An unknown `keyid` gets `503 SigningUnavailable` (the app keeps its current update), never an unsigned manifest.

### 6.2 Keys and certificates per property

| Item | Where |
|---|---|
| Private key (PKCS#8 PEM) | Secrets Manager, one secret per property and environment: `<propertyId>/<env>/updates-signing-key`, passed to the server as `TK_UPDATES_SIGNING_KEY` (single-property) or stored encrypted in the property's secret store (multi-property, `TRELLISKIT.md` section 10.4). Never in the repository, never in an image, never in logs |
| Certificate (X.509 PEM, self-signed, `codeSigning` extended key usage, 10 year validity) | Committed to the property folder: `<property>/mobile/updates/certificate.pem`, embedded in every build of that property |
| Key id | `main` for every property (the value in `app.config.js` `codeSigningMetadata.keyid`). A rotation introduces a second id (section 6.3) |

Generation (once per property, by an operator, on a trusted machine):

```bash
cd mobile
npm run updates:keys -- --property ../samples/example --common-name "Example Community updates"
```

The script wraps `npx expo-updates codesigning:generate` and writes the certificate to `<property>/mobile/updates/certificate.pem` and the private key to `~/.trelliskit/keys/<propertyId>/` (outside the repository; the developer default). For production the operator stores that private key in Secrets Manager (`aws secretsmanager put-secret-value`, surfaced to the server as `TK_UPDATES_SIGNING_KEY`) and deletes the local copy. The sample property's committed certificate is a development certificate whose private key is generated locally per developer (section 18).

### 6.3 Server key registry

`UpdateSigningKey` records (no private material in API payloads):

```json
{
  "id": "usk_...", "keyId": "main", "algorithm": "rsa-v1_5-sha256", "keyBits": 2048,
  "source": "Environment", "certificateSha256": "4f2a...", "certificateNotAfterUtc": "2036-12-01T00:00:00Z",
  "status": "Active", "lastUsedUtc": "2026-12-02T18:00:00Z", "labels": [], "tags": {}, "createdUtc": "..."
}
```

| Route | Who | Notes |
|---|---|---|
| `POST /api/v1/admin/updates/signing-keys/enumerate` | Admin | `EnumerationResult<UpdateSigningKey>` |
| `POST /api/v1/admin/updates/signing-keys` | Admin, member session only | `{ keyId, certificatePem, privateKeyPem? }`. With `privateKeyPem` (multi-property mode), the key is encrypted at rest (AES-256-GCM with the engine secret key) and write-only; without it, `source: "Environment"` and the server reads `TK_UPDATES_SIGNING_KEY`. The server verifies the key matches the certificate (`400 ValidationFailed` otherwise). Audited `updates.signingKey.create` |
| `POST /api/v1/admin/updates/signing-keys/{id}/retire` | Admin | `status: "Retired"`; manifests requested with that `keyid` get `503 SigningUnavailable`. Audited |

Rotation: generate a key with a new id (for example `main2`), register it, ship store binaries embedding its certificate and `keyid`, keep signing `main` requests until installs on `main` binaries drain (the adoption page shows installs by `keyid`), then retire `main`. The built client has `keyid: "main"` fixed, so rotation needs an `app.config.js` change (open question 14). A compromised key is retired immediately; affected binaries stop receiving updates until users install a new binary, which is the safe failure.

Startup check: when updates are enabled and a property has no active signing key, the server logs an error once and `GET /api/v1/admin/updates/health` reports `signing: "Missing"`; it still starts.

---

## 7. Channels, rollout, rollback, critical updates

### 7.1 Staged rollout

- Each update group has `rolloutPercentage` (0.01 to 100, two decimals). The iOS and Android updates of a group share it unless set per platform.
- Bucketing: `bucket = (first 4 bytes of SHA-256(UTF-8 "<updateGroupId>:<installId>") as an unsigned big-endian integer) mod 10000`. An install is in the rollout when `bucket < rolloutPercentage * 100`. Deterministic per install and per group, independent across groups.
- Raising the percentage only adds installs; lowering it removes installs that have not downloaded yet (installs already on the update keep it, because the next selection still finds it, see 7.2).
- `Paused`: the update stops being offered to installs that do not run it; installs already on it keep it (they are not rolled back). Resume restores `Published`.

### 7.2 One active rollout per target

For one (property, platform, runtime version, channel) at most one update may be below 100 percent. Publishing a newer update while an older one is mid-rollout returns `409 RolloutConflict` unless `supersede: true` is sent, which completes the older one at its current percentage (installs outside it keep receiving the update before it, which is still `Published`). This keeps the selection in 3.2 well defined: the newest update reaches its bucket, everyone else gets the newest fully rolled out update.

### 7.3 Rollback

| Action | Route | Effect |
|---|---|---|
| Roll back to a previous update | `POST /api/v1/admin/update-groups/{id}/rollback` `{ "targetUpdateGroupId": "upg_old", "rolloutPercentage": 100, "message": "Roll back comment composer" }` | Marks the current group `RolledBack` (no longer offered) and creates a new group that republishes the target group's assets and `expoConfig` with new UUIDs and a new `createdAt` (so clients accept it), `kind: "Republish"`, `republishOf: "upg_old"`. Installs pick it up on their next check |
| Roll back to the embedded bundle | `POST /api/v1/admin/update-groups/rollback-to-embedded` `{ "platform": "ios" \| "android" \| "all", "runtimeVersion", "channel", "rolloutPercentage": 100, "message" }` | Creates a group of `kind: "RollBackToEmbedded"` records; selection answers with the `rollBackToEmbedded` directive (3.2 step 4). Publishing any later update supersedes it |

Both are audited (`updates.rollback`, `updates.rollbackToEmbedded`) and appear in the update history. A rolled back group can be republished the same way (rollback is itself undoable by "rolling back" to the rolled back group).

### 7.4 Channels

- Every property has the built-in channels `production`, `preview`, and `development`. A `SystemAdmin` may add custom channels (`^[a-z][a-z0-9-]{1,31}$`, at most `updates.maxChannels` (10) per property, for example `staff`) through `POST /api/v1/admin/updates/channels`; builds opt into a custom channel only through their build profile. Channels are enumerable: `POST /api/v1/admin/updates/channels/enumerate` returns `{ name, builtIn, latestByPlatform: { ios: UpdateSummary|null, android: UpdateSummary|null }, activeInstalls30d, labels, tags }`.
- Promotion between channels is a publish, not a server-side copy: the fingerprint runtime version differs per channel (the channel header is part of the native configuration, section 5), so an update exported for `preview` binaries cannot be served to `production` binaries. The normal flow is to publish a commit to `preview`, test on TestFlight and internal testing builds, then run the CLI again for the same commit with `--channel production` at a low percentage. Assets are content addressed, so the second publish uploads only what the store lacks. The group records `promotedFrom` when the CLI passes `--promoted-from <groupId>`.

### 7.5 Minimum and known runtime versions

`POST /api/v1/admin/updates/runtime-versions/enumerate` returns per (platform, runtime version): `{ platform, runtimeVersion, firstSeenUtc, lastSeenUtc, activeInstalls30d, latestUpdate: UpdateSummary|null, binaryVersions: { objects (max 10), totalRecords } }`, built from adoption rows. The publish tool uses it to warn about unknown runtime versions (section 5).

### 7.6 Critical updates

An update group published with `critical: true` (and optional `criticalMessage`, up to 200 characters) is marked in `extra.critical` (and `metadata.critical: "true"`). Behavior is in section 8.3. Critical never forces a reload without the member's tap, so it never discards unsaved input.

---

## 8. Apply behavior in the app

### 8.1 Checks

| When | Action |
|---|---|
| Cold launch | The library checks automatically (`ON_LOAD`) and downloads in the background without blocking launch |
| Return to foreground | If at least `updates.checkIntervalMinutes` (15, read from `GET /api/v1/property`, section 2) passed since the last check, call `Updates.checkForUpdateAsync()`, then `Updates.fetchUpdateAsync()` when one is available |
| Development builds (`__DEV__` or `expo-dev-client`) | No checks (the library is disabled there) |

Downloaded updates apply on the next cold launch. The app reads state from `Updates.useUpdates()` (`isUpdatePending`, `downloadedUpdate`, `checkError`, `downloadError`).

### 8.2 Failure handling

- The library's error recovery rolls back to the previous working update when a new update crashes on launch, and reports its id in `expo-recent-failed-update-ids` next time; the server excludes it for that install (3.2 step 2) and the app reports a `LaunchFailed` event when it can.
- Network or signature failures leave the running update in place and are reported as `CheckFailed` or `DownloadFailed` events with a bounded error code (section 9.2).

### 8.3 Critical updates

When `downloadedUpdate.manifest.extra.critical` is true (or `metadata.critical` is `"true"`), the app shows a non-dismissible banner (phone) or top bar (tablet): `criticalMessage` or the string `updates.criticalPrompt` ("An important update is ready."), with a "Restart now" button that calls `Updates.reloadAsync()`. It is shown on the screen the member is on, never over a composer with unsaved text (the app defers it until the composer closes, at most 10 minutes). Non-critical downloaded updates show nothing.

### 8.4 About screen

Account, then About shows: app version (native), update id and message (or "Built-in"), channel, runtime version (first 8 characters), and the last check time, with a "Check for updates" button that runs the 8.1 foreground flow immediately. Support tickets (Phase 5) attach these values.

---

## 9. Adoption telemetry

### 9.1 Install rows (from manifest requests)

Each manifest request upserts `updateinstalls` keyed by (`propertyid`, `installidhash`) where `installidhash` is HMAC-SHA256 of the install id with a key derived from `auth.tokenEncryptionKey` (HKDF, info `tk-updates-install`). Raw install ids are never stored. Columns: platform, channel, runtime version, current update UUID, embedded update UUID, `keyid`, native app version (`X-Client-Version`), first seen, last check. Rows not seen for `updates.installRetentionDays` (90) are deleted. Signed-in API calls do not link installs to members; adoption is device based by design.

Adoption of an update = installs whose `currentupdateid` is that update's UUID and whose last check is within 30 days.

### 9.2 Client events

`POST /api/v1/updates/events` (Anonymous, property headers and `X-TK-Install-Id` required)

```json
{ "events": [
  { "type": "Downloaded", "updateId": "6f0b6c1e-...", "occurredUtc": "2026-12-01T17:20:00Z" },
  { "type": "Applied", "updateId": "6f0b6c1e-...", "occurredUtc": "2026-12-01T19:02:00Z", "launchDurationMs": 840 },
  { "type": "DownloadFailed", "updateId": "6f0b6c1e-...", "errorCode": "HashMismatch", "occurredUtc": "..." }
] }
```

| Field | Rule |
|---|---|
| `events` | 1 to 20 |
| `type` | `CheckFailed`, `Downloaded`, `DownloadFailed`, `Applied`, `LaunchFailed`, `RolledBackToEmbedded`, `CriticalPromptShown`, `CriticalRestarted` |
| `errorCode` | Bounded: `Network`, `SignatureInvalid`, `HashMismatch`, `Storage`, `Timeout`, `Server`, `Unknown` |
| `occurredUtc` | Not more than 7 days old or 5 minutes in the future (dropped otherwise) |

Returns `202`. Events are stored in `updateevents` for `updates.eventRetentionDays` (30) and aggregated per update into `updateeventcounts` (by type, by day). Rate limited per install (section 19). The app queues events in storage and sends them in batches on foreground.

### 9.3 Admin adoption views

`GET /api/v1/admin/update-groups/{id}/adoption?days=14` and `POST /api/v1/admin/update-groups/{id}/adoption/enumerate` return `EnumerationResult<UpdateAdoptionDay>` with scalar `days` and `byPlatform` context. POST selects the window with `filters.days` (1 to 90). `objects` holds day records `{ date, checks, downloads, applied, failures, labels, tags }`. Binary-version counts use `GET /api/v1/admin/update-groups/{id}/adoption/binary-versions` and `POST /api/v1/admin/update-groups/{id}/adoption/binary-versions/enumerate`, returning `EnumerationResult<UpdateCountRow>` with exact grouped totals across platforms. Signing-key install counts also use GET plus POST `/api/v1/admin/updates/signing-keys/installs/enumerate`. Every form obeys standard paging/search/date/metadata filters.

---

## 10. Publish API and CLI

### 10.1 Credentials

- Member session with `SystemAdmin`, or the break-glass token, or a deploy key: `Authorization: Bearer tkdk_<secret>`. Deploy keys (`UpdateDeployKey`) are created in the console (`POST /api/v1/admin/updates/deploy-keys` `{ name, channels: ["preview"], expiresUtc?, labels?, tags? }` returns the secret once; stored as a SHA-256 hash; `POST /api/v1/admin/updates/deploy-keys/enumerate`; `POST .../{id}/revoke`). A deploy key may only create and publish update groups on its listed channels, never roll back to embedded, manage keys, or call any other admin route. Every use is audited with the key name.

### 10.2 Upload and publish (one request per platform)

`POST /api/v1/admin/updates` with `Content-Type: multipart/form-data`, `Authorization: Bearer <member access token, admin token, or deploy key>`, `X-TK-Property`, and `X-TK-App`. This is the route the built mobile publish tool calls.

| Part | `name` | Content |
|---|---|---|
| 1 (first) | `update` | `application/json`: the update descriptor (10.3) |
| 2 to n | the asset's `key` (MD5 hex) | The asset bytes, one part per asset including the launch asset, in any order, with the part `Content-Type` equal to the asset's `contentType` |

Server behavior, in order:

1. Authenticate; a deploy key must list the `channel` (`403 Forbidden`).
2. Parse and validate the descriptor (`400 ValidationFailed`, `400 UnknownChannel`, `400 UnsupportedPlatform`).
3. Stream every file part to a temporary object while computing SHA-256 and the size; a part whose SHA-256 (base64url) differs from the descriptor's `hash` for that `key` fails the request with `400 AssetMismatch` (`fieldErrors["assets[<index>]"]`). Parts are never buffered whole in memory.
4. Assets whose SHA-256 is already stored for the property may be omitted from the request (the tool sends `HEAD /api/v1/admin/updates/assets/{sha256Hex}` first and skips stored ones; `200` stored, `404` missing). Any declared asset that is neither stored nor uploaded fails with `409 AssetsMissing` listing the missing keys.
5. Move verified assets to their content-addressed keys (`updates/assets/{propertyId}/{sha256 hex}`); existing identical objects are not rewritten.
6. Create the update and its group (10.4), assign a UUID and `createdAt` (the descriptor's `createdUtc` when it is later than every existing update for the same platform, runtime version, and channel, otherwise the previous `createdAt` plus 1 ms), and publish it at `rolloutPercentage`. `rolloutPercentage: 0` stores it as `Draft` (publish later from the console or with `POST /api/v1/admin/update-groups/{id}/publish`).
7. Refuse to publish when the property has no active signing key (`503 SigningUnavailable`), so nothing unsignable is ever live; the upload is kept as `Draft`.

Success `201 UpdateGroup`. Errors: `400 ValidationFailed`, `400 AssetMismatch`, `401 Unauthorized`, `403 Forbidden`, `409 AssetsMissing`, `409 RolloutConflict`, `413 TooLarge` (above `updates.maxUploadMegabytes`), `503 SigningUnavailable`.

**Grouping platforms.** The descriptor may carry `updateGroupId` (returned by the first platform's response). When absent (the current tool), the server joins the update to an existing `Draft` or just-published group with the same `channel`, `gitCommit`, and `message`, the other platform, and a `createdUtc` within 10 minutes; otherwise it creates a new group. The group's rollout and critical flag come from the first update and later requests may not change them (`409 RolloutConflict` if they differ).

**Proxies.** The request can be hundreds of megabytes: the local nginx config sets `client_max_body_size` for `/api/v1/admin/updates` to `updates.maxUploadMegabytes` and disables request buffering for that location; in AWS the ALB has no body limit and the CloudFront behavior for `/api/v1/admin/*` allows `POST` bodies of that size.

### 10.3 Update descriptor (the `update` part)

```json
{
  "platform": "ios",
  "runtimeVersion": "a4c7f2e9d1b3c5a7e9f1b3d5c7a9e1f3b5d7c9e1",
  "channel": "preview",
  "message": "Fix comment composer",
  "critical": false,
  "criticalMessage": null,
  "rolloutPercentage": 100,
  "gitCommit": "4e1d2c9a7f",
  "gitDirty": false,
  "createdUtc": "2026-12-01T17:00:00.000Z",
  "updateGroupId": null,
  "expoConfig": { "...": "npx expo config --type public --json output" },
  "launchAsset": { "key": "3b1f0c4e9a7d2b6c8e0f1a3c5e7d9b2f", "hash": "pYvP7fMk6hCq1kGx0s3Vq6m8bXo2c9aT1uW4yZ7dE0Q", "contentType": "application/javascript", "fileExtension": ".bundle" },
  "assets": [ { "key": "9c2e4f6a8b0d1e3f5a7c9e1b3d5f7a9c", "hash": "Q2xn1Zb3...", "contentType": "image/png", "fileExtension": ".png" } ],
  "labels": [],
  "tags": {}
}
```

| Field | Rule |
|---|---|
| `platform` | `ios` or `android` |
| `runtimeVersion` | The fingerprint for this platform and channel (section 5), 1 to 128 characters |
| `channel` | A property channel |
| `message` | 1 to 500 characters |
| `critical`, `criticalMessage` | Optional; `criticalMessage` up to 200 characters (engine addition; the built tool sends only `critical`) |
| `rolloutPercentage` | 0 to 100, up to two decimals; default 100 for `preview` and `development`, 10 for `production` when omitted |
| `gitCommit` | 7 to 40 hex characters, optional; `gitDirty` optional |
| `createdUtc` | Optional client timestamp (see step 6) |
| `updateGroupId` | Optional (see grouping) |
| `expoConfig` | The public Expo config; stored as an opaque document (like Phase 1 raw webhook payloads, the one intentional opaque column; never queried), at most 256 KB, served as `extra.expoClient` |
| `launchAsset`, `assets` | `key` (MD5 hex, also the file part name), `hash` (SHA-256 base64url, no padding), `contentType`, `fileExtension`. Keys unique within the update; at most `updates.maxAssetsPerUpdate` (2000) assets, each at most `updates.maxAssetMegabytes` (50) |
| `labels`, `tags` | Optional metadata on the group |

### 10.3.1 Lifecycle routes

| Method and path | Body | Success | Errors |
|---|---|---|---|
| `HEAD /api/v1/admin/updates/assets/{sha256Hex}` | | `200` stored, `404` missing (no body) | |
| `POST /api/v1/admin/update-groups/{id}/publish` | `{ "rolloutPercentage": 10, "supersede": false }` | `200 UpdateGroup` (`Published`) | `409 UpdateNotDraft`, `409 RolloutConflict`, `503 SigningUnavailable` |
| `POST /api/v1/admin/update-groups/{id}/rollout` | `{ "rolloutPercentage": 50, "platform"?: "ios" }` | `200 UpdateGroup` | `409 UpdateNotPublished` |
| `POST /api/v1/admin/update-groups/{id}/pause`, `.../resume` | `{ "reason"? }` | `200 UpdateGroup` | |
| `POST /api/v1/admin/update-groups/{id}/critical` | `{ "critical": true, "criticalMessage"? }` | `200 UpdateGroup`. Applies to installs that have not yet downloaded it (the flag is part of the signed manifest) | |
| `POST /api/v1/admin/update-groups/{id}/rollback` | Section 7.3 | `201 UpdateGroup` (the republish) | |
| `POST /api/v1/admin/update-groups/rollback-to-embedded` | Section 7.3 | `201 UpdateGroup` | |
| `DELETE /api/v1/admin/update-groups/{id}` | | `204` (Drafts only; published history is permanent) | `409 UpdateNotDraft` |
| `POST /api/v1/admin/update-groups/enumerate` (and `GET`) | `EnumerationQuery`; orderings `CreatedDescending` (default), `CreatedAscending`; `filters`: `platform`, `channel`, `runtimeVersion`, `status`, `kind`, `critical`, `gitCommit`; `search` over message | `200 EnumerationResult<UpdateGroup>` | |
| `GET /api/v1/admin/update-groups/{id}` | | `200 UpdateGroup` with `updates` (one per platform, fixed bound 2) and `assets: { objects (max 20), totalRecords, enumerate }` | |
| `POST /api/v1/admin/updates/{id}/assets/enumerate` | `EnumerationQuery` | `200 EnumerationResult<{ hash, key, contentType, fileExtension, sizeBytes, isLaunchAsset }>` | |
| `GET /api/v1/admin/updates/health` | | `200 { enabled, signing, activeKeyId, signingProblem, storage, storageMode, rolloutPlanTemplate }`; latest releases are in the channel enumeration | |

### 10.4 Update shapes

```json
{
  "id": "upg_...",
  "kind": "Publish",
  "channel": "production",
  "message": "Fix comment composer",
  "status": "Published",
  "rolloutPercentage": 25,
  "critical": false,
  "criticalMessage": null,
  "gitCommit": "4e1d2c9", "gitDirty": false,
  "republishOf": null, "promotedFrom": null,
  "platforms": ["ios", "android"],
  "updates": [ { "id": "upd_...", "uuid": "6f0b6c1e-...", "platform": "ios", "runtimeVersion": "a4c7f2e9...", "createdAt": "2026-12-01T17:00:00.000Z", "launchAssetSizeBytes": 2841932, "assetCount": 118, "totalBytes": 9812331, "rolloutPercentage": 25 } ],
  "createdBy": { "type": "DeployKey", "name": "ci-preview" },
  "publishedUtc": "2026-12-01T17:05:00Z", "pausedUtc": null, "rolledBackUtc": null,
  "adoption": { "activeInstalls": 412, "onThisUpdate": 96 },
  "labels": [], "tags": {}, "createdUtc": "2026-12-01T17:00:00Z", "updatedUtc": "2026-12-01T17:05:00Z"
}
```

`status`: `Draft`, `Published`, `Paused`, `RolledBack`, `Superseded` (a newer group reached 100 percent on every target of this group). `kind`: `Publish`, `Republish`, `RollBackToEmbedded`.

### 10.5 CLI

`npm run publish-update -- [options]` in `mobile/` (Node, no Expo account needed):

| Option | Default | Meaning |
|---|---|---|
| `--channel <name>` | required | Target channel |
| `--message "<text>"` | required | Update message |
| `--platform ios\|android\|all` | `all` | Platforms to export and publish |
| `--rollout <percent>` | `100` for `preview` and `development`, `10` for `production` | Initial rollout (`0` uploads as a Draft) |
| `--critical` | off | Section 7.6 |
| `--promoted-from <groupId>` | none | Records that this publish repeats a group tested on another channel (section 7.4) |
| `--supersede`, `--allow-new-runtime`, `--label`, `--tag k=v`, `--dry-run` | off | Engine additions by this contract (sections 5, 7.2); the built tool may add them later |
| `--api <origin>` | `TK_UPDATES_API_URL`, else the property's `https://<domains.api>` | API origin |

Credentials: a bearer token (`TK_UPDATES_DEPLOY_KEY` in CI, else `TK_ADMIN_TOKEN` or a member access token) plus `X-TK-Property` and `X-TK-App` from the property. Steps: resolve `TK_PROPERTY_DIR`; for each platform resolve the runtime version for the target channel's native config (`npx expo-updates runtimeversion:resolve --platform <p>` with the channel set as the build does); run `npx expo export --platform <p> --output-dir dist/updates` and read `dist/updates/metadata.json` (`fileMetadata.<platform>.bundle` and `.assets[]` with `path` and `ext`); run `npx expo config --type public --json` for `expoConfig`; hash every file (MD5 hex `key`, SHA-256 base64url `hash`); send one `POST /api/v1/admin/updates` multipart request per platform; print the group id, the console URL, and the rollout. Exit codes: 0 success, 2 validation, 3 upload, 4 publish conflict, 5 auth. CI (`.github/workflows/`) may run it on tags `mobile-update-*` with a deploy key limited to `preview`; publishing to `production` is a separate, deliberate run.

---|---|---|
| `--channel <name>` | required | Target channel |
| `--message "<text>"` | required | Update message |
| `--platform ios\|android\|all` | `all` | Platforms to export and publish |
| `--rollout <percent>` | `100` for `preview` and `development`, `10` for `production` | Initial rollout |
| `--critical`, `--critical-message "<text>"` | off | Section 7.6 |
| `--draft` | off | Create and upload, do not publish |
| `--supersede` | off | Section 7.2 |
| `--allow-new-runtime` | off | Section 5 |
| `--label <l>` (repeatable), `--tag k=v` (repeatable) | none | Metadata on the group |
| `--api <origin>` | `TK_UPDATES_API_URL`, else the property's `https://<domains.api>` | API origin |
| `--dry-run` | off | Export and print the plan without calling the API |

Credentials: `TK_UPDATES_DEPLOY_KEY` (CI) or `TK_ADMIN_TOKEN`. Steps: resolve `TK_PROPERTY_DIR`; for each platform run `npx expo-updates runtimeversion:resolve --platform <p>`; run `npx expo export --platform ios --platform android --output-dir dist/updates` and read `dist/updates/metadata.json` (`fileMetadata.<platform>.bundle` and `.assets[]` with `path` and `ext`); run `npx expo config --type public --json`; hash every file (SHA-256 base64url for `hash`, MD5 hex for `key`, SHA-256 hex for storage); create the group; upload missing assets with up to 6 parallel PUTs and retries; publish; print the group id, the console URL, and the rollout. Exit codes: 0 success, 2 validation, 3 upload, 4 publish conflict, 5 auth. CI (`.github/workflows/`) may run it on tags `mobile-update-*` with a deploy key limited to `preview`; promotion to `production` stays a console action.

---

## 11. Content and configuration manifest

### 11.1 Route

`GET /api/v1/app/manifest?since=<contentVersion>` (Anonymous or signed in; property resolved per request; mobile, tablet, and web). Request headers used for targeting: `X-Client` (`web`, `mobile`), `X-Client-Version` (app version; web sends its build version), `X-Client-Update-Id`, `X-TK-Install-Id` (mobile) or `X-TK-Visitor-Id` (web: a random id kept in `localStorage`, regenerated when storage is unavailable), `X-Client-Platform` (`ios`, `android`, `web`), `X-Client-Channel` (mobile builds send their update channel), and the access token when signed in. Supports `If-None-Match` (the response `ETag` covers the content version and the evaluation result) and returns `304` when nothing changed for this caller.

```json
{
  "contentVersion": 42,
  "publishedUtc": "2026-12-02T10:00:00Z",
  "fullRefresh": false,
  "since": 40,
  "evaluatedFor": { "platform": "ios", "appVersion": "1.3.0", "updateId": "6f0b6c1e-...", "signedIn": true, "membership": "Active" },
  "flags": { "forums.pollsEnabled": true, "feed.composerVersion": "v2", "media.maxUploadsHint": 10 },
  "killSwitches": { "forums": { "killed": false }, "messaging": { "killed": true, "message": "Messaging is paused while we fix a problem." } },
  "strings": { "upserts": { "mobile.feed.emptyTitle": "Nothing here yet" }, "deletes": ["web.home.tagline"] },
  "banners": { "objects": [ { "...": "Banner (section 14.1)" } ], "deletes": ["ban_old"] },
  "assets": { "upserts": [ { "...": "RemoteAsset descriptor (section 14.2)" } ], "deletes": ["ras_old"] },
  "binary": { "minimumVersion": "1.2.0", "latestVersion": "1.4.0", "updateRequired": false, "updateRecommended": true, "storeUrl": "https://apps.apple.com/app/id0000000000" },
  "checkIntervalMinutes": 15,
  "updatesCheckIntervalMinutes": 15,
  "property": { "...": "public property config (GET /api/v1/property), only when changed since `since`" }
}
```

| Field | Rule |
|---|---|
| `contentVersion` | Monotonic integer per property, incremented by every content publish (section 16) |
| `since` | Echo of the request. Without `since`, everything is returned as upserts (a full snapshot) |
| `fullRefresh` | True when `since` is older than the retained change log or more than `appConfig.maxDeltaItems` (500) items changed; then `strings`, `banners`, and `assets` are omitted and the client re-enumerates them (11.2) and replaces its cache. Also true without `since` when the snapshot exceeds 500 items |
| `flags` | Every non-kill-switch flag, **resolved for this caller** (section 12.3) to its value. Always complete (not a delta), bounded by `appConfig.maxFlags` (200). Flags the caller has no matching rule for get their default value |
| `killSwitches` | Every kill switch feature key resolved for this caller: `{ killed, message? }`. Always complete, bounded by the engine's feature key list |
| `strings` | Delta of remote string overrides for this client type (`mobile.*`, `web.*`, `shared.*`) |
| `banners` | Active banners for this caller after targeting (always complete, bounded by `appConfig.maxActiveBanners` (20)) plus `deletes` of banners the client may have cached |
| `assets` | Delta of remote asset descriptors |
| `binary` | Mobile only (section 15.3); null on web |
| `checkIntervalMinutes` | How often the client re-fetches this manifest while in the foreground |
| `updatesCheckIntervalMinutes` | Echo of `updates.checkIntervalMinutes` from the property configuration (the client reads it from `GET /api/v1/property`) |
| `property` | Present when the property configuration changed after `since` (so clients refresh `GET /api/v1/property` data without a separate call) |

`Cache-Control: private, max-age=0` (responses depend on the caller). The route is cheap: content is cached in memory per property and content version; only flag and banner evaluation runs per request.

### 11.2 Re-enumeration routes (for `fullRefresh` and admin previews)

`POST /api/v1/app/strings/enumerate`, `POST /api/v1/app/banners/enumerate`, `POST /api/v1/app/assets/enumerate` (Anonymous, same headers) with `EnumerationQuery` (member maximum) return the current published objects visible to the caller, with `filters.client` (`mobile`, `web`) for strings. Each response's objects carry the `contentVersion` they belong to, and the result adds `contentVersion` at the top level so the client can record it.

### 11.3 Client behavior

| Moment | Mobile and tablet | Web |
|---|---|---|
| First launch offline | Start from the manifest bundled at build time (`mobile/assets/app-manifest.json`, written by the build from the published content) | Start from the copy inlined in the build (`www` build step) |
| Launch, foreground, sign-in, sign-out, membership change | Fetch with `since` = cached version (throttled to `checkIntervalMinutes` except on sign-in, sign-out, and membership change, which always refetch because evaluation changes) | Same, on load, tab focus, and the same account events |
| Storage | Cached manifest in `expo-file-system` (JSON), assets by hash in the cache directory | `localStorage` for the manifest (wrapped in try and catch), Cache API for assets |
| Apply | Flags and kill switches apply immediately (screens re-render); strings apply on the next render of each screen; banners and assets as they arrive | Same |

A `503 FeatureDisabled` from any API route also updates the cached kill switch for that feature immediately (section 13.2), so the client reacts before the next manifest check.

---

## 12. Feature flags and targeting rules

### 12.1 FeatureFlag

```json
{
  "id": "ffl_...",
  "key": "feed.composerVersion",
  "name": "Composer version",
  "description": "Which composer the feed uses.",
  "kind": "Variant",
  "valueType": "String",
  "variants": ["v1", "v2"],
  "defaultValue": "v1",
  "offValue": "v1",
  "enabled": true,
  "clients": ["mobile", "web"],
  "rules": [
    {
      "id": "ffr_...",
      "position": 1,
      "description": "Staff first",
      "conditions": [ { "attribute": "userRole", "operator": "In", "values": ["SystemAdmin", "Moderator"] } ],
      "value": "v2",
      "rolloutPercentage": 100
    },
    {
      "id": "ffr_...",
      "position": 2,
      "description": "Half of iOS 1.3+",
      "conditions": [
        { "attribute": "platform", "operator": "In", "values": ["ios"] },
        { "attribute": "appVersion", "operator": "SemverRange", "values": [">=1.3.0 <2.0.0"] }
      ],
      "value": "v2",
      "rolloutPercentage": 50
    }
  ],
  "state": "Published",
  "hasDraftChanges": false,
  "publishedInVersion": 42,
  "labels": ["feed"],
  "tags": { "owner": "web-team" },
  "createdUtc": "...", "updatedUtc": "..."
}
```

| Field | Rule |
|---|---|
| `key` | `^[a-z][a-zA-Z0-9]*(\.[a-z][a-zA-Z0-9]*){0,3}$`, 3 to 64 characters, unique per property, immutable after first publish (clients code against keys) |
| `kind` | `Boolean`, `Variant` (one of `variants`), `Value` (a free value of `valueType`), `KillSwitch` (section 13) |
| `valueType` | `Boolean`, `String` (up to 500 characters), `Integer` (64-bit), `Decimal`. `Boolean` kind implies `Boolean` |
| `variants` | `Variant` only: 2 to 20 distinct strings |
| `defaultValue` | Value when enabled and no rule matches |
| `offValue` | Value when `enabled` is false |
| `clients` | Subset of `mobile`, `web`, `server`; a flag is only sent to listed client types (`server` flags are evaluated by the backend only, through `IFeatureFlags`) |
| `rules` | Ordered, at most `appConfig.maxRulesPerFlag` (20); first matching rule wins |
| `state` | `Draft` (never published), `Published`, `Archived` (removed from manifests at the next publish; key stays reserved) |

### 12.2 Conditions

A rule matches when **every** condition matches (AND); use several rules for OR. At most `appConfig.maxConditionsPerRule` (10) conditions per rule and `appConfig.maxValuesPerCondition` (100) values per condition.

| `attribute` | Operators | Source |
|---|---|---|
| `platform` | `In`, `NotIn` | `X-Client-Platform` (`ios`, `android`, `web`) |
| `client` | `In`, `NotIn` | `X-Client` |
| `appVersion` | `SemverRange` (npm-style range, for example `>=1.2.0 <2.0.0`), `In`, `NotIn` | `X-Client-Version` |
| `updateId` | `In`, `NotIn` | `X-Client-Update-Id` (UUIDs or `embedded`) |
| `runtimeVersion`, `channel` | `In`, `NotIn` | Install row for `X-TK-Install-Id`, and `X-Client-Channel` |
| `signedIn` | `Is` (`true`, `false`) | Access token |
| `membership` | `In` (`Active`, `Inactive`, `None`) | Caller's membership (signed in) |
| `userRole` | `In`, `NotIn` | Caller's roles (`SystemAdmin`, `Moderator`, `FeedModerator`, `DirectoryModerator`, `EventManager`, `ReleaseManager`, `User`) |
| `userLabels` | `HasAny`, `HasAll`, `HasNone` | Caller's user labels (including `staff:` labels; evaluation happens on the server and only the resolved value is returned, so staff metadata never leaks) |
| `userTag` | `Equals` (`values[0]` is `key=value`), `HasKey` | Caller's user tags |
| `userId` | `In` (at most 100 ids) | Caller |
| `profileField` | `Equals`, `In` (`values[0]` is the field key) | Configurable profile fields (`TRELLISKIT.md` section 11), filterable fields only |
| `country` | `In`, `NotIn` | CloudFront `CloudFront-Viewer-Country` header in AWS; absent locally (condition fails) |
| `date` | `Before`, `After` (UTC timestamp) | Server clock |

Unknown attributes or operators, bad semver ranges, and values over the limits return `400 InvalidTargetingRule`. Conditions referencing data that is absent for the caller (anonymous caller with `membership`) do not match.

### 12.3 Evaluation

1. Disabled flag: `offValue`.
2. For each rule in order: if every condition matches, then if `rolloutPercentage` is 100 return `value`; otherwise compute `bucket = (first 4 bytes of SHA-256("<flagKey>:<ruleId>:<subject>")) mod 10000` with `subject` = user id when signed in, else the install or visitor id; return `value` when `bucket < rolloutPercentage * 100`, else continue with the next rule.
3. No rule returned: `defaultValue`.

Evaluation is deterministic, side-effect free, and the same code serves the manifest, the server-side `IFeatureFlags.Get<T>(key, RequestContext)` API used by backend services, and the admin "Test as" preview.

### 12.4 Admin routes (`SystemAdmin`)

| Method and path | Notes |
|---|---|
| `POST /api/v1/admin/app/flags/enumerate` | `EnumerationQuery`; `filters`: `kind`, `state`, `hasDraftChanges`, `client`; orderings `KeyAscending` (default), `UpdatedDescending`, `CreatedDescending`; `search` over key, name, description |
| `POST /api/v1/admin/app/flags` | Create (draft). `201 FeatureFlag`. `409 FlagKeyInUse` |
| `GET /api/v1/admin/app/flags/{id}` | Draft view plus `published` (the live definition, or null) and `history: { objects (max 10), totalRecords, enumerate }` |
| `PUT /api/v1/admin/app/flags/{id}` | Replace the draft (rules replace the whole list). `If-Match` required (`412 PreconditionFailed`). `200 FeatureFlag` |
| `POST /api/v1/admin/app/flags/{id}/archive`, `.../restore` | Draft change |
| `DELETE /api/v1/admin/app/flags/{id}` | Only never-published flags; `204` |
| `POST /api/v1/admin/app/flags/{id}/evaluate` | `{ "as": { "platform": "ios", "appVersion": "1.3.0", "userId": "usr_...", "signedIn": true, "installId"?: "..." }, "useDraft": true }` returns `{ value, matchedRuleId, reason: "Disabled" \| "Rule" \| "RuleOutsideRollout" \| "Default", bucket }` |

Flag drafts publish only through content publishing (section 16), so flags, strings, and banners move together and can be rolled back together. Kill switches are the exception (section 13.3).

---

## 13. Kill switches

### 13.1 Model

A kill switch is a `FeatureFlag` with `kind: "KillSwitch"`, `valueType: "Boolean"`, `key` equal to an engine **feature key**, `offValue: false` (not killed), and rules whose `value` is `true` (killed) for the matching callers. It adds `message` (shown to members, up to 200 characters, word filtered) and `blocksServer` (default true).

Engine feature keys (fixed list, extended by later phases): `signups`, `purchases`, `signupCodes`, `feed`, `feedComposer`, `comments`, `reactions`, `mediaUpload`, `videoUpload`, `linkPreviews`, `forums`, `polls`, `directory`, `events`, `library`, `messaging`, `assistants`, `notificationsPush`, `search`. Every kill switch exists for every property from the start (seeded with no rules, not killed), so the console always shows the full panel.

### 13.2 Enforcement

- **Server:** when `blocksServer` is true, the authorization component evaluates the kill switch for the request's attributes on every route of that feature (a route-to-feature map in `AuthorizationService`). Killed: `503 FeatureDisabled` with `message` and `Retry-After: 60`. Admin routes and moderation routes are never blocked by kill switches (operators must be able to clean up), and `SystemAdmin` callers are exempt unless the rule targets them explicitly.
- **Clients:** hide or disable the feature's entry points, show the `message` in place of the screen, and stop background work for it (for example, uploads pause for `mediaUpload`). On `503 FeatureDisabled` they update the cached switch at once.
- Kill switch evaluation results are cached in memory per (property, rule set version) and invalidated on change across tasks through the database change token (section 16.3), so a flip reaches every API task within `appConfig.killSwitchPropagationSeconds` (10).

### 13.3 Flipping

Kill switches change **live**, outside content versioning, because they are emergency controls:

| Method and path | Who | Body | Notes |
|---|---|---|---|
| `POST /api/v1/admin/app/kill-switches/enumerate` | `SystemAdmin`, `ReleaseManager` | `EnumerationQuery` | Items: the switch, `killedForEveryone` (bool), `ruleCount`, `lastChangedBy`, `lastChangedUtc` |
| `PUT /api/v1/admin/app/kill-switches/{featureKey}` | `SystemAdmin`, `ReleaseManager` | `{ "killed": true, "message": "...", "rules"?: [...], "reason": "..." }` (`killed: true` without rules kills for everyone; `rules` targets, for example, one app version) | Applies immediately, increments the kill switch revision (not the content version), audited `appConfig.killSwitch` with the reason, and notifies every `SystemAdmin` in-app (category `Account`, type `KillSwitchChanged`) |

`ReleaseManager` is a new Phase 1 style role (`resourceType: "Global"`) for operators who may flip kill switches and view (not publish) updates, without full admin rights. The console shows it in the roles editor.

---

## 14. Remote strings, banners, assets

### 14.1 Banners (announcements)

```json
{
  "id": "ban_...",
  "title": "Scheduled maintenance",
  "body": "The app will be read-only on **Sunday** from 06:00 to 07:00 UTC.",
  "style": "Warning",
  "placement": "Global",
  "dismissible": true,
  "priority": 10,
  "startsUtc": "2026-12-05T00:00:00Z",
  "endsUtc": "2026-12-07T07:00:00Z",
  "link": { "label": "Details", "path": "/support", "url": null },
  "imageAssetId": null,
  "clients": ["mobile", "web"],
  "rules": [ { "...": "the flag rule shape; value is ignored, a match shows the banner" } ],
  "labels": [], "tags": {}
}
```

| Field | Rule |
|---|---|
| `title` | 1 to 80; `body` 0 to 500, Markdown subset of Phase 2 section 3 without headings |
| `style` | `Info`, `Success`, `Warning`, `Critical` (`Critical` is not dismissible) |
| `placement` | `Global` (top of every screen), `Home`, `Feed`, `Forums`, `Library`, `Events`, `Directory`, `Messages`, `SignIn`, `Account` |
| `link` | `path` (app route, Phase 2 path rules) or `url` (`https` only), not both |
| `rules` | Same targeting as flags; no rules means everyone |
| Dismissal | Stored on the device per banner id; a banner edited after dismissal (new content version) shows again only when `title` or `body` changed |

### 14.2 Remote assets

```json
{ "id": "ras_...", "key": "onboarding.welcome", "contentType": "image/webp", "sizeBytes": 48211, "width": 1200, "height": 800,
  "sha256": "c0ffee...", "url": "https://api.example.test/app-content/example/c0ffee...", "altText": "Members at an event", "darkVariant": { "sha256": "...", "url": "..." },
  "labels": [], "tags": {} }
```

Upload: `POST /api/v1/admin/app/assets` (`{ key, altText }`, draft) then `PUT /api/v1/admin/app/assets/{id}/content` with a raw body (`image/png`, `image/jpeg`, `image/webp`, at most `appConfig.maxAssetMegabytes` (10)); optional `PUT .../dark-content`. The Phase 2 image pipeline re-encodes the image in its own format without metadata (EXIF and GPS removed) and stores it content addressed under `app-content/{propertyId}/{sha256 hex}` in the updates storage (section 17). `key` is how clients reference an asset (`^[a-z][a-zA-Z0-9]*(\.[a-z][a-zA-Z0-9]*){0,3}$`). Clients download each hash once and keep it until no published descriptor references it.

### 14.3 Remote strings

- The engine string catalog `shared/src/strings/catalog.json` is generated in CI from the website's and the mobile app's strings modules: `{ "key": "mobile.feed.emptyTitle", "default": "No posts yet", "tokens": ["name"], "maxLength": 120 }` for every key. It ships inside the server image.
- An override `{ id, key, value, notes, labels, tags }` must name a catalog key (`400 UnknownStringKey`), use only that key's tokens in `{token}` form (`400 InvalidStringTokens`), and fit `maxLength`. Values pass the word filter.
- Clients look up `override ?? default` and substitute tokens (`{name}` = property name) exactly as for built-in strings.
- Admin: `POST /api/v1/admin/app/strings/enumerate` returns catalog keys joined with overrides (`filters`: `client`, `overridden` true or false; `search` over key, default, and value), `PUT /api/v1/admin/app/strings/{key}` (draft upsert), `DELETE /api/v1/admin/app/strings/{key}` (draft removal).

### 14.4 Admin routes for banners and assets

`POST /api/v1/admin/app/banners/enumerate` (filters: `active`, `placement`, `style`, `state`), `POST /api/v1/admin/app/banners`, `GET`, `PUT` (`If-Match`), `DELETE` (draft removal); `POST /api/v1/admin/app/assets/enumerate`, `POST`, `GET`, `PUT` (metadata), content routes above, `DELETE`. All edits are drafts until a content publish.

---

## 15. Binary version policy (minimum native version)

### 15.1 Source: property configuration

The policy is property configuration (section 2), served in `GET /api/v1/property` and read by the built mobile client from there:

| Field | Meaning |
|---|---|
| `mobile.minimumBinaryVersion` | Semver string for both platforms, or `{ "ios", "android" }`. Binaries below it see the blocking screen. Absent means no minimum |
| `mobile.latestBinaryVersion` | Same shape, optional. Binaries below it see a dismissible "A new version is available" banner (once per version per device) |
| `mobile.storeUrls` | `{ "ios", "android" }` store links |
| `mobile.appStoreId` | Apple numeric id, used to open the native App Store sheet on iOS |

In single-property mode these live in `property.json`; in multi-property mode in the property record, edited on the console's Binary versions page (audited `property.update`, applied immediately, no content publish). One server setting remains: `mobile.enforceMinimumOnApi` (default false); when true, member API routes return `426 ClientUpdateRequired` to `X-Client: mobile` callers below the minimum (auth, `/me`, membership, account deletion, property, and update routes are exempt so members can still sign out, delete their account, and update).

### 15.2 Delivery

The app manifest's `binary` block carries the resolved values for the caller's platform and app version: `updateRequired` (below minimum), `updateRecommended` (below latest), `storeUrl`. The client already has the raw values from `GET /api/v1/property` (with the bundled property copy as the offline fallback); the `binary` block is a convenience for clients that only read the app manifest.

### 15.3 Client behavior

`updateRequired` shows a full-screen, non-dismissible "Update required" screen (strings `updates.requiredTitle`, `updates.requiredBody`) with an "Open the App Store" or "Open Google Play" button (`Linking.openURL(storeUrl)`) and "Sign out" and "Contact support" links. It is checked on launch from the cached manifest and on every manifest refresh. A code update can lift a minimum mistake (by publishing a corrected policy), so operators never strand users. Changes to the policy are audited and do not require a content publish.

---

## 16. Content publishing and versioning

### 16.1 Drafts and versions

- Flags (except kill switches), strings, banners, and remote assets are edited as drafts. Draft edits never reach clients.
- `POST /api/v1/admin/app/publish` `{ "message": "Holiday banner and composer v2 for staff", "expectedVersion": 41 }` publishes every pending draft change atomically as `contentVersion` 42, records a `ContentVersion` `{ id, version, message, publishedBy, publishedUtc, changeCounts: { flags, strings, banners, assets }, labels, tags }`, writes change log rows, and audits `appConfig.publish`. `409 ContentVersionConflict` when `expectedVersion` is not the current version (another admin published in between); the console then shows the new changes and asks again.
- `POST /api/v1/admin/app/drafts/discard` `{ "objectIds"?: [...] }` discards all or some drafts.
- `GET /api/v1/admin/app/drafts` returns pending changes `{ objects (max 100), totalRecords, enumerate: "/api/v1/admin/app/drafts/enumerate" }`; each change shows the object, change type (`Create`, `Update`, `Archive`, `Delete`), editor, and a field-level diff.
- `POST /api/v1/admin/app/versions/enumerate` lists versions; `GET /api/v1/admin/app/versions/{version}` shows its changes (`changes: { objects (max 50), totalRecords, enumerate }`).

### 16.2 Rollback

`POST /api/v1/admin/app/versions/{version}/restore` `{ "message": "Roll back to 40" }` publishes a new version whose content equals the state at `version` (flags, strings, banners, assets as they were then; kill switches untouched). Restore is itself a version, so it can be undone. Audited `appConfig.restore`.

### 16.3 Storage model (temporal rows) and propagation

Each content table holds immutable revision rows: `revisionid` PK, `objectid`, `validfromversion`, `validtoversion` NULL (current), `isdraft`. A publish closes the current revisions of changed objects (`validtoversion = N`) and opens the draft revisions (`validfromversion = N`, `isdraft = false`) in one batch, plus `appcontentchanges (version, objecttype, objectid, change)` rows. Child rows (flag rules, conditions, condition values, banner rules) reference `revisionid`, so the state at any version is a plain query (`validfromversion <= V AND (validtoversion IS NULL OR validtoversion > V)`). Restores copy those rows as new drafts and publish them.

Each API task caches the current content per property and polls `appcontentstate (propertyid, contentversion, killswitchrevision, updatedutc)` every `appConfig.propagationSeconds` (5); a change reloads the cache. That keeps multi-task deployments consistent without a message bus.

Change log rows older than `appConfig.changeLogRetentionVersions` (200 versions) are pruned; clients behind that get `fullRefresh: true`.

---

## 17. Storage layout and infrastructure

### 17.1 Storage

One `updates` storage location per deployment, partitioned by property id:

| Path | Contents | Served |
|---|---|---|
| `updates/assets/{propertyId}/{sha256 hex}` | Code update bundles and assets (content addressed; shared by every update and channel that uses the same bytes) | Public, immutable, CDN |
| `app-content/{propertyId}/{sha256 hex}` | Remote content assets (re-encoded images) | Public, immutable, CDN |
| `updates/staging/{propertyId}/{groupId}/...` | Not used: uploads go directly to the content-addressed key with checksum verification | |

| Setting (environment variable) | Local | AWS |
|---|---|---|
| `updates.storageMode` (`TK_UPDATES_STORAGE_MODE`) | `Local` | `S3` |
| `updates.bucket` (`TK_UPDATES_BUCKET`) | unused | the bucket name from infra |
| `updates.directory` | `{storage.directory}/updates` (Docker: the `server-data` volume) | unused |
| `updates.assetBaseUrl` (`TK_UPDATES_ASSET_BASE_URL`) | `{origin}/api/v1/updates/assets` (root-relative is not allowed: Expo needs absolute URLs, so local mode builds it from `web.publicApiBaseUrl` or the request host) | `https://<domains.api>/updates/assets` |
| `updates.contentAssetBaseUrl` (`TK_UPDATES_CONTENT_ASSET_BASE_URL`) | `{origin}/api/v1/app/content-assets` | `https://<domains.api>/app-content` |

Asset bytes are never deleted while any update or content revision references them; a weekly `UpdateAssetGcWorker` deletes unreferenced objects older than 30 days. Lifecycle rules never expire objects (references decide).

### 17.2 AWS (infra/)

| Need | Design |
|---|---|
| Bucket | One S3 bucket per environment, `<propertyId>-<env>-updates-<account>` (single-property stacks) or a shared platform bucket (multi-property), versioning on, public access blocked, SSE-S3, `x-amz-checksum-sha256` required by bucket policy for PUTs |
| CDN | CloudFront behaviors on the API distribution: `/updates/assets/*` and `/app-content/*` to the bucket with Origin Access Control, cache policy with long TTLs, compression on; `/api/v1/updates/manifest` and `/api/v1/app/manifest` to the ALB with caching disabled and an origin request policy forwarding `expo-*`, `X-TK-*`, `X-Client*`, `Accept`, `If-None-Match`, `Authorization`, and `CloudFront-Viewer-Country` |
| Secrets | Secrets Manager secret `<propertyId>/<env>/updates-signing-key` read by the task role, injected as `TK_UPDATES_SIGNING_KEY` |
| IAM | Task role: `s3:PutObject`, `s3:GetObject`, `s3:HeadObject`, `s3:DeleteObject`, `s3:ListBucket` on the bucket; presigned PUTs are generated with the task role |
| Alarms | Manifest endpoint 5xx rate above 1 percent over 5 minutes, p95 latency above 1 second, `SigningUnavailable` count above 0, and an update's `LaunchFailed` share above `updates.launchFailureAlarmPercent` (5) within an hour of publish (metric from the server), all to the property's alarm email |
| Health | The manifest endpoint is included in the synthetic health checks with a fixed test runtime version (expects a signed `noUpdateAvailable`) |
| Cost | No per-user fees; storage is a few hundred MB per property; egress through CloudFront |

`infra/` tests (cdk-nag clean) assert the bucket policy, OAC, the cache-disabled manifest behaviors, and the secret grant.

---

## 18. Local mode

| Concern | Local behavior |
|---|---|
| Storage | `updates.storageMode = Local` under `{storage.directory}/updates` and `/app-content` |
| Asset delivery | `GET /api/v1/updates/assets/{propertyId}/{sha256Hex}` and `GET /api/v1/app/content-assets/{propertyId}/{sha256Hex}` (Anonymous, immutable caching, `Range`) |
| Uploads | Signed local PUT route (section 10.2) |
| Signing key | `npm run updates:keys -- --local --property <dir>` writes a development key pair: the private key to `~/.trelliskit/keys/<propertyId>/private-key.pem` (outside the repository; the Docker stack mounts that folder read-only and the server reads it through `TK_UPDATES_SIGNING_KEY_FILE`) and the certificate to `<property>/mobile/updates/certificate.local.pem` (gitignored). Local release builds embed the local certificate; production builds always embed the committed one |
| Testing on a device | Build a release-configuration binary pointed at the LAN API (`EXPO_PUBLIC_API_URL`, used for `updates.url`), with `expo-channel-name: development`. The development build profile allows cleartext HTTP to the LAN address (iOS ATS exception and Android network security config, development profile only); preview and production builds require HTTPS |
| Expo Go and dev client | Updates are disabled (library behavior); content manifest, flags, kill switches, and banners still work |
| Tests | The backend suites use a generated throwaway key pair and verify signatures with the same algorithm the library uses; a recorded `expo-updates` request fixture checks header parsing |

---

## 19. Rate limits

| Route | Limit | Setting |
|---|---|---|
| `GET /api/v1/updates/manifest` | 60 per hour per install id and 600 per hour per IP | `rateLimits.updateChecksPerInstallPerHour`, `rateLimits.updateChecksPerIpPerHour` |
| `POST /api/v1/updates/events` | 30 per hour per install id | `rateLimits.updateEventsPerInstallPerHour` |
| `GET /api/v1/app/manifest` | 120 per hour per install or visitor id, 1200 per hour per IP | `rateLimits.appManifestPerClientPerHour`, `rateLimits.appManifestPerIpPerHour` |
| Content and binary policy enumerations | 60 per hour per client id | `rateLimits.appContentEnumeratePerHour` |
| Asset GETs (local mode) | none (immutable and cacheable) | |
| Admin and deploy key routes | none (audited) | |

---

## 20. Telemetry

Instruments on the `TrellisKit` meter; labels are low cardinality (never update ids, install ids, flag keys of non-kill-switch flags, or text). All carry `trelliskit.property`.

| Instrument | Type | Labels |
|---|---|---|
| `trelliskit.updates.checks` | Counter | `platform`, `channel`, `outcome` (`update`, `no_update`, `rollback_directive`, `not_in_rollout`, `error`), `protocol` (0, 1) |
| `trelliskit.updates.check.duration` | Histogram (s) | `platform` |
| `trelliskit.updates.downloads` | Counter | `platform`, `channel` (from `Downloaded` events) |
| `trelliskit.updates.applied` | Counter | `platform`, `channel` |
| `trelliskit.updates.failures` | Counter | `platform`, `channel`, `stage` (`check`, `download`, `launch`), `error_code` (the bounded list of 9.2) |
| `trelliskit.updates.signing` | Counter | `outcome` (`signed`, `unknown_keyid`, `missing_key`, `error`) |
| `trelliskit.updates.publishes` | Counter | `action` (`create`, `publish`, `rollout`, `pause`, `resume`, `rollback`, `rollback_to_embedded`), `credential` (`member`, `admin_token`, `deploy_key`) |
| `trelliskit.updates.asset.bytes` | Counter | `direction` (`uploaded`, `served_local`) |
| `trelliskit.updates.active_installs` | Gauge | `platform`, `channel`, `on_latest` (true, false) |
| `trelliskit.app.manifest.requests` | Counter | `client`, `outcome` (`full`, `delta`, `not_modified`, `full_refresh`, `error`) |
| `trelliskit.app.manifest.duration` | Histogram (s) | `client` |
| `trelliskit.app.content.publishes` | Counter | `action` (`publish`, `restore`, `discard`) |
| `trelliskit.killswitch.changes` | Counter | `feature`, `action` (`kill`, `unkill`, `target`) |
| `trelliskit.killswitch.blocked` | Counter | `feature`, `client` |
| `trelliskit.flags.evaluations` | Counter | `surface` (`manifest`, `server`, `preview`), `reason` (`disabled`, `rule`, `default`) |

`X-Client-Update-Id` on API calls is stamped on request history and spans (`client.update_id`), not metrics. Spans: `updates.manifest` (attributes `update.platform`, `update.channel`, `update.runtime_version`, `update.selected_id`), `updates.publish`, `app.manifest`, `app.publish`. Grafana: a Releases folder with dashboards for update checks and outcomes, adoption by channel, failures by stage and code, manifest latency, kill switch blocks, and content publishes.

---

## 21. Admin console

| Page | Contents |
|---|---|
| Releases, then Updates (`/admin/updates`) | Enumerated update groups (filters: platform, channel, runtime version, status, kind, critical, labels, tags; search over message), row actions View, View JSON, Raise rollout, Pause or Resume, Roll back, Delete (Draft). Header cards per channel and platform: live update, rollout, installs on latest |
| Update detail | Message, commit, who published, per-platform UUIDs and sizes, rollout slider with steps (1, 5, 10, 25, 50, 100) and confirmation, adoption chart and table (9.3), failure breakdown, asset list (enumerated), audit trail |
| Roll back dialog | Choose a previous group (enumerated picker filtered to the same channel and runtime versions) or "Built-in bundle" (rollback to embedded), with rollout and message |
| Channels | Built-in and custom channels with their latest updates and active installs; add custom channel |
| Runtime versions | Section 7.5 table, installs by binary version and `keyid` |
| Signing and deploy keys | Signing keys (status, certificate expiry, last used, retire), health status; deploy keys (create with a one-time secret display, channels, expiry, last used, revoke) |
| Kill switches (`/admin/app/kill-switches`) | One row per feature key with a large toggle, message, targeting editor, last change and reason; changes ask for a reason and apply immediately |
| Feature flags (`/admin/app/flags`) | Enumerated table, editor with a rule builder (conditions with autocomplete for labels, tags, roles, profile fields), "Test as" panel (12.4), draft badge, history |
| Strings (`/admin/app/strings`) | Catalog browser (search, client filter, overridden filter), side-by-side default and override with token validation and live preview |
| Banners (`/admin/app/banners`) | Table and editor with style preview per placement on phone, tablet, and web frames, schedule, targeting |
| Assets (`/admin/app/assets`) | Grid of remote assets with upload, dark variant, alt text, usage key |
| Content versions (`/admin/app/versions`) | Pending drafts with diffs and a Publish button (message required), version history, Restore |
| Binary versions | The section 15 property fields (minimum and latest versions, store URLs, App Store id) with the current install distribution per binary version, so the minimum is set with eyes open |

Access: everything requires `SystemAdmin`; `ReleaseManager` sees Updates (read only) and Kill switches.

---

## 22. Settings

All in `SettingsRegistry.cs`, property-scoped in multi-property mode unless marked Deployment.

| Key | Type | Default | Apply | Environment variable |
|---|---|---|---|---|
| `updates.enabled` | Boolean | true | Live | `TK_UPDATES_ENABLED` |
| `updates.storageMode` | Enum `Local`, `S3` (Deployment) | `Local` | Restart | `TK_UPDATES_STORAGE_MODE` |
| `updates.bucket` | String (Deployment) | empty | Restart | `TK_UPDATES_BUCKET` |
| `updates.s3Region` | String (Deployment) | `media.s3Region` | Restart | `TK_UPDATES_S3_REGION` |
| `updates.assetBaseUrl` | String (absolute URL or empty for derived) | empty | Live | `TK_UPDATES_ASSET_BASE_URL` |
| `updates.contentAssetBaseUrl` | String | empty | Live | `TK_UPDATES_CONTENT_ASSET_BASE_URL` |
| `updates.signingKeyPem` | Secret | empty | Live | `TK_UPDATES_SIGNING_KEY` (or `TK_UPDATES_SIGNING_KEY_FILE` for a path) |
| `updates.signingKeyId` | String | `main` | Live | `TK_UPDATES_SIGNING_KEY_ID` |
| `updates.checkIntervalMinutes` | Integer 1 to 1440 (exposed to clients as `updates.checkIntervalMinutes` in `GET /api/v1/property`; a property configuration value of the same name overrides it) | 15 | Live | |
| `updates.maxAssetsPerUpdate` | Integer 10 to 10000 | 2000 | Live | |
| `updates.maxAssetMegabytes` | Integer 1 to 500 | 50 | Live | |
| `updates.maxUploadMegabytes` | Integer 10 to 5000 | 500 | Live | |
| `updates.maxChannels` | Integer 3 to 50 | 10 | Live | |
| `updates.uploadUrlMinutes` | Integer 5 to 720 | 60 | Live | |
| `updates.installRetentionDays` | Integer 7 to 3650 | 90 | Live | |
| `updates.eventRetentionDays` | Integer 1 to 365 | 30 | Live | |
| `updates.launchFailureAlarmPercent` | Integer 1 to 100 | 5 | Live | |
| `appConfig.maxFlags` | Integer 10 to 1000 | 200 | Live | |
| `appConfig.maxRulesPerFlag` | Integer 1 to 100 | 20 | Live | |
| `appConfig.maxConditionsPerRule` | Integer 1 to 50 | 10 | Live | |
| `appConfig.maxValuesPerCondition` | Integer 1 to 1000 | 100 | Live | |
| `appConfig.maxActiveBanners` | Integer 1 to 100 | 20 | Live | |
| `appConfig.maxDeltaItems` | Integer 50 to 5000 | 500 | Live | |
| `appConfig.maxAssetMegabytes` | Integer 1 to 50 | 10 | Live | |
| `appConfig.checkIntervalMinutes` | Integer 1 to 1440 | 15 | Live | |
| `appConfig.propagationSeconds` | Integer 1 to 60 | 5 | Live | |
| `appConfig.killSwitchPropagationSeconds` | Integer 1 to 60 | 10 | Live | |
| `appConfig.changeLogRetentionVersions` | Integer 10 to 10000 | 200 | Live | |
| `mobile.enforceMinimumOnApi` | Boolean | false | Live | |
| `rateLimits.updateChecksPerInstallPerHour` | Integer 1 to 10000 | 60 | Live | |
| `rateLimits.updateChecksPerIpPerHour` | Integer 1 to 100000 | 600 | Live | |
| `rateLimits.updateEventsPerInstallPerHour` | Integer 1 to 10000 | 30 | Live | |
| `rateLimits.appManifestPerClientPerHour` | Integer 1 to 10000 | 120 | Live | |
| `rateLimits.appManifestPerIpPerHour` | Integer 1 to 100000 | 1200 | Live | |
| `rateLimits.appContentEnumeratePerHour` | Integer 1 to 10000 | 60 | Live | |

Client build variables: `TK_PROPERTY_DIR` (as today), `EXPO_PUBLIC_API_URL` (updates URL and API), `TK_UPDATES_CHANNEL` (overrides the build profile's channel for local builds), `TK_UPDATES_DEPLOY_KEY` and `TK_UPDATES_API_URL` (publish CLI).

---

## 23. Data model

Migration "Live updates", the next free version (assigned at implementation), in `SqliteSchema.cs` and `PostgresqlSchema.cs`. Type mapping as in Phase 2. **Every table has `propertyid TEXT NOT NULL` first, and every index and unique constraint starts with `propertyid`.** Labels and tags use the shared tables with the target types of section 0.1. No JSON columns except `updategroupconfigs.expoclient` (opaque, never queried; section 10.3).

| Table | Key columns |
|---|---|
| `updategroups` | `id` PK, `kind`, `channel`, `message`, `status`, `rolloutpercentage` (stored as integer basis points 1 to 10000), `critical`, `criticalmessage`, `gitcommit`, `gitdirty`, `republishof` NULL, `promotedfrom` NULL, `createdbytype` (`Member`, `AdminToken`, `DeployKey`), `createdbyid`, `createdbyname`, `publishedutc`, `pausedutc`, `rolledbackutc`, `supersededutc`, `createdutc`, `updatedutc` |
| `updates` | `id` PK, `groupid` FK, `uuid` UNIQUE, `platform`, `runtimeversion`, `channel`, `kind` (copied from the group for selection), `status` (copied), `rolloutbasispoints` (per platform override or the group's), `createdat` (manifest `createdAt`, millisecond precision), `launchassethash`, `assetcount`, `totalbytes`, `createdutc`, `updatedutc` |
| `updategroupconfigs` | `updateid` PK FK, `expoclient` (TEXT, opaque) |
| `updateassets` | PK (`propertyid`, `sha256hex`), `hashb64url`, `contenttype`, `sizebytes`, `storedutc` NULL (null until upload verified), `lastreferencedutc` |
| `updateassetrefs` | PK (`propertyid`, `updateid`, `position`), `sha256hex`, `key`, `fileextension`, `contenttype`, `islaunchasset` |
| `updatechannels` | PK (`propertyid`, `name`), `builtin`, `createdutc` |
| `updatedeploykeys` | `id` PK, `name`, `secrethash` UNIQUE, `channels` child table `updatedeploykeychannels (deploykeyid, channel)`, `expiresutc`, `lastusedutc`, `revokedutc`, `createdbyuserid`, `createdutc` |
| `updatesigningkeys` | `id` PK, `keyid`, `algorithm`, `keybits`, `source` (`Environment`, `Stored`), `certificatepem`, `certificatesha256`, `certificatenotafterutc`, `privatekeyciphertext` NULL, `privatekeynonce` NULL, `status` (`Active`, `Retired`), `lastusedutc`, `createdutc`, `retiredutc` |
| `updateinstalls` | `id` PK, `installidhash` UNIQUE, `platform`, `channel`, `runtimeversion`, `currentupdateuuid`, `embeddedupdateuuid`, `keyid`, `appversion`, `firstseenutc`, `lastcheckutc` |
| `updateinstallfailures` | PK (`propertyid`, `installidhash`, `updateuuid`), `createdutc` |
| `updateevents` | `id` PK, `installidhash`, `updateuuid`, `type`, `errorcode`, `launchdurationms`, `occurredutc`, `receivedutc` |
| `updateeventcounts` | PK (`propertyid`, `updateuuid`, `date`, `type`), `count` |
| `featureflags` | `revisionid` PK, `objectid` (`ffl_`), `key`, `name`, `description`, `kind`, `valuetype`, `defaultvalue`, `offvalue`, `enabled`, `message` (kill switches), `blocksserver`, `state`, `validfromversion` NULL, `validtoversion` NULL, `isdraft`, `editedbyuserid`, `createdutc`, `updatedutc` |
| `featureflagvariants` | PK (`propertyid`, `revisionid`, `position`), `value` |
| `featureflagclients` | PK (`propertyid`, `revisionid`, `client`) |
| `featureflagrules` | `id` PK (`ffr_`), `revisionid` FK, `ownertype` (`FeatureFlag`, `Banner`), `position`, `description`, `value`, `rolloutbasispoints` |
| `featureflagconditions` | `id` PK, `ruleid` FK, `position`, `attribute`, `operator`, `argument` NULL (tag key or profile field key) |
| `featureflagconditionvalues` | PK (`propertyid`, `conditionid`, `position`), `value` |
| `killswitchstate` | PK (`propertyid`, `featurekey`), `flagobjectid`, `revision`, `lastchangedbyuserid`, `lastchangedreason`, `lastchangedutc` (kill switch rule rows live in `featureflagrules` with `ownertype = 'KillSwitch'` against a live, non-versioned revision) |
| `remotestrings` | `revisionid` PK, `objectid` (`rst_`), `stringkey`, `value`, `notes`, `validfromversion`, `validtoversion`, `isdraft`, `editedbyuserid`, `createdutc` |
| `banners` | `revisionid` PK, `objectid` (`ban_`), `title`, `body`, `style`, `placement`, `dismissible`, `priority`, `startsutc`, `endsutc`, `linklabel`, `linkpath`, `linkurl`, `imageassetobjectid`, `validfromversion`, `validtoversion`, `isdraft`, `editedbyuserid`, `createdutc` |
| `bannerclients` | PK (`propertyid`, `revisionid`, `client`) |
| `remoteassets` | `revisionid` PK, `objectid` (`ras_`), `assetkey`, `contenttype`, `sizebytes`, `width`, `height`, `sha256hex`, `darksha256hex` NULL, `alttext`, `validfromversion`, `validtoversion`, `isdraft`, `editedbyuserid`, `createdutc` |
| `contentversions` | `id` PK (`cve_`), `version` UNIQUE, `message`, `kind` (`Publish`, `Restore`), `restoredfromversion` NULL, `publishedbyuserid`, `flagchanges`, `stringchanges`, `bannerchanges`, `assetchanges`, `publishedutc` |
| `appcontentchanges` | PK (`propertyid`, `version`, `objecttype`, `objectid`), `change` (`Upsert`, `Delete`) |
| `appcontentstate` | `propertyid` PK, `contentversion`, `killswitchrevision`, `updatedutc` |

Indexes (all prefixed by `propertyid`):

| Table | Index | Purpose |
|---|---|---|
| `updates` | `idx_updates_select (platform, runtimeversion, channel, status, createdat DESC, id)` | Manifest selection |
| `updates` | `ux_updates_uuid (uuid)` UNIQUE | Lookup by Expo id |
| `updategroups` | `idx_updategroups_list (createdutc DESC, id DESC)`, `idx_updategroups_channel (channel, status, createdutc)` | Console |
| `updateassetrefs` | `idx_updateassetrefs_hash (sha256hex)` | GC, dedupe |
| `updateinstalls` | `idx_updateinstalls_current (currentupdateuuid, lastcheckutc)`, `idx_updateinstalls_runtime (platform, runtimeversion, lastcheckutc)`, `idx_updateinstalls_prune (lastcheckutc)` | Adoption, runtime versions, retention |
| `updateevents` | `idx_updateevents_update (updateuuid, occurredutc)`, `idx_updateevents_prune (receivedutc)` | |
| `featureflags` | `idx_featureflags_current (objectid) WHERE validtoversion IS NULL AND isdraft = false`, `ux_featureflags_key_current (lower(key)) WHERE validtoversion IS NULL AND isdraft = false` UNIQUE, `idx_featureflags_version (validfromversion, validtoversion)`, `idx_featureflags_draft (objectid) WHERE isdraft = true` | Current state, history |
| `remotestrings`, `banners`, `remoteassets` | The same three current, version, and draft indexes on each | |
| `featureflagrules` | `idx_rules_owner (revisionid, position)` | |
| `featureflagconditions` | `idx_conditions_rule (ruleid, position)` | |

Write rules from Phase 2 section 15 apply (single batch transactions, recomputed counters, conditional claims, `jobleases` for `UpdateAssetGcWorker`, `UpdateInstallPruneWorker`, `UpdateEventRollupWorker`, `ContentChangeLogPruneWorker`).

---

## 24. Backend structure

Services: `UpdateManifestService` (protocol parsing with a structured field parser for `expo-expect-signature`, `expo-extra-params`, and `expo-recent-failed-update-ids`; selection; multipart writer), `UpdateSigningService` (`IUpdateSigner` over `System.Security.Cryptography.RSA`, keys from settings or the encrypted store), `UpdatePublishService`, `UpdateRolloutService`, `UpdateAdoptionService`, `AppManifestService`, `FeatureFlagService` (evaluation engine shared with `IFeatureFlags`), `KillSwitchService` (enforcement hook in `AuthorizationService`), `RemoteStringService` (catalog validation), `BannerService`, `RemoteAssetService` (Phase 2 image pipeline), `ContentPublishService`. Integration seam: `IUpdateObjectStore` (presigned PUT with checksum, head, get, delete; S3 and local implementations, reusing the Phase 2 `IObjectStore` patterns). Route registrars: `UpdateManifestRoutes`, `UpdateAssetRoutes`, `UpdateEventRoutes`, `AdminUpdateRoutes`, `AdminUpdateKeyRoutes`, `AppManifestRoutes`, `AdminAppConfigRoutes`, `KillSwitchRoutes`. Workers: `UpdateAssetGcWorker`, `UpdateInstallPruneWorker`, `UpdateEventRollupWorker`, `ContentChangeLogPruneWorker`, `ContentCacheRefreshWorker` (per task, polls `appcontentstate`).

Touchstone suites (both providers, and the cross-property isolation suite): `UpdateManifestSuite` (every header, protocol 0 and 1, accept negotiation, selection incl. failed ids and rollout buckets, directives, signatures verified with the certificate, `createdAt` monotonicity), `UpdatePublishSuite` (create, upload verification, publish, conflicts, deploy key scopes), `UpdateRolloutSuite` (bucketing determinism, raise, lower, pause, supersede, rollback republish, rollback to embedded), `UpdateAdoptionSuite`, `FeatureFlagSuite` (every attribute and operator, rule order, rollout determinism, staff metadata never leaked), `KillSwitchSuite` (server enforcement, exemptions, propagation with a fixed clock), `AppManifestSuite` (delta, full refresh, ETag and 304, per caller evaluation), `ContentPublishSuite` (drafts, conflicts, restore, temporal queries), `RemoteStringSuite` (catalog and token validation), `BinaryPolicySuite` (property fields in `GET /api/v1/property`, both shapes of `minimumBinaryVersion`, `426` and exemptions).

---

## 25. Client notes

**Shared:** `ApiClient` methods `getAppManifest`, `enumerateAppStrings`, `enumerateAppBanners`, `enumerateAppAssets`, `reportUpdateEvents`; admin methods for every route in sections 6, 7, 10, 12 to 16. A `flags` module (`getFlag(key, fallback)`, `isKilled(featureKey)`, subscription for re-render), a `strings` resolver that applies overrides with token substitution, and the `catalog.json` generator script used by CI. The 503 `FeatureDisabled` handler lives in the shared request pipeline.

**Mobile and tablet:** `mobile/src/updates/` (install id, extra params, foreground checks, critical prompt, update-required screen, event queue, About screen), `mobile/src/appConfig/` (manifest cache, banners host per placement, asset cache by hash, kill switch gates around tabs and composers), `app.config.js` updates block (section 2), `scripts/publish-update.mjs`, `scripts/updates-keys.mjs`, and the build step that writes `assets/app-manifest.json`. Tablet shows `Global` banners in the split view header.

**Website:** the content manifest client (no code updates: the web app deploys normally), banners host, kill switch gates, string overrides, the inlined fallback manifest at build, and the admin pages of section 21.

---

## 26. Delivery order

1. Schema, settings, storage abstraction (local and S3), signing key registry and signer, structured field parser.
2. Manifest endpoint with selection, signatures, directives, asset delivery; adoption rows. Backend suites against recorded `expo-updates` requests.
3. Publish API and CLI, deploy keys, local key generation; first end-to-end OTA on a local release build.
4. Rollout, pause, rollback, rollback to embedded, critical updates, client events; admin Updates pages.
5. Infra: bucket, CloudFront behaviors, secret, alarms, synthetic check.
6. Content manifest with flags (evaluation engine, server `IFeatureFlags`), kill switches (server enforcement and clients), binary version policy.
7. Remote strings (catalog generation in CI), banners, remote assets, content publishing, restore; admin pages.
8. Web content manifest client; Grafana Releases folder; `REST_API.md`, Postman; simulated user testing pass (including a forced rollback drill and a kill switch drill).

---

## 27. Open questions (with recommended answers)

| # | Question | Recommendation |
|---|---|---|
| 1 | `TRELLISKIT.md` 8.1 says "`204` when the app is current". Protocol 1 clients expect a directive | Answer protocol 1 clients with a signed `noUpdateAvailable` directive (section 3.3) and update section 8.1's wording; `204` is not used |
| 2 | Signing key custody per property in multi-property mode | Single-property stacks read `TK_UPDATES_SIGNING_KEY` from Secrets Manager; multi-property deployments store each property's key encrypted in the per-property secret store with the same engine secret key the AI contract uses. Import only from a member session |
| 3 | Who may publish to `production`? | `SystemAdmin` (member session or admin token); CI deploy keys limited to `preview` by default, so publishing the tested commit to `production` is a deliberate separate run |
| 4 | Default initial production rollout | 10 percent, raised by hand after watching launch failures for an hour (the alarm covers regressions) |
| 5 | Should the app prompt for every downloaded update? | No: silent apply on next launch; prompt only for `critical` updates and never interrupt a composer |
| 6 | Install id privacy (store data safety forms) | Random per install, HMAC hashed at rest, never joined to member ids, deleted after 90 days idle; declare it as an app functionality identifier not used for tracking |
| 7 | Do flags evaluate on the server or the client? | Server: the manifest returns resolved values, so targeting data (user labels, `staff:` metadata, roles) never reaches clients and rules can change without client logic changes |
| 8 | Do kill switches go through content publishing? | No: they are live emergency controls with a required reason and audit, flipped by `SystemAdmin` or the new `ReleaseManager` role |
| 9 | Enforce the minimum binary version on the API? | Off by default (`mobile.enforceMinimumOnApi: false`); the client screen is enough normally, and the switch exists for security fixes |
| 10 | Custom channels | Allow up to 10 per property (for example a staff channel), but keep the three built-ins as the normal path |
| 11 | Should source maps be uploaded with updates for crash symbolication? | Not in this milestone; add with crash reporting later (the CLI keeps the exported source maps as a CI artifact) |
| 12 | Sample property signing certificate | Commit a development-only certificate for the sample at `samples/example/mobile/updates/certificate.pem` with its private key only in developers' `~/.trelliskit/keys/<propertyId>/`; production properties commit only their production certificate |
| 13 | Conflict with the built mobile client: rollout bucketing needs an install id, but the client does not yet send `expo-extra-params: installid="..."` or `X-TK-Install-Id` | Add the install id to the client (section 2). Until it ships, the server treats every install as outside partial rollouts, so updates reach devices only at 100 percent and adoption rows are keyed by IP plus platform (approximate) |
| 14 | Conflict: the client fixes `keyid="main"`, so key rotation needs a new binary with a new `keyid` | Accept: rotation is rare and already requires a new certificate in a store build; the server keeps signing `main` until those installs drain |
| 15 | Conflict: the fingerprint differs per channel because `expo-channel-name` is a static request header in native config, so one export cannot serve `preview` and `production` | Accept for now and publish per channel (section 7.4, no server-side promote). Later, move the channel header to a runtime override (`Updates.setUpdateRequestHeadersOverride`) or exclude the updates request headers from the fingerprint so a tested update can be promoted byte for byte |
| 16 | Conflict: the built tool sends one multipart request per platform with no group id | Accepted as the publish contract (section 10.2) with server-side grouping by channel, commit, message, and time; recommend the tool pass `updateGroupId` from the first response, and switch to presigned direct-to-S3 uploads if bundles approach the proxy limits |
| 17 | `criticalMessage` and `mobile.latestBinaryVersion` are engine additions not yet in the client | Keep them optional; the client falls back to the `updates.criticalPrompt` string and ignores `latestBinaryVersion` until implemented |


---

## Resolved decisions (property owner, 2026-11-11)

These answers override the recommendations in the open questions section wherever they differ.

| # | Question | Decision |
|---|---|---|
| 1 | Response when the app is current | The protocol's signed `noUpdateAvailable` directive (fix the "204" wording in `TRELLISKIT.md` 8.1) |
| 2 | Signing key custody in multi-property mode | Uploaded in the admin portal, encrypted per property in the database, never shown again |
| 3 | Who publishes to production | Admins (or a ReleaseManager role); CI deploy keys may publish to `preview` only |
| 4 | Rollout | **Admins control distribution schedules and percentages per update:** the admin portal offers a rollout plan (stages with percentages and start times, for example 5% now, 25% after 24 hours, 100% after 72 hours), manual overrides at any time, pause and resume, and automatic halt when the client failure rate for the update exceeds a configurable threshold. No fixed engine default beyond a plan template admins can edit |
| 5 | Apply mode for non-critical updates | Silent download, applied on next launch; only critical updates prompt |
| 6 | Install ids | Hashed at rest, never linked to members, deleted after 90 days idle |
| 7 | Feature flag evaluation | Server-side |
| 8 | Kill switches | Live immediately (outside content versioning), audited |
| 9 | API enforcement of the minimum binary version | Off by default; admin toggle |
| 10 | Custom channels | Up to 10 per property beyond production, preview, development |
| 11 | Source maps | **Included now:** the publish tool uploads source maps with each update; they are stored privately (never served to clients) and used to symbolicate client error reports in the admin portal |
| 12 | Sample certificate | Committed (public by design); private keys never in the repository |
| 13 | Install id from the client | The client sends a stable random install id (`X-TK-Install-Id`, generated once per install and stored in secure storage) on update checks so staged rollouts work |
| 14 | Signing key rotation | **Supported without a new binary:** builds embed the active certificate and a standby certificate (key ids `main` and `standby`); the server can switch signing to the standby key and then publish a new standby certificate in the next store release. Rotation procedure documented in the admin portal |
| 15 | Promotion between channels | **Fixed now:** the channel is removed from the native fingerprint (sent as a request header configured at runtime, not baked into native config), so one runtime version serves all channels and the server can promote an update from `preview` to `production` |
| 16 | Update grouping | The publish tool sends an `updateGroupId` shared by the iOS and Android uploads of one release |
| 17 | `criticalMessage` and `latestBinaryVersion` | Optional manifest and property fields; the client reads them (critical prompt text; "a newer version is available" hint) |


---

## Client implementation notes (supersede sections 2, 5, 6.3, and 10.3 where they differ)

Recorded from the built mobile client (`trelliskit/mobile`, expo-updates 58), 2026-11-11.

1. **Install id:** sent as the `X-TK-Install-Id` request header on manifest requests only (never on API requests); an empty value means no id. The client does not use `setExtraParamAsync('installid')`.
2. **Channel at runtime:** `expo-channel-name` is set at runtime with `Updates.setUpdateRequestHeadersOverride` (embedded headers are identical for every channel), so all channels of a binary share one runtime version and the server may promote `preview` to `production`. Never promote `development` (its published config has no API URL).
3. **Key rotation (decision 14) uses certificate-chain mode:** expo-updates 58 embeds exactly one certificate and one key id, so builds embed `certificate.pem` (key id `main`) as a self-signed CA certificate, and `standby-certificate.pem` (key id `standby`) is issued by it. The server:
   - reads `expo-expect-signature` to know which certificate the binary embeds (not which key to sign with);
   - signing with the embedded certificate's own key: sends no `certificate_chain` part;
   - signing with a rotated-to key: sends a `certificate_chain` part containing the PEM certificates from the signing key up to, but not including, the embedded certificate (for the first rotation, just `standby-certificate.pem`);
   - puts the actual signing key's id in `expo-signature`.
   Binaries built before this change embed the old non-CA certificate with the same `main` key and accept only `main` signatures without a chain.
4. **Upload descriptor additions:** client-supplied `updateGroupId` (group by it), `criticalMessage` (up to 200 characters, implies critical), `sourceMaps[]` (`part`, `path`, `key`, `hash`, `contentType`, `size`, `bundleKey`) with `sourcemap-<md5>` file parts, stored privately and never served.
5. **Manifest:** serve `extra.criticalMessage` when set.
6. **Property:** `mobile.latestBinaryVersion` (same shape as `minimumBinaryVersion`) drives a dismissible "new version available" hint.

---

## As-built notes (remote content, ReleaseManager, garbage collection, symbolication, targeting; 2026-11-12)

These notes record what was built and supersede the sections above where they differ.

1. **Schema.** Migration 80 "Remote strings, banners, assets, and update failure details" adds `appstringcatalog` (the per-property copy of the engine string catalog, re-synced whenever the embedded catalog's SHA-256 changes), the temporal revision tables `remotestrings` (keyed by `stringkey`; a revision with `value` NULL and state `Archived` removes the override), `banners` and `bannerclients` (banner targeting rules are `featureflagrules` rows with `ownertype = 'Banner'` against the banner revision), `remoteassets` (main and dark variant hashes, sizes, and dimensions on the revision), `updateeventdetails` (private message and stack trace of failure events), and `idx_updateassets_gc`. Every table is property-owned and in `PropertyTables`. There is no separate content asset table: `app-content/{propertyId}/{sha256}` objects are referenced by `remoteassets` revisions.
2. **Publishing.** Strings, banners, and assets publish inside the same transaction as flag drafts (`POST /api/v1/admin/app/publish`); the change log uses object types `RemoteString` (object id = the catalog key), `Banner`, and `RemoteAsset`; `ContentVersion.changeCounts` carries real counts; restore rebuilds all four types; discard accepts `ffl_`, `rst_` (or a string key), `ban_`, and `ras_` ids. `GET /api/v1/admin/app/drafts` keeps `objects` as flag drafts and adds `counts { flags, strings, banners, assets }`. Publishing an asset draft without content is `400 ValidationFailed` (`fieldErrors["assets.<id>"]`). Banners and assets use archive and restore (draft changes) plus `DELETE` for never-published objects, like flags; a string `DELETE` discards the draft or drafts the removal of a published override (`pendingDelete`).
3. **String catalog.** `shared/scripts/strings-catalog.mjs` (`npm run strings:catalog --workspace shared`, `--check` for CI) parses (never runs) `www/src/strings.js` (`strings`, keys `web.<path>`) and `mobile/src/strings.js` (`stringTemplates` as `mobile.<path>` and `placesTemplates` as `mobile.places.<path>`), keeps string leaves only (functions and arrays are not overridable), and writes `shared/src/strings/catalog.json` and the server's embedded copy `backend/src/TrellisKit.Server/Assets/string-catalog.json` (so the server image needs no file from `shared/`). `maxLength` is twice the default, at least 200 and at most 2000. Labels and tags of a remote string attach to its catalog key (target type `RemoteString`, target id = key).
4. **Manifest parts.** Strings and assets are full snapshots without `since` and deltas after it; banners are always the complete active list for the caller (published, not archived, inside the schedule, for the client, rules matched on the server, at most `appConfig.maxActiveBanners`) plus `deletes` of changed banners that are not active. `fullRefresh` (with null parts) also answers a `since` ahead of the current version and a full snapshot above `appConfig.maxDeltaItems`. Client banners carry `contentHash` (SHA-256 of title and body, 16 hex characters) for the dismissal rule of section 14.1 and never carry rules. All three re-enumeration routes return `AppContentPageView` (`EnumerationResult` plus `contentVersion`). Banners enumerate the independently bounded active set with standard `maxResults`, `skip`, continuations, priority/created ordering, search, dates and visible metadata filters. Tokens bind caller/client context, content version and visible banner revisions. Clients follow every page before replacing their cached snapshot; an incomplete retrieval preserves the previous snapshot.
5. **Remote assets.** Uploads (`PUT .../content`, `.../dark-content`) accept PNG, JPEG, and WebP up to `appConfig.maxAssetMegabytes`, are decoded with the engine image processor, scaled down to a 4096 pixel long edge, re-encoded to WebP quality 90 (metadata dropped), and stored once per hash. Local delivery is `GET /api/v1/app/content-assets/{propertyId}/{sha256Hex}` (immutable caching, ETag, 304, and a single byte `Range`: `206` with `Content-Range`, `416` outside the asset); it serves only hashes some revision references. `updates.contentAssetBaseUrl` points descriptors at a CDN instead.
6. **ReleaseManager.** A global role (`resourceType: Global`). It reaches every `/api/v1/admin/update-groups/**`, `/api/v1/admin/updates/**`, and `/api/v1/admin/app/**` route (updates, rollouts, plans, rollback, promotion, channels, runtime versions, health, failures, symbolication, flags, kill switches, strings, banners, assets, drafts, publish, versions) and is staff for the metadata of those objects; `/api/v1/admin/updates/signing-keys/**` and `/api/v1/admin/updates/deploy-keys/**` stay SystemAdmin only, as does every other admin route. This widens section 13.3 (which allowed only kill switches and read-only updates) as the property owner asked. `userRole` targeting sees `ReleaseManager`. The admin console shows ReleaseManager holders Home and the Releases group without Signing and deploy keys.
7. **UpdateAssetGcWorker.** Per property, job lease `update_asset_gc`, every `updates.assetGcIntervalHours` (168), retention `updates.assetGcRetentionDays` (30, measured from `updateassets.lastreferencedutc`), batches of `updates.assetGcBatchSize` (200), at most 50 batches per pass. Each batch deletes the rows in one statement that re-checks `updateassetrefs`, then the objects. Metrics `trelliskit.updates.gc.deleted`, `trelliskit.updates.gc.bytes` (label `kind: asset`), and `trelliskit.updates.gc.runs` (`outcome`). Since migration 91 the same pass, under the same lease, also collects private source maps of deleted draft groups and content asset images: deleting a draft group queues its source maps in `updategcobjects` (kind `SourceMap`), and every stored `app-content/` image is queued on upload (kind `ContentAsset`; the migration backfills the hashes remote asset revisions already reference). Queued objects older than `updates.assetGcRetentionDays` that something references again (an `updatesourcemaps` row with the hash, or a `remoteassets` revision with it as `sha256hex` or `darksha256hex`) restart their wait; unreferenced ones are deleted in batches of `updates.assetGcBatchSize` (at most 50 batches per kind) by a statement that re-checks the references, then their objects. Metrics use `kind: sourcemap` and `kind: content`. Content images stored before migration 91 that no revision referenced then are not queued.
8. **Symbolication.** Clients may attach `message` (500 characters) and `stackTrace` (16384 characters) to `LaunchFailed`, `DownloadFailed`, and `CheckFailed` events; they are stored in `updateeventdetails` for `updates.eventRetentionDays` and never listed or served (the failure list shows only `hasStackTrace`). `POST /api/v1/admin/updates/{updateId}/failures/enumerate` lists them; `POST /api/v1/admin/updates/{updateId}/symbolicate` (`{ stackTrace }` or `{ eventId }`) parses Hermes, V8, and JavaScriptCore frames (at most 200) and maps each through the update's private source map (the one whose `bundleKey` is the launch asset, else the first) with a server-side Base64 VLQ decoder; reported columns are treated as 1-based. Maps above `updates.maxSourceMapMegabytes` (100) are `413`; index maps (`sections` with embedded `map`s at generated `offset`s, in order, nested at most 4 deep, at most 10000 sections) are decoded section by section: a position maps through the last section starting at or before it, relative to the section's offset (the column only on the section's first line); `url` sections and unreadable maps are `422 SourceMapInvalid`. The most recently used decoded map is cached per task. Metric `trelliskit.updates.symbolications` (`outcome`). The mobile app records the last fatal JavaScript error and attaches it to the next `LaunchFailed` event for that update.
9. **Targeting.** `profileField` (`Equals`, `In`) names the field in `argument` or, without one, in `values[0]` (candidates are then the remaining values); it matches the signed-in caller's option keys, or the text, number, boolean, date, or level of the field, case-insensitively; only filterable fields are accepted (`400 InvalidTargetingRule`), and anonymous callers never match. Values are loaded for signed-in manifest callers and for kill switch rules that use the attribute. `runtimeVersion` (and `channel` when `X-Client-Channel` is absent) come from the install row of `X-TK-Install-Id` (the HMAC the update manifest endpoint stores) for mobile manifest callers, kill switch rules that use them, and the Test as preview's `installId`.
10. **Kill switch responses.** `503 FeatureDisabled` bodies add `feature` (the engine feature key) next to `error` and `message`.
11. **Settings added.** `updates.contentAssetBaseUrl` (`TK_UPDATES_CONTENT_ASSET_BASE_URL`), `updates.assetGcRetentionDays`, `updates.assetGcBatchSize`, `updates.assetGcIntervalHours`, `updates.maxSourceMapMegabytes`, `appConfig.maxStringOverrides` (2000), `appConfig.maxBanners` (100), `appConfig.maxActiveBanners` (20), `appConfig.maxRemoteAssets` (200), `appConfig.maxAssetMegabytes` (10), `appConfig.maxDeltaItems` (500), `rateLimits.appContentEnumeratePerHour` (60).
12. **Clients.** `shared/src/appContent.js` provides the manifest store (cache, `since` deltas, full refresh re-enumeration, `noteFeatureDisabled`), `remoteStringOverlay` (a read-through proxy over a strings module, so overrides replace keys without editing call sites; the module object is never modified), banner and asset helpers, and the new `ApiClient` methods. The website and the mobile app apply flags, kill switches, string overrides, banners (dismissible slots per placement), and remote assets (Cache API on the web, files by hash on mobile) at runtime. Fallback manifest (section 11.3): `shared/scripts/app-manifest.mjs` builds it at build time from `TK_APP_MANIFEST_FILE` (a saved anonymous manifest body) or the anonymous `GET /api/v1/app/manifest` of `TK_APP_MANIFEST_URL` (the API origin, 5 second timeout), reduced by `fallbackManifestOf` to a full snapshot of flags, kill switches, string overrides, banners, asset descriptors, the content version, and the check interval (never the binary policy); without a source, or when it cannot be read, it is the empty fallback, so builds never fail on it. The website inlines it as `virtual:trelliskit/app-manifest` (Vite plugin) and the mobile app bundles `mobile/assets/app-manifest.json` (committed empty; `npm run manifest:bundle`, run by the `eas-build-post-install` hook, rewrites it). Both stores start from it only when nothing is cached, and the first online fetch asks for changes since its version. Kill switches gate the mobile app like the website: `FeatureGate` and `CatalogGate` show the operator's message with a forced recheck instead of the feature (`feed`, `forums`, `search`, `messaging`, `assistants`, and the `library`, `events`, and `directory` catalogs), `feedComposer` hides new posts on the feed and turns the compose screen off, and `comments` replaces the comment composer with a notice.

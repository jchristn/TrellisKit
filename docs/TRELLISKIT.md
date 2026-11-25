# TrellisKit: Engine and Properties

Status: binding specification for the engine/property split (2026-11-11). All teams implement it exactly.

**TrellisKit** is the reusable community and membership engine (trelliskit.com). A **property** is a configured personality (name, brand, copy, domains, legal text, profile taxonomy, reactions, products) that runs on the engine. Nothing in engine code may name a property, a brand, a sport, a person, a domain, a price, or a store product id. Those come from the property configuration.

Deployments run in `SingleProperty` mode by default or `MultiProperty` mode with isolated property data (section 10). Real properties live in separate repositories selected by directory path. Adding a property requires configuration, not property-specific engine code.

---

## 1. Repository layout

This standalone repository contains the engine and its neutral sample. Real properties have their own repositories and build workflows. A typical local workspace uses sibling checkouts; absolute property paths work as well.

```text
TrellisKit/                         this repository
  backend/src/TrellisKit.sln        TrellisKit.Core, TrellisKit.Server, Test.*
  www/                             engine web app, package "@trelliskit/www"
  mobile/                          engine mobile app, package "@trelliskit/mobile"
  shared/                          package "@trelliskit/shared"
  docker/                          engine stack, parameterized by property
  infra/src/TrellisKit.Infra*       AWS CDK app, parameterized by property
  assets/                          Grafana dashboards, Postman collection
  samples/example/                 neutral sample property (the default)
  docs/                            engine contracts and architecture
  .github/workflows/               engine tests.yml and infra.yml
<property>/                        separate property repository
  property.json                    identity and configuration (section 2)
  brand/                           logos and icons
  web/content/*.md                 marketing copy
  legal/*.md                       legal documents
  server/settings.json             server settings defaults
  mobile/                          generated app assets and public update certificate
  email/                           optional email templates and layout
  deploy/                          property-owned deployment selection and context
  .github/workflows/               property builds with a pinned engine checkout
```

All engine paths in this specification are relative to the TrellisKit repository root unless stated otherwise. `TK_PROPERTY_DIR` is absolute or relative to that engine root. Docker `deploy/local.env` selection is resolved relative to the engine's `docker/` directory. A property's CI checks out its pinned engine revision separately and supplies the absolute property path; engine CI runs only neutral fixtures.

### Cross-cutting engine rules

- **Deployment modes:** single-property or multi-property (multi-tenant) with per-property data isolation, separate accounts per property, and property resolution on every request (section 10).
- **Configurable profile fields:** administrators define collected and displayed profile fields per property (section 11).
- **Administrative email:** lifecycle emails (sign-up, subscription, cancellation, payment failure, moderation, security, and staff notices) rendered with HtmlPageBuilder and sent through Mailgun with SendWithMailgun (section 12).
- **Data platform:** PostgreSQL only (SQLite removed), pgvector, and RecallDB in the Docker stack with an init script for databases and credentials (section 13).
- **Property documents:** terms, policies, waivers, and other documents declared in `property.json` `documents`, versioned (seeded from the folder, published by administrators), served at public slugs, and accepted by members at sign-up and again when a version requires it (section 14).
- **AI:** admin-managed LLM model endpoints and AssistantHub-style assistants on PolyPrompt (section 9).
- **Live updates:** self-hosted over-the-air code updates (Expo Updates protocol) and content/config updates (feature flags, kill switches, strings, assets) for every property (section 8).
- **Access models:** each property chooses `Open` (no payments), `PartiallyGated`, or `FullyGated` (section 7).
- **Labels, tags, and enumeration:** every object carries `labels` and `tags`, every retrieval of many objects uses `EnumerationQuery` and `EnumerationResult`, and no route returns an unbounded list. Binding specification: `docs/ENUMERATION_AND_METADATA.md`.

### Separate engine and property repositories

The engine and each property remain independent repositories. Rules:

- **Engine code never references a specific property.** No path, id, name, or default pointing at a real property anywhere in `backend/`, `www/`, `mobile/`, `shared/`, `docker/` (engine files), `infra/src`, engine docs, or engine tests.
- **The engine ships a neutral sample property** at `samples/example/` (Example Community). It is the default when no property is selected, and every engine test, fixture, CI job, and doc example uses it (or test-local fixtures), never a real property.
- **A property is selected only by directory path:** `TK_PROPERTY_DIR` (absolute, or relative to the engine root). It is the single selector for the backend (setting `property.directory`), the website build, the mobile build (`app.config.js`), Docker (`docker/.env` or the shell environment; the folder is mounted read-only at `/app/property` and the website build receives it as a build context), and infra (CDK context `propertyDir`). Default when unset: `samples/example`.
- **Property-owned deployment values** (which property, its domains, AWS account ids, store ids) live in the property folder or are passed at deploy time, never committed in engine files. Each real deployment points `TK_PROPERTY_DIR` at its property folder.
- **Layout:** this repository contains the engine at its root; each real property lives outside it (section 1). Paths resolve relative to the engine component or from `TK_PROPERTY_DIR`, never from an assumed umbrella checkout.

---

## 2. `property.json`

Public by design: it never contains secrets (secrets stay in server settings and environment variables). The backend serves its public subset at `GET /api/v1/property`.

```json
{
  "schemaVersion": 1,
  "id": "app_mwczw7mg_F8RsDcNidltPWZS1seB",
  "slug": "example",
  "name": "Example Community",
  "shortName": "Example",
  "tagline": "Learn together",
  "description": "A members-only community for an example club.",
  "owner": { "name": "Example Owner", "copyrightHolder": null },
  "locale": "en-US",
  "domains": {
    "web": "example.test",
    "api": "api.example.test",
    "media": "media.example.test",
    "redirects": []
  },
  "contact": { "supportEmail": "support@example.test", "emailFromName": "Example Community", "emailFromAddress": "no-reply@example.test" },
  "brand": {
    "logo": "brand/logo.png",
    "icon": "brand/icon.png",
    "iconIco": "brand/icon.ico",
    "colors": {
      "light": { "primary": "#1F5FBF", "primaryHover": "#194D9C", "onPrimary": "#FFFFFF", "accent": "#1F2933", "onAccent": "#FFFFFF", "accentSoft": "#E6EEF9", "onAccentSoft": "#1F5FBF", "themeColor": "#1F2933" },
      "dark":  { "primary": "#8DB4F0", "primaryHover": "#A8C6F4", "onPrimary": "#101820", "accent": "#F3F4F6", "onAccent": "#101820", "accentSoft": "#1C2A40", "onAccentSoft": "#8DB4F0", "themeColor": "#101820" }
    }
  },
  "mobile": { "bundleId": "com.example.community", "androidPackage": "com.example.community", "scheme": "examplecommunity", "easProjectId": "" },
  "membership": {
    "productName": "Example Community Membership",
    "priceUsdCents": 2999,
    "interval": "Year",
    "storeProductId": "example.membership.annual",
    "minimumAge": 18,
    "trialDays": 0
  },
  "profile": {
    "affiliation": { "enabled": true, "label": "Club" },
    "rank": {
      "enabled": true,
      "label": "Level",
      "levelLabel": "Stars",
      "maxLevel": 3,
      "values": [
        { "key": "Bronze", "label": "Bronze", "color": "#A0662D" },
        { "key": "Silver", "label": "Silver", "color": "#9AA3AD" },
        { "key": "Gold", "label": "Gold", "color": "#C9A227" }
      ]
    },
    "rankHistory": { "enabled": true, "label": "History", "awardedByLabel": "Awarded by", "maxEntries": 12 },
    "goals": {
      "enabled": true,
      "label": "Goals",
      "values": [
        { "key": "Networking", "label": "Networking" },
        { "key": "Learning", "label": "Learning" },
        { "key": "Fitness", "label": "Fitness" },
        { "key": "Technique", "label": "Technique" },
        { "key": "Teaching", "label": "Teaching" },
        { "key": "Community", "label": "Community" },
        { "key": "Other", "label": "Other", "freeText": true }
      ]
    }
  },
  "reactions": [
    { "key": "like", "emoji": "👍", "label": "Like" },
    { "key": "dislike", "emoji": "👎", "label": "Dislike" },
    { "key": "heart", "emoji": "❤️", "label": "Love" },
    { "key": "laugh", "emoji": "😂", "label": "Laugh" },
    { "key": "fire", "emoji": "🔥", "label": "Fire" },
    { "key": "star", "emoji": "⭐", "label": "Star" },
    { "key": "mindblown", "emoji": "🤯", "label": "Mind blown" },
    { "key": "thanks", "emoji": "🙏", "label": "Thanks" },
    { "key": "hug", "emoji": "🤗", "label": "Hug" },
    { "key": "idea", "emoji": "💡", "label": "Idea" }
  ],
  "features": { "signupCodes": true, "directory": true, "events": true, "library": true, "messaging": true },
  "web": { "theme": "web/theme.css", "content": "web/content", "legal": "legal", "storeBadges": false },
  "terminology": { "member": "member", "members": "members", "community": "community" }
}
```

Rules:
- `rank`, `rankHistory`, `goals`, `affiliation`, and `features` may each be disabled; clients hide disabled fields and the backend rejects values for disabled fields.
- Keys (`rank.values[].key`, `goals.values[].key`, `reactions[].key`) are the stable values stored in the database and sent over the API. Labels are display text and can change freely.
- The backend validates `property.json` at startup (schema version, required fields, unique keys, color format, positive price) and refuses to start with a message naming the problem.
- Reactions in the property replace the reactions setting in the Phase 2 contract; the Phase 2 contract is updated to read them from the property.

### 2.1 Brand palette (seeded from `property.json`, edited in the admin portal)

Decided 2026-11-12: brand colors are configurable both in `property.json` (the seed) and from the admin dashboard, with no rebuild.

- **Keys.** Each scheme (`light`, `dark`) has `primary`, `primaryHover`, `onPrimary`, `accent`, `onAccent`, `accentSoft`, `onAccentSoft`, and `themeColor`, each `#RRGGBB`. `onAccentSoft` is the text and icon color on `accentSoft` (selected chips, active navigation, avatar initials, soft alerts); it is optional in `property.json` and defaults to `primary`, so a property can keep a bright `primary` for fills and buttons while using a darker shade for text on the soft accent.
- **Storage and versions.** The palette is stored per property (tables `propertybrandpalettes`, `propertybrandcolors`, `propertybrandoverrides`, migration 71), seeded on the property's first load. Every change is a new version (source `Seed`, `Edit`, `RevertPrevious`, `RevertSeed`, `DirectorySync`); administrators can revert to the previous version or to the `property.json` seed. At most `property.maxPaletteVersions` (20) versions are kept.
- **The folder wins on change** (MULTIPROPERTY_CONTRACT resolved decision 15). In `SingleProperty`, when the `brand.colors` section of `property.json` changes on disk, it replaces the stored palette on the next start as a new version (audited `property.branding.sync`); while it is unchanged, admin edits stand. In `MultiProperty` the database is the source after the first load.
- **Who edits.** Property administrators (SystemAdmin, the admin token, a delegated platform session) at `/api/v1/admin/property/branding`, and platform administrators for any property at `/api/v1/platform/properties/{propertyId}/branding`. Every edit, revert, and override is audited. Shapes are in `REST_API.md` ("Admin: branding and the AI switch").
- **Accessibility (WCAG 2.1 AA), enforced on the server.** Seven pairs are checked in each scheme: `onPrimary` on `primary` and on `primaryHover`, `onAccent` on `accent`, `onAccentSoft` on `accentSoft`, and `primary` on the engine page background (normal text, 4.5:1); `primary` on `accentSoft` and `accent` on the page background (components, 3:1). The page background is the engine's (light `#FFFFFF`, dark `#0E141D`). An edit with a failing pair is rejected with a field error per pair (`contrast.<scheme>.<pair>`) naming both colors and the ratio, unless the administrator overrides that pair explicitly; an override is stored with the version and recorded in the audit trail as a warning. Seeded palettes are never refused (failing pairs are logged and shown failing on the Branding page). `shared/src/brand.js` mirrors the backend's math and pairs so clients show the same numbers.
- **Clients.** `GET /api/v1/property` serves the effective palette as `brand.colors` (every key) with `brand.paletteVersion`, and its `ETag` changes with the version. The website builds its theme from `property.json` (CSS custom properties) and, once `GET /api/v1/property` loads, replaces them at runtime with the live palette (a `<style>` element using `html:root`, so it wins over the build theme); the last live palette is kept in `localStorage` and applied by the inline head script before the first paint, so prerendered pages do not flash their build-time colors after the first visit. The mobile app builds its theme tokens from the bundled property and then from the cached and ETag-revalidated `GET /api/v1/property`, so a palette edit reaches the app at the next launch or foreground. App icons, the splash screen, the web manifest, favicons, and the Open Graph image are generated at build time and do not follow runtime edits (rebuild to change them).
- **Admin UI.** Admin, System, Branding (and the platform console's per-property Branding tab, the same component): color pickers for every key in both schemes, a live preview of navigation, buttons, cards, chips, avatar initials, links, alerts, and accent surfaces in both themes, the contrast table with every pair's ratio and pass or fail, per-pair overrides, presets (the `property.json` seed, the previous version, and the engine palette presets below), save, and revert.
- **Palette presets (decided 2026-11-14: administrators and members both pick).** The engine ships a catalog of neutral, named palettes (16 today, at most 32), each with every key in both schemes and each passing every pair above in both schemes without an override (asserted by the backend `PalettePresets` suite and `shared/test/palettePresets.test.js`). The catalog is defined once, in `backend/src/TrellisKit.Core/Assets/palette-presets.json` (embedded in the server); clients get it from the API (`BrandingView.presets`, and `GET /api/v1/property` `brand.presets` for the presets members may choose), so hex values are not duplicated across languages.
  - *Administrators.* The Branding page shows the catalog as a gallery with light and dark swatches. "Use this palette" fills the editor (with the note "Preset: <name>"); saving is an ordinary edit, so the preset becomes a new palette version and revert, restore, and history work unchanged. Administrators also choose which presets members may pick (`memberPresets`: `All`, the default, `None`, or `Selected` with a subset) with `PUT /api/v1/admin/property/branding/member-presets` (platform: `PUT /api/v1/platform/properties/{propertyId}/branding/member-presets`), audited `property.branding.memberPresets`. The choice is stored per property (table `propertypalettechoices`, migration 92), reloaded with the property, and part of the `GET /api/v1/property` `ETag`.
  - *Members.* Settings, Appearance on the website (`/app/settings/appearance`) and in the mobile app (Account, Appearance; one column on phones, two on tablets) offer the property palette (always available, the default) and every allowed preset. The choice is stored on the account (`users.palettepreset`, `GET`/`PUT /api/v1/me/appearance`), so it follows the member to every client; it is cleared on account deletion. Clients resolve it against `brand.presets`: an allowed preset replaces `brand.colors` in the runtime palette (the website's `html:root` style, kept in `localStorage` for the next first paint; the mobile theme tokens through `useTheme`, no rebuild), in both light and dark with the member's scheme setting; a preset the administrator stops offering falls back to the property palette (the stored preference is kept and applies again if the preset is offered again). Signing out returns the device to the property palette. Logos, icons, the splash screen, and the build-time assets never change.

### 2.2 Public landing pages (`publicPages`)

The optional `publicPages` section turns the server-rendered landing pages on or off and names the first path segment of each kind (docs/PHASE4_CONTRACT.md section 18). Absent, the engine defaults apply:

```json
"publicPages": { "enabled": true, "venuesPath": "venues", "eventsPath": "events", "profilesPath": "profiles" }
```

- Each path word is 2 to 40 lowercase letters and digits with single hyphens, the three are distinct, and none may be a reserved first segment the website, member app, or server already uses (`account-deleted`, `admin`, `announcements`, `api`, `app`, `assets`, `assistants`, `auth`, `brand`, `embed`, `forgot-password`, `healthz`, `join`, `lessons`, `openapi`, `platform`, `public`, `reset-password`, `sign-in`, `sign-up`, `sitemap`, `swagger`, `threads`, `verify-email`). The backend refuses to start on a bad section; `GET /api/v1/property` serves it as `publicPages` (defaults filled in) so clients build the same URLs.
- A property chooses its own words, for example `"venuesPath": "places"`; the engine never hard-codes them. Page URLs are `web.publicBaseUrl` plus `/<word>/<slug>`.
- `enabled: false` removes every page, every `publicUrl`, the dynamic sitemap children, and the member's page settings route (`404 NotAvailable`); every two-segment path then passes through to the website.

---

### 2.3 Mobile navigation (`mobile.navigation`)

The mobile app has a complete side drawer, opened from the upper-left menu button, and up to five bottom shortcuts on top-level screens. Members is a community destination, not a Settings action. The engine owns routing and access rules; each property owns its ordering and shortcuts:

```json
"mobile": {
  "navigation": {
    "drawerItems": ["feed", "forums", "members", "training", "events", "directory", "messages", "notifications", "announcements", "ideas", "assistants", "profile", "support", "settings", "about"],
    "shortcuts": ["feed", "forums", "messages", "profile"],
    "accountMenu": ["profile", "settings", "support", "about"],
    "labels": { "directory": "Explore" }
  }
}
```

- Supported identifiers (17): `feed`, `forums`, `members`, `library`, `training`, `catalog`, `events`, `directory`, `messages`, `assistants`, `announcements`, `ideas`, `profile`, `notifications`, `support`, `settings`, `about`. Values are case-sensitive identifiers, not arbitrary routes. Arrays must have distinct known entries; `shortcuts` accepts at most five, and `drawerItems` and `accountMenu` at most 17. The server and mobile build loader reject invalid declarations.
- `training` (default label "Training", icon `barbell`, route `/training`, switch `features.training`, default on) is the Training hub. `library` and `catalog` (the technique catalog, `/training/library/techniques`, switch `features.catalog`, default **off**) are embedded in it: they carry `coveredBy: "training"`, so placing `training` on any surface keeps them out of the drawer. A property may still place them itself, older property files and deep links keep working, and when Training is off or hidden for the caller an embedded destination placed nowhere returns to the drawer. Without `drawerItems` the engine drawer lists every destination that is not embedded (`DefaultMobileDrawerItems`).
- Bottom-bar labels longer than 12 characters (`MaxMobileBarLabelLength`) show only the icon, with the label as its accessibility label, at the largest text sizes; the drawer and account menu always show the full label (up to `MaxMobileLabelLength`, 24).
- `accountMenu` lists the destinations shown, in order, in the menu opened from the member's avatar in the header (followed by Sign out). Missing or empty hides the avatar button. Guests never see the button.
- `labels` overrides the label of a known destination, 1 to 24 characters each after trimming. Overrides win over property terminology and the app string catalog. Unknown keys and empty or longer values are rejected.
- Missing/null navigation or arrays use engine defaults (four shortcuts: feed, forums, messages, profile). An explicit empty `shortcuts` array hides the bottom bar. Placement is a preference: a known destination placed on no surface is appended to the drawer, so a configuration cannot make enabled screens unreachable. A destination counts as placed when it is in `drawerItems`, `shortcuts`, or `accountMenu`; when the header always offers it (Notifications, which has the header bell); or when the destination that embeds it is placed (`coveredBy` in the registry). Community destinations and account/help destinations render in separate groups, and an empty account group is omitted.
- The header does not repeat Messages when Messages is a bottom shortcut. The Map screen drops its Places and Events switch when both `directory` and `events` are bottom shortcuts.
- Both surfaces resolve from the same registry (`shared/src/mobileNavigation.js`). Feature switches hide disabled destinations. Assistants requires the server's `features.ai === true`. Guests see only anonymously browsable destinations plus Profile and About; account actions, Members and Messages require sign-in. Partially gated signed-in features stay visible with lock badges. Destination screens and the server enforce authorization; navigation never grants access.
- Labels use the app string catalog. Members uses `terminology.members`, and the venue directory uses `directory.venueLabelPlural`. Events and the venue directory are separate destinations into the existing Map screen (`/map?section=events` and `/map?section=places`). Existing deep-link routes remain valid.
- `GET /api/v1/property` returns the declaration under `mobile.navigation`. A runtime declaration overrides bundled values per key (`drawerItems`, `shortcuts`, `accountMenu`, `labels`). An older server that omits navigation or sends null preserves the bundled declaration. Consequently a client-only release can adopt a property's new navigation before a server upgrade; offline startup uses the bundled configuration.
- `web.navigation` (optional, build input for the website, validated by the server but not served) orders the website's member sections: `primary` lists at most 8 known section ids shown first, in order (the rest follow in engine order), and `labels` overrides section labels (1 to 24 characters). Section ids: `feed`, `forums`, `search`, `members`, `library`, `training`, `events`, `directory`, `messages`, `ideas`, `announcements`, `support`, `assistants`, `notifications`, `profile`. The library is covered by Training on the website too. Both surfaces validate through one shared function, `validateNavigation(surface, navigation)` (`shared/src/navigation.js`), and the server applies the same rules (`PropertyValidator`), so the rules cannot drift.
- Detail screens retain their Back action. The drawer closes after selection, outside tap, accessibility escape, and Android Back. Onboarding and paywall flows retain their existing access gates. The UI supports safe areas, light/dark schemes, scrolling at large text sizes, and tablet widths.

---

## 3. API changes (engine-neutral names)

Phase 1 profile fields are renamed. A migration renames the columns (no data loss); the API uses only the new names.

| Before (Phase 1) | After |
|---|---|
| `belt` | `rank` (a key from `profile.rank.values`, or null) |
| `stripes` | `rankLevel` (0 to `profile.rank.maxLevel`) |
| `academy` | `affiliation` |
| `lineage[]` `{ id, belt, promotedByName, promotedByUserId, academy, dateAwarded }` | `rankHistory[]` `{ id, rank, awardedByName, awardedByUserId, affiliation, dateAwarded }` |
| `goals` (fixed enum) | `goals` (keys from `profile.goals.values`) |
| admin member filter `belt=` | `rank=` |

`profileComplete` becomes: `displayName` and `handle` set, `ageAttested` true, and `rank` set only when `profile.rank.enabled`.

New anonymous route: `GET /api/v1/property` returns the public property configuration: everything in `property.json` except `web`, plus absolute URLs for the brand images (served by the backend at `GET /api/v1/property/assets/{file}` from the property's `brand/` folder, cacheable).

Other renames:
- The web refresh cookie is `tk_refresh`.
- Request headers `X-Client` and `X-Client-Version` are unchanged.
- Access token and refresh token formats unchanged (existing sessions may be invalidated; acceptable).

---

## 4. Engine naming

| Thing | Name |
|---|---|
| .NET projects and namespaces | `TrellisKit.Core`, `TrellisKit.Server`, `TrellisKitServer`, `TrellisKitSuites` |
| Solution | `TrellisKit.sln` |
| Environment variables | `TK_*` (for example `TK_DB_PASSWORD`, `TK_ADMIN_TOKEN`, `TK_SETTINGS_FILE`, `TK_TEST_PROVIDER`, `TK_TEST_PG_*`) |
| Settings file | `trelliskit.json` |
| Telemetry meter, activity source | `TrellisKit`, metrics `trelliskit.*`; Grafana queries `trelliskit_*`; telemetry service name defaults to `trelliskit-server` with resource attribute `trelliskit.property` = property id |
| Product name in OpenAPI, logs, admin console title | the property `name` (engine name "TrellisKit" only in places that describe the engine itself, for example "Powered by TrellisKit" in the admin footer) |
| npm packages | `@trelliskit/shared`, `trelliskit` root, `@trelliskit/www`, `@trelliskit/mobile` |
| Docker | project `trelliskit`, containers `trelliskit-*`; published image `jchristn77/trelliskit-server` (server and website in one container) and `jchristn77/trelliskit-www` (website only); development images `trelliskit/server:local` built from `TK_PROPERTY_DIR`; Postgres database and user `trelliskit` |
| Infra | `TrellisKit.Infra`, stacks `<PropertyId>-<Env>-*`, all names and domains from the property |
| PrettyId prefixes, database table names | neutral |

---

## 5. Clients

- **Website:** brand, theme tokens, logos, favicon, manifest, OG image, page titles, meta, JSON-LD, sitemap, footer, marketing copy, and legal pages all come from the active property at build time (and `GET /api/v1/property` at runtime for the member app and admin). `src/strings.js` keeps engine UI strings only; any string containing the property name uses a `{name}` placeholder filled from the property. The profile editor, member cards, and admin filters render rank, level, history, affiliation, and goals from `profile` config, hiding disabled sections.
- **Mobile:** `app.config.js` takes name, slug, scheme, bundle id, package, icons, splash, and colors from the property; the theme reads colors from the property; strings use `{name}`; profile UI is driven by `profile` config fetched from `GET /api/v1/property` (with a bundled copy as fallback for first launch offline).
- **Shared:** exposes property helpers (load, validate shape, look up rank labels and colors, format "Gold rank, level 2" style text from config without hard-coded words) used by both apps.

---

## 6. Non-goals

- No multi-tenancy (one property per deployment).
- No runtime switching of properties.
- No change to Phase 1 behaviour other than the renames above.

---

## 7. Access models (payment and gating)

Each property chooses how members pay and what is gated. The engine supports three models, configured in `property.json` under `access`. Absent `access` means `FullyGated` (the original behaviour).

```json
"access": {
  "model": "FullyGated",
  "anonymousBrowsing": false,
  "gatedFeatures": ["feed", "forums", "library", "events", "directory", "messaging"],
  "defaultAudience": "Members"
}
```

| Model | Payments | What requires what |
|---|---|---|
| `Open` (no payments) | None. Stripe, App Store, and Google Play purchase flows, paywall screens, membership pages, and sign-up codes are hidden and their routes return `404 NotAvailable`. `membership` in `property.json` is optional | Everything requires only a signed-in account (age attestation still applies). With `anonymousBrowsing: true`, objects whose `audience` is `Public` can be read signed out |
| `PartiallyGated` | Optional membership, sold exactly as today | Features listed in `gatedFeatures` require an active membership; other features need only sign-in. Individual objects can also be gated by `audience` (below), so a free feed can carry members-only posts and a members-only library can expose a free sample lesson |
| `FullyGated` | Required membership (the original behaviour) | Everything beyond account, profile, and purchase requires an active membership; `gatedFeatures` and `audience` are ignored except `Public` sample content (Phase 2 public sample posts) |

**Audience on content.** Every content object (feed posts, comments inherit from their parent, media inherits from its owner object, forum threads, lessons, events, and later objects) has `audience`: `Public` (readable signed out when `anonymousBrowsing` is true, and shown as samples on the marketing site), `SignedIn` (any signed-in account), or `Members` (active membership). New objects default to `access.defaultAudience`. Audience is a filterable field in every enumeration of content.

**Enforcement.** One engine authorization component decides access for every route from (model, feature, object audience, caller state). Clients read `access` from `GET /api/v1/property` and show locked items with an upgrade prompt only in models with payments; in `Open` they never show prices, paywalls, or codes. Store and policy rules still apply: the mobile apps never show sign-up codes or web pricing.

**Testing.** Engine tests cover all three models with test fixtures; the sample property stays `FullyGated` until the access-model milestone lands, then switches to `PartiallyGated` so the engine's default exercises the most general case.

**As built (access-model milestone).** The configuration is stored per property (seeded from `property.json` `access`, edited at `PUT /api/v1/admin/property/access`, audited), and the sample property is `PartiallyGated`. The exact route-by-model behaviour, the denial bodies, locked stubs, payments in `Open`, the client-visible shapes, and the list of client changes are in `docs/ACCESS_MODELS_CONTRACT.md`, which is binding.

---

## 8. Live updates (over the air)

Apps update themselves when opened, without an app store release, in two layers. Both are engine features available to every property; both obey `docs/ENUMERATION_AND_METADATA.md` (update records, flags, and assets carry labels and tags, and every list is an enumeration).

Store policy: Apple Guideline 3.3.1(b) and Google Play policy allow interpreted code and asset updates that do not change the app's primary purpose or bypass review of new native capabilities. Native changes (new native modules, permissions, SDK upgrades) always ship through the stores.

### 8.1 Code updates: self-hosted Expo Updates

TrellisKit serves the Expo Updates protocol (v1) itself; there is no dependency on Expo's hosted service.

| Part | Design |
|---|---|
| Manifest endpoint | `GET /api/v1/updates/manifest` (anonymous; the app calls it on launch). Reads `expo-platform`, `expo-runtime-version`, `expo-channel-name`, `expo-current-update-id`, and an install id header. Returns the newest published, compatible update for that platform, runtime version, and channel (multipart manifest per the protocol), a `rollBackToEmbedded` directive, or the protocol's signed `noUpdateAvailable` directive when the app is current |
| Assets | Bundles and assets are content-addressed (SHA-256) and served from the `updates` storage location: S3 plus CloudFront in AWS, the backend's disk store locally. The manifest lists each asset's hash, key, and URL; clients verify hashes |
| Code signing | Mandatory. Manifests are signed (RSA-SHA256, the expo-updates code signing scheme); the app embeds the certificate and rejects unsigned or tampered manifests. The private key lives in Secrets Manager (`TK_UPDATES_SIGNING_KEY`), never in the repository; the certificate is generated per property and committed to the property's `mobile/` folder |
| Runtime version | Policy `fingerprint` (a hash of the native layer). An update is only served to binaries with the same runtime version, so JavaScript never runs against incompatible native code |
| Channels | `production`, `preview`, `development` per property, set at build time (EAS build profile or local build), so TestFlight and store builds can receive different updates |
| Publishing | `npm run publish-update -- --channel production --message "..."` in `mobile/` runs `expo export` for iOS and Android, then uploads the bundles and assets to `POST /api/v1/admin/updates` (admin token or CI deploy credential). CI can publish on tags. Each upload creates an update record: id, platform, runtime version, channel, message, git commit, created time, status (`Draft`, `Published`, `RolledBack`), rollout percentage, labels, tags |
| Staged rollout | Publish to a percentage (deterministic by install id), raise it in steps, or pause. Rollback republishes the previous update or issues `rollBackToEmbedded` |
| Apply behaviour | The app checks on launch and on return to foreground (at most every 15 minutes, setting `updates.checkIntervalMinutes`), downloads in the background, and applies on the next launch. Updates marked `critical` show a "Restart to update" prompt immediately |
| Minimum native version | Setting `mobile.minimumBinaryVersion` per platform. Older binaries see a blocking "Update required" screen with the store link (for native changes that code updates cannot deliver) |
| Password autofill | Optional `mobile.appleTeamId` (public Apple Team ID) and `mobile.androidCertFingerprints` (SHA-256 of the Play signing and upload certificates). With them the website serves `/.well-known/apple-app-site-association` (webcredentials) and `/.well-known/assetlinks.json` (get_login_creds), and the iOS build declares `webcredentials:<domains.web>`, so iOS and Android offer to save the app's passwords and share them with the website |
| Admin console | Updates page: enumerate updates with filters (platform, channel, runtime version, status, labels, tags), publish, adjust rollout, pause, roll back, and see adoption (installs on each update) |
| Telemetry | Apps send `X-Client-Update-Id` on API calls; metrics `trelliskit.updates.checks`, `.downloads`, `.applied`, `.failures` by platform and channel; adoption per update |

### 8.2 Content and configuration updates

Server-published, versioned content that every client (mobile, tablet, and web) fetches on open and caches, so behaviour and content change without any code release.

| Part | Design |
|---|---|
| App manifest | `GET /api/v1/app/manifest?since=<version>` returns the current content version and only what changed since `since`: public property config, remote string overrides, feature flags, announcements and banners, and remote asset descriptors (hash, URL, size). Bounded: changes beyond 500 items return `fullRefresh: true` and the client re-enumerates |
| Feature flags and kill switches | Engine-level flags with targeting by platform, app version range, update id, user labels and tags, membership state, and percentage rollout. Kill switches disable a feature everywhere within one manifest check. Flags are labeled and tagged objects managed in the admin console |
| Remote strings | Per-property overrides of engine UI strings (with `{name}` tokens), validated against the engine's string keys |
| Remote assets | Onboarding images, banners, and other content images, downloaded once by hash and cached (mobile file system; web Cache API) |
| Publishing | Admin console: edit flags, strings, announcements, and assets as drafts, then publish a new content version (audited). Rollback restores a previous version |
| Offline | Clients start from the last cached manifest (or the copy bundled at build time) and refresh in the background |

### 8.3 Infrastructure

An `updates` storage location (S3 bucket plus a CloudFront path behind the API domain) and the code signing secret are added to `infra/`; locally the Docker stack serves updates from its media volume. Self-hosting means no per-user fees and no third-party dependency for app updates; the trade-off is that TrellisKit owns availability of the manifest endpoint, so it is covered by the API's health checks and alarms.

---

## 9. AI: model endpoints and assistants

Lower priority than the community phases: designed now, built incrementally (milestone "Engine: AI models and assistants" in the property plan). Engine feature, available to every property, off by default (`features.ai` in `property.json`). The backend uses the **PolyPrompt** NuGet package (4.0.0 or later; one client per provider) for every model call; TrellisKit never hand-rolls provider HTTP. Design references: Armada `ModelEndpoint` (auth model, write-only secrets), Pneuma `ModelRunner` (encryption at rest, health checks, concurrency), AssistantHub (assistants with per-purpose model slots, public chat).

### 9.1 Model endpoints (admin portal)

`ModelEndpoint` (prefix `mep_`), managed under Admin, then AI, then Model endpoints:

| Field | Notes |
|---|---|
| `name`, `description`, `enabled` | |
| `kind` | `Completion`, `Embedding`, `Rerank` |
| `apiType` | `Ollama`, `OpenAI`, `OpenAICompatible`, `Gemini`, plus the other PolyPrompt providers as they are enabled (`AzureOpenAI`, `Anthropic`, `Bedrock`, `VertexAI`, `OllamaCloud`, `XAI`, `Cohere`, `VoyageAI`) |
| `model` | Model name (for example `llama3.1:8b`, `gpt-4.1-mini`, `gemini-2.5-flash`) |
| `endpointUrl` | Base URL |
| `authType` | `None`, `Bearer` (Authorization: Bearer value), `ApiKey` (the provider's native API key mechanism), `Header` (custom header name and value), `QueryParameter` (parameter name and value), `Basic` (username and password) |
| `authHeaderName`, `authQueryParameter`, `authUsername` | Used by the matching `authType` |
| `authValue` | Secret. Write-only in the API (`authValue` accepted on create and update; reads return only `hasAuthValue`), encrypted at rest with the engine's secret key (AES-256-GCM), redacted from logs, request history, and PolyPrompt call details. Admin-only audited `POST .../reveal` for operators |
| `extraHeaders` | List of `{ name, value }`; values write-only like `authValue` |
| `providerSettings` | `region`, `project`, `apiVersion`, `deployment`, `accessKeyId` for the providers that need them |
| `parameters` | `temperature`, `topP`, `maxTokens`, `contextWindow`, `reasoningEffort`, `streaming`, `timeoutMs`, `firstResponseTimeoutMs`, `dimensions` (embeddings) |
| `limits` | `maxConcurrentRequests`, `maxQueueDepth`, `maxRetries` |
| `healthCheck` | `enabled`, `intervalMs`, `timeoutMs`, `healthyThreshold`, `unhealthyThreshold`; results: `healthStatus`, `lastCheckedUtc`, `lastLatencyMs`, `lastError`, uptime percentage over a bounded rolling window |
| `labels`, `tags`, `createdUtc`, `updatedUtc` | Per the enumeration and metadata spec |

Routes: `POST /api/v1/admin/ai/endpoints/enumerate` (and the GET form), CRUD on `/api/v1/admin/ai/endpoints/{id}`, `{id}/test` (send a short prompt and report latency and output), `{id}/models/enumerate` (PolyPrompt `ListModelsAsync`, bounded), `{id}/health` and `{id}/health/check`, `{id}/duplicate`, `{id}/reveal`. A background worker runs health checks. Telemetry: requests, tokens, latency, and errors per endpoint and purpose.

PolyPrompt mapping: each endpoint becomes the matching PolyPrompt client (`OllamaCompletionClient`, `OpenAiCompletionClient`, `GeminiCompletionClient`, embedding and rerank clients), built by one engine factory. `Header` and `QueryParameter` auth are applied through a pre-configured `HttpClient` handler passed to PolyPrompt; completions use `ChatAsync` / `ChatStreamingAsync` / `ToolChatAsync` with `CompletionOptions` from `parameters`; embeddings use `EmbedAsync`.

### 9.2 Assistants

`Assistant` (prefix `ast_`), managed under Admin, then AI, then Assistants:

| Field | Notes |
|---|---|
| `name`, `description`, `enabled`, `title`, `avatar` | Shown in the chat UI |
| `systemPrompt` | With `{name}`-style property tokens available |
| `models` | Endpoint ids per purpose: `chat` (required), `titleGeneration`, `queryRewrite`, `embedding`, `rerank`, `moderation`, `toolRouting` (each optional; unset purposes skip that stage or fall back to `chat`) |
| `audience` | Who may chat: `Public`, `SignedIn`, or `Members`, evaluated with the property's access model (section 7) |
| `knowledge` | Optional grounding on engine content (feed posts, library lessons, forum threads) selected by labels and tags, retrieved through bounded enumerations; vector retrieval arrives in a later step |
| `tools` | Optional allow-list of engine tools the assistant may call (for example "search the library", "find a gym"), all read-only and permission-checked as the chatting member |
| `limits` | Messages per member per day, maximum conversation length, maximum tokens per reply |
| `streaming`, `showReasoning`, `starterPrompts`, `disclaimer` | Chat UX |
| `labels`, `tags` | |

Conversations: `Conversation` (prefix `acv_`) per member and assistant, and `ConversationMessage` (prefix `acm_`) with role, content, model endpoint used, token counts, latency, and feedback (thumbs up or down with an optional note). Every list (conversations, messages, assistants, feedback) is an enumeration; members see only their own conversations; admins see aggregated analytics and, when a member reports a reply, that conversation.

Routes (members, subject to `audience`): `POST /api/v1/assistants/enumerate`, `GET /api/v1/assistants/{id}` (public fields), `POST /api/v1/assistants/{id}/conversations` (start), `POST /api/v1/assistant-conversations/{id}/messages` (send; streams the reply with server-sent events), `POST /api/v1/assistant-conversations/{id}/messages/enumerate`, feedback, delete conversation. Admin: assistants CRUD, test console, conversation analytics, feedback queue.

### 9.3 Chat surfaces

- Web: a chat page per assistant at `/app/assistants/{id}` (and `/assistants/{id}` for `Public` assistants), plus an embeddable widget (`<script>` loader that renders an iframe hosted on the property's domain) for marketing pages.
- Mobile and tablet: an Assistant screen reachable from navigation when the property enables AI, with streaming replies, starter prompts, conversation history, and feedback.
- Moderation: member messages pass the word filter; replies are labeled as AI-generated; members can report a reply into the moderation queue.

### 9.5 Switching AI per property

Decided 2026-11-12: property administrators (and platform administrators) switch AI on or off for their property in the admin portal (Admin, AI, AI settings; the platform console's AI tab). `property.json` `features.ai` (alias `features.assistants`) is the seed: it is stored per property (table `propertyai`, migration 71) on the first load, and in `SingleProperty` a changed folder value replaces it on the next start (audited `property.ai.sync`). A change is audited (`property.ai.update`) and applies to the next request on every instance without a restart: `GET /api/v1/property` `features.ai` (and its `ETag`) follow at once. AI is available when the property's switch and the deployment setting `ai.enabled` are both on. Turning AI off keeps every endpoint, assistant, and conversation; the AI routes answer `404 NotAvailable` and the AI workers pause until it is on again.

### 9.4 Delivery steps

1. Model endpoints: entity, encryption, PolyPrompt factory, admin CRUD, test, health checks, telemetry.
2. Assistants and conversations: admin configuration, member chat with streaming (web first, then mobile and tablet).
3. Knowledge grounding on engine content by labels and tags, then vector retrieval with an embedding endpoint.
4. Tools and the embeddable widget.

---

## 10. Deployment modes: single-property and multi-property (multi-tenant)

Decided 2026-11-11 (supersedes "one property per deployment" and the "no multi-tenancy" deviation). Built right after Phase 2, before Phase 3, so later phases are tenant-aware from the start.

| Mode | Description |
|---|---|
| `SingleProperty` (default) | One backend serves one property, exactly as today. The property comes from `TK_PROPERTY_DIR`. Every request is implicitly for that property |
| `MultiProperty` | One backend serves several properties (several mobile apps and websites). Properties are registered by a platform administrator; every request is resolved to exactly one property and all data is isolated per property |

Setting `deployment.mode` (env `TK_DEPLOYMENT_MODE`, Restart). Both modes run the same code: single-property is a multi-property deployment with exactly one registered property and resolution that always picks it.

### 10.1 Property resolution (every request)

In order, first match wins; a request that resolves to no property, an unknown property, or a disabled property gets `404 PropertyNotFound` (except platform-admin routes):

1. **Mobile and tablet apps:** header `X-TK-Property: <propertyId>`, baked into each app at build time from its property, plus the app's bundle id or package (`X-TK-App: com.example.app`) which must match one of the property's registered app ids. All API calls, push token registrations, store verifications, and update checks (section 8) carry both headers.
2. **Websites:** the request host (`example.test`, `api.example.test`, `www.example.test`) matched against the property's `domains`.
3. **Single-property mode:** the only property.

Store webhooks (Apple, Google, Stripe) resolve the property from their payload (bundle id, package name, or Stripe account and metadata), never from headers. The resolved property id is stored on the request context, stamped on request history, logs, traces, and metrics (`trelliskit.property` label, low cardinality by design), and passed to every service and database call.

### 10.2 Data isolation

Every property-owned table gets `propertyid` (not null, indexed, first column of composite indexes and unique constraints: email, handle, sign-up code, store subscription id, and so on are unique per property). Every query is scoped by property through the shared data-access helpers (the requirements' tenant isolation rules in `AUTHENTICATION.md`, applied with property as the tenant): no repository method takes an id without the property id, and a test suite runs every route as property A against data in property B and asserts nothing leaks. Labels and tags, enumeration, media storage keys (prefixed by property id), caches, rate limits, and background workers are all per property.

Existing single-property databases migrate by adding `propertyid` filled with the current property's id.

### 10.3 Accounts

**Separate per property** (decided): each property has its own accounts. The same email can register independently in two properties with different passwords, sign-in links, profiles, memberships, and data. Sign in with Apple, Google, and Facebook identities are scoped per property (the same Google account may be linked in two properties as two separate members). Sessions and tokens are bound to their property and rejected elsewhere.

### 10.4 Administration

| Role | Scope |
|---|---|
| `PlatformAdmin` | The deployment: register, configure, enable, and disable properties; manage platform settings, integrations shared across properties, and platform admins. Exists only in multi-property mode (in single-property mode the property's `SystemAdmin` has every right) |
| `SystemAdmin` and the other Phase 1 roles | Within one property only |

Property registration in multi-property mode: a platform admin creates a property by uploading or pointing at a property folder (property.json plus brand, legal, and content), or by cloning the sample. Property configuration is stored in the database (with assets in the object store), versioned, and editable in the admin portal; `property.json` remains the import and export format, so a property can move between deployments and between single and multi-property mode.

Per-property integration settings: each property has its own Stripe account, App Store and Google Play app ids and credentials, OAuth client ids, email sender, push credentials, and update channels. Secrets are stored encrypted per property.

### 10.5 Clients

Mobile and tablet builds embed their property id and app id and send them on every request (section 10.1); nothing else changes for members. The web app is served per property domain. Infra supports one stack per property (today) or one shared API and database stack with a website and CDN per property. Each property's `deploy/infra.json` (or, in the shared topology, the deployment file) also selects who manages DNS (`dns.mode`: `Route53`, the default, creates hosted zones and every record; `External` creates none and lists every record as a stack output) and the deployment mail (`mail.provider`: `Mailgun` or `None` create no Amazon SES identity and no SES DNS records, and the Mailgun records come from the Mailgun dashboard; any other value or none keeps the SES identity). See `infra/README.md`.

---

## 11. Configurable profile fields

Decided 2026-11-11: each property's administrators define which profile fields are collected and what appears on profile pages, in the admin portal. `property.json` `profile` seeds the initial field set for a new property (for example a rank with a level, a rank history, an affiliation, and goals); after that the database is the source of truth and `property.json` is the export format. Built together with section 10.

### 11.1 Field definitions

`ProfileField` (prefix `pfd_`), per property, managed under Admin, then Members, then Profile fields:

| Attribute | Values |
|---|---|
| `key` | Stable identifier (`rank`, `affiliation`, `instagram`); immutable once values exist |
| `label`, `helpText`, `placeholder` | Display text |
| `type` | `Text`, `LongText`, `Number`, `Date`, `Boolean`, `Url`, `Email`, `Phone`, `Country`, `SingleSelect`, `MultiSelect`, `Rank` (ordered values with colors plus a level, for example a grade with a level), `History` (repeating entries with configurable sub-fields, for example a rank history) |
| `options` | For select and rank types: ordered `{ key, label, color? }`; rank adds `levelLabel` and `maxLevel` (optionally per option, at most `rank.maxLevel`) and the verified rank awarding rules `rank.awarding` (section 16) |
| `validation` | Required, minimum and maximum length or value, pattern, maximum entries for `History` and `MultiSelect` |
| `collectAt` | `SignUp` (asked during onboarding), `Profile` (optional, editable later), or `AdminOnly` (set by staff) |
| `editableBy` | `Member`, `Staff` |
| `visibility` | `Public` (anyone who can see the profile), `Members`, `Staff`, `Private` (the member and staff) |
| `showOnProfile`, `profileOrder`, `showOnCard` | Whether and where it appears on the profile page and on compact member cards (comments, posts) |
| `searchable`, `filterable` | Included in member search text and offered as a filter in member enumerations and the member search UX |
| `enabled`, `labels`, `tags` | Disabling hides a field without deleting values |

Built-in account fields (display name, handle, avatar, bio) are always present; their labels, visibility, and whether bio is collected are configurable too.

### 11.2 Values, API, and UX

- Values are stored structurally per property, user, and field (typed columns, plus child rows for multi-value and `History` entries); no JSON blobs.
- `Me` and profile payloads return `profile: { fieldKey: value }` with only the fields the caller may see; `PUT /api/v1/me/profile` accepts the same shape with server-side validation from the definitions. The Phase 1 to 2 `rank`, `rankLevel`, `affiliation`, `goals`, and `rankHistory` fields become the seeded `Rank`, `Text`, `MultiSelect`, and `History` fields of an existing property (a compatibility mapping keeps them working during the transition).
- Clients render onboarding forms, profile editors, profile pages, member cards, and member search filters entirely from `GET /api/v1/property` field definitions (web, mobile, tablet).
- Admin: create, edit, reorder, enable or disable fields; preview the onboarding form and profile page; see how many members have each field filled. Changing a field's type is allowed only while it has no values.
- Member search and every member enumeration filter on filterable fields (`filters.profile.<key>`), alongside labels and tags.

---

## 12. Administrative email

Decided 2026-11-11: TrellisKit sends a complete set of administrative (transactional) emails for every lifecycle event, rendered with the **HtmlPageBuilder** NuGet package (1.2.0 or later) and delivered through the **Mailgun messages API**, with **SendWithMailgun** retained for address validation. SMTP (MailKit) remains as a fallback provider for local development (Mailpit) and for properties without Mailgun. Engine feature, per property.

### 12.1 Providers

| Provider | Use | Configuration (per property, admin portal; secrets encrypted) |
|---|---|---|
| `Mailgun` (default for production) | Direct messages API delivery of HTML, text, headers, tags, and tracking choices; `MailgunValidator` optionally validates addresses at sign-up (risk `high` or result `undeliverable` blocks with a friendly message; `unknown` allows) | Sending domain (for example `mg.example.test`), API key (secret), region base URL (US or EU), from name and address, reply-to, optional validation API key and toggle |
| `Smtp` | Local development (Mailpit in Docker) and fallback | Existing SMTP settings |
| `Log` | No provider configured: the message (subject and links) is written to the server log | none |

The engine records a Mailgun send span alongside its `trelliskit.email.*` metrics. SendWithMailgun telemetry remains available for address validation. Mailgun webhooks (`delivered`, `failed`, `complained`, `unsubscribed`) arrive at `POST /api/v1/webhooks/mailgun/{propertyId}`, are signature-verified, and update the message record; permanent failures and complaints mark the address as suppressed so the engine stops sending non-essential mail to it.

### 12.2 Templates (HtmlPageBuilder)

- Every email is an `EmailTemplate` (one per message type): engine default subject, preheader, heading, and body text with `{token}` placeholders, the tokens it accepts (common tokens `name`, `memberName`, `supportEmail`, `webUrl` plus its samples), sample values for previews, and an optional button (`ButtonLabel` linking to the `ButtonToken` value, default `link`).
- Rendering has three steps. The renderer picks the subject, preheader, heading, and body (the property override when one is saved, else the default). `EmailTemplate.Compose(EmailComposer)` builds the body from blocks; the default adds the body paragraphs and the button. The template's `Layout` (default `StandardEmailLayout`, built with HtmlPageBuilder) turns the composed `EmailDocument` into a complete, inline-styled HTML document plus a matching plain-text version. Every value is encoded (never raw HTML).
- Body blocks (`EmailBlock`, abstract; each renders HTML and text): paragraph, subheading, Markdown (the Phase 2 subset), button (http(s) only, with a copy-the-link line), fields (key/value table; empty values hidden), code (preformatted, truncated at 20,000 characters), callout (Info, Success, Warning, Danger), notice (small muted text with an optional link, for example an unsubscribe line), list (bulleted or numbered), divider. Hosts can subclass `EmailBlock` and `EmailLayout`.
- Extensibility for code that embeds the engine: subclass `EmailTemplate`, or `TypedEmailTemplate<TContext>` (its `TokensFor(context)` flattens a typed context into tokens), override `Compose`, and register it at startup with `EmailTemplateCatalog.Register` (keys: a lowercase letter, then lowercase letters, digits, and underscores; duplicates rejected). Registered templates are queued, retried, previewed, overridden, and listed like the engine's; send typed ones with `EmailService.SendAsync`. Anything a template renders must come from its tokens, because queued messages are rendered again from the stored tokens on every attempt.
- Property templates without code: JSON files in the property's `email/templates/` folder, one per template, named `<key>.json` (comments and trailing commas allowed; at most 200 files of 64 KB, 50 blocks each). A file named after an engine or registered template replaces its default text, button, and (with `blocks`) composition; its category, recipient, and tokens stay the engine's, and fields left out keep the engine's values. Any other key adds a template and must give `category` (`Essential`, `Optional`, `Staff`), `recipient` (`Member`, `Staff`, `Moderators`), `subject`, and `heading`. Fields: `subject`, `preheader`, `heading`, `body`, `buttonLabel` (empty removes the button), `buttonToken`, `markdownTokens`, `samples` (extra tokens), `description`, and `blocks`, a list of `{ "type": ... }` with types `body` (the editable body paragraphs), `button` (the template's, or `label` plus `urlToken`), `paragraph`, `subheading`, `callout` (`tone`), `notice` (`text`, optional `label` plus `urlToken`), `list` (`items`, `ordered`), `fields` (`label`/`value` rows), `code` (`text`), `markdown` (`token`), `divider`. Text, labels, values, and items take `{token}` placeholders, validated against the template's tokens. Platform templates cannot be replaced. A file with a problem is skipped with a server warning and the engine template is used.
- A shared layout applies the property's brand: logo (absolute URL from `domains.api`, or the website origin), primary color, property name, footer with the support email, the postal address (`email.postalAddress`; the line is left out when it is not set), and links to the privacy policy and email preferences. No engine template names a property; every brand value comes from the property configuration (`{name}`-style tokens).
- Admins can override each template's subject, preheader, heading, and body paragraphs per property (stored as data with token placeholders, validated against the template's allowed tokens), preview the rendered result with sample data, and send a test email to themselves. Overrides apply on top of property templates; the template default (property file, else engine) is the fallback.
- Layout and emails render correctly in light and dark mail clients and at 360 px wide.

### 12.3 Message catalog

| Event | Recipient | Category | Notes |
|---|---|---|---|
| Welcome after sign-up | Member | Essential | Sent after email verification (or immediately for verified social sign-ups) |
| Verify email | Member | Essential | Existing |
| Password reset requested; password changed | Member | Essential | Changed notice includes "not you?" guidance |
| New sign-in method linked or removed; email changed | Member | Essential | Security notices |
| Membership started (subscription purchased) | Member | Essential | Store, plan, price, renewal date; receipt link for Stripe |
| Renewal succeeded | Member | Essential | Stripe receipts; store renewals summarized |
| Payment failed or billing retry; grace period | Member | Essential | How to fix the payment method per store |
| Subscription canceled (auto-renew turned off) | Member | Essential | Access continues until the period end date |
| Membership expired or lapsed | Member | Essential | How to rejoin (web only for store policy reasons) |
| Refund or revocation | Member | Essential | |
| Free membership granted (sign-up code or admin comp) | Member | Essential | Grant summary and end date |
| Free membership ending (14 and 3 days) | Member | Essential | Existing reminder worker |
| Account banned or muted; restriction lifted | Member | Essential | Reason and appeal guidance |
| Content removed by moderators | Member | Essential | Which item and why |
| Account deletion requested and completed; data export ready | Member | Essential | Export link expires |
| Support ticket replies (Phase 5) | Member | Essential | |
| Mentions, replies, new feed posts, digests | Member | Optional | Governed by notification preferences (Phase 2) |
| New member signed up; new subscription; cancellation; payment failure; refund | Staff (configurable recipients) | Staff | Per-event toggles and recipients in the admin portal |
| New report, content auto-hidden, moderation overdue | Moderators | Staff | Respects moderator scope |
| Directory submission, event RSVP changes, support ticket opened (later phases) | Staff | Staff | |
| Integration failures (webhook errors, failing model endpoint, payment provider errors) | Platform and property admins | Staff | Rate-limited |
| Daily administrator summary (`staff_admin_summary`): members, sign-ups, active members, activity by type, items needing attention, platform health | Addresses in the deployment setting `AdminSummary.Recipients` | Staff | Once per day per recipient after `AdminSummary.SendHour`; see [ADMIN_SUMMARY.md](ADMIN_SUMMARY.md) |

Essential messages ignore marketing preferences but never go to suppressed addresses. Optional messages respect notification preferences and include one-click unsubscribe headers (`List-Unsubscribe`, `List-Unsubscribe-Post`).

### 12.4 Delivery pipeline

- Emails are queued as records (`EmailMessage`, prefix `eml_`: property, recipient, template, category, status `Queued`, `Sent`, `Delivered`, `Failed`, `Suppressed`, provider message id, attempts, labels, tags), sent by a background worker with retries and backoff, and idempotent per event (a duplicate store webhook never sends two emails).
- Admin portal: an Email section with provider settings, a test send, template overrides with preview, staff notification recipients per event, the suppression list, and a bounded, filterable message log (EnumerationQuery with labels and tags; bodies are not stored, only metadata).
- Telemetry: `trelliskit.email.queued`, `.sent`, `.failed`, `.suppressed` by template and provider, plus the Mailgun delivery span.
- Optional `email/layout.json` configures property-specific presentation, dark logo, footer, and ownership. Delivery preserves plain text, reply-to, unsubscribe headers, and correlation variables. Optional tracking defaults off and Essential/Staff tracking is always disabled. The catalog test endpoint is admin-only. See [EMAIL.md](EMAIL.md) for the full configuration and verification contract.

---

## 13. Data platform: PostgreSQL only, pgvector, and RecallDB

Decided 2026-11-11: TrellisKit is **PostgreSQL-only**. SQLite is removed from the engine entirely (provider, dialect, tests, run-from-source default), so the data layer has one SQL dialect and pgvector is available in every environment. This supersedes the "Sqlite and Postgresql providers" rule in `CLAUDE.md` and the provider sections of earlier contracts.

| Area | Design |
|---|---|
| System of record | PostgreSQL (17 or later) with the `pgvector` extension, in every environment: local Docker, CI, and AWS (RDS PostgreSQL with pgvector) |
| Vector and retrieval store | **RecallDB** in the Docker stack by default (on the same PostgreSQL server, its own database and credentials), used for assistant knowledge grounding and semantic search |
| Full-text search | Verbex (section 9 of the Phase 3 contract decisions), independent of the database |
| Docker | `compose.yaml` runs PostgreSQL with pgvector as the system of record, plus RecallDB. An **init script** (mounted into the PostgreSQL container's `docker-entrypoint-initdb.d`) creates the `trelliskit` database and role, the RecallDB database and role, enables `pgvector` where needed, and grants least-privilege rights; passwords come from the env file, never hard-coded in the script |
| Running from source | Requires a PostgreSQL server; `docker compose up -d postgres` (or the full stack) provides one; the engine fails fast with a clear message when it cannot connect |
| Tests | Every backend test runs against PostgreSQL: each test gets an isolated schema (as today) on a PostgreSQL started by the test harness (Testcontainers) or provided by CI. CI runs the full backend suite on Linux (service container) and a build-only check on Windows and macOS |
| Migration | Done at the start of the multi-property milestone, together with the `propertyid` rework: the Sqlite provider folder, dialect, and SQLite-only tests are deleted; the database abstraction (driver base, domain method interfaces, `Implementations/` and `Queries/`) stays so the structure remains clean |

---

## 14. Property documents and acceptance

Decided 2026-11-12: TrellisKit provides a generic required-documents facility. The documents themselves (terms, privacy policy, guidelines, waivers, refund and subscription terms, informational pages) are property assets; the engine versions them, serves them, and records members' acceptances.

### 14.1 `property.json` `documents`

An optional list (at most 30). Absent or empty means the property has no documents (no gate; the website falls back to its standard legal pages).

```json
"documents": [
  { "key": "terms", "title": "Terms of Service", "file": "legal/terms-of-service.md", "version": "1.0", "kind": "Terms",
    "acceptance": "AcceptedWithTerms", "separateCheckbox": false, "reacceptOnChange": false, "slug": "terms", "footer": true },
  { "key": "participation-agreement", "title": "Participation Agreement", "file": "legal/participation-agreement.md", "version": "1.0", "kind": "Waiver",
    "acceptance": "RequiredAtSignup", "separateCheckbox": true, "reacceptOnChange": true, "slug": "participation-agreement", "footer": true }
]
```

| Field | Rule |
|---|---|
| `key` | 1 to 64 letters, digits, underscores, or hyphens; unique; `acceptances` is reserved. Stored in acceptance records and used in the API |
| `title` | 1 to 120 characters |
| `file` | Markdown file relative to the property folder (`.md`, no `..`, no absolute path). It must exist when the folder is loaded |
| `version` | Display version, 1 to 32 characters (for example `1.0`) |
| `kind` | `Terms`, `Privacy`, `Guidelines`, `Refund`, `Subscription`, `Waiver`, `AccountDeletion`, `Other` |
| `acceptance` | `RequiredAtSignup` (must be accepted to finish sign-up), `AcceptedWithTerms` (accepted through the sign-up terms checkbox), `ShownAtPurchase` (linked on purchase screens, never accepted), `InformationOnly` (public page only) |
| `separateCheckbox` | `RequiredAtSignup` only: sign-up shows its own required checkbox; otherwise the terms checkbox covers it |
| `reacceptOnChange` | `RequiredAtSignup` and `AcceptedWithTerms` only: signed-in members must accept a new version before continuing |
| `slug` | Website path segment (`/terms`), lowercase letters, digits, and hyphens, unique |
| `footer` | Whether the website footer links it (default true) |
| `description` | Optional meta description (at most 300 characters) |

"Gating documents" are those with acceptance `RequiredAtSignup` or `AcceptedWithTerms`.

### 14.2 Versions (seeded from the folder; the folder wins on change)

- Versions live in `propertydocumentversions` (migration 74): key, sequence (1-based publish counter), display version, title, Markdown content, change summary, `requiresReacceptance`, source (`Seed`, `Edit`, `DirectorySync`), seed hash, author, time. The current version is the highest sequence. Versions are never pruned (they are legal records).
- On the property's first load every declared document is seeded from its file as sequence 1. In `SingleProperty`, when a document's version, title, or file changes on disk, the folder wins on the next start: a new `DirectorySync` version (audited `document.sync`) that requires re-acceptance only when the document has `reacceptOnChange` and its display version is new. While the folder is unchanged, admin-published versions stand. In `MultiProperty` the database is the source after the first load (a property registered without a folder gets its first version from an admin publish).
- Administrators publish new versions from the admin console (`POST /api/v1/admin/documents/{key}/versions`, audited `document.publish`) with a change summary and a re-acceptance option (default `reacceptOnChange`). Platform administrators do the same for any property under `/api/v1/platform/properties/{propertyId}/documents`. A member must have accepted at least the latest version that required re-acceptance (`requiredSequence`); documents without `reacceptOnChange` need any accepted version.
- Setting `property.maxDocumentCharacters` (default 200000) bounds published content; seeded files are stored in full.

### 14.3 Acceptance

- Acceptances live in `propertydocumentacceptances`: member, key, version id, display version, sequence, title, method (`Signup`, `Reacceptance`), client (`Web`, `Ios`, `Android`, `Other`, from `X-Client` and the mobile app's `X-Client-Platform`), and time. Every write is audited (`document.accept`). They are included in the account export (`documentAcceptances`) and kept after account deletion as legal records, for the period the property's privacy policy states.
- **Email sign-up** (`POST /api/v1/auth/register`): `acceptTerms` accepts every document the terms checkbox covers; `acceptedDocuments: [{ key, version }]` must list every separately accepted `RequiredAtSignup` document at its current version, otherwise `400 ValidationFailed` with `fieldErrors["acceptedDocuments.<key>"]` and nothing is stored.
- **Social sign-up**: the account is created with no acceptances; the age attestation step (`POST /api/v1/me/age-attestation`) may carry `acceptTerms` and `acceptedDocuments`, and `POST /api/v1/me/documents/accept` records acceptance at any time.
- **The gate**: after age attestation, a member who has not accepted the required version of every gating document gets `403 DocumentAcceptanceRequired` with `documents: [keys]` on member (content) routes, admin and moderator routes, Phase 4 staff routes, and purchase and sign-up code routes. Account routes (`/me`, `/auth`, handles, membership status, support) stay open so clients can show the prompt. The break-glass admin token and a platform administrator's delegated session are never gated. `MeResponse.pendingDocuments` lists what is pending, each with `previouslyAccepted` (true when a newer version replaces one the member accepted, so the prompt says "updated"; false for a first acceptance, which says "Please review and accept"); clients show a blocking review-and-accept step (web member app and admin console, mobile setup step) and re-check on `DocumentAcceptanceRequired`, app start, and foreground.

### 14.4 Clients

- `GET /api/v1/property` serves `documents` (summaries with the live current version; the ETag follows publishes). `GET /api/v1/public/documents/{key}` serves the current content (anonymous).
- **Website:** each document is a page at `/<slug>`, prerendered for SEO from the build-time file and replaced at runtime by the live content, with version and last updated; the footer lists documents with `footer: true`; sign-up links the terms-covered documents and shows a required checkbox (with a link to read the full text) for each separately accepted document; checkout links `ShownAtPurchase` documents. The admin console has a Documents page (list, counts, versions, publish, acceptances) and a Documents section in the member drawer; the platform console has the same page per property.
- **Mobile:** sign-up mirrors the website; a document screen renders any document; Account lists the documents and the member's acceptance history; the paywall and membership screens link `ShownAtPurchase` documents; a setup step after age attestation handles pending documents.

---

## 15. Training hub

The Training hub is an engine feature available to every property (`features.training`, default on). It gathers a member's training into one destination: an Overview and one Library, with further sections reported by the server only when they have data. Library and catalog are combined in navigation only; courses (series and lessons) and techniques (the catalog) remain separate data sets with separate editors and review.

### 15.1 Switches

| Switch | Default when missing | Effect |
|--------|----------------------|--------|
| `features.training` | on | The `training` destination and `GET /api/v1/me/training`. Off hides the hub; the library, events, and saves stay reachable on their own routes. |
| `features.saves` | on | Saves and private collections. Independent of Training: turning Training off never removes saved items. |
| `features.catalog` | **off** | The reviewed technique catalog. The one switch that is off when missing (`OffByDefaultFeatures` beside `propertyFeatureEnabled` in `shared/src/access.js`; `PropertyFeatures.Catalog` on the server). |

Kill switches are scoped: the `training` kill switch (`KillSwitchCatalog`) hides the hub and blocks `/api/v1/me/training` only; `/library`, `/series`, `/lessons`, and deep links keep following the `library` switch, and catalog routes follow the `catalog` switch. `training` is a gateable access feature key (`FeatureKeyEnum.Training`); its access is the union of its sections' access, and a member who can see no section gets `404 NotAvailable` from `GET /api/v1/me/training`.

### 15.2 Section contract

The server reports the hub's sections in order, each with whether it is available to the caller:

```json
{
  "sections": [
    { "key": "Overview", "available": true },
    { "key": "Library", "available": true, "segments": ["Courses", "Techniques", "Saved"] },
    { "key": "Schedule", "available": false, "reason": "NoData" },
    { "key": "Progress", "available": true }
  ]
}
```

- `key` is a `TrainingSectionEnum` value: `Overview`, `Library`, `Schedule`, `Programs`, `Progress`. `Programs` is reserved for structured programs or curricula.
- `reason` (only when `available` is false) is a `TrainingSectionReasonEnum` value: `FeatureOff`, `NoAccess`, `NoData`, `SignedOut`.
- Clients show only sections with `available: true` and ignore unknown keys and unknown segments, so a newer server can add sections without breaking older apps.

### 15.3 Navigation

- Mobile: the `training` destination (section 2.3) routes to `/training`. Properties may place it in the bar, the drawer, the account menu, or several; placing it keeps `library` and `catalog` out of the drawer.
- Website: the `training` member section (`/app/training`) follows `web.navigation` (section 2.3); the library is covered by it in the navigation and keeps its own routes.

### 15.4 One Library: lesson techniques and search

- **Lesson techniques** (`REST_API.md`, "Lesson techniques"; `lessoncatalogentries`, ids `lce_`, migration 135): library staff link up to 50 published catalog entries to a lesson, each with an optional start offset in seconds and an order. Members read "Techniques in this lesson" for a lesson they can see and "Lessons that teach this" under their own lesson access (locked stubs for PartiallyGated free accounts); signed-out visitors read the Public lessons that teach an entry. Archived or unpublished lessons and unpublished entries drop out for members (staff see `warning: "EntryNotPublished"`); deleting either side deletes its links. Every link route is `404 NotAvailable` while `features.catalog` or `features.library` is off. The link data sets stay separate: a link never changes the lesson or the entry.
- **Library search** (`REST_API.md`, "Library search"): `POST /api/v1/library/search` runs one search over series, lessons, and (while `features.catalog` is on) published catalog entries, each through the kind's own enumeration and access rules, bounded per kind and continuable through the kind's own route. It uses the shared search limiter. Migration 135 adds trigram GIN indexes on the series and lesson search columns when the `pg_trgm` extension is installed in the database (the Docker init script, the infrastructure bootstrap, and the test harness install it; deployments without it keep working with sequential scans).

## 16. Ranks, curricula, and attendance

A property's `Rank` profile field (section 11) is its rank system: the member's own value is the **self-reported** rank, and rank awarders record **verified** promotions on top of it. No engine table duplicates the field's options. Everything here is off until the server setting `ranks.enabled` is on (every route answers `404 NotAvailable` while it is off); the feature key `ranks` can additionally be gated in `PartiallyGated` (`access.gatedFeatures`). Routes are listed in `REST_API.md`, section "Ranks, curricula, and attendance".

### 16.1 Property keys (Rank profile fields)

| Key | Values |
|---|---|
| `options[]` | Ordered lowest to highest by array position. |
| `options[].maxLevel` | Optional, `0` to `rank.maxLevel`: the level maximum of that option (for example four levels on most options and the field maximum on the top one). Missing means `rank.maxLevel`. Awards above it are `400 LevelOutOfRange`. |
| `rank.levelLabel`, `rank.maxLevel` | As before; `rankLabel` texts read "Option, N LevelLabel". |
| `rank.awarding.optionAwarderMinimum` | Option key, default none: an awarder's verified option must be at or above it to award anything. |
| `rank.awarding.allowEqualOption` | Default `false`: whether an awarder may award their own option (otherwise the awarded option must be strictly below theirs). Never above the awarder's own level. |
| `rank.awarding.allowEqualOptionLevels` | Default `true`: whether an awarder may award levels within their own option to a member who already holds it verified (never above the awarder's own level). Levels within lower options are always allowed. |

SystemAdmin is exempt from all three rules. Awards the rules refuse are `403 AwardAboveOwnRank`; nobody awards, verifies, or checks in themselves (`403 SelfAward`, `403 Forbidden`). The validator refuses an option `maxLevel` above `rank.maxLevel` and awarding keys that name no option. The rules are stored with the field (`profilefields.rankawarding`) and edited with the field in the admin console (`PUT /api/v1/admin/profile-fields/{id}` `rank.awarding`).

### 16.2 Settings

| Key | Default | Range | Meaning |
|---|---|---|---|
| `ranks.enabled` | `false` | | Turns verified ranks, lineage, curricula, and attendance on. |
| `ranks.showVerificationLabels` | `true` | | When `false`, clients show no "verified" or "self-reported" label (both rank responses carry `showVerificationLabels`), so a property can verify members in bulk before the labels appear. |

`GET /api/v1/property` reports both switches as `features.ranks` and `features.rankVerificationLabels` (they are part of its ETag).
| `attendance.checkInWindowBeforeMinutes` | `60` | 0 to 720 | Member check-in opens this long before the start. |
| `attendance.checkInWindowAfterMinutes` | `120` | 0 to 720 | Member check-in closes this long after the start. |
| `attendance.codeRotationSeconds` | `60` | 15 to 600 | Each rotating check-in code lasts this long; the previous code is accepted for one more period. |

### 16.3 Model (migrations 132 to 134)

| Table | Holds |
|---|---|
| `memberranks` (`mrk_`) | The member's current verified rank per Rank field: option, level, status (`Verified`, `Revoked`, `Disputed`), the award it came from, awarder, award time. One row per member and field. |
| `rankpromotions` (`rkp_`) | Every award and revoke: member, field, option, level, action, actor, note (at most 500), optional event, revoke reason, and on award rows `revokedUtc`. |
| `rankrequirements` (`rkq_`) | Requirements for an option and level (level null covers every level of the option without its own row): minimum days in grade, minimum check-ins, notes, optional curriculum. |
| `lineageentries` (`lin_`) | Self-reported lineage: who promoted the member to an option (a member, or a name) and when, verified by that member or a SystemAdmin. |
| `curricula` (`cur_`), `curriculumitems` (`cui_`), `curriculumprogress` (`cup_`) | Curricula grouped by the options of a Rank field; items are `Lesson`, `Series`, `CatalogEntry` (a catalog entry identifier stored as text, no foreign key; clients resolve titles), or `Custom` text; progress holds self-completion and verification. |
| `attendance` (`att_`) | One check-in per member, target (`Event` or `VenueSession`), and occurrence date. |

**Effective rank:** the member's `memberranks` row when its status is `Verified`, otherwise the profile value (self-reported). An award sets the stored profile value to the awarded rank; while a verified rank stands the member cannot change that field (`409 RankVerified`; unchanged values pass, so clients may send the whole profile). A revoke marks the row `Revoked`; the field is self-reported and editable again.

**Curriculum items:** `Lesson` items are satisfied by `LessonProgress.Completed`; `Series` items when every published lesson of the series is complete; `CatalogEntry` and `Custom` items by self-completion when optional and by verification when required. Any item a rank awarder verifies counts.

**Eligibility:** the next rank is the next level of the current option up to its `maxLevel`, then level 0 of the next option that is not retired. Time in grade and attendance count from the current verified award (attendance counts every check-in without one); a requirement's curriculum counts the required items of the next option's group. A member is eligible when every configured requirement is met.

**Attendance:** a check-in target is an event (its occurrence date is its start date in its time zone) or a venue schedule session (`targetId` `{venueId}:{sessionIndex}`, the zero-based position in the venue's `schedule.sessions`; the occurrence date must fall on the session's weekday). The window is measured from the event start, or from the session's start time on the occurrence date in the venue schedule's time zone, so drop-in and regular sessions from the directory count without an event. Members check in with the target's rotating six-digit code (an HMAC of the property, target, date, and period keyed from `auth.tokenEncryptionKey`) or by scanning its QR payload; rank awarders check members in without a code at any time after the window opens.

### 16.4 Roles, audit, notifications, and lifecycle

- `RankAwarder` (resource type `Ranks`, resource id `*`) is property-scoped and assigned like `LibraryEditor`: `POST /api/v1/admin/moderators { userId, resourceType: "Ranks", resourceId: "*" }` or the member roles editor. It awards and revokes ranks, verifies curriculum items, reads eligibility and requirements, and checks members in. SystemAdmin holds every RankAwarder permission; only SystemAdmin replaces requirements. Curricula are edited by SystemAdmin and LibraryEditor.
- Audited: `rank.award`, `rank.revoke`, `rank.requirements`, `lineage.update`, `lineage.verify`, `curriculum.create`, `curriculum.update`, `curriculum.items`, `curriculum.publish`, `curriculum.archive`, `curriculum.verify`, `attendance.checkIn`, `attendance.delete`.
- Notifications: `RankPromoted` ("You were promoted to {rank}", in-app and push) with the essential `rank_promoted` email; `LineageVerificationRequested` to a member named as promoter.
- Member-written text (promotion notes and revoke reasons, lineage promoter names) is word filtered and written to `contentchanges` (`RankPromotion`, `LineageEntry`).
- Account export adds `memberRanks`, `rankPromotions`, `lineage`, `curriculumProgress`, and `attendance`; account deletion erases them (rows the member wrote about others keep only their identifier). The tables are in `PropertyTables` and purged with the property.

### 16.5 Clients and the check-in QR payload

Every client surface is hidden unless `GET /api/v1/property` reports `features.ranks: true` (a missing switch counts as off, `ranksFeatureEnabled`); "Verified" and "Self-reported" labels also need `features.rankVerificationLabels` (`rankVerificationLabelsEnabled`) and the rank response's `showVerificationLabels`.

- **Member app (web and mobile):** the label next to the Rank field on profile pages and member cards (`rankBadgeMap` of `GET /api/v1/me/rank` or `GET /api/v1/members/{id}/rank`, cached per member for five minutes); a verified Rank field is read-only in the profile editor with a short explanation (`verifiedRankFieldKeys`, and the field named by a `409 RankVerified`); My rank (web `/app/rank`, mobile `/rank`: effective rank, next requirements, history, attendance count, lineage; the lineage editor is on the web); curricula (web `/app/curricula` and `/app/curricula/{id}`, mobile `/curricula` and `/curricula/{id}`) grouped by rank with self-completion of CatalogEntry and Custom items (catalog items link only with `features.catalog`); "Check in" (code entry) on event detail and on today's venue schedule session rows (target `{venueId}:{sessionIndex}`, occurrence date the device's date; the server checks the venue's time zone and window).
- **Rank staff (SystemAdmin, RankAwarder):** "Show check-in code" next to "Check in" shows the QR code, the six digits, a countdown to the next rotation (the client fetches the next code when `expiresUtc` passes), and the roster. The admin console adds Ranks (awards with filters, award form with per-option level choices, revoke with a reason, eligibility, requirements, lineage lookup and verification), Curricula (editor with rank groups and item pickers for lessons, series, catalog entries with `features.catalog`, and custom text; publish, archive; member progress with verification), Attendance (event or venue session roster, code display, staff check-in without a code), the rank history panel on the member drawer, `RankAwarder` in the member roles editor (scope `Ranks`), and the `ranks` access feature checkbox. RankAwarder holders see Ranks, Curricula, and Attendance; LibraryEditor holders see Curricula.
- **Reusable components** for the Training hub's Overview and Progress: web `www/src/views/ranks/` (`RankCard`, `CurriculumCard`, `RankProgressPanel`, `ProgressBar`, `QrCodeSvg`, `CheckInControls`), mobile `mobile/src/components/ranks/` (`RankCard`, `CurriculumCard`, `RankProgressPanel`, `ProgressBar`, `QrCodeView`, `CheckInControls`); each file documents its props.

**Check-in QR payload.** Clients encode an https universal link on the property's web domain, built by `checkInLink(property, code)` from the `GET /api/v1/attendance/check-in-code` response:

```
https://{domains.web}/app/check-in?t={targetType}&id={targetId}&d={occurrenceDate}&c={code}
```

Values are URL-encoded (`id=ven_1%3A2` for session 2 of venue `ven_1`); `d` is omitted for events without an occurrence date. A phone's system camera opens the link: the web route `/app/check-in` checks a signed-in member in at once (sign-in first when needed) and falls back to code entry when the code has rotated, offering "Open in the app" (`{mobile.scheme}://check-in?...`) when the property has an app scheme. In the app, `linkTargets` maps the link (and `/app/check-in` paths) to the native `/check-in` route; the system camera opens the app directly only in builds that claim the web domain as an associated domain (`applinks:`), which needs a new binary, so current binaries reach the app through the web page's "Open in the app" link. No camera or barcode module is involved, so the screens ship in an over-the-air update; the QR image is drawn by the shared dependency-free encoder `qrMatrix` (`shared/src/qrCode.js`: SVG on the web, plain views on mobile). `parseCheckInPayload` accepts the link, the app path, a route parameter object, and the server's compact `qrPayload` (`checkin?targetType=...&targetId=...&occurrenceDate=...&code=...`), which clients use only when the property has no web domain.

## 17. Saves

Generic saves and named private collections, an engine feature available to every property (routes and errors: `REST_API.md`, "Saves").

- **Switch:** `property.json` `features.saves` (default `true` when absent; `false` makes every saves route `404 NotAvailable`). It is independent of any navigation or hub switch, so turning a hub off never removes saved threads, venues, or events. The `saves` kill switch answers `503 FeatureDisabled` on `/api/v1/me/saves` and `/api/v1/me/save-collections`. `saves` is also an access feature key: in `PartiallyGated`, listing it in `access.gatedFeatures` makes saving (and creating collections) need a membership; reading and removing saves stay open.
- **Targets:** `Lesson`, `Series`, `CatalogEntry`, `Venue`, `Instructor`, `Event`, `Post`, `Thread`. Each type follows its own feature switch and read rules. Library, directory, event, and catalog types resolve through the content target registry (`ContentTargetProvider.SaveSummaryAsync`, which providers may override to add an image); a type whose provider is not registered is unavailable. `CatalogEntry` follows `features.catalog` (default off) and the catalog's publication and moderation state. Posts and threads follow the feed and forum visibility rules. A module may replace a type's resolver with `FavoriteService.RegisterResolver`.
- **No leaks:** a saved target the member can no longer open (archived, unpublished, moderated, feature off, or access lost) is listed with `target.available: false` and no title, image, or link. Deleting a target removes every member's saves of it (`FavoriteService.TargetsDeletedAsync`, called by each content service where it deletes or purges a target).
- **Collections:** at most `saves.maxCollectionsPerMember` (default 50, 1 to 500) per member, names unique per member (case-insensitive), ordered by the member; deleting a collection keeps its saves. A save belongs to at most 20 collections per request. The database refuses any membership that links rows of different members or properties.
- **Limits:** `saves.maxPerMember` (default 2000, 100 to 20000), `409 LimitReached` beyond it. Batched state lookups take at most 100 targets.
- **Notes:** at most 500 characters, visible to the member and to moderators (clients say so; never "private"). They pass the word filter and the content changelog (`contentchanges`, target type `Favorite`, context the saved target).
- **Data:** tables `favorites`, `favoritecollections`, `favoritecollectionitems` (migration 126), all property-scoped and purged before `users`. Labels and tags use the shared metadata tables (target types `Favorite` and `FavoriteCollection`). Account exports carry `saves` and `saveCollections`; account deletion deletes both.
- **Metrics:** `trelliskit.saves.created` and `trelliskit.saves.removed`, labeled `target_type`.
- **Clients:** `shared/src/favoritesApi.js` (`saveItem`, `unsaveItem`, `listSaves`, `saveState`, collection methods on `ApiClient`) and `shared/src/favorites.js` (`FavoriteTargetType`, `createSaveStateBatcher`, which batches state lookups into requests of at most 100 targets).

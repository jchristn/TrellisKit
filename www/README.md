# Website

The TrellisKit website engine: property-driven marketing pages, the web member app (`/app`), and the admin console (`/admin`). React 19, Vite 6, React Router 7, plain JSX. Package `@trelliskit/www`.

The engine never names a property. Everything brand-specific (name, colors, logos, favicon, manifest, social image, titles, JSON-LD, sitemap domain, marketing copy, legal text, profile taxonomy, price) comes from the active property folder at build time, and from `GET /api/v1/property` at runtime for the member app and admin console. See `docs/TRELLISKIT.md`.

## Choosing the property

The build serves one property, selected only by directory path:

| Variable | Meaning |
|---|---|
| `TK_PROPERTY_DIR` | Property folder: absolute, or relative to the engine root (the folder that contains `www/`). A relative path that has no `property.json` there is also tried relative to the current directory and to `www/`. Default: `samples/example` (the neutral Example Community). |
| `TK_REPOSITORY_URL` | Optional engine repository link for the admin top bar (hidden when unset). |
| `TK_BACKEND_URL` | Backend for the dev and preview proxies (default `http://127.0.0.1:8000`). |

```sh
cd www
npm run build                                                   # Example Community (samples/example)
TK_PROPERTY_DIR=../../<property folder> npm run build          # another property folder
TK_PROPERTY_DIR=/abs/path/to/property npm run dev
```

## Property folder

```text
<property>/
|-- property.json            identity and configuration (docs/TRELLISKIT.md section 2), validated at build time
|-- brand/                   logo.png, icon.png, icon.ico (paths from property.json brand.*)
|-- legal/*.md               privacy-policy, terms-of-service, subscription-terms, community-guidelines, account-deletion
`-- web/
    |-- content/*.md         marketing pages (optional, one route per file)
    |-- theme.css            extra CSS after the generated theme (optional)
    |-- og-image.png         1200 x 630 social image (optional; generated from the logo when absent)
    `-- images/              content images (optional; png, jpg, webp, gif, svg), served at /images/<name>
```

**Theme.** `scripts/property.mjs` generates CSS custom properties for light and dark from `brand.colors` (`--brand-primary`, `--brand-primary-hover`, `--brand-primary-contrast`, `--brand-accent`, `--brand-accent-contrast`, `--color-accent-soft`, `--brand-theme-color`) and appends `web/theme.css` if present. `src/index.css` holds neutral engine defaults only; the generated theme (`virtual:trelliskit/theme.css`, imported by `main.jsx`) loads after it and wins.

**Brand files.** `scripts/brand-assets.mjs` (pure JavaScript via `pngjs`) writes `favicon.ico`, `favicon-32.png`, `icon-192.png`, `icon-512.png`, `apple-touch-icon.png`, `brand-mark.png`, `logo.png`, and `og-image.png` at the site root, every file in `web/images/` at `/images/<name>`, plus `site.webmanifest`, `sitemap.xml`, and `robots.txt` (from `domains.web`). They are emitted into `dist/` by the Vite plugin and served by the dev server; nothing brand-specific is committed under `www/` (there is no `public/` folder).

**Content pages.** Each `web/content/<slug>.md` is a route (`home.md` is `/`). Front matter is simple `key: value` lines (dotted keys nest, `#` lines are comments):

| Key | Use |
|---|---|
| `title`, `metaTitle`, `metaDescription` | Head tags (`metaTitle` defaults to `<title> \| {name}`) |
| `ogType`, `priority`, `changefreq`, `sitemap` | Open Graph type and sitemap entry (`sitemap: false` adds noindex) |
| `nav`, `navOrder` | Header and footer navigation label and order |
| `layout` | `sections` renders edge to edge (each section brings its container); default wraps in the page container |
| `jsonLd` | Entities for this page, for example `person, book` |
| `person.*`, `book.*`, `organization.*` | JSON-LD entities (Person and Book are published only when the content defines them; empty values are omitted) |

The body is Markdown (headings, paragraphs, lists, blockquotes, tables, links, `[PLACEHOLDER: ...]`) plus container directives `::: kind attrs` ... `:::` (nest with more colons): `section`, `aside`, `article`, `div`, `header` (`class`, `inner`), `excerpt`, `cards` (`class`, `item`, `tag=ol`, `numbered`, `media`), `faq` (each `###` question is a collapsible `<details>` row), `quotes`, `checks` (`class`, `label`), `actions` (`size`, `variant`; the first link is primary, external links open in a new tab), `photos`, `toc`. Headings and paragraphs take a trailing `{#id .class icon=name}`. Widgets are paragraphs of the form `@name attrs`: `@logo`, `@price`, `@join`, `@signup-link label="..."`, `@store-badges`, `@cover title="..." subtitle="..." size=lg src="/images/cover.jpg"` (without `src`, a placeholder cover), `@media label="..." class="..."`. `{tokens}` such as `{name}`, `{price}`, `{interval}`, `{minimumAge}`, `{owner}`, `{domain}`, `{supportEmail}`, `{productName}` are filled from `property.json` everywhere (see `propertyTokens` in `@trelliskit/shared`).

**Legal pages** render from the property's `legal/` folder at fixed engine paths (`/privacy`, `/terms`, `/subscription-terms`, `/community-guidelines`, `/delete-account`); a missing file means no route and no footer link.

## Layout

```text
www/
|-- index.html, vite.config.js, eslint.config.js, playwright.config.js
|-- scripts/
|   |-- property.mjs              TK_PROPERTY_DIR resolution, property loading and validation, theme CSS, manifest
|   |-- brand-assets.mjs          icon, logo, and social image generation
|   |-- vite-plugin-trelliskit.mjs  virtual:trelliskit/site and virtual:trelliskit/theme.css, index.html values, emitted files
|   |-- prerender.mjs, prerender-entry.jsx
|-- src/
|   |-- main.jsx, App.jsx         routes for /, /app, /admin (marketing routes come from the property)
|   |-- strings.js                engine UI strings only (English); property text uses {name}-style tokens
|   |-- property/                 buildSite.js (build-time bundle), content.js (front matter, directives), current.js (fill)
|   |-- context/                  PropertyContext (build copy, then GET /api/v1/property), ThemeContext, AuthContext, AdminAuthContext, ToastContext
|   |-- components/               shared UI incl. ProfileForm (config-driven profile) and RankText
|   |-- hooks/, layouts/, utils/, views/marketing|auth|member|admin
|-- test/                         Vitest + React Testing Library; fixtures/dojo (ranks) and fixtures/bookclub (no ranks)
|-- e2e/                          Playwright smoke tests (1280 and 360, light and dark)
```

**Runtime property.** `PropertyProvider` starts from the build-time copy. `MemberLayout` and `AdminApp` call `useRuntimeProperty`, which loads `GET /api/v1/property` once and merges it over the build copy (invalid or failed responses keep the build copy). Components read `useProperty()` (`property`, `site`, `fill`); non-React helpers use `fill` from `src/property/current.js`.

**Profile.** `ProfileForm`, `MemberProfileView`, the admin members table, filters (`rank=`), and the member drawer render rank (picker with color swatch), level (`0..maxLevel`, labeled `levelLabel`), rank history (labeled from `rankHistory`), affiliation, and goals (free text for `freeText` values) from `profile` in the property, and hide disabled sections. Rank text uses `formatRank` from `@trelliskit/shared`. Member and admin navigation hide sections whose `features` flag is false.

**Admin console.** Title `{name} Admin`; the sidebar footer shows the property name and "Powered by TrellisKit".

## Identity and membership (Phase 1)

The binding contract is `docs/PHASE1_CONTRACT.md`.

**Session.** `AuthContext` keeps the access token in memory only and `Me` in state. Every request sends `X-Client: web` and `X-Client-Version` (from `package.json`). The refresh token is the HttpOnly `tk_refresh` cookie; `POST /api/v1/auth/refresh` (no body, `credentials: 'include'`) restores the session on page load and runs on any 401 from a non-auth route. Concurrent 401s share one in-flight refresh (`singleFlight` from `@trelliskit/shared`) and each request retries once; if refresh fails the session ends. A `localStorage` hint (`tk.hasSession`) skips the restore call for anonymous visitors on marketing pages; `/app` and `/admin` always try.

**Public pages.** `/sign-in`, `/sign-up` (password policy hints, minimum age check with `isAdult` and the property's `membership.minimumAge`, terms links, optional sign-up code), `/forgot-password`, `/reset-password?token=`, `/verify-email?token=`, `/join?code=`, `/auth/apple/callback`, `/account-deleted`. Google (Identity Services ID token), Apple (Sign in with Apple JS, popup; Apple gets the SHA-256 hex of a random nonce and the backend gets the raw nonce), and Facebook (JS SDK access token) buttons appear only when `GET /api/v1/auth/providers` enables them.

**Onboarding.** `RequireMember` gates `/app/*` by level: signed in, then age attestation (`/app/onboarding/age`), then profile (`/app/onboarding/profile`), then an active membership (`/app/membership`; SystemAdmin bypasses). `/app/account` needs only the age step.

**Sign-up codes.** `/join?code=X` stores the code in sessionStorage (mirrored to localStorage for 24 hours so it survives opening the verification email in another tab). New accounts redeem it automatically right after registration, or after social sign-in plus age attestation, and skip Stripe. Existing accounts land on the redeem confirmation on `/app/membership`.

**Membership.** `/app/membership` shows status, starts Stripe Checkout or the billing portal, explains App Store and Google Play billing, polls `GET /membership` every 2 seconds for up to 30 seconds after `?checkout=success`, lists history, and has the "Redeem a code" form (check, then confirm; one generic invalid message; separate 429 message).

**Account settings.** `/app/account`: profile (shared `ProfileForm` with avatar upload, live handle availability, and the property-configured rank, level, affiliation, goals, and rank history), sign-in methods (link and unlink providers, set or change password), email verification resend, data export, and account deletion with a typed `DELETE` confirmation.

## Feed and engagement (Phase 2)

The binding contracts are `docs/PHASE2_CONTRACT.md` and `docs/ENUMERATION_AND_METADATA.md` (which overrides every list shape).

**Member app.** `/app/feed` (composer for posters, pinned then latest, infinite scroll by continuation token, "Your posts"), `/app/feed/:id` (post, comment tree, `?comment=` permalink), `/app/notifications` (bell with 60 second unread polling in `MemberLayout`), `/app/settings/notifications` (categories by channel, pause, notification hours with time zone), `/app/settings/blocked`, `/app/members/:id` (report, block). Components live in `src/components/feed/`. Bodies render through the shared Markdown AST only (no HTML). Video plays with native HLS on Safari and the hls.js light build elsewhere (`withCredentials`), loaded on demand. `useMediaGrant` keeps the media cookie fresh and renews it after a 403.

**Lists, filters, metadata.** Every list sends an EnumerationQuery (GET form). `MetadataFilterBar` adds the label chip input with autocomplete and All/Any, tag `key = value` rows with "has key", search and ordering, and active filter chips; state lives in the URL (`labels=`, `labelMatch=`, `tag.<key>=`). Admin tables convert their page to `maxResults` and `skip` and their sort to `ordering` (`utils/adminQuery.js`). `MetadataEditor` and `MetadataPanel` edit labels and tags (members never see `staff:` metadata).

**Admin console.** Feed management (`/admin/feed`), Moderation (`/admin/moderation`: queue, action history, moderators, word filter), Media library (`/admin/media`), Posting permissions and the feed moderator toggle in the member drawer, and moderation and engagement tiles on Home. Members with `Moderator` or `FeedModerator` sign in and see Home and Moderation only.

**Marketing.** Public sample posts load in the browser from `GET /public/feed` after hydration (never prerendered), at the `@public-posts` widget or at the end of the home page; `features.publicSamples: false` turns them off. Public sample threads work the same way from `POST /public/threads/enumerate` (the `@public-threads` widget, or the end of the home page, only when the server reports `features.forums`), and each links to the public thread page `/threads/{id}` (`GET /public/threads/{id}`, client-rendered from `spa.html`).

**Live access policy.** Marketing pages are prerendered from the build-time `property.json`, then `MarketingLayout` loads `GET /api/v1/property` after hydration and re-renders price, membership, and gating copy from the live access policy, so an admin change (for example to `Open`) shows without a rebuild. In content files, `when=cond` on a directive (`::: div when=purchases`) or in a line's trailing attribute block (`Members-only community {.eyebrow when=membersOnly}`) keeps it only while the condition holds. Conditions are comma separated (all must hold), each optionally negated with `!`: `purchases`, `signupCodes`, `anonymousBrowsing`, `membersOnly`, `Open`, `PartiallyGated`, `FullyGated`. When nothing is sold, every other line using `{price}`, `{priceAmount}`, `{interval}`, `{trialDays}`, or `{productName}` is dropped, and so are such sentences in meta descriptions.

## Admin sign in

The admin console accepts an email and password for members with the `SystemAdmin` role (it then uses the member session above), or the break-glass admin token via "Use admin token" (validated with `POST /api/v1/settings/enumerate` and `{ "maxResults": 1 }` and kept in `sessionStorage` for the tab). A signed-in non-admin sees "This account is not an administrator." In local Docker, the bootstrap admin from the server settings becomes SystemAdmin once its email is verified (Mailpit at http://127.0.0.1:8025).

Phase 1 admin views: **Members** (server-side search and filters, sortable columns, bulk revoke and ban, detail drawer with flags, scoped roles, comps, ban and mute, sessions), **Subscriptions** (store and status filters, event log with raw payloads), **Sign-up codes** (create single or batch with CSV, edit with locked fields after the first redemption, deactivate, copy code and join link, QR PNG download via `qrcode`, detail page with a 90-day SVG chart, outcomes, revoke and revoke-all with a required reason, CSV exports), **Audit Log**, and membership and code tiles on Home.

## Marketing site, SEO, and prerendering

Marketing routes are the property's content pages, its legal documents, and `/join` (head tags only). `src/views/marketing/siteMeta.js` (`createSiteMeta(site)`) builds titles, descriptions, canonical URLs on `https://<domains.web>`, Open Graph and Twitter tags, JSON-LD (`Organization` and `WebSite` always; `Person` and `Book` only when content front matter defines them), `sitemap.xml`, and `robots.txt`. Pages call `useRouteMeta(path)` to keep the head in sync during client navigation.

**Prerendering.** `npm run build` runs:

1. `vite build` (the client build; the Vite plugin emits brand files, manifest, sitemap, and robots)
2. `node scripts/prerender.mjs`: builds `scripts/prerender-entry.jsx` with `vite build --ssr` into `node_modules/.cache/trelliskit-prerender` (deleted afterwards), renders each route with `react-dom/server` inside the same `PropertyProvider`, `ThemeProvider`, and `AppRoutes` tree the client uses, and writes `dist/index.html` (home), `dist/<route>/index.html`, and `dist/spa.html` (the client shell with `noindex`, served for `/app/*`, `/admin/*`, auth pages, and 404s).

The markup is hydration safe (`test/prerender.test.jsx`). `main.jsx` hydrates when `#root[data-prerendered]` matches the path and renders on the client otherwise.

`npm run preview` serves `dist` the way production should (prerendered file if present, else `spa.html`). Production nginx needs the same rule: `try_files $uri $uri/index.html /spa.html;` with `Cache-Control: no-cache` on all HTML.

## Develop

Install from the repository root (npm workspaces):

```sh
npm install
```

Run the dev server on http://localhost:5173. `/api` and `/openapi.json` are proxied to the backend at http://127.0.0.1:8000 (override with `TK_BACKEND_URL`). The dev server builds the property from `TK_PROPERTY_DIR` (default `samples/example`) and reloads when its files change.

```sh
cd www
npm run dev
```

## Build, lint, test

```sh
cd www
npm run build      # production build to www/dist (with prerender)
npm run lint       # ESLint, zero warnings allowed
npm test           # Vitest unit and component tests (against test/fixtures/dojo)
```

End-to-end smoke tests (Playwright). Install a browser once, then run; the config starts the Vite dev server unless `WWW_E2E_BASE_URL` points at a running site:

```sh
npx playwright install chromium
npm run test:e2e
WWW_E2E_BASE_URL=http://127.0.0.1:8080 npm run test:e2e   # against the Docker www service
```

## Docker

The image is built from the engine root so it can include `shared/`. The property arrives as a separate named build context `property` (default `samples/example`), copied to `/property` and selected with `TK_PROPERTY_DIR=/property`:

```sh
docker build -f docker/www/Dockerfile --build-context property=<property folder> -t trelliskit/www:<property id> .
docker run --rm -p 8080:8080 trelliskit/www:<property id>
```

nginx (`docker/www/nginx.conf`) serves the SPA on port 8080 with history fallback, proxies `/api/` and `/openapi.json` to `http://server:8000`, and answers `GET /healthz` for health checks.

Catalog retrievals use the same `EnumerationQuery` / `EnumerationResult` contract as persisted lists. Settings and preference editors collect continuation pages before reconstructing their local editable models; the SDK returns `objects`, not `fields` or `categories`. Domain and application catalogs have separate `/admin/property/domains/enumerate` and `/admin/property/apps/enumerate` routes. Email templates, staff events, and document lists render server pages; template category filters are sent as `filters.category`. Mutation responses retain their documented editable-object shapes.

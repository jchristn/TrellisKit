# Shared

`@trelliskit/shared`: JavaScript shared by `www/` and `mobile/`: API client, enums, validators, formatters, and property helpers. No browser-only or Node-only APIs are used. Engine code only: nothing here names a property.

`ApiClient` options (all optional and backwards compatible): `headers` (object or function, for example `{ 'X-Client': 'mobile', 'X-Client-Version': '1.0.0' }`), `onUnauthorized({ method, path, error })` (resolve true after refreshing to retry the request once), and `credentials`. Per request: `rawBody` with `contentType` (avatar upload), `responseType` (`'json'`, `'text'`, `'blob'`), and `retryOnUnauthorized: false`. `client.property(signal)` reads `GET /api/v1/property` (anonymous). `singleFlight(fn)` shares one in-flight call (use it for token refresh). `identity.js` holds the Phase 1 enums and validators (`passwordProblems`, `isDisplayName`, `normalizeSignupCode`, `isSignupCode`, `parseDateOnly`). `isAdult(dateOfBirth, today, minimumAge)` takes the property's `membership.minimumAge` (default 18).

## Phase 2 and enumeration modules

| Module | Purpose |
|---|---|
| `enumeration.js` | `createEnumerationQuery` (clamps `maxResults` to 100 member or 500 admin, drops `skip` with a token), `toEnumerationBody` (POST `/enumerate`), `toEnumerationParams` and `toEnumerationSearch` (GET form: repeated `labels=`, `tag.<key>=<value>`, collection filters at the top level), `parseEnumerationParams` (URL state), `normalizeEnumerationResult` (also accepts older `CursorPage` and page-number shapes), `sortToOrdering` and `orderingToSort` (`-createdUtc` is `CreatedDescending`) |
| `metadata.js` | Label and tag limits, `validateMetadata` (problem codes), `isStaffKey`, `stripStaffMetadata`, `splitStaffMetadata`, `metadataForSave` (members never send `staff:` metadata), `metadataOf` |
| `engagementApi.js` | `ApiClient` methods for feed, public feed, link previews, reactions, comments, media, notifications, push tokens, reports, blocks, member search, moderators, word filter, `enumerate(path, query)`, label and tag enumeration, and generic metadata routes. List methods take an EnumerationQuery |
| `markdown.js` | The Markdown subset parser (`parseMarkdown` to an AST, never HTML), `isSafeHref` (http and https only), `toPlainText`, `markdownExcerpt`, `firstLinkUrl`, `scalarLength` |
| `mentions.js` | `extractMentionHandles`, `mentionQueryAt` (autocomplete trigger), `insertMention` |
| `cursor.js` | `createCursorPager` (continuation tokens, de-duplication by id) and `mergeUniqueById` |
| `reactions.js` | `reactionSet(property)`, `visibleReactions`, `applyReaction` (optimistic toggles), `createReactionSetCache` |
| `upload.js` | `uploadMedia` and `resumeUpload` (single PUT or parallel multipart with progress; resume through `upload-urls` sends only missing parts), `pollMedia`, `checkMediaFile`, `createFetchPut`. The caller supplies `slice` and `put`, so it runs on the web and in React Native |
| `notifications.js`, `moderation.js` | Categories, badge text, notification hours helpers; report reasons, actions per state, staff detection |

`ApiError` now carries `retryAfterSeconds` (from `Retry-After`) and a `fieldErrors` getter; `ApiClient.url` repeats array query values.

## Aggregate metrics (`metricsApi.js`)

`ApiClient` exposes `adminMetricRegistry(query, signal)`, `adminQueryMetrics({ ...enumeration, query: metricQuery }, signal)`, `adminMetricPages(query, signal)`, `adminMetricWorkers(query, signal)`, and `adminMetricAnnotations(query, signal)`. Responses retain their server envelopes, incomplete buckets, suppressed/null values, comparisons, and annotations.

Platform clients use `platformCompareMetrics({ query, includeSeries, ...enumeration }, signal)` for `/platform/metrics/comparisons/enumerate`, or `platformQueryMetrics(propertyId, query, signal)` for one property. Comparison pages are bounded to 20 rows and default to `ValueDescending`. The legacy `/platform/metrics/properties/enumerate` snapshot route remains unchanged. Authentication and property selection use the client's normal configuration.

## Property helpers (`property.js`)

The property shape is defined in `docs/TRELLISKIT.md` section 2.

| Helper | Purpose |
|---|---|
| `validateProperty(p)`, `loadProperty(jsonOrObject)` | Shape checks (schema version, required fields, `#RRGGBB` colors, positive price, unique keys); `loadProperty` throws with every problem named |
| `mergeProperty(base, runtime)` | Lay a `GET /api/v1/property` response over a bundled copy (invalid responses are ignored) |
| `rankConfig`, `rankHistoryConfig`, `affiliationConfig`, `goalsConfig`, `isProfileSectionEnabled` | Profile sections (`{ enabled: false }` when absent) |
| `findRank`, `rankLabel`, `rankColor`, `rankOptions`, `clampRankLevel` | Rank lookup by key |
| `formatRank(p, rank, level, { withLabel })` | Rank text from configuration only, for example "Gold tier, 2 stars" |
| `goalOptions`, `goalLabel`, `isFreeTextGoal` | Goals by key |
| `reactionList`, `findReaction` | Reactions by key |
| `propertyTokens`, `formatTemplate`, `fillTemplate` | Fill `{name}`, `{price}`, `{interval}`, `{minimumAge}`, `{owner}`, `{domain}`, `{supportEmail}`, and other tokens in engine strings |
| `formatUsdCents`, `intervalWord` | Price and billing interval text |

Breaking change (besides the package rename): the property-specific rank and goal enum exports were removed. Use `rankOptions(property)`, `rankLabel(property, key)`, `goalOptions(property)`, and `goalLabel(property, key)` with the property from `GET /api/v1/property` (or a bundled copy).

## Test

```sh
npm test --workspace shared     # node --test; fixtures in test/fixtures (dojo with ranks, bookclub without)
```

### Collection methods

Collection methods take `(query = {}, signal)` and return the full `EnumerationResult`; methods with a parent id take `(id, query = {}, signal)`. `mediaStatus(ids, query, signal)` additionally selects at most 20 requested assets. Use `objects` and `continuationToken` rather than assuming a catalog fits in one page. Filters such as `days`, `from`, `to` and worker names belong in `query.filters`. `getUpdateGroupAdoption(id, query, signal)` pages day rows; binary-version counts have a separate enumeration. Fixed configuration editors may use the shared continuation collector, which refuses missing/repeated cursors and explicit page-limit overflow instead of silently truncating. Dashboard tables retain the envelope for navigation. Mutation methods retain their existing resource response shapes.

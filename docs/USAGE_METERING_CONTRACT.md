# Usage Metering Contract

Status: binding engine specification (2026-11-12). Usage metering measures what each property (tenant) does on a TrellisKit deployment so a property administrator and a platform administrator can see utilization over time, and so a future usage-based billing engine can charge for it. This contract defines the meters, the rollups, the periods, the routes, and the guarantees. It does **not** define prices, plans, invoices, or payment: there is no billing in the engine.

It follows `docs/ENUMERATION_AND_METADATA.md` (every list bounded, labels and tags on every object) and `docs/MULTIPROPERTY_CONTRACT.md` (every row property-owned, property-bound data access, workers per property).

---

## 1. Concepts

| Term | Meaning |
|---|---|
| Meter | One named measurement of one property, for example `posts.new` or `storage.total`. The meter catalog (section 2) is fixed by the engine version |
| Kind | `Count` (things that happened in a bucket; summed over a period), `Distinct` (distinct members in a bucket; not summable), `Gauge` (a level as of an instant, for example bytes stored; the value of a period is the value at its end) |
| Unit | `count`, `bytes`, `tokens`, `usdMicros` (millionths of a US dollar, as recorded by the AI usage rollups; a cost, not a price) |
| Source | Where a meter comes from: `Derived` (recomputed from the engine's own tables), `Recorded` (counted on write by the engine, no source table), `Measured` (sizes read from the object store) |
| Bucket | One time slot of a series: an hour (UTC hour), a local day, a local week, or a local month |
| Hourly row | `usagehourly`: one value per meter per UTC hour. Raw, kept `usage.hourlyRetentionDays` (90) |
| Daily row | `usagedaily`: one value per meter per local day of the property. Kept long term (`usage.dailyRetentionDays`, 0 = forever, the default). Exposed as the `UsageRecord` object |
| Open, closed | A daily row is open until `usage.closeAfterHours` (48) after its day ends; then it is closed and immutable |

---

## 2. Meters

Every meter is per property. "In the bucket" means the timestamp named is `>= bucket start` and `< bucket end`. Soft-deleted rows count in the flow meters (they were created) and stop counting in the gauges at their deletion time. Rows that were hard-deleted before a bucket was computed are invisible to it (see section 5.3).

### 2.1 Members

| Meter | Kind | Unit | Definition |
|---|---|---|---|
| `users.new` | Count | count | Member accounts (`users`) with `createdutc` in the bucket |
| `users.deleted` | Count | count | Member accounts with `deletedutc` in the bucket (account deletion) |
| `users.total` | Gauge | count | Member accounts that exist at the instant: `createdutc < t` and not deleted at `t` |
| `users.active` | Distinct | count | Distinct members active in the bucket. Active means the member signed in (an `authsessions` row created, `principalkind = 'Member'`) or made an authenticated request (a session touch, recorded in `usageactivity` at UTC hour granularity, at most once per member per hour; a session's `lastusedutc` also counts). Delegated platform sessions never count. For a day this is DAU |
| `users.activeWeekly` | Distinct | count | Daily rows only: distinct members active in the 7 local days ending with the row's day (WAU as of that day) |
| `users.activeMonthly` | Distinct | count | Daily rows only: the same over 30 local days (MAU as of that day) |

### 2.2 Content

| Meter | Kind | Unit | Definition |
|---|---|---|---|
| `posts.new` | Count | count | Feed posts (`feedposts`) plus forum threads (`forumthreads`) with `createdutc` in the bucket, in any state (draft, scheduled, published, later deleted) |
| `posts.total` | Gauge | count | Feed posts plus forum threads created before `t` and not deleted at `t` |
| `comments.new` | Count | count | Comments (`comments`, on feed posts and threads) with `createdutc` in the bucket |
| `comments.total` | Gauge | count | Comments created before `t` and not deleted at `t` |

### 2.3 Interactions

Interactions are the member actions that are neither content nor sign-ins:

| Meter | Kind | Unit | Definition |
|---|---|---|---|
| `reactions.new` | Count | count | Reactions (`reactions`) with `createdutc` in the bucket. A reaction removed before its bucket is computed is not counted (reactions are hard-deleted) |
| `pollVotes.new` | Count | count | Poll votes (`pollvotes`) with `createdutc` in the bucket (one per selected option) |
| `follows.new` | Count | count | Explicit follows: forum follows (`forumfollows`) plus thread follows with `source = 'Explicit'` (automatic follows from authoring or commenting are not interactions) |
| `assistantMessages.new` | Count | count | Member and visitor messages sent to AI assistants (`aimessages` with `role = 'User'`) |
| `interactions.new` | Count | count | The sum of the four meters above. Event RSVPs and direct messages join this sum when those features ship (a new engine version adds them to the sum from that version on; closed periods are never recomputed) |

### 2.4 Media and storage

| Meter | Kind | Unit | Definition |
|---|---|---|---|
| `images.new`, `videos.new` | Count | count | Media rows (`mediaattachments`) of kind `Image` or `Video` with `createdutc` in the bucket whose upload completed (status other than `Uploading` when computed) |
| `images.bytes`, `videos.bytes` | Count | bytes | The original sizes (`sizebytes`) of those uploads |
| `images.total`, `videos.total` | Gauge | count | Completed media of that kind created before `t` and not deleted at `t` |
| `storage.originals` | Gauge | bytes | Sum of `mediaattachments.sizebytes` of completed media created before `t` and not deleted at `t` (tracked on write: the size is recorded when the upload completes) |
| `storage.imageVariants` | Gauge | bytes | Sum of `mediavariants.sizebytes` (WebP and JPEG renditions) of those images, doubled for public media (a public copy of each served output is stored) |
| `storage.videoRenditions` | Gauge | bytes | Sum of the measured bytes of each ready video's transcoded outputs and public copies (`usagemediasizes`, section 6) |
| `storage.updateAssets` | Gauge | bytes | Sum of `updateassets.sizebytes` of live update assets stored before `t` |
| `storage.exports` | Gauge | bytes | Sum of `accountexports.sizebytes` of account export archives completed before `t` and not expired at `t` |
| `storage.total` | Gauge | bytes | The sum of the five storage classes |

Not metered (small and bounded by configuration): avatars, link preview images, and property brand files. Media soft-deleted and awaiting purge are not counted (consistent with the platform property metrics `storageBytes`).

### 2.5 Platform consumption

| Meter | Kind | Unit | Source | Definition |
|---|---|---|---|---|
| `api.requests` | Count | count | Recorded | Requests under `/api/` resolved to the property, any status, except `/api/v1/health*`. Counted in memory by each server instance at the end of the request and added to the hourly row by the next flush (section 4.4) |
| `api.errors` | Count | count | Recorded | The subset with status 500 or above |
| `push.sent` | Count | count | Derived | Push deliveries (`pushdeliveries`) accepted by the push provider (`status` `Sent` or `Ok`) with `createdutc` in the bucket |
| `email.sent` | Count | count | Derived | Administrative and transactional email handed to the provider (`emailmessages.sentutc` in the bucket) plus category email sends (`emailsends` with `status = 'Sent'`, by `createdutc`) |
| `ai.requests` | Count | count | Derived | Model calls in the AI usage rollups (`aiusagerollups`, non-test traffic), any outcome |
| `ai.promptTokens`, `ai.completionTokens` | Count | tokens | Derived | Prompt and completion tokens of those calls |
| `ai.costMicros` | Count | usdMicros | Derived | The cost the AI rollups recorded from each endpoint's configured token costs |

---

## 3. Time zone, buckets, and periods

### 3.1 Property time zone

Each property has `usage.timeZone` (property setting, an IANA zone id such as `America/New_York`; default `UTC`) and `usage.weekStartsOn` (property setting, `Monday` by default as in ISO 8601, any day name; decided 2026-11-12). Local days, weeks, months, quarters, and years are computed in that zone with the engine's time zone database, so a local day is 23 or 25 hours long across a daylight saving change. Hourly buckets are UTC hours (labeled in local time by clients).

A time zone change applies from the next rollup: open daily rows are deleted and recomputed in the new zone; closed rows keep the zone they were computed in (each row records its `timeZone`).

Zones with a non-whole-hour offset (for example `Asia/Kolkata`): derived meters are exact per local day; recorded meters (`api.*`) and the hourly activity of `users.active` are assigned to the local day containing the start of their UTC hour.

### 3.2 Periods

`now` is the request time. `today` is the local day containing `now`. All periods end at `now` (the current bucket is partial) except `PreviousQuarter` (a whole closed quarter) and `Range`. Decided 2026-11-12: both a trailing 90 days (`Last90Days`) and calendar quarters (`PreviousQuarter`, `ThisQuarter`) are offered.

| `period` | From (inclusive) | To (exclusive) | Default bucket | Previous equivalent period |
|---|---|---|---|---|
| `LastDay` | the UTC hour containing `now` minus 23 hours | `now` | Hour (24) | shifted back 24 hours |
| `LastWeek` | local midnight of `today` minus 6 days | `now` | Day (7) | both ends shifted back 7 local days |
| `LastMonth` | `today` minus 29 days | `now` | Day (30) | shifted back 30 local days |
| `Last90Days` | `today` minus 89 days (trailing 90 days) | `now` | Day (90) | shifted back 90 local days |
| `PreviousQuarter` | local midnight of the first day of the previous calendar quarter | local midnight of the first day of the current quarter (a whole quarter; does not end at `now`) | Day (90 to 92) | the calendar quarter before it |
| `LastYear` | `today` minus 364 days | `now` | Week (53) | shifted back 365 local days |
| `ThisWeek` | local midnight of the latest `weekStartsOn` day | `now` | Day | the previous week, to the same local time offset into it |
| `ThisMonth` | local midnight of the 1st | `now` | Day | the previous month to the same local day and time, capped at its end |
| `ThisQuarter` | local midnight of the first day of the calendar quarter (January, April, July, October) | `now` | Day | the previous calendar quarter to the same offset, capped at its end |
| `ThisYear` | local midnight of January 1 | `now` | Day up to 93 days, else Week | the previous year to the same local month, day, and time |
| `Range` | `from` | `to` | by length | the same number of local days (or the same duration for date-time bounds) immediately before |

`Range` takes `from` and `to` as local dates (`YYYY-MM-DD`; `to` is inclusive, so the range ends at local midnight after `to`) or as ISO 8601 date-times with an offset (exclusive end). `from` must be before `to` and not after `now`; `to` is clamped to `now`; at most 1830 days (5 years). Invalid input is `400 ValidationFailed` with `fieldErrors.period`, `fieldErrors.from`, or `fieldErrors.to`.

### 3.3 Bucket size

The default bucket follows the period length `L`: Hour when `L <= 2 days`, Day when `L <= 93 days` (a whole calendar quarter, including a daylight saving hour), Week when `L <= 400 days`, else Month. A `bucket` query parameter (`Hour`, `Day`, `Week`, `Month`) overrides it when the result has at most 400 buckets and, for `Hour`, `L <= 14 days` and the period starts inside the hourly retention. Weeks start on `weekStartsOn`; the first and last buckets are clipped to the period. Hour buckets read hourly rows; Day, Week, and Month buckets read daily rows.

### 3.4 Period totals

| Kind | `total` of a period |
|---|---|
| Count | Sum over the period: whole local days from daily rows, partial days at either end from hourly rows (when inside the hourly retention; otherwise the whole day) |
| Gauge | The value at the period end (the latest hourly row inside the hourly retention, else the daily row of the last day) |
| Distinct, trailing (`users.activeWeekly`, `users.activeMonthly`) | The value of the last daily row of the period (WAU or MAU as of the period end); in Week and Month buckets the value of the bucket's last day; no values in Hour buckets (`null`) |
| Distinct (`users.active`) | Distinct members active anywhere in the period, computed live from the activity sources when the period starts inside `usage.activityRetentionDays` (`exact: true`); otherwise the largest `users.activeMonthly` in the period (`exact: false`, a lower bound) |

`change` is `(total - previousTotal) / previousTotal`, or null when `previousTotal` is 0.

---

## 4. Collection and rollup

### 4.1 Tables (migration 70, property-owned, every index starts with `propertyid`)

| Table | Columns | Keys |
|---|---|---|
| `usagehourly` | `propertyid, meter, periodstartutc, value, computedutc` | PK `(propertyid, meter, periodstartutc)` |
| `usagedaily` | `propertyid, id, meter, day, timezone, periodstartutc, periodendutc, value, closed, computedutc, createdutc` | PK `(propertyid, meter, day)`; unique `(propertyid, id)`; index `(propertyid, periodstartutc, meter)` |
| `usageactivity` | `propertyid, userid, hourutc` | PK `(propertyid, hourutc, userid)` |
| `usagemediasizes` | `propertyid, mediaid, sizebytes, objectcount, measuredutc` | PK `(propertyid, mediaid)`; index `(propertyid, measuredutc)` |
| `usagestate` | `propertyid, timezone, backfillstartutc, backfillcursorutc, hourlycursorutc, backfilledutc, lastrolluputc, lastflushutc, storagescanutc, storagecursor, storagemeasured, storagedriftbytes, storagemissing, updatedutc` | PK `(propertyid)` |

Migration 70 also adds `(propertyid, createdutc)` indexes on the source tables that lacked one (feed posts, threads, comments, reactions, poll votes, follows, media, push deliveries, email sends, assistant messages, sessions) and `(propertyid, sentutc)` on `emailmessages`. Usage record ids are deterministic: `usd_<yyyymmdd>_<meter with dots as dashes>`.

### 4.2 Worker

`UsageWorker` runs per property (one instance per enabled property, like every property worker) every `usage.rollupIntervalMinutes` (5). Each pass (the property's worker; `UsageService.RunPassAsync` runs steps 3 to 7):

1. **Flush** this instance's recorded counters (always, without the lease; section 4.4).
2. Acquire the property's job lease `usage_rollup` (bound to the property, so properties never block one another; one server instance rolls up a property at a time). Without the lease the pass ends.
3. **Measure storage** (section 6), at most `usage.storageBatchSize` (100) media per pass, before the gauges are computed.
4. **Backfill** (first run, section 4.3), at most `usage.backfillDaysPerPass` (92) local days and 7 days of hours per pass.
5. **Recompute the open window**: every hourly bucket whose end is after `now - closeAfterHours`, and every open daily row, for every derived and measured meter. Recorded meters in daily rows are the sum of their hourly rows.
6. **Close** daily rows whose day ended at least `closeAfterHours` ago.
7. **Prune** hourly rows older than `usage.hourlyRetentionDays`, activity rows older than `usage.activityRetentionDays` (35), measurements of media that no longer exist, and (when `usage.dailyRetentionDays` is not 0) daily rows older than it. Prunes delete in batches of at most 5000 rows.

Every step is bounded; a pass that has more work leaves its cursor in `usagestate` and continues next pass. Closing is part of the recompute: a row written after its close time is written closed, and the write never changes a row that is already closed. Rows left open by downtime are recomputed and closed oldest first, within the backfill bound.

### 4.3 Accuracy and idempotency

- **Recompute, never increment.** Derived and measured values are computed from the source tables for a list of buckets and written with replace semantics (`INSERT ... ON CONFLICT DO UPDATE SET value = EXCLUDED.value`), including zeros. Running a pass twice, or two passes for the same buckets, produces the same rows. No derived counter is ever incremented, so nothing is double counted.
- **Recorded meters** are the only additive values. Each server instance counts its own requests and adds them once: a flush atomically swaps the in-memory counters out, writes them in one statement, and on failure adds them back for the next flush. A process crash loses at most the counts since the last flush (an undercount, never a double count). Daily rows of recorded meters are recomputed as the sum of hourly rows.
- **Backfill.** On the first pass for a property, `backfillstartutc` is set to the local midnight before the oldest row of its members, posts, threads, comments, or media; daily rows are computed from there to today and closed when older than `closeAfterHours`; hourly rows are computed for the last `hourlyRetentionDays`. Recorded meters have no history (zero before metering started). Activity before metering started comes from session sign-in and last-use times only (a lower bound).
- **Closed periods are immutable.** Once a daily row is closed it is never recomputed, even if source rows change later (a removed reaction, a purged post). This makes every closed local day a stable statement of usage for billing.

### 4.4 Recorded counters

Each property's services hold a `UsageRecorder`: a lock-free counter per (meter, UTC hour). `PostRouting` records every API request into the resolved property's recorder (`api.requests`, and `api.errors` for 5xx). The flush writes `INSERT ... ON CONFLICT DO UPDATE SET value = usagehourly.value + EXCLUDED.value`. Flushes run every pass and at shutdown. `usage.recordRequests` (true) turns request counting off.

Member activity is recorded on the session touch (at most once per member per `auth.sessionTouchMinutes`): one `usageactivity` row per member per UTC hour (`ON CONFLICT DO NOTHING`).

---

## 5. Retention

| Data | Kept | Setting |
|---|---|---|
| Hourly rows | 90 days (7 to 400) | `usage.hourlyRetentionDays` |
| Daily rows (`UsageRecord`) | Forever (0), or N days | `usage.dailyRetentionDays` |
| Member activity hours | 35 days (31 to 400) | `usage.activityRetentionDays` |
| Video rendition measurements | While the media exists | |

Purging a property (`PropertyPurgeWorker`) deletes its usage rows like every property-owned table. Account deletion does not change usage history (usage rows hold no member data except `usageactivity`, whose rows for the member are deleted with the account by the activity retention).

---

## 6. Storage accounting

Originals, image variants, update assets, and exports are **tracked on write**: their byte sizes are stored on their rows when written, and the gauges sum those rows. Video renditions have no size on any row, so they are **measured**: each pass lists the object store keys under the video's transcoded prefix and public video prefix (`MediaKeys.VideoPrefix`, `MediaKeys.PublicVideoPrefix`), reads each object's size, and stores the total in `usagemediasizes`. Ready videos without a measurement are measured first (newest first), then measurements older than `usage.storageReconcileDays` (7) are refreshed oldest first, at most `usage.storageBatchSize` per pass, for both local storage and S3.

Reconciliation of tracked sizes: the same pass checks up to `storageBatchSize` media originals (cursor by id) with a HEAD request and records in `usagestate` how many were checked, how many were missing, and the total byte difference (`storagedriftbytes`). Drift is reported (summary `storageReconciliation`, telemetry), never silently written back into media rows.

---

## 7. Routes

All routes return JSON unless noted. Property routes: the property's `SystemAdmin` (or a delegated platform session) or the break-glass admin token. Platform routes: a platform administrator token, platform host, `MultiProperty` only (like every `/api/v1/platform/*` route).

### 7.1 Property administrator

| Method and path | Input | Success | Errors |
|---|---|---|---|
| `GET /api/v1/admin/usage/summary`, `POST /api/v1/admin/usage/summary/enumerate` | Query `period` (default `LastMonth`), `from`, `to` (`Range`), `bucket` (optional override), `meters` (optional comma-separated meter ids or group names: `users`, `content`, `interactions`, `media`, `storage`, `platform`, `ai`) | `200 EnumerationResult<UsageObservationRow>` | `400 ValidationFailed` |
| `GET /api/v1/admin/usage` and `POST /api/v1/admin/usage/enumerate` | `EnumerationQuery`; filters `meter` (an id or group name), `fromDay`, `toDay` (local dates, inclusive), `closed` (`true`/`false`); orderings `CreatedDescending` (default, by period start), `CreatedAscending`, `MeterAscending`, `ValueDescending`; the standard created window applies to `periodStartUtc`; labels and tags (`UsageRecord`) | `200 EnumerationResult<UsageRecord>` | `400 ValidationFailed`, `400 InvalidContinuationToken` |
| `GET /api/v1/admin/usage/export` | Query `period`, `from`, `to` (as summary), `format` (`json` default, or `csv`), `granularity` (`Day` default, or `Hour` for periods of at most 31 days inside the hourly retention) | `200` file, `Content-Disposition: attachment; filename="usage-<propertyId>-<from>-<to>.<ext>"` | `400 ValidationFailed` |

`UsageSummary`:

```json
{
  "propertyId": "app_x",
  "period": "LastMonth",
  "timeZone": "America/New_York",
  "weekStartsOn": "Monday",
  "fromUtc": "2026-10-14T04:00:00Z",
  "toUtc": "2026-11-12T18:03:11Z",
  "previousFromUtc": "2026-09-14T04:00:00Z",
  "previousToUtc": "2026-10-13T18:03:11Z",
  "bucket": "Day",
  "success": true,
  "maxResults": 1,
  "skip": 0,
  "continuationToken": "opaque-next-page",
  "endOfResults": false,
  "totalRecords": 30,
  "recordsRemaining": 29,
  "timestampUtc": "2026-11-12T18:03:11Z",
  "objects": [
    { "id": "posts.new:2026-10-14T04:00:00Z", "meter": "posts.new", "group": "content", "kind": "Count", "unit": "count", "total": 120, "previousTotal": 100, "change": 0.2, "exact": true, "startUtc": "2026-10-14T04:00:00Z", "endUtc": "2026-10-15T04:00:00Z", "label": "2026-10-14", "value": 3, "labels": [], "tags": {} }
  ],
  "dataThroughUtc": "2026-11-12T18:00:04Z",
  "backfillComplete": true,
  "storageReconciliation": { "checkedUtc": "2026-11-12T17:55:00Z", "checked": 100, "missing": 0, "driftBytes": 0 },
  "generatedUtc": "2026-11-12T18:03:11Z"
}
```

The source selection has at most 400 buckets and the bounded meter catalog (section 2). Each transport page uses the standard admin cap and carries actual meter/bucket observations in `objects`. GET accepts standard query parameters; POST puts period/from/to/bucket/meters in `filters`. Follow continuation tokens to render the requested chart window; the example counts are illustrative. `label` is the local date (`YYYY-MM-DD`), week start date, month (`YYYY-MM`), or local hour (`YYYY-MM-DDTHH:00`).

`UsageRecord` (one daily row): `{ id, propertyId, meter, group, kind, unit, day, timeZone, periodStartUtc, periodEndUtc, value, closed, computedUtc, createdUtc, labels, tags }`. Usage records carry labels and tags like every object (staff only; the generic metadata routes with `targetType` `UsageRecord`).

### 7.2 Platform

| Method and path | Input | Success |
|---|---|---|
| `GET /api/v1/platform/properties/{propertyId}/usage/summary`, `POST /api/v1/platform/properties/{propertyId}/usage/summary/enumerate` | as 7.1 | `200 EnumerationResult<UsageObservationRow>` |
| `GET /api/v1/platform/properties/{propertyId}/usage`, `POST .../usage/enumerate` | as 7.1 | `200 EnumerationResult<UsageRecord>` |
| `GET /api/v1/platform/properties/{propertyId}/usage/export` | as 7.1 | `200` file |
| `GET /api/v1/platform/usage/properties`, `POST /api/v1/platform/usage/properties/enumerate` | `EnumerationQuery`; filters `period` (default `LastMonth`), `from`, `to`, `status`; search over id, slug, name; orderings `NameAscending`, `CreatedDescending`, `CreatedAscending`, and the computed `UsersDescending`, `ActiveUsersDescending`, `PostsDescending`, `CommentsDescending`, `InteractionsDescending`, `ImagesDescending`, `VideosDescending`, `StorageDescending`, `ApiRequestsDescending`, `PushDescending`, `EmailDescending`, `AiTokensDescending`, `AiCostDescending` (sorted over at most 500 matching registry rows, paged with standard continuation tokens or `skip`; larger matching sets must be narrowed) | `200 EnumerationResult<PropertyUsageRow>` |

`PropertyUsageRow`: `{ propertyId, slug, name, status, period, fromUtc, toUtc, usersTotal, usersNew, activeUsers, posts, comments, interactions, images, imageBytes, videos, videoBytes, storageBytes, apiRequests, pushSent, emailSent, aiTokens, aiCostMicros, dataThroughUtc, labels, tags }` (every value is the period total of section 3.4 in that property's own time zone). The existing `POST /api/v1/platform/metrics/properties/enumerate` is unchanged.

---

## 8. Clients

- **Property admin console:** a "Usage" page (System group): a period picker (Last day, Last week, Last month, Last 90 days, Previous quarter, This quarter, Last year, This week, This month, This year, Custom range with two dates; kept in the URL), summary cards per meter group with the change versus the previous equivalent period, and a chart per meter: line or area for gauges (totals over time), bars for counts per bucket, a stacked area for storage by class. Every chart has a text alternative (a one-sentence summary) and a "Show data" table; export buttons (CSV, JSON).
- **Platform console:** a "Usage" overview (the comparison table with period picker, ordering by each meter, totals row) and a "Usage" tab on each property's detail page (the same page as the property console, reading the platform routes).
- Responsive at phone and tablet widths; light and dark themes; English strings in the strings module.

---

## 9. Telemetry

| Instrument | Labels | Meaning |
|---|---|---|
| `trelliskit.usage.passes` | `outcome` (`ok`, `skipped`, `error`) | Rollup passes |
| `trelliskit.usage.pass.duration` | | Pass duration (seconds) |
| `trelliskit.usage.rows` | `granularity` (`hour`, `day`) | Rollup rows written |
| `trelliskit.usage.backfill.days` | | Local days backfilled |
| `trelliskit.usage.storage.measured` | `outcome` (`ok`, `missing`) | Video renditions measured and originals reconciled |
| `trelliskit.usage.exports` | `format` | Usage exports served |
| `trelliskit.usage.storage.bytes` (gauge) | `property`, `class` | Latest storage gauge per class |
| `trelliskit.usage.users.total` (gauge) | `property` | Latest member total |
| `trelliskit.usage.lag.seconds` (gauge) | `property` | Seconds since the last completed rollup |

Grafana: `assets/grafana/usage.json` (dashboard "Usage", queries `trelliskit_usage_*`, `$property` variable). The gauges carry the `trelliskit.property` label explicitly; business usage over time is read from the Usage pages, not from Grafana.

---

## 10. Settings

Deployment (`/api/v1/settings`, section "Usage"): `usage.enabled` (true), `usage.rollupIntervalMinutes` (5, 1 to 60), `usage.closeAfterHours` (48, 1 to 168), `usage.hourlyRetentionDays` (90, 7 to 400), `usage.dailyRetentionDays` (0 = forever, 0 to 36500), `usage.activityRetentionDays` (35, 31 to 400), `usage.backfillDaysPerPass` (92, 1 to 366), `usage.storageBatchSize` (100, 1 to 1000), `usage.storageReconcileDays` (7, 1 to 90), `usage.recordRequests` (true). Per property (`/api/v1/admin/property/settings`, section "Usage"): `usage.timeZone` (`UTC`, a valid IANA id), `usage.weekStartsOn` (`Monday`).

---

## 11. Future billing hooks

The engine has no billing. A billing engine (inside or outside TrellisKit) reads usage this way:

1. **Billable meters** are a subset of the catalog chosen by the billing engine (for example `users.activeMonthly` peak, `storage.total` average or end-of-period, `videos.bytes`, `api.requests`, `ai.costMicros`). Meter ids are stable; a meter's definition never changes within an engine major version, and a changed definition gets a new id.
2. **Stable export.** `GET /api/v1/platform/properties/{id}/usage/export?period=Range&from=YYYY-MM-DD&to=YYYY-MM-DD&format=json` returns daily rows and period totals (`schemaVersion: 1`). The JSON carries `complete` (true when every daily row of the range is closed) and `closedThroughDay`; a billing engine bills a range only when `complete` is true.
3. **Immutable closed periods.** Closed daily rows never change, so re-reading a closed range always returns the same numbers; a billing run is reproducible and idempotent by `(propertyId, from, to)`.
4. **Time zone of record.** Each row carries the zone it was computed in; a billing engine bills by local day in the property's zone, matching what the administrator saw on the Usage page.
5. **Gauges for billing** (storage, members) aggregate daily rows (end-of-day values) by average, maximum, or end of period, decided by the billing engine, never by the engine.
6. **Future additions** (not built): a usage webhook when a day closes, per-property quotas and alerts on meters, and a platform `billing` module reading the export; none of them change this contract's rows.

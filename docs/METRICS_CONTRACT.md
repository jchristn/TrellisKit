# Metrics and charts contract

This contract describes the generic engine metrics introduced by schema migrations 120–121. A property's name, domain, profile taxonomy, event names, and domain dashboards remain in its configuration or extension code. Usage billing records remain a separate contract: registering a console metric does not add it to billing exports.

## Registry and extensions

`MetricsService.Registry` registers the engine catalog, including every `UsageMeters` entry, and merges `property.json`'s `metrics.definitions`, `metrics.events`, and `metrics.pages`. Each definition has a stable key, title, unit, kind (`Count`, `Gauge`, `Distinct`, `Distribution`), source, allowed dimension names, allowed buckets, retention, required role, optional feature, and optional local drilldown path. All shipped metrics require `SystemAdmin`; configuration cannot weaken that requirement.

There are at most 256 definitions, 100 property definitions, 100 custom events, 20 property pages, 24 charts per page, eight dimensions per metric, and 100 custom alert rules. Existing keys cannot be replaced. Keys are bounded identifiers, not executable queries. The six engine page ids are `health`, `growth`, `engagement`, `ai-usage`, `workers`, and `moderation-support`. A matching property page id appends charts; a new id creates a page. Charts declare `metric`, `form`, optional `groupBy`, `percentile`, `threshold`, and `feature`.

Trusted server extensions register gauge definitions and call `RegisterSnapshot` before workers start. Up to 100 producer callbacks may each return at most 1,000 observations per pass. Callbacks use the property-bound database. The service validates all returned metric keys/dimensions before writing. This is the extension boundary for a property's own schema; configuration never executes arbitrary SQL. Server code can also call `RecordAsync` for registered counters, current distinct-count snapshots, gauges, and distribution observations. A count observation supplies the increment in `value`; `samples` is observation/sample size, not an automatically maintained unique-member set. A `Distinct` extension must compute its distinct cardinality at its source; it is stored and queried as a snapshot, never summed across repeated hours.

Example property declaration:

```json
{
  "metrics": {
    "enabled": true,
    "retentionDays": 180,
    "workerRetentionDays": 30,
    "productEventsEnabled": false,
    "definitions": [
      { "key": "community.capacity", "title": "Capacity", "kind": "Gauge", "source": "Snapshot", "unit": "count", "groupings": ["category"], "memberSensitive": true }
    ],
    "pages": [
      { "id": "community-overview", "title": "Community overview", "question": "How is the community doing?", "charts": [
        { "id": "capacity", "metric": "community.capacity", "title": "Capacity", "form": "StackedArea", "groupBy": "category" }
      ] }
    ]
  }
}
```

The declaration makes a metric queryable; its extension callback supplies the data. Properties may also declare pages using existing engine metrics without adding producers.

## Query API, bounds, and privacy

`GET /api/v1/admin/metrics` and `/admin/metrics/pages`, and their POST `/enumerate` forms, accept `EnumerationQuery` and return standard pages of definitions or page declarations. `POST /api/v1/admin/metrics/query` and `/query/enumerate` accept `MetricEnumerationQuery`: standard enumeration fields plus nested `query: { metric, from, to, bucket, groupBy, filters, compare, percentile }`; dates must be UTC, `from` is inclusive and `to` exclusive. `Hour`, `Day`, and `Week` are accepted only when declared by the metric. Filters are exact dimension-value matches, with at most eight filters and 100 characters/value. Maximum range is 1,830 days, further bounded to 2,000 buckets. At most 20 leading groups plus `Other` are returned; grouping ranks observed sample volume before folding, and folded groups remain represented in totals. This is not an unbounded scan of every label.

Responses are `EnumerationResult<MetricObservationRow>` with scalar metric/unit/calendar and completed-rollup watermark context. Each object is an actual bucket identified by period (`Current` or `Previous`) and series key, with UTC start/end, nullable value, completion, suppression and sample count. Standard paging/search/date/metadata filters apply, including filters.period/seriesKey. `GET /api/v1/admin/metrics/annotations` and its POST `/enumerate` form page annotations separately. The complete metric selection and property are bound into observation cursors. Missing data stays null. A ratio has zero numerator when a measured count denominator exists but no numerator events occurred; absent denominators remain gaps. Empty or suppressed data is not fabricated as success. Distribution sketches have logarithmic 1.1-ratio bins and are merged before selecting percentiles; p50/p95/p99 are approximations bounded by those bins, never averages of percentiles.

Member-sensitive groupings smaller than five are suppressed with null value and zero samples. When a query combines potentially overlapping sensitive gauge groups, the smallest component sample count is used conservatively rather than summing people across profile fields. Repeated snapshots never inflate the privacy sample count: gauges use the most recent snapshot within each requested bucket. Profile metrics contain only declared select-field keys/options and aggregated counts, never names or member identifiers. Cohort and contribution metrics contain aggregated counts/percentages. Message metrics count activity; they do not store message content. Route templates replace unknown path segments with `{id}`; error dimensions store only bounded error identifiers; no URL queries, response messages, credentials, email addresses, or raw content enter these aggregates.

Operational request, worker, AI, dependency, and realtime series use UTC. Business day/week boundaries use `usage.timeZone` and `usage.weekStartsOn`. Hour buckets use UTC instants in all calendars so repeated/skipped daylight-saving hours remain unambiguous. Comparison covers the immediately preceding interval of equal elapsed duration. Weekly/monthly active usage meters are rolling cardinality snapshots; their values are not additive across days.

## Producers and source semantics

`MetricsWorker` runs once per minute with a property-scoped durable lease. Source rollups replace a bounded overlapping window atomically, preserving replay safety. The initial window is 24 hours; later passes overlap the previous hour and are capped at 48 hours. Recovery after a longer outage does not invent missing history. Snapshot callbacks capture the current hour; old lifecycle states cannot be reconstructed, so historical snapshot gaps remain visible. Hours are marked complete after they close. Existing usage workers independently retain their own backfill/calendar behavior.

| Family | Source and meaning |
|---|---|
| Requests | Request history: counts/status classes, response-code families, normalized route, bounded client headers, latency sketches, rate limits, UTC weekday/hour load. |
| Growth | Usage totals/new/deleted/daily-weekly-monthly active; source methods from identities; opt-in signup source/funnel events; select-field mix; subscription status/store plus code/comp entitlements; durable cancellation/terminal-status transitions; observed weekly cohort return activity. |
| Engagement | Usage interaction counts and storage classes; forum threads/replies/unanswered and first external reply latency; lesson plays/completions by series; event responses and checked-in/no-show Going RSVPs after event end; listing lifecycle/submissions/decisions; conversation/message counts; notification sent/delivered/read counts; top 1/10/50% contribution share. |
| AI | Non-test call records: calls, outcomes, token classes, cost, total/first-token latency; provider/endpoint/model/assistant dimensions; cost per successful call; endpoint health; assistant conversation/message counts. Absent first-token timings remain absent. |
| Workers | Every interval pass: start/end, outcome, item count, interval seconds, exception type only; aggregate duration/items/run counts and last-success lag. |
| Moderation/support | Reports/reasons, actions, response latency, word-filter actions and subsequent restorations of flagged targets, unresolved reports, ticket opened/resolved/backlog/first-response/type, auth failure/reset/lockout/rate-limit response counts. |
| Dependencies | Observed database query success, model endpoint health, recent email/push outcomes and storage processing outcomes. A lack of observations is a gap, not a successful synthetic delivery probe. |
| Projection | Storage's latest observation plus linear growth over the last seven days projected 30 days; at least two observations required. |

Notification delivered/opened stages are recorded only where the channel actually exposes them. In-app read timestamps supply opens; push receipts supply delivery; email delivery webhooks supply delivery. There is no fabricated email-open tracking pixel. Cohort boundaries, returning-activity windows, and week ages use the configured timezone and week start; ages use local calendar-date differences so DST cannot shift a week. Cohort history starts when snapshots run; no historical member-level behavioral stream is reconstructed. Membership categories may overlap when a member has multiple entitlement sources. Library completion rates compare observed completions to observed plays in the selected period, not a deduplicated per-member conversion cohort.

Membership lapse/cancellation counts are captured atomically by the subscription store on a real status or auto-renew change. The dimensions distinguish current `status`, observed `transition` (`Cancelled`, `Expired`, `Revoked`, `Refunded`), and `store`. Concurrent/repeated reconciliation cannot double-count or move past events. A newly imported already-expired subscription does not invent a historical transition. Changing auto-renew back on is not a lapse; a later cancellation is a new transition.

## Retention and operational annotations

Aggregate/annotation retention defaults to 180 days (1–1,830 configurable); worker detail defaults to 30 days (1–365). A definition can shorten aggregate retention; the effective bound is the smaller of property and definition retention. Cleanup deletes at most 5,000 expired rows/table or metric each pass, preserving property scope. Usage records keep their existing usage retention. Request history keeps its existing shorter policy; rollups do not extend raw-body retention.

`AnnotateAsync` stores a bounded id/kind/label/time, deduplicated by property and id. Deploy initialization, live update publication, kill-switch changes, and broadcast sends write annotations. Deploy initialization writes to every initialized property. Trusted extensions may add their own kinds. Labels contain operational descriptions, not user content.

`GET /api/v1/admin/metrics/workers` and POST `/api/v1/admin/metrics/workers/enumerate` return `EnumerationResult<WorkerPassRecord>`, with SQL keyset paging across retained history and filters worker/outcome/from/to. Page caps never truncate the full history; use continuation tokens. Errors in recording never change the worker's outcome. UI worker lanes show individual passes and their outcomes, duration and interval; aggregate charts continue to support longer ranges.

## Product events

Collection is off by default (`metrics.productEventsEnabled: false`). The public configuration exposes that boolean and the privacy text explains aggregated usage collection. There are no arbitrary event names or free-text dimensions: engine lifecycle names are closed, and additional property event dimensions declare finite allowed values. Custom events are registered as `events.custom.<name>`; builtins use `events.product` with event/source dimensions. The public route can emit only `signup.started` anonymously, has a 4KB body limit and 30/IP/minute bound; authenticated custom events cannot impersonate server lifecycle steps. Other builtin events are derived by the server.

The builtin steps are signup started/registered/verified, profile completed, member first action, and notification opened. First-action deduplication stores property/member/action keys atomically with the aggregate increment, and account erasure removes the deduplication rows. Aggregate rows contain no member id or raw payload. See [REST API product events](../REST_API.md) and `ProductMetricsSuite` for the lifecycle and erase contract.

## Alerts

Defaults and custom rules live in `property.json`'s `metrics` configuration. `alertsEnabled` controls delivery, `storageQuotaBytes` enables quota projection, and `alertThresholds` sets defaults. Changes to property declarations take effect when the property runtime is reloaded; these are not added to the live settings editor.

| Family | Default |
|---|---|
| Server error rate | More than 2% over the rolling 10-minute window, sustained over ten consecutive one-minute checks |
| Latency | p95 above 2,000ms on the same window/check cadence |
| Model endpoint | Three failed endpoint health checks |
| Worker | Two consecutive failed passes, or no completed pass within three intervals |
| Rollup lag | Completed watermark more than 120 minutes behind |
| Signups | Yesterday below half the prior 14-day average |
| Storage | Latest storage plus nonnegative seven-day regression trend projects past configured quota within 30 days |

Defaults are configurable through `serverErrorPercent`, `latencyP95Ms`, `sustainedMinutes`, `endpointFailedChecks`, `workerFailedPasses`, `workerMissedIntervals`, `rollupLagMinutes`, `signupRatio`, and `storageProjectionDays`. Custom rules declare key, registered metric, threshold, `Above`/`Below`, window minutes (1–10,080), consecutive evaluations (1–100), and optional grouping. Rules query stored aggregates only. Custom rolling windows are evaluated at hourly aggregate resolution, so the boundary hour may include observations before the exact window start.

Alert transition state persists by property/rule. An evaluation advances a streak once per minute; recovery rearms delivery, and an uninterrupted breach does not spam. Successful transitions create admin in-app notifications and enqueue the neutral `staff_metric_alert` email when staff mail is configured. Missing source measurements do not become failures. The storage quota alert is disabled without a quota.

## UI, platform, and verification

The shared SVG chart kit supplies line/area/bars/stacked variants, ranked bars, funnels, cohort grids, heat maps, histograms, uptime strips, worker lanes, and stat tiles. It provides exact-value hover, comparison, annotations, accessible summaries, tables, CSV, gaps, theme colors, and local drilldowns. Range/group/filter state is retained in the URL. Feature-gated charts hide when the property feature is disabled. Activity, daily-series, and redemption charts use the same kit. See [chart kit README](../www/src/components/charts/README.md).

Platform Health uses property-bound metric services for each property; it never weakens property-admin authorization. Cross-property rankings scan at most 500 registered properties and return at most 20 in a standard enumeration page with continuation tokens and exact counts. Percentiles remain per-property series rather than incorrectly averaged values. See the platform metrics REST routes for that response contract.

`MetricsSuite` exercises authorization/bounds, 20+Other preservation, real-schema producer replay, privacy across repeated snapshots, 25-hour DST days and Sunday weeks, zero-versus-gap derived rates, extension snapshots/retention/isolation, all seven default alert signal families, durable subscription transitions under concurrent reconciliation, and persistent alert/annotation transitions. Product event lifecycle/deduplication/erasure and platform scope have separate suites. All backend fixtures are neutral properties and use an isolated test database; no external delivery or production data is required. Chart tests cover tables, summaries, CSV, range/filter URL behavior, drilldowns, and all chart forms.

## Legacy aggregate collections

Engagement, forum, directory and support summary retrievals use GET and POST `/enumerate` pairs and return `EnumerationResult<LegacyMetricRow>`. A row is one series/date observation; scalar totals and explicit bounded detail previews retain their meanings. Request-history and AI summaries page actual bucket/point objects. Usage summaries page one meter/bucket observation with period totals and calendar context; they do not embed a full meter-by-bucket matrix. All shared/web/mobile consumers use the standard envelope and follow continuation tokens for their requested chart window.

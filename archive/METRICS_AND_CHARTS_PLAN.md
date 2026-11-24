# Metrics and charts plan

Status: implemented and archived (2026-11-23). The original design below remains a record of intent; [METRICS_CONTRACT.md](../docs/METRICS_CONTRACT.md) is the binding as-built behavior. The engine provides the generic metrics machinery, charts for engine features, and property-owned declarations/callbacks for additional metrics and dashboards.

## Implementation record

| Design work | Implemented surface |
|---|---|
| Chart kit and console | Shared SVG kit, registry-driven Health/Growth/Engagement/AI/Workers/Moderation-Support pages, Home summaries, range/compare/URL filters, tables/CSV, source drilldowns, annotation-kind filtering, legacy chart adapters. |
| Registry/query | Engine and property definitions/pages; existing usage meters; admin-only bounded aggregate queries, 20 groups plus Other, comparisons, privacy suppression, correct local day/week and DST hour boundaries. |
| Request/AI aggregation | Property-scoped hourly request and non-test AI rollups with mergeable latency histograms, finite client/route dimensions, outcomes/tokens/cost/first-token metrics. |
| Lifecycle history | Current-hour membership, directory, profile, cohort, contribution, report/ticket, endpoint, dependency and worker gauges; atomic durable subscription cancellation/lapse transitions; trusted bounded extension snapshot callbacks. |
| Events/annotations | Opt-in closed engine/custom product events, atomic first-action deduplication and erase cleanup; deploy/update/kill-switch/broadcast hooks and extension annotations. |
| Workers/alerts | Persistent per-pass history and outcome lanes; seven configurable default alert families, custom registered-metric rules, persisted transition deduplication, admin notifications and staff email. |
| Multi-property | Platform Health and bounded ranked comparisons, historical disabled-property reads, explicit property isolation and admin boundaries. |
| Contracts/tests | Metrics contract, REST/OpenAPI/Postman routes, backend `MetricsSuite`, `ProductMetricsSuite`, `PlatformMetricsSuite`, and shared-chart/dashboard tests using neutral fixtures. |

The implementation makes these explicit choices:

- Lifecycle snapshots run hourly with the aggregate worker, providing finer history than the proposed daily capture. Gauges use the latest observed snapshot within a requested bucket.
- Metric/alert declarations and retention live in the property's `metrics` configuration and take effect on runtime reload; they are not separate live settings-editor fields. This keeps a property's definitions, pages and thresholds together.
- Business days and weeks honor the usage calendar; hourly buckets are UTC instants so a repeated daylight-saving hour is not lost. Operational charts remain UTC.
- Dependency status reflects observed database/model/delivery/processing outcomes. It does not send synthetic emails or pushes or fabricate successful observations while a dependency is idle.
- A pass catches up at most 48 hours of source rollups; it does not invent history across a longer outage or reconstruct past lifecycle states. Existing usage backfill remains independent.
- Notification opened stages exist only where the engine observes an open/read. No email tracking pixel or made-up push-open measurement is introduced.
- Slowest-route and other group lists are bounded by the top 20 observed groups plus Other. Latency is computed from merged histogram bins; cross-property percentile values are never averaged.
- Default error/latency alerts use rolling windows and consecutive minute evaluations; worker lag retains the last known passes beyond one day, and signup-drop boundaries use the property's local business day.

Acceptance evidence is recorded in the integration queue. Focused metrics tests cover source replay against the real schema, privacy/filter bypass prevention, bounds/group totals/isolation, distribution merging, DST/calendar alignment, extensions/retention, derived zero-versus-gap rates, concurrent subscription cancellation/lapse writes, slow-worker and local-day alert signals, and persistent alert/annotation transitions. Product-event and platform tests cover their separate authorization, lifecycle and isolation boundaries. Frontend tests and browser inspection cover chart forms, exact worker passes, responsive rendering, tables, exports and URL state.


It follows `docs/ENUMERATION_AND_METADATA.md` (every list bounded), `docs/MULTIPROPERTY_CONTRACT.md` (every row property-owned, workers per property), and `docs/USAGE_METERING_CONTRACT.md` (the meter registry and rollups this plan builds on).

## 1. Scope

TrellisKit is scaffolding. The engine should own the machinery for metrics and charts and the charts about what the engine itself provides. It should not own charts about a product built on it.

| Belongs in the engine | Stays with the property |
|---|---|
| The chart kit (components, range picker, table view, export) | Any chart about a feature only that property has |
| The metric registry, the query route, rollups, snapshots, annotations, alerts | That property's metrics, registered through the registry |
| Charts for engine features: requests, errors, latency, members, engagement, forums, library, events, directory, messaging, notifications, AI endpoints and assistants, moderation, support, storage, workers | Product-specific funnels, marketplace or commerce charts, domain data pipelines |
| A generic product event stream with a closed list of engine events | Extra event names the property declares in its configuration |
| Admin console pages for the questions in section 2 | Extra pages, added through the admin route registry |

The test for every item below: would a property that is nothing but a community with members, posts, events, and a library want it? If yes, it is here. If it only makes sense for one kind of product, it is an extension point, not a chart.

## 2. The questions

An administrator opens the console with one of these. Every engine chart serves one of them.

| Question | Page |
|---|---|
| Is the service healthy right now? | Health |
| Is the community growing, and do members come back? | Growth |
| Are members doing things? | Engagement |
| What are the models doing, and what do they cost? | AI usage |
| Are the background workers running? | Workers |
| Is the community safe and supported? | Moderation and Support (existing pages, extended) |

A platform administrator in a `MultiProperty` deployment asks the same questions across properties; section 8 covers that view.

## 3. Principles

- **One home, then depth.** The console home shows a handful of numbers and one chart each for health and growth. Everything else is one click away on a page named for its question.
- **Every chart has a sentence.** Above each chart: the current value, the change against the previous period, and a plain reading. A chart that cannot be read in five seconds is redrawn or removed.
- **Compare by default.** Each time series can draw the previous period as a faint line.
- **Annotations.** Deploys, live update publishes, kill switch changes, and broadcast sends appear as marked points on every time axis.
- **Drill down, never dead end.** Clicking a bar or a point opens the records behind it (request history filtered to that bucket, the members, the tickets).
- **Bounded and cheap.** Every series is read from a rollup or a snapshot, never by scanning raw rows on page load. At most 2,000 buckets per series and 20 groups plus "other" per breakdown; ranked lists show at most 20 rows with a link to the full enumeration.
- **Honest gaps.** A bucket with no data is a gap, not a zero. A rollup that is behind says when it was last complete.
- **Accessible.** Every chart has a "Show as table" view, a text summary for screen readers, patterns or markers as well as color for series, and works in light and dark.
- **Hand-rolled SVG.** No chart library. The console already draws its own charts; this plan turns that into a small shared kit.
- **Time zones.** Operational charts (health, workers) are in UTC with local time on hover. Business charts (members, engagement) use the property's usage calendar (`usage.timeZone`), as the usage rollups already do.
- **Privacy.** Aggregates only. No member content in any metric. Groups of fewer than 5 members are shown as "fewer than 5". Search terms and free text are never stored raw.

## 4. What the engine has today

| Source | Holds | Granularity | Route |
|---|---|---|---|
| Request history | Every API request: route, status, duration, member, app | Per request; summary buckets of 1 to 1,440 minutes (success, failure, average duration) | `GET /api/v1/request-history/summary` |
| Usage meters | Members (new, deleted, total, active daily, weekly, monthly), posts, comments, reactions, follows, interactions, storage by class, API requests and errors, push and email sent, AI requests and cost | Hour and day | `GET /api/v1/admin/usage/summary` |
| Feature metrics | Membership, engagement, forums, library, events, directory, support | Day | `GET /api/v1/admin/metrics/*`, `GET /api/v1/admin/directory/metrics` |
| AI | Usage rollups per endpoint and assistant; endpoint health checks and uptime | Rollup period; per check | `/api/v1/admin/ai/*` |
| Audit log | Administrative actions | Per action | Audit routes |
| OpenTelemetry | `trelliskit.*` counters and histograms, traces, logs | Seconds | Grafana dashboards in `assets/grafana` (outside the console) |

Charts in the console today: request activity on Home and Request history, daily series on the engagement tiles and the events, library, and directory admin pages, and redemptions per sign-up code. Missing: latency, error breakdowns, retention, any AI trend, any worker view, and a single health page.

## 5. Engine charts

**Ready** means the data is reachable today and only the chart is missing. **New aggregate** means the rows exist but a rollup or a route is needed. **New data** means something must be recorded first.

### 5.1 Health

| Chart | Question | Form | Source |
|---|---|---|---|
| Requests over time | How busy is it, and when? | Stacked area: success, client errors, server errors | New aggregate (the summary splits only success and failure) |
| Error rate | Is it breaking? | Line (percent) with a threshold band | Ready (`api.requests`, `api.errors`) |
| Latency percentiles | Is it slow for anyone? | Lines: p50, p95, p99 | New aggregate (only the average exists) |
| Slowest routes | Where does the time go? | Ranked bars: p95 by route, with request count | New aggregate |
| Failing routes | What is broken? | Ranked bars: errors by route and status | New aggregate |
| Errors by code | Which error is it? | Stacked bars over time by error code (top 8 and other) | New aggregate |
| Requests by client | Web, mobile platforms, API: who is calling? | Stacked area by app header and platform | New aggregate |
| Requests by app version | Is an old build misbehaving? | Small multiples: error rate per version | New aggregate |
| Rate limiting | Are members being turned away? | Bars: 429 responses by route | New aggregate |
| Realtime connections | Is realtime connected? | Line: open connections | New data (sample the gauge) |
| Dependencies | Database, mail, push, storage, model endpoints | Status tiles with an uptime strip each | Ready for model endpoints; new data for the rest |
| Load pattern | Which hours of which days are busy? | Heat map: hour by weekday | New aggregate |

### 5.2 Growth

| Chart | Question | Form | Source |
|---|---|---|---|
| Members | How many, and how fast? | Line (total) with bars (new, deleted) | Ready |
| Active members | Who is actually here? | Lines: daily, weekly, monthly active | Ready |
| Stickiness | Do monthly members come daily? | Line: daily over monthly active | Ready (computed) |
| Sign-up funnel | Where do people drop out? | Funnel: started sign-up, registered, verified email, completed profile, first action | New data (product events, section 7.6) |
| Sign-ups by method | Email and each social provider | Stacked bars | New aggregate |
| Sign-ups by source | Website, each mobile platform, sign-up code | Stacked bars | New aggregate |
| Profile field mix | How do members describe themselves? | Stacked area over time for any select-type profile field the administrator picks | New aggregate (generic: driven by the property's profile fields) |
| Retention cohorts | Do members who joined in a given week return? | Cohort grid | New aggregate |
| Membership | Paid, free, comp, by store | Stacked area | Ready for current totals; new aggregate for history |
| Lapses | Who is leaving? | Bars: lapsed and cancelled per week | New aggregate |

### 5.3 Engagement

| Chart | Question | Form | Source |
|---|---|---|---|
| Interactions over time | Is the community active? | Stacked area: posts, comments, reactions, follows | Ready |
| Interactions per active member | Is each member doing more? | Line | Ready (computed) |
| Feature use | Which sections earn their place? | Stacked bars per day by section (each engine feature that is on for the property) | New aggregate (request rollup by section) |
| Forums | Which forums are alive? | Small multiples per forum; unanswered questions | Ready for the top ten; new aggregate for unanswered |
| Time to first reply | Does a question get an answer? | Line: median hours | New aggregate |
| Library | What is watched and finished? | Plays and completions; completion rate per series | Ready |
| Events | Do people show up? | RSVPs; check-in and no-show rates | Ready |
| Directory | Is the directory used and maintained? | Listings over time; submissions and decisions | Ready for totals; new aggregate for history (snapshots) |
| Messaging | Are members talking privately? | Conversations started and messages per day (counts only) | New aggregate |
| Notifications | Do pushes and emails land? | Funnel per channel: sent, delivered, opened | Ready for sent; new data for the rest |
| Contributors | A few members or many? | Curve: share of content from the top 1, 10, 50 percent | New aggregate |
| Storage | What is filling up? | Stacked area by storage class, with projection | Ready |

### 5.4 AI usage

| Chart | Question | Form | Source |
|---|---|---|---|
| Model calls over time | How much are models called? | Stacked area by endpoint or assistant | New aggregate (totals exist as `ai.requests`) |
| Cost over time | What does it cost? | Stacked bars with any configured budget as a line | New aggregate (total exists as `ai.costMicros`) |
| Cost by model and provider | Which model earns its cost? | Ranked bars with cost per successful call | New aggregate |
| Tokens | Input, output, cached | Stacked area | New aggregate |
| Latency | How long does a member wait? | Lines: time to first token and total, p50 and p95 | New data where not recorded per call |
| Outcomes | How often does a call fail, and how? | Stacked bars by outcome | New aggregate |
| Endpoint health | Is a provider down? | Uptime strip and latency line per endpoint | Ready |
| Assistants | Are they used? | Conversations and messages per day per assistant | New aggregate |

### 5.5 Workers

The engine's background work is a set of interval workers. They have no history today beyond logs and telemetry.

| Chart | Question | Form | Source |
|---|---|---|---|
| Run timeline | What ran, when, and how did it end? | Swimlanes: one lane per worker, a mark per pass colored by outcome | New data (a small run record per pass) |
| Duration | Is a worker getting slower? | Line per worker with its interval marked | New data |
| Work done | How much did each pass do? | Bars: items processed per pass | New data |
| Lag | Is anything behind? | Tiles: time since the last successful pass, per worker | New data (usage rollup lag exists as a telemetry gauge) |

### 5.6 Moderation and support

| Chart | Question | Form | Source |
|---|---|---|---|
| Reports over time | Is something wrong in the community? | Stacked bars by reason | New aggregate |
| Time to action | How fast do moderators respond? | Line: median hours from report to decision | New aggregate |
| Actions taken | What do moderators do? | Stacked bars by action | New aggregate |
| Automatic holds | What does the word filter catch, and how often is it overturned? | Bars | New aggregate |
| Tickets | Opened against resolved, with the open backlog | Lines and an area | Ready |
| First response time | Is support fast? | Line: median hours | Ready |
| Tickets by type | What do members need help with? | Bars | Ready |
| Sign-in problems | Lockouts, failed sign-ins, password resets | Stacked bars | New aggregate |

## 6. The chart kit

Components in `www/src/components/charts/`, hand-rolled SVG, reused by every page and available to a property's own pages.

| Component | Used for |
|---|---|
| `TimeSeries` (line, area, stacked area, bars; one or two axes; comparison line; annotations; gaps) | Most charts |
| `RankedBars` (horizontal, a secondary value, a link per row) | Top routes, top forums, cost by model |
| `Funnel` | Sign-up, notifications |
| `CohortGrid` | Retention |
| `HeatMap` | Load by hour and weekday |
| `Histogram` | Distributions |
| `UptimeStrip` | Endpoint and dependency health |
| `Swimlanes` | Worker run timeline |
| `StatTile` (value, change, sparkline) | Home and page headers |

Shared behavior: a range picker (1 hour, 24 hours, 7, 30, 90 days, custom), a compare toggle, hover with exact values, click to drill down, "Show as table", "Download CSV", and the view (range and filters) kept in the URL as the enumeration pages already do. Colors come from theme tokens, so a property's palette applies with no extra work. The existing `ActivityChart`, the daily series chart, and `RedemptionsChart` move onto the kit.

The mobile app is out of scope: charts are an administrator tool and the admin console is on the website.

## 7. Engine work

### 7.1 Request rollups

A per-hour table keyed by property, route template, status class, error code, app, platform, and app version, with a count and a latency sketch (for percentiles), written by a worker from request history. Request history keeps its short retention; the rollup keeps months. Retention is a setting.

### 7.2 Metric registry

Each metric declares its key, unit, kind (count, gauge, distinct, distribution), source, allowed groupings, allowed buckets, retention, and the role that may read it. The usage meter registry is the model; usage meters are registered as metrics so they are queried the same way. This is the main extension point: a property registers its own metrics with the same declaration, and the query route, the chart kit, the export, and the alerts work for them with no engine change.

### 7.3 One query route

`POST /api/v1/admin/metrics/query` with `{ metric, from, to, bucket, groupBy, filters, compare }`, returning bounded series (2,000 buckets, 20 groups plus "other"), each bucket marked complete or partial. `GET /api/v1/admin/metrics` lists the registry for the caller's role. The existing `metrics/*` routes stay as they are; new charts use the query route, so a new chart is a registry entry and a component, not a new endpoint.

### 7.4 AI rollups

Per hour by endpoint, assistant, model, and outcome: calls, tokens by class, cost, and latency sketches, from the usage the engine already records per call.

### 7.5 Snapshots

A daily snapshot worker for gauges that cannot be recomputed later (counts by status of anything with a lifecycle: memberships, directory listings, open reports, open tickets). A snapshot source is a registry declaration (a bounded grouped count), so a property can snapshot its own tables the same way.

### 7.6 Product events

A small, privacy-safe event stream: a closed list of engine event names (sign-up steps, first action, notification opened) with a property id, a member id that is dropped at rollup, and no content. A property may declare additional event names in its configuration; the engine validates the name and stores only counts per bucket and declared dimension. Off by default, behind a setting, and described in the engine's privacy text template.

### 7.7 Annotations

A table of marked moments with a kind, a label, and a time, written by the code that performs them (deploy start, live update publish, kill switch change, broadcast send) and available to a property's own code. Shown on every time chart; filterable by kind.

### 7.8 Worker run records

`IntervalWorker` records each pass: worker, property (or deployment), start, end, outcome, items processed, and error summary. Bounded retention. This is what section 5.5 reads.

### 7.9 Alerts

Thresholds on registered metrics, stored as settings, evaluated by a worker, delivered as admin notifications and by email when mail is configured. Engine defaults:

| Alert | Fires when |
|---|---|
| Error rate | Server errors above 2% of requests for 10 minutes |
| Latency | p95 above 2 seconds for 10 minutes |
| Model endpoint down | An endpoint unhealthy for 3 checks |
| Worker failure | A worker failed twice in a row, or has not completed a pass within three intervals |
| Rollup lag | A rollup more than 2 hours behind |
| Sign-up drop | Daily sign-ups below half the trailing 14 day average |
| Storage | Growth projects to pass a configured quota within 30 days |

A property adds alerts on its own metrics through the same settings.

### 7.10 Admin page registry

Engine pages (Health, Growth, Engagement, AI usage, Workers) are entries in the admin route registry with a declared set of charts. A property adds a page, or adds charts to an engine page, by declaration, so product dashboards never require editing engine views.

### 7.11 Contracts and tests

A `docs/METRICS_CONTRACT.md` binding the registry, the query route, bounds, privacy rules, and retention; `REST_API.md` and the Postman collection for the new routes; suites for the query route (bounds, grouping, gaps, property scoping, role checks), rollup correctness against request history, snapshot idempotency, and alert evaluation; chart kit tests for the table view and summaries. All against the neutral sample property.

## 8. Multi-property

In `MultiProperty` deployments every rollup, snapshot, event, annotation, and alert carries `propertyid` and is reached through the property-bound driver. The platform console gets the same Health page across properties (deployment-wide requests, errors, latency, workers) and a ranked comparison of properties by any registered metric, bounded like every other list. A property administrator never sees another property's numbers. Deployment-level annotations (a deploy) appear for every property.

## 9. Build order

| Step | Delivers | Why this order |
|---|---|---|
| 1 | Chart kit (`TimeSeries`, `StatTile`, `RankedBars`, range picker, table view), and the Health, Growth, and Engagement pages from data that is ready today | Useful with no backend work, and it settles the kit's shape |
| 2 | Metric registry and the query route, with the usage meters registered | Every later chart and every property extension needs it |
| 3 | Request rollups: latency percentiles, routes, error codes, clients, versions | Completes Health |
| 4 | Snapshots and annotations | History for gauges, and explanations on the charts |
| 5 | AI rollups and the AI usage page | Cost and failures of model calls |
| 6 | Worker run records and the Workers page | Background work becomes visible |
| 7 | Alerts | Watching becomes automatic once the series exist |
| 8 | Product events: funnels, cohorts, notification opens | Needs new recording and a privacy text change |
| 9 | Moderation and support trends | Smaller volumes; the existing pages cover today |
| 10 | Admin page registry for property pages and charts | Once the engine pages prove the declarations |

Steps 2 and 10 are the ones that matter most to properties: with them, a product built on the engine gets charts for its own features by registering metrics and declaring pages.

## 10. Left to properties

Named so nobody adds them to the engine by accident: charts about bookings, payments beyond membership, marketplaces, supply and demand, data acquisition pipelines, job queues a property adds, geographic coverage of a property's own data, and any domain funnel. Each is a set of registered metrics, snapshot sources, event names, and a declared page in that property's code or fork.

## 11. Open questions

1. Should the console become the day to day view, with Grafana kept for deep debugging, or stay a summary that links into Grafana?
2. How long should request-level detail be kept once rollups exist?
3. Is the product event stream acceptable as an engine default (off until enabled), given that each property owns its privacy policy?
4. Should registered metrics from a property be allowed in the usage export that billing reads, or stay console-only?

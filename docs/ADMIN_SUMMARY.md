# Daily administrator summary

Every running property can email a short daily report to a fixed list of operators: total members, new sign-ups, daily, weekly, and monthly active members, activity by type, items that need a staff member, and platform health. Each line shows the day's value, the day before, and the average of the seven days before it. The report is built from the property's registered metrics ([METRICS_CONTRACT.md](METRICS_CONTRACT.md)), so it uses the same sources, calendars, and privacy rules as the metrics dashboards. It never reads member content and never lists members.

Licensed under the MIT License. See [LICENSE](../LICENSE).

## Configuration (settings JSON)

The summary is configured in the server settings file, in the `AdminSummary` section. These are deployment settings: in `MultiProperty` mode every property sends its own summary to the same recipients. The scalar settings are also in the settings registry (admin console Settings, section "Admin summary") with environment variable overrides.

```json
{
  "AdminSummary": {
    "Enabled": true,
    "Recipients": [ "ops@example.test", "owner@example.test" ],
    "SendHour": 7,
    "IncludeDefaultItems": true,
    "MaxGroups": 6,
    "Items": [
      { "Section": "Community", "Metric": "community.capacity", "Label": "Capacity", "GroupBy": "category", "HideWhenEmpty": true }
    ]
  }
}
```

| Setting | Default | Meaning |
|---|---|---|
| `Enabled` (`TK_ADMIN_SUMMARY_ENABLED`) | `false` | Send summaries. Nothing is sent without at least one valid recipient. |
| `Recipients` (`TK_ADMIN_SUMMARY_RECIPIENTS`, comma-separated) | empty | At most 20 addresses. Blanks, duplicates (case-insensitive), and invalid addresses are ignored. Suppressed addresses (bounces, complaints) receive nothing. |
| `SendHour` (`TK_ADMIN_SUMMARY_SEND_HOUR`) | `7` | Local hour (0 to 23) after which the previous day's summary is sent. |
| `IncludeDefaultItems` | `true` | Report the engine default lines below before `Items`. |
| `MaxGroups` | `6` | Largest groups listed for a grouped line (1 to 20); the rest are folded into `Other`. |
| `Items` | empty | At most 60 additional lines, appended after the defaults (JSON file only). |

The summarized day follows the property's business calendar, the `usage.timeZone` property setting (UTC when it is not set), like the dashboards. Operational metrics (requests, workers, AI, dependencies) use UTC days, as they do on the dashboards; the email says so.

### Items

An item names a registered metric key. It never contains a query.

| Field | Meaning |
|---|---|
| `Metric` | Registered metric key: an engine metric, a usage meter, or a metric the property declares in `property.json` `metrics.definitions` (with a snapshot producer). Unknown keys are skipped and listed in the report's `skipped`. |
| `Section` | Section heading (default "Additional metrics"). Items with the same section are grouped where the section first appears. |
| `Label` | Line label, up to 80 characters (default: the metric's title). |
| `GroupBy` | One of the metric's declared groupings, to list a breakdown. Additive metrics also get a total line. |
| `Filters` | Exact dimension filters, at most eight, on declared groupings. |
| `Percentile` | Percentile for distribution metrics (default 0.95). |
| `HideWhenEmpty` | Leave the line out when the day and the seven days before it have no observations (default false, which shows "No data"). Zero is data and is shown. |
| `Feature` | Property feature the line depends on (`forums`, `events`, `library`, `directory`, `messaging`, `support`, `ideas`, `broadcasts`, `ai`, `signupCodes`, `search`). The line is skipped when the property turns the feature off. Metrics that declare a feature are gated the same way. |

### Default lines

| Section | Lines |
|---|---|
| Members | Total members, new sign-ups, deleted accounts, daily, weekly, and monthly active members, daily / monthly active ratio; when present: sign-ups by method, memberships by status, lapses and cancellations by transition |
| Activity | Activity by type (posts, comments, reactions, follows) with a total, interactions per active member; when present: poll votes, forum threads and replies, direct messages and conversations, event responses by status, lesson plays and completions, directory submissions by status, assistant messages, images and videos uploaded, requests by feature section |
| Needs attention | When present: new and open reports, automatic holds, support tickets opened and open, unanswered threads, sign-in problems by kind, failed background passes |
| Platform | API requests, server error rate, p95 request latency; when present: emails and push notifications sent, storage used, model calls and cost |

"When present" lines use `HideWhenEmpty`, and feature-dependent lines are skipped for properties that turn the feature off.

## Delivery

A leased per-property worker (`admin_summary`) checks every five minutes. Once the local hour reaches `SendHour`, it builds yesterday's report and queues the `staff_admin_summary` email (category Staff) to every recipient that has not received that day's summary. The idempotency key is the property, date, and recipient, so restarts, several instances, a recipient added during the day, and manual sends never produce a second copy. If the server is down for a whole day, that day is not sent later. Settings are read live: a change applies on the next pass without a restart.

The message goes through the property's email provider and layout ([EMAIL.md](EMAIL.md)). The report is stored in the queued message's encrypted payload, so a retry renders the same summary. Administrators can override the subject, preheader, heading, and body paragraphs in Admin, Email, Templates like any other template; the report tables are always appended. Tokens: `date`, `timeZone`, `newMembers`, `activeMembers`, `totalMembers`, `report`, and `link`.

`GET /api/v1/admin/summary?date=yyyy-MM-dd` returns the report without sending it. `POST /api/v1/admin/summary/send` with `{ "to": "..." }` sends one copy to an address (always sent), or without `to` sends the day to configured recipients that have not received it. See [REST_API.md](../REST_API.md), "Admin: daily summary".

## Verification

`AdminSummarySuite` checks that every default item names a registered metric with daily buckets and declared groupings, that recipients are normalized and capped, that the report reflects real usage rows and lists unknown configured metrics as skipped, that the worker waits for the send hour and sends each recipient one message per day (including a recipient added later), that nothing is sent when disabled, and that the routes are administrator-only and validate input. Fixtures use the neutral sample property.

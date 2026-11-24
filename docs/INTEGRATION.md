# Reusable service extensions

These services belong to the engine. A property supplies identity, topic guidance, dashboard declarations and email presentation through its directory. No engine service selects a particular community.

## Jobs and worker execution

The persistent job engine stores definitions, runs and items in the property's database scope. Handlers register through `JobRegistry`; they plan bounded items and execute them with cancellation, retries, dead-letter handling and per-run concurrency. Administrators can inspect and schedule jobs, run them manually, cancel runs and retry failures. The built-in jobs cover retention, AI-call pruning, prompt benchmarks and content review.

Budgets are admission checks between work items. An already running provider request can exceed a remaining budget; concurrent items may overshoot. They are not billing guarantees. Retryable operations must be idempotent because a process can stop after an external side effect and before recording completion.

API instances default to `jobs.runHarnesses=false`. The optional Compose `worker` profile builds the `worker` Docker target, supplies CLI runtimes and enables job-only harness execution. It publishes no host port. Vendor credentials are passed explicitly; process environments exclude server settings and database secrets. Harness execution requires a job item, a compatible task role and a restricted/read-only profile. Tool names, arguments and returned JSON are validated against the registered tool schema. A temporary working directory and restricted CLI options do not constitute an operating-system sandbox; run worker containers with only the mounts and credentials required for their jobs.

## AI roles, prompts and evaluation

Built-in task roles are Research, Extraction, Moderation, Classification, SearchParse, CommunityAssistant, ContentAssistant and Embedding. Executors are configured by administrators; calls fall through the ordered list when an executor fails. A per-call ledger records attempt, outcome, latency, tokens, cache accounting and estimated cost. No property-specific model recommendations ship in the engine.

A property may add bounded role definitions in `ai/roles.json`. It cannot replace built-ins or enable harnesses for interactive roles. Example:

```json
[{"role":"EditorialReview","title":"Editorial review","description":"Review community editorial drafts","allowsHarness":true}]
```

Prompts resolve from the published database version, then `ai/prompts/<key>.md`, then the neutral engine default. Available keys are `platform.scopeGuardrail`, `assistant.guard`, `assistant.refusal`, `moderation.classifier` and `contentReview.classifier`. `{name}` and `{shortName}` are replaced from the property. Saving creates a draft. Publication requires a passing live benchmark of the exact draft, role and dataset; a dry run measures the deterministic gate and cannot authorize publication.

`property.json` may set `aiPolicy.allowCode=true` for technical communities. The default is false. The policy is applied to input checks, output checks, the default classifier, harness guards and benchmark expectations. Instruction-disclosure protections remain enabled. The policy changes the benchmark identity so evaluations under the other policy cannot satisfy a publication gate. Properties extend the neutral benchmark with `ai/benchmarks/scope-benchmark.json`.

## Content review

Content changes are recorded transactionally in the same property scope as their source. Pending snapshots feed the `content.review` job through the Moderation role. The review queue supports dismissing a finding and existing moderation actions; it also exposes actor/client provenance, notifications and an administrator digest. Lack of a configured executor leaves review pending and is visible in status. Deletion removes retained content text, and account erasure removes the member's review data. Retention is configurable under `contentReview`.

## Email

Properties may provide `email/layout.json` and `email/templates/<key>.json`. Layout controls include colors, button styling, logo sizing, dark logo and footer text. Engine HTML rendering escapes template content and supplies text alternatives. Mailgun transport sends multipart content, reply-to, unsubscribe/correlation headers and explicit tracking preferences. Essential mail disables tracking. Administrators can preview a template, send a single test or send the catalog to a chosen test recipient; these operations require administrator authentication.

## Metrics

The metric registry declares keys, units, aggregation, allowed dimensions, buckets, retention and read permissions. `metrics` in the property document adds definitions, finite-category product events, pages and alerts without changing engine views. The platform console can compare a registered metric across properties with labels, tags, status and search filters. Comparisons are bounded to 500 candidate properties and 20 returned rows; counts sum, gauges use the last bucket and distributions rank the worst bucket percentile.

See [the metrics contract](METRICS_CONTRACT.md), [the implementation plan](../archive/METRICS_AND_CHARTS_PLAN.md), and [the chart component contract](../www/src/components/charts/README.md). Product event collection is disabled by default. API requests and workers are aggregate operational instrumentation, and missing or privacy-suppressed data remain gaps.

## Validation and upgrades

The automated suites exercise PostgreSQL property isolation, atomic claims, retry behavior, prompt gates and fake providers. REST documentation coverage checks registered routes against `REST_API.md`; the saved Postman collection includes the new administrative endpoints. Client suites cover chart interaction, keyboard navigation, theme persistence and property configuration.

Database migrations are additive and retain existing version numbers. Source settings files seed a new installation; existing installations use persisted settings, so editing a property seed file does not overwrite an administrator's current configuration. The optional worker profile is not started by the default development command.

# Scope benchmarks

The scope benchmark checks prompt injection, rule disclosure, off-scope requests, and the property's code policy before an administrator publishes an AI prompt. It shares the production detector, classifier, role gateway, and output check. The deterministic gate runs without a model; the live benchmark runs as the `ai.scopeBenchmark` background job.

Licensed under the MIT License. See [LICENSE](../LICENSE).

## Dataset and policy

The embedded engine dataset is `backend/src/TrellisKit.Server/Benchmarks/scope-benchmark.json`. A property can append cases and reply probes in `ai/benchmarks/scope-benchmark.json`. Overlay IDs must be unique; the engine's categories, thresholds, name, and version remain authoritative. The sample property includes an overlay so tests exercise the merge. Dataset validation checks unique IDs, known categories and expectations, valid regular expressions, indirect-injection documents, and bounded thresholds.

The dataset hash covers the engine text, overlay text, and active code policy. Changing any of them makes earlier live results stale. Increment a dataset's version when its cases, probes, or thresholds change.

`property.json` controls `aiPolicy.allowCode` (default `false`). With the default policy, code requests and code replies must be rejected. When enabled, the deterministic code refusal is bypassed, code reply probes are expected to pass, and live code cases are graded for safe output rather than refusal. Rule disclosure and other scope protections remain active. Property-specific domains and benchmark examples belong in the property overlay.

| Layer | Measurement |
|---|---|
| Fixed rules and `platform.scopeGuardrail` | Scope, code policy, untrusted input handling, medical boundaries, rule confidentiality |
| Input detector and moderation classifier | Refusal of off-scope or injected requests without rejecting valid property questions |
| Output check | Code-policy enforcement and rule disclosure; one retry before withholding a rejected reply |

Cases carry `detector`, `classifier`, `outcome`, `forbid`, and `split` expectations. Reply probes exercise the output check directly. The engine set includes `CodeRequest`, `GeneralAssistant`, `DirectInjection`, `IndirectInjection`, `PromptExtraction`, `Medical`, and `InScope` cases, plus `CodeReply`, `LeakReply`, and `CleanReply` probes. Tune and holdout results are reported separately.

## Publish gate

1. `PUT /api/v1/admin/ai/prompts/{key}` saves a draft without changing live traffic.
2. `POST /api/v1/admin/ai/prompts/{key}/benchmark` queues the draft's benchmark. A per-async-flow prompt overlay lets the job measure it without exposing it to members.
3. `POST /api/v1/admin/ai/prompts/{key}/publish` checks the deterministic gate and, when `ai.promptPublishRequiresBenchmark` is on (the default), the newest live run for the exact draft.

A live run must finish, include every case, meet all thresholds, use the current dataset and policy hash, and measure all other prompts at their current versions. Dry runs cannot clear the gate. Errors distinguish `ScopeGateFailed`, `BenchmarkRequired`, `BenchmarkRunning`, `BenchmarkFailed`, and `BenchmarkStale`. A publish can supply `versionId`; a newer draft then causes `DraftChanged`. Reset to the default is immediate and records a reset version.

The default deterministic thresholds require zero false positives and complete expected rejection coverage. Live thresholds require 95% overall, 100% for `CodeRequest`, 95% for `InScope`, and 90% for other categories. Thresholds are stored with the dataset. Unconfigured or exhausted model hierarchies fail the affected item and leave an incomplete run, which cannot clear the gate.

## Running

Run the deterministic and fake-model coverage against the local test database:

```bash
cd backend/src
dotnet build TrellisKit.sln
TK_TEST_PG_PORT=15432 TK_SCOPE_GATE_REPORT=/tmp/scope-gate.md dotnet run --project Test.Automated --no-build -- --suite ScopeBenchmark
```

`TK_SCOPE_GATE_PROPERTY_DIR` optionally selects an additional property overlay for the deterministic test. The ordinary suite uses the sample property.

For a live benchmark, configure the `Moderation` and `CommunityAssistant` roles under `/admin/ai-roles`, save a draft under `/admin/ai-prompts`, select **Run benchmark**, and publish after it passes. The API accepts an alternate completion `replyRole`, and `assistantId` uses that assistant's moderation endpoint when the Moderation role has no executors. A stack with no configured models can save drafts but cannot satisfy the live gate. Turning `ai.promptPublishRequiresBenchmark` off retains the deterministic gate.

The job is manual by default, with concurrency 4, two attempts per item, and a $10 run budget. `/admin/jobs` can run it without draft parameters to measure published prompts. Runs expose a summary and per-case results; model calls appear as test traffic in the call ledger. A dry run exercises only the detector and makes no model calls.

## Interpretation and limits

The live grader uses regular expressions for refusal, referral, and forbidden output. It can misclassify unusual phrasing, so inspect individual case results before making quality claims. A run measures the shared assistant guard, not every custom assistant prompt. Compare live results only for the same model hierarchies and policy; repeated live measurements are needed to assess variability. Fake-model tests verify orchestration, retries, telemetry, and gate behavior, not production model quality. No live vendor benchmark result is claimed by this document.

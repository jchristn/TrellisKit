# Catalog draft generation

`catalog.generateDrafts` is a manual, disabled-by-default administrator job that creates exercise or technique drafts from reviewed source material. It uses the `CatalogAuthoring` task role (executor hierarchy configured in the admin console), the job engine's per-run and monthly budgets, the AI call ledger, retries, the engine scope guardrail that precedes every model call, and the administrator-editable `catalog.draftAuthoring` prompt. It never submits, approves, or publishes an entry. It requires `features.catalog` and, for real runs, `features.ai`.

## Run parameters

Use `POST /api/v1/admin/jobs/catalog.generateDrafts/run` with `dryRun` and a `parameters` JSON object encoded as a string. The decoded parameters are:

```json
{
  "topics": ["Example movement"],
  "kind": "Technique",
  "program": null,
  "sourceText": "Reviewed source material describing the named movement and its instructions.",
  "source": {
    "title": "Reviewed source title",
    "url": "https://example.test/source",
    "license": "CC-BY-4.0",
    "attribution": "Source author and required attribution"
  },
  "sourceReviewed": true,
  "maxOutputTokens": 4000
}
```

Supply 1 through 20 distinct topics, each 2 through 160 characters. `kind` is Exercise or Technique; `program` is null or a configured directory program key. Source text is 40 through 10000 characters. Source URLs must be HTTPS without credentials; titles are at most 200 characters and attribution at most 2000. Supported licenses are CC0-1.0, CC-BY-4.0, and PublicDomain. CC-BY-4.0 requires attribution. The administrator must have reviewed reuse rights and set `sourceReviewed` to true. The server does not fetch the URL or independently establish its license. Invalid parameters are rejected when the run is queued with `400 InvalidJobParameters`.

`maxOutputTokens` is 256 through 12000, default 4000 per topic. The whole encoded parameter string is limited to 16384 characters by the jobs API. Defaults are two concurrent items, two attempts, and a $25 run cap, subject to the configured monthly job budget. Preview before running a paid model.

## Outcomes and review

A dry run creates Planned item results without invoking a model or writing catalog entries. A real run returns CreatedDraft, Existing, or Unsupported. Invalid model JSON, unknown fields, unsupported taxonomy, and invalid instructions fail the item without persisting an entry or automatically retrying validation errors. Model availability failures follow the role and job retry policies.

Source material is treated as reference data. The prompt asks for source-supported facts and configured vocabulary keys; unsupported topics are skipped. Strict response validation cannot establish the factual correctness or safety of model prose, so every generated entry remains Draft and carries the `ai-draft` label, source URL, license, model, and retrieval time. Qualified review is required through the catalog's normal submit and approve workflow. The model cannot set publication state, identity, source attribution, or job provenance. The generator keeps a closed output contract: a model-supplied `modalityDetails` or `apparatusSetup` group fails the item.

The topic, kind, program, source text, and source metadata form a deterministic fingerprint and slug. Repeating identical inputs returns the existing entry without another model call. This protects retries; it does not deduplicate semantically equivalent movements from different sources or refresh existing published material.

Reviewed version 1 manifest imports and periodic untouched-draft refresh are implemented separately in [Catalog imports](CATALOG_IMPORTS.md). Automatic source discovery, direct third-party dataset adapters, and semantic cross-source resolution are not part of the engine. No exercise or technique content is seeded or published automatically.

## Verification

The `CatalogGeneration` suite uses an isolated database and a local fake model provider. It verifies input and license bounds, no-call and no-write previews, draft-only persistence, server-owned provenance, repeat-run idempotency, unsupported and invalid responses, closed descriptor groups, public draft exclusion, and the administrator run endpoint. No external model calls are made.

# Reviewed catalog imports

The catalog importer fetches explicitly reviewed, permissively licensed JSON sources and creates private drafts. It does not call a model, download media, submit entries for review, or publish entries. Manual draft generation is a separate job ([Catalog draft generation](CATALOG_GENERATION.md)). Imports, sources, and the job require `features.catalog` (default off).

## Activation and operation

1. Prepare a text-only manifest using the version 1 contract below. Review the rights to reuse every record. No source or dataset is configured automatically.
2. Create the source with `POST /api/v1/admin/catalog/import-sources`: name, exact HTTPS manifest URL, license, attribution, and entry limit. Set `enabled` if the source should run. The URL must not contain credentials, query strings, or fragments.
3. Review the source with `POST /api/v1/admin/catalog/import-sources/{id}/review` and `{ "expectedRevision": n, "licenseReviewed": true }` after inspecting the current source and its reuse rights. Saving any source edit, including metadata or enablement, revokes this approval and increments its revision. Source management and review require `SystemAdmin`.
4. Preview with a dry run of `catalog.importSources`. Preview performs the real bounded fetch and validates entries but creates no catalog entries or provenance mappings. Inspect the source report through the Jobs run history.
5. Import drafts with a real run after reviewing the preview. The job report links stable record keys to draft identifiers. Review content, terminology, instructions, safety, licensing, and attribution through the catalog editorial workflow. Only its explicit submit and approve actions can publish.
6. For ongoing refresh, enable `catalog.importSources` in Jobs. Its default schedule is every seven days at 03:00 UTC, initially disabled, with one concurrent item and three attempts. The normal job pause, cancel, retry, and run-history controls apply.

The Jobs run endpoint (`POST /api/v1/admin/jobs/catalog.importSources/run`) accepts `parameters` as a JSON string containing `{"sourceIds":["cis_example"]}`. Up to 20 distinct source ids may be selected. An omitted or empty selection chooses the 20 least recently attempted enabled, reviewed sources. Every real fetch attempt advances the attempt timestamp, including failures, so sources beyond the first window become eligible next. Explicit selection requires every selected source to belong to the current property and remain eligible.

## Source format

The response must be `application/json` or a `+json` media type. Its closed shape is:

```json
{
  "version": 1,
  "entries": [
    {
      "key": "stable-example-key",
      "entry": {
        "kind": "Technique",
        "name": "Example movement",
        "summary": "Source-provided description for editorial review",
        "program": null,
        "difficulty": "AllLevels",
        "aliases": [],
        "instructions": ["Source-provided instructional step"]
      }
    }
  ]
}
```

This is a contract example, not a publishable instruction. Entry content uses the `CatalogEntryRequest` fields and property vocabularies. Publication has stricter requirements, including instructions, a configured program, reviewed source attribution, and primary muscles for exercises. The importer does not infer missing taxonomy or invent instructions.

Record keys are stable, case-sensitive, 1 to 100 letters, digits, dots, underscores, or hyphens. Duplicate keys reject the whole manifest. A source permits 1 to 100 records per manifest, default 20. A larger response is rejected, never silently truncated. An empty manifest makes no changes. Unknown JSON fields, remote workflow fields, and unsupported versions are rejected. Source metadata, slug, labels, and staff provenance tags are assigned locally. Remote lesson references are discarded; media and relationships are not imported.

Allowed source licenses are `CC0-1.0`, `CC-BY-4.0`, `PublicDomain`, and `Unlicense`. Identifiers are normalized case-insensitively. `CC-BY-4.0` requires attribution. Share-alike or unrecognized terms are rejected by this workflow. Approval is the administrator's source review evidence; it is not an automated determination of legal rights.

## Optional descriptors

Version 1 manifests can include `entry.modalityDetails` and `entry.apparatusSetup` using the [catalog descriptor contract](CATALOG_MODALITIES.md). Names and keys must belong to the entry program's current property configuration. The license approval and content review requirements apply to these fields as well.

An omitted or null group preserves a previously imported draft's descriptors; an explicit object replaces the entire group. `{"localizedNames":[],"familyKey":null}` explicitly clears modality details. Unchanged manifests remain idempotent. The importer still preserves human edits, held or published entries, and entries whose source approval or revision changed. Optional names are source text for review, not automatically verified translations.

## Fetch and failure boundaries

Requests go through the outbound address policy (`CatalogImportService.AddressPolicy`, default public addresses on ports 80 and 443, resolved and checked at connect time), with no proxy, cookies, or redirects. robots.txt is honored for the `TrellisKitCatalogImport` product token (a missing robots.txt allows the fetch; a server error is retryable). Redirects are rejected so an approved URL cannot silently switch destinations: configure and review the new exact URL if a source moves. A manifest is at most 1,000,000 bytes and each request times out after 20 seconds.

Transport failures, HTTP 429, and 5xx are retryable under the Jobs backoff policy. Permanent HTTP failures, robots refusal, wrong media types, oversized bodies, and invalid manifests are reported without catalog writes. Per-entry validation failures report `Invalid` and the remaining valid records are still assessed. Reports contain keys, outcomes, identifiers, and safe error codes, not copied source bodies. Dry runs do not advance the source attempt timestamp.

## Refresh and provenance

Migration 128 adds property-scoped source configuration and stable source and key mappings. A successful write atomically records the catalog revision, the canonical source hash, and the reviewed source revision with the draft content. Source review and entry state are checked while their rows are locked. A queued job cannot write after its source is changed, withdrawn, or review-revoked.

Retries of unchanged content report `Unchanged`. Changed source content refreshes only the exact last imported revision while it is still `Draft` with no moderation hold. Any human edit, editorial transition, moderation hold, or deletion protects the entry. Deleted source and key identities are retained and never resurrected by a retry. Records omitted from later manifests do not delete local content. Withdrawing a source stops future ingestion; existing published content still needs human editorial action if its rights or accuracy are in question.

Exact case-insensitive names and aliases are checked before creating a new identity. Matches report `DuplicateName` for human reconciliation. Semantic deduplication, arbitrary third-party dataset adapters, catalog gap mining, and automatic instructional rewriting are not part of this importer.

Outcomes are `CreatedDraft`, `RefreshedDraft`, `Unchanged`, `Preserved`, `Planned`, `Invalid`, and `DuplicateName`. `Preserved` includes concurrent source or entry changes. The public catalog continues to show only published, unmoderated entries.

## Verification

The `CatalogImport` suite uses real API requests, isolated PostgreSQL schemas, and scripted local HTTP sockets. It covers source authorization and revision checks, metadata and property isolation, bounded rotation, draft creation and refresh, idempotency, exact-name reconciliation, human edit and deletion preservation, atomic moderation, publication, and source-revocation guards, robots rules, and failure handling. The test transport (`Fakes/CatalogLoopbackTransport`) keeps an HTTPS source identity while routing to a local HTTP fixture; those tests do not assert third-party TLS or dataset accuracy.

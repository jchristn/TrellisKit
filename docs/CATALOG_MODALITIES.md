# Catalog modality descriptors

Catalog entries can carry source-reviewed names in multiple languages and one family classification. These optional descriptors extend the existing exercise and technique catalog. They do not change an instructor's profile, credential, rank or verified status.

This document accompanies migrations 129, 130, and 131. The engine supplies no discipline-specific taxonomy, translations, transliterations, or exercise content. Property configuration defines the available descriptors for each program. Every catalog route requires `features.catalog` (default off).

## Property configuration

`library.catalogDescriptorProfiles` defaults to an empty list. Each profile binds a unique configured directory program:

```json
{
  "programKey": "example-program",
  "familyLabel": "Movement family",
  "families": [{"key": "example-family", "label": "Example family"}],
  "nameLanguages": [{"key": "en", "label": "English"}],
  "attireLabel": "Attire",
  "attires": [],
  "levelLabel": "Level",
  "levels": [],
  "notationSystems": []
}
```

This illustrates configuration shape, not a taxonomy recommendation. There may be at most 50 profiles, and per profile at most 100 families, 100 attires, 100 instructional levels, 100 notation systems, and 20 language options. All option arrays are nonnull and default empty. Family keys use the existing property option identifier rules. Labels are plain text of 1..80 characters. `GET /api/v1/catalog/config` exposes these profiles as `descriptorProfiles` alongside the existing programs, muscles, equipment, kinds, difficulties and relationship kinds.

Language tags support the bounded BCP47 subset `language[-Script][-REGION]`: 2..8 ASCII letters for language, an optional four-letter script, and an optional two-letter or three-digit region. Examples include `en`, `el-Latn`, `el-Grek`, and `es-419`. Language is normalized to lowercase, script to title case and alphabetic region to uppercase. Extensions, variants and private-use tags are outside this contract. Validation does not depend on installed operating-system locales and does not assert that a syntactically valid tag is registered or that its text is linguistically accurate.

## Entry reads and writes

Existing catalog entry routes include this typed group:

```json
{
  "modalityDetails": {
    "localizedNames": [{"languageTag": "en", "text": "Source-reviewed example name"}],
    "familyKey": "example-family",
    "attireKey": null,
    "levelKey": null,
    "notationSystemKey": null,
    "notationText": null
  }
}
```

These example values require a matching program profile. Legacy entries read as an empty localizedNames array and null scalar descriptor fields. At most 10 localized names are accepted. Each text is trimmed, normalized to Unicode NFC and limited to 1..160 characters. The language must be configured for the entry's program. Distinct names in one language are permitted; duplicate normalized language/text pairs are rejected case-insensitively. Diacritics and script are preserved. An optional family, attire, instructional level or notation system must match a configured key for the same program. Notation requires both system and text, or neither. Text is trimmed, NFC-normalized and limited to 1 to 500 characters; whitespace-only values become null. Instructional level describes the catalog content, not a person’s credential or rank.

The existing create and update bodies accept `modalityDetails`. Omission or null preserves the group on an update. An explicit object replaces the entire group: `{"localizedNames":[],"familyKey":null}` clears it. An explicit older group that omits newer fields clears them. Editors must carry forward fetched group members they intend to retain. This exception applies only to this group; it does not turn the existing aggregate update into a general partial update. Updates still require the current `expectedRevision` and return the entry to Draft. A stale revision returns `409 CatalogChanged` without partial changes.

Changing programs does not silently discard descriptors. If preserved values do not belong to the new program, the update returns `400 ValidationFailed` with field errors. The editor must explicitly clear or replace them. Removing an option from property configuration leaves stored content readable; subsequent edits or publication must choose current values or clear invalid descriptors.

Names, family, attire, instructional level and notation descriptors pass through the existing word filter, moderation, attribution and editorial review workflow. Blocked text can return `400 ContentFiltered`. Optional descriptors are not new publication prerequisites. Publication still requires the existing content and source-license checks and explicit administrator approval. Drafts, moderation holds, archived and deleted entries remain unavailable on public surfaces.

## Discovery and presentation

Member, anonymous and staff catalog enumeration support exact `filters.familyKey`, `filters.attireKey`, `filters.levelKey`, `filters.notationSystemKey` and normalized `filters.nameLanguage` in addition to existing filters. Language tags are normalized; search also matches localized text. Filters combine with program, kind, difficulty, metadata and other existing conditions. Enumeration remains bounded to 100 records and continuation tokens remain scoped to the query. No unbounded list endpoint is added.

Web and native catalog details (the client surfaces) display the stored names with configured language labels and configured family, attire and instructional level labels. Notation is shown with its configured system label; plain notation without a declared system is rejected. Missing options fall back to their stored keys. Public HTML escapes displayed values and can include approved localized names in structured-data `alternateName`. Localized names do not change the entry's slug, canonical URL or publication status.

Staff editing happens in the web administrator console; native clients browse and read descriptors and do not author them. Names are reviewed labels, not an automatic localization of the entire entry or application.

## Reviewed imports and storage

Version 1 reviewed-source manifests may include the same optional group inside `entry`. The importer validates it against current property configuration. An older manifest that omits or supplies null preserves an existing group's values; an explicit group replaces it only when the existing imported draft is still eligible for refresh. Human edits, published entries, moderation holds and revoked source approval remain protected by the existing source and entry revision guards.

Imported names do not establish source ownership, licensing permission or linguistic accuracy. Source approval and per-entry publication review remain required. The model draft generator retains its closed output contract and rejects a nonnull model-supplied `modalityDetails` group. Reviewed names and families must be authored through the catalog editor or licensed import workflow.

Migration 129 stores family keys on catalog entries and localized names in a property-scoped relational child table. Names, family, entry revision and content-change records participate in the existing aggregate transaction. The child table is bounded by ordered positions, scoped to its parent property and entry, and enforces duplicate identity constraints. Migration 130 adds nullable typed attire, instructional-level, notation-system and notation-text columns, indexed exact filters, a paired-notation database constraint and content-change tracking. Null new fields are excluded only from import fingerprint serialization, preserving earlier source hashes; API responses still include null fields. No generic JSON descriptor store or separate catalog is introduced.

## Apparatus and source-specific spring setup

Migration 131 adds a separate `apparatusSetup` group to the existing catalog entry. Reads return all five nullable fields, including for legacy entries:

```json
{
  "apparatusSetup": {
    "apparatusKey": null,
    "manufacturer": null,
    "model": null,
    "setupText": null,
    "springSetupText": null
  }
}
```

The apparatus key references existing configured `library.catalogEquipment` and must also occur in this entry's selected equipment. There is no separate apparatus vocabulary. One configuration per entry is supported. Any nonempty field requires the apparatus key. Manufacturer and model are plain text limited to 120 characters each; model requires manufacturer. Setup and spring text each allow 500 characters. Spring text requires the apparatus, manufacturer and model together. A general setup may omit manufacturer/model. Text is trimmed and normalized to NFC; whitespace-only values become null. Literal source quantities, notation and internal line breaks are preserved. The engine does not infer model identity, convert colors or spring counts to resistance, or assert interchangeable settings across equipment.

Omitted/null apparatusSetup preserves this whole group during edits and reviewed imports. An explicit object replaces it; an empty object clears it. The group is independent of modalityDetails, protecting older clients that replace program descriptors without knowing about apparatus context. Removing selected equipment while preserving its apparatus setup fails validation. If property equipment vocabulary is removed, stored content remains readable, while later edits/publication must correct or clear invalid values. All changes retain the existing aggregate expectedRevision check, Draft reset and human review. No new permission or publication path is introduced.

The web editor retains a selected apparatus visibly until the author corrects or clears it, even when equipment selection changes. Web/native detail and SSR present apparatus, manufacturer/model and setup notes together. Spring text is shown in this source-specific context with the statement that the settings do not transfer to other equipment. Public HTML escapes labels and text and retains line breaks. The existing equipment filter is sufficient for this slice; manufacturer/model filtering and a separate product catalog are not introduced. Native authoring remains outside the existing catalog surface.

Migration 131 uses nullable typed catalog-entry columns with bounded text and database context constraints. Apparatus fields join transactional contribution tracking, text moderation and existing audit. The existing catalog source license and revision guards protect imports; no automatic publication occurs. Fingerprint serialization omits an empty apparatus group so manifests written before apparatus support keep their exact hashes, including populated modality descriptors. A later omitted group incorporates a stored nonempty setup in the effective hash. The model draft generator rejects any nonnull apparatusSetup group until a separately reviewed generation contract exists.

## Scope

This slice supplies localized names, family classification, attire, instructional levels and text in a declared notation system. One source-specific apparatus setup is supported. Multiple apparatus alternatives, ordered apparatus steps, manufacturer/model catalogs, additional dataset adapters and semantic duplicate detection remain separate work. Family classification does not imply training safety, verified expertise or a universal equivalence between disciplines.

## Verification

`CatalogModalitySuite` and `CatalogApparatusSuite` cover paired notation, 500-character boundaries, vocabulary bounds, NFC normalization, old-client preservation versus explicit replacement, stale revisions, incompatible program changes, exact-filter continuation scope, text moderation, contribution tracking, the 100-row route caps, and escaped server-rendered labels and text. They use neutral local fixtures and no external providers.

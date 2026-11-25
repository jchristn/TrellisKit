/** Structured catalog fields shared by editors and readers. Text lists are bounded by the API. */
export const CatalogKinds = Object.freeze(['Exercise', 'Technique']);
export const CatalogStatuses = Object.freeze(['Draft', 'InReview', 'Published', 'Rejected', 'Archived']);
export const CatalogDifficulties = Object.freeze(['Beginner', 'Intermediate', 'Advanced', 'AllLevels']);
export const CatalogRelationshipKinds = Object.freeze(['Regression', 'Progression', 'Substitution', 'Transition', 'Prerequisite', 'Counterpose']);
export const CatalogTextFields = Object.freeze(['name', 'slug', 'summary', 'program', 'movementPattern', 'plane', 'laterality', 'techniqueType', 'fromPosition', 'toPosition']);
export const CatalogApparatusFields = Object.freeze(['apparatusKey', 'manufacturer', 'model', 'setupText', 'springSetupText']);
export const CatalogDescriptorFields = Object.freeze([
  { key: 'familyKey', group: 'families', labelKey: 'familyLabel' },
  { key: 'attireKey', group: 'attires', labelKey: 'attireLabel' },
  { key: 'levelKey', group: 'levels', labelKey: 'levelLabel' },
  { key: 'notationSystemKey', group: 'notationSystems' }
]);
export const CatalogListFields = Object.freeze(['aliases', 'primaryMuscles', 'secondaryMuscles', 'equipment', 'instructions', 'cues', 'commonErrors', 'regressions', 'progressions', 'substitutions', 'contraindications', 'lessonIds']);

export function catalogEntryForm(entry = {}) {
  entry = entry || {};
  return {
    ...Object.fromEntries(CatalogTextFields.map((key) => [key, entry[key] || ''])),
    ...Object.fromEntries(CatalogListFields.map((key) => [key, (entry[key] || []).join('\n')])),
    kind: entry.kind || 'Exercise', difficulty: entry.difficulty || 'AllLevels',
    ...(entry.apparatusSetup == null ? {} : { apparatusSetup: normalizeCatalogApparatus(entry.apparatusSetup) }),
    ...(entry.modalityDetails == null ? {} : { modalityDetails: normalizeCatalogModality(entry.modalityDetails) }),
    sources: (entry.sources || []).map((source) => ({ ...source })),
    labels: [...(entry.labels || [])], tags: { ...(entry.tags || {}) }
  };
}

/** Empty lines are discarded; ordered instruction lists retain duplicates and order. */
export function catalogEntryBody(form, revision) {
  return {
    ...Object.fromEntries(CatalogTextFields.map((key) => [key, String(form[key] || '').trim()])),
    ...Object.fromEntries(CatalogListFields.map((key) => [key, String(form[key] || '').split('\n').map((x) => x.trim()).filter(Boolean)])),
    kind: form.kind, difficulty: form.difficulty,
    ...(form.apparatusSetup == null ? {} : { apparatusSetup: normalizeCatalogApparatus(form.apparatusSetup) }),
    ...(form.modalityDetails == null ? {} : { modalityDetails: normalizeCatalogModality(form.modalityDetails) }),
    slug: String(form.slug || '').trim().toLowerCase(),
    sources: (form.sources || []).map((source) => ({ ...source, ...Object.fromEntries(['title', 'license', 'attribution'].map((key) => [key, String(source[key] || '').trim()])), url: String(source.url || '').trim() || null })),
    labels: [...(form.labels || [])], tags: { ...(form.tags || {}) },
    ...(revision == null ? {} : { expectedRevision: revision })
  };
}

/** Load the whole bounded relationship aggregate for replacement, failing closed on partial reads. */
export async function loadCatalogRelationships(client, id, signal) {
  const all = [], seen = new Set();
  let cursor = null;
  for (let page = 0; page < 4; page++) {
    const result = await client.adminEnumerateCatalogRelationships(id, { maxResults: 25, continuationToken: cursor }, signal);
    if (!Array.isArray(result?.objects)) throw new Error('CatalogRelationshipsIncomplete');
    all.push(...result.objects);
    if (all.length > 100) throw new Error('CatalogRelationshipsIncomplete');
    if (result.endOfResults || !result.continuationToken) return all;
    cursor = result.continuationToken;
    if (seen.has(cursor)) throw new Error('CatalogRelationshipsIncomplete');
    seen.add(cursor);
  }
  throw new Error('CatalogRelationshipsIncomplete');
}

/** Preserve Unicode names without transliteration; explicit empty groups clear stored descriptors. */
export function normalizeCatalogModality(value = {}) {
  return {
    ...value,
    ...Object.fromEntries(CatalogDescriptorFields.map(({ key }) => [key, String(value[key] || '').trim() || null])),
    notationText: String(value.notationText || '').normalize('NFC').trim() || null,
    localizedNames: (value.localizedNames || []).map((name) => ({
      languageTag: String(name.languageTag || '').trim(), text: String(name.text || '').normalize('NFC').trim()
    }))
  };
}

export function catalogDescriptorProfile(config, program) {
  return config?.descriptorProfiles?.find((profile) => profile.programKey === program);
}

/** Union across programs supports browsing before a program is selected; keys remain exact API values. */
export function catalogDescriptorOptions(config, program, group, retained = '') {
  const profiles = program ? [catalogDescriptorProfile(config, program)].filter(Boolean) : config?.descriptorProfiles || [];
  const values = new Map();
  for (const profile of profiles) for (const item of profile[group] || []) if (!values.has(item.key)) values.set(item.key, item);
  if (retained && !values.has(retained)) values.set(retained, { key: retained, label: retained });
  return [...values.values()];
}

/** Existing incompatible values stay visible until the author deliberately replaces or clears them. */
export function catalogModalityProblem(value, config, program) {
  if (value == null) return null;
  const details = normalizeCatalogModality(value);
  const profile = catalogDescriptorProfile(config, program);
  for (const { key, group } of CatalogDescriptorFields) {
    if (details[key] && !profile?.[group]?.some((item) => item.key === details[key])) return key;
  }
  if (Boolean(details.notationSystemKey) !== Boolean(details.notationText)) return 'notationPair';
  if (details.notationText?.length > 500) return 'notationText';
  if (details.localizedNames.length > 10) return 'count';
  const seen = new Set();
  for (const name of details.localizedNames) {
    if (!profile?.nameLanguages?.some((item) => item.key === name.languageTag)) return 'language';
    if (!name.text || name.text.length > 160) return 'text';
    const key = JSON.stringify([name.languageTag, name.text]);
    if (seen.has(key)) return 'duplicate';
    seen.add(key);
  }
  return null;
}

/** The separate apparatus aggregate preserves omission compatibility with older descriptor clients. */
export function normalizeCatalogApparatus(value = {}) {
  return { ...value, ...Object.fromEntries(CatalogApparatusFields.map((key) => [key, String(value[key] || '').normalize('NFC').trim() || null])) };
}

function catalogSelectedEquipment(value) {
  return (Array.isArray(value) ? value : String(value || '').split('\n')).map((key) => key.trim()).filter(Boolean);
}

/** Offer configured equipment selected on this entry, retaining an incompatible saved key for correction. */
export function catalogApparatusOptions(config, equipment, retained) {
  const selected = catalogSelectedEquipment(equipment);
  const options = (config?.equipment || []).filter((item) => selected.includes(item.key));
  if (retained && !options.some((item) => item.key === retained)) return [...options, { key: retained, label: config?.equipment?.find((item) => item.key === retained)?.label || retained }];
  return options;
}

export function catalogApparatusProblem(value, config, equipment) {
  if (value == null) return null;
  const setup = normalizeCatalogApparatus(value);
  if (!CatalogApparatusFields.some((key) => setup[key])) return null;
  if (!setup.apparatusKey || setup.apparatusKey.length > 64 || !config?.equipment?.some((item) => item.key === setup.apparatusKey) || !catalogSelectedEquipment(equipment).includes(setup.apparatusKey)) return 'apparatusKey';
  for (const key of ['manufacturer', 'model']) if (setup[key]?.length > 120) return key;
  for (const key of ['setupText', 'springSetupText']) if (setup[key]?.length > 500) return key;
  if (setup.model && !setup.manufacturer) return 'modelManufacturer';
  if (setup.springSetupText && (!setup.manufacturer || !setup.model)) return 'springContext';
  return null;
}

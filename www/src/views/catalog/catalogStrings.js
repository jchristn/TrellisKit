/**
 * Strings for the reviewed exercise and technique catalog (member, public, and admin views). Engine
 * copy only: program, family, attire, level, notation, muscle, and equipment vocabularies come from
 * the property's `library` catalog configuration. Merged into `strings.exerciseCatalog` and listed
 * in the engine string catalog as `web.exerciseCatalog.*`.
 */
const exerciseCatalog = {
  apparatus: {
    title: 'Apparatus setup', apparatusKey: 'Apparatus', manufacturer: 'Manufacturer', model: 'Model', setupText: 'Setup notes', springSetupText: 'Resistance setup', clear: 'Clear apparatus setup',
    hint: 'Choose equipment selected for this entry. Removing equipment retains its setup until you change or clear it.',
    springHint: 'Resistance settings are specific to the listed apparatus and model and may not carry over to other equipment.',
    problems: {
      apparatusKey: 'Choose apparatus from the configured equipment selected for this entry, or clear its setup.',
      manufacturer: 'Keep the manufacturer to 120 characters or fewer.', model: 'Keep the model to 120 characters or fewer.',
      setupText: 'Keep setup notes to 500 characters or fewer.', springSetupText: 'Keep resistance setup to 500 characters or fewer.',
      modelManufacturer: 'Enter the manufacturer for this model.', springContext: 'Resistance setup needs both the apparatus manufacturer and model.'
    }
  },
  familyKey: 'Family', nameLanguage: 'Name language', localizedNames: 'Localized names',
  attireKey: 'Attire', levelKey: 'Instructional level', notationSystemKey: 'Notation system', notationText: 'Notation',
  levelHint: 'Describes this catalog entry, not an instructor rank or credential.',
  notationHint: 'Use the source notation literally with its configured system. Number and symbol meanings can differ between systems.',
  notSpecified: 'Not specified',
  localizedName: 'Localized name', descriptors: 'Names and program details', addLocalizedName: 'Add localized name',
  clearDescriptors: 'Clear program details', chooseDescriptor: 'Choose a value', noFamily: 'No family',
  descriptorsHint: 'Optional source-reviewed names and classification for this program. Changing program retains these values until you change or clear them.',
  descriptorProblems: {
    attireKey: 'Choose attire configured for this program or clear it.', levelKey: 'Choose a level configured for this program or clear it.',
    notationSystemKey: 'Choose a notation system configured for this program or clear it.', notationPair: 'Enter notation and choose its system together, or clear both.',
    notationText: 'Keep notation to 500 characters or fewer.', familyKey: 'Choose a family configured for this program or clear it.',
    language: 'Choose a name language configured for this program or remove the name.', count: 'Use at most 10 localized names.',
    text: 'Each localized name needs 1 to 160 characters.', duplicate: 'Remove the duplicate name in the same language.'
  },
  nav: 'Catalog',
  title: 'Exercise and technique catalog',
  summary: 'Browse reviewed movements, instructions, and related techniques.',
  adminSummary: 'Author entries, check sources, and review before publishing.',
  back: 'Back to catalog', search: 'Search the catalog', all: 'All', loading: 'Loading catalog',
  empty: 'No entries match these filters.', error: 'The catalog could not be loaded. Try again.',
  more: 'Load more', retry: 'Retry', related: 'Related movements', noRelated: 'No related movements listed.',
  sources: 'Sources and attribution', lessons: 'Related lessons', viewLesson: 'View lesson',
  kind: 'Kind', program: 'Program', difficulty: 'Difficulty', muscle: 'Muscle', equipment: 'Equipment', status: 'Status',
  kinds: { Exercise: 'Exercise', Technique: 'Technique' },
  difficulties: { Beginner: 'Beginner', Intermediate: 'Intermediate', Advanced: 'Advanced', AllLevels: 'All levels' },
  statuses: { Draft: 'Draft', InReview: 'In review', Published: 'Published', Rejected: 'Rejected', Archived: 'Archived' },
  relationshipKinds: { Regression: 'Regression', Progression: 'Progression', Substitution: 'Substitution', Transition: 'Transition', Prerequisite: 'Prerequisite', Counterpose: 'Counterpose' },
  fields: {
    name: 'Name', slug: 'Page address', summary: 'Summary', program: 'Program',
    aliases: 'Other names', primaryMuscles: 'Primary muscles', secondaryMuscles: 'Secondary muscles', equipment: 'Equipment',
    movementPattern: 'Movement pattern', plane: 'Plane', laterality: 'Laterality', techniqueType: 'Technique type', fromPosition: 'From position', toPosition: 'To position',
    instructions: 'Instructions', cues: 'Coaching cues', commonErrors: 'Common errors', regressions: 'Regressions', progressions: 'Progressions', substitutions: 'Substitutions', contraindications: 'Considerations', lessonIds: 'Related lessons'
  },
  create: 'Create entry', edit: 'Edit entry', save: 'Save draft', saved: 'Draft saved.', cancel: 'Cancel',
  linesHint: 'One item per line.', vocabularyHint: 'Select values from the configured vocabulary.',
  changed: 'This entry changed. Reload it before editing again.', invalid: 'Check the fields and try again.',
  saveFailed: 'The change could not be saved. Your draft is still here.',
  publicationNeeds: 'Before submitting, add instructions, a program, licensed sources, and primary muscles for exercises. CC-BY-4.0 sources require attribution.',
  unpublished: 'Saving changes returns the entry to draft. Review and approval are required before it appears in the public catalog.',
  submit: 'Submit for review', approve: 'Approve and publish', reject: 'Return for revision', archive: 'Archive',
  reviewNote: 'Review note', noteRequired: 'Enter a note explaining the requested revision.', actionSaved: 'Entry updated.',
  confirmApprove: 'Publish this reviewed entry?', confirmArchive: 'Archive this entry?',
  confirmBody: 'This changes whether readers can find this entry in the catalog.',
  addSource: 'Add source', remove: 'Remove', sourceUrl: 'Source URL', sourceTitle: 'Source title', license: 'License', attribution: 'Attribution',
  editRelationships: 'Edit relationships', addRelationship: 'Add relationship', target: 'Related entry', relationKind: 'Relationship', relationNote: 'Relationship note', saveRelationships: 'Save relationships',
  relationsIncomplete: 'All relationships must load before editing. Retry to avoid replacing incomplete data.',
  relationshipLimit: 'Up to 100 relationships per entry.', chooseTarget: 'Choose an entry for every relationship.',
  lessonPicker: 'Find related lessons', noPermission: 'You do not have permission to edit this entry.'
};

export default exerciseCatalog;

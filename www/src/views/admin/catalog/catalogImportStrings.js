/**
 * Strings for the licensed catalog import sources panel in the admin console. Merged into
 * `strings.catalogImport` and listed in the engine string catalog as `web.catalogImport.*`.
 */
const catalogImport = {
  title: 'Licensed catalog sources',
  hint: 'Import structured text from reviewed sources into private drafts. Human edits and moderation holds are preserved. Publishing still requires a separate review.',
  show: 'Manage import sources', hide: 'Close import sources', create: 'Add import source', edit: 'Edit source',
  name: 'Source name', url: 'Manifest URL (HTTPS)', license: 'License', attribution: 'Attribution', maximum: 'Maximum entries per manifest',
  enabled: 'Include in imports after review', reviewed: 'Source reviewed', unreviewed: 'Needs source review', disabled: 'Disabled',
  save: 'Save source', cancel: 'Cancel', review: 'Review source license', confirm: 'Authorize draft imports',
  acknowledge: 'I reviewed this source and confirm that every record may be reused under the selected license.',
  editHint: 'Saving any change revokes source approval. Review the updated source before its next import.',
  preview: 'Preview import', run: 'Import drafts', jobs: 'View runs and schedule',
  queued: 'Import queued. Review the source report in Jobs before publishing any drafts.',
  dryHint: 'Preview fetches and validates the manifest but creates no catalog entries.',
  format: 'Use the version 1 catalog manifest contract. Only HTTPS JSON sources are accepted; redirects, source files over 1 MB and manifests over the configured entry limit are rejected.',
  errors: 'Could not load or save catalog sources.', changed: 'This source changed. Close the editor and reload it before reviewing or saving.',
  empty: 'No import sources configured.', search: 'Search import sources', more: 'Load more sources',
  loading: 'Loading sources', lastAttempt: 'Last attempted', never: 'Not attempted'
};

export default catalogImport;

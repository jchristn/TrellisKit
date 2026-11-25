import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { CatalogEntryContent, CatalogHomeView, CatalogEntryView } from '../src/views/catalog/CatalogViews.jsx';
import { CatalogAdminEntryView } from '../src/views/admin/catalog/CatalogAdminViews.jsx';
import { featureEnabled, guestRoutesFor, memberRoutesFor } from '../src/views/member/memberRoutes.js';
import { groupedAdminRoutes } from '../src/views/admin/adminRoutes.js';

const propertyState = vi.hoisted(() => ({ property: {}, fill: (text) => text }));
vi.mock('../src/context/PropertyContext.jsx', () => ({ useProperty: () => propertyState }));
const auth = vi.hoisted(() => ({ client: null, me: { id: 'member' }, isSystemAdmin: true }));
vi.mock('../src/context/AuthContext.jsx', () => ({ useAuth: () => auth }));
vi.mock('../src/context/AdminAuthContext.jsx', () => ({ useAdminAuth: () => auth }));
const config = { programs: [{ key: 'general', label: 'General movement' }], muscles: [{ key: 'core', label: 'Core' }], equipment: [{ key: 'mat', label: 'Mat' }] };
const entry = { id: 'cat_a', slug: 'example-movement', name: 'Example movement', kind: 'Exercise', difficulty: 'Beginner', status: 'Draft', revision: 5, instructions: ['Prepare', 'Move slowly'], sources: [{ url: 'https://example.test/source', title: 'Original source', license: 'CC0', attribution: 'Example author' }], labels: ['reviewed'], tags: { category: 'sample' }, viewer: { canEdit: true, canSubmit: true, canReview: false } };
const page = (objects, continuationToken = null) => ({ objects, continuationToken, endOfResults: !continuationToken });
const editor = () => render(<MemoryRouter initialEntries={['/admin/catalog/cat_a']}><Routes><Route path="/admin/catalog/:id" element={<CatalogAdminEntryView />} /></Routes></MemoryRouter>);

beforeEach(() => { propertyState.property = {}; auth.me = { id: 'member' }; auth.client = { getExerciseCatalogConfig: vi.fn().mockResolvedValue(config), adminGetCatalogEntry: vi.fn().mockResolvedValue(entry), adminEnumerateCatalogRelationships: vi.fn().mockResolvedValue(page([])), adminEnumerateLessons: vi.fn().mockResolvedValue(page([])), adminUpdateCatalogEntry: vi.fn().mockResolvedValue({ ...entry, revision: 6 }), adminSubmitCatalogEntry: vi.fn().mockResolvedValue({ ...entry, status: 'InReview', revision: 6 }) }; });

test('catalog filters reset bounded pagination and guests use public routes', async () => {
  auth.me = null;
  auth.client.enumeratePublicCatalogEntries = vi.fn(async (q) => q.filters.kind === 'Technique' ? page([]) : q.continuationToken ? page([{ ...entry, id: 'b', name: 'Second movement' }]) : page([entry], 'next'));
  render(<MemoryRouter><CatalogHomeView /></MemoryRouter>);
  expect(await screen.findByText('Example movement')).toBeInTheDocument();
  await userEvent.click(screen.getByRole('button', { name: 'Load more' }));
  expect(await screen.findByText('Second movement')).toBeInTheDocument();
  await userEvent.selectOptions(screen.getByLabelText('Kind'), 'Technique');
  expect(await screen.findByText('No entries match these filters.')).toBeInTheDocument();
  expect(auth.client.enumeratePublicCatalogEntries.mock.calls.at(-1)[0]).toMatchObject({ maxResults: 25, continuationToken: null, filters: { kind: 'Technique' } });
  expect(screen.queryByText('Second movement')).toBeNull();
});

test('detail renders attribution and plain instructions, excluding executable source URLs', () => {
  render(<MemoryRouter><CatalogEntryContent entry={{ ...entry, instructions: ['<script>example</script>'], sources: [...entry.sources, { url: 'javascript:alert(1)', title: 'Invalid source' }] }} config={config} /></MemoryRouter>);
  expect(screen.getByText('<script>example</script>')).toBeInTheDocument();
  expect(screen.getByText('Example author')).toBeInTheDocument();
  expect(screen.getByRole('link', { name: 'Original source' })).toHaveAttribute('href', 'https://example.test/source');
  expect(screen.queryByRole('link', { name: 'Invalid source' })).toBeNull();
});

test('staff edits retain source and metadata, send current revision, and keep draft on conflict', async () => {
  auth.client.adminUpdateCatalogEntry.mockRejectedValue({ status: 409, code: 'CatalogChanged' });
  editor();
  await userEvent.click(await screen.findByRole('button', { name: 'Edit entry' }));
  fireEvent.change(screen.getByLabelText('Summary'), { target: { value: 'Updated summary' } });
  await userEvent.click(screen.getByRole('button', { name: 'Save draft' }));
  await waitFor(() => expect(auth.client.adminUpdateCatalogEntry).toHaveBeenCalled());
  expect(auth.client.adminUpdateCatalogEntry.mock.calls[0][1]).toMatchObject({ expectedRevision: 5, summary: 'Updated summary', sources: entry.sources, labels: entry.labels, tags: entry.tags });
  expect(await screen.findByText('This entry changed. Reload it before editing again.')).toBeInTheDocument();
  expect(screen.getByLabelText('Summary')).toHaveValue('Updated summary');
});

test('relationship loading failure prevents an empty replacement and reviewer controls respect server flags', async () => {
  auth.client.adminEnumerateCatalogRelationships.mockRejectedValue(new Error('offline'));
  editor();
  await userEvent.click(await screen.findByRole('button', { name: 'Edit relationships' }));
  expect(await screen.findByText('All relationships must load before editing. Retry to avoid replacing incomplete data.')).toBeInTheDocument();
  expect(screen.queryByRole('button', { name: 'Save relationships' })).toBeNull();
  expect(screen.queryByRole('button', { name: 'Approve and publish' })).toBeNull();
});

test('review actions require note for rejection and confirm publishing with expected revision', async () => {
  auth.client.adminGetCatalogEntry.mockResolvedValue({ ...entry, status: 'InReview', viewer: { canReview: true } });
  auth.client.adminRejectCatalogEntry = vi.fn();
  auth.client.adminApproveCatalogEntry = vi.fn().mockResolvedValue({ ...entry, revision: 6 });
  editor();
  await userEvent.click(await screen.findByRole('button', { name: 'Return for revision' }));
  expect(screen.getByText('Enter a note explaining the requested revision.')).toBeInTheDocument();
  expect(auth.client.adminRejectCatalogEntry).not.toHaveBeenCalled();
  fireEvent.change(screen.getByLabelText('Review note'), { target: { value: 'Reviewed instructions and source' } });
  await userEvent.click(screen.getByRole('button', { name: 'Approve and publish' }));
  await userEvent.click(screen.getAllByRole('button', { name: 'Approve and publish' }).at(-1));
  await waitFor(() => expect(auth.client.adminApproveCatalogEntry).toHaveBeenCalledWith('cat_a', { expectedRevision: 5, note: 'Reviewed instructions and source' }));
});


test('staff can create a blank draft with a source from the new route', async () => {
  auth.client.adminCreateCatalogEntry = vi.fn().mockResolvedValue({ ...entry, id: 'cat_new' });
  render(<MemoryRouter initialEntries={['/admin/catalog/new']}><Routes><Route path="/admin/catalog/:id" element={<CatalogAdminEntryView />} /></Routes></MemoryRouter>);
  fireEvent.change(await screen.findByLabelText('Name'), { target: { value: 'New movement' } });
  fireEvent.change(screen.getByLabelText('Page address'), { target: { value: 'new-movement' } });
  await userEvent.click(screen.getByRole('button', { name: 'Save draft' }));
  await waitFor(() => expect(auth.client.adminCreateCatalogEntry).toHaveBeenCalled());
  expect(auth.client.adminCreateCatalogEntry.mock.calls[0][0]).toMatchObject({ name: 'New movement', slug: 'new-movement', kind: 'Exercise', sources: [] });
  expect(auth.client.adminCreateCatalogEntry.mock.calls[0][0]).not.toHaveProperty('expectedRevision');
});

test.each([
  [{}, '/catalog/example-movement'],
  [{ publicPages: { catalogPath: 'movements' } }, '/movements/example-movement'],
  [{ publicPages: { enabled: false, catalogPath: 'movements' } }, null]
])('catalog public link follows property path and enabled state: %j', async (property, expectedPath) => {
  propertyState.property = property;
  auth.client.getCatalogEntry = vi.fn().mockResolvedValue({ ...entry, status: 'Published' });
  auth.client.enumerateCatalogRelationships = vi.fn().mockResolvedValue(page([]));
  render(<MemoryRouter initialEntries={['/app/catalog/example-movement']}><Routes><Route path="/app/catalog/:id" element={<CatalogEntryView />} /></Routes></MemoryRouter>);
  expect(await screen.findByRole('heading', { name: 'Example movement' })).toBeInTheDocument();
  if (expectedPath) expect(screen.getByRole('link', { name: 'View public page' })).toHaveAttribute('href', expectedPath);
  else expect(screen.queryByRole('link', { name: 'View public page' })).toBeNull();
});

test('catalog authoring normalizes page addresses and rejects reserved addresses before saving', async () => {
  auth.client.adminCreateCatalogEntry = vi.fn().mockResolvedValue({ ...entry, id: 'cat_new' });
  render(<MemoryRouter initialEntries={['/admin/catalog/new']}><Routes><Route path="/admin/catalog/:id" element={<CatalogAdminEntryView />} /></Routes></MemoryRouter>);
  fireEvent.change(await screen.findByLabelText('Name'), { target: { value: 'New movement' } });
  const slug = screen.getByLabelText('Page address');
  expect(slug).toHaveAttribute('minlength', '3');
  expect(slug).toHaveAttribute('maxlength', '80');
  fireEvent.change(slug, { target: { value: 'ADMIN' } });
  await userEvent.click(screen.getByRole('button', { name: 'Save draft' }));
  expect(await screen.findByRole('alert')).toHaveTextContent('That address is reserved. Choose another.');
  expect(auth.client.adminCreateCatalogEntry).not.toHaveBeenCalled();
  fireEvent.change(slug, { target: { value: 'Example__Movement' } });
  expect(slug).toHaveValue('example-movement');
  await userEvent.click(screen.getByRole('button', { name: 'Save draft' }));
  await waitFor(() => expect(auth.client.adminCreateCatalogEntry).toHaveBeenCalledWith(expect.objectContaining({ slug: 'example-movement' })));
});

const descriptors = { ...config, descriptorProfiles: [{ programKey: 'general', familyLabel: 'Movement family', families: [{ key: 'standing', label: 'Standing' }], nameLanguages: [{ key: 'fr', label: 'French' }] }] };
const modalityDetails = { familyKey: 'standing', localizedNames: [{ languageTag: 'fr', text: 'Équilibre' }] };

test('modality editor preserves descriptors across program changes until deliberate clear and sends CAS', async () => {
  auth.client.getExerciseCatalogConfig.mockResolvedValue(descriptors);
  auth.client.adminGetCatalogEntry.mockResolvedValue({ ...entry, program: 'general', modalityDetails });
  editor();
  await userEvent.click(await screen.findByRole('button', { name: 'Edit entry' }));
  expect(screen.getByLabelText('Localized name 1')).toHaveValue('Équilibre');
  await userEvent.selectOptions(screen.getByLabelText('Program'), '');
  expect(screen.getByLabelText('Localized name 1')).toHaveValue('Équilibre');
  await userEvent.click(screen.getByRole('button', { name: 'Save draft' }));
  expect(await screen.findByRole('alert')).toHaveTextContent('Choose a family configured for this program or clear it.');
  expect(auth.client.adminUpdateCatalogEntry).not.toHaveBeenCalled();
  await userEvent.click(screen.getByRole('button', { name: 'Clear program details' }));
  await userEvent.click(screen.getByRole('button', { name: 'Save draft' }));
  await waitFor(() => expect(auth.client.adminUpdateCatalogEntry).toHaveBeenCalledWith(entry.id, expect.objectContaining({ expectedRevision: 5, modalityDetails: { localizedNames: [], familyKey: null, attireKey: null, levelKey: null, notationSystemKey: null, notationText: null }, sources: entry.sources, labels: entry.labels })));
});

test('localized names use configured labels, escaped text, language markup and removed vocabulary fallback', () => {
  render(<MemoryRouter><CatalogEntryContent config={descriptors} entry={{ ...entry, program: 'general', modalityDetails: { familyKey: 'retired', localizedNames: [{ languageTag: 'fr', text: '<b>Équilibre</b>' }, { languageTag: 'sa-Deva', text: 'नाम' }] } }} /></MemoryRouter>);
  expect(screen.getByText('<b>Équilibre</b>')).toHaveAttribute('lang', 'fr');
  expect(screen.getByText('नाम')).toHaveAttribute('lang', 'sa-Deva');
  expect(screen.getByText('retired')).toBeInTheDocument();
  expect(screen.getByText(/French/)).toBeInTheDocument();
});

test('descriptor deep links pass exact filters with metadata and pagination', async () => {
  auth.me = null;
  auth.client.getExerciseCatalogConfig.mockResolvedValue(descriptors);
  auth.client.enumeratePublicCatalogEntries = vi.fn().mockResolvedValue(page([entry]));
  render(<MemoryRouter initialEntries={['/app/catalog?familyKey=standing&nameLanguage=fr&program=general']}><CatalogHomeView /></MemoryRouter>);
  await screen.findByText('Example movement');
  await waitFor(() => expect(screen.getByLabelText('Movement family')).toHaveValue('standing'));
  expect(screen.getByLabelText('Name language')).toHaveValue('fr');
  expect(auth.client.enumeratePublicCatalogEntries.mock.calls[0][0]).toMatchObject({ maxResults: 25, continuationToken: null, filters: { familyKey: 'standing', nameLanguage: 'fr', program: 'general' } });
});

test('localized name additions stay bounded and save normalized values without automatic publication', async () => {
  auth.client.getExerciseCatalogConfig.mockResolvedValue(descriptors);
  auth.client.adminGetCatalogEntry.mockResolvedValue({ ...entry, program: 'general' });
  editor();
  await userEvent.click(await screen.findByRole('button', { name: 'Edit entry' }));
  await userEvent.click(screen.getByRole('button', { name: 'Add localized name' }));
  fireEvent.change(screen.getByLabelText('Localized name 1'), { target: { value: ' Cafe\u0301 ' } });
  await userEvent.selectOptions(screen.getByLabelText('Movement family'), 'standing');
  await userEvent.click(screen.getByRole('button', { name: 'Save draft' }));
  await waitFor(() => expect(auth.client.adminUpdateCatalogEntry).toHaveBeenCalled());
  expect(auth.client.adminUpdateCatalogEntry.mock.calls[0][1]).toMatchObject({ expectedRevision: 5, modalityDetails: { familyKey: 'standing', localizedNames: [{ languageTag: 'fr', text: 'Café' }] } });
  expect(auth.client.adminSubmitCatalogEntry).not.toHaveBeenCalled();
});

test('localized name editor caps ten rows and rejects normalized duplicates before saving', async () => {
  auth.client.getExerciseCatalogConfig.mockResolvedValue(descriptors);
  auth.client.adminGetCatalogEntry.mockResolvedValue({ ...entry, program: 'general', modalityDetails: { familyKey: null, localizedNames: Array.from({ length: 10 }, (_, i) => ({ languageTag: 'fr', text: 'Name ' + i })) } });
  editor();
  await userEvent.click(await screen.findByRole('button', { name: 'Edit entry' }));
  expect(screen.getByRole('button', { name: 'Add localized name' })).toBeDisabled();
  fireEvent.change(screen.getByLabelText('Localized name 2'), { target: { value: 'Name 0' } });
  await userEvent.click(screen.getByRole('button', { name: 'Save draft' }));
  expect(await screen.findByRole('alert')).toHaveTextContent('Remove the duplicate name in the same language.');
  expect(auth.client.adminUpdateCatalogEntry).not.toHaveBeenCalled();
  await userEvent.click(screen.getByRole('button', { name: 'Remove localized name 2' }));
  expect(screen.getByRole('button', { name: 'Add localized name' })).toBeEnabled();
});

const programDetailsConfig = { ...descriptors, descriptorProfiles: [{ ...descriptors.descriptorProfiles[0], attireLabel: 'Practice attire', attires: [{ key: 'standard', label: 'Standard attire' }], levelLabel: 'Entry level', levels: [{ key: 'intro', label: 'Introductory' }], notationSystems: [{ key: 'sample', label: 'Sample notation' }] }] };
const programDetails = { ...modalityDetails, attireKey: 'standard', levelKey: 'intro', notationSystemKey: 'sample', notationText: '1 → 2\nR + L' };

test('program descriptor editing retains literal paired notation and prior names on CAS save', async () => {
  auth.client.getExerciseCatalogConfig.mockResolvedValue(programDetailsConfig);
  auth.client.adminGetCatalogEntry.mockResolvedValue({ ...entry, program: 'general', modalityDetails: programDetails });
  editor(); await userEvent.click(await screen.findByRole('button', { name: 'Edit entry' }));
  expect(screen.getByLabelText('Practice attire')).toHaveValue('standard');
  expect(screen.getByLabelText('Entry level')).toHaveValue('intro');
  expect(screen.getByLabelText('Notation')).toHaveAttribute('maxlength', '500');
  expect(screen.getByText('Describes this catalog entry, not an instructor rank or credential.')).toBeInTheDocument();
  fireEvent.change(screen.getByLabelText('Notation'), { target: { value: '  1 → 3\nR + L  ' } });
  await userEvent.selectOptions(screen.getByLabelText('Notation system'), '');
  await userEvent.click(screen.getByRole('button', { name: 'Save draft' }));
  expect(await screen.findByRole('alert')).toHaveTextContent('Enter notation and choose its system together');
  expect(auth.client.adminUpdateCatalogEntry).not.toHaveBeenCalled();
  await userEvent.selectOptions(screen.getByLabelText('Notation system'), 'sample');
  await userEvent.click(screen.getByRole('button', { name: 'Save draft' }));
  await waitFor(() => expect(auth.client.adminUpdateCatalogEntry).toHaveBeenCalledWith(entry.id, expect.objectContaining({ expectedRevision: 5, modalityDetails: { ...programDetails, notationText: '1 → 3\nR + L' }, labels: entry.labels, sources: entry.sources })));
});

test('retired program details remain visible and require explicit clear after program switch', async () => {
  auth.client.getExerciseCatalogConfig.mockResolvedValue(programDetailsConfig);
  auth.client.adminGetCatalogEntry.mockResolvedValue({ ...entry, program: 'general', modalityDetails: { ...programDetails, familyKey: null, localizedNames: [], attireKey: 'retired-attire' } });
  editor(); await userEvent.click(await screen.findByRole('button', { name: 'Edit entry' }));
  expect(screen.getByLabelText('Practice attire')).toHaveValue('retired-attire');
  await userEvent.selectOptions(screen.getByLabelText('Program'), '');
  expect(screen.getByLabelText('Attire')).toHaveValue('retired-attire');
  expect(screen.getByLabelText('Notation')).toHaveValue(programDetails.notationText);
  await userEvent.click(screen.getByRole('button', { name: 'Save draft' }));
  expect(await screen.findByRole('alert')).toHaveTextContent('Choose attire configured for this program or clear it.');
  await userEvent.click(screen.getByRole('button', { name: 'Clear program details' }));
  await userEvent.click(screen.getByRole('button', { name: 'Save draft' }));
  await waitFor(() => expect(auth.client.adminUpdateCatalogEntry).toHaveBeenCalledWith(entry.id, expect.objectContaining({ modalityDetails: { familyKey: null, localizedNames: [], attireKey: null, levelKey: null, notationSystemKey: null, notationText: null } })));
});

test('detail labels notation system beside escaped literal notation and preserves retired labels', () => {
  render(<MemoryRouter><CatalogEntryContent config={programDetailsConfig} entry={{ ...entry, program: 'general', modalityDetails: { ...programDetails, attireKey: 'retired-attire', notationText: '<b>1-2</b>\nR + L' } }} /></MemoryRouter>);
  expect(screen.getByText('retired-attire')).toBeInTheDocument();
  expect(screen.getByText('Sample notation')).toBeInTheDocument();
  expect(screen.getByText('<b>1-2</b> R + L')).toHaveStyle({ whiteSpace: 'pre-wrap' });
  expect(screen.getByText('Describes this catalog entry, not an instructor rank or credential.')).toBeInTheDocument();
});

test('all typed descriptor filters bind public deep links without modifying metadata', async () => {
  auth.me = null; auth.client.getExerciseCatalogConfig.mockResolvedValue(programDetailsConfig);
  auth.client.enumeratePublicCatalogEntries = vi.fn().mockResolvedValue(page([entry]));
  render(<MemoryRouter initialEntries={['/app/catalog?program=general&attireKey=standard&levelKey=intro&notationSystemKey=sample']}><CatalogHomeView /></MemoryRouter>);
  await screen.findByText('Example movement');
  await waitFor(() => expect(screen.getByLabelText('Practice attire')).toHaveValue('standard'));
  expect(screen.getByLabelText('Entry level')).toHaveValue('intro');
  expect(screen.getByLabelText('Notation system')).toHaveValue('sample');
  expect(auth.client.enumeratePublicCatalogEntries.mock.calls[0][0]).toMatchObject({ maxResults: 25, continuationToken: null, filters: { program: 'general', attireKey: 'standard', levelKey: 'intro', notationSystemKey: 'sample' } });
});

const apparatusConfig = { ...descriptors, equipment: [...config.equipment, { key: 'apparatus', label: 'Example apparatus' }, { key: 'unselected', label: 'Unselected equipment' }] };
const apparatusSetup = { apparatusKey: 'apparatus', manufacturer: 'Example maker', model: 'Example model', setupText: 'Position A\nPosition B', springSetupText: 'Source setting A' };

test('apparatus editor picks configured selected equipment and preserves all context on CAS save', async () => {
  auth.client.getExerciseCatalogConfig.mockResolvedValue(apparatusConfig);
  auth.client.adminGetCatalogEntry.mockResolvedValue({ ...entry, equipment: ['apparatus'], apparatusSetup, program: 'general', modalityDetails });
  editor(); await userEvent.click(await screen.findByRole('button', { name: 'Edit entry' }));
  const picker = screen.getByLabelText('Apparatus');
  expect([...picker.options].map((option) => option.value)).toEqual(['', 'apparatus']);
  expect(screen.getByLabelText('Manufacturer')).toHaveAttribute('maxlength', '120');
  expect(screen.getByLabelText('Resistance setup')).toHaveAttribute('maxlength', '500');
  fireEvent.change(screen.getByLabelText('Model'), { target: { value: '' } });
  await userEvent.click(screen.getByRole('button', { name: 'Save draft' }));
  expect(await screen.findByRole('alert')).toHaveTextContent('Resistance setup needs both the apparatus manufacturer and model.');
  expect(auth.client.adminUpdateCatalogEntry).not.toHaveBeenCalled();
  fireEvent.change(screen.getByLabelText('Model'), { target: { value: ' Revised model ' } });
  await userEvent.click(screen.getByRole('button', { name: 'Save draft' }));
  await waitFor(() => expect(auth.client.adminUpdateCatalogEntry).toHaveBeenCalledWith(entry.id, expect.objectContaining({ expectedRevision: 5, equipment: ['apparatus'], apparatusSetup: { ...apparatusSetup, model: 'Revised model' }, modalityDetails: expect.objectContaining(modalityDetails) })));
});

test('removing equipment retains incompatible setup until its independent clear', async () => {
  auth.client.getExerciseCatalogConfig.mockResolvedValue(apparatusConfig);
  auth.client.adminGetCatalogEntry.mockResolvedValue({ ...entry, equipment: ['apparatus'], apparatusSetup, program: 'general', modalityDetails });
  editor(); await userEvent.click(await screen.findByRole('button', { name: 'Edit entry' }));
  await userEvent.click(screen.getByRole('checkbox', { name: 'Example apparatus' }));
  expect(screen.getByLabelText('Apparatus')).toHaveValue('apparatus');
  expect(screen.getByLabelText('Resistance setup')).toHaveValue('Source setting A');
  await userEvent.click(screen.getByRole('button', { name: 'Save draft' }));
  expect(await screen.findByRole('alert')).toHaveTextContent('Choose apparatus from the configured equipment selected for this entry, or clear its setup.');
  expect(auth.client.adminUpdateCatalogEntry).not.toHaveBeenCalled();
  await userEvent.click(screen.getByRole('button', { name: 'Clear apparatus setup' }));
  expect(screen.getByLabelText('Localized name 1')).toHaveValue('Équilibre');
  await userEvent.click(screen.getByRole('button', { name: 'Save draft' }));
  await waitFor(() => expect(auth.client.adminUpdateCatalogEntry).toHaveBeenCalledWith(entry.id, expect.objectContaining({ equipment: [], apparatusSetup: { apparatusKey: null, manufacturer: null, model: null, setupText: null, springSetupText: null }, modalityDetails: expect.objectContaining(modalityDetails) })));
});

test('apparatus read card keeps escaped literal resistance settings with manufacturer/model and retired key fallback', () => {
  render(<MemoryRouter><CatalogEntryContent config={config} entry={{ ...entry, apparatusSetup: { ...apparatusSetup, apparatusKey: 'retired-apparatus', springSetupText: '<b>Setting A</b>\nSetting B' } }} /></MemoryRouter>);
  const heading = screen.getByRole('heading', { name: 'Apparatus setup' });
  const card = heading.closest('section');
  expect(card).toHaveTextContent('retired-apparatus');
  expect(card).toHaveTextContent('Example maker');
  expect(card).toHaveTextContent('Example model');
  expect(card).toHaveTextContent('<b>Setting A</b>');
  expect(screen.getByText('<b>Setting A</b> Setting B')).toHaveStyle({ whiteSpace: 'pre-wrap' });
  expect(card).toHaveTextContent('may not carry over to other equipment');
});

test('empty apparatus aggregate is absent from public content', () => {
  render(<MemoryRouter><CatalogEntryContent config={config} entry={{ ...entry, apparatusSetup: { apparatusKey: null, manufacturer: null, model: null, setupText: null, springSetupText: null } }} /></MemoryRouter>);
  expect(screen.queryByRole('heading', { name: 'Apparatus setup' })).toBeNull();
});

test.each([
  [{}, false],
  [{ features: { catalog: false } }, false],
  [{ features: { catalog: 'yes' } }, false],
  [{ features: { catalog: true } }, true]
])('the catalog is opt-in: member and admin navigation follow features.catalog %j', (property, on) => {
  expect(featureEnabled(property, 'catalog')).toBe(on);
  expect(memberRoutesFor(property).some((route) => route.id === 'catalog')).toBe(on);
  const adminIds = groupedAdminRoutes(property).flatMap((group) => group.routes).map((route) => route.id);
  expect(adminIds.includes('catalog')).toBe(on);
  expect(featureEnabled(property, 'library')).toBe(true);
});

test('signed-out visitors browse the catalog only when it is on and anonymous library browsing is allowed', () => {
  const open = { features: { catalog: true }, access: { model: 'Open', anonymousBrowsing: true } };
  expect(guestRoutesFor(open).some((route) => route.id === 'catalog')).toBe(true);
  expect(guestRoutesFor({ ...open, features: {} }).some((route) => route.id === 'catalog')).toBe(false);
  expect(guestRoutesFor({ ...open, access: { model: 'FullyGated' } }).some((route) => route.id === 'catalog')).toBe(false);
});


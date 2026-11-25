import { fireEvent, render, screen, waitFor } from '@testing-library/react-native';
import CatalogHome, { catalogFilters } from '../src/components/exerciseCatalog/CatalogHome';
import CatalogDetail from '../src/components/exerciseCatalog/CatalogDetail';
import { resetSessionFilters } from '../src/hooks/useListFilters';
import { Text } from 'react-native';
import CatalogGate from '../src/components/CatalogGate';
import { resolveAppLink } from '../src/linkTargets';

jest.mock('react-native-safe-area-context', () => ({ ...jest.requireActual('react-native-safe-area-context'), useSafeAreaInsets: () => ({ top: 0, right: 0, bottom: 0, left: 0 }) }));

const mockAuth = { api: null, user: null };
const mockPush = jest.fn();
jest.mock('../src/auth/AuthProvider', () => ({ useAuth: () => mockAuth, useOptionalAuth: () => mockAuth }));
jest.mock('expo-router', () => ({ useRouter: () => ({ push: mockPush }) }));
const mockProperty = { value: {} };
jest.mock('../src/property', () => ({ ...jest.requireActual('../src/property'), useProperty: () => mockProperty.value }));
jest.mock('../src/appConfig/AppContentProvider', () => ({ ...jest.requireActual('../src/appConfig/AppContentProvider'), useKillSwitch: () => ({ killed: false, message: null }) }));
const config = { programs: [{ key: 'general', label: 'General movement' }], muscles: [{ key: 'core', label: 'Core' }], equipment: [{ key: 'mat', label: 'Mat' }] };
const entry = { id: 'cat_a', slug: 'example-movement', name: 'Example movement', kind: 'Exercise', difficulty: 'Beginner', instructions: ['Prepare', 'Move slowly'], sources: [{ url: 'https://reference.example.test/source', title: 'Original source', license: 'CC0', attribution: 'Example author' }] };
const page = (objects, continuationToken = null) => ({ objects, continuationToken, endOfResults: !continuationToken });

beforeEach(() => {
  resetSessionFilters(); mockPush.mockClear(); mockAuth.user = null;
  mockAuth.api = { getExerciseCatalogConfig: jest.fn().mockResolvedValue(config), getPublicCatalogEntry: jest.fn().mockResolvedValue(entry), enumeratePublicCatalogEntries: jest.fn(async (q) => q.continuationToken ? page([{ ...entry, id: 'cat_b', name: 'Second movement' }]) : page([entry], 'next')), enumeratePublicCatalogRelationships: jest.fn().mockResolvedValue(page([{ id: 'rel', targetId: 'cat_b', targetSlug: 'second', targetName: 'Second movement', kind: 'Regression', note: 'Use this variation' }])) };
});

test('native catalog sends vocabulary filters separately from metadata', () => {
  expect(catalogFilters({ kind: 'Technique', muscle: 'core', equipment: 'mat', search: 'sample', labels: ['a'] })).toEqual({ kind: 'Technique', muscle: 'core', equipment: 'mat' });
});

test('guest browsing uses public pages, bounded load more and catalog navigation', async () => {
  await render(<CatalogHome />);
  expect(await screen.findByText('Example movement')).toBeOnTheScreen();
  await fireEvent.press(screen.getByLabelText('Load more'));
  expect(await screen.findByText('Second movement')).toBeOnTheScreen();
  expect(mockAuth.api.enumeratePublicCatalogEntries.mock.calls.at(-1)[0]).toMatchObject({ maxResults: 25, continuationToken: 'next' });
  await fireEvent.press(screen.getByLabelText('Example movement'));
  expect(mockPush).toHaveBeenCalledWith('/catalog/example-movement');
});

test('native detail shows instructions and attribution and follows published relationships', async () => {
  await render(<CatalogDetail entryId="cat_a" />);
  expect(await screen.findByText('Example movement')).toBeOnTheScreen();
  expect(screen.getByText('1. Prepare')).toBeOnTheScreen();
  expect(screen.getByText('Example author')).toBeOnTheScreen();
  await waitFor(() => expect(screen.getByText('Second movement')).toBeOnTheScreen());
  await fireEvent.press(screen.getByLabelText('Second movement'));
  expect(mockPush).toHaveBeenCalledWith('/catalog/second');
  expect(mockAuth.api.enumeratePublicCatalogRelationships.mock.calls[0]).toEqual(['cat_a', { maxResults: 25, continuationToken: null }]);
});

test('native descriptor filters retain exact values separately from search metadata', () => {
  expect(catalogFilters({ familyKey: 'standing', nameLanguage: 'fr', program: 'general', search: 'Équilibre' })).toEqual({ familyKey: 'standing', nameLanguage: 'fr', program: 'general' });
});

test('native detail preserves Unicode names and removed vocabulary keys', async () => {
  mockAuth.api.getExerciseCatalogConfig.mockResolvedValue({ ...config, descriptorProfiles: [{ programKey: 'general', familyLabel: 'Movement family', families: [], nameLanguages: [{ key: 'fr', label: 'French' }] }] });
  mockAuth.api.getPublicCatalogEntry.mockResolvedValue({ ...entry, program: 'general', modalityDetails: { familyKey: 'retired', localizedNames: [{ languageTag: 'fr', text: 'Équilibre' }, { languageTag: 'sa-Deva', text: 'नाम' }] } });
  await render(<CatalogDetail entryId="cat_a" />);
  expect(await screen.findByText('Équilibre')).toBeOnTheScreen();
  expect(screen.getByText('French')).toBeOnTheScreen();
  expect(screen.getByText('नाम')).toBeOnTheScreen();
  expect(screen.getByText('sa-Deva')).toBeOnTheScreen();
  expect(screen.getByText('retired')).toBeOnTheScreen();
  expect(screen.getByText('Movement family')).toBeOnTheScreen();
});

test('native descriptor selection resets the cursor and submits exact configured family and language', async () => {
  mockAuth.api.getExerciseCatalogConfig.mockResolvedValue({ ...config, descriptorProfiles: [{ programKey: 'general', familyLabel: 'Movement family', families: [{ key: 'standing', label: 'Standing' }], nameLanguages: [{ key: 'fr', label: 'French' }] }] });
  await render(<CatalogHome />);
  await screen.findByText('Example movement');
  await fireEvent.press(screen.getByTestId('filters-button'));
  await fireEvent.press(screen.getByRole('radio', { name: 'General movement' }));
  await fireEvent.press(screen.getByRole('radio', { name: 'Standing' }));
  await fireEvent.press(screen.getByRole('radio', { name: 'French' }));
  await fireEvent.press(screen.getByTestId('filters-panel-apply'));
  await waitFor(() => expect(mockAuth.api.enumeratePublicCatalogEntries.mock.calls.at(-1)[0]).toMatchObject({ maxResults: 25, continuationToken: null, filters: { program: 'general', familyKey: 'standing', nameLanguage: 'fr' } }));
});

test('native typed descriptors show system with literal notation and explain instructional level', async () => {
  mockAuth.api.getExerciseCatalogConfig.mockResolvedValue({ ...config, descriptorProfiles: [{ programKey: 'general', attireLabel: 'Practice attire', attires: [{ key: 'standard', label: 'Standard attire' }], levelLabel: 'Entry level', levels: [{ key: 'intro', label: 'Introductory' }], notationSystems: [{ key: 'sample', label: 'Sample notation' }] }] });
  mockAuth.api.getPublicCatalogEntry.mockResolvedValue({ ...entry, program: 'general', modalityDetails: { attireKey: 'retired-attire', levelKey: 'intro', notationSystemKey: 'sample', notationText: '1 → 2\nR + L' } });
  await render(<CatalogDetail entryId="cat_a" />);
  expect(await screen.findByText('1 → 2\nR + L')).toBeOnTheScreen();
  expect(screen.getByText('Sample notation')).toBeOnTheScreen();
  expect(screen.getByText('retired-attire')).toBeOnTheScreen();
  expect(screen.getByText('Introductory')).toBeOnTheScreen();
  expect(screen.getByText('Describes this catalog entry, not an instructor rank or credential.')).toBeOnTheScreen();
});

test('native typed descriptor selections send exact configured filters with a fresh cursor', async () => {
  mockAuth.api.getExerciseCatalogConfig.mockResolvedValue({ ...config, descriptorProfiles: [{ programKey: 'general', attires: [{ key: 'standard', label: 'Standard attire' }], levels: [{ key: 'intro', label: 'Introductory' }], notationSystems: [{ key: 'sample', label: 'Sample notation' }] }] });
  await render(<CatalogHome />); await screen.findByText('Example movement');
  await fireEvent.press(screen.getByTestId('filters-button'));
  await fireEvent.press(screen.getByRole('radio', { name: 'General movement' }));
  await fireEvent.press(screen.getByRole('radio', { name: 'Standard attire' }));
  await fireEvent.press(screen.getByRole('radio', { name: 'Introductory' }));
  await fireEvent.press(screen.getByRole('radio', { name: 'Sample notation' }));
  await fireEvent.press(screen.getByTestId('filters-panel-apply'));
  await waitFor(() => expect(mockAuth.api.enumeratePublicCatalogEntries.mock.calls.at(-1)[0]).toMatchObject({ maxResults: 25, continuationToken: null, filters: { program: 'general', attireKey: 'standard', levelKey: 'intro', notationSystemKey: 'sample' } }));
});

test('native apparatus context keeps source settings, manufacturer and model together with retired key fallback', async () => {
  mockAuth.api.getPublicCatalogEntry.mockResolvedValue({ ...entry, apparatusSetup: { apparatusKey: 'retired-apparatus', manufacturer: 'Example maker', model: 'Example model', setupText: 'Position A\nPosition B', springSetupText: 'Source setting A\nSource setting B' } });
  await render(<CatalogDetail entryId="cat_a" />);
  expect(await screen.findByText('Apparatus setup')).toBeOnTheScreen();
  expect(screen.getByText('retired-apparatus')).toBeOnTheScreen();
  expect(screen.getByText('Example maker')).toBeOnTheScreen();
  expect(screen.getByText('Example model')).toBeOnTheScreen();
  expect(screen.getByText('Position A\nPosition B')).toBeOnTheScreen();
  expect(screen.getByText('Source setting A\nSource setting B')).toBeOnTheScreen();
  expect(screen.getByText('Resistance settings are specific to the listed apparatus and model and may not carry over to other equipment.')).toBeOnTheScreen();
});

test('native catalog keeps empty apparatus setup hidden and preserves existing equipment filtering', async () => {
  mockAuth.api.getPublicCatalogEntry.mockResolvedValue({ ...entry, apparatusSetup: { apparatusKey: null, manufacturer: null, model: null, setupText: null, springSetupText: null } });
  await render(<CatalogDetail entryId="cat_a" />); await screen.findByText('Example movement');
  expect(screen.queryByText('Apparatus setup')).toBeNull();
  expect(catalogFilters({ equipment: 'mat', apparatusKey: 'unsupported-separate-filter' })).toEqual({ equipment: 'mat' });
});


test('catalog source opens inside the app instead of launching a browser', async () => {
  await render(<CatalogDetail entryId="cat_a" />);
  await fireEvent.press(await screen.findByLabelText('Original source'));
  expect(mockPush).toHaveBeenCalledWith({ pathname: '/external-content', params: { url: 'https://reference.example.test/source' } });
});

describe('opt-in catalog gate', () => {
  const open = { access: { model: 'Open', anonymousBrowsing: true } };
  test.each([
    [{ ...open }, false],
    [{ ...open, features: { catalog: false } }, false],
    [{ ...open, features: { catalog: true } }, true],
    [{ ...open, features: { catalog: true, library: false } }, false]
  ])('features %j opens the catalog: %s', async (property, opens) => {
    mockProperty.value = property;
    await render(<CatalogGate feature="library" optIn="catalog"><Text>catalog-body</Text></CatalogGate>);
    if (opens) expect(screen.getByText('catalog-body')).toBeOnTheScreen();
    else expect(screen.queryByText('catalog-body')).toBeNull();
  });

  test('catalog links stay inside the app', () => {
    expect(resolveAppLink('/catalog', {})).toEqual({ kind: 'native', path: '/catalog' });
    expect(resolveAppLink('/app/catalog/example-movement', {})).toEqual({ kind: 'native', path: '/catalog/example-movement' });
  });
});


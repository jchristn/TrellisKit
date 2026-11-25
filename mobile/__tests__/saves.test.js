import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react-native';
import fs from 'fs';
import path from 'path';
import { Alert, View } from 'react-native';
import SaveButton from '../src/components/SaveButton';
import SavedList, { moveCollection } from '../src/components/training/SavedList';
import * as Storage from '../src/library/storage';
import strings from '../src/strings';
import { clearSaveQueue, replaySaves, resetSaveState, saveQueue, sendSave } from '../src/training/saves';

jest.mock('react-native-safe-area-context', () => ({ ...jest.requireActual('react-native-safe-area-context'), useSafeAreaInsets: () => ({ top: 0, right: 0, bottom: 0, left: 0 }) }));

const mockAuth = { api: null, user: null };
const mockPush = jest.fn();
const mockProperty = { value: { features: {} } };
jest.mock('../src/auth/AuthProvider', () => ({ useAuth: () => mockAuth, useOptionalAuth: () => mockAuth }));
jest.mock('expo-router', () => ({ useRouter: () => ({ push: mockPush, replace: jest.fn() }) }));
jest.mock('../src/property', () => ({ ...jest.requireActual('../src/property'), useProperty: () => mockProperty.value }));

const t = strings.training;
const page = (objects, continuationToken = null) => ({ objects, continuationToken, endOfResults: !continuationToken, totalRecords: objects.length });
const apiError = (status, code) => Object.assign(new Error(code), { status, code });

function makeApi(overrides = {}) {
  return {
    saveState: jest.fn(async (targets) => ({ items: targets.map((target) => ({ ...target, saved: target.targetId === 'les_saved', favoriteId: target.targetId === 'les_saved' ? 'fav_1' : null })) })),
    saveItem: jest.fn(async (targetType, targetId) => ({ id: 'fav_new', targetType, targetId })),
    unsaveItem: jest.fn(async () => null),
    listSaves: jest.fn(async () => page([])),
    listSaveCollections: jest.fn(async () => page([])),
    createSaveCollection: jest.fn(async (body) => ({ id: 'fvc_new', ...body })),
    updateSaveCollection: jest.fn(async (id, body) => ({ id, ...body })),
    orderSaveCollections: jest.fn(async () => null),
    deleteSaveCollection: jest.fn(async () => null),
    ...overrides
  };
}

beforeEach(async () => {
  resetSaveState();
  Storage.__store.clear();
  await clearSaveQueue();
  mockPush.mockClear();
  mockProperty.value = { features: {} };
  mockAuth.user = { id: 'usr_1' };
  mockAuth.api = makeApi();
});

describe('save toggle', () => {
  test('hidden for signed-out visitors and when features.saves is off', async () => {
    mockAuth.user = null;
    const { rerender } = await render(<SaveButton targetType="Lesson" targetId="les_1" />);
    expect(screen.queryByTestId('save-button')).toBeNull();
    mockAuth.user = { id: 'usr_1' };
    mockProperty.value = { features: { saves: false } };
    await rerender(<SaveButton targetType="Lesson" targetId="les_1" />);
    expect(screen.queryByTestId('save-button')).toBeNull();
    expect(mockAuth.api.saveState).not.toHaveBeenCalled();
  });

  test('toggles on a screen share one batched saved-state request', async () => {
    await render(
      <View>
        <SaveButton targetType="Lesson" targetId="les_saved" title="Saved lesson" testID="a" />
        <SaveButton targetType="Series" targetId="ser_1" title="A course" testID="b" />
        <SaveButton targetType="Event" targetId="evt_1" title="An event" testID="c" />
      </View>
    );
    expect(await screen.findByTestId('a')).toHaveProp('accessibilityLabel', t.save.removeLabel);
    expect(screen.getByTestId('b')).toHaveProp('accessibilityLabel', t.save.saveLabelFor('A course'));
    expect(mockAuth.api.saveState).toHaveBeenCalledTimes(1);
    expect(mockAuth.api.saveState.mock.calls[0][0]).toEqual([{ targetType: 'Lesson', targetId: 'les_saved' }, { targetType: 'Series', targetId: 'ser_1' }, { targetType: 'Event', targetId: 'evt_1' }]);
  });

  test('saving is optimistic and stays saved when the server agrees', async () => {
    let finish;
    mockAuth.api.saveItem.mockImplementation(() => new Promise((resolve) => { finish = resolve; }));
    await render(<SaveButton targetType="CatalogEntry" targetId="cat_1" title="A technique" />);
    const button = await screen.findByTestId('save-button');
    fireEvent.press(button);
    await waitFor(() => expect(screen.getByTestId('save-button')).toHaveProp('accessibilityState', { disabled: false, selected: true }));
    expect(finish).toBeDefined();
    await act(async () => finish({ id: 'fav_2' }));
    expect(screen.getByTestId('save-button')).toHaveProp('accessibilityLabel', t.save.removeLabel);
    expect(mockAuth.api.saveItem).toHaveBeenCalledWith('CatalogEntry', 'cat_1', {});
  });

  test('a refused save rolls back and says why', async () => {
    mockAuth.api.saveItem.mockRejectedValue(apiError(409, 'LimitReached'));
    await render(<SaveButton targetType="Venue" targetId="ven_1" title="A place" />);
    await fireEvent.press(await screen.findByTestId('save-button'));
    await waitFor(() => expect(screen.getByTestId('save-button')).toHaveProp('accessibilityState', { disabled: false, selected: false }));
    expect(screen.getByTestId('save-button')).toHaveProp('accessibilityHint', t.save.failed);
    expect(await saveQueue().size()).toBe(0);
  });

  test('unsaving a saved item calls DELETE and a network failure is queued, not rolled back', async () => {
    mockAuth.api.unsaveItem.mockRejectedValue(apiError(0, 'NetworkError'));
    await render(<SaveButton targetType="Lesson" targetId="les_saved" title="Saved lesson" />);
    await waitFor(() => expect(screen.getByTestId('save-button')).toHaveProp('accessibilityLabel', t.save.removeLabel));
    await fireEvent.press(screen.getByTestId('save-button'));
    await waitFor(() => expect(screen.getByTestId('save-button')).toHaveProp('accessibilityHint', t.save.queued));
    expect(screen.getByTestId('save-button')).toHaveProp('accessibilityState', { disabled: false, selected: false });
    expect(await saveQueue().entries()).toEqual([{ lessonId: 'Lesson:les_saved', body: expect.objectContaining({ targetType: 'Lesson', targetId: 'les_saved', saved: false }) }]);
  });
});

describe('offline save queue', () => {
  test('offline changes queue without a request and replay in order, so the last choice wins', async () => {
    const api = makeApi();
    expect(await sendSave(api, 'Thread', 'thr_1', true, { offline: true })).toEqual({ queued: true });
    expect(await sendSave(api, 'Thread', 'thr_1', false, { offline: true })).toEqual({ queued: true });
    expect(api.saveItem).not.toHaveBeenCalled();
    const calls = [];
    api.saveItem.mockImplementation(async (type, id) => calls.push('save ' + type + ' ' + id));
    api.unsaveItem.mockImplementation(async (type, id) => calls.push('unsave ' + type + ' ' + id));
    expect(await replaySaves(api)).toEqual({ sent: 2, dropped: 0, remaining: 0 });
    expect(calls).toEqual(['save Thread thr_1', 'unsave Thread thr_1']);
  });

  test('a server error during replay keeps the rest; a refusal drops that entry', async () => {
    const api = makeApi();
    await sendSave(api, 'Post', 'fpo_1', true, { offline: true });
    await sendSave(api, 'Post', 'fpo_2', true, { offline: true });
    api.saveItem.mockRejectedValueOnce(apiError(503, 'Unavailable'));
    expect(await replaySaves(api)).toEqual({ sent: 0, dropped: 0, remaining: 2 });
    api.saveItem.mockRejectedValueOnce(apiError(404, 'NotFound'));
    expect(await replaySaves(api)).toEqual({ sent: 1, dropped: 1, remaining: 0 });
  });

  test('errors a retry would not fix are thrown to the caller', async () => {
    const api = makeApi({ saveItem: jest.fn().mockRejectedValue(apiError(403, 'Forbidden')) });
    await expect(sendSave(api, 'Instructor', 'ins_1', true)).rejects.toMatchObject({ status: 403 });
    expect(await saveQueue().size()).toBe(0);
  });
});

describe('Saved list', () => {
  const saves = [
    { id: 'fav_1', targetType: 'Lesson', targetId: 'les_1', note: null, collectionIds: [], target: { available: true, title: 'First steps' } },
    { id: 'fav_2', targetType: 'Event', targetId: 'evt_1', note: 'Bring water', collectionIds: ['fvc_1'], target: { available: false, title: null } }
  ];
  const collections = [{ id: 'fvc_1', name: 'Favorites', position: 0 }, { id: 'fvc_2', name: 'Later', position: 1 }];

  test('rows open their targets, unavailable targets do not, and notes are visible to you and moderators', async () => {
    mockAuth.api.listSaves.mockResolvedValue(page(saves));
    await render(<SavedList />);
    expect(await screen.findByText('First steps')).toBeOnTheScreen();
    expect(screen.getByText(t.saved.noteHint)).toBeOnTheScreen();
    expect(t.saved.noteHint).toBe('Visible to you and moderators.');
    expect(screen.getByText(t.saved.unavailable)).toBeOnTheScreen();
    expect(screen.getByTestId('training-saved-row-fav_2')).toHaveProp('accessibilityState', { disabled: true });
    await fireEvent.press(screen.getByTestId('training-saved-row-fav_1'));
    expect(mockPush).toHaveBeenCalledWith('/lessons/les_1');
  });

  test('the type filter asks the server for that type only', async () => {
    await render(<SavedList />);
    await screen.findByTestId('training-saved-empty');
    await fireEvent.press(screen.getByRole('radio', { name: t.saved.types.CatalogEntry }));
    await waitFor(() => expect(mockAuth.api.listSaves.mock.calls.at(-1)[0]).toMatchObject({ maxResults: 25, filters: { targetTypes: ['CatalogEntry'] } }));
    expect(await screen.findByText(t.saved.emptyFiltered)).toBeOnTheScreen();
  });

  test('removing a save is immediate and comes back when the server refuses', async () => {
    mockAuth.api.listSaves.mockResolvedValue(page(saves));
    mockAuth.api.unsaveItem.mockRejectedValueOnce(apiError(500, 'InternalError'));
    await render(<SavedList />);
    await fireEvent.press(await screen.findByTestId('training-saved-remove-fav_1'));
    expect(await screen.findByText('First steps')).toBeOnTheScreen();
    await fireEvent.press(screen.getByTestId('training-saved-remove-fav_1'));
    await waitFor(() => expect(screen.queryByText('First steps')).toBeNull());
    expect(mockAuth.api.unsaveItem).toHaveBeenLastCalledWith('Lesson', 'les_1');
  });

  test('collections can be created, renamed, reordered, and deleted, and filter the list', async () => {
    let current = collections.slice();
    mockAuth.api.listSaveCollections.mockImplementation(async () => page(current));
    mockAuth.api.createSaveCollection.mockImplementation(async (body) => { current = [...current, { id: 'fvc_3', name: body.name }]; return current.at(-1); });
    mockAuth.api.updateSaveCollection.mockImplementation(async (id, body) => { current = current.map((c) => (c.id === id ? { ...c, ...body } : c)); });
    mockAuth.api.deleteSaveCollection.mockImplementation(async (id) => { current = current.filter((c) => c.id !== id); });
    jest.spyOn(Alert, 'alert').mockImplementation((title, body, buttons) => buttons.find((b) => b.style === 'destructive').onPress());
    await render(<SavedList />);
    await fireEvent.press(await screen.findByRole('radio', { name: 'Later' }));
    await waitFor(() => expect(mockAuth.api.listSaves.mock.calls.at(-1)[0].filters).toEqual({ collectionId: 'fvc_2' }));

    await fireEvent.press(screen.getByTestId('training-saved-manage'));
    const sheet = await screen.findByTestId('saved-collections-sheet');
    await fireEvent.changeText(within(sheet).getByTestId('saved-collection-name'), 'Drills');
    await fireEvent.press(within(sheet).getByTestId('saved-collection-create'));
    await waitFor(() => expect(mockAuth.api.createSaveCollection).toHaveBeenCalledWith({ name: 'Drills' }));
    expect(await screen.findByTestId('saved-collection-fvc_3')).toBeOnTheScreen();

    await fireEvent.press(screen.getByTestId('saved-collection-rename-fvc_1'));
    await fireEvent.changeText(screen.getByTestId('saved-collection-rename-input-fvc_1'), 'Best');
    await fireEvent.press(screen.getByTestId('saved-collection-rename-save-fvc_1'));
    await waitFor(() => expect(mockAuth.api.updateSaveCollection).toHaveBeenCalledWith('fvc_1', { name: 'Best' }));

    await fireEvent.press(await screen.findByTestId('saved-collection-down-fvc_1'));
    await waitFor(() => expect(mockAuth.api.orderSaveCollections).toHaveBeenCalledWith(['fvc_2', 'fvc_1', 'fvc_3']));

    await fireEvent.press(screen.getByTestId('saved-collection-delete-fvc_3'));
    await waitFor(() => expect(mockAuth.api.deleteSaveCollection).toHaveBeenCalledWith('fvc_3'));
    await waitFor(() => expect(screen.queryByTestId('saved-collection-fvc_3')).toBeNull());
    Alert.alert.mockRestore();
  });

  test('a duplicate collection name is explained', async () => {
    mockAuth.api.createSaveCollection.mockRejectedValue(apiError(409, 'DuplicateName'));
    await render(<SavedList />);
    await fireEvent.press(await screen.findByTestId('training-saved-manage'));
    await fireEvent.changeText(await screen.findByTestId('saved-collection-name'), 'Favorites');
    await fireEvent.press(screen.getByTestId('saved-collection-create'));
    expect(await screen.findByTestId('saved-collections-error')).toHaveTextContent(t.saved.duplicateName);
  });

  test('a note and collections are saved for one item', async () => {
    mockAuth.api.listSaves.mockResolvedValue(page(saves));
    mockAuth.api.listSaveCollections.mockResolvedValue(page(collections));
    await render(<SavedList />);
    await fireEvent.press(await screen.findByTestId('training-saved-edit-fav_1'));
    const sheet = await screen.findByTestId('saved-item-sheet');
    expect(within(sheet).getByText(t.saved.noteHint)).toBeOnTheScreen();
    await fireEvent.changeText(within(sheet).getByTestId('saved-item-note'), 'Practice twice');
    await fireEvent(within(sheet).getByTestId('saved-item-collection-fvc_2'), 'valueChange', true);
    await fireEvent.press(within(sheet).getByTestId('saved-item-save'));
    await waitFor(() => expect(mockAuth.api.saveItem).toHaveBeenCalledWith('Lesson', 'les_1', { note: 'Practice twice', collectionIds: ['fvc_2'] }));
    expect(await screen.findByTestId('saved-item-status')).toHaveTextContent(t.saved.noteSaved);
  });

  test('moving a collection past either end changes nothing', () => {
    expect(moveCollection(collections, 'fvc_1', -1)).toBe(collections);
    expect(moveCollection(collections, 'fvc_2', 1)).toBe(collections);
    expect(moveCollection(collections, 'fvc_2', -1).map((c) => c.id)).toEqual(['fvc_2', 'fvc_1']);
  });
});

describe('save toggles on detail screens', () => {
  const src = path.resolve(__dirname, '../src/components');
  test.each([
    ['library/LessonDetail.jsx', 'Lesson'],
    ['library/SeriesDetail.jsx', 'Series'],
    ['exerciseCatalog/CatalogDetail.jsx', 'CatalogEntry'],
    ['places/VenueDetail.jsx', 'Venue'],
    ['places/InstructorDetail.jsx', 'Instructor'],
    ['events/EventDetail.jsx', 'Event'],
    ['feed/PostDetail.jsx', 'Post'],
    ['forums/ThreadDetail.jsx', 'Thread']
  ])('%s has a %s save toggle', (file, type) => {
    const source = fs.readFileSync(path.join(src, file), 'utf8');
    expect(source).toMatch(new RegExp('<SaveRow targetType="' + type + '"'));
  });
});

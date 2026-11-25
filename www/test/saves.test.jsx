import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import SaveButton from '../src/components/SaveButton.jsx';
import SavedView from '../src/views/training/SavedView.jsx';
import strings from '../src/strings.js';

const propertyState = vi.hoisted(() => ({ property: {}, fill: (text) => text }));
vi.mock('../src/context/PropertyContext.jsx', () => ({ useProperty: () => propertyState }));
const auth = vi.hoisted(() => ({ client: null, me: { id: 'usr_me' } }));
vi.mock('../src/context/AuthContext.jsx', () => ({ useAuth: () => auth }));

const t = strings.training;
const page = (objects, continuationToken = null) => ({ objects, continuationToken, endOfResults: !continuationToken, totalRecords: objects.length });
const favorite = (id, extra = {}) => ({ id, targetType: 'Lesson', targetId: 'les_' + id, note: null, collectionIds: [], target: { available: true, title: 'Lesson ' + id, imageUrl: null, href: null }, labels: [], tags: {}, ...extra });

function savesClient(extra = {}) {
  return {
    saveState: vi.fn(async (targets) => ({ items: targets.map((target) => ({ ...target, saved: target.targetId === 'les_saved', favoriteId: target.targetId === 'les_saved' ? 'fav_saved' : null })) })),
    saveItem: vi.fn(async (targetType, targetId, options) => ({ id: 'fav_new', targetType, targetId, note: options.note ?? null, collectionIds: options.collectionIds || [] })),
    unsaveItem: vi.fn(async () => null),
    listSaves: vi.fn(async () => page([])),
    listSaveCollections: vi.fn(async () => page([{ id: 'col_1', name: 'Guard work' }, { id: 'col_2', name: 'Escapes' }])),
    createSaveCollection: vi.fn(async (body) => ({ id: 'col_new', name: body.name })),
    updateSaveCollection: vi.fn(async () => ({})),
    orderSaveCollections: vi.fn(async () => ({})),
    deleteSaveCollection: vi.fn(async () => null),
    ...extra
  };
}

beforeEach(() => {
  propertyState.property = {};
  auth.me = { id: 'usr_me' };
  auth.client = savesClient();
});

describe('SaveButton', () => {
  it('asks for every toggle on the page in one batched request and names itself by state', async () => {
    render(<MemoryRouter><SaveButton targetType="Lesson" targetId="les_1" title="Opening" /><SaveButton targetType="Lesson" targetId="les_saved" title="Closing" /></MemoryRouter>);
    const save = await screen.findByRole('button', { name: 'Save Opening' });
    expect(save).toHaveAttribute('aria-pressed', 'false');
    expect(screen.getByRole('button', { name: t.save.savedLabel })).toHaveAttribute('aria-pressed', 'true');
    expect(auth.client.saveState).toHaveBeenCalledTimes(1);
    expect(auth.client.saveState.mock.calls[0][0]).toEqual([{ targetType: 'Lesson', targetId: 'les_1' }, { targetType: 'Lesson', targetId: 'les_saved' }]);
  });

  it('toggles at once and rolls back with a message when the server refuses', async () => {
    auth.client.saveItem.mockRejectedValueOnce(new Error('offline'));
    render(<MemoryRouter><SaveButton targetType="Event" targetId="evt_1" title="Open session" /></MemoryRouter>);
    await userEvent.click(await screen.findByRole('button', { name: 'Save Open session' }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Save Open session' })).toHaveAttribute('aria-pressed', 'false'));
    expect(screen.getByRole('status')).toHaveTextContent(t.save.failed);
    await userEvent.click(screen.getByRole('button', { name: 'Save Open session' }));
    await waitFor(() => expect(screen.getByRole('button', { name: t.save.savedLabel })).toHaveAttribute('aria-pressed', 'true'));
    expect(auth.client.saveItem).toHaveBeenLastCalledWith('Event', 'evt_1', {});
    await userEvent.click(screen.getByRole('button', { name: t.save.savedLabel }));
    await waitFor(() => expect(auth.client.unsaveItem).toHaveBeenCalledWith('Event', 'evt_1'));
  });

  it('is hidden for signed-out visitors and when saves are off', async () => {
    auth.me = null;
    const { container, rerender } = render(<MemoryRouter><SaveButton targetType="Lesson" targetId="les_1" title="Opening" /></MemoryRouter>);
    expect(container).toBeEmptyDOMElement();
    auth.me = { id: 'usr_me' };
    propertyState.property = { features: { saves: false } };
    rerender(<MemoryRouter><SaveButton targetType="Lesson" targetId="les_2" title="Opening" /></MemoryRouter>);
    expect(container).toBeEmptyDOMElement();
    expect(auth.client.saveState).not.toHaveBeenCalled();
  });

  it('picks collections, creates one, and saves a note visible to the member and moderators', async () => {
    render(<MemoryRouter><SaveButton targetType="CatalogEntry" targetId="cat_1" title="Hip escape" /></MemoryRouter>);
    await userEvent.click(await screen.findByRole('button', { name: t.save.optionsLabel('Hip escape') }));
    const dialog = await screen.findByRole('dialog');
    await userEvent.click(await within(dialog).findByRole('checkbox', { name: 'Escapes' }));
    await userEvent.type(within(dialog).getByLabelText(t.save.newCollection), 'Favorites');
    await userEvent.click(within(dialog).getByRole('button', { name: t.save.addCollection }));
    expect(await within(dialog).findByRole('checkbox', { name: 'Favorites' })).toBeChecked();
    expect(within(dialog).getByText(t.save.noteHint)).toBeInTheDocument();
    await userEvent.type(within(dialog).getByLabelText(t.save.note), 'Drill weekly');
    await userEvent.click(within(dialog).getByRole('button', { name: t.save.apply }));
    await waitFor(() => expect(auth.client.saveItem).toHaveBeenCalledWith('CatalogEntry', 'cat_1', { collectionIds: ['col_2', 'col_new'], note: 'Drill weekly' }));
    expect(auth.client.createSaveCollection).toHaveBeenCalledWith({ name: 'Favorites' });
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
    expect(screen.getByRole('button', { name: t.save.savedLabel })).toHaveAttribute('aria-pressed', 'true');
  });
});

function Where() {
  const location = useLocation();
  return <span data-testid="where">{location.pathname + location.search}</span>;
}

const renderSaved = (path = '/app/training/library/saved') => render(
  <MemoryRouter initialEntries={[path]}>
    <Routes>
      <Route path="/app/training/library/saved" element={<SavedView />} />
      <Route path="*" element={null} />
    </Routes>
    <Where />
  </MemoryRouter>
);

describe('SavedView', () => {
  it('lists saves with notes, filters by type and collection, and removes with rollback', async () => {
    auth.client.listSaves.mockImplementation(async () => page([favorite('1', { note: 'Watch twice' }), favorite('2', { target: { available: false, title: null } })]));
    auth.client.unsaveItem.mockRejectedValueOnce(new Error('offline'));
    renderSaved();
    expect(await screen.findByRole('link', { name: /Lesson 1/ })).toHaveAttribute('href', '/app/training/lessons/les_1');
    expect(screen.getByText(/Watch twice/)).toBeInTheDocument();
    expect(screen.getByText(t.saved.notesHint)).toBeInTheDocument();
    expect(screen.getByText(t.savedUnavailable)).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: t.saved.types.CatalogEntry }));
    await waitFor(() => expect(auth.client.listSaves.mock.calls.at(-1)[0].filters).toEqual({ targetTypes: ['CatalogEntry'] }));
    expect(screen.getByTestId('where')).toHaveTextContent('type=CatalogEntry');
    await userEvent.selectOptions(await screen.findByLabelText(t.saved.collectionFilterLabel), 'col_1');
    await waitFor(() => expect(auth.client.listSaves.mock.calls.at(-1)[0].filters).toEqual({ targetTypes: ['CatalogEntry'], collectionId: 'col_1' }));

    await userEvent.click(screen.getByRole('button', { name: t.saved.remove('Lesson 1') }));
    await waitFor(() => expect(auth.client.unsaveItem).toHaveBeenCalledWith('Lesson', 'les_1'));
    expect(await screen.findByRole('link', { name: /Lesson 1/ })).toBeInTheDocument();
    expect(screen.getAllByText(t.saved.removeFailed).length).toBeGreaterThan(0);
  });

  it('creates, renames, reorders, and deletes collections', async () => {
    renderSaved();
    const panel = await screen.findByTestId('saved-collections');
    await within(panel).findByText('Guard work');
    await userEvent.type(within(panel).getByLabelText(t.saved.collectionName), 'Takedowns');
    await userEvent.click(within(panel).getByRole('button', { name: t.saved.create }));
    await waitFor(() => expect(auth.client.createSaveCollection).toHaveBeenCalledWith({ name: 'Takedowns' }));

    await userEvent.click(within(panel).getByRole('button', { name: t.saved.renameLabel('Guard work') }));
    const input = within(panel).getByLabelText(t.saved.renameLabel('Guard work'));
    await userEvent.clear(input);
    await userEvent.type(input, 'Guard');
    await userEvent.click(within(panel).getByRole('button', { name: t.saved.renameSave }));
    await waitFor(() => expect(auth.client.updateSaveCollection).toHaveBeenCalledWith('col_1', { name: 'Guard' }));

    await userEvent.click(within(panel).getByRole('button', { name: t.saved.moveDown('Guard work') }));
    await waitFor(() => expect(auth.client.orderSaveCollections).toHaveBeenCalledWith(['col_2', 'col_1']));
    expect(within(panel).getByRole('button', { name: t.saved.moveUp('Guard work') })).toBeDisabled();

    await userEvent.click(within(panel).getByRole('button', { name: t.saved.deleteLabel('Escapes') }));
    const confirm = await screen.findByRole('dialog');
    expect(within(confirm).getByText(t.saved.deleteBody)).toBeInTheDocument();
    await userEvent.click(within(confirm).getByRole('button', { name: t.saved.delete }));
    await waitFor(() => expect(auth.client.deleteSaveCollection).toHaveBeenCalledWith('col_2'));
  });

  it('sends signed-out visitors and properties without saves back to Courses', async () => {
    propertyState.property = { features: { saves: false } };
    renderSaved();
    await waitFor(() => expect(screen.getByTestId('where')).toHaveTextContent('/app/training/library/courses'));
    expect(auth.client.listSaves).not.toHaveBeenCalled();
  });
});

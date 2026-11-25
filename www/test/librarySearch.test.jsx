import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Outlet, Route, Routes } from 'react-router-dom';
import LibraryView, { searchKinds } from '../src/views/training/LibraryView.jsx';
import { LessonTechniques, TechniqueLessons } from '../src/views/training/LessonTechniques.jsx';
import LessonTechniquesEditor, { TaughtInLessons, lessonTechniqueItems } from '../src/views/admin/library/LessonTechniquesEditor.jsx';
import { effectiveNavigation } from '../src/views/admin/PropertyConfigView.jsx';
import { normalizeOverview } from '../src/views/training/trainingModel.js';
import strings from '../src/strings.js';

const propertyState = vi.hoisted(() => ({ property: {}, fill: (text) => text }));
vi.mock('../src/context/PropertyContext.jsx', () => ({ useProperty: () => propertyState }));
const auth = vi.hoisted(() => ({ client: null, me: { id: 'usr_me' } }));
vi.mock('../src/context/AuthContext.jsx', () => ({ useAuth: () => auth }));
vi.mock('../src/context/AdminAuthContext.jsx', () => ({ useAdminAuth: () => auth }));

const t = strings.training;
const page = (objects, continuationToken = null) => ({ objects, continuationToken, endOfResults: !continuationToken, totalRecords: objects.length });
const series = { id: 'ser_1', slug: 'foundations', title: 'Foundations', lessonCount: 1, levels: [], viewer: {} };
const lesson = (id, title) => ({ id, slug: id, title, kind: 'Video', durationSeconds: 60, levels: [], series: { id: 'ser_1', slug: 'foundations', title: 'Foundations' }, viewer: {} });
const entry = { id: 'cat_1', slug: 'hip-escape', name: 'Hip escape', summary: 'Move the hips away.', status: 'Published' };

beforeEach(() => {
  propertyState.property = { features: { catalog: true } };
  auth.me = { id: 'usr_me' };
  auth.client = {
    searchLibrary: vi.fn(async () => ({ series: page([series]), lessons: page([lesson('les_1', 'Guard basics')], 'next'), techniques: page([entry]) })),
    enumerateLessons: vi.fn(async () => page([lesson('les_2', 'Guard retention')]))
  };
});

function renderLibrary(path, overview = null) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route element={<Outlet context={{ overview }} />}>
          <Route path="/app/training/library" element={<LibraryView />}>
            <Route path="courses" element={<p>Courses content</p>} />
            <Route path="techniques" element={<p>Techniques content</p>} />
            <Route path="saved" element={<p>Saved content</p>} />
          </Route>
        </Route>
      </Routes>
    </MemoryRouter>
  );
}

const segmentNames = () => within(screen.getByRole('tablist', { name: t.segmentsLabel })).getAllByRole('tab').map((tab) => tab.textContent);

describe('one Library with unified search', () => {
  it('searches courses, lessons, and techniques in one request and continues a kind through its own route', async () => {
    renderLibrary('/app/training/library/courses');
    expect(segmentNames()).toEqual(['Courses', 'Techniques', 'Saved']);
    await userEvent.type(screen.getByRole('searchbox', { name: t.search.placeholder }), 'guard{Enter}');
    const results = await screen.findByTestId('library-search-results');
    await waitFor(() => expect(auth.client.searchLibrary).toHaveBeenCalledWith({ query: 'guard', kinds: ['Series', 'Lesson', 'CatalogEntry'], maxResultsPerKind: 10 }, expect.anything()));
    expect(await within(results).findByText('Guard basics')).toBeInTheDocument();
    expect(within(results).getByRole('link', { name: /Hip escape/ })).toHaveAttribute('href', '/app/training/catalog/hip-escape');
    expect(within(results).getByTestId('series-card-ser_1')).toBeInTheDocument();
    expect(screen.queryByText('Courses content')).toBeNull();
    await userEvent.click(within(results).getByRole('button', { name: t.search.more }));
    expect(await within(results).findByText('Guard retention')).toBeInTheDocument();
    expect(auth.client.enumerateLessons.mock.calls[0][0]).toMatchObject({ search: 'guard', continuationToken: 'next' });
    await userEvent.click(screen.getByRole('button', { name: t.search.clear }));
    expect(await screen.findByText('Courses content')).toBeInTheDocument();
  });

  it('asks for techniques only while the Techniques segment shows', async () => {
    propertyState.property = { features: {} };
    renderLibrary('/app/training/library/courses?search=guard');
    await waitFor(() => expect(auth.client.searchLibrary).toHaveBeenCalled());
    expect(auth.client.searchLibrary.mock.calls[0][0].kinds).toEqual(['Series', 'Lesson']);
    expect(segmentNames()).toEqual(['Courses', 'Saved']);
    expect(searchKinds(['courses', 'techniques'])).toEqual(['Series', 'Lesson', 'CatalogEntry']);
  });

  it('hides Techniques when the overview does not list it and redirects its address to Courses', async () => {
    const overview = normalizeOverview({ sections: [{ key: 'Library', available: true, segments: ['Courses', 'Saved'] }] });
    renderLibrary('/app/training/library/techniques', overview);
    expect(await screen.findByText('Courses content')).toBeInTheDocument();
    expect(segmentNames()).toEqual(['Courses', 'Saved']);
  });

  it('needs two characters and reports rate limits', async () => {
    auth.client.searchLibrary.mockRejectedValueOnce({ status: 429, code: 'RateLimited' });
    renderLibrary('/app/training/library/courses?search=g');
    expect(screen.getByText(t.search.tooShort)).toBeInTheDocument();
    expect(screen.getByText('Courses content')).toBeInTheDocument();
    expect(auth.client.searchLibrary).not.toHaveBeenCalled();
    await userEvent.type(screen.getByRole('searchbox', { name: t.search.placeholder }), 'u{Enter}');
    expect(await screen.findByText(t.search.rateLimited)).toBeInTheDocument();
  });
});

describe('lesson and technique links', () => {
  it('lists the techniques in a lesson with times that seek the player', async () => {
    auth.client = { listLessonCatalogEntries: vi.fn(async () => page([{ ...entry, startSeconds: 90, position: 0 }, { id: 'cat_2', name: 'Bridge', position: 1, startSeconds: null }])) };
    const onSeek = vi.fn(() => true);
    render(<MemoryRouter><LessonTechniques client={auth.client} lessonId="les_1" onSeek={onSeek} /></MemoryRouter>);
    expect(await screen.findByRole('heading', { name: t.lessonTechniques.title })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Hip escape' })).toHaveAttribute('href', '/app/training/catalog/hip-escape');
    await userEvent.click(screen.getByRole('button', { name: t.lessonTechniques.seek('Hip escape', '1:30') }));
    expect(onSeek).toHaveBeenCalledWith(90);
    expect(auth.client.listLessonCatalogEntries).toHaveBeenCalledWith('les_1', expect.objectContaining({ maxResults: 50 }), expect.anything());
  });

  it('shows times as text without a seekable player and hides the section without the catalog', async () => {
    auth.client = { listLessonCatalogEntries: vi.fn(async () => page([{ ...entry, startSeconds: 90 }])) };
    const { unmount } = render(<MemoryRouter><LessonTechniques client={auth.client} lessonId="les_1" /></MemoryRouter>);
    expect(await screen.findByText(t.lessonTechniques.at('1:30'))).toBeInTheDocument();
    unmount();
    propertyState.property = { features: {} };
    const { container } = render(<MemoryRouter><LessonTechniques client={auth.client} lessonId="les_1" /></MemoryRouter>);
    expect(container).toBeEmptyDOMElement();
    expect(auth.client.listLessonCatalogEntries).toHaveBeenCalledTimes(1);
  });

  it('lists the lessons that teach a technique, public ones for signed-out visitors', async () => {
    auth.client = { listCatalogEntryLessons: vi.fn(async () => page([{ ...lesson('les_1', 'Guard basics'), locked: true }])), listPublicCatalogEntryLessons: vi.fn(async () => page([])) };
    const { unmount } = render(<MemoryRouter><TechniqueLessons client={auth.client} entryId="cat_1" /></MemoryRouter>);
    const link = await screen.findByRole('link', { name: /Guard basics/ });
    expect(link).toHaveAttribute('href', '/app/training/lessons/les_1');
    expect(link).toHaveTextContent(t.techniqueLessons.locked);
    unmount();
    render(<MemoryRouter><TechniqueLessons client={auth.client} entryId="cat_1" publicPage /></MemoryRouter>);
    expect(await screen.findByText(t.techniqueLessons.empty)).toBeInTheDocument();
    expect(auth.client.listPublicCatalogEntryLessons).toHaveBeenCalledWith('cat_1', expect.anything());
  });
});

describe('admin console', () => {
  it('edits the techniques in a lesson: add, time, order, and save the whole list', async () => {
    const client = {
      listLessonCatalogEntries: vi.fn(async () => page([{ id: 'cat_2', name: 'Bridge', position: 0, startSeconds: 30, status: 'Draft' }])),
      adminEnumerateCatalogEntries: vi.fn(async () => page([entry])),
      adminSetLessonCatalogEntries: vi.fn(async (id, items) => page(items.map((item) => ({ id: item.catalogEntryId, name: item.catalogEntryId === 'cat_1' ? 'Hip escape' : 'Bridge', startSeconds: item.startSeconds, position: item.position }))))
    };
    render(<MemoryRouter><LessonTechniquesEditor client={client} lessonId="les_1" /></MemoryRouter>);
    expect(await screen.findByText(t.admin.unpublishedWarning)).toBeInTheDocument();
    await userEvent.type(screen.getByLabelText(t.admin.techniquesSearch), 'hip');
    await userEvent.click(await screen.findByRole('option', { name: /Hip escape/ }));
    await userEvent.type(screen.getByLabelText(t.admin.startTimeFor('Hip escape')), '1:30');
    await userEvent.click(screen.getByRole('button', { name: t.admin.moveUp('Hip escape') }));
    await userEvent.click(screen.getByRole('button', { name: t.admin.save }));
    await waitFor(() => expect(client.adminSetLessonCatalogEntries).toHaveBeenCalledWith('les_1', [
      { catalogEntryId: 'cat_1', startSeconds: 90, position: 0 },
      { catalogEntryId: 'cat_2', startSeconds: 30, position: 1 }
    ]));
    expect(client.adminEnumerateCatalogEntries.mock.calls[0][0]).toMatchObject({ search: 'hip', filters: { status: 'Published' } });
    expect(lessonTechniqueItems([{ id: 'x', start: '1:99' }])).toBeNull();
  });

  it('never saves over links that did not load', async () => {
    const client = { listLessonCatalogEntries: vi.fn(async () => { throw new Error('offline'); }), adminSetLessonCatalogEntries: vi.fn() };
    render(<MemoryRouter><LessonTechniquesEditor client={client} lessonId="les_1" /></MemoryRouter>);
    expect(await screen.findByText(t.admin.techniquesLoadError)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: t.admin.save })).toBeNull();
  });

  it('shows the lessons a catalog entry is taught in', async () => {
    const client = { listCatalogEntryLessons: vi.fn(async () => page([lesson('les_1', 'Guard basics')])) };
    render(<MemoryRouter><TaughtInLessons client={client} entryId="cat_1" /></MemoryRouter>);
    expect(await screen.findByRole('link', { name: /Guard basics/ })).toHaveAttribute('href', '/admin/library/lessons/les_1');
  });

  it('reports the effective navigation for web and mobile', () => {
    const nav = effectiveNavigation({ features: { catalog: true }, mobile: { navigation: { shortcuts: ['feed', 'training', 'profile'], drawerItems: ['feed', 'training'] } } });
    expect(nav.web).toContain('Training');
    expect(nav.web).not.toContain('Library');
    expect(nav.bar).toEqual(['Feed', 'Training', 'Profile']);
    expect(nav.drawer).not.toContain('Techniques');
  });
});

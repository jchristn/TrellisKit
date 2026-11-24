import { describe, expect, it } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { createDirectoryFake, createEventsFake, createLibraryFake, makeEvent, makeLesson, makeProgress, makeSeries, makeSubmission, makeVenue } from '@trelliskit/shared/testing';
import { AppRoutes } from '../src/App.jsx';
import { ThemeProvider } from '../src/context/ThemeContext.jsx';
import { SESSION_HINT_KEY } from '../src/context/AuthContext.jsx';
import LibraryAdminView from '../src/views/admin/library/LibraryAdminView.jsx';
import LessonEditorView, { lessonBody } from '../src/views/admin/library/LessonEditorView.jsx';
import EventsAdminView from '../src/views/admin/events/EventsAdminView.jsx';
import EventEditorView from '../src/views/admin/events/EventEditorView.jsx';
import DirectoryAdminView from '../src/views/admin/directory/DirectoryAdminView.jsx';
import { groupedAdminRoutes } from '../src/views/admin/adminRoutes.js';
import { isConsoleStaff, staffAreas } from '../src/utils/engagement.js';
import { authResponse, createFetch, json, makeMe, renderAdmin } from './helpers.jsx';
import { fakeResponse } from './phase4Helpers.jsx';

/** A token-mode admin fetch whose Phase 4 routes are served by the fakes. */
function adminFetch(fakes) {
  return createFetch((call) => {
    if (call.path === '/api/v1/settings/enumerate') return json({});
    if (call.path === '/api/v1/health') return json({ status: 'Healthy', version: '1.0.0' });
    for (const fake of fakes) {
      const result = fake.handle({ method: call.method, path: call.path, query: call.query, body: call.body });
      if (result) return fakeResponse(result);
    }
    return undefined;
  });
}

const ids = (groups) => groups.flatMap((g) => g.routes.map((r) => r.id));

describe('staff roles in the admin console', () => {
  it('maps roles to areas and shows only the sections of the areas held', () => {
    const editor = makeMe({ roles: [{ role: 'LibraryEditor', resourceType: 'Library', resourceId: '*' }], membership: null });
    expect(staffAreas(editor)).toEqual(['library']);
    expect(isConsoleStaff(editor)).toBe(true);
    expect(isConsoleStaff(makeMe())).toBe(false);
    expect(staffAreas(makeMe({ roles: ['EventManager', { role: 'DirectoryModerator' }] }))).toEqual(['directory', 'events']);
    const property = { features: {} };
    expect(ids(groupedAdminRoutes(property, { moderatorOnly: true, staffAreas: ['library'] }))).toEqual(['home', 'moderation', 'library']);
    expect(ids(groupedAdminRoutes(property, { moderatorOnly: true, staffAreas: ['events', 'directory'] }))).toEqual(['home', 'moderation', 'events', 'directory']);
    expect(ids(groupedAdminRoutes({ features: { library: false } }))).not.toContain('library');
  });

  it('lets a library editor into the library page and sends other Phase 4 pages home', async () => {
    const me = makeMe({ roles: [{ role: 'LibraryEditor', resourceType: 'Library', resourceId: '*' }] });
    const fake = createLibraryFake({ series: [makeSeries('ser_1', { title: 'Getting started', status: 'Draft' })] });
    const { fetchImpl } = createFetch((call) => {
      if (call.path === '/api/v1/auth/refresh') return json(authResponse(me));
      if (call.path === '/api/v1/health') return json({ status: 'Healthy', version: '1.0.0' });
      if (call.path === '/api/v1/media/grant') return json({ expiresUtc: '2099-01-01T00:00:00Z' });
      return fakeResponse(fake.handle({ method: call.method, path: call.path, query: call.query, body: call.body }));
    });
    const view = render(<ThemeProvider><MemoryRouter initialEntries={['/admin/library']}><AppRoutes fetchImpl={fetchImpl} restoreOnLoad /></MemoryRouter></ThemeProvider>);
    expect(await screen.findByText('Getting started', {}, { timeout: 10000 })).toBeInTheDocument();
    const nav = screen.getByRole('navigation', { name: 'Admin navigation' });
    expect(within(nav).getAllByRole('link').map((a) => a.textContent)).toEqual(['Home', 'Moderation', 'Library']);
    expect(screen.queryByRole('tab', { name: 'Settings' })).toBeNull();
    expect(screen.queryByRole('tab', { name: 'Levels' })).toBeNull();
    view.unmount();

    render(<ThemeProvider><MemoryRouter initialEntries={['/admin/events']}><AppRoutes fetchImpl={fetchImpl} restoreOnLoad /></MemoryRouter></ThemeProvider>);
    await waitFor(() => expect(screen.queryByRole('tab', { name: 'Proposals' })).toBeNull());
    expect(await screen.findByRole('navigation', { name: 'Admin navigation' }, { timeout: 10000 })).toBeInTheDocument();
  }, 20000);
});

describe('library admin', () => {
  it('creates a series, lists lessons, and opens the lesson editor', async () => {
    const s1 = makeSeries('ser_1', { title: 'Getting started', status: 'Published' });
    const fake = createLibraryFake({ series: [s1], lessons: [makeLesson('les_1', s1, { title: 'First steps' })] });
    const { fetchImpl } = adminFetch([fake]);
    renderAdmin(<LibraryAdminView />, { fetchImpl, path: '/admin/library', routePath: '/admin/library' });
    expect(await screen.findByText('Getting started')).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: 'Settings' })).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'New series' }));
    const drawer = screen.getByRole('dialog');
    await userEvent.type(within(drawer).getByLabelText('Title'), 'Advanced work');
    await userEvent.click(within(drawer).getByRole('button', { name: 'Save' }));
    await waitFor(() => expect(fake.state.series.map((x) => x.title)).toContain('Advanced work'));
    expect(fake.calls.find((c) => c.path === '/admin/series' && c.method === 'POST').body).toMatchObject({ title: 'Advanced work' });
    await userEvent.click(screen.getByRole('tab', { name: 'Lessons' }));
    expect(await screen.findByText('First steps')).toBeInTheDocument();
  });

  it('lesson editor: markers, resources, and the audited viewers list', async () => {
    const s1 = makeSeries('ser_1', { title: 'Getting started' });
    const fake = createLibraryFake({ series: [s1], lessons: [makeLesson('les_1', s1, { title: 'First steps' })] });
    fake.state.viewers.push({ user: { id: 'usr_9', displayName: 'Pat Viewer' }, progress: makeProgress('les_1', { percent: 55 }) });
    const { fetchImpl } = adminFetch([fake]);
    renderAdmin(<LessonEditorView />, { fetchImpl, path: '/admin/library/lessons/les_1', routePath: '/admin/library/lessons/:id' });
    expect(await screen.findByRole('heading', { name: 'First steps', level: 1 })).toBeInTheDocument();
    expect(await screen.findByText('Pat Viewer')).toBeInTheDocument();
    expect(fake.state.audits).toEqual([{ action: 'library.viewingHistory.read', targetType: 'Lesson', targetId: 'les_1' }]);
    await userEvent.click(screen.getByRole('button', { name: 'Add a marker' }));
    await userEvent.clear(screen.getByLabelText('Time'));
    await userEvent.type(screen.getByLabelText('Time'), '1:35');
    await userEvent.type(screen.getByLabelText('Label'), 'Common mistakes');
    await userEvent.click(screen.getByRole('button', { name: 'Add a resource' }));
    await userEvent.type(screen.getAllByLabelText('Title')[1], 'Further reading');
    await userEvent.type(screen.getByLabelText('Link (https)'), 'https://example.test/reading');
    await userEvent.click(screen.getByRole('button', { name: 'Save' }));
    await waitFor(() => expect(fake.calls.some((c) => c.method === 'PUT' && c.path === '/admin/lessons/les_1')).toBe(true));
    const body = fake.calls.find((c) => c.method === 'PUT' && c.path === '/admin/lessons/les_1').body;
    expect(body.markers).toEqual([{ seconds: 95, label: 'Common mistakes' }]);
    expect(body.resources).toEqual([{ kind: 'Link', title: 'Further reading', mediaId: null, url: 'https://example.test/reading', text: null, position: 1 }]);
  }, 20000);

  it('lessonBody drops incomplete rows and sorts markers', () => {
    const body = lessonBody({ title: ' T ', slug: '', summary: '', description: '', kind: 'Video', levels: [], audience: 'Members', downloadable: true, commentsEnabled: true, notifyStaffOnComment: false,
      markers: [{ time: '2:00', label: 'B' }, { time: '0:30', label: 'A' }, { time: 'x', label: 'bad' }, { time: '1:00', label: '' }],
      resources: [{ kind: 'Reference', title: 'Book', text: 'Ch 3', url: '', mediaId: null }, { kind: 'Link', title: '', url: 'https://x.example.test' }],
      captions: [{ language: 'en', label: '', mediaId: 'med_1' }, { language: 'fr', label: 'French', mediaId: null }], labels: [], tags: {} });
    expect(body.title).toBe('T');
    expect(body.markers).toEqual([{ seconds: 30, label: 'A' }, { seconds: 120, label: 'B' }]);
    expect(body.resources).toEqual([{ kind: 'Reference', title: 'Book', mediaId: null, url: null, text: 'Ch 3', position: 1 }]);
    expect(body.captions).toEqual([{ language: 'en', label: 'en', mediaId: 'med_1', position: 1 }]);
    expect(body.videoMediaId).toBeUndefined();
  });
});

describe('events admin', () => {
  it('creates a repeating event with a live summary', async () => {
    const fake = createEventsFake();
    const { fetchImpl } = adminFetch([fake]);
    renderAdmin(<EventEditorView />, { fetchImpl, path: '/admin/events/new', routePath: '/admin/events/new', extraRoutes: null });
    await userEvent.type(screen.getByLabelText('Title'), 'Weekly session');
    await userEvent.type(screen.getByLabelText('Starts'), '2026-12-01T18:00');
    await userEvent.type(screen.getByLabelText('Ends'), '2026-12-01T19:00');
    await userEvent.type(screen.getByLabelText('Place name'), 'Example Hall');
    await userEvent.click(screen.getByLabelText('Repeats'));
    await userEvent.click(screen.getByRole('button', { name: 'Tue' }));
    await userEvent.click(screen.getByRole('button', { name: 'Thu' }));
    await userEvent.click(screen.getByLabelText('After'));
    await userEvent.clear(screen.getByLabelText('Number of occurrences'));
    await userEvent.type(screen.getByLabelText('Number of occurrences'), '6');
    expect(screen.getByTestId('recurrence-summary')).toHaveTextContent('Every week on Tuesday and Thursday, 6 times');
    await userEvent.click(screen.getByRole('button', { name: 'Create draft' }));
    await waitFor(() => expect(fake.calls.some((c) => c.method === 'POST' && c.path === '/admin/events')).toBe(true));
    const body = fake.calls.find((c) => c.method === 'POST' && c.path === '/admin/events').body;
    expect(body).toMatchObject({ title: 'Weekly session', startLocal: '2026-12-01T18:00', recurrence: { frequency: 'Weekly', interval: 1, weekdays: ['Tue', 'Thu'], count: 6 } });
  }, 20000);

  it('asks for the scope when saving or canceling an occurrence', async () => {
    const fake = createEventsFake({ events: [makeEvent('evt_1', { title: 'Weekly session', status: 'Published', startUtc: '2026-12-01T18:00:00Z', endUtc: '2026-12-01T19:00:00Z', timeZone: 'UTC', recurrence: { id: 'erc_1', rule: { frequency: 'Weekly', interval: 1, weekdays: ['Tue'] }, summary: 'Every week on Tuesday', occurrenceIndex: 2, isException: false, originalStartUtc: '2026-12-01T18:00:00Z' } })] });
    const { fetchImpl } = adminFetch([fake]);
    renderAdmin(<EventEditorView />, { fetchImpl, path: '/admin/events/evt_1', routePath: '/admin/events/:id' });
    const title = await screen.findByLabelText('Title');
    expect(screen.getByText(/Occurrence 3 of a repeating event/)).toBeInTheDocument();
    await userEvent.clear(title);
    await userEvent.type(title, 'Weekly session (new room)');
    await userEvent.click(screen.getByRole('button', { name: 'Save' }));
    const dialog = await screen.findByRole('dialog');
    await userEvent.click(within(dialog).getByLabelText('This and following occurrences'));
    await userEvent.click(within(dialog).getByRole('button', { name: 'Save' }));
    await waitFor(() => expect(fake.calls.some((c) => c.method === 'PUT' && c.path === '/admin/events/evt_1')).toBe(true));
    expect(fake.calls.find((c) => c.method === 'PUT').body).toMatchObject({ title: 'Weekly session (new room)', scope: 'Following', notifyAttendees: true, recurrence: { frequency: 'Weekly', weekdays: ['Tue'] } });

    await userEvent.click(await screen.findByRole('button', { name: 'Cancel event' }));
    const cancel = await screen.findByRole('dialog');
    await userEvent.click(within(cancel).getByLabelText('All occurrences'));
    await userEvent.type(within(cancel).getByLabelText('Reason (shown to members)'), 'Room closed');
    await userEvent.click(within(cancel).getByRole('button', { name: 'Cancel event' }));
    await waitFor(() => expect(fake.calls.some((c) => c.path === '/admin/events/evt_1/cancel')).toBe(true));
    expect(fake.calls.find((c) => c.path === '/admin/events/evt_1/cancel').body).toEqual({ reason: 'Room closed', notifyAttendees: true, scope: 'All' });
  }, 20000);

  it('reviews member proposals', async () => {
    const fake = createEventsFake({ events: [makeEvent('evt_p', { title: 'Picnic', status: 'Proposed', proposedBy: { id: 'usr_5', displayName: 'Sam Proposer' }, proposalNote: 'Bring food' })] });
    const { fetchImpl } = adminFetch([fake]);
    renderAdmin(<EventsAdminView />, { fetchImpl, path: '/admin/events?tab=proposals', routePath: '/admin/events' });
    expect(await screen.findByText('Picnic')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Approve' }));
    const dialog = await screen.findByRole('dialog');
    expect(within(dialog).getByText(/Bring food/)).toBeInTheDocument();
    await userEvent.click(within(dialog).getByRole('button', { name: 'Approve' }));
    await waitFor(() => expect(fake.event('evt_p').status).toBe('Published'));
    expect(fake.event('evt_p').hosts[0].id).toBe('usr_5');
  });
});

describe('directory admin', () => {
  it('reviews a submission with the diff, a pin, and approves it', async () => {
    const fake = createDirectoryFake({ venues: [makeVenue('ven_1', { name: 'Example Club Downtown' })] });
    fake.state.submissions.push(makeSubmission('dsb_1', { type: 'NewVenue', target: null, submitter: { id: 'usr_5', displayName: 'Sam Submitter' }, note: 'Opened last month.', fields: { name: 'Example Club Uptown', address: { line1: '200 Example Avenue', locality: 'Exampleville', countryCode: 'US' }, addressVisibility: 'Exact' }, changes: [] }));
    const { fetchImpl } = adminFetch([fake]);
    renderAdmin(<DirectoryAdminView />, { fetchImpl, path: '/admin/directory?tab=submissions', routePath: '/admin/directory' });
    await userEvent.click(await screen.findByText('Sam Submitter'));
    const drawer = await screen.findByRole('dialog');
    expect(await within(drawer).findByText('Opened last month.')).toBeInTheDocument();
    expect(within(drawer).getByText('200 Example Avenue, Exampleville, US')).toBeInTheDocument();
    await userEvent.type(within(drawer).getByLabelText('Latitude'), '37.75');
    await userEvent.type(within(drawer).getByLabelText('Longitude'), '-122.45');
    await userEvent.click(within(drawer).getByRole('button', { name: 'Approve' }));
    await waitFor(() => expect(fake.state.submissions[0].status).toBe('Approved'));
    expect(fake.calls.find((c) => c.path === '/admin/directory/submissions/dsb_1/approve').body).toEqual({ notifySubmitter: true, geo: { lat: 37.75, lng: -122.45 } });
    expect(fake.state.venues.find((x) => x.name === 'Example Club Uptown').address.line1).toBe('200 Example Avenue');
  });

  it('lists venues with the needs-a-pin badge and edits one', async () => {
    const fake = createDirectoryFake({ venues: [makeVenue('ven_1', { name: 'Pinless Place', geo: null, lat: null, lng: null })] });
    const { fetchImpl } = adminFetch([fake]);
    renderAdmin(<DirectoryAdminView />, { fetchImpl, path: '/admin/directory', routePath: '/admin/directory' });
    expect(await screen.findByText('Pinless Place')).toBeInTheDocument();
    expect(screen.getAllByText('Needs a pin').length).toBeGreaterThan(0);
    await userEvent.click(screen.getByText('Pinless Place'));
    const drawer = await screen.findByRole('dialog');
    await userEvent.type(within(drawer).getByLabelText('Latitude'), '37.7');
    await userEvent.type(within(drawer).getByLabelText('Longitude'), '-122.4');
    await userEvent.click(within(drawer).getByRole('button', { name: 'Save' }));
    await waitFor(() => expect(fake.venue('ven_1').geo).toMatchObject({ lat: 37.7, lng: -122.4, source: 'Manual' }));
  });
});

describe('route wiring', () => {
  it('mounts the Phase 4 member routes in the app', async () => {
    const fake = createLibraryFake({ series: [makeSeries('ser_1', { title: 'Getting started' })] });
    const me = makeMe();
    const { fetchImpl } = createFetch((call) => {
      if (call.path === '/api/v1/auth/refresh') return json(authResponse(me));
      if (call.path === '/api/v1/media/grant') return json({ expiresUtc: '2099-01-01T00:00:00Z' });
      if (call.path === '/api/v1/notifications/unread-count') return json({ unreadCount: 0 });
      return fakeResponse(fake.handle({ method: call.method, path: call.path, query: call.query, body: call.body }));
    });
    window.localStorage.setItem(SESSION_HINT_KEY, '1');
    render(<ThemeProvider><MemoryRouter initialEntries={['/app/library']}><AppRoutes fetchImpl={fetchImpl} restoreOnLoad /></MemoryRouter></ThemeProvider>);
    expect(await screen.findByTestId('series-card-ser_1', {}, { timeout: 10000 })).toBeInTheDocument();
    expect(screen.queryByText('Coming soon')).toBeNull();
  }, 20000);
});



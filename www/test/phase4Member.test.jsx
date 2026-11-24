import { describe, expect, it, vi } from 'vitest';
import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Route } from 'react-router-dom';
import { createDirectoryFake, createEventsFake, createLibraryFake, makeEvent, makeEventCategory, makeInstructor, makeLesson, makeProgress, makeSeries, makeVenue } from '@trelliskit/shared/testing';
import LibraryHomeView, { lessonFiltersFrom } from '../src/views/library/LibraryHomeView.jsx';
import SeriesView from '../src/views/library/SeriesView.jsx';
import LessonView from '../src/views/library/LessonView.jsx';
import { playerShortcut, lessonVideoSource } from '../src/components/library/LessonPlayer.jsx';
import EventsView, { eventFiltersFrom } from '../src/views/events/EventsView.jsx';
import EventDetailView from '../src/views/events/EventDetailView.jsx';
import { EventProposeView, MyEventProposalsView } from '../src/views/events/EventProposalViews.jsx';
import DirectoryView, { venueFiltersFrom } from '../src/views/directory/DirectoryView.jsx';
import { InstructorView, VenueView } from '../src/views/directory/VenueViews.jsx';
import { MySubmissionsView, SubmissionView } from '../src/views/directory/SubmissionViews.jsx';
import VenueMap from '../src/components/directory/VenueMap.jsx';
import { MEMBER_ROUTES, guestRoutesFor } from '../src/views/member/memberRoutes.js';
import { ThemeProvider } from '../src/context/ThemeContext.jsx';
import { parseCsv } from '../src/utils/csv.js';
import { makeMe } from './helpers.jsx';
import { phase4Fetch, renderP4, siteWith } from './phase4Helpers.jsx';

const LEVELS = { levelSource: 'Custom', levels: [{ key: 'starter', label: 'Starter', color: '#2E7D32' }, { key: 'next', label: 'Next steps', color: '#1F5FBF' }], progressIntervalSeconds: 15 };

function libraryFake() {
  const s1 = makeSeries('ser_1', { title: 'Getting started', levels: [{ key: 'starter', label: 'Starter' }], lessonCount: 2, sortOrder: 10 });
  const s2 = makeSeries('ser_2', { title: 'Going further', levels: [{ key: 'next', label: 'Next steps' }], sortOrder: 20, locked: true, viewer: { canFollow: false, canEdit: false, canOpen: false, requires: 'Membership' } });
  const l1 = makeLesson('les_1', s1, { title: 'First steps', position: 1, kind: 'Article', video: null, durationSeconds: 0, levels: [{ key: 'starter', label: 'Starter' }], resources: [{ id: 'lrs_1', kind: 'Link', title: 'Further reading', url: 'https://example.test/reading', media: null, text: null }, { id: 'lrs_2', kind: 'Reference', title: 'Handbook', url: null, media: null, text: 'Chapter 3' }] });
  const l2 = makeLesson('les_2', s1, { title: 'Second steps', position: 2, markers: [{ id: 'lmk_1', seconds: 0, label: 'Introduction' }, { id: 'lmk_2', seconds: 95, label: 'Common mistakes' }] });
  const l3 = makeLesson('les_3', s2, { title: 'Members lesson', position: 1, locked: true, audience: 'Members', viewer: { canPlay: false, canOpen: false, requires: 'Membership', canComment: false, canReact: false, canReport: true } });
  return createLibraryFake({ series: [s1, s2], lessons: [l1, l2, l3], progress: [makeProgress('les_2', { positionSeconds: 60, maxPositionSeconds: 60, watchedSeconds: 60, percent: 20, lastWatchedUtc: '2026-11-10T00:00:00Z' })] });
}

describe('member routes and access', () => {
  it('opens library, events, and the venue directory, and lets guests browse catalogs under anonymous browsing', () => {
    for (const id of ['library', 'events', 'directory']) expect(MEMBER_ROUTES.find((r) => r.id === id)).toMatchObject({ live: true, catalog: true });
    const partial = { access: { model: 'PartiallyGated', anonymousBrowsing: true, gatedFeatures: ['library', 'directory'] }, features: {} };
    expect(guestRoutesFor(partial).map((r) => r.id)).toEqual(expect.arrayContaining(['library', 'events', 'directory']));
    expect(guestRoutesFor({ access: { model: 'FullyGated' }, features: {} }).map((r) => r.id)).not.toContain('library');
  });

  it('reads filters from the URL', () => {
    expect(lessonFiltersFrom(new URLSearchParams('level=a&level=b&kind=Video&duration=short&resources=true'))).toEqual({ levels: ['a', 'b'], kind: 'Video', hasResources: true, maxDurationSeconds: 599 });
    expect(eventFiltersFrom(new URLSearchParams('when=Past&category=x&format=Online&format=Bad&spots=true&rsvp=Going'))).toEqual({ when: 'Past', category: ['x'], format: ['Online'], hasSpots: true, rsvpStatus: ['Going'] });
    expect(venueFiltersFrom(new URLSearchParams('program=youth&visitor=Welcome&verified=true&lat=37.774929&lng=-122.419416&r=25'))).toEqual({ programs: ['youth'], visitorPolicy: ['Welcome'], verifiedOnly: true, near: { lat: 37.77, lng: -122.42, radiusKm: 25 } });
    expect(venueFiltersFrom(new URLSearchParams('bbox=1,2,3,4'))).toEqual({ bbox: { south: 1, west: 2, north: 3, east: 4 } });
    expect(parseCsv('name,programs\n"Club, A","a;b"\n\nB,\n')).toEqual([['name', 'programs'], ['Club, A', 'a;b'], ['B', '']]);
  });
});

describe('library', () => {
  it('shows continue watching, series grouped by level with lock badges, and the viewing history note', async () => {
    const fake = libraryFake();
    const { fetchImpl } = phase4Fetch([fake]);
    await renderP4(<LibraryHomeView />, { fetchImpl, site: siteWith({ library: LEVELS }) });
    const continueSection = await screen.findByRole('heading', { name: 'Continue watching' });
    expect(continueSection).toBeInTheDocument();
    expect(await screen.findByTestId('lesson-row-les_2')).toHaveTextContent('Second steps');
    expect(await screen.findByRole('region', { name: 'Starter' })).toBeInTheDocument();
    const next = screen.getByRole('region', { name: 'Next steps' });
    expect(within(next).getByTestId('series-card-ser_2')).toHaveTextContent('Members only');
    expect(screen.getByText(/Library staff can see individual viewing history/)).toBeInTheDocument();
  });

  it('switches to lesson results with level chips and search, sending filters and labels', async () => {
    const fake = libraryFake();
    const { fetchImpl } = phase4Fetch([fake]);
    await renderP4(<LibraryHomeView />, { fetchImpl, site: siteWith({ library: LEVELS }) });
    await userEvent.click(await screen.findByRole('button', { name: /Starter/ }));
    expect(await screen.findByRole('heading', { name: 'Matching lessons' })).toBeInTheDocument();
    await screen.findByTestId('lesson-row-les_1');
    const call = fake.calls.filter((c) => c.path === '/lessons/enumerate').pop();
    expect(call.body.filters).toEqual({ levels: ['starter'] });
  });

  it('series page lists lessons, follows, and shows a locked stub prompt', async () => {
    const fake = libraryFake();
    const { fetchImpl } = phase4Fetch([fake]);
    await renderP4(<SeriesView />, { fetchImpl, path: '/app/series/getting-started', routePath: '/app/series/:id' });
    expect(await screen.findByRole('heading', { name: 'Getting started', level: 1 })).toBeInTheDocument();
    expect(await screen.findByTestId('lesson-row-les_1')).toHaveTextContent('First steps');
    await userEvent.click(screen.getByRole('button', { name: 'Follow Getting started' }));
    await waitFor(() => expect(fake.series('ser_1').isFollowed).toBe(true));
  });

  it('lesson page: resources, mark complete, reset, and locked lessons prompt for membership', async () => {
    const fake = libraryFake();
    const { fetchImpl } = phase4Fetch([fake], makeMe(), (call) => (call.path.endsWith('/comments') ? new Response(JSON.stringify({ objects: [], continuationToken: null, endOfResults: true }), { status: 200, headers: { 'content-type': 'application/json' } }) : undefined));
    const view = await renderP4(<LessonView />, { fetchImpl, path: '/app/lessons/les_1', routePath: '/app/lessons/:id' });
    expect(await screen.findByRole('heading', { name: 'First steps', level: 1 })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Further reading' })).toHaveAttribute('href', 'https://example.test/reading');
    expect(screen.getByText('Chapter 3')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: /Mark as complete/ }));
    await waitFor(() => expect(fake.progressOf('les_1').completed).toBe(true));
    expect(await screen.findByRole('button', { name: 'Mark as not complete' })).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Reset my progress' }));
    await waitFor(() => expect(fake.progressOf('les_1')).toBeNull());
    expect(screen.getByRole('link', { name: /Next lesson: Second steps/ })).toBeInTheDocument();
    view.unmount();

    await renderP4(<LessonView />, { fetchImpl, path: '/app/lessons/les_3', routePath: '/app/lessons/:id' });
    expect(await screen.findByRole('region', { name: 'This lesson is for members.' })).toBeInTheDocument();
    expect(screen.queryByTestId('lesson-video')).toBeNull();
  });

  it('player helpers: keyboard shortcuts and the video source', () => {
    expect(playerShortcut({ key: ' ' })).toEqual({ kind: 'toggle' });
    expect(playerShortcut({ key: 'l' })).toEqual({ kind: 'seekBy', seconds: 10 });
    expect(playerShortcut({ key: 'ArrowLeft' })).toEqual({ kind: 'seekBy', seconds: -5 });
    expect(playerShortcut({ key: '.', shiftKey: true })).toEqual({ kind: 'rate', step: 1 });
    expect(playerShortcut({ key: 'x' })).toBeNull();
    expect(lessonVideoSource({ video: { status: 'Ready', video: { hlsUrl: '/v/master.m3u8', durationSeconds: 300 } } })).toMatchObject({ hlsUrl: '/v/master.m3u8', status: 'Ready', durationSeconds: 300 });
    expect(lessonVideoSource({ video: null })).toBeNull();
  });
});

describe('events', () => {
  function eventsFake() {
    return createEventsFake({
      categories: [makeEventCategory('meetup', 'Meetup', '#1F5FBF')],
      events: [
        makeEvent('evt_1', { title: 'Autumn meetup', capacity: 1, startUtc: '2026-12-05T18:00:00Z', endUtc: '2026-12-05T21:00:00Z', category: { key: 'meetup', label: 'Meetup', color: '#1F5FBF' }, recurrence: { id: 'erc_1', rule: { frequency: 'Weekly', interval: 1, weekdays: ['Sat'] }, summary: 'Every week on Saturday', occurrenceIndex: 0, isException: false, originalStartUtc: '2026-12-05T18:00:00Z' } }),
        makeEvent('evt_2', { title: 'Online session', format: 'Online', online: { platformLabel: 'Video call', joinUrl: null, joinUrlAvailableUtc: '2026-12-10T17:45:00Z' }, startUtc: '2026-12-10T18:00:00Z', endUtc: '2026-12-10T19:00:00Z' }),
        makeEvent('evt_3', { title: 'Members night', locked: true, viewer: { ...makeEvent('x').viewer, canOpen: false, canRsvp: false, requires: 'Membership' }, startUtc: '2026-12-12T18:00:00Z', endUtc: '2026-12-12T19:00:00Z' })
      ],
      rsvps: [{ id: 'rsv_other', eventId: 'evt_1', userId: 'usr_9', user: { id: 'usr_9', displayName: 'Pat' }, status: 'Going', guestCount: 0, createdUtc: '2026-11-01T00:00:00Z' }]
    });
  }

  it('lists upcoming events with category chips and filters, and offers proposals and the calendar feed', async () => {
    const fake = eventsFake();
    const { fetchImpl } = phase4Fetch([fake]);
    await renderP4(<EventsView />, { fetchImpl, site: siteWith({ events: { categories: [{ key: 'meetup', label: 'Meetup', color: '#1F5FBF' }], proposals: true } }) });
    expect(await screen.findByTestId('event-row-evt_1')).toHaveTextContent('Autumn meetup');
    expect(screen.getByTestId('event-row-evt_3')).toHaveTextContent('Members only');
    expect(screen.getByRole('link', { name: /Propose an event/ })).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: /Meetup/ }));
    await waitFor(() => expect(fake.calls.filter((c) => c.path === '/events/enumerate').pop().body.filters).toEqual({ when: 'Upcoming', category: ['meetup'] }));
    await userEvent.click(screen.getByRole('button', { name: /Show my calendar link/ }));
    expect(await screen.findByTestId('calendar-feed-url')).toHaveValue('https://api.example.test/api/v1/calendar/cfd_fake.secret1.ics');
  });

  it('calendar view loads the month window and places events on their day', async () => {
    const fake = eventsFake();
    const { fetchImpl } = phase4Fetch([fake]);
    await renderP4(<EventsView />, { fetchImpl, path: '/app/events?view=calendar&y=2026&m=12', routePath: '/app/events' });
    const day = await screen.findByTestId('day-2026-12-10');
    expect(await within(day).findByText('Online session')).toBeInTheDocument();
    const call = fake.calls.filter((c) => c.path === '/events/enumerate').pop();
    expect(call.body.filters).toMatchObject({ when: 'All', startsAfterUtc: '2026-11-29T00:00:00.000Z' });
  });

  it('detail: time, recurrence, RSVP into the waitlist, not going, and the join note', async () => {
    const fake = eventsFake();
    const { fetchImpl } = phase4Fetch([fake]);
    const view = await renderP4(<EventDetailView />, { fetchImpl, path: '/app/events/evt_1', routePath: '/app/events/:id' });
    expect(await screen.findByRole('heading', { name: 'Autumn meetup', level: 1 })).toBeInTheDocument();
    expect(screen.getByText(/Every week on Saturday/)).toBeInTheDocument();
    // One Comments heading (the comment thread's own), not a second one from the page.
    await waitFor(() => expect(screen.getAllByRole('heading', { name: /^Comments/ })).toHaveLength(1));
    expect(screen.getByText(/Full/)).toBeInTheDocument();
    await userEvent.click(within(screen.getByTestId('rsvp-control')).getByRole('button', { name: 'Join the waitlist' }));
    expect(await screen.findByText(/You are on the waitlist, position 1/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Every date/ })).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Not going' }));
    await waitFor(() => expect(fake.state.rsvps.some((r) => r.userId === 'usr_1')).toBe(false));
    view.unmount();

    await renderP4(<EventDetailView />, { fetchImpl, path: '/app/events/evt_2', routePath: '/app/events/:id' });
    expect(await screen.findByText(/The join link appears here from/)).toBeInTheDocument();
  });

  it('locked events prompt for membership and hide the RSVP control', async () => {
    const fake = eventsFake();
    const { fetchImpl } = phase4Fetch([fake]);
    await renderP4(<EventDetailView />, { fetchImpl, path: '/app/events/evt_3', routePath: '/app/events/:id' });
    expect(await screen.findByRole('region', { name: 'Members only' })).toBeInTheDocument();
    expect(screen.queryByTestId('rsvp-control')).toBeNull();
  });

  it('proposals: validate, send without recurrence or hosts, list, and withdraw', async () => {
    const fake = eventsFake();
    const { fetchImpl } = phase4Fetch([fake]);
    const site = siteWith({ events: { proposals: true } });
    await renderP4(<EventProposeView />, { fetchImpl, site, path: '/app/events/propose', routePath: '/app/events/propose', extraRoutes: <Route path="/app/events/proposals" element={<MyEventProposalsView />} /> });
    await userEvent.click(screen.getByRole('button', { name: 'Send proposal' }));
    expect(await screen.findAllByText('Required.')).not.toHaveLength(0);
    await userEvent.type(screen.getByLabelText('Title'), 'Picnic in the park');
    await userEvent.type(screen.getByLabelText('Starts'), '2026-12-20T12:00');
    await userEvent.type(screen.getByLabelText('Ends'), '2026-12-20T14:00');
    await userEvent.type(screen.getByLabelText('Place name'), 'Example Park');
    await userEvent.click(screen.getByRole('button', { name: 'Send proposal' }));
    expect(await screen.findByRole('heading', { name: 'My event proposals' })).toBeInTheDocument();
    const sent = fake.calls.find((c) => c.path === '/events/proposals' && c.method === 'POST');
    expect(sent.body).toMatchObject({ title: 'Picnic in the park', startLocal: '2026-12-20T12:00', location: { name: 'Example Park' } });
    expect(sent.body.recurrence).toBeUndefined();
    expect(sent.body.hostUserIds).toBeUndefined();
    await userEvent.click(await screen.findByRole('button', { name: 'Withdraw' }));
    expect(await screen.findByText('Withdrawn')).toBeInTheDocument();
  }, 20000);
});

describe('venue directory', () => {
  const site = siteWith({ directory: { venueLabel: 'Club', venueLabelPlural: 'Clubs', instructorLabel: 'Coach', instructorLabelPlural: 'Coaches', programs: [{ key: 'youth', label: 'Youth' }, { key: 'beginners', label: 'Beginners' }], submissions: true, claims: true, map: { provider: 'None' } } });
  function directoryFake() {
    return createDirectoryFake({
      venues: [
        makeVenue('ven_1', { name: 'Example Club Downtown', programs: ['youth'], isVerified: true, scheduleText: 'Mon 6 pm', phone: '+1 555 0100' }),
        makeVenue('ven_2', { name: 'Example Club Uptown', lat: 37.8044, lng: -122.2712, addressVisibility: 'Approximate', programs: ['beginners'] }),
        makeVenue('ven_3', { name: 'Members Hall', locked: true, viewer: { canOpen: false, requires: 'Membership' } })
      ],
      instructors: [makeInstructor('ins_1', { displayName: 'Pat Example' })],
      links: [{ venueId: 'ven_1', instructorId: 'ins_1' }],
      programs: [{ key: 'youth', label: 'Youth' }, { key: 'beginners', label: 'Beginners' }]
    });
  }

  it('lists venues in the configured terms, filters by program, and uses a rounded location', async () => {
    const fake = directoryFake();
    const { fetchImpl } = phase4Fetch([fake]);
    const geolocation = { getCurrentPosition: vi.fn((ok) => ok({ coords: { latitude: 37.774929, longitude: -122.419416 } })) };
    Object.defineProperty(navigator, 'geolocation', { value: geolocation, configurable: true });
    await renderP4(<DirectoryView />, { fetchImpl, site, path: '/app/directory', routePath: '/app/directory' });
    expect(await screen.findByRole('heading', { name: 'Clubs', level: 1 })).toBeInTheDocument();
    expect(await screen.findByTestId('venue-row-ven_1')).toHaveTextContent('Example Club Downtown');
    expect(screen.getByTestId('venue-row-ven_2')).toHaveTextContent('Exampleville, EX');
    expect(screen.getByTestId('venue-row-ven_2')).not.toHaveTextContent('100 Example Street');
    expect(screen.getByRole('link', { name: /Add a club/ })).toBeInTheDocument();
    expect(screen.queryByTestId('venue-map')).toBeNull();
    await userEvent.click(screen.getByRole('button', { name: 'Youth' }));
    await waitFor(() => expect(fake.calls.filter((c) => c.path === '/venues/enumerate').pop().body.filters).toEqual({ programs: ['youth'] }));
    await userEvent.click(screen.getByRole('button', { name: /Use my location/ }));
    await waitFor(() => expect(fake.calls.filter((c) => c.path === '/venues/enumerate').pop().body.filters.near).toEqual({ lat: 37.77, lng: -122.42, radiusKm: 50 }));
    expect(await screen.findByText('Near my location')).toBeInTheDocument();
  });

  it('venue and instructor pages: details, coaches, report closed, and claim', async () => {
    const fake = directoryFake();
    const { fetchImpl } = phase4Fetch([fake], makeMe(), (call) => (call.path === '/api/v1/events/enumerate' ? new Response(JSON.stringify({ objects: [], endOfResults: true }), { status: 200, headers: { 'content-type': 'application/json' } }) : undefined));
    const view = await renderP4(<VenueView />, { fetchImpl, site, path: '/app/venues/ven_1', routePath: '/app/venues/:id' });
    expect(await screen.findByRole('heading', { name: 'Example Club Downtown', level: 1 })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Coaches' })).toBeInTheDocument();
    expect(await screen.findByText('Pat Example')).toBeInTheDocument();
    expect(screen.getByText('Mon 6 pm')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Report as closed' }));
    await userEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Send for review' }));
    await waitFor(() => expect(fake.state.submissions[0]).toMatchObject({ type: 'ReportClosed', status: 'Pending' }));
    view.unmount();

    await renderP4(<InstructorView />, { fetchImpl, site, path: '/app/instructors/ins_1', routePath: '/app/instructors/:id' });
    expect(await screen.findByRole('heading', { name: 'Pat Example', level: 1 })).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'This is me' }));
    const dialog = screen.getByRole('dialog');
    await userEvent.type(within(dialog).getByRole('textbox'), 'I teach the Monday evening class.');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Send for review' }));
    await waitFor(() => expect(fake.state.submissions.some((s) => s.type === 'ClaimInstructor')).toBe(true));
  });

  it('submission form: exact address note, duplicate step, confirm, and my suggestions', async () => {
    const fake = directoryFake();
    const { fetchImpl } = phase4Fetch([fake]);
    await renderP4(<SubmissionView />, { fetchImpl, site, path: '/app/directory/submit?type=NewVenue', routePath: '/app/directory/submit', extraRoutes: <Route path="/app/directory/submissions" element={<MySubmissionsView />} /> });
    expect(screen.getByText(/shown exactly as written once staff approve it/)).toBeInTheDocument();
    await userEvent.type(screen.getByLabelText('Name'), 'Example Club Downtown');
    await userEvent.type(screen.getByLabelText('City or town'), 'Exampleville');
    await userEvent.selectOptions(screen.getByLabelText('Country'), 'US');
    await userEvent.click(screen.getByRole('button', { name: 'Youth' }));
    await userEvent.type(screen.getByLabelText('Latitude'), '37.75');
    await userEvent.type(screen.getByLabelText('Longitude'), '-122.45');
    await userEvent.click(screen.getByRole('button', { name: 'Send for review' }));
    expect(await screen.findByRole('heading', { name: 'Is it one of these?' })).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'None of these, send mine' }));
    expect(await screen.findByRole('heading', { name: 'My suggestions' })).toBeInTheDocument();
    const sent = fake.calls.filter((c) => c.path === '/directory/submissions').pop();
    expect(sent.body).toMatchObject({ type: 'NewVenue', confirmNotDuplicate: true, fields: { name: 'Example Club Downtown', addressVisibility: 'Exact', programs: ['youth'], address: { locality: 'Exampleville', countryCode: 'US' }, geo: { lat: 37.75, lng: -122.45 } } });
    expect(await screen.findByText('Waiting for review')).toBeInTheDocument();
  }, 20000);

  it('suggest an edit sends only changed fields', async () => {
    const fake = directoryFake();
    const { fetchImpl } = phase4Fetch([fake]);
    await renderP4(<SubmissionView />, { fetchImpl, site, path: '/app/directory/submit?type=EditVenue&target=ven_1', routePath: '/app/directory/submit', extraRoutes: <Route path="/app/directory/submissions" element={<MySubmissionsView />} /> });
    const schedule = await screen.findByLabelText('Schedule');
    await userEvent.clear(schedule);
    await userEvent.type(schedule, 'Tue 7 pm');
    await userEvent.click(screen.getByRole('button', { name: 'Send for review' }));
    await screen.findByRole('heading', { name: 'My suggestions' });
    const sent = fake.calls.filter((c) => c.path === '/directory/submissions').pop();
    expect(sent.body.fields).toEqual({ scheduleText: 'Tue 7 pm' });
  });

  it('the map renders through MapLibre when a style is configured, and falls back when it fails', async () => {
    const handlers = {};
    const sources = {};
    class FakeMap {
      constructor(options) { this.options = options; FakeMap.last = this; setTimeout(() => handlers.load && handlers.load(), 0); }
      addControl() {}
      on(event, layer, fn) { handlers[typeof layer === 'function' ? event : event + ':' + layer] = typeof layer === 'function' ? layer : fn; }
      off() {}
      addSource(id) { sources[id] = { setData: vi.fn(), getClusterExpansionZoom: () => 12 }; }
      addLayer() {}
      getSource(id) { return sources[id]; }
      getBounds() { return { getSouth: () => 37.6, getWest: () => -122.6, getNorth: () => 37.9, getEast: () => -122.2 }; }
      getZoom() { return 11; }
      getCanvas() { return { style: {} }; }
      easeTo() {}
      fitBounds() {}
      remove() {}
    }
    const loader = () => Promise.resolve({ Map: FakeMap, NavigationControl: class {}, AttributionControl: class {} });
    const fake = directoryFake();
    const client = { enumerateDirectoryMap: vi.fn((q) => Promise.resolve(fake.handle({ method: 'POST', path: '/directory/map/enumerate', body: { ...q, maxResults: q.maxResults } }).body)) };
    const property = { directory: { map: { provider: 'MapLibre', styleUrl: 'https://tiles.example.test/style.json', attribution: 'Example tiles', defaultCenter: { lat: 37.7, lng: -122.4 }, defaultZoom: 10 } } };
    render(<ThemeProvider><VenueMap property={property} client={client} query={{ filters: { programs: ['youth'] } }} onSelect={() => {}} loader={loader} /></ThemeProvider>);
    expect(screen.getByTestId('venue-map')).toBeInTheDocument();
    await waitFor(() => expect(client.enumerateDirectoryMap).toHaveBeenCalled());
    expect(client.enumerateDirectoryMap.mock.calls[0][0]).toMatchObject({ bbox: { south: 37.6, west: -122.6, north: 37.9, east: -122.2 }, zoom: 11, filters: { programs: ['youth'] } });
    await waitFor(() => expect(sources.venues.setData).toHaveBeenCalled());
    expect(sources.venues.setData.mock.calls[0][0].features.map((f) => f.id)).toEqual(['ven_1']);
    expect(FakeMap.last.options.style).toBe('https://tiles.example.test/style.json');

    const failed = vi.fn();
    render(<ThemeProvider><VenueMap property={property} client={client} query={{}} onSelect={() => {}} onFailed={failed} loader={() => Promise.reject(new Error('no webgl'))} /></ThemeProvider>);
    await waitFor(() => expect(failed).toHaveBeenCalled());
    const none = render(<ThemeProvider><VenueMap property={{ directory: { map: { provider: 'None' } } }} client={client} query={{}} onSelect={() => {}} loader={loader} /></ThemeProvider>);
    expect(none.container.innerHTML).toBe('');
    await act(async () => {});
  });
});

import { dailyMetricPage } from '../../shared/src/testing/metricFake.js';
import { useState } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { createDirectoryFake, createEventsFake, createLibraryFake, createSupportFake, makeInstructor, makeVenue } from '@trelliskit/shared/testing';
import { ThemeProvider } from '../src/context/ThemeContext.jsx';
import EntityPicker, { PICKER_MAX_RESULTS, memberItem, pickerSearches, venueItem } from '../src/components/admin/EntityPicker.jsx';
import DailySeriesChart, { alignDailySeries, describeDailySeries } from '../src/components/usage/DailySeriesChart.jsx';
import PinMap, { pinFromLngLat } from '../src/components/directory/PinMap.jsx';
import { EngagementTiles, engagementSeries } from '../src/views/admin/EngagementTiles.jsx';
import EventEditorView, { adminEventBody } from '../src/views/admin/events/EventEditorView.jsx';
import EventsAdminView from '../src/views/admin/events/EventsAdminView.jsx';
import LibraryAdminView from '../src/views/admin/library/LibraryAdminView.jsx';
import DirectoryAdminView from '../src/views/admin/directory/DirectoryAdminView.jsx';
import SupportInboxView, { mergeTicketTimeline } from '../src/views/admin/SupportInboxView.jsx';
import MembersView from '../src/views/admin/MembersView.jsx';
import { eventFormFrom } from '../src/components/events/EventForm.jsx';
import { publicCatalogEnabled } from '../src/views/marketing/PublicSampleCatalog.jsx';
import { BUILD_SITE } from '../src/property/buildSite.js';
import { createFetch, json, renderAdmin, renderSite } from './helpers.jsx';
import { fakeResponse } from './phase4Helpers.jsx';

function adminFetch(fakes, extra = () => undefined) {
  return createFetch((call) => {
    const custom = extra(call);
    if (custom) return custom;
    if (call.path === '/api/v1/settings/enumerate') return json({});
    if (call.path === '/api/v1/health') return json({ status: 'Healthy', version: '1.0.0' });
    for (const fake of fakes) {
      const result = fake.handle({ method: call.method, path: call.path, query: call.query, body: call.body, headers: call.headers });
      if (result) return fakeResponse(result);
    }
    return undefined;
  });
}

const days = (values) => values.map((count, i) => ({ date: '2026-11-0' + (i + 1), count }));

describe('daily series charts (uPlot with a data table)', () => {
  it('aligns series on one date axis and describes totals and peaks', () => {
    const aligned = alignDailySeries([{ key: 'a', label: 'A', points: days([1, 0, 3]) }, { key: 'b', label: 'B', points: [{ date: '2026-11-02T00:00:00Z', value: 5 }] }]);
    expect(aligned.dates).toEqual(['2026-11-01', '2026-11-02', '2026-11-03']);
    expect(aligned.columns).toEqual([[1, 0, 3], [0, 5, 0]]);
    expect(aligned.xs[1] - aligned.xs[0]).toBe(86400);
    expect(describeDailySeries('Activity', [{ label: 'A' }, { label: 'B' }], aligned)).toBe('Activity: A 4 in total, peak 3 on 2026-11-03; B 5 in total, peak 5 on 2026-11-02.');
  });

  it('reads the event metrics day key so the chart matches the totals', () => {
    const aligned = alignDailySeries([{ key: 'events', label: 'Events', points: [{ day: '2026-11-14', count: 2 }, { day: '2026-11-18', count: 4 }] }]);
    expect(aligned.dates).toEqual(['2026-11-14', '2026-11-18']);
    expect(aligned.columns).toEqual([[2, 4]]);
  });

  it('renders the chart as an image with a text alternative and a toggleable table', async () => {
    render(<DailySeriesChart title="Activity" series={[{ key: 'a', label: 'Posts', points: days([2, 4]) }]} />);
    expect(screen.getByRole('img', { name: /Activity: Posts 6 in total/ })).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Show data' }));
    const table = screen.getByRole('table', { name: 'Activity by day' });
    expect(within(table).getAllByRole('row')).toHaveLength(3);
    expect(within(table).getByRole('rowheader', { name: '2026-11-02' }).closest('tr')).toHaveTextContent('4');
  });

  it('says so when there is no activity', () => {
    render(<DailySeriesChart title="Activity" series={[{ key: 'a', label: 'Posts', points: [] }]} />);
    expect(screen.getByText('No activity in this window yet.')).toBeInTheDocument();
    expect(screen.queryByRole('img')).toBeNull();
  });
});

describe('engagement metrics with the forums series (PHASE3 section 12)', () => {
  it('adds the forums series only when the server sends them', () => {
    expect(engagementSeries({ postsByDay: [], commentsByDay: [], reactionsByDay: [] }).map((x) => x.key)).toEqual(['posts', 'comments', 'reactions']);
    expect(engagementSeries({ postsByDay: [], commentsByDay: [], reactionsByDay: [], threadsByDay: [], threadCommentsByDay: [] }).map((x) => x.key)).toEqual(['posts', 'comments', 'reactions', 'threads', 'threadComments']);
  });

  it('shows forum thread and comment tiles and the chart from /admin/metrics/engagement', async () => {
    const metrics = { days: 30, postsByDay: days([1, 2]), commentsByDay: days([3, 1]), reactionsByDay: days([0, 5]), threadsByDay: days([2, 2]), threadCommentsByDay: days([4, 3]), activeMembers: 7, openReports: 0, scheduledPosts: 1 };
    const { fetchImpl, calls } = createFetch((call) => (call.path === '/api/v1/admin/metrics/engagement/enumerate' ? json(dailyMetricPage(metrics, call.body)) : undefined));
    renderAdmin(<EngagementTiles />, { fetchImpl });
    expect(await screen.findByText('Forum threads')).toBeInTheDocument();
    expect(screen.getByText('Forum threads').closest('.kpi-card, a, div')).toHaveTextContent('4');
    expect(screen.getByText('Forum comments').closest('.kpi-card, a, div')).toHaveTextContent('7');
    expect(screen.getByRole('img', { name: /Forum threads 4 in total.*Forum comments 7 in total/ })).toBeInTheDocument();
    expect(calls.find((c) => c.path === '/api/v1/admin/metrics/engagement/enumerate').body.filters).toEqual({ days: 30 });
  });
});

describe('Phase 4 metrics as charts', () => {
  it('events, library, and directory metrics render charts with their tables', async () => {
    const events = createEventsFake();
    const library = createLibraryFake();
    const directory = createDirectoryFake({ venues: [makeVenue('ven_1', { createdUtc: '2026-11-03T10:00:00Z' })] });
    const extra = (call) => {
      if (call.path === '/api/v1/admin/metrics/events/enumerate') return json(dailyMetricPage({ days: 30, eventsByDay: days([1, 2]), rsvpsByDay: days([5, 0]), checkInRate: 0.5, noShowRate: 0.1, byCategory: { objects: [], totalRecords: 0 } }, call.body));
      if (call.path === '/api/v1/admin/metrics/library/enumerate') return json(dailyMetricPage({ days: 30, playsByDay: days([9, 3]), completionsByDay: days([1, 1]), activeLearners: 4, topLessons: { objects: [] }, completionRateBySeries: { objects: [] } }, call.body));
      return undefined;
    };
    const { fetchImpl, calls } = adminFetch([events, library, directory], extra);
    const one = renderAdmin(<EventsAdminView />, { fetchImpl, path: '/admin/events?tab=metrics', routePath: '/admin/events' });
    expect(await screen.findByRole('img', { name: /Events and RSVPs per day: Events 3 in total.*RSVPs 5 in total/ })).toBeInTheDocument();
    one.unmount();
    const two = renderAdmin(<LibraryAdminView />, { fetchImpl, path: '/admin/library?tab=metrics', routePath: '/admin/library' });
    expect(await screen.findByRole('img', { name: /Plays and completions per day: Plays 12 in total/ })).toBeInTheDocument();
    two.unmount();
    renderAdmin(<DirectoryAdminView />, { fetchImpl, path: '/admin/directory?tab=metrics', routePath: '/admin/directory' });
    expect(await screen.findByRole('img', { name: /New Venues 1 in total/ })).toBeInTheDocument();
    expect(screen.getByText('Active Venues').closest('div')).toHaveTextContent('1');
    await userEvent.click(screen.getByRole('button', { name: 'Show data' }));
    expect(screen.getByRole('table', { name: /by day/ })).toBeInTheDocument();
    expect(calls.find((c) => c.path === '/api/v1/admin/directory/metrics/enumerate').body.filters).toEqual({ days: 30 });
  }, 20000);
});

describe('admin pickers (bounded search-as-you-type)', () => {
  it('searches after two characters, picks by keyboard, and removes a choice', async () => {
    const search = vi.fn(async (term) => [{ id: 'usr_1', label: 'Sam Rivera', detail: 'sam@example.test' }, { id: 'usr_2', label: 'Sam Lee' }].filter((x) => x.label.toLowerCase().includes(term.toLowerCase())));
    function Host() {
      const [value, setValue] = useState([]);
      return <EntityPicker label="Hosts" value={value} onChange={setValue} search={search} multiple max={2} />;
    }
    render(<Host />);
    const box = screen.getByRole('combobox', { name: 'Hosts' });
    await userEvent.type(box, 's');
    await new Promise((r) => setTimeout(r, 300));
    expect(search).not.toHaveBeenCalled();
    await userEvent.type(box, 'am');
    const list = await screen.findByRole('listbox', { name: 'Hosts results' });
    expect(within(list).getAllByRole('option')).toHaveLength(2);
    await userEvent.keyboard('{ArrowDown}{Enter}');
    expect(screen.getByText('Sam Lee')).toBeInTheDocument();
    expect(search.mock.calls.at(-1)[0]).toBe('sam');
    await userEvent.type(screen.getByRole('combobox', { name: 'Hosts' }), 'sam');
    const again = await screen.findByRole('listbox', { name: 'Hosts results' });
    expect(within(again).getAllByRole('option').map((o) => o.textContent)).toEqual(['Sam Rivera sam@example.test']);
    await userEvent.click(within(again).getByRole('option'));
    expect(screen.queryByRole('combobox', { name: 'Hosts' })).toBeNull();
    expect(screen.getByText('The most is 2.')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Remove Sam Lee' }));
    expect(screen.getByRole('combobox', { name: 'Hosts' })).toBeInTheDocument();
  });

  it('pickerSearches send bounded queries and map results to names', async () => {
    const client = {
      enumerateAdminMembers: vi.fn(async () => ({ objects: [{ id: 'usr_1', displayName: 'Sam', email: 's@example.test' }] })),
      searchMembers: vi.fn(async () => ({ objects: [{ id: 'usr_2', displayName: 'Jo', handle: 'jo' }] })),
      adminEnumerateVenues: vi.fn(async () => ({ objects: [{ id: 'ven_1', name: 'Hall', address: { locality: 'Exampleville', region: 'EX' } }] })),
      adminEnumerateInstructors: vi.fn(async () => ({ objects: [{ id: 'ins_1', displayName: 'Coach' }] })),
      enumerateVenues: vi.fn(async () => ({ objects: [{ id: 'ven_2', name: 'Member Hall', address: { locality: 'Exampleville' } }] })),
      enumerateInstructors: vi.fn(async () => ({ objects: [{ id: 'ins_2', displayName: 'Member Coach' }] }))
    };
    expect(await pickerSearches(client, { isSystemAdmin: true }).members('sa')).toEqual([{ id: 'usr_1', label: 'Sam', detail: 's@example.test' }]);
    expect(client.enumerateAdminMembers.mock.calls[0][0]).toEqual({ search: 'sa', maxResults: PICKER_MAX_RESULTS });
    expect(await pickerSearches(client).members('jo')).toEqual([{ id: 'usr_2', label: 'Jo', detail: '@jo' }]);
    expect(await pickerSearches(client, { directoryStaff: true }).venues('ha')).toEqual([{ id: 'ven_1', label: 'Hall', detail: 'Exampleville, EX' }]);
    expect(client.adminEnumerateVenues.mock.calls[0][0].maxResults).toBe(PICKER_MAX_RESULTS);
    expect((await pickerSearches(client, { isSystemAdmin: true }).instructors('co'))[0].label).toBe('Coach');
    // Other staff (an event manager) cannot read the admin directory: the pickers use the member routes.
    expect(await pickerSearches(client).venues('ha')).toEqual([{ id: 'ven_2', label: 'Member Hall', detail: 'Exampleville' }]);
    expect(client.enumerateVenues.mock.calls[0][0]).toEqual({ search: 'ha', maxResults: PICKER_MAX_RESULTS });
    expect((await pickerSearches(client).instructors('co'))[0].label).toBe('Member Coach');
    expect(client.adminEnumerateVenues).toHaveBeenCalledTimes(1);
    expect(memberItem(null)).toBeNull();
    expect(venueItem({ id: 'v', name: 'X' })).toEqual({ id: 'v', label: 'X', detail: '' });
  });

  it('the event editor picks the venue, hosts, and instructors by name and sends their ids', async () => {
    const events = createEventsFake();
    const directory = createDirectoryFake({ venues: [makeVenue('ven_1', { name: 'Example Hall' })], instructors: [makeInstructor('ins_1', { displayName: 'Robin Coach' })] });
    const members = { objects: [{ id: 'usr_7', displayName: 'Harper Host', email: 'harper@example.test' }], totalRecords: 1, endOfResults: true };
    const { fetchImpl, calls } = adminFetch([events, directory], (call) => (call.path === '/api/v1/admin/members/enumerate' ? json(members) : undefined));
    renderAdmin(<EventEditorView />, { fetchImpl, path: '/admin/events/new', routePath: '/admin/events/new' });
    await userEvent.type(await screen.findByLabelText('Title'), 'Open session');
    await userEvent.type(screen.getByLabelText('Starts'), '2026-12-01T18:00');
    await userEvent.type(screen.getByLabelText('Ends'), '2026-12-01T19:00');
    await userEvent.type(screen.getByRole('combobox', { name: 'Directory venue' }), 'Exam');
    await userEvent.click(within(await screen.findByRole('listbox', { name: 'Directory venue results' })).getByRole('option', { name: /Example Hall/ }));
    await userEvent.type(screen.getByRole('combobox', { name: 'Hosts' }), 'Harp');
    await userEvent.click(within(await screen.findByRole('listbox', { name: 'Hosts results' })).getByRole('option', { name: /Harper Host/ }));
    await userEvent.type(screen.getByRole('combobox', { name: 'Directory instructors' }), 'Robin');
    await userEvent.click(within(await screen.findByRole('listbox', { name: 'Directory instructors results' })).getByRole('option', { name: /Robin Coach/ }));
    expect(screen.queryByPlaceholderText('ven_...')).toBeNull();
    await userEvent.click(screen.getByRole('button', { name: 'Create draft' }));
    await waitFor(() => expect(events.calls.some((c) => c.method === 'POST' && c.path === '/admin/events')).toBe(true));
    const body = events.calls.find((c) => c.method === 'POST' && c.path === '/admin/events').body;
    expect(body).toMatchObject({ hostUserIds: ['usr_7'], instructorIds: ['ins_1'], location: { venueId: 'ven_1' } });
    expect(calls.find((c) => c.path === '/api/v1/admin/venues/enumerate').body).toMatchObject({ search: 'Exam', maxResults: PICKER_MAX_RESULTS });
  }, 20000);

  it('adminEventBody still accepts comma separated ids from older callers', () => {
    const form = { ...eventFormFrom(null), title: 'x', startLocal: '2026-12-01T18:00', endLocal: '2026-12-01T19:00', timeZone: 'UTC' };
    const body = adminEventBody(form, { hostUserIds: 'usr_1, usr_2', instructorIds: '', reminders: '' }, { audience: 'Members' });
    expect(body.hostUserIds).toEqual(['usr_1', 'usr_2']);
    expect(adminEventBody(form, { hosts: [{ id: 'usr_3' }], instructors: [], reminders: '' }, { audience: 'Members' }).hostUserIds).toEqual(['usr_3']);
  });
});

describe('venue pin editor map', () => {
  function fakeMaplibre() {
    const handlers = {};
    const markers = [];
    class FakeMap {
      constructor(options) { this.options = options; FakeMap.last = this; setTimeout(() => handlers.load && handlers.load(), 0); }
      addControl() {}
      on(event, fn) { handlers[event] = fn; }
      remove() {}
    }
    class FakeMarker {
      constructor(options) { this.options = options; this.handlers = {}; markers.push(this); }
      setLngLat(lngLat) { this.lngLat = { lng: lngLat[0], lat: lngLat[1] }; return this; }
      getLngLat() { return this.lngLat; }
      addTo() { return this; }
      on(event, fn) { this.handlers[event] = fn; }
      setDraggable(value) { this.options.draggable = value; }
      remove() {}
    }
    return { handlers, markers, FakeMap, loader: () => Promise.resolve({ Map: FakeMap, Marker: FakeMarker, NavigationControl: class {}, AttributionControl: class {} }) };
  }
  const property = { directory: { map: { provider: 'MapLibre', styleUrl: 'https://tiles.example.test/style.json', defaultCenter: { lat: 37.7, lng: -122.4 }, defaultZoom: 10 } } };

  it('places the pin on click and moves it on drag, rounded to 5 places', async () => {
    const maps = fakeMaplibre();
    const onChange = vi.fn();
    const view = render(<ThemeProvider><PinMap property={property} pin={{ lat: '', lng: '' }} onChange={onChange} loader={maps.loader} /></ThemeProvider>);
    expect(screen.getByTestId('pin-map')).toBeInTheDocument();
    await waitFor(() => expect(maps.handlers.load).toBeTruthy());
    await act(async () => { await new Promise((r) => setTimeout(r, 5)); });
    expect(screen.getByText(/Click the map to place the pin/)).toBeInTheDocument();
    act(() => maps.handlers.click({ lngLat: { lat: 37.1234567, lng: -122.7654321 } }));
    expect(onChange).toHaveBeenCalledWith({ lat: '37.12346', lng: '-122.76543' });
    view.rerender(<ThemeProvider><PinMap property={property} pin={{ lat: '37.12346', lng: '-122.76543' }} onChange={onChange} loader={maps.loader} /></ThemeProvider>);
    await waitFor(() => expect(maps.markers).toHaveLength(1));
    expect(maps.markers[0].options.draggable).toBe(true);
    maps.markers[0].lngLat = { lat: 37.5, lng: -122.5 };
    act(() => maps.markers[0].handlers.dragend());
    expect(onChange).toHaveBeenLastCalledWith({ lat: '37.5', lng: '-122.5' });
    expect(screen.getByText(/Drag the pin or click the map/)).toBeInTheDocument();
  });

  it('renders nothing without a map style (the numeric fields remain) or when the map fails', async () => {
    const none = render(<ThemeProvider><PinMap property={{ directory: { map: { provider: 'None' } } }} pin={{ lat: '', lng: '' }} onChange={() => {}} /></ThemeProvider>);
    expect(none.container.innerHTML).toBe('');
    none.unmount();
    render(<ThemeProvider><PinMap property={property} pin={{ lat: '', lng: '' }} onChange={() => {}} loader={() => Promise.reject(new Error('no webgl'))} /></ThemeProvider>);
    await waitFor(() => expect(screen.queryByTestId('pin-map')).toBeNull());
    expect(pinFromLngLat({ lat: 1.000004, lng: 2 })).toEqual({ lat: '1', lng: '2' });
  });
});

describe('public events, venues, and sample lessons on marketing pages', () => {
  const live = { ...BUILD_SITE.property, features: { ...(BUILD_SITE.property.features || {}), publicSamples: true, events: true, library: true, directory: true } };

  it('follows the feature and public switches', () => {
    expect(publicCatalogEnabled(live, 'events')).toBe(true);
    expect(publicCatalogEnabled({ ...live, events: { publicListingsEnabled: false } }, 'events')).toBe(false);
    expect(publicCatalogEnabled({ ...live, features: { ...live.features, library: false } }, 'lessons')).toBe(false);
    expect(publicCatalogEnabled({ ...live, features: { ...live.features, publicSamples: false } }, 'venues')).toBe(false);
    expect(publicCatalogEnabled(live, 'nope')).toBe(false);
  });

  it('shows them on the home page with a join call to action and bounded requests', async () => {
    const { fetchImpl, calls } = createFetch((call) => {
      if (call.path === '/api/v1/property') return json(live);
      if (call.path === '/api/v1/public/events/enumerate') return json({ objects: [{ id: 'evt_1', title: 'Open house', startUtc: '2026-12-01T18:00:00Z', timeZone: 'UTC', format: 'InPerson', location: { name: 'Example Hall' } }], endOfResults: true });
      if (call.path === '/api/v1/public/lessons/enumerate') return json({ objects: [{ id: 'les_1', title: 'First steps', durationSeconds: 300, series: { title: 'Basics' } }], endOfResults: true });
      if (call.path === '/api/v1/public/venues/enumerate') return json({ objects: [], endOfResults: true });
      return undefined;
    });
    renderSite('/', { fetchImpl });
    const events = await screen.findByRole('region', { name: 'Upcoming events' });
    expect(within(events).getByRole('heading', { name: 'Open house' })).toBeInTheDocument();
    expect(within(events).getByText(/Example Hall/)).toBeInTheDocument();
    expect(within(events).getByRole('link', { name: 'Join to RSVP: Open house' })).toHaveAttribute('href', '/sign-up');
    const lessons = await screen.findByRole('region', { name: 'Sample lessons' });
    expect(within(lessons).getByText(/From Basics/)).toBeInTheDocument();
    expect(screen.queryByRole('region', { name: /Find a/ })).toBeNull();
    for (const path of ['/api/v1/public/events/enumerate', '/api/v1/public/lessons/enumerate', '/api/v1/public/venues/enumerate']) {
      expect(calls.find((c) => c.path === path).body.maxResults).toBeLessThanOrEqual(20);
    }
  });

  it('never requests a listing the live property turns off (no 404s from the build-time copy)', async () => {
    const off = { ...live, events: { ...(live.events || {}), publicListingsEnabled: false }, directory: { ...(live.directory || {}), publicListingsEnabled: false } };
    const { fetchImpl, calls } = createFetch((call) => {
      if (call.path === '/api/v1/property') return json(off);
      if (call.path === '/api/v1/public/lessons/enumerate') return json({ objects: [{ id: 'les_1', title: 'First steps', durationSeconds: 300, series: { title: 'Basics' } }], endOfResults: true });
      return undefined;
    });
    renderSite('/', { fetchImpl });
    expect(await screen.findByRole('region', { name: 'Sample lessons' })).toBeInTheDocument();
    expect(calls.some((c) => c.path === '/api/v1/public/events/enumerate')).toBe(false);
    expect(calls.some((c) => c.path === '/api/v1/public/venues/enumerate')).toBe(false);
  });
});

describe('admin ticket drawer history', () => {
  it('merges loaded history before the embedded newest messages', () => {
    const preview = { objects: [{ id: 'm3' }, { id: 'm4' }], totalRecords: 4 };
    expect(mergeTicketTimeline(preview, [])).toEqual({ items: [{ id: 'm3' }, { id: 'm4' }], hidden: 2, splitAt: 0 });
    expect(mergeTicketTimeline(preview, [{ id: 'm1' }])).toEqual({ items: [{ id: 'm1' }, { id: 'm3' }, { id: 'm4' }], hidden: 1, splitAt: 1 });
    expect(mergeTicketTimeline(preview, [{ id: 'm1' }, { id: 'm2' }, { id: 'm3' }]).hidden).toBe(0);
  });

  it('pages through the full message history with continuation', async () => {
    const support = createSupportFake();
    const ticket = support.handle({ method: 'POST', path: '/api/v1/tickets', body: { type: 'Billing', title: 'Long thread', body: 'The first message of a long thread.' }, headers: { 'x-client': 'web' } }).body;
    for (let i = 1; i <= 75; i += 1) support.handle({ method: 'POST', path: '/api/v1/admin/tickets/' + ticket.id + '/messages', body: { kind: i % 10 === 0 ? 'Note' : 'Reply', body: 'Message number ' + i } });
    const { fetchImpl, calls } = adminFetch([support]);
    renderAdmin(<SupportInboxView />, { fetchImpl, path: '/admin/support?ticket=' + ticket.id, routePath: '/admin/support' });
    const drawer = await screen.findByRole('dialog', { name: 'Ticket #' + ticket.number });
    expect(await within(drawer).findByText('Message number 75')).toBeInTheDocument();
    expect(within(drawer).queryByText('Message number 1')).toBeNull();
    // 76 messages: the opening reply plus 75.
    expect(within(drawer).getByText('56 earlier messages are not shown.')).toBeInTheDocument();
    await userEvent.click(within(drawer).getByRole('button', { name: 'Show earlier messages' }));
    expect(await within(drawer).findByText('Message number 1')).toBeInTheDocument();
    expect(within(drawer).getByText('6 earlier messages are not shown.')).toBeInTheDocument();
    await userEvent.click(within(drawer).getByRole('button', { name: 'Show earlier messages' }));
    await waitFor(() => expect(within(drawer).queryByRole('button', { name: 'Show earlier messages' })).toBeNull());
    const pages = calls.filter((c) => c.path === '/api/v1/admin/tickets/' + ticket.id + '/messages/enumerate');
    expect(pages.map((c) => c.body)).toEqual([{ maxResults: 50, ordering: 'CreatedAscending' }, { maxResults: 50, ordering: 'CreatedAscending', continuationToken: 'o:50' }]);
    expect(within(drawer).getAllByText(/^Message number \d+$/)).toHaveLength(75);
  }, 20000);
});

describe('block protection staff roles in the members filter', () => {
  it('offers every staff role', async () => {
    const { fetchImpl } = createFetch((call) => (call.path === '/api/v1/admin/members/enumerate' ? json({ objects: [], totalRecords: 0, endOfResults: true }) : undefined));
    renderAdmin(<MembersView />, { fetchImpl, path: '/admin/members', routePath: '/admin/members' });
    const role = await screen.findByLabelText('Role');
    const options = within(role).getAllByRole('option').map((o) => o.textContent);
    expect(options).toEqual(expect.arrayContaining(['Library editor', 'Support agent', 'Communicator', 'Directory moderator', 'Event manager']));
  });
});

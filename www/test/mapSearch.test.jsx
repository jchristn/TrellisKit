import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, fireEvent, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useLocation, useNavigate } from 'react-router-dom';
import { createDirectoryFake, createEventsFake, makeEvent, makeVenue } from '@trelliskit/shared/testing';
import DirectoryView from '../src/views/directory/DirectoryView.jsx';
import EventsView from '../src/views/events/EventsView.jsx';
import { styleFont } from '../src/components/maps/SearchMap.jsx';
import { phase4Fetch, renderP4, siteWith } from './phase4Helpers.jsx';

/** A controllable MapLibre stand-in: tests move it, click its layers, and read its feature state. */
class FakeMap {
  constructor(options) {
    this.options = options;
    this.handlers = {};
    this.sources = {};
    this.state = {};
    this.center = { lng: options.center[0], lat: options.center[1] };
    this.zoom = options.zoom;
    this.bounds = { south: 37.6, west: -122.6, north: 37.9, east: -122.2 };
    this.jumpTo = vi.fn((o) => this.move({ center: { lng: o.center[0], lat: o.center[1] }, zoom: o.zoom }));
    this.easeTo = vi.fn((o) => this.move({ center: { lng: o.center[0], lat: o.center[1] }, zoom: o.zoom }));
    this.fitBounds = vi.fn(([[west, south], [east, north]]) => this.move({ bounds: { south, west, north, east } }));
    FakeMap.instances.push(this);
    setTimeout(() => this.fire('load'), 0);
  }
  static get last() { return FakeMap.instances[FakeMap.instances.length - 1]; }
  addControl() {}
  on(event, layer, fn) {
    const key = typeof layer === 'function' ? event : event + ':' + layer;
    (this.handlers[key] = this.handlers[key] || []).push(typeof layer === 'function' ? layer : fn);
  }
  off(event, fn) { this.handlers[event] = (this.handlers[event] || []).filter((h) => h !== fn); }
  fire(key, arg) { for (const h of (this.handlers[key] || []).slice()) h(arg); }
  addSource(id) { this.sources[id] = { setData: vi.fn(), getClusterExpansionZoom: () => 13 }; }
  addLayer() {}
  getSource(id) { return this.sources[id]; }
  getBounds() { const b = this.bounds; return { getSouth: () => b.south, getWest: () => b.west, getNorth: () => b.north, getEast: () => b.east }; }
  getCenter() { return this.center; }
  getZoom() { return this.zoom; }
  getCanvas() { return { style: {} }; }
  setFeatureState(target, value) { this.state[target.source + ':' + target.id] = value.highlight; }
  move({ bounds, center, zoom }) {
    if (bounds) this.bounds = bounds;
    if (center) this.center = center;
    if (zoom !== undefined) this.zoom = zoom;
    this.fire('moveend');
  }
  remove() {}
}
FakeMap.instances = [];

vi.mock('maplibre-gl', () => ({ default: { Map: FakeMap, NavigationControl: class {}, AttributionControl: class {} } }));

const MAP = { provider: 'MapLibre', styleUrl: 'https://tiles.example.test/style.json', attribution: 'Example tiles', defaultCenter: { lat: 37.7, lng: -122.4 }, defaultZoom: 10 };
const DIRECTORY = { venueLabel: 'Club', venueLabelPlural: 'Clubs', instructorLabel: 'Coach', instructorLabelPlural: 'Coaches', programs: [{ key: 'youth', label: 'Youth' }], map: MAP };

function Probe() {
  const location = useLocation();
  const navigate = useNavigate();
  return (
    <>
      <output data-testid="location">{location.search}</output>
      <button type="button" onClick={() => navigate(-1)}>Go back</button>
    </>
  );
}

function param(name) {
  return new URLSearchParams(screen.getByTestId('location').textContent).get(name);
}

function lastCall(fake, path) {
  return fake.calls.filter((c) => c.path === path).pop();
}

function directoryFake() {
  return createDirectoryFake({
    venues: [
      makeVenue('ven_1', { name: 'Example Club Downtown', programs: ['youth'], isVerified: true }),
      makeVenue('ven_2', { name: 'Example Club Uptown', lat: 37.8044, lng: -122.2712 }),
      makeVenue('ven_3', { name: 'Faraway Club', lat: 10, lng: 20 })
    ],
    programs: [{ key: 'youth', label: 'Youth' }]
  });
}

async function renderDirectory(fake, path = '/app/directory', directory = DIRECTORY) {
  const { fetchImpl } = phase4Fetch([fake]);
  await renderP4(<><DirectoryView /><Probe /></>, { fetchImpl, site: siteWith({ directory }), path, routePath: '/app/directory' });
  await waitFor(() => expect(FakeMap.last && FakeMap.last.sources.venues).toBeTruthy());
  return FakeMap.last;
}

beforeEach(() => {
  FakeMap.instances = [];
  try { window.localStorage.clear(); } catch { /* ignore */ }
});

afterEach(() => vi.restoreAllMocks());

describe('map-first venue search', () => {
  it('collects every map feature page independently of the server feature budget', async () => {
    const fake = createDirectoryFake({ venues: Array.from({ length: 101 }, (_, i) => makeVenue('ven_' + i)) });
    const map = await renderDirectory(fake);
    await waitFor(() => expect(map.sources.venues.setData.mock.calls.at(-1)?.[0].features).toHaveLength(101));
    const calls = fake.calls.filter((call) => call.path === '/directory/map/enumerate');
    expect(calls.some((call) => call.body.continuationToken === 'o:100')).toBe(true);
    expect(calls.every((call) => call.body.maxResults === 100)).toBe(true);
  });

  it('opens on the map, searches the first area, and "Search this area" re-queries after a move; back restores the previous area', async () => {
    const fake = directoryFake();
    const map = await renderDirectory(fake);
    expect(map.options.center).toEqual([-122.4, 37.7]);
    expect(map.options.zoom).toBe(10);
    await waitFor(() => expect(lastCall(fake, '/venues/enumerate')).toBeTruthy());
    expect(lastCall(fake, '/venues/enumerate').body.filters.bbox).toEqual({ south: 37.6, west: -122.6, north: 37.9, east: -122.2 });
    expect(param('bbox')).toBe('37.6,-122.6,37.9,-122.2');
    expect(param('at')).toBe('37.7,-122.4,10');
    expect(await screen.findByTestId('venue-row-ven_1')).toBeInTheDocument();
    expect(screen.queryByTestId('venue-row-ven_3')).toBeNull();
    expect(screen.queryByRole('button', { name: /Search this area/ })).toBeNull();

    act(() => map.move({ bounds: { south: 9, west: 19, north: 11, east: 21 }, center: { lat: 10, lng: 20 }, zoom: 7 }));
    await waitFor(() => expect(param('at')).toBe('10,20,7'));
    expect(param('bbox')).toBe('37.6,-122.6,37.9,-122.2');
    await userEvent.click(await screen.findByRole('button', { name: /Search this area/ }));
    await waitFor(() => expect(lastCall(fake, '/venues/enumerate').body.filters.bbox).toEqual({ south: 9, west: 19, north: 11, east: 21 }));
    expect(param('bbox')).toBe('9,19,11,21');
    expect(await screen.findByTestId('venue-row-ven_3')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Search this area/ })).toBeNull();

    await userEvent.click(screen.getByRole('button', { name: 'Go back' }));
    await waitFor(() => expect(param('bbox')).toBe('37.6,-122.6,37.9,-122.2'));
    await waitFor(() => expect(lastCall(fake, '/venues/enumerate').body.filters.bbox).toEqual({ south: 37.6, west: -122.6, north: 37.9, east: -122.2 }));
    expect(await screen.findByRole('button', { name: /Search this area/ })).toBeInTheDocument();
  });

  it('restores the viewport and area from the URL and follows a changed viewport', async () => {
    const fake = directoryFake();
    const map = await renderDirectory(fake, '/app/directory?at=10,20,6&bbox=9,19,11,21');
    expect(map.options.center).toEqual([20, 10]);
    expect(map.options.zoom).toBe(6);
    await waitFor(() => expect(lastCall(fake, '/venues/enumerate').body.filters.bbox).toEqual({ south: 9, west: 19, north: 11, east: 21 }));
    expect(await screen.findByTestId('venue-row-ven_3')).toBeInTheDocument();
    expect(window.localStorage.getItem('tk.mapSearch.venues')).toBeTruthy();
  });

  it('clusters zoom in when clicked (client clusters to their expansion zoom, server clusters to their box)', async () => {
    const fake = directoryFake();
    const map = await renderDirectory(fake);
    await waitFor(() => expect(map.handlers['click:venues-clusters']).toBeTruthy());
    act(() => map.fire('click:venues-clusters', { features: [{ properties: { cluster_id: 4 }, geometry: { coordinates: [-122.3, 37.8] } }] }));
    await waitFor(() => expect(map.easeTo).toHaveBeenCalledWith({ center: [-122.3, 37.8], zoom: 13 }));
    act(() => map.fire('click:server-clusters', { features: [{ properties: { south: 1, west: 2, north: 3, east: 4 } }] }));
    expect(map.fitBounds).toHaveBeenCalledWith([[2, 1], [4, 3]], expect.objectContaining({ padding: 40 }));
  });

  it('the advanced pane toggles and its filters reach the list and the map', async () => {
    const fake = directoryFake();
    await renderDirectory(fake);
    await waitFor(() => expect(lastCall(fake, '/venues/enumerate')).toBeTruthy());
    const toggle = screen.getByRole('button', { name: /Advanced search/ });
    expect(toggle).toHaveAttribute('aria-expanded', 'false');
    expect(screen.queryByLabelText('Verified only')).toBeNull();
    await userEvent.click(toggle);
    expect(toggle).toHaveAttribute('aria-expanded', 'true');
    expect(document.getElementById(toggle.getAttribute('aria-controls'))).not.toHaveAttribute('hidden');
    await userEvent.click(screen.getByLabelText('Verified only'));
    await waitFor(() => expect(lastCall(fake, '/venues/enumerate').body.filters).toEqual({ verifiedOnly: true, bbox: { south: 37.6, west: -122.6, north: 37.9, east: -122.2 } }));
    await waitFor(() => expect(lastCall(fake, '/directory/map/enumerate').body.filters).toEqual({ verifiedOnly: true }));
    expect(param('verified')).toBe('true');
    await userEvent.click(screen.getByRole('button', { name: 'Youth' }));
    await waitFor(() => expect(lastCall(fake, '/venues/enumerate').body.filters.programs).toEqual(['youth']));
    expect(screen.getByText('Your position is rounded to about 1 km before it is sent and is never stored.')).toBeInTheDocument();
  });

  it('a place lookup moves the map and searches the area it lands on', async () => {
    const fake = createDirectoryFake({ venues: [makeVenue('ven_3', { name: 'Faraway Club', lat: 10, lng: 20 })], geocodeCandidates: [{ label: 'Faraway, EX', lat: 10, lng: 20, bbox: { south: 9.5, west: 19.5, north: 10.5, east: 20.5 } }] });
    const map = await renderDirectory(fake);
    await waitFor(() => expect(lastCall(fake, '/venues/enumerate')).toBeTruthy());
    await userEvent.click(screen.getByRole('button', { name: /Advanced search/ }));
    await userEvent.type(screen.getByRole('searchbox', { name: 'Place' }), 'Faraway');
    await userEvent.click(screen.getByRole('button', { name: 'Find place' }));
    await userEvent.click(await screen.findByRole('button', { name: 'Faraway, EX' }));
    expect(map.fitBounds).toHaveBeenCalled();
    await waitFor(() => expect(param('bbox')).toBe('9.5,19.5,10.5,20.5'));
    expect(await screen.findByTestId('venue-row-ven_3')).toBeInTheDocument();
  });

  it('rows and markers highlight each other; a marker click selects its row and opens the place preview', async () => {
    const fake = directoryFake();
    const map = await renderDirectory(fake);
    const row = await screen.findByTestId('result-ven_1');
    fireEvent.mouseEnter(row);
    await waitFor(() => expect(map.state['venues:ven_1']).toBe(true));
    expect(row).toHaveClass('is-highlighted');
    fireEvent.mouseLeave(row);
    await waitFor(() => expect(map.state['venues:ven_1']).toBe(false));
    act(() => map.fire('mousemove:venues-points', { features: [{ properties: { id: 'ven_2' } }] }));
    await waitFor(() => expect(screen.getByTestId('result-ven_2')).toHaveClass('is-highlighted'));
    expect(screen.queryByTestId('venue-preview')).toBeNull();
    act(() => map.fire('click:venues-points', { features: [{ properties: { id: 'ven_1' } }] }));
    await waitFor(() => expect(screen.getByTestId('result-ven_1')).toHaveClass('is-selected'));
    expect(map.state['venues:ven_1']).toBe(true);
    // The preview is a small dialog with the place's essentials and a link to its full page.
    expect(await screen.findByTestId('venue-preview')).toBeInTheDocument();
    const open = await screen.findByTestId('venue-preview-open');
    expect(open.getAttribute('href')).toMatch(/^\/app\/venues\//);
    fireEvent.click(screen.getByRole('button', { name: 'Close' }));
    await waitFor(() => expect(screen.queryByTestId('venue-preview')).toBeNull());
    expect(screen.getByTestId('result-ven_1')).toHaveClass('is-selected');
  });

  it('falls back to the list without a map style, and switches to the list when the map fails', async () => {
    const fake = directoryFake();
    const { fetchImpl } = phase4Fetch([fake]);
    const view = await renderP4(<DirectoryView />, { fetchImpl, site: siteWith({ directory: { ...DIRECTORY, map: { provider: 'None' } } }), path: '/app/directory', routePath: '/app/directory' });
    expect(await screen.findByTestId('venue-row-ven_3')).toBeInTheDocument();
    expect(screen.queryByTestId('venue-search')).toBeNull();
    expect(screen.queryByRole('button', { name: /Advanced search/ })).toBeNull();
    expect(lastCall(fake, '/venues/enumerate').body.filters || {}).toEqual({});
    view.unmount();

    const failing = directoryFake();
    const map = await renderDirectory(failing);
    act(() => map.fire('error', { error: { message: 'style failed' } }));
    expect(await screen.findByText('The map could not be loaded, so the list is shown.')).toBeInTheDocument();
    expect(await screen.findByTestId('venue-row-ven_1')).toBeInTheDocument();
    expect(screen.queryByTestId('venue-search')).toBeNull();
  });
});

describe('map-first event search', () => {
  const site = siteWith({ directory: { map: MAP }, events: { categories: [] } });
  const geo = (lat, lng) => ({ venue: null, name: 'Example Hall', address: { line1: null, locality: 'Exampleville', region: 'EX', countryCode: 'US' }, addressVisibility: 'Approximate', geo: { lat, lng, precision: 'Approximate' } });
  function eventsFake() {
    return createEventsFake({
      events: [
        makeEvent('evt_1', { title: 'Park meetup', location: geo(37.75, -122.45) }),
        makeEvent('evt_2', { title: 'Online session', format: 'Online', location: null, online: { platformLabel: 'Video call', joinUrl: null } }),
        makeEvent('evt_3', { title: 'Far workshop', location: geo(10, 20) })
      ]
    });
  }

  it('opens on the map; online events join the list only with Include online and never the map', async () => {
    const fake = eventsFake();
    const { fetchImpl } = phase4Fetch([fake]);
    await renderP4(<><EventsView /><Probe /></>, { fetchImpl, site, path: '/app/events', routePath: '/app/events' });
    expect(screen.getByRole('radio', { name: 'Map' })).toHaveAttribute('aria-checked', 'true');
    await waitFor(() => expect(FakeMap.last && FakeMap.last.sources.events).toBeTruthy());
    const map = FakeMap.last;
    await waitFor(() => expect(lastCall(fake, '/events/enumerate')).toBeTruthy());
    expect(lastCall(fake, '/events/enumerate').body.filters).toEqual({ when: 'Upcoming', bbox: { south: 37.6, west: -122.6, north: 37.9, east: -122.2 } });
    expect(await screen.findByTestId('event-row-evt_1')).toBeInTheDocument();
    expect(screen.queryByTestId('event-row-evt_2')).toBeNull();
    await waitFor(() => expect(map.sources.events.setData).toHaveBeenCalled());
    expect(map.sources.events.setData.mock.calls.at(-1)[0].features.map((f) => f.id)).toEqual(['evt_1']);

    await userEvent.click(screen.getByRole('button', { name: /Advanced search/ }));
    await userEvent.click(screen.getByLabelText('Include online events'));
    await waitFor(() => expect(lastCall(fake, '/events/enumerate').body.filters.includeOnline).toBe(true));
    expect(await screen.findByTestId('event-row-evt_2')).toBeInTheDocument();
    await waitFor(() => expect(lastCall(fake, '/events/map/enumerate').body.filters).toEqual({ when: 'Upcoming' }));
    expect(param('online')).toBe('true');

    fireEvent.change(screen.getByLabelText('Starts on or after'), { target: { value: '2026-12-01' } });
    await waitFor(() => expect(lastCall(fake, '/events/map/enumerate').body.filters.startsAfterUtc).toBe('2026-12-01T00:00:00Z'));
    expect(lastCall(fake, '/events/enumerate').body.filters.startsAfterUtc).toBe('2026-12-01T00:00:00Z');

    act(() => map.move({ bounds: { south: 9, west: 19, north: 11, east: 21 }, center: { lat: 10, lng: 20 }, zoom: 7 }));
    await userEvent.click(await screen.findByRole('button', { name: /Search this area/ }));
    expect(await screen.findByTestId('event-row-evt_3')).toBeInTheDocument();
    expect(screen.queryByTestId('event-row-evt_1')).toBeNull();
  });

  it('keeps the list and calendar views, and has no map view without a map style', async () => {
    const fake = eventsFake();
    const { fetchImpl } = phase4Fetch([fake]);
    await renderP4(<EventsView />, { fetchImpl, site: siteWith({ directory: { map: { provider: 'None' } } }), path: '/app/events', routePath: '/app/events' });
    expect(screen.queryByRole('radio', { name: 'Map' })).toBeNull();
    expect(screen.getByRole('radio', { name: 'List' })).toHaveAttribute('aria-checked', 'true');
    expect(await screen.findByTestId('event-row-evt_2')).toBeInTheDocument();
    expect(FakeMap.instances.length).toBe(0);
  });
});

describe('cluster label font', () => {
  it('uses the first literal text-font of the style and never the MapLibre default', () => {
    const map = { getStyle: () => ({ layers: [{ type: 'fill', layout: {} }, { type: 'symbol', layout: { 'text-font': ['step', ['zoom'], 'x'] } }, { type: 'symbol', layout: { 'text-font': ['Noto Sans Italic'] } }, { type: 'symbol', layout: { 'text-font': ['Noto Sans Regular'] } }] }) };
    expect(styleFont(map)).toEqual(['Noto Sans Regular']);
    expect(styleFont({ getStyle: () => ({ layers: [{ type: 'symbol', layout: { 'text-font': ['Noto Sans Italic'] } }] }) })).toEqual(['Noto Sans Italic']);
    expect(styleFont({ getStyle: () => ({ layers: [] }) })).toBeNull();
    expect(styleFont({ getStyle: () => { throw new Error('not loaded'); } })).toBeNull();
  });
});

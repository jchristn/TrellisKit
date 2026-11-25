import { fireEvent, screen, waitFor, within } from '@testing-library/react-native';
import { renderRouter } from 'expo-router/testing-library';
import path from 'path';
import { createDirectoryFake, createEventsFake, makeEvent, makeVenue } from '@trelliskit/shared/testing';
import { eventMapFilters, localDateToUtc } from '../src/components/events/EventsMapSearch';
import { placeTarget } from '../src/components/maps/PlaceLookup';
import { nearestSnap, snapHeights } from '../src/components/maps/ResultsSheet';
import { drawableFeatures } from '../src/components/maps/SearchMap';
import { viewportFromBbox } from '../src/components/maps/useMapSearch';
import { venueMapFilters } from '../src/components/places/PlacesMapSearch';
import { resetMapModules } from '../src/native/maps';
import { installFakeServer, makeUser, resetEnvironment, signedInAs } from '../test-utils/fakeServer';
import { propertyWithFeatures } from '../test-utils/property';
import { showMapSection } from '../test-utils/navigation';

const mockCamera = { fitBounds: jest.fn(), flyTo: jest.fn(), easeTo: jest.fn(), jumpTo: jest.fn() };

jest.mock('@maplibre/maplibre-react-native', () => {
  const React = require('react');
  const { View } = require('react-native');
  const Map = ({ children, testID, onRegionDidChange, onDidFailLoadingMap }) => (
    <View testID={testID} onRegionDidChange={onRegionDidChange} onDidFailLoadingMap={onDidFailLoadingMap}>
      {children}
    </View>
  );
  const Camera = React.forwardRef(function Camera(_props, ref) {
    React.useImperativeHandle(ref, () => mockCamera);
    return null;
  });
  const Marker = ({ children }) => <View>{children}</View>;
  return { Map, Camera, Marker };
});

const APP_DIR = path.resolve(__dirname, '../app');
const MAP = { provider: 'MapLibre', styleUrl: 'https://tiles.example.test/style.json', attribution: 'Map data', defaultCenter: { lat: 37.77, lng: -122.42 }, defaultZoom: 10, maxZoom: 18, maxFeatures: 300 };
const withMap = (map = {}) => ({ ...propertyWithFeatures({}), directory: { map: { ...MAP, ...map } } });

const downtown = makeVenue('ven_1', { name: 'Downtown Hall', lat: 37.7749, lng: -122.4194 });
const uptown = makeVenue('ven_2', { name: 'Uptown Room', lat: 37.8, lng: -122.3 });
const placed = (id, title, lat, lng, extra = {}) => makeEvent(id, { title, location: { venue: null, name: 'Example Hall', address: { locality: 'Exampleville', countryCode: 'US' }, addressVisibility: 'Exact', geo: { lat, lng, precision: 'Exact' }, distanceKm: null }, ...extra });

function chain(...fakes) {
  return async (call) => {
    for (const fake of fakes) {
      const result = await fake.handle({ method: call.method, path: call.path, query: call.query, body: call.body });
      if (result !== undefined) return result;
    }
    return undefined;
  };
}

function serve(propertyBody) {
  const dir = createDirectoryFake({ venues: [downtown, uptown], settings: { map: { maxFeatures: propertyBody.directory?.map?.maxFeatures || 500 } } });
  const evt = createEventsFake({ events: [placed('evt_1', 'Open day', 37.78, -122.41), placed('evt_2', 'Far meetup', 10, 10), makeEvent('evt_3', { title: 'Online talk', format: 'Online', location: null })] });
  const server = installFakeServer({ 'GET /property': propertyBody, ...signedInAs(makeUser()) }, { fallback: chain(dir, evt) });
  return server;
}

const region = (west, south, east, north, zoom = 11) => ({ nativeEvent: { bounds: [west, south, east, north], zoom } });
const lastBody = (server, route) => server.called(route).at(-1).body;

describe('map search helpers', () => {
  test('filters, dates, places, snaps, viewports, and drawable features', () => {
    expect(venueMapFilters({ programs: ['open'], visitorPolicy: [], verifiedOnly: true, includeClosed: true })).toEqual({ programs: ['open'], verifiedOnly: true });
    expect(localDateToUtc('2027-02-30')).toBeNull();
    expect(localDateToUtc('')).toBeNull();
    expect(new Date(localDateToUtc('2027-01-31', 1)).getDate()).toBe(1);
    const typed = eventMapFilters({ when: 'Past', fromDate: '2027-01-01', toDate: 'nope', category: ['meetup'], hasSpots: true, rsvpStatus: ['Going'] }, false);
    expect(typed).toEqual({ when: 'Past', startsAfterUtc: localDateToUtc('2027-01-01'), category: ['meetup'], hasSpots: true });
    expect(eventMapFilters({}).when).toBe('Upcoming');
    expect(placeTarget({ lat: 1, lng: 2, bbox: null })).toEqual({ lat: 1, lng: 2, zoom: 11 });
    expect(placeTarget({ lat: 1, lng: 2, bbox: { south: 0, west: 1, north: 2, east: 3 } })).toEqual({ bbox: { south: 0, west: 1, north: 2, east: 3 } });
    const heights = snapHeights(800);
    expect(nearestSnap(heights, heights[1] + 10)).toBe(1);
    expect(nearestSnap(heights, heights[1], -2)).toBe(2);
    expect(nearestSnap(heights, heights[0], 2)).toBe(0);
    expect(viewportFromBbox({ south: -10, west: 170, north: 10, east: -170 }, 4)).toEqual({ lat: 0, lng: 180, zoom: 4 });
    expect(drawableFeatures([{ kind: 'Point', event: { id: 'evt_1', geo: { lat: 1, lng: 2 } } }, { kind: 'Point', event: { id: 'evt_2', geo: null } }, { kind: 'Cluster', count: 2, lat: 1, lng: 1 }, null]).length).toBe(2);
  });
});

describe('map-first search', () => {
  beforeEach(() => {
    resetEnvironment();
    resetMapModules();
    Object.values(mockCamera).forEach((fn) => fn.mockClear());
  });
  afterEach(() => delete global.fetch);

  test('venues: the first view is searched, a moved camera offers Search this area, and the pill re-queries by the visible box', async () => {
    const server = serve(withMap());
    await renderRouter(APP_DIR, { initialUrl: '/map' });
    const map = await screen.findByTestId('places-map-maplibre');
    expect(screen.getByTestId('places-sheet-handle')).toHaveProp('accessibilityState', { expanded: true });
    expect(server.called('POST /venues/enumerate')).toHaveLength(0);
    await fireEvent(map, 'regionDidChange', region(-122.6, 37.6, -122.2, 37.9));
    await waitFor(() => expect(server.called('POST /venues/enumerate').length).toBe(1));
    expect(lastBody(server, 'POST /venues/enumerate').filters).toEqual({ bbox: { south: 37.6, west: -122.6, north: 37.9, east: -122.2 } });
    expect(await screen.findByTestId('venue-ven_1')).toBeOnTheScreen();
    expect(screen.queryByTestId('places-search-area')).toBeNull();
    await fireEvent(map, 'regionDidChange', region(-122.5, 37.75, -122.35, 37.85, 13));
    expect(await screen.findByTestId('places-search-area')).toHaveTextContent('Search this area');
    expect(server.called('POST /venues/enumerate')).toHaveLength(1);
    await fireEvent.press(screen.getByTestId('places-search-area'));
    await waitFor(() => expect(server.called('POST /venues/enumerate').length).toBe(2));
    expect(lastBody(server, 'POST /venues/enumerate').filters.bbox).toEqual({ south: 37.75, west: -122.5, north: 37.85, east: -122.35 });
    await waitFor(() => expect(lastBody(server, 'POST /directory/map/enumerate')).toMatchObject({ bbox: { south: 37.75, west: -122.5, north: 37.85, east: -122.35 }, zoom: 13 }));
    await waitFor(() => expect(screen.queryByTestId('places-search-area')).toBeNull());
    expect(screen.getByTestId('venue-ven_1')).toBeOnTheScreen();
    expect(screen.queryByTestId('venue-ven_2')).toBeNull();
  });

  test('a cluster tap zooms to its box and searches where the camera lands', async () => {
    const server = serve(withMap({ maxFeatures: 1 }));
    await renderRouter(APP_DIR, { initialUrl: '/map' });
    const map = await screen.findByTestId('places-map-maplibre');
    await fireEvent(map, 'regionDidChange', region(-123, 37, -122, 38, 8));
    const cluster = await screen.findByLabelText('2 Venues here. Tap to zoom in.');
    await fireEvent.press(cluster);
    expect(mockCamera.fitBounds).toHaveBeenCalledWith([-122.4194, 37.7749, -122.3, 37.8], { duration: 300 });
    await fireEvent(map, 'regionDidChange', region(-122.45, 37.76, -122.28, 37.81, 12));
    await waitFor(() => expect(lastBody(server, 'POST /venues/enumerate').filters.bbox).toEqual({ south: 37.76, west: -122.45, north: 37.81, east: -122.28 }));
    expect(screen.queryByTestId('places-search-area')).toBeNull();
  });

  test('the advanced search sheet sends its filters to the list and the map; a place moves the camera and searches there', async () => {
    const server = serve(withMap());
    await renderRouter(APP_DIR, { initialUrl: '/map' });
    const map = await screen.findByTestId('places-map-maplibre');
    await fireEvent(map, 'regionDidChange', region(-122.6, 37.6, -122.2, 37.9));
    await screen.findByTestId('venue-ven_1');
    await fireEvent.press(screen.getByTestId('places-filters-button'));
    const sheet = await screen.findByTestId('places-filters-sheet');
    await fireEvent(within(sheet).getByTestId('places-verified'), 'valueChange', true);
    await fireEvent(within(sheet).getByTestId('places-closed'), 'valueChange', true);
    await fireEvent.press(within(sheet).getByTestId('places-visitors-Welcome'));
    await fireEvent.changeText(within(sheet).getByTestId('places-filters-panel-search'), 'hall');
    await fireEvent.press(within(sheet).getByTestId('places-filters-panel-apply'));
    await waitFor(() => expect(lastBody(server, 'POST /venues/enumerate')).toMatchObject({ search: 'hall', filters: { visitorPolicy: ['Welcome'], verifiedOnly: true, includeClosed: true, bbox: { south: 37.6, west: -122.6, north: 37.9, east: -122.2 } } }));
    await waitFor(() => expect(lastBody(server, 'POST /directory/map/enumerate')).toMatchObject({ search: 'hall', filters: { visitorPolicy: ['Welcome'], verifiedOnly: true } }));
    expect(lastBody(server, 'POST /directory/map/enumerate').filters.includeClosed).toBeUndefined();

    await fireEvent.changeText(screen.getByTestId('places-place'), 'Exampleville');
    await fireEvent(screen.getByTestId('places-place'), 'submitEditing');
    await fireEvent.press(await screen.findByTestId('places-place-0'));
    expect(mockCamera.flyTo).toHaveBeenCalledWith({ center: [-122.42, 37.77], zoom: 11, duration: 300 });
    await fireEvent(map, 'regionDidChange', region(-122.5, 37.7, -122.3, 37.85));
    await waitFor(() => expect(lastBody(server, 'POST /venues/enumerate').filters.bbox).toEqual({ south: 37.7, west: -122.5, north: 37.85, east: -122.3 }));
  });

  test('events: map points for the area, a point tap highlights its row, and Include online adds online events to the list only', async () => {
    const server = serve(withMap());
    const router = await renderRouter(APP_DIR, { initialUrl: '/map' });
    await showMapSection('events');
    const map = await screen.findByTestId('screen-events-map-maplibre');
    await fireEvent(map, 'regionDidChange', region(-122.6, 37.6, -122.2, 37.9));
    await waitFor(() => expect(server.called('POST /events/enumerate').length).toBeGreaterThan(0));
    expect(lastBody(server, 'POST /events/enumerate')).toMatchObject({ ordering: 'StartAscending', filters: { when: 'Upcoming', bbox: { south: 37.6, west: -122.6, north: 37.9, east: -122.2 } } });
    expect(await screen.findByTestId('event-evt_1')).toBeOnTheScreen();
    expect(screen.queryByTestId('event-evt_2')).toBeNull();
    expect(screen.queryByTestId('event-evt_3')).toBeNull();
    await waitFor(() => expect(server.called('POST /events/map/enumerate').length).toBeGreaterThan(0));
    await fireEvent.press(await screen.findByTestId('map-point-evt_1'));
    await waitFor(() => expect(screen.getByTestId('event-evt_1')).toHaveProp('accessibilityState', expect.objectContaining({ selected: true })));

    await fireEvent.press(screen.getByTestId('screen-events-filters-button'));
    const sheet = await screen.findByTestId('screen-events-filters-sheet');
    await fireEvent(within(sheet).getByTestId('screen-events-include-online'), 'valueChange', true);
    await fireEvent.press(within(sheet).getByTestId('screen-events-rsvp-Going'));
    await fireEvent.press(within(sheet).getByTestId('screen-events-rsvp-Going'));
    await fireEvent.press(within(sheet).getByTestId('screen-events-when-All'));
    await fireEvent.press(within(sheet).getByTestId('screen-events-filters-panel-apply'));
    await waitFor(() => expect(lastBody(server, 'POST /events/enumerate').filters).toMatchObject({ when: 'All', includeOnline: true }));
    expect(await screen.findByTestId('event-evt_3')).toBeOnTheScreen();
    await waitFor(() => expect(lastBody(server, 'POST /events/map/enumerate').filters).toEqual({ when: 'All' }));
    await fireEvent.press(screen.getByTestId('map-point-evt_1'));
    await waitFor(() => expect(router.getPathname()).toBe('/events/evt_1'));
  });

  test('without a map provider both halves are lists; a map that fails to load falls back to the list', async () => {
    const server = serve(propertyWithFeatures({}));
    await renderRouter(APP_DIR, { initialUrl: '/map' });
    expect(await screen.findByTestId('venue-ven_1')).toBeOnTheScreen();
    expect(screen.queryByTestId('places-map-maplibre')).toBeNull();
    expect(screen.queryByTestId('places-sheet')).toBeNull();
    expect((lastBody(server, 'POST /venues/enumerate').filters || {}).bbox).toBeUndefined();
    expect(screen.getByTestId('places-near-me')).toBeOnTheScreen();
    await showMapSection('events');
    expect(await screen.findByTestId('events-mode')).toBeOnTheScreen();
    expect(screen.queryByTestId('screen-events-map-maplibre')).toBeNull();
    expect(lastBody(server, 'POST /events/enumerate').filters.bbox).toBeUndefined();
  });

  test('a style that fails to load shows the list with a note', async () => {
    serve(withMap());
    await renderRouter(APP_DIR, { initialUrl: '/map' });
    await fireEvent(await screen.findByTestId('places-map-maplibre'), 'didFailLoadingMap');
    expect(await screen.findByTestId('places-map-unavailable')).toHaveTextContent('The map is not available, so Venues are shown as a list.');
    expect(await screen.findByTestId('venue-ven_1')).toBeOnTheScreen();
  });
});

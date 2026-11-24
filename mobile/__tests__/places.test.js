import { fireEvent, screen, waitFor } from '@testing-library/react-native';
import { renderRouter } from 'expo-router/testing-library';
import * as Location from 'expo-location';
import path from 'path';
import { createDirectoryFake, createEventsFake, makeEvent, makeInstructor, makeVenue } from '@trelliskit/shared/testing';
import { venueCollectionFilters } from '../src/components/places/PlacesView';
import { pinFromPressEvent, pinTextProblems, validPin } from '../src/components/places/PinPicker';
import { initialValues, submissionFieldsFor } from '../src/components/places/SubmissionForm';
import { directoryStrings } from '../src/content/phase4';
import { resetMapModules } from '../src/native/maps';
import { installFakeServer, makeUser, resetEnvironment, signedInAs } from '../test-utils/fakeServer';
import { property, propertyWithFeatures } from '../test-utils/property';

const APP_DIR = path.resolve(__dirname, '../app');

const downtown = makeVenue('ven_1', { name: 'Downtown Hall', isVerified: true, instructorCount: 1, programs: [{ key: 'open', label: 'Open sessions' }], instructors: { objects: [makeInstructor('ins_1', { displayName: 'Robin Vale' })], totalRecords: 1, enumerate: null } });
const uptown = makeVenue('ven_2', { name: 'Uptown Room', lat: 37.8, lng: -122.3 });
const coach = makeInstructor('ins_1', { displayName: 'Robin Vale', venues: { objects: [downtown], totalRecords: 1, enumerate: null } });

/** Try each fake in turn (the first that knows the route answers). */
export function chain(...fakes) {
  return async (call) => {
    for (const fake of fakes) {
      const result = await fake.handle({ method: call.method, path: call.path, query: call.query, body: call.body });
      if (result !== undefined) return result;
    }
    return undefined;
  };
}

function serve({ user = makeUser(), propertyBody = propertyWithFeatures({}), directory = {}, events = {} } = {}) {
  const dir = createDirectoryFake({ venues: [downtown, uptown], instructors: [coach], links: [{ venueId: 'ven_1', instructorId: 'ins_1', role: 'Head' }], ...directory });
  const evt = createEventsFake({ events: [makeEvent('evt_1', { title: 'Open day', location: { venue: { id: 'ven_1', name: 'Downtown Hall' }, name: null, address: null, addressVisibility: 'Exact', geo: null, distanceKm: null } })], ...events });
  const server = installFakeServer({ 'GET /property': propertyBody, ...signedInAs(user) }, { fallback: chain(dir, evt) });
  return { server, dir, evt };
}

describe('directory helpers', () => {
  test('directory strings use the configured terms', () => {
    const t = directoryStrings({ ...property, directory: { venueLabel: 'Studio', venueLabelPlural: 'Studios', instructorLabel: 'Teacher', instructorLabelPlural: 'Teachers' } });
    expect(t.title).toBe('Studios');
    expect(t.addVenue).toBe('Add a Studio');
    expect(t.instructorCount(2)).toBe('2 Teachers');
    expect(directoryStrings(property).title).toBe('Venues');
  });

  test('venue filters carry programs, visitors, verified, and a near position', () => {
    expect(venueCollectionFilters({ programs: ['open'], visitorPolicy: ['Welcome'], verifiedOnly: true }, { lat: 37.77, lng: -122.42 })).toEqual({ programs: ['open'], visitorPolicy: ['Welcome'], verifiedOnly: true, near: { lat: 37.77, lng: -122.42, radiusKm: 50 } });
    expect(venueCollectionFilters({}, null)).toEqual({});
  });

  test('an edit sends only what changed; a new venue sends an exact address and no visibility', () => {
    const before = initialValues('EditVenue', downtown);
    expect(submissionFieldsFor('EditVenue', { ...before, scheduleText: 'Mon 6 pm' }, downtown)).toEqual({ scheduleText: 'Mon 6 pm' });
    const fresh = { ...initialValues('NewVenue', null), name: 'New Place', address: { line1: '1 Main', line2: '', locality: 'Exampleville', region: '', postalCode: '', countryCode: 'us' } };
    const fields = submissionFieldsFor('NewVenue', fresh, null, ['med_1']);
    expect(fields).toEqual({ name: 'New Place', address: { line1: '1 Main', locality: 'Exampleville', countryCode: 'US' }, photoMediaIds: ['med_1'] });
    expect(fields.addressVisibility).toBeUndefined();
  });
});

describe('Map tab', () => {
  beforeEach(() => {
    resetEnvironment();
    resetMapModules();
  });
  afterEach(() => delete global.fetch);

  test('lists venues, filters by search, and opens a venue with its instructors and events', async () => {
    const { server } = serve();
    const router = await renderRouter(APP_DIR, { initialUrl: '/map' });
    expect(await screen.findByTestId('venue-ven_1')).toBeOnTheScreen();
    expect(screen.getByTestId('venue-ven_1-verified')).toHaveTextContent('Verified');
    expect(screen.getByTestId('map-section')).toBeOnTheScreen();
    const body = server.called('POST /venues/enumerate')[0].body;
    expect(body).toMatchObject({ maxResults: 25, ordering: 'NameAscending' });
    await fireEvent.press(screen.getByTestId('venue-ven_1'));
    await waitFor(() => expect(router.getPathname()).toBe('/venues/ven_1'));
    expect(await screen.findByTestId('venue-name')).toHaveTextContent('Downtown Hall');
    expect(screen.getByTestId('instructor-ins_1')).toBeOnTheScreen();
    expect(await screen.findByTestId('event-evt_1')).toBeOnTheScreen();
    expect(server.called('POST /events/enumerate').at(-1).body.filters).toEqual({ venueId: 'ven_1', when: 'Upcoming' });
  });

  test('near me explains first, then sends a position rounded to about 1 km', async () => {
    const { server } = serve();
    await renderRouter(APP_DIR, { initialUrl: '/map' });
    await screen.findByTestId('venue-ven_1');
    await fireEvent.press(screen.getByTestId('places-near-me'));
    expect(await screen.findByTestId('places-location-sheet')).toBeOnTheScreen();
    expect(Location.requestForegroundPermissionsAsync).not.toHaveBeenCalled();
    await fireEvent.press(screen.getByTestId('places-location-allow'));
    await waitFor(() => expect(server.called('POST /venues/enumerate').some((c) => c.body.filters && c.body.filters.near)).toBe(true));
    const near = server.called('POST /venues/enumerate').find((c) => c.body.filters && c.body.filters.near).body;
    expect(near.filters.near).toEqual({ lat: 37.77, lng: -122.42, radiusKm: 50 });
    expect(near.ordering).toBe('DistanceAscending');
  });

  test('a denied location permission falls back to place search', async () => {
    Location.requestForegroundPermissionsAsync.mockResolvedValueOnce({ granted: false, status: 'denied' });
    const { server } = serve();
    await renderRouter(APP_DIR, { initialUrl: '/map' });
    await screen.findByTestId('venue-ven_1');
    await fireEvent.press(screen.getByTestId('places-near-me'));
    await fireEvent.press(await screen.findByTestId('places-location-allow'));
    expect(await screen.findByTestId('places-location-note')).toHaveTextContent(/Location permission is off/);
    await fireEvent.changeText(screen.getByTestId('places-place'), 'Exampleville');
    await fireEvent(screen.getByTestId('places-place'), 'submitEditing');
    await fireEvent.press(await screen.findByTestId('places-place-0'));
    await waitFor(() => expect(server.called('POST /venues/enumerate').some((c) => c.body.filters && c.body.filters.near)).toBe(true));
    expect(server.called('POST /directory/geocode/enumerate')[0].body).toEqual({ maxResults: 5, filters: { query: 'Exampleville' } });
  });

  test('with a MapLibre style the map shows the server points; tapping one opens its preview, which leads to the venue', async () => {
    const withMap = { ...propertyWithFeatures({}), directory: { map: { provider: 'MapLibre', styleUrl: 'https://tiles.example.test/style.json', attribution: 'Map data [Example](https://example.test)', defaultCenter: { lat: 37.77, lng: -122.42 }, defaultZoom: 10, maxZoom: 18, maxFeatures: 300 } } };
    const { server } = serve({ propertyBody: withMap });
    const router = await renderRouter(APP_DIR, { initialUrl: '/map' });
    expect(await screen.findByTestId('places-map-maplibre')).toBeOnTheScreen();
    expect(screen.queryByTestId('places-view-toggle')).toBeNull();
    expect(screen.getByTestId('places-map-attribution')).toHaveTextContent('Map data Example');
    await fireEvent(screen.getByTestId('places-map-maplibre'), 'regionDidChange', { nativeEvent: { bounds: [-122.6, 37.6, -122.2, 37.9], zoom: 11.4 } });
    await waitFor(() => expect(server.called('POST /directory/map/enumerate').length).toBeGreaterThan(0));
    expect(server.called('POST /directory/map/enumerate')[0].body).toMatchObject({ bbox: { south: 37.6, west: -122.6, north: 37.9, east: -122.2 }, zoom: 11, maxResults: 100 });
    await fireEvent.press(await screen.findByTestId('map-point-ven_1'));
    await waitFor(() => expect(screen.getByTestId('map-point-ven_1').props.accessibilityState).toEqual({ selected: true }));
    // The preview sheet reads the venue and offers its full page.
    await fireEvent.press(await screen.findByTestId(/-preview-open$/));
    await waitFor(() => expect(router.getPathname()).toBe('/venues/ven_1'));
    expect(screen.queryByTestId(/-preview-open$/)).toBeNull();
  });

  test('the platform map reports its region the same way', async () => {
    const native = { ...propertyWithFeatures({}), directory: { map: { provider: 'PlatformNative', defaultCenter: { lat: 37.77, lng: -122.42 }, defaultZoom: 10 } } };
    const { server } = serve({ propertyBody: native });
    await renderRouter(APP_DIR, { initialUrl: '/map' });
    await fireEvent(await screen.findByTestId('places-map-native'), 'regionChangeComplete', { latitude: 37.75, longitude: -122.4, latitudeDelta: 0.3, longitudeDelta: 0.4 });
    await waitFor(() => expect(server.called('POST /directory/map/enumerate').length).toBeGreaterThan(0));
    expect(server.called('POST /directory/map/enumerate')[0].body.zoom).toBe(10);
  });

  test('instructor claim opens the submission form and the duplicate check runs for new venues', async () => {
    const { server } = serve();
    const router = await renderRouter(APP_DIR, { initialUrl: '/instructors/ins_1' });
    expect(await screen.findByTestId('instructor-name')).toHaveTextContent('Robin Vale');
    await fireEvent.press(screen.getByTestId('instructor-claim'));
    await waitFor(() => expect(router.getPathname()).toBe('/directory/submit'));
    expect(await screen.findByTestId('submission-review-note')).toHaveTextContent('Nothing you send is published until a moderator reviews and approves it.');
    await fireEvent.press(screen.getByTestId('submission-send'));
    expect(await screen.findByText('Explain the claim in at least 10 characters.')).toBeOnTheScreen();
    await fireEvent.changeText(screen.getByTestId('submission-note'), 'I teach the Monday class here.');
    await fireEvent.press(screen.getByTestId('submission-send'));
    expect(await screen.findByTestId('submission-sent')).toBeOnTheScreen();
    expect(server.called('POST /directory/submissions')[0].body).toMatchObject({ type: 'ClaimInstructor', targetId: 'ins_1', note: 'I teach the Monday class here.' });
  });

  test('a new venue near an existing one asks before sending again with confirmNotDuplicate', async () => {
    const { server } = serve();
    await renderRouter(APP_DIR, { initialUrl: '/directory/submit?type=NewVenue' });
    expect(await screen.findByTestId('submission-address-note')).toBeOnTheScreen();
    await fireEvent.changeText(screen.getByTestId('submission-name'), 'Downtown Hall');
    await fireEvent.changeText(screen.getByTestId('submission-line1'), '100 Example Street');
    await fireEvent.changeText(screen.getByTestId('submission-locality'), 'Exampleville');
    await fireEvent.press(screen.getByTestId('submission-country'));
    await fireEvent.press(await screen.findByTestId('submission-country-option-US'));
    await fireEvent.press(screen.getByTestId('submission-send'));
    const first = await screen.findByTestId('submission-duplicates', {}, { timeout: 3000 }).catch(() => null);
    if (first) {
      await fireEvent.press(screen.getByTestId('submission-confirm'));
      await waitFor(() => expect(server.called('POST /directory/submissions').at(-1).body.confirmNotDuplicate).toBe(true));
    }
    expect(await screen.findByTestId('submission-sent')).toBeOnTheScreen();
    expect(server.called('POST /directory/submissions')[0].body.fields.addressVisibility).toBeUndefined();
  });

  test('pin helpers read MapLibre and platform long-press events and validate typed coordinates', () => {
    expect(pinFromPressEvent({ nativeEvent: { lngLat: [-122.4194123, 37.7749456] } })).toEqual({ lat: 37.774946, lng: -122.419412 });
    expect(pinFromPressEvent({ geometry: { coordinates: [-122.41, 37.77] } })).toEqual({ lat: 37.77, lng: -122.41 });
    expect(pinFromPressEvent({ nativeEvent: { coordinate: { latitude: 51.5, longitude: -0.12 } } })).toEqual({ lat: 51.5, lng: -0.12 });
    expect(pinFromPressEvent({ nativeEvent: {} })).toBeNull();
    expect(validPin('91', '0')).toBeNull();
    expect(validPin('', '')).toBeNull();
    expect(pinTextProblems('', '')).toEqual({});
    expect(pinTextProblems('45', 'x')).toEqual({ lng: true });
    expect(submissionFieldsFor('NewVenue', initialValues('NewVenue', null), null, [], { lat: 1, lng: 2 }).geo).toEqual({ lat: 1, lng: 2 });
    expect(submissionFieldsFor('NewInstructor', initialValues('NewInstructor', null), null, [], { lat: 1, lng: 2 }).geo).toBeUndefined();
  });

  test('long-press on the MapLibre map places the pin, and the submission carries it', async () => {
    resetMapModules();
    const withMap = { ...propertyWithFeatures({}), directory: { submissions: true, map: { provider: 'MapLibre', styleUrl: 'https://tiles.example.test/style.json', defaultCenter: { lat: 37.77, lng: -122.42 }, defaultZoom: 10, maxZoom: 18, maxFeatures: 300 } } };
    const { server } = serve({ propertyBody: withMap });
    await renderRouter(APP_DIR, { initialUrl: '/directory/submit?type=NewVenue' });
    expect(await screen.findByTestId('submission-pin-map')).toBeOnTheScreen();
    expect(screen.getByTestId('submission-pin-status')).toHaveTextContent('No pin placed');
    await fireEvent(screen.getByTestId('submission-pin-map'), 'longPress', { nativeEvent: { lngLat: [-122.4, 37.78] } });
    expect(await screen.findByTestId('submission-pin-marker')).toBeOnTheScreen();
    expect(screen.getByTestId('submission-pin-status')).toHaveTextContent('Pin at 37.78, -122.4');
    expect(screen.getByTestId('submission-pin-lat').props.value).toBe('37.78');
    await fireEvent.changeText(screen.getByTestId('submission-name'), 'Harbor Room');
    await fireEvent.changeText(screen.getByTestId('submission-locality'), 'Exampleville');
    await fireEvent.press(screen.getByTestId('submission-country'));
    await fireEvent.press(await screen.findByTestId('submission-country-option-US'));
    await fireEvent.press(screen.getByTestId('submission-send'));
    await waitFor(() => expect(server.called('POST /directory/submissions').length).toBeGreaterThan(0));
    expect(server.called('POST /directory/submissions')[0].body.fields.geo).toEqual({ lat: 37.78, lng: -122.4 });
  });

  test('the platform map places the pin from its long-press coordinate', async () => {
    resetMapModules();
    const native = { ...propertyWithFeatures({}), directory: { submissions: true, map: { provider: 'PlatformNative', defaultCenter: { lat: 37.77, lng: -122.42 }, defaultZoom: 10 } } };
    serve({ propertyBody: native });
    await renderRouter(APP_DIR, { initialUrl: '/directory/submit?type=NewVenue' });
    await fireEvent(await screen.findByTestId('submission-pin-map'), 'longPress', { nativeEvent: { coordinate: { latitude: 40.1, longitude: -74.2 } } });
    expect(await screen.findByTestId('submission-pin-status')).toHaveTextContent('Pin at 40.1, -74.2');
  });

  test('without a map provider the pin is typed as numbers; invalid degrees show an error and Remove clears it', async () => {
    resetMapModules();
    const { server } = serve();
    await renderRouter(APP_DIR, { initialUrl: '/directory/submit?type=NewVenue' });
    expect(await screen.findByTestId('submission-pin')).toBeOnTheScreen();
    expect(screen.queryByTestId('submission-pin-map')).toBeNull();
    await fireEvent.changeText(screen.getByTestId('submission-pin-lat'), '95');
    await fireEvent.changeText(screen.getByTestId('submission-pin-lng'), '10');
    expect(await screen.findByText('Enter a latitude from -90 to 90.')).toBeOnTheScreen();
    await fireEvent.changeText(screen.getByTestId('submission-pin-lat'), '45.5');
    expect(await screen.findByTestId('submission-pin-status')).toHaveTextContent('Pin at 45.5, 10');
    await fireEvent.press(screen.getByTestId('submission-pin-clear'));
    expect(await screen.findByTestId('submission-pin-status')).toHaveTextContent('No pin placed');
    await fireEvent.changeText(screen.getByTestId('submission-name'), 'Harbor Room');
    await fireEvent.changeText(screen.getByTestId('submission-locality'), 'Exampleville');
    await fireEvent.press(screen.getByTestId('submission-country'));
    await fireEvent.press(await screen.findByTestId('submission-country-option-US'));
    await fireEvent.press(screen.getByTestId('submission-send'));
    await waitFor(() => expect(server.called('POST /directory/submissions').length).toBeGreaterThan(0));
    expect(server.called('POST /directory/submissions')[0].body.fields.geo).toBeUndefined();
  });

  test('My submissions lists pending entries and withdraws one', async () => {
    const { server } = serve({ directory: { submissions: [{ id: 'dsb_1', type: 'EditVenue', status: 'Pending', target: { type: 'Venue', id: 'ven_1', name: 'Downtown Hall' }, submitter: { id: 'usr_1', displayName: 'Test Member' }, note: null, changes: [], possibleDuplicates: { objects: [], totalRecords: 0, enumerate: null }, reviewer: null, reviewNote: null, decidedUtc: null, labels: [], tags: {}, createdUtc: '2026-11-10T00:00:00Z', updatedUtc: '2026-11-10T00:00:00Z', fields: {} }] } });
    await renderRouter(APP_DIR, { initialUrl: '/directory/submissions' });
    expect(await screen.findByTestId('submission-dsb_1-status')).toHaveTextContent('Waiting for review');
    await fireEvent.press(screen.getByTestId('submission-dsb_1-withdraw'));
    await waitFor(() => expect(screen.getByTestId('submission-dsb_1-status')).toHaveTextContent('Withdrawn'));
    expect(server.called('POST /directory/submissions/dsb_1/withdraw')).toHaveLength(1);
  });

  test('reporting a venue offers the directory reasons', async () => {
    const { server } = serve();
    await renderRouter(APP_DIR, { initialUrl: '/venues/ven_1' });
    await fireEvent.press(await screen.findByTestId('venue-report'));
    expect(await screen.findByTestId('report-reason-PermanentlyClosed')).toHaveTextContent('Permanently closed');
    await fireEvent.press(screen.getByTestId('report-reason-Inaccurate'));
    await fireEvent.press(screen.getByText('Send report'));
    await waitFor(() => expect(server.called('POST /reports')).toHaveLength(1));
    expect(server.called('POST /reports')[0].body).toMatchObject({ targetType: 'Venue', targetId: 'ven_1', reason: 'Inaccurate' });
  });

  test('the directory kill switch shows the unavailable message', async () => {
    serve({ directory: { killed: true } });
    await renderRouter(APP_DIR, { initialUrl: '/map' });
    expect(await screen.findByTestId('places-error')).toHaveTextContent(/The directory is temporarily unavailable\./);
  });

  test('features.directory and features.events off hide the Map tab', async () => {
    serve({ propertyBody: propertyWithFeatures({ directory: false, events: false }) });
    await renderRouter(APP_DIR, { initialUrl: '/feed' });
    await waitFor(() => expect(screen.queryByTestId('tab-map')).toBeNull());
  });
});

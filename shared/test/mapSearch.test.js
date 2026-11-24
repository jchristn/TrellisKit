import { test } from 'node:test';
import assert from 'node:assert/strict';
import { ApiClient, bboxChanged, formatBbox, formatViewport, isInBox, mapFeatureItem, mapFeaturesToGeoJson, parseBbox, parseViewport } from '../src/index.js';
import { createEventsFake, makeEvent } from '../src/testing/index.js';

test('viewport and bbox URL forms round trip and reject bad input', () => {
  assert.deepEqual(parseViewport('37.7749,-122.4194,11.5'), { lat: 37.7749, lng: -122.4194, zoom: 11.5 });
  assert.equal(formatViewport({ lat: 37.774929, lng: -122.419416, zoom: 11.123 }), '37.7749,-122.4194,11.12');
  assert.equal(parseViewport('91,0,3'), null);
  assert.equal(parseViewport('1,2'), null);
  assert.equal(parseViewport(null), null);
  assert.equal(formatViewport(null), '');
  assert.deepEqual(parseBbox('-17,179,-15,-179'), { south: -17, west: 179, north: -15, east: -179 });
  assert.equal(parseBbox('10,0,5,1'), null, 'south above north');
  assert.equal(parseBbox('1,2,3'), null);
  assert.equal(formatBbox({ south: 1.234567, west: 2, north: 3, east: 4 }), '1.2346,2,3,4');
  assert.equal(formatBbox(null), '');
});

test('isInBox handles the antimeridian and bboxChanged ignores small moves', () => {
  assert.equal(isInBox(-16, 179.6, { south: -17, west: 179, north: -15, east: -179 }), true);
  assert.equal(isInBox(-16, -179.6, { south: -17, west: 179, north: -15, east: -179 }), true);
  assert.equal(isInBox(-16, 0, { south: -17, west: 179, north: -15, east: -179 }), false);
  assert.equal(isInBox(null, 0, { south: -1, west: -1, north: 1, east: 1 }), false);
  const box = { south: 0, west: 0, north: 10, east: 10 };
  assert.equal(bboxChanged(box, { south: 0.1, west: 0.1, north: 10.1, east: 10.1 }), false);
  assert.equal(bboxChanged(box, { south: 2, west: 2, north: 12, east: 12 }), true);
  assert.equal(bboxChanged(box, null), true);
  assert.equal(bboxChanged(null, null), false);
});

test('map features: venue and event points become GeoJSON; clusters are skipped', () => {
  const features = [
    { kind: 'Point', venue: { id: 'ven_1', name: 'A', geo: { lat: 1, lng: 2 } } },
    { kind: 'Point', event: { id: 'evt_1', title: 'Meetup', geo: { lat: 3, lng: 4 } } },
    { kind: 'Cluster', count: 3, lat: 0, lng: 0 }
  ];
  assert.equal(mapFeatureItem(features[1]).id, 'evt_1');
  assert.equal(mapFeatureItem(features[2]), null);
  const geo = mapFeaturesToGeoJson(features);
  assert.deepEqual(geo.features.map((f) => [f.id, f.properties.kind, f.properties.name]), [['ven_1', 'venue', 'A'], ['evt_1', 'event', 'Meetup']]);
});

test('the events client sends the map body and the fake answers points, clusters, and bbox lists', async () => {
  const at = (id, lat, lng, extra = {}) => makeEvent(id, { location: { venue: null, name: 'Hall', address: { locality: 'Exampleville', countryCode: 'US' }, addressVisibility: 'Exact', geo: { lat, lng, precision: 'Exact' } }, ...extra });
  const fake = createEventsFake({ events: [at('evt_1', 10, 20), at('evt_2', 10.5, 20.5), at('evt_3', 50, 50), makeEvent('evt_4', { format: 'Online', location: null })] });
  const client = new ApiClient({ fetchImpl: fake.fetchImpl });
  const box = { south: 9, west: 19, north: 11, east: 21 };
  const map = await client.enumerateEventMap({ bbox: box, zoom: 7.4, maxResults: 100, filters: { when: 'Upcoming' } });
  const sent = fake.calls.find((c) => c.path === '/events/map/enumerate');
  assert.deepEqual(sent.body.bbox, box);
  assert.equal(sent.body.zoom, 7);
  assert.deepEqual(map.objects.map((o) => o.event.id).sort(), ['evt_1', 'evt_2']);
  const first = await client.enumerateEventMap({ bbox: box, zoom: 7, maxResults: 1 });
  const last = await client.enumerateEventMap({ bbox: box, zoom: 7, maxResults: 1, continuationToken: first.continuationToken });
  assert.equal(first.endOfResults, false);
  assert.equal(last.endOfResults, true);
  assert.equal(first.totalRecords, 2);
  assert.notEqual(first.objects[0].id, last.objects[0].id);
  fake.state.maxFeatures = 1;
  const clustered = await client.enumerateEventMap({ bbox: box, zoom: 3, maxResults: 1 });
  assert.equal(clustered.objects[0].kind, 'Cluster');
  assert.equal(clustered.objects[0].count, 2);
  const list = await client.enumerateEvents({ filters: { bbox: box } });
  assert.deepEqual(list.objects.map((o) => o.id).sort(), ['evt_1', 'evt_2']);
  const online = await client.enumerateEvents({ filters: { bbox: box, includeOnline: true } });
  assert.deepEqual(online.objects.map((o) => o.id).sort(), ['evt_1', 'evt_2', 'evt_4']);
});

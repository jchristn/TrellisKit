import { test } from 'node:test';
import assert from 'node:assert/strict';
import {
  ApiClient, changedFields, directoryConfig, directoryMethods, directoryTerms, fillDirectory, formatAddress, formatChangeValue, formatDistance, haversineKm, instructorPath,
  isValidPosition, mapEnumerationBody, mapFeaturesToGeoJson, mobileMapProvider, programLabel, programLabels, roundCoordinate, roundNear, submissionFields, toBbox,
  validateSubmission, venueHasPin, venuePath, webMapStyle
} from '../src/index.js';
import { createDirectoryFake, directoryStaffViewer, makeInstructor, makeSubmission, makeVenue } from '../src/testing/index.js';

function recorder() {
  const calls = [];
  const fetchImpl = async (url, init) => {
    calls.push({ method: init.method, url: url.replace('/api/v1', ''), body: init.body ? JSON.parse(init.body) : undefined });
    return new Response(JSON.stringify({ ok: true }), { status: 200, headers: { 'content-type': 'application/json' } });
  };
  return { calls, client: new ApiClient({ fetchImpl }) };
}

test('directory ApiClient methods exist and map to their routes', async () => {
  const names = ['enumerateVenues', 'getVenue', 'enumerateVenueInstructors', 'enumerateInstructors', 'getInstructor', 'enumerateInstructorVenues', 'enumerateDirectoryMap', 'geocodePlace',
    'enumerateDirectoryPrograms', 'createDirectorySubmission', 'enumerateMySubmissions', 'withdrawSubmission', 'unlinkMyInstructor', 'enumeratePublicVenues', 'getPublicVenue',
    'enumeratePublicDirectoryMap', 'adminEnumerateVenues', 'adminCreateVenue', 'adminGetVenue', 'adminUpdateVenue', 'adminDeleteVenue', 'adminVenueAction', 'adminLinkVenueInstructor',
    'adminUnlinkVenueInstructor', 'adminEnumerateInstructors', 'adminCreateInstructor', 'adminGetInstructor', 'adminUpdateInstructor', 'adminDeleteInstructor', 'adminLinkInstructorUser',
    'adminUnlinkInstructorUser', 'adminEnumerateSubmissions', 'adminGetSubmission', 'adminUpdateSubmission', 'adminApproveSubmission', 'adminRejectSubmission', 'adminImportDirectory',
    'adminRegeocode', 'adminDirectoryMetrics', 'adminExportDirectoryCsv', 'adminEnumerateDirectoryPrograms', 'adminCreateDirectoryProgram', 'adminUpdateDirectoryProgram', 'adminDeleteDirectoryProgram',
    'adminOrderDirectoryPrograms', 'adminGetDirectorySettings', 'adminUpdateDirectorySettings', 'grantDirectoryModerator', 'revokeDirectoryModerator',
    'enumerateVenueReviews', 'getVenueReview', 'createVenueReview', 'updateVenueReview', 'deleteVenueReview', 'replyToVenueReview', 'deleteVenueReviewReply', 'enumerateVenueMedia', 'addVenueMedia', 'removeVenueMedia'];
  for (const name of names) assert.equal(typeof ApiClient.prototype[name], 'function', name);
  assert.equal(Object.keys(directoryMethods).length, names.length);

  const { calls, client } = recorder();
  await client.enumerateVenues({ filters: { near: { lat: 37.774929, lng: -122.419416 }, programs: ['youth'] } });
  await client.enumerateDirectoryMap({ bbox: { south: 37.6, west: -122.6, north: 37.9, east: -122.2 }, zoom: 11.4, maxResults: 5000, labels: ['x'], filters: { programs: ['youth'] } });
  await client.geocodePlace('  Exampleville  ');
  await client.adminVenueAction('ven_1', 'verify', 'Checked');
  await client.adminVenueAction('ven_1', 'close', 'Moved away');
  await client.adminApproveSubmission('dsb_1', { geo: { lat: '37.1', lng: '-122.2' } });
  await client.adminRejectSubmission('dsb_1', { reviewNote: 'Duplicate' });
  await client.adminRegeocode({ venueIds: ['ven_1'] });
  await client.adminImportDirectory({ rows: [{ name: 'A' }] });
  await client.adminLinkVenueInstructor('ven_1', 'ins_1', 'Head');
  await client.adminDirectoryMetrics({ filters: { days: 3 } });
  await client.grantDirectoryModerator('usr_2');
  assert.deepEqual(calls.map((c) => c.method + ' ' + c.url), [
    'POST /venues/enumerate', 'POST /directory/map/enumerate', 'POST /directory/geocode/enumerate', 'POST /admin/venues/ven_1/verify', 'POST /admin/venues/ven_1/close',
    'POST /admin/directory/submissions/dsb_1/approve', 'POST /admin/directory/submissions/dsb_1/reject', 'POST /admin/directory/regeocode', 'POST /admin/directory/import',
    'PUT /admin/venues/ven_1/instructors/ins_1', 'POST /admin/directory/metrics/enumerate', 'POST /admin/moderators'
  ]);
  assert.deepEqual(calls[0].body.filters.near, { lat: 37.77, lng: -122.42 }, 'near is rounded to 2 places');
  assert.deepEqual(calls[1].body, { maxResults: 1000, labels: ['x'], labelMatch: 'All', filters: { programs: ['youth'] }, bbox: { south: 37.6, west: -122.6, north: 37.9, east: -122.2 }, zoom: 11 });
  assert.deepEqual(calls[2].body, { maxResults: 25, filters: { query: 'Exampleville' } });
  assert.deepEqual(calls[3].body, { note: 'Checked' });
  assert.deepEqual(calls[4].body, { reason: 'Moved away' });
  assert.deepEqual(calls[5].body, { notifySubmitter: true, geo: { lat: 37.1, lng: -122.2 } });
  assert.deepEqual(calls[6].body, { reviewNote: 'Duplicate', notifySubmitter: true });
  assert.deepEqual(calls[7].body, { venueIds: ['ven_1'], onlyMissing: true });
  assert.deepEqual(calls[8].body, { rows: [{ name: 'A' }], dryRun: true }, 'import defaults to a dry run');
  assert.deepEqual(calls[9].body, { role: 'Head' });
  assert.equal(calls[10].url, '/admin/directory/metrics/enumerate', 'metrics days are clamped to 7..365');
  assert.deepEqual(calls[11].body, { userId: 'usr_2', resourceType: 'Directory', resourceId: '*' });
  assert.throws(() => client.adminVenueAction('ven_1', 'explode'), /Unknown venue action/);
  assert.equal(mapEnumerationBody({}).maxResults, 100);
  assert.deepEqual(mapEnumerationBody({ maxResults: 1, continuationToken: 'page2', ordering: 'CreatedAscending' }), { maxResults: 1, continuationToken: 'page2', ordering: 'CreatedAscending' });
});

test('terms, configuration, and programs come from the property', () => {
  const property = { directory: { venueLabel: 'Club', venueLabelPlural: 'Clubs', instructorLabel: 'Coach', instructorLabelPlural: 'Coaches', programs: [{ key: 'youth', label: 'Youth' }], map: { provider: 'MapLibre', styleUrl: 'https://tiles.example.test/style.json', darkStyleUrl: 'https://tiles.example.test/dark.json', defaultCenter: { lat: 10, lng: 20 }, defaultZoom: 5 } } };
  const terms = directoryTerms(property);
  assert.deepEqual([terms.venue, terms.venues, terms.instructor, terms.instructors, terms.venueLower], ['Club', 'Clubs', 'Coach', 'Coaches', 'club']);
  assert.equal(fillDirectory('Add a {venueLower} with its {instructors} near {place}', property, { place: 'Exampleville' }), 'Add a club with its Coaches near Exampleville');
  assert.equal(fillDirectory('Find {venues}', terms), 'Find Clubs');
  assert.deepEqual(directoryTerms({}), { venue: 'Venue', venues: 'Venues', instructor: 'Instructor', instructors: 'Instructors', venueLower: 'venue', venuesLower: 'venues', instructorLower: 'instructor', instructorsLower: 'instructors' });
  const config = directoryConfig(property);
  assert.equal(config.map.defaultZoom, 5);
  assert.equal(config.map.maxZoom, 18);
  assert.equal(config.submissions, true);
  assert.equal(directoryConfig({}).map.provider, 'None');
  assert.equal(webMapStyle(property), 'https://tiles.example.test/style.json');
  assert.equal(webMapStyle(property, { dark: true }), 'https://tiles.example.test/dark.json');
  assert.equal(webMapStyle({}), null);
  assert.equal(webMapStyle({ directory: { map: { provider: 'PlatformNative', styleUrl: 'https://s.example.test' } } }), 'https://s.example.test');
  assert.equal(mobileMapProvider({ directory: { map: { provider: 'MapLibre' } } }), 'None', 'MapLibre without a style is the list');
  assert.equal(mobileMapProvider({ directory: { map: { provider: 'PlatformNative' } } }), 'PlatformNative');
  assert.equal(programLabel(property, 'youth'), 'Youth');
  assert.equal(programLabel(property, 'retired'), 'retired');
  assert.deepEqual(programLabels(property, [{ key: 'youth', label: 'Kids' }, 'youth']), ['Kids', 'Youth']);
});

test('coordinates, distance, addresses', () => {
  assert.equal(roundCoordinate(37.774929, 2), 37.77);
  assert.equal(roundCoordinate(-0.001, 2), 0);
  assert.equal(roundCoordinate('x'), null);
  assert.equal(roundCoordinate(null), null);
  assert.equal(roundCoordinate(1.23456789, 5), 1.23457);
  assert.deepEqual(roundNear({ lat: 1.234, lng: 5.678, radiusKm: 0 }), { lat: 1.23, lng: 5.68, radiusKm: 0.1 });
  assert.equal(isValidPosition(91, 0), false);
  assert.equal(isValidPosition(0, -180), true);
  assert.equal(isValidPosition(null, 0), false);
  assert.ok(Math.abs(haversineKm({ lat: 0, lng: 0 }, { lat: 0, lng: 1 }) - 111.2) < 0.2);
  assert.equal(formatDistance(2.43), '2.4 km');
  assert.equal(formatDistance(0.851), '850 m');
  assert.equal(formatDistance(12.6), '13 km');
  assert.equal(formatDistance(null), '');
  const address = { line1: '100 Example Street', line2: null, locality: 'Exampleville', region: 'EX', postalCode: '00000', countryCode: 'US', formatted: '100 Example Street, Exampleville, EX 00000' };
  assert.equal(formatAddress(address, 'Exact'), '100 Example Street, Exampleville, EX 00000');
  assert.equal(formatAddress(address, 'Approximate'), 'Exampleville, EX, US');
  assert.equal(formatAddress(address, 'LocalityOnly', { includeCountry: false }), 'Exampleville, EX');
  assert.equal(formatAddress(address, 'LocalityOnly', { isStaff: true }), '100 Example Street, Exampleville, EX 00000, US');
  assert.equal(formatAddress(null), '');
  assert.equal(venueHasPin({ geo: { lat: 1, lng: 2, precision: 'Approximate' }, addressVisibility: 'Approximate' }), true);
  assert.equal(venueHasPin({ geo: { lat: null, lng: null, precision: 'None' } }), false);
  assert.deepEqual(toBbox({ getSouth: () => 1.234567, getWest: () => 190, getNorth: () => 95, getEast: () => 3 }), { south: 1.2346, west: -170, north: 90, east: 3 });
  assert.equal(toBbox(null), null);
  const geo = mapFeaturesToGeoJson([{ kind: 'Point', venue: { id: 'ven_1', name: 'A', geo: { lat: 1, lng: 2 } } }, { kind: 'Cluster', count: 3 }]);
  assert.deepEqual(geo.features[0].geometry.coordinates, [2, 1]);
  assert.equal(geo.features.length, 1);
  assert.equal(venuePath('ven_1'), '/venues/ven_1');
  assert.equal(instructorPath('ins_1'), '/instructors/ins_1');
});

test('submission validation and edit diffs', () => {
  assert.deepEqual(validateSubmission({ type: 'NewVenue', fields: { name: 'A', websiteUrl: 'http://x', address: { line1: '1 Road' } } }), { name: 'tooShort', websiteUrl: 'https', 'address.countryCode': 'countryRequired', 'address.locality': 'required' });
  assert.deepEqual(validateSubmission({ type: 'NewVenue', fields: { name: 'Example Club', address: { locality: 'Exampleville', countryCode: 'US' } } }), {});
  assert.deepEqual(validateSubmission({ type: 'ClaimInstructor', targetId: 'ins_1', note: 'me' }), { note: 'tooShort' });
  assert.deepEqual(validateSubmission({ type: 'EditVenue', fields: {} }), { targetId: 'required' });
  assert.deepEqual(validateSubmission({ type: 'Nope' }), { type: 'required' });
  assert.deepEqual(validateSubmission({ type: 'NewInstructor', fields: { displayName: 'Pat Example', socialLinks: [{ kind: 'X', url: 'ftp://x' }] } }), { socialLinks: 'https' });
  const current = { name: 'Old', programs: [{ key: 'a', label: 'A' }], address: { line1: '1', locality: 'X', formatted: '1, X' } };
  assert.deepEqual(changedFields(current, { name: 'Old', programs: ['a', 'b'], address: { line1: '1', locality: 'X' } }), { programs: ['a', 'b'] });
  assert.ok(submissionFields('EditInstructor').includes('venueIds'));
  assert.ok(submissionFields('NewVenue').includes('photoMediaIds'));
  assert.equal(formatChangeValue(['a', 'b']), 'a, b');
  assert.equal(formatChangeValue({ kind: 'X', url: 'https://x.example.test' }), 'X: https://x.example.test');
  assert.equal(formatChangeValue({ lat: 1.123456, lng: 2 }), '1.12346, 2');
  assert.equal(formatChangeValue(null), '');
});

function directoryWorld() {
  const v1 = makeVenue('ven_1', { name: 'Example Club Downtown', lat: 37.7749, lng: -122.4194, programs: ['youth'], isVerified: true, labels: ['featured'] });
  const v2 = makeVenue('ven_2', { name: 'Example Club Uptown', lat: 37.8044, lng: -122.2712, addressVisibility: 'Approximate', programs: ['beginners'] });
  const v3 = makeVenue('ven_3', { name: 'Hidden Studio', lat: 37.7, lng: -122.5, addressVisibility: 'LocalityOnly' });
  const v4 = makeVenue('ven_4', { name: 'Far Away', lat: 40.7128, lng: -74.006, status: 'Closed' });
  const v5 = makeVenue('ven_5', { name: 'Members Hall', lat: 37.76, lng: -122.43, locked: true, viewer: { canOpen: false, requires: 'Membership' }, audience: 'Members', phone: '+1 555 0100' });
  const i1 = makeInstructor('ins_1', { displayName: 'Pat Example' });
  const i2 = makeInstructor('ins_2', { displayName: 'Lee Linked', linkedUser: { id: 'usr_7', displayName: 'Lee' } });
  return createDirectoryFake({ venues: [v1, v2, v3, v4, v5], instructors: [i1, i2], links: [{ venueId: 'ven_1', instructorId: 'ins_1' }, { venueId: 'ven_2', instructorId: 'ins_1' }], programs: [{ key: 'youth', label: 'Youth' }, { key: 'beginners', label: 'Beginners' }] });
}

test('directory fake: venue reads, visibility, near, bbox, filters, locked stubs', async () => {
  const fake = directoryWorld();
  const client = new ApiClient({ fetchImpl: fake.fetchImpl });
  const all = await client.enumerateVenues();
  assert.deepEqual(all.objects.map((v) => v.id), ['ven_1', 'ven_2', 'ven_3', 'ven_5'], 'closed venues are hidden by default');
  assert.equal(all.objects[0].programs[0].label, 'Youth');
  assert.equal(all.objects[0].description, undefined);
  const approx = all.objects.find((v) => v.id === 'ven_2');
  assert.deepEqual([approx.geo.lat, approx.geo.lng, approx.geo.precision, approx.address.line1], [37.8, -122.27, 'Approximate', null]);
  const hidden = all.objects.find((v) => v.id === 'ven_3');
  assert.equal(hidden.geo.lat, null);
  const near = await client.enumerateVenues({ filters: { near: { lat: 37.7749, lng: -122.4194, radiusKm: 12 } } });
  assert.deepEqual(near.objects.map((v) => v.id), ['ven_1', 'ven_5', 'ven_3']);
  assert.equal(near.objects[0].distanceKm, 0.5, 'distance from the rounded position');
  const box = await client.enumerateVenues({ filters: { bbox: { south: 37.79, west: -122.3, north: 37.81, east: -122.2 } } });
  assert.deepEqual(box.objects.map((v) => v.id), ['ven_2']);
  await assert.rejects(client.enumerateVenues({ filters: { near: { lat: 1, lng: 1 }, bbox: { south: 0, west: 0, north: 1, east: 1 } } }), (e) => e.code === 'InvalidGeo');
  assert.deepEqual((await client.enumerateVenues({ filters: { programs: ['youth'] } })).objects.map((v) => v.id), ['ven_1']);
  assert.deepEqual((await client.enumerateVenues({ labels: ['featured'] })).objects.map((v) => v.id), ['ven_1']);
  assert.deepEqual((await client.enumerateVenues({ filters: { includeClosed: true, verifiedOnly: true } })).objects.map((v) => v.id), ['ven_1']);
  const locked = await client.getVenue('ven_5');
  assert.equal(locked.phone, null);
  assert.equal(locked.description, null);
  const detail = await client.getVenue('example-club-downtown');
  assert.equal(detail.instructors.totalRecords, 1);
  assert.equal((await client.enumerateVenueInstructors('ven_1')).objects[0].id, 'ins_1');
  assert.deepEqual((await client.enumerateInstructorVenues('ins_1')).objects.map((v) => v.id), ['ven_1', 'ven_2']);
  assert.equal((await client.getInstructor('ins_1')).venues.totalRecords, 2);
  assert.deepEqual((await client.enumerateInstructors({ filters: { claimed: true } })).objects.map((i) => i.id), ['ins_2']);
});

test('directory fake: map points, clusters, place search', async () => {
  const fake = directoryWorld();
  const client = new ApiClient({ fetchImpl: fake.fetchImpl });
  const bbox = { south: 37.6, west: -122.6, north: 37.9, east: -122.2 };
  const points = await client.enumerateDirectoryMap({ bbox, zoom: 11 });
  assert.deepEqual(points.objects.map((f) => f.kind + ':' + f.venue.id).sort(), ['Point:ven_1', 'Point:ven_2', 'Point:ven_5']);
  assert.equal(points.endOfResults, true);
  const first = await client.enumerateDirectoryMap({ bbox, zoom: 11, maxResults: 1 });
  const second = await client.enumerateDirectoryMap({ bbox, zoom: 11, maxResults: 1, continuationToken: first.continuationToken });
  assert.equal(first.totalRecords, 3);
  assert.equal(first.objects.length, 1);
  assert.notEqual(first.objects[0].id, second.objects[0].id);
  fake.state.settings.map.maxFeatures = 1;
  const clustered = await client.enumerateDirectoryMap({ bbox, zoom: 3, maxResults: 1 });
  assert.equal(clustered.objects.every((f) => f.kind === 'Cluster' || f.kind === 'Point'), true);
  assert.ok(clustered.objects.some((f) => f.kind === 'Cluster' && f.count >= 2));
  assert.equal(clustered.continuationToken, null);
  await assert.rejects(client.enumerateDirectoryMap({}), (e) => e.code === 'InvalidGeo');
  const found = await client.geocodePlace('example');
  assert.equal(found.objects[0].label, 'Exampleville, EX');
  fake.state.geocoderConfigured = false;
  await assert.rejects(client.geocodePlace('example'), (e) => e.status === 503 && e.code === 'IntegrationNotConfigured');
});

test('directory fake: submissions, duplicate step, claims, limits, review', async () => {
  const fake = directoryWorld();
  const client = new ApiClient({ fetchImpl: fake.fetchImpl });
  const draft = await client.createDirectorySubmission({ type: 'NewVenue', fields: { name: 'Example Club Downtown', address: { locality: 'Exampleville', countryCode: 'US' } } });
  assert.equal(draft.status, 'Draft');
  assert.equal(draft.possibleDuplicates.objects[0].id, 'ven_1');
  const queued = await client.createDirectorySubmission({ type: 'NewVenue', confirmNotDuplicate: true, fields: { name: 'Example Club Downtown', address: { line1: '9 Road', locality: 'Exampleville', countryCode: 'US' }, addressVisibility: 'Exact' } });
  assert.equal(queued.status, 'Pending');
  assert.equal(queued.changes[0].current, null);
  const edit = await client.createDirectorySubmission({ type: 'EditVenue', targetId: 'ven_1', fields: { scheduleText: 'Mon 6 pm' }, note: 'New times' });
  assert.deepEqual(edit.target, { type: 'Venue', id: 'ven_1', name: 'Example Club Downtown' });
  await assert.rejects(client.createDirectorySubmission({ type: 'ClaimInstructor', targetId: 'ins_2', note: 'This is my entry, honestly.' }), (e) => e.code === 'AlreadyClaimed');
  await assert.rejects(client.createDirectorySubmission({ type: 'ClaimInstructor', targetId: 'ins_1', note: 'short' }), (e) => e.code === 'ValidationFailed');
  const claim = await client.createDirectorySubmission({ type: 'ClaimInstructor', targetId: 'ins_1', note: 'I teach the evening classes here.' });
  await assert.rejects(client.createDirectorySubmission({ type: 'ClaimInstructor', targetId: 'ins_1', note: 'I teach the evening classes here.' }), (e) => e.code === 'AlreadyClaimed');
  const mine = await client.enumerateMySubmissions();
  assert.equal(mine.totalRecords, 3);
  assert.equal((await client.withdrawSubmission(edit.id)).status, 'Withdrawn');
  await assert.rejects(client.withdrawSubmission(edit.id), (e) => e.status === 409);

  const queue = await client.adminEnumerateSubmissions();
  assert.deepEqual(queue.objects.map((s) => s.id), [queued.id, claim.id]);
  await client.adminUpdateSubmission(queued.id, { name: 'Example Club Riverside' });
  const approved = await client.adminApproveSubmission(queued.id, { geo: { lat: 37.75, lng: -122.45 }, reviewNote: 'Welcome' });
  assert.equal(approved.submission.status, 'Approved');
  assert.equal(approved.target.name, 'Example Club Riverside');
  assert.equal(approved.target.geo.source, 'Manual');
  assert.equal(approved.target.address.line1, '9 Road', 'member-submitted addresses stay exact');
  const claimed = await client.adminApproveSubmission(claim.id);
  assert.equal(claimed.target.linkedUser.id, 'usr_1');
  await assert.rejects(client.adminRejectSubmission(claim.id, { reviewNote: 'late' }), (e) => e.status === 409);
  await client.unlinkMyInstructor('ins_1');
  assert.equal(fake.instructor('ins_1').linkedUser, null);

  fake.state.submissions.push(makeSubmission('dsb_seed', { createdUtc: '2026-01-01T00:00:00Z' }));
  assert.equal((await client.adminEnumerateSubmissions()).objects[0].id, 'dsb_seed', 'oldest first');
  fake.state.maxPending = 0;
  await assert.rejects(client.createDirectorySubmission({ type: 'ReportClosed', targetId: 'ven_2' }), (e) => e.code === 'SubmissionLimitReached');
  fake.state.submissionsEnabled = false;
  await assert.rejects(client.createDirectorySubmission({ type: 'ReportClosed', targetId: 'ven_2' }), (e) => e.code === 'SubmissionsDisabled');
});

test('directory fake: admin venues, instructors, programs, import, kill switch', async () => {
  const fake = directoryWorld();
  const client = new ApiClient({ fetchImpl: fake.fetchImpl });
  const created = await client.adminCreateVenue({ name: 'New Place', address: { locality: 'Exampleville', countryCode: 'US' } });
  assert.equal(created.geo.source, 'None');
  const needsPin = await client.adminEnumerateVenues({ filters: { geoSource: 'None' } });
  assert.ok(needsPin.objects.some((v) => v.id === created.id));
  const pinned = await client.adminUpdateVenue(created.id, { geo: { lat: 37.7, lng: -122.4 } });
  assert.equal(pinned.geo.source, 'Manual');
  assert.equal(pinned.viewer.canEdit, directoryStaffViewer.canEdit);
  assert.equal((await client.adminVenueAction(created.id, 'verify')).isVerified, true);
  assert.equal((await client.adminVenueAction(created.id, 'archive')).status, 'Archived');
  await assert.rejects(client.getVenue(created.id), (e) => e.status === 404);
  const exact = await client.adminGetVenue('ven_2');
  assert.equal(exact.address.line1, '100 Example Street', 'staff see the exact address');
  const ins = await client.adminCreateInstructor({ displayName: 'New Coach' });
  await client.adminLinkVenueInstructor('ven_2', ins.id, 'Guest');
  assert.equal((await client.getVenue('ven_2')).instructors.totalRecords, 2);
  await client.adminUnlinkVenueInstructor('ven_2', ins.id);
  assert.equal((await client.adminLinkInstructorUser(ins.id, 'usr_3')).linkedUser.id, 'usr_3');
  await assert.rejects(client.adminLinkInstructorUser(ins.id, 'usr_4'), (e) => e.code === 'AlreadyClaimed');
  const program = await client.adminCreateDirectoryProgram({ key: 'advanced', label: 'Advanced' });
  await assert.rejects(client.adminCreateDirectoryProgram({ key: 'Advanced', label: 'Dup' }), (e) => e.status === 409);
  await client.adminDeleteDirectoryProgram(program.id);
  assert.equal((await client.enumerateDirectoryPrograms()).objects.some((p) => p.key === 'advanced'), false, 'retired programs leave member lists');
  const dry = await client.adminImportDirectory({ rows: [{ name: 'Imported' }, { summary: 'no name' }] });
  assert.deepEqual([dry.created, dry.skipped, dry.errors.totalRecords], [1, 1, 1]);
  assert.equal(fake.state.venues.some((v) => v.name === 'Imported'), false, 'dry run writes nothing');
  assert.equal((await client.adminRegeocode({ onlyMissing: true })).queued >= 0, true);
  assert.match(await client.adminExportDirectoryCsv(), /^type,id,name/);
  fake.state.killed = true;
  await assert.rejects(client.enumerateVenues(), (e) => e.status === 503 && e.code === 'FeatureDisabled');
  assert.ok((await client.adminEnumerateVenues()).totalRecords > 0, 'admin stays open');
  fake.state.venues[0].audience = 'Public';
  assert.deepEqual((await client.enumeratePublicVenues()).objects.map((v) => [v.id, v.viewer, v.phone]), [['ven_1', undefined, undefined]]);
});

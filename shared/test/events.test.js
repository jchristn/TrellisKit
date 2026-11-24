import { test } from 'node:test';
import assert from 'node:assert/strict';
import {
  ApiClient, buildRecurrenceRule, categoryOf, dateKeyInZone, defaultRecurrenceForm, eventMethods, eventsConfig, formatEventShort, formatEventTime, formatReminderOffset,
  groupEventsByDay, isPastEvent, isRecurring, joinUrlOf, localInputValue, monthGrid, monthWindowUtc, recurrenceFormFromRule, recurrenceSummary, rsvpState, setPositionOfLocal,
  validateEventForm, validateRecurrenceForm, weekdayOfLocal
} from '../src/index.js';
import { createEventsFake, eventStaffViewer, makeEvent, makeEventCategory } from '../src/testing/index.js';

function recorder() {
  const calls = [];
  const fetchImpl = async (url, init) => {
    calls.push({ method: init.method, url: url.replace('/api/v1', ''), body: init.body ? JSON.parse(init.body) : undefined });
    return new Response(JSON.stringify({ ok: true }), { status: 200, headers: { 'content-type': 'application/json' } });
  };
  return { calls, client: new ApiClient({ fetchImpl }) };
}

test('event ApiClient methods exist and map to their routes', async () => {
  const names = ['enumerateEvents', 'enumerateEventMap', 'getEvent', 'enumerateEventOccurrences', 'rsvpEvent', 'cancelRsvp', 'enumerateAttendees', 'getEventIcsUrl', 'downloadEventIcs', 'enumerateMyEvents',
    'enumerateEventCategories', 'getCalendarFeed', 'rotateCalendarFeed', 'deleteCalendarFeed', 'enumerateEventComments', 'createEventComment', 'putEventReaction', 'deleteEventReaction',
    'enumerateEventReactions', 'promoteEvent', 'unpromoteEvent', 'proposeEvent', 'enumerateMyEventProposals', 'withdrawEventProposal', 'enumeratePublicEvents', 'getPublicEvent',
    'adminEnumerateEvents', 'adminCreateEvent', 'adminGetEvent', 'adminUpdateEvent', 'adminPublishEvent', 'adminCancelEvent', 'adminDuplicateEvent', 'adminDeleteEvent', 'adminEnumerateRsvps',
    'adminRsvpAction', 'adminCheckInRsvp', 'adminUndoCheckInRsvp', 'adminExportRsvpsCsv', 'adminEventMetrics', 'adminEnumerateEventProposals', 'adminApproveEvent', 'adminRejectEvent',
    'adminEnumerateEventCategories', 'adminCreateEventCategory', 'adminUpdateEventCategory', 'adminDeleteEventCategory', 'adminOrderEventCategories', 'grantEventManager', 'revokeEventManager'];
  for (const name of names) assert.equal(typeof ApiClient.prototype[name], 'function', name);
  assert.equal(Object.keys(eventMethods).length, names.length);

  const { calls, client } = recorder();
  await client.enumerateEvents({ filters: { when: 'Upcoming', near: { lat: 37.774929, lng: -122.419416, radiusKm: 9000 } } });
  await client.rsvpEvent('evt_1', { status: 'Going', guestCount: 2.7, hideFromAttendeeLists: 1 });
  await client.cancelRsvp('evt_1');
  await client.enumerateEventOccurrences('evt_1');
  await client.proposeEvent({ title: 'Idea', recurrence: { frequency: 'Weekly' }, hostUserIds: ['usr_2'] });
  await client.adminUpdateEvent('evt_1', { title: 'New' }, 'Following');
  await client.adminUpdateEvent('evt_1', { title: 'New', scope: 'All' });
  await client.adminUpdateEvent('evt_1', { title: 'One only' });
  await client.adminCancelEvent('evt_1', { reason: 'Weather', scope: 'All' });
  await client.adminApproveEvent('evt_2', { reviewNote: 'Nice' });
  await client.adminRejectEvent('evt_2', { reviewNote: 'Not now' });
  await client.adminRsvpAction('evt_1', 'rsv_1', 'move-to-waitlist', { reason: 'x' });
  await client.adminCheckInRsvp('evt_1', 'rsv_1');
  await client.adminEventMetrics({ filters: { days: 0 } });
  await client.adminDuplicateEvent('evt_1', { startLocal: '2026-12-12T18:00' });
  await client.promoteEvent('evt_1');
  assert.deepEqual(calls.map((c) => c.method + ' ' + c.url), [
    'POST /events/enumerate', 'PUT /events/evt_1/rsvp', 'DELETE /events/evt_1/rsvp', 'POST /events/evt_1/occurrences/enumerate', 'POST /events/proposals', 'PUT /admin/events/evt_1',
    'PUT /admin/events/evt_1', 'PUT /admin/events/evt_1', 'POST /admin/events/evt_1/cancel', 'POST /admin/events/evt_2/approve', 'POST /admin/events/evt_2/reject',
    'POST /admin/events/evt_1/rsvps/rsv_1/move-to-waitlist', 'POST /admin/events/evt_1/rsvps/rsv_1/check-in', 'POST /admin/metrics/events/enumerate', 'POST /admin/events/evt_1/duplicate',
    'POST /events/evt_1/promote'
  ]);
  assert.deepEqual(calls[0].body.filters.near, { lat: 37.77, lng: -122.42, radiusKm: 500 }, 'near is rounded and clamped');
  assert.deepEqual(calls[1].body, { status: 'Going', guestCount: 2, hideFromAttendeeLists: true });
  assert.deepEqual(calls[4].body, { title: 'Idea' }, 'proposals never send recurrence or hosts');
  assert.equal(calls[5].body.scope, 'Following');
  assert.equal(calls[6].body.scope, 'All');
  assert.equal(calls[7].body.scope, undefined);
  assert.deepEqual(calls[8].body, { reason: 'Weather', notifyAttendees: true, scope: 'All' });
  assert.deepEqual(calls[9].body, { publish: true, notifyMembers: false, reviewNote: 'Nice' });
  assert.deepEqual(calls[11].body, { notifyMember: true, reason: 'x' });
  assert.deepEqual(calls[15].body, { notifyMembers: false });
  assert.throws(() => client.adminRsvpAction('evt_1', 'rsv_1', 'explode'), /Unknown RSVP action/);
  assert.equal(client.getEventIcsUrl('evt_1'), '/api/v1/events/evt_1/ics');
  assert.equal(client.getEventIcsUrl('evt_1', { scope: 'Series' }), '/api/v1/events/evt_1/ics?scope=Series');
});

test('formatEventTime shows the event zone and the viewer zone when they differ', () => {
  const e = { startUtc: '2026-12-05T18:00:00Z', endUtc: '2026-12-05T21:00:00Z', timeZone: 'America/Los_Angeles' };
  const t = formatEventTime(e, 'America/New_York');
  assert.equal(t.primary, 'Sat, Dec 5, 2026, 10:00 AM to 1:00 PM PST');
  assert.equal(t.secondary, 'Sat, Dec 5, 2026, 1:00 PM to 4:00 PM EST');
  assert.equal(formatEventTime(e, 'America/Los_Angeles').secondary, null);
  assert.equal(formatEventTime(e, 'US/Pacific').secondary, null, 'same wall clock in a different zone id');
  const allDay = formatEventTime({ startUtc: '2026-12-05T08:00:00Z', endUtc: '2026-12-07T08:00:00Z', timeZone: 'America/Los_Angeles', isAllDay: true }, 'Asia/Tokyo');
  assert.equal(allDay.primary, 'Sat, Dec 5, 2026 to Sun, Dec 6, 2026');
  assert.equal(allDay.secondary, null);
  const overnight = formatEventTime({ startUtc: '2026-12-06T04:00:00Z', endUtc: '2026-12-06T09:00:00Z', timeZone: 'America/Los_Angeles' });
  assert.equal(overnight.multiDay, true);
  assert.equal(formatEventTime({ startUtc: 'nope' }).primary, '');
  assert.equal(formatEventTime({ ...e, timeZone: 'Not/AZone' }).zone, 'UTC');
  assert.equal(formatEventShort(e), 'Sat, Dec 5, 10:00 AM PST');
  assert.equal(dateKeyInZone('2026-12-06T04:00:00Z', 'America/Los_Angeles'), '2026-12-05');
  assert.equal(localInputValue('2026-12-06T04:00:00Z', 'America/Los_Angeles'), '2026-12-05T20:00');
  assert.equal(isPastEvent(e, new Date('2027-01-01T00:00:00Z')), true);
});

test('recurrence rules: build, summarize, round trip, validate', () => {
  assert.equal(weekdayOfLocal('2026-12-08T19:00'), 'Tue');
  assert.equal(setPositionOfLocal('2026-12-08'), 2);
  assert.equal(setPositionOfLocal('2026-12-29'), -1);
  const form = defaultRecurrenceForm('2026-12-08T19:00');
  assert.equal(buildRecurrenceRule(form), null, 'off by default');
  const weekly = buildRecurrenceRule({ ...form, enabled: true, weekdays: ['Thu', 'Tue'], ends: 'After', count: 12 });
  assert.deepEqual(weekly, { frequency: 'Weekly', interval: 1, weekdays: ['Tue', 'Thu'], monthDay: null, setPosition: null, count: 12, untilDate: null });
  assert.equal(recurrenceSummary(weekly), 'Every week on Tuesday and Thursday, 12 times');
  const monthlyPos = buildRecurrenceRule({ ...form, enabled: true, frequency: 'Monthly', monthlyMode: 'Position', setPosition: -1, positionWeekday: 'Fri', interval: 2 });
  assert.equal(recurrenceSummary(monthlyPos), 'Every 2 months on the last Friday');
  const monthlyDate = buildRecurrenceRule({ ...form, enabled: true, frequency: 'Monthly', monthDay: 15, ends: 'On', untilDate: '2027-03-01' });
  assert.equal(recurrenceSummary(monthlyDate), 'Every month on day 15 until March 1, 2027');
  assert.equal(recurrenceSummary({ frequency: 'Monthly', monthDay: -1, interval: 1 }), 'Every month on the last day');
  assert.equal(recurrenceSummary({ frequency: 'Daily', interval: 1 }), 'Every day');
  assert.equal(recurrenceSummary({ frequency: 'Weekly', interval: 1, weekdays: ['Mon', 'Tue', 'Wed', 'Thu', 'Fri'], count: 1 }), 'Every week on weekdays, 1 time');
  assert.equal(recurrenceSummary({ frequency: 'Yearly', interval: 1 }, { startLocal: '2026-12-08' }), 'Every year on December 8');
  assert.equal(recurrenceSummary(null), '');
  const back = recurrenceFormFromRule(monthlyPos, '2026-12-08T19:00');
  assert.equal(back.monthlyMode, 'Position');
  assert.equal(back.positionWeekday, 'Fri');
  assert.deepEqual(buildRecurrenceRule(back), monthlyPos);
  assert.deepEqual(buildRecurrenceRule(recurrenceFormFromRule(monthlyDate)), monthlyDate);
  assert.deepEqual(validateRecurrenceForm({ ...form, enabled: true, weekdays: [], ends: 'On', untilDate: '2026-01-01' }, '2026-12-08T19:00'), { weekdays: 'weekdaysRequired', untilDate: 'untilBeforeStart' });
  assert.deepEqual(validateRecurrenceForm({ ...form, enabled: true, ends: 'After', count: 0, interval: 100 }), { interval: 'intervalInvalid', count: 'countInvalid' });
  assert.deepEqual(validateRecurrenceForm(form), {});
});

test('rsvpState drives the RSVP control', () => {
  const now = new Date('2026-11-11T12:00:00Z');
  const open = makeEvent('evt_1', { capacity: 10, spotsLeft: 3 });
  assert.equal(rsvpState(open, now).canGoing, true);
  const full = makeEvent('evt_2', { capacity: 10, spotsLeft: 0 });
  const s = rsvpState(full, now);
  assert.equal(s.full, true);
  assert.equal(s.joinsWaitlist, true);
  assert.equal(s.canGoing, true);
  assert.equal(rsvpState({ ...full, waitlistEnabled: false }, now).canGoing, false);
  assert.equal(rsvpState({ ...open, myRsvp: { status: 'Waitlisted', position: 2 } }, now).position, 2);
  assert.equal(rsvpState({ ...open, status: 'Canceled' }, now).closed, true);
  assert.equal(rsvpState({ ...open, rsvpClosesUtc: '2026-11-10T00:00:00Z' }, now).canInterested, false);
  const locked = rsvpState({ ...open, locked: true, viewer: { canOpen: false, requires: 'Membership' } }, now);
  assert.equal(locked.locked, true);
  assert.equal(locked.requires, 'Membership');
  assert.equal(locked.canGoing, false);
  assert.equal(rsvpState({ ...open, myRsvp: { status: 'Going' } }, now).canCancel, true);
  assert.equal(joinUrlOf({ online: { joinUrl: 'https://call.example.test/x' } }), 'https://call.example.test/x');
  assert.equal(joinUrlOf({ online: { joinUrl: 'http://insecure' } }), null);
  assert.equal(isRecurring({ recurrence: { id: 'erc_1' } }), true);
});

test('calendar grid, grouping, categories, validation', () => {
  const weeks = monthGrid(2026, 12);
  assert.equal(weeks[0][0].key, '2026-11-30');
  assert.equal(weeks[0][1].key, '2026-12-01');
  assert.equal(weeks.every((w) => w.length === 7), true);
  assert.equal(weeks[weeks.length - 1][6].inMonth, false);
  assert.equal(monthGrid(2026, 12, { weekStartsOn: 0 })[0][0].key, '2026-11-29');
  const win = monthWindowUtc(2026, 12);
  assert.equal(win.startsAfterUtc, '2026-11-29T00:00:00.000Z');
  const grouped = groupEventsByDay([{ id: 'b', startUtc: '2026-12-06T04:00:00Z' }, { id: 'a', startUtc: '2026-12-05T18:00:00Z' }], 'America/Los_Angeles');
  assert.deepEqual(grouped['2026-12-05'].map((e) => e.id), ['a', 'b']);
  const property = { events: { categories: [{ key: 'meetup', label: 'Meetup', color: '#123456' }], proposals: true } };
  assert.equal(categoryOf(property, 'meetup').label, 'Meetup');
  assert.equal(categoryOf(property, { category: { key: 'x', label: 'Own' } }).label, 'Own');
  assert.equal(categoryOf(property, 'gone').label, 'gone');
  assert.deepEqual(eventsConfig({}), { categories: [], proposals: false, calendarFeedEnabled: true, defaultReminderOffsetsMinutes: [1440, 120] });
  assert.equal(eventsConfig(property).proposals, true);
  assert.equal(formatReminderOffset(1440), '1 day before');
  assert.equal(formatReminderOffset(120), '2 hours before');
  assert.equal(formatReminderOffset(45), '45 minutes before');
  assert.deepEqual(validateEventForm({ title: 'Hi', format: 'Hybrid', startLocal: '2026-12-05T18:00', endLocal: '2026-12-05T17:00', timeZone: 'UTC', online: { joinUrl: 'http://x' }, capacity: 0, reminderOffsetsMinutes: [1, 2, 3, 4] }), {
    title: 'tooShort', endLocal: 'endBeforeStart', location: 'locationRequired', 'online.joinUrl': 'https', capacity: 'capacityInvalid', reminderOffsetsMinutes: 'tooManyReminders'
  });
  assert.deepEqual(validateEventForm({ title: 'Meetup', format: 'InPerson', startLocal: '2026-12-05T18:00', endLocal: '2026-12-05T21:00', timeZone: 'UTC', location: { name: 'Hall' }, reminderOffsetsMinutes: [1440] }), {});
  const base = { title: 'Meetup', format: 'InPerson', startLocal: '2026-12-05T18:00', endLocal: '2026-12-05T21:00', timeZone: 'UTC' };
  assert.deepEqual(validateEventForm({ ...base, location: { name: 'Hall', address: { line1: '1 Example Way', locality: 'Exampleville', countryCode: '' } } }), { 'location.address.countryCode': 'countryRequired' });
  assert.deepEqual(validateEventForm({ ...base, location: { name: 'Hall', address: { line1: '1 Example Way', locality: 'Exampleville', countryCode: 'US' } } }), {});
  assert.deepEqual(validateEventForm({ ...base, location: { venueId: 'ven_1', address: { line1: '1 Example Way', countryCode: '' } } }), {});
});

test('events fake: RSVP capacity, waitlist order, promotion, closed and canceled', async () => {
  const fake = createEventsFake({ events: [makeEvent('evt_1', { capacity: 2, maxGuestsPerRsvp: 1 }), makeEvent('evt_2', { capacity: 1, waitlistEnabled: false })] });
  const client = new ApiClient({ fetchImpl: fake.fetchImpl });
  fake.state.rsvps.push({ id: 'rsv_a', eventId: 'evt_1', userId: 'usr_a', user: { id: 'usr_a' }, status: 'Going', guestCount: 1, createdUtc: 'x' });
  let e = await client.rsvpEvent('evt_1', { status: 'Going' });
  assert.equal(e.myRsvp.status, 'Waitlisted');
  assert.equal(e.myRsvp.position, 1);
  assert.equal(e.spotsLeft, 0);
  e = await client.rsvpEvent('evt_1', { status: 'Interested' });
  assert.equal(e.myRsvp.status, 'Interested');
  e = await client.rsvpEvent('evt_1', { status: 'Going' });
  assert.equal(e.myRsvp.status, 'Waitlisted');
  fake.state.rsvps = fake.state.rsvps.filter((r) => r.id !== 'rsv_a');
  await client.adminRsvpAction('evt_1', fake.state.rsvps[0].id, 'check-in');
  e = await client.getEvent('evt_1');
  e = await client.rsvpEvent('evt_1', { status: 'Going' });
  assert.equal(e.myRsvp.status, 'Going', 'promoted once space opened');
  fake.state.rsvps.push({ id: 'rsv_b', eventId: 'evt_2', userId: 'usr_b', user: { id: 'usr_b' }, status: 'Going', guestCount: 0, createdUtc: 'x' });
  await assert.rejects(client.rsvpEvent('evt_2', { status: 'Going' }), (err) => err.code === 'EventFull' && err.status === 409);
  e = await client.cancelRsvp('evt_1');
  assert.equal(e.myRsvp, null);
  const past = createEventsFake({ events: [makeEvent('evt_p', { startUtc: '2026-11-01T00:00:00Z', endUtc: '2026-11-01T02:00:00Z' }), makeEvent('evt_c', { status: 'Canceled' })] });
  const pc = new ApiClient({ fetchImpl: past.fetchImpl });
  await assert.rejects(pc.rsvpEvent('evt_p', { status: 'Going' }), (err) => err.code === 'RsvpClosed');
  await assert.rejects(pc.rsvpEvent('evt_c', { status: 'Going' }), (err) => err.code === 'EventCanceled');
  await assert.rejects(pc.createEventComment('evt_c', { body: 'hi' }), (err) => err.code === 'EventCanceled');
  const locked = createEventsFake({ events: [makeEvent('evt_l', { locked: true, viewer: { ...makeEvent('x').viewer, canOpen: false, requires: 'Membership' }, online: { platformLabel: 'Call', joinUrl: 'https://call.example.test' } })] });
  const lc = new ApiClient({ fetchImpl: locked.fetchImpl });
  const stub = await lc.getEvent('evt_l');
  assert.equal(stub.description, null);
  assert.equal(stub.online, null);
  await assert.rejects(lc.rsvpEvent('evt_l', { status: 'Going' }), (err) => err.status === 402);
});

test('events fake: lists, recurrence, scopes, proposals, calendar feed, kill switch', async () => {
  const fake = createEventsFake({ categories: [makeEventCategory('meetup', 'Meetup', '#123456')], events: [makeEvent('evt_1', { category: { key: 'meetup', label: 'Meetup', color: '#123456' }, labels: ['social'] }), makeEvent('evt_old', { startUtc: '2026-10-01T00:00:00Z', endUtc: '2026-10-01T01:00:00Z' })] });
  const client = new ApiClient({ fetchImpl: fake.fetchImpl });
  assert.deepEqual((await client.enumerateEvents()).objects.map((e) => e.id), ['evt_1']);
  assert.deepEqual((await client.enumerateEvents({ filters: { when: 'Past' } })).objects.map((e) => e.id), ['evt_old']);
  assert.deepEqual((await client.enumerateEvents({ labels: ['nope'] })).objects, []);
  assert.deepEqual((await client.enumerateEvents({ filters: { category: ['meetup'] } })).objects.map((e) => e.id), ['evt_1']);
  assert.equal((await client.enumerateEvents()).objects[0].description, undefined);

  const created = await client.adminCreateEvent({ title: 'Weekly session', startLocal: '2026-12-01T18:00', endLocal: '2026-12-01T19:00', timeZone: 'UTC', recurrence: { frequency: 'Weekly', interval: 1, weekdays: ['Tue'], count: 4 } });
  assert.equal(created.status, 'Draft');
  assert.equal(created.recurrence.summary, 'Every week on Tuesday, 4 times');
  await client.adminPublishEvent(created.id);
  const occurrences = await client.enumerateEventOccurrences(created.id);
  assert.equal(occurrences.totalRecords, 4);
  const third = occurrences.objects[2];
  await client.adminUpdateEvent(third.id, { title: 'Moved session' }, 'One');
  assert.equal(fake.event(third.id).recurrence.isException, true);
  assert.equal(fake.event(occurrences.objects[3].id).title, 'Weekly session');
  await client.adminUpdateEvent(third.id, { summary: 'From here' }, 'Following');
  assert.equal(fake.event(occurrences.objects[3].id).summary, 'From here');
  assert.notEqual(fake.event(occurrences.objects[1].id).summary, 'From here');
  await client.adminCancelEvent(occurrences.objects[1].id, { reason: 'Holiday' });
  assert.equal(fake.event(occurrences.objects[1].id).status, 'Canceled');
  assert.equal(fake.event(occurrences.objects[0].id).status, 'Published');
  await assert.rejects(client.adminCancelEvent(created.id, { reason: '' }), (e) => e.code === 'ValidationFailed');
  const ics = await client.downloadEventIcs(created.id, { scope: 'Series' });
  assert.match(ics, /RRULE:FREQ=WEEKLY/);

  const proposal = await client.proposeEvent({ title: 'Picnic', startLocal: '2026-12-20T12:00', endLocal: '2026-12-20T14:00', timeZone: 'UTC', location: { name: 'Park' } });
  assert.equal(proposal.status, 'Proposed');
  assert.equal((await client.enumerateMyEventProposals()).totalRecords, 1);
  assert.equal((await client.adminEnumerateEventProposals()).objects[0].id, proposal.id);
  const approved = await client.adminApproveEvent(proposal.id, { reviewNote: 'Lovely' });
  assert.equal(approved.status, 'Published');
  assert.equal(approved.hosts[0].id, 'usr_1');
  await assert.rejects(client.adminApproveEvent(proposal.id), (e) => e.status === 409);
  const second = await client.proposeEvent({ title: 'Another' });
  assert.equal((await client.withdrawEventProposal(second.id)).status, 'Withdrawn');

  const feed = await client.getCalendarFeed();
  const rotated = await client.rotateCalendarFeed();
  assert.notEqual(feed.url, rotated.url);
  await client.deleteCalendarFeed();
  assert.equal(fake.state.feed, null);
  assert.equal((await client.adminGetEvent('evt_1')).viewer.canEdit, eventStaffViewer.canEdit);
  const csv = await client.adminExportRsvpsCsv('evt_1');
  assert.match(csv, /^displayName,handle/);
  fake.state.killed = true;
  await assert.rejects(client.enumerateEvents(), (e) => e.status === 503 && e.code === 'FeatureDisabled');
});

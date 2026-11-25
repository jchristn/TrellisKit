import { act, fireEvent, screen, waitFor } from '@testing-library/react-native';
import { renderRouter } from 'expo-router/testing-library';
import * as Calendar from 'expo-calendar';
import * as SecureStore from 'expo-secure-store';
import path from 'path';
import { Dimensions } from 'react-native';
import { createEventsFake, makeEvent, makeEventCategory } from '@trelliskit/shared/testing';
import { webcalUrl } from '../src/components/events/CalendarFeed';
import { eventCollectionFilters } from '../src/components/events/EventList';
import { proposalBody, toLocalInput } from '../src/components/events/ProposalForm';
import { calendarDetails } from '../src/native/calendar';
import { installFakeServer, makeUser, resetEnvironment, signedInAs } from '../test-utils/fakeServer';
import { propertyWithFeatures } from '../test-utils/property';
import { showMapSection } from '../test-utils/navigation';

const APP_DIR = path.resolve(__dirname, '../app');
const START = '2099-12-05T18:00:00Z';
const END = '2099-12-05T21:00:00Z';
const category = makeEventCategory('meetup', 'Meetup', '#1F5FBF');
const propertyBody = { ...propertyWithFeatures({}), events: { categories: [category], proposals: true, calendarFeedEnabled: true, defaultReminderOffsetsMinutes: [1440, 120] } };

const meetup = makeEvent('evt_1', { title: 'Autumn meetup', startUtc: START, endUtc: END, category: { key: 'meetup', label: 'Meetup', color: '#1F5FBF' }, capacity: 40, spotsLeft: 40 });
const full = makeEvent('evt_2', { title: 'Small workshop', startUtc: START, endUtc: END, capacity: 1, goingCount: 1, spotsLeft: 0, waitlistEnabled: true });
const weekly = makeEvent('evt_3', { title: 'Weekly session', startUtc: START, endUtc: END, recurrence: { id: 'erc_1', rule: { frequency: 'Weekly', interval: 1, weekdays: ['Tue'] }, summary: 'Every week on Tuesday', occurrenceIndex: 1, isException: false, originalStartUtc: START } });
const weekly2 = makeEvent('evt_4', { title: 'Weekly session', startUtc: '2099-12-12T18:00:00Z', endUtc: '2099-12-12T21:00:00Z', recurrence: { ...weekly.recurrence, occurrenceIndex: 2 } });
const locked = makeEvent('evt_5', { title: 'Members night', startUtc: START, endUtc: END, locked: true, description: null, viewer: { ...meetup.viewer, canOpen: false, canRsvp: false, requires: 'Membership' } });

function serve({ user = makeUser(), events = [meetup, full, weekly, weekly2, locked], seed = {} } = {}) {
  const fake = createEventsFake({ events, categories: [category], rsvps: [{ id: 'rsv_9', eventId: 'evt_2', userId: 'usr_9', user: { id: 'usr_9', displayName: 'Other Member' }, status: 'Going', guestCount: 0, createdUtc: '2026-11-01T00:00:00Z' }], ...seed });
  const server = installFakeServer({ 'GET /property': propertyBody, ...signedInAs(user) }, { fallback: (call) => fake.handle({ method: call.method, path: call.path, query: call.query, body: call.body }) });
  return { server, fake };
}

describe('event helpers', () => {
  test('list filters, proposal bodies, calendar fields, and the feed URL', () => {
    expect(eventCollectionFilters('upcoming', { category: ['meetup'], format: ['Online'], hasSpots: true })).toEqual({ when: 'Upcoming', category: ['meetup'], format: ['Online'], hasSpots: true });
    expect(eventCollectionFilters('past', {})).toEqual({ when: 'Past' });
    expect(eventCollectionFilters('mine', {})).toEqual({ upcoming: true });
    expect(toLocalInput('2099-12-05 18:00')).toBe('2099-12-05T18:00');
    expect(toLocalInput('tomorrow')).toBeNull();
    const body = proposalBody({ title: ' Picnic ', summary: '', description: '', category: null, format: 'Online', start: '2099-12-05 18:00', end: '2099-12-05 19:00', timeZone: 'UTC', locationName: '', line1: '', locality: '', countryCode: '', platformLabel: 'Video call', joinUrl: 'https://call.example.test/x', capacity: '' });
    expect(body).toMatchObject({ title: 'Picnic', format: 'Online', startLocal: '2099-12-05T18:00', endLocal: '2099-12-05T19:00', online: { platformLabel: 'Video call', joinUrl: 'https://call.example.test/x' }, capacity: null });
    expect(body.location).toBeUndefined();
    expect(body.recurrence).toBeUndefined();
    const details = calendarDetails(meetup, { url: 'https://example.test/app/events/evt_1' });
    expect(details).toMatchObject({ title: 'Autumn meetup', allDay: false, timeZone: 'America/Los_Angeles', alarms: [{ relativeOffset: -1440 }, { relativeOffset: -120 }] });
    expect(details.location).toContain('Example Hall');
    expect(webcalUrl('https://api.example.test/api/v1/calendar/cfd_1.x.ics')).toBe('webcal://api.example.test/api/v1/calendar/cfd_1.x.ics');
  });
});

describe('events on a phone', () => {
  beforeEach(() => {
    resetEnvironment();
    Calendar.__reset();
  });
  afterEach(() => delete global.fetch);

  async function openEvents() {
    const router = await renderRouter(APP_DIR, { initialUrl: '/map' });
    await showMapSection('events');
    return router;
  }

  test('lists upcoming events with spots, the repeat rule, and the lock', async () => {
    const { server } = serve();
    await openEvents();
    expect(await screen.findByTestId('event-evt_1')).toBeOnTheScreen();
    expect(screen.getByTestId('event-evt_1-capacity')).toHaveTextContent('40 spots left');
    expect(screen.getByTestId('event-evt_2-capacity')).toHaveTextContent('Full');
    expect(screen.getByTestId('event-evt_5-locked')).toBeOnTheScreen();
    expect(screen.getAllByText('Every week on Tuesday', { exact: false }).length).toBe(2);
    expect(server.called('POST /events/enumerate')[0].body).toMatchObject({ maxResults: 25, ordering: 'StartAscending', filters: { when: 'Upcoming' } });
    await fireEvent.press(screen.getByTestId('events-mode-mine'));
    await waitFor(() => expect(server.called('POST /me/events/enumerate').length).toBeGreaterThan(0));
  });

  test('RSVP going, then not going; a full event offers the waitlist', async () => {
    const { server } = serve();
    await renderRouter(APP_DIR, { initialUrl: '/events/evt_1' });
    expect(await screen.findByTestId('event-title')).toHaveTextContent('Autumn meetup');
    await fireEvent(screen.getByTestId('rsvp-hide'), 'valueChange', true);
    await fireEvent.press(screen.getByTestId('rsvp-going'));
    expect(await screen.findByTestId('rsvp-status')).toHaveTextContent('You are going.');
    expect(server.called('PUT /events/evt_1/rsvp')[0].body).toEqual({ status: 'Going', hideFromAttendeeLists: true });
    await fireEvent.press(screen.getByTestId('rsvp-cancel'));
    await waitFor(() => expect(screen.queryByTestId('rsvp-status')).toBeNull());
    expect(server.called('DELETE /events/evt_1/rsvp')).toHaveLength(1);
  });

  test('joining the waitlist of a full event shows the position', async () => {
    serve();
    await renderRouter(APP_DIR, { initialUrl: '/events/evt_2' });
    expect(await screen.findByTestId('rsvp-going')).toHaveTextContent('Join the waitlist');
    await fireEvent.press(screen.getByTestId('rsvp-going'));
    expect(await screen.findByTestId('rsvp-status')).toHaveTextContent(/waitlist/);
  });

  test('add to calendar creates the device event once, then updates it', async () => {
    serve();
    await renderRouter(APP_DIR, { initialUrl: '/events/evt_1' });
    await fireEvent.press(await screen.findByTestId('event-calendar'));
    expect(await screen.findByTestId('event-calendar-note')).toHaveTextContent('Added to your calendar.');
    expect(Calendar.__created).toHaveLength(1);
    expect(Calendar.__created[0]).toMatchObject({ title: 'Autumn meetup' });
    expect(SecureStore.__store.get('tk.calendarEvent.evt_1')).toBe('dev_evt_1');
    expect(screen.getByTestId('event-calendar')).toHaveTextContent('Update in calendar');
    await fireEvent.press(screen.getByTestId('event-calendar'));
    await waitFor(() => expect(screen.getByTestId('event-calendar-note')).toHaveTextContent('Your calendar was updated.'));
    expect(Calendar.__created).toHaveLength(1);
    expect(Calendar.__updated).toHaveLength(1);
  });

  test('add-only calendar access on iOS 17 adds each occurrence once, then offers Added and opens the calendar', async () => {
    const { Linking, Platform } = require('react-native');
    const version = jest.spyOn(Platform, 'Version', 'get').mockReturnValue('17.2');
    expect(Platform.OS).toBe('ios');
    Calendar.getCalendarPermissions = jest.fn(async (writeOnly) => ({ granted: Boolean(writeOnly), status: writeOnly ? 'granted' : 'denied' }));
    const openURL = jest.spyOn(Linking, 'openURL').mockResolvedValue(true);
    try {
      serve();
      await renderRouter(APP_DIR, { initialUrl: '/events/evt_1' });
      await fireEvent.press(await screen.findByTestId('event-calendar'));
      expect(await screen.findByTestId('event-calendar-note')).toHaveTextContent('Added to your calendar.');
      expect(Calendar.requestCalendarPermissions).toHaveBeenLastCalledWith(true);
      expect(Calendar.__created).toHaveLength(1);
      expect(SecureStore.__store.get('tk.calendarEvent.evt_1')).toBe('dev_evt_1');
      await waitFor(() => expect(screen.getByTestId('event-calendar')).toHaveTextContent('Added to calendar'));
      await fireEvent.press(screen.getByTestId('event-calendar'));
      await waitFor(() => expect(openURL).toHaveBeenCalledWith('calshow:' + Math.floor(Date.parse(START) / 1000 - 978307200)));
      expect(Calendar.__created).toHaveLength(1);
      expect(Calendar.__updated).toHaveLength(0);

      // Calling the module directly (another screen, a second tap before the label changed) never duplicates.
      const { saveToDeviceCalendar } = require('../src/native/calendar');
      expect(await saveToDeviceCalendar(meetup)).toBe('alreadyAdded');
      expect(Calendar.__created).toHaveLength(1);
      // Another occurrence is its own device event.
      expect(await saveToDeviceCalendar(weekly)).toBe('added');
      expect(Calendar.__created).toHaveLength(2);
    } finally {
      delete Calendar.getCalendarPermissions;
      openURL.mockRestore();
      version.mockRestore();
    }
  });

  test('with full calendar access a stored event is updated, not added again', async () => {
    Calendar.getCalendarPermissions = jest.fn(async () => ({ granted: true, status: 'granted' }));
    const { Platform } = require('react-native');
    const version = jest.spyOn(Platform, 'Version', 'get').mockReturnValue('17.2');
    expect(Platform.OS).toBe('ios');
    try {
      const { saveToDeviceCalendar, hasAddOnlyAccess } = require('../src/native/calendar');
      expect(await hasAddOnlyAccess()).toBe(false);
      expect(await saveToDeviceCalendar(meetup)).toBe('added');
      expect(await saveToDeviceCalendar(meetup)).toBe('updated');
      expect(Calendar.__created).toHaveLength(1);
      expect(Calendar.__updated).toHaveLength(1);
    } finally {
      delete Calendar.getCalendarPermissions;
      version.mockRestore();
    }
  });

  test('a denied calendar permission explains how to turn it on', async () => {
    Calendar.requestCalendarPermissions.mockResolvedValueOnce({ granted: false, status: 'denied' });
    serve();
    await renderRouter(APP_DIR, { initialUrl: '/events/evt_1' });
    await fireEvent.press(await screen.findByTestId('event-calendar'));
    expect(await screen.findByTestId('event-calendar-note')).toHaveTextContent(/Calendar permission is off/);
  });

  test('a repeating event shows its rule and its other dates, each with its own RSVP', async () => {
    const { server } = serve();
    const router = await renderRouter(APP_DIR, { initialUrl: '/events/evt_3' });
    expect(await screen.findByTestId('event-recurrence')).toHaveTextContent(/Every week on Tuesday/);
    expect(screen.getByTestId('event-recurrence')).toHaveTextContent(/Each date has its own RSVP/);
    await waitFor(() => expect(server.called('POST /events/evt_3/occurrences/enumerate').length).toBe(1));
    const other = await screen.findByTestId('occurrence-evt_4');
    await fireEvent.press(other);
    await waitFor(() => expect(router.getPathname()).toBe('/events/evt_4'));
  });

  test('a locked event shows the membership prompt and loads no comments', async () => {
    const { server } = serve();
    await renderRouter(APP_DIR, { initialUrl: '/events/evt_5' });
    expect(await screen.findByTestId('event-locked-prompt')).toBeOnTheScreen();
    expect(server.calls.some((c) => c.path.startsWith('/events/evt_5/comments'))).toBe(false);
  });

  test('the events kill switch shows the unavailable message', async () => {
    serve({ seed: { killed: true } });
    await openEvents();
    expect(await screen.findByTestId('events-error')).toHaveTextContent(/Events are temporarily unavailable\./);
  });

  test('propose an event: validation, then the review note and My proposals with withdraw', async () => {
    const { server } = serve();
    const router = await renderRouter(APP_DIR, { initialUrl: '/events/propose' });
    expect(await screen.findByTestId('proposal-review-note')).toHaveTextContent(/reviewed before it is published/);
    await fireEvent.press(screen.getByTestId('proposal-send'));
    expect(await screen.findByText('Enter a title of at least 3 characters.')).toBeOnTheScreen();
    await fireEvent.changeText(screen.getByTestId('proposal-title'), 'Park picnic');
    await fireEvent.changeText(screen.getByTestId('proposal-start'), '2099-06-01 12:00');
    await fireEvent.changeText(screen.getByTestId('proposal-end'), '2099-06-01 15:00');
    await fireEvent.press(screen.getByTestId('proposal-zone'));
    await fireEvent.press(await screen.findByTestId('proposal-zone-option-America/New_York'));
    await fireEvent.changeText(screen.getByTestId('proposal-place'), 'North Park');
    await fireEvent.press(screen.getByTestId('proposal-send'));
    expect(await screen.findByTestId('proposal-sent')).toBeOnTheScreen();
    expect(server.called('POST /events/proposals')[0].body).toMatchObject({ title: 'Park picnic', startLocal: '2099-06-01T12:00', timeZone: 'America/New_York', location: { name: 'North Park' } });
    await fireEvent.press(screen.getByTestId('proposal-to-proposals'));
    await waitFor(() => expect(router.getPathname()).toBe('/events/proposals'));
    const status = await screen.findByText('Waiting for review');
    expect(status).toBeOnTheScreen();
    const withdraw = screen.getAllByText('Withdraw')[0];
    await fireEvent.press(withdraw);
    await waitFor(() => expect(server.calls.some((c) => /\/events\/proposals\/.+\/withdraw$/.test(c.path))).toBe(true));
  });

  test('the calendar feed shows the private link and rotates it', async () => {
    const { server } = serve();
    await renderRouter(APP_DIR, { initialUrl: '/events/calendar-feed' });
    const first = (await screen.findByTestId('calendar-feed-url')).props.children;
    await fireEvent.press(screen.getByTestId('calendar-feed-rotate'));
    await waitFor(() => expect(screen.getByTestId('calendar-feed-url').props.children).not.toBe(first));
    expect(server.called('POST /me/calendar-feed/rotate')).toHaveLength(1);
  });
});

describe('events on a tablet', () => {
  const original = Dimensions.get('window');
  const screenSize = Dimensions.get('screen');
  beforeEach(() => {
    resetEnvironment();
    Dimensions.set({ window: { ...original, width: 1100, height: 820 }, screen: { ...screenSize, width: 1100, height: 820 } });
  });
  afterEach(async () => {
    await act(async () => Dimensions.set({ window: original, screen: screenSize }));
    delete global.fetch;
  });

  test('the list and the selected event sit side by side', async () => {
    serve();
    const router = await renderRouter(APP_DIR, { initialUrl: '/map' });
    await showMapSection('events');
    expect(await screen.findByTestId('events-select')).toBeOnTheScreen();
    await fireEvent.press(await screen.findByTestId('event-evt_1'));
    expect(await screen.findByTestId('event-title')).toHaveTextContent('Autumn meetup');
    expect(router.getPathname()).toBe('/map');
  });
});

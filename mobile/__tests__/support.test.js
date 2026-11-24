import { fireEvent, screen, waitFor, within } from '@testing-library/react-native';
import { createSupportFake } from '@trelliskit/shared/testing';
import * as Device from 'expo-device';
import { renderRouter } from 'expo-router/testing-library';
import path from 'path';
import { Platform } from 'react-native';
import strings from '../src/strings';
import { makeUser, resetEnvironment } from '../test-utils/fakeServer';
import { atWidth, me, phase5Property, serveFakes } from '../test-utils/phase5';
import { property } from '../test-utils/property';

const APP_DIR = path.resolve(__dirname, '../app');
const t = strings.support;

function create(fake, body, headers = {}) {
  return fake.handle({ method: 'POST', path: '/tickets', body: { type: 'Billing', title: 'Charged twice', body: 'I was charged twice this month.', mediaIds: [], includeDiagnostics: false, ...body }, headers }).body;
}

beforeEach(() => resetEnvironment());
afterEach(() => delete global.fetch);

describe('new support request', () => {
  test('sends the type, subject, description, and device diagnostics by default, then opens the ticket', async () => {
    const fake = createSupportFake({ me });
    serveFakes([fake]);
    const router = await renderRouter(APP_DIR, { initialUrl: '/support' });
    expect(await screen.findByTestId('support-empty')).toHaveTextContent(t.empty);
    await fireEvent.press(screen.getByTestId('support-new'));
    await fireEvent.press(await screen.findByTestId('new-ticket-type-Billing'));
    await fireEvent.changeText(screen.getByTestId('new-ticket-title'), 'Charged twice');
    await fireEvent.changeText(screen.getByTestId('new-ticket-body'), 'The store charged me twice this month.');
    expect(screen.getByTestId('new-ticket-diagnostics').props.value).toBe(true);
    await fireEvent.press(screen.getByTestId('new-ticket-submit'));
    const ticket = await waitFor(() => {
      expect(fake.state.tickets).toHaveLength(1);
      return fake.state.tickets[0];
    });
    const call = fake.calls.find((c) => c.method === 'POST' && c.path === '/tickets');
    expect(call.body).toMatchObject({ type: 'Billing', title: 'Charged twice', body: 'The store charged me twice this month.', includeDiagnostics: true, mediaIds: [] });
    expect(call.headers['x-client-platform']).toBe(Platform.OS === 'android' ? 'Android' : 'Ios');
    expect(call.headers['x-client-os-version']).toBe(Platform.Version === undefined || Platform.Version === null ? undefined : String(Platform.Version));
    expect(call.headers['x-client-device-model']).toBe(Device.modelName);
    await waitFor(() => expect(router.getPathname()).toBe('/support/tickets/' + ticket.id));
    expect(await screen.findByTestId('ticket-detail-title')).toHaveTextContent('Charged twice');
    expect(screen.getByTestId('ticket-detail-device')).toHaveTextContent(t.diagnosticsAttached);
    expect(ticket.device).toMatchObject({ client: 'mobile', deviceModel: Device.modelName });
  });

  test('turning diagnostics off sends no device headers, and the form checks its fields', async () => {
    const fake = createSupportFake({ me });
    serveFakes([fake]);
    await renderRouter(APP_DIR, { initialUrl: '/support/new?type=Bug' });
    await screen.findByTestId('new-ticket-type-Bug');
    await fireEvent.press(screen.getByTestId('new-ticket-submit'));
    expect(await screen.findByText(t.subjectTooShort)).toBeOnTheScreen();
    expect(screen.getByText(t.bodyTooShort)).toBeOnTheScreen();
    await fireEvent.changeText(screen.getByTestId('new-ticket-title'), 'App crashes');
    await fireEvent.changeText(screen.getByTestId('new-ticket-body'), 'It closes when I open a video.');
    await fireEvent(screen.getByTestId('new-ticket-diagnostics'), 'valueChange', false);
    await fireEvent.press(screen.getByTestId('new-ticket-submit'));
    await waitFor(() => expect(fake.state.tickets).toHaveLength(1));
    const call = fake.calls.find((c) => c.method === 'POST' && c.path === '/tickets');
    expect(call.body).toMatchObject({ type: 'Bug', includeDiagnostics: false });
    expect(call.headers['x-client-platform']).toBeUndefined();
    expect(fake.state.tickets[0].device).toBeNull();
  });
});

describe('ticket detail', () => {
  test('shows staff replies as the support team, sends a reply, closes, and reopens', async () => {
    const fake = createSupportFake({ me });
    const ticket = create(fake);
    fake.handle({ method: 'POST', path: '/admin/tickets/' + ticket.id + '/messages', body: { kind: 'Reply', body: 'We refunded the second charge.' } });
    fake.handle({ method: 'POST', path: '/admin/tickets/' + ticket.id + '/messages', body: { kind: 'Note', body: 'Internal: refund issued' } });
    serveFakes([fake]);
    await renderRouter(APP_DIR, { initialUrl: '/support/tickets/' + ticket.id });
    expect(await screen.findByText('We refunded the second charge.')).toBeOnTheScreen();
    expect(screen.queryByText('Internal: refund issued')).toBeNull();
    expect(screen.getByText(t.staffName('Support Agent'))).toBeOnTheScreen();
    expect(screen.getByTestId('ticket-detail-status')).toHaveTextContent(t.statusFilter + ': ' + t.statuses.WaitingOnMember);

    await fireEvent.changeText(screen.getByTestId('ticket-detail-reply'), 'Thank you!');
    await fireEvent.press(screen.getByTestId('ticket-detail-send'));
    expect(await screen.findByText('Thank you!')).toBeOnTheScreen();
    await waitFor(() => expect(screen.getByTestId('ticket-detail-status')).toHaveTextContent(t.statusFilter + ': ' + t.statuses.Open));

    await fireEvent.press(screen.getByTestId('ticket-detail-close'));
    expect(await screen.findByTestId('ticket-detail-closed')).toHaveTextContent(t.closedNotice);
    expect(screen.getByTestId('ticket-detail-notice')).toHaveTextContent(t.closed);
    await fireEvent.press(screen.getByTestId('ticket-detail-reopen'));
    await waitFor(() => expect(screen.getByTestId('ticket-detail-status')).toHaveTextContent(t.statusFilter + ': ' + t.statuses.Open));
    expect(screen.getByTestId('ticket-detail-reply')).toBeOnTheScreen();
  });

  test('the opening text shows once: it is the first message, not also a separate body card', async () => {
    const fake = createSupportFake({ me });
    const ticket = create(fake, { body: 'Opening text only once.' });
    serveFakes([fake]);
    await renderRouter(APP_DIR, { initialUrl: '/support/tickets/' + ticket.id });
    expect(await screen.findByText('Opening text only once.')).toBeOnTheScreen();
    expect(screen.getAllByText('Opening text only once.')).toHaveLength(1);
    expect(screen.queryByTestId('ticket-detail-body')).toBeNull();
  });

  test('a ticket without messages still shows its opening text', async () => {
    const fake = createSupportFake({ me });
    const ticket = create(fake, { body: 'Body without messages.' });
    serveFakes([fake], { routes: { ['POST /tickets/' + ticket.id + '/messages/enumerate']: { objects: [], continuationToken: null, endOfResults: true, totalRecords: 0 } } });
    await renderRouter(APP_DIR, { initialUrl: '/support/tickets/' + ticket.id });
    expect(await screen.findByTestId('ticket-detail-body')).toHaveTextContent('Body without messages.', { exact: false });
  });

  test('status chips filter the list', async () => {
    const fake = createSupportFake({ me });
    create(fake, { title: 'First request' });
    serveFakes([fake]);
    await renderRouter(APP_DIR, { initialUrl: '/support' });
    expect(await screen.findByText('First request')).toBeOnTheScreen();
    await fireEvent.press(screen.getByTestId('support-status-Closed'));
    await waitFor(() => expect(fake.calls.filter((c) => c.path === '/tickets/enumerate').at(-1).body.filters).toEqual({ status: ['Closed'] }));
    expect(await screen.findByTestId('support-empty')).toBeOnTheScreen();
  });
});

describe('support in every access model', () => {
  test('a non-member in a fully gated property reaches support, and the paywall links to it', async () => {
    const fake = createSupportFake({ me });
    const unpaid = makeUser({ membership: { isActive: false, source: null, store: null, expiresUtc: null }, membershipRequired: true });
    serveFakes([fake], { user: unpaid, propertyDoc: phase5Property({ access: { model: 'FullyGated', gatedFeatures: [] } }) });
    const router = await renderRouter(APP_DIR, { initialUrl: '/' });
    await waitFor(() => expect(router.getPathname()).toBe('/subscribe'));
    await fireEvent.press(await screen.findByTestId('paywall-help'));
    await waitFor(() => expect(router.getPathname()).toBe('/support'));
    expect(await screen.findByTestId('support-new')).toBeOnTheScreen();
  });

  test('features.support off shows the property support email instead', async () => {
    const fake = createSupportFake({ me });
    serveFakes([fake], { propertyDoc: phase5Property({ features: { support: false } }) });
    await renderRouter(APP_DIR, { initialUrl: '/support' });
    expect(await screen.findByTestId('support-email')).toBeOnTheScreen();
    expect(screen.getByTestId('support-email-address')).toHaveTextContent(property.contact.supportEmail);
    expect(fake.calls.some((c) => c.path.startsWith('/tickets') || c.path.startsWith('/support'))).toBe(false);
  });

  test('Account and Profile link to support', async () => {
    const fake = createSupportFake({ me });
    serveFakes([fake]);
    const router = await renderRouter(APP_DIR, { initialUrl: '/account' });
    await fireEvent.press(await screen.findByTestId('account-support'));
    await waitFor(() => expect(router.getPathname()).toBe('/support'));
  });
});

describe('support on a tablet', () => {
  test('the ticket list and the ticket sit side by side', async () => {
    const fake = createSupportFake({ me });
    const ticket = create(fake, { title: 'Side by side' });
    serveFakes([fake]);
    await atWidth(1180, async () => {
      await renderRouter(APP_DIR, { initialUrl: '/support' });
      expect(await screen.findByTestId('support-select')).toBeOnTheScreen();
      await fireEvent.press(await screen.findByTestId('ticket-' + ticket.id));
      const pane = await screen.findByTestId('split-detail-pane');
      expect(await within(pane).findByTestId('ticket-detail-title')).toHaveTextContent('Side by side');
      expect(screen.getByTestId('support-filters-side-panel')).toBeOnTheScreen();
    });
  });
});

import { fireEvent, render, screen, waitFor } from '@testing-library/react-native';
import fs from 'fs';
import path from 'path';
import { qrMatrix } from '@trelliskit/shared';
import CheckInScreen from '../src/components/ranks/CheckInScreen';
import { CheckInControls, mondayFirstDay, venueSessionCheckIn } from '../src/components/ranks/CheckIn';
import QrCodeView from '../src/components/ranks/QrCodeView';
import { resolveAppLink } from '../src/linkTargets';

jest.mock('react-native-safe-area-context', () => ({ ...jest.requireActual('react-native-safe-area-context'), useSafeAreaInsets: () => ({ top: 0, right: 0, bottom: 0, left: 0 }) }));
const mockAuth = { api: null, user: { id: 'usr_me', roles: [{ role: 'User' }] } };
const mockRouter = { push: jest.fn(), replace: jest.fn() };
jest.mock('../src/auth/AuthProvider', () => ({ useAuth: () => mockAuth, useOptionalAuth: () => mockAuth }));
jest.mock('expo-router', () => ({ useRouter: () => mockRouter }));
const mockProperty = { value: {} };
jest.mock('../src/property', () => ({ ...jest.requireActual('../src/property'), useProperty: () => mockProperty.value }));

const property = { domains: { web: 'example.test' }, profileFields: [], features: { ranks: true } };
const page = (objects) => ({ objects, continuationToken: null, endOfResults: true, totalRecords: objects.length });

beforeEach(() => {
  mockProperty.value = property;
  mockAuth.user = { id: 'usr_me', roles: [{ role: 'User' }] };
  mockAuth.api = {};
  mockRouter.replace.mockClear();
});

test('no camera or barcode module is added, so the app stays update-compatible', () => {
  const pkg = JSON.parse(fs.readFileSync(path.resolve(__dirname, '../package.json'), 'utf8'));
  const deps = Object.keys({ ...pkg.dependencies, ...pkg.devDependencies });
  expect(deps.filter((name) => /camera|barcode|qr|vision-camera|svg/i.test(name))).toEqual([]);
});

test('the check-in route is registered on the root stack with a title', () => {
  const layout = fs.readFileSync(path.resolve(__dirname, '../app/_layout.jsx'), 'utf8');
  expect(layout).toMatch(/<Stack\.Screen name="check-in" options=\{\{ title: strings\.ranks\.checkIn\.title \}\} \/>/);
  expect(layout).toMatch(/<Stack\.Screen name="rank" options=\{\{ headerShown: false \}\} \/>/);
  expect(layout).toMatch(/<Stack\.Screen name="curricula" options=\{\{ headerShown: false \}\} \/>/);
});

test('the scanned universal link opens the native check-in route', () => {
  const link = 'https://example.test/app/check-in?t=VenueSession&id=ven_1%3A2&d=2026-11-25&c=123456';
  expect(resolveAppLink(link, property)).toEqual({ kind: 'native', path: '/check-in?t=VenueSession&id=ven_1%3A2&d=2026-11-25&c=123456' });
  expect(resolveAppLink('https://example.test/app/rank', property)).toEqual({ kind: 'native', path: '/rank' });
  expect(resolveAppLink('/app/curricula/cur_1', property)).toEqual({ kind: 'native', path: '/curricula/cur_1' });
  expect(resolveAppLink('/check-in?t=Event&id=evt_1', property)).toEqual({ kind: 'native', path: '/check-in?t=Event&id=evt_1' });
});

test('opening the route with a code checks the member in once', async () => {
  mockAuth.api = { checkIn: jest.fn().mockResolvedValue({ id: 'att_1' }) };
  await render(<CheckInScreen params={{ t: 'VenueSession', id: 'ven_1:2', d: '2026-11-25', c: '123456' }} />);
  expect(await screen.findByTestId('check-in-done')).toHaveTextContent('You are checked in.');
  expect(mockAuth.api.checkIn).toHaveBeenCalledTimes(1);
  expect(mockAuth.api.checkIn).toHaveBeenCalledWith({ targetType: 'VenueSession', targetId: 'ven_1:2', occurrenceDate: '2026-11-25', code: '123456', method: 'QR' });
  await fireEvent.press(screen.getByTestId('check-in-my-rank'));
  expect(mockRouter.replace).toHaveBeenCalledWith('/rank');
});

test('an expired code falls back to code entry; invalid links are refused', async () => {
  mockAuth.api = { checkIn: jest.fn().mockRejectedValueOnce({ status: 400, code: 'InvalidCode' }).mockResolvedValueOnce({ id: 'att_1' }) };
  await render(<CheckInScreen params={{ t: 'Event', id: 'evt_1', c: '111111' }} />);
  expect(await screen.findByTestId('check-in-error')).toHaveTextContent(/That number is not valid/);
  await fireEvent.changeText(screen.getByTestId('check-in-form-code'), '22-2222');
  await fireEvent.press(screen.getByTestId('check-in-form-submit'));
  await waitFor(() => expect(mockAuth.api.checkIn).toHaveBeenLastCalledWith({ targetType: 'Event', targetId: 'evt_1', code: '222222', method: 'SelfWithCode' }));
  expect(await screen.findByTestId('check-in-done')).toBeOnTheScreen();
  await render(<CheckInScreen params={{ t: 'Room', id: 'x' }} />);
  expect(screen.getByTestId('check-in-invalid')).toBeOnTheScreen();
});

test('members check in from an event with a code; staff controls stay hidden', async () => {
  mockAuth.api = { checkIn: jest.fn().mockResolvedValue({ id: 'att_1' }) };
  await render(<CheckInControls targetType="Event" targetId="evt_1" title="Open session" testID="event-check-in" />);
  expect(screen.queryByTestId('event-check-in-staff')).toBeNull();
  await fireEvent.press(screen.getByLabelText('Check in to Open session'));
  await fireEvent.press(screen.getByTestId('event-check-in-form-submit'));
  expect(screen.getByText('Enter the six digits of the check-in number.')).toBeOnTheScreen();
  await fireEvent.changeText(screen.getByTestId('event-check-in-form-code'), '654321');
  await fireEvent.press(screen.getByTestId('event-check-in-form-submit'));
  expect(await screen.findByTestId('event-check-in-done')).toHaveTextContent('You are checked in to Open session.');
  expect(mockAuth.api.checkIn).toHaveBeenCalledWith({ targetType: 'Event', targetId: 'evt_1', code: '654321', method: 'SelfWithCode' });
});

test('check-in is hidden when ranks are off or nobody is signed in', async () => {
  mockProperty.value = { ...property, features: {} };
  await render(<CheckInControls targetType="Event" targetId="evt_1" testID="off" />);
  expect(screen.queryByTestId('off')).toBeNull();
  mockProperty.value = property;
  mockAuth.user = null;
  await render(<CheckInControls targetType="Event" targetId="evt_1" testID="guest" />);
  expect(screen.queryByTestId('guest')).toBeNull();
});

test('rank staff show a rotating QR code of the universal link with the roster', async () => {
  mockAuth.user = { id: 'usr_staff', roles: [{ role: 'RankAwarder', resourceType: 'Ranks', resourceId: '*' }] };
  mockAuth.api = {
    getCheckInCode: jest.fn().mockResolvedValue({ targetType: 'Event', targetId: 'evt_1', code: '482913', expiresUtc: new Date(Date.now() + 45000).toISOString(), qrPayload: 'checkin?targetType=Event&targetId=evt_1&code=482913' }),
    enumerateAttendance: jest.fn().mockResolvedValue(page([{ id: 'att_1', userId: 'usr_2', user: { displayName: 'Checked Member' }, method: 'QR' }]))
  };
  await render(<CheckInControls targetType="Event" targetId="evt_1" title="Open session" testID="staff" />);
  await fireEvent.press(screen.getByTestId('staff-staff'));
  expect(await screen.findByTestId('staff-staff-panel-code')).toHaveTextContent('482913');
  expect(screen.getByLabelText('Check-in QR image')).toBeOnTheScreen();
  expect(screen.getByText(/New number in \d+ seconds/)).toBeOnTheScreen();
  expect(await screen.findByText('Checked Member · QR scan')).toBeOnTheScreen();
  expect(mockAuth.api.getCheckInCode).toHaveBeenCalledWith({ targetType: 'Event', targetId: 'evt_1' });
  expect(mockAuth.api.enumerateAttendance.mock.calls[0][0]).toMatchObject({ maxResults: 50, filters: { targetType: 'Event', targetId: 'evt_1' } });
});

test('the QR view draws one view per run of dark modules', async () => {
  const value = 'https://example.test/app/check-in?t=Event&id=evt_1&c=123456';
  await render(<QrCodeView value={value} size={200} label="Code" />);
  const qr = qrMatrix(value);
  const runs = qr.modules.reduce((n, row) => n + row.filter((dark, i) => dark && !row[i - 1]).length, 0);
  expect(screen.getByLabelText('Code').props.children).toHaveLength(runs);
});

test('venue sessions get check-in only on their weekday, with the session index target', async () => {
  const now = new Date(2026, 10, 25, 9);
  const today = mondayFirstDay(now);
  const action = venueSessionCheckIn({ id: 'ven_1' }, now);
  expect(action({ name: 'Other', dayOfWeek: (today + 1) % 7 }, 4)).toBeNull();
  const element = action({ name: 'Today', dayOfWeek: today }, 3);
  expect(element.props).toMatchObject({ targetType: 'VenueSession', targetId: 'ven_1:3', occurrenceDate: '2026-11-25' });
});

import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { RankCard } from '../src/views/ranks/RankCard.jsx';
import { CurriculumCard } from '../src/views/ranks/CurriculumCard.jsx';
import { RankProgressPanel } from '../src/views/ranks/RankProgressPanel.jsx';
import { QrCodeSvg } from '../src/views/ranks/QrCodeSvg.jsx';
import MyRankView from '../src/views/ranks/MyRankView.jsx';
import CheckInView from '../src/views/ranks/CheckInView.jsx';
import { CurriculumDetailView } from '../src/views/ranks/CurriculaViews.jsx';
import { CheckInControls, mondayFirstDay, venueSessionCheckIn } from '../src/views/ranks/CheckIn.jsx';
import { lineageBody } from '../src/views/ranks/LineageEditor.jsx';
import { clearMemberRankCache } from '../src/views/ranks/rankHooks.js';
import { CardFields, ProfileFieldList } from '../src/components/profile/ProfileFieldDisplay.jsx';
import { ProfileFieldsEditor } from '../src/components/profile/ProfileFieldInputs.jsx';
import { featureEnabled } from '../src/views/member/memberRoutes.js';

const propertyState = vi.hoisted(() => ({ property: {}, fill: (text) => text }));
vi.mock('../src/context/PropertyContext.jsx', () => ({ useProperty: () => propertyState }));
const auth = vi.hoisted(() => ({ client: null, me: { id: 'usr_me', roles: [{ role: 'User' }] } }));
vi.mock('../src/context/AuthContext.jsx', () => ({ useAuth: () => auth }));

const rankField = {
  key: 'level', label: 'Level', type: 'Rank', showOnCard: true, showOnProfile: true, profileOrder: 5,
  options: [{ key: 'first', label: 'First', color: '#cccccc', maxLevel: 2 }, { key: 'second', label: 'Second', color: '#3355aa' }, { key: 'third', label: 'Third', color: '#111111' }],
  rank: { levelLabel: 'Marks', maxLevel: 4 }
};
const property = (extra = {}) => ({ domains: { web: 'example.test' }, mobile: { scheme: 'exampleapp' }, profileFields: [rankField], features: { ranks: true, rankVerificationLabels: true, ...extra } });
const effective = { fieldKey: 'level', fieldLabel: 'Level', optionKey: 'first', optionLabel: 'First', color: '#cccccc', level: 1, maxLevel: 2, rankLabel: 'First, 1 Marks', verified: true, source: 'Verified', status: 'Verified', awarder: { id: 'usr_staff', displayName: 'Staff Member' }, awardedUtc: '2026-10-01T00:00:00Z' };
const next = { optionKey: 'first', optionLabel: 'First', level: 2, rankLabel: 'First, 2 Marks', requirement: { minimumDaysInGrade: 30, minimumAttendance: 10 }, daysInGrade: 40, daysInGradeMet: true, attendance: 4, attendanceMet: false, curriculum: null, eligible: false };
const page = (objects, continuationToken = null, totalRecords = objects.length) => ({ objects, continuationToken, endOfResults: !continuationToken, totalRecords });

beforeEach(() => {
  clearMemberRankCache();
  propertyState.property = property();
  auth.me = { id: 'usr_me', roles: [{ role: 'User' }] };
  auth.client = {};
});

test('ranks stay off unless the property opts in', () => {
  expect(featureEnabled({ features: {} }, 'ranks')).toBe(false);
  expect(featureEnabled({ features: { ranks: true } }, 'ranks')).toBe(true);
  propertyState.property = property({ ranks: false });
  const { container } = render(<MemoryRouter><CheckInControls targetType="Event" targetId="evt_1" title="Open session" /></MemoryRouter>);
  expect(container).toBeEmptyDOMElement();
});

test('the rank card shows the verified rank, next rank, and requirement progress bars', () => {
  render(<MemoryRouter><RankCard rank={effective} next={next} /></MemoryRouter>);
  expect(screen.getByText('First, 1 Marks')).toBeInTheDocument();
  expect(screen.getByText('Verified')).toBeInTheDocument();
  expect(screen.getByText(/Verified by Staff Member/)).toBeInTheDocument();
  expect(screen.getByText('First, 2 Marks')).toBeInTheDocument();
  const bars = screen.getAllByRole('progressbar');
  expect(bars).toHaveLength(2);
  expect(bars[1]).toHaveAttribute('aria-valuenow', '4');
  expect(bars[1]).toHaveAttribute('aria-valuemax', '10');
});

test('verification labels hide when the server turns them off', () => {
  render(<MemoryRouter><RankCard rank={{ ...effective, verified: false, source: 'SelfReported' }} next={null} showVerificationLabels={false} /></MemoryRouter>);
  expect(screen.queryByText('Self-reported')).toBeNull();
  expect(screen.getByText('You hold the highest rank.')).toBeInTheDocument();
});

test('profile fields and member cards show verified or self-reported labels next to the rank', () => {
  const profile = { level: { key: 'second', level: 0 } };
  const { rerender } = render(<MemoryRouter><CardFields fields={[rankField]} profile={profile} rankBadges={{ level: 'selfReported' }} /></MemoryRouter>);
  expect(screen.getByText('Second')).toBeInTheDocument();
  expect(screen.getByText('Self-reported')).toBeInTheDocument();
  rerender(<MemoryRouter><ProfileFieldList fields={[rankField]} profile={profile} rankBadges={{ level: 'verified' }} /></MemoryRouter>);
  expect(screen.getByText('Verified')).toBeInTheDocument();
  rerender(<MemoryRouter><ProfileFieldList fields={[rankField]} profile={profile} /></MemoryRouter>);
  expect(screen.queryByText('Verified')).toBeNull();
});

test('a verified rank field is read-only with an explanation in the profile editor', () => {
  render(<ProfileFieldsEditor fields={[rankField]} values={{ level: { key: 'first', level: 1 } }} onChange={() => {}} idPrefix="profile" locked={{ level: 'Your Level was verified and can only change with a new award from rank staff.' }} />);
  expect(screen.getByLabelText(/Level/, { selector: 'select#profile-level' })).toBeDisabled();
  expect(screen.getByText('Your Level was verified and can only change with a new award from rank staff.')).toBeInTheDocument();
});

test('curriculum and progress panels draw SVG progress bars', () => {
  render(<MemoryRouter>
    <CurriculumCard curriculum={{ id: 'cur_1', name: 'Foundations', itemCount: 4, progress: { completed: 1, total: 4 } }} />
    <RankProgressPanel rank={{ history: [{ id: 'rkp_1', action: 'Award', rankLabel: 'First, 1 Marks', createdUtc: '2026-10-01T00:00:00Z', actor: { displayName: 'Staff Member' }, note: 'Steady work' }], historyTotal: 3 }} attendanceTotal={7} curricula={[{ id: 'cur_1', name: 'Foundations', progress: { completed: 2, total: 4 } }]} />
  </MemoryRouter>);
  expect(screen.getAllByRole('link', { name: 'Foundations' }).map((a) => a.getAttribute('href'))).toEqual(['/app/curricula/cur_1', '/app/curricula/cur_1']);
  expect(screen.getByText('7 check-ins')).toBeInTheDocument();
  expect(screen.getByText('Steady work')).toBeInTheDocument();
  expect(screen.getByText('Showing the 1 most recent of 3.')).toBeInTheDocument();
  expect(screen.getAllByRole('progressbar').length).toBe(2);
});

test('My rank shows the rank, history, attendance, and saves edited lineage', async () => {
  auth.client = {
    getMyRank: vi.fn().mockResolvedValue({ showVerificationLabels: true, fields: [{ rank: effective, next }], history: [], historyTotal: 0, lineage: [{ id: 'lin_1', fieldKey: 'level', optionKey: 'first', optionLabel: 'First', promotedByName: 'A Teacher', verified: false }] }),
    enumerateMyAttendance: vi.fn().mockResolvedValue(page([], null, 12)),
    putMyLineage: vi.fn().mockResolvedValue({ fieldKey: 'level', entries: [{ id: 'lin_2', fieldKey: 'level', optionKey: 'second', optionLabel: 'Second', promotedByName: 'Someone Else', verified: false }] }),
    searchMembers: vi.fn().mockResolvedValue(page([]))
  };
  render(<MemoryRouter><MyRankView /></MemoryRouter>);
  expect(await screen.findByText('First, 1 Marks')).toBeInTheDocument();
  expect(await screen.findByText('12 check-ins')).toBeInTheDocument();
  expect(screen.getByText(/A Teacher/)).toBeInTheDocument();
  await userEvent.click(screen.getByRole('button', { name: 'Edit lineage' }));
  const form = screen.getByRole('form', { name: 'Edit lineage' });
  await userEvent.selectOptions(within(form).getAllByLabelText('Rank')[0], 'second');
  const name = within(form).getByLabelText('Name (not a member)');
  fireEvent.change(name, { target: { value: 'Someone Else' } });
  fireEvent.change(within(form).getByLabelText('Date'), { target: { value: '2024-03-01' } });
  await userEvent.click(within(form).getByRole('button', { name: 'Save lineage' }));
  await waitFor(() => expect(auth.client.putMyLineage).toHaveBeenCalledWith({ fieldKey: 'level', entries: [{ optionKey: 'second', promotedByName: 'Someone Else', promotedUtc: '2024-03-01T00:00:00Z' }] }));
  expect(await screen.findByText('Lineage saved.')).toBeInTheDocument();
  expect(screen.getByText(/Someone Else/)).toBeInTheDocument();
});

test('lineage bodies name a member or a name, never both', () => {
  expect(lineageBody('level', [{ optionKey: 'first', member: [{ id: 'usr_2' }], name: 'ignored', date: '' }])).toEqual({ fieldKey: 'level', entries: [{ optionKey: 'first', promotedByUserId: 'usr_2' }] });
});

test('curriculum detail groups items by rank, links library items, and self-completes allowed items', async () => {
  auth.client = {
    getCurriculum: vi.fn().mockResolvedValue({ id: 'cur_1', name: 'Foundations', fieldKey: 'level', items: [
      { id: 'cui_1', optionKey: 'second', itemType: 'Lesson', targetId: 'les_1', title: 'Lesson one', required: true, selfCompletable: false, state: 'Incomplete', satisfied: false },
      { id: 'cui_2', optionKey: 'first', itemType: 'Custom', customText: 'Practice the basics', required: false, selfCompletable: true, state: 'Incomplete', satisfied: false },
      { id: 'cui_3', optionKey: 'first', itemType: 'CatalogEntry', targetId: 'cat_1', title: 'Catalog move', required: true, selfCompletable: true, state: 'Verified', satisfied: true }
    ] }),
    completeCurriculumItem: vi.fn().mockResolvedValue({ id: 'cui_2', optionKey: 'first', itemType: 'Custom', customText: 'Practice the basics', required: false, selfCompletable: true, state: 'SelfComplete', satisfied: true })
  };
  render(<MemoryRouter initialEntries={['/app/curricula/cur_1']}><Routes><Route path="/app/curricula/:id" element={<CurriculumDetailView />} /></Routes></MemoryRouter>);
  expect(await screen.findByRole('heading', { name: 'Foundations' })).toBeInTheDocument();
  const groups = screen.getAllByRole('region');
  expect(within(groups[0]).getByRole('heading', { name: /^First/ })).toBeInTheDocument();
  expect(within(groups[1]).getByRole('heading', { name: /^Second/ })).toBeInTheDocument();
  expect(screen.getByRole('link', { name: 'Lesson one' })).toHaveAttribute('href', '/app/lessons/les_1');
  expect(screen.queryByRole('link', { name: 'Catalog move' })).toBeNull();
  expect(screen.queryByRole('button', { name: 'Mark Catalog move complete' })).toBeNull();
  await userEvent.click(screen.getByRole('button', { name: 'Mark Practice the basics complete' }));
  expect(auth.client.completeCurriculumItem).toHaveBeenCalledWith('cur_1', 'cui_2');
  expect(await screen.findByRole('button', { name: 'Mark Practice the basics not complete' })).toHaveAttribute('aria-pressed', 'true');
  expect(screen.getByText('2 of 3 complete')).toBeInTheDocument();
});

test('the check-in link page checks a member in with the scanned code', async () => {
  auth.client = { checkIn: vi.fn().mockResolvedValue({ id: 'att_1' }) };
  render(<MemoryRouter initialEntries={['/app/check-in?t=VenueSession&id=ven_1%3A2&d=2026-11-25&c=123456']}><Routes><Route path="/app/check-in" element={<CheckInView />} /></Routes></MemoryRouter>);
  expect(await screen.findByText('You are checked in.')).toBeInTheDocument();
  expect(auth.client.checkIn).toHaveBeenCalledTimes(1);
  expect(auth.client.checkIn).toHaveBeenCalledWith({ targetType: 'VenueSession', targetId: 'ven_1:2', occurrenceDate: '2026-11-25', code: '123456', method: 'QR' });
  expect(screen.getByRole('link', { name: 'Open in the app' })).toHaveAttribute('href', 'exampleapp://check-in?t=VenueSession&id=ven_1%3A2&d=2026-11-25&c=123456');
});

test('an expired scanned code falls back to code entry, and bad links are refused', async () => {
  auth.client = { checkIn: vi.fn().mockRejectedValueOnce({ status: 400, code: 'InvalidCode' }).mockResolvedValueOnce({ id: 'att_1' }) };
  const { unmount } = render(<MemoryRouter initialEntries={['/app/check-in?t=Event&id=evt_1&c=111111']}><Routes><Route path="/app/check-in" element={<CheckInView />} /></Routes></MemoryRouter>);
  expect(await screen.findByText(/That code is not valid/)).toBeInTheDocument();
  fireEvent.change(screen.getByLabelText('Check-in code'), { target: { value: '22a2222' } });
  await userEvent.click(screen.getByRole('button', { name: 'Check in' }));
  await waitFor(() => expect(auth.client.checkIn).toHaveBeenLastCalledWith({ targetType: 'Event', targetId: 'evt_1', code: '222222', method: 'SelfWithCode' }));
  expect(await screen.findByText('You are checked in.')).toBeInTheDocument();
  unmount();
  render(<MemoryRouter initialEntries={['/app/check-in?t=Room&id=x']}><Routes><Route path="/app/check-in" element={<CheckInView />} /></Routes></MemoryRouter>);
  expect(screen.getByRole('alert')).toHaveTextContent('This check-in link is not valid');
});

test('members enter a code from the event check-in button', async () => {
  auth.client = { checkIn: vi.fn().mockRejectedValue({ status: 409, code: 'AlreadyCheckedIn' }) };
  render(<MemoryRouter><CheckInControls targetType="Event" targetId="evt_1" title="Open session" /></MemoryRouter>);
  expect(screen.queryByRole('button', { name: 'Show check-in code' })).toBeNull();
  await userEvent.click(screen.getByRole('button', { name: 'Check in' }));
  const dialog = screen.getByRole('dialog');
  await userEvent.click(within(dialog).getByRole('button', { name: 'Check in' }));
  expect(within(dialog).getByRole('alert')).toHaveTextContent('Enter the six digits of the code.');
  fireEvent.change(within(dialog).getByLabelText('Check-in code'), { target: { value: '654321' } });
  await userEvent.click(within(dialog).getByRole('button', { name: 'Check in' }));
  expect(await within(dialog).findByText('You are already checked in.')).toBeInTheDocument();
});

test('rank staff see a rotating QR code with the universal link, the code, a countdown, and the roster', async () => {
  auth.me = { id: 'usr_staff', roles: [{ role: 'RankAwarder', resourceType: 'Ranks', resourceId: '*' }] };
  const expires = new Date(Date.now() + 45000).toISOString();
  auth.client = {
    getCheckInCode: vi.fn().mockResolvedValue({ targetType: 'VenueSession', targetId: 'ven_1:0', occurrenceDate: '2026-11-25', code: '482913', expiresUtc: expires, rotationSeconds: 60, qrPayload: 'checkin?targetType=VenueSession&targetId=ven_1%3A0&occurrenceDate=2026-11-25&code=482913' }),
    enumerateAttendance: vi.fn().mockResolvedValue(page([{ id: 'att_1', userId: 'usr_2', user: { id: 'usr_2', displayName: 'Checked Member' }, method: 'QR', checkedInUtc: '2026-11-25T18:00:00Z' }])),
    deleteAttendance: vi.fn()
  };
  render(<MemoryRouter><CheckInControls targetType="VenueSession" targetId="ven_1:0" occurrenceDate="2026-11-25" title="Evening session" /></MemoryRouter>);
  await userEvent.click(screen.getByRole('button', { name: 'Show check-in code' }));
  expect(await screen.findByText('482913')).toBeInTheDocument();
  expect(screen.getByTestId('check-in-qr').querySelector('path').getAttribute('d')).toMatch(/^M4 4h7/);
  expect(screen.getByText(/New code in \d+ seconds/)).toBeInTheDocument();
  expect(await screen.findByText('Checked Member')).toBeInTheDocument();
  expect(auth.client.getCheckInCode).toHaveBeenCalledWith({ targetType: 'VenueSession', targetId: 'ven_1:0', occurrenceDate: '2026-11-25' });
  expect(auth.client.enumerateAttendance.mock.calls[0][0]).toMatchObject({ maxResults: 50, filters: { targetType: 'VenueSession', targetId: 'ven_1:0', occurrenceDate: '2026-11-25' } });
});

test('venue schedule rows get check-in only for today\'s sessions with the session index target', () => {
  const now = new Date(2026, 10, 25, 9);
  const today = mondayFirstDay(now);
  const action = venueSessionCheckIn({ id: 'ven_1' }, now);
  render(<MemoryRouter>{action({ name: 'Today', dayOfWeek: today }, 3)}{action({ name: 'Other', dayOfWeek: (today + 1) % 7 }, 4)}</MemoryRouter>);
  expect(screen.getAllByRole('button', { name: 'Check in' })).toHaveLength(1);
});

test('the QR component draws the encoded link', () => {
  render(<QrCodeSvg value="https://example.test/app/check-in?t=Event&id=evt_1&c=123456" label="Check-in QR code" />);
  const svg = screen.getByRole('img', { name: 'Check-in QR code' });
  expect(svg.getAttribute('viewBox')).toMatch(/^0 0 \d+ \d+$/);
});

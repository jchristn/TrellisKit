import { fireEvent, render, screen, waitFor } from '@testing-library/react-native';
import RankCard from '../src/components/ranks/RankCard';
import CurriculumCard from '../src/components/ranks/CurriculumCard';
import RankProgressPanel from '../src/components/ranks/RankProgressPanel';
import MyRank from '../src/components/ranks/MyRank';
import { CurriculumDetail, curriculumItemRoute } from '../src/components/ranks/Curricula';
import { clearMemberRankCache } from '../src/components/ranks/rankHooks';
import { CardFields, ProfileFieldList } from '../src/components/profile/ProfileDisplay';
import strings from '../src/strings';

jest.mock('react-native-safe-area-context', () => ({ ...jest.requireActual('react-native-safe-area-context'), useSafeAreaInsets: () => ({ top: 0, right: 0, bottom: 0, left: 0 }) }));
const mockAuth = { api: null, user: { id: 'usr_me', roles: [{ role: 'User' }] } };
const mockPush = jest.fn();
jest.mock('../src/auth/AuthProvider', () => ({ useAuth: () => mockAuth, useOptionalAuth: () => mockAuth }));
jest.mock('expo-router', () => ({ useRouter: () => ({ push: mockPush, replace: mockPush }) }));
const mockProperty = { value: {} };
jest.mock('../src/property', () => ({ ...jest.requireActual('../src/property'), useProperty: () => mockProperty.value }));

const rankField = {
  key: 'level', label: 'Level', type: 'Rank', showOnCard: true, showOnProfile: true, profileOrder: 5,
  options: [{ key: 'first', label: 'First', color: '#cccccc', maxLevel: 2 }, { key: 'second', label: 'Second', color: '#3355aa' }],
  rank: { levelLabel: 'Marks', maxLevel: 4 }
};
const property = (features = {}) => ({ domains: { web: 'example.test' }, profileFields: [rankField], features: { ranks: true, rankVerificationLabels: true, ...features } });
const effective = { fieldKey: 'level', fieldLabel: 'Level', optionKey: 'first', optionLabel: 'First', color: '#cccccc', level: 1, rankLabel: 'First, 1 Marks', verified: true, source: 'Verified', awarder: { displayName: 'Staff Member' }, awardedUtc: '2026-10-01T12:00:00Z' };
const next = { optionKey: 'first', level: 2, rankLabel: 'First, 2 Marks', requirement: { minimumDaysInGrade: 30, minimumAttendance: 10 }, daysInGrade: 40, daysInGradeMet: true, attendance: 4, attendanceMet: false, eligible: false };

beforeEach(() => {
  clearMemberRankCache();
  mockPush.mockClear();
  mockProperty.value = property();
  mockAuth.api = {};
});

test('strings stay engine-neutral', () => {
  const text = JSON.stringify(strings.ranks);
  expect(text).not.toMatch(/\b(belt|stripe|dojo|gym|instructor|jiu|kimono)\b/i);
  expect(strings.access.features.ranks).toBeTruthy();
});

test('the rank card shows the verified rank, its badge, and requirement progress', async () => {
  await render(<RankCard rank={effective} next={next} />);
  expect(screen.getByText('First, 1 Marks')).toBeOnTheScreen();
  expect(screen.getByLabelText('Level, verified')).toBeOnTheScreen();
  expect(screen.getByText(/Verified by Staff Member/)).toBeOnTheScreen();
  expect(screen.getByText('Next: First, 2 Marks')).toBeOnTheScreen();
  expect(screen.getByTestId('rank-card-level-checks-attendance').props.accessibilityValue).toMatchObject({ now: 4, max: 10 });
});

test('verification labels are hidden when the server turns them off', async () => {
  await render(<RankCard rank={{ ...effective, verified: false, source: 'SelfReported' }} next={null} showVerificationLabels={false} />);
  expect(screen.queryByText('Self-reported')).toBeNull();
  expect(screen.getByText('You hold the highest rank.')).toBeOnTheScreen();
});

test('profile fields and member cards show the rank verification label', async () => {
  const profile = { level: { key: 'second', level: 0 } };
  await render(<CardFields profile={profile} fields={[rankField]} testID="card" rankBadges={{ level: 'selfReported' }} />);
  expect(screen.getByText('Self-reported')).toBeOnTheScreen();
  await render(<ProfileFieldList profile={profile} fields={[rankField]} rankBadges={{ level: 'verified' }} />);
  expect(screen.getByText('Verified')).toBeOnTheScreen();
  await render(<ProfileFieldList profile={profile} fields={[rankField]} />);
  expect(screen.queryByText('Verified')).toBeNull();
});

test('curriculum and progress panels draw progress bars without native modules', async () => {
  const onPress = jest.fn();
  await render(<>
    <CurriculumCard curriculum={{ id: 'cur_1', name: 'Foundations', itemCount: 4, progress: { completed: 1, total: 4 } }} onPress={onPress} />
    <RankProgressPanel rank={{ history: [{ id: 'rkp_1', action: 'Award', rankLabel: 'First, 1 Marks', createdUtc: '2026-10-01T12:00:00Z' }], historyTotal: 1 }} attendanceTotal={7} curricula={[]} />
  </>);
  expect(screen.getByTestId('curriculum-card-cur_1-progress').props.accessibilityValue).toMatchObject({ now: 1, max: 4, text: '25%' });
  expect(screen.getByText('7 check-ins')).toBeOnTheScreen();
  expect(screen.getByText(/Awarded First, 1 Marks/)).toBeOnTheScreen();
  await fireEvent.press(screen.getByLabelText('Foundations'));
  expect(onPress).toHaveBeenCalled();
});

test('My rank loads the rank, attendance, and lineage, and links to curricula', async () => {
  mockAuth.api = {
    getMyRank: jest.fn().mockResolvedValue({ showVerificationLabels: true, fields: [{ rank: effective, next }], history: [], historyTotal: 0, lineage: [{ id: 'lin_1', fieldKey: 'level', optionKey: 'first', optionLabel: 'First', promotedByName: 'A Teacher', verified: true }] }),
    enumerateMyAttendance: jest.fn().mockResolvedValue({ objects: [], totalRecords: 12, endOfResults: true })
  };
  await render(<MyRank />);
  expect(await screen.findByText('First, 1 Marks')).toBeOnTheScreen();
  expect(await screen.findByText('12 check-ins')).toBeOnTheScreen();
  expect(screen.getByText(/A Teacher/)).toBeOnTheScreen();
  await fireEvent.press(screen.getByTestId('my-rank-curricula'));
  expect(mockPush).toHaveBeenCalledWith('/curricula');
});

test('My rank says ranks are unavailable when the property has not opted in', async () => {
  mockProperty.value = property({ ranks: false });
  mockAuth.api = { getMyRank: jest.fn() };
  await render(<MyRank />);
  expect(screen.getByText('Ranks are not available right now.')).toBeOnTheScreen();
  expect(mockAuth.api.getMyRank).not.toHaveBeenCalled();
});

test('curriculum detail groups by rank and self-completes allowed items', async () => {
  mockAuth.api = {
    getCurriculum: jest.fn().mockResolvedValue({ id: 'cur_1', name: 'Foundations', fieldKey: 'level', items: [
      { id: 'cui_1', optionKey: 'second', itemType: 'Lesson', targetId: 'les_1', title: 'Lesson one', required: true, selfCompletable: false, state: 'Incomplete', satisfied: false },
      { id: 'cui_2', optionKey: 'first', itemType: 'Custom', customText: 'Practice the basics', required: false, selfCompletable: true, state: 'Incomplete', satisfied: false }
    ] }),
    completeCurriculumItem: jest.fn().mockResolvedValue({ id: 'cui_2', optionKey: 'first', itemType: 'Custom', customText: 'Practice the basics', required: false, selfCompletable: true, state: 'SelfComplete', satisfied: true })
  };
  await render(<CurriculumDetail curriculumId="cur_1" />);
  expect(await screen.findByText('Foundations')).toBeOnTheScreen();
  expect(screen.getByTestId('curriculum-group-first')).toBeOnTheScreen();
  expect(screen.getByTestId('curriculum-group-second')).toBeOnTheScreen();
  await fireEvent.press(screen.getByLabelText('Open Lesson one'));
  expect(mockPush).toHaveBeenCalledWith('/lessons/les_1');
  expect(screen.queryByTestId('curriculum-item-cui_1-toggle')).toBeNull();
  await fireEvent.press(screen.getByLabelText('Mark Practice the basics complete'));
  expect(mockAuth.api.completeCurriculumItem).toHaveBeenCalledWith('cur_1', 'cui_2');
  await waitFor(() => expect(screen.getByLabelText('Mark Practice the basics not complete')).toBeOnTheScreen());
  expect(screen.getByText('1 of 2 complete')).toBeOnTheScreen();
});

test('catalog curriculum items link only when the catalog is on', () => {
  const item = { itemType: 'CatalogEntry', targetId: 'cat_1', targetAvailable: true };
  expect(curriculumItemRoute(item, { features: {} })).toBeNull();
  expect(curriculumItemRoute(item, { features: { catalog: true } })).toBe('/catalog/cat_1');
  expect(curriculumItemRoute({ itemType: 'Series', targetId: 'ser_1' }, {})).toBe('/series/ser_1');
});

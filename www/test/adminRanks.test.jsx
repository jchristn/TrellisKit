import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { AccessFeatures } from '@trelliskit/shared';
import RanksAdminView, { AwardForm, requirementsBody } from '../src/views/admin/ranks/RanksAdminView.jsx';
import { CurriculaAdminView, CurriculumAdminEditView, curriculumItemsBody } from '../src/views/admin/ranks/CurriculaAdminViews.jsx';
import AttendanceAdminView from '../src/views/admin/ranks/AttendanceAdminView.jsx';
import MemberRankPanel from '../src/views/admin/ranks/MemberRankPanel.jsx';
import { groupedAdminRoutes } from '../src/views/admin/adminRoutes.js';
import { staffAreas } from '../src/utils/engagement.js';
import strings from '../src/strings.js';

const propertyState = vi.hoisted(() => ({ property: {}, fill: (text) => text }));
vi.mock('../src/context/PropertyContext.jsx', () => ({ useProperty: () => propertyState }));
const auth = vi.hoisted(() => ({ client: null, api: null, me: { id: 'usr_admin' }, isSystemAdmin: true, mode: 'session', staffAreas: [] }));
vi.mock('../src/context/AdminAuthContext.jsx', () => ({ useAdminAuth: () => auth }));
vi.mock('../src/context/AuthContext.jsx', () => ({ useAuth: () => auth }));

const rankField = {
  key: 'level', label: 'Level', type: 'Rank', showOnCard: true, showOnProfile: true,
  options: [{ key: 'first', label: 'First', maxLevel: 2 }, { key: 'second', label: 'Second', maxLevel: 0 }, { key: 'third', label: 'Third' }],
  rank: { levelLabel: 'Marks', maxLevel: 4, awarding: { allowEqualOption: false } }
};
const page = (objects, continuationToken = null) => ({ objects, continuationToken, endOfResults: !continuationToken, totalRecords: objects.length });
const ids = (routes) => routes.flatMap((g) => g.routes.map((r) => r.id));

beforeEach(() => {
  propertyState.property = { domains: { web: 'example.test' }, profileFields: [rankField], features: { ranks: true } };
  auth.isSystemAdmin = true;
  auth.mode = 'session';
  auth.me = { id: 'usr_admin' };
  auth.staffAreas = [];
  auth.api = { listProfileFields: vi.fn().mockResolvedValue(page([rankField])) };
  auth.client = {
    searchMembers: vi.fn().mockResolvedValue(page([{ id: 'usr_2', displayName: 'Second Member' }])),
    enumerateAdminMembers: vi.fn().mockResolvedValue(page([{ id: 'usr_2', displayName: 'Second Member', email: 'second@example.test' }]))
  };
});

test('rank admin pages appear only with features.ranks and for their staff roles', () => {
  expect(ids(groupedAdminRoutes({ features: {} }))).not.toContain('ranks');
  const on = { features: { ranks: true } };
  expect(ids(groupedAdminRoutes(on))).toEqual(expect.arrayContaining(['ranks', 'curricula', 'attendance']));
  const awarder = ids(groupedAdminRoutes(on, { moderatorOnly: true, moderator: false, staffAreas: staffAreas({ roles: [{ role: 'RankAwarder' }] }) }));
  expect(awarder).toEqual(expect.arrayContaining(['home', 'ranks', 'curricula', 'attendance']));
  const editor = ids(groupedAdminRoutes(on, { moderatorOnly: true, moderator: false, staffAreas: ['library'] }));
  expect(editor).toContain('curricula');
  expect(editor).not.toContain('ranks');
  expect(AccessFeatures).toContain('ranks');
  expect(strings.access.features.ranks).toBeTruthy();
  expect(strings.enums.role.RankAwarder).toBe('Rank awarder');
});

test('the award form offers each option\'s own level choices and sends the award', async () => {
  auth.client.awardRank = vi.fn().mockResolvedValue({ id: 'rkp_1' });
  const onDone = vi.fn();
  render(<AwardForm client={auth.client} field={rankField} awarder={{ isSystemAdmin: true }} search={async () => [{ id: 'usr_2', label: 'Second Member' }]} onDone={onDone} initial={{ member: { id: 'usr_2', label: 'Second Member' } }} />);
  const level = screen.getByLabelText('Level');
  expect(within(level).getAllByRole('option').map((o) => o.textContent)).toEqual(['First', 'First, 1 Marks', 'First, 2 Marks']);
  await userEvent.selectOptions(screen.getByLabelText('Rank'), 'third');
  expect(within(screen.getByLabelText('Level')).getAllByRole('option')).toHaveLength(5);
  await userEvent.selectOptions(screen.getByLabelText('Rank'), 'second');
  expect(screen.getByLabelText('Level')).toBeDisabled();
  await userEvent.selectOptions(screen.getByLabelText('Rank'), 'first');
  await userEvent.selectOptions(screen.getByLabelText('Level'), '2');
  fireEvent.change(screen.getByLabelText('Note'), { target: { value: 'Consistent progress' } });
  await userEvent.click(screen.getByRole('button', { name: 'Award rank' }));
  await waitFor(() => expect(auth.client.awardRank).toHaveBeenCalledWith({ userId: 'usr_2', fieldKey: 'level', optionKey: 'first', level: 2, note: 'Consistent progress' }));
  expect(onDone).toHaveBeenCalled();
});

test('the award form warns when the awarding rules refuse the rank and shows server refusals', async () => {
  auth.client.awardRank = vi.fn().mockRejectedValue({ status: 403, code: 'AwardAboveOwnRank' });
  render(<AwardForm client={auth.client} field={rankField} awarder={{ isSystemAdmin: false, rank: { optionKey: 'second', level: 0, status: 'Verified' } }} search={async () => []} onDone={() => {}} initial={{ member: { id: 'usr_2', label: 'Second Member' }, optionKey: 'third' }} />);
  expect(screen.getByRole('note')).toHaveTextContent('The awarding rules do not let you award this rank.');
  await userEvent.click(screen.getByRole('button', { name: 'Award rank' }));
  expect(await screen.findByRole('alert')).toHaveTextContent('The awarding rules do not let you award this rank.');
});

test('the awards list filters, and revoking asks for a reason', async () => {
  auth.client.enumerateRankAwards = vi.fn().mockResolvedValue(page([
    { id: 'rkp_1', userId: 'usr_2', user: { id: 'usr_2', displayName: 'Second Member' }, fieldKey: 'level', optionKey: 'first', level: 1, rankLabel: 'First, 1 Marks', action: 'Award', actor: { displayName: 'Admin' }, createdUtc: '2026-11-01T00:00:00Z' },
    { id: 'rkp_0', userId: 'usr_3', user: { id: 'usr_3', displayName: 'Third Member' }, fieldKey: 'level', optionKey: 'first', level: 0, rankLabel: 'First', action: 'Award', revokedUtc: '2026-11-02T00:00:00Z', createdUtc: '2026-10-01T00:00:00Z' }
  ]));
  auth.client.revokeRankAward = vi.fn().mockResolvedValue({ id: 'rkp_2' });
  render(<MemoryRouter initialEntries={['/admin/ranks']}><Routes><Route path="/admin/ranks" element={<RanksAdminView />} /></Routes></MemoryRouter>);
  expect(await screen.findByText('Second Member')).toBeInTheDocument();
  expect(auth.client.enumerateRankAwards.mock.calls[0][0]).toMatchObject({ maxResults: 25, filters: { fieldKey: 'level' } });
  await userEvent.selectOptions(screen.getByLabelText('Status'), 'Revoked');
  await waitFor(() => expect(auth.client.enumerateRankAwards.mock.calls.at(-1)[0].filters).toEqual({ fieldKey: 'level', status: 'Revoked' }));
  expect(screen.getAllByRole('button', { name: 'Revoke' })).toHaveLength(1);
  await userEvent.click(screen.getByRole('button', { name: 'Revoke' }));
  const dialog = screen.getByRole('dialog');
  await userEvent.click(within(dialog).getByRole('button', { name: 'Revoke' }));
  expect(within(dialog).getByRole('alert')).toHaveTextContent('Required');
  fireEvent.change(within(dialog).getByLabelText('Reason'), { target: { value: 'Entered in error' } });
  await userEvent.click(within(dialog).getByRole('button', { name: 'Revoke' }));
  await waitFor(() => expect(auth.client.revokeRankAward).toHaveBeenCalledWith('rkp_1', 'Entered in error'));
  expect(await screen.findByText('Award revoked.')).toBeInTheDocument();
});

test('eligibility lists members and pre-fills an award for the next rank', async () => {
  auth.client.enumerateRankAwards = vi.fn().mockResolvedValue(page([]));
  auth.client.enumerateRankEligibility = vi.fn().mockResolvedValue(page([{ userId: 'usr_2', user: { displayName: 'Second Member' }, current: { rankLabel: 'First, 1 Marks' }, next: { optionKey: 'first', level: 2, rankLabel: 'First, 2 Marks', requirement: { minimumAttendance: 5 }, attendance: 6, attendanceMet: true, eligible: true }, eligible: true }]));
  render(<MemoryRouter><RanksAdminView /></MemoryRouter>);
  await userEvent.click(await screen.findByRole('tab', { name: 'Eligibility' }));
  expect(await screen.findByText('Second Member')).toBeInTheDocument();
  expect(auth.client.enumerateRankEligibility.mock.calls[0][0]).toMatchObject({ filters: { fieldKey: 'level', eligibleOnly: true } });
  expect(screen.getByText('Check-ins: 6 of 5')).toBeInTheDocument();
  await userEvent.click(screen.getByRole('button', { name: 'Award rank' }));
  const dialog = await screen.findByRole('dialog');
  expect(within(dialog).getByLabelText('Rank')).toHaveValue('first');
  expect(within(dialog).getByLabelText('Level')).toHaveValue('2');
  expect(within(dialog).getByText('Second Member')).toBeInTheDocument();
});

test('SystemAdmin edits requirements per rank and level', async () => {
  auth.client.enumerateRankAwards = vi.fn().mockResolvedValue(page([]));
  auth.client.enumerateRankRequirements = vi.fn().mockResolvedValue(page([{ id: 'rkq_1', fieldKey: 'level', optionKey: 'first', level: null, minimumDaysInGrade: 30, minimumAttendance: 10 }]));
  auth.client.adminEnumerateCurricula = vi.fn().mockResolvedValue(page([{ id: 'cur_1', name: 'Foundations' }]));
  auth.client.putRankRequirements = vi.fn().mockImplementation(async (body) => ({ fieldKey: 'level', updated: 2, requirements: body.requirements }));
  render(<MemoryRouter><RanksAdminView /></MemoryRouter>);
  await userEvent.click(await screen.findByRole('tab', { name: 'Requirements' }));
  expect(await screen.findByDisplayValue('30')).toBeInTheDocument();
  await userEvent.click(screen.getByRole('button', { name: 'Add requirement' }));
  const sets = screen.getAllByRole('group');
  await userEvent.selectOptions(within(sets[1]).getByLabelText('Rank'), 'first');
  await userEvent.click(screen.getByRole('button', { name: 'Save requirements' }));
  expect(await screen.findByText('Each rank and level can have only one requirement.')).toBeInTheDocument();
  await userEvent.selectOptions(within(sets[1]).getByLabelText('Level'), '1');
  await userEvent.selectOptions(within(sets[1]).getByLabelText('Curriculum'), 'cur_1');
  await userEvent.click(screen.getByRole('button', { name: 'Save requirements' }));
  await waitFor(() => expect(auth.client.putRankRequirements).toHaveBeenCalledWith({ fieldKey: 'level', requirements: [
    { optionKey: 'first', level: null, minimumDaysInGrade: 30, minimumAttendance: 10 },
    { optionKey: 'first', level: 1, minimumDaysInGrade: 0, minimumAttendance: 0, curriculumId: 'cur_1' }
  ] }));
  expect(await screen.findByText('Requirements saved.')).toBeInTheDocument();
  expect(requirementsBody('level', [])).toEqual({ fieldKey: 'level', requirements: [] });
});

test('rank awarders see requirements read-only', async () => {
  auth.isSystemAdmin = false;
  auth.client.getMyRank = vi.fn().mockResolvedValue({ fields: [] });
  auth.client.enumerateRankAwards = vi.fn().mockResolvedValue(page([]));
  auth.client.enumerateRankRequirements = vi.fn().mockResolvedValue(page([{ id: 'rkq_1', fieldKey: 'level', optionKey: 'first', level: null, minimumDaysInGrade: 30, minimumAttendance: 10 }]));
  auth.client.adminEnumerateCurricula = vi.fn().mockResolvedValue(page([]));
  render(<MemoryRouter><RanksAdminView /></MemoryRouter>);
  await userEvent.click(await screen.findByRole('tab', { name: 'Requirements' }));
  expect(await screen.findByText('Only system administrators change requirements.')).toBeInTheDocument();
  expect(screen.queryByRole('button', { name: 'Save requirements' })).toBeNull();
  expect(screen.getByDisplayValue('30')).toBeDisabled();
});

test('lineage lookup lets a SystemAdmin verify entries', async () => {
  auth.client.enumerateRankAwards = vi.fn().mockResolvedValue(page([]));
  auth.client.getMemberRank = vi.fn().mockResolvedValue({ userId: 'usr_2', ranks: [], lineage: [{ id: 'lin_1', fieldKey: 'level', optionKey: 'first', optionLabel: 'First', promotedByName: 'Someone', verified: false }] });
  auth.client.verifyLineage = vi.fn().mockResolvedValue({ id: 'lin_1', fieldKey: 'level', optionKey: 'first', optionLabel: 'First', verified: true });
  render(<MemoryRouter><RanksAdminView /></MemoryRouter>);
  await userEvent.click(await screen.findByRole('tab', { name: 'Lineage' }));
  await userEvent.type(screen.getByLabelText('Member'), 'Sec');
  await userEvent.click(await screen.findByRole('option', { name: /Second Member/ }));
  await userEvent.click(await screen.findByRole('button', { name: 'Verify' }));
  expect(auth.client.verifyLineage).toHaveBeenCalledWith('lin_1');
  expect(await screen.findByText('Verified')).toBeInTheDocument();
});

test('curricula are created and edited with rank groups and item pickers, then published', async () => {
  auth.client.adminEnumerateCurricula = vi.fn().mockResolvedValue(page([{ id: 'cur_1', name: 'Foundations', status: 'Draft', visibility: 'Members', itemCount: 0 }]));
  auth.client.adminCreateCurriculum = vi.fn().mockResolvedValue({ id: 'cur_2' });
  render(<MemoryRouter initialEntries={['/admin/curricula']}><Routes><Route path="/admin/curricula" element={<CurriculaAdminView />} /><Route path="/admin/curricula/:id" element={<p>Editor page</p>} /></Routes></MemoryRouter>);
  expect(await screen.findByRole('link', { name: 'Foundations' })).toHaveAttribute('href', '/admin/curricula/cur_1');
  await userEvent.click(screen.getByRole('button', { name: 'New curriculum' }));
  fireEvent.change(screen.getByLabelText('Name'), { target: { value: 'Second set' } });
  await userEvent.click(screen.getAllByRole('button', { name: 'New curriculum' }).at(-1));
  await waitFor(() => expect(auth.client.adminCreateCurriculum).toHaveBeenCalledWith({ name: 'Second set', description: '', fieldKey: 'level', visibility: 'Members', position: 0 }));
  expect(await screen.findByText('Editor page')).toBeInTheDocument();
});

test('the curriculum editor saves items, offers catalog entries only with the catalog, and publishes', async () => {
  const curriculum = { id: 'cur_1', name: 'Foundations', status: 'Draft', fieldKey: 'level', visibility: 'Members', updatedUtc: 'u1', items: [{ id: 'cui_1', optionKey: 'first', itemType: 'Lesson', targetId: 'les_1', title: 'Lesson one', required: true }] };
  auth.client.adminGetCurriculum = vi.fn().mockResolvedValue(curriculum);
  auth.client.adminReplaceCurriculumItems = vi.fn().mockImplementation(async (_id, items) => ({ ...curriculum, updatedUtc: 'u2', items: items.map((item, i) => ({ id: item.id || 'cui_new' + i, ...item, title: item.customText })) }));
  auth.client.adminPublishCurriculum = vi.fn().mockResolvedValue({ ...curriculum, status: 'Published' });
  render(<MemoryRouter initialEntries={['/admin/curricula/cur_1']}><Routes><Route path="/admin/curricula/:id" element={<CurriculumAdminEditView />} /></Routes></MemoryRouter>);
  expect(await screen.findByText('Lesson one')).toBeInTheDocument();
  const typeOptions = within(screen.getAllByLabelText('Type')[0]).getAllByRole('option').map((o) => o.value);
  expect(typeOptions).toEqual(['Lesson', 'Series', 'Custom']);
  await userEvent.click(screen.getByRole('button', { name: 'Add item' }));
  const rows = screen.getAllByLabelText('Type');
  await userEvent.selectOptions(rows[1], 'Custom');
  await userEvent.click(screen.getByRole('button', { name: 'Save items' }));
  expect(await screen.findByText(/Choose a target for every item/)).toBeInTheDocument();
  fireEvent.change(screen.getByLabelText('Text'), { target: { value: 'Show the basics' } });
  await userEvent.selectOptions(screen.getAllByLabelText('Rank group')[1], 'second');
  await userEvent.click(screen.getByRole('button', { name: 'Save items' }));
  await waitFor(() => expect(auth.client.adminReplaceCurriculumItems).toHaveBeenCalledWith('cur_1', [
    { id: 'cui_1', itemType: 'Lesson', targetId: 'les_1', optionKey: 'first', required: true },
    { itemType: 'Custom', customText: 'Show the basics', optionKey: 'second', required: true }
  ]));
  await userEvent.click(screen.getByRole('button', { name: 'Publish' }));
  await waitFor(() => expect(auth.client.adminPublishCurriculum).toHaveBeenCalledWith('cur_1'));
  expect(await screen.findByText('Curriculum published.')).toBeInTheDocument();
});

test('catalog entries are offered as curriculum items with the catalog on', async () => {
  propertyState.property = { ...propertyState.property, features: { ranks: true, catalog: true } };
  auth.client.adminGetCurriculum = vi.fn().mockResolvedValue({ id: 'cur_1', name: 'Foundations', status: 'Published', fieldKey: 'level', items: [{ id: 'cui_1', itemType: 'Custom', customText: 'Text item', required: false }] });
  render(<MemoryRouter initialEntries={['/admin/curricula/cur_1']}><Routes><Route path="/admin/curricula/:id" element={<CurriculumAdminEditView />} /></Routes></MemoryRouter>);
  const type = await screen.findByLabelText('Type');
  expect(within(type).getAllByRole('option').map((o) => o.value)).toEqual(['Lesson', 'Series', 'CatalogEntry', 'Custom']);
  expect(screen.queryByRole('button', { name: 'Publish' })).toBeNull();
  expect(curriculumItemsBody([{ itemType: 'CatalogEntry', target: [{ id: 'cat_1' }], customText: '', required: false, optionKey: '' }])).toEqual([{ itemType: 'CatalogEntry', targetId: 'cat_1', required: false }]);
});

test('member progress drill-down verifies items', async () => {
  auth.client.adminGetCurriculum = vi.fn().mockResolvedValue({ id: 'cur_1', name: 'Foundations', status: 'Published', fieldKey: 'level', items: [] });
  auth.client.adminGetCurriculumProgress = vi.fn().mockResolvedValue({ id: 'cur_1', items: [{ id: 'cui_1', optionKey: 'first', itemType: 'Custom', customText: 'Show the basics', state: 'SelfComplete', satisfied: false }] });
  auth.client.verifyCurriculumItem = vi.fn().mockResolvedValue({ id: 'cui_1', optionKey: 'first', itemType: 'Custom', customText: 'Show the basics', state: 'Verified', satisfied: true });
  render(<MemoryRouter initialEntries={['/admin/curricula/cur_1']}><Routes><Route path="/admin/curricula/:id" element={<CurriculumAdminEditView />} /></Routes></MemoryRouter>);
  await screen.findByRole('heading', { name: 'Foundations' });
  await userEvent.type(screen.getAllByLabelText('Member')[0], 'Sec');
  await userEvent.click(await screen.findByRole('option', { name: /Second Member/ }));
  expect(await screen.findByText('0 of 1 items satisfied')).toBeInTheDocument();
  await userEvent.click(screen.getByRole('button', { name: 'Verify' }));
  expect(auth.client.verifyCurriculumItem).toHaveBeenCalledWith('cur_1', 'usr_2', 'cui_1');
  expect(await screen.findByText('1 of 1 items satisfied')).toBeInTheDocument();
});

test('attendance shows a venue session roster and checks a member in without a code', async () => {
  auth.client.adminEnumerateVenues = vi.fn().mockResolvedValue(page([{ id: 'ven_1', name: 'North venue' }]));
  auth.client.adminGetVenue = vi.fn().mockResolvedValue({ id: 'ven_1', schedule: { sessions: [{ name: 'Morning', dayOfWeek: 0, startTime: '07:00' }, { name: 'Evening', dayOfWeek: 2, startTime: '18:00' }] } });
  auth.client.enumerateAttendance = vi.fn().mockResolvedValue(page([]));
  auth.client.checkIn = vi.fn().mockResolvedValue({ id: 'att_1' });
  render(<MemoryRouter><AttendanceAdminView /></MemoryRouter>);
  await userEvent.selectOptions(screen.getByLabelText('Check-in for'), 'VenueSession');
  await userEvent.type(screen.getByLabelText('Venue'), 'Nor');
  await userEvent.click(await screen.findByRole('option', { name: /North venue/ }));
  await userEvent.selectOptions(await screen.findByLabelText('Session'), '1');
  fireEvent.change(screen.getByLabelText('Date'), { target: { value: '2026-11-25' } });
  expect(await screen.findByText('Nobody has checked in yet.')).toBeInTheDocument();
  expect(auth.client.enumerateAttendance.mock.calls.at(-1)[0].filters).toEqual({ targetType: 'VenueSession', targetId: 'ven_1:1', occurrenceDate: '2026-11-25' });
  await userEvent.type(screen.getAllByLabelText('Member').at(-1), 'Sec');
  await userEvent.click(await screen.findByRole('option', { name: /Second Member/ }));
  await userEvent.click(screen.getByRole('button', { name: 'Check in' }));
  await waitFor(() => expect(auth.client.checkIn).toHaveBeenCalledWith({ targetType: 'VenueSession', targetId: 'ven_1:1', occurrenceDate: '2026-11-25', userId: 'usr_2' }));
  expect(await screen.findByText('Member checked in.')).toBeInTheDocument();
});

test('the member detail drawer panel lists rank history and is hidden when ranks are off', async () => {
  auth.client.enumerateRankAwards = vi.fn().mockResolvedValue(page([{ id: 'rkp_1', action: 'Award', rankLabel: 'First, 1 Marks', createdUtc: '2026-11-01T00:00:00Z' }]));
  const { unmount } = render(<MemoryRouter><MemberRankPanel userId="usr_2" /></MemoryRouter>);
  expect(await screen.findByText(/Awarded First, 1 Marks/)).toBeInTheDocument();
  expect(auth.client.enumerateRankAwards).toHaveBeenCalledWith({ filters: { userId: 'usr_2' }, maxResults: 10 }, expect.anything());
  expect(screen.getByRole('link', { name: 'Open in Ranks' })).toHaveAttribute('href', '/admin/ranks?userId=usr_2');
  unmount();
  propertyState.property = { features: {} };
  const { container } = render(<MemoryRouter><MemberRankPanel userId="usr_2" /></MemoryRouter>);
  expect(container).toBeEmptyDOMElement();
});

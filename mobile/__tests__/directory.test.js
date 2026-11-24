import { act, fireEvent, screen, waitFor, within } from '@testing-library/react-native';
import { renderRouter } from 'expo-router/testing-library';
import path from 'path';
import { Dimensions, Platform } from 'react-native';
import { directoryChips, directoryCollectionFilters, emptyDirectoryFilters } from '../src/profile/directory';
import strings from '../src/strings';
import { enumerationResult } from '../test-utils/community';
import { installFakeServer, makeUser, resetEnvironment, signedInAs } from '../test-utils/fakeServer';
import { allFields, propertyWithFields } from '../test-utils/fields';

const APP_DIR = path.resolve(__dirname, '../app');
const c = strings.directory.chips;

function member(id, overrides = {}) {
  return { id, displayName: 'Member ' + id, handle: 'm' + id, avatarUrl: null, isStaff: false, isDeleted: false, labels: [], tags: {}, profile: { level: { key: 'Gold', level: 1 }, team: 'North' }, ...overrides };
}

/** The last first-page request (FlashList also asks for later pages on its own as it reaches the end). */
function lastFirstPage(server) {
  return server.called('POST /members/directory/enumerate').filter((call) => !call.body.continuationToken).pop().body;
}

function directoryServer(extra = {}) {
  return installFakeServer({
    ...signedInAs(makeUser()),
    'GET /property': propertyWithFields(),
    'POST /members/directory/enumerate': (call) =>
      call.body.continuationToken === 'page-2'
        ? { objects: [member('usr_3')], continuationToken: null, endOfResults: true }
        : { objects: [member('usr_1'), member('usr_2', { isStaff: true, profile: {} })], continuationToken: 'page-2', endOfResults: false },
    'POST /labels/enumerate': enumerationResult([{ label: 'mentors', count: 2 }]),
    'POST /tags/keys/enumerate': enumerationResult([]),
    'GET /users/usr_1': { id: 'usr_1', displayName: 'Member usr_1', handle: 'musr_1', avatarUrl: null, bio: null, profile: {}, createdUtc: '2026-01-01T00:00:00Z', isBlockedByMe: false },
    ...extra
  });
}

describe('directory filter state', () => {
  test('profile filters drop empty operators; chips describe each operator and remove only it', () => {
    const filters = { ...emptyDirectoryFilters(), profile: { level: { minRank: 'Gold', minLevel: 1 }, country: { in: ['CA', 'US'] }, team: { prefix: '' }, mentor: { eq: true }, years: { gte: '3' }, milestones: { hasValue: false } } };
    expect(directoryCollectionFilters(filters, allFields)).toEqual({ profile: { level: { minRank: 'Gold', minLevel: 1 }, country: { in: ['CA', 'US'] }, mentor: { eq: true }, years: { gte: 3 }, milestones: { hasValue: false } } });
    expect(directoryCollectionFilters(emptyDirectoryFilters(), allFields)).toBeUndefined();
    const chips = directoryChips(filters, allFields);
    expect(chips.map((chip) => chip.text)).toEqual([
      c.minRank('Level', 'Gold'),
      c.minLevel('Pips', '1'),
      c.in('Country', 'Canada, United States'),
      c.eq('Mentor', strings.directory.yes),
      c.gte('Years', '3'),
      c.hasValue('Milestones', false)
    ]);
    expect(chips[0].remove.profile).toEqual({ level: { minLevel: 1 }, country: { in: ['CA', 'US'] }, mentor: { eq: true }, years: { gte: 3 }, milestones: { hasValue: false } });
  });
});

describe('member directory screen', () => {
  beforeEach(() => { Platform.OS = 'android'; resetEnvironment(); });
  afterEach(() => { Platform.OS = 'ios'; delete global.fetch; });

  test('is reachable from the main drawer and lists members with card fields; pages load by continuation token', async () => {
    const server = directoryServer();
    const router = await renderRouter(APP_DIR, { initialUrl: '/profile' });
    await fireEvent.press(await screen.findByTestId('header-menu'));
    await fireEvent.press(await screen.findByTestId('drawer-members'));
    await waitFor(() => expect(router.getPathname()).toBe('/members'));
    expect(await screen.findByTestId('directory-member-usr_1')).toBeOnTheScreen();
    expect(server.called('POST /members/directory/enumerate')[0].body).toEqual({ maxResults: 25 });
    expect(server.called('POST /members/directory/enumerate')[0].headers).toMatchObject({ 'X-Client': 'mobile' });
    await waitFor(() => expect(screen.getByTestId('directory-member-usr_1-card-level-text')).toHaveTextContent('Gold, 1 pip'));
    expect(screen.getByTestId('directory-member-usr_1-card-level-bar')).toHaveStyle({ backgroundColor: '#B8860B' });
    expect(screen.getByTestId('directory-member-usr_1-card-team')).toHaveTextContent('North');
    expect(screen.queryByTestId('directory-member-usr_2-card')).toBeNull();
    expect(within(screen.getByTestId('directory-member-usr_2')).getByText(strings.feed.staff)).toBeOnTheScreen();

    // Infinite scroll: reaching the end asks for the next page with the continuation token.
    const more = screen.queryByTestId('directory-load-more');
    if (more) await fireEvent.press(more);
    expect(await screen.findByTestId('directory-member-usr_3')).toBeOnTheScreen();
    expect(server.called('POST /members/directory/enumerate').pop().body).toEqual({ maxResults: 25, continuationToken: 'page-2' });
    await waitFor(() => expect(screen.queryByTestId('directory-load-more')).toBeNull());

    await fireEvent.press(screen.getByTestId('directory-member-usr_1'));
    await waitFor(() => expect(router.getPathname()).toBe('/members/usr_1'));
  });

  test('phone: profile filters in the bottom sheet build filters.profile; chips with a count, removal, and Clear all', async () => {
    const server = directoryServer();
    await renderRouter(APP_DIR, { initialUrl: '/members' });
    await screen.findByTestId('directory-member-usr_1');
    await fireEvent.press(screen.getByTestId('directory-filters-button'));
    const sheet = await screen.findByTestId('directory-filters-sheet');
    const p = 'directory-profile-filters-';
    expect(within(sheet).queryByTestId(p + 'motto')).toBeNull();
    expect(within(sheet).queryByTestId(p + 'flavor')).toBeNull();

    await fireEvent.press(within(sheet).getByTestId(p + 'level-minRank-Gold'));
    await fireEvent.press(within(sheet).getByTestId(p + 'level-minLevel-inc'));
    await fireEvent.press(within(sheet).getByTestId(p + 'focus-any-Reading'));
    await fireEvent.press(within(sheet).getByTestId(p + 'style-in-Calm'));
    await fireEvent.changeText(within(sheet).getByTestId(p + 'country-in'), 'Canad');
    await fireEvent.press(await within(sheet).findByTestId(p + 'country-in-option-CA'));
    await fireEvent.changeText(within(sheet).getByTestId(p + 'team-text'), 'nor');
    await fireEvent.changeText(within(sheet).getByTestId(p + 'years-gte'), '2');
    await fireEvent.changeText(within(sheet).getByTestId(p + 'since-lte'), '2025-01-01');
    await fireEvent.press(within(sheet).getByTestId(p + 'mentor-eq-true'));
    await fireEvent.changeText(within(sheet).getByTestId('directory-filters-panel-search'), 'pat');
    await fireEvent.press(within(sheet).getByTestId('directory-filters-panel-ordering-DisplayNameAscending'));
    await fireEvent.press(within(sheet).getByTestId('directory-filters-panel-apply'));

    await waitFor(() =>
      expect(lastFirstPage(server)).toEqual({
        maxResults: 25,
        search: 'pat',
        ordering: 'DisplayNameAscending',
        filters: {
          profile: {
            level: { minRank: 'Gold', minLevel: 1 },
            focus: { any: ['Reading'] },
            style: { in: ['Calm'] },
            country: { in: ['CA'] },
            team: { prefix: 'nor' },
            years: { gte: 2 },
            since: { lte: '2025-01-01' },
            mentor: { eq: true }
          }
        }
      })
    );
    expect(screen.getByTestId('directory-filters-button')).toHaveProp('accessibilityLabel', strings.filters.buttonActive(10));
    expect(screen.getByTestId('directory-filters-chip-profile-level-minRank')).toHaveTextContent(c.minRank('Level', 'Gold'), { exact: false });
    expect(screen.getByTestId('directory-filters-chip-profile-country-in')).toHaveTextContent(c.in('Country', 'Canada'), { exact: false });

    await fireEvent.press(screen.getByTestId('directory-filters-chip-profile-level-minRank'));
    await waitFor(() => expect(lastFirstPage(server).filters.profile.level).toEqual({ minLevel: 1 }));

    await fireEvent.press(screen.getByTestId('directory-filters-clear-all'));
    await waitFor(() => expect(lastFirstPage(server)).toEqual({ maxResults: 25, ordering: 'DisplayNameAscending' }));
    expect(screen.queryByTestId('directory-filters-active')).toBeNull();
  });

  test('labels and tags filter the directory with targetType User', async () => {
    const server = directoryServer();
    await renderRouter(APP_DIR, { initialUrl: '/members' });
    await screen.findByTestId('directory-member-usr_1');
    await fireEvent.press(screen.getByTestId('directory-filters-button'));
    const sheet = await screen.findByTestId('directory-filters-sheet');
    const labelInput = within(sheet).getByTestId('directory-filters-panel-label-input');
    await fireEvent(labelInput, 'focus');
    await fireEvent.changeText(labelInput, 'men');
    await fireEvent.press(await within(sheet).findByTestId('directory-filters-panel-label-input-suggestion-mentors'));
    expect(server.called('POST /labels/enumerate').pop().body).toMatchObject({ targetType: 'User', prefix: 'men' });
    await fireEvent.press(within(sheet).getByTestId('directory-filters-panel-apply'));
    await waitFor(() => expect(lastFirstPage(server)).toEqual({ maxResults: 25, labels: ['mentors'], labelMatch: 'All' }));
  });

  test('an empty result says so, and a failed load offers Retry', async () => {
    let fail = true;
    directoryServer({ 'POST /members/directory/enumerate': () => (fail ? { status: 500, body: { error: 'Internal', message: 'Boom.' } } : { objects: [], endOfResults: true }) });
    await renderRouter(APP_DIR, { initialUrl: '/members' });
    expect(await screen.findByTestId('directory-error')).toBeOnTheScreen();
    fail = false;
    await fireEvent.press(within(screen.getByTestId('directory-error')).getByRole('button', { name: strings.common.retry }));
    expect(await screen.findByTestId('directory-empty')).toHaveTextContent(strings.directory.empty);
  });

  test('tablet: filters sit in a side panel next to the members', async () => {
    const original = Dimensions.get('window');
    const screenSize = Dimensions.get('screen');
    Dimensions.set({ window: { ...original, width: 1180, height: 820 }, screen: { ...screenSize, width: 1180, height: 820 } });
    try {
      const server = directoryServer();
      await renderRouter(APP_DIR, { initialUrl: '/members' });
      const panel = await screen.findByTestId('directory-filters-side-panel');
      expect(screen.queryByTestId('directory-filters-button')).toBeNull();
      await fireEvent.press(await within(panel).findByTestId('directory-profile-filters-focus-any-Writing'));
      await fireEvent.press(within(panel).getByTestId('directory-filters-panel-apply'));
      await waitFor(() => expect(lastFirstPage(server).filters).toEqual({ profile: { focus: { any: ['Writing'] } } }));
      expect(await screen.findByTestId('directory-filters-chip-profile-focus-any')).toBeOnTheScreen();
    } finally {
      await act(async () => Dimensions.set({ window: original, screen: screenSize }));
    }
  });
});

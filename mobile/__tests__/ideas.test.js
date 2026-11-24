import { Platform } from 'react-native';
import { fireEvent, screen, waitFor, within } from '@testing-library/react-native';
import { createBroadcastsFake, createIdeasFake } from '@trelliskit/shared/testing';
import { renderRouter } from 'expo-router/testing-library';
import path from 'path';
import strings from '../src/strings';
import { makeUser, resetEnvironment } from '../test-utils/fakeServer';
import { atWidth, me, phase5Property, serveFakes } from '../test-utils/phase5';

const APP_DIR = path.resolve(__dirname, '../app');
const t = strings.ideas;

function board(seed = {}) {
  const fake = createIdeasFake({ me, staff: false, ...seed });
  const dark = fake.addIdea({ title: 'Dark mode', author: null, status: 'Planned', voters: ['usr_7', 'usr_8'] });
  const offline = fake.addIdea({ title: 'Offline videos', author: null, voters: ['usr_7'] });
  return { fake, dark, offline };
}

function lastEnumerate(fake) {
  return fake.calls.filter((c) => c.path === '/ideas/enumerate').at(-1).body;
}

beforeEach(() => { Platform.OS = 'android'; resetEnvironment(); });
afterEach(() => { Platform.OS = 'ios'; delete global.fetch; });

describe('ideas board', () => {
  test('lists ideas with votes and statuses, filters by status chips, and orders through the filter panel', async () => {
    const { fake, dark, offline } = board();
    serveFakes([fake]);
    await renderRouter(APP_DIR, { initialUrl: '/ideas' });
    expect(await screen.findByTestId('idea-' + dark.id)).toBeOnTheScreen();
    expect(screen.getByTestId('idea-' + dark.id + '-votes')).toHaveTextContent('2');
    expect(screen.getByTestId('idea-' + dark.id + '-status')).toHaveTextContent('Planned');
    expect(screen.getByTestId('idea-' + offline.id)).toBeOnTheScreen();
    expect(lastEnumerate(fake)).toMatchObject({ maxResults: 20, ordering: 'Top' });

    await fireEvent.press(await screen.findByTestId('ideas-status-Planned'));
    await waitFor(() => expect(lastEnumerate(fake).filters).toEqual({ status: ['Planned'] }));
    await waitFor(() => expect(screen.queryByTestId('idea-' + offline.id)).toBeNull());
    await fireEvent.press(screen.getByTestId('ideas-flags-voted'));
    await waitFor(() => expect(lastEnumerate(fake).filters).toEqual({ status: ['Planned'], votedByMe: true }));

    await fireEvent.press(screen.getByTestId('ideas-filters-button'));
    await fireEvent.press(await screen.findByTestId('ideas-filters-panel-ordering-Trending'));
    await fireEvent.press(screen.getByTestId('ideas-filters-panel-apply'));
    await waitFor(() => expect(lastEnumerate(fake).ordering).toBe('Trending'));
  });

  test('Suggest an idea opens a support request for an enhancement when staff publish ideas', async () => {
    const { fake } = board();
    serveFakes([fake]);
    const router = await renderRouter(APP_DIR, { initialUrl: '/ideas' });
    await fireEvent.press(await screen.findByTestId('ideas-suggest'));
    await waitFor(() => expect(router.getPathname()).toBe('/support/new'));
  });

  test('members post ideas directly when the property allows it', async () => {
    const { fake } = board({ memberSubmissions: 'Direct' });
    const doc = { ...phase5Property(), ideas: { memberSubmissions: 'Direct' } };
    serveFakes([fake], { propertyDoc: doc });
    const router = await renderRouter(APP_DIR, { initialUrl: '/ideas' });
    await fireEvent.press(await screen.findByTestId('ideas-suggest'));
    await waitFor(() => expect(router.getPathname()).toBe('/ideas/new'));
    await fireEvent.press(await screen.findByTestId('new-idea-submit'));
    expect(await screen.findByText(t.titleTooShort)).toBeOnTheScreen();
    await fireEvent.changeText(screen.getByTestId('new-idea-title'), 'Calendar sync');
    await fireEvent.changeText(screen.getByTestId('new-idea-body'), 'Add events to my calendar.');
    await fireEvent.press(screen.getByTestId('new-idea-submit'));
    const created = await waitFor(() => {
      const i = fake.state.ideas.find((x) => x.title === 'Calendar sync');
      expect(i).toBeTruthy();
      return i;
    });
    await waitFor(() => expect(router.getPathname()).toBe('/ideas/' + created.id));
    expect(await screen.findByTestId('idea-header-title')).toHaveTextContent('Calendar sync');
  });
});

describe('an idea', () => {
  test('votes, unvotes, follows, and comments', async () => {
    const { fake, dark } = board();
    serveFakes([fake]);
    await renderRouter(APP_DIR, { initialUrl: '/ideas/' + dark.id });
    expect(await screen.findByTestId('idea-header-title')).toHaveTextContent('Dark mode');
    expect(screen.getByTestId('idea-header-vote')).toHaveTextContent(t.vote + ' · ' + t.votes(2));
    await fireEvent.press(screen.getByTestId('idea-header-vote'));
    await waitFor(() => expect(screen.getByTestId('idea-header-vote')).toHaveTextContent(t.voted + ' · ' + t.votes(3)));
    expect(fake.state.votes.some((v) => v.ideaId === dark.id && v.userId === me.id)).toBe(true);
    await fireEvent.press(screen.getByTestId('idea-header-vote'));
    await waitFor(() => expect(screen.getByTestId('idea-header-vote')).toHaveTextContent(t.vote + ' · ' + t.votes(2)));

    // Voting follows the idea automatically; Follow toggles it.
    expect(screen.getByTestId('idea-header-follow')).toHaveTextContent(t.following);
    await fireEvent.press(screen.getByTestId('idea-header-follow'));
    await waitFor(() => expect(screen.getByTestId('idea-header-follow')).toHaveTextContent(t.follow));
    expect(fake.state.follows.some((f) => f.ideaId === dark.id && f.userId === me.id)).toBe(false);
    await fireEvent.press(screen.getByTestId('idea-header-follow'));
    await waitFor(() => expect(screen.getByTestId('idea-header-follow')).toHaveTextContent(t.following));
    expect(fake.state.follows.some((f) => f.ideaId === dark.id && f.userId === me.id)).toBe(true);

    await fireEvent.changeText(await screen.findByTestId('comment-composer-body'), 'Please do this');
    await fireEvent.press(await screen.findByTestId('comment-composer-submit'));
    await waitFor(() => expect(fake.state.comments.some((c) => c.targetId === dark.id && c.body === 'Please do this')).toBe(true));
    expect(await screen.findByText('Please do this')).toBeOnTheScreen();
  });

  test('existing comments render in the shared comment tree, replies nested under their parent', async () => {
    const { fake, dark } = board();
    const base = { targetType: 'Idea', targetId: dark.id, state: 'Visible', author: { id: 'usr_7', displayName: 'Sam Lee', handle: 'sam' }, mentions: [], attachments: [], reactionCounts: {}, reactionTotal: 0, myReactions: [], labels: [], tags: {}, createdUtc: '2026-11-10T18:00:00Z' };
    fake.state.comments.push({ ...base, id: 'cmt_top', parentCommentId: null, depth: 0, body: 'Top level thought', replyCount: 1 });
    fake.state.comments.push({ ...base, id: 'cmt_child', parentCommentId: 'cmt_top', depth: 1, body: 'A nested reply' });
    serveFakes([fake]);
    await renderRouter(APP_DIR, { initialUrl: '/ideas/' + dark.id });
    expect(await screen.findByTestId('comment-cmt_top')).toBeOnTheScreen();
    expect(await screen.findByText('Top level thought')).toBeOnTheScreen();
    expect(await screen.findByTestId('comment-cmt_child')).toBeOnTheScreen();
    expect(screen.getByTestId('comment-cmt_child').props.accessibilityLabel).toBe(strings.comments.depthLabel(1));
    expect(fake.calls.some((c) => c.path === '/ideas/' + dark.id + '/comments/enumerate')).toBe(true);
  });

  test('a failed vote rolls back and says why', async () => {
    const { fake, dark } = board();
    fake.fail({ method: 'PUT', path: '/ideas/' + dark.id + '/vote', status: 409, code: 'IdeaMerged' });
    serveFakes([fake]);
    await renderRouter(APP_DIR, { initialUrl: '/ideas/' + dark.id });
    await fireEvent.press(await screen.findByTestId('idea-header-vote'));
    expect(await screen.findByTestId('idea-header-error')).toHaveTextContent(t.errors.IdeaMerged);
    expect(screen.getByTestId('idea-header-vote')).toHaveTextContent(t.vote + ' · ' + t.votes(2));
  });

  test('a merged idea shows the banner and has no comment composer', async () => {
    const fake = createIdeasFake({ me, staff: true });
    const target = fake.addIdea({ title: 'Themes', author: null });
    const dup = fake.addIdea({ title: 'Dark theme', author: null });
    fake.handle({ method: 'POST', path: '/admin/ideas/' + dup.id + '/merge', body: { intoIdeaId: target.id } });
    serveFakes([fake]);
    await renderRouter(APP_DIR, { initialUrl: '/ideas/' + dup.id });
    expect(await screen.findByTestId('idea-header-merged')).toHaveTextContent(t.merged('Themes'));
    expect(screen.getByTestId('idea-header-open-merged')).toBeOnTheScreen();
    expect(screen.queryByTestId('comment-composer-body')).toBeNull();
  });
});

describe('ideas access', () => {
  test('features.ideas off explains the board is not available, and Profile hides the entry', async () => {
    const { fake } = board();
    serveFakes([fake], { propertyDoc: phase5Property({ features: { ideas: false } }) });
    await renderRouter(APP_DIR, { initialUrl: '/ideas' });
    expect(await screen.findByTestId('ideas-off')).toBeOnTheScreen();
    expect(fake.calls.some((c) => c.path.startsWith('/ideas'))).toBe(false);
  });

  test('a non-member sees the upgrade prompt where ideas are gated', async () => {
    const { fake } = board();
    const user = makeUser({ membership: { isActive: false, source: null, store: null, expiresUtc: null }, membershipRequired: false, accessTier: 'SignedIn' });
    serveFakes([fake], { user, propertyDoc: phase5Property({ access: { model: 'PartiallyGated', gatedFeatures: ['ideas'] } }) });
    await renderRouter(APP_DIR, { initialUrl: '/ideas' });
    expect(await screen.findByTestId('ideas-locked-prompt')).toBeOnTheScreen();
  });

  test('the drawer links to ideas, announcements, and support', async () => {
    const { fake } = board();
    serveFakes([fake]);
    const router = await renderRouter(APP_DIR, { initialUrl: '/profile' });
    await fireEvent.press(await screen.findByTestId('header-menu'));
    expect(await screen.findByTestId('drawer-announcements')).toBeOnTheScreen();
    expect(screen.getByTestId('drawer-support')).toBeOnTheScreen();
    await fireEvent.press(screen.getByTestId('drawer-ideas'));
    await waitFor(() => expect(router.getPathname()).toBe('/ideas'));
  });
});

describe('ideas on a tablet', () => {
  test('the board and the idea sit side by side', async () => {
    const { fake, dark } = board();
    serveFakes([fake]);
    await atWidth(1180, async () => {
      await renderRouter(APP_DIR, { initialUrl: '/ideas' });
      expect(await screen.findByTestId('ideas-select')).toBeOnTheScreen();
      await fireEvent.press(await screen.findByTestId('idea-' + dark.id));
      const pane = await screen.findByTestId('split-detail-pane');
      expect(await within(pane).findByTestId('idea-header-title')).toHaveTextContent('Dark mode');
      await fireEvent.press(within(pane).getByTestId('idea-header-vote'));
      await waitFor(() => expect(screen.getByTestId('idea-' + dark.id + '-votes')).toHaveTextContent('3'));
    });
  });
});

describe('announcements', () => {
  function seededBroadcasts() {
    const fake = createBroadcastsFake({ me });
    for (const [id, title, body] of [
      ['bct_1', 'Holiday schedule', 'Opening hours change next week.'],
      ['bct_2', 'New feature', 'Try the ideas board.']
    ]) {
      fake.state.broadcasts.push({ id, title, status: 'Sent', inApp: { title, body }, sentUtc: '2026-11-11T12:00:00Z', labels: [], tags: {} });
      fake.state.deliveries.push({ broadcastId: id, user: me });
    }
    return fake;
  }

  test('lists received announcements and opens one', async () => {
    const fake = seededBroadcasts();
    serveFakes([fake]);
    const router = await renderRouter(APP_DIR, { initialUrl: '/announcements' });
    expect(await screen.findByTestId('announcement-bct_1')).toBeOnTheScreen();
    await fireEvent.press(screen.getByTestId('announcement-bct_2'));
    await waitFor(() => expect(router.getPathname()).toBe('/announcements/bct_2'));
    expect(await screen.findByTestId('announcement-title')).toHaveTextContent('New feature');
    expect(fake.calls.some((c) => c.path === '/announcements/bct_2')).toBe(true);
  });

  test('an unknown announcement says it is not available', async () => {
    const fake = seededBroadcasts();
    serveFakes([fake]);
    await renderRouter(APP_DIR, { initialUrl: '/announcements/bct_9' });
    expect(await screen.findByText(strings.announcements.goneTitle)).toBeOnTheScreen();
  });

  test('on a tablet the announcement opens beside the list', async () => {
    const fake = seededBroadcasts();
    serveFakes([fake]);
    await atWidth(1180, async () => {
      await renderRouter(APP_DIR, { initialUrl: '/announcements' });
      await fireEvent.press(await screen.findByTestId('announcement-bct_1'));
      const pane = await screen.findByTestId('split-detail-pane');
      expect(await within(pane).findByTestId('announcement-title')).toHaveTextContent('Holiday schedule');
    });
  });
});

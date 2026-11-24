import { act, fireEvent, screen, waitFor, within } from '@testing-library/react-native';
import { router as appRouter } from 'expo-router';
import { renderRouter } from 'expo-router/testing-library';
import path from 'path';
import { StyleSheet } from 'react-native';
import { firstUrl, isSafeUrl, parseMarkdown, toPlainText } from '../src/content/markdown';
import { extractHandles, insertMention, mentionQueryAt } from '../src/content/mentions';
import { cookieHeader, grantNeedsRefresh, mediaHeaders, resolveMediaUrl } from '../src/media/MediaProvider';
import strings from '../src/strings';
import { layout } from '../src/theme/tokens';
import { installFakeServer, makeUser, resetEnvironment, signedInAs } from '../test-utils/fakeServer';
import { enumerationResult, makeComment, makePost, makeNotification, staffSummary, userSummary } from '../test-utils/community';
import { expectedRankLine, property, rankField, rankKeys } from '../test-utils/property';

const APP_DIR = path.resolve(__dirname, '../app');

function profile(overrides = {}) {
  return { id: 'usr_2', displayName: 'Alex Chen', handle: 'alexchen', avatarUrl: null, bio: 'Hello there', profile: {}, createdUtc: '2026-01-01T00:00:00Z', isBlockedByMe: false, labels: [], tags: {}, ...overrides };
}

describe('Markdown subset', () => {
  test('blocks: headings (posts only), lists with one nesting level, quotes, paragraphs with line breaks', () => {
    const blocks = parseMarkdown('## Title\n\nLine one\nline two\n\n- a\n  - a1\n- b\n\n1. first\n2. second\n\n> quoted');
    expect(blocks.map((b) => b.type)).toEqual(['heading', 'paragraph', 'list', 'list', 'quote']);
    expect(blocks[1].children.map((n) => n.type)).toEqual(['text', 'break', 'text']);
    expect(blocks[2].items[0].list.items).toHaveLength(1);
    expect(blocks[3]).toMatchObject({ ordered: true, start: 1 });
    expect(parseMarkdown('## Title', { headings: false })[0].type).toBe('paragraph');
  });

  test('inline: emphasis, strike, safe links only, bare URLs without trailing punctuation, escapes', () => {
    const [p] = parseMarkdown('**b** *i* _u_ ~~s~~ [ok](https://example.test/x) [bad](javascript:alert(1)) see https://example.test/a. \\*not\\* snake_case_word');
    const types = p.children.map((n) => n.type);
    expect(types).toEqual(expect.arrayContaining(['strong', 'em', 'strike', 'link']));
    const links = p.children.filter((n) => n.type === 'link').map((n) => n.href);
    expect(links).toEqual(['https://example.test/x', 'https://example.test/a']);
    expect(toPlainText('[bad](javascript:alert(1))')).toBe('[bad](javascript:alert(1))');
    expect(toPlainText('\\*not\\* snake_case_word <b>x</b> `code`')).toBe('*not* snake_case_word <b>x</b> `code`');
    expect(isSafeUrl('ftp://example.test')).toBe(false);
    expect(firstUrl('hi https://example.test/z, bye')).toBe('https://example.test/z');
  });

  test('mentions link only when the server resolved them; emails are not mentions', () => {
    const [p] = parseMarkdown('Hi @AlexChen and @ghost, mail member@example.test.', { mentions: [{ userId: 'usr_2', handle: 'AlexChen', displayName: 'Alex Chen' }] });
    const mentions = p.children.filter((n) => n.type === 'mention');
    expect(mentions).toEqual([{ type: 'mention', handle: 'AlexChen', userId: 'usr_2', displayName: 'Alex Chen' }]);
    expect(toPlainText('Hi @ghost.')).toBe('Hi @ghost.');
    expect(extractHandles('a @abc. b@def @x @good_one')).toEqual(['abc', 'good_one']);
    expect(mentionQueryAt('Hi @al', 6)).toEqual({ query: 'al', start: 3, end: 6 });
    expect(mentionQueryAt('mail a@b', 8)).toBeNull();
    expect(mentionQueryAt('Hi @', 4)).toBeNull();
    expect(insertMention('Hi @al there', { start: 3, end: 6 }, 'alexchen')).toEqual({ text: 'Hi @alexchen there', cursor: 13 });
  });
});

describe('media delivery', () => {
  const api = { baseUrl: 'http://127.0.0.1:8000', accessToken: 'tok' };
  test('grant cookies become one Cookie header; the bearer token only goes to the API origin', () => {
    const grant = { expiresUtc: '2099-01-01T00:00:00Z', cookies: [{ name: 'CloudFront-Policy', value: 'p' }, { name: 'CloudFront-Signature', value: 's' }, { name: 'CloudFront-Key-Pair-Id', value: 'k' }] };
    expect(cookieHeader(grant)).toBe('CloudFront-Policy=p; CloudFront-Signature=s; CloudFront-Key-Pair-Id=k');
    expect(resolveMediaUrl(api, '/api/v1/media/files/med_1/thumb.webp')).toBe('http://127.0.0.1:8000/api/v1/media/files/med_1/thumb.webp');
    expect(mediaHeaders(api, 'https://media.example.test/media/med_1/thumb.webp', grant)).toEqual({ Cookie: cookieHeader(grant) });
    expect(mediaHeaders(api, 'http://127.0.0.1:8000/api/v1/media/files/med_1/x', grant).Authorization).toBe('Bearer tok');
    expect(grantNeedsRefresh({ expiresUtc: new Date(Date.now() + 5 * 60000).toISOString() })).toBe(true);
    expect(grantNeedsRefresh({ expiresUtc: new Date(Date.now() + 60 * 60000).toISOString() })).toBe(false);
  });

  test('videos play HLS with the grant headers', async () => {
    resetEnvironment();
    const video = { id: 'med_v', kind: 'Video', status: 'Ready', width: 1920, height: 1080, video: { hlsUrl: 'https://media.example.test/video/med_v/master.m3u8', mp4Url: null, posterUrl: null, posterThumbUrl: null }, image: null, labels: [], tags: {} };
    installFakeServer({
      ...signedInAs(makeUser()),
      'POST /media/grant': { mode: 'CloudFront', expiresUtc: '2099-01-01T00:00:00Z', cookies: [{ name: 'CloudFront-Policy', value: 'p' }] },
      'POST /feed/enumerate': enumerationResult([makePost('fpo_1', { attachments: [video] })])
    });
    await renderRouter(APP_DIR, { initialUrl: '/feed' });
    await waitFor(() => expect(screen.getByTestId('post-fpo_1-media-video-0').props.player.source).toEqual({ uri: video.video.hlsUrl, headers: { Cookie: 'CloudFront-Policy=p', 'X-TK-Property': property.id, 'X-TK-App': property.mobile.bundleId }, contentType: 'hls' }));
    delete global.fetch;
  });
});

describe('report and block', () => {
  beforeEach(() => resetEnvironment());
  afterEach(() => delete global.fetch);

  test('report a post: a reason is required, Other needs details; then thanks and the block offer', async () => {
    const server = installFakeServer({
      ...signedInAs(makeUser()),
      'POST /feed/enumerate': (call) => enumerationResult([makePost('fpo_1'), makePost('fpo_2', { author: userSummary({ id: 'usr_3', handle: 'sam', displayName: 'Sam' }) })]),
      'POST /reports': { status: 201, body: { id: 'rpt_1', status: 'Open', createdUtc: '2026-11-11T00:00:00Z' } },
      'POST /users/usr_2/block': { blocked: true }
    });
    await renderRouter(APP_DIR, { initialUrl: '/feed' });
    await fireEvent.press(await screen.findByTestId('post-fpo_1-menu'));
    await fireEvent.press(await screen.findByTestId('post-fpo_1-actions-report'));
    const sheet = await screen.findByTestId('report');
    await fireEvent.press(within(sheet).getByTestId('report-submit'));
    expect(within(sheet).getByTestId('report-error')).toHaveTextContent(strings.report.chooseReason);
    await fireEvent.press(within(sheet).getByTestId('report-reason-Other'));
    await fireEvent.changeText(within(sheet).getByTestId('report-details'), 'short');
    await fireEvent.press(within(sheet).getByTestId('report-submit'));
    expect(within(sheet).getByTestId('report-error')).toHaveTextContent(strings.report.detailsRequired);
    await fireEvent.changeText(within(sheet).getByTestId('report-details'), 'Keeps posting the same link.');
    await fireEvent.press(within(sheet).getByTestId('report-submit'));
    expect(await within(sheet).findByTestId('report-thanks')).toHaveTextContent('Thanks. A moderator will review this.');
    expect(server.called('POST /reports')[0].body).toEqual({ targetType: 'FeedPost', targetId: 'fpo_1', reason: 'Other', details: 'Keeps posting the same link.' });

    await fireEvent.press(within(sheet).getByTestId('report-block'));
    await waitFor(() => expect(server.called('POST /users/usr_2/block')).toHaveLength(1));
    // The blocked author's posts leave the feed (and the sheet with them).
    await waitFor(() => expect(screen.queryByTestId('post-fpo_1')).toBeNull());
    expect(screen.getByTestId('post-fpo_2')).toBeOnTheScreen();
  });

  test('staff authors cannot be blocked: no block action, and the report thanks explains why', async () => {
    installFakeServer({ ...signedInAs(makeUser()), 'POST /feed/enumerate': enumerationResult([makePost('fpo_1', { author: staffSummary })]), 'POST /reports': { status: 201, body: { id: 'rpt_1', status: 'Open' } } });
    await renderRouter(APP_DIR, { initialUrl: '/feed' });
    await fireEvent.press(await screen.findByTestId('post-fpo_1-menu'));
    expect(screen.queryByTestId('post-fpo_1-actions-block')).toBeNull();
    await fireEvent.press(screen.getByTestId('post-fpo_1-actions-report'));
    await fireEvent.press(await screen.findByTestId('report-reason-Spam'));
    await fireEvent.press(screen.getByTestId('report-submit'));
    await screen.findByTestId('report-thanks');
    expect(screen.queryByTestId('report-block')).toBeNull();
    expect(screen.getByText(strings.block.staff)).toBeOnTheScreen();
  });

  test('blocking a comment author from the menu hides their comments', async () => {
    const server = installFakeServer({
      ...signedInAs(makeUser()),
      'GET /feed/fpo_1': makePost('fpo_1'),
      'POST /feed/fpo_1/comments/enumerate': enumerationResult([makeComment('cmt_1', { author: userSummary({ id: 'usr_5', handle: 'pest' }) })]),
      'POST /users/usr_5/block': { blocked: true }
    });
    await renderRouter(APP_DIR, { initialUrl: '/feed/fpo_1' });
    await fireEvent.press(await screen.findByTestId('comment-cmt_1-menu'));
    await fireEvent.press(await screen.findByTestId('comment-cmt_1-actions-block'));
    await fireEvent.press(await screen.findByTestId('comment-cmt_1-block-confirm'));
    expect(await screen.findByTestId('comment-cmt_1-placeholder')).toHaveTextContent(strings.comments.placeholders.BlockedAuthor);
    expect(server.called('POST /users/usr_5/block')).toHaveLength(1);
  });

  test('a server CannotBlockStaff answer is explained', async () => {
    installFakeServer({ ...signedInAs(makeUser()), 'GET /users/usr_2': profile(), 'POST /users/usr_2/block': { status: 400, body: { error: 'CannotBlockStaff', message: 'No.' } } });
    await renderRouter(APP_DIR, { initialUrl: '/members/usr_2' });
    await fireEvent.press(await screen.findByTestId('member-block'));
    await fireEvent.press(await screen.findByTestId('member-block-confirm-confirm'));
    expect(await screen.findByTestId('member-block-confirm-error')).toHaveTextContent(strings.block.staff);
  });

  test('member profile: rank from the property, block and unblock, and staff profiles explain they cannot be blocked', async () => {
    const rank = rankField && rankKeys.length ? { key: rankKeys[0], level: 1 } : null;
    const server = installFakeServer({
      ...signedInAs(makeUser()),
      'GET /users/usr_2': profile(rank ? { profile: { [rankField.key]: rank } } : {}),
      'GET /users/usr_9': profile({ id: 'usr_9', displayName: 'Example Owner', handle: 'owner', isStaff: true }),
      'POST /users/usr_2/block': { blocked: true },
      'DELETE /users/usr_2/block': { blocked: false }
    });
    await renderRouter(APP_DIR, { initialUrl: '/members/usr_2' });
    expect(await screen.findByTestId('member-name')).toHaveTextContent('Alex Chen');
    if (rank) expect(screen.getByTestId('member-card-' + rankField.key + '-text')).toHaveTextContent(expectedRankLine(rank));
    expect(screen.getByTestId('member-bio')).toHaveTextContent('Hello there');
    await fireEvent.press(screen.getByTestId('member-block'));
    await fireEvent.press(await screen.findByTestId('member-block-confirm-confirm'));
    expect(await screen.findByTestId('member-message')).toHaveTextContent(strings.block.blocked('alexchen'));
    expect(screen.getByTestId('member-block')).toHaveTextContent(strings.member.unblock);
    await fireEvent.press(screen.getByTestId('member-block'));
    await waitFor(() => expect(server.called('DELETE /users/usr_2/block')).toHaveLength(1));
    expect(await screen.findByTestId('member-message')).toHaveTextContent(strings.block.unblocked('alexchen'));

    await act(async () => appRouter.push('/members/usr_9'));
    expect(await screen.findByTestId('member-staff-note')).toHaveTextContent(strings.block.staff);
    expect(screen.queryByTestId('member-block')).toBeNull();
  });

  test('blocked members list pages and unblocks, and explains staff cannot be blocked', async () => {
    const server = installFakeServer({
      ...signedInAs(makeUser()),
      'POST /me/blocks/enumerate': (call) => (call.body.continuationToken ? enumerationResult([{ user: userSummary({ id: 'usr_4', displayName: 'Pat' }), createdUtc: '2026-11-01T00:00:00Z' }]) : enumerationResult([{ user: userSummary(), createdUtc: '2026-11-01T00:00:00Z' }], 'page-2')),
      'DELETE /users/usr_2/block': { blocked: false }
    });
    await renderRouter(APP_DIR, { initialUrl: '/account/blocked' });
    expect(await screen.findByTestId('blocked-usr_2')).toBeOnTheScreen();
    expect(screen.getByTestId('blocked-staff-note')).toHaveTextContent(strings.blocked.staffNote);
    await fireEvent.press(screen.getByTestId('blocked-load-more'));
    expect(await screen.findByTestId('blocked-usr_4')).toBeOnTheScreen();
    expect(server.called('POST /me/blocks/enumerate')[1].body).toEqual({ maxResults: 50, continuationToken: 'page-2' });
    await fireEvent.press(screen.getByTestId('blocked-usr_2-unblock'));
    await waitFor(() => expect(screen.queryByTestId('blocked-usr_2')).toBeNull());
    // The blocks collection only orders by block time; a Name ordering would be 400 ValidationFailed.
    await fireEvent.press(screen.getByTestId('blocked-filters-button'));
    expect(await screen.findByTestId('blocked-filters-panel-ordering-CreatedAscending')).toBeOnTheScreen();
    expect(screen.queryByTestId('blocked-filters-panel-ordering-DisplayNameAscending')).toBeNull();
  });
});

describe('accessibility of Phase 2 screens', () => {
  const INTERACTIVE = ['button', 'radio', 'checkbox', 'tab', 'imagebutton'];
  function textOf(node) {
    if (!node) return '';
    if (typeof node === 'string') return node;
    if (Array.isArray(node)) return node.map(textOf).join('');
    return textOf(node.children);
  }
  function hostNodes(node, out = []) {
    if (!node || typeof node === 'string') return out;
    if (Array.isArray(node)) {
      node.forEach((n) => hostNodes(n, out));
      return out;
    }
    out.push(node);
    hostNodes(node.children, out);
    return out;
  }
  beforeEach(() => resetEnvironment());
  afterEach(() => delete global.fetch);

  test.each([
    ['/feed', { 'POST /feed/enumerate': enumerationResult([makePost('fpo_1', { editedUtc: '2026-11-10T19:00:00Z', reactionCounts: { [property.reactions[0].key]: 1 }, reactionTotal: 1 })]) }],
    ['/feed/fpo_1', { 'GET /feed/fpo_1': makePost('fpo_1'), 'POST /feed/fpo_1/comments/enumerate': enumerationResult([makeComment('cmt_1', { childCount: 1, children: [makeComment('cmt_2', { depth: 1 })] })]) }],
    ['/notifications', { 'POST /notifications/enumerate': enumerationResult([makeNotification('ntf_1')]) }],
    ['/account/blocked', { 'POST /me/blocks/enumerate': enumerationResult([{ user: userSummary(), createdUtc: '2026-11-01T00:00:00Z' }]) }],
    ['/members/usr_2', { 'GET /users/usr_2': profile() }],
    ['/compose', {}]
  ])('%s: controls are labeled and at least 44 points tall', async (url, routes) => {
    installFakeServer({ ...signedInAs(makeUser({ flags: { canPostToFeed: true } })), ...routes });
    const router = await renderRouter(APP_DIR, { initialUrl: url });
    await waitFor(() => expect(router.getPathname()).toBe(url.split('?')[0]));
    await waitFor(() => expect(screen.queryByLabelText(/Loading|Signing you in/)).toBeNull());
    await screen.findAllByRole('button');
    const controls = hostNodes(screen.toJSON()).filter((n) => n.props && INTERACTIVE.includes(n.props.accessibilityRole) && n.type !== 'Text');
    expect(controls.length).toBeGreaterThan(0);
    for (const node of controls) {
      const name = node.props.accessibilityLabel || textOf(node);
      expect({ url, name: Boolean(name && name.trim()) }).toEqual({ url, name: true });
      const style = StyleSheet.flatten(node.props.style) || {};
      const slop = typeof node.props.hitSlop === 'number' ? node.props.hitSlop * 2 : node.props.hitSlop ? (node.props.hitSlop.top || 0) + (node.props.hitSlop.bottom || 0) : 0;
      const height = Math.max(style.minHeight || 0, style.height || 0) + slop;
      expect({ url, name, ok: height >= layout.minTouchTarget }).toEqual({ url, name, ok: true });
    }
  });
});

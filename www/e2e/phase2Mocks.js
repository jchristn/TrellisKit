import { expect } from '@playwright/test';

/** Mocked API for the Phase 2 smoke tests (EnumerationResult lists, a SystemAdmin session). */
const NOW = '2026-11-10T18:00:00Z';
const user = (id, name, extra = {}) => ({ id, displayName: name, handle: name.toLowerCase().replace(/\s+/g, ''), avatarUrl: null, rank: null, rankLevel: 0, isStaff: false, isDeleted: false, ...extra });
const page = (objects, extra = {}) => ({ success: true, maxResults: 25, skip: 0, continuationToken: null, endOfResults: true, totalRecords: objects.length, recordsRemaining: 0, timestampUtc: NOW, objects, ...extra });

const me = {
  id: 'usr_1', email: 'admin@example.test', emailVerified: true, displayName: 'Pat Admin', handle: 'pat', avatarUrl: null, rank: null, rankLevel: 0, ageAttested: true, profileComplete: true,
  signInMethods: ['Email'], roles: [{ role: 'SystemAdmin', resourceType: 'Global', resourceId: '*' }], labels: [], tags: {},
  flags: { canPostToFeed: true, canPost: true, canPostMedia: true, canUploadVideo: true, isMuted: false, mutedUntilUtc: null },
  membership: { isActive: true, source: 'AdminComp', expiresUtc: null }, createdUtc: NOW
};

const viewer = { canEdit: true, canDelete: true, canPin: true, canComment: true, canReact: true, canReport: false, canModerate: true, canAttachMedia: true, canAttachVideo: true };
const post = (id, extra = {}) => ({
  id, kind: 'Original', author: user('usr_2', 'Alex Chen', { isStaff: true }), title: 'A post with a fairly long title that should wrap cleanly ' + id,
  body: 'Hello **members**. Read https://example.test/about and say hi to @sam.\n\n- one\n- two\n\n> quoted', excerpt: 'Hello members.', mentions: [{ userId: 'usr_3', handle: 'sam', displayName: 'Sam' }],
  attachments: [], linkPreview: { url: 'https://example.test/about', canonicalUrl: 'https://example.test/about', siteName: 'Example', title: 'About us', description: 'Who we are.', imageUrl: null },
  visibility: 'Public', state: 'Published', commentsEnabled: true, isPinned: id === 'fpo_1', publishedUtc: NOW, createdUtc: NOW, updatedUtc: NOW, editedUtc: id === 'fpo_1' ? NOW : null,
  revisionCount: 1, commentCount: 2, reactionCounts: { like: 3 }, reactionTotal: 3, myReactions: ['like'], commentTarget: { type: 'FeedPost', id }, labels: [], tags: {}, viewer, ...extra
});
const comment = (id, extra = {}) => ({
  id, targetType: 'FeedPost', targetId: 'fpo_1', parentCommentId: null, depth: 0, state: 'Visible', author: user('usr_3', 'Sam Lee'), body: 'A comment that says something useful.', mentions: [], attachments: [],
  createdUtc: NOW, editedUtc: null, childCount: 0, descendantCount: 0, reactionCounts: {}, reactionTotal: 0, myReactions: [], labels: [], tags: {},
  viewer: { canEdit: false, canDelete: true, canReply: true, canReact: true, canReport: true }, children: [], moreChildren: null, ...extra
});

export async function mockApi(page_) {
  await page_.route('**/api/v1/**', async (route) => {
    const url = new URL(route.request().url());
    const path = url.pathname.replace('/api/v1', '');
    const send = (body, status = 200) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) });
    if (path === '/auth/refresh') return send({ accessToken: 'a', accessTokenExpiresUtc: '2099-01-01T00:00:00Z', user: me });
    if (path === '/property') return send({}, 404);
    if (path === '/health') return send({ status: 'Healthy', version: '1.0.0', startedUtc: NOW, uptimeSeconds: 10, database: 'Sqlite' });
    if (path === '/media/grant') return send({ mode: 'Local', expiresUtc: '2099-01-01T00:00:00Z', cookies: [] });
    if (path === '/notifications/unread-count') return send({ unreadCount: 3 });
    if (path === '/feed') return send(page([post('fpo_1'), post('fpo_2'), post('fpo_3', { visibility: 'Members' })]));
    if (path === '/feed/fpo_1') return send(post('fpo_1'));
    if (path === '/feed/fpo_1/comments') {
      return send(page([comment('cmt_1', { childCount: 1, descendantCount: 1, children: [comment('cmt_2', { parentCommentId: 'cmt_1', depth: 1, body: 'A reply.' })] }), comment('cmt_3', { state: 'Deleted', body: null, author: null, viewer: {}, childCount: 1, children: [comment('cmt_4', { parentCommentId: 'cmt_3', depth: 1 })] })], { commentCount: 4 }));
    }
    if (path === '/notifications') {
      return send(page([
        { id: 'ntf_1', type: 'CommentReply', category: 'Replies', title: 'Sam Lee replied to your comment', body: 'A reply.', actor: user('usr_3', 'Sam Lee'), path: '/feed/fpo_1?comment=cmt_2', isRead: false, createdUtc: NOW, updatedUtc: NOW },
        { id: 'ntf_2', type: 'NewFeedPost', category: 'FeedPosts', title: 'Alex Chen posted: Welcome', body: 'Hello members.', actor: user('usr_2', 'Alex Chen'), path: '/feed/fpo_1', isRead: true, createdUtc: NOW, updatedUtc: NOW }
      ], { unreadCount: 1 }));
    }
    if (path === '/admin/moderation/queue') {
      return send(page([{ targetType: 'Comment', targetId: 'cmt_9', targetState: 'Hidden', preview: { excerpt: 'Reported comment text', hasMedia: false }, author: user('usr_5', 'Riley'), reportCount: 3, reporterCount: 3, reasons: { Spam: 2, Harassment: 1 }, firstReportedUtc: NOW, lastReportedUtc: NOW, dueUtc: '2026-11-10T06:00:00Z', isOverdue: true, status: 'Open' }]));
    }
    if (path.startsWith('/labels/') || path.startsWith('/tags/')) return send(page([]));
    return send({ error: 'NotFound', message: 'Not mocked: ' + path }, 404);
  });
  await page_.addInitScript(() => window.localStorage.setItem('tk.hasSession', '1'));
}


/** Fail when the page scrolls horizontally. */
export async function noHorizontalScroll(page_) {
  const overflow = await page_.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);
  expect(overflow).toBeLessThanOrEqual(0);
}

/**
 * Phase 2 payload builders (docs/PHASE2_CONTRACT.md section 2, 5, 7, 8) for tests. Reaction keys
 * come from the active property so the suite runs for any property.
 */
import { property } from './property';

/** The active property's reaction keys, in order. */
export const reactionKeys = (property.reactions || []).map((r) => r.key);
export const likeKey = reactionKeys[0];
export const secondKey = reactionKeys[1] || reactionKeys[0];

export function userSummary(overrides = {}) {
  return { id: 'usr_2', displayName: 'Alex Chen', handle: 'alexchen', avatarUrl: null, profile: {}, isStaff: false, isDeleted: false, ...overrides };
}

export const staffSummary = userSummary({ id: 'usr_9', displayName: 'Example Owner', handle: 'owner', isStaff: true });

const postViewer = { canEdit: false, canDelete: false, canPin: false, canComment: true, canReact: true, canReport: true, canModerate: false, canAttachMedia: true, canAttachVideo: true };
const commentViewer = { canEdit: false, canDelete: false, canReply: true, canReact: true, canReport: true, canModerate: false, canAttachMedia: true, canAttachVideo: true };

export function makePost(id, overrides = {}) {
  return {
    id,
    kind: 'Original',
    author: userSummary(),
    title: null,
    body: 'Post ' + id,
    excerpt: 'Post ' + id,
    mentions: [],
    attachments: [],
    linkPreview: null,
    visibility: 'Members',
    state: 'Published',
    commentsEnabled: true,
    isPinned: false,
    pinnedUtc: null,
    publishAtUtc: null,
    publishedUtc: '2026-11-10T18:00:00Z',
    createdUtc: '2026-11-10T17:58:00Z',
    updatedUtc: '2026-11-10T18:00:00Z',
    editedUtc: null,
    revisionCount: 0,
    commentCount: 0,
    reactionCounts: {},
    reactionTotal: 0,
    myReactions: [],
    commentTarget: { type: 'FeedPost', id },
    labels: [],
    tags: {},
    source: null,
    moderation: null,
    viewer: { ...postViewer },
    ...overrides
  };
}

export function makeComment(id, overrides = {}) {
  return {
    id,
    targetType: 'FeedPost',
    targetId: 'fpo_1',
    parentCommentId: null,
    replyToCommentId: null,
    replyToUser: null,
    depth: 0,
    state: 'Visible',
    author: userSummary(),
    body: 'Comment ' + id,
    mentions: [],
    attachments: [],
    linkPreview: null,
    createdUtc: '2026-11-10T18:20:00Z',
    editedUtc: null,
    revisionCount: 0,
    childCount: 0,
    descendantCount: 0,
    reactionCounts: {},
    reactionTotal: 0,
    myReactions: [],
    viewer: { ...commentViewer },
    children: [],
    moreChildren: null,
    labels: [],
    tags: {},
    ...overrides
  };
}

export function makeMedia(id, overrides = {}) {
  return {
    id,
    kind: 'Image',
    status: 'Ready',
    failureReason: null,
    ownerUserId: 'usr_1',
    contentType: 'image/jpeg',
    sizeBytes: 1000,
    width: 1200,
    height: 900,
    durationSeconds: null,
    altText: null,
    blurhash: 'LKO2?U%2Tw=w]~RBVZRi};RPxuwH',
    image: {
      variants: {
        thumb: { width: 320, height: 240, webpUrl: '/api/v1/media/files/' + id + '/thumb.webp', jpegUrl: '/api/v1/media/files/' + id + '/thumb.jpg' },
        medium: { width: 1080, height: 810, webpUrl: '/api/v1/media/files/' + id + '/medium.webp', jpegUrl: '/api/v1/media/files/' + id + '/medium.jpg' },
        large: { width: 2048, height: 1536, webpUrl: '/api/v1/media/files/' + id + '/large.webp', jpegUrl: '/api/v1/media/files/' + id + '/large.jpg' }
      }
    },
    video: null,
    createdUtc: '2026-11-10T18:00:00Z',
    readyUtc: '2026-11-10T18:00:04Z',
    ...overrides
  };
}

export function makeNotification(id, overrides = {}) {
  return {
    id,
    type: 'CommentReply',
    category: 'Replies',
    title: 'Alex Chen replied to your comment',
    body: 'Does this work for beginners too?',
    actor: userSummary(),
    actors: [userSummary()],
    actorCount: 1,
    target: { type: 'Comment', id: 'cmt_1' },
    context: { feedPostId: 'fpo_1', commentId: 'cmt_1' },
    path: '/feed/fpo_1?comment=cmt_1',
    isRead: false,
    readUtc: null,
    createdUtc: '2026-11-10T18:20:00Z',
    updatedUtc: '2026-11-10T18:20:00Z',
    ...overrides
  };
}

/** The contract's default preferences document (every channel on where it is supported). */
export function defaultPreferences(overrides = {}) {
  return {
    pushPaused: false,
    categories: [
      { category: 'Replies', inApp: true, push: true, email: false, editable: { inApp: true, push: true, email: false } },
      { category: 'Mentions', inApp: true, push: true, email: false, editable: { inApp: true, push: true, email: false } },
      { category: 'Reactions', inApp: true, push: true, email: false, editable: { inApp: true, push: true, email: false } },
      { category: 'FeedPosts', inApp: true, push: true, email: true, editable: { inApp: true, push: true, email: true } },
      { category: 'Moderation', inApp: true, push: true, email: false, editable: { inApp: false, push: true, email: false } },
      { category: 'Account', inApp: true, push: true, email: true, editable: { inApp: false, push: true, email: false } }
    ],
    notificationHours: { enabled: true, start: '07:00', end: '22:00', timeZone: 'America/Los_Angeles' },
    ...overrides
  };
}

/** An EnumerationResult (docs/ENUMERATION_AND_METADATA.md section 2.3). */
export function enumerationResult(objects, continuationToken = null, extra = {}) {
  return { success: true, maxResults: 25, skip: 0, continuationToken, endOfResults: !continuationToken, totalRecords: null, recordsRemaining: null, timestampUtc: '2026-11-11T23:00:00Z', objects, ...extra };
}

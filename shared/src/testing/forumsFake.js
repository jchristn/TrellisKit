import { dailyMetricPage } from './metricFake.js';

/**
 * An in-memory fake of the Phase 3 forum routes (docs/PHASE3_CONTRACT.md), for client tests and
 * for building the website and the mobile app before the backend exists (section 20: clients
 * build against the contract with the shared ApiClient fakes). It follows the contract shapes
 * and error codes closely enough to drive every screen; it is not a second backend.
 *
 * Use `fake.handle({ method, path, query, body })` (path relative to `/api/v1`) from a test
 * harness, or `fake.fetchImpl` as an `ApiClient` fetch. Unknown routes return undefined from
 * `handle` (so a harness can fall through) and 404 from `fetchImpl`.
 */

const VIEWER_THREAD = Object.freeze({
  canEdit: false, canDelete: false, canComment: true, canReact: true, canReport: true,
  canModerate: false, canPin: false, canLock: false, canMove: false,
  canAcceptAnswer: false, canVote: true, canClosePoll: false, canSetFeedbackStatus: false,
  canPromote: false, canFollow: true, canAttachMedia: true, canAttachVideo: true
});

const MODERATOR_THREAD = Object.freeze({ canModerate: true, canPin: true, canLock: true, canMove: true, canAcceptAnswer: true, canClosePoll: true, canSetFeedbackStatus: true });

/** A UserSummary. */
export function fakeUser(id, displayName, extra = {}) {
  return { id, displayName, handle: String(displayName).toLowerCase().replace(/[^a-z0-9]+/g, ''), avatarUrl: null, rank: null, rankLevel: 0, isStaff: false, isDeleted: false, ...extra };
}

/** A Forum (section 2.1). */
export function makeForum(id, extra = {}) {
  const name = extra.name || 'Forum ' + id;
  return {
    id, slug: extra.slug || id.replace(/^frm_/, '').replace(/[^a-z0-9-]/g, '-'), name, description: 'About ' + name + '.', guidelines: 'Be kind.',
    icon: { emoji: null, mediaId: null, imageUrl: null }, color: null, section: null, sortOrder: 10, audience: 'Members', threadCreation: 'Members',
    allowedThreadTypes: ['Discussion', 'Question', 'Poll', 'Feedback'], defaultOrdering: 'Active', allowMedia: true, allowVideo: true, isArchived: false,
    threadCount: 0, commentCount: 0, lastActivityUtc: null, lastThread: null, isFollowed: false, unreadThreadCount: 0, labels: [], tags: {},
    viewer: { canCreateThread: true, canFollow: true, canModerate: false, allowedThreadTypes: extra.allowedThreadTypes || ['Discussion', 'Question', 'Poll', 'Feedback'] },
    createdUtc: '2026-11-01T00:00:00Z', updatedUtc: '2026-11-01T00:00:00Z',
    ...extra
  };
}

/** A Poll (section 6.2) from option labels. */
export function makePoll(id, threadId, labels, extra = {}) {
  return {
    id, threadId, multipleChoice: false, maxChoices: null, resultsVisibility: 'AfterVote', allowVoteChange: true, closesUtc: null, closedUtc: null, isClosed: false,
    totalVoters: 0, resultsVisible: false, myVote: [],
    options: labels.map((label, i) => ({ id: id.replace(/^pol_/, 'pop_') + '_' + (i + 1), label, position: i + 1, voteCount: 0, percent: null, labels: [], tags: {} })),
    labels: [], tags: {},
    ...extra
  };
}

/** A Published Original Thread (section 2.2) in `forum`. */
export function makeThread(id, forum, extra = {}) {
  return {
    id, forum: { id: forum.id, slug: forum.slug, name: forum.name }, kind: 'Original', type: 'Discussion', author: fakeUser('usr_2', 'Alex Chen'),
    title: 'Thread ' + id, body: 'Body of ' + id, excerpt: 'Body of ' + id, mentions: [], attachments: [], linkPreview: null, audience: forum.audience || 'Members',
    state: 'Published', isPinned: false, pinnedUtc: null, isLocked: false, lockedUtc: null, lockReason: null, acceptedAnswer: null, poll: null, feedback: null,
    commentTarget: { type: 'Thread', id }, source: null, promotion: null, commentCount: 0, uniqueCommenterCount: 0, reactionCounts: {}, reactionTotal: 0, myReactions: [],
    trendingScore: 0, lastActivityUtc: '2026-11-10T18:00:00Z', lastCommentUtc: null, isFollowed: false, isUnread: false, editedUtc: null, revisionCount: 0,
    moderation: null, labels: [], tags: {}, viewer: { ...VIEWER_THREAD }, publishedUtc: '2026-11-10T18:00:00Z', createdUtc: '2026-11-10T18:00:00Z', updatedUtc: '2026-11-10T18:00:00Z',
    ...extra
  };
}

/** A visible thread Comment (Phase 2 section 7.2 plus `isAcceptedAnswer`, `isFeedbackReview`). */
export function makeThreadComment(id, threadId, extra = {}) {
  return {
    id, targetType: 'Thread', targetId: threadId, parentCommentId: null, replyToCommentId: null, replyToUser: null, depth: 0, state: 'Visible', author: fakeUser('usr_3', 'Sam Lee'),
    body: 'Comment ' + id, mentions: [], attachments: [], linkPreview: null, createdUtc: '2026-11-10T19:00:00Z', editedUtc: null, revisionCount: 0, childCount: 0, descendantCount: 0,
    reactionCounts: {}, reactionTotal: 0, myReactions: [], isAcceptedAnswer: false, isFeedbackReview: false, labels: [], tags: {},
    viewer: { canEdit: false, canDelete: false, canReply: true, canReact: true, canReport: true, canModerate: false, canAttachMedia: true, canAttachVideo: true },
    children: [], moreChildren: null, ...extra
  };
}

/** Viewer capabilities of a moderator in scope (merge into a thread's `viewer`). */
export const moderatorViewer = MODERATOR_THREAD;

function clone(value) {
  return value === undefined ? undefined : JSON.parse(JSON.stringify(value));
}

function page(objects, query = {}, { max = 100 } = {}) {
  const size = Math.max(1, Math.min(max, Math.floor(Number(query.maxResults) || 25)));
  const token = query.continuationToken;
  const offset = token && /^o:\d+$/.test(token) ? Number(token.slice(2)) : Math.max(0, Math.floor(Number(query.skip) || 0));
  const slice = objects.slice(offset, offset + size);
  const end = offset + size >= objects.length;
  return {
    success: true, maxResults: size, skip: offset, continuationToken: end ? null : 'o:' + (offset + size), endOfResults: end,
    totalRecords: objects.length, recordsRemaining: Math.max(0, objects.length - offset - slice.length), timestampUtc: '2026-11-11T00:00:00Z', objects: clone(slice)
  };
}

function asList(value) {
  if (value === undefined || value === null || value === '') return [];
  return Array.isArray(value) ? value : String(value).split(',');
}

function truthy(value) {
  return value === true || value === 'true';
}

function matchesMeta(item, query) {
  const labels = (query.labels || []).map((l) => String(l).toLowerCase());
  if (labels.length) {
    const own = (item.labels || []).map((l) => String(l).toLowerCase());
    const hit = query.labelMatch === 'Any' ? labels.some((l) => own.includes(l)) : labels.every((l) => own.includes(l));
    if (!hit) return false;
  }
  for (const [key, value] of Object.entries(query.tags || {})) {
    const own = item.tags || {};
    const found = Object.keys(own).find((k) => k.toLowerCase() === key.toLowerCase());
    if (!found) return false;
    if (value !== '*' && own[found] !== value) return false;
  }
  return true;
}

function snippet(text, term) {
  const source = String(text || '');
  const at = source.toLowerCase().indexOf(term.toLowerCase());
  if (at < 0) return null;
  const start = Math.max(0, at - 40);
  const cut = source.slice(start, start + 200);
  return { text: cut, highlights: [{ start: at - start, length: term.length }] };
}

function error(status, code, message, fieldErrors) {
  return { status, body: { error: code, message: message || code, ...(fieldErrors ? { fieldErrors } : {}) } };
}

function ok(body, status = 200) {
  return { status, body: clone(body) };
}

/**
 * Create a fake.
 * @param {{ me?: object, forums?: object[], threads?: object[], comments?: object[], feedPosts?: object[], now?: () => string }} [seed]
 */
export function createForumsFake(seed = {}) {
  let n = 0;
  const nextId = (prefix) => prefix + 'fake' + (++n);
  const now = seed.now || (() => '2026-11-11T12:00:00Z');
  const state = {
    me: seed.me || fakeUser('usr_1', 'Sam Rivera'),
    forums: (seed.forums || []).map(clone),
    threads: (seed.threads || []).map(clone),
    comments: (seed.comments || []).map(clone),
    feedPosts: (seed.feedPosts || []).map(clone),
    follows: (seed.follows || []).map(clone),
    mutes: (seed.mutes || []).map(clone),
    moderators: (seed.moderators || []).map(clone),
    moderationActions: [],
    pollVoters: {},
    reads: []
  };
  const calls = [];

  const forum = (idOrSlug) => state.forums.find((f) => f.id === idOrSlug || f.slug === idOrSlug);
  const thread = (id) => state.threads.find((t) => t.id === id && !t.deletedUtc);
  const pollOwner = (pollId) => state.threads.find((t) => t.poll && t.poll.id === pollId && !t.deletedUtc);
  const isMine = (t) => t.author && t.author.id === state.me.id;

  function presentPoll(t) {
    const poll = t.poll;
    if (!poll) return null;
    const visible = poll.resultsVisibility === 'Always' || poll.isClosed || isMine(t) || (poll.resultsVisibility === 'AfterVote' && poll.myVote.length > 0) || Boolean(t.viewer && t.viewer.canModerate);
    return {
      ...poll,
      resultsVisible: visible,
      options: poll.options.map((o) => ({ ...o, voteCount: visible ? o.voteCount : null, percent: visible ? (poll.totalVoters ? Math.round((o.voteCount / poll.totalVoters) * 1000) / 10 : 0) : null }))
    };
  }

  function presentThread(t) {
    const linked = t.kind === 'LinkedFeedPost' && t.source ? state.feedPosts.find((p) => p.id === t.source.id) : null;
    const out = { ...t, poll: presentPoll(t) };
    if (linked) Object.assign(out, { body: linked.body, mentions: linked.mentions || [], attachments: linked.attachments || [], commentCount: linked.commentCount || 0, reactionCounts: linked.reactionCounts || {}, reactionTotal: linked.reactionTotal || 0, myReactions: linked.myReactions || [] });
    delete out.deletedUtc;
    return out;
  }

  function summary(t) {
    const full = presentThread(t);
    const { body: _b, mentions: _m, linkPreview: _l, acceptedAnswer, attachments, ...rest } = full;
    return {
      ...rest,
      hasAcceptedAnswer: Boolean(acceptedAnswer),
      attachmentPreview: (attachments || []).slice(0, 4),
      attachmentCount: (attachments || []).length,
      poll: full.poll ? { id: full.poll.id, isClosed: full.poll.isClosed, closesUtc: full.poll.closesUtc, totalVoters: full.poll.totalVoters, hasVoted: full.poll.myVote.length > 0 } : null,
      feedback: full.feedback ? { status: full.feedback.status } : null
    };
  }

  function filterThreads(list, query, { forumId = null } = {}) {
    const f = query.filters || {};
    const types = asList(f.type);
    const statuses = asList(f.feedbackStatus);
    const forumIds = asList(f.forumIds);
    const term = String(query.search || '').trim();
    let out = list.filter((t) => !t.deletedUtc && (forumId ? t.forum.id === forumId : true) && (forumIds.length ? forumIds.includes(t.forum.id) : true));
    out = out.filter((t) => (f.state ? asList(f.state).includes(t.state) : t.state === 'Published' || isMine(t)));
    if (types.length) out = out.filter((t) => types.includes(t.type));
    if (statuses.length) out = out.filter((t) => t.feedback && statuses.includes(t.feedback.status));
    if (truthy(f.answered)) out = out.filter((t) => t.type === 'Question' && t.acceptedAnswer);
    if (f.answered === false || f.answered === 'false') out = out.filter((t) => t.type === 'Question' && !t.acceptedAnswer);
    if (truthy(f.unanswered)) out = out.filter((t) => t.type === 'Question' && !t.acceptedAnswer && !t.commentCount);
    if (truthy(f.followedOnly)) out = out.filter((t) => t.isFollowed);
    if (truthy(f.unreadOnly)) out = out.filter((t) => t.isUnread);
    if (truthy(f.hasMedia)) out = out.filter((t) => (t.attachments || []).length > 0);
    if (f.authorUserId) out = out.filter((t) => t.author && t.author.id === f.authorUserId);
    if (truthy(f.pinned)) out = out.filter((t) => t.isPinned);
    if (truthy(f.locked)) out = out.filter((t) => t.isLocked);
    if (truthy(f.promoted)) out = out.filter((t) => t.promotion);
    out = out.filter((t) => matchesMeta(t, query));
    if (term) {
      out = out.map((t) => {
        const s = snippet(t.title, term) ? { field: 'Title', commentId: null, ...snippet(t.title, term) } : snippet(t.body, term) ? { field: 'Body', commentId: null, ...snippet(t.body, term) } : null;
        let found = s;
        if (!found && f.includeComments !== 'false' && f.includeComments !== false) {
          const c = state.comments.find((x) => x.targetType === 'Thread' && x.targetId === t.id && snippet(x.body, term));
          if (c) found = { field: 'Comment', commentId: c.id, ...snippet(c.body, term) };
        }
        return found ? { ...t, searchSnippet: found } : null;
      }).filter(Boolean);
    }
    const ordering = query.ordering || (term ? 'Relevance' : 'Active');
    const by = {
      Active: (a, b) => String(b.lastActivityUtc).localeCompare(String(a.lastActivityUtc)) || b.id.localeCompare(a.id),
      Hot: (a, b) => (b.trendingScore || 0) - (a.trendingScore || 0) || String(b.lastActivityUtc).localeCompare(String(a.lastActivityUtc)),
      CreatedDescending: (a, b) => String(b.publishedUtc).localeCompare(String(a.publishedUtc)),
      CreatedAscending: (a, b) => String(a.publishedUtc).localeCompare(String(b.publishedUtc)),
      Top: (a, b) => (b.reactionTotal + 2 * b.commentCount) - (a.reactionTotal + 2 * a.commentCount),
      Relevance: () => 0
    };
    out.sort(by[ordering] || by.Active);
    return { list: out, ordering, term };
  }

  function threadPage(t) {
    const out = summary(t);
    if (t.searchSnippet) out.searchSnippet = t.searchSnippet;
    return out;
  }

  function convKey(type, id) {
    return type + ':' + id;
  }

  function commentsOf(type, id) {
    return state.comments.filter((c) => convKey(c.targetType, c.targetId) === convKey(type, id) && !c.deleted);
  }

  function reactionTarget(segment, id) {
    if (segment === 'threads') return thread(id);
    if (segment === 'feed') return state.feedPosts.find((p) => p.id === id);
    if (segment === 'comments') return state.comments.find((c) => c.id === id);
    return null;
  }

  function writable(t) {
    if (t.isLocked) return error(409, 'ThreadLocked', 'This thread is locked.');
    const f = t.forum ? forum(t.forum.id) : null;
    if (f && f.isArchived) return error(409, 'ForumArchived', 'This forum is archived.');
    return null;
  }

  function touch(t) {
    t.lastActivityUtc = now();
    t.updatedUtc = now();
  }

  function adminForum(f) {
    return { ...f, openReportCount: 0, moderatorCount: state.moderators.filter((m) => m.scope === 'AllForums' || m.forumId === f.id).length, pinnedCount: state.threads.filter((t) => t.forum.id === f.id && t.isPinned).length, threadsLast30d: f.threadCount };
  }

  function handleForums(method, parts, query, body) {
    if (parts.length === 2 && parts[1] === 'enumerate' && method === 'POST') {
      const f = body.filters || {};
      let list = state.forums.filter((x) => (truthy(f.includeArchived) ? true : !x.isArchived));
      if (f.section) list = list.filter((x) => x.section === f.section);
      if (truthy(f.followedOnly)) list = list.filter((x) => x.isFollowed);
      if (body.search) list = list.filter((x) => (x.name + ' ' + x.description).toLowerCase().includes(String(body.search).toLowerCase()));
      list = list.filter((x) => matchesMeta(x, body)).sort((a, b) => a.sortOrder - b.sortOrder);
      return ok(page(list, body));
    }
    const target = forum(decodeURIComponent(parts[1] || ''));
    if (!target) return error(404, 'NotFound', 'Not found.');
    if (parts.length === 2 && method === 'GET') {
      state.reads.push({ forumId: target.id });
      return ok(target);
    }
    if (parts[2] === 'follow') {
      target.isFollowed = method === 'PUT';
      return ok(target);
    }
    if (parts[2] === 'read' && method === 'POST') {
      target.unreadThreadCount = 0;
      for (const t of state.threads) if (t.forum.id === target.id) t.isUnread = false;
      return { status: 204, body: null };
    }
    if (parts[2] === 'threads' && parts[3] === 'enumerate') {
      const { list, ordering, term } = filterThreads(state.threads, body, { forumId: target.id });
      const promotePinned = !term && (ordering === 'Active' || ordering === 'Hot') && (body.filters || {}).includePinned !== 'false' && (body.filters || {}).includePinned !== false;
      const ordered = promotePinned ? list.filter((t) => t.isPinned).sort((a, b) => String(b.pinnedUtc).localeCompare(String(a.pinnedUtc))).concat(list.filter((t) => !t.isPinned)) : list;
      const result = page(ordered, body);
      result.objects = result.objects.map(threadPage);
      if (!body.continuationToken) target.unreadThreadCount = 0;
      return ok(result);
    }
    if (parts[2] === 'threads' && parts.length === 3 && method === 'POST') return createThread(target, body);
    return undefined;
  }

  function createThread(target, body) {
    if (target.isArchived) return error(409, 'ForumArchived', 'This forum is archived.');
    if (!target.allowedThreadTypes.includes(body.type)) return error(400, 'ThreadTypeNotAllowed', 'This forum does not allow ' + body.type + ' threads.');
    const title = String(body.title || '').trim();
    if (title.length < 3) return error(400, 'ValidationFailed', 'Title is too short.', { title: 'Title must be 3 to 140 characters.' });
    if (body.type === 'Poll' && (!body.poll || !Array.isArray(body.poll.options) || body.poll.options.length < 2)) return error(400, 'PollInvalid', 'A poll needs at least two options.', { 'poll.options': 'Add at least two options.' });
    if (body.type === 'Feedback' && !(body.mediaIds || []).some((id) => String(id).includes('vid'))) return error(400, 'FeedbackVideoRequired', 'Attach a video to request feedback.');
    const id = nextId('thr_');
    const t = makeThread(id, target, {
      type: body.type, title, body: body.body || '', excerpt: String(body.body || '').slice(0, 200), author: clone(state.me), labels: body.labels || [], tags: body.tags || {},
      attachments: (body.mediaIds || []).map((mid) => ({ id: mid, kind: String(mid).includes('vid') ? 'Video' : 'Image', status: 'Ready', altText: null, labels: [], tags: {} })),
      isFollowed: true, publishedUtc: now(), createdUtc: now(), updatedUtc: now(), lastActivityUtc: now(),
      viewer: { ...VIEWER_THREAD, canEdit: true, canDelete: true, canAcceptAnswer: body.type === 'Question', canClosePoll: body.type === 'Poll', canReport: false },
      poll: body.type === 'Poll' ? { ...makePoll(nextId('pol_'), id, body.poll.options.map((o) => o.label)), ...body.poll, options: undefined, myVote: [], totalVoters: 0, isClosed: false, closedUtc: null } : null,
      feedback: body.type === 'Feedback' ? { status: 'Open', focus: (body.feedback && body.feedback.focus) || '', reviewer: null, reviewCommentId: null, statusUtc: now() } : null
    });
    if (t.poll) t.poll.options = makePoll(t.poll.id, id, body.poll.options.map((o) => o.label)).options;
    state.threads.unshift(t);
    target.threadCount += 1;
    return ok(presentThread(t), 201);
  }

  function handleThreads(method, parts, query, body) {
    if (parts.length === 2 && parts[1] === 'enumerate' && method === 'POST') {
      const { list } = filterThreads(state.threads, body);
      const result = page(list, body);
      result.objects = result.objects.map(threadPage);
      return ok(result);
    }
    const t = thread(decodeURIComponent(parts[1] || ''));
    if (!t || (t.state !== 'Published' && !isMine(t) && !(t.viewer && t.viewer.canModerate))) return error(404, 'NotFound', 'Not found.');
    const action = parts[2];
    if (!action) {
      if (method === 'GET') {
        t.isUnread = false;
        return ok(presentThread(t));
      }
      if (method === 'PUT') {
        for (const key of ['title', 'body', 'labels', 'tags', 'audience', 'type']) if (body[key] !== undefined) t[key] = body[key];
        if (body.feedback && t.feedback) t.feedback.focus = body.feedback.focus;
        if (body.poll && t.poll) {
          // Section 6.4: every field before the first vote; only closesUtc after it.
          const voted = t.poll.totalVoters > 0 || Boolean(t.poll.firstVoteUtc);
          const others = Object.keys(body.poll).filter((k) => k !== 'closesUtc');
          if (voted && others.length) return error(409, 'PollOptionsLocked', 'After the first vote only the closing time can change.');
          if (body.poll.closesUtc !== undefined) t.poll.closesUtc = body.poll.closesUtc;
          if (!voted) {
            for (const key of ['multipleChoice', 'maxChoices', 'resultsVisibility', 'allowVoteChange', 'voterVisibility']) if (body.poll[key] !== undefined) t.poll[key] = body.poll[key];
            if (Array.isArray(body.poll.options)) {
              t.poll.options = body.poll.options.map((o, i) => ({ id: t.poll.id.replace(/^pol_/, 'pop_') + '_e' + (i + 1), label: o.label, position: i + 1, voteCount: 0, percent: null, labels: [], tags: {} }));
            }
          }
        }
        t.editedUtc = now();
        t.revisionCount += 1;
        return ok(presentThread(t));
      }
      if (method === 'DELETE') {
        t.deletedUtc = now();
        return { status: 204, body: null };
      }
    }
    const linked = t.kind === 'LinkedFeedPost';
    if (action === 'comments' || action === 'reactions') {
      if (linked) return error(409, 'UseCommentTarget', 'Use FeedPost ' + t.source.id + ' for this conversation.');
      return handleConversation(method, 'Thread', t.id, parts.slice(2), query, body, t);
    }
    if (action === 'revisions') return ok(page([], body));
    if (action === 'pin' || action === 'unpin') {
      if (action === 'pin' && state.threads.filter((x) => x.forum.id === t.forum.id && x.isPinned).length >= 5) return error(409, 'PinLimitReached', 'This forum already has the most pinned threads.');
      t.isPinned = action === 'pin';
      t.pinnedUtc = t.isPinned ? now() : null;
      return ok(presentThread(t));
    }
    if (action === 'lock' || action === 'unlock') {
      t.isLocked = action === 'lock';
      t.lockedUtc = t.isLocked ? now() : null;
      t.lockReason = t.isLocked ? body.reason || null : null;
      return ok(presentThread(t));
    }
    if (action === 'move') {
      const to = forum(body.forumId);
      if (!to) return error(404, 'NotFound', 'Not found.');
      if (to.isArchived) return error(409, 'ForumArchived', 'That forum is archived.');
      if (!to.allowedThreadTypes.includes(t.type)) return error(400, 'ThreadTypeNotAllowed', 'That forum does not allow this thread type.');
      t.forum = { id: to.id, slug: to.slug, name: to.name };
      t.isPinned = false;
      return ok(presentThread(t));
    }
    if (action === 'accept-answer') {
      if (method === 'DELETE') {
        t.acceptedAnswer = null;
        for (const c of state.comments) if (c.targetId === t.id) c.isAcceptedAnswer = false;
        return ok(presentThread(t));
      }
      const c = state.comments.find((x) => x.id === body.commentId && x.targetId === t.id && x.state === 'Visible');
      if (!c || t.type !== 'Question') return error(400, 'InvalidAcceptedAnswer', 'That comment cannot be accepted.');
      for (const x of state.comments) if (x.targetId === t.id) x.isAcceptedAnswer = x.id === c.id;
      t.acceptedAnswer = { ...clone(c), children: [], moreChildren: null };
      return ok(presentThread(t));
    }
    if (action === 'feedback') {
      if (!t.feedback) return error(400, 'ValidationFailed', 'Not a Feedback thread.');
      if (body.status === 'Reviewed') {
        const c = state.comments.find((x) => x.id === body.reviewCommentId && x.targetId === t.id);
        if (!c) return error(400, 'ValidationFailed', 'Choose a review comment.', { reviewCommentId: 'Choose a visible comment of this thread.' });
        for (const x of state.comments) if (x.targetId === t.id) x.isFeedbackReview = x.id === c.id;
        t.feedback.reviewCommentId = c.id;
      }
      t.feedback.status = body.status;
      t.feedback.reviewer = clone(state.me);
      t.feedback.statusUtc = now();
      return ok(presentThread(t));
    }
    if (action === 'promote') {
      if (t.promotion) return error(409, 'AlreadyPromoted', 'This thread is already in the feed.');
      const feedPostId = nextId('fpo_');
      t.promotion = { feedPostId, promotedBy: clone(state.me), promotedUtc: now() };
      const card = {
        id: feedPostId, kind: 'PromotedThread', author: t.author, title: body.title || t.title, body: t.body, excerpt: body.excerpt || t.excerpt, mentions: [], attachments: t.attachments.slice(0, 4),
        linkPreview: null, visibility: 'Members', state: 'Published', commentsEnabled: true, isPinned: Boolean(body.pin), publishedUtc: now(), createdUtc: now(), updatedUtc: now(), editedUtc: null,
        commentCount: t.commentCount, reactionCounts: t.reactionCounts, reactionTotal: t.reactionTotal, myReactions: t.myReactions, commentTarget: { type: 'Thread', id: t.id },
        source: { type: 'Thread', id: t.id, forumId: t.forum.id, forumName: t.forum.name, title: t.title }, labels: [], tags: {}, moderation: null,
        viewer: { canEdit: false, canDelete: false, canPin: false, canComment: true, canReact: true, canReport: true, canModerate: false, canAttachMedia: true, canAttachVideo: true }
      };
      state.feedPosts.unshift(card);
      return ok(card, 201);
    }
    if (action === 'unpromote') {
      if (t.promotion) state.feedPosts = state.feedPosts.filter((p) => p.id !== t.promotion.feedPostId);
      t.promotion = null;
      return ok(presentThread(t));
    }
    if (action === 'follow') {
      t.isFollowed = method === 'PUT';
      return ok({ isFollowed: t.isFollowed });
    }
    return undefined;
  }

  function handleConversation(method, type, id, rest, query, body, owner) {
    const [kind, sub, key] = rest;
    if (kind === 'reactions') {
      if (owner && owner.isLocked) return error(409, 'ThreadLocked', 'This thread is locked.');
      return reactions(method, owner, sub, key, body);
    }
    if (kind !== 'comments') return undefined;
    if ((sub === 'enumerate' && method === 'POST') || (!sub && method === 'GET')) {
      const q = method === 'GET' ? { ...query, maxResults: query.maxResults } : body;
      const all = commentsOf(type, id).filter((c) => !c.parentCommentId);
      const result = page(all, q);
      result.commentCount = commentsOf(type, id).length;
      if (owner && owner.type === 'Question') result.acceptedAnswerId = owner.acceptedAnswer ? owner.acceptedAnswer.id : null;
      return ok(result);
    }
    if (!sub && method === 'POST') {
      if (owner) {
        const blocked = writable(owner);
        if (blocked) return blocked;
      }
      if (!String(body.body || '').trim() && !(body.mediaIds || []).length) return error(400, 'ValidationFailed', 'Write something.', { body: 'Write something.' });
      const c = makeThreadComment(nextId('cmt_'), id, { targetType: type, body: body.body, author: clone(state.me), parentCommentId: body.parentCommentId || null, depth: body.parentCommentId ? 1 : 0, createdUtc: now(), viewer: { canEdit: true, canDelete: true, canReply: true, canReact: true, canReport: false, canModerate: false, canAttachMedia: true, canAttachVideo: true }, labels: body.labels || [], tags: body.tags || {} });
      state.comments.push(c);
      if (owner) {
        owner.commentCount += 1;
        touch(owner);
      }
      return ok(c, 201);
    }
    return undefined;
  }

  function reactions(method, owner, sub, key) {
    if (!owner) return error(404, 'NotFound', 'Not found.');
    if (sub === 'enumerate') return ok(page([], {}));
    if (!key && sub) {
      key = sub;
    }
    const mine = owner.myReactions || [];
    const counts = { ...(owner.reactionCounts || {}) };
    const on = method === 'PUT';
    if (on && !mine.includes(key)) {
      owner.myReactions = mine.concat(key);
      counts[key] = (counts[key] || 0) + 1;
    }
    if (!on && mine.includes(key)) {
      owner.myReactions = mine.filter((k) => k !== key);
      counts[key] = Math.max(0, (counts[key] || 0) - 1);
      if (!counts[key]) delete counts[key];
    }
    owner.reactionCounts = counts;
    owner.reactionTotal = Object.values(counts).reduce((a, b) => a + b, 0);
    return ok({ reactionCounts: owner.reactionCounts, reactionTotal: owner.reactionTotal, myReactions: owner.myReactions });
  }

  function handlePolls(method, parts, body) {
    const t = pollOwner(decodeURIComponent(parts[1] || ''));
    if (!t) return error(404, 'NotFound', 'Not found.');
    const poll = t.poll;
    if (parts[2] === 'close') {
      poll.isClosed = true;
      poll.closedUtc = now();
      return ok(presentPoll(t));
    }
    if (parts[2] === 'voters') return ok(page((state.pollVoters[poll.id] || []), body));
    if (parts[2] !== 'votes') return undefined;
    if (poll.isClosed) return error(409, 'PollClosed', 'This poll is closed.');
    if (t.isLocked) return error(409, 'ThreadLocked', 'This thread is locked.');
    const had = poll.myVote.length > 0;
    if (had && !poll.allowVoteChange) return error(409, 'PollVoteLocked', 'Votes cannot be changed in this poll.');
    const ids = method === 'DELETE' ? [] : body.optionIds || [];
    if (method === 'PUT') {
      if (!ids.length || ids.some((id) => !poll.options.find((o) => o.id === id))) return error(400, 'ValidationFailed', 'Choose an option.');
      if (!poll.multipleChoice && ids.length > 1) return error(400, 'ValidationFailed', 'Choose one option.');
      if (poll.multipleChoice && poll.maxChoices && ids.length > poll.maxChoices) return error(400, 'ValidationFailed', 'Too many options.');
    }
    for (const o of poll.options) if (poll.myVote.includes(o.id)) o.voteCount -= 1;
    for (const o of poll.options) if (ids.includes(o.id)) o.voteCount += 1;
    poll.totalVoters += (ids.length ? 1 : 0) - (had ? 1 : 0);
    poll.myVote = ids.slice();
    if (poll.voterVisibility === 'Visible') {
      const list = (state.pollVoters[poll.id] || []).filter((v) => v.user.id !== state.me.id);
      if (ids.length) list.unshift({ user: clone(state.me), optionIds: ids.slice(), createdUtc: now() });
      state.pollVoters[poll.id] = list;
    }
    touch(t);
    return ok(presentPoll(t));
  }

  function handleFeed(method, parts, query, body) {
    const id = decodeURIComponent(parts[1] || '');
    const post = state.feedPosts.find((p) => p.id === id);
    if (!post) return undefined;
    if (parts[2] === 'discuss' && method === 'POST') {
      if (post.discussion) return error(409, 'AlreadyPromoted', 'This post already has a discussion thread.');
      const to = forum(body.forumId);
      if (!to) return error(404, 'NotFound', 'Not found.');
      if (to.isArchived) return error(409, 'ForumArchived', 'That forum is archived.');
      if (!to.allowedThreadTypes.includes('Discussion')) return error(400, 'ThreadTypeNotAllowed', 'That forum does not allow discussions.');
      const tid = nextId('thr_');
      const t = makeThread(tid, to, { kind: 'LinkedFeedPost', author: post.author, title: body.title || post.title || String(post.excerpt || '').slice(0, 140), body: null, commentTarget: { type: 'FeedPost', id: post.id }, source: { type: 'FeedPost', id: post.id, title: post.title, author: post.author }, labels: body.labels || [], tags: body.tags || {} });
      state.threads.unshift(t);
      post.discussion = { threadId: tid, forumId: to.id, forumName: to.name };
      return ok(presentThread(t), 201);
    }
    if (parts[2] === 'undiscuss' && method === 'POST') {
      if (post.discussion) {
        const t = thread(post.discussion.threadId);
        if (t) t.deletedUtc = now();
      }
      post.discussion = null;
      return ok(post);
    }
    if (parts[2] === 'comments' || parts[2] === 'reactions') return handleConversation(method, 'FeedPost', post.id, parts.slice(2), query, body, post);
    if (parts.length === 2 && method === 'GET') return ok(post);
    return undefined;
  }

  function handleAdmin(method, parts, query, body) {
    const [, area, a2, a3] = parts;
    if (area === 'forums') {
      if (a2 === 'enumerate') {
        const f = body.filters || {};
        let list = state.forums.slice();
        if (f.isArchived !== undefined) list = list.filter((x) => String(x.isArchived) === String(f.isArchived));
        if (f.section) list = list.filter((x) => x.section === f.section);
        if (f.audience) list = list.filter((x) => x.audience === f.audience);
        if (body.search) list = list.filter((x) => x.name.toLowerCase().includes(String(body.search).toLowerCase()));
        list = list.filter((x) => matchesMeta(x, body)).sort((x, y) => x.sortOrder - y.sortOrder);
        const result = page(list, body, { max: 500 });
        result.objects = result.objects.map(adminForum);
        return ok(result);
      }
      if (a2 === 'order' && method === 'PUT') {
        const ids = body.forumIds || [];
        const active = state.forums.filter((x) => !x.isArchived).map((x) => x.id);
        if (ids.length !== active.length || new Set(ids).size !== ids.length || ids.some((x) => !active.includes(x))) return error(400, 'ValidationFailed', 'List every active forum once.');
        ids.forEach((id, i) => { forum(id).sortOrder = (i + 1) * 10; });
        return ok({ updated: ids.length });
      }
      if (!a2 && method === 'POST') {
        const slug = body.slug || String(body.name || '').toLowerCase().trim().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '');
        if (state.forums.some((x) => x.slug === slug)) return error(409, 'SlugInUse', 'That slug is in use.', { slug: 'Choose another slug.' });
        const f = makeForum(nextId('frm_'), { ...body, slug, sortOrder: (Math.max(0, ...state.forums.map((x) => x.sortOrder)) + 10), createdUtc: now(), updatedUtc: now() });
        f.viewer.allowedThreadTypes = f.allowedThreadTypes;
        state.forums.push(f);
        return ok(f, 201);
      }
      const f = forum(a2);
      if (!f) return error(404, 'NotFound', 'Not found.');
      if (!a3 && method === 'GET') return ok({ ...adminForum(f), moderators: { objects: state.moderators.filter((m) => m.scope === 'AllForums' || m.forumId === f.id).slice(0, 10), totalRecords: 0, enumerate: '/api/v1/admin/forums/' + f.id + '/moderators/enumerate' } });
      if (!a3 && method === 'PUT') {
        if (body.slug && state.forums.some((x) => x.slug === body.slug && x.id !== f.id)) return error(409, 'SlugInUse', 'That slug is in use.', { slug: 'Choose another slug.' });
        Object.assign(f, body, { updatedUtc: now() });
        if (body.allowedThreadTypes) f.viewer.allowedThreadTypes = body.allowedThreadTypes;
        return ok(f);
      }
      if (!a3 && method === 'DELETE') {
        if (state.threads.some((t) => t.forum.id === f.id)) return error(409, 'ForumNotEmpty', 'Archive a forum that has threads instead.');
        state.forums = state.forums.filter((x) => x.id !== f.id);
        return { status: 204, body: null };
      }
      if (a3 === 'archive' || a3 === 'unarchive') {
        f.isArchived = a3 === 'archive';
        return ok(f);
      }
      if (a3 === 'moderators') return ok(page(state.moderators.filter((m) => m.scope === 'AllForums' || m.forumId === f.id), body, { max: 500 }));
    }
    if (area === 'moderators' && method === 'POST' && body.resourceType === 'Forum') {
      const row = { user: fakeUser(body.userId, 'Member ' + body.userId), roles: [{ role: 'Moderator', resourceType: 'Forum', resourceId: body.resourceId }], scope: body.resourceId === '*' ? 'AllForums' : 'Forum', forumId: body.resourceId === '*' ? null : body.resourceId, grantedUtc: now(), labels: [], tags: {} };
      state.moderators.push(row);
      return ok(row);
    }
    if (area === 'moderators' && method === 'DELETE' && query.resourceType === 'Forum') {
      state.moderators = state.moderators.filter((m) => !(m.user.id === a2 && (m.forumId || '*') === query.resourceId));
      return { status: 204, body: null };
    }
    if (area === 'threads' && a2 === 'enumerate') {
      const { list } = filterThreads(state.threads.filter((t) => (body.filters && asList(body.filters.state).includes('Deleted')) ? true : !t.deletedUtc).map((t) => ({ ...t, state: t.deletedUtc ? 'Deleted' : t.state })), { ...body, filters: { state: ['Processing', 'Published', 'Hidden', 'Removed', 'Deleted'], ...(body.filters || {}) } });
      const result = page(list, body, { max: 500 });
      result.objects = result.objects.map((o) => ({ ...summary(o), state: o.state }));
      return ok(result);
    }
    if (area === 'trending' && a2 === 'enumerate') {
      const list = state.threads.filter((t) => !t.deletedUtc && t.state === 'Published').sort((x, y) => (y.trendingScore || 0) - (x.trendingScore || 0));
      const items = list.map((t, i) => ({ thread: summary(t), breakdown: { reactions: t.reactionTotal, comments: t.commentCount, uniqueCommenters: t.uniqueCommenterCount, ageHours: 5, numerator: t.reactionTotal + 2 * t.commentCount + 3 * t.uniqueCommenterCount, denominator: 18.52, score: (t.reactionTotal + 2 * t.commentCount + 3 * t.uniqueCommenterCount) / 18.52 }, rank: i + 1, computedUtc: now(), promotion: t.promotion ? { feedPostId: t.promotion.feedPostId, promotedUtc: t.promotion.promotedUtc } : null }));
      return ok(page(items, body, { max: 500 }));
    }
    if (area === 'metrics' && a2 === 'forums') {
      const days = [{ date: '2026-11-10', count: 2 }, { date: '2026-11-11', count: 3 }];
      return ok(dailyMetricPage({ threadsByDay: days, commentsByDay: days, votesByDay: days, activeThreads: state.threads.length, openReviewRequests: state.threads.filter((t) => t.feedback && t.feedback.status === 'Open').length, unansweredQuestions: state.threads.filter((t) => t.type === 'Question' && !t.acceptedAnswer).length, byForum: { objects: state.forums.slice(0, 20).map(adminForum), totalRecords: state.forums.length, enumerate: '/api/v1/admin/forums/enumerate' } }, body));
    }
    if (area === 'moderation' && a2 === 'forum-mutes') {
      if (a3 === 'enumerate') {
        const f = body.filters || {};
        let list = state.mutes.slice();
        if (f.forumId) list = list.filter((m) => m.forum.id === f.forumId);
        if (f.userId) list = list.filter((m) => m.user.id === f.userId);
        if (f.activeOnly !== 'false' && f.activeOnly !== false) list = list.filter((m) => !m.endedUtc);
        return ok(page(list, body, { max: 500 }));
      }
      if (!a3 && method === 'POST') {
        const f = forum(body.forumId);
        if (!f) return error(404, 'NotFound', 'Not found.');
        const mute = { id: nextId('fmu_'), user: fakeUser(body.userId, 'Member ' + body.userId), forum: { id: f.id, name: f.name }, untilUtc: body.untilUtc || null, reason: body.reason || '', mutedBy: clone(state.me), createdUtc: now(), labels: [], tags: {} };
        state.mutes = state.mutes.filter((m) => !(m.user.id === body.userId && m.forum.id === f.id)).concat(mute);
        return ok(mute, 201);
      }
      if (a3 && method === 'DELETE') {
        const mute = state.mutes.find((m) => m.id === a3);
        if (!mute) return error(404, 'NotFound', 'Not found.');
        mute.endedUtc = now();
        return { status: 204, body: null };
      }
    }
    if (area === 'moderation' && a2 === 'actions' && method === 'POST' && body.targetType === 'Thread') {
      const t = state.threads.find((x) => x.id === body.targetId);
      if (!t) return error(404, 'NotFound', 'Not found.');
      const next = { Hide: 'Hidden', Unhide: 'Published', Remove: 'Removed', Restore: 'Published' }[body.action];
      if (next) t.state = next;
      t.moderation = next === 'Published' ? null : { state: next, reason: body.reason, actedUtc: now() };
      const row = { id: nextId('mac_'), moderator: clone(state.me), targetType: 'Thread', targetId: t.id, action: body.action, reason: body.reason, note: body.note || null, notifiedAuthor: Boolean(body.notifyAuthor), createdUtc: now() };
      state.moderationActions.push(row);
      return ok(row);
    }
    return undefined;
  }

  function handleSearch(body) {
    const term = String(body.search || '').trim();
    if (!term) return error(400, 'ValidationFailed', 'Enter something to search for.', { search: 'Required.' });
    const types = asList((body.filters || {}).types);
    const hits = [];
    const want = (type) => !types.length || types.includes(type);
    if (want('Thread')) for (const t of state.threads) if (!t.deletedUtc && t.state === 'Published' && matchesMeta(t, body)) {
      const s = snippet(t.title, term) || snippet(t.body, term);
      if (s) hits.push({ type: 'Thread', id: t.id, title: t.title, snippet: s, path: '/threads/' + t.id, author: t.author, forum: t.forum, createdUtc: t.createdUtc, labels: t.labels, tags: t.tags });
    }
    if (want('FeedPost')) for (const p of state.feedPosts) if (matchesMeta(p, body)) {
      const s = snippet(p.title, term) || snippet(p.body, term);
      if (s) hits.push({ type: 'FeedPost', id: p.id, title: p.title, snippet: s, path: '/feed/' + p.id, author: p.author, createdUtc: p.createdUtc, labels: p.labels || [], tags: p.tags || {} });
    }
    if (want('Comment')) for (const c of state.comments) if (matchesMeta(c, body)) {
      const s = snippet(c.body, term);
      if (s) hits.push({ type: 'Comment', id: c.id, title: null, snippet: s, path: (c.targetType === 'Thread' ? '/threads/' : '/feed/') + c.targetId + '?comment=' + c.id, author: c.author, createdUtc: c.createdUtc, labels: c.labels || [], tags: c.tags || {} });
    }
    return ok(page(hits, body));
  }

  function handleMe(method, parts, body) {
    if (parts[1] === 'follows' && parts[2] === 'enumerate') {
      const type = (body.filters || {}).targetType;
      const rows = [];
      if (!type || type === 'Forum') for (const f of state.forums.filter((x) => x.isFollowed)) rows.push({ id: 'ffw_' + f.id, targetType: 'Forum', target: f, createdUtc: f.createdUtc, labels: [], tags: {} });
      if (!type || type === 'Thread') for (const t of state.threads.filter((x) => x.isFollowed && !x.deletedUtc)) rows.push({ id: 'tfw_' + t.id, targetType: 'Thread', target: summary(t), createdUtc: t.createdUtc, labels: [], tags: {} });
      return ok(page(rows, body));
    }
    return undefined;
  }

  function handlePublic(method, parts, body) {
    if (parts[1] !== 'threads') return undefined;
    const list = state.threads.filter((t) => t.audience === 'Public' && t.state === 'Published' && !t.deletedUtc);
    const strip = (t) => {
      const { viewer: _v, myReactions: _r, isFollowed: _f, isUnread: _u, ...rest } = summary(t);
      return { ...rest, body: t.body };
    };
    if (parts[2] === 'enumerate') {
      const result = page(list, { ...body, maxResults: Math.min(20, body.maxResults || 20) }, { max: 20 });
      result.objects = result.objects.map((o) => strip(list.find((t) => t.id === o.id)));
      return ok(result);
    }
    const t = list.find((x) => x.id === parts[2]);
    return t ? ok(strip(t)) : error(404, 'NotFound', 'Not found.');
  }

  /**
   * Answer one request.
   * @param {{ method: string, path: string, query?: object, body?: any }} call
   * @returns {{ status: number, body: any }|undefined}
   */
  function handle({ method, path, query = {}, body }) {
    const clean = String(path || '').split('?')[0].replace(/^\/api\/v1/, '');
    const parts = clean.split('/').filter(Boolean);
    const m = String(method || 'GET').toUpperCase();
    const b = body && typeof body === 'object' ? body : {};
    calls.push({ method: m, path: clean, query, body: b });
    switch (parts[0]) {
      case 'forums': return handleForums(m, parts, query, b);
      case 'threads': return handleThreads(m, parts, query, b);
      case 'polls': return handlePolls(m, parts, b);
      case 'feed': return handleFeed(m, parts, query, b);
      case 'comments': {
        const c = state.comments.find((x) => x.id === parts[1]);
        if (!c) return undefined;
        if (parts[2] === 'reactions') return reactions(m, c, parts[3], parts[4]);
        if (!parts[2] && m === 'DELETE') {
          c.state = 'Deleted';
          c.body = null;
          return { status: 204, body: null };
        }
        return undefined;
      }
      case 'admin': return handleAdmin(m, parts, query, b);
      case 'search': return parts[1] === 'enumerate' ? handleSearch(b) : undefined;
      case 'me': return handleMe(m, parts, b);
      case 'public': return handlePublic(m, parts, b);
      default: return undefined;
    }
  }

  /** A fetch implementation over `handle` (unknown routes answer 404 NotFound). */
  async function fetchImpl(url, init = {}) {
    const parsed = new URL(url, 'http://localhost');
    let body = init.body;
    if (typeof body === 'string') {
      try {
        body = JSON.parse(body);
      } catch {
        body = undefined;
      }
    }
    const result = handle({ method: init.method || 'GET', path: parsed.pathname, query: Object.fromEntries(parsed.searchParams.entries()), body }) || error(404, 'NotFound', 'No fake route for ' + parsed.pathname);
    if (result.status === 204) return new Response(null, { status: 204 });
    return new Response(JSON.stringify(result.body), { status: result.status, headers: { 'content-type': 'application/json' } });
  }

  return { state, calls, handle, fetchImpl, forum, thread };
}

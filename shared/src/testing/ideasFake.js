/**
 * An in-memory fake of the Phase 5 ideas board routes (docs/PHASE5_CONTRACT.md section 7): the
 * board with orderings and filters, votes (idempotent, `409 IdeaMerged`, closed statuses),
 * follows, comments and reactions, the public board, and the staff routes (create, update,
 * status, merge, unmerge, delete, status CRUD and order). `addIdea(seed)` adds an idea directly.
 */

import { asList, clone, createFakeServer, error, matchesMeta, noContent, ok, page, summaryUser, truthy } from './fakeKit.js';

const DEFAULT_STATUSES = [
  { key: 'UnderReview', label: 'Under review', color: '#607D8B', isClosed: false, isInitial: true },
  { key: 'Planned', label: 'Planned', color: '#2E7D32', isClosed: false, isInitial: false },
  { key: 'InProgress', label: 'In progress', color: '#1565C0', isClosed: false, isInitial: false },
  { key: 'Shipped', label: 'Shipped', color: '#6A1B9A', isClosed: true, isInitial: false },
  { key: 'Declined', label: 'Declined', color: '#9E9E9E', isClosed: true, isInitial: false }
];

/**
 * Create a fake.
 * @param {{ me?: object, staff?: boolean, memberSubmissions?: 'Direct'|'StaffOnly', voteOnClosed?: boolean, statuses?: object[], now?: () => string }} [seed]
 */
export function createIdeasFake(seed = {}) {
  let n = 0;
  const nextId = (prefix) => prefix + 'fake' + (++n);
  const now = seed.now || (() => '2026-11-12T12:00:00Z');
  const state = {
    me: seed.me || summaryUser('usr_1', 'Sam Rivera'),
    staff: seed.staff !== false,
    memberSubmissions: seed.memberSubmissions || 'Direct',
    voteOnClosed: seed.voteOnClosed === true,
    statuses: (seed.statuses || DEFAULT_STATUSES).map((s, i) => ({ id: 'ids_' + s.key.toLowerCase(), sortOrder: (i + 1) * 10, isRetired: false, labels: [], tags: {}, ...clone(s) })),
    ideas: [],
    votes: [],
    follows: [],
    comments: [],
    reactions: []
  };

  const statusOf = (key) => state.statuses.find((s) => s.key === key) || null;
  const initialStatus = () => (state.statuses.find((s) => s.isInitial) || state.statuses[0]).key;
  const idea = (id) => state.ideas.find((i) => i.id === id && !i.deletedUtc);

  function present(i) {
    const s = statusOf(i.statusKey) || { key: i.statusKey, label: i.statusKey, color: null, isClosed: false };
    const merged = i.mergedIntoId ? idea(i.mergedIntoId) : null;
    const mine = i.author && i.author.id === state.me.id;
    const reactions = state.reactions.filter((r) => r.ideaId === i.id);
    const counts = {};
    for (const r of reactions) counts[r.key] = (counts[r.key] || 0) + 1;
    return {
      id: i.id, title: i.title, body: i.body, author: i.showAuthor ? clone(i.author) : null,
      status: { key: s.key, label: s.label, color: s.color, isClosed: s.isClosed }, statusNote: i.statusNote, statusUtc: i.statusUtc,
      voteCount: state.votes.filter((v) => v.ideaId === i.id).length, hasVoted: state.votes.some((v) => v.ideaId === i.id && v.userId === state.me.id),
      isFollowed: state.follows.some((f) => f.ideaId === i.id && f.userId === state.me.id),
      mergedInto: merged ? { id: merged.id, title: merged.title } : null, commentTarget: { type: 'Idea', id: i.id },
      commentCount: state.comments.filter((c) => c.targetId === i.id).length, reactionCounts: counts, reactionTotal: reactions.length,
      myReactions: reactions.filter((r) => r.userId === state.me.id).map((r) => r.key), audience: i.audience, locked: false,
      labels: clone(i.labels), tags: clone(i.tags),
      viewer: { canVote: !i.mergedIntoId && (!s.isClosed || state.voteOnClosed), canComment: !i.mergedIntoId, canReact: true, canReport: !mine, canEdit: state.staff || (mine && i.statusKey === initialStatus()), canSetStatus: state.staff, canMerge: state.staff, canOpen: true, requires: null },
      createdUtc: i.createdUtc, updatedUtc: i.updatedUtc
    };
  }

  /** Add an idea; `voters` are user ids that voted. Returns the presented Idea. */
  function addIdea({ title = 'Idea', body = 'Body.', author = state.me, status, audience = 'Members', labels = [], tags = {}, voters = [], createdUtc } = {}) {
    const i = {
      id: nextId('ida_'), title, body, author: author ? clone(author) : null, showAuthor: Boolean(author), statusKey: status || initialStatus(), statusNote: '', statusUtc: now(),
      mergedIntoId: null, audience, labels: clone(labels), tags: clone(tags), deletedUtc: null, createdUtc: createdUtc || now(), updatedUtc: now()
    };
    state.ideas.push(i);
    for (const userId of voters) state.votes.push({ ideaId: i.id, userId, createdUtc: now() });
    return present(i);
  }

  function filterIdeas(body, { admin = false, publicOnly = false } = {}) {
    const f = body.filters || {};
    const statuses = asList(f.status);
    const term = String(body.search || '').trim().toLowerCase();
    let list = state.ideas.filter((i) => !i.deletedUtc)
      .filter((i) => (admin ? (!truthy(f.merged) || i.mergedIntoId) : (truthy(f.includeMerged) || !i.mergedIntoId)))
      .filter((i) => !publicOnly || i.audience === 'Public')
      .filter((i) => !statuses.length || statuses.includes(i.statusKey))
      .filter((i) => !truthy(f.open) || !(statusOf(i.statusKey) || {}).isClosed)
      .filter((i) => !truthy(f.votedByMe) || state.votes.some((v) => v.ideaId === i.id && v.userId === state.me.id))
      .filter((i) => !f.authorUserId || (i.author && i.author.id === f.authorUserId))
      .filter((i) => !f.audience || i.audience === f.audience)
      .filter((i) => !term || i.title.toLowerCase().includes(term) || i.body.toLowerCase().includes(term))
      .filter((i) => matchesMeta(i, body))
      .map(present);
    if (body.ordering === 'CreatedDescending') list = list.sort((a, b) => b.createdUtc.localeCompare(a.createdUtc) || (b.id < a.id ? -1 : 1));
    else if (body.ordering === 'UpdatedDescending') list = list.sort((a, b) => b.updatedUtc.localeCompare(a.updatedUtc));
    else list = list.sort((a, b) => b.voteCount - a.voteCount || (b.id < a.id ? -1 : 1));
    return list;
  }

  function stripPublic(i) {
    const { hasVoted: _h, myReactions: _r, viewer: _v, isFollowed: _f, ...rest } = i;
    return rest;
  }

  function memberRoute(method, parts, body, query = {}) {
    if (parts[1] === 'enumerate') return ok(page(filterIdeas(body), body));
    if (parts[1] === 'statuses' && parts[2] === 'enumerate') return ok(page(state.statuses.filter((s) => !s.isRetired).sort((a, b) => a.sortOrder - b.sortOrder), body));
    if (!parts[1] && method === 'POST') {
      if (state.memberSubmissions !== 'Direct' && !state.staff) return error(403, 'Forbidden', 'Ideas are published by staff.');
      if (String(body.title || '').trim().length < 3) return error(400, 'ValidationFailed', 'Add a title.', { title: 'Too short.' });
      const created = addIdea({ title: body.title, body: body.body || '', labels: body.labels || [], tags: body.tags || {}, voters: [state.me.id] });
      state.follows.push({ ideaId: created.id, userId: state.me.id });
      return ok(present(idea(created.id)), 201);
    }
    const i = idea(parts[1]);
    if (!i) return error(404, 'NotFound', 'Not found.');
    const action = parts[2];
    if (!action && method === 'GET') return ok(present(i));
    if (!action && method === 'PUT') {
      if (!present(i).viewer.canEdit) return error(403, 'Forbidden', 'You cannot edit this idea.');
      for (const key of ['title', 'body', 'labels', 'tags']) if (body[key] !== undefined) i[key] = clone(body[key]);
      i.updatedUtc = now();
      return ok(present(i));
    }
    if (action === 'vote') {
      if (i.mergedIntoId) return error(409, 'IdeaMerged', 'This idea was merged.', undefined);
      if (method === 'PUT') {
        if ((statusOf(i.statusKey) || {}).isClosed && !state.voteOnClosed) return error(409, 'Conflict', 'Voting is closed.');
        if (!state.votes.some((v) => v.ideaId === i.id && v.userId === state.me.id)) state.votes.push({ ideaId: i.id, userId: state.me.id, createdUtc: now() });
        if (!state.follows.some((f) => f.ideaId === i.id && f.userId === state.me.id)) state.follows.push({ ideaId: i.id, userId: state.me.id });
      } else {
        state.votes = state.votes.filter((v) => !(v.ideaId === i.id && v.userId === state.me.id));
      }
      return ok(present(i));
    }
    if (action === 'follow') {
      state.follows = state.follows.filter((f) => !(f.ideaId === i.id && f.userId === state.me.id));
      if (method === 'PUT') state.follows.push({ ideaId: i.id, userId: state.me.id });
      return ok({ isFollowed: method === 'PUT' });
    }
    if (action === 'comments') {
      if (parts[3] === 'enumerate') return ok(page(state.comments.filter((c) => c.targetId === i.id), body));
      // GET is the Phase 2 comment tree page (top-level comments with the idea's comment count).
      if (method === 'GET' && !parts[3]) {
        const all = state.comments.filter((c) => c.targetId === i.id);
        const roots = all.filter((c) => !c.parentCommentId);
        const ordered = query.ordering === 'CreatedDescending' ? roots.slice().reverse() : roots;
        return ok({ ...page(ordered, query), commentCount: all.length });
      }
      if (method === 'POST') {
        if (i.mergedIntoId) return error(409, 'IdeaMerged', 'This idea was merged.');
        const parent = body.parentCommentId ? state.comments.find((x) => x.id === body.parentCommentId) : null;
        const c = { id: nextId('cmt_'), targetType: 'Idea', targetId: i.id, parentCommentId: body.parentCommentId || null, depth: parent ? (parent.depth || 0) + 1 : 0, state: 'Visible', author: clone(state.me), body: body.body || '', mentions: [], attachments: [], reactionCounts: {}, reactionTotal: 0, myReactions: [], replyCount: 0, children: [], moreChildren: null, viewer: { canEdit: true, canDelete: true, canReply: true, canReact: true, canReport: false }, labels: [], tags: {}, createdUtc: now() };
        state.comments.push(c);
        return ok(c, 201);
      }
    }
    if (action === 'reactions') {
      if (parts[3] === 'enumerate') return ok(page(state.reactions.filter((r) => r.ideaId === i.id).map((r) => ({ user: summaryUser(r.userId, 'Member ' + r.userId), key: r.key })), body));
      const key = parts[3];
      state.reactions = state.reactions.filter((r) => !(r.ideaId === i.id && r.userId === state.me.id && r.key === key));
      if (method === 'PUT') state.reactions.push({ ideaId: i.id, userId: state.me.id, key });
      const p = present(i);
      return ok({ reactionCounts: p.reactionCounts, reactionTotal: p.reactionTotal, myReactions: p.myReactions });
    }
    return undefined;
  }

  function adminRoute(method, parts, body) {
    if (!state.staff) return error(403, 'Forbidden', 'Staff only.');
    if (parts[2] === 'statuses') {
      if (parts[3] === 'enumerate') return ok(page(state.statuses.slice().sort((a, b) => a.sortOrder - b.sortOrder), body, { max: 500 }));
      if (parts[3] === 'order' && method === 'PUT') {
        const ids = body.ideaStatusIds || [];
        const ordered = [...ids.map((id) => state.statuses.find((x) => x.id === id)).filter(Boolean), ...state.statuses.filter((x) => !ids.includes(x.id)).sort((a, b) => a.sortOrder - b.sortOrder)];
        ordered.forEach((s, i) => { s.sortOrder = (i + 1) * 10; });
        return ok({ objects: state.statuses.slice().sort((a, b) => a.sortOrder - b.sortOrder) });
      }
      if (!parts[3] && method === 'POST') {
        if (!body.key || statusOf(body.key)) return error(400, 'ValidationFailed', 'Key is required and unique.', { key: 'Invalid.' });
        const s = { id: nextId('ids_'), key: body.key, label: body.label || body.key, color: body.color || null, isClosed: Boolean(body.isClosed), isInitial: Boolean(body.isInitial), sortOrder: (state.statuses.length + 1) * 10, isRetired: false, labels: [], tags: {} };
        state.statuses.push(s);
        return ok(s, 201);
      }
      const s = state.statuses.find((x) => x.id === parts[3]);
      if (!s) return error(404, 'NotFound', 'Not found.');
      if (method === 'PUT') { Object.assign(s, body, { id: s.id }); return ok(s); }
      if (method === 'DELETE') { s.isRetired = true; return noContent(); }
      return undefined;
    }
    if (parts[2] === 'enumerate') return ok(page(filterIdeas(body, { admin: true }), body, { max: 500 }));
    if (!parts[2] && method === 'POST') return ok(addIdea({ title: body.title, body: body.body || '', author: null, status: body.status, audience: body.audience || 'Members', labels: body.labels || [], tags: body.tags || {} }), 201);
    const i = idea(parts[2]);
    if (!i) return error(404, 'NotFound', 'Not found.');
    if (!parts[3] && method === 'PUT') {
      for (const key of ['title', 'body', 'labels', 'tags', 'audience']) if (body[key] !== undefined) i[key] = clone(body[key]);
      i.updatedUtc = now();
      return ok(present(i));
    }
    if (!parts[3] && method === 'DELETE') { i.deletedUtc = now(); return noContent(); }
    if (parts[3] === 'status') {
      if (!statusOf(body.status)) return error(400, 'ValidationFailed', 'Unknown status.', { status: 'Unknown.' });
      i.statusKey = body.status;
      i.statusNote = body.statusNote || '';
      i.statusUtc = now();
      i.notifiedVoters = body.notifyVoters !== false;
      return ok(present(i));
    }
    if (parts[3] === 'merge') {
      const target = idea(body.intoIdeaId);
      if (!target || target.id === i.id) return error(400, 'ValidationFailed', 'Choose another idea.', { intoIdeaId: 'Invalid.' });
      for (const v of state.votes.filter((x) => x.ideaId === i.id)) {
        if (!state.votes.some((x) => x.ideaId === target.id && x.userId === v.userId)) state.votes.push({ ...v, ideaId: target.id, movedFrom: i.id });
      }
      i.mergedIntoId = target.id;
      return ok(present(i));
    }
    if (parts[3] === 'unmerge') {
      state.votes = state.votes.filter((v) => v.movedFrom !== i.id);
      i.mergedIntoId = null;
      return ok(present(i));
    }
    return undefined;
  }

  function route({ method, parts, body, query }) {
    if (parts[0] === 'ideas') return memberRoute(method, parts, body, query || {});
    if (parts[0] === 'admin' && parts[1] === 'ideas') return adminRoute(method, parts, body);
    if (parts[0] === 'public' && parts[1] === 'ideas') {
      if (parts[2] === 'enumerate') return ok(page(filterIdeas(body, { publicOnly: true }).map(stripPublic), body));
      const i = idea(parts[2]);
      return i && i.audience === 'Public' ? ok(stripPublic(present(i))) : error(404, 'NotFound', 'Not found.');
    }
    if (parts[0] === 'reports' && method === 'POST' && body.targetType === 'Idea') return ok({ id: nextId('rpt_'), ...clone(body), createdUtc: now() }, 201);
    return undefined;
  }

  const server = createFakeServer(route);
  return { state, calls: server.calls, handle: server.handle, fetchImpl: server.fetchImpl, fail: server.fail, addIdea, idea: (id) => (idea(id) ? present(idea(id)) : null) };
}

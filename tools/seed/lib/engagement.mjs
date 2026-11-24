// Comments, reactions, and reports on any comment target (feed posts, threads, events,
// lessons, ideas). Comments are matched by their seed tag `<objectKey>/<commentKey>`.

import { createHash } from 'node:crypto';
import { expandText } from './util.mjs';

function flatten(nodes, out = []) {
  for (const n of nodes ?? []) {
    out.push(n);
    flatten(n.children, out);
  }
  return out;
}

export function eligibleMembers(ctx) {
  return [...ctx.members.values()].filter((m) => m.session && m.membershipLabel && !m.membershipLabel.startsWith('None'));
}

// Deterministic pick of `count` members (excluding `exclude`) for a stable key.
export function pickMembers(ctx, seedKey, count, exclude = []) {
  const pool = eligibleMembers(ctx).filter((m) => !exclude.includes(m.key));
  const scored = pool.map((m) => ({ m, s: createHash('sha1').update(`${seedKey}:${m.key}`).digest('hex') }));
  scored.sort((a, b) => a.s.localeCompare(b.s));
  return scored.slice(0, Math.min(count, scored.length)).map((x) => x.m);
}

function reactionKeys(ctx, wanted) {
  const allowed = new Set((ctx.property.reactions ?? []).map((r) => r.key));
  const keys = (wanted ?? ctx.seed.defaults?.reactionKeys ?? [...allowed]).filter((k) => allowed.has(k));
  return keys.length ? keys : [...allowed].slice(0, 1);
}

// spec.autoReactions = { count, keys } or spec.reactions = { key: [memberKeys] }
export async function react(ctx, { path, objectKey, spec, authorKey, currentTotal = 0 }) {
  const plan = [];
  if (spec.reactions) {
    for (const [key, who] of Object.entries(spec.reactions)) for (const k of who) plan.push({ member: ctx.member(k), key });
  }
  if (spec.autoReactions?.count) {
    const keys = reactionKeys(ctx, spec.autoReactions.keys);
    const people = pickMembers(ctx, objectKey, spec.autoReactions.count, [authorKey]);
    people.forEach((m, i) => plan.push({ member: m, key: keys[i % keys.length] }));
  }
  if (!plan.length || currentTotal >= plan.length) return;
  for (const p of plan) {
    const res = await ctx.api.request('PUT', `${path}/reactions/${p.key}`, { auth: p.member.session, ok: [402, 403, 404, 'UnknownReaction'] });
    if (res.error) ctx.log.debug(`reaction ${p.key} by ${p.member.key} on ${objectKey}: ${res.error}`);
    else ctx.count('reactions');
  }
}

// Creates a comment tree. `base` is the comment collection path (for example /api/v1/feed/{id}).
export async function commentTree(ctx, { base, objectKey, comments, readAs, commentPath = (id) => `${base}/comments` }) {
  if (!comments?.length) return new Map();
  const existing = new Map();
  const data = await ctx.api.request('POST', `${base}/comments/enumerate`, {
    auth: readAs ?? ctx.adminSession,
    body: { maxResults: 50, ordering: 'CreatedAscending', filters: { depth: 5, childLimit: 20 } },
    ok: [404, 402],
  });
  if (!data.error) {
    for (const c of flatten(data.data.objects)) {
      const key = c.tags?.[ctx.namespace];
      if (key) existing.set(key, c.id);
    }
  }
  const ids = new Map();
  async function create(list, parentId) {
    for (const c of list) {
      const tagKey = `${objectKey}/${c.key}`;
      let id = existing.get(tagKey);
      if (!id) {
        const body = { body: expandText(c.body, ctx), labels: c.labels ?? [], tags: ctx.tags(tagKey, c.tags) };
        if (parentId) body.parentCommentId = parentId;
        const author = ctx.member(c.author);
        const res = await ctx.api.request('POST', commentPath(parentId), { auth: author.session, body, ok: [402, 403, 409] });
        if (res.error) {
          ctx.warn(`comment ${tagKey} by ${c.author}: ${res.error}`);
          continue;
        }
        id = res.data.id;
        ctx.count('comments');
        if (c.autoReactions || c.reactions) await react(ctx, { path: `/api/v1/comments/${id}`, objectKey: tagKey, spec: c, authorKey: c.author });
        if (c.report) await report(ctx, { targetType: 'Comment', targetId: id, spec: c.report, key: tagKey });
      } else ctx.count('comments', 'existing');
      ids.set(c.key, id);
      if (c.replies?.length) await create(c.replies, id);
    }
  }
  await create(comments, null);
  return ids;
}

export async function report(ctx, { targetType, targetId, spec, key }) {
  const by = ctx.member(spec.by);
  const res = await ctx.api.request('POST', '/api/v1/reports', {
    auth: by.session,
    body: { targetType, targetId, reason: spec.reason ?? 'Other', details: spec.details, labels: [], tags: ctx.tags(`${key}/report`) },
    ok: [400, 404],
  });
  if (res.error) ctx.warn(`report on ${key}: ${res.error}`);
  else ctx.count('reports', res.status === 201 ? 'created' : 'existing');
}

// --reset-content: removes every object that carries the seed tag. Accounts, memberships,
// and staff roles stay. Objects the API cannot delete are canceled, closed, deactivated, or
// withdrawn instead, and canceled events are re-tagged so a later seed run creates them again.

import { ApiError } from './client.mjs';
import { readCredentials } from './credentials.mjs';
import { Session } from './client.mjs';
import path from 'node:path';

const TICKET_STATUSES = ['Open', 'InProgress', 'WaitingOnMember', 'Resolved', 'Closed'];

async function seeded(ctx, enumeratePath, { auth = 'admin', filters, extra } = {}) {
  return ctx.api.enumerateAll(enumeratePath, { maxResults: 100, tags: { [ctx.namespace]: '*' }, ...(filters ? { filters } : {}), ...(extra ?? {}) }, { auth });
}

async function attempt(ctx, label, fn) {
  try {
    await fn();
    return true;
  } catch (err) {
    ctx.warn(`${label}: ${err instanceof ApiError ? `${err.status} ${err.code ?? ''} ${err.body?.message ?? ''}` : err.message}`);
    return false;
  }
}

export async function resetContent(ctx, { only, skip }) {
  const want = (name) => (!only || only.has(name)) && !skip.has(name);
  ctx.log.info(`Resetting seeded content (tag "${ctx.namespace}")`);

  // Member sessions for routes that act as the owner (withdrawals, conversation deletes).
  const credentials = await readCredentials(path.resolve(ctx.seedDir, ctx.seed.accounts?.credentialsFile ?? 'ACCOUNTS.local.md'));
  const sessions = new Map();
  for (const spec of ctx.seed.members ?? []) {
    const c = credentials.get(spec.key);
    if (c) sessions.set(spec.key, new Session({ key: spec.key, email: c.email, password: c.password }));
  }
  const adminKey = ctx.seed.accounts?.adminMember;
  const adminSession = sessions.get(adminKey);
  const staffAuth = adminSession ?? 'admin';

  if (want('ai')) {
    const state = await ctx.api.get('/api/v1/admin/property/ai');
    if (state.deploymentEnabled) {
      if (!state.enabled) await ctx.api.put('/api/v1/admin/property/ai', { enabled: true });
      try {
        for (const a of await seeded(ctx, '/api/v1/admin/ai/assistants/enumerate')) {
          if (await attempt(ctx, `delete assistant ${a.name}`, () => ctx.api.del(`/api/v1/admin/ai/assistants/${a.id}`))) ctx.count('AI assistants', 'deleted');
        }
        for (const e of await seeded(ctx, '/api/v1/admin/ai/endpoints/enumerate')) {
          if (await attempt(ctx, `delete endpoint ${e.name}`, () => ctx.api.del(`/api/v1/admin/ai/endpoints/${e.id}`))) ctx.count('AI endpoints', 'deleted');
        }
      } finally {
        await ctx.api.put('/api/v1/admin/property/ai', { enabled: state.enabled });
      }
    }
  }

  if (want('broadcasts')) {
    for (const b of await seeded(ctx, '/api/v1/admin/broadcasts/enumerate', { auth: staffAuth })) {
      if (b.status === 'Draft') {
        if (await attempt(ctx, `delete broadcast ${b.title}`, () => ctx.api.del(`/api/v1/admin/broadcasts/${b.id}`, { auth: staffAuth }))) ctx.count('broadcasts', 'deleted');
      } else if (b.status === 'Scheduled' || b.status === 'Sending') {
        if (await attempt(ctx, `cancel broadcast ${b.title}`, () => ctx.api.post(`/api/v1/admin/broadcasts/${b.id}/cancel`, {}, { auth: staffAuth }))) ctx.count('broadcasts', 'canceled');
      } else if (b.status === 'Sent') {
        ctx.count('broadcasts', 'kept (sent; no delete route)');
      }
    }
  }

  if (want('ideas')) {
    for (const i of await seeded(ctx, '/api/v1/admin/ideas/enumerate', { auth: staffAuth, filters: { includeMerged: true } })) {
      if (await attempt(ctx, `delete idea ${i.title}`, () => ctx.api.del(`/api/v1/admin/ideas/${i.id}`, { auth: staffAuth }))) ctx.count('ideas', 'deleted');
    }
  }

  if (want('support')) {
    for (const t of await seeded(ctx, '/api/v1/admin/tickets/enumerate', { auth: staffAuth, filters: { status: TICKET_STATUSES } })) {
      await attempt(ctx, `delete ticket ${t.number}`, async () => {
        if (t.status !== 'Closed') await ctx.api.put(`/api/v1/admin/tickets/${t.id}`, { status: 'Closed' }, { auth: staffAuth });
        await ctx.api.del(`/api/v1/admin/tickets/${t.id}`, { auth: staffAuth });
        ctx.count('tickets', 'deleted');
      });
    }
  }

  if (want('messages')) {
    for (const spec of ctx.seed.conversations ?? []) {
      for (const who of [spec.from, spec.to]) {
        const s = sessions.get(who);
        const other = (ctx.seed.members ?? []).find((m) => m.key === (who === spec.from ? spec.to : spec.from));
        if (!s || !other) continue;
        await attempt(ctx, `delete conversation ${spec.key} for ${who}`, async () => {
          const me = await ctx.api.get('/api/v1/auth/me', { auth: s });
          const others = await ctx.api.post('/api/v1/admin/members/enumerate', { maxResults: 5, search: other.handle });
          const otherId = others.objects.find((o) => o.handle === other.handle)?.id;
          if (!otherId || !me) return;
          const list = await ctx.api.post('/api/v1/conversations/enumerate', { maxResults: 5, filters: { folder: 'All', withUserId: otherId } }, { auth: s });
          for (const c of list.objects ?? []) {
            await ctx.api.del(`/api/v1/conversations/${c.id}`, { auth: s });
            ctx.count('conversations', `deleted for ${who === spec.from ? 'sender' : 'recipient'}`);
          }
        });
      }
    }
  }

  if (want('library')) {
    for (const l of await seeded(ctx, '/api/v1/admin/lessons/enumerate', { auth: staffAuth })) {
      if (await attempt(ctx, `delete lesson ${l.title}`, () => ctx.api.del(`/api/v1/admin/lessons/${l.id}`, { auth: staffAuth }))) ctx.count('lessons', 'deleted');
    }
    for (const s of await seeded(ctx, '/api/v1/admin/series/enumerate', { auth: staffAuth })) {
      const ok = await attempt(ctx, `delete series ${s.title}`, () => ctx.api.del(`/api/v1/admin/series/${s.id}`, { auth: staffAuth }));
      if (ok) ctx.count('series', 'deleted');
      else if (await attempt(ctx, `archive series ${s.title}`, () => ctx.api.post(`/api/v1/admin/series/${s.id}/archive`, {}, { auth: staffAuth }))) ctx.count('series', 'archived');
    }
  }

  if (want('events')) {
    for (const e of await seeded(ctx, '/api/v1/admin/events/enumerate', { auth: staffAuth, filters: { when: 'All' } })) {
      const key = e.tags?.[ctx.namespace];
      if (e.status === 'Draft') {
        if (await attempt(ctx, `delete event ${e.title}`, () => ctx.api.del(`/api/v1/admin/events/${e.id}?scope=${e.recurrence ? 'All' : 'One'}`, { auth: staffAuth }))) ctx.count('events', 'deleted');
        continue;
      }
      await attempt(ctx, `cancel event ${e.title}`, async () => {
        if (e.status !== 'Canceled') {
          await ctx.api.request('POST', `/api/v1/admin/events/${e.id}/cancel`, { auth: staffAuth, body: { reason: 'Demo content reset', notifyAttendees: false, scope: e.recurrence ? 'All' : 'One' }, ok: ['EventCanceled'] });
        }
        const tags = { ...(e.tags ?? {}) };
        delete tags[ctx.namespace];
        tags[`${ctx.namespace}-reset`] = key;
        await ctx.api.put(`/api/v1/admin/events/${e.id}`, { tags }, { auth: staffAuth });
        ctx.count('events', 'canceled');
      });
    }
    for (const spec of ctx.seed.eventProposals ?? []) {
      const s = sessions.get(spec.proposer);
      if (!s) continue;
      for (const p of await ctx.findByTag('/api/v1/me/events/proposals/enumerate', spec.key, { auth: s })) {
        if (p.status !== 'Proposed') continue;
        if (await attempt(ctx, `withdraw proposal ${p.title}`, () => ctx.api.post(`/api/v1/events/proposals/${p.id}/withdraw`, {}, { auth: s }))) ctx.count('event proposals', 'withdrawn');
      }
    }
  }

  if (want('directory')) {
    for (const spec of ctx.seed.directorySubmissions ?? []) {
      const s = sessions.get(spec.submitter);
      if (!s) continue;
      for (const sub of await ctx.findByTag('/api/v1/me/directory/submissions/enumerate', spec.key, { auth: s })) {
        if (sub.status !== 'Pending') continue;
        if (await attempt(ctx, `withdraw submission ${spec.key}`, () => ctx.api.post(`/api/v1/directory/submissions/${sub.id}/withdraw`, {}, { auth: s }))) ctx.count('directory submissions', 'withdrawn');
      }
    }
    // Seeded reviews and member media are deleted by their authors so the next run can write them again.
    const venueIds = new Map((await seeded(ctx, '/api/v1/admin/venues/enumerate', { auth: staffAuth, filters: { includeClosed: true } })).map((v) => [v.tags?.[ctx.namespace], v.id]));
    for (const spec of ctx.seed.venueReviews ?? []) {
      const s = sessions.get(spec.author);
      const venueId = venueIds.get(spec.venue);
      if (!s || !venueId) continue;
      for (const r of await ctx.findByTag(`/api/v1/venues/${venueId}/reviews/enumerate`, spec.key, { auth: s })) {
        if (await attempt(ctx, `delete venue review ${spec.key}`, () => ctx.api.del(`/api/v1/venues/${venueId}/reviews/${r.id}`, { auth: s }))) ctx.count('venue reviews', 'deleted');
      }
    }
    for (const spec of ctx.seed.venueMedia ?? []) {
      const s = sessions.get(spec.member);
      const venueId = venueIds.get(spec.venue);
      if (!s || !venueId) continue;
      for (const m of await ctx.findByTag(`/api/v1/venues/${venueId}/media/enumerate`, spec.key, { auth: s })) {
        if (await attempt(ctx, `delete venue media ${spec.key}`, () => ctx.api.del(`/api/v1/venues/${venueId}/media/${m.id}`, { auth: s }))) ctx.count('venue media', 'deleted');
      }
    }
    for (const i of await seeded(ctx, '/api/v1/admin/instructors/enumerate', { auth: staffAuth })) {
      if (await attempt(ctx, `delete instructor ${i.displayName}`, () => ctx.api.del(`/api/v1/admin/instructors/${i.id}`, { auth: staffAuth }))) ctx.count('instructors', 'deleted');
    }
    for (const v of await seeded(ctx, '/api/v1/admin/venues/enumerate', { auth: staffAuth, filters: { includeClosed: true } })) {
      if (await attempt(ctx, `delete venue ${v.name}`, () => ctx.api.del(`/api/v1/admin/venues/${v.id}`, { auth: staffAuth }))) ctx.count('venues', 'deleted');
    }
  }

  if (want('threads') || want('forums')) {
    for (const t of await seeded(ctx, '/api/v1/admin/threads/enumerate', { auth: staffAuth })) {
      if (await attempt(ctx, `delete thread ${t.title}`, () => ctx.api.del(`/api/v1/threads/${t.id}`, { auth: staffAuth }))) ctx.count('threads', 'deleted');
    }
  }
  if (want('forums')) {
    for (const f of await seeded(ctx, '/api/v1/admin/forums/enumerate', { extra: {} })) {
      if (await attempt(ctx, `delete forum ${f.name}`, () => ctx.api.del(`/api/v1/admin/forums/${f.id}`))) ctx.count('forums', 'deleted');
    }
  }

  if (want('feed')) {
    for (const p of await seeded(ctx, '/api/v1/admin/feed/enumerate')) {
      if (await attempt(ctx, `delete post ${p.title}`, () => ctx.api.del(`/api/v1/admin/feed/${p.id}`))) ctx.count('feed posts', 'deleted');
    }
  }

  if (want('codes')) {
    for (const c of await seeded(ctx, '/api/v1/admin/signup-codes/enumerate')) {
      if (!c.isActive) continue;
      if (await attempt(ctx, `deactivate code ${c.code}`, () => ctx.api.post(`/api/v1/admin/signup-codes/${c.id}/deactivate`, {}))) ctx.count('sign-up codes', 'deactivated');
    }
  }
  ctx.log.info('Accounts, memberships, and staff roles were kept. Run the seeder again to recreate the content.');
}

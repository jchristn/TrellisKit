/**
 * An in-memory fake of the Phase 5 broadcast routes (docs/PHASE5_CONTRACT.md section 8): drafts,
 * edits (`409 BroadcastNotEditable` after sending), test sends, scheduling, sending with the
 * `confirmCount` check (within 5 percent of the preview count), cancel, deliveries, segments with
 * preview, and the member announcements. `audienceCount` (seed, default 10) is the preview count
 * of every segment; sending delivers to the caller too, so announcements appear.
 */

import { asList, clone, createFakeServer, error, matchesMeta, noContent, ok, page, summaryUser } from './fakeKit.js';

const KNOWN_FIELDS = ['membership', 'membershipSource', 'store', 'labels', 'tags', 'signedUpAfterUtc', 'signedUpBeforeUtc', 'activeWithinDays', 'inactiveForDays', 'hasPushToken', 'hasVerifiedEmail', 'role', 'eventRsvp', 'seriesFollowers'];

/**
 * Create a fake.
 * @param {{ me?: object, staff?: boolean, audienceCount?: number, maxPerDay?: number, now?: () => string }} [seed]
 */
export function createBroadcastsFake(seed = {}) {
  let n = 0;
  const nextId = (prefix) => prefix + 'fake' + (++n);
  const now = seed.now || (() => '2026-11-12T12:00:00Z');
  const state = {
    me: seed.me || summaryUser('usr_1', 'Sam Rivera', { isStaff: true }),
    staff: seed.staff !== false,
    audienceCount: Number.isFinite(seed.audienceCount) ? seed.audienceCount : 10,
    maxPerDay: Number.isFinite(seed.maxPerDay) ? seed.maxPerDay : 3,
    sentToday: 0,
    broadcasts: [],
    segments: [],
    deliveries: [],
    tests: []
  };

  function segmentProblems(segment) {
    for (const f of (segment && segment.filters) || []) {
      const field = String(f.field || '');
      if (!KNOWN_FIELDS.includes(field) && !field.startsWith('profile.')) return error(400, 'SegmentInvalid', 'Unknown segment field ' + field + '.');
    }
    return null;
  }

  function preview() {
    const count = state.audienceCount;
    return { count, byChannel: { inApp: count, push: Math.floor(count / 2), email: count }, sample: { objects: [clone(state.me)].slice(0, Math.min(10, count)), totalRecords: count } };
  }

  function present(b) {
    return clone(b);
  }

  function editable(b) {
    return b.status === 'Draft' || b.status === 'Scheduled';
  }

  function adminRoute(method, parts, body) {
    if (!state.staff) return error(403, 'Forbidden', 'Staff only.');
    if (parts[2] === 'segments') {
      if (parts[3] === 'preview') {
        const segment = body.segment || (body.segmentId && state.segments.find((s) => s.id === body.segmentId));
        if (body.segmentId && !segment) return error(404, 'NotFound', 'Not found.');
        return segmentProblems(segment) || ok(preview());
      }
      if (parts[3] === 'enumerate') return ok(page(state.segments.filter((s) => matchesMeta(s, body)), body, { max: 500 }));
      if (!parts[3] && method === 'POST') {
        const bad = segmentProblems(body);
        if (bad) return bad;
        const s = { id: nextId('bsg_'), name: body.name || 'Segment', description: body.description || '', filters: clone(body.filters || []), createdBy: clone(state.me), labels: clone(body.labels || []), tags: clone(body.tags || {}), createdUtc: now(), updatedUtc: now() };
        state.segments.push(s);
        return ok(s, 201);
      }
      const s = state.segments.find((x) => x.id === parts[3]);
      if (!s) return error(404, 'NotFound', 'Not found.');
      if (method === 'GET') return ok(s);
      if (method === 'PUT') {
        const bad = segmentProblems(body);
        if (bad) return bad;
        Object.assign(s, clone(body), { id: s.id, updatedUtc: now() });
        return ok(s);
      }
      if (method === 'DELETE') { state.segments = state.segments.filter((x) => x !== s); return noContent(); }
      return undefined;
    }
    if (parts[2] === 'enumerate') {
      const f = body.filters || {};
      const statuses = asList(f.status);
      const list = state.broadcasts.filter((b) => (!statuses.length || statuses.includes(b.status)) && (!f.channel || (b.channels && b.channels[f.channel])) && matchesMeta(b, body));
      return ok(page(list.slice().reverse().map(present), body, { max: 500 }));
    }
    if (!parts[2] && method === 'POST') {
      const bad = segmentProblems(body.segment);
      if (bad) return bad;
      if (body.category === 'Essential' && !body.essentialReason) return error(400, 'ValidationFailed', 'An essential broadcast needs a reason.', { essentialReason: 'Required.' });
      const b = {
        id: nextId('bct_'), title: body.title || '', channels: { inApp: true, push: false, email: false, ...(body.channels || {}) }, inApp: clone(body.inApp || null), push: clone(body.push || null), email: clone(body.email || null),
        segment: body.segment ? clone(body.segment) : (body.segmentId ? { id: body.segmentId } : null), category: body.category || 'Optional', essentialReason: body.essentialReason || null,
        status: 'Draft', sendAtUtc: null, stats: { targeted: 0, inAppCreated: 0, pushSent: 0, pushFailed: 0, emailQueued: 0, emailSent: 0, emailDelivered: 0, emailFailed: 0, emailSuppressed: 0, skippedPreferences: 0 },
        createdBy: clone(state.me), labels: clone(body.labels || []), tags: clone(body.tags || {}), createdUtc: now(), updatedUtc: now(), sentUtc: null
      };
      state.broadcasts.push(b);
      return ok(present(b), 201);
    }
    const b = state.broadcasts.find((x) => x.id === parts[2]);
    if (!b) return error(404, 'NotFound', 'Not found.');
    const action = parts[3];
    if (!action && method === 'GET') return ok(present(b));
    if (!action && method === 'PUT') {
      if (!editable(b)) return error(409, 'BroadcastNotEditable', 'This broadcast can no longer be edited.');
      for (const key of ['title', 'channels', 'inApp', 'push', 'email', 'segment', 'category', 'essentialReason', 'labels', 'tags']) if (body[key] !== undefined) b[key] = clone(body[key]);
      b.updatedUtc = now();
      return ok(present(b));
    }
    if (!action && method === 'DELETE') {
      if (b.status !== 'Draft') return error(409, 'BroadcastNotEditable', 'Only drafts can be deleted.');
      state.broadcasts = state.broadcasts.filter((x) => x !== b);
      return noContent();
    }
    if (action === 'test') {
      state.tests.push({ broadcastId: b.id, channels: clone(body.channels || []) });
      return ok({ accepted: true, channels: body.channels || [] }, 202);
    }
    if (action === 'schedule') {
      if (!editable(b)) return error(409, 'BroadcastNotEditable', 'This broadcast can no longer be edited.');
      if (!body.sendAtUtc) return error(400, 'ValidationFailed', 'Choose a time.', { sendAtUtc: 'Required.' });
      if (state.sentToday >= state.maxPerDay) return error(409, 'BroadcastLimitReached', 'The daily broadcast limit is reached.');
      b.status = 'Scheduled';
      b.sendAtUtc = body.sendAtUtc;
      return ok(present(b));
    }
    if (action === 'send') {
      if (!editable(b)) return error(409, 'BroadcastNotEditable', 'This broadcast can no longer be sent.');
      if (state.sentToday >= state.maxPerDay) return error(409, 'BroadcastLimitReached', 'The daily broadcast limit is reached.');
      const count = preview().count;
      const confirm = Number(body.confirmCount);
      if (!Number.isFinite(confirm) || Math.abs(confirm - count) > Math.max(0, count * 0.05)) return error(400, 'ValidationFailed', 'The audience changed; preview again.', { confirmCount: 'Mismatch.' });
      state.sentToday += 1;
      b.status = 'Sent';
      b.sentUtc = now();
      b.stats.targeted = count;
      b.stats.inAppCreated = b.channels.inApp ? count : 0;
      state.deliveries.push({ id: nextId('bdl_'), broadcastId: b.id, user: clone(state.me), inAppStatus: b.channels.inApp ? 'Created' : 'Skipped', pushStatus: b.channels.push ? 'Sent' : 'Skipped', emailStatus: b.channels.email ? 'Queued' : 'Skipped', labels: [], tags: {}, createdUtc: now(), updatedUtc: now() });
      return ok({ ...present(b), status: 'Sending' });
    }
    if (action === 'cancel') {
      if (b.status === 'Sent' || b.status === 'Canceled') return error(409, 'BroadcastNotEditable', 'Nothing to cancel.');
      b.status = 'Canceled';
      return ok(present(b));
    }
    if (action === 'deliveries' && parts[4] === 'enumerate') {
      const f = body.filters || {};
      const list = state.deliveries.filter((d) => d.broadcastId === b.id && (!f.status || [d.inAppStatus, d.pushStatus, d.emailStatus].includes(f.status)));
      return ok(page(list, body, { max: 500 }));
    }
    return undefined;
  }

  function announcement(b) {
    return { id: b.id, title: (b.inApp && b.inApp.title) || b.title, body: (b.inApp && b.inApp.body) || '', sentUtc: b.sentUtc, labels: clone(b.labels), tags: clone(b.tags) };
  }

  function route({ method, parts, body }) {
    if (parts[0] === 'admin' && parts[1] === 'broadcasts') return adminRoute(method, parts, body);
    if (parts[0] === 'announcements') {
      const received = state.broadcasts.filter((b) => b.status === 'Sent' && state.deliveries.some((d) => d.broadcastId === b.id && d.user.id === state.me.id));
      if (parts[1] === 'enumerate') return ok(page(received.slice().reverse().map(announcement).filter((a) => matchesMeta(a, body)), body));
      const b = received.find((x) => x.id === parts[1]);
      return b ? ok(announcement(b)) : error(404, 'NotFound', 'Not found.');
    }
    return undefined;
  }

  const server = createFakeServer(route);
  return { state, calls: server.calls, handle: server.handle, fetchImpl: server.fetchImpl, fail: server.fail };
}

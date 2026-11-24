import { dailyMetricPage } from './metricFake.js';

/**
 * An in-memory fake of the Phase 5 support routes (docs/PHASE5_CONTRACT.md section 6, as-built
 * note C): member tickets with the device block from the diagnostics headers, replies, close and
 * reopen, ticket types, and the staff inbox (notes hidden from members, updates, delete of Closed
 * tickets, publish and link ideas, export, metrics, ticket type CRUD and order).
 *
 * `me` is the requester; `staff` (default true) lets the admin routes answer. `ideasFake`, when
 * given, receives ideas published from tickets.
 */

import { asList, clone, createFakeServer, error, matchesMeta, noContent, ok, page, summaryUser, truthy } from './fakeKit.js';

const DEFAULT_TYPES = ['Support', 'Bug', 'Enhancement', 'Billing'];

/**
 * Create a fake.
 * @param {{ me?: object, staff?: boolean, ticketTypes?: object[], now?: () => string, ideasFake?: object }} [seed]
 */
export function createSupportFake(seed = {}) {
  let n = 0;
  const nextId = (prefix) => prefix + 'fake' + (++n);
  const now = seed.now || (() => '2026-11-12T12:00:00Z');
  const state = {
    me: seed.me || summaryUser('usr_1', 'Sam Rivera'),
    staff: seed.staff !== false,
    agent: summaryUser('usr_9', 'Support Agent', { isStaff: true }),
    ticketTypes: (seed.ticketTypes || DEFAULT_TYPES.map((key, i) => ({ id: 'tty_' + key.toLowerCase(), key, label: key, description: '', helpText: '', staffRecipients: [], sortOrder: (i + 1) * 10, isRetired: false, labels: [], tags: {} }))).map(clone),
    tickets: [],
    messages: [],
    nextNumber: 1001
  };

  const ticket = (id) => state.tickets.find((t) => t.id === id);
  const typeOf = (key) => state.ticketTypes.find((t) => t.key === key && !t.isRetired);

  function presentMessage(m) {
    return { id: m.id, ticketId: m.ticketId, kind: m.kind, author: clone(m.author), authorRole: m.authorRole, body: m.body, attachments: clone(m.attachments), labels: [], tags: {}, createdUtc: m.createdUtc };
  }

  function present(t, { staff = false } = {}) {
    const msgs = state.messages.filter((m) => m.ticketId === t.id && (staff || m.kind === 'Reply'));
    const out = {
      ...clone(t),
      type: { key: t.type, label: (typeOf(t.type) || { label: t.type }).label },
      messages: { objects: msgs.slice(-20).map(presentMessage), totalRecords: msgs.length, enumerate: '/api/v1/tickets/' + t.id + '/messages/enumerate' },
      viewer: { canReply: t.status !== 'Closed', canClose: t.status !== 'Closed', canReopen: t.status === 'Closed' || t.status === 'Resolved', canAttach: true, ...(staff ? { canNote: true, canAssign: true, canSetStatus: true } : {}) }
    };
    if (!staff) delete out.membership;
    return out;
  }

  function summary(t, staff) {
    const { messages: _m, ...rest } = present(t, { staff });
    return rest;
  }

  function filterTickets(list, body, staff) {
    const f = body.filters || {};
    const statuses = asList(f.status);
    const types = asList(f.type);
    const term = String(body.search || '').trim().toLowerCase();
    let out = list.filter((t) => (statuses.length ? statuses.includes(t.status) : (staff ? t.status !== 'Closed' : true)))
      .filter((t) => !types.length || types.includes(t.type))
      .filter((t) => !f.priority || asList(f.priority).includes(t.priority))
      .filter((t) => !f.assigneeUserId || (t.assignee && t.assignee.id === f.assigneeUserId))
      .filter((t) => !truthy(f.unassigned) || !t.assignee)
      .filter((t) => !f.requesterUserId || (t.requester && t.requester.id === f.requesterUserId))
      .filter((t) => !truthy(f.hasIdea) || t.idea)
      .filter((t) => !term || String(t.number).includes(term) || t.title.toLowerCase().includes(term) || t.body.toLowerCase().includes(term))
      .filter((t) => matchesMeta(t, body));
    if (body.ordering === 'CreatedAscending') out = out.sort((a, b) => a.createdUtc.localeCompare(b.createdUtc) || a.number - b.number);
    else out = out.sort((a, b) => b.lastActivityUtc.localeCompare(a.lastActivityUtc) || b.number - a.number);
    return out;
  }

  function addMessage(t, kind, author, authorRole, body) {
    const m = { id: nextId('tkm_'), ticketId: t.id, kind, author: clone(author), authorRole, body: String(body.body || ''), attachments: (body.mediaIds || []).map((id) => ({ id, kind: 'Image' })), createdUtc: now() };
    state.messages.push(m);
    t.lastActivityUtc = now();
    t.updatedUtc = now();
    return m;
  }

  function memberRoute(method, parts, body, headers) {
    if (!parts[1] && method === 'POST') {
      const problems = {};
      if (!typeOf(body.type)) problems.type = 'Unknown type.';
      if (String(body.title || '').trim().length < 3) problems.title = 'Too short.';
      if (String(body.body || '').trim().length < 10) problems.body = 'Too short.';
      if (Object.keys(problems).length) return error(400, 'ValidationFailed', 'Check the form.', problems);
      const device = body.includeDiagnostics ? {
        client: headers['x-client'] || null, clientVersion: headers['x-client-version'] || null, platform: headers['x-client-platform'] || null,
        osVersion: headers['x-client-os-version'] || null, deviceModel: headers['x-client-device-model'] || null, updateId: headers['x-client-update-id'] || null, locale: null, timeZone: null
      } : null;
      const t = {
        id: nextId('tkt_'), number: state.nextNumber++, type: body.type, title: String(body.title).trim(), body: String(body.body), status: 'Open', priority: 'Normal',
        requester: clone(state.me), assignee: null, device, membership: { isActive: true, source: 'Subscription', store: null, expiresUtc: null }, idea: null,
        firstResponseDueUtc: now(), firstResponseUtc: null, isOverdue: false, lastActivityUtc: now(), resolvedUtc: null, closedUtc: null,
        labels: clone(body.labels || []), tags: clone(body.tags || {}), createdUtc: now(), updatedUtc: now()
      };
      state.tickets.push(t);
      // The server stores the opening text and attachments as the first Reply (as REST_API.md "Support tickets").
      addMessage(t, 'Reply', state.me, 'Member', body);
      return ok(present(t), 201);
    }
    if (parts[1] === 'enumerate') return ok(page(filterTickets(state.tickets.filter((t) => t.requester && t.requester.id === state.me.id), body, false).map((t) => summary(t, false)), body));
    const t = ticket(parts[1]);
    if (!t || !t.requester || t.requester.id !== state.me.id) return error(404, 'NotFound', 'Not found.');
    if (!parts[2] && method === 'GET') return ok(present(t));
    if (parts[2] === 'messages' && parts[3] === 'enumerate') return ok(page(state.messages.filter((m) => m.ticketId === t.id && m.kind === 'Reply').map(presentMessage), body));
    if (parts[2] === 'messages' && method === 'POST') {
      if (t.status === 'Closed') return error(409, 'TicketClosed', 'This ticket is closed.');
      const m = addMessage(t, 'Reply', state.me, 'Member', body);
      if (t.status === 'WaitingOnMember' || t.status === 'Resolved') t.status = 'Open';
      return ok(presentMessage(m), 201);
    }
    if (parts[2] === 'close') {
      t.status = 'Closed';
      t.closedUtc = now();
      return ok(present(t));
    }
    if (parts[2] === 'reopen') {
      t.status = 'Open';
      t.closedUtc = null;
      return ok(present(t));
    }
    return undefined;
  }

  function adminRoute(method, parts, query, body) {
    if (!state.staff) return error(403, 'Forbidden', 'Staff only.');
    if (parts[1] === 'support' && parts[2] === 'ticket-types') {
      if (parts[3] === 'enumerate') return ok(page(state.ticketTypes.slice().sort((a, b) => a.sortOrder - b.sortOrder), body, { max: 500 }));
      if (parts[3] === 'order' && method === 'PUT') {
        const ids = body.ticketTypeIds || [];
        const ordered = [...ids.map((id) => state.ticketTypes.find((x) => x.id === id)).filter(Boolean), ...state.ticketTypes.filter((x) => !ids.includes(x.id)).sort((a, b) => a.sortOrder - b.sortOrder)];
        ordered.forEach((tt, i) => { tt.sortOrder = (i + 1) * 10; });
        return ok({ objects: state.ticketTypes.slice().sort((a, b) => a.sortOrder - b.sortOrder) });
      }
      if (!parts[3] && method === 'POST') {
        if (!body.key || state.ticketTypes.some((x) => x.key === body.key)) return error(400, 'ValidationFailed', 'Key is required and unique.', { key: 'Invalid.' });
        const tt = { id: nextId('tty_'), key: body.key, label: body.label || body.key, description: body.description || '', helpText: body.helpText || '', staffRecipients: body.staffRecipients || [], sortOrder: (state.ticketTypes.length + 1) * 10, isRetired: false, labels: body.labels || [], tags: body.tags || {} };
        state.ticketTypes.push(tt);
        return ok(tt, 201);
      }
      const tt = state.ticketTypes.find((x) => x.id === parts[3]);
      if (!tt) return error(404, 'NotFound', 'Not found.');
      if (method === 'GET') return ok(tt);
      if (method === 'PUT') { Object.assign(tt, body, { id: tt.id }); return ok(tt); }
      if (method === 'DELETE') { tt.isRetired = true; return noContent(); }
      return undefined;
    }
    if (parts[1] === 'metrics' && parts[2] === 'support') {
      return ok(dailyMetricPage({ openedByDay: [{ date: '2026-11-12', count: state.tickets.length }], resolvedByDay: [], firstResponseMedianHours: null, overdueNow: 0, byType: { objects: state.ticketTypes.slice(0, 20).map((tt) => ({ key: tt.key, label: tt.label, count: state.tickets.filter((t) => t.type === tt.key).length })), totalRecords: state.ticketTypes.length, enumerate: '/api/v1/admin/support/ticket-types/enumerate' }, days: Number((body.filters || query).days) || 30 }, body));
    }
    if (parts[1] !== 'tickets') return undefined;
    if (parts[2] === 'enumerate') return ok(page(filterTickets(state.tickets, body, true).map((t) => summary(t, true)), body, { max: 500 }));
    if (parts[2] === 'export.csv') {
      const rows = filterTickets(state.tickets, { filters: { ...query } }, true).map((t) => [t.number, t.type, t.status, t.priority, JSON.stringify(t.title)].join(','));
      return { status: 200, body: ['number,type,status,priority,title', ...rows].join('\n') + '\n', contentType: 'text/csv' };
    }
    const t = ticket(parts[2]);
    if (!t) return error(404, 'NotFound', 'Not found.');
    if (!parts[3] && method === 'GET') return ok(present(t, { staff: true }));
    if (!parts[3] && method === 'PUT') {
      for (const key of ['status', 'priority', 'type', 'labels', 'tags']) if (body[key] !== undefined) t[key] = clone(body[key]);
      if (body.assigneeUserId !== undefined) t.assignee = body.assigneeUserId ? summaryUser(body.assigneeUserId, 'Agent ' + body.assigneeUserId, { isStaff: true }) : null;
      t.updatedUtc = now();
      return ok(present(t, { staff: true }));
    }
    if (!parts[3] && method === 'DELETE') {
      if (t.status !== 'Closed') return error(409, 'TicketNotClosed', 'Close the ticket first.');
      state.tickets = state.tickets.filter((x) => x !== t);
      state.messages = state.messages.filter((m) => m.ticketId !== t.id);
      return noContent();
    }
    if (parts[3] === 'messages' && parts[4] === 'enumerate') {
      const kinds = Array.isArray(body.filters && body.filters.kind) ? body.filters.kind : (body.filters && body.filters.kind ? [body.filters.kind] : []);
      let list = state.messages.filter((m) => m.ticketId === t.id && (!kinds.length || kinds.includes(m.kind)));
      if (body.ordering === 'CreatedDescending') list = list.slice().reverse();
      return ok(page(list.map(presentMessage), body, { max: 100 }));
    }
    if (parts[3] === 'messages' && method === 'POST') {
      const kind = body.kind === 'Note' ? 'Note' : 'Reply';
      const m = addMessage(t, kind, state.agent, 'Staff', body);
      if (kind === 'Reply') {
        t.status = body.setStatus || 'WaitingOnMember';
        if (!t.firstResponseUtc) t.firstResponseUtc = now();
      }
      return ok(presentMessage(m), 201);
    }
    if (parts[3] === 'publish-idea') {
      const idea = seed.ideasFake && typeof seed.ideasFake.addIdea === 'function'
        ? seed.ideasFake.addIdea({ title: body.title || t.title, body: body.body || t.body, author: body.showRequester ? t.requester : null, audience: body.audience, voters: t.requester ? [t.requester.id] : [] })
        : { id: nextId('ida_'), title: body.title || t.title, body: body.body || t.body, author: body.showRequester ? clone(t.requester) : null, voteCount: 1, labels: [], tags: {} };
      t.idea = { id: idea.id, title: idea.title };
      return ok(idea, 201);
    }
    if (parts[3] === 'link-idea') {
      if (!body.ideaId) return error(400, 'ValidationFailed', 'ideaId is required.', { ideaId: 'Required.' });
      t.idea = { id: body.ideaId, title: null };
      return ok(present(t, { staff: true }));
    }
    return undefined;
  }

  function route({ method, parts, query, body, headers }) {
    if (parts[0] === 'tickets') return memberRoute(method, parts, body, headers);
    if (parts[0] === 'support' && parts[1] === 'ticket-types' && parts[2] === 'enumerate') {
      return ok(page(state.ticketTypes.filter((t) => !t.isRetired).sort((a, b) => a.sortOrder - b.sortOrder), body));
    }
    if (parts[0] === 'admin' && ((parts[1] === 'support' && parts[2] === 'ticket-types') || parts[1] === 'tickets' || (parts[1] === 'metrics' && parts[2] === 'support'))) return adminRoute(method, parts, query, body);
    return undefined;
  }

  const server = createFakeServer(route);
  return { state, calls: server.calls, handle: server.handle, fetchImpl: server.fetchImpl, fail: server.fail, ticket };
}

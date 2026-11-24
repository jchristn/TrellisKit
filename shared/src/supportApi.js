/**
 * Phase 5 support ticket API methods (docs/PHASE5_CONTRACT.md section 6, as-built note C), mixed
 * into `ApiClient.prototype`. Support is an account feature: every signed-in account reaches it
 * in every access model, so a lapsed member can always ask for billing help.
 *
 * Every list is the canonical `POST {collection}/enumerate` with an EnumerationQuery; admin
 * collections allow 500 results per page.
 */

import { ENUMERATION_MAX_RESULTS_ADMIN, toEnumerationBody, toEnumerationParams } from './enumeration.js';

const enc = encodeURIComponent;
const ADMIN = { maxLimit: ENUMERATION_MAX_RESULTS_ADMIN };

/** Ticket statuses (contract section 2.3). */
export const TicketStatus = Object.freeze({ Open: 'Open', InProgress: 'InProgress', WaitingOnMember: 'WaitingOnMember', Resolved: 'Resolved', Closed: 'Closed' });

/** Ticket priorities (contract section 2.3). */
export const TicketPriority = Object.freeze({ Low: 'Low', Normal: 'Normal', High: 'High', Urgent: 'Urgent' });

/** Ticket message kinds (contract section 2.3). */
export const TicketMessageKind = Object.freeze({ Reply: 'Reply', Note: 'Note', System: 'System' });

/** Ticket error codes (contract section 0.4, as-built note C). */
export const TicketError = Object.freeze({ TicketClosed: 'TicketClosed', TicketNotClosed: 'TicketNotClosed' });

/** Headers the mobile app sends on ticket creation only (contract section 6.1). */
export const DIAGNOSTICS_HEADERS = Object.freeze({ platform: 'X-Client-Platform', osVersion: 'X-Client-Os-Version', deviceModel: 'X-Client-Device-Model' });

function enumBody(query, limits) {
  return toEnumerationBody(query || {}, limits);
}

function idOf(value) {
  return typeof value === 'string' ? value : value && value.id;
}

/**
 * The diagnostics headers for `POST /tickets`: present only when `includeDiagnostics` is true and
 * a value is known. Empty otherwise.
 * @param {{ platform?: string, osVersion?: string, deviceModel?: string }|null|undefined} diagnostics
 * @param {boolean} includeDiagnostics
 * @returns {Record<string,string>}
 */
export function diagnosticsHeaders(diagnostics, includeDiagnostics) {
  const out = {};
  if (!includeDiagnostics || !diagnostics || typeof diagnostics !== 'object') return out;
  for (const [key, header] of Object.entries(DIAGNOSTICS_HEADERS)) {
    const value = diagnostics[key];
    if (value !== undefined && value !== null && String(value).trim() !== '') out[header] = String(value).trim().slice(0, 200);
  }
  return out;
}

/** Query-string form of the admin ticket filters (paging keys dropped) for the CSV export. */
function exportParams(query) {
  const params = toEnumerationParams(query || {}, ADMIN);
  delete params.maxResults;
  delete params.continuationToken;
  delete params.skip;
  return params;
}

/** @this {import('./ApiClient.js').ApiClient} */
export const supportMethods = {
  // ---- Member tickets (section 6.1) ----

  /**
   * POST /tickets. `diagnostics` (`{ platform, osVersion, deviceModel }`) becomes the
   * `X-Client-Platform`, `X-Client-Os-Version`, and `X-Client-Device-Model` headers of this call
   * only, and only when `includeDiagnostics` is true.
   * @param {{ type: string, title: string, body: string, mediaIds?: string[], includeDiagnostics?: boolean, labels?: string[], tags?: Record<string,string>, diagnostics?: object }} ticket
   */
  createTicket({ type, title, body, mediaIds, includeDiagnostics = false, labels, tags, diagnostics } = {}) {
    const payload = { type, title, body, mediaIds: Array.isArray(mediaIds) ? mediaIds.slice() : [], includeDiagnostics: Boolean(includeDiagnostics) };
    if (labels) payload.labels = labels;
    if (tags) payload.tags = tags;
    const headers = diagnosticsHeaders(diagnostics, Boolean(includeDiagnostics));
    return this.post('/tickets', payload, Object.keys(headers).length ? { headers } : undefined);
  },
  /** POST /tickets/enumerate (own tickets). Orderings `LastActivityDescending` (default), `CreatedDescending`; filters `status`, `type`. */
  enumerateTickets(query = {}, signal) { return this.post('/tickets/enumerate', enumBody(query), { signal }); },
  /** GET /tickets/{id} (notes excluded). */
  getTicket(id, signal) { return this.get('/tickets/' + enc(idOf(id)), { signal }); },
  /** POST /tickets/{id}/messages/enumerate (`CreatedAscending` default; replies only). */
  enumerateTicketMessages(id, query = {}, signal) { return this.post('/tickets/' + enc(idOf(id)) + '/messages/enumerate', enumBody(query), { signal }); },
  /** POST /tickets/{id}/messages with `{ body, mediaIds }`. */
  replyToTicket(id, { body, mediaIds } = {}) { return this.post('/tickets/' + enc(idOf(id)) + '/messages', { body, mediaIds: Array.isArray(mediaIds) ? mediaIds.slice() : [] }); },
  /** POST /tickets/{id}/close. */
  closeTicket(id) { return this.post('/tickets/' + enc(idOf(id)) + '/close'); },
  /** POST /tickets/{id}/reopen (within `support.reopenDays`). */
  reopenTicket(id) { return this.post('/tickets/' + enc(idOf(id)) + '/reopen'); },
  /** POST /support/ticket-types/enumerate (enabled types with description and help text). */
  enumerateTicketTypes(query = {}, signal) { return this.post('/support/ticket-types/enumerate', enumBody(query), { signal }); },

  // ---- Staff tickets (section 6.2) ----

  /**
   * POST /admin/tickets/enumerate. Orderings `LastActivityDescending`, `CreatedAscending`,
   * `DueAscending`, `PriorityDescending`; filters `status`, `type`, `priority`, `assigneeUserId`,
   * `unassigned`, `requesterUserId`, `overdue`, `hasIdea`.
   */
  adminEnumerateTickets(query = {}, signal) { return this.post('/admin/tickets/enumerate', enumBody(query, ADMIN), { signal }); },
  /** GET /admin/tickets/{id} (with notes and the membership snapshot). */
  adminGetTicket(id, signal) { return this.get('/admin/tickets/' + enc(idOf(id)), { signal }); },
  /**
   * POST /admin/tickets/{id}/messages/enumerate: the full history (replies, notes, and system lines;
   * filter `kind`), `CreatedAscending` by default, paged with `continuationToken`.
   */
  adminEnumerateTicketMessages(id, query = {}, signal) { return this.post('/admin/tickets/' + enc(idOf(id)) + '/messages/enumerate', enumBody({ ordering: 'CreatedAscending', ...query }, ADMIN), { signal }); },
  /** POST /admin/tickets/{id}/messages with `{ kind: "Reply" | "Note", body, mediaIds, setStatus? }`. */
  adminPostTicketMessage(id, { kind = TicketMessageKind.Reply, body, mediaIds, setStatus } = {}) {
    const payload = { kind: kind === TicketMessageKind.Note ? TicketMessageKind.Note : TicketMessageKind.Reply, body, mediaIds: Array.isArray(mediaIds) ? mediaIds.slice() : [] };
    if (setStatus) payload.setStatus = setStatus;
    return this.post('/admin/tickets/' + enc(idOf(id)) + '/messages', payload);
  },
  /** PUT /admin/tickets/{id} with any of `{ status, priority, type, assigneeUserId, labels, tags }`. */
  adminUpdateTicket(id, body) { return this.put('/admin/tickets/' + enc(idOf(id)), body || {}); },
  /** DELETE /admin/tickets/{id} (Closed tickets only, else `409 TicketNotClosed`; as-built note C). */
  adminDeleteTicket(id) { return this.delete('/admin/tickets/' + enc(idOf(id))); },
  /** POST /admin/tickets/{id}/publish-idea with `{ title?, body?, showRequester, audience? }`; resolves the Idea. */
  adminPublishIdeaFromTicket(id, { title, body, showRequester = false, audience } = {}) {
    const payload = { showRequester: Boolean(showRequester) };
    if (title) payload.title = title;
    if (body) payload.body = body;
    if (audience) payload.audience = audience;
    return this.post('/admin/tickets/' + enc(idOf(id)) + '/publish-idea', payload);
  },
  /** POST /admin/tickets/{id}/link-idea with `{ ideaId }`. */
  adminLinkIdea(id, ideaId) { return this.post('/admin/tickets/' + enc(idOf(id)) + '/link-idea', { ideaId: idOf(ideaId) }); },
  /** The URL of `GET /admin/tickets/export.csv` with the enumerate filters (for a same-origin download link). */
  adminTicketExportUrl(query = {}) { return this.url('/admin/tickets/export.csv', exportParams(query)); },
  /** GET /admin/tickets/export.csv as text (sends the bearer token; for clients that cannot use a link). */
  adminExportTickets(query = {}, signal) { return this.get('/admin/tickets/export.csv', { query: exportParams(query), responseType: 'text', signal }); },
  /** GET /admin/metrics/support?days= (1 to 365, default 30). */
  adminSupportMetrics(query = {}, signal) { return this.post('/admin/metrics/support/enumerate', toEnumerationBody(query, { maxLimit: 500 }), { signal }); },

  // ---- Staff ticket types (section 6.2, SystemAdmin) ----

  /** POST /admin/support/ticket-types/enumerate (includes retired types). */
  adminEnumerateTicketTypes(query = {}, signal) { return this.post('/admin/support/ticket-types/enumerate', enumBody(query, ADMIN), { signal }); },
  /** GET /admin/support/ticket-types/{id}. */
  adminGetTicketType(id, signal) { return this.get('/admin/support/ticket-types/' + enc(idOf(id)), { signal }); },
  /** POST /admin/support/ticket-types with `{ key, label, description, helpText, staffRecipients, labels?, tags? }`. */
  adminCreateTicketType(body) { return this.post('/admin/support/ticket-types', body); },
  /** PUT /admin/support/ticket-types/{id}. */
  adminUpdateTicketType(id, body) { return this.put('/admin/support/ticket-types/' + enc(idOf(id)), body); },
  /** DELETE /admin/support/ticket-types/{id} (retires; existing tickets keep the type). */
  adminDeleteTicketType(id) { return this.delete('/admin/support/ticket-types/' + enc(idOf(id))); },
  /** PUT /admin/support/ticket-types/order with `{ ticketTypeIds }`. */
  adminOrderTicketTypes(ticketTypeIds) { return this.put('/admin/support/ticket-types/order', { ticketTypeIds: (ticketTypeIds || []).map(idOf) }); }
};

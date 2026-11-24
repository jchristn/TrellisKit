import { test } from 'node:test';
import assert from 'node:assert/strict';
import { ApiClient, diagnosticsHeaders, supportMethods } from '../src/index.js';
import { combineFakes, createIdeasFake, createSupportFake } from '../src/testing/index.js';

function recorder() {
  const calls = [];
  const fetchImpl = async (url, init) => {
    calls.push({ method: init.method, url: url.replace('/api/v1', ''), body: init.body ? JSON.parse(init.body) : undefined, headers: init.headers });
    return new Response(JSON.stringify({ ok: true }), { status: 200, headers: { 'content-type': 'application/json' } });
  };
  return { calls, client: new ApiClient({ fetchImpl, headers: { 'X-Client': 'Mobile' } }) };
}

const DIAG = { platform: 'Ios', osVersion: '18.1', deviceModel: 'iPhone15,2' };

test('every support method exists on ApiClient', () => {
  const names = ['createTicket', 'enumerateTickets', 'getTicket', 'enumerateTicketMessages', 'replyToTicket', 'closeTicket', 'reopenTicket', 'enumerateTicketTypes',
    'adminEnumerateTickets', 'adminGetTicket', 'adminEnumerateTicketMessages', 'adminPostTicketMessage', 'adminUpdateTicket', 'adminDeleteTicket', 'adminPublishIdeaFromTicket', 'adminLinkIdea',
    'adminTicketExportUrl', 'adminExportTickets', 'adminSupportMetrics', 'adminEnumerateTicketTypes', 'adminGetTicketType', 'adminCreateTicketType', 'adminUpdateTicketType',
    'adminDeleteTicketType', 'adminOrderTicketTypes'];
  for (const name of names) assert.equal(typeof ApiClient.prototype[name], 'function', name);
  assert.equal(Object.keys(supportMethods).length, names.length);
});

test('createTicket sends the diagnostics headers only on that call and only when included', async () => {
  const { calls, client } = recorder();
  await client.createTicket({ type: 'Billing', title: 'Charged twice', body: 'I was charged twice.', includeDiagnostics: true, diagnostics: DIAG });
  await client.createTicket({ type: 'Billing', title: 'Charged twice', body: 'I was charged twice.', includeDiagnostics: false, diagnostics: DIAG });
  await client.enumerateTickets();
  assert.equal(calls[0].headers['X-Client-Platform'], 'Ios');
  assert.equal(calls[0].headers['X-Client-Os-Version'], '18.1');
  assert.equal(calls[0].headers['X-Client-Device-Model'], 'iPhone15,2');
  assert.equal(calls[0].headers['X-Client'], 'Mobile');
  assert.deepEqual(calls[0].body, { type: 'Billing', title: 'Charged twice', body: 'I was charged twice.', mediaIds: [], includeDiagnostics: true });
  assert.equal(calls[1].headers['X-Client-Platform'], undefined);
  assert.equal(calls[1].body.includeDiagnostics, false);
  assert.equal(calls[2].headers['X-Client-Platform'], undefined);
  assert.deepEqual(diagnosticsHeaders({ platform: ' ', osVersion: '1' }, true), { 'X-Client-Os-Version': '1' });
  assert.deepEqual(diagnosticsHeaders(DIAG, false), {});
});

test('the admin ticket history pages with continuation, oldest first', async () => {
  const { calls, client } = recorder();
  await client.adminEnumerateTicketMessages('tkt_1', { maxResults: 900, continuationToken: 'c2' });
  assert.equal(calls[0].method + ' ' + calls[0].url, 'POST /admin/tickets/tkt_1/messages/enumerate');
  assert.deepEqual(calls[0].body, { maxResults: 500, continuationToken: 'c2', ordering: 'CreatedAscending' });
});

test('support methods map to the contract routes', async () => {
  const { calls, client } = recorder();
  await client.enumerateTickets({ filters: { status: ['Open'], type: 'Bug' } });
  await client.getTicket('tkt_1');
  await client.enumerateTicketMessages('tkt_1');
  await client.replyToTicket('tkt_1', { body: 'More detail' });
  await client.closeTicket('tkt_1');
  await client.reopenTicket('tkt_1');
  await client.enumerateTicketTypes();
  await client.adminEnumerateTickets({ maxResults: 900, ordering: 'DueAscending', filters: { unassigned: true } });
  await client.adminGetTicket('tkt_1');
  await client.adminPostTicketMessage('tkt_1', { kind: 'Note', body: 'Internal' });
  await client.adminPostTicketMessage('tkt_1', { body: 'Reply', setStatus: 'Resolved' });
  await client.adminUpdateTicket('tkt_1', { priority: 'High' });
  await client.adminDeleteTicket('tkt_1');
  await client.adminPublishIdeaFromTicket('tkt_1', { title: 'Dark mode' });
  await client.adminLinkIdea('tkt_1', 'ida_1');
  await client.adminSupportMetrics({ filters: { days: 9999 } });
  await client.adminExportTickets({ filters: { status: 'Open' }, maxResults: 10 });
  await client.adminEnumerateTicketTypes();
  await client.adminCreateTicketType({ key: 'Access' });
  await client.adminUpdateTicketType('tty_1', { label: 'Access' });
  await client.adminDeleteTicketType('tty_1');
  await client.adminOrderTicketTypes(['tty_2', { id: 'tty_1' }]);
  assert.deepEqual(calls.map((c) => c.method + ' ' + c.url), [
    'POST /tickets/enumerate', 'GET /tickets/tkt_1', 'POST /tickets/tkt_1/messages/enumerate', 'POST /tickets/tkt_1/messages', 'POST /tickets/tkt_1/close', 'POST /tickets/tkt_1/reopen',
    'POST /support/ticket-types/enumerate', 'POST /admin/tickets/enumerate', 'GET /admin/tickets/tkt_1', 'POST /admin/tickets/tkt_1/messages', 'POST /admin/tickets/tkt_1/messages',
    'PUT /admin/tickets/tkt_1', 'DELETE /admin/tickets/tkt_1', 'POST /admin/tickets/tkt_1/publish-idea', 'POST /admin/tickets/tkt_1/link-idea', 'POST /admin/metrics/support/enumerate',
    'GET /admin/tickets/export.csv?status=Open', 'POST /admin/support/ticket-types/enumerate', 'POST /admin/support/ticket-types', 'PUT /admin/support/ticket-types/tty_1',
    'DELETE /admin/support/ticket-types/tty_1', 'PUT /admin/support/ticket-types/order'
  ]);
  assert.deepEqual(calls[3].body, { body: 'More detail', mediaIds: [] });
  assert.equal(calls[7].body.maxResults, 500);
  assert.deepEqual(calls[7].body.filters, { unassigned: true });
  assert.deepEqual(calls[9].body, { kind: 'Note', body: 'Internal', mediaIds: [] });
  assert.deepEqual(calls[10].body, { kind: 'Reply', body: 'Reply', mediaIds: [], setStatus: 'Resolved' });
  assert.deepEqual(calls[13].body, { showRequester: false, title: 'Dark mode' });
  assert.deepEqual(calls[14].body, { ideaId: 'ida_1' });
  assert.deepEqual(calls[21].body, { ticketTypeIds: ['tty_2', 'tty_1'] });
  assert.equal(client.adminTicketExportUrl({ filters: { status: 'Open', overdue: true }, labels: ['vip'] }), '/api/v1/admin/tickets/export.csv?labels=vip&labelMatch=All&status=Open&overdue=true');
});

test('the support fake: a ticket lifecycle with device capture, notes hidden from members, and delete', async () => {
  const ideas = createIdeasFake();
  const support = createSupportFake({ ideasFake: ideas });
  const fake = combineFakes(support, ideas);
  const client = new ApiClient({ fetchImpl: fake.fetchImpl, headers: { 'X-Client': 'Mobile', 'X-Client-Version': '1.4.0' } });
  const types = await client.enumerateTicketTypes();
  assert.deepEqual(types.objects.map((t) => t.key), ['Support', 'Bug', 'Enhancement', 'Billing']);
  await assert.rejects(client.createTicket({ type: 'Nope', title: 'x', body: 'y' }), (e) => e.code === 'ValidationFailed' && Boolean(e.fieldErrors.type && e.fieldErrors.title));
  const ticket = await client.createTicket({ type: 'Enhancement', title: 'Dark mode please', body: 'The site is bright at night.', includeDiagnostics: true, diagnostics: DIAG });
  assert.equal(ticket.status, 'Open');
  assert.deepEqual([ticket.device.client, ticket.device.clientVersion, ticket.device.platform, ticket.device.osVersion, ticket.device.deviceModel], ['Mobile', '1.4.0', 'Ios', '18.1', 'iPhone15,2']);
  assert.equal(ticket.membership, undefined);

  await client.adminPostTicketMessage(ticket.id, { kind: 'Note', body: 'Seen this before' });
  await client.adminPostTicketMessage(ticket.id, { kind: 'Reply', body: 'Thanks, noted.' });
  const member = await client.getTicket(ticket.id);
  assert.equal(member.status, 'WaitingOnMember');
  // The opening text is the first Reply, as on the server.
  assert.deepEqual(member.messages.objects.map((m) => m.kind), ['Reply', 'Reply']);
  assert.equal(member.messages.objects[0].body, 'The site is bright at night.');
  const staff = await client.adminGetTicket(ticket.id);
  assert.deepEqual(staff.messages.objects.map((m) => m.kind), ['Reply', 'Note', 'Reply']);
  assert.ok(staff.membership);

  await client.replyToTicket(ticket.id, { body: 'Great, thank you' });
  assert.equal((await client.getTicket(ticket.id)).status, 'Open');
  const idea = await client.adminPublishIdeaFromTicket(ticket.id, { showRequester: false });
  assert.equal(idea.voteCount, 1);
  assert.equal((await client.adminGetTicket(ticket.id)).idea.id, idea.id);

  await assert.rejects(client.adminDeleteTicket(ticket.id), (e) => e.status === 409 && e.code === 'TicketNotClosed');
  await client.closeTicket(ticket.id);
  await assert.rejects(client.replyToTicket(ticket.id, { body: 'Hello again' }), (e) => e.code === 'TicketClosed');
  const inbox = await client.adminEnumerateTickets();
  assert.equal(inbox.objects.length, 0);
  const closed = await client.adminEnumerateTickets({ filters: { status: 'Closed' } });
  assert.equal(closed.objects.length, 1);
  const csv = await client.adminExportTickets({ filters: { status: 'Closed' } });
  assert.match(csv, /^number,type,status,priority,title\n1001,Enhancement,Closed/);
  assert.equal(await client.adminDeleteTicket(ticket.id), null);
  await assert.rejects(client.getTicket(ticket.id), (e) => e.status === 404);

  const created = await client.adminCreateTicketType({ key: 'Access', label: 'Access' });
  await client.adminOrderTicketTypes([created.id]);
  assert.equal((await client.adminEnumerateTicketTypes()).objects[0].key, 'Access');
  const metrics = await client.adminSupportMetrics({ filters: { days: 7 } });
  assert.equal(metrics.byType.objects.length, 5);
});

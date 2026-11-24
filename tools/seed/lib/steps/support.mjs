// Support tickets in several states (members open them, the support agent replies, assigns,
// and moves them along), plus ideas published from Enhancement tickets.

const ALL_STATUSES = ['Open', 'InProgress', 'WaitingOnMember', 'Resolved', 'Closed'];

export async function seedSupport(ctx) {
  const specs = ctx.seed.tickets ?? [];
  if (!specs.length || ctx.property.features?.support === false) return;
  ctx.log.info(`Support tickets (${specs.length})`);
  const agentKey = ctx.seed.supportAgent ?? [...ctx.members.values()].find((m) => (m.spec.roles ?? []).some((r) => r.role === 'SupportAgent'))?.key;
  const agent = agentKey ? ctx.member(agentKey) : ctx.member(ctx.seed.accounts.adminMember);

  for (const spec of specs) {
    const requester = ctx.member(spec.requester);
    const found = await ctx.findByTag('/api/v1/admin/tickets/enumerate', spec.key, { auth: agent.session, filters: { status: ALL_STATUSES } });
    let ticket = found[0];
    if (!ticket) {
      ticket = await ctx.api.post('/api/v1/tickets', {
        type: spec.type, title: spec.title, body: spec.body, includeDiagnostics: false, labels: spec.labels ?? [], tags: ctx.tags(spec.key, spec.tags),
      }, { auth: requester.session });
      ctx.count('tickets');
      if (spec.state !== 'Open' || spec.priority) {
        await ctx.api.put(`/api/v1/admin/tickets/${ticket.id}`, { assigneeUserId: agent.userId, priority: spec.priority }, { auth: agent.session });
      }
      for (const r of spec.replies ?? []) {
        await ctx.api.post(`/api/v1/admin/tickets/${ticket.id}/messages`, { kind: r.kind ?? 'Reply', body: r.body }, { auth: ctx.member(r.by ?? agentKey).session });
        ctx.count('ticket replies');
      }
      for (const r of spec.memberReplies ?? []) {
        await ctx.api.post(`/api/v1/tickets/${ticket.id}/messages`, { body: r.body }, { auth: requester.session });
        ctx.count('ticket replies');
      }
      if (spec.publishIdea) await publishIdea(ctx, agent, ticket.id, spec);
      const target = spec.state ?? 'Open';
      if (!['Open', 'WaitingOnMember'].includes(target)) {
        await ctx.api.put(`/api/v1/admin/tickets/${ticket.id}`, { status: target }, { auth: agent.session });
      }
    } else {
      ctx.count('tickets', 'existing');
      if (spec.publishIdea && !(await ctx.findByTag('/api/v1/admin/ideas/enumerate', spec.publishIdea.ideaKey, { auth: agent.session })).length) {
        await publishIdea(ctx, agent, ticket.id, spec);
      }
    }
    ctx.setId('ticket', spec.key, ticket.id);
  }
}

async function publishIdea(ctx, agent, ticketId, spec) {
  const res = await ctx.api.request('POST', `/api/v1/admin/tickets/${ticketId}/publish-idea`, {
    auth: agent.session, body: { showRequester: spec.publishIdea.showRequester ?? false, audience: spec.publishIdea.audience }, ok: ['TicketHasIdea', 'NotAnEnhancement'],
  });
  if (res.error) {
    ctx.warn(`publish idea from ${spec.key}: ${res.error}`);
    return;
  }
  const idea = res.data;
  await ctx.api.put(`/api/v1/admin/ideas/${idea.id}`, { tags: ctx.tags(spec.publishIdea.ideaKey), labels: spec.publishIdea.labels ?? [] }, { auth: agent.session });
  ctx.setId('idea', spec.publishIdea.ideaKey, idea.id);
  ctx.count('ideas (from tickets)');
}

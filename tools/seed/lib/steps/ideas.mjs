// Ideas board: staff ideas and ideas published from tickets, member votes, and statuses.
// Votes go in before a closing status so closed ideas still show their votes.

import { expandText } from '../util.mjs';

export async function seedIdeas(ctx) {
  const specs = ctx.seed.ideas ?? [];
  if (!specs.length || ctx.property.features?.ideas === false) return;
  ctx.log.info(`Ideas (${specs.length})`);
  for (const spec of specs) {
    const staff = ctx.member(spec.creator ?? ctx.seed.supportAgent ?? ctx.seed.accounts.adminMember);
    let idea = (await ctx.findByTag('/api/v1/admin/ideas/enumerate', spec.key, { auth: staff.session }))[0];
    if (!idea && spec.fromTicket) {
      ctx.warn(`idea ${spec.key}: not published from ticket ${spec.fromTicket} (run the support step first)`);
      continue;
    }
    if (!idea) {
      idea = await ctx.api.post('/api/v1/admin/ideas', {
        title: spec.title, body: expandText(spec.body, ctx), audience: spec.audience, showAuthor: spec.showAuthor ?? true,
        labels: spec.labels ?? [], tags: ctx.tags(spec.key, spec.tags),
      }, { auth: staff.session });
      ctx.count('ideas');
    } else if (!spec.fromTicket) ctx.count('ideas', 'existing');
    ctx.setId('idea', spec.key, idea.id);

    if ((idea.voteCount ?? 0) < (spec.voters ?? []).length) {
      for (const v of spec.voters ?? []) {
        const res = await ctx.api.request('PUT', `/api/v1/ideas/${idea.id}/vote`, { auth: ctx.member(v).session, ok: [402, 403, 409] });
        if (res.error) ctx.log.debug(`vote by ${v} on ${spec.key}: ${res.error}`);
        else ctx.count('idea votes');
      }
    }
    if (spec.status && idea.status?.key !== spec.status) {
      await ctx.api.put(`/api/v1/admin/ideas/${idea.id}/status`, { status: spec.status, statusNote: spec.statusNote ?? '', notifyVoters: spec.notifyVoters ?? true }, { auth: staff.session });
      ctx.count('idea statuses', 'set');
    }
    await import('../engagement.mjs').then(({ commentTree }) => commentTree(ctx, { base: `/api/v1/ideas/${idea.id}`, objectKey: spec.key, comments: spec.comments }));
  }
}

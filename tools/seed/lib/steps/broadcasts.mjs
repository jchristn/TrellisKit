// Broadcasts: created by a Communicator (or admin), then sent (with the confirmed recipient
// count from the segment preview) or scheduled.

import { isoInHours } from '../util.mjs';

export async function seedBroadcasts(ctx) {
  const specs = ctx.seed.broadcasts ?? [];
  if (!specs.length || ctx.property.features?.broadcasts === false) return;
  ctx.log.info(`Broadcasts (${specs.length})`);
  for (const spec of specs) {
    const creator = ctx.member(spec.creator ?? ctx.seed.accounts.adminMember);
    let b = (await ctx.findByTag('/api/v1/admin/broadcasts/enumerate', spec.key, { auth: creator.session })).find((x) => x.status !== 'Canceled');
    if (!b) {
      const channels = spec.channels ?? { inApp: true, push: false, email: false };
      const body = {
        title: spec.title, channels, inApp: spec.inApp,
        push: spec.push ?? (channels.push ? { title: spec.inApp.title, body: spec.inApp.body.slice(0, 180) } : undefined),
        email: channels.email ? spec.email : undefined,
        segment: spec.segment, category: spec.category, labels: spec.labels ?? [], tags: ctx.tags(spec.key, spec.tags),
      };
      b = await ctx.api.post('/api/v1/admin/broadcasts', body, { auth: creator.session });
      ctx.count('broadcasts');
    } else ctx.count('broadcasts', 'existing');
    ctx.setId('broadcast', spec.key, b.id);
    if (b.status !== 'Draft') continue;
    if (spec.action === 'send') {
      const preview = await ctx.api.post('/api/v1/admin/broadcasts/segments/preview', { segment: b.segment ?? spec.segment, category: spec.category }, { auth: creator.session });
      const res = await ctx.api.request('POST', `/api/v1/admin/broadcasts/${b.id}/send`, { auth: creator.session, body: { confirmCount: preview.count }, ok: ['BroadcastLimitReached', 400] });
      if (res.error) ctx.warn(`send broadcast ${spec.key}: ${res.error} ${res.data?.message ?? ''}`);
      else ctx.count('broadcasts', `sent to ${preview.count}`);
    } else if (spec.action === 'schedule') {
      const res = await ctx.api.request('POST', `/api/v1/admin/broadcasts/${b.id}/schedule`, { auth: creator.session, body: { sendAtUtc: isoInHours(spec.sendInHours ?? 48) }, ok: ['BroadcastLimitReached', 400] });
      if (res.error) ctx.warn(`schedule broadcast ${spec.key}: ${res.error} ${res.data?.message ?? ''}`);
      else ctx.count('broadcasts', 'scheduled');
    }
  }
}

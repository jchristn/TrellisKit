// Forums (admin). The engine seeds its default forum on first use; these are added next to it.

import { pick } from '../util.mjs';

export async function seedForums(ctx) {
  const specs = ctx.seed.forums ?? [];
  if (ctx.property.features?.forums === false) {
    ctx.warn('forums are off for this property; skipping forums and threads');
    return;
  }
  // Touch the member list so the default forum is seeded the way a first visit would. A seed file without
  // members (forums only, for a live property) has no session to do that with, and does not need it.
  if (ctx.members.size) await ctx.api.request('POST', '/api/v1/forums/enumerate', { auth: ctx.adminSession, body: { maxResults: 1 } });
  if (!specs.length) return;
  ctx.log.info(`Forums (${specs.length})`);
  for (const spec of specs) {
    const found = await ctx.findByTag('/api/v1/admin/forums/enumerate', spec.key);
    if (found.length) {
      ctx.setId('forum', spec.key, found[0].id);
      ctx.count('forums', 'existing');
      continue;
    }
    const body = {
      ...pick(spec, ['name', 'slug', 'description', 'guidelines', 'icon', 'color', 'section', 'audience', 'threadCreation', 'allowedThreadTypes', 'defaultOrdering', 'allowMedia', 'allowVideo', 'maxOpenFeedbackPerMember']),
      labels: spec.labels ?? [], tags: ctx.tags(spec.key, spec.tags),
    };
    const res = await ctx.api.request('POST', '/api/v1/admin/forums', { body, ok: ['SlugInUse'] });
    if (res.error === 'SlugInUse') {
      // A forum with this slug exists without the seed tag (for example after a reset): adopt it.
      const forum = ctx.members.size
        ? await ctx.api.get(`/api/v1/forums/${spec.slug}`, { auth: ctx.adminSession })
        : (await ctx.api.enumerateAll('/api/v1/admin/forums/enumerate', { maxResults: 200 })).find((f) => f.slug === spec.slug);
      if (!forum) throw new Error(`forum ${spec.slug} is in use but was not found`);
      await ctx.api.put(`/api/v1/admin/forums/${forum.id}`, body);
      ctx.setId('forum', spec.key, forum.id);
      ctx.count('forums', 'adopted');
      continue;
    }
    ctx.setId('forum', spec.key, res.data.id);
    ctx.count('forums');
  }
}

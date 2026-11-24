// Sign-up codes (admin). Codes are matched by their code string; seeded ones carry the seed tag.

export async function seedCodes(ctx) {
  const specs = ctx.seed.signupCodes ?? [];
  if (!specs.length) return;
  if (ctx.property.access?.model === 'Open' || ctx.property.access?.signupCodesEnabled === false) {
    ctx.warn('sign-up codes are not available in this access model; skipping codes');
    return;
  }
  ctx.log.info(`Sign-up codes (${specs.length})`);
  for (const spec of specs) {
    const existing = (await ctx.api.post('/api/v1/admin/signup-codes/enumerate', { maxResults: 5, search: spec.code })).objects
      .find((c) => c.code.toUpperCase() === spec.code.toUpperCase());
    if (existing) {
      ctx.setId('code', spec.key, existing.id);
      if (!existing.isActive) {
        await ctx.api.put(`/api/v1/admin/signup-codes/${existing.id}`, { isActive: true, tags: ctx.tags(spec.key, spec.tags), labels: spec.labels ?? [] });
        ctx.count('sign-up codes', 'reactivated');
      } else ctx.count('sign-up codes', 'existing');
      continue;
    }
    const body = {
      code: spec.code, name: spec.name, description: spec.description, notes: spec.notes,
      grantType: spec.grantType ?? 'DurationMonths', grantMonths: spec.grantMonths, grantEndUtc: spec.grantEndUtc,
      maxRedemptions: spec.maxRedemptions ?? null, expiresUtc: spec.expiresInDays ? new Date(Date.now() + spec.expiresInDays * 86400_000).toISOString() : undefined,
      labels: spec.labels ?? [], tags: ctx.tags(spec.key, spec.tags),
    };
    const created = await ctx.api.post('/api/v1/admin/signup-codes', body);
    ctx.setId('code', spec.key, created.id);
    ctx.count('sign-up codes');
  }
}

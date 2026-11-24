// Writes the local credentials file for every seeded account.

import { writeCredentials } from '../credentials.mjs';

export async function writeAccounts(ctx) {
  const rows = [...ctx.members.values()].map((m) => ({
    key: m.key,
    name: m.displayName,
    email: m.email,
    password: m.password,
    roles: (m.roles ?? []).join(', '),
    membership: m.membershipLabel ?? '',
    notes: m.spec.notes ?? '',
  }));
  const extra = [];
  const codes = ctx.seed.signupCodes ?? [];
  if (codes.length) {
    extra.push('## Sign-up codes', '', '| Code | Name | Grant | Max redemptions | Notes |', '|---|---|---|---|---|');
    for (const c of codes) {
      extra.push(`| \`${c.code}\` | ${c.name} | ${c.grantType ?? 'DurationMonths'}${c.grantMonths ? ` ${c.grantMonths} months` : ''} | ${c.maxRedemptions ?? 'unlimited'} | ${c.notes ?? ''} |`);
    }
  }
  extra.push('', `Admin token (break-glass, local only): see the property's server settings (auth.adminToken).`);
  await writeCredentials(ctx.credentialsFile, {
    title: `${ctx.property.name}: local demo accounts`,
    webBaseUrl: ctx.webBaseUrl,
    adminUrl: `${ctx.webBaseUrl}/admin`,
    rows,
    extra,
  });
}

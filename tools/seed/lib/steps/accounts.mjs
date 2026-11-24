// Member and staff accounts: register (or sign in), verify the email through Mailpit,
// fill the profile, upload the avatar, grant memberships, flags, and staff roles.

import { readFile } from 'node:fs/promises';
import path from 'node:path';
import { ApiError, Session } from '../client.mjs';
import { randomPassword } from '../util.mjs';
import { readCredentials } from '../credentials.mjs';

const GRANT_ROLE = { Feed: 'FeedModerator', Forum: 'Moderator', Events: 'EventManager', Library: 'LibraryEditor', Directory: 'DirectoryModerator', Messaging: 'Moderator' };

function emailFor(ctx, spec) {
  if (spec.email) return spec.email.toLowerCase();
  const domain = ctx.seed.accounts?.emailDomain ?? 'example.test';
  return `${(spec.handle ?? spec.key).toLowerCase().replace(/[^a-z0-9._-]/g, '')}@${domain}`;
}

function dateOfBirthFor(spec, index) {
  if (spec.dateOfBirth) return spec.dateOfBirth;
  const year = 1975 + ((index * 7) % 28);
  const month = String(1 + ((index * 5) % 12)).padStart(2, '0');
  const day = String(1 + ((index * 11) % 27)).padStart(2, '0');
  return `${year}-${month}-${day}`;
}

function requiredDocuments(property) {
  return (property.documents ?? [])
    .filter((d) => d.acceptance === 'RequiredAtSignup' || d.acceptance === 'AcceptedWithTerms')
    .map((d) => ({ key: d.key, version: d.version }));
}

async function findMemberByEmail(ctx, email) {
  const data = await ctx.api.post('/api/v1/admin/members/enumerate', { maxResults: 10, search: email });
  return (data.objects ?? []).find((m) => (m.email ?? '').toLowerCase() === email) ?? null;
}

async function resetPassword(ctx, member) {
  if (!ctx.mailpit.enabled) throw new Error(`cannot sign in as ${member.email} and no Mailpit is configured to reset the password`);
  const since = Date.now();
  await ctx.api.post('/api/v1/auth/password/forgot', { email: member.email }, { auth: null });
  const token = await ctx.mailpit.findToken(member.email, /reset-password\?token=([A-Za-z0-9_-]+)/, { sinceMs: since });
  if (!token) throw new Error(`no password reset email for ${member.email} in Mailpit`);
  await ctx.api.post('/api/v1/auth/password/reset', { token, newPassword: member.password }, { auth: null });
  ctx.count('accounts', 'password reset');
}

async function verifyEmail(ctx, member, sinceMs) {
  if (!ctx.mailpit.enabled) return false;
  let token = await ctx.mailpit.findToken(member.email, /verify-email\?token=([A-Za-z0-9_-]+)/, { sinceMs, timeoutMs: 15000 });
  if (!token) {
    const since = Date.now();
    await ctx.api.post('/api/v1/auth/email/resend', {}, { auth: member.session, ok: [429] });
    token = await ctx.mailpit.findToken(member.email, /verify-email\?token=([A-Za-z0-9_-]+)/, { sinceMs: since });
  }
  if (!token) {
    ctx.warn(`no verification email for ${member.email}; the account stays unverified`);
    return false;
  }
  await ctx.api.post('/api/v1/auth/email/verify', { token }, { auth: null, ok: ['InvalidToken'] });
  return true;
}

function sameRoles(current, desired) {
  const norm = (list) => list.map((r) => `${r.role}|${r.resourceId ?? '*'}`).sort().join(',');
  return norm(current) === norm(desired);
}

export async function seedAccounts(ctx) {
  const specs = ctx.seed.members ?? [];
  ctx.log.info(`Accounts (${specs.length})`);
  const credentialsFile = path.resolve(ctx.seedDir, ctx.seed.accounts?.credentialsFile ?? 'ACCOUNTS.local.md');
  ctx.credentialsFile = credentialsFile;
  const known = await readCredentials(credentialsFile);
  const documents = requiredDocuments(ctx.property);

  let index = 0;
  for (const spec of specs) {
    index += 1;
    const email = emailFor(ctx, spec);
    const prior = known.get(spec.key);
    const member = {
      key: spec.key, spec, email,
      password: prior && prior.email === email ? prior.password : randomPassword(ctx.seed.accounts?.passwordLength ?? 20),
      displayName: spec.displayName, handle: spec.handle,
    };
    member.session = new Session({ key: spec.key, email, password: member.password });
    ctx.members.set(spec.key, member);

    let existing = await findMemberByEmail(ctx, email);
    const startedMs = Date.now();
    if (!existing) {
      const res = await ctx.api.request('POST', '/api/v1/auth/register', {
        auth: null,
        body: { email, password: member.password, displayName: spec.displayName, dateOfBirth: dateOfBirthFor(spec, index), acceptTerms: true, acceptedDocuments: documents },
      });
      member.session.adopt(res.data);
      ctx.count('accounts');
    } else {
      try {
        await member.session.accessToken(ctx.api);
      } catch (err) {
        if (!(err instanceof ApiError) || err.status !== 401) throw err;
        await resetPassword(ctx, member);
        member.session.invalidate();
        await member.session.accessToken(ctx.api);
      }
      ctx.count('accounts', 'existing');
    }

    let me = await ctx.api.get('/api/v1/auth/me', { auth: member.session });
    if (!me.emailVerified && spec.verifyEmail !== false) await verifyEmail(ctx, member, startedMs);
    if (me.pendingDocuments?.length) {
      me = await ctx.api.post('/api/v1/me/documents/accept', { acceptTerms: true, documents: me.pendingDocuments.map((d) => ({ key: d.key, version: d.version })) }, { auth: member.session });
    }
    if (!me.ageAttested) {
      me = await ctx.api.post('/api/v1/me/age-attestation', { dateOfBirth: dateOfBirthFor(spec, index), acceptTerms: true, acceptedDocuments: documents }, { auth: member.session });
    }

    const profileBody = {
      displayName: spec.displayName, handle: spec.handle, bio: spec.bio ?? null,
      profile: spec.profile ?? {},
      labels: spec.labels ?? [], tags: ctx.tags(spec.key, spec.tags),
    };
    me = await ctx.api.put('/api/v1/me/profile', profileBody, { auth: member.session });
    member.userId = me.id;
    member.handle = me.handle;
    member.displayName = me.displayName;

    if (spec.avatar && !me.avatarUrl) {
      const { file } = await ctx.assetPath(spec.avatar);
      if (file) {
        const bytes = await readFile(file);
        const type = file.endsWith('.png') ? 'image/png' : 'image/jpeg';
        await ctx.api.request('PUT', '/api/v1/me/avatar', { auth: member.session, raw: bytes, contentType: type });
        ctx.count('avatars');
      }
    }
    if (spec.messaging) await ctx.api.put('/api/v1/me/messaging/settings', spec.messaging, { auth: member.session, ok: [404] });
  }

  // Staff roles, flags, and memberships need the admin token.
  for (const member of ctx.members.values()) {
    const spec = member.spec;
    const detail = await ctx.api.get(`/api/v1/admin/members/${member.userId}`);
    if (spec.flags && Object.entries(spec.flags).some(([k, v]) => detail.flags?.[k] !== v)) {
      await ctx.api.put(`/api/v1/admin/members/${member.userId}/flags`, spec.flags);
      ctx.count('member flags', 'set');
    }
    const plainRoles = (spec.roles ?? []).map((r) => ({ role: r.role, resourceType: r.resourceType, resourceId: r.resourceId ?? '*' }));
    const grants = spec.grants ?? [];
    const desired = [
      ...plainRoles,
      ...grants.map((g) => ({ role: GRANT_ROLE[g.resourceType] ?? 'Moderator', resourceType: g.resourceType, resourceId: g.resourceId ?? '*' })),
    ];
    const current = (detail.roles ?? []).filter((r) => r.role !== 'User');
    if (!sameRoles(current, desired)) {
      await ctx.api.put(`/api/v1/admin/members/${member.userId}/roles`, { roles: plainRoles.map((r) => ({ role: r.role, resourceType: r.resourceType, resourceId: r.resourceId })) });
      for (const g of grants) {
        await ctx.api.post('/api/v1/admin/moderators', { userId: member.userId, resourceType: g.resourceType, resourceId: g.resourceId ?? '*' });
      }
      if (desired.length) ctx.count('staff roles', 'granted', desired.length);
    }
    member.roles = desired.map((r) => (r.resourceType && !['Global'].includes(r.resourceType) && r.role !== 'SystemAdmin' ? `${r.role} (${r.resourceType})` : r.role));
    member.membershipLabel = await ensureMembership(ctx, member, detail);
  }
}

async function ensureMembership(ctx, member, detail) {
  const spec = member.spec.membership ?? { type: 'none' };
  const active = detail.membership?.isActive;
  if (spec.type === 'none' || spec === 'none') return active ? `Active (${detail.membership.source})` : 'None (paywall)';
  if (active) return `Active (${detail.membership.source})`;
  if (spec.type === 'code') {
    const code = (ctx.seed.signupCodes ?? []).find((c) => c.key === spec.code);
    if (!code) throw new Error(`member ${member.key}: unknown sign-up code ${spec.code}`);
    const res = await ctx.api.request('POST', '/api/v1/signup-codes/redeem', { auth: member.session, body: { code: code.code }, ok: ['AlreadyRedeemed', 'InvalidCode'] });
    if (res.error) ctx.warn(`member ${member.key}: redeeming ${code.code} answered ${res.error}`);
    else ctx.count('code redemptions');
    return `Code ${code.code}`;
  }
  if (spec.type === 'comp') {
    await ctx.api.post(`/api/v1/admin/members/${member.userId}/comp`, {
      grantType: spec.grantType ?? 'DurationMonths', months: spec.months ?? 12, endUtc: spec.endUtc,
      reason: spec.reason ?? 'Demo data', labels: [], tags: ctx.tags(`comp:${member.key}`),
    });
    ctx.count('admin comps');
    return `Comp (${spec.reason ?? 'demo'})`;
  }
  throw new Error(`member ${member.key}: unknown membership type ${spec.type}`);
}

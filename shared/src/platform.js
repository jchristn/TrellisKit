/**
 * Platform console API methods and helpers (REST_API.md "Platform (MultiProperty)"), mixed into
 * `ApiClient.prototype`. Every method maps to exactly one route under `/api/v1/platform`. Those
 * routes exist only in `MultiProperty` deployments and only on a platform host; they take a
 * platform administrator token (`tkp_...`, from `platformLogin`) as the bearer token, never a
 * member token.
 *
 * Lists send the canonical `POST .../enumerate` form with an `EnumerationQuery`
 * (docs/ENUMERATION_AND_METADATA.md) and resolve an `EnumerationResult`.
 */

import { ENUMERATION_MAX_RESULTS_ADMIN, toEnumerationBody } from './enumeration.js';
import { ApiError } from './ApiError.js';

const enc = encodeURIComponent;
const ADMIN = { maxLimit: ENUMERATION_MAX_RESULTS_ADMIN };
const BASE = '/platform';

/** `PropertyRecord.status` values. */
export const PropertyStatus = Object.freeze({ Disabled: 'Disabled', Enabled: 'Enabled', Deleting: 'Deleting', Deleted: 'Deleted' });

/** Property domain kinds. */
export const DomainKind = Object.freeze({ Web: 'Web', Api: 'Api', Media: 'Media', Redirect: 'Redirect', Alias: 'Alias' });

/** Property app platforms. */
export const AppPlatform = Object.freeze({ Ios: 'Ios', Android: 'Android' });

/** Fixed bounds of a property record (REST_API.md). */
export const PROPERTY_MAX_DOMAINS = 20;
export const PROPERTY_MAX_APPS = 10;

/** Orderings of the platform collections. */
export const PropertyOrderings = Object.freeze(['CreatedDescending', 'CreatedAscending', 'NameAscending', 'UpdatedDescending']);
export const PlatformAdminOrderings = Object.freeze(['CreatedDescending', 'CreatedAscending', 'EmailAscending']);

/** Collection filters accepted by the platform enumerations. */
export const PROPERTY_FILTER_KEYS = Object.freeze(['status', 'host']);
export const PLATFORM_AUDIT_FILTER_KEYS = Object.freeze(['propertyId', 'actorUserId', 'action', 'targetType', 'targetId']);

/** The request headers that select a property (docs/TRELLISKIT.md section 10.1). */
export const PropertyHeader = 'X-TK-Property';
export const AppHeader = 'X-TK-App';

function enumerateBody(query) {
  return toEnumerationBody(query || {}, ADMIN);
}

/** @this {import('./ApiClient.js').ApiClient} */
export const platformMethods = {
  // ---- Sign-in (anonymous on a platform host) ----

  /** POST /platform/auth/login `{ email, password }`: `{ accessToken, expiresUtc, admin }`. */
  platformLogin(email, password) {
    return this.post(BASE + '/auth/login', { email, password }, { retryOnUnauthorized: false });
  },

  /** POST /platform/auth/accept-invite `{ token, password }` (204). */
  platformAcceptInvite(token, password) {
    return this.post(BASE + '/auth/accept-invite', { token, password }, { retryOnUnauthorized: false });
  },

  /** POST /platform/auth/logout (204). */
  platformLogout() {
    return this.post(BASE + '/auth/logout', undefined, { retryOnUnauthorized: false });
  },

  /** GET /platform/auth/me: the signed-in `PlatformAdmin`. */
  getPlatformMe(signal) {
    return this.get(BASE + '/auth/me', { signal, retryOnUnauthorized: false });
  },

  /**
   * Whether this host serves the platform console, decided by the backend: `GET /platform/auth/me`
   * answers `404` (`NotAvailable` in SingleProperty, `PropertyNotFound` on a host that is not a
   * platform host) when it does not. Resolves `{ available, signedIn, admin, code }`; network
   * failures reject with the ApiError (status 0).
   * @param {AbortSignal} [signal]
   */
  async platformAvailability(signal) {
    try {
      const admin = await this.getPlatformMe(signal);
      return { available: true, signedIn: true, admin: admin || null, code: null };
    } catch (err) {
      if (!(err instanceof ApiError) || err.status === 0) throw err;
      if (err.status === 404) return { available: false, signedIn: false, admin: null, code: err.code || 'NotFound' };
      if (err.status === 401) return { available: true, signedIn: false, admin: null, code: err.code || 'Unauthorized' };
      // 403 is the break-glass token: the console is available, the caller has no admin record.
      if (err.status === 403) return { available: true, signedIn: true, admin: null, code: err.code || 'Forbidden' };
      throw err;
    }
  },

  // ---- Platform administrators ----

  /** POST /platform/admins/enumerate. */
  listPlatformAdmins(query, signal) {
    return this.post(BASE + '/admins/enumerate', enumerateBody(query), { signal });
  },

  /**
   * POST /platform/admins `{ email, displayName }`: `{ admin, inviteToken, inviteDelivery, inviteExpiresUtc }`.
   * `inviteDelivery` is `Email` (the invitation was emailed; `inviteToken` is null) or `Log` (no deployment
   * email provider; the one-time `inviteToken` is returned once).
   */
  invitePlatformAdmin({ email, displayName }) {
    return this.post(BASE + '/admins', { email, displayName });
  },

  /** DELETE /platform/admins/{adminId} (204). */
  removePlatformAdmin(adminId) {
    return this.delete(BASE + '/admins/' + enc(adminId));
  },

  // ---- Properties ----

  /** POST /platform/properties/enumerate; filters `status`, `host`; search over id, slug, name. */
  listProperties(query, signal) {
    return this.post(BASE + '/properties/enumerate', enumerateBody(query), { signal });
  },

  /**
   * POST /platform/properties `{ name?, slug?, document? | cloneFromPropertyId?, domains?, apps?, labels?, tags? }`.
   * The id is always generated by the server; the property starts `Disabled`.
   */
  createProperty(body) {
    return this.post(BASE + '/properties', body);
  },

  /** GET /platform/properties/{propertyId}. */
  getProperty(propertyId, signal) {
    return this.get(BASE + '/properties/' + enc(propertyId), { signal });
  },

  /** PUT /platform/properties/{propertyId} with any of `name`, `domains`, `apps` (each replaces), `labels`, `tags`. */
  updateProperty(propertyId, body) {
    return this.put(BASE + '/properties/' + enc(propertyId), body);
  },

  /** POST /platform/properties/{propertyId}/enable. */
  enableProperty(propertyId) {
    return this.post(BASE + '/properties/' + enc(propertyId) + '/enable');
  },

  /** POST /platform/properties/{propertyId}/disable `{ reason? }`. */
  disableProperty(propertyId, reason) {
    return this.post(BASE + '/properties/' + enc(propertyId) + '/disable', reason ? { reason } : {});
  },

  /** DELETE /platform/properties/{propertyId} `{ confirm: <propertyId> }` (202, status `Deleting`). */
  deleteProperty(propertyId, confirm) {
    return this.request('DELETE', BASE + '/properties/' + enc(propertyId), { body: { confirm } });
  },

  /** POST /platform/properties/{propertyId}/restore (back to `Disabled` while the purge has not started). */
  restoreProperty(propertyId) {
    return this.post(BASE + '/properties/' + enc(propertyId) + '/restore');
  },

  /** POST /platform/properties/{propertyId}/admins/enumerate: the property's members annotated as SystemAdmin. */
  listPropertyAdmins(propertyId, query, signal) {
    return this.post(BASE + '/properties/' + enc(propertyId) + '/admins/enumerate', enumerateBody(query), { signal });
  },

  /** POST /platform/properties/{propertyId}/admins `{ userId }`. */
  grantPropertyAdmin(propertyId, userId) {
    return this.post(BASE + '/properties/' + enc(propertyId) + '/admins', { userId });
  },

  /** DELETE /platform/properties/{propertyId}/admins/{userId}. */
  revokePropertyAdmin(propertyId, userId) {
    return this.delete(BASE + '/properties/' + enc(propertyId) + '/admins/' + enc(userId));
  },

  /**
   * POST /platform/properties/{propertyId}/admin-session: `{ accessToken, accessTokenExpiresUtc, propertyId }`.
   * A delegated, non-refreshable session bound to that property; send it with `X-TK-Property`.
   */
  createDelegatedSession(propertyId) {
    return this.post(BASE + '/properties/' + enc(propertyId) + '/admin-session');
  },

  // ---- Audit and settings ----

  /** POST /platform/audit/enumerate; filters `propertyId` (default the platform realm), `actorUserId`, `action`, `targetType`, `targetId`. */
  listPlatformAudit(query, signal) {
    return this.post(BASE + '/audit/enumerate', enumerateBody(query), { signal });
  },

  /** GET /platform/settings (same body as GET /settings). */
  getPlatformSettings(query = {}, signal) {
    return this.post(BASE + '/settings/enumerate', enumerateBody(query), { signal });
  },

  /** PUT /platform/settings `{ values }`. */
  updatePlatformSettings(values) {
    return this.put(BASE + '/settings', { values });
  },

  /** POST /platform/settings/restart (202). */
  restartPlatformServer() {
    return this.post(BASE + '/settings/restart', {});
  },

  /** GET /platform/properties/{propertyId}/settings (same body as GET /admin/property/settings). */
  getPlatformPropertySettings(propertyId, query = {}, signal) {
    return this.post(BASE + '/properties/' + enc(propertyId) + '/settings/enumerate', enumerateBody(query), { signal });
  },

  /** PUT /platform/properties/{propertyId}/settings `{ values, clear }`. */
  updatePlatformPropertySettings(propertyId, { values = {}, clear = [] } = {}) {
    return this.put(BASE + '/properties/' + enc(propertyId) + '/settings', { values, clear });
  },

  /** GET /platform/properties/{propertyId}/branding (same body as GET /admin/property/branding). */
  getPlatformPropertyBranding(propertyId, signal) {
    return this.get(BASE + '/properties/' + enc(propertyId) + '/branding', { signal });
  },

  /** PUT /platform/properties/{propertyId}/branding `{ light?, dark?, contrastOverrides?, note? }`. */
  updatePlatformPropertyBranding(propertyId, body) {
    return this.put(BASE + '/properties/' + enc(propertyId) + '/branding', body || {});
  },

  /** POST /platform/properties/{propertyId}/branding/revert `{ to: 'Previous' | 'Seed', note? }`. */
  revertPlatformPropertyBranding(propertyId, to, note) {
    return this.post(BASE + '/properties/' + enc(propertyId) + '/branding/revert', note ? { to, note } : { to });
  },

  /** POST /platform/properties/{propertyId}/branding/versions/enumerate (same as the admin route). */
  enumeratePlatformPropertyBrandingVersions(propertyId, query = {}, signal) {
    return this.post(BASE + '/properties/' + enc(propertyId) + '/branding/versions/enumerate', enumerateBody(query), { signal });
  },

  /** POST /platform/properties/{propertyId}/branding/revert `{ to: 'Version', version, note? }`. */
  restorePlatformPropertyBrandingVersion(propertyId, version, note) {
    const body = { to: 'Version', version: Math.floor(Number(version)) };
    if (note) body.note = note;
    return this.post(BASE + '/properties/' + enc(propertyId) + '/branding/revert', body);
  },

  /** PUT /platform/properties/{propertyId}/branding/member-presets `{ mode, presetIds? }` (same as the admin route). */
  updatePlatformPropertyMemberPresets(propertyId, body) {
    return this.put(BASE + '/properties/' + enc(propertyId) + '/branding/member-presets', body || {});
  },

  /** GET /platform/properties/{propertyId}/ai (same body as GET /admin/property/ai). */
  getPlatformPropertyAi(propertyId, signal) {
    return this.get(BASE + '/properties/' + enc(propertyId) + '/ai', { signal });
  },

  /** PUT /platform/properties/{propertyId}/ai `{ enabled }`. */
  updatePlatformPropertyAi(propertyId, enabled) {
    return this.put(BASE + '/properties/' + enc(propertyId) + '/ai', { enabled: Boolean(enabled) });
  },

  // ---- Observability ----

  /**
   * POST /platform/request-history/enumerate. One property per call: `filters.propertyId` is a
   * property id, `platform` (default), or `unresolved`; other filters `method`, `statusCode`,
   * `pathContains`, `userId`; search over path and principal name.
   */
  listPlatformRequestHistory(query, signal) {
    return this.post(BASE + '/request-history/enumerate', enumerateBody(query), { signal });
  },

  /**
   * POST /platform/metrics/properties/enumerate: `{ propertyId, slug, name, status, members,
   * activeMembers, requests24h, errors24h, storageBytes, labels, tags }` per property; filter `status`.
   */
  listPropertyMetrics(query, signal) {
    return this.post(BASE + '/metrics/properties/enumerate', enumerateBody(query), { signal });
  },

  // ---- Metadata discovery (console autocomplete) ----

  /** POST /platform/labels/enumerate `{ targetType? (Property default, or PlatformAdmin), prefix? }`. */
  enumeratePlatformLabels({ targetType, prefix, ...query } = {}, signal) {
    return this.post(BASE + '/labels/enumerate', { ...enumerateBody(query), ...(targetType ? { targetType } : {}), ...(prefix ? { prefix } : {}) }, { signal });
  },

  /** POST /platform/tags/keys/enumerate. */
  enumeratePlatformTagKeys({ targetType, prefix, ...query } = {}, signal) {
    return this.post(BASE + '/tags/keys/enumerate', { ...enumerateBody(query), ...(targetType ? { targetType } : {}), ...(prefix ? { prefix } : {}) }, { signal });
  },

  /** POST /platform/tags/values/enumerate `{ key, targetType?, prefix? }`. */
  enumeratePlatformTagValues({ targetType, key, prefix, ...query } = {}, signal) {
    return this.post(BASE + '/tags/values/enumerate', { ...enumerateBody(query), key, ...(targetType ? { targetType } : {}), ...(prefix ? { prefix } : {}) }, { signal });
  }
};

/** `inviteDelivery` values of `POST /platform/admins`. */
export const InviteDelivery = Object.freeze({ Email: 'Email', Log: 'Log' });

/** Orderings of the property metrics collection. */
export const PropertyMetricsOrderings = Object.freeze(['CreatedDescending', 'CreatedAscending', 'NameAscending']);

/** Special `filters.propertyId` values of the platform request history. */
export const PlatformRequestScope = Object.freeze({ Platform: 'platform', Unresolved: 'unresolved' });

/**
 * Deletion state of a property record. A deleted property is disabled at once, restorable until
 * `deleteScheduledUtc` (30 days by default, `platform.propertyPurgeDelayHours`), then purged.
 * @param {{ status?: string, deleteScheduledUtc?: string|null, purgeStartedUtc?: string|null }} record
 * @param {number} [now] epoch milliseconds
 * @returns {{ deleting: boolean, restorable: boolean, purging: boolean, deleted: boolean, purgeAtUtc: string|null, msLeft: number, daysLeft: number }}
 */
export function propertyDeletionState(record, now = Date.now()) {
  const status = record && record.status;
  const purgeAtUtc = (record && record.deleteScheduledUtc) || null;
  const purging = Boolean(record && record.purgeStartedUtc) && status === PropertyStatus.Deleting;
  const deleting = status === PropertyStatus.Deleting;
  const at = purgeAtUtc ? Date.parse(purgeAtUtc) : NaN;
  const msLeft = deleting && Number.isFinite(at) ? Math.max(0, at - now) : 0;
  return {
    deleting,
    restorable: deleting && !purging,
    purging,
    deleted: status === PropertyStatus.Deleted,
    purgeAtUtc,
    msLeft,
    daysLeft: Math.ceil(msLeft / 86400000)
  };
}

/**
 * Time left on a delegated session.
 * @param {{ accessTokenExpiresUtc?: string }|null} session
 * @param {number} [now] epoch milliseconds
 * @returns {{ active: boolean, msLeft: number, minutes: number, seconds: number }}
 */
export function delegationTimeLeft(session, now = Date.now()) {
  const at = session && session.accessTokenExpiresUtc ? Date.parse(session.accessTokenExpiresUtc) : NaN;
  const msLeft = Number.isFinite(at) ? Math.max(0, at - now) : 0;
  const total = Math.floor(msLeft / 1000);
  return { active: msLeft > 0, msLeft, minutes: Math.floor(total / 60), seconds: total % 60 };
}

/**
 * The per-property Stripe webhook path (`POST /api/v1/webhooks/stripe/{propertyId}`; each
 * property's Stripe account posts to its own URL).
 * @param {string} propertyId
 * @returns {string}
 */
export function stripeWebhookPath(propertyId) {
  return '/api/v1/webhooks/stripe/' + enc(String(propertyId || ''));
}

/**
 * Parse pasted or uploaded `property.json` text for `createProperty({ document })`. The server
 * validates the document fully; this only checks it is a JSON object.
 * @param {string} text
 * @returns {{ document: object|null, error: 'Empty'|'InvalidJson'|'NotObject'|null }}
 */
export function parsePropertyDocument(text) {
  const source = String(text || '').trim();
  if (!source) return { document: null, error: 'Empty' };
  let value;
  try {
    value = JSON.parse(source);
  } catch {
    return { document: null, error: 'InvalidJson' };
  }
  if (!value || typeof value !== 'object' || Array.isArray(value)) return { document: null, error: 'NotObject' };
  return { document: value, error: null };
}

/**
 * Domain and app rows for the `PUT /platform/properties/{id}` body: trimmed, lowercased hosts,
 * empty rows dropped, and only the fields the route reads.
 * @param {Array<{ host?: string, kind?: string }>} domains
 * @param {Array<{ platform?: string, appId?: string, storeAppId?: string|null, enabled?: boolean }>} apps
 * @returns {{ domains: Array<{ host: string, kind: string }>, apps: Array<{ platform: string, appId: string, storeAppId: string|null, enabled: boolean }> }}
 */
export function domainsAndAppsBody(domains, apps) {
  return {
    domains: (domains || [])
      .map((d) => ({ host: String((d && d.host) || '').trim().toLowerCase().replace(/\.$/, ''), kind: (d && d.kind) || DomainKind.Web }))
      .filter((d) => d.host),
    apps: (apps || [])
      .map((a) => ({
        platform: (a && a.platform) || AppPlatform.Ios,
        appId: String((a && a.appId) || '').trim(),
        storeAppId: a && a.storeAppId ? String(a.storeAppId).trim() || null : null,
        enabled: !(a && a.enabled === false)
      }))
      .filter((a) => a.appId)
  };
}

/**
 * Client-side checks that mirror the server (hosts and app ids unique within the property, at
 * most 20 domains and 10 apps). Keys follow the server's `fieldErrors` (`domains[0].host`).
 * The server stays authoritative and also enforces uniqueness across the deployment.
 * @returns {Record<string,string>} problem codes: `Duplicate`, `TooMany`, `InvalidHost`, `InvalidAppId`
 */
export function domainsAndAppsProblems(domains, apps) {
  const problems = {};
  const hosts = new Set();
  (domains || []).forEach((d, i) => {
    if (!d.host) return;
    if (!/^[a-z0-9]([a-z0-9-]*[a-z0-9])?(\.[a-z0-9]([a-z0-9-]*[a-z0-9])?)+$/.test(d.host)) problems['domains[' + i + '].host'] = 'InvalidHost';
    else if (hosts.has(d.host)) problems['domains[' + i + '].host'] = 'Duplicate';
    hosts.add(d.host);
  });
  if ((domains || []).length > PROPERTY_MAX_DOMAINS) problems.domains = 'TooMany';
  const appIds = new Set();
  (apps || []).forEach((a, i) => {
    if (!a.appId) return;
    const key = a.platform + ':' + a.appId.toLowerCase();
    if (!/^[A-Za-z][A-Za-z0-9_]*(\.[A-Za-z0-9_]+)+$/.test(a.appId)) problems['apps[' + i + '].appId'] = 'InvalidAppId';
    else if (appIds.has(key)) problems['apps[' + i + '].appId'] = 'Duplicate';
    appIds.add(key);
  });
  if ((apps || []).length > PROPERTY_MAX_APPS) problems.apps = 'TooMany';
  return problems;
}

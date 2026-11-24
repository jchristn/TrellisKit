/**
 * Live updates and app configuration admin API methods (docs/LIVE_UPDATES_CONTRACT.md sections 6, 7, 10, 12,
 * 13, and 16), mixed into `ApiClient.prototype`, plus pure helpers the admin console uses (rollout steps, rollout
 * plan templates, promotion rules, and the signing key rotation checklist). Every list is the canonical
 * `POST .../enumerate` with an EnumerationQuery and resolves an EnumerationResult.
 */

import { ENUMERATION_MAX_RESULTS_ADMIN, toEnumerationBody } from './enumeration.js';

const enc = encodeURIComponent;
const ADMIN = { maxLimit: ENUMERATION_MAX_RESULTS_ADMIN };
const GROUPS = '/admin/update-groups';
const UPDATES = '/admin/updates';
const APP = '/admin/app';

function body(query) {
  return toEnumerationBody(query || {}, ADMIN);
}

/** Update group statuses. */
export const UpdateGroupStatus = Object.freeze({ Draft: 'Draft', Published: 'Published', Paused: 'Paused', RolledBack: 'RolledBack', Superseded: 'Superseded' });

/** Update group kinds. */
export const UpdateGroupKind = Object.freeze({ Publish: 'Publish', Republish: 'Republish', RollBackToEmbedded: 'RollBackToEmbedded' });

/** Signing key statuses. */
export const SigningKeyStatus = Object.freeze({ Active: 'Active', Standby: 'Standby', Retired: 'Retired' });

/** Feature flag kinds that admins create (kill switches have their own routes). */
export const FlagKind = Object.freeze({ Boolean: 'Boolean', Variant: 'Variant', Value: 'Value' });

/** Feature flag value types. */
export const FlagValueType = Object.freeze({ Boolean: 'Boolean', String: 'String', Integer: 'Integer', Decimal: 'Decimal' });

/** Feature flag states. */
export const FlagState = Object.freeze({ Draft: 'Draft', Published: 'Published', Archived: 'Archived' });

/** Update platforms. */
export const UPDATE_PLATFORMS = Object.freeze(['ios', 'android']);

/** Built-in update channels; development is never promoted. */
export const BUILT_IN_CHANNELS = Object.freeze(['production', 'preview', 'development']);

/** Rollout steps offered by the console slider (contract section 21). */
export const ROLLOUT_STEPS = Object.freeze([1, 5, 10, 25, 50, 100]);

/** Filter keys of POST /admin/update-groups/enumerate. */
export const UPDATE_GROUP_FILTER_KEYS = Object.freeze(['platform', 'channel', 'runtimeVersion', 'status', 'kind', 'critical', 'gitCommit']);

/** Filter keys of POST /admin/app/flags/enumerate. */
export const FLAG_FILTER_KEYS = Object.freeze(['kind', 'state', 'hasDraftChanges', 'client']);

/** Targeting attributes and their operators (contract section 12.2). */
export const FLAG_ATTRIBUTES = Object.freeze({
  platform: ['In', 'NotIn'],
  client: ['In', 'NotIn'],
  appVersion: ['SemverRange', 'In', 'NotIn'],
  updateId: ['In', 'NotIn'],
  runtimeVersion: ['In', 'NotIn'],
  channel: ['In', 'NotIn'],
  signedIn: ['Is'],
  membership: ['In'],
  userRole: ['In', 'NotIn'],
  userLabels: ['HasAny', 'HasAll', 'HasNone'],
  userTag: ['Equals', 'HasKey'],
  userId: ['In'],
  profileField: ['Equals', 'In'],
  country: ['In', 'NotIn'],
  date: ['Before', 'After']
});

/** Banner styles (contract section 14.1); `Critical` is never dismissible. */
export const BannerStyle = Object.freeze({ Info: 'Info', Success: 'Success', Warning: 'Warning', Critical: 'Critical' });

/** Banner placements (contract section 14.1); `Global` banners show at the top of every screen. */
export const BANNER_PLACEMENTS = Object.freeze(['Global', 'Home', 'Feed', 'Forums', 'Library', 'Events', 'Directory', 'Messages', 'SignIn', 'Account']);

/** Filter keys of POST /admin/app/strings/enumerate. */
export const REMOTE_STRING_FILTER_KEYS = Object.freeze(['client', 'overridden', 'hasDraftChanges']);

/** Filter keys of POST /admin/app/banners/enumerate. */
export const BANNER_FILTER_KEYS = Object.freeze(['active', 'placement', 'style', 'state', 'hasDraftChanges']);

/** Filter keys of POST /admin/app/assets/enumerate. */
export const REMOTE_ASSET_FILTER_KEYS = Object.freeze(['state', 'hasDraftChanges']);

/** Image types accepted for remote asset content (re-encoded to WebP on the server). */
export const REMOTE_ASSET_CONTENT_TYPES = Object.freeze(['image/png', 'image/jpeg', 'image/webp']);

/** Longest banner title and body. */
export const BANNER_TITLE_MAX = 80;
export const BANNER_BODY_MAX = 500;

/** Longest stack trace the symbolication route accepts. */
export const STACK_TRACE_MAX = 65536;

/**
 * The `{token}` names a string uses, in order of first appearance.
 * @param {string} text
 * @returns {string[]}
 */
export function stringTokens(text) {
  const out = [];
  for (const match of String(text || '').matchAll(/\{([A-Za-z][A-Za-z0-9]*)\}/g)) if (!out.includes(match[1])) out.push(match[1]);
  return out;
}

/**
 * Tokens an override uses that its catalog entry does not declare (the server answers 400 InvalidStringTokens for them).
 * @param {string} value Override value.
 * @param {string[]} allowed The entry's tokens.
 * @returns {string[]}
 */
export function invalidStringTokens(value, allowed) {
  const ok = new Set(allowed || []);
  return stringTokens(value).filter((token) => !ok.has(token));
}

/**
 * A client-side banner problem for the editor (the server validates the same rules), or null.
 * @param {{ title?: string, body?: string, link?: { label?: string, path?: string, url?: string } | null }} banner
 * @returns {string|null} The field name with a problem.
 */
export function bannerProblem(banner) {
  const b = banner || {};
  const title = String(b.title || '').trim();
  if (title.length < 1 || title.length > BANNER_TITLE_MAX) return 'title';
  if (String(b.body || '').length > BANNER_BODY_MAX) return 'body';
  const link = b.link;
  if (link && (link.path || link.url)) {
    if (link.path && link.url) return 'link';
    if (link.path && !String(link.path).startsWith('/')) return 'link.path';
    if (link.url && !/^https:\/\//i.test(String(link.url))) return 'link.url';
    if (!String(link.label || '').trim()) return 'link.label';
  }
  return null;
}

/** Longest critical prompt text. */
export const CRITICAL_MESSAGE_MAX = 200;

/**
 * The next rollout step above a percentage, or null at 100.
 * @param {number} percent
 */
export function nextRolloutStep(percent) {
  const current = Number(percent) || 0;
  return ROLLOUT_STEPS.find((step) => step > current) || null;
}

/**
 * Parse a rollout plan template (`5@0,25@24,100@72`: percent@hoursAfterPublish).
 * @param {string} template
 * @returns {{ percentage: number, hoursAfterPublish: number }[]} Stages; throws on a malformed template.
 */
export function parseRolloutTemplate(template) {
  const stages = [];
  for (const part of String(template || '').split(',').map((p) => p.trim()).filter(Boolean)) {
    const [pct, hours] = part.split('@');
    const percentage = Number(pct);
    const hoursAfterPublish = Number(hours);
    if (!Number.isFinite(percentage) || !Number.isFinite(hoursAfterPublish) || percentage <= 0 || percentage > 100 || hoursAfterPublish < 0) {
      throw new Error('Rollout templates are percent@hours stages, for example 5@0,25@24,100@72.');
    }
    stages.push({ percentage, hoursAfterPublish });
  }
  return stages;
}

/**
 * Validate rollout plan stages before sending them: at most 10, increasing percentages, time order.
 * @param {{ percentage: number, startsUtc?: string, hoursAfterPublish?: number }[]} stages
 * @returns {string|null} The first problem, or null.
 */
export function rolloutPlanProblem(stages) {
  const list = stages || [];
  if (list.length > 10) return 'At most 10 stages.';
  let lastPct = 0;
  let lastTime = -Infinity;
  for (const stage of list) {
    const pct = Number(stage.percentage);
    if (!Number.isFinite(pct) || pct <= 0 || pct > 100) return 'Percentages are 0.01 to 100.';
    if (pct <= lastPct) return 'Percentages must increase.';
    const time = stage.startsUtc ? Date.parse(stage.startsUtc) : Number(stage.hoursAfterPublish) * 3600000;
    if (!Number.isFinite(time)) return 'Every stage needs a start time.';
    if (time < lastTime) return 'Stages must be in time order.';
    lastPct = pct;
    lastTime = time;
  }
  return null;
}

/**
 * Whether a group may be promoted to another channel (never from development, never a draft or a rollback to the
 * built-in bundle).
 * @param {object} group UpdateGroup.
 */
export function canPromote(group) {
  return Boolean(group) && group.channel !== 'development' && group.kind !== UpdateGroupKind.RollBackToEmbedded && group.status !== UpdateGroupStatus.Draft;
}

/** Whether a group's rollout can change (Published or Paused). */
export function hasRollout(group) {
  return Boolean(group) && (group.status === UpdateGroupStatus.Published || group.status === UpdateGroupStatus.Paused);
}

/**
 * The signing key rotation checklist (resolved decision 14 and client implementation note 3), computed from the
 * signing key enumeration. Steps: a standby key is registered with its private key; it is issued by the active key's
 * certificate, which is a CA; switch signing to it; ship store builds that embed the next certificate; retire the old
 * key once installs on it drain.
 * @param {object[]} keys UpdateSigningKey objects.
 * @param {{ objects?: { key: string, count: number }[] }} [installsByKeyId]
 * @returns {{ id: string, done: boolean }[]}
 */
export function rotationSteps(keys, installsByKeyId) {
  const list = keys || [];
  const active = list.find((k) => k.status === SigningKeyStatus.Active) || null;
  const standby = list.find((k) => k.status === SigningKeyStatus.Standby) || null;
  const anchor = standby && list.find((k) => k.keyId === standby.issuedByKeyId);
  const retired = list.some((k) => k.status === SigningKeyStatus.Retired);
  const drained = active && (installsByKeyId?.objects || []).every((row) => row.key === active.keyId || row.key === '' || Number(row.count) === 0);
  return [
    { id: 'standby', done: Boolean(standby && standby.privateKeyAvailable) || Boolean(active && retired) },
    { id: 'chain', done: Boolean(anchor && anchor.isCertificateAuthority) || Boolean(active && active.issuedByKeyId) },
    { id: 'activate', done: Boolean(active && active.issuedByKeyId) },
    { id: 'ship', done: false },
    { id: 'retire', done: Boolean(retired && drained) }
  ];
}

/** @this {import('./ApiClient.js').ApiClient} */
export const updatesMethods = {
  // ---- Update groups (section 10.3.1) ----

  /** POST /admin/update-groups/enumerate; filters platform, channel, runtimeVersion, status, kind, critical, gitCommit. */
  enumerateUpdateGroups(query = {}, signal) { return this.post(GROUPS + '/enumerate', body(query), { signal }); },
  /** GET /admin/update-groups/{id}: detail with updates, plan, auto-halt counts, adoption, and an asset preview. */
  getUpdateGroup(id, signal) { return this.get(GROUPS + '/' + enc(id), { signal }); },
  /** GET /admin/update-groups/{id}/adoption?days= (1 to 90). */
  getUpdateGroupAdoption(id, query = {}, signal) { return this.post(GROUPS + '/' + enc(id) + '/adoption/enumerate', body(query), { signal }); },
  getUpdateGroupBinaryVersions(id, query = {}, signal) { return this.post(GROUPS + '/' + enc(id) + '/adoption/binary-versions/enumerate', body(query), { signal }); },
  /** POST /admin/updates/{updateId}/assets/enumerate. */
  enumerateUpdateAssets(updateId, query = {}, signal) { return this.post(UPDATES + '/' + enc(updateId) + '/assets/enumerate', body(query), { signal }); },
  /** POST /admin/update-groups/{id}/publish `{ rolloutPercentage?, supersede? }`. */
  publishUpdateGroup(id, { rolloutPercentage, supersede = false } = {}) { return this.post(GROUPS + '/' + enc(id) + '/publish', { rolloutPercentage, supersede }); },
  /** POST /admin/update-groups/{id}/rollout `{ rolloutPercentage, platform? }`. */
  setUpdateRollout(id, rolloutPercentage, platform) { return this.post(GROUPS + '/' + enc(id) + '/rollout', { rolloutPercentage, platform: platform || undefined }); },
  /** POST /admin/update-groups/{id}/pause `{ reason? }`. */
  pauseUpdateGroup(id, reason) { return this.post(GROUPS + '/' + enc(id) + '/pause', { reason: reason || undefined }); },
  /** POST /admin/update-groups/{id}/resume `{ reason? }` (also clears an automatic halt). */
  resumeUpdateGroup(id, reason) { return this.post(GROUPS + '/' + enc(id) + '/resume', { reason: reason || undefined }); },
  /** POST /admin/update-groups/{id}/critical `{ critical, criticalMessage? }`. */
  setUpdateCritical(id, critical, criticalMessage) { return this.post(GROUPS + '/' + enc(id) + '/critical', { critical: Boolean(critical), criticalMessage: criticalMessage || undefined }); },
  /** POST /admin/update-groups/{id}/rollback `{ targetUpdateGroupId, rolloutPercentage?, message? }`. */
  rollbackUpdateGroup(id, { targetUpdateGroupId, rolloutPercentage, message } = {}) { return this.post(GROUPS + '/' + enc(id) + '/rollback', { targetUpdateGroupId, rolloutPercentage, message }); },
  /** POST /admin/update-groups/rollback-to-embedded `{ platform, runtimeVersion, channel, rolloutPercentage?, message? }`. */
  rollbackToEmbedded(request) { return this.post(GROUPS + '/rollback-to-embedded', request); },
  /** POST /admin/update-groups/{id}/promote `{ channel, rolloutPercentage?, message?, supersede? }`. */
  promoteUpdateGroup(id, { channel, rolloutPercentage, message, supersede = false } = {}) { return this.post(GROUPS + '/' + enc(id) + '/promote', { channel, rolloutPercentage, message, supersede }); },
  /** PUT /admin/update-groups/{id}/rollout-plan `{ stages?, useTemplate?, autoHaltEnabled?, autoHaltFailurePercent?, autoHaltMinInstalls? }`. */
  setUpdateRolloutPlan(id, plan) { return this.put(GROUPS + '/' + enc(id) + '/rollout-plan', plan || {}); },
  /** DELETE /admin/update-groups/{id} (Drafts only). */
  deleteUpdateGroup(id) { return this.delete(GROUPS + '/' + enc(id)); },

  // ---- Channels, runtime versions, keys, health (sections 6.3, 7.4, 7.5, 10.1) ----

  /** POST /admin/updates/channels/enumerate. */
  enumerateUpdateChannels(query = {}, signal) { return this.post(UPDATES + '/channels/enumerate', body(query), { signal }); },
  /** POST /admin/updates/channels `{ name }`. */
  createUpdateChannel(name) { return this.post(UPDATES + '/channels', { name }); },
  /** DELETE /admin/updates/channels/{name}. */
  deleteUpdateChannel(name) { return this.delete(UPDATES + '/channels/' + enc(name)); },
  /** POST /admin/updates/runtime-versions/enumerate; filters platform, channel. */
  enumerateRuntimeVersions(query = {}, signal) { return this.post(UPDATES + '/runtime-versions/enumerate', body(query), { signal }); },
  /** POST /admin/updates/deploy-keys/enumerate; filter active. */
  enumerateDeployKeys(query = {}, signal) { return this.post(UPDATES + '/deploy-keys/enumerate', body(query), { signal }); },
  /** POST /admin/updates/deploy-keys `{ name, channels, expiresUtc?, labels?, tags? }`: the secret is returned once. */
  createDeployKey(request) { return this.post(UPDATES + '/deploy-keys', request); },
  /** POST /admin/updates/deploy-keys/{id}/revoke. */
  revokeDeployKey(id) { return this.post(UPDATES + '/deploy-keys/' + enc(id) + '/revoke', {}); },
  /** POST /admin/updates/signing-keys/enumerate; filter status. */
  enumerateSigningKeys(query = {}, signal) { return this.post(UPDATES + '/signing-keys/enumerate', body(query), { signal }); },
  /** POST /admin/updates/signing-keys `{ keyId, certificatePem, privateKeyPem?, status? }` (private keys are write-only). */
  registerSigningKey(request) { return this.post(UPDATES + '/signing-keys', request); },
  /** POST /admin/updates/signing-keys/{id}/activate (member session). */
  activateSigningKey(id) { return this.post(UPDATES + '/signing-keys/' + enc(id) + '/activate', {}); },
  /** POST /admin/updates/signing-keys/{id}/retire. */
  retireSigningKey(id) { return this.post(UPDATES + '/signing-keys/' + enc(id) + '/retire', {}); },
  /** POST /admin/updates/signing-keys/installs/enumerate: active-install counts by embedded key. */
  getInstallsByKeyId(query = {}, signal) { return this.post(UPDATES + '/signing-keys/installs/enumerate', body(query), { signal }); },
  /** GET /admin/updates/health. */
  getUpdatesHealth(signal) { return this.get(UPDATES + '/health', { signal }); },

  // ---- Feature flags (section 12.4) ----

  /** POST /admin/app/flags/enumerate; filters kind, state, hasDraftChanges, client. */
  enumerateFlags(query = {}, signal) { return this.post(APP + '/flags/enumerate', body(query), { signal }); },
  /** POST /admin/app/flags (draft). */
  createFlag(flag) { return this.post(APP + '/flags', flag); },
  /** GET /admin/app/flags/{id}: `{ flag, published, history }`. */
  getFlag(id, signal) { return this.get(APP + '/flags/' + enc(id), { signal }); },
  /** PUT /admin/app/flags/{id} with If-Match (the flag's eTag). */
  updateFlag(id, changes, etag) { return this.request('PUT', APP + '/flags/' + enc(id), { body: changes, headers: etag ? { 'If-Match': etag } : undefined }); },
  /** POST /admin/app/flags/{id}/archive. */
  archiveFlag(id) { return this.post(APP + '/flags/' + enc(id) + '/archive', {}); },
  /** POST /admin/app/flags/{id}/restore. */
  restoreFlag(id) { return this.post(APP + '/flags/' + enc(id) + '/restore', {}); },
  /** DELETE /admin/app/flags/{id} (never published only). */
  deleteFlag(id) { return this.delete(APP + '/flags/' + enc(id)); },
  /** POST /admin/app/flags/{id}/evaluate `{ as, useDraft }`. */
  evaluateFlag(id, as, useDraft = true) { return this.post(APP + '/flags/' + enc(id) + '/evaluate', { as: as || {}, useDraft }); },
  /** POST /admin/app/flags/{id}/history/enumerate. */
  enumerateFlagHistory(id, query = {}, signal) { return this.post(APP + '/flags/' + enc(id) + '/history/enumerate', body(query), { signal }); },

  // ---- Content publishing (section 16) ----

  /** POST /admin/app/drafts/enumerate: draft page with contentVersion and draft counts. */
  getContentDrafts(query = {}, signal) { return this.post(APP + '/drafts/enumerate', body(query), { signal }); },
  /** POST /admin/app/drafts/discard `{ objectIds? }`. */
  discardContentDrafts(objectIds) { return this.post(APP + '/drafts/discard', objectIds ? { objectIds } : {}); },
  /** POST /admin/app/publish `{ message, expectedVersion }`. */
  publishContent(message, expectedVersion) { return this.post(APP + '/publish', { message, expectedVersion }); },
  /** POST /admin/app/versions/enumerate. */
  enumerateContentVersions(query = {}, signal) { return this.post(APP + '/versions/enumerate', body(query), { signal }); },
  /** GET /admin/app/versions/{version}. */
  getContentVersion(version, signal) { return this.get(APP + '/versions/' + enc(version), { signal }); },
  /** POST /admin/app/versions/{version}/restore `{ message? }`. */
  restoreContentVersion(version, message) { return this.post(APP + '/versions/' + enc(version) + '/restore', { message: message || undefined }); },

  // ---- Kill switches (section 13.3) ----

  /** POST /admin/app/kill-switches/enumerate; filter killed. */
  enumerateKillSwitches(query = {}, signal) { return this.post(APP + '/kill-switches/enumerate', body(query), { signal }); },
  /** PUT /admin/app/kill-switches/{featureKey} `{ killed, message?, rules?, blocksServer?, reason }`. */
  setKillSwitch(featureKey, { killed, message, rules, blocksServer, reason } = {}) {
    return this.put(APP + '/kill-switches/' + enc(featureKey), { killed: Boolean(killed), message: message || undefined, rules: rules || undefined, blocksServer, reason });
  },

  // ---- App manifest (section 11.1) ----

  /** GET /app/manifest?since= (anonymous or signed in). */
  getAppManifest({ since, headers } = {}, signal) { return this.get('/app/manifest', { query: { since }, headers, signal }); },
  /** POST /app/strings/enumerate (full refresh); filter client. */
  enumerateAppStrings(query = {}, { headers } = {}, signal) { return this.post('/app/strings/enumerate', toEnumerationBody(query || {}), { headers, signal }); },
  /** POST /app/banners/enumerate (full refresh). */
  enumerateAppBanners(query = {}, { headers } = {}, signal) { return this.post('/app/banners/enumerate', toEnumerationBody(query || {}), { headers, signal }); },
  /** POST /app/assets/enumerate (full refresh). */
  enumerateAppAssets(query = {}, { headers } = {}, signal) { return this.post('/app/assets/enumerate', toEnumerationBody(query || {}), { headers, signal }); },

  // ---- Remote strings (section 14.3) ----

  /** POST /admin/app/strings/enumerate: catalog keys joined with overrides; filters client, overridden, hasDraftChanges. */
  enumerateRemoteStrings(query = {}, signal) { return this.post(APP + '/strings/enumerate', body(query), { signal }); },
  /** GET /admin/app/strings/{key}: `{ string, history }`. */
  getRemoteString(key, signal) { return this.get(APP + '/strings/' + enc(key), { signal }); },
  /** PUT /admin/app/strings/{key} `{ value, notes?, labels?, tags? }` (draft upsert). */
  putRemoteString(key, { value, notes, labels, tags } = {}) { return this.put(APP + '/strings/' + enc(key), { value, notes: notes || undefined, labels, tags }); },
  /** DELETE /admin/app/strings/{key} (draft removal; a published override is removed at the next publish). */
  deleteRemoteString(key) { return this.delete(APP + '/strings/' + enc(key)); },
  /** POST /admin/app/strings/{key}/history/enumerate. */
  enumerateRemoteStringHistory(key, query = {}, signal) { return this.post(APP + '/strings/' + enc(key) + '/history/enumerate', body(query), { signal }); },

  // ---- Banners (section 14.1) ----

  /** POST /admin/app/banners/enumerate; filters active, placement, style, state, hasDraftChanges. */
  enumerateBanners(query = {}, signal) { return this.post(APP + '/banners/enumerate', body(query), { signal }); },
  /** POST /admin/app/banners (draft). */
  createBanner(banner) { return this.post(APP + '/banners', banner); },
  /** GET /admin/app/banners/{id}: `{ banner, published, history }`. */
  getBanner(id, signal) { return this.get(APP + '/banners/' + enc(id), { signal }); },
  /** PUT /admin/app/banners/{id} with If-Match (the banner's eTag). */
  updateBanner(id, changes, etag) { return this.request('PUT', APP + '/banners/' + enc(id), { body: changes, headers: etag ? { 'If-Match': etag } : undefined }); },
  /** POST /admin/app/banners/{id}/archive. */
  archiveBanner(id) { return this.post(APP + '/banners/' + enc(id) + '/archive', {}); },
  /** POST /admin/app/banners/{id}/restore. */
  restoreBanner(id) { return this.post(APP + '/banners/' + enc(id) + '/restore', {}); },
  /** DELETE /admin/app/banners/{id} (never published only). */
  deleteBanner(id) { return this.delete(APP + '/banners/' + enc(id)); },
  /** POST /admin/app/banners/{id}/history/enumerate. */
  enumerateBannerHistory(id, query = {}, signal) { return this.post(APP + '/banners/' + enc(id) + '/history/enumerate', body(query), { signal }); },

  // ---- Remote assets (section 14.2) ----

  /** POST /admin/app/assets/enumerate; filters state, hasDraftChanges. */
  enumerateRemoteAssets(query = {}, signal) { return this.post(APP + '/assets/enumerate', body(query), { signal }); },
  /** POST /admin/app/assets `{ key, altText?, labels?, tags? }` (draft). */
  createRemoteAsset(asset) { return this.post(APP + '/assets', asset); },
  /** GET /admin/app/assets/{id}: `{ asset, published, history }`. */
  getRemoteAsset(id, signal) { return this.get(APP + '/assets/' + enc(id), { signal }); },
  /** PUT /admin/app/assets/{id} `{ key?, altText?, labels?, tags? }` with If-Match. */
  updateRemoteAsset(id, changes, etag) { return this.request('PUT', APP + '/assets/' + enc(id), { body: changes, headers: etag ? { 'If-Match': etag } : undefined }); },
  /** PUT /admin/app/assets/{id}/content (or /dark-content with `dark`): the raw image bytes. */
  uploadRemoteAssetContent(id, data, contentType, { dark = false } = {}) {
    return this.request('PUT', APP + '/assets/' + enc(id) + (dark ? '/dark-content' : '/content'), { rawBody: data, contentType: contentType || 'image/png' });
  },
  /** POST /admin/app/assets/{id}/archive. */
  archiveRemoteAsset(id) { return this.post(APP + '/assets/' + enc(id) + '/archive', {}); },
  /** POST /admin/app/assets/{id}/restore. */
  restoreRemoteAsset(id) { return this.post(APP + '/assets/' + enc(id) + '/restore', {}); },
  /** DELETE /admin/app/assets/{id} (never published only). */
  deleteRemoteAsset(id) { return this.delete(APP + '/assets/' + enc(id)); },
  /** POST /admin/app/assets/{id}/history/enumerate. */
  enumerateRemoteAssetHistory(id, query = {}, signal) { return this.post(APP + '/assets/' + enc(id) + '/history/enumerate', body(query), { signal }); },

  // ---- Launch failures and symbolication ----

  /** POST /admin/updates/{updateId}/failures/enumerate: reported failures of one platform's update; filter type. */
  enumerateUpdateFailures(updateId, query = {}, signal) { return this.post(UPDATES + '/' + enc(updateId) + '/failures/enumerate', body(query), { signal }); },
  /** POST /admin/updates/{updateId}/symbolicate `{ stackTrace }` or `{ eventId }`: server-side symbolication with the private source maps. */
  symbolicateUpdate(updateId, { stackTrace, eventId } = {}) { return this.post(UPDATES + '/' + enc(updateId) + '/symbolicate', eventId ? { eventId } : { stackTrace: String(stackTrace || '') }); }
};

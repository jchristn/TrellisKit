/**
 * Profile field, member directory, administrative email, property settings, and property domain
 * API methods (REST_API.md "Profile fields", "Administrative email", "Admin: property domains"),
 * mixed into `ApiClient.prototype`. Every method maps to exactly one route. Lists send the
 * canonical `POST .../enumerate` form with an `EnumerationQuery` and resolve an `EnumerationResult`.
 */

import { ENUMERATION_MAX_RESULTS_ADMIN, toEnumerationBody } from './enumeration.js';

const enc = encodeURIComponent;
const ADMIN = { maxLimit: ENUMERATION_MAX_RESULTS_ADMIN };
const FIELDS = '/admin/profile-fields';
const EMAIL = '/admin/email';

/** `POST /admin/profile-fields/preview` targets and viewers. */
export const ProfilePreviewTarget = Object.freeze({ Onboarding: 'Onboarding', ProfilePage: 'ProfilePage', Card: 'Card', Editor: 'Editor' });
export const ProfileViewer = Object.freeze({ Anonymous: 'Anonymous', SignedIn: 'SignedIn', Member: 'Member', Self: 'Self', Staff: 'Staff' });

/** Orderings of the profile field collection. */
export const ProfileFieldOrderings = Object.freeze(['ProfileOrderAscending', 'CreatedDescending', 'CreatedAscending', 'LabelAscending']);

/** Orderings of the member directory. */
export const MemberDirectoryOrderings = Object.freeze(['CreatedDescending', 'CreatedAscending', 'DisplayNameAscending']);

/** Email enumerations (REST_API.md "Email admin"). */
export const EmailStatus = Object.freeze({ Queued: 'Queued', Sent: 'Sent', Delivered: 'Delivered', Failed: 'Failed', Suppressed: 'Suppressed' });
export const EmailCategory = Object.freeze({ Essential: 'Essential', Optional: 'Optional', Staff: 'Staff' });
export const EmailSuppressionReason = Object.freeze({ Bounce: 'Bounce', Complaint: 'Complaint', Unsubscribed: 'Unsubscribed', Manual: 'Manual' });
export const EmailProvider = Object.freeze({ Auto: 'Auto', Mailgun: 'Mailgun', Smtp: 'Smtp', Log: 'Log' });
export const EmailMessageOrderings = Object.freeze(['CreatedDescending', 'CreatedAscending', 'UpdatedDescending']);
export const EmailSuppressionOrderings = Object.freeze(['CreatedDescending', 'CreatedAscending', 'EmailAscending']);

/** Template override limits (one line of at most 200 for subject, preheader, heading; body 5000). */
export const EMAIL_TEMPLATE_LINE_MAX = 200;
export const EMAIL_TEMPLATE_BODY_MAX = 5000;

/** Domain `source` values. */
export const DomainSource = Object.freeze({ Platform: 'Platform', Property: 'Property' });

/**
 * The `{token}` placeholders of a template text that are not in `allowed` (the template's
 * `tokens`), in order of first use. The server rejects them; this gives instant feedback.
 * @param {string} text
 * @param {string[]} allowed
 */
export function unknownTemplateTokens(text, allowed) {
  const known = new Set(allowed || []);
  const out = [];
  for (const match of String(text || '').matchAll(/\{([A-Za-z][A-Za-z0-9_]*)\}/g)) {
    if (!known.has(match[1]) && !out.includes(match[1])) out.push(match[1]);
  }
  return out;
}

/** Verification state of a domain view: `Verified`, `Pending` (a TXT challenge exists), or `Unverified`. */
export function domainVerificationState(domain) {
  if (domain && domain.verifiedUtc) return 'Verified';
  if (domain && domain.verification) return 'Pending';
  return 'Unverified';
}

function brandingVersionBody(version, note) {
  const body = { to: 'Version', version: Math.floor(Number(version)) };
  if (note) body.note = note;
  return body;
}

function adminBody(query) {
  return toEnumerationBody(query || {}, ADMIN);
}

/** @this {import('./ApiClient.js').ApiClient} */
export const propertyAdminMethods = {
  // ---- Profile field definitions (SystemAdmin) ----

  /** POST /admin/profile-fields/enumerate (every definition, built-ins included; at most 104). */
  listProfileFields(query, signal) {
    return this.post(FIELDS + '/enumerate', adminBody({ maxResults: 200, ordering: 'ProfileOrderAscending', ...(query || {}) }), { signal });
  },
  /** GET /admin/profile-fields/{id}. */
  getProfileField(id, signal) { return this.get(FIELDS + '/' + enc(id), { signal }); },
  /** POST /admin/profile-fields: `201 ProfileField`. */
  createProfileField(body) { return this.post(FIELDS, body); },
  /** PUT /admin/profile-fields/{id} with any attributes (options and subFields replace their lists). */
  updateProfileField(id, body) { return this.put(FIELDS + '/' + enc(id), body); },
  /**
   * DELETE /admin/profile-fields/{id}. A field with values needs `deleteValues: true` and the
   * field key as `confirm` (202); 204 when it had none.
   */
  deleteProfileField(id, { deleteValues = false, confirm = null } = {}) {
    return this.request('DELETE', FIELDS + '/' + enc(id), {
      query: deleteValues ? { deleteValues: 'true' } : undefined,
      body: confirm ? { confirm } : undefined
    });
  },
  /** POST /admin/profile-fields/reorder `{ order }`: every field id exactly once. */
  reorderProfileFields(order) { return this.post(FIELDS + '/reorder', { order }); },
  /** POST /admin/profile-fields/preview `{ target, viewer, fields?, values? }`. */
  previewProfileFields(body, signal) { return this.post(FIELDS + '/preview', body, { signal }); },
  /** POST /admin/profile-fields/{id}/stats/enumerate: option-count rows with scalar profile totals. */
  getProfileFieldStats(id, query = {}, signal) { return this.post(FIELDS + '/' + enc(id) + '/stats/enumerate', adminBody(query), { signal }); },
  /** PUT /admin/members/{id}/profile `{ profile }`: `200` admin member detail. */
  updateMemberProfile(id, profile) { return this.put('/admin/members/' + enc(id) + '/profile', { profile }); },
  /** POST /admin/members/enumerate (accepts `filters.profile`). */
  enumerateAdminMembers(query, signal) { return this.post('/admin/members/enumerate', adminBody(query), { signal }); },

  // ---- Member directory (Member) ----

  /**
   * POST /members/directory/enumerate: `EnumerationResult<UserSummary>` of active members, with
   * `filters.profile.<key>.<operator>` on filterable fields the caller can see.
   */
  enumerateMemberDirectory(query, signal) { return this.post('/members/directory/enumerate', toEnumerationBody(query || {}), { signal }); },

  // ---- Property settings and integrations (SystemAdmin) ----

  /** POST /admin/property/settings/enumerate: property setting rows (secrets never returned; `isSet`). */
  getServerSettings(query = {}, signal) { return this.post('/settings/enumerate', adminBody(query), { signal }); },
  getPropertySettings(query = {}, signal) { return this.post('/admin/property/settings' + '/enumerate', adminBody(query), { signal }); },
  /** GET /admin/property/access: the stored access policy (ACCESS_MODELS_CONTRACT.md section 6). */
  getPropertyAccess(signal) { return this.get('/admin/property/access', { signal }); },
  /** PUT /admin/property/access `{ model?, anonymousBrowsing?, gatedFeatures?, defaultAudience? }` (audited property.access.update). */
  updatePropertyAccess(body) { return this.put('/admin/property/access', body); },
  /** GET /admin/property/branding: the brand palette with contrast checks and presets (TRELLISKIT.md section 2.1). */
  getPropertyBranding(signal) { return this.get('/admin/property/branding', { signal }); },
  /** PUT /admin/property/branding `{ light?, dark?, contrastOverrides?, note? }` (audited property.branding.update). */
  updatePropertyBranding(body) { return this.put('/admin/property/branding', body || {}); },
  /** POST /admin/property/branding/revert `{ to: 'Previous' | 'Seed', note? }` (audited property.branding.revert). */
  revertPropertyBranding(to, note) { return this.post('/admin/property/branding/revert', note ? { to, note } : { to }); },
  /**
   * POST /admin/property/branding/versions/enumerate: every kept palette version (newest first),
   * `EnumerationResult<{ version, source, note, createdUtc, light, dark, isCurrent, labels, tags }>`.
   */
  enumeratePropertyBrandingVersions(query = {}, signal) { return this.post('/admin/property/branding/versions/enumerate', adminBody(query), { signal }); },
  /** POST /admin/property/branding/revert `{ to: 'Version', version, note? }`: restore any kept version as a new version. */
  restorePropertyBrandingVersion(version, note) { return this.post('/admin/property/branding/revert', brandingVersionBody(version, note)); },
  /** PUT /admin/property/branding/member-presets `{ mode: 'All' | 'None' | 'Selected', presetIds? }` (audited property.branding.memberPresets). */
  updatePropertyMemberPresets(body) { return this.put('/admin/property/branding/member-presets', body || {}); },
  /** GET /admin/property/ai: `{ enabled, deploymentEnabled, available, seedEnabled, syncsFromFolder, revision, ... }`. */
  getPropertyAi(signal) { return this.get('/admin/property/ai', { signal }); },
  /** PUT /admin/property/ai `{ enabled }` (audited property.ai.update; applies without a restart). */
  updatePropertyAi(enabled) { return this.put('/admin/property/ai', { enabled: Boolean(enabled) }); },
  /** PUT /admin/property/settings `{ values, clear }` (all or nothing). */
  updatePropertySettings({ values = {}, clear = [] } = {}) { return this.put('/admin/property/settings', { values, clear }); },
  /** POST /admin/property/integrations/{name}/test `{ to? }`: `{ ok, message }`. */
  testIntegration(name, to) { return this.post('/admin/property/integrations/' + enc(name) + '/test', to ? { to } : {}); },

  // ---- Property domains (SystemAdmin) ----

  /** Enumerate property domains or application identities; each response includes propertyId. */
  getPropertyApps(query = {}, signal) { return this.post('/admin/property/apps/enumerate', adminBody(query), { signal }); },
  getPropertyDomains(query = {}, signal) { return this.post('/admin/property/domains' + '/enumerate', adminBody(query), { signal }); },
  /** PUT /admin/property/domains `{ domains?, apps? }` (a provided list replaces the stored one). */
  updatePropertyDomains(body) { return this.put('/admin/property/domains', body); },
  /** POST /admin/property/domains/{domainId}/verify: the domain view. */
  verifyPropertyDomain(domainId) { return this.post('/admin/property/domains/' + enc(domainId) + '/verify'); },

  // ---- Administrative email (SystemAdmin) ----

  /** POST /admin/email/templates/enumerate: template page, searchable and filterable by category. */
  listEmailTemplates(query = {}, signal) { return this.post(EMAIL + '/templates' + '/enumerate', adminBody(query), { signal }); },
  /** GET /admin/email/templates/{key}. */
  getEmailTemplate(key, signal) { return this.get(EMAIL + '/templates/' + enc(key), { signal }); },
  /** PUT /admin/email/templates/{key} `{ subject?, preheader?, heading?, body? }`. */
  updateEmailTemplate(key, body) { return this.put(EMAIL + '/templates/' + enc(key), body); },
  /** DELETE /admin/email/templates/{key} (engine default again). */
  resetEmailTemplate(key) { return this.delete(EMAIL + '/templates/' + enc(key)); },
  /** POST /admin/email/templates/{key}/preview: `{ subject, preheader, html, text }`. */
  previewEmailTemplate(key, body, signal) { return this.post(EMAIL + '/templates/' + enc(key) + '/preview', body || {}, { signal }); },
  /** POST /admin/email/test `{ template?, to? }`: `{ ok, message }`. */
  sendTestEmail({ template, to } = {}) {
    const body = {};
    if (template) body.template = template;
    if (to) body.to = to;
    return this.post(EMAIL + '/test', body);
  },
  /** POST /admin/email/test/catalog: send the fixed-bound catalog with sample data. */
  sendTestEmailCatalog({ to } = {}) { return this.post(EMAIL + '/test/catalog', to ? { to } : {}); },
  /** POST /admin/email/staff-events/enumerate: configured staff-event page. */
  listEmailStaffEvents(query = {}, signal) { return this.post(EMAIL + '/staff-events' + '/enumerate', adminBody(query), { signal }); },
  /** PUT /admin/email/staff-events/{template} `{ enabled?, recipients? }`. */
  updateEmailStaffEvent(template, body) { return this.put(EMAIL + '/staff-events/' + enc(template), body); },
  /** POST /admin/email/messages/enumerate; filters `status`, `template`, `category`, `recipient`, `userId`. */
  listEmailMessages(query, signal) { return this.post(EMAIL + '/messages/enumerate', adminBody(query), { signal }); },
  /** GET /admin/email/messages/{id}. */
  getEmailMessage(id, signal) { return this.get(EMAIL + '/messages/' + enc(id), { signal }); },
  /** POST /admin/email/suppressions/enumerate; filter `reason`. */
  listEmailSuppressions(query, signal) { return this.post(EMAIL + '/suppressions/enumerate', adminBody(query), { signal }); },
  /** POST /admin/email/suppressions `{ email, note?, labels?, tags? }`. */
  addEmailSuppression(body) { return this.post(EMAIL + '/suppressions', body); },
  /** DELETE /admin/email/suppressions/{id}. */
  removeEmailSuppression(id) { return this.delete(EMAIL + '/suppressions/' + enc(id)); }
};

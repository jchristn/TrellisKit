import { ApiError, collectEnumeration, metricSeriesFromEnumeration, legacyMetricSeriesFromEnumeration, usageSeriesFromEnumeration } from '@trelliskit/shared';

/**
 * Admin API methods built on the shared ApiClient. Views call these named methods instead of
 * building URLs. Raw `fetch` use is limited to `getOpenApiSpec` and `executeExplorer`, which
 * need paths outside `/api/v1` or the raw Response.
 *
 * @param {import('@trelliskit/shared').ApiClient} client
 * @param {{ onUnauthorized?: () => void }} [options]
 */
export function createAdminApi(client, { onUnauthorized } = {}) {
  const enc = encodeURIComponent;

  async function call(fn) {
    try {
      return await fn();
    } catch (err) {
      if (err instanceof ApiError && err.status === 401 && onUnauthorized) onUnauthorized();
      throw err;
    }
  }

  async function authHeaders() {
    return client.baseHeaders();
  }

  return {
    client,
    metricRegistry: (signal) => call(() => collectEnumeration((q, sig) => client.adminMetricRegistry(q, sig), {}, { signal })),
    metricPages: (signal) => call(() => collectEnumeration((q, sig) => client.adminMetricPages(q, sig), {}, { signal })),
    queryMetric: (query, signal) => call(async () => { const [points, annotations] = await Promise.all([collectEnumeration((q, sig) => client.adminQueryMetrics({ ...q, query }, sig), { maxResults: 100 }, { signal, maxPages: 1000 }), collectEnumeration((q, sig) => client.adminMetricAnnotations(q, sig), { createdAfterUtc: query.from, createdBeforeUtc: query.to }, { signal })]); return metricSeriesFromEnumeration(points, annotations.objects); }),
    metricWorkers: (query, signal) => call(() => client.adminMetricWorkers(query, signal)),

    /** GET /health (anonymous). */
    getHealth: (signal) => client.health(signal),

    /** GET /settings. Used to validate the admin token. */
    getSettings: (signal) => call(async () => { const page = await collectEnumeration((q, sig) => client.getServerSettings(q, sig), {}, { signal }); return { ...page, fields: page.objects }; }),

    /** PUT /settings with only changed keys. */
    updateSettings: (values) => call(() => client.put('/settings', { values })),

    /** POST /settings/restart. */
    restartServer: () => call(() => client.post('/settings/restart', {})),

    /** GET /request-history with filters and paging. */
    getRequestHistory: (query, signal) => call(() => client.get('/request-history', { query, signal })),

    /** GET /request-history/summary. */
    getRequestHistorySummary: (filters, signal) => call(async () => { const page = await collectEnumeration((q, sig) => client.adminRequestHistorySummary(q, sig), { filters, maxResults: 100 }, { signal }); return { ...page, buckets: page.objects }; }),

    /** GET /request-history/{id}. */
    getRequestHistoryEntry: (id, signal) => call(() => client.get('/request-history/' + encodeURIComponent(id), { signal })),

    /** DELETE /request-history/{id}. */
    deleteRequestHistoryEntry: (id) => call(() => client.delete('/request-history/' + encodeURIComponent(id))),

    /** DELETE /request-history with filters. */
    deleteRequestHistoryBulk: (query) => call(() => client.delete('/request-history', { query })),

    /** GET /admin/members (paged, filtered, sorted). */
    listMembers: (query, signal) => call(() => client.get('/admin/members', { query, signal })),
    /** GET /admin/members/{id}. */
    getMember: (id, signal) => call(() => client.get('/admin/members/' + enc(id), { signal })),
    /** PUT /admin/members/{id}/flags. */
    updateMemberFlags: (id, flags) => call(() => client.put('/admin/members/' + enc(id) + '/flags', flags)),
    /** PUT /admin/members/{id}/roles. */
    updateMemberRoles: (id, roles) => call(() => client.put('/admin/members/' + enc(id) + '/roles', { roles })),
    /** POST /admin/members/{id}/comp. */
    grantComp: (id, body) => call(() => client.post('/admin/members/' + enc(id) + '/comp', body)),
    /** DELETE /admin/members/{id}/comp/{compId} with a reason. */
    endComp: (id, compId, reason) => call(() => client.request('DELETE', '/admin/members/' + enc(id) + '/comp/' + enc(compId), { body: { reason } })),
    /** POST /admin/members/{id}/ban. */
    banMember: (id, reason) => call(() => client.post('/admin/members/' + enc(id) + '/ban', { reason })),
    /** POST /admin/members/{id}/unban. */
    unbanMember: (id) => call(() => client.post('/admin/members/' + enc(id) + '/unban')),
    /** POST /admin/members/{id}/mute. */
    muteMember: (id, untilUtc, reason) => call(() => client.post('/admin/members/' + enc(id) + '/mute', { untilUtc, reason })),
    /** POST /admin/members/{id}/unmute. */
    unmuteMember: (id) => call(() => client.post('/admin/members/' + enc(id) + '/unmute')),
    /** DELETE /admin/members/{id} with the typed confirmation and a reason. */
    deleteMember: (id, reason) => call(() => client.request('DELETE', '/admin/members/' + enc(id), { body: { confirm: 'DELETE', reason } })),
    /** DELETE /admin/members/{id}/sessions. */
    revokeMemberSessions: (id) => call(() => client.delete('/admin/members/' + enc(id) + '/sessions')),

    /** GET /admin/subscriptions. */
    listSubscriptions: (query, signal) => call(() => client.get('/admin/subscriptions', { query, signal })),
    /** GET /admin/subscriptions/{id}. */
    getSubscription: (id, signal) => call(() => client.get('/admin/subscriptions/' + enc(id), { signal })),
    /** GET /admin/metrics/membership. */
    getMembershipMetrics: (signal) => call(() => client.get('/admin/metrics/membership', { signal })),

    /** GET /admin/signup-codes. */
    listSignupCodes: (query, signal) => call(() => client.get('/admin/signup-codes', { query, signal })),
    /** POST /admin/signup-codes. */
    createSignupCode: (body) => call(() => client.post('/admin/signup-codes', body)),
    /** GET /admin/signup-codes/{id}. */
    getSignupCode: (id, signal) => call(() => client.get('/admin/signup-codes/' + enc(id), { signal })),
    /** PUT /admin/signup-codes/{id}. */
    updateSignupCode: (id, body) => call(() => client.put('/admin/signup-codes/' + enc(id), body)),
    /** POST /admin/signup-codes/{id}/deactivate. */
    deactivateSignupCode: (id) => call(() => client.post('/admin/signup-codes/' + enc(id) + '/deactivate')),
    deleteSignupCode: (id) => call(() => client.delete('/admin/signup-codes/' + enc(id))),
    /** POST /admin/signup-codes/batches. */
    createSignupCodeBatch: (body) => call(() => client.post('/admin/signup-codes/batches', body)),
    /** GET /admin/signup-codes/{id}/redemptions. */
    listCodeRedemptions: (id, query, signal) => call(() => client.get('/admin/signup-codes/' + enc(id) + '/redemptions', { query, signal })),
    /** POST /admin/signup-code-redemptions/{id}/revoke. */
    revokeRedemption: (redemptionId, reason) => call(() => client.post('/admin/signup-code-redemptions/' + enc(redemptionId) + '/revoke', { reason })),
    /** POST /admin/signup-codes/{id}/revoke-all. */
    revokeAllRedemptions: (id, reason) => call(() => client.post('/admin/signup-codes/' + enc(id) + '/revoke-all', { reason })),
    /** GET /admin/signup-codes/summary. */
    getSignupCodeSummary: (signal) => call(() => client.get('/admin/signup-codes/summary', { signal })),
    /** One page of GET /admin/signup-codes/export.csv as `{ text, headers }` (see `fetchCsvPages`). */
    exportSignupCodes: (query) => call(() => client.get('/admin/signup-codes/export.csv', { query, responseType: 'textWithHeaders' })),
    /** One page of GET /admin/signup-codes/{id}/redemptions/export.csv as `{ text, headers }`. */
    exportCodeRedemptions: (id, query) => call(() => client.get('/admin/signup-codes/' + enc(id) + '/redemptions/export.csv', { query, responseType: 'textWithHeaders' })),

    /** GET /admin/audit. */
    listAudit: (query, signal) => call(() => client.get('/admin/audit', { query, signal })),

    // ---- Phase 2 (docs/PHASE2_CONTRACT.md section 10); lists take enumeration GET params ----

    /** GET /admin/feed. */
    listAdminFeed: (query, signal) => call(() => client.get('/admin/feed', { query, signal })),
    /** GET /admin/feed/{id} (any state, with revisions, moderation actions, report summary). */
    getAdminFeedPost: (id, signal) => call(() => client.get('/admin/feed/' + enc(id), { signal })),
    /** POST /admin/feed (needs a member session; optional authorUserId). */
    createAdminFeedPost: (body) => call(() => client.post('/admin/feed', body)),
    /** PUT /admin/feed/{id}. */
    updateAdminFeedPost: (id, body) => call(() => client.put('/admin/feed/' + enc(id), body)),
    /** DELETE /admin/feed/{id}. */
    deleteAdminFeedPost: (id) => call(() => client.delete('/admin/feed/' + enc(id))),
    /** POST /admin/feed/{id}/pin. */
    pinAdminFeedPost: (id) => call(() => client.post('/admin/feed/' + enc(id) + '/pin')),
    /** POST /admin/feed/{id}/unpin. */
    unpinAdminFeedPost: (id) => call(() => client.post('/admin/feed/' + enc(id) + '/unpin')),
    /** POST /admin/feed/{id}/publish-now. */
    publishFeedPostNow: (id) => call(() => client.post('/admin/feed/' + enc(id) + '/publish-now')),
    /** GET /admin/metrics/engagement?days=. */
    getEngagementMetrics: (days, signal) => call(async () => legacyMetricSeriesFromEnumeration(await collectEnumeration((q, sig) => client.adminEngagementMetrics(q, sig), { filters: { days }, maxResults: 100 }, { signal }))),

    /** GET /admin/moderation/queue. */
    listModerationQueue: (query, signal) => call(() => client.get('/admin/moderation/queue', { query, signal })),
    /** GET /admin/moderation/queue/{targetType}/{targetId}. */
    getModerationTarget: (targetType, targetId, signal) => call(() => client.get('/admin/moderation/queue/' + enc(targetType) + '/' + enc(targetId), { signal })),
    /** POST /admin/moderation/queue/AssistantMessage/{messageId}/snapshot/enumerate: the snapshot's messages, oldest first (SystemAdmin). */
    enumerateAssistantSnapshot: (messageId, query, signal) => call(() => client.post('/admin/moderation/queue/AssistantMessage/' + enc(messageId) + '/snapshot/enumerate', query || {}, { signal })),
    /** POST /admin/moderation/queue/{targetType}/{targetId}/dismiss. */
    dismissReports: (targetType, targetId, note) => call(() => client.post('/admin/moderation/queue/' + enc(targetType) + '/' + enc(targetId) + '/dismiss', note ? { note } : {})),
    /** POST /admin/moderation/actions. */
    moderate: (body) => call(() => client.post('/admin/moderation/actions', body)),
    /** GET /admin/moderation/actions. */
    listModerationActions: (query, signal) => call(() => client.get('/admin/moderation/actions', { query, signal })),
    /** GET /admin/moderation/content. */
    listModerationContent: (query, signal) => call(() => client.get('/admin/moderation/content', { query, signal })),

    /** GET /admin/moderators. */
    listModerators: (query, signal) => call(() => client.get('/admin/moderators', { query, signal })),
    /** POST /admin/moderators. */
    grantModerator: (userId, resourceType = 'Feed') => call(() => client.post('/admin/moderators', { userId, resourceType })),
    /** DELETE /admin/moderators/{userId}?resourceType=. */
    revokeModerator: (userId, resourceType = 'Feed') => call(() => client.delete('/admin/moderators/' + enc(userId), { query: { resourceType } })),

    /** GET /admin/moderation/word-filter. */
    listWordFilter: (query, signal) => call(() => client.get('/admin/moderation/word-filter', { query, signal })),
    /** POST /admin/moderation/word-filter. */
    createWordFilterTerm: (body) => call(() => client.post('/admin/moderation/word-filter', body)),
    /** PUT /admin/moderation/word-filter/{id}. */
    updateWordFilterTerm: (id, body) => call(() => client.put('/admin/moderation/word-filter/' + enc(id), body)),
    /** DELETE /admin/moderation/word-filter/{id}. */
    deleteWordFilterTerm: (id) => call(() => client.delete('/admin/moderation/word-filter/' + enc(id))),
    /** POST /admin/moderation/word-filter/import. */
    importWordFilter: (body) => call(() => client.post('/admin/moderation/word-filter/import', body)),
    /** POST /admin/moderation/word-filter/test. */
    testWordFilter: (text) => call(() => client.post('/admin/moderation/word-filter/test', { text })),

    /** GET /admin/media. */
    listAdminMedia: (query, signal) => call(() => client.get('/admin/media', { query, signal })),
    /** GET /admin/media/summary. */
    getMediaSummary: (signal) => call(() => client.get('/admin/media/summary', { signal })),
    /** GET /admin/media/{id}. */
    getAdminMedia: (id, signal) => call(() => client.get('/admin/media/' + enc(id), { signal })),
    /** PUT /admin/media/{id}. */
    updateAdminMedia: (id, body) => call(() => client.put('/admin/media/' + enc(id), body)),
    /** POST /admin/media/{id}/reprocess. */
    reprocessMedia: (id) => call(() => client.post('/admin/media/' + enc(id) + '/reprocess')),
    /** DELETE /admin/media/{id}. */
    deleteAdminMedia: (id) => call(() => client.delete('/admin/media/' + enc(id))),

    /** PUT /metadata/{targetType}/{targetId}. */
    putMetadata: (targetType, targetId, body) => call(() => client.putMetadata(targetType, targetId, body)),

    // ---- Multi-property part 2: profile fields, member profiles, email, property settings, domains ----

    /** POST /admin/profile-fields/enumerate (every definition, built-ins included, in profile order). */
    listProfileFields: (query, signal) => call(() => client.listProfileFields(query, signal)),
    /** GET /admin/profile-fields/{id}. */
    getProfileField: (id, signal) => call(() => client.getProfileField(id, signal)),
    /** POST /admin/profile-fields. */
    createProfileField: (body) => call(() => client.createProfileField(body)),
    /** PUT /admin/profile-fields/{id}. */
    updateProfileField: (id, body) => call(() => client.updateProfileField(id, body)),
    /** DELETE /admin/profile-fields/{id} (`deleteValues` and `confirm` when the field has values). */
    deleteProfileField: (id, options) => call(() => client.deleteProfileField(id, options)),
    /** POST /admin/profile-fields/reorder with every field id. */
    reorderProfileFields: (order) => call(() => client.reorderProfileFields(order)),
    /** POST /admin/profile-fields/preview. */
    previewProfileFields: (body, signal) => call(() => client.previewProfileFields(body, signal)),
    /** GET /admin/profile-fields/{id}/stats. */
    getProfileFieldStats: (id, signal) => call(async () => { const page = await collectEnumeration((q, sig) => client.getProfileFieldStats(id, q, sig), {}, { signal }); return { ...page, optionCounts: page.objects }; }),
    /** PUT /admin/members/{id}/profile. */
    updateMemberProfile: (id, profile) => call(() => client.updateMemberProfile(id, profile)),
    /** POST /admin/members/enumerate (accepts `filters.profile`). */
    enumerateAdminMembers: (query, signal) => call(() => client.enumerateAdminMembers(query, signal)),

    /** GET /admin/property/settings. */
    getPropertySettings: (signal) => call(async () => { const page = await collectEnumeration((q, sig) => client.getPropertySettings(q, sig), {}, { signal }); return { ...page, fields: page.objects }; }),
    /** GET /admin/property/access. */
    getPropertyAccess: (signal) => call(() => client.getPropertyAccess(signal)),
    /** PUT /admin/property/access. */
    updatePropertyAccess: (body) => call(() => client.updatePropertyAccess(body)),

    /** GET /admin/property/branding: the brand palette with contrast checks and presets. */
    getPropertyBranding: (signal) => call(() => client.getPropertyBranding(signal)),

    /** PUT /admin/property/branding `{ light?, dark?, contrastOverrides?, note? }`. */
    updatePropertyBranding: (body) => call(() => client.updatePropertyBranding(body)),

    /** POST /admin/property/branding/revert `{ to: 'Previous' | 'Seed', note? }`. */
    revertPropertyBranding: (to, note) => call(() => client.revertPropertyBranding(to, note)),
    /** POST /admin/property/branding/versions/enumerate. */
    enumeratePropertyBrandingVersions: (query, signal) => call(() => client.enumeratePropertyBrandingVersions(query, signal)),
    /** POST /admin/property/branding/revert `{ to: 'Version', version }`. */
    restorePropertyBrandingVersion: (version, note) => call(() => client.restorePropertyBrandingVersion(version, note)),
    /** PUT /admin/property/branding/member-presets `{ mode, presetIds? }`. */
    updatePropertyMemberPresets: (body) => call(() => client.updatePropertyMemberPresets(body)),

    /** GET /admin/property/ai: the property's AI switch. */
    getPropertyAi: (signal) => call(() => client.getPropertyAi(signal)),

    /** PUT /admin/property/ai `{ enabled }` (applies without a restart). */
    updatePropertyAi: (enabled) => call(() => client.updatePropertyAi(enabled)),
    /** PUT /admin/property/settings `{ values, clear }`. */
    updatePropertySettings: (body) => call(() => client.updatePropertySettings(body)),
    /** POST /admin/property/integrations/{name}/test. */
    testIntegration: (name, to) => call(() => client.testIntegration(name, to)),

    /** GET /admin/property/domains. */
    getPropertyDomains: (signal) => call(async () => { const [domains, apps] = await Promise.all([collectEnumeration((q, sig) => client.getPropertyDomains(q, sig), {}, { signal }), collectEnumeration((q, sig) => client.getPropertyApps(q, sig), {}, { signal })]); return { propertyId: domains.propertyId, domains: domains.objects, apps: apps.objects }; }),
    /** PUT /admin/property/domains `{ domains?, apps? }`. */
    updatePropertyDomains: (body) => call(() => client.updatePropertyDomains(body)),
    /** POST /admin/property/domains/{domainId}/verify. */
    verifyPropertyDomain: (domainId) => call(() => client.verifyPropertyDomain(domainId)),

    /** GET /admin/email/templates. */
    listEmailTemplates: (query, signal) => call(() => client.listEmailTemplates(query, signal)),
    /** GET /admin/email/templates/{key}. */
    getEmailTemplate: (key, signal) => call(() => client.getEmailTemplate(key, signal)),
    /** PUT /admin/email/templates/{key}. */
    updateEmailTemplate: (key, body) => call(() => client.updateEmailTemplate(key, body)),
    /** DELETE /admin/email/templates/{key}. */
    resetEmailTemplate: (key) => call(() => client.resetEmailTemplate(key)),
    /** POST /admin/email/templates/{key}/preview. */
    previewEmailTemplate: (key, body, signal) => call(() => client.previewEmailTemplate(key, body, signal)),
    /** POST /admin/email/test `{ template?, to? }`. */
    sendTestEmail: (body) => call(() => client.sendTestEmail(body)),
    sendTestEmailCatalog: (body) => call(() => client.sendTestEmailCatalog(body)),
    /** GET /admin/email/staff-events. */
    listEmailStaffEvents: (query, signal) => call(() => client.listEmailStaffEvents(query, signal)),
    /** PUT /admin/email/staff-events/{template}. */
    updateEmailStaffEvent: (template, body) => call(() => client.updateEmailStaffEvent(template, body)),
    /** POST /admin/email/messages/enumerate. */
    listEmailMessages: (query, signal) => call(() => client.listEmailMessages(query, signal)),
    /** GET /admin/email/messages/{id}. */
    getEmailMessage: (id, signal) => call(() => client.getEmailMessage(id, signal)),
    /** POST /admin/email/suppressions/enumerate. */
    listEmailSuppressions: (query, signal) => call(() => client.listEmailSuppressions(query, signal)),
    /** POST /admin/email/suppressions. */
    addEmailSuppression: (body) => call(() => client.addEmailSuppression(body)),
    /** DELETE /admin/email/suppressions/{id}. */
    removeEmailSuppression: (id) => call(() => client.removeEmailSuppression(id)),

    /** GET /admin/usage/summary (docs/USAGE_METERING_CONTRACT.md section 7.1). */
    getUsageSummary: (filters, signal) => call(async () => usageSeriesFromEnumeration(await collectEnumeration((q, sig) => client.adminUsageSummary(q, sig), { filters, maxResults: 100 }, { signal, maxPages: 1000 }))),
    /** GET /admin/usage/export: resolves `{ text, headers }` (the file and its Content-Disposition). */
    exportUsage: (query, format) => call(() => client.get('/admin/usage/export', { query: { ...query, format }, responseType: 'textWithHeaders' })),

    /** GET /openapi.json (outside /api/v1). */
    async getOpenApiSpec(signal) {
      let response;
      try {
        response = await client.fetchImpl(client.baseUrl + '/openapi.json', {
          headers: { Accept: 'application/json', ...(await authHeaders()) },
          signal
        });
      } catch {
        throw new ApiError('Unable to reach the server.', { status: 0 });
      }
      if (!response.ok) throw new ApiError('Request failed with status ' + response.status + '.', { status: response.status });
      try {
        return await response.json();
      } catch {
        throw new ApiError('The OpenAPI document is not valid JSON.', { status: response.status });
      }
    },

    /**
     * Execute an arbitrary request for the API Explorer and return the raw Response.
     * @param {{ method: string, url: string, headers?: Record<string,string>, body?: string, signal?: AbortSignal }} request
     */
    async executeExplorer({ method, url, headers = {}, body, signal }) {
      return client.fetchImpl(client.baseUrl + url, {
        method,
        headers: { ...headers, ...(await authHeaders()) },
        body: body === undefined || body === '' ? undefined : body,
        signal,
        credentials: 'include'
      });
    }
  };
}

/** Extract `fieldErrors` from an ApiError body, if present. */
export function getFieldErrors(err) {
  const body = err && err.body;
  if (body && typeof body === 'object' && body.fieldErrors && typeof body.fieldErrors === 'object') return body.fieldErrors;
  return {};
}

/** A display message for any error. */
export function errorMessage(err, fallback) {
  if (err && typeof err.message === 'string' && err.message) return err.message;
  return fallback;
}

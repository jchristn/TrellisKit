// AI: model endpoints and assistants. The AI admin routes answer only while the property's
// AI switch is on, so the step turns it on, configures, and then leaves it as the seed file
// says (`ai.leaveEnabled`, default false: off, ready for an administrator to switch on).

import { pick } from '../util.mjs';

export async function seedAi(ctx) {
  const spec = ctx.seed.ai;
  if (!spec) return;
  const state = await ctx.api.get('/api/v1/admin/property/ai');
  if (!state.deploymentEnabled) {
    ctx.warn('ai.enabled is off for the deployment; skipping AI configuration');
    return;
  }
  ctx.log.info(`AI (${(spec.endpoints ?? []).length} endpoints, ${(spec.assistants ?? []).length} assistants)`);
  if (!state.enabled) await ctx.api.put('/api/v1/admin/property/ai', { enabled: true });
  try {
    for (const e of spec.endpoints ?? []) {
      let endpoint = (await ctx.findByTag('/api/v1/admin/ai/endpoints/enumerate', e.key))[0];
      if (!endpoint) {
        const body = {
          ...pick(e, ['name', 'description', 'kind', 'apiType', 'model', 'endpointUrl', 'authType', 'parameters', 'limits', 'healthCheck', 'pricing']),
          enabled: e.enabled ?? true, labels: e.labels ?? [], tags: ctx.tags(e.key, e.tags),
        };
        endpoint = await ctx.api.post('/api/v1/admin/ai/endpoints', body);
        ctx.count('AI endpoints');
        await chooseAvailableModel(ctx, endpoint, e);
      } else ctx.count('AI endpoints', 'existing');
      ctx.setId('aiEndpoint', e.key, endpoint.id);
    }
    for (const a of spec.assistants ?? []) {
      let assistant = (await ctx.findByTag('/api/v1/admin/ai/assistants/enumerate', a.key))[0];
      if (!assistant) {
        const body = {
          ...pick(a, ['name', 'title', 'description', 'audience', 'avatar', 'systemPrompt', 'generation', 'knowledge', 'tools', 'limits', 'starterPrompts', 'disclaimer', 'moderationMode', 'budget', 'embed', 'showReasoning']),
          enabled: a.enabled ?? true, streaming: a.streaming ?? true,
          models: { chat: ctx.id('aiEndpoint', a.chat), embedding: a.embedding ? ctx.id('aiEndpoint', a.embedding) : null },
          labels: a.labels ?? [], tags: ctx.tags(a.key, a.tags),
        };
        const res = await ctx.api.request('POST', '/api/v1/admin/ai/assistants', { body, ok: [400] });
        if (res.error) {
          // Fall back to keyword knowledge and no tools when the deployment cannot use them.
          ctx.warn(`assistant ${a.key}: ${res.error} ${res.data?.message ?? ''} ${JSON.stringify(res.data?.fieldErrors ?? {})}; retrying with keyword knowledge and no tools`);
          body.knowledge = { ...(body.knowledge ?? {}), mode: 'Keyword' };
          body.tools = { enabled: false };
          assistant = await ctx.api.post('/api/v1/admin/ai/assistants', body);
        } else assistant = res.data;
        ctx.count('AI assistants');
        await ctx.api.request('POST', `/api/v1/admin/ai/assistants/${assistant.id}/knowledge/reindex`, { body: {}, ok: [400, 404] });
      } else ctx.count('AI assistants', 'existing');
      ctx.setId('aiAssistant', a.key, assistant.id);
    }
  } finally {
    const leaveOn = spec.leaveEnabled ?? false;
    await ctx.api.put('/api/v1/admin/property/ai', { enabled: leaveOn });
    ctx.count('AI switch', leaveOn ? 'left on' : 'left off');
  }
}

// When the configured model is not installed but a fallback is, switch to the fallback.
async function chooseAvailableModel(ctx, endpoint, spec) {
  if (!spec.fallbackModels?.length) return;
  const res = await ctx.api.request('POST', `/api/v1/admin/ai/endpoints/${endpoint.id}/models/enumerate`, { body: { maxResults: 200 }, ok: [502, 400, 404] });
  if (res.error) {
    ctx.warn(`could not list models of ${spec.name}: ${res.error}`);
    return;
  }
  const names = new Set((res.data.objects ?? []).map((m) => m.name));
  if (names.has(spec.model)) return;
  const fallback = spec.fallbackModels.find((m) => names.has(m));
  if (fallback) {
    await ctx.api.put(`/api/v1/admin/ai/endpoints/${endpoint.id}`, { model: fallback });
    ctx.warn(`model ${spec.model} is not installed; ${spec.name} uses ${fallback}`);
  } else ctx.warn(`neither ${spec.model} nor its fallbacks are installed on ${spec.endpointUrl}`);
}

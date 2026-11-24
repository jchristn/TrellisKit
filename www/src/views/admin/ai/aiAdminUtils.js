import { useCallback, useEffect, useRef, useState } from 'react';

/** Kinds each provider API supports (mirrors the server; the server is authoritative). */
export const API_KINDS = {
  Ollama: ['Completion', 'Embedding'],
  OpenAI: ['Completion', 'Embedding'],
  OpenAICompatible: ['Completion', 'Embedding'],
  Gemini: ['Completion', 'Embedding'],
  AzureOpenAI: ['Completion', 'Embedding'],
  Anthropic: ['Completion'],
  Bedrock: ['Completion', 'Embedding', 'Rerank'],
  VertexAI: ['Completion', 'Embedding'],
  OllamaCloud: ['Completion', 'Embedding'],
  XAI: ['Completion'],
  Cohere: ['Completion', 'Embedding', 'Rerank'],
  VoyageAI: ['Embedding', 'Rerank'],
  Tei: ['Embedding', 'Rerank']
};

/**
 * Auth types allowed per provider API (section 3.3, mirrors the server): all six except providers
 * that need their native key; Azure OpenAI takes an api-key or an Entra bearer token; Vertex AI
 * and Bedrock take their own credentials (ApiKey) or the host's ambient credentials (None).
 */
export function authTypesFor(apiType) {
  if (apiType === 'XAI' || apiType === 'OllamaCloud') return ['ApiKey'];
  if (apiType === 'AzureOpenAI') return ['ApiKey', 'Bearer'];
  if (apiType === 'VertexAI' || apiType === 'Bedrock') return ['ApiKey', 'None'];
  return ['None', 'ApiKey', 'Bearer', 'Header', 'QueryParameter', 'Basic'];
}

/** Every `providerSettings` field (AI contract section 1.2). */
export const PROVIDER_FIELDS = Object.freeze(['region', 'project', 'apiVersion', 'deployment', 'accessKeyId']);

/** Allowed characters of a provider setting (1 to 200 of A-Z a-z 0-9 . _ : -). */
export const PROVIDER_VALUE = /^[A-Za-z0-9._:-]{1,200}$/;

/**
 * The `providerSettings` fields a provider uses, and which of them are required (mirrors the
 * server's validation): AzureOpenAI `deployment` (required) and `apiVersion`; VertexAI `project`
 * and `region` (both required); Bedrock `region` (required) plus `accessKeyId` (required with
 * ApiKey auth). Every other provider uses none.
 * @param {string} apiType
 * @param {string} authType
 * @returns {{ used: string[], required: string[] }}
 */
export function providerFieldsFor(apiType, authType) {
  switch (apiType) {
    case 'AzureOpenAI': return { used: ['deployment', 'apiVersion'], required: ['deployment'] };
    case 'VertexAI': return { used: ['project', 'region'], required: ['project', 'region'] };
    case 'Bedrock': return authType === 'ApiKey' ? { used: ['region', 'accessKeyId'], required: ['region', 'accessKeyId'] } : { used: ['region'], required: ['region'] };
    default: return { used: [], required: [] };
  }
}

/**
 * Problems with the provider settings of a form, as `{ field: 'required' | 'invalid' }`.
 * @param {object} form endpointForm state
 */
export function providerSettingsProblems(form) {
  const { used, required } = providerFieldsFor(form.apiType, form.authType);
  const problems = {};
  for (const field of used) {
    const value = String(form[field] || '').trim();
    if (!value && required.includes(field)) problems[field] = 'required';
    else if (value && !PROVIDER_VALUE.test(value)) problems[field] = 'invalid';
  }
  return problems;
}

/** Endpoint `parameters.toolCalling` modes: provider function calling, a JSON protocol in the prompt, or no tools. */
export const TOOL_CALLING_MODES = Object.freeze(['Native', 'Prompt', 'Off']);

/** Endpoint `parameters.reasoningEffort` values (empty in the form means it is not sent). */
export const REASONING_EFFORTS = Object.freeze(['Minimal', 'Low', 'Medium', 'High']);

/** Health check modes an endpoint kind can use: a connectivity check, or a real call of the endpoint's own kind. */
export function healthModesFor(kind) {
  return ['Connectivity', ['Completion', 'Embedding', 'Rerank'].includes(kind) ? kind : 'Completion'];
}

/**
 * Whether an endpoint form would send a credential over plain http (the form warns; the server
 * allows it, because administrators run gateways and local model servers without TLS).
 * @param {object} form endpointForm state
 */
export function sendsCredentialsInClear(form) {
  const url = String((form && form.endpointUrl) || '').trim().toLowerCase();
  if (!url.startsWith('http://')) return false;
  return form.authType !== 'None' || (form.extraHeaders || []).some((h) => String(h.name || '').trim());
}

/** Whether an auth type stores a secret value. */
export function authUsesSecret(authType) {
  return authType !== 'None';
}

function numberOrNull(value) {
  if (value === '' || value === null || value === undefined) return null;
  const n = Number(value);
  return Number.isFinite(n) ? n : null;
}

/**
 * The create or update body for an endpoint form. Secrets follow the write-only rules: `secretMode`
 * keep omits `authValue`, replace sends it, and clear sends an empty string. Extra headers without a
 * new value keep the stored value of the same name.
 */
export function endpointBody(form, { isCreate }) {
  const body = {
    name: form.name.trim(),
    description: form.description.trim() || null,
    enabled: form.enabled,
    kind: form.kind,
    apiType: form.apiType,
    model: form.model.trim(),
    endpointUrl: form.endpointUrl.trim(),
    authType: form.authType,
    authHeaderName: form.authType === 'Header' ? form.authHeaderName.trim() : null,
    authQueryParameter: form.authType === 'QueryParameter' ? form.authQueryParameter.trim() : null,
    authUsername: form.authType === 'Basic' ? form.authUsername.trim() : null,
    extraHeaders: form.extraHeaders.filter((h) => h.name.trim()).map((h) => (h.value ? { name: h.name.trim(), value: h.value } : { name: h.name.trim() })),
    parameters: {
      temperature: numberOrNull(form.temperature),
      topP: numberOrNull(form.topP),
      maxTokens: numberOrNull(form.maxTokens),
      contextWindow: numberOrNull(form.contextWindow),
      streaming: form.streaming,
      timeoutMs: numberOrNull(form.timeoutMs),
      firstResponseTimeoutMs: numberOrNull(form.firstResponseTimeoutMs),
      dimensions: form.kind === 'Embedding' ? numberOrNull(form.dimensions) : null,
      toolCalling: form.kind === 'Completion' ? (form.toolCalling || 'Native') : null,
      reasoningEffort: form.kind === 'Completion' && REASONING_EFFORTS.includes(form.reasoningEffort) ? form.reasoningEffort : null
    },
    limits: { maxConcurrentRequests: numberOrNull(form.maxConcurrentRequests), maxQueueDepth: numberOrNull(form.maxQueueDepth), maxRetries: numberOrNull(form.maxRetries) },
    healthCheck: {
      enabled: form.healthEnabled, intervalMs: numberOrNull(form.intervalMs),
      mode: healthModesFor(form.kind).includes(form.healthMode) ? form.healthMode : 'Connectivity',
      timeoutMs: numberOrNull(form.healthTimeoutMs), healthyThreshold: numberOrNull(form.healthyThreshold), unhealthyThreshold: numberOrNull(form.unhealthyThreshold)
    },
    pricing: {
      inputPerMillionTokens: numberOrNull(form.inputPrice), outputPerMillionTokens: numberOrNull(form.outputPrice),
      cacheReadPerMillionTokens: numberOrNull(form.cacheReadPrice), cacheWritePerMillionTokens: numberOrNull(form.cacheWritePrice)
    }
  };
  // Providers with settings send every field: the used ones (empty as null) and null for the rest,
  // which the server requires to be null.
  const { used } = providerFieldsFor(form.apiType, form.authType);
  if (used.length) {
    body.providerSettings = {};
    for (const field of PROVIDER_FIELDS) body.providerSettings[field] = used.includes(field) ? (String(form[field] || '').trim() || null) : null;
  }
  if (form.authType === 'None') {
    if (!isCreate) body.authValue = '';
  } else if (isCreate) {
    if (form.authValue) body.authValue = form.authValue;
  } else if (form.secretMode === 'replace') {
    body.authValue = form.authValue;
  } else if (form.secretMode === 'clear') {
    body.authValue = '';
  }
  return body;
}

/** Form state for an endpoint (empty for create). */
export function endpointForm(endpoint) {
  const e = endpoint || {};
  const p = e.parameters || {};
  const l = e.limits || {};
  const h = e.healthCheck || {};
  const price = e.pricing || {};
  const ps = e.providerSettings || {};
  const str = (v) => (v === null || v === undefined ? '' : String(v));
  return {
    name: e.name || '', description: e.description || '', enabled: e.enabled !== false, kind: e.kind || 'Completion', apiType: e.apiType || 'Ollama',
    model: e.model || '', endpointUrl: e.endpointUrl || '', authType: e.authType || 'None', authHeaderName: e.authHeaderName || '', authQueryParameter: e.authQueryParameter || '',
    authUsername: e.authUsername || '', authValue: '', secretMode: 'keep', hasAuthValue: Boolean(e.hasAuthValue),
    extraHeaders: (e.extraHeaders || []).map((x) => ({ name: x.name, value: '', hasValue: x.hasValue })),
    temperature: str(p.temperature), topP: str(p.topP), maxTokens: str(p.maxTokens), contextWindow: str(p.contextWindow), streaming: p.streaming !== false,
    timeoutMs: str(p.timeoutMs ?? 120000), firstResponseTimeoutMs: str(p.firstResponseTimeoutMs), dimensions: str(p.dimensions),
    toolCalling: TOOL_CALLING_MODES.includes(p.toolCalling) ? p.toolCalling : 'Native',
    reasoningEffort: REASONING_EFFORTS.includes(p.reasoningEffort) ? p.reasoningEffort : '',
    maxConcurrentRequests: str(l.maxConcurrentRequests ?? 2), maxQueueDepth: str(l.maxQueueDepth ?? 20), maxRetries: str(l.maxRetries ?? 2),
    healthEnabled: h.enabled !== false, intervalMs: str(h.intervalMs ?? 60000), healthMode: ['Connectivity', 'Completion', 'Embedding', 'Rerank'].includes(h.mode) ? h.mode : 'Connectivity',
    healthTimeoutMs: str(h.timeoutMs ?? 10000), healthyThreshold: str(h.healthyThreshold ?? 2), unhealthyThreshold: str(h.unhealthyThreshold ?? 2),
    inputPrice: str(price.inputPerMillionTokens), outputPrice: str(price.outputPerMillionTokens),
    cacheReadPrice: str(price.cacheReadPerMillionTokens), cacheWritePrice: str(price.cacheWritePerMillionTokens),
    region: str(ps.region), project: str(ps.project), apiVersion: str(ps.apiVersion), deployment: str(ps.deployment), accessKeyId: str(ps.accessKeyId)
  };
}

/** Whether the endpoint host changed while credentials are stored (section 2.3 asks for confirmation). */
export function hostChangedWithSecrets(endpoint, form) {
  if (!endpoint || !(endpoint.hasAuthValue || (endpoint.extraHeaders || []).some((h) => h.hasValue))) return false;
  const host = (url) => {
    try {
      return new URL(url).host;
    } catch {
      return '';
    }
  };
  return host(endpoint.endpointUrl) !== host(form.endpointUrl.trim());
}

/** Lines of a textarea, trimmed and non-empty. */
export function lines(text) {
  return String(text || '').split(/\r?\n/).map((x) => x.trim()).filter(Boolean);
}

/** Comma-separated values, trimmed and non-empty. */
export function commaList(text) {
  return String(text || '').split(',').map((x) => x.trim()).filter(Boolean);
}

/**
 * An enumeration with "load more" paging: re-runs when `deps` change (the fetcher must be stable).
 * @param {(query: object, signal: AbortSignal) => Promise<object>} fetcher
 */
export function usePagedList(fetcher) {
  const [state, setState] = useState({ items: [], token: null, loading: true, error: null, total: null });
  const [nonce, setNonce] = useState(0);
  const active = useRef(0);
  useEffect(() => {
    const run = ++active.current;
    const controller = new AbortController();
    setState((s) => ({ ...s, loading: true, error: null }));
    fetcher({ maxResults: 50 }, controller.signal).then(
      (page) => { if (run === active.current) setState({ items: (page && page.objects) || [], token: page && !page.endOfResults ? page.continuationToken : null, loading: false, error: null, total: page ? page.totalRecords : null }); },
      (error) => { if (run === active.current && error.name !== 'AbortError') setState((s) => ({ ...s, loading: false, error })); }
    );
    return () => controller.abort();
  }, [fetcher, nonce]);
  const more = useCallback(async () => {
    if (!state.token) return;
    const page = await fetcher({ maxResults: 50, continuationToken: state.token });
    setState((s) => ({ ...s, items: s.items.concat((page && page.objects) || []), token: page && !page.endOfResults ? page.continuationToken : null }));
  }, [fetcher, state.token]);
  const reload = useCallback(() => setNonce((n) => n + 1), []);
  return { ...state, more, reload };
}

/** Purposes an assistant can assign a model endpoint to, and the endpoint kind each one needs. */
export const PURPOSES = [
  ['chat', 'Completion'], ['chatFallback', 'Completion'], ['titleGeneration', 'Completion'], ['queryRewrite', 'Completion'],
  ['embedding', 'Embedding'], ['rerank', 'Rerank'], ['moderation', 'Completion'], ['toolRouting', 'Completion']
];

/**
 * Bounds and defaults of the assistant `tools` limits (mirrors the server; the server is
 * authoritative). `maxTokensPerTurn` 0 means no extra cap; otherwise it is 1000 to 1000000.
 */
export const TOOL_LIMITS = Object.freeze({
  maxToolIterations: { min: 1, max: 10, default: 4 },
  maxToolCallsPerTurn: { min: 1, max: 12, default: 6 },
  toolCallTimeoutMs: { min: 100, max: 60000, default: 15000 },
  maxToolOutputCharacters: { min: 200, max: 100000, default: 8000 },
  maxTokensPerTurn: { min: 0, max: 1000000, default: 32000, zeroOrMin: 1000 }
});

/**
 * Client-side problems with the tool limits of an assistant form, keyed like the server's
 * `fieldErrors` (`tools.maxToolIterations`), as `'range'` per out-of-bounds field.
 * @param {object} form assistantForm state
 */
export function toolLimitProblems(form) {
  const problems = {};
  for (const [key, bound] of Object.entries(TOOL_LIMITS)) {
    const raw = form[key];
    if (raw === '' || raw === null || raw === undefined) continue;
    const n = Number(raw);
    const ok = Number.isInteger(n) && n >= bound.min && n <= bound.max && !(bound.zeroOrMin && n !== 0 && n < bound.zeroOrMin);
    if (!ok) problems['tools.' + key] = 'range';
  }
  return problems;
}

/**
 * The tool names to show as checkboxes: registry entries, never aliases (an alias such as a second
 * name of one tool is not its own checkbox). An allowed name that is an alias maps to its tool.
 * @param {object[]} registry AssistantToolView items
 * @param {string[]} allowed
 * @returns {string[]} The allowed list with aliases resolved to their canonical names, de-duplicated.
 */
export function canonicalToolNames(registry, allowed) {
  const byAlias = new Map();
  for (const tool of registry || []) for (const alias of tool.aliases || []) byAlias.set(alias, tool.name);
  const out = [];
  for (const name of allowed || []) {
    const canonical = byAlias.get(name) || name;
    if (!out.includes(canonical)) out.push(canonical);
  }
  return out;
}

/** Form state for an assistant (empty for create; a new one takes `defaultAudience`, the access model's default for assistants). */
export function assistantForm(assistant, defaultAudience = 'Members') {
  const a = assistant || {};
  const m = a.models || {};
  const k = a.knowledge || {};
  const l = a.limits || {};
  const g = a.generation || {};
  const e = a.embed || {};
  const b = a.budget || {};
  const t = a.tools || {};
  const str = (v) => (v === null || v === undefined ? '' : String(v));
  const models = {};
  for (const [purpose] of PURPOSES) models[purpose] = m[purpose] || '';
  return {
    name: a.name || '', title: a.title || '', description: a.description || '', enabled: a.enabled !== false, audience: a.audience || defaultAudience,
    emoji: (a.avatar && a.avatar.emoji) || '', disclaimer: a.disclaimer || '', starterPrompts: (a.starterPrompts || []).join('\n'),
    systemPrompt: a.systemPrompt || '', models, temperature: str(g.temperature), topP: str(g.topP),
    knowledgeEnabled: Boolean(k.enabled), knowledgeMode: k.mode || 'Keyword', citations: k.citations !== false, maxContextItems: str(k.maxContextItems ?? 6),
    sources: (k.sources || []).map((s) => ({ labels: (s.labels || []).join(', '), labelMatch: s.labelMatch || 'All', maxItems: str(s.maxItems ?? 200) })),
    messagesPerMemberPerDay: str(l.messagesPerMemberPerDay ?? 50), messagesPerVisitorPerDay: str(l.messagesPerVisitorPerDay ?? 10),
    maxMessagesPerConversation: str(l.maxMessagesPerConversation ?? 100), maxTokensPerReply: str(l.maxTokensPerReply ?? 800), maxMessageCharacters: str(l.maxMessageCharacters ?? 4000),
    streaming: a.streaming !== false, showReasoning: Boolean(a.showReasoning), moderationMode: a.moderationMode || 'Off',
    budgetMonthly: str(b.monthlyUsd), budgetHardStop: Boolean(b.hardStop),
    embedEnabled: Boolean(e.enabled), embedOrigins: (e.allowedOrigins || []).join('\n'), embedLabel: e.launcherLabel || '', embedPosition: e.position || 'BottomRight',
    toolsEnabled: Boolean(t.enabled), toolsAllowed: Array.isArray(t.allowed) ? t.allowed.slice() : [], showToolActivity: t.showToolActivity !== false,
    maxToolIterations: str(t.maxToolIterations ?? TOOL_LIMITS.maxToolIterations.default), maxToolCallsPerTurn: str(t.maxToolCallsPerTurn ?? TOOL_LIMITS.maxToolCallsPerTurn.default),
    toolCallTimeoutMs: str(t.toolCallTimeoutMs ?? TOOL_LIMITS.toolCallTimeoutMs.default), maxToolOutputCharacters: str(t.maxToolOutputCharacters ?? TOOL_LIMITS.maxToolOutputCharacters.default),
    maxTokensPerTurn: str(t.maxTokensPerTurn ?? TOOL_LIMITS.maxTokensPerTurn.default)
  };
}

/** The create or update body for an assistant form (empty models are sent as null to clear them). */
export function assistantBody(form) {
  const models = {};
  for (const [purpose] of PURPOSES) models[purpose] = form.models[purpose] || null;
  return {
    name: form.name.trim(), title: form.title.trim() || form.name.trim(), description: form.description.trim() || null, enabled: form.enabled, audience: form.audience,
    avatar: { emoji: form.emoji.trim() || null }, disclaimer: form.disclaimer.trim() || null, starterPrompts: lines(form.starterPrompts).slice(0, 6),
    systemPrompt: form.systemPrompt, models, generation: { temperature: numberOrNull(form.temperature), topP: numberOrNull(form.topP) },
    knowledge: {
      enabled: form.knowledgeEnabled, mode: form.knowledgeMode, citations: form.citations, maxContextItems: numberOrNull(form.maxContextItems),
      sources: form.sources.map((s) => ({ targetType: 'FeedPost', labels: commaList(s.labels), labelMatch: s.labelMatch, maxItems: numberOrNull(s.maxItems) }))
    },
    limits: {
      messagesPerMemberPerDay: numberOrNull(form.messagesPerMemberPerDay), messagesPerVisitorPerDay: numberOrNull(form.messagesPerVisitorPerDay),
      maxMessagesPerConversation: numberOrNull(form.maxMessagesPerConversation), maxTokensPerReply: numberOrNull(form.maxTokensPerReply), maxMessageCharacters: numberOrNull(form.maxMessageCharacters)
    },
    streaming: form.streaming, showReasoning: form.showReasoning, moderationMode: form.moderationMode,
    budget: { monthlyUsd: numberOrNull(form.budgetMonthly), hardStop: form.budgetHardStop },
    embed: { enabled: form.embedEnabled, allowedOrigins: lines(form.embedOrigins), launcherLabel: form.embedLabel.trim() || null, position: form.embedPosition },
    tools: {
      enabled: Boolean(form.toolsEnabled), allowed: (form.toolsAllowed || []).slice(), showToolActivity: form.showToolActivity !== false,
      maxToolIterations: numberOrNull(form.maxToolIterations), maxToolCallsPerTurn: numberOrNull(form.maxToolCallsPerTurn), toolCallTimeoutMs: numberOrNull(form.toolCallTimeoutMs),
      maxToolOutputCharacters: numberOrNull(form.maxToolOutputCharacters), maxTokensPerTurn: numberOrNull(form.maxTokensPerTurn)
    }
  };
}

/** The widget snippet for an embeddable assistant (section 13.3). */
export function widgetSnippet(origin, assistant) {
  const embed = assistant.embed || {};
  const label = embed.launcherLabel ? ' data-label="' + String(embed.launcherLabel).replace(/"/g, '&quot;') + '"' : '';
  const position = embed.position ? ' data-position="' + embed.position + '"' : '';
  return '<script src="' + origin + '/assistant-widget.js" data-assistant="' + assistant.id + '"' + position + label + ' async></script>';
}

/** US dollars, with four decimals for amounts under ten cents. */
export function usd(value) {
  const n = Number(value) || 0;
  return '$' + (n !== 0 && Math.abs(n) < 0.1 ? n.toFixed(4) : n.toFixed(2));
}

/** A rate (0 to 1) as a percentage. */
export function percent(value) {
  return value === null || value === undefined ? '-' : (Math.round(Number(value) * 1000) / 10) + '%';
}

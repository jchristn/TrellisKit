import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import strings from '../strings.js';
import { buildCodeSnippets, buildRelativeUrl, flattenOpenApiSpec, getParameterDefault, getRequestBodyTemplate, headersToObject, methodAllowsBody } from '../utils/openApi.js';
import useApiQuery from './useApiQuery.js';

const HISTORY_KEY = 'tk.admin.explorer.history';
const SELECTED_KEY = 'tk.admin.explorer.selected';
const HISTORY_LIMIT = 12;

function readJson(key, fallback) {
  try {
    const raw = window.localStorage.getItem(key);
    return raw ? JSON.parse(raw) : fallback;
  } catch {
    return fallback;
  }
}

function writeJson(key, value) {
  try {
    window.localStorage.setItem(key, JSON.stringify(value));
  } catch {
    // Ignore storage failures.
  }
}

function defaultsFor(operation, location) {
  const values = {};
  for (const p of operation.parameters) {
    if (p.in === location) values[p.name] = getParameterDefault(p);
  }
  return values;
}

function isStreaming(response) {
  const type = response.headers.get('content-type') || '';
  const encoding = response.headers.get('transfer-encoding') || '';
  return type.includes('text/event-stream') || encoding.includes('chunked');
}

/**
 * State and execution for the API Explorer: loads /openapi.json, tracks the selected operation
 * and its inputs, executes requests with the admin session, streams bodies, and keeps a
 * 12-entry history in localStorage.
 */
export function useApiExplorer(api) {
  const specFetcher = useCallback((signal) => api.getOpenApiSpec(signal), [api]);
  const specQuery = useApiQuery(specFetcher);
  const spec = specQuery.data;
  const operations = useMemo(() => flattenOpenApiSpec(spec), [spec]);

  const [operationId, setOperationIdState] = useState(() => readJson(SELECTED_KEY, null));
  const [pathParams, setPathParams] = useState({});
  const [queryParams, setQueryParams] = useState({});
  const [headerParams, setHeaderParams] = useState({});
  const [extraHeaders, setExtraHeaders] = useState([]);
  const [body, setBody] = useState('');
  const [response, setResponse] = useState(null);
  const [running, setRunning] = useState(false);
  const [history, setHistory] = useState(() => readJson(HISTORY_KEY, []));
  const controllerRef = useRef(null);
  const [initializedFor, setInitializedFor] = useState(null);

  const operation = useMemo(() => operations.find((op) => op.id === operationId) || null, [operations, operationId]);

  if (operation && initializedFor !== operation.id) {
    setInitializedFor(operation.id);
    setPathParams(defaultsFor(operation, 'path'));
    setQueryParams(defaultsFor(operation, 'query'));
    setHeaderParams(defaultsFor(operation, 'header'));
    setExtraHeaders([]);
    setBody(getRequestBodyTemplate(spec, operation.requestBody));
    setResponse(null);
  }

  const selectOperation = useCallback((id) => {
    setOperationIdState(id);
    writeJson(SELECTED_KEY, id);
  }, []);

  const headers = useMemo(() => {
    const result = { Accept: 'application/json' };
    for (const [k, v] of Object.entries(headerParams)) if (v !== '') result[k] = v;
    for (const h of extraHeaders) if (h.name.trim()) result[h.name.trim()] = h.value;
    if (operation && methodAllowsBody(operation.method) && body.trim() && !Object.keys(result).some((k) => k.toLowerCase() === 'content-type')) {
      result['Content-Type'] = 'application/json';
    }
    return result;
  }, [headerParams, extraHeaders, body, operation]);

  const relativeUrl = operation ? buildRelativeUrl(operation.path, pathParams, queryParams) : '';
  const resolvedUrl = operation ? window.location.origin + relativeUrl : '';
  const sendBody = operation && methodAllowsBody(operation.method) ? body : '';

  const bodyError = useMemo(() => {
    if (!sendBody.trim()) return null;
    const type = Object.entries(headers).find(([k]) => k.toLowerCase() === 'content-type');
    if (type && !String(type[1]).includes('json')) return null;
    try {
      JSON.parse(sendBody);
      return null;
    } catch (err) {
      return strings.admin.explorer.invalidJson(err.message);
    }
  }, [sendBody, headers]);

  const missingRequired = useMemo(() => {
    if (!operation) return [];
    return operation.parameters
      .filter((p) => p.required || p.in === 'path')
      .filter((p) => {
        const source = p.in === 'path' ? pathParams : p.in === 'query' ? queryParams : p.in === 'header' ? headerParams : {};
        return !String(source[p.name] ?? '').trim();
      })
      .map((p) => p.name);
  }, [operation, pathParams, queryParams, headerParams]);

  const snippets = useMemo(
    () => (operation ? buildCodeSnippets({ method: operation.method, url: resolvedUrl, headers, body: sendBody }) : null),
    [operation, resolvedUrl, headers, sendBody]
  );

  const persistHistory = useCallback((entry) => {
    setHistory((prev) => {
      const next = [entry, ...prev].slice(0, HISTORY_LIMIT);
      writeJson(HISTORY_KEY, next);
      return next;
    });
  }, []);

  const execute = useCallback(async () => {
    if (!operation) return;
    const controller = new AbortController();
    controllerRef.current = controller;
    setRunning(true);
    setResponse(null);
    const started = performance.now();
    try {
      const raw = await api.executeExplorer({ method: operation.method, url: relativeUrl, headers, body: sendBody || undefined, signal: controller.signal });
      const meta = { status: raw.status, statusText: raw.statusText, headers: headersToObject(raw.headers), body: '', durationMs: 0, bytes: 0, streaming: false };
      if (raw.body && typeof raw.body.getReader === 'function' && isStreaming(raw)) {
        meta.streaming = true;
        const reader = raw.body.getReader();
        const decoder = new TextDecoder();
        let text = '';
        let bytes = 0;
        for (;;) {
          const { done, value } = await reader.read();
          if (done) break;
          bytes += value.byteLength;
          text += decoder.decode(value, { stream: true });
          setResponse({ ...meta, body: text, bytes, durationMs: performance.now() - started });
        }
        meta.body = text + decoder.decode();
        meta.bytes = bytes;
      } else {
        meta.body = await raw.text();
        meta.bytes = new TextEncoder().encode(meta.body).length;
      }
      meta.durationMs = performance.now() - started;
      setResponse(meta);
      persistHistory({
        id: Date.now() + '-' + Math.random().toString(36).slice(2, 8),
        operationId: operation.id,
        method: operation.method,
        path: relativeUrl,
        status: meta.status,
        at: new Date().toISOString(),
        pathParams,
        queryParams,
        headerParams,
        extraHeaders,
        body
      });
    } catch (err) {
      const aborted = err && err.name === 'AbortError';
      setResponse({ error: aborted ? strings.admin.explorer.cancelled : strings.admin.explorer.networkError + ' ' + (err && err.message ? err.message : ''), durationMs: performance.now() - started });
    } finally {
      controllerRef.current = null;
      setRunning(false);
    }
  }, [api, operation, relativeUrl, headers, sendBody, persistHistory, pathParams, queryParams, headerParams, extraHeaders, body]);

  const cancel = useCallback(() => {
    if (controllerRef.current) controllerRef.current.abort();
  }, []);

  const loadHistory = useCallback((entry) => {
    const op = operations.find((o) => o.id === entry.operationId);
    if (!op) return;
    setOperationIdState(op.id);
    writeJson(SELECTED_KEY, op.id);
    setInitializedFor(op.id);
    setPathParams(entry.pathParams || {});
    setQueryParams(entry.queryParams || {});
    setHeaderParams(entry.headerParams || {});
    setExtraHeaders(entry.extraHeaders || []);
    setBody(entry.body || '');
    setResponse(null);
  }, [operations]);

  const removeHistory = useCallback((id) => {
    setHistory((prev) => {
      const next = prev.filter((h) => h.id !== id);
      writeJson(HISTORY_KEY, next);
      return next;
    });
  }, []);

  useEffect(() => () => { if (controllerRef.current) controllerRef.current.abort(); }, []);

  return {
    specQuery,
    operations,
    operation,
    selectOperation,
    pathParams, setPathParams,
    queryParams, setQueryParams,
    headerParams, setHeaderParams,
    extraHeaders, setExtraHeaders,
    body, setBody,
    headers,
    relativeUrl,
    resolvedUrl,
    bodyError,
    missingRequired,
    snippets,
    execute,
    cancel,
    running,
    response,
    history,
    loadHistory,
    removeHistory
  };
}

export default useApiExplorer;

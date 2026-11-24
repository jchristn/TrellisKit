/**
 * Helpers that flatten and interpret an OpenAPI 3 document for the API Explorer.
 */

const METHODS = ['get', 'post', 'put', 'patch', 'delete', 'head', 'options'];

/**
 * Resolve a local `$ref` such as `#/components/schemas/Thing`.
 * @param {object} spec
 * @param {object} value
 * @param {number} [depth]
 */
export function resolveRef(spec, value, depth = 0) {
  if (!value || typeof value !== 'object' || !value.$ref || depth > 20) return value;
  const ref = String(value.$ref);
  if (!ref.startsWith('#/')) return value;
  let target = spec;
  for (const part of ref.slice(2).split('/')) {
    const key = part.replace(/~1/g, '/').replace(/~0/g, '~');
    target = target ? target[key] : undefined;
  }
  return target ? resolveRef(spec, target, depth + 1) : value;
}

/**
 * Flatten an OpenAPI spec into a list of operations.
 * @param {object} spec
 * @returns {Array<{ id: string, tag: string, method: string, path: string, summary: string, description: string, deprecated: boolean, parameters: Array<object>, requestBody: object|null }>}
 */
export function flattenOpenApiSpec(spec) {
  const operations = [];
  if (!spec || typeof spec !== 'object' || !spec.paths) return operations;
  for (const [path, rawItem] of Object.entries(spec.paths)) {
    const item = resolveRef(spec, rawItem) || {};
    const shared = (item.parameters || []).map((p) => resolveRef(spec, p));
    for (const method of METHODS) {
      const op = item[method];
      if (!op) continue;
      const own = (op.parameters || []).map((p) => resolveRef(spec, p));
      const merged = new Map();
      for (const p of [...shared, ...own]) {
        if (p && p.name) merged.set((p.in || 'query') + ':' + p.name, p);
      }
      operations.push({
        id: op.operationId || method.toUpperCase() + ' ' + path,
        tag: (op.tags && op.tags[0]) || '',
        method: method.toUpperCase(),
        path,
        summary: op.summary || '',
        description: op.description || '',
        deprecated: Boolean(op.deprecated),
        parameters: [...merged.values()],
        requestBody: op.requestBody ? resolveRef(spec, op.requestBody) : null
      });
    }
  }
  return operations;
}

/**
 * Group operations by tag, preserving first-seen order.
 * @param {Array<object>} operations
 * @param {string} untaggedLabel
 */
export function groupOperations(operations, untaggedLabel) {
  const groups = [];
  const index = new Map();
  for (const op of operations) {
    const tag = op.tag || untaggedLabel;
    if (!index.has(tag)) {
      index.set(tag, groups.length);
      groups.push({ tag, operations: [] });
    }
    groups[index.get(tag)].operations.push(op);
  }
  return groups;
}

/** Filter operations by a search string over method, path, id, and summary. */
export function filterOperations(operations, search) {
  const q = (search || '').trim().toLowerCase();
  if (!q) return operations;
  return operations.filter((op) =>
    (op.method + ' ' + op.path + ' ' + op.id + ' ' + op.summary + ' ' + op.tag).toLowerCase().includes(q)
  );
}

/**
 * Default value for a parameter: example, default, first enum value, or empty.
 * @param {object} parameter
 * @returns {string}
 */
export function getParameterDefault(parameter) {
  if (!parameter) return '';
  const schema = parameter.schema || {};
  const candidates = [parameter.example, schema.example, schema.default, schema.enum && schema.enum[0]];
  for (const c of candidates) {
    if (c !== undefined && c !== null) return String(c);
  }
  return '';
}

/** Build an example value from a schema. */
export function exampleFromSchema(spec, rawSchema, depth = 0) {
  const schema = resolveRef(spec, rawSchema);
  if (!schema || typeof schema !== 'object' || depth > 8) return null;
  if (schema.example !== undefined) return schema.example;
  if (schema.default !== undefined) return schema.default;
  if (Array.isArray(schema.enum) && schema.enum.length) return schema.enum[0];
  if (Array.isArray(schema.allOf)) {
    return schema.allOf.reduce((acc, part) => {
      const value = exampleFromSchema(spec, part, depth + 1);
      return value && typeof value === 'object' && !Array.isArray(value) ? { ...acc, ...value } : acc;
    }, {});
  }
  const variants = schema.oneOf || schema.anyOf;
  if (Array.isArray(variants) && variants.length) return exampleFromSchema(spec, variants[0], depth + 1);
  const type = Array.isArray(schema.type) ? schema.type.find((t) => t !== 'null') : schema.type;
  if (type === 'object' || schema.properties) {
    const result = {};
    for (const [name, prop] of Object.entries(schema.properties || {})) {
      result[name] = exampleFromSchema(spec, prop, depth + 1);
    }
    if (!schema.properties && schema.additionalProperties) return {};
    return result;
  }
  if (type === 'array') {
    const item = exampleFromSchema(spec, schema.items, depth + 1);
    return item === null ? [] : [item];
  }
  if (type === 'integer' || type === 'number') return 0;
  if (type === 'boolean') return false;
  if (type === 'string') {
    if (schema.format === 'date-time') return new Date(0).toISOString();
    return '';
  }
  return null;
}

/**
 * Example JSON body text for a request body definition.
 * @returns {string}
 */
export function getRequestBodyTemplate(spec, requestBody) {
  if (!requestBody || !requestBody.content) return '';
  const content = requestBody.content['application/json'] || Object.values(requestBody.content)[0];
  if (!content) return '';
  if (content.example !== undefined) return JSON.stringify(content.example, null, 2);
  if (content.examples) {
    const first = Object.values(content.examples)[0];
    const resolved = resolveRef(spec, first);
    if (resolved && resolved.value !== undefined) return JSON.stringify(resolved.value, null, 2);
  }
  const value = exampleFromSchema(spec, content.schema);
  return value === null ? '' : JSON.stringify(value, null, 2);
}

/** Substitute `{name}` path parameters, URL-encoding each value. */
export function substitutePathParams(path, values) {
  return path.replace(/\{([^}]+)\}/g, (match, name) => {
    const value = values && values[name];
    return value === undefined || value === '' ? match : encodeURIComponent(value);
  });
}

/** Build the relative URL (path plus query string) for a request. */
export function buildRelativeUrl(path, pathParams, queryParams) {
  const resolved = substitutePathParams(path, pathParams);
  const params = new URLSearchParams();
  for (const [key, value] of Object.entries(queryParams || {})) {
    if (value !== undefined && value !== null && value !== '') params.append(key, value);
  }
  const qs = params.toString();
  return qs ? resolved + '?' + qs : resolved;
}

/** Whether an operation needs a confirmation before running. */
export function isDestructive(method, path) {
  const m = String(method).toUpperCase();
  return m === 'DELETE' || /\/bulk(\/|$)/i.test(path) || /\/restart(\/|$)/i.test(path);
}

/** Whether a method normally carries a body. */
export function methodAllowsBody(method) {
  return ['POST', 'PUT', 'PATCH', 'DELETE'].includes(String(method).toUpperCase());
}

function shellQuote(value) {
  return "'" + String(value).replace(/'/g, "'\\''") + "'";
}

function csharpString(value) {
  return '"' + String(value).replace(/\\/g, '\\\\').replace(/"/g, '\\"').replace(/\n/g, '\\n') + '"';
}

/**
 * Build curl, fetch, and C# snippets for the exact composed request. The admin token is
 * represented by the `$TK_ADMIN_TOKEN` placeholder so it is never copied by accident.
 * @param {{ method: string, url: string, headers: Record<string,string>, body: string }} request
 * @returns {{ curl: string, fetch: string, csharp: string }}
 */
export function buildCodeSnippets({ method, url, headers = {}, body = '' }) {
  const allHeaders = { ...headers, Authorization: 'Bearer $TK_ADMIN_TOKEN' };
  const hasBody = body !== undefined && body !== null && body !== '';

  const curlLines = ['curl -X ' + method + ' ' + shellQuote(url)];
  for (const [k, v] of Object.entries(allHeaders)) {
    const header = k + ': ' + v;
    curlLines.push('  -H ' + (k === 'Authorization' ? '"' + header + '"' : shellQuote(header)));
  }
  if (hasBody) curlLines.push('  --data ' + shellQuote(body));
  const curl = curlLines.join(' \\\n');

  const fetchHeaders = { ...headers, Authorization: 'Bearer ${process.env.TK_ADMIN_TOKEN}' };
  const headerLines = Object.entries(fetchHeaders).map(([k, v]) =>
    '    ' + JSON.stringify(k) + ': ' + (k === 'Authorization' ? '`' + v + '`' : JSON.stringify(v))
  );
  const fetchParts = [
    'const response = await fetch(' + JSON.stringify(url) + ', {',
    '  method: ' + JSON.stringify(method) + ',',
    '  headers: {',
    headerLines.join(',\n'),
    '  }' + (hasBody ? ',' : '')
  ];
  if (hasBody) fetchParts.push('  body: ' + JSON.stringify(body));
  fetchParts.push('});', 'const text = await response.text();');
  const fetchSnippet = fetchParts.join('\n');

  const contentType = Object.entries(headers).find(([k]) => k.toLowerCase() === 'content-type');
  const cs = [
    'using HttpClient client = new HttpClient();',
    'using HttpRequestMessage request = new HttpRequestMessage(new HttpMethod(' + csharpString(method) + '), ' + csharpString(url) + ');',
    'request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + Environment.GetEnvironmentVariable("TK_ADMIN_TOKEN"));'
  ];
  for (const [k, v] of Object.entries(headers)) {
    if (k.toLowerCase() === 'content-type') continue;
    cs.push('request.Headers.TryAddWithoutValidation(' + csharpString(k) + ', ' + csharpString(v) + ');');
  }
  if (hasBody) {
    cs.push('request.Content = new StringContent(' + csharpString(body) + ', System.Text.Encoding.UTF8, ' + csharpString(contentType ? contentType[1] : 'application/json') + ');');
  }
  cs.push('using HttpResponseMessage response = await client.SendAsync(request);');
  cs.push('string text = await response.Content.ReadAsStringAsync();');

  return { curl, fetch: fetchSnippet, csharp: cs.join('\n') };
}

/** Convert a Headers object to a plain object. */
export function headersToObject(headers) {
  const result = {};
  if (headers && typeof headers.forEach === 'function') headers.forEach((value, key) => { result[key] = value; });
  return result;
}

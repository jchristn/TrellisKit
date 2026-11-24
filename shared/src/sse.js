/**
 * Incremental server-sent events parser for `fetch` streams (docs/AI_CONTRACT.md section 10.1).
 *
 * Browsers cannot POST or send `Authorization` with `EventSource`, so the website reads the reply
 * stream with `fetch` and a `ReadableStream` reader, and the mobile app with `fetch` from
 * `expo/fetch`; both feed the decoded text to this parser. Events are `event:`, `id:`, and `data:`
 * lines ended by a blank line; `:` lines are comments (heartbeats) and are ignored. `data` is parsed
 * as JSON when it is JSON.
 */

/**
 * Create a parser.
 * @param {(event: { event: string, id: string|null, data: any }) => void} onEvent Called once per complete event.
 * @returns {{ push: (text: string) => void, end: () => void }}
 */
export function createSseParser(onEvent) {
  let buffer = '';
  let name = null;
  let id = null;
  let data = [];

  function dispatch() {
    if (name === null && data.length === 0) return;
    const raw = data.join('\n');
    let parsed = raw;
    if (raw.length > 0) {
      try {
        parsed = JSON.parse(raw);
      } catch {
        parsed = raw;
      }
    }
    onEvent({ event: name || 'message', id, data: parsed });
    name = null;
    id = null;
    data = [];
  }

  function line(text) {
    if (text === '') {
      dispatch();
      return;
    }
    if (text.startsWith(':')) return;
    const colon = text.indexOf(':');
    const field = colon < 0 ? text : text.slice(0, colon);
    let value = colon < 0 ? '' : text.slice(colon + 1);
    if (value.startsWith(' ')) value = value.slice(1);
    if (field === 'event') name = value;
    else if (field === 'data') data.push(value);
    else if (field === 'id') id = value;
  }

  return {
    push(text) {
      buffer += String(text || '').replace(/\r\n/g, '\n').replace(/\r/g, '\n');
      let index = buffer.indexOf('\n');
      while (index >= 0) {
        line(buffer.slice(0, index));
        buffer = buffer.slice(index + 1);
        index = buffer.indexOf('\n');
      }
    },
    end() {
      if (buffer.length > 0) {
        line(buffer);
        buffer = '';
      }
      dispatch();
    }
  };
}

/**
 * Read a `fetch` response as server-sent events, yielding each event. Uses the body reader when the
 * runtime streams (browsers, `expo/fetch`), and falls back to reading the whole text otherwise.
 * @param {Response} response
 * @param {AbortSignal} [signal]
 * @returns {AsyncGenerator<{ event: string, id: string|null, data: any }>}
 */
export async function* readSseEvents(response, signal) {
  const queue = [];
  const parser = createSseParser((event) => queue.push(event));
  const body = response && response.body;
  if (body && typeof body.getReader === 'function') {
    const reader = body.getReader();
    const decoder = new TextDecoder();
    try {
      while (true) {
        if (signal && signal.aborted) break;
        const { value, done } = await reader.read();
        if (done) break;
        parser.push(typeof value === 'string' ? value : decoder.decode(value, { stream: true }));
        while (queue.length > 0) yield queue.shift();
      }
      parser.push(decoder.decode());
    } finally {
      try {
        reader.releaseLock();
      } catch {
        // The reader may already be released.
      }
    }
  } else {
    parser.push(await response.text());
  }
  parser.end();
  while (queue.length > 0) yield queue.shift();
}

/** Events that end a reply stream (exactly one arrives last). */
export const SSE_TERMINAL_EVENTS = Object.freeze(['reply.completed', 'reply.blocked', 'error']);

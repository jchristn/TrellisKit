/**
 * Deterministic doubles for `RealtimeClient` tests: a fake WebSocket class with server-side
 * controls, a fake clock (timers plus `now`), a seeded random source, and `flushAsync`.
 */

/** Let pending promise chains (including fetch over a fake) settle. */
export async function flushAsync(rounds = 8) {
  for (let i = 0; i < rounds; i += 1) {
    await new Promise((resolve) => (typeof globalThis.setImmediate === 'function' ? globalThis.setImmediate(resolve) : globalThis.setTimeout(resolve, 0)));
  }
}

/**
 * A fake clock: `setTimeout`, `clearTimeout`, `now`, and `advance(ms)` (runs due timers in order,
 * settling promises after each). Pass it as `timers` to `RealtimeClient` and the stores.
 * @param {number} [start]
 */
export function createFakeClock(start = 0) {
  let current = start;
  let nextId = 1;
  const timers = new Map();
  const clock = {
    now: () => current,
    setTimeout(fn, ms) {
      const id = nextId++;
      timers.set(id, { id, due: current + Math.max(0, Number(ms) || 0), fn });
      return id;
    },
    clearTimeout(id) { timers.delete(id); },
    /** Pending timers as `{ id, due }`, earliest first. */
    pending() { return Array.from(timers.values()).sort((a, b) => a.due - b.due || a.id - b.id).map(({ id, due }) => ({ id, due })); },
    /** Advance time, running every timer that falls due (including timers they schedule). */
    async advance(ms) {
      const target = current + Math.max(0, ms);
      await flushAsync();
      for (;;) {
        const due = Array.from(timers.values()).filter((t) => t.due <= target).sort((a, b) => a.due - b.due || a.id - b.id)[0];
        if (!due) break;
        timers.delete(due.id);
        current = due.due;
        due.fn();
        await flushAsync();
      }
      current = target;
      await flushAsync();
    }
  };
  return clock;
}

/** A random source that returns the given values in turn (then the last one). */
export function sequenceRandom(values) {
  const list = Array.isArray(values) && values.length ? values.slice() : [0.5];
  let i = 0;
  return () => {
    const v = list[Math.min(i, list.length - 1)];
    i += 1;
    return v;
  };
}

/**
 * A fake WebSocket class. Every instance is recorded in `instances`; `last()` is the newest.
 * Server-side controls on an instance: `accept()`, `serverSend(type, data)`, `serverSendRaw(text)`,
 * `serverClose(code, reason)`, `fail()`; `sentFrames` holds the parsed client frames.
 */
export function createFakeWebSocket() {
  const instances = [];
  let serverCounter = 0;

  class FakeWebSocket {
    constructor(url, protocols, options) {
      this.url = url;
      this.protocols = protocols;
      this.options = options;
      this.constructorArgs = arguments.length;
      this.readyState = FakeWebSocket.CONNECTING;
      this.sent = [];
      this.closeCalls = [];
      this.onopen = null;
      this.onmessage = null;
      this.onclose = null;
      this.onerror = null;
      instances.push(this);
    }

    get sentFrames() { return this.sent.map((text) => JSON.parse(text)); }

    framesOf(type) { return this.sentFrames.filter((f) => f.type === type); }

    send(text) {
      if (this.readyState !== FakeWebSocket.OPEN) throw new Error('Not open');
      this.sent.push(String(text));
    }

    close(code = 1000, reason = '') {
      if (this.readyState === FakeWebSocket.CLOSED) return;
      this.closeCalls.push({ code, reason });
      this.readyState = FakeWebSocket.CLOSED;
      const handler = this.onclose;
      queueMicrotask(() => { if (handler) handler({ code, reason, wasClean: true }); });
    }

    accept() {
      this.readyState = FakeWebSocket.OPEN;
      if (this.onopen) this.onopen({});
    }

    serverSend(type, data = {}) {
      serverCounter += 1;
      this.serverSendRaw(JSON.stringify({ type, id: 's-' + serverCounter, ts: '2026-11-12T12:00:00.000Z', data }));
    }

    serverSendRaw(text) {
      if (this.onmessage) this.onmessage({ data: text });
    }

    serverClose(code = 1000, reason = '') {
      this.readyState = FakeWebSocket.CLOSED;
      if (this.onclose) this.onclose({ code, reason, wasClean: true });
    }

    fail() {
      this.readyState = FakeWebSocket.CLOSED;
      if (this.onerror) this.onerror({});
      if (this.onclose) this.onclose({ code: 1006, reason: '', wasClean: false });
    }
  }
  FakeWebSocket.CONNECTING = 0;
  FakeWebSocket.OPEN = 1;
  FakeWebSocket.CLOSING = 2;
  FakeWebSocket.CLOSED = 3;

  return { WebSocket: FakeWebSocket, instances, last: () => instances[instances.length - 1] || null };
}

/** The `welcome` data a server sends (contract 4.5), with overrides. */
export function welcomeData(extra = {}) {
  return { connectionId: 'rtc_fake1', userId: 'usr_1', serverTime: '2026-11-12T12:00:00Z', heartbeatSeconds: 25, maxInboundFrameBytes: 16384, capabilities: ['messaging', 'notifications', 'tickets'], resume: { accepted: true, syncRequired: true }, ...extra };
}

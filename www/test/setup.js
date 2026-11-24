import '@testing-library/jest-dom/vitest';
import { afterEach, vi } from 'vitest';
import { cleanup } from '@testing-library/react';

afterEach(() => {
  cleanup();
  window.sessionStorage.clear();
  window.localStorage.clear();
});

if (!window.matchMedia) {
  window.matchMedia = (query) => ({
    matches: false,
    media: query,
    onchange: null,
    addEventListener: () => {},
    removeEventListener: () => {},
    addListener: () => {},
    removeListener: () => {},
    dispatchEvent: () => false
  });
}

// jsdom does not implement scrolling; stub it so layout effects do not log errors.
window.scrollTo = () => {};

// The member app opens one realtime socket per session. Tests must never reach the network, so
// the default WebSocket is inert (it never opens); realtime tests inject the shared fake socket.
class InertWebSocket {
  constructor(url) {
    this.url = url;
    this.readyState = 0;
  }
  send() {}
  close() { this.readyState = 3; }
}
InertWebSocket.CONNECTING = 0;
InertWebSocket.OPEN = 1;
InertWebSocket.CLOSING = 2;
InertWebSocket.CLOSED = 3;
window.WebSocket = InertWebSocket;
globalThis.WebSocket = InertWebSocket;

// jsdom has no canvas: uPlot charts (admin metrics, usage) render an inert stand-in. Test files that
// inspect the plots (usageViews) install their own recorder, which takes precedence.
vi.mock('uplot', () => {
  function InertPlot(opts, data, el) {
    this.opts = opts;
    this.data = data;
    this.el = el;
  }
  InertPlot.prototype.destroy = function destroy() {};
  InertPlot.prototype.setSize = function setSize() {};
  InertPlot.paths = { bars: () => null };
  InertPlot.tzDate = (date) => date;
  return { default: InertPlot };
});

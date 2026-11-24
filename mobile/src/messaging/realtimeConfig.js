import { AppState } from 'react-native';
import { resolveApiUrl } from '../config';

/**
 * Realtime wiring that tests replace: the WebSocket constructor (React Native's global by
 * default; null means the polling fallback only), whether the app opens a realtime client at all,
 * and the timer source. Production never calls the setters.
 */

let socketImpl;
let enabled = true;
let timers = null;

/** The WebSocket constructor for the realtime client, or null for polling only. */
export function getWebSocketImpl() {
  if (socketImpl !== undefined) return socketImpl;
  return typeof globalThis.WebSocket === 'function' ? globalThis.WebSocket : null;
}

/** Replace the WebSocket constructor (tests); `undefined` restores the default. */
export function setWebSocketImpl(impl) {
  socketImpl = impl;
}

/** Whether the app opens a realtime client for a signed-in session. */
export function isRealtimeEnabled() {
  return enabled;
}

/** Turn the realtime client on or off (tests). */
export function setRealtimeEnabled(value) {
  enabled = Boolean(value);
}

/** Timers for the realtime client and the background close (tests pass a fake clock). */
export function getRealtimeTimers() {
  return timers;
}

/** Replace the timers (tests); null restores the real ones. */
export function setRealtimeTimers(value) {
  timers = value || null;
}

/**
 * The WebSocket URL on the API domain (`wss://<api>/api/v1/ws`, contract section 4.1).
 * @param {string} [base] API origin (default: the configured API URL).
 */
export function realtimeUrl(base = resolveApiUrl()) {
  const origin = String(base || '').replace(/\/+$/, '');
  if (!origin) return null;
  return origin.replace(/^https:/i, 'wss:').replace(/^http:/i, 'ws:') + '/api/v1/ws';
}

/** Seconds in the background before the socket closes (contract section 19). */
export const BackgroundCloseMs = 30000;

/** Whether the app is in the foreground right now. */
export function appIsActive() {
  const state = AppState.currentState;
  return state === undefined || state === null || state === 'active' || state === 'unknown';
}

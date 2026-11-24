/**
 * A tiny in-memory event bus for content changes made on one screen that another screen shows
 * (a post created or edited in the composer appears in the feed without a reload).
 */
const listeners = new Map();

/**
 * @param {'post'|'postDeleted'|'blocked'} type
 * @param {(payload: any) => void} fn
 * @returns {() => void} Unsubscribe.
 */
export function onContentEvent(type, fn) {
  if (!listeners.has(type)) listeners.set(type, new Set());
  listeners.get(type).add(fn);
  return () => listeners.get(type).delete(fn);
}

/** Tell every listener of `type`. */
export function emitContentEvent(type, payload) {
  for (const fn of Array.from(listeners.get(type) || [])) {
    try {
      fn(payload);
    } catch {
      // One failing listener does not stop the others.
    }
  }
}

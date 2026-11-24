import { appIsActive } from './realtimeConfig';

/**
 * The conversation on screen right now (phone conversation screen or the tablet detail pane).
 * The push handler reads it so a `DirectMessage` push for the open conversation shows no banner
 * (contract section 4.9), and the realtime client sends it in the `state` frame.
 */

let activeId = null;
const listeners = new Set();

/** The open conversation id, or null. */
export function getActiveConversationId() {
  return activeId;
}

/** Set (or clear with null) the open conversation. */
export function setActiveConversationId(id) {
  const next = id || null;
  if (next === activeId) return;
  activeId = next;
  for (const fn of Array.from(listeners)) {
    try {
      fn(activeId);
    } catch {
      // Listener errors never break the screen.
    }
  }
}

/** Listen to changes; returns an unsubscribe function. */
export function onActiveConversationChange(fn) {
  listeners.add(fn);
  return () => listeners.delete(fn);
}

/**
 * Whether a push should be hidden: a `DirectMessage` for the conversation that is open while the
 * app is in the foreground.
 * @param {any} data The push `data` (`{ type, conversationId, seq, path }`).
 * @param {{ active?: boolean, conversationId?: string|null }} [state]
 */
export function shouldSuppressPush(data, { active = appIsActive(), conversationId = activeId } = {}) {
  if (!data || data.type !== 'DirectMessage' || !conversationId || !active) return false;
  return data.conversationId === conversationId;
}

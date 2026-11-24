/**
 * Block and unblock signals inside the app. A block hides the conversation with that member from
 * the blocker's lists and an unblock restores it (docs/PHASE5_CONTRACT.md section 3.2). The server
 * also publishes `conversation.updated` over the realtime socket, but the socket may be closed (or
 * polling), so the open conversation lists reload when this fires: an unblocked conversation comes
 * back without a manual refresh.
 */

const listeners = new Set();

/**
 * Subscribe to block changes.
 * @param {(change: { userId: string, blocked: boolean }) => void} listener
 * @returns {() => void} Unsubscribe.
 */
export function onBlocksChanged(listener) {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

/**
 * Tell the open lists that the member blocked or unblocked someone.
 * @param {string} userId The other member.
 * @param {boolean} blocked
 */
export function notifyBlocksChanged(userId, blocked) {
  for (const listener of Array.from(listeners)) {
    try {
      listener({ userId, blocked });
    } catch {
      // A listener's failure never stops the others.
    }
  }
}

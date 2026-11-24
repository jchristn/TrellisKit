import { useEffect } from 'react';

/** Keep-awake tag used while an assistant reply streams. */
export const StreamingKeepAwakeTag = 'tk.assistantReply';

function keepAwakeModule() {
  try {
    return require('expo-keep-awake');
  } catch {
    return null;
  }
}

/**
 * Keep the screen awake only while `active` (docs/AI_CONTRACT.md section 11.2: while a reply
 * streams). Released when `active` turns false and on unmount. Failures (no native module, a
 * platform that refuses) are ignored: the reply streams either way.
 * @param {boolean} active
 * @param {string} [tag]
 */
export function useKeepAwakeWhile(active, tag = StreamingKeepAwakeTag) {
  useEffect(() => {
    if (!active) return undefined;
    const mod = keepAwakeModule();
    if (!mod || typeof mod.activateKeepAwakeAsync !== 'function') return undefined;
    Promise.resolve()
      .then(() => mod.activateKeepAwakeAsync(tag))
      .catch(() => {});
    return () => {
      Promise.resolve()
        .then(() => (typeof mod.deactivateKeepAwake === 'function' ? mod.deactivateKeepAwake(tag) : undefined))
        .catch(() => {});
    };
  }, [active, tag]);
}

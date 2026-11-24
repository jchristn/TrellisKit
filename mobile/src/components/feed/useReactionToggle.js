import { useCallback, useEffect, useRef, useState } from 'react';
import { reactions as reactionsApi } from '../../api/community';
import { applyReaction, reactionFields } from '../../content/reactions';
import { describeContentError } from '../../content/errors';
import strings from '../../strings';
import { useAccessPrompt } from '../AccessPrompt';

function haptic() {
  try {
    const Haptics = require('expo-haptics');
    Haptics.selectionAsync().catch(() => {});
  } catch {
    // Haptics are optional.
  }
}

/**
 * Optimistic reaction toggling for one target (contract 6.2). The change shows at once (with a
 * selection haptic), then the server's `{ reactionCounts, reactionTotal, myReactions }` replaces
 * the local copy when no other toggle is still in flight. A failed request reverses just that
 * change and reports an error.
 * @param {object} options
 * @param {object} options.api
 * @param {{ type: string, id: string }} options.target The post's commentTarget, or the comment.
 * @param {object} options.fields Current reaction fields.
 * @param {(fields: object) => void} options.onChange
 */
export function useReactionToggle({ api, target, fields, onChange }) {
  const latest = useRef(reactionFields(fields));
  const pending = useRef(0);
  const [error, setError] = useState(null);
  const accessPrompt = useAccessPrompt();
  useEffect(() => {
    latest.current = reactionFields(fields);
  }, [fields]);

  const toggle = useCallback(
    async (key, on = !latest.current.myReactions.includes(key)) => {
      const next = applyReaction(latest.current, key, on);
      latest.current = next;
      onChange(next);
      setError(null);
      haptic();
      pending.current += 1;
      try {
        const result = await reactionsApi.toggle(api, target, key, on);
        pending.current -= 1;
        if (pending.current === 0 && result && result.reactionCounts) {
          latest.current = reactionFields(result);
          onChange(latest.current);
        }
      } catch (e) {
        pending.current -= 1;
        const reverted = applyReaction(latest.current, key, !on);
        latest.current = reverted;
        onChange(reverted);
        if (accessPrompt.handle(e)) return;
        setError(e && e.code && e.code !== 'NetworkError' ? describeContentError(e) : strings.reactions.failed);
      }
    },
    [api, target, onChange, accessPrompt]
  );

  return { toggle, error, clearError: () => setError(null) };
}

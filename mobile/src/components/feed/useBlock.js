import { useCallback, useState } from 'react';
import { safety } from '../../api/community';
import { describeContentError } from '../../content/errors';
import { notifyBlocksChanged } from '../../messaging/blockEvents';
import strings from '../../strings';

/**
 * Block and unblock a member (contract 9.2). Staff cannot be blocked: the server answers
 * `400 CannotBlockStaff`, and the app explains why instead of offering the action when it already
 * knows the member is staff (UserSummary.isStaff).
 */
export function useBlock(api) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(null);

  const run = useCallback(
    async (user, block) => {
      setError(null);
      if (block && user && user.isStaff) {
        setError(strings.block.staff);
        return { ok: false, code: 'CannotBlockStaff' };
      }
      setBusy(true);
      try {
        const result = block ? await safety.block(api, user.id) : await safety.unblock(api, user.id);
        const blocked = result ? Boolean(result.blocked) : block;
        notifyBlocksChanged(user.id, blocked);
        return { ok: true, blocked };
      } catch (e) {
        setError(describeContentError(e));
        return { ok: false, code: e && e.code };
      } finally {
        setBusy(false);
      }
    },
    [api]
  );

  return { block: (user) => run(user, true), unblock: (user) => run(user, false), busy, error, clearError: () => setError(null) };
}

import { useFocusEffect } from 'expo-router';
import { useCallback, useEffect, useRef, useState } from 'react';
import { useKillSwitch } from '../appConfig/AppContentProvider';
import { useAuth } from '../auth/AuthProvider';
import { useIsOffline } from '../hooks/useNetworkStatus';
import { fileStorage } from '../library/storage';
import { normalizeOverview, readCachedOverview, writeCachedOverview } from './hub';

/**
 * The signed-in member's Training overview (`GET /me/training`), shared by every Training
 * section screen. The last response is kept in memory (so switching sections does not reload)
 * and on the device for 24 hours, per member, so Continue training and downloads still show
 * offline. Signed-out callers never request it: `guest` is true and `overview` is null.
 *
 * `status` is `loading`, `ready`, `error`, or `libraryOnly`: the server refused the overview with
 * 402 `MembershipRequired`, 403, 404 `NotAvailable` (no section available or `features.training`
 * off), or 503 `FeatureDisabled` (the `training` kill switch), so the hub shows the Library alone.
 * `stale` is true while a cached copy is shown (network failures and other errors).
 */

const memory = new Map();

/** Overview refusals after which the hub falls back to the Library alone. */
export const LibraryOnlyStatuses = Object.freeze([402, 403, 404, 503]);

/** Forget the in-memory overviews (sign-out, tests). */
export function resetTrainingOverviewMemory() {
  memory.clear();
}

function initialState(userId) {
  if (!userId) return { status: 'ready', overview: null, stale: false, error: null };
  const hit = memory.get(userId);
  return hit ? { status: 'ready', overview: hit, stale: false, error: null } : { status: 'loading', overview: null, stale: false, error: null };
}

export function useTrainingOverview() {
  const { api, user } = useAuth();
  const offline = useIsOffline();
  const killed = useKillSwitch('training').killed;
  const userId = user ? user.id : null;
  const [state, setState] = useState(() => initialState(userId));
  const [stateUser, setStateUser] = useState(userId);
  const generation = useRef(0);
  if (stateUser !== userId) {
    setStateUser(userId);
    setState(initialState(userId));
  }

  const load = useCallback(async () => {
    const gen = (generation.current += 1);
    if (!userId) return;
    if (killed) {
      setState({ status: 'libraryOnly', overview: null, stale: false, error: null });
      return;
    }
    const fallback = async (error) => {
      if (error && LibraryOnlyStatuses.includes(error.status)) {
        memory.delete(userId);
        if (gen === generation.current) setState({ status: 'libraryOnly', overview: null, stale: false, error });
        return;
      }
      const cached = memory.get(userId) ? { overview: memory.get(userId) } : await readCachedOverview(fileStorage, userId);
      if (gen !== generation.current) return;
      if (cached) setState({ status: 'ready', overview: cached.overview, stale: true, error: error || null });
      else setState({ status: 'error', overview: null, stale: false, error: error || null });
    };
    if (offline) return fallback(null);
    try {
      const overview = normalizeOverview(await api.getTrainingOverview());
      if (gen !== generation.current) return;
      memory.set(userId, overview);
      setState({ status: 'ready', overview, stale: false, error: null });
      writeCachedOverview(fileStorage, userId, overview);
    } catch (error) {
      await fallback(error);
    }
  }, [api, userId, offline, killed]);

  useEffect(() => {
    // Loading is the effect's purpose: the request result (or the cached copy) becomes state.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    load();
  }, [load]);

  // The hub stays mounted: refresh quietly when a section regains focus, so a lesson finished
  // elsewhere leaves Continue training.
  const focused = useRef(false);
  useFocusEffect(useCallback(() => {
    if (focused.current) load();
    focused.current = true;
  }, [load]));

  return { ...state, guest: !userId, offline, reload: load };
}

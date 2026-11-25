import { propertyFeatureEnabled } from '@trelliskit/shared';
import { useEffect, useRef } from 'react';
import { AppState } from 'react-native';
import { useAuth } from '../auth/AuthProvider';
import { useIsOffline } from '../hooks/useNetworkStatus';
import { fileStorage } from '../library/storage';
import { useProperty } from '../property';
import { clearCachedOverview } from './hub';
import { clearSaveQueue, replaySaves, resetSaveState } from './saves';
import { resetTrainingOverviewMemory } from './useTrainingOverview';

/**
 * Background Training upkeep for a signed-in member: replays saves queued offline when the app
 * starts, comes back online, or returns to the foreground. When the member signs out (or another
 * member signs in) the previous member's queued saves, cached saved states, and cached overview are
 * removed from the device. Renders nothing.
 */
export default function TrainingSync() {
  const { api, user } = useAuth();
  const property = useProperty();
  const offline = useIsOffline();
  const enabled = propertyFeatureEnabled(property, 'saves');
  const userId = user ? user.id : null;
  const previous = useRef(null);

  useEffect(() => {
    const before = previous.current;
    if (before && before !== userId) {
      clearSaveQueue().catch(() => {});
      clearCachedOverview(fileStorage, before);
      resetSaveState();
      resetTrainingOverviewMemory();
    }
    previous.current = userId;
  }, [userId]);

  useEffect(() => {
    if (!userId || !enabled || offline || !api || typeof api.saveItem !== 'function') return undefined;
    const run = () => {
      replaySaves(api).catch(() => {});
    };
    run();
    const sub = AppState.addEventListener('change', (state) => {
      if (state === 'active') run();
    });
    return () => sub.remove();
  }, [api, userId, enabled, offline]);

  return null;
}

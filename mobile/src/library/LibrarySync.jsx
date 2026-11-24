import { libraryConfig } from '@trelliskit/shared';
import { useEffect, useRef } from 'react';
import { AppState } from 'react-native';
import { useAuth } from '../auth/AuthProvider';
import { featureOn } from '../content/phase4';
import { useIsOffline } from '../hooks/useNetworkStatus';
import { useProperty } from '../property';
import { clearAllDownloads, maintainDownloads } from './downloads';
import { clearProgressQueue, replayProgress } from './progress';

/**
 * Background library upkeep for a signed-in member: replays queued progress reports and keeps
 * download licenses valid when the app starts, comes back online, or returns to the foreground.
 * When the member signs out (or the session ends), queued progress and every downloaded file are
 * removed from the device (docs/PHASE4_CONTRACT.md sections 5.5 and 5.9). Renders nothing.
 */
export default function LibrarySync() {
  const { api, user } = useAuth();
  const property = useProperty();
  const offline = useIsOffline();
  const enabled = featureOn(property, 'library');
  const downloads = enabled && libraryConfig(property).downloadsEnabled;
  const userId = user ? user.id : null;
  const previous = useRef(null);

  useEffect(() => {
    if (previous.current && previous.current !== userId) {
      clearProgressQueue().catch(() => {});
      clearAllDownloads().catch(() => {});
    }
    previous.current = userId;
  }, [userId]);

  useEffect(() => {
    if (!userId || !enabled || offline) return undefined;
    const run = () => {
      replayProgress(api).catch(() => {});
      if (downloads) maintainDownloads(api).catch(() => {});
    };
    run();
    const sub = AppState.addEventListener('change', (state) => {
      if (state === 'active') run();
    });
    return () => sub.remove();
  }, [api, userId, enabled, downloads, offline]);

  return null;
}

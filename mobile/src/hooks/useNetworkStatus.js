import * as Network from 'expo-network';
import { useEffect, useState } from 'react';

/**
 * Whether a network state means the device is offline. Unknown values (null or undefined, for
 * example before Android has validated the connection) count as online so the banner never
 * flashes at launch.
 * @param {{ isConnected?: boolean|null, isInternetReachable?: boolean|null }|null} state
 */
export function isOfflineState(state) {
  return Boolean(state) && (state.isConnected === false || state.isInternetReachable === false);
}

/**
 * Track connectivity with expo-network (NWPathMonitor on iOS, ConnectivityManager on Android).
 * @returns {boolean} True while the device is offline.
 */
export function useIsOffline() {
  const [offline, setOffline] = useState(false);
  useEffect(() => {
    let active = true;
    let subscription = null;
    try {
      subscription = Network.addNetworkStateListener((state) => {
        if (active) setOffline(isOfflineState(state));
      });
    } catch {
      subscription = null;
    }
    Promise.resolve()
      .then(() => Network.getNetworkStateAsync())
      .then((state) => {
        if (active) setOffline(isOfflineState(state));
      })
      .catch(() => {});
    return () => {
      active = false;
      if (subscription && subscription.remove) subscription.remove();
    };
  }, []);
  return offline;
}

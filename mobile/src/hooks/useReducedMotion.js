import { useEffect, useState } from 'react';
import { AccessibilityInfo } from 'react-native';

/**
 * Whether the system Reduce Motion (iOS) or Remove animations (Android) setting is on.
 * @returns {boolean}
 */
export function useReducedMotion() {
  const [reduced, setReduced] = useState(false);
  useEffect(() => {
    let active = true;
    Promise.resolve()
      .then(() => AccessibilityInfo.isReduceMotionEnabled())
      .then((value) => {
        if (active) setReduced(Boolean(value));
      })
      .catch(() => {});
    let subscription = null;
    try {
      subscription = AccessibilityInfo.addEventListener('reduceMotionChanged', (value) => {
        if (active) setReduced(Boolean(value));
      });
    } catch {
      subscription = null;
    }
    return () => {
      active = false;
      if (subscription && subscription.remove) subscription.remove();
    };
  }, []);
  return reduced;
}

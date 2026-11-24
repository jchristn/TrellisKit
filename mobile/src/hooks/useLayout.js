import { useWindowDimensions } from 'react-native';
import { layout } from '../theme/tokens';

/**
 * Whether the current window is wide enough for the tablet layout.
 * @param {number} width Window width.
 * @returns {boolean}
 */
export function isWideWidth(width) {
  return typeof width === 'number' && width >= layout.wideBreakpoint;
}

/** Window-size facts used by layout components. Updates on rotation and split screen. */
export function useLayout() {
  const { width, height } = useWindowDimensions();
  return { width, height, isWide: isWideWidth(width) };
}

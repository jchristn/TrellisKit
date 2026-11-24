import { useEffect, useMemo, useState } from 'react';
import { Animated, PanResponder, Pressable, StyleSheet, View } from 'react-native';
import { useLayout } from '../../hooks/useLayout';
import { useTheme } from '../../theme';
import AppText from '../AppText';

/** Snap positions of the results sheet. */
export const SheetSnap = Object.freeze({ Collapsed: 0, Half: 1, Full: 2 });

/** Sheet heights for a window height: collapsed shows the handle and header, half and full show the list. */
export function snapHeights(height) {
  const h = Math.max(320, Number(height) || 640);
  return [Math.round(Math.min(150, h * 0.22)), Math.round(h * 0.45), Math.round(h * 0.82)];
}

/** The nearest snap for a dragged height, nudged one step by a fast flick. */
export function nearestSnap(heights, value, velocity = 0) {
  let best = 0;
  for (let i = 1; i < heights.length; i++) if (Math.abs(heights[i] - value) < Math.abs(heights[best] - value)) best = i;
  if (velocity < -0.8) best = Math.min(heights.length - 1, best + 1);
  if (velocity > 0.8) best = Math.max(0, best - 1);
  return best;
}

/**
 * The sheet's drag controller: the animated height, the handle's gesture, and snapping. Its state
 * lives in this closure; the component tells it the current heights and how to report a snap.
 */
function createSheetDrag(initialHeights, initialSnap) {
  let heights = initialHeights;
  let start = heights[initialSnap];
  let current = heights[initialSnap];
  let onSnap = () => {};
  const anim = new Animated.Value(current);
  const snapTo = (next) => {
    current = heights[next];
    onSnap(next);
    Animated.spring(anim, { toValue: heights[next], useNativeDriver: false, bounciness: 0 }).start();
  };
  const pan = PanResponder.create({
    onMoveShouldSetPanResponder: (_e, g) => Math.abs(g.dy) > 6,
    onPanResponderGrant: () => {
      start = current;
    },
    onPanResponderMove: (_e, g) => {
      current = Math.max(heights[0], Math.min(heights[heights.length - 1], start - g.dy));
      anim.setValue(current);
    },
    onPanResponderRelease: (_e, g) => snapTo(nearestSnap(heights, current, g.vy)),
    onPanResponderTerminate: () => snapTo(nearestSnap(heights, current, 0))
  });
  return {
    anim,
    pan,
    snapTo,
    bind(callback) {
      onSnap = callback;
    },
    resize(nextHeights, snap) {
      heights = nextHeights;
      current = heights[snap];
      anim.setValue(current);
    }
  };
}

/**
 * The draggable results sheet of a map search: it sits over the bottom of the map with a handle
 * that drags between collapsed, half, and full height (a fast flick moves one step), and the
 * handle is also a button that steps through the heights for screen readers and switch access
 * (accessibilityState.expanded). `snapRequest` ({ snap, seq }) opens it from outside, for example
 * when a map point is selected. Tablets get a wider card on the leading edge.
 * @param {object} props
 * @param {string} props.title Header text (for example the result count).
 * @param {string} props.expandLabel Handle label while collapsed or half open.
 * @param {string} props.collapseLabel Handle label while full.
 * @param {import('react').ReactNode} [props.header] Controls under the title.
 * @param {{ snap: number, seq: number }|null} [props.snapRequest]
 */
export default function ResultsSheet({ title, expandLabel, collapseLabel, header = null, children, snapRequest = null, testID = 'results-sheet' }) {
  const theme = useTheme();
  const { height, isWide } = useLayout();
  const heights = useMemo(() => snapHeights(height), [height]);
  const [snap, setSnap] = useState(SheetSnap.Half);
  const [drag] = useState(() => createSheetDrag(heights, SheetSnap.Half));

  useEffect(() => {
    drag.bind(setSnap);
  }, [drag]);

  useEffect(() => {
    drag.resize(heights, snap);
    // Re-apply the snap when the window height changes (rotation, split screen).
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [heights]);

  const requestKey = snapRequest ? snapRequest.seq : null;
  useEffect(() => {
    if (snapRequest && snapRequest.snap !== undefined) drag.snapTo(snapRequest.snap);
    // requestKey stands for snapRequest.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [requestKey]);

  const go = (next) => drag.snapTo(next);

  const full = snap === SheetSnap.Full;
  return (
    <Animated.View
      testID={testID}
      style={[
        styles.sheet,
        {
          height: drag.anim,
          backgroundColor: theme.colors.background,
          borderColor: theme.colors.border,
          borderTopLeftRadius: theme.radii.lg,
          borderTopRightRadius: theme.radii.lg,
          left: isWide ? theme.spacing.lg : 0,
          right: isWide ? undefined : 0,
          width: isWide ? theme.layout.listPaneWidth : undefined
        }
      ]}
    >
      <View {...drag.pan.panHandlers}>
        <Pressable
          onPress={() => go(full ? SheetSnap.Collapsed : snap + 1)}
          accessibilityRole="button"
          accessibilityLabel={full ? collapseLabel : expandLabel}
          accessibilityState={{ expanded: snap !== SheetSnap.Collapsed }}
          testID={testID + '-handle'}
          style={{ alignItems: 'center', paddingTop: theme.spacing.sm, paddingBottom: theme.spacing.xs }}
        >
          <View style={[styles.grip, { backgroundColor: theme.colors.border }]} />
        </Pressable>
        <View style={{ paddingHorizontal: theme.spacing.lg, gap: theme.spacing.sm, paddingBottom: theme.spacing.sm }}>
          <AppText variant="heading" accessibilityRole="header" testID={testID + '-title'}>
            {title}
          </AppText>
          {header}
        </View>
      </View>
      <View style={styles.fill}>{children}</View>
    </Animated.View>
  );
}

const styles = StyleSheet.create({
  fill: { flex: 1 },
  sheet: { position: 'absolute', bottom: 0, borderWidth: StyleSheet.hairlineWidth, overflow: 'hidden' },
  grip: { width: 40, height: 5, borderRadius: 3 }
});

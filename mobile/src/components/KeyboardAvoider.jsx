import { useCallback, useRef, useState } from 'react';
import { KeyboardAvoidingView, Platform, View } from 'react-native';

/**
 * The KeyboardAvoidingView props for a platform: `padding` everywhere, with the caller's header
 * offset on iOS and the measured window position of the view on Android.
 * @param {string} os Platform.OS.
 * @param {number} iosOffset Header height on iOS.
 * @param {number} measuredTop Where the view starts in the window (Android).
 * @returns {{ behavior: 'padding', keyboardVerticalOffset: number }}
 */
export function keyboardAvoidingProps(os, iosOffset, measuredTop) {
  return { behavior: 'padding', keyboardVerticalOffset: os === 'ios' ? iosOffset || 0 : measuredTop || 0 };
}

/**
 * Keeps a screen's bottom composer above the software keyboard on both platforms.
 *
 * iOS keeps the `padding` behavior with the caller's fixed header offset. Android builds run edge
 * to edge (`edgeToEdgeEnabled`), so `adjustResize` no longer shrinks the window when the keyboard
 * opens and the composer would sit behind it; Android therefore uses `padding` too, with the
 * offset measured from where this view starts in the window (the header and status bar above it).
 * @param {object} props
 * @param {number} [props.iosOffset] keyboardVerticalOffset on iOS (the header height).
 * @param {object} [props.style] Style of the avoiding view (usually `{ flex: 1 }`).
 * @param {string} [props.testID]
 * @param {string} [props.os] Platform override for tests.
 */
export default function KeyboardAvoider({ iosOffset = 0, style, testID, children, os = Platform.OS }) {
  const wrapper = useRef(null);
  const [top, setTop] = useState(0);
  const measure = useCallback(() => {
    const node = wrapper.current;
    if (os === 'ios' || !node || typeof node.measureInWindow !== 'function') return;
    node.measureInWindow((_x, y) => {
      if (typeof y === 'number' && Number.isFinite(y)) setTop(Math.max(0, Math.round(y)));
    });
  }, [os]);
  if (os === 'ios') {
    return (
      <KeyboardAvoidingView style={style} {...keyboardAvoidingProps(os, iosOffset, 0)} testID={testID}>
        {children}
      </KeyboardAvoidingView>
    );
  }
  return (
    <View ref={wrapper} style={{ flex: 1 }} onLayout={measure} collapsable={false}>
      <KeyboardAvoidingView style={style} {...keyboardAvoidingProps(os, iosOffset, top)} testID={testID}>
        {children}
      </KeyboardAvoidingView>
    </View>
  );
}

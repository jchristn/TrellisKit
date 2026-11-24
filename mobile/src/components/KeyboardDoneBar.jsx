import { useEffect, useState } from 'react';
import { Dimensions, Keyboard, LayoutAnimation, Platform, Pressable, View } from 'react-native';
import strings from '../strings';
import { useTheme } from '../theme';
import AppText from './AppText';

const HIDDEN = { visible: false, height: 0 };

/**
 * The space a keyboard frame covers at the bottom of the window: its top edge to the window's
 * bottom edge. A floating or undocked keyboard (tablets) covers none, so content keeps its place.
 * @param {{ screenY?: number, height?: number }|null|undefined} frame Keyboard end coordinates.
 * @param {number} windowHeight Window height.
 * @returns {number}
 */
export function keyboardInset(frame, windowHeight) {
  if (!frame || typeof frame.screenY !== 'number' || typeof frame.height !== 'number' || !(windowHeight > 0)) return 0;
  if (frame.height <= 0 || frame.screenY >= windowHeight) return 0;
  const docked = frame.screenY + frame.height >= windowHeight - 1;
  return docked ? Math.max(0, Math.round(windowHeight - frame.screenY)) : 0;
}

/**
 * Whether the software keyboard is showing, and how much of the window's bottom it covers.
 * iOS only (Android has the system back control): every other platform reports hidden.
 * @param {string} [os] Platform override for tests.
 * @returns {{ visible: boolean, height: number }}
 */
export function useKeyboardFrame(os = Platform.OS) {
  const [frame, setFrame] = useState(HIDDEN);
  useEffect(() => {
    if (os !== 'ios') return undefined;
    const animate = (event) => {
      // Move with the keyboard instead of jumping (the same curve the system uses).
      if (event && event.duration > 0) LayoutAnimation.configureNext({ duration: event.duration, update: { duration: event.duration, type: LayoutAnimation.Types.keyboard } });
    };
    const show = Keyboard.addListener('keyboardWillShow', (event) => {
      animate(event);
      setFrame({ visible: true, height: keyboardInset(event && event.endCoordinates, Dimensions.get('window').height) });
    });
    const hide = Keyboard.addListener('keyboardWillHide', (event) => {
      animate(event);
      setFrame(HIDDEN);
    });
    return () => {
      show.remove();
      hide.remove();
    };
  }, [os]);
  return frame;
}

/**
 * iOS bar with a Done button that dismisses the keyboard, followed by a spacer as tall as the
 * keyboard. Number, phone, and multiline keyboards have no return key that closes them, so
 * without this a member cannot reach buttons under the keyboard.
 *
 * It is laid out in the normal flow as the last child of a full-height column (the app root and
 * every modal that holds inputs), so while the keyboard is open the content above it ends at the
 * bar: nothing is hidden behind the keyboard, and a composer that avoids the keyboard itself
 * (KeyboardAvoider) finds it is already clear and adds nothing. It does not depend on the native
 * input accessory view. Android uses the system back control and renders nothing.
 * @param {object} props
 * @param {string} [props.os] Platform override for tests.
 */
export default function KeyboardDoneBar({ os = Platform.OS }) {
  const theme = useTheme();
  const frame = useKeyboardFrame(os);
  if (os !== 'ios' || !frame.visible) return null;
  return (
    <View testID="keyboard-done-bar" style={{ backgroundColor: theme.colors.surfaceRaised }}>
      <View style={{ flexDirection: 'row', justifyContent: 'flex-end', paddingHorizontal: theme.spacing.md, paddingVertical: theme.spacing.xs, borderTopWidth: 1, borderTopColor: theme.colors.border }}>
        <Pressable onPress={() => Keyboard.dismiss()} accessibilityRole="button" accessibilityLabel={strings.common.keyboardDone} hitSlop={8} testID="keyboard-done" style={{ minHeight: 36, justifyContent: 'center', paddingHorizontal: theme.spacing.sm }}>
          <AppText style={{ fontWeight: '600' }} color="primary">
            {strings.common.keyboardDone}
          </AppText>
        </Pressable>
      </View>
      <View style={{ height: frame.height }} />
    </View>
  );
}

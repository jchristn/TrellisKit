import { Modal, Pressable, ScrollView, StyleSheet, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { useLayout } from '../hooks/useLayout';
import strings from '../strings';
import { useTheme } from '../theme';
import AppText from './AppText';
import Button from './Button';
import KeyboardDoneBar from './KeyboardDoneBar';

/**
 * Bottom sheet (a modal card) for menus, pickers, and short flows. On wide windows it is a
 * centered card. The backdrop and the Close button dismiss it; screen readers stay inside it.
 * While the iOS keyboard is open the sheet sits above it, with the Done bar in between.
 * @param {object} props
 * @param {boolean} props.visible
 * @param {string} props.title
 * @param {() => void} props.onClose
 * @param {boolean} [props.scroll] Scroll the body (default true).
 */
export default function Sheet(props) {
  // Hidden sheets render nothing, so lists of cards with menus stay cheap.
  return props.visible ? <SheetBody {...props} /> : null;
}

function SheetBody({ visible, title, onClose, children, scroll = true, testID }) {
  const theme = useTheme();
  const insets = useSafeAreaInsets();
  const { isWide, height } = useLayout();
  const body = <View style={{ gap: theme.spacing.md }}>{children}</View>;
  return (
    <Modal visible={visible} transparent animationType="fade" onRequestClose={onClose} supportedOrientations={['portrait', 'landscape']}>
      <View style={styles.fill}>
      <View style={[styles.fill, { justifyContent: isWide ? 'center' : 'flex-end' }]}>
        <Pressable style={[StyleSheet.absoluteFill, { backgroundColor: 'rgba(0,0,0,0.45)' }]} onPress={onClose} accessibilityElementsHidden importantForAccessibility="no-hide-descendants" />
        <View
          testID={testID}
          accessibilityViewIsModal
          style={{
            backgroundColor: theme.colors.background,
            borderTopLeftRadius: theme.radii.lg,
            borderTopRightRadius: theme.radii.lg,
            borderRadius: isWide ? theme.radii.lg : undefined,
            maxHeight: height * 0.85,
            width: isWide ? theme.layout.maxContentWidth : '100%',
            alignSelf: 'center',
            paddingHorizontal: theme.spacing.lg,
            paddingTop: theme.spacing.lg,
            paddingBottom: Math.max(insets.bottom, theme.spacing.lg),
            gap: theme.spacing.md
          }}
        >
          <View style={{ flexDirection: 'row', alignItems: 'center', gap: theme.spacing.md }}>
            <AppText variant="heading" accessibilityRole="header" style={{ flex: 1 }}>
              {title}
            </AppText>
            <Button label={strings.postMenu.close} variant="ghost" onPress={onClose} style={{ alignSelf: 'auto' }} testID={testID ? testID + '-close' : undefined} />
          </View>
          {scroll ? <ScrollView keyboardShouldPersistTaps="handled">{body}</ScrollView> : body}
        </View>
      </View>
      {/* A modal is its own window, so it carries its own Done bar; the sheet rises above the keyboard with it. */}
      <KeyboardDoneBar />
      </View>
    </Modal>
  );
}

const styles = StyleSheet.create({
  fill: { flex: 1 }
});

import Ionicons from '@expo/vector-icons/Ionicons';
import { Pressable, StyleSheet, View } from 'react-native';
import { useTheme } from '../theme';
import AppText from './AppText';

/**
 * Icon-only (or icon and short text) button with a 44 point target. `label` is the accessible
 * name; `text` is optional visible text next to the icon.
 */
export default function IconButton({ icon, label, text, onPress, onLongPress, color = 'text', disabled = false, selected, accessibilityHint, badge, testID, style, buttonRef }) {
  const theme = useTheme();
  const tint = theme.colors[color] || color;
  return (
    <Pressable
      ref={buttonRef}
      testID={testID}
      onPress={disabled ? undefined : onPress}
      onLongPress={disabled ? undefined : onLongPress}
      disabled={disabled}
      accessibilityRole="button"
      accessibilityLabel={label}
      accessibilityHint={accessibilityHint}
      accessibilityState={selected === undefined ? { disabled } : { disabled, selected }}
      hitSlop={4}
      style={({ pressed }) => [styles.base, { minHeight: theme.layout.minTouchTarget, minWidth: theme.layout.minTouchTarget, paddingHorizontal: text ? theme.spacing.sm : 0, opacity: disabled ? 0.5 : pressed ? 0.6 : 1 }, style]}
    >
      <View style={[styles.row, { gap: theme.spacing.xs }]}>
        <Ionicons name={icon} size={22} color={tint} />
        {text ? <AppText color={color}>{text}</AppText> : null}
      </View>
      {badge ? (
        <View testID={testID ? testID + '-badge' : undefined} style={[styles.badge, { backgroundColor: theme.colors.danger, borderRadius: theme.radii.pill }]} importantForAccessibility="no" accessibilityElementsHidden>
          <AppText variant="caption" style={{ color: theme.dark ? '#0E1116' : '#FFFFFF', fontWeight: '700' }}>
            {badge}
          </AppText>
        </View>
      ) : null}
    </Pressable>
  );
}

const styles = StyleSheet.create({
  base: { alignItems: 'center', justifyContent: 'center' },
  row: { flexDirection: 'row', alignItems: 'center' },
  badge: { position: 'absolute', top: 2, right: 0, minWidth: 18, paddingHorizontal: 4, alignItems: 'center' }
});

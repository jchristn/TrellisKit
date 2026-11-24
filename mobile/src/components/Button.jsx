import { Pressable, StyleSheet, View } from 'react-native';
import { useTheme } from '../theme';
import AppText from './AppText';

/**
 * Accessible button. Grows with its label (no fixed height) and keeps a 44 point minimum
 * touch target.
 * @param {object} props
 * @param {string} props.label Visible label, also the default accessibility label.
 * @param {'primary'|'secondary'|'ghost'} [props.variant]
 * @param {boolean} [props.disabled]
 * @param {string} [props.accessibilityHint]
 * @param {React.ReactNode} [props.icon] Optional leading icon element.
 */
export default function Button({ label, onPress, variant = 'primary', disabled = false, accessibilityLabel, accessibilityHint, icon, style, testID }) {
  const theme = useTheme();
  const { colors, spacing, radii, layout } = theme;

  let background = colors.primary;
  let foreground = 'onPrimary';
  let borderColor = colors.primary;
  if (variant === 'secondary') {
    background = colors.secondary;
    foreground = 'onSecondary';
    borderColor = colors.border;
  } else if (variant === 'ghost') {
    background = 'transparent';
    foreground = 'primary';
    borderColor = 'transparent';
  }
  if (disabled) {
    background = colors.disabled;
    foreground = 'onDisabled';
    borderColor = colors.border;
  }

  return (
    <Pressable
      testID={testID}
      onPress={disabled ? undefined : onPress}
      disabled={disabled}
      accessibilityRole="button"
      accessibilityLabel={accessibilityLabel || label}
      accessibilityHint={accessibilityHint}
      accessibilityState={{ disabled }}
      style={({ pressed }) => [
        styles.base,
        {
          minHeight: layout.minTouchTarget,
          paddingHorizontal: spacing.lg,
          paddingVertical: spacing.md,
          borderRadius: radii.md,
          backgroundColor: background,
          borderColor,
          opacity: pressed && !disabled ? 0.85 : 1
        },
        style
      ]}
    >
      <View style={[styles.row, { gap: spacing.sm }]}>
        {icon}
        <AppText variant="label" color={foreground} style={styles.label}>
          {label}
        </AppText>
      </View>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  base: {
    alignSelf: 'stretch',
    borderWidth: 1,
    justifyContent: 'center'
  },
  row: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'center',
    flexWrap: 'wrap'
  },
  label: {
    textAlign: 'center',
    flexShrink: 1
  }
});

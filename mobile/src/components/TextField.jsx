import { TextInput, View } from 'react-native';
import { useTheme } from '../theme';
import AppText from './AppText';

/**
 * Labeled text input with optional hint and error. The label is the input's accessibility label
 * (the visible label is hidden from screen readers so it is not read twice), the hint and any
 * error are its accessibility hint (`hideLabel` drops the visible label when a wrapper shows it), and a new error is announced as an alert. The input grows
 * with the system text size (minimum height only). `compact` renders a one-line field (a multiline
 * field keeps growing as text wraps) with the label as its placeholder and no visible hint.
 * Text capitalizes sentences unless `autoCapitalize` says otherwise, single-line text is vertically
 * centered (no fixed line height, which iOS draws off center). On iOS the app's Done bar
 * (KeyboardDoneBar) sits above every keyboard, so number and multiline keyboards can be dismissed.
 */
export default function TextField({ label, value, onChangeText, hint, error, status, statusColor = 'textMuted', testID, style, multiline, compact = false, hideLabel = false, placeholder, ...inputProps }) {
  const theme = useTheme();
  const { colors, spacing, radii, typography, layout } = theme;
  return (
    <View style={[{ gap: spacing.xs }, style]}>
      {compact || hideLabel ? null : (
        <AppText variant="label" accessibilityElementsHidden importantForAccessibility="no">
          {label}
        </AppText>
      )}
      <TextInput
        testID={testID}
        value={value}
        onChangeText={onChangeText}
        accessibilityLabel={label}
        accessibilityHint={[error, hint].filter(Boolean).join('. ') || undefined}
        placeholderTextColor={colors.textMuted}
        multiline={multiline}
        autoCapitalize="sentences"
        placeholder={placeholder || (compact ? label : undefined)}
        style={[
          typography.body,
          multiline ? null : { lineHeight: undefined, paddingVertical: 0 },
          {
            color: colors.text,
            backgroundColor: colors.surface,
            borderColor: error ? colors.danger : colors.border,
            borderWidth: 1,
            borderRadius: radii.md,
            paddingHorizontal: spacing.md,
            paddingVertical: multiline ? spacing.sm : 0,
            minHeight: multiline && !compact ? 96 : layout.minTouchTarget,
            textAlignVertical: multiline && !compact ? 'top' : 'center'
          }
        ]}
        {...inputProps}
      />
      {status ? (
        <AppText variant="caption" color={statusColor} testID={testID ? testID + '-status' : undefined}>
          {status}
        </AppText>
      ) : null}
      {hint && !error && !compact ? (
        <AppText variant="caption" color="textMuted">
          {hint}
        </AppText>
      ) : null}
      {error ? (
        <AppText variant="caption" color="danger" accessibilityRole="alert" testID={testID ? testID + '-error' : undefined}>
          {error}
        </AppText>
      ) : null}
    </View>
  );
}

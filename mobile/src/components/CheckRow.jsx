import { Switch, View } from 'react-native';
import { useTheme } from '../theme';
import AppText from './AppText';

/** A switch with a visible label, for agreements and toggles. The switch carries the label for screen readers. */
export default function CheckRow({ label, accessibilityLabel, value, onValueChange, error, testID, children }) {
  const theme = useTheme();
  return (
    <View style={{ gap: theme.spacing.xs }}>
      <View style={{ flexDirection: 'row', alignItems: 'center', gap: theme.spacing.md, minHeight: theme.layout.minTouchTarget }}>
        <Switch
          testID={testID}
          value={value}
          onValueChange={onValueChange}
          accessibilityLabel={accessibilityLabel || label}
          accessibilityHint={error || undefined}
          trackColor={{ true: theme.colors.primary, false: theme.colors.border }}
        />
        <AppText style={{ flex: 1 }} accessibilityElementsHidden importantForAccessibility="no">
          {label}
        </AppText>
      </View>
      {children}
      {error ? (
        <AppText variant="caption" color="danger" accessibilityRole="alert">
          {error}
        </AppText>
      ) : null}
    </View>
  );
}

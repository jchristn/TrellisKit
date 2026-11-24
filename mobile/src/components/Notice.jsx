import { View } from 'react-native';
import { useTheme } from '../theme';
import AppText from './AppText';

/**
 * Inline message box. `tone` is error, success, or info. Errors are announced as alerts.
 */
export default function Notice({ tone = 'info', children, testID }) {
  const theme = useTheme();
  const color = tone === 'error' ? 'danger' : tone === 'success' ? 'success' : 'text';
  if (!children) return null;
  return (
    <View
      testID={testID}
      accessibilityRole={tone === 'error' ? 'alert' : undefined}
      accessibilityLiveRegion="polite"
      style={{
        borderWidth: 1,
        borderColor: tone === 'error' ? theme.colors.danger : theme.colors.border,
        backgroundColor: theme.colors.surface,
        borderRadius: theme.radii.md,
        padding: theme.spacing.md
      }}
    >
      <AppText color={color}>{children}</AppText>
    </View>
  );
}

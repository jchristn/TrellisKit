import { ActivityIndicator, View } from 'react-native';
import { useTheme } from '../theme';
import AppText from './AppText';

/** Centered spinner with an accessible label. */
export default function Loading({ label, testID }) {
  const theme = useTheme();
  return (
    <View testID={testID} accessible accessibilityRole="progressbar" accessibilityLabel={label} accessibilityState={{ busy: true }} style={{ flex: 1, alignItems: 'center', justifyContent: 'center', gap: theme.spacing.md, padding: theme.spacing.xl }}>
      <ActivityIndicator color={theme.colors.primary} />
      {label ? <AppText color="textMuted">{label}</AppText> : null}
    </View>
  );
}

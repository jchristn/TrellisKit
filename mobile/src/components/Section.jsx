import { View } from 'react-native';
import { useTheme } from '../theme';
import AppText from './AppText';
import Card from './Card';

/** Titled card section used on settings screens. */
export default function Section({ title, children, testID }) {
  const theme = useTheme();
  return (
    <View style={{ gap: theme.spacing.sm }} testID={testID}>
      {title ? (
        <AppText variant="heading" accessibilityRole="header">
          {title}
        </AppText>
      ) : null}
      <Card>{children}</Card>
    </View>
  );
}

/** Label and value on one line. */
export function InfoRow({ label, value, valueColor = 'text', testID }) {
  const theme = useTheme();
  return (
    <View accessible accessibilityLabel={label + ': ' + value} style={{ flexDirection: 'row', flexWrap: 'wrap', justifyContent: 'space-between', gap: theme.spacing.sm }}>
      <AppText color="textMuted">{label}</AppText>
      <AppText testID={testID} color={valueColor} style={{ fontWeight: '600', flexShrink: 1, textAlign: 'right' }}>
        {value}
      </AppText>
    </View>
  );
}

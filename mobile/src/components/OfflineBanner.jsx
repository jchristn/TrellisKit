import Ionicons from '@expo/vector-icons/Ionicons';
import { View } from 'react-native';
import strings from '../strings';
import { useTheme } from '../theme';
import AppText from './AppText';

/**
 * Full-width notice shown at the top of the app while the device is offline. It appears and
 * disappears without animation and is announced once by screen readers.
 * @param {object} props
 * @param {boolean} props.offline
 * @param {number} [props.topInset] Safe area inset to pad above the text.
 */
export default function OfflineBanner({ offline, topInset = 0 }) {
  const theme = useTheme();
  if (!offline) return null;
  return (
    <View
      testID="offline-banner"
      accessible
      accessibilityRole="alert"
      accessibilityLiveRegion="polite"
      accessibilityLabel={strings.offline.banner}
      style={{
        paddingTop: topInset + theme.spacing.sm,
        paddingBottom: theme.spacing.sm,
        paddingHorizontal: theme.spacing.lg,
        backgroundColor: theme.colors.text,
        flexDirection: 'row',
        alignItems: 'center',
        gap: theme.spacing.sm
      }}
    >
      <Ionicons name="cloud-offline-outline" size={20} color={theme.colors.background} importantForAccessibility="no" accessibilityElementsHidden />
      <AppText color="background" style={{ flex: 1 }}>
        {strings.offline.banner}
      </AppText>
    </View>
  );
}

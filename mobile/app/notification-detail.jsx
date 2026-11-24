import { Stack, useLocalSearchParams, useRouter } from 'expo-router';
import AppText from '../src/components/AppText';
import Button from '../src/components/Button';
import MemberGate from '../src/components/MemberGate';
import Screen from '../src/components/Screen';
import strings from '../src/strings';
import { useTheme } from '../src/theme';

/** Staff destinations without native administrative actions retain the original notification text. */
export default function NotificationDetailScreen() {
  const params = useLocalSearchParams();
  const theme = useTheme();
  const router = useRouter();
  const text = (value, max) => typeof value === 'string' ? value.slice(0, max) : '';
  const title = text(params.notificationTitle, 1000) || strings.nativeMetrics.detailTitle;
  const body = text(params.notificationBody, 8000);
  return <MemberGate><Screen testID="screen-notification-detail" contentStyle={{ gap: theme.spacing.lg }}>
    <Stack.Screen options={{ title: strings.nativeMetrics.detailTitle }} />
    <AppText variant="title" accessibilityRole="header">{title}</AppText>
    <AppText>{body || strings.nativeMetrics.detailEmpty}</AppText>
    <AppText color="textMuted">{strings.nativeMetrics.staffDetail}</AppText>
    <Button label={strings.nativeMetrics.viewNotifications} onPress={() => router.replace('/notifications')} />
  </Screen></MemberGate>;
}

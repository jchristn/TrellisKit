import { useRouter } from 'expo-router';
import { useState } from 'react';
import { View } from 'react-native';
import AppText from '../../src/components/AppText';
import Button from '../../src/components/Button';
import Loading from '../../src/components/Loading';
import Screen from '../../src/components/Screen';
import { useAuth } from '../../src/auth/AuthProvider';
import { Routes } from '../../src/auth/routing';
import { enablePushNotifications } from '../../src/notifications/push';
import strings from '../../src/strings';
import { useTheme } from '../../src/theme';

/**
 * Notification permission step, shown once per device. "Turn on notifications" asks for
 * permission, then stores and registers the Expo push token. Either choice continues to the feed;
 * a denied or unavailable permission never blocks the member.
 */
export default function AllowNotificationsScreen() {
  const theme = useTheme();
  const router = useRouter();
  const { api, user, markNotificationsPrompted } = useAuth();
  const [busy, setBusy] = useState(false);
  const t = strings.allowNotifications;

  async function finish() {
    await markNotificationsPrompted();
    router.replace(Routes.feed);
  }

  async function enable() {
    setBusy(true);
    try {
      await enablePushNotifications(api, { userId: user ? user.id : '' });
    } catch {
      // Permission problems never block onboarding.
    }
    await finish();
  }

  return (
    <Screen contentStyle={{ gap: theme.spacing.lg }} testID="screen-allow-notifications">
      <AppText variant="title" accessibilityRole="header">
        {t.heading}
      </AppText>
      <AppText color="textMuted">{t.body}</AppText>
      <View style={{ gap: theme.spacing.md }}>
        <Button label={t.enable} accessibilityHint={t.enableHint} onPress={enable} disabled={busy} testID="allow-notifications-enable" />
        <Button label={t.continue} accessibilityHint={t.continueHint} variant="ghost" onPress={finish} disabled={busy} testID="allow-notifications-continue" />
      </View>
      {busy ? <Loading label={t.working} /> : null}
    </Screen>
  );
}

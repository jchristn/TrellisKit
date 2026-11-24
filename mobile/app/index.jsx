import { Redirect } from 'expo-router';
import { View } from 'react-native';
import AppText from '../src/components/AppText';
import Button from '../src/components/Button';
import Loading from '../src/components/Loading';
import Screen from '../src/components/Screen';
import { useAuth } from '../src/auth/AuthProvider';
import { nextRoute } from '../src/auth/routing';
import strings from '../src/strings';
import { useTheme } from '../src/theme';

/**
 * Entry route. Restores the session from secure storage, then sends the member to the right
 * step: welcome, age attestation, profile setup, the paywall, or the feed.
 */
export default function Index() {
  const theme = useTheme();
  const { session, restore, signOut } = useAuth();

  if (session.status === 'loading') return <Loading label={strings.launch.restoring} testID="launch-loading" />;

  if (session.status === 'offline') {
    return (
      <Screen edges={['top', 'bottom', 'left', 'right']} contentStyle={{ flexGrow: 1, justifyContent: 'center', gap: theme.spacing.lg }}>
        <View style={{ gap: theme.spacing.sm }} testID="launch-offline">
          <AppText variant="title" accessibilityRole="header">
            {strings.launch.offlineTitle}
          </AppText>
          <AppText color="textMuted">{strings.launch.offlineBody}</AppText>
        </View>
        <Button label={strings.common.retry} onPress={restore} />
        <Button label={strings.launch.signOut} variant="ghost" onPress={() => signOut({ revoke: false })} />
      </Screen>
    );
  }

  return <Redirect href={nextRoute(session)} />;
}

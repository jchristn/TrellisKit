import { Redirect, Stack } from 'expo-router';
import Loading from '../../src/components/Loading';
import { useAuth } from '../../src/auth/AuthProvider';
import { nextRoute } from '../../src/auth/routing';
import strings from '../../src/strings';
import { useNestedStackOptions } from '../../src/hooks/useStackOptions';

/**
 * Account settings. Requires a session but not a membership, so a member whose membership has
 * lapsed can still manage sign-in methods, export data, or delete the account.
 */
export default function AccountLayout() {
  const screenOptions = useNestedStackOptions();
  const { session } = useAuth();
  if (session.status === 'loading') return <Loading label={strings.common.loading} />;
  if (session.status !== 'signedIn') return <Redirect href={nextRoute(session)} />;
  return (
    <Stack screenOptions={screenOptions}>
      <Stack.Screen name="index" options={{ title: strings.account.title }} />
      <Stack.Screen name="edit-profile" options={{ title: strings.account.editProfileTitle }} />
      <Stack.Screen name="sign-in-methods" options={{ title: strings.signInMethods.title }} />
      <Stack.Screen name="password" options={{ title: strings.password.changeTitle }} />
      <Stack.Screen name="membership" options={{ title: strings.membership.title }} />
      <Stack.Screen name="delete" options={{ title: strings.deleteAccount.title }} />
      <Stack.Screen name="notifications" options={{ title: strings.notificationSettings.title }} />
      <Stack.Screen name="blocked" options={{ title: strings.blocked.title }} />
      <Stack.Screen name="messaging" options={{ title: strings.messaging.settingsTitle }} />
      <Stack.Screen name="appearance" options={{ title: strings.appearance.title }} />
      <Stack.Screen name="legal" options={{ title: strings.documents.legalTitle }} />
    </Stack>
  );
}

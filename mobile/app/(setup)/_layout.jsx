import { Redirect, Stack } from 'expo-router';
import Loading from '../../src/components/Loading';
import { useAuth } from '../../src/auth/AuthProvider';
import { nextRoute } from '../../src/auth/routing';
import strings from '../../src/strings';
import { useStackOptions } from '../../src/hooks/useStackOptions';

/** Signed-in setup steps: age attestation, required documents, profile, paywall, notification prompt. */
export default function SetupLayout() {
  const screenOptions = useStackOptions({ headerBackVisible: false, gestureEnabled: false });
  const { session } = useAuth();
  if (session.status === 'loading') return <Loading label={strings.common.loading} />;
  if (session.status !== 'signedIn') return <Redirect href={nextRoute(session)} />;
  return (
    <Stack screenOptions={screenOptions}>
      <Stack.Screen name="age" options={{ title: strings.age.title }} />
      <Stack.Screen name="review-documents" options={{ title: strings.documents.reviewTitle }} />
      <Stack.Screen name="profile-setup" options={{ title: strings.profileSetup.title }} />
      <Stack.Screen name="subscribe" options={{ title: strings.paywall.title }} />
      <Stack.Screen name="allow-notifications" options={{ title: strings.allowNotifications.title }} />
    </Stack>
  );
}

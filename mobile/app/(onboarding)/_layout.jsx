import { Redirect, Stack } from 'expo-router';
import { useAuth } from '../../src/auth/AuthProvider';
import { nextRoute } from '../../src/auth/routing';
import strings from '../../src/strings';
import { useStackOptions } from '../../src/hooks/useStackOptions';

/**
 * Onboarding stack for signed-out users. As soon as a sign-in succeeds the session becomes
 * signedIn and this layout forwards to the next setup step.
 */
export default function OnboardingLayout() {
  const screenOptions = useStackOptions();
  const { session } = useAuth();
  if (session.status === 'signedIn') return <Redirect href={nextRoute(session)} />;
  return (
    <Stack screenOptions={screenOptions}>
      <Stack.Screen name="welcome" options={{ headerShown: false }} />
      <Stack.Screen name="sign-in" options={{ title: strings.signIn.title }} />
      <Stack.Screen name="email-sign-in" options={{ title: strings.emailSignIn.title }} />
      <Stack.Screen name="register" options={{ title: strings.register.title }} />
      <Stack.Screen name="forgot-password" options={{ title: strings.forgotPassword.title }} />
    </Stack>
  );
}

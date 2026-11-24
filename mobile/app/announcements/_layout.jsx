import { Redirect, Stack } from 'expo-router';
import { useAuth } from '../../src/auth/AuthProvider';
import { nextRoute } from '../../src/auth/routing';
import Loading from '../../src/components/Loading';
import { useNestedStackOptions } from '../../src/hooks/useStackOptions';
import strings from '../../src/strings';

/** Announcements I received (docs/PHASE5_CONTRACT.md section 8): the list and one announcement (`/announcements/{id}`, the `Broadcast` notification path). */
export default function AnnouncementsLayout() {
  const screenOptions = useNestedStackOptions();
  const { session } = useAuth();
  if (session.status === 'loading') return <Loading label={strings.common.loading} />;
  if (session.status !== 'signedIn') return <Redirect href={nextRoute(session)} />;
  return (
    <Stack screenOptions={screenOptions}>
      <Stack.Screen name="index" options={{ title: strings.announcements.title }} />
      <Stack.Screen name="[id]" options={{ title: strings.announcements.title }} />
    </Stack>
  );
}

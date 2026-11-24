import { Redirect, Stack } from 'expo-router';
import { useAuth } from '../../src/auth/AuthProvider';
import { nextRoute } from '../../src/auth/routing';
import Loading from '../../src/components/Loading';
import { useNestedStackOptions } from '../../src/hooks/useStackOptions';
import strings from '../../src/strings';

/**
 * Support (docs/PHASE5_CONTRACT.md section 6): the ticket list (`/support`), a new request
 * (`/support/new`), and a ticket (`/support/tickets/{id}`). Support is never gated: it needs a
 * session but no membership, so it is reachable in every access model, including from the
 * paywall, and a lapsed member can always ask for billing help.
 */
export default function SupportLayout() {
  const screenOptions = useNestedStackOptions();
  const { session } = useAuth();
  if (session.status === 'loading') return <Loading label={strings.common.loading} />;
  if (session.status !== 'signedIn') return <Redirect href={nextRoute(session)} />;
  return (
    <Stack screenOptions={screenOptions}>
      <Stack.Screen name="index" options={{ title: strings.support.title }} />
      <Stack.Screen name="new" options={{ title: strings.support.newTitle }} />
      <Stack.Screen name="tickets/[id]" options={{ title: strings.support.ticketTitle }} />
    </Stack>
  );
}

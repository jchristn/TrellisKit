import { Redirect, Stack } from 'expo-router';
import { useAuth } from '../../src/auth/AuthProvider';
import { nextRoute } from '../../src/auth/routing';
import IdeasGate from '../../src/components/ideas/IdeasGate';
import Loading from '../../src/components/Loading';
import { useNestedStackOptions } from '../../src/hooks/useStackOptions';
import strings from '../../src/strings';

/**
 * The ideas board (docs/PHASE5_CONTRACT.md section 7): the board (`/ideas`), an idea
 * (`/ideas/{id}`), and a new idea (`/ideas/new`, when members submit ideas directly). Gated by
 * `features.ideas` and the access model.
 */
export default function IdeasLayout() {
  const screenOptions = useNestedStackOptions();
  const { session } = useAuth();
  if (session.status === 'loading') return <Loading label={strings.common.loading} />;
  if (session.status !== 'signedIn') return <Redirect href={nextRoute(session)} />;
  return (
    <IdeasGate>
      <Stack screenOptions={screenOptions}>
        <Stack.Screen name="index" options={{ title: strings.ideas.title }} />
        <Stack.Screen name="[id]" options={{ title: strings.ideas.ideaTitle }} />
        <Stack.Screen name="new" options={{ title: strings.ideas.newTitle }} />
      </Stack>
    </IdeasGate>
  );
}

import { Stack } from 'expo-router';
import { ServerCapability } from '@trelliskit/shared';
import { CapabilityGate } from '../../src/components/ComingSoon';
import strings from '../../src/strings';
import { useNestedStackOptions } from '../../src/hooks/useStackOptions';

/** Thread detail stack (`/threads/{id}`, the push and notification path of section 9.3); "Coming soon" until the server reports `features.forums`. */
export default function ThreadsLayout() {
  const screenOptions = useNestedStackOptions();
  return (
    <CapabilityGate capability={ServerCapability.Forums} title={strings.forums.title} summary={strings.forums.summary} items={strings.forums.comingSoonItems} edges={['top', 'left', 'right']} testID="screen-threads-coming-soon">
      <Stack screenOptions={screenOptions}>
      <Stack.Screen name="[id]" options={{ title: strings.forums.threadTitle }} />
      </Stack>
    </CapabilityGate>
  );
}

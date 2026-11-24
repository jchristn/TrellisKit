import { Stack } from 'expo-router';
import strings from '../../src/strings';
import { useNestedStackOptions } from '../../src/hooks/useStackOptions';

/** Assistants stack: `/assistants` (the list and conversation history) and `/assistants/{id}` (a chat). */
export default function AssistantsLayout() {
  const screenOptions = useNestedStackOptions();
  return (
    <Stack screenOptions={screenOptions}>
      <Stack.Screen name="index" options={{ title: strings.assistants.title }} />
      <Stack.Screen name="[id]" options={{ title: strings.assistants.title }} />
    </Stack>
  );
}

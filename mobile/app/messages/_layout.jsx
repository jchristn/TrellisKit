import { Stack } from 'expo-router';
import MemberGate from '../../src/components/MemberGate';
import MessagingGate from '../../src/components/messaging/MessagingGate';
import { useNestedStackOptions } from '../../src/hooks/useStackOptions';
import strings from '../../src/strings';

/**
 * Direct messages (docs/PHASE5_CONTRACT.md section 19): the conversation list (`/messages`, with
 * `?folder=Requests` or `/messages/requests`), a conversation (`/messages/{id}`), message search,
 * and messaging settings. Reached from the Messages button in the top bar. Gated by the access
 * model and `features.messaging`.
 */
export default function MessagesLayout() {
  const screenOptions = useNestedStackOptions();
  return (
    <MemberGate>
      <MessagingGate>
        <Stack screenOptions={screenOptions}>
          <Stack.Screen name="index" options={{ title: strings.messaging.title }} />
          <Stack.Screen name="[id]" options={{ title: strings.messaging.conversationTitle }} />
          <Stack.Screen name="requests" options={{ title: strings.messaging.folders.Requests }} />
          <Stack.Screen name="search" options={{ title: strings.messaging.searchTitle }} />
        </Stack>
      </MessagingGate>
    </MemberGate>
  );
}

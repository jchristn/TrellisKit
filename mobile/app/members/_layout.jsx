import { Stack } from 'expo-router';
import MemberGate from '../../src/components/MemberGate';
import { useNestedStackOptions } from '../../src/hooks/useStackOptions';
import strings from '../../src/strings';

/** The member directory (`/members`) and member profiles (`/members/{id}`), reached from the Profile tab, authors, and mentions. */
export default function MembersLayout() {
  const screenOptions = useNestedStackOptions();
  return (
    <MemberGate>
      <Stack screenOptions={screenOptions}>
        <Stack.Screen name="index" options={{ title: strings.directory.title }} />
        <Stack.Screen name="[id]" options={{ title: strings.member.title }} />
      </Stack>
    </MemberGate>
  );
}

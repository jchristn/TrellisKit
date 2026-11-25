import { Stack } from 'expo-router';
import MemberGate from '../../src/components/MemberGate';
import { useNestedStackOptions } from '../../src/hooks/useStackOptions';
import strings from '../../src/strings';

/** The member's verified rank (`/rank`), shown when the property reports `features.ranks`. */
export default function RankLayout() {
  const screenOptions = useNestedStackOptions();
  return (
    <MemberGate>
      <Stack screenOptions={screenOptions}>
        <Stack.Screen name="index" options={{ title: strings.ranks.title }} />
      </Stack>
    </MemberGate>
  );
}

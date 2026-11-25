import { Stack } from 'expo-router';
import MemberGate from '../../src/components/MemberGate';
import { useNestedStackOptions } from '../../src/hooks/useStackOptions';
import strings from '../../src/strings';

/** Published curricula (`/curricula`, `/curricula/{id}`), shown when the property reports `features.ranks`. */
export default function CurriculaLayout() {
  const screenOptions = useNestedStackOptions();
  return (
    <MemberGate>
      <Stack screenOptions={screenOptions}>
        <Stack.Screen name="index" options={{ title: strings.ranks.curricula.title }} />
        <Stack.Screen name="[id]" options={{ title: strings.ranks.curricula.title }} />
      </Stack>
    </MemberGate>
  );
}

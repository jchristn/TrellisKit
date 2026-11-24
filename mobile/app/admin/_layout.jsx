import { Stack } from 'expo-router';
import MemberGate from '../../src/components/MemberGate';
import { useNestedStackOptions } from '../../src/hooks/useStackOptions';
import strings from '../../src/strings';

/** Native administrative destinations retain the parent stack's Back button. */
export default function AdminLayout() {
  const screenOptions = useNestedStackOptions();
  return <MemberGate><Stack screenOptions={screenOptions}><Stack.Screen name="metrics/[pageId]" options={{ title: strings.nativeMetrics.title }} /></Stack></MemberGate>;
}

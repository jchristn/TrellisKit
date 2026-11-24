import { Stack } from 'expo-router';
import { useNestedStackOptions } from '../../src/hooks/useStackOptions';
import strings from '../../src/strings';

/**
 * Property documents (`/documents/{key}`). Public: reached from sign-up before an account exists,
 * from the documents step, from purchase screens, and from Account.
 */
export default function DocumentsLayout() {
  const screenOptions = useNestedStackOptions();
  return (
    <Stack screenOptions={screenOptions}>
      <Stack.Screen name="[key]" options={{ title: strings.documents.title }} />
    </Stack>
  );
}

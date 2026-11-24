import { Stack } from 'expo-router';
import CatalogGate from '../../src/components/CatalogGate';
import { useNestedStackOptions } from '../../src/hooks/useStackOptions';
import strings from '../../src/strings';

/** Lesson screens (`/lessons/{id}`). */
export default function Layout() {
  const screenOptions = useNestedStackOptions();
  return (
    <CatalogGate feature="library">
      <Stack screenOptions={screenOptions}>
        <Stack.Screen name="[id]" options={{ title: strings.library.lessonTitle }} />
      </Stack>
    </CatalogGate>
  );
}

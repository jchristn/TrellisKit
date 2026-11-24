import { Stack } from 'expo-router';
import CatalogGate from '../../src/components/CatalogGate';
import { useDirectoryStrings } from '../../src/content/phase4';
import { useNestedStackOptions } from '../../src/hooks/useStackOptions';

/** Instructor screens (`/instructors/{id}`). */
export default function Layout() {
  const screenOptions = useNestedStackOptions();
  const t = useDirectoryStrings();
  return (
    <CatalogGate feature="directory">
      <Stack screenOptions={screenOptions}>
        <Stack.Screen name="[id]" options={{ title: t.instructorTitle }} />
      </Stack>
    </CatalogGate>
  );
}

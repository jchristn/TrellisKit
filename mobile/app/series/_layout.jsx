import { Stack } from 'expo-router';
import CatalogGate from '../../src/components/CatalogGate';
import { useNestedStackOptions } from '../../src/hooks/useStackOptions';
import strings from '../../src/strings';

/** Series screens (`/series/{id}`, the `NewLessonInSeries` path). */
export default function Layout() {
  const screenOptions = useNestedStackOptions();
  return (
    <CatalogGate feature="library">
      <Stack screenOptions={screenOptions}>
        <Stack.Screen name="[id]" options={{ title: strings.library.seriesTitle }} />
      </Stack>
    </CatalogGate>
  );
}

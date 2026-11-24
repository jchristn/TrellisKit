import { Stack } from 'expo-router';
import CatalogGate from '../../src/components/CatalogGate';
import { useDirectoryStrings } from '../../src/content/phase4';
import { useNestedStackOptions } from '../../src/hooks/useStackOptions';

/** Directory submissions: add, suggest an edit, claim, report closed, and My submissions. */
export default function Layout() {
  const screenOptions = useNestedStackOptions();
  const t = useDirectoryStrings();
  return (
    <CatalogGate feature="directory">
      <Stack screenOptions={screenOptions}>
        <Stack.Screen name="submit" options={{ title: t.submit.NewVenue, presentation: 'modal' }} />
        <Stack.Screen name="submissions" options={{ title: t.submissions.title }} />
      </Stack>
    </CatalogGate>
  );
}

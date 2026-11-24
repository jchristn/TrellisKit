import { Stack } from 'expo-router';
import CatalogGate from '../../../src/components/CatalogGate';
import { useTabStackOptions } from '../../../src/hooks/useStackOptions';
import strings from '../../../src/strings';

/** Library tab stack: the library home and Downloads (docs/PHASE4_CONTRACT.md section 16). */
export default function LibraryLayout() {
  const screenOptions = useTabStackOptions();
  return (
    <CatalogGate feature="library">
      <Stack screenOptions={screenOptions}>
        <Stack.Screen name="index" options={{ title: strings.library.title }} />
        <Stack.Screen name="downloads" options={{ title: strings.library.downloads.title }} />
      </Stack>
    </CatalogGate>
  );
}

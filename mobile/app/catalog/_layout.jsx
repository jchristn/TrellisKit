import { Stack } from 'expo-router';
import CatalogGate from '../../src/components/CatalogGate';
import { useNestedStackOptions } from '../../src/hooks/useStackOptions';
import strings from '../../src/strings';

/** The opt-in exercise and technique catalog (features.catalog), read under the library access rules. */
export default function Layout() {
  const screenOptions = useNestedStackOptions();
  return <CatalogGate feature="library" optIn="catalog"><Stack screenOptions={screenOptions}><Stack.Screen name="index" options={{ title: strings.exerciseCatalog.title }} /><Stack.Screen name="[id]" options={{ title: strings.exerciseCatalog.title }} /></Stack></CatalogGate>;
}

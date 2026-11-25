import { Stack } from 'expo-router';
import CatalogGate from '../../src/components/CatalogGate';
import { useNestedStackOptions } from '../../src/hooks/useStackOptions';
import strings from '../../src/strings';

/**
 * Training stack (`/training`), the `training` navigation destination. Placeholder until the
 * Training hub screens land: the index shows the Library home.
 */
export default function TrainingLayout() {
  const screenOptions = useNestedStackOptions();
  return (
    <CatalogGate feature="library">
      <Stack screenOptions={screenOptions}>
        <Stack.Screen name="index" options={{ title: strings.tabs.training }} />
      </Stack>
    </CatalogGate>
  );
}

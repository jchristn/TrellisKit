import { Stack } from 'expo-router';
import { useDirectoryStrings, featureOn } from '../../../src/content/phase4';
import { useTabStackOptions } from '../../../src/hooks/useStackOptions';
import { useProperty } from '../../../src/property';
import strings from '../../../src/strings';

/** Map tab stack: the directory and events (docs/PHASE4_CONTRACT.md section 16). */
export default function MapLayout() {
  const screenOptions = useTabStackOptions();
  const property = useProperty();
  const t = useDirectoryStrings();
  return (
    <Stack screenOptions={screenOptions}>
      <Stack.Screen name="index" options={{ title: featureOn(property, 'directory') ? t.title : strings.events.title }} />
    </Stack>
  );
}

import { propertyFeatureEnabled } from '@trelliskit/shared';
import { Stack, useRouter } from 'expo-router';
import Button from '../../src/components/Button';
import ErrorState from '../../src/components/ErrorState';
import Screen from '../../src/components/Screen';
import { useNestedStackOptions } from '../../src/hooks/useStackOptions';
import { drawerHeaderOptions, useAppNavigation } from '../../src/navigation/AppNavigation';
import { useProperty } from '../../src/property';
import strings from '../../src/strings';

const SectionRoutes = Object.freeze(['library/index', 'library/courses', 'library/techniques', 'library/saved', 'saved', 'schedule', 'progress']);

/**
 * The Training stack (`/training`), the `training` navigation destination (docs/TRELLISKIT.md
 * section 15): Overview (`index`), the Library segments (`library/courses`, `library/techniques`,
 * `library/saved`, and `saved`), Schedule, Progress, and curricula. Lessons, series, and catalog
 * entries keep their own routes (`/lessons/{id}`, `/series/{id}`, `/catalog/{id}`), so deep links
 * and push targets still open them. The `training` kill switch, like a refused overview
 * (402, 403, 404, 503), leaves the Library alone; with `features.training` off the library stays
 * reachable at `/library`.
 */
export default function TrainingLayout() {
  const screenOptions = useNestedStackOptions();
  const property = useProperty();
  const router = useRouter();
  const appNavigation = useAppNavigation();
  // Section and segment screens are the Training destination itself: they keep the menu button.
  const sectionOptions = (props) => (SectionRoutes.includes(props.route && props.route.name) && appNavigation && appNavigation.enabled && appNavigation.isMainDestination ? { ...screenOptions(props), ...drawerHeaderOptions(appNavigation) } : screenOptions(props));
  const title = (appNavigation && appNavigation.labels && appNavigation.labels.training) || strings.training.title;
  if (!propertyFeatureEnabled(property, 'training')) {
    return (
      <Screen testID="training-off">
        <ErrorState title={strings.training.unavailable} />
        <Button label={strings.tabs.library} variant="secondary" onPress={() => router.replace('/library')} testID="training-off-library" />
      </Screen>
    );
  }
  return (
    <Stack screenOptions={sectionOptions}>
      <Stack.Screen name="index" options={{ title }} />
      <Stack.Screen name="library/index" options={{ title }} />
      <Stack.Screen name="library/courses" options={{ title }} />
      <Stack.Screen name="library/techniques" options={{ title }} />
      <Stack.Screen name="library/saved" options={{ title }} />
      <Stack.Screen name="saved" options={{ title }} />
      <Stack.Screen name="schedule" options={{ title }} />
      <Stack.Screen name="progress" options={{ title }} />
      <Stack.Screen name="curricula/[id]" options={{ title: strings.ranks.curricula.title }} />
    </Stack>
  );
}

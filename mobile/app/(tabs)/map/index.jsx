import { catalogAccess } from '@trelliskit/shared';
import { Stack, useLocalSearchParams, useRouter } from 'expo-router';
import { View } from 'react-native';
import { useAuth } from '../../../src/auth/AuthProvider';
import { BannerSlot } from '../../../src/appConfig';
import CatalogGate from '../../../src/components/CatalogGate';
import ChipGroup from '../../../src/components/ChipGroup';
import EventsPane from '../../../src/components/events/EventsPane';
import PlacesView from '../../../src/components/places/PlacesView';
import { featureOn, useDirectoryStrings } from '../../../src/content/phase4';
import { useAppNavigation } from '../../../src/navigation/AppNavigation';
import { useProperty } from '../../../src/property';
import strings from '../../../src/strings';
import { useTheme } from '../../../src/theme';

/**
 * The Map tab (docs/PHASE4_CONTRACT.md section 16): the venue directory with its map and list,
 * and Events, switched with a segment control (omitted when both are bottom shortcuts). Each half
 * shows only when its feature is on and the caller may browse it (catalog access, resolved decision 2).
 */
export default function MapScreen() {
  const theme = useTheme();
  const property = useProperty();
  const { user } = useAuth();
  const t = useDirectoryStrings();
  const places = featureOn(property, 'directory') && catalogAccess(property, user, 'directory', 'read').allowed;
  const events = featureOn(property, 'events') && catalogAccess(property, user, 'events', 'read').allowed;
  const { section = 'places' } = useLocalSearchParams();
  const router = useRouter();
  const setSection = (next) => router.setParams({ section: next });
  const current = section === 'places' && places ? 'places' : events ? 'events' : 'places';
  const appNavigation = useAppNavigation();
  // When the bottom bar already separates places and events, the screen does not repeat the switch.
  const split = Boolean(appNavigation?.enabled && appNavigation.shortcutIds?.includes('directory') && appNavigation.shortcutIds.includes('events'));
  const placesTitle = appNavigation?.labels?.directory || t.title;
  if (!places && !events) return <CatalogGate feature={featureOn(property, 'directory') ? 'directory' : 'events'}>{null}</CatalogGate>;
  return (
    <View style={{ flex: 1, backgroundColor: theme.colors.background }} testID="screen-map">
      <Stack.Screen options={{ title: current === 'events' ? appNavigation?.labels?.events || strings.events.title : placesTitle }} />
      {places && events && !split ? (
        <View style={{ paddingHorizontal: theme.spacing.lg, paddingTop: theme.spacing.md }}>
          <ChipGroup
            label={t.segments}
            hideLabel
            options={[
              { value: 'places', label: t.title },
              { value: 'events', label: t.eventsSegment }
            ]}
            value={current}
            onToggle={setSection}
            testID="map-section"
          />
        </View>
      ) : null}
      <BannerSlot placement={current === 'places' ? 'Directory' : 'Events'} style={{ paddingHorizontal: theme.spacing.lg, paddingTop: theme.spacing.md }} />
      <View style={{ flex: 1 }}>{current === 'places' ? <PlacesView inset={places && events && !split} /> : <EventsPane inset={places && events && !split} />}</View>
    </View>
  );
}

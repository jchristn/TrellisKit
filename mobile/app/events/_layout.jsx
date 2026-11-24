import { Stack } from 'expo-router';
import CatalogGate from '../../src/components/CatalogGate';
import { useNestedStackOptions } from '../../src/hooks/useStackOptions';
import strings from '../../src/strings';

/** Event screens: the list (`/events`), an event (`/events/{id}`), proposals, and the calendar feed. */
export default function Layout() {
  const screenOptions = useNestedStackOptions();
  return (
    <CatalogGate feature="events">
      <Stack screenOptions={screenOptions}>
        <Stack.Screen name="index" options={{ title: strings.events.title }} />
        <Stack.Screen name="[id]" options={{ title: strings.events.eventTitle }} />
        <Stack.Screen name="propose" options={{ title: strings.events.propose.title, presentation: 'modal' }} />
        <Stack.Screen name="proposals" options={{ title: strings.events.proposals.title }} />
        <Stack.Screen name="calendar-feed" options={{ title: strings.events.calendar.feedTitle }} />
      </Stack>
    </CatalogGate>
  );
}

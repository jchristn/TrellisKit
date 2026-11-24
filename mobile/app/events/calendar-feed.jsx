import CalendarFeed from '../../src/components/events/CalendarFeed';
import Screen from '../../src/components/Screen';

/** The calendar feed (`/events/calendar-feed`). */
export default function Page() {
  return (
    <Screen testID="screen-calendar-feed">
      <CalendarFeed />
    </Screen>
  );
}

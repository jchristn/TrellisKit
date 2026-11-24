import { SafeAreaView } from 'react-native-safe-area-context';
import { BannerSlot } from '../../src/appConfig';
import EventsPane from '../../src/components/events/EventsPane';
import { useTheme } from '../../src/theme';

/** Events (`/events`). */
export default function Page() {
  const theme = useTheme();
  return (
    <SafeAreaView testID="screen-events-route" edges={['left', 'right']} style={{ flex: 1, backgroundColor: theme.colors.background }}>
      <BannerSlot placement="Events" style={{ paddingHorizontal: theme.spacing.lg, paddingTop: theme.spacing.md }} />
      <EventsPane testID="events-pane" />
    </SafeAreaView>
  );
}

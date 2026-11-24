import { useLocalSearchParams } from 'expo-router';
import { SafeAreaView } from 'react-native-safe-area-context';
import EventDetail from '../../src/components/events/EventDetail';
import { useTheme } from '../../src/theme';

/** An event (`/events/{id}`, the reminder and change push path). */
export default function Screen() {
  const theme = useTheme();
  const { id, comment } = useLocalSearchParams();
  const value = Array.isArray(id) ? id[0] : id;
  const commentId = (Array.isArray(comment) ? comment[0] : comment) || null;
  return (
    <SafeAreaView testID="screen-event" edges={['left', 'right']} style={{ flex: 1, backgroundColor: theme.colors.background }}>
      <EventDetail key={value} eventId={value} commentId={commentId} />
    </SafeAreaView>
  );
}

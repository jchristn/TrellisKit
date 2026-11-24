import { useLocalSearchParams } from 'expo-router';
import { SafeAreaView } from 'react-native-safe-area-context';
import Screen from '../../../src/components/Screen';
import SupportEmailCard from '../../../src/components/support/SupportEmailCard';
import TicketDetail from '../../../src/components/support/TicketDetail';
import { emitContentEvent } from '../../../src/content/events';
import { useProperty } from '../../../src/property';
import { supportEnabled } from '../../../src/support/support';
import { useTheme } from '../../../src/theme';

/** `/support/tickets/{id}` (the `TicketUpdate` notification path): one support request. */
export default function TicketScreen() {
  const theme = useTheme();
  const property = useProperty();
  const { id } = useLocalSearchParams();
  const ticketId = Array.isArray(id) ? id[0] : id;
  if (!supportEnabled(property)) {
    return (
      <Screen testID="screen-ticket">
        <SupportEmailCard />
      </Screen>
    );
  }
  return (
    <SafeAreaView testID="screen-ticket" edges={['left', 'right', 'bottom']} style={{ flex: 1, backgroundColor: theme.colors.background }}>
      <TicketDetail key={ticketId} ticketId={ticketId} onTicketChange={(ticket) => emitContentEvent('ticketChanged', ticket)} />
    </SafeAreaView>
  );
}

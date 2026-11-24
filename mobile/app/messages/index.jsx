import { useLocalSearchParams } from 'expo-router';
import ConversationsScreen from '../../src/components/messaging/ConversationsScreen';

/** `/messages` (and `/messages?folder=Requests`): the conversation list. */
export default function MessagesScreen() {
  const { folder } = useLocalSearchParams();
  return <ConversationsScreen initialFolder={Array.isArray(folder) ? folder[0] : folder || 'Inbox'} />;
}

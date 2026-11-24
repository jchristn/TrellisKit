import ConversationsScreen from '../../src/components/messaging/ConversationsScreen';

/** `/messages/requests` (the `MessageRequest` notification path): the Requests folder. */
export default function MessageRequestsScreen() {
  return <ConversationsScreen initialFolder="Requests" />;
}

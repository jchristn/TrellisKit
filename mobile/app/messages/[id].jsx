import { useLocalSearchParams, useRouter } from 'expo-router';
import { SafeAreaView } from 'react-native-safe-area-context';
import ConversationView from '../../src/components/messaging/ConversationView';
import { useTheme } from '../../src/theme';

/** Height of the navigation header above the conversation, for the keyboard-aware composer on iOS. */
const HeaderOffset = 96;

/** `/messages/{id}`: one conversation (phones, and push deep links on any device). */
export default function ConversationScreen() {
  const theme = useTheme();
  const router = useRouter();
  const { id } = useLocalSearchParams();
  const conversationId = Array.isArray(id) ? id[0] : id;
  return (
    <SafeAreaView testID="screen-conversation" edges={['left', 'right', 'bottom']} style={{ flex: 1, backgroundColor: theme.colors.background }}>
      <ConversationView key={conversationId} conversationId={conversationId} keyboardOffset={HeaderOffset} onClosed={() => (router.canGoBack() ? router.back() : router.replace('/messages'))} />
    </SafeAreaView>
  );
}

import { aiAvailable } from '@trelliskit/shared';
import { useFocusEffect, useRouter } from 'expo-router';
import { useCallback, useEffect, useState } from 'react';
import { Alert, Pressable, View } from 'react-native';
import { useAssistantsApi } from '../../ai/useAssistantsApi';
import { chatErrorCode } from '../../ai/useAssistantChat';
import AppText from '../AppText';
import { AccessPromptCard } from '../AccessPrompt';
import Button from '../Button';
import Card from '../Card';
import ErrorState from '../ErrorState';
import IconButton from '../IconButton';
import Loading from '../Loading';
import Screen from '../Screen';
import Section from '../Section';
import TextField from '../TextField';
import { useProperty } from '../../property';
import strings from '../../strings';
import { useTheme } from '../../theme';

const s = strings.assistants;

function ConversationRow({ conversation, onOpen, onArchive, onDelete }) {
  const theme = useTheme();
  return (
    <View style={{ flexDirection: 'row', alignItems: 'center', gap: theme.spacing.sm }}>
      <Pressable accessibilityRole="button" accessibilityLabel={conversation.title || s.untitled} onPress={() => onOpen(conversation)} style={{ flex: 1, minHeight: theme.layout.minTouchTarget, justifyContent: 'center' }} testID={'conversation-' + conversation.id}>
        <AppText variant="label">{conversation.title || s.untitled}</AppText>
        <AppText variant="caption" color="textMuted">{conversation.assistant.title + (conversation.lastMessagePreview ? ' · ' + conversation.lastMessagePreview : '')}</AppText>
      </Pressable>
      <IconButton icon={conversation.isArchived ? 'arrow-undo-outline' : 'archive-outline'} label={conversation.isArchived ? s.unarchive : s.archive} onPress={() => onArchive(conversation)} />
      <IconButton icon="trash-outline" label={s.deleteConversation} onPress={() => onDelete(conversation)} />
    </View>
  );
}

/**
 * Assistants home (docs/AI_CONTRACT.md section 12): the assistants the member may use and their
 * conversation history with search and an archived filter. Shown at `/assistants` (from Profile)
 * and as the `/ai` tab when the server reports `features.aiPlacement === 'Tab'`. Without
 * `features.ai` nothing is called. Signed-out guests (anonymous browsing) use a visitor session
 * and see the Public assistants and their own visitor conversations, with a sign-in prompt.
 */
export default function AssistantsHome({ testID = 'screen-assistants' }) {
  const theme = useTheme();
  const router = useRouter();
  const { api, visitor } = useAssistantsApi();
  const available = aiAvailable(useProperty());
  const [assistants, setAssistants] = useState({ status: 'loading', items: [], error: null });
  const [conversations, setConversations] = useState({ status: 'loading', items: [] });
  const [search, setSearch] = useState('');
  const [archived, setArchived] = useState(false);
  const [version, setVersion] = useState(0);

  const loadAssistants = useCallback(() => {
    if (!available) return;
    api.enumerateAssistants({ maxResults: 50 }).then(
      (page) => setAssistants({ status: 'ready', items: (page && page.objects) || [], error: null }),
      (error) => setAssistants({ status: 'error', items: [], error })
    );
  }, [api, available]);
  useEffect(() => { loadAssistants(); }, [loadAssistants]);

  useEffect(() => {
    if (!available) return undefined;
    let active = true;
    const timer = setTimeout(() => {
      api.enumerateConversations({ maxResults: 50, search: search.trim() || undefined, filters: { archived: archived ? 'true' : 'false' } }).then(
        (page) => { if (active) setConversations({ status: 'ready', items: (page && page.objects) || [] }); },
        () => { if (active) setConversations({ status: 'error', items: [] }); }
      );
    }, search ? 300 : 0);
    return () => { active = false; clearTimeout(timer); };
  }, [api, available, search, archived, version]);
  useFocusEffect(useCallback(() => { setVersion((v) => v + 1); }, []));

  if (!available) {
    return (
      <Screen testID={testID}>
        <ErrorState title={s.errors.NotAvailable} testID="assistants-unavailable" />
      </Screen>
    );
  }
  const openAssistant = (assistant) => router.push('/assistants/' + assistant.id);
  const openConversation = (conversation) => router.push({ pathname: '/assistants/[id]', params: { id: conversation.assistant.id, conversation: conversation.id } });
  const archive = async (conversation) => {
    await api.updateConversation(conversation.id, { isArchived: !conversation.isArchived }).catch(() => {});
    setVersion((v) => v + 1);
  };
  const remove = (conversation) => {
    Alert.alert(s.deleteConfirmTitle, s.deleteConfirmBody, [
      { text: strings.common.cancel, style: 'cancel' },
      { text: s.deleteConversation, style: 'destructive', onPress: async () => { await api.deleteConversation(conversation.id).catch(() => {}); setVersion((v) => v + 1); } }
    ]);
  };

  return (
    <Screen testID={testID}>
      <AppText color="textMuted">{s.summary}</AppText>
      {visitor ? <AccessPromptCard required="SignIn" feature="assistants" title={s.guestTitle} body={s.guestBody} testID="assistants-guest" /> : null}
      <Section title={s.title}>
        {assistants.status === 'loading' ? <Loading label={strings.common.loading} /> : null}
        {assistants.status === 'error' ? <ErrorState title={s.errors[chatErrorCode(assistants.error)] || s.errors.generic} onRetry={() => { setAssistants({ status: 'loading', items: [], error: null }); loadAssistants(); }} /> : null}
        {assistants.status === 'ready' && assistants.items.length === 0 ? <AppText color="textMuted">{s.empty}</AppText> : null}
        {assistants.items.map((assistant) => (
          <Pressable key={assistant.id} accessibilityRole="button" accessibilityLabel={assistant.title} accessibilityHint={s.openHint} onPress={() => openAssistant(assistant)} testID={'assistant-' + assistant.id}>
            <Card>
              <AppText variant="heading">{((assistant.avatar && assistant.avatar.emoji) ? assistant.avatar.emoji + ' ' : '') + assistant.title}</AppText>
              {assistant.description ? <AppText color="textMuted">{assistant.description}</AppText> : null}
              {assistant.limits && assistant.limits.remainingToday !== null && assistant.limits.remainingToday !== undefined ? (
                <AppText variant="caption" color="textMuted">{assistant.limits.remainingToday === 0 ? s.limitReached : s.remaining(assistant.limits.remainingToday)}</AppText>
              ) : null}
            </Card>
          </Pressable>
        ))}
      </Section>
      <Section title={s.conversations}>
        <TextField label={s.searchConversations} value={search} onChangeText={setSearch} compact testID="conversation-search" />
        <Button label={archived ? s.hideArchived : s.showArchived} variant="secondary" onPress={() => setArchived((a) => !a)} testID="conversation-archived" />
        {conversations.status === 'ready' && conversations.items.length === 0 ? <AppText color="textMuted">{s.noConversations}</AppText> : null}
        <View style={{ gap: theme.spacing.xs }}>
          {conversations.items.map((c) => <ConversationRow key={c.id} conversation={c} onOpen={openConversation} onArchive={archive} onDelete={remove} />)}
        </View>
      </Section>
    </Screen>
  );
}

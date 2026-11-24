import { useLocalSearchParams, useNavigation, useRouter } from 'expo-router';
import { useCallback, useEffect, useRef, useState } from 'react';
import { AccessibilityInfo, ScrollView, View } from 'react-native';
import KeyboardAvoider from '../../src/components/KeyboardAvoider';
import { SafeAreaView } from 'react-native-safe-area-context';
import { aiAvailable } from '@trelliskit/shared';
import { useAssistantsApi } from '../../src/ai/useAssistantsApi';
import { useProperty } from '../../src/property';
import { deviceFlags } from '../../src/api/tokenStore';
import { useKeepAwakeWhile } from '../../src/ai/keepAwake';
import { chatErrorCode, useAssistantChat } from '../../src/ai/useAssistantChat';
import AppText from '../../src/components/AppText';
import Button from '../../src/components/Button';
import ErrorState from '../../src/components/ErrorState';
import Loading from '../../src/components/Loading';
import Notice from '../../src/components/Notice';
import TextField from '../../src/components/TextField';
import AssistantMessage from '../../src/components/ai/AssistantMessage';
import ReportSheet from '../../src/components/feed/ReportSheet';
import strings from '../../src/strings';
import { useTheme } from '../../src/theme';
import FeatureGate from '../../src/components/FeatureGate';

const s = strings.assistants;

function Chat({ api, assistant, conversationId, canReport }) {
  const theme = useTheme();
  const router = useRouter();
  const [text, setText] = useState('');
  const [firstUse, setFirstUse] = useState(false);
  const [reporting, setReporting] = useState(null);
  const scrollRef = useRef(null);
  const onConversation = useCallback((conversation) => router.setParams({ conversation: conversation.id }), [router]);
  const chat = useAssistantChat(api, assistant, conversationId || null, onConversation);
  const { state } = chat;
  // The screen stays on only while a reply streams (AI contract section 11.2).
  useKeepAwakeWhile(state.streaming);
  const flag = 'tk.ai.notice.' + assistant.id;
  const remaining = state.remainingToday ?? (assistant.limits ? assistant.limits.remainingToday : null);
  const exhausted = remaining === 0;
  const max = (assistant.limits && assistant.limits.maxMessageCharacters) || 4000;

  useEffect(() => {
    let active = true;
    deviceFlags.get(flag).then((seen) => { if (active) setFirstUse(!seen); });
    return () => { active = false; };
  }, [flag]);

  const wasStreaming = useRef(false);
  useEffect(() => {
    if (wasStreaming.current && !state.streaming) AccessibilityInfo.announceForAccessibility(s.replyDone);
    if (!wasStreaming.current && state.streaming) AccessibilityInfo.announceForAccessibility(s.thinking);
    wasStreaming.current = state.streaming;
  }, [state.streaming]);

  const send = async (value) => {
    const message = value ?? text;
    if (!message.trim()) return;
    setText('');
    const ok = await chat.send(message);
    if (!ok) setText(message);
  };

  if (chat.loading) return <Loading label={strings.common.loading} />;
  return (
    <KeyboardAvoider style={{ flex: 1 }} iosOffset={90}>
      <ScrollView ref={scrollRef} contentContainerStyle={{ padding: theme.spacing.lg, gap: theme.spacing.md }} onContentSizeChange={() => scrollRef.current && scrollRef.current.scrollToEnd({ animated: false })} testID="assistant-transcript">
        {firstUse ? (
          <Notice testID="assistant-first-use">
            {s.firstUseTitle + '. ' + s.firstUseBody}
          </Notice>
        ) : null}
        {firstUse ? <Button label={s.firstUseOk} variant="secondary" onPress={() => { deviceFlags.set(flag); setFirstUse(false); }} testID="assistant-first-use-ok" /> : null}
        {state.messages.length === 0 && assistant.starterPrompts && assistant.starterPrompts.length ? (
          <View style={{ gap: theme.spacing.sm }}>
            <AppText variant="label">{s.starterTitle}</AppText>
            {assistant.starterPrompts.map((prompt) => <Button key={prompt} label={prompt} variant="secondary" disabled={exhausted || state.streaming} onPress={() => send(prompt)} />)}
          </View>
        ) : null}
        {state.messages.map((m) => <AssistantMessage key={m.id} message={m} onRate={chat.rate} onReport={canReport && !state.streaming ? setReporting : null} testID={'assistant-message-' + m.id} />)}
        {state.streaming ? <AppText color="textMuted" accessibilityLiveRegion="polite">{s.thinking}</AppText> : null}
        {state.error ? <AppText color="danger" testID="assistant-error">{s.errors[state.error.code] || s.errors.generic}</AppText> : null}
      </ScrollView>
      <View style={{ padding: theme.spacing.lg, gap: theme.spacing.sm, borderTopWidth: 1, borderTopColor: theme.colors.border }}>
        <TextField label={s.composerLabel} value={text} onChangeText={setText} placeholder={s.composerPlaceholder} multiline maxLength={max} compact editable={!exhausted} testID="assistant-composer" />
        <View style={{ flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', gap: theme.spacing.sm }}>
          <AppText variant="caption" color="textMuted" style={{ flex: 1 }} testID="assistant-remaining">{remaining === null || remaining === undefined ? '' : exhausted ? s.limitReached : s.remaining(remaining)}</AppText>
          {state.streaming
            ? <Button label={s.stop} accessibilityHint={s.stopHint} variant="secondary" onPress={chat.stop} testID="assistant-stop" />
            : <Button label={s.send} accessibilityHint={s.sendHint} onPress={() => send()} disabled={exhausted || !text.trim()} testID="assistant-send" />}
        </View>
        {assistant.disclaimer ? <AppText variant="caption" color="textMuted">{assistant.disclaimer}</AppText> : null}
        <AppText variant="caption" color="textMuted" testID="assistant-privacy">{s.privacyNotice}</AppText>
      </View>
      {reporting ? <ReportSheet target={{ targetType: 'AssistantMessage', targetId: reporting.id }} onClose={() => setReporting(null)} /> : null}
    </KeyboardAvoider>
  );
}

/**
 * Assistant chat (docs/AI_CONTRACT.md section 12): `/assistant/{id}` starts a new conversation and
 * `/assistant/{id}?conversation={conversationId}` continues one. Replies stream through expo/fetch.
 */
function AssistantChatScreenContent() {
  const theme = useTheme();
  const navigation = useNavigation();
  const { id, conversation } = useLocalSearchParams();
  const { api, visitor } = useAssistantsApi();
  const available = aiAvailable(useProperty());
  const [state, setState] = useState({ status: 'loading', assistant: null, error: null });
  const fetchAssistant = useCallback(() => {
    if (!available) return;
    api.getAssistant(id).then(
      (assistant) => setState({ status: 'ready', assistant, error: null }),
      (error) => setState({ status: 'error', assistant: null, error })
    );
  }, [api, id, available]);
  useEffect(() => { fetchAssistant(); }, [fetchAssistant]);
  const retry = () => {
    setState({ status: 'loading', assistant: null, error: null });
    fetchAssistant();
  };
  useEffect(() => {
    if (state.assistant) navigation.setOptions({ title: state.assistant.title });
  }, [navigation, state.assistant]);
  return (
    <SafeAreaView edges={['left', 'right', 'bottom']} style={{ flex: 1, backgroundColor: theme.colors.background }} testID="screen-assistant-chat">
      {!available ? <ErrorState title={s.errors.NotAvailable} testID="assistants-unavailable" /> : null}
      {available && state.status === 'loading' ? <Loading label={strings.common.loading} /> : null}
      {state.status === 'error' ? <ErrorState title={s.errors[chatErrorCode(state.error)] || s.errors.generic} onRetry={retry} /> : null}
      {state.assistant ? <Chat key={state.assistant.id} api={api} assistant={state.assistant} conversationId={conversation || null} canReport={!visitor} /> : null}
    </SafeAreaView>
  );
}

/**
 * Gated by the access model: the `assistants` feature (ACCESS_MODELS_CONTRACT.md section 8.7).
 * Signed-out guests chat with Public assistants through a visitor session when anonymous
 * browsing is on (src/ai/visitor.js).
 */
export default function AssistantChatScreen() {
  return (
    <FeatureGate feature="assistants">
      <AssistantChatScreenContent />
    </FeatureGate>
  );
}

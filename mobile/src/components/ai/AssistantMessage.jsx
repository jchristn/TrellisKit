import { useRouter } from 'expo-router';
import { Platform, Text, View } from 'react-native';
import AppText from '../AppText';
import IconButton from '../IconButton';
import MarkdownBody from '../feed/MarkdownBody';
import ToolActivity from './ToolActivity';
import strings from '../../strings';
import { useTheme } from '../../theme';

const s = strings.assistants;

/**
 * Split assistant Markdown into prose and fenced code segments (docs/AI_CONTRACT.md section 9.2).
 * Prose renders with the Phase 2 subset; code renders as monospaced text, never executed.
 * @param {string} text
 * @returns {{ type: 'prose'|'code', value: string }[]}
 */
export function splitCodeFences(text) {
  const segments = [];
  let prose = [];
  let code = null;
  for (const line of String(text || '').replace(/\r\n/g, '\n').split('\n')) {
    const fence = /^\s*```/.test(line);
    if (code) {
      if (fence) {
        segments.push({ type: 'code', value: code.join('\n') });
        code = null;
      } else code.push(line);
    } else if (fence) {
      if (prose.length) segments.push({ type: 'prose', value: prose.join('\n') });
      prose = [];
      code = [];
    } else prose.push(line);
  }
  if (code) segments.push({ type: 'code', value: code.join('\n') });
  if (prose.length && prose.join('').trim()) segments.push({ type: 'prose', value: prose.join('\n') });
  return segments;
}

/** One transcript message: the member's text, or an AI reply with its badge, citations, feedback, and Report. */
export default function AssistantMessage({ message, onRate, onReport, testID }) {
  const theme = useTheme();
  const router = useRouter();
  const user = message.role === 'User';
  const bubble = {
    alignSelf: user ? 'flex-end' : 'flex-start',
    maxWidth: '88%',
    padding: theme.spacing.md,
    borderRadius: theme.radii.lg,
    backgroundColor: user ? theme.colors.primary : theme.colors.surface,
    gap: theme.spacing.xs
  };
  if (user) {
    return (
      <View style={bubble} testID={testID}>
        <AppText color="onPrimary">{message.content}</AppText>
      </View>
    );
  }
  const failed = message.status === 'Failed' || message.status === 'Cancelled';
  const rating = message.feedback && message.feedback.rating;
  const stored = !String(message.id).startsWith('pending_');
  const canRate = Boolean(onRate) && stored && message.status === 'Completed';
  // Members report a reply (POST /reports, targetType AssistantMessage; AI contract section 12.3).
  const canReport = Boolean(onReport) && stored && (message.status === 'Completed' || message.status === 'Blocked');
  const mono = Platform.select({ ios: 'Menlo', default: 'monospace' });
  return (
    <View style={bubble} testID={testID} accessibilityLabel={s.aiBadge + ' ' + (message.content || '')}>
      <AppText variant="caption" color="textMuted">{s.aiBadge}</AppText>
      <ToolActivity message={message} />
      {message.status === 'Blocked' ? <AppText color="textMuted">{s.blocked}</AppText> : null}
      {splitCodeFences(message.content).map((segment, i) =>
        segment.type === 'code' ? (
          <View key={i} style={{ backgroundColor: theme.colors.background, borderRadius: theme.radii.sm, padding: theme.spacing.sm }}>
            <Text selectable style={{ fontFamily: mono, fontSize: 14, color: theme.colors.text }}>{segment.value}</Text>
          </View>
        ) : (
          <MarkdownBody key={i} body={segment.value} headings code />
        )
      )}
      {failed ? <AppText color="danger">{message.status === 'Cancelled' ? s.stopped : s.errors[message.errorCode] || s.errors.generic}</AppText> : null}
      {message.citations && message.citations.length ? (
        <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: theme.spacing.xs }} accessibilityLabel={s.sources}>
          {message.citations.map((c) => (
            <Text
              key={c.index + ':' + c.targetId}
              accessibilityRole="link"
              onPress={() => (c.targetType === 'FeedPost' ? router.push('/feed/' + c.targetId) : null)}
              style={[theme.typography.caption, { color: theme.colors.primary, borderWidth: 1, borderColor: theme.colors.border, borderRadius: theme.radii.pill, paddingHorizontal: theme.spacing.sm }]}
            >
              {'[' + c.index + '] ' + c.title}
            </Text>
          ))}
        </View>
      ) : null}
      {canRate || canReport ? (
        <View style={{ flexDirection: 'row' }}>
          {canRate ? <IconButton icon={rating === 'Up' ? 'thumbs-up' : 'thumbs-up-outline'} label={s.feedbackUp} selected={rating === 'Up'} onPress={() => onRate(message, 'Up')} testID={'feedback-up-' + message.id} /> : null}
          {canRate ? <IconButton icon={rating === 'Down' ? 'thumbs-down' : 'thumbs-down-outline'} label={s.feedbackDown} selected={rating === 'Down'} onPress={() => onRate(message, 'Down')} testID={'feedback-down-' + message.id} /> : null}
          {canReport ? <IconButton icon="flag-outline" label={s.reportReply} onPress={() => onReport(message)} testID={'report-reply-' + message.id} /> : null}
        </View>
      ) : null}
    </View>
  );
}

import { formatRelativeTime, snippetSegments } from '@trelliskit/shared';
import { Pressable, Text, View } from 'react-native';
import strings from '../../strings';
import { useTheme } from '../../theme';
import AppText from '../AppText';

/** Type and status markers for a thread or ThreadSummary. */
export function threadMarkers(thread) {
  const t = strings.forums;
  const out = [t.types[thread.type] || thread.type];
  if (thread.isPinned) out.push(t.pinned);
  if (thread.isLocked) out.push(t.locked);
  if (thread.type === 'Question' && (thread.hasAcceptedAnswer || thread.acceptedAnswer)) out.push(t.answered);
  if (thread.poll) out.push(thread.poll.isClosed ? t.pollClosed : t.pollOpen);
  if (thread.feedback && thread.feedback.status) out.push(t.feedbackStatus[thread.feedback.status] || thread.feedback.status);
  if (thread.promotion) out.push(t.promoted);
  if (thread.kind === 'LinkedFeedPost') out.push(t.fromFeed);
  if (thread.state && thread.state !== 'Published') out.push(t.states[thread.state] || thread.state);
  return out;
}

/**
 * One thread in a list (docs/PHASE3_CONTRACT.md section 2.3): markers, title, the excerpt or the
 * search snippet with highlights, author, counts, last activity, and an unread dot.
 */
export default function ThreadCard({ thread, onPress, selected = false, showForum = false, testID }) {
  const theme = useTheme();
  const id = testID || 'thread-' + thread.id;
  const markers = threadMarkers(thread);
  const snippet = thread.searchSnippet;
  const author = thread.author && !thread.author.isDeleted ? thread.author.displayName : strings.comments.placeholders.Deleted;
  const label = [thread.isUnread ? strings.forums.unread : null, thread.title, markers.join(', '), strings.forums.commentCount(thread.commentCount || 0)].filter(Boolean).join('. ');
  return (
    <Pressable
      testID={id}
      onPress={() => onPress(thread)}
      accessibilityRole="button"
      accessibilityLabel={label}
      accessibilityState={{ selected }}
      style={({ pressed }) => ({
        gap: theme.spacing.xs,
        padding: theme.spacing.md,
        borderRadius: theme.radii.md,
        borderWidth: selected ? 2 : 1,
        borderColor: selected ? theme.colors.primary : theme.colors.border,
        backgroundColor: theme.colors.surface,
        opacity: pressed ? 0.85 : 1
      })}
    >
      <AppText variant="caption" color="primary" style={{ fontWeight: '700' }} testID={id + '-markers'}>
        {markers.join(' · ')}
      </AppText>
      <View style={{ flexDirection: 'row', alignItems: 'center', gap: theme.spacing.xs }}>
        {thread.isUnread ? <View testID={id + '-unread'} style={{ width: 8, height: 8, borderRadius: 4, backgroundColor: theme.colors.primary }} /> : null}
        <AppText variant="label" style={{ flex: 1, fontWeight: thread.isUnread ? '700' : '600' }}>
          {thread.title}
        </AppText>
      </View>
      {snippet && snippet.text ? (
        <AppText variant="caption" color="textMuted" testID={id + '-snippet'}>
          {snippetSegments(snippet.text, snippet.highlights).map((seg, i) => (
            <Text key={i} style={seg.highlight ? { fontWeight: '700', color: theme.colors.text } : null}>
              {seg.text}
            </Text>
          ))}
        </AppText>
      ) : thread.excerpt ? (
        <AppText variant="caption" color="textMuted">
          {thread.excerpt}
        </AppText>
      ) : null}
      <AppText variant="caption" color="textMuted">
        {[strings.forums.by(author), showForum && thread.forum ? strings.forums.inForum(thread.forum.name) : null, strings.forums.commentCount(thread.commentCount || 0), thread.lastActivityUtc ? formatRelativeTime(thread.lastActivityUtc) : null].filter(Boolean).join(' · ')}
      </AppText>
    </Pressable>
  );
}

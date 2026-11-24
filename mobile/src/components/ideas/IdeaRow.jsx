import { Pressable, View } from 'react-native';
import strings from '../../strings';
import { useTheme } from '../../theme';
import AppText from '../AppText';

const t = strings.ideas;

/** An idea's status as a small colored chip. */
export function StatusChip({ status, testID }) {
  const theme = useTheme();
  if (!status) return null;
  return (
    <View style={{ flexDirection: 'row', alignItems: 'center', gap: theme.spacing.xs, alignSelf: 'flex-start', paddingHorizontal: theme.spacing.sm, paddingVertical: theme.spacing.xxs, borderRadius: theme.radii.pill, borderWidth: 1, borderColor: status.color || theme.colors.border }} testID={testID}>
      {status.color ? <View style={{ width: 8, height: 8, borderRadius: 4, backgroundColor: status.color }} /> : null}
      <AppText variant="caption">{status.label || status.key}</AppText>
    </View>
  );
}

/** One idea on the board: votes, title, status, and comment count. */
export default function IdeaRow({ idea, onOpen, selected = false }) {
  const theme = useTheme();
  const votes = t.votes(Number(idea.voteCount) || 0);
  const status = idea.status ? idea.status.label || idea.status.key : '';
  return (
    <Pressable
      onPress={() => onOpen(idea)}
      accessibilityRole="button"
      accessibilityLabel={t.rowLabel(idea.title, status, votes)}
      accessibilityState={{ selected }}
      testID={'idea-' + idea.id}
      style={({ pressed }) => ({ flexDirection: 'row', gap: theme.spacing.md, paddingVertical: theme.spacing.md, paddingHorizontal: theme.spacing.sm, borderRadius: theme.radii.md, backgroundColor: selected ? theme.colors.surface : 'transparent', opacity: pressed ? 0.7 : 1 })}
    >
      <View style={{ width: 56, alignItems: 'center', justifyContent: 'center', borderRadius: theme.radii.md, borderWidth: 1, borderColor: idea.hasVoted ? theme.colors.primary : theme.colors.border, paddingVertical: theme.spacing.xs }}>
        <AppText variant="heading" color={idea.hasVoted ? 'primary' : 'text'} testID={'idea-' + idea.id + '-votes'}>
          {String(Number(idea.voteCount) || 0)}
        </AppText>
      </View>
      <View style={{ flex: 1, gap: theme.spacing.xs }}>
        <AppText variant="label">
          {idea.title}
        </AppText>
        <View style={{ flexDirection: 'row', alignItems: 'center', gap: theme.spacing.sm, flexWrap: 'wrap' }}>
          <StatusChip status={idea.status} testID={'idea-' + idea.id + '-status'} />
          <AppText variant="caption" color="textMuted">
            {t.comments(Number(idea.commentCount) || 0)}
          </AppText>
        </View>
      </View>
    </Pressable>
  );
}

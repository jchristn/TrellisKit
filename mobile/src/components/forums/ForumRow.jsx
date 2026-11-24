import Ionicons from '@expo/vector-icons/Ionicons';
import { unreadBadge } from '@trelliskit/shared';
import { Pressable, View } from 'react-native';
import strings from '../../strings';
import { useTheme } from '../../theme';
import AppText from '../AppText';

/**
 * One forum in the forum list (docs/PHASE3_CONTRACT.md section 2.1): emoji or color swatch, name,
 * description, thread count, and the unread badge (capped at 99). Names come from the server.
 * `view` picks the shape: `list` (a compact row), `cards` (a taller card with a larger icon and
 * the comment count), or `gallery` (a tile with a large icon, the name, and the thread count).
 */
export default function ForumRow({ forum, onPress, selected = false, view = 'list', testID }) {
  const theme = useTheme();
  const badge = unreadBadge(forum.unreadThreadCount);
  const id = testID || 'forum-' + forum.id;
  const label = [forum.name, badge ? strings.forums.unreadLabel(badge) : null, forum.isArchived ? strings.forums.archived : null].filter(Boolean).join(', ');
  const gallery = view === 'gallery';
  const cards = view === 'cards';
  const iconSize = gallery ? 72 : cards ? 48 : 36;
  const icon = (
    <View style={{ width: iconSize, height: iconSize, borderRadius: gallery ? theme.radii.lg : theme.radii.md, alignItems: 'center', justifyContent: 'center', backgroundColor: forum.color || theme.colors.background }} importantForAccessibility="no" accessibilityElementsHidden>
      {forum.icon && forum.icon.emoji ? (
        <AppText style={gallery ? { fontSize: 36, lineHeight: 44 } : cards ? { fontSize: 24, lineHeight: 30 } : null}>{forum.icon.emoji}</AppText>
      ) : (
        // A forum without an emoji gets a neutral default icon on its tile.
        <Ionicons testID={id + '-default-icon'} name="chatbubbles-outline" size={Math.round(iconSize / 2)} color={forum.color ? '#ffffff' : theme.colors.textMuted} />
      )}
    </View>
  );
  const unread = badge ? (
    <View testID={id + '-unread'} style={[{ minWidth: 24, paddingHorizontal: 6, paddingVertical: 2, borderRadius: theme.radii.pill, backgroundColor: theme.colors.primary, alignItems: 'center' }, gallery || cards ? { position: 'absolute', top: theme.spacing.sm, right: theme.spacing.sm } : null]}>
      <AppText variant="caption" color="onPrimary" style={{ fontWeight: '700' }}>
        {badge}
      </AppText>
    </View>
  ) : null;
  const counts = strings.forums.threadCount(forum.threadCount || 0) + (forum.isArchived ? ' · ' + strings.forums.archived : '');
  return (
    <Pressable
      testID={id}
      onPress={() => onPress(forum)}
      accessibilityRole="button"
      accessibilityLabel={label}
      accessibilityState={{ selected }}
      style={({ pressed }) => ({
        flexDirection: gallery || cards ? 'column' : 'row',
        alignItems: gallery ? 'center' : cards ? 'flex-start' : 'center',
        gap: gallery || cards ? theme.spacing.sm : theme.spacing.md,
        minHeight: theme.layout.minTouchTarget,
        padding: gallery || cards ? theme.spacing.lg : theme.spacing.md,
        borderRadius: gallery || cards ? theme.radii.lg : theme.radii.md,
        borderWidth: 1,
        borderColor: selected ? theme.colors.primary : theme.colors.border,
        backgroundColor: theme.colors.surface,
        opacity: pressed ? 0.85 : 1
      })}
    >
      {icon}
      <View style={gallery ? { alignItems: 'center', gap: 2 } : cards ? { alignSelf: 'stretch', gap: 4 } : { flex: 1, gap: 2 }}>
        <AppText variant={cards ? 'heading' : 'label'} style={gallery ? { textAlign: 'center' } : null}>
          {forum.name}
        </AppText>
        {forum.description && !gallery ? (
          <AppText variant={cards ? 'body' : 'caption'} color="textMuted">
            {forum.description}
          </AppText>
        ) : null}
        <AppText variant="caption" color="textMuted" style={gallery ? { textAlign: 'center' } : null}>
          {counts}
          {cards && strings.forums.commentCount ? ' · ' + strings.forums.commentCount(forum.commentCount || 0) : ''}
        </AppText>
      </View>
      {unread}
    </Pressable>
  );
}

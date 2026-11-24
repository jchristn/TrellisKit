import { formatRelativeTime } from '@trelliskit/shared';
import { Pressable, View } from 'react-native';
import { useAuth } from '../../auth/AuthProvider';
import { badgeText } from '../../notifications/deepLinks';
import { conversationName, otherMemberOf, previewText } from '../../messaging/format';
import strings from '../../strings';
import { useTheme } from '../../theme';
import AppText from '../AppText';
import Avatar from '../Avatar';

const t = strings.messaging;

/** One conversation in the list: avatar, name, last message preview, time, unread badge, and markers. */
export default function ConversationRow({ conversation, onOpen, selected = false }) {
  const theme = useTheme();
  const { api, user } = useAuth();
  const meId = user ? user.id : null;
  const other = otherMemberOf(conversation, meId);
  const name = conversationName(conversation, meId);
  const preview = previewText(conversation, meId);
  const unread = Number(conversation.unreadCount) || 0;
  const when = (conversation.lastMessage && conversation.lastMessage.createdUtc) || conversation.updatedUtc;
  const marker = conversation.requestState === 'Pending' ? t.requestLabel : conversation.requestState === 'AwaitingAcceptance' ? t.awaitingLabel : conversation.isMuted ? t.muted : null;
  const id = 'conversation-' + conversation.id;
  return (
    <Pressable
      onPress={() => onOpen(conversation)}
      accessibilityRole="button"
      accessibilityLabel={t.rowLabel(name, preview, unread)}
      accessibilityState={{ selected }}
      testID={id}
      style={({ pressed }) => ({
        flexDirection: 'row',
        alignItems: 'center',
        gap: theme.spacing.md,
        paddingVertical: theme.spacing.md,
        paddingHorizontal: theme.spacing.sm,
        borderRadius: theme.radii.md,
        minHeight: theme.layout.minTouchTarget,
        backgroundColor: selected ? theme.colors.surface : 'transparent',
        opacity: pressed ? 0.7 : 1
      })}
    >
      <Avatar uri={other && other.avatarUrl ? api.url(other.avatarUrl) : null} token={api.accessToken} name={name} size={44} />
      <View style={{ flex: 1, gap: theme.spacing.xxs }}>
        <View style={{ flexDirection: 'row', alignItems: 'center', gap: theme.spacing.sm }}>
          <AppText variant="label" style={{ flex: 1, fontWeight: unread ? '700' : '600' }}>
            {name}
          </AppText>
          {when ? (
            <AppText variant="caption" color="textMuted">
              {formatRelativeTime(when)}
            </AppText>
          ) : null}
        </View>
        <View style={{ flexDirection: 'row', alignItems: 'center', gap: theme.spacing.sm }}>
          <AppText color={unread ? 'text' : 'textMuted'} style={{ flex: 1 }} testID={id + '-preview'}>
            {preview}
          </AppText>
          {marker ? (
            <AppText variant="caption" color="textMuted" testID={id + '-marker'}>
              {marker}
            </AppText>
          ) : null}
          {unread ? (
            <View style={{ minWidth: 22, paddingHorizontal: 6, borderRadius: theme.radii.pill, backgroundColor: theme.colors.primary, alignItems: 'center' }} testID={id + '-unread'}>
              <AppText variant="caption" color="onPrimary" style={{ fontWeight: '700' }}>
                {badgeText(unread)}
              </AppText>
            </View>
          ) : null}
        </View>
      </View>
    </Pressable>
  );
}

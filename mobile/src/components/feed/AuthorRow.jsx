import { formatRelativeTime } from '@trelliskit/shared';
import { useRouter } from 'expo-router';
import { Pressable, View } from 'react-native';
import { useAuth } from '../../auth/AuthProvider';
import strings from '../../strings';
import { useTheme } from '../../theme';
import AppText from '../AppText';
import Avatar from '../Avatar';
import { CardFields } from '../profile/ProfileDisplay';

/** Small "Staff" badge for SystemAdmin, Moderator, and FeedModerator holders. */
export function StaffBadge() {
  const theme = useTheme();
  return (
    <View style={{ backgroundColor: theme.colors.accentSoft, borderRadius: theme.radii.pill, paddingHorizontal: theme.spacing.sm, paddingVertical: theme.spacing.xxs }}>
      <AppText variant="caption" color="onAccentSoft" style={{ fontWeight: '700' }}>
        {strings.feed.staff}
      </AppText>
    </View>
  );
}

/**
 * Author line for posts and comments: avatar, name (opens the member's profile), staff badge,
 * card fields from the author's `UserSummary.profile` (for example a rank as a color bar), time, and the "edited" marker that opens the edit history.
 */
export default function AuthorRow({ author, createdUtc, editedUtc, onOpenHistory, onNavigate, size = 40, testID }) {
  const theme = useTheme();
  const router = useRouter();
  const { api } = useAuth();
  const person = author || { displayName: strings.feed.deletedMember, isDeleted: true };
  const canOpen = Boolean(person.id) && !person.isDeleted;
  const time = createdUtc ? formatRelativeTime(createdUtc) : '';
  return (
    <View testID={testID} style={{ flexDirection: 'row', alignItems: 'center', gap: theme.spacing.md }}>
      <Pressable
        disabled={!canOpen}
        onPress={() => {
          if (onNavigate) onNavigate();
          router.push('/members/' + person.id);
        }}
        accessibilityRole={canOpen ? 'button' : undefined}
        accessibilityLabel={strings.feed.authorLabel(person.displayName)}
        accessibilityHint={canOpen ? strings.feed.profileHint : undefined}
        style={{ flex: 1, flexDirection: 'row', alignItems: 'center', gap: theme.spacing.md, minHeight: theme.layout.minTouchTarget }}
        testID={testID ? testID + '-author' : undefined}
      >
        <Avatar uri={person.avatarUrl ? api.url(person.avatarUrl) : null} token={api.accessToken} name={person.displayName} size={size} />
        <View style={{ flex: 1, gap: theme.spacing.xxs }}>
          <View style={{ flexDirection: 'row', alignItems: 'center', flexWrap: 'wrap', gap: theme.spacing.xs }}>
            <AppText variant="label">{person.displayName}</AppText>
            {person.isStaff ? <StaffBadge /> : null}
          </View>
          <CardFields profile={person.profile} testID={testID ? testID + '-card' : undefined} />
        </View>
      </Pressable>
      <View style={{ alignItems: 'flex-end' }}>
        {time ? (
          <AppText variant="caption" color="textMuted">
            {time}
          </AppText>
        ) : null}
        {editedUtc ? (
          <Pressable
            onPress={onOpenHistory}
            disabled={!onOpenHistory}
            accessibilityRole="button"
            accessibilityLabel={strings.feed.edited}
            accessibilityHint={strings.feed.editedHint}
            hitSlop={12}
            style={{ minHeight: 20 }}
            testID={testID ? testID + '-edited' : undefined}
          >
            <AppText variant="caption" color="primary" style={{ textDecorationLine: 'underline' }}>
              {strings.feed.edited}
            </AppText>
          </Pressable>
        ) : null}
      </View>
    </View>
  );
}

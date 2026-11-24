import { Pressable, View } from 'react-native';
import { useAuth } from '../../auth/AuthProvider';
import strings from '../../strings';
import { useTheme } from '../../theme';
import AppText from '../AppText';
import Avatar from '../Avatar';
import { StaffBadge } from '../feed/AuthorRow';
import { CardFields } from './ProfileDisplay';

/** One member row: avatar, name, staff badge, handle, and card fields. Opens the profile. */
export default function MemberRow({ member, fields, onOpen }) {
  const theme = useTheme();
  const { api } = useAuth();
  return (
    <Pressable
      onPress={() => onOpen(member)}
      accessibilityRole="button"
      accessibilityLabel={member.displayName + (member.handle ? ', @' + member.handle : '')}
      accessibilityHint={strings.directory.rowHint}
      testID={'directory-member-' + member.id}
      style={({ pressed }) => ({ flexDirection: 'row', alignItems: 'center', gap: theme.spacing.md, paddingVertical: theme.spacing.sm, minHeight: theme.layout.minTouchTarget, opacity: pressed ? 0.85 : 1 })}
    >
      <Avatar uri={member.avatarUrl ? api.url(member.avatarUrl) : null} token={api.accessToken} name={member.displayName} size={44} />
      <View style={{ flex: 1, gap: theme.spacing.xxs }}>
        <View style={{ flexDirection: 'row', alignItems: 'center', flexWrap: 'wrap', gap: theme.spacing.xs }}>
          <AppText variant="label">{member.displayName}</AppText>
          {member.isStaff ? <StaffBadge /> : null}
        </View>
        {member.handle ? (
          <AppText variant="caption" color="textMuted">
            {'@' + member.handle}
          </AppText>
        ) : null}
        <CardFields profile={member.profile} fields={fields} testID={'directory-member-' + member.id + '-card'} />
      </View>
    </Pressable>
  );
}


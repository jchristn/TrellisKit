import { aiAvailable, propertyFeatureEnabled, signedOutBrowsable } from '@trelliskit/shared';
import { useRouter } from 'expo-router';
import { View } from 'react-native';
import AppText from '../../../src/components/AppText';
import Avatar from '../../../src/components/Avatar';
import Button from '../../../src/components/Button';
import Card from '../../../src/components/Card';
import { CardFields, ProfileFieldList } from '../../../src/components/profile/ProfileDisplay';
import Section from '../../../src/components/Section';
import Screen from '../../../src/components/Screen';
import { useAuth } from '../../../src/auth/AuthProvider';
import { builtInsOf, useProfileFields } from '../../../src/profile/fields';
import { useProperty } from '../../../src/property';
import strings from '../../../src/strings';
import { useTheme } from '../../../src/theme';
import { AccessPromptCard } from '../../../src/components/AccessPrompt';

/**
 * Profile tab: the member's own profile from `Me.profile` (header with card fields, then bio and
 * every field shown on the profile, in profile order), the member directory, account settings,
 * the ideas board, announcements, help and support, About this app, and sign-out. Signed-out guests (anonymous browsing) get the sign-in prompt.
 */
export default function ProfileScreen() {
  const theme = useTheme();
  const router = useRouter();
  const { api, user, signOut } = useAuth();
  const fields = useProfileFields();
  const property = useProperty();
  const bio = builtInsOf(fields).bio;
  const t = strings.profile;
  const profile = (user && user.profile) || {};

  return (
    <Screen testID="screen-profile" contentStyle={{ gap: theme.spacing.lg }}>
      {user ? (
        <Card testID="profile-summary">
          <View style={{ flexDirection: 'row', alignItems: 'center', gap: theme.spacing.lg }}>
            <Avatar uri={user.avatarUrl ? api.url(user.avatarUrl) : null} token={api.accessToken} name={user.displayName} />
            <View style={{ flex: 1, gap: theme.spacing.xxs }}>
              <AppText variant="heading" testID="profile-name">
                {user.displayName}
              </AppText>
              {user.handle ? <AppText color="textMuted">{'@' + user.handle}</AppText> : null}
              <CardFields profile={profile} fields={fields} testID="profile-card" />
            </View>
          </View>
          {bio && bio.showOnProfile !== false && user.bio ? <AppText testID="profile-bio-text">{user.bio}</AppText> : null}
          <ProfileFieldList profile={profile} fields={fields} testID="profile-fields" />
        </Card>
      ) : null}
      {!user ? (
        <>
          <AccessPromptCard required="SignIn" title={strings.access.guestProfileTitle} body={strings.access.joinInBody} testID="profile-guest" />
          {aiAvailable(property) && signedOutBrowsable(property, 'assistants') ? <Button label={strings.assistants.menu} accessibilityHint={strings.assistants.menuHint} variant="secondary" onPress={() => router.push('/assistants')} testID="profile-assistants" /> : null}
          <Button label={t.about} accessibilityHint={t.aboutHint} variant="secondary" onPress={() => router.push('/profile/about')} />
        </>
      ) : (
      <Section title={t.settingsTitle} testID="profile-settings">
        <View style={{ gap: theme.spacing.md }}>
          <Button label={t.members} accessibilityHint={t.membersHint} variant="secondary" onPress={() => router.push('/members')} testID="profile-members" />
          <Button label={t.account} accessibilityHint={t.accountHint} onPress={() => router.push('/account')} testID="profile-account" />
          {propertyFeatureEnabled(property, 'ideas') ? <Button label={strings.ideas.menu} accessibilityHint={strings.ideas.menuHint} variant="secondary" onPress={() => router.push('/ideas')} testID="profile-ideas" /> : null}
          <Button label={strings.announcements.menu} accessibilityHint={strings.announcements.menuHint} variant="secondary" onPress={() => router.push('/announcements')} testID="profile-announcements" />
          <Button label={strings.support.menu} accessibilityHint={strings.support.menuHint} variant="secondary" onPress={() => router.push('/support')} testID="profile-support" />
          {aiAvailable(property) ? <Button label={strings.assistants.menu} accessibilityHint={strings.assistants.menuHint} variant="secondary" onPress={() => router.push('/assistants')} testID="profile-assistants" /> : null}
          <Button label={t.about} accessibilityHint={t.aboutHint} variant="secondary" onPress={() => router.push('/profile/about')} />
          <Button label={t.signOut} accessibilityHint={t.signOutHint} variant="ghost" onPress={() => signOut({ notice: 'signedOut' })} testID="profile-sign-out" />
        </View>
      </Section>
      )}
    </Screen>
  );
}

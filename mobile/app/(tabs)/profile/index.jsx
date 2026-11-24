import { aiAvailable, signedOutBrowsable } from '@trelliskit/shared';
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
 * every field shown on the profile, in profile order), account settings, About this app, and sign-out.
 * Community destinations live in the main drawer. Signed-out guests (anonymous browsing) get the sign-in prompt.
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
          <Button label={t.account} accessibilityHint={t.accountHint} onPress={() => router.push('/account')} testID="profile-account" />
          <Button label={t.about} accessibilityHint={t.aboutHint} variant="secondary" onPress={() => router.push('/profile/about')} />
          <Button label={t.signOut} accessibilityHint={t.signOutHint} variant="ghost" onPress={() => signOut({ notice: 'signedOut' })} testID="profile-sign-out" />
        </View>
      </Section>
      )}
    </Screen>
  );
}

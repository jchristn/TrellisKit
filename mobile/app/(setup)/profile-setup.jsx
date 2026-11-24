import { missingProfileFields } from '@trelliskit/shared';
import { useRouter } from 'expo-router';
import { useState } from 'react';
import AppText from '../../src/components/AppText';
import Notice from '../../src/components/Notice';
import Screen from '../../src/components/Screen';
import ProfileForm from '../../src/components/profile/ProfileForm';
import { useAuth } from '../../src/auth/AuthProvider';
import { nextRoute } from '../../src/auth/routing';
import { missingLabels, stillMissing, useProfileFields } from '../../src/profile/fields';
import strings from '../../src/strings';
import { useTheme } from '../../src/theme';

/**
 * Profile setup during onboarding, and "Complete your profile" for existing accounts whose
 * `profileComplete` turned false (for example after a new required sign-up field was added).
 * Shows the built-ins collected at sign-up, then every sign-up field in profile order; required
 * fields must validate before Continue and optional ones can be skipped.
 */
export default function ProfileSetupScreen() {
  const theme = useTheme();
  const router = useRouter();
  const fields = useProfileFields();
  const { api, user, updateUser } = useAuth();
  const missing = missingProfileFields(user, fields);
  const existing = missing.length > 0 && !missing.some((m) => m.key === 'displayName' || m.key === 'handle') && Boolean(user && user.handle);
  const [draft, setDraft] = useState(null);
  // The note follows the form: a field drops out of "Still needed" as soon as it has a value.
  const labels = missingLabels(stillMissing(missing, draft), fields);
  const t = strings.profileSetup;
  return (
    <Screen contentStyle={{ gap: theme.spacing.lg }} testID="screen-profile-setup">
      <AppText variant="title" accessibilityRole="header">
        {existing ? t.completeHeading : t.heading}
      </AppText>
      <AppText color="textMuted">{existing ? t.completeBody : t.body}</AppText>
      <Notice tone="info" testID="profile-setup-missing">
        {labels ? t.missing(labels) : null}
      </Notice>
      <ProfileForm api={api} user={user} mode="onboarding" onUserChange={updateUser} onSaved={(me) => router.replace(nextRoute(updateUser(me)))} submitLabel={t.submit} onValuesChange={setDraft} />
    </Screen>
  );
}

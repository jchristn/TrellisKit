import { useState } from 'react';
import Notice from '../../src/components/Notice';
import Screen from '../../src/components/Screen';
import ProfileForm from '../../src/components/profile/ProfileForm';
import PublicPageEditor from '../../src/components/profile/PublicPageEditor';
import { useAuth } from '../../src/auth/AuthProvider';
import strings from '../../src/strings';
import { useTheme } from '../../src/theme';

/** Edit profile: the built-ins, then every field the member edits, staff-set fields read-only, and the opt-in public profile page. */
export default function EditProfileScreen() {
  const theme = useTheme();
  const { api, user, updateUser } = useAuth();
  const [saved, setSaved] = useState(false);
  return (
    <Screen contentStyle={{ gap: theme.spacing.lg }} testID="screen-edit-profile">
      <Notice tone="success">{saved ? strings.account.saved : null}</Notice>
      <ProfileForm
        api={api}
        user={user}
        mode="editor"
        onUserChange={updateUser}
        onSaved={(me) => {
          updateUser(me);
          setSaved(true);
        }}
        submitLabel={strings.account.save}
      />
      <PublicPageEditor api={api} onSaved={(page) => updateUser({ ...user, publicPage: { enabled: page.enabled, slug: page.slug, path: page.path, url: page.url } })} />
    </Screen>
  );
}

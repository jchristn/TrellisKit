import { useRouter } from 'expo-router';
import { useState } from 'react';
import AppText from '../../src/components/AppText';
import Button from '../../src/components/Button';
import DateField from '../../src/components/DateField';
import Notice from '../../src/components/Notice';
import Screen from '../../src/components/Screen';
import { account } from '../../src/api/endpoints';
import { useAuth } from '../../src/auth/AuthProvider';
import { describeError } from '../../src/auth/errors';
import { nextRoute } from '../../src/auth/routing';
import { validateDateOfBirth } from '../../src/forms';
import strings from '../../src/strings';
import { useTheme } from '../../src/theme';

/**
 * Age attestation for accounts created through Apple, Google, or Facebook. The server closes an
 * account whose date of birth is under the property minimum age (403 AgeRequirementNotMet), and the app signs out.
 */
export default function AgeScreen() {
  const theme = useTheme();
  const router = useRouter();
  const { api, updateUser, signOut } = useAuth();
  const [dob, setDob] = useState({ month: '', day: '', year: '' });
  const [error, setError] = useState(null);
  const [message, setMessage] = useState(null);
  const [busy, setBusy] = useState(false);

  async function submit() {
    setMessage(null);
    const result = validateDateOfBirth(dob);
    if (!result.dateOfBirth) {
      setError(result.error);
      return;
    }
    setError(null);
    setBusy(true);
    try {
      const me = await account.attestAge(api, result.dateOfBirth);
      router.replace(nextRoute(updateUser(me)));
    } catch (e) {
      setBusy(false);
      if (e && e.code === 'AgeRequirementNotMet') {
        await signOut({ revoke: false, notice: 'ageRequirement' });
        router.replace('/welcome');
        return;
      }
      setMessage(describeError(e));
    }
  }

  return (
    <Screen contentStyle={{ gap: theme.spacing.lg }}>
      <AppText variant="title" accessibilityRole="header">
        {strings.age.heading}
      </AppText>
      <AppText color="textMuted">{strings.age.body}</AppText>
      <DateField label={strings.register.dateOfBirth} value={dob} onChange={setDob} error={error} testID="age-dob" />
      <Notice tone="error">{message}</Notice>
      <Button label={strings.age.submit} onPress={submit} disabled={busy} testID="age-submit" />
      <Button label={strings.age.signOut} variant="ghost" onPress={() => signOut()} />
    </Screen>
  );
}

import { useRouter } from 'expo-router';
import { useState } from 'react';
import { View } from 'react-native';
import AppText from '../../src/components/AppText';
import Button from '../../src/components/Button';
import Notice from '../../src/components/Notice';
import Screen from '../../src/components/Screen';
import TextField from '../../src/components/TextField';
import { auth as authApi } from '../../src/api/endpoints';
import { useAuth } from '../../src/auth/AuthProvider';
import { describeError } from '../../src/auth/errors';
import strings from '../../src/strings';
import { useTheme } from '../../src/theme';

/** Request a password reset email. The reset itself happens on the emailed web link. */
export default function ForgotPasswordScreen() {
  const theme = useTheme();
  const router = useRouter();
  const { api } = useAuth();
  const t = strings.forgotPassword;
  const [email, setEmail] = useState('');
  const [sent, setSent] = useState(false);
  const [error, setError] = useState(null);
  const [busy, setBusy] = useState(false);

  async function submit() {
    setError(null);
    if (!email.trim()) {
      setError(t.invalidEmail);
      return;
    }
    setBusy(true);
    try {
      await authApi.forgotPassword(api, email.trim());
      setSent(true);
    } catch (e) {
      setError(describeError(e));
    } finally {
      setBusy(false);
    }
  }

  if (sent) {
    return (
      <Screen contentStyle={{ gap: theme.spacing.lg }}>
        <View style={{ gap: theme.spacing.sm }} testID="forgot-sent">
          <AppText variant="title" accessibilityRole="header">
            {t.sentTitle}
          </AppText>
          <AppText>{t.sentBody}</AppText>
        </View>
        <Button label={t.backToSignIn} variant="secondary" onPress={() => router.back()} />
      </Screen>
    );
  }

  return (
    <Screen contentStyle={{ gap: theme.spacing.lg }}>
      <AppText>{t.body}</AppText>
      <TextField label={t.email} value={email} onChangeText={setEmail} autoCapitalize="none" autoCorrect={false} keyboardType="email-address" autoComplete="email" testID="forgot-email" />
      <Notice tone="error">{error}</Notice>
      <Button label={t.submit} onPress={submit} disabled={busy} testID="forgot-submit" />
    </Screen>
  );
}

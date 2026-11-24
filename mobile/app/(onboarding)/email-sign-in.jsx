import { useRouter } from 'expo-router';
import { useState } from 'react';
import { View } from 'react-native';
import Button from '../../src/components/Button';
import LinkButton from '../../src/components/LinkButton';
import Notice from '../../src/components/Notice';
import Screen from '../../src/components/Screen';
import TextField from '../../src/components/TextField';
import { auth as authApi } from '../../src/api/endpoints';
import { useAuth } from '../../src/auth/AuthProvider';
import { offerToSavePassword } from '../../src/native/sharedCredentials';
import { useProperty } from '../../src/property';
import { describeError } from '../../src/auth/errors';
import strings from '../../src/strings';
import { useTheme } from '../../src/theme';

/** Email and password sign-in. On success iOS offers to save the login for the website domain (sharedCredentials). */
export default function EmailSignInScreen() {
  const theme = useTheme();
  const router = useRouter();
  const { api, completeSignIn } = useAuth();
  const property = useProperty();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(null);
  const t = strings.emailSignIn;

  async function submit() {
    setError(null);
    if (!email.trim() || !password) {
      setError(t.missing);
      return;
    }
    setBusy(true);
    try {
      const result = await authApi.login(api, { email: email.trim(), password });
      offerToSavePassword(property, email.trim(), password);
      await completeSignIn(result);
    } catch (e) {
      setError(describeError(e));
      setBusy(false);
    }
  }

  return (
    <Screen contentStyle={{ gap: theme.spacing.lg }}>
      <TextField label={t.email} value={email} onChangeText={setEmail} autoCapitalize="none" autoCorrect={false} keyboardType="email-address" autoComplete="email" textContentType="username" testID="email-input" />
      <TextField label={t.password} value={password} onChangeText={setPassword} secureTextEntry autoComplete="current-password" textContentType="password" testID="password-input" onSubmitEditing={submit} />
      <Notice tone="error" testID="email-sign-in-error">
        {error}
      </Notice>
      <Button label={t.submit} onPress={submit} disabled={busy} testID="email-sign-in-submit" />
      <View style={{ alignItems: 'flex-start' }}>
        <LinkButton label={t.forgot} accessibilityHint={t.forgotHint} onPress={() => router.push('/forgot-password')} />
        <LinkButton label={t.noAccount} onPress={() => router.replace('/register')} />
      </View>
    </Screen>
  );
}

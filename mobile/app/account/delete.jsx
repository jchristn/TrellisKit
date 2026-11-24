import { useRouter } from 'expo-router';
import { useState } from 'react';
import { View } from 'react-native';
import AppText from '../../src/components/AppText';
import Button from '../../src/components/Button';
import Notice from '../../src/components/Notice';
import Screen from '../../src/components/Screen';
import TextField from '../../src/components/TextField';
import { account } from '../../src/api/endpoints';
import { useAuth } from '../../src/auth/AuthProvider';
import { describeError } from '../../src/auth/errors';
import strings from '../../src/strings';
import { useTheme } from '../../src/theme';

/**
 * In-app account deletion (App Store Guideline 5.1.1(v)). The member types DELETE to enable the
 * button. Afterwards the server's message and storeSubscriptionNote are shown before sign-out.
 */
export default function DeleteAccountScreen() {
  const theme = useTheme();
  const router = useRouter();
  const { api, signOut } = useAuth();
  const t = strings.deleteAccount;
  const [confirm, setConfirm] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(null);
  const [result, setResult] = useState(null);
  const confirmed = confirm.trim() === t.confirmWord;

  async function remove() {
    if (!confirmed) return;
    setError(null);
    setBusy(true);
    try {
      setResult((await account.deleteAccount(api)) || {});
    } catch (e) {
      setError(describeError(e));
    } finally {
      setBusy(false);
    }
  }

  async function done() {
    await signOut({ revoke: false, notice: 'accountDeleted' });
    router.replace('/welcome');
  }

  if (result) {
    return (
      <Screen contentStyle={{ gap: theme.spacing.lg }} testID="delete-done">
        <AppText variant="title" accessibilityRole="header">
          {t.doneTitle}
        </AppText>
        {result.message ? <AppText testID="delete-message">{result.message}</AppText> : null}
        <Notice testID="delete-store-note">{result.storeSubscriptionNote || null}</Notice>
        <Button label={t.done} onPress={done} testID="delete-finish" />
      </Screen>
    );
  }

  return (
    <Screen contentStyle={{ gap: theme.spacing.lg }} testID="screen-delete">
      <View style={{ gap: theme.spacing.sm }}>
        <AppText variant="title" accessibilityRole="header">
          {t.heading}
        </AppText>
        <AppText>{t.body}</AppText>
        <AppText color="textMuted">{t.storeNote}</AppText>
      </View>
      <TextField label={t.confirmLabel} value={confirm} onChangeText={setConfirm} autoCapitalize="characters" autoCorrect={false} testID="delete-confirm" />
      <Notice tone="error">{error}</Notice>
      <Button label={t.submit} onPress={remove} disabled={!confirmed || busy} testID="delete-submit" />
    </Screen>
  );
}

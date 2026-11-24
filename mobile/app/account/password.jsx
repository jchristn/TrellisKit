import { useNavigation } from 'expo-router';
import { useLayoutEffect, useState } from 'react';
import AppText from '../../src/components/AppText';
import Button from '../../src/components/Button';
import Notice from '../../src/components/Notice';
import Screen from '../../src/components/Screen';
import TextField from '../../src/components/TextField';
import { account } from '../../src/api/endpoints';
import { useAuth } from '../../src/auth/AuthProvider';
import { describeError, fieldErrorsOf } from '../../src/auth/errors';
import { passwordError } from '../../src/forms';
import strings from '../../src/strings';
import { useTheme } from '../../src/theme';

/** Set a password (social-only accounts) or change the existing one. */
export default function PasswordScreen() {
  const theme = useTheme();
  const navigation = useNavigation();
  const { api, user, refreshUser } = useAuth();
  const t = strings.password;
  const hasPassword = Boolean(user && (user.signInMethods || []).includes('Email'));
  const [current, setCurrent] = useState('');
  const [next, setNext] = useState('');
  const [errors, setErrors] = useState({});
  const [message, setMessage] = useState(null);
  const [busy, setBusy] = useState(false);

  useLayoutEffect(() => {
    navigation.setOptions({ title: hasPassword ? t.changeTitle : t.setTitle });
  }, [navigation, hasPassword, t.changeTitle, t.setTitle]);

  async function submit() {
    setMessage(null);
    const found = {};
    const policy = passwordError(next, user && user.email);
    if (policy) found.newPassword = policy;
    if (hasPassword && !current) found.currentPassword = strings.emailSignIn.missing;
    setErrors(found);
    if (Object.keys(found).length) return;
    setBusy(true);
    try {
      await account.setPassword(api, { currentPassword: hasPassword ? current : undefined, newPassword: next });
      setCurrent('');
      setNext('');
      setMessage({ tone: 'success', text: t.saved });
      refreshUser().catch(() => {});
    } catch (e) {
      const fields = fieldErrorsOf(e);
      setErrors(fields);
      setMessage(Object.keys(fields).length ? null : { tone: 'error', text: describeError(e) });
    } finally {
      setBusy(false);
    }
  }

  return (
    <Screen contentStyle={{ gap: theme.spacing.lg }} testID="screen-password">
      {hasPassword ? null : <AppText color="textMuted">{t.setBody}</AppText>}
      {hasPassword ? <TextField label={t.current} value={current} onChangeText={setCurrent} secureTextEntry autoComplete="current-password" textContentType="password" error={errors.currentPassword} testID="password-current" /> : null}
      <TextField label={t.next} hint={t.hint} value={next} onChangeText={setNext} secureTextEntry autoComplete="new-password" textContentType="newPassword" maxLength={128} error={errors.newPassword} testID="password-new" />
      <Notice tone={message ? message.tone : 'info'}>{message ? message.text : null}</Notice>
      <Button label={t.submit} onPress={submit} disabled={busy} testID="password-submit" />
    </Screen>
  );
}

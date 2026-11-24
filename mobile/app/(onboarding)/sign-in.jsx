import Ionicons from '@expo/vector-icons/Ionicons';
import { useRouter } from 'expo-router';
import { useState } from 'react';
import { View } from 'react-native';
import { BannerSlot } from '../../src/appConfig';
import AppText from '../../src/components/AppText';
import Button from '../../src/components/Button';
import ErrorState from '../../src/components/ErrorState';
import Loading from '../../src/components/Loading';
import Notice from '../../src/components/Notice';
import Screen from '../../src/components/Screen';
import SocialButtons from '../../src/components/auth/SocialButtons';
import { auth as authApi } from '../../src/api/endpoints';
import { useAuth } from '../../src/auth/AuthProvider';
import { describeError } from '../../src/auth/errors';
import { getSocialCredential } from '../../src/native/social';
import strings from '../../src/strings';
import { useTheme } from '../../src/theme';

/** Sign-in choices: Apple, Google, Facebook (when enabled), and email. */
export default function SignInScreen() {
  const theme = useTheme();
  const router = useRouter();
  const { api, providers, providersError, reloadProviders, notice, completeSignIn } = useAuth();
  const noticeText = notice === 'sessionExpired' || notice === 'accountBanned' ? strings.welcome.notices[notice] : null;
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(null);

  async function signInWith(provider) {
    setError(null);
    setBusy(true);
    try {
      const credential = await getSocialCredential(provider, providers);
      if (!credential) return;
      const result = await authApi.social(api, provider, credential);
      await completeSignIn(result);
    } catch (e) {
      setError(describeError(e, { providerLabel: strings.signIn.providerLabels[provider] }));
    } finally {
      setBusy(false);
    }
  }

  return (
    <Screen contentStyle={{ gap: theme.spacing.xl }}>
      <BannerSlot placement="SignIn" />
      <View style={{ gap: theme.spacing.sm }}>
        <AppText variant="title" accessibilityRole="header">
          {strings.signIn.heading}
        </AppText>
        <AppText color="textMuted">{strings.signIn.body}</AppText>
      </View>
      <Notice testID="sign-in-notice">{noticeText}</Notice>
      <Notice tone="error" testID="sign-in-error">
        {error}
      </Notice>
      {providersError ? (
        <ErrorState title={strings.signIn.providersFailedTitle} message={strings.signIn.providersFailedBody} onRetry={reloadProviders} retryHint={strings.signIn.providersRetryHint} testID="sign-in-providers-error" />
      ) : null}
      {!providers ? (
        <Loading label={strings.signIn.loadingProviders} />
      ) : (
        <View style={{ gap: theme.spacing.md }}>
          <SocialButtons providers={providers} onPress={signInWith} disabled={busy} />
          {providers.email !== false ? (
            <>
              <Button
                testID="sign-in-email"
                label={strings.signIn.email}
                accessibilityHint={strings.signIn.emailHint}
                variant="secondary"
                disabled={busy}
                onPress={() => router.push('/email-sign-in')}
                icon={<Ionicons name="mail-outline" size={20} color={theme.colors.onSecondary} />}
              />
              <Button testID="sign-in-register" label={strings.signIn.createAccount} accessibilityHint={strings.signIn.createAccountHint} variant="ghost" disabled={busy} onPress={() => router.push('/register')} />
            </>
          ) : null}
        </View>
      )}
      {busy ? <Loading label={strings.signIn.working} /> : null}
    </Screen>
  );
}

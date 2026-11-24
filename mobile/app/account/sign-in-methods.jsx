import { useRouter } from 'expo-router';
import { useState } from 'react';
import { Platform, View } from 'react-native';
import AppText from '../../src/components/AppText';
import Button from '../../src/components/Button';
import Notice from '../../src/components/Notice';
import Screen from '../../src/components/Screen';
import Section, { InfoRow } from '../../src/components/Section';
import { useAppleAvailable } from '../../src/components/auth/SocialButtons';
import { account } from '../../src/api/endpoints';
import { useAuth } from '../../src/auth/AuthProvider';
import { describeError } from '../../src/auth/errors';
import { getSocialCredential, nativeSocialAvailability, SocialProviders } from '../../src/native/social';
import strings from '../../src/strings';
import { useTheme } from '../../src/theme';

const METHOD = { apple: 'Apple', google: 'Google', facebook: 'Facebook' };

/** Link and unlink Apple, Google, and Facebook; set or change the password. */
export default function SignInMethodsScreen() {
  const theme = useTheme();
  const router = useRouter();
  const { api, user, providers, updateUser } = useAuth();
  const appleAvailable = useAppleAvailable();
  const native = nativeSocialAvailability();
  const t = strings.signInMethods;
  const [message, setMessage] = useState(null);
  const [busy, setBusy] = useState(false);

  if (!user) return null;
  const methods = user.signInMethods || [];
  const hasPassword = methods.includes('Email');

  function canLink(provider) {
    const p = providers || {};
    if (provider === 'apple') return Platform.OS === 'ios' && Boolean(p.apple && p.apple.enabled) && appleAvailable;
    if (provider === 'google') return Boolean(p.google && p.google.enabled) && native.googleAvailable;
    return Boolean(p.facebook && p.facebook.enabled) && native.facebookAvailable;
  }

  async function link(provider) {
    setMessage(null);
    setBusy(true);
    const name = strings.signIn.providerLabels[provider];
    try {
      const credential = await getSocialCredential(provider, providers);
      if (!credential) return;
      updateUser(await account.linkIdentity(api, provider, credential));
      setMessage({ tone: 'success', text: t.linkedMessage(name) });
    } catch (e) {
      setMessage({ tone: 'error', text: describeError(e, { providerLabel: name }) });
    } finally {
      setBusy(false);
    }
  }

  async function unlink(provider) {
    setMessage(null);
    setBusy(true);
    const name = strings.signIn.providerLabels[provider];
    try {
      updateUser(await account.unlinkIdentity(api, provider));
      setMessage({ tone: 'success', text: t.unlinkedMessage(name) });
    } catch (e) {
      setMessage({ tone: 'error', text: describeError(e) });
    } finally {
      setBusy(false);
    }
  }

  return (
    <Screen contentStyle={{ gap: theme.spacing.lg }} testID="screen-sign-in-methods">
      <AppText color="textMuted">{t.body}</AppText>
      <Notice tone={message ? message.tone : 'info'} testID="methods-message">
        {message ? message.text : null}
      </Notice>
      <Section>
        <InfoRow label={t.emailPassword} value={hasPassword ? t.linked : t.notLinked} testID="method-email" />
        <Button label={hasPassword ? t.changePassword : t.setPassword} variant="secondary" onPress={() => router.push('/account/password')} testID="method-password" />
      </Section>
      {SocialProviders.map((provider) => {
        const name = strings.signIn.providerLabels[provider];
        const linked = methods.includes(METHOD[provider]);
        const available = canLink(provider);
        if (!linked && !available) return null;
        return (
          <Section key={provider} testID={'method-' + provider}>
            <InfoRow label={name} value={linked ? t.linked : t.notLinked} />
            <View>
              {linked ? (
                <Button label={t.unlink(name)} variant="ghost" onPress={() => unlink(provider)} disabled={busy} testID={'unlink-' + provider} />
              ) : (
                <Button label={t.link(name)} variant="secondary" onPress={() => link(provider)} disabled={busy} testID={'link-' + provider} />
              )}
            </View>
          </Section>
        );
      })}
    </Screen>
  );
}

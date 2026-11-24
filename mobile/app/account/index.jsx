import { useRouter } from 'expo-router';
import { useState } from 'react';
import { View } from 'react-native';
import { BannerSlot } from '../../src/appConfig';
import AppText from '../../src/components/AppText';
import Button from '../../src/components/Button';
import Notice from '../../src/components/Notice';
import Screen from '../../src/components/Screen';
import Section, { InfoRow } from '../../src/components/Section';
import { auth as authApi, runAccountExport } from '../../src/api/endpoints';
import { useAuth } from '../../src/auth/AuthProvider';
import { describeError } from '../../src/auth/errors';
import { openInAppBrowser } from '../../src/links';
import strings from '../../src/strings';
import { useTheme } from '../../src/theme';
import { useProperty } from '../../src/property';
import { propertyFeatureEnabled, purchasesEnabled } from '@trelliskit/shared';

/**
 * An export queued earlier in this app session that was still being prepared (resumed on the next
 * tap). Another member's id answers 404, which clears it.
 */
let pendingExportId = null;

/** Account hub: profile, email verification, sign-in methods, membership, legal documents, data, deletion. */
export default function AccountScreen() {
  const theme = useTheme();
  const router = useRouter();
  const { api, user, signOut } = useAuth();
  const t = strings.account;
  // The membership section shows only where memberships are sold (not in the Open access model).
  const property = useProperty();
  const sells = purchasesEnabled(property);
  const messagingOn = propertyFeatureEnabled(property, 'messaging');
  const [message, setMessage] = useState(null);
  const [busy, setBusy] = useState(false);

  if (!user) return null;

  async function resend() {
    setMessage(null);
    setBusy(true);
    try {
      await authApi.resendVerification(api);
      setMessage({ tone: 'success', text: t.resent });
    } catch (e) {
      setMessage({ tone: 'error', text: describeError(e) });
    } finally {
      setBusy(false);
    }
  }

  async function exportData() {
    setMessage({ tone: 'info', text: t.exportPreparing });
    setBusy(true);
    try {
      const result = await runAccountExport(api, {
        exportId: pendingExportId,
        onQueued: (id) => {
          pendingExportId = id;
        }
      });
      if (result && result.status === 'Ready' && result.downloadUrl) {
        pendingExportId = null;
        setMessage({ tone: 'success', text: t.exportReady });
        await openInAppBrowser(api.url(result.downloadUrl));
      } else if (result && (result.status === 'Failed' || result.status === 'Expired')) {
        pendingExportId = null;
        setMessage({ tone: 'error', text: t.exportFailed });
      } else {
        setMessage({ tone: 'info', text: t.exportStillPreparing });
      }
    } catch (e) {
      if (e && e.status === 404) pendingExportId = null;
      setMessage({ tone: 'error', text: e && e.code === 'ExportInProgress' ? t.exportStillPreparing : describeError(e) });
    } finally {
      setBusy(false);
    }
  }

  return (
    <Screen contentStyle={{ gap: theme.spacing.xl }} testID="screen-account">
      <BannerSlot placement="Account" />
      <Notice tone={message ? message.tone : 'info'} testID="account-message">
        {message ? message.text : null}
      </Notice>

      <Section title={t.profileSection}>
        <AppText variant="heading">{user.displayName}</AppText>
        {user.handle ? <AppText color="textMuted">{'@' + user.handle}</AppText> : null}
        <Button label={t.editProfile} variant="secondary" onPress={() => router.push('/account/edit-profile')} testID="account-edit-profile" />
      </Section>

      <Section title={t.emailSection}>
        <InfoRow label={user.email || ''} value={user.emailVerified ? t.verified : t.notVerified} valueColor={user.emailVerified ? 'success' : 'warning'} testID="account-email-status" />
        {user.emailVerified ? null : <Button label={t.resend} variant="secondary" onPress={resend} disabled={busy} testID="account-resend" />}
      </Section>

      <Section title={t.signInSection}>
        <Button label={t.signInMethods} variant="secondary" onPress={() => router.push('/account/sign-in-methods')} testID="account-sign-in-methods" />
      </Section>

      {sells ? (
        <Section title={t.membershipSection}>
          <Button label={t.membership} variant="secondary" onPress={() => router.push('/account/membership')} testID="account-membership" />
        </Section>
      ) : null}

      <Section title={t.communitySection}>
        <Button label={t.notifications} accessibilityHint={t.notificationsHint} variant="secondary" onPress={() => router.push('/account/notifications')} testID="account-notifications" />
        {messagingOn ? <Button label={t.messaging} accessibilityHint={t.messagingHint} variant="secondary" onPress={() => router.push('/account/messaging')} testID="account-messaging" /> : null}
        <Button label={t.blocked} accessibilityHint={t.blockedHint} variant="secondary" onPress={() => router.push('/account/blocked')} testID="account-blocked" />
      </Section>

      <Section title={t.appearanceSection}>
        <Button label={t.appearance} accessibilityHint={t.appearanceHint} variant="secondary" onPress={() => router.push('/account/appearance')} testID="account-appearance" />
      </Section>

      <Section title={t.supportSection}>
        <Button label={strings.support.menu} accessibilityHint={strings.support.menuHint} variant="secondary" onPress={() => router.push('/support')} testID="account-support" />
      </Section>

      <Section title={t.legalSection}>
        <Button label={t.legal} accessibilityHint={t.legalHint} variant="secondary" onPress={() => router.push('/account/legal')} testID="account-legal" />
      </Section>

      <Section title={t.dataSection}>
        <Button label={t.exportData} accessibilityHint={t.exportHint} variant="secondary" onPress={exportData} disabled={busy} testID="account-export" />
        <Button label={t.deleteAccount} accessibilityHint={t.deleteHint} variant="ghost" onPress={() => router.push('/account/delete')} testID="account-delete" />
      </Section>

      <View>
        <Button label={t.signOut} variant="ghost" onPress={() => signOut({ notice: 'signedOut' })} testID="account-sign-out" />
      </View>
    </Screen>
  );
}

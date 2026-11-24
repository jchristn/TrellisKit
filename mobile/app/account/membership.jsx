import { Redirect, useRouter } from 'expo-router';
import { useCallback, useEffect, useState } from 'react';
import { Platform } from 'react-native';
import AppText from '../../src/components/AppText';
import Button from '../../src/components/Button';
import DocumentLinks from '../../src/components/DocumentLinks';
import ErrorState from '../../src/components/ErrorState';
import Screen from '../../src/components/Screen';
import Section, { InfoRow } from '../../src/components/Section';
import { useAuth } from '../../src/auth/AuthProvider';
import { isSystemAdmin } from '../../src/auth/routing';
import { storeConfig } from '../../src/billing/purchases';
import { purchaseDocuments } from '../../src/documents/documents';
import { AppleSubscriptionsUrl, googleSubscriptionsUrl, openExternal } from '../../src/links';
import { storeProductIdOf, useProperty } from '../../src/property';
import strings from '../../src/strings';
import { useTheme } from '../../src/theme';
import { purchasesEnabled } from '@trelliskit/shared';

function formatDate(iso) {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return '';
  return date.toLocaleDateString(undefined, { year: 'numeric', month: 'long', day: 'numeric' });
}

/** Describe the membership type without mentioning how a non-store membership was obtained. */
export function membershipTypeLabel(membership) {
  const t = strings.membership;
  if (!membership || !membership.source) return null;
  if (membership.source !== 'Subscription') return t.typeComplimentary;
  if (membership.store === 'Apple') return t.typeAppStore;
  if (membership.store === 'Google') return t.typeGooglePlay;
  return t.typeSubscription;
}

/**
 * Membership status. "Manage subscription" opens App Store or Google Play subscription settings
 * for store subscriptions on the matching platform. Other memberships show status and expiry
 * only, with no links out of the app. The property's `ShownAtPurchase` documents are linked
 * (they open in the app).
 */
export default function MembershipScreen() {
  const theme = useTheme();
  const router = useRouter();
  const { user, providers, refreshUser } = useAuth();
  const property = useProperty();
  const t = strings.membership;
  const [refreshFailed, setRefreshFailed] = useState(false);

  const reload = useCallback(() => {
    setRefreshFailed(false);
    return refreshUser().catch(() => setRefreshFailed(true));
  }, [refreshUser]);

  useEffect(() => {
    // Refresh once on open; the cached membership shows meanwhile. State changes only after
    // the request settles.
    refreshUser().catch(() => setRefreshFailed(true));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  if (!user) return null;
  if (!purchasesEnabled(property)) return <Redirect href="/account" />;
  const m = user.membership || { isActive: false };
  const type = membershipTypeLabel(m);
  const isStoreSub = m.source === 'Subscription' && (m.store === 'Apple' || m.store === 'Google');
  const storeMatches = (m.store === 'Apple' && Platform.OS === 'ios') || (m.store === 'Google' && Platform.OS === 'android');
  const productId = storeConfig(providers, Platform.OS, storeProductIdOf(property)).productId;
  const manageUrl = Platform.OS === 'ios' ? AppleSubscriptionsUrl : googleSubscriptionsUrl(productId);

  return (
    <Screen contentStyle={{ gap: theme.spacing.lg }} testID="screen-membership">
      {refreshFailed ? <ErrorState title={t.refreshFailedTitle} message={t.refreshFailedBody} onRetry={reload} retryHint={t.retryHint} testID="membership-refresh-error" /> : null}
      <Section>
        <InfoRow label={t.status} value={m.isActive ? t.active : t.inactive} valueColor={m.isActive ? 'success' : 'warning'} testID="membership-status" />
        {type ? <InfoRow label={t.type} value={type} testID="membership-type" /> : null}
        {m.isActive ? (
          <InfoRow
            label={m.autoRenew ? t.renews : t.expires}
            value={m.expiresUtc ? formatDate(m.expiresUtc) : t.noExpiry}
            testID="membership-expiry"
          />
        ) : null}
        {m.isActive && m.source === 'Subscription' ? <InfoRow label={t.autoRenew} value={m.autoRenew ? t.on : t.off} /> : null}
      </Section>
      {isSystemAdmin(user) ? <AppText color="textMuted">{t.adminAccess}</AppText> : null}
      {isStoreSub && storeMatches ? (
        <Button label={t.manage} accessibilityHint={Platform.OS === 'ios' ? t.manageHintIos : t.manageHintAndroid} variant="secondary" onPress={() => openExternal(manageUrl)} testID="membership-manage" />
      ) : null}
      {isStoreSub && !storeMatches ? <AppText color="textMuted">{m.store === 'Apple' ? t.otherStoreApple : t.otherStoreGoogle}</AppText> : null}
      <DocumentLinks documents={purchaseDocuments(property)} testID="membership-document" />
      {!m.isActive ? <Button label={t.becomeMember} onPress={() => router.push('/subscribe')} testID="membership-subscribe" /> : null}
    </Screen>
  );
}

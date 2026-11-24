import { Redirect, useRouter } from 'expo-router';
import { Platform, View } from 'react-native';
import AppText from '../../src/components/AppText';
import Button from '../../src/components/Button';
import Card from '../../src/components/Card';
import DocumentLinks from '../../src/components/DocumentLinks';
import LinkButton from '../../src/components/LinkButton';
import Loading from '../../src/components/Loading';
import Notice from '../../src/components/Notice';
import Screen from '../../src/components/Screen';
import { useAuth } from '../../src/auth/AuthProvider';
import { hasMemberAccess, needsPaywall, nextRoute } from '../../src/auth/routing';
import { usePurchases } from '../../src/billing/PurchaseProvider';
import { usePaywall } from '../../src/billing/usePaywall';
import { paywallDocuments } from '../../src/documents/documents';
import { openInAppBrowser, PrivacyUrl, TermsUrl } from '../../src/links';
import strings from '../../src/strings';
import { useTheme } from '../../src/theme';
import { useProperty } from '../../src/property';
import { purchasesEnabled } from '@trelliskit/shared';

/**
 * Paywall. In-app purchase only: the price comes from the store, there is no other way to pay
 * shown here, and Restore purchases plus the auto-renewal terms are always visible. Only shown
 * where memberships are sold; an optional upgrade (PartiallyGated) offers Not now. Get help opens
 * support, which every signed-in account reaches in every access model. The property's Terms,
 * Privacy, and `ShownAtPurchase` documents are linked in the app (the website pages without
 * configured documents).
 */
export default function SubscribeScreen() {
  const theme = useTheme();
  const router = useRouter();
  const { api, session, user, signOut } = useAuth();
  const purchases = usePurchases();
  const t = strings.paywall;
  const property = useProperty();
  const paywall = usePaywall({ api, purchases });
  const { phase, product } = paywall;
  const busy = phase === 'purchasing' || phase === 'verifying';
  const price = product ? product.displayPrice : null;
  const legalDocuments = paywallDocuments(property);

  // A verified purchase (from this screen, Restore, or the app-wide listener) refreshes the
  // member; once the membership is active the paywall hands off to the next step.
  if (hasMemberAccess(user) || !purchasesEnabled(property)) return <Redirect href={nextRoute(session)} />;
  // Reached as an upgrade (PartiallyGated, a locked item): the member may go back without buying.
  const optional = !needsPaywall(user);

  let body;
  if (phase === 'loading') {
    body = <Loading label={t.loading} testID="paywall-loading" />;
  } else if (phase === 'unavailable') {
    body = (
      <Card testID="paywall-unavailable">
        <AppText variant="heading" accessibilityRole="header">
          {paywall.reason === 'productMissing' ? t.productMissing : paywall.reason === 'unavailable' ? t.unavailableTitle : t.loadFailedTitle}
        </AppText>
        {paywall.reason === 'productMissing' ? null : (
          <AppText color="textMuted" accessibilityRole={paywall.reason === 'unavailable' ? undefined : 'alert'}>
            {paywall.reason === 'unavailable' ? t.unavailableBody : paywall.message || t.unavailableBody}
          </AppText>
        )}
        {paywall.reason === 'unavailable' ? null : <Button label={strings.common.retry} accessibilityHint={t.retryHint} variant="secondary" onPress={paywall.reload} testID="paywall-retry" />}
      </Card>
    );
  } else {
    body = (
      <View style={{ gap: theme.spacing.md }}>
        <Card testID="paywall-product" accessible accessibilityLabel={(product.displayName || product.title || t.productFallbackName) + ', ' + t.pricePerYear(price)}>
          <AppText variant="heading">{product.displayName || product.title || t.productFallbackName}</AppText>
          <AppText variant="title" testID="paywall-price">
            {t.pricePerYear(price)}
          </AppText>
        </Card>
        <Button label={t.subscribe(price)} accessibilityHint={t.subscribeHint} onPress={paywall.buy} disabled={busy || phase === 'pending'} testID="paywall-subscribe" />
        {busy ? <Loading label={phase === 'verifying' ? t.verifying : t.purchasing} /> : null}
      </View>
    );
  }

  return (
    <Screen contentStyle={{ gap: theme.spacing.lg }} testID="screen-subscribe">
      <View style={{ gap: theme.spacing.sm }}>
        <AppText variant="title" accessibilityRole="header">
          {t.heading}
        </AppText>
        <AppText color="textMuted">{t.body}</AppText>
        <View accessible accessibilityLabel={t.benefitsLabel + ': ' + t.benefits.join(', ')}>
          {t.benefits.map((benefit) => (
            <AppText key={benefit}>{'\u2022 ' + benefit}</AppText>
          ))}
        </View>
      </View>

      {body}

      <Notice tone={paywall.tone} testID="paywall-message">
        {phase === 'unavailable' ? null : paywall.message}
      </Notice>

      {phase !== 'unavailable' && phase !== 'loading' ? <Button label={t.restore} accessibilityHint={t.restoreHint} variant="secondary" onPress={paywall.restore} disabled={busy} testID="paywall-restore" /> : null}

      <AppText variant="caption" color="textMuted" testID="paywall-terms">
        {Platform.OS === 'ios' ? t.termsIos : t.termsAndroid}
      </AppText>
      {legalDocuments.length ? (
        <DocumentLinks documents={legalDocuments} testID="paywall-document" />
      ) : (
        <View style={{ flexDirection: 'row', gap: theme.spacing.lg, flexWrap: 'wrap' }}>
          <LinkButton label={t.terms} role="link" onPress={() => openInAppBrowser(TermsUrl)} testID="paywall-terms-link" />
          <LinkButton label={t.privacy} role="link" onPress={() => openInAppBrowser(PrivacyUrl)} testID="paywall-privacy-link" />
        </View>
      )}

      <View style={{ gap: theme.spacing.sm }}>
        <Button label={t.account} accessibilityHint={t.accountHint} variant="ghost" onPress={() => router.push('/account')} />
        <Button label={t.help} accessibilityHint={t.helpHint} variant="ghost" onPress={() => router.push('/support')} testID="paywall-help" />
        {optional ? <Button label={strings.access.notNow} variant="secondary" onPress={() => (router.canGoBack() ? router.back() : router.replace(nextRoute(session)))} testID="paywall-not-now" /> : null}
        <Button label={t.signOut} variant="ghost" onPress={() => signOut()} testID="paywall-sign-out" />
      </View>
    </Screen>
  );
}

import { useCallback, useEffect, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import strings from '../../strings.js';
import { currentProperty, fill } from '../../property/current.js';
import { intervalWord, normalizeEnumerationResult } from '@trelliskit/shared';
import { useAuth } from '../../context/AuthContext.jsx';
import { useToast } from '../../context/ToastContext.jsx';
import useApiQuery from '../../hooks/useApiQuery.js';
import useDocumentMeta from '../../hooks/useDocumentMeta.js';
import { browser } from '../../utils/browser.js';
import { authErrorMessage } from '../../utils/authErrors.js';
import { formatDate, formatShortDate } from '../../utils/format.js';
import { clearPendingCode, getPendingCode } from '../../utils/pendingCode.js';
import { onboardingRedirect } from '../../utils/onboarding.js';
import RedeemCodeForm from '../../components/RedeemCodeForm.jsx';
import { PurchaseDocumentsNote } from '../../components/documents/DocumentChecks.jsx';
import Icon from '../../components/Icon.jsx';
import { Badge } from '../../components/Badges.jsx';
import { Banner, ErrorBanner, Loading } from '../../components/States.jsx';
import TableColumns from '../../components/TableColumns.jsx';

const m = strings.membershipApp;
const en = strings.enums;

/** Checkout return polling (contract section 5): every 2 seconds, up to 30 seconds. Tests may shorten. */
export const CHECKOUT_POLL = { intervalMs: 2000, timeoutMs: 30000 };

function MembershipStatus({ membership }) {
  const ms = membership || {};
  const active = Boolean(ms.isActive);
  return (
    <dl className="kv-grid">
      <div className="kv-item"><dt>{m.stateLabel}</dt><dd><Badge tone={active ? 'success' : 'neutral'}>{active ? m.active : m.inactive}</Badge></dd></div>
      {active && ms.source && <div className="kv-item"><dt>{m.source}</dt><dd>{en.membershipSource[ms.source] || ms.source}</dd></div>}
      {active && ms.store && <div className="kv-item"><dt>{m.store}</dt><dd>{en.store[ms.store] || ms.store}</dd></div>}
      {active && (
        <div className="kv-item">
          <dt>{ms.autoRenew ? m.renews : m.expires}</dt>
          <dd title={ms.expiresUtc || ''}>{ms.expiresUtc ? formatDate(ms.expiresUtc) : m.lifetime}</dd>
        </div>
      )}
      {active && ms.source === 'Subscription' && <div className="kv-item"><dt>{m.autoRenew}</dt><dd>{ms.autoRenew ? m.autoRenewOn : m.autoRenewOff}</dd></div>}
      {active && ms.willConvertToPaid && <div className="kv-item"><dt>{m.convertsToPaid}</dt><dd>{strings.common.yes}</dd></div>}
    </dl>
  );
}

function useCheckoutPolling(enabled, api, onActive) {
  const [state, setState] = useState(enabled ? 'polling' : null);
  useEffect(() => {
    if (!enabled) return undefined;
    let active = true;
    let timer = null;
    const started = Date.now();
    const tick = async () => {
      try {
        const data = await api.getMembership();
        if (!active) return;
        if (data && data.membership && data.membership.isActive) {
          setState('confirmed');
          onActive(data);
          return;
        }
      } catch {
        // Keep polling until the timeout; a later poll may succeed.
      }
      if (!active) return;
      if (Date.now() - started + CHECKOUT_POLL.intervalMs > CHECKOUT_POLL.timeoutMs) setState('timeout');
      else timer = setTimeout(tick, CHECKOUT_POLL.intervalMs);
    };
    tick();
    return () => {
      active = false;
      clearTimeout(timer);
    };
    // onActive is intentionally read once per polling session.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [enabled, api]);
  return state;
}

/**
 * /app/membership: status, Stripe Checkout and the billing portal, store-managed explanation,
 * history, and the "Redeem a code" form (website only).
 */
function MembershipView() {
  const auth = useAuth();
  const toast = useToast();
  const [params, setParams] = useSearchParams();
  const checkout = params.get('checkout');
  const [moreHistory, setMoreHistory] = useState(null);
  const [initialCode] = useState(() => params.get('code') || (getPendingCode() && getPendingCode().code) || '');
  const [override, setOverride] = useState(null);
  const [actionError, setActionError] = useState('');
  const [busy, setBusy] = useState('');
  const [stripeEnabled, setStripeEnabled] = useState(null);
  const [discount, setDiscount] = useState(null);
  useDocumentMeta(m.meta, strings.member.metaDescription);

  const fetcher = useCallback((signal) => auth.api.getMembership(signal), [auth.api]);
  const query = useApiQuery(fetcher);
  const data = override || query.data;
  const membership = (data && data.membership) || (auth.me && auth.me.membership) || null;

  const { getProviders, setMe } = auth;
  useEffect(() => {
    let active = true;
    getProviders().then(
      (p) => { if (active) setStripeEnabled(Boolean(p && p.stripe && p.stripe.enabled)); },
      () => { if (active) setStripeEnabled(false); }
    );
    return () => { active = false; };
  }, [getProviders]);

  const applyMembership = useCallback((value) => {
    if (value) setMe((me) => (me ? { ...me, membership: value } : me));
  }, [setMe]);

  const pollState = useCheckoutPolling(checkout === 'success', auth.api, (fresh) => {
    setOverride(fresh);
    applyMembership(fresh.membership);
  });

  const join = async () => {
    setBusy('checkout');
    setActionError('');
    try {
      const result = await auth.api.startCheckout(discount ? discount.code : undefined);
      browser.assign(result.url);
    } catch (err) {
      if (err && err.code === 'InvalidCode') {
        setDiscount(null);
        setActionError(m.invalidCode);
      } else if (err && (err.status === 429 || err.code === 'RateLimited')) {
        setActionError(m.rateLimited);
      } else if (err && err.code === 'AlreadyMember') {
        setActionError(m.alreadyMember);
        query.reload();
      } else {
        setActionError(err && err.status === 503 ? m.stripeDisabled : authErrorMessage(err));
      }
      setBusy('');
    }
  };

  const portal = async () => {
    setBusy('portal');
    setActionError('');
    try {
      const result = await auth.api.openBillingPortal();
      browser.assign(result.url);
    } catch (err) {
      setActionError(err && err.code === 'NoStripeCustomer' ? m.noStripeCustomer : authErrorMessage(err));
      setBusy('');
    }
  };

  const onRedeemed = (result) => {
    clearPendingCode();
    toast.success(m.redeemed(result.grantSummary || ''));
    applyMembership(result.membership);
    setOverride(null);
    query.reload();
    if (params.get('code')) {
      const nextParams = new URLSearchParams(params);
      nextParams.delete('code');
      setParams(nextParams, { replace: true });
    }
  };

  const dismissCheckout = () => {
    const nextParams = new URLSearchParams(params);
    nextParams.delete('checkout');
    setParams(nextParams, { replace: true });
  };

  const active = Boolean(membership && membership.isActive);
  const store = membership && membership.store;
  const source = membership && membership.source;
  const property = currentProperty();
  const fallbackPrice = property.membership.priceUsdCents / 100;
  const offer = (data && data.webOffer) || { priceUsd: fallbackPrice, interval: property.membership.interval, productName: property.membership.productName };
  const price = Number(offer.priceUsd || fallbackPrice).toFixed(2);
  const interval = intervalWord(offer.interval || property.membership.interval);
  const preview = data && data.history;
  // Bounded preview (docs/ENUMERATION_AND_METADATA.md 2.4): `{ objects, totalRecords, enumerate }`, or a plain list from older servers.
  const previewItems = Array.isArray(preview) ? preview : (preview && Array.isArray(preview.objects) ? preview.objects : []);
  const history = moreHistory ? moreHistory.items : previewItems;
  const historyHasMore = moreHistory ? Boolean(moreHistory.token) : Boolean(preview && preview.enumerate && typeof preview.totalRecords === 'number' && preview.totalRecords > previewItems.length);
  const loadHistory = async () => {
    const path = String(preview.enumerate || '').replace(/^\/api\/v1/, '');
    try {
      const result = normalizeEnumerationResult(await auth.client.post(path, { maxResults: 100, ...(moreHistory && moreHistory.token ? { continuationToken: moreHistory.token } : {}) }));
      setMoreHistory((prev) => ({ items: [...(prev ? prev.items : []), ...result.objects], token: result.endOfResults ? null : result.continuationToken }));
    } catch {
      setMoreHistory((prev) => prev || { items: previewItems, token: null });
    }
  };
  // A non-expiring free membership (a lifetime code or comp) has nothing to renew, so the join offer stays hidden.
  const lifetime = active && source !== 'Subscription' && !membership.expiresUtc;
  const canJoin = (!active || source !== 'Subscription') && !lifetime;

  const onDiscount = (grant) => {
    clearPendingCode();
    if (params.get('code')) {
      const nextParams = new URLSearchParams(params);
      nextParams.delete('code');
      setParams(nextParams, { replace: true });
    }
    if (!canJoin) {
      setActionError(m.discountAlreadyMember);
      return;
    }
    setActionError('');
    setDiscount(grant);
  };
  const appReady = auth.me && onboardingRedirect({ ...auth.me, membership }) === null;

  return (
    <div className="member-view">
      <header className="page-header">
        <div className="page-header-text">
          <h1>{m.title}</h1>
          <p className="page-summary">{m.summary}</p>
        </div>
      </header>

      {checkout === 'success' && pollState === 'polling' && <Banner tone="info" icon="info">{m.checkoutConfirming}</Banner>}
      {checkout === 'success' && pollState === 'confirmed' && (
        <Banner tone="success" icon="check" actions={<Link to="/app" className="button button-primary button-sm">{m.goToApp}</Link>}>{m.checkoutConfirmed}</Banner>
      )}
      {checkout === 'success' && pollState === 'timeout' && <Banner tone="warning" icon="alert">{m.checkoutTimeout}</Banner>}
      {checkout === 'cancelled' && (
        <Banner tone="info" icon="info" actions={<button type="button" className="icon-button" onClick={dismissCheckout} aria-label={strings.common.dismiss} title={strings.common.dismiss}><Icon name="close" size={14} /></button>}>{m.checkoutCancelled}</Banner>
      )}

      <section className="panel" aria-labelledby="membership-status-title">
        <div className="panel-header">
          <h2 id="membership-status-title">{m.statusTitle}</h2>
          {appReady && checkout !== 'success' && <Link to="/app" className="button button-secondary button-sm">{m.goToApp}</Link>}
        </div>
        {query.loading && !data ? <Loading inline /> : <MembershipStatus membership={membership} />}
        {query.error && !data && <ErrorBanner message={m.loadError + ' ' + authErrorMessage(query.error, '')} onRetry={query.reload} />}
        {!active && !query.loading && <p className="muted">{m.none}</p>}
        {active && store === 'Stripe' && (
          <div className="form-actions">
            <button type="button" className="button button-secondary" onClick={portal} disabled={Boolean(busy)}>{busy === 'portal' ? m.manageOpening : m.manageBilling}</button>
          </div>
        )}
        {active && (store === 'Apple' || store === 'Google') && (
          <Banner tone="info" icon="info">
            {m.storeManaged(en.store[store])} {store === 'Apple' ? m.storeAppleHelp : m.storeGoogleHelp}
          </Banner>
        )}
        {active && source === 'SignupCode' && <p className="muted small">{lifetime ? m.codeMembershipLifetimeNote : m.codeMembershipNote}</p>}
        {active && source === 'AdminComp' && <p className="muted small">{m.compNote}</p>}
        <ErrorBanner message={actionError} onDismiss={() => setActionError('')} />
      </section>

      <p className="muted small support-link"><Link to="/app/support">{strings.support.needHelp}</Link></p>

      {canJoin && (
        <section className="panel price-panel" aria-labelledby="membership-join-title">
          <h2 id="membership-join-title">{fill(m.joinTitle)}</h2>
          <p className="price"><span className="price-amount">{'$' + price}</span><span className="price-period">{fill(strings.marketing.priceLabel, { interval })}</span></p>
          <p className="muted">{m.joinBody}</p>
          <PurchaseDocumentsNote property={property} />
          {discount && (
            <Banner tone="success" icon="check" actions={<button type="button" className="button button-secondary button-sm" onClick={() => setDiscount(null)} disabled={Boolean(busy)}>{m.removeDiscount}</button>}>
              {m.discountApplied(discount.code, discount.discountSummary || discount.grantSummary || '')}
            </Banner>
          )}
          {stripeEnabled === false ? <Banner tone="info" icon="info">{m.stripeDisabled}</Banner> : (
            <div className="form-actions form-actions-start">
              <button type="button" className="button button-primary button-lg" onClick={join} disabled={Boolean(busy) || stripeEnabled === null}>
                {busy === 'checkout' ? m.joinRedirecting : (discount ? m.discountJoinButton : m.joinButton(price, interval))}
              </button>
            </div>
          )}
        </section>
      )}

      <section className="panel" aria-labelledby="membership-redeem-title">
        <h2 id="membership-redeem-title">{m.redeemTitle}</h2>
        <p className="muted">{m.redeemBody}</p>
        <RedeemCodeForm initialCode={initialCode} autoValidate={Boolean(initialCode)} onRedeemed={onRedeemed} onDiscount={onDiscount} onCleared={clearPendingCode} />
      </section>

      <section className="panel" aria-labelledby="membership-history-title">
        <h2 id="membership-history-title">{m.historyTitle}</h2>
        {history.length === 0 ? <p className="muted">{m.historyEmpty}</p> : (
          <div className="table-scroll">
            <TableColumns id="tk.member.MembershipView"><table className="data-table">
              <caption className="visually-hidden">{m.historyTitle}</caption>
              <thead>
                <tr>
                  <th scope="col">{m.historyColumns.source}</th>
                  <th scope="col">{m.historyColumns.store}</th>
                  <th scope="col">{m.historyColumns.starts}</th>
                  <th scope="col">{m.historyColumns.ends}</th>
                  <th scope="col">{m.historyColumns.status}</th>
                </tr>
              </thead>
              <tbody>
                {history.map((item, index) => (
                  <tr key={(item.startsUtc || '') + index}>
                    <td>{en.membershipSource[item.source] || item.source}</td>
                    <td>{item.store ? (en.store[item.store] || item.store) : '-'}</td>
                    <td className="nowrap" title={item.startsUtc}>{formatShortDate(item.startsUtc)}</td>
                    <td className="nowrap" title={item.endsUtc || ''}>{item.endsUtc ? formatShortDate(item.endsUtc) : m.lifetime}</td>
                    <td><Badge tone={item.status === 'Active' ? 'success' : 'neutral'}>{en.historyStatus[item.status] || item.status}</Badge></td>
                  </tr>
                ))}
              </tbody>
            </table></TableColumns>
          </div>
        )}
        {historyHasMore && <button type="button" className="button button-secondary button-sm" onClick={loadHistory}>{m.historyMore}</button>}
      </section>
    </div>
  );
}

export default MembershipView;

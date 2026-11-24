import { useEffect, useRef, useState } from 'react';
import { isSignupCode, normalizeSignupCode } from '@trelliskit/shared';
import strings from '../strings.js';
import { useAuth } from '../context/AuthContext.jsx';
import { formatDate } from '../utils/format.js';
import { Banner, ErrorBanner } from './States.jsx';

const m = strings.membershipApp;

/**
 * Message for a validate or redeem failure. Every invalid reason shows the same generic text
 * (anti-enumeration); rate limiting and duplicate redemption get their own messages.
 */
export function redeemErrorMessage(err) {
  if (!err) return m.invalidCode;
  if (err.status === 429 || err.code === 'RateLimited') return m.rateLimited;
  if (err.code === 'AlreadyRedeemed' || err.status === 409) return m.alreadyRedeemed;
  if (err.status === 0) return strings.auth.errors.unreachable;
  return m.invalidCode;
}

/**
 * Two-step "Redeem a code" form: check the code (shows what it grants), then confirm. A discount
 * code (validation `kind: 'Discount'`) is not redeemed here: confirming hands it to `onDiscount`
 * so the page can send it with checkout.
 * @param {{ initialCode?: string, autoValidate?: boolean, onRedeemed: (result: object) => void, onDiscount?: (grant: object) => void, onCleared?: () => void }} props
 */
function RedeemCodeForm({ initialCode = '', autoValidate = false, onRedeemed, onDiscount, onCleared }) {
  const { api } = useAuth();
  const [code, setCode] = useState(initialCode);
  const [grant, setGrant] = useState(null);
  const [error, setError] = useState('');
  const [busy, setBusy] = useState('');
  const autoRan = useRef(false);

  const validate = async (value) => {
    const normalized = normalizeSignupCode(value);
    setError('');
    setGrant(null);
    if (!isSignupCode(normalized)) {
      setError(m.invalidCode);
      return;
    }
    setBusy('checking');
    try {
      const result = await api.validateCode(normalized);
      if (result && result.valid) setGrant({ ...result, code: normalized });
      else setError(m.invalidCode);
    } catch (err) {
      setError(redeemErrorMessage(err));
    } finally {
      setBusy('');
    }
  };

  useEffect(() => {
    if (!autoValidate || !initialCode || autoRan.current) return;
    autoRan.current = true;
    validate(initialCode);
    // validate is stable enough for a one-time run on mount.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [autoValidate, initialCode]);

  const redeem = async () => {
    setBusy('redeeming');
    setError('');
    try {
      const result = await api.redeemCode(grant.code);
      setGrant(null);
      setCode('');
      onRedeemed(result, grant.code);
    } catch (err) {
      setError(redeemErrorMessage(err));
    } finally {
      setBusy('');
    }
  };

  const reset = () => {
    setGrant(null);
    setError('');
    setCode('');
    if (onCleared) onCleared();
  };

  return (
    <div className="redeem-form">
      {!grant && (
        <form className="inline-form" onSubmit={(e) => { e.preventDefault(); validate(code); }} noValidate>
          <div className="field field-grow">
            <label className="label-text" htmlFor="redeem-code">{m.codeLabel}</label>
            <input
              id="redeem-code"
              type="text"
              className="mono"
              autoComplete="off"
              spellCheck="false"
              maxLength={40}
              placeholder={m.codePlaceholder}
              value={code}
              onChange={(e) => setCode(e.target.value)}
              disabled={Boolean(busy)}
              aria-invalid={Boolean(error)}
            />
          </div>
          <button type="submit" className="button button-secondary" disabled={Boolean(busy) || !code.trim()}>{busy === 'checking' ? m.checking : m.check}</button>
        </form>
      )}
      <ErrorBanner message={error} />
      {grant && grant.kind === 'Discount' && (
        <div className="grant-card" aria-live="polite">
          <span className="label-text">{m.discountTitle}</span>
          <strong className="grant-summary">{grant.discountSummary || grant.grantSummary}</strong>
          {grant.name && <span>{grant.name} <code className="mono">{grant.code}</code></span>}
          {grant.description && <p className="muted">{grant.description}</p>}
          <p className="small">{m.discountNote}</p>
          <div className="form-actions">
            <button type="button" className="button button-secondary" onClick={reset} disabled={Boolean(busy)}>{m.cancelRedeem}</button>
            <button type="button" className="button button-primary" onClick={() => { const chosen = grant; setGrant(null); setCode(''); if (onDiscount) onDiscount(chosen); }} disabled={Boolean(busy) || !onDiscount}>{m.useDiscount}</button>
          </div>
        </div>
      )}
      {grant && grant.kind !== 'Discount' && (
        <div className="grant-card" aria-live="polite">
          <span className="label-text">{m.grantTitle}</span>
          <strong className="grant-summary">{grant.grantSummary}</strong>
          {grant.name && <span>{grant.name} <code className="mono">{grant.code}</code></span>}
          {grant.description && <p className="muted">{grant.description}</p>}
          <p className="small">{grant.membershipEndsUtc ? m.grantEnds(formatDate(grant.membershipEndsUtc)) : m.grantLifetime}</p>
          {grant.note && <Banner tone="info" icon="info">{grant.note}</Banner>}
          <div className="form-actions">
            <button type="button" className="button button-secondary" onClick={reset} disabled={Boolean(busy)}>{m.cancelRedeem}</button>
            <button type="button" className="button button-primary" onClick={redeem} disabled={Boolean(busy)}>{busy === 'redeeming' ? m.redeeming : m.confirmRedeem}</button>
          </div>
        </div>
      )}
    </div>
  );
}

export default RedeemCodeForm;

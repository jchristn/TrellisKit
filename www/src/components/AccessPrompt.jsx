import { createContext, useCallback, useContext, useMemo, useState } from 'react';
import { Link, useLocation } from 'react-router-dom';
import { AccessRequirement, accessAction, accessDenial } from '@trelliskit/shared';
import strings from '../strings.js';
import { useProperty } from '../context/PropertyContext.jsx';
import { MEMBERSHIP_PATH } from '../utils/onboarding.js';
import Icon from './Icon.jsx';
import Modal from './Modal.jsx';

const s = strings.access;

/** Display name of an access feature key (for example "Forums"), or null. */
export function featureName(feature) {
  return (feature && s.features[feature]) || null;
}

function signInPath(location) {
  return '/sign-in?next=' + encodeURIComponent(location.pathname + location.search);
}

/**
 * The call to action for an access requirement (contract sections 3.4 and 8.5 to 8.8):
 * `SignIn` offers sign in and sign up, `Membership` offers the membership page when purchases
 * are on, and says so plainly when they are off (nothing is for sale in `Open`).
 *
 * @param {{ required: string, feature?: string|null, onNavigate?: () => void, onDismiss?: () => void }} props
 */
export function AccessActions({ required, feature = null, onNavigate, onDismiss }) {
  const { property } = useProperty();
  const location = useLocation();
  const action = accessAction(property, required);
  return (
    <div className="access-actions">
      {action === 'signIn' && (
        <>
          <Link to={signInPath(location)} className="button button-primary button-sm" onClick={onNavigate}>{s.signInCta}</Link>
          <Link to="/sign-up" className="button button-secondary button-sm" onClick={onNavigate}>{s.signUpCta}</Link>
        </>
      )}
      {action === 'upgrade' && (
        <Link to={MEMBERSHIP_PATH + (feature ? '?feature=' + encodeURIComponent(feature) : '')} className="button button-primary button-sm" onClick={onNavigate}>{s.upgradeCta}</Link>
      )}
      {onDismiss && <button type="button" className="button button-secondary button-sm" onClick={onDismiss}>{s.notNow}</button>}
    </div>
  );
}

function promptText(property, fill, required, feature) {
  const name = featureName(feature);
  const action = accessAction(property, required);
  if (action === 'signIn') return { title: s.signInTitle, body: s.signInBody(name) };
  if (action === 'upgrade') return { title: fill(s.upgradeTitle), body: fill(s.upgradeBody(name)) };
  return { title: fill(s.noPurchaseTitle), body: fill(s.noPurchaseBody(name)) };
}

/**
 * Inline prompt for a locked page, a locked composer, or a denied load: a lock icon, a title,
 * one sentence, and the actions.
 *
 * @param {{ required: string, feature?: string|null, compact?: boolean, title?: string, body?: string }} props
 */
export function AccessPromptCard({ required, feature = null, compact = false, title, body }) {
  const { property, fill } = useProperty();
  const text = promptText(property, fill, required, feature);
  return (
    <section className={'access-prompt' + (compact ? ' access-prompt-compact' : '')} aria-label={title || text.title} data-required={required}>
      <Icon name={required === AccessRequirement.SignIn ? 'user' : 'lock'} size={compact ? 18 : 28} />
      <div className="access-prompt-text">
        <h2 className={compact ? 'access-prompt-title-sm' : 'access-prompt-title'}>{title || text.title}</h2>
        <p className="muted">{body || text.body}</p>
      </div>
      <AccessActions required={required} feature={feature} />
    </section>
  );
}

/** A small "Members only" lock badge. */
export function LockBadge({ feature = null }) {
  const name = featureName(feature);
  return (
    <span className="lock-badge" title={s.lockBadge} aria-label={name ? s.lockBadgeLabel(name) : s.lockBadge}>
      <Icon name="lock" size={12} />
      <span className="lock-badge-text">{s.lockBadge}</span>
    </span>
  );
}

const AccessPromptContext = createContext(null);

/**
 * The one access-denial handler for the website. `handle(error)` turns a `402` (or a `401` with
 * `required: "SignIn"`) into the upgrade or sign-in dialog and returns true; any other error
 * returns false so the caller shows its own message. `show(denial)` opens the dialog directly
 * (for example when a member taps a locked card).
 */
export function AccessPromptProvider({ children }) {
  const [denial, setDenial] = useState(null);
  const { property, fill } = useProperty();
  const show = useCallback((next) => setDenial(next ? { required: next.required || AccessRequirement.Membership, feature: next.feature || null } : null), []);
  const handle = useCallback((error) => {
    const found = accessDenial(error);
    if (!found) return false;
    setDenial(found);
    return true;
  }, []);
  const value = useMemo(() => ({ show, handle }), [show, handle]);
  const close = () => setDenial(null);
  const text = denial ? promptText(property, fill, denial.required, denial.feature) : null;
  return (
    <AccessPromptContext.Provider value={value}>
      {children}
      {denial && (
        <Modal open onClose={close} title={text.title} size="sm">
          <div className="access-dialog" data-required={denial.required}>
            <p>{text.body}</p>
            <AccessActions required={denial.required} feature={denial.feature} onNavigate={close} onDismiss={close} />
          </div>
        </Modal>
      )}
    </AccessPromptContext.Provider>
  );
}

const FALLBACK = Object.freeze({ show: () => {}, handle: () => false });

/** `{ show, handle }` from the nearest AccessPromptProvider (outside one nothing is handled, so callers show their own message). */
export function useAccessPrompt() {
  return useContext(AccessPromptContext) || FALLBACK;
}

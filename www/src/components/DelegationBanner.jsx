import { useEffect, useState } from 'react';
import { delegationTimeLeft } from '@trelliskit/shared';
import strings from '../strings.js';
import { useAdminAuth } from '../context/AdminAuthContext.jsx';
import { browser } from '../utils/browser.js';
import Icon from './Icon.jsx';

const d = strings.admin.delegation;
const TICK_MS = 1000;

/** Where "End delegation" returns: the property's page in the platform console. */
export function platformReturnUrl(propertyId) {
  return '/platform/properties/' + encodeURIComponent(propertyId || '');
}

/**
 * Persistent banner for a delegated session (a platform administrator acting in this property):
 * names the property, counts down to the token expiry, and ends the delegation (returning to the
 * platform console). The session is not refreshable, so it ends itself at expiry.
 */
function DelegationBanner() {
  const { delegation, endDelegation } = useAdminAuth();
  const [now, setNow] = useState(() => Date.now());
  const left = delegationTimeLeft(delegation, now);

  useEffect(() => {
    if (!delegation) return undefined;
    const timer = setInterval(() => setNow(Date.now()), TICK_MS);
    return () => clearInterval(timer);
  }, [delegation]);

  useEffect(() => {
    if (delegation && !left.active) endDelegation({ expired: true });
  }, [delegation, left.active, endDelegation]);

  if (!delegation) return null;

  const end = () => {
    const target = platformReturnUrl(delegation.propertyId);
    endDelegation();
    browser.assign(target);
  };

  return (
    <div className="banner banner-warning delegation-banner" role="region" aria-label={d.role}>
      <Icon name="shield" />
      <span className="banner-text">
        {d.banner(delegation.propertyName || delegation.propertyId)}{' '}
        <strong className="mono" title={d.timeLeftTitle} aria-live="off">{d.timeLeft(left.minutes, left.seconds)}</strong>
      </span>
      <button type="button" className="button button-secondary button-sm" onClick={end}>{d.end}</button>
    </div>
  );
}

export default DelegationBanner;

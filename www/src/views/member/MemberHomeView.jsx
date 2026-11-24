import { Link, Navigate } from 'react-router-dom';
import strings from '../../strings.js';
import { useAuth } from '../../context/AuthContext.jsx';
import useDocumentMeta from '../../hooks/useDocumentMeta.js';
import Icon from '../../components/Icon.jsx';
import { Badge } from '../../components/Badges.jsx';
import { formatDate } from '../../utils/format.js';
import { featureAccess } from '@trelliskit/shared';
import { hasActiveMembership } from '../../utils/onboarding.js';
import { featureEnabled, memberRoutesFor, routeAvailable } from './memberRoutes.js';
import { useProperty } from '../../context/PropertyContext.jsx';
import { purchasesEnabled, showsLockBadge } from '@trelliskit/shared';
import { LockBadge } from '../../components/AccessPrompt.jsx';

const m = strings.member;

/** /app landing for a signed-in member (with a membership where the access model requires one); gated sections carry a lock badge. */
function MemberHomeView() {
  const { me } = useAuth();
  const { property } = useProperty();
  useDocumentMeta(m.metaTitle(m.title), m.metaDescription);
  const ms = (me && me.membership) || {};
  return (
    <div className="member-view">
      <section className="member-hero">
        <h1>{m.welcomeTitle(me.displayName || me.email)}</h1>
        <p className="muted">{m.welcomeBody}</p>
        {purchasesEnabled(property) && <Link to="/app/membership" className="membership-chip">
          <Badge tone={ms.isActive ? 'success' : 'neutral'}>{ms.isActive ? m.membershipActive : m.membershipInactive}</Badge>
          {ms.isActive && <span className="muted small">{ms.expiresUtc ? formatDate(ms.expiresUtc) : strings.membershipApp.lifetime}</span>}
        </Link>}
      </section>
      <ul className="member-grid">
        {memberRoutesFor(property).map((route) => (
          <li key={route.id}>
            <Link to={'/app/' + route.id} className="member-tile">
              <Icon name={route.icon} size={20} />
              <span className="member-tile-name">{route.label}</span>
              {!routeAvailable(property, route) && <span className="badge badge-info">{m.comingSoon}</span>}
              {route.access && showsLockBadge(property, me, route.access) && <LockBadge feature={route.access} />}
            </Link>
          </li>
        ))}
      </ul>
    </div>
  );
}

export default MemberHomeView;

/**
 * /app for a member: the feed is the landing page for an account with an active membership whenever
 * the property has the feed, so signing in lands there. A property without the feed keeps the section overview (MemberHomeView).
 */
export function MemberLanding() {
  const { property } = useProperty();
  const { me } = useAuth();
  // Accounts allowed to read the feed land there; gated accounts keep the membership overview.
  if (featureEnabled(property, 'feed') && (hasActiveMembership(me) || featureAccess(property, me, 'feed').allowed)) return <Navigate to="/app/feed" replace />;
  return <MemberHomeView />;
}

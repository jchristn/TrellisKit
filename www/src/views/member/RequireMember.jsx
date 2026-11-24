import { Navigate, useLocation } from 'react-router-dom';
import { accessOf, catalogAccess, featureAccess, signedOutBrowsable } from '@trelliskit/shared';
import strings from '../../strings.js';
import { useAuth } from '../../context/AuthContext.jsx';
import { useProperty, useRuntimeProperty } from '../../context/PropertyContext.jsx';
import { LEVELS, onboardingRedirect } from '../../utils/onboarding.js';
import { EmptyState, Loading } from '../../components/States.jsx';
import { useAppContent, useKillSwitch } from '../../context/AppContentContext.jsx';
import { AccessPromptCard } from '../../components/AccessPrompt.jsx';
import useDocumentMeta from '../../hooks/useDocumentMeta.js';

function LockedPage({ required, feature }) {
  useDocumentMeta(strings.access.pageTitle);
  return (
    <div className="member-view">
      <AccessPromptCard required={required} feature={feature} />
    </div>
  );
}

/**
 * Guard for /app routes. Signed-out visitors go to /sign-in (with `next`), and signed-in
 * members are routed through onboarding (age, profile, and the membership step when
 * `Me.membershipRequired`) until they reach `level`.
 *
 * - `browse` (an access feature key) lets signed-out visitors in when the property allows
 *   anonymous browsing of that feature (ACCESS_MODELS_CONTRACT.md section 8.8); `any` (the
 *   member shell) lets them in whenever anonymous browsing is on.
 * - `feature` (an access feature key) shows the sign-in or upgrade prompt instead of the page
 *   when the caller may not read that feature (a gated feature in PartiallyGated).
 * - `catalog` (`library`, `events`, `directory`) applies the Phase 4 catalog rule (`catalogAccess`):
 *   signed-out visitors browse `Public` objects under anonymous browsing, free accounts browse with
 *   locked stubs, and `FullyGated` needs a membership.
 * - `guest` is rendered instead of `children` for signed-out visitors whenever anonymous browsing
 *   is on (the /app guest home); without anonymous browsing they go to sign in as usual.
 *
 * @param {{ level?: number, browse?: string, feature?: string, catalog?: string, guest?: React.ReactNode, children: React.ReactNode }} props
 */
function FeatureOffPage({ message }) {
  useDocumentMeta(strings.member.featureOff.title);
  const { refresh } = useAppContent();
  // The switch may already be back on: a forced manifest check clears it without waiting for the interval.
  const action = <button type="button" className="button button-secondary" onClick={() => refresh({ force: true })}>{strings.member.featureOff.retry}</button>;
  return (
    <div className="member-view">
      <EmptyState icon="info" title={strings.member.featureOff.title} action={action}>{message || strings.member.featureOff.body}</EmptyState>
    </div>
  );
}

// Access feature keys whose kill switch has the same engine feature key (the member directory route's
// `feature: directory` is the people directory, so only `catalog` names the venue directory).
const KILLABLE_FEATURES = Object.freeze(['feed', 'forums', 'search', 'messaging', 'assistants']);

/** The engine kill switch key that guards a route, from its guard props. */
export function killSwitchKeyFor({ browse = null, feature = null, catalog = null }) {
  if (catalog) return catalog;
  if (feature && KILLABLE_FEATURES.includes(feature)) return feature;
  if (browse && browse !== 'any' && KILLABLE_FEATURES.includes(browse)) return browse;
  return null;
}

function RequireMember({ level = LEVELS.member, browse = null, feature = null, catalog = null, guest = null, children }) {
  const { status, me, client } = useAuth();
  const { property, runtimeSettled, canLoadRuntime: hasProvider } = useProperty();
  const location = useLocation();
  const here = location.pathname + location.search;
  // A killed feature shows the operator's message in place of the screen (LIVE_UPDATES_CONTRACT.md section 13.2).
  const kill = useKillSwitch(killSwitchKeyFor({ browse, feature, catalog }));
  // Signed-out decisions (anonymous browsing) must use the server's current access policy, not the
  // build-time copy: an admin can switch the model at any time.
  useRuntimeProperty(status === 'signedIn' ? null : client);
  const canLoadRuntime = Boolean(hasProvider) && Boolean(client) && typeof client.property === 'function';

  if (status === 'restoring') return <div className="center-page"><Loading label={strings.member.restoring} /></div>;
  if (status !== 'signedIn' || !me) {
    if (canLoadRuntime && !runtimeSettled) return <div className="center-page"><Loading label={strings.member.restoring} /></div>;
    if (guest && accessOf(property).anonymousBrowsing) return guest;
    if (kill.killed && (catalog || browse)) return <FeatureOffPage message={kill.message} />;
    // `any` is the member shell: guests pass when anonymous browsing is on, and each section's own guard decides.
    if (catalog && catalogAccess(property, null, catalog, 'read').allowed) return children;
    if (browse === 'any' ? accessOf(property).anonymousBrowsing : browse && signedOutBrowsable(property, browse)) return children;
    return <Navigate to={'/sign-in?next=' + encodeURIComponent(here)} replace />;
  }

  const redirect = onboardingRedirect(me, level);
  if (redirect && redirect !== location.pathname) return <Navigate to={redirect} replace />;
  if (kill.killed) return <FeatureOffPage message={kill.message} />;
  if (catalog) {
    const access = catalogAccess(property, me, catalog, 'read');
    if (!access.allowed) return <LockedPage required={access.requires} feature={catalog} />;
  }
  if (feature) {
    const access = featureAccess(property, me, feature, 'read');
    if (!access.allowed) return <LockedPage required={access.requires} feature={feature} />;
  }
  return children;
}

export default RequireMember;

import { Navigate, useLocation } from 'react-router-dom';
import strings from '../../strings.js';
import { useAdminAuth } from '../../context/AdminAuthContext.jsx';
import { Loading } from '../../components/States.jsx';
import { isForumModerator } from '../../utils/engagement.js';
import { hasPendingDocuments } from '@trelliskit/shared';
import { useAuth } from '../../context/AuthContext.jsx';
import DocumentsReviewPanel from '../../components/documents/DocumentsReviewPanel.jsx';

/**
 * Guard for admin routes: redirects to /admin/login without a valid session or token. With
 * `adminOnly`, moderators (who see only Home and Moderation) are sent to the moderation queue;
 * with `forumModerator`, only forum moderators (and administrators) stay (PHASE3_CONTRACT.md 11.5).
 * With `area` (`library`, `events`, `directory`, `ranks`, or a list of them), administrators and holders of that area's staff
 * role stay (docs/PHASE4_CONTRACT.md section 1.1); others go to Home. With `supportStaff` or
 * `communicator`, only SupportAgent or Communicator holders (and administrators) stay
 * (PHASE5_CONTRACT.md as-built note D). With `releaseManager`, only ReleaseManager holders (and
 * administrators) stay (docs/LIVE_UPDATES_CONTRACT.md as-built notes).
 *
 * A signed-in member (session mode) with documents to accept (`Me.pendingDocuments`) sees the
 * "Review and accept" step instead of the console until they accept: the server gates staff
 * routes for them. The break-glass token and a delegated platform session are never gated.
 */
function RequireAdmin({ children, adminOnly = false, forumModerator = false, area = null, supportStaff = false, communicator = false, releaseManager = false }) {
  const { isAuthenticated, isLoading, isModeratorOnly, me, mode, staffAreas = [], staff } = useAdminAuth();
  const auth = useAuth();
  const location = useLocation();
  if (isLoading) return <div className="center-page"><Loading label={strings.admin.login.restoring} /></div>;
  if (!isAuthenticated) return <Navigate to="/admin/login" replace state={{ from: location.pathname + location.search }} />;
  if (mode === 'session' && hasPendingDocuments(me)) {
    return (
      <div className="center-page">
        <div className="documents-review-admin">
          <DocumentsReviewPanel me={me} client={auth.client} onAccepted={(next) => auth.setMe(next)} onReloadMe={auth.reloadMe} onSignOut={auth.signOut} />
        </div>
      </div>
    );
  }
  if (area && isModeratorOnly && ![].concat(area).some((one) => staffAreas.includes(one))) return <Navigate to="/admin/home" replace />;
  // Phase 5 staff sections: SupportAgent (support inbox, ideas) and Communicator (broadcasts).
  if (supportStaff && staff && !staff.supportStaff) return <Navigate to="/admin/home" replace />;
  if (communicator && staff && !staff.communicator) return <Navigate to="/admin/home" replace />;
  // Releases sections (updates, channels, flags, kill switches, strings, banners, assets): ReleaseManager holders and administrators.
  if (releaseManager && staff && !staff.releaseManager) return <Navigate to="/admin/home" replace />;
  if (supportStaff || communicator || releaseManager) return children;
  if (adminOnly && isModeratorOnly) return <Navigate to={staff && !staff.moderator ? '/admin/home' : '/admin/moderation'} replace />;
  if (forumModerator && isModeratorOnly && !isForumModerator(me)) return <Navigate to="/admin/moderation" replace />;
  return children;
}

export default RequireAdmin;

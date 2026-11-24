import { useState } from 'react';
import { Navigate, useNavigate, useSearchParams } from 'react-router-dom';
import { hasPendingDocuments } from '@trelliskit/shared';
import strings from '../../strings.js';
import { useAuth } from '../../context/AuthContext.jsx';
import useDocumentMeta from '../../hooks/useDocumentMeta.js';
import { onboardingRedirect, safeNext } from '../../utils/onboarding.js';
import { routeAfterAuth } from '../../utils/postAuth.js';
import DocumentsReviewPanel from '../../components/documents/DocumentsReviewPanel.jsx';

const r = strings.documents.review;

/**
 * /app/onboarding/documents: the blocking "Review and accept" step for `Me.pendingDocuments`
 * (documents never accepted, or a new version that requires re-acceptance). Reached from the
 * member app guard, after sign-in, and after any `403 DocumentAcceptanceRequired`. Accepting
 * refreshes `Me` and continues to `next` through the rest of onboarding.
 */
function DocumentsReviewView() {
  const auth = useAuth();
  const navigate = useNavigate();
  const [params] = useSearchParams();
  const next = safeNext(params.get('next'));
  const [done, setDone] = useState(false);
  useDocumentMeta(r.meta, strings.member.metaDescription);

  if (auth.me && !hasPendingDocuments(auth.me) && !done) return <Navigate to={onboardingRedirect(auth.me) || next} replace />;

  const onAccepted = async (me) => {
    setDone(true);
    auth.setMe(me);
    const path = await routeAfterAuth({ me, api: auth.api, setMe: auth.setMe, next });
    navigate(path, { replace: true });
  };

  const signOut = async () => {
    await auth.signOut();
    navigate('/sign-in', { replace: true });
  };

  return (
    <div className="member-view onboarding">
      <DocumentsReviewPanel me={auth.me} client={auth.client} onAccepted={onAccepted} onReloadMe={auth.reloadMe} onSignOut={signOut} />
    </div>
  );
}

export default DocumentsReviewView;

import { useEffect } from 'react';
import { Link, useLocation } from 'react-router-dom';
import { useAuth } from '../../context/AuthContext.jsx';
import strings from '../../strings.js';
import useDocumentMeta from '../../hooks/useDocumentMeta.js';
import { Banner } from '../../components/States.jsx';
import AuthCard from './AuthCard.jsx';

const a = strings.auth;

/**
 * Shown after account deletion (or an under-18 age attestation) with the server message and any
 * store subscription note. The local session ends here, after navigation has committed, so the
 * member app guard does not redirect to sign-in first.
 */
function AccountDeletedPage() {
  const location = useLocation();
  const { endSession } = useAuth();
  const state = location.state || {};
  const shouldEnd = Boolean(state.endSession);
  useEffect(() => {
    if (shouldEnd) endSession();
  }, [shouldEnd, endSession]);
  useDocumentMeta(a.accountDeletedMeta, a.metaDescription);
  return (
    <AuthCard title={state.title || a.accountDeletedTitle}>
      <p>{state.message || a.accountDeletedBody}</p>
      {state.storeSubscriptionNote && <Banner tone="warning" icon="alert">{state.storeSubscriptionNote}</Banner>}
      <Link to="/" className="button button-secondary button-block">{strings.common.backHome}</Link>
    </AuthCard>
  );
}

export default AccountDeletedPage;

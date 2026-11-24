import { useEffect, useRef, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import strings from '../../strings.js';
import { useAuth } from '../../context/AuthContext.jsx';
import useDocumentMeta from '../../hooks/useDocumentMeta.js';
import { authErrorMessage } from '../../utils/authErrors.js';
import { Banner, Loading } from '../../components/States.jsx';
import AuthCard from './AuthCard.jsx';

const a = strings.auth;

/**
 * /verify-email?token=: verifies once on load. The token is single use, so a ref guards against
 * the double effect in development StrictMode.
 */
function VerifyEmailPage() {
  const { api, isSignedIn, me, reloadMe } = useAuth();
  const [params] = useSearchParams();
  const token = params.get('token') || '';
  const started = useRef(false);
  const [state, setState] = useState(token ? 'verifying' : 'missing');
  const [message, setMessage] = useState('');
  useDocumentMeta(a.verifyMeta, a.metaDescription);

  useEffect(() => {
    if (!token || started.current) return;
    started.current = true;
    api.verifyEmail(token).then(
      () => setState('done'),
      (err) => {
        setState('failed');
        setMessage(err && err.code === 'InvalidToken' ? a.verifyInvalid : authErrorMessage(err, a.verifyInvalid));
      }
    );
  }, [api, token]);

  useEffect(() => {
    // After success, and after a failure (the link may already have been used), refresh the
    // signed-in account so the page can tell an already-verified address from a bad link.
    if ((state === 'done' || state === 'failed') && isSignedIn) reloadMe().catch(() => {});
  }, [state, isSignedIn, reloadMe]);

  const alreadyVerified = state === 'failed' && isSignedIn && Boolean(me && me.emailVerified);

  return (
    <AuthCard title={a.verifyTitle}>
      {state === 'verifying' && <Loading label={a.verifying} />}
      {state === 'missing' && <Banner tone="danger" icon="alert">{a.verifyMissingToken}</Banner>}
      {state === 'failed' && !alreadyVerified && <Banner tone="danger" icon="alert">{message}</Banner>}
      {alreadyVerified && <Banner tone="success" icon="check">{a.verifyAlready}</Banner>}
      {state === 'done' && <Banner tone="success" icon="check">{a.verifyDone}</Banner>}
      {state !== 'verifying' && (
        <Link to={isSignedIn ? '/app' : '/sign-in'} className="button button-primary button-block">{isSignedIn ? a.continueToApp : a.signInLink}</Link>
      )}
    </AuthCard>
  );
}

export default VerifyEmailPage;

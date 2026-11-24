import { useState } from 'react';
import { Navigate, useSearchParams } from 'react-router-dom';
import { normalizeSignupCode } from '@trelliskit/shared';
import strings from '../../strings.js';
import { useAuth } from '../../context/AuthContext.jsx';
import { getPendingCode, setPendingCode } from '../../utils/pendingCode.js';
import { Loading } from '../../components/States.jsx';

/**
 * /join?code=X (contract section 6). The code is saved so it survives sign-up, social sign-in,
 * age attestation, and email verification. Signed-out visitors continue to sign-up with the code
 * prefilled; signed-in members go straight to the redeem confirmation.
 */
function JoinPage() {
  const { isRestoring, isSignedIn } = useAuth();
  const [params] = useSearchParams();
  const [pending] = useState(() => {
    const code = normalizeSignupCode(params.get('code') || '');
    return code ? setPendingCode(code) : getPendingCode();
  });

  if (isRestoring) return <div className="center-page"><Loading label={strings.member.restoring} /></div>;
  if (isSignedIn) return <Navigate to={pending ? '/app/membership?code=' + encodeURIComponent(pending.code) : '/app/membership'} replace />;
  return <Navigate to="/sign-up" replace />;
}

export default JoinPage;

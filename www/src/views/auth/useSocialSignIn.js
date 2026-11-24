import { useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import { useAuth } from '../../context/AuthContext.jsx';
import { authErrorMessage } from '../../utils/authErrors.js';
import { routeAfterAuth } from '../../utils/postAuth.js';

/**
 * Shared handler for social credentials on the sign-in and sign-up pages: exchanges the
 * provider credential for a session, then routes through onboarding and any pending code.
 */
export function useSocialSignIn({ next, setBusy, setError }) {
  const auth = useAuth();
  const navigate = useNavigate();
  return useCallback(async (provider, body) => {
    setBusy(true);
    setError('');
    try {
      const response = await auth.socialSignIn(provider, body);
      const path = await routeAfterAuth({ me: response.user, isNewAccount: response.isNewAccount, api: auth.api, setMe: auth.setMe, next });
      navigate(path, { replace: true });
    } catch (err) {
      setError(authErrorMessage(err));
      setBusy(false);
    }
  }, [auth, navigate, next, setBusy, setError]);
}

export default useSocialSignIn;

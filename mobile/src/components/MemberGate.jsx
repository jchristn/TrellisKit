import { Redirect } from 'expo-router';
import { useAuth } from '../auth/AuthProvider';
import { canEnterTabs, nextRoute } from '../auth/routing';
import strings from '../strings';
import Loading from './Loading';

/**
 * Gate for member screens that live outside the tab layout (Messages, Notifications). Signed-out
 * members, including a session revoked while the screen is open, go to the step they need.
 */
export default function MemberGate({ children }) {
  const { session } = useAuth();
  if (session.status === 'loading') return <Loading label={strings.launch.restoring} />;
  if (!canEnterTabs(session)) return <Redirect href={nextRoute(session)} />;
  return children;
}

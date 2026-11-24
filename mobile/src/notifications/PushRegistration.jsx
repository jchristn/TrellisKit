import { useEffect } from 'react';
import { useAuth } from '../auth/AuthProvider';
import { getNotifications } from '../native/notifications';
import { syncPushRegistration } from './push';

/**
 * Keeps this device's push token registered for the signed-in member: once per launch and
 * member, and again when the platform rotates the device token. Renders nothing and never asks
 * for permission (that happens on the onboarding notification step).
 */
export default function PushRegistration() {
  const { api, session } = useAuth();
  const userId = session.status === 'signedIn' && session.user ? session.user.id : null;

  useEffect(() => {
    if (!userId) return undefined;
    const Notifications = getNotifications();
    if (!Notifications) return undefined;
    syncPushRegistration(api, { userId, Notifications }).catch(() => {});
    let subscription = null;
    try {
      if (typeof Notifications.addPushTokenListener === 'function') {
        subscription = Notifications.addPushTokenListener(() => {
          syncPushRegistration(api, { userId, Notifications }).catch(() => {});
        });
      }
    } catch {
      subscription = null;
    }
    return () => {
      if (subscription && subscription.remove) subscription.remove();
    };
  }, [api, userId]);

  return null;
}

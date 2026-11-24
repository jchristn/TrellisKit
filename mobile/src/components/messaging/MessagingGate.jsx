import { canUseMessaging } from '@trelliskit/shared';
import { useAuth } from '../../auth/AuthProvider';
import { useProperty } from '../../property';
import strings from '../../strings';
import { AccessPromptCard } from '../AccessPrompt';
import ErrorState from '../ErrorState';
import Screen from '../Screen';

/**
 * Shows the messaging screens only when direct messages can be read (contract section 0.3 and
 * resolved decision 26): `features.messaging` off explains that messages are not available; a
 * gated model shows the upgrade prompt to non-members (a lapsed member loses access to their
 * history). No messaging request is made while locked.
 */
export default function MessagingGate({ children }) {
  const { user } = useAuth();
  const property = useProperty();
  const access = canUseMessaging(property, user, 'read');
  if (access.allowed) return children;
  if (access.reason === 'FeatureOff') {
    return (
      <Screen testID="messaging-off">
        <ErrorState title={strings.messaging.featureOffTitle} message={strings.messaging.featureOffBody} />
      </Screen>
    );
  }
  return (
    <Screen testID="messaging-locked">
      <AccessPromptCard required={access.requires} feature="messaging" testID="messaging-locked-prompt" />
    </Screen>
  );
}

import { canUseIdeas } from '@trelliskit/shared';
import { useAuth } from '../../auth/AuthProvider';
import { useProperty } from '../../property';
import strings from '../../strings';
import { AccessPromptCard } from '../AccessPrompt';
import ErrorState from '../ErrorState';
import Screen from '../Screen';

/**
 * Shows the ideas board only when it can be read (contract section 0.3): `features.ideas` off
 * explains that it is not available; where `ideas` is gated a non-member sees the upgrade prompt.
 */
export default function IdeasGate({ children }) {
  const { user } = useAuth();
  const property = useProperty();
  const access = canUseIdeas(property, user, 'read');
  if (access.allowed) return children;
  if (access.reason === 'FeatureOff') {
    return (
      <Screen testID="ideas-off">
        <ErrorState title={strings.ideas.featureOffTitle} message={strings.ideas.featureOffBody} />
      </Screen>
    );
  }
  return (
    <Screen testID="ideas-locked">
      <AccessPromptCard required={access.requires} feature="ideas" testID="ideas-locked-prompt" />
    </Screen>
  );
}

import { AccessRequirement, featureAccess } from '@trelliskit/shared';
import { useKillSwitch } from '../appConfig/AppContentProvider';
import { killSwitchKeyFor } from '../appConfig/killSwitches';
import { useAuth } from '../auth/AuthProvider';
import { useProperty } from '../property';
import { AccessPromptCard } from './AccessPrompt';
import FeatureOff from './FeatureOff';
import Screen from './Screen';

/**
 * Shows a feature's screen only when the caller may read it under the access model
 * (ACCESS_MODELS_CONTRACT.md sections 3.2 and 8.7): a gated feature in PartiallyGated shows the
 * upgrade prompt to non-members, and signed-out guests see the sign-in prompt unless the feature is
 * open to anonymous browsing (and `allowGuest` is not false). A feature whose kill switch is on for the caller
 * shows the operator's message instead (LIVE_UPDATES_CONTRACT.md section 13.2), like the website's route guard.
 * No API call is made while locked or killed.
 * @param {{ feature: string, allowGuest?: boolean, children: React.ReactNode, testID?: string }} props
 */
export default function FeatureGate({ feature, allowGuest = true, children, testID }) {
  const { user } = useAuth();
  const property = useProperty();
  const killKey = killSwitchKeyFor({ feature });
  const kill = useKillSwitch(killKey);
  if (kill.killed) return <FeatureOff featureKey={killKey} message={kill.message} />;
  const access = featureAccess(property, user, feature, 'read');
  const required = !user && !allowGuest ? AccessRequirement.SignIn : access.allowed ? null : access.requires;
  if (!required) return children;
  return (
    <Screen testID={testID || 'feature-locked-' + feature}>
      <AccessPromptCard required={required} feature={feature} testID={'feature-locked-' + feature + '-prompt'} />
    </Screen>
  );
}

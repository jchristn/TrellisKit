import { catalogAccess } from '@trelliskit/shared';
import { useKillSwitch } from '../appConfig/AppContentProvider';
import { killSwitchKeyFor } from '../appConfig/killSwitches';
import { useAuth } from '../auth/AuthProvider';
import { featureOn } from '../content/phase4';
import { useProperty } from '../property';
import strings from '../strings';
import { AccessPromptCard } from './AccessPrompt';
import ErrorState from './ErrorState';
import FeatureOff from './FeatureOff';
import Screen from './Screen';

/**
 * Gate for the Phase 4 catalogs (library, events, venue directory; resolved decision 2): members
 * and staff always pass; signed-in free accounts pass in `Open` and `PartiallyGated` (gated objects
 * arrive as locked stubs), need a membership in `FullyGated`; signed-out visitors pass when
 * anonymous browsing is on (they see `Public` objects only). A feature switched off for the
 * property shows a not-available message and makes no API call; so does a catalog whose kill switch is on for
 * the caller (with the operator's message, LIVE_UPDATES_CONTRACT.md section 13.2). `optIn` names an
 * additional opt-in property switch (for example `catalog`) that must be explicitly `true`; a missing
 * switch counts as off and shows the same not-available message.
 * @param {{ feature: 'library'|'events'|'directory', optIn?: string, action?: 'read'|'write', children: React.ReactNode, testID?: string }} props
 */
export default function CatalogGate({ feature, optIn, action = 'read', children, testID }) {
  const { user } = useAuth();
  const property = useProperty();
  const killKey = killSwitchKeyFor({ catalog: feature });
  const kill = useKillSwitch(killKey);
  const optedIn = !optIn || property?.features?.[optIn] === true;
  if (!featureOn(property, feature) || !optedIn) {
    return (
      <Screen testID={testID || 'catalog-off-' + (optedIn ? feature : optIn)}>
        <ErrorState title={strings.errors.generic} message={strings.access.features[optedIn ? feature : optIn] || ''} />
      </Screen>
    );
  }
  if (kill.killed) return <FeatureOff featureKey={killKey} message={kill.message} />;
  const access = catalogAccess(property, user, feature, action);
  if (access.allowed) return children;
  return (
    <Screen testID={testID || 'catalog-locked-' + feature}>
      <AccessPromptCard required={access.requires} feature={feature} testID={'catalog-locked-' + feature + '-prompt'} />
    </Screen>
  );
}

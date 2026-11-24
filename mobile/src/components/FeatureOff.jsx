import { useAppContent } from '../appConfig/AppContentProvider';
import strings from '../strings';
import ErrorState from './ErrorState';
import Screen from './Screen';

/**
 * The screen shown in place of a feature whose kill switch is on for this caller
 * (docs/LIVE_UPDATES_CONTRACT.md section 13.2): the operator's message, or the generic notice, and a
 * "Try again" that forces a manifest check, so a switch turned back off clears without waiting for the
 * check interval. No API call of the feature is made while it shows.
 * @param {{ featureKey: string, message?: string|null, inline?: boolean, testID?: string }} props
 */
export default function FeatureOff({ featureKey, message = null, inline = false, testID }) {
  const content = useAppContent();
  const id = testID || 'feature-off-' + featureKey;
  const state = (
    <ErrorState
      title={strings.appContent.featureOffTitle}
      message={message || strings.appContent.featureUnavailable}
      onRetry={content && content.store ? () => content.store.refresh({ force: true }).catch(() => {}) : undefined}
      retryHint={strings.appContent.featureOffRetryHint}
      testID={id}
    />
  );
  if (inline) return state;
  return <Screen testID={id + '-screen'}>{state}</Screen>;
}

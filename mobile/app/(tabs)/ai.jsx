import AssistantsHome from '../../src/components/ai/AssistantsHome';
import FeatureGate from '../../src/components/FeatureGate';

/** The Assistants tab, shown only when the server reports `features.aiPlacement === 'Tab'` (docs/AI_CONTRACT.md section 12). */
function AssistantsTabContent() {
  return <AssistantsHome testID="screen-assistants-tab" />;
}

/** Gated by the access model: the `assistants` feature (ACCESS_MODELS_CONTRACT.md section 8.7); guests use a visitor session. */
export default function AssistantsTab() {
  return (
    <FeatureGate feature="assistants">
      <AssistantsTabContent />
    </FeatureGate>
  );
}

import AssistantsHome from '../../src/components/ai/AssistantsHome';
import FeatureGate from '../../src/components/FeatureGate';

/** `/assistants`: the assistants home, opened from Profile (docs/AI_CONTRACT.md section 12). */
function AssistantsScreenContent() {
  return <AssistantsHome />;
}

/** Gated by the access model: the `assistants` feature (ACCESS_MODELS_CONTRACT.md section 8.7); guests use a visitor session. */
export default function AssistantsScreen() {
  return (
    <FeatureGate feature="assistants">
      <AssistantsScreenContent />
    </FeatureGate>
  );
}

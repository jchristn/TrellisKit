import TrainingLibrary from '../../../src/components/training/TrainingLibrary';
import { useTrainingOverview } from '../../../src/training/useTrainingOverview';

/** The Training Library's Techniques segment (`/training/library/techniques`). */
export default function TrainingTechniquesScreen() {
  const hub = useTrainingOverview();
  return <TrainingLibrary hub={hub} segment="techniques" />;
}

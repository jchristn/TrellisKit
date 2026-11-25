import TrainingLibrary from '../../../src/components/training/TrainingLibrary';
import { useTrainingOverview } from '../../../src/training/useTrainingOverview';

/** The Training Library's Saved segment (`/training/library/saved`). */
export default function TrainingSavedScreen() {
  const hub = useTrainingOverview();
  return <TrainingLibrary hub={hub} segment="saved" />;
}

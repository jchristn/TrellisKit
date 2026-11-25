import { useLocalSearchParams } from 'expo-router';
import TrainingLibrary from '../../../src/components/training/TrainingLibrary';
import { useTrainingOverview } from '../../../src/training/useTrainingOverview';

/** The Training Library (`/training/library`, Courses unless `?segment=` names another segment). */
export default function TrainingLibraryScreen() {
  const hub = useTrainingOverview();
  const { segment } = useLocalSearchParams();
  return <TrainingLibrary hub={hub} segment={(Array.isArray(segment) ? segment[0] : segment) || 'courses'} />;
}

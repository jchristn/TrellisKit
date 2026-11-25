import TrainingLibrary from '../../../src/components/training/TrainingLibrary';
import { useTrainingOverview } from '../../../src/training/useTrainingOverview';

/** The Training Library's Courses segment (`/training/library/courses`). */
export default function TrainingCoursesScreen() {
  const hub = useTrainingOverview();
  return <TrainingLibrary hub={hub} segment="courses" />;
}

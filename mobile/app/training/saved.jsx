import TrainingLibrary from '../../src/components/training/TrainingLibrary';
import { useTrainingOverview } from '../../src/training/useTrainingOverview';

/**
 * Saved (`/training/saved`, the same screen as `/training/library/saved`): the member's saves with
 * a type filter, collections to create, rename, reorder, and delete, and notes (visible to you and
 * moderators). Signed-out visitors get the Library's first segment.
 */
export default function TrainingSavedScreen() {
  const hub = useTrainingOverview();
  return <TrainingLibrary hub={hub} segment="saved" />;
}

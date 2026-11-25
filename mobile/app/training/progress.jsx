import { Redirect } from 'expo-router';
import Loading from '../../src/components/Loading';
import TrainingFrame from '../../src/components/training/TrainingFrame';
import TrainingProgress from '../../src/components/training/TrainingProgress';
import strings from '../../src/strings';
import { segmentHref } from '../../src/training/hub';
import { useTrainingOverview } from '../../src/training/useTrainingOverview';

/** Training Progress (`/training/progress`): signed-in members only; guests (and members without the overview) open the Library. */
export default function TrainingProgressScreen() {
  const hub = useTrainingOverview();
  if (hub.guest || hub.status === 'libraryOnly') return <Redirect href={segmentHref('courses')} />;
  if (hub.status === 'loading') return <Loading label={strings.training.loading} />;
  return (
    <TrainingFrame hub={hub} current="Progress">
      <TrainingProgress />
    </TrainingFrame>
  );
}

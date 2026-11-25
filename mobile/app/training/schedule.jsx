import { Redirect } from 'expo-router';
import Loading from '../../src/components/Loading';
import TrainingFrame from '../../src/components/training/TrainingFrame';
import TrainingSchedule from '../../src/components/training/TrainingSchedule';
import strings from '../../src/strings';
import { segmentHref } from '../../src/training/hub';
import { useTrainingOverview } from '../../src/training/useTrainingOverview';

/** Training Schedule (`/training/schedule`): signed-in members only; guests (and members without the overview) open the Library. */
export default function TrainingScheduleScreen() {
  const hub = useTrainingOverview();
  if (hub.guest || hub.status === 'libraryOnly') return <Redirect href={segmentHref('courses')} />;
  if (hub.status === 'loading') return <Loading label={strings.training.loading} />;
  return (
    <TrainingFrame hub={hub} current="Schedule">
      <TrainingSchedule />
    </TrainingFrame>
  );
}

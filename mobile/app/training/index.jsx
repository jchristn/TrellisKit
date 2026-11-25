import { Redirect, useRouter } from 'expo-router';
import { useState } from 'react';
import Button from '../../src/components/Button';
import ErrorState from '../../src/components/ErrorState';
import Loading from '../../src/components/Loading';
import Screen from '../../src/components/Screen';
import TrainingFrame from '../../src/components/training/TrainingFrame';
import TrainingLibrary from '../../src/components/training/TrainingLibrary';
import TrainingOverview from '../../src/components/training/TrainingOverview';
import strings from '../../src/strings';
import { sectionHref, segmentHref, visibleSections } from '../../src/training/hub';
import { useTrainingOverview } from '../../src/training/useTrainingOverview';

const t = strings.training;

/**
 * Training Overview (`/training`). Signed-out visitors see the public Library only (the overview
 * route is never called for them). Members see the section tabs and the Overview cards; when the
 * server reports no Overview the first available section opens instead.
 */
export default function TrainingScreen() {
  const hub = useTrainingOverview();
  const router = useRouter();
  const [refreshing, setRefreshing] = useState(false);
  // Signed out, or the server refused the overview (402, 403, 404, 503): the Library alone.
  if (hub.guest || hub.status === 'libraryOnly') return <TrainingLibrary hub={hub} segment="courses" />;
  if (hub.status === 'loading') return <Loading label={t.loading} testID="training-loading" />;
  if (hub.status === 'error') {
    return (
      <Screen testID="training-error">
        <ErrorState title={t.loadFailed} onRetry={hub.reload} />
        <Button label={t.segments.courses} variant="secondary" onPress={() => router.replace(segmentHref('courses'))} testID="training-error-library" />
      </Screen>
    );
  }
  const sections = visibleSections(hub.overview);
  if (!sections.includes('Overview')) return <Redirect href={sections.length ? sectionHref(sections[0]) : segmentHref('courses')} />;
  const refresh = async () => {
    setRefreshing(true);
    await hub.reload();
    setRefreshing(false);
  };
  return (
    <TrainingFrame hub={hub} current="Overview">
      <TrainingOverview overview={hub.overview} offline={hub.offline || hub.stale} refreshing={refreshing} onRefresh={refresh} />
    </TrainingFrame>
  );
}

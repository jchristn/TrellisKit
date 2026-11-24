import { SubmissionTypes } from '@trelliskit/shared';
import { useLocalSearchParams, useNavigation } from 'expo-router';
import { useLayoutEffect } from 'react';
import CatalogGate from '../../src/components/CatalogGate';
import Screen from '../../src/components/Screen';
import SubmissionForm from '../../src/components/places/SubmissionForm';
import { useDirectoryStrings } from '../../src/content/phase4';

function param(value) {
  return Array.isArray(value) ? value[0] : value;
}

/**
 * A directory submission (`/directory/submit?type=NewVenue|EditVenue|NewInstructor|EditInstructor|ClaimInstructor|ReportClosed&targetId=`).
 * Writing needs the directory write access (a membership where the directory is gated).
 */
export default function SubmitScreen() {
  const navigation = useNavigation();
  const t = useDirectoryStrings();
  const params = useLocalSearchParams();
  const type = SubmissionTypes.includes(param(params.type)) ? param(params.type) : 'NewVenue';
  const targetId = param(params.targetId) || null;
  useLayoutEffect(() => {
    navigation.setOptions({ title: t.submit[type] });
  }, [navigation, t, type]);
  return (
    <CatalogGate feature="directory" action="write">
      <Screen testID="screen-submit">
        <SubmissionForm key={type + ':' + (targetId || '')} type={type} targetId={targetId} />
      </Screen>
    </CatalogGate>
  );
}

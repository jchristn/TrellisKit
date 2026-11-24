import CatalogGate from '../../src/components/CatalogGate';
import Screen from '../../src/components/Screen';
import ProposalForm from '../../src/components/events/ProposalForm';

/** Propose an event (`/events/propose`, resolved decision 10); needs the events write access. */
export default function ProposeScreen() {
  return (
    <CatalogGate feature="events" action="write">
      <Screen testID="screen-propose">
        <ProposalForm />
      </Screen>
    </CatalogGate>
  );
}

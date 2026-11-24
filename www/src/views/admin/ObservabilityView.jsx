import strings from '../../strings.js';
import PageHeader from '../../components/PageHeader.jsx';
import ServiceLinks from '../../components/ServiceLinks.jsx';
import { Banner } from '../../components/States.jsx';

const o = strings.admin.observability;

/** Local observability tools with URLs and default credentials in plain sight. */
function ObservabilityView() {
  return (
    <div className="view">
      <PageHeader title={strings.admin.routes.observability.label} summary={strings.admin.routes.observability.summary} />
      <Banner tone="info">{o.intro}</Banner>
      <section className="panel">
        <ServiceLinks />
      </section>
      <p className="muted small">{o.productionNote}</p>
    </div>
  );
}

export default ObservabilityView;

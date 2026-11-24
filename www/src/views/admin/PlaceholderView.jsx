import strings from '../../strings.js';
import PageHeader from '../../components/PageHeader.jsx';
import { Badge } from '../../components/Badges.jsx';
import { Banner } from '../../components/States.jsx';
import { routeTurnedOff } from './adminRoutes.js';

const p = strings.admin.placeholder;

/**
 * Admin section that is not open yet: its backend has not shipped (`comingSoon`), the server does
 * not report the `capability` it needs, or the server reports the capability turned off for this
 * property. Renders from route metadata and the property document only (no calls).
 */
function PlaceholderView({ route, property = null }) {
  const waitingOnCapability = Boolean(route.capability) && !route.comingSoon;
  const turnedOff = routeTurnedOff(property, route);
  if (turnedOff) {
    return (
      <div className="view" data-testid={'admin-turned-off-' + route.id}>
        <PageHeader title={route.label} summary={route.summary} actions={<Badge tone="neutral">{p.offBadge}</Badge>} />
        <section className="panel placeholder-panel">
          <h2>{p.offHeading(route.label)}</h2>
          <p className="muted">{p.offBody(route.capability)}</p>
          <Banner tone="info" icon="info">
            <strong>{p.offTitle}:</strong> {p.offHow(route.capability)}
          </Banner>
        </section>
      </div>
    );
  }
  return (
    <div className="view" data-testid={'admin-coming-soon-' + route.id}>
      <PageHeader title={route.label} summary={route.summary} actions={<Badge tone="info">{p.badge}</Badge>} />
      <section className="panel placeholder-panel">
        <h2>{p.heading(route.label)}</h2>
        <p className="muted">{waitingOnCapability ? p.capabilityBody : p.body}</p>
        {Array.isArray(route.planned) && route.planned.length > 0 && (
          <>
            <h3 className="section-title">{p.plannedTitle}</h3>
            <ul className="bullet-list">
              {route.planned.map((item) => <li key={item}>{item}</li>)}
            </ul>
          </>
        )}
        <Banner tone="warning" icon="alert">
          <strong>{p.missingTitle}:</strong> {waitingOnCapability ? p.missingCapability(route.capability, route.endpoint) : p.missing(route.endpoint)}
        </Banner>
      </section>
    </div>
  );
}

export default PlaceholderView;

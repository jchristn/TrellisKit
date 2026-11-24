import { Link } from 'react-router-dom';
import strings from '../../strings.js';
import useDocumentMeta from '../../hooks/useDocumentMeta.js';
import Icon from '../../components/Icon.jsx';

const m = strings.member;

/**
 * Friendly page for a member section that is not open yet (its backend has not shipped, or the
 * server does not report its capability). It renders from the route metadata only and makes no
 * API calls.
 * @param {{ route: { id: string, label: string, icon: string } }} props
 */
function ComingSoonView({ route }) {
  useDocumentMeta(m.metaTitle(route.label), m.metaDescription);
  return (
    <div className="member-view" data-testid={'coming-soon-' + route.id}>
      <section className="member-hero">
        <Icon name={route.icon} size={32} />
        <h1>{route.label}</h1>
        <span className="badge badge-info">{m.comingSoon}</span>
        <h2 className="section-title">{m.comingSoonTitle(route.label)}</h2>
        <p className="muted">{m.comingSoonBody(route.label)}</p>
        <Link to="/app/feed" className="button button-secondary">{m.comingSoonBack}</Link>
      </section>
    </div>
  );
}

export default ComingSoonView;

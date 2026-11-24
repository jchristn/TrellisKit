import strings from '../strings.js';
import CopyButton from './CopyButton.jsx';
import Icon from './Icon.jsx';

const o = strings.admin.observability;

export const SERVICES = [
  { id: 'grafana', url: 'http://127.0.0.1:3000', ...o.grafana },
  { id: 'prometheus', url: 'http://127.0.0.1:9090', ...o.prometheus },
  { id: 'tempo', url: 'http://127.0.0.1:3200', ...o.tempo }
];

/** List of local observability tools with URLs and credentials in plain sight. */
function ServiceLinks() {
  return (
    <ul className="service-list">
      {SERVICES.map((svc) => (
        <li key={svc.id} className="service-item">
          <div className="service-main">
            <strong>{svc.name}</strong>
            <span className="muted small">{svc.body}</span>
          </div>
          <dl className="service-meta">
            <div>
              <dt>{o.url}</dt>
              <dd className="copy-row">
                <a href={svc.url} target="_blank" rel="noreferrer" className="mono" aria-label={o.openInNewTab(svc.name)}>
                  {svc.url} <Icon name="external" size={12} />
                </a>
                <CopyButton value={svc.url} label={o.url} />
              </dd>
            </div>
            <div>
              <dt>{o.credentials}</dt>
              <dd>{svc.creds}</dd>
            </div>
          </dl>
        </li>
      ))}
    </ul>
  );
}

export default ServiceLinks;

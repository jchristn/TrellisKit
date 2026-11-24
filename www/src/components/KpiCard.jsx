import { Link } from 'react-router-dom';

/** Metric tile. Renders as a link when `to` is set; `children` render below the hint (for example a change versus a previous period). */
function KpiCard({ label, value, tone, to, hint, children }) {
  const body = (
    <>
      <span className="kpi-label">{label}</span>
      <span className={'kpi-value' + (tone ? ' tone-' + tone : '')}>{value}</span>
      {hint && <span className="kpi-hint">{hint}</span>}
      {children}
    </>
  );
  return to ? <Link to={to} className="kpi-card kpi-link">{body}</Link> : <div className="kpi-card">{body}</div>;
}

export default KpiCard;

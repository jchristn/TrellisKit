/** Badge and pill components for status, methods, and generic labels. */

export function Badge({ tone = 'neutral', children, title }) {
  return <span className={'badge badge-' + tone} title={title}>{children}</span>;
}

export function MethodBadge({ method }) {
  const m = String(method || '').toUpperCase();
  return <span className={'badge method method-' + m.toLowerCase()}>{m}</span>;
}

export function statusTone(code) {
  if (typeof code !== 'number') return 'neutral';
  if (code >= 500) return 'danger';
  if (code >= 400) return 'warning';
  if (code >= 300) return 'info';
  return 'success';
}

export function StatusBadge({ code }) {
  const failed = typeof code === 'number' && code >= 400;
  return (
    <span className={'badge badge-' + statusTone(code)}>
      {failed ? '✕ ' : ''}{code ?? ''}
    </span>
  );
}

export function healthTone(status) {
  if (status === 'Healthy') return 'success';
  if (status === 'Degraded') return 'warning';
  if (status === 'Unhealthy' || status === 'Unreachable') return 'danger';
  return 'neutral';
}

export function HealthDot({ status, label }) {
  return (
    <span className={'health health-' + healthTone(status)}>
      <span className="health-dot" aria-hidden="true" />
      <span>{label}</span>
    </span>
  );
}

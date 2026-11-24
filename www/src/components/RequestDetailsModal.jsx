import { useCallback, useState } from 'react';
import strings from '../strings.js';
import { useAdminAuth } from '../context/AdminAuthContext.jsx';
import useApiQuery from '../hooks/useApiQuery.js';
import { errorMessage } from '../utils/api.js';
import { formatBytes, formatPreciseDateTime, formatDuration, prettyBody } from '../utils/format.js';
import Modal from './Modal.jsx';
import CodeBlock from './CodeBlock.jsx';
import CopyButton from './CopyButton.jsx';
import CopyableId from './CopyableId.jsx';
import Icon from './Icon.jsx';
import { MethodBadge, StatusBadge } from './Badges.jsx';
import { ErrorBanner, Loading } from './States.jsx';

const d = strings.admin.details;

function Section({ title, copyValue, children, defaultOpen = true }) {
  const [open, setOpen] = useState(defaultOpen);
  return (
    <section className="detail-section">
      <div className="detail-section-header">
        <button
          type="button"
          className="section-toggle"
          aria-expanded={open}
          onClick={() => setOpen((v) => !v)}
          aria-label={open ? d.collapse(title) : d.expand(title)}
        >
          <Icon name={open ? 'chevronDown' : 'chevronRight'} size={14} />
          <h3>{title}</h3>
        </button>
        {copyValue !== undefined && <CopyButton value={copyValue} label={title} />}
      </div>
      {open && <div className="detail-section-body">{children}</div>}
    </section>
  );
}

function HeadersTable({ headers }) {
  const entries = Object.entries(headers || {});
  if (entries.length === 0) return <p className="muted">{d.noHeaders}</p>;
  return (
    <div className="table-scroll">
      <table className="kv-table">
        <thead><tr><th scope="col">{d.header}</th><th scope="col">{d.value}</th></tr></thead>
        <tbody>
          {entries.map(([k, v]) => (
            <tr key={k}><td className="mono nowrap">{k}</td><td className="mono break">{Array.isArray(v) ? v.join(', ') : String(v)}</td></tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function Body({ body, truncated, bytes }) {
  return (
    <>
      {truncated && <p className="warning-text"><Icon name="alert" size={14} /> {d.truncated(formatBytes(bytes))}</p>}
      {body === null || body === undefined || body === '' ? <p className="muted">{d.noBody}</p> : <CodeBlock value={prettyBody(body)} label={d.value} />}
    </>
  );
}

/**
 * Request inspector: metadata, headers, bodies, and raw JSON for one request history entry.
 * The list endpoint omits bodies, so this always fetches the detail endpoint.
 */
function RequestDetailsModal({ id, onClose }) {
  const { api } = useAdminAuth();
  const fetcher = useCallback((signal) => api.getRequestHistoryEntry(id, signal), [api, id]);
  const { data, error, loading, reload } = useApiQuery(fetcher, { enabled: Boolean(id) });

  const meta = data ? [
    [d.fields.method, <MethodBadge key="m" method={data.method} />],
    [d.fields.path, <code key="p" className="mono break">{data.path}</code>],
    [d.fields.url, <code key="u" className="mono break">{data.url}</code>],
    [d.fields.statusCode, <StatusBadge key="s" code={data.statusCode} />],
    [d.fields.duration, formatDuration(data.durationMs)],
    [d.fields.createdUtc, <span key="c" title={data.createdUtc}>{formatPreciseDateTime(data.createdUtc)}</span>],
    [d.fields.completedUtc, <span key="cc" title={data.completedUtc}>{formatPreciseDateTime(data.completedUtc)}</span>],
    [d.fields.sourceIp, <code key="ip" className="mono">{data.sourceIp}</code>],
    [d.fields.principal, data.principalName || strings.admin.requests.anonymous],
    [d.fields.userId, data.userId ? <CopyableId key="uid" value={data.userId} label={d.fields.userId} /> : strings.common.none],
    [d.fields.requestBytes, formatBytes(data.requestBodyBytes)],
    [d.fields.responseBytes, formatBytes(data.responseBodyBytes)]
  ] : [];

  return (
    <Modal
      open={Boolean(id)}
      onClose={onClose}
      size="xl"
      title={d.title}
      subtitle={<CopyableId value={id} label={d.fields.id} />}
    >
      {loading && !data && <Loading />}
      {error && <ErrorBanner message={errorMessage(error, d.loadError)} onRetry={reload} />}
      {data && (
        <div className="detail-sections">
          <Section title={d.metadata} copyValue={JSON.stringify({ id: data.id, method: data.method, url: data.url, statusCode: data.statusCode, durationMs: data.durationMs, createdUtc: data.createdUtc }, null, 2)}>
            <dl className="kv-grid">
              {meta.map(([label, value]) => (
                <div key={label} className="kv-item"><dt>{label}</dt><dd>{value}</dd></div>
              ))}
            </dl>
          </Section>
          <Section title={d.requestHeaders} copyValue={JSON.stringify(data.requestHeaders || {}, null, 2)}>
            <HeadersTable headers={data.requestHeaders} />
          </Section>
          <Section title={d.requestBody} copyValue={data.requestBody || ''}>
            <Body body={data.requestBody} truncated={data.requestBodyTruncated} bytes={data.requestBodyBytes} />
          </Section>
          <Section title={d.responseHeaders} copyValue={JSON.stringify(data.responseHeaders || {}, null, 2)}>
            <HeadersTable headers={data.responseHeaders} />
          </Section>
          <Section title={d.responseBody} copyValue={data.responseBody || ''}>
            <Body body={data.responseBody} truncated={data.responseBodyTruncated} bytes={data.responseBodyBytes} />
          </Section>
          <Section title={d.rawJson} copyValue={JSON.stringify(data, null, 2)} defaultOpen={false}>
            <CodeBlock value={JSON.stringify(data, null, 2)} label={d.rawJson} maxHeight={500} />
          </Section>
        </div>
      )}
    </Modal>
  );
}

export default RequestDetailsModal;

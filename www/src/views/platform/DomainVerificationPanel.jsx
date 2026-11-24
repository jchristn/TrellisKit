import strings from '../../strings.js';
import { formatDateTime } from '../../utils/format.js';
import CopyButton from '../../components/CopyButton.jsx';
import DataTable from '../../components/DataTable.jsx';
import { Badge } from '../../components/Badges.jsx';

const d = strings.platform.detail;
const col = d.verificationColumns;

/** Who added a domain: `Platform` (platform administrators and the property folder) or `Property` (its own administrators). */
export function domainSource(domain) {
  return (domain && domain.source) || 'Platform';
}

function VerificationRecord({ domain }) {
  if (domain.verifiedUtc) return <span className="muted">-</span>;
  const v = domain.verification;
  if (!v) return <span className="muted small">{d.noRecord}</span>;
  return (
    <div className="stack-form">
      <span className="copy-row">
        <span className="label-text">{d.recordName}</span>
        <code className="mono break">{v.recordName}</code>
        <CopyButton value={v.recordName} label={d.recordName + ' ' + domain.host} />
      </span>
      <span className="copy-row">
        <span className="label-text">{d.recordValue}</span>
        <code className="mono break">{v.recordValue}</code>
        <CopyButton value={v.recordValue} label={d.recordValue + ' ' + domain.host} />
      </span>
      <span className="muted small">{v.lastCheckedUtc ? d.lastChecked(formatDateTime(v.lastCheckedUtc)) : d.neverChecked}</span>
      {v.error && <span className="field-error">{d.checkError(v.error)}</span>}
    </div>
  );
}

/**
 * Read-only DNS ownership state of a property's domains: who added each one, whether it is
 * verified, and for an unverified domain the TXT record its owner must publish. Platform-added
 * domains are verified at once; property-added domains are checked from the property's own console.
 */
function DomainVerificationPanel({ record }) {
  const domains = (record && record.domains) || [];
  const columns = [
    { key: 'host', label: col.host, render: (row) => <code className="mono">{row.host}</code> },
    { key: 'kind', label: col.kind, render: (row) => strings.platform.domainKinds[row.kind] || row.kind },
    { key: 'source', label: col.source, render: (row) => <Badge tone={domainSource(row) === 'Platform' ? 'info' : 'neutral'}>{d.sources[domainSource(row)] || domainSource(row)}</Badge> },
    {
      key: 'state', label: col.state, className: 'nowrap', render: (row) => (row.verifiedUtc
        ? <Badge tone="success" title={row.verifiedUtc}>{d.verifiedOn(formatDateTime(row.verifiedUtc))}</Badge>
        : <Badge tone="warning">{d.awaitingTxt}</Badge>)
    },
    { key: 'record', label: col.record, className: 'cell-wrap', render: (row) => <VerificationRecord domain={row} /> }
  ];
  return (
    <section className="panel" aria-labelledby="domain-verification-title">
      <div className="panel-header"><h2 id="domain-verification-title">{d.verificationTitle}</h2></div>
      <p className="muted small">{d.verificationSummary}</p>
      <DataTable caption={d.verificationTitle} columns={columns} rows={domains} rowKey={(row) => row.id || row.host} emptyMessage={d.verificationEmpty} />
    </section>
  );
}

export default DomainVerificationPanel;

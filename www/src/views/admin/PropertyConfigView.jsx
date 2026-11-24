import { useCallback, useEffect, useState } from 'react';
import { ApiError, domainVerificationState, stripeWebhookPath } from '@trelliskit/shared';
import strings from '../../strings.js';
import { useAdminAuth } from '../../context/AdminAuthContext.jsx';
import { useProperty } from '../../context/PropertyContext.jsx';
import { useToast } from '../../context/ToastContext.jsx';
import useApiQuery from '../../hooks/useApiQuery.js';
import { errorMessage } from '../../utils/api.js';
import { formatDateTime } from '../../utils/format.js';
import PageHeader from '../../components/PageHeader.jsx';
import CopyButton from '../../components/CopyButton.jsx';
import DomainsAppsEditor from '../../components/DomainsAppsEditor.jsx';
import Icon from '../../components/Icon.jsx';
import { Badge } from '../../components/Badges.jsx';
import { ErrorBanner, Loading } from '../../components/States.jsx';
import TableColumns from '../../components/TableColumns.jsx';

const p = strings.admin.propertyConfig;
const route = strings.admin.routes.property;

/** The public origin Stripe should call: the property's API domain, else this site's origin. */
export function stripeWebhookUrl(property, origin = window.location.origin) {
  const api = property && property.domains && property.domains.api;
  const base = api ? 'https://' + api : origin;
  return base + stripeWebhookPath(property && property.id);
}

function KvCopy({ label, value }) {
  return (
    <div className="kv-item">
      <dt>{label}</dt>
      <dd className="copy-row"><code className="mono">{value}</code><CopyButton value={value} label={label} /></dd>
    </div>
  );
}

/**
 * A `409 DomainInUse` or `AppIdInUse` names the host or app id in its message; return the error
 * with a `fieldErrors` entry on the offending row (`domains[i].host`, `apps[i].appId`) so the
 * editor highlights it. Other errors pass through.
 */
export function conflictToRowError(err, body) {
  if (!(err instanceof ApiError) || err.status !== 409) return err;
  const message = err.message || '';
  const fieldErrors = {};
  if (err.code === 'DomainInUse') {
    const index = (body.domains || []).findIndex((d) => d.host && message.toLowerCase().includes(d.host.toLowerCase()));
    if (index >= 0) fieldErrors['domains[' + index + '].host'] = message;
  } else if (err.code === 'AppIdInUse') {
    const index = (body.apps || []).findIndex((a) => a.appId && message.includes(a.appId));
    if (index >= 0) fieldErrors['apps[' + index + '].appId'] = message;
  }
  if (!Object.keys(fieldErrors).length) return err;
  return new ApiError(message, { status: err.status, code: err.code, body: { ...(err.body && typeof err.body === 'object' ? err.body : {}), fieldErrors } });
}

const STATE_TONES = { Verified: 'success', Pending: 'warning', Unverified: 'neutral' };

/** One domain with its verification state, TXT record, last check, and a Verify action. */
function DomainVerificationRow({ domain, onVerified }) {
  const { api } = useAdminAuth();
  const toast = useToast();
  const [busy, setBusy] = useState(false);
  const state = domainVerificationState(domain);
  const v = domain.verification;
  const verify = async () => {
    setBusy(true);
    try {
      const updated = await api.verifyPropertyDomain(domain.id);
      if (updated && updated.verifiedUtc) toast.success(p.verify.verified(domain.host));
      else toast.error(p.verify.notYet(domain.host));
      onVerified(updated);
    } catch (err) {
      toast.error(errorMessage(err, strings.common.errorGeneric));
    } finally {
      setBusy(false);
    }
  };
  return (
    <tr>
      <td><code className="mono">{domain.host}</code></td>
      <td>{strings.platform.domainKinds[domain.kind] || domain.kind}</td>
      <td><Badge tone={domain.source === 'Platform' ? 'info' : 'neutral'}>{p.verify.sources[domain.source] || domain.source}</Badge></td>
      <td>
        <span title={domain.verifiedUtc ? formatDateTime(domain.verifiedUtc) : undefined}><Badge tone={STATE_TONES[state]}>{p.verify.states[state]}</Badge></span>
        {domain.verifiedUtc && <div className="muted small">{formatDateTime(domain.verifiedUtc)}</div>}
      </td>
      <td>
        {state === 'Verified' ? <span className="muted small">{p.verify.noAction}</span> : (
          <div className="domain-verify">
            {v ? (
              <>
                <div className="muted small">{p.verify.instructions}</div>
                <div className="copy-row"><span className="muted small">{p.verify.recordName}</span> <code className="mono">{v.recordName}</code><CopyButton value={v.recordName} label={p.verify.recordName} /></div>
                <div className="copy-row"><span className="muted small">{p.verify.recordValue}</span> <code className="mono">{v.recordValue}</code><CopyButton value={v.recordValue} label={p.verify.recordValue} /></div>
                <div className="muted small">{v.lastCheckedUtc ? p.verify.lastChecked(formatDateTime(v.lastCheckedUtc)) : p.verify.neverChecked}</div>
                {v.error && <span className="field-error" role="alert">{v.error}</span>}
              </>
            ) : <span className="muted small">{p.verify.noChallenge}</span>}
            <div>
              <button type="button" className="button button-secondary button-sm" onClick={verify} disabled={busy || !v} aria-label={p.verify.verifyHost(domain.host)}>
                <Icon name="refresh" size={14} /><span>{busy ? p.verify.checking : p.verify.verify}</span>
              </button>
            </div>
          </div>
        )}
      </td>
    </tr>
  );
}

/** Domains (with DNS verification) and app ids through `/admin/property/domains`. */
function DomainsPanel() {
  const { api, mode } = useAdminAuth();
  const fetcher = useCallback((signal) => api.getPropertyDomains(signal), [api]);
  const query = useApiQuery(fetcher);
  const [record, setRecord] = useState(null);
  const [version, setVersion] = useState(0);
  useEffect(() => {
    if (query.data) setRecord(query.data);
  }, [query.data]);

  if (query.loading && !record) return <Loading />;
  if (query.error && !record) return <ErrorBanner message={p.loadError + ' ' + errorMessage(query.error, '')} onRetry={query.reload} />;
  if (!record) return null;
  const domains = record.domains || [];
  const pending = domains.filter((d) => !d.verifiedUtc).length;

  const onVerified = (updated) => {
    if (!updated || !updated.id) {
      query.reload();
      return;
    }
    setRecord((r) => ({ ...r, domains: (r.domains || []).map((d) => (d.id === updated.id ? { ...d, ...updated } : d)) }));
  };

  const save = async (body) => {
    try {
      return await api.updatePropertyDomains(body);
    } catch (err) {
      throw conflictToRowError(err, body);
    }
  };

  return (
    <>
      <section className="panel" aria-labelledby="property-domains-title">
        <div className="panel-header row-between">
          <h2 id="property-domains-title">{p.domainsTitle}</h2>
          <button type="button" className="icon-button" onClick={query.reload} disabled={query.loading} aria-label={strings.common.refresh} title={strings.common.refresh}><Icon name="refresh" className={query.loading ? 'spin' : ''} /></button>
        </div>
        <p className="muted small">{pending ? p.verify.pendingSummary(pending) : p.verify.allVerified}</p>
        {domains.length === 0 ? <p className="muted">{p.noDomains}</p> : (
          <div className="table-scroll">
            <TableColumns id="tk.admin.PropertyConfigView"><table className="data-table">
              <caption className="visually-hidden">{p.domainsTitle}</caption>
              <thead><tr><th scope="col">{p.verify.host}</th><th scope="col">{p.verify.kind}</th><th scope="col">{p.verify.source}</th><th scope="col">{p.verify.state}</th><th scope="col">{p.verify.dns}</th></tr></thead>
              <tbody>{domains.map((d) => <DomainVerificationRow key={d.id} domain={d} onVerified={onVerified} />)}</tbody>
            </table></TableColumns>
          </div>
        )}
      </section>
      <section className="panel" aria-labelledby="property-edit-title">
        <div className="panel-header"><h2 id="property-edit-title">{p.editTitle}</h2></div>
        <p className="muted small">{mode === 'delegated' ? p.editHintDelegated : p.editHint}</p>
        <DomainsAppsEditor key={version} record={record} onSave={save} onSaved={(updated) => { setRecord(updated); setVersion((x) => x + 1); }} />
      </section>
    </>
  );
}

/**
 * Domains and apps: the property id, the per-property Stripe webhook URL, every domain with its
 * DNS ownership verification, and the editable domain and app id lists. Property administrators
 * and delegated platform administrators both use `/admin/property/domains`; domains a property
 * administrator adds stay unverified (not routed) until their TXT record checks out.
 */
function PropertyConfigView() {
  const { property } = useProperty();
  return (
    <div className="view">
      <PageHeader title={route.label} summary={route.summary} />

      <section className="panel" aria-labelledby="property-identity-title">
        <div className="panel-header"><h2 id="property-identity-title">{p.identityTitle}</h2></div>
        <dl className="kv-grid kv-grid-wide">
          {property && property.id && <KvCopy label={p.propertyId} value={property.id} />}
          {property && property.slug && (
            <div className="kv-item"><dt>{p.slug}</dt><dd><code className="mono">{property.slug}</code> <span className="muted small">{p.slugHint}</span></dd></div>
          )}
        </dl>
      </section>

      <section className="panel" aria-labelledby="property-stripe-title">
        <div className="panel-header"><h2 id="property-stripe-title">{p.stripeTitle}</h2></div>
        <dl className="kv-grid kv-grid-wide">
          <KvCopy label={p.stripeUrl} value={stripeWebhookUrl(property)} />
        </dl>
        <p className="muted small">{p.stripeHint}</p>
      </section>

      <DomainsPanel />
    </div>
  );
}

export default PropertyConfigView;

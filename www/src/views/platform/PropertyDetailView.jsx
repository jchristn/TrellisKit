import { useCallback, useMemo, useState } from 'react';
import { Link, useParams, useSearchParams } from 'react-router-dom';
import { PropertyStatus, propertyDeletionState } from '@trelliskit/shared';
import strings from '../../strings.js';
import { usePlatformAuth } from '../../context/PlatformAuthContext.jsx';
import { useToast } from '../../context/ToastContext.jsx';
import useApiQuery from '../../hooks/useApiQuery.js';
import { errorMessage, getFieldErrors } from '../../utils/api.js';
import { normalizePage } from '../../utils/query.js';
import { platformMetadataSource, recordStripeWebhookUrl, startDelegation } from '../../utils/platformQuery.js';
import { formatDateTime } from '../../utils/format.js';
import PageHeader from '../../components/PageHeader.jsx';
import CopyButton from '../../components/CopyButton.jsx';
import DomainsAppsEditor from '../../components/DomainsAppsEditor.jsx';
import MetadataEditor from '../../components/MetadataEditor.jsx';
import Modal from '../../components/Modal.jsx';
import ConfirmModal from '../../components/ConfirmModal.jsx';
import ReasonModal from '../../components/ReasonModal.jsx';
import DataTable from '../../components/DataTable.jsx';
import Icon from '../../components/Icon.jsx';
import { Banner, ErrorBanner, Loading } from '../../components/States.jsx';
import { PropertyStatusBadge } from './PropertiesView.jsx';
import PlatformAuditView from './PlatformAuditView.jsx';
import DomainVerificationPanel from './DomainVerificationPanel.jsx';
import PropertyIntegrationsPanel from './PropertyIntegrationsPanel.jsx';
import UsageDashboard from '../../components/usage/UsageDashboard.jsx';
import { platformUsageSource } from '../../utils/usageApi.js';
import BrandingEditor from '../../components/branding/BrandingEditor.jsx';
import AiSwitchPanel from '../../components/branding/AiSwitchPanel.jsx';
import DocumentsManager from '../../components/documents/DocumentsManager.jsx';

const d = strings.platform.detail;
const pa = strings.platform.propertyAdmins;
const TABS = ['overview', 'usage', 'domains', 'branding', 'documents', 'ai', 'integrations', 'admins', 'audit', 'danger'];

function Kv({ label, children }) {
  return <div className="kv-item"><dt>{label}</dt><dd>{children}</dd></div>;
}

function NotReadyList({ error }) {
  const fe = getFieldErrors(error);
  const entries = Object.entries(fe);
  if (!entries.length) return null;
  return <ul className="metadata-problems">{entries.map(([k, v]) => <li key={k} className="field-error"><code className="mono">{k}</code>: {v}</li>)}</ul>;
}

/** Typed confirmation: the delete button stays disabled until the property id is typed exactly. */
export function DeleteConfirmModal({ open, record, onConfirm, onCancel }) {
  const [typed, setTyped] = useState('');
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const matches = typed === record.id;
  const submit = async (ev) => {
    ev.preventDefault();
    if (!matches) {
      setError(d.deleteMismatch);
      return;
    }
    setBusy(true);
    setError('');
    try {
      await onConfirm(typed);
    } catch (err) {
      setError(errorMessage(err, strings.common.errorGeneric));
      setBusy(false);
    }
  };
  return (
    <Modal
      open={open}
      onClose={busy ? () => {} : onCancel}
      title={d.deleteConfirmTitle}
      size="sm"
      footer={(
        <>
          <button type="button" className="button button-secondary" onClick={onCancel} disabled={busy}>{strings.common.cancel}</button>
          <button type="submit" form="delete-property-form" className="button button-danger" disabled={busy || !matches}>{d.deleteButton}</button>
        </>
      )}
    >
      <form id="delete-property-form" className="stack-form" onSubmit={submit} noValidate>
        <p className="confirm-message">{d.deleteBody}</p>
        <p>{d.deleteConfirmBody(record.name)}</p>
        <p><code className="mono">{record.id}</code></p>
        <ErrorBanner message={error} />
        <label className="field" htmlFor="delete-confirm">
          <span className="label-text">{d.deleteConfirmLabel}</span>
          <input id="delete-confirm" className="mono" autoComplete="off" spellCheck="false" value={typed} onChange={(x) => setTyped(x.target.value)} aria-invalid={Boolean(typed) && !matches} />
        </label>
      </form>
    </Modal>
  );
}

function RenameModal({ open, record, onSave, onCancel }) {
  const [name, setName] = useState(record.name);
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const submit = async (ev) => {
    ev.preventDefault();
    if (!name.trim()) return;
    setBusy(true);
    try {
      await onSave(name.trim());
    } catch (err) {
      setError(errorMessage(err, strings.common.errorGeneric));
      setBusy(false);
    }
  };
  return (
    <Modal
      open={open}
      onClose={busy ? () => {} : onCancel}
      title={d.renameTitle}
      size="sm"
      footer={(
        <>
          <button type="button" className="button button-secondary" onClick={onCancel} disabled={busy}>{strings.common.cancel}</button>
          <button type="submit" form="rename-property-form" className="button button-primary" disabled={busy || !name.trim()}>{d.renameSave}</button>
        </>
      )}
    >
      <form id="rename-property-form" className="stack-form" onSubmit={submit} noValidate>
        <ErrorBanner message={error} />
        <label className="field" htmlFor="rename-input">
          <span className="label-text">{d.name}</span>
          <input id="rename-input" value={name} onChange={(x) => setName(x.target.value)} />
        </label>
      </form>
    </Modal>
  );
}

function Overview({ record, onChange }) {
  const { call, client } = usePlatformAuth();
  const metadataSource = useMemo(() => platformMetadataSource(client), [client]);
  const toast = useToast();
  const [dialog, setDialog] = useState(null);
  const [notReady, setNotReady] = useState(null);
  const [acting, setActing] = useState(false);
  const [meta, setMeta] = useState({ labels: record.labels || [], tags: record.tags || {} });
  const [metaProblems, setMetaProblems] = useState([]);
  const [savingMeta, setSavingMeta] = useState(false);
  const stripeUrl = recordStripeWebhookUrl(record);
  const del = propertyDeletionState(record);

  const enable = async () => {
    setNotReady(null);
    try {
      onChange(await call((client) => client.enableProperty(record.id)));
      toast.success(d.enabled);
    } catch (err) {
      if (err && err.code === 'PropertyNotReady') setNotReady(err);
      else toast.error(errorMessage(err, strings.common.errorGeneric));
    }
  };

  const act = async () => {
    setActing(true);
    try {
      await startDelegation(call, record);
    } catch (err) {
      toast.error(err && err.status === 403 ? d.actBreakGlass : errorMessage(err, strings.common.errorGeneric));
      setActing(false);
    }
  };

  const saveMeta = async () => {
    if (metaProblems.length) return;
    setSavingMeta(true);
    try {
      onChange(await call((client) => client.updateProperty(record.id, { labels: meta.labels, tags: meta.tags })));
      toast.success(d.metadataSaved);
    } catch (err) {
      toast.error(errorMessage(err, strings.common.errorGeneric));
    } finally {
      setSavingMeta(false);
    }
  };

  return (
    <>
      <section className="panel" aria-labelledby="overview-title">
        <div className="panel-header">
          <h2 id="overview-title">{d.overviewTitle}</h2>
          <div className="toolbar-right">
            {!del.deleting && !del.deleted && <button type="button" className="button button-secondary button-sm" onClick={() => setDialog('rename')}><Icon name="edit" size={14} /><span>{d.rename}</span></button>}
            {record.status === PropertyStatus.Disabled && <button type="button" className="button button-primary button-sm" onClick={enable}><Icon name="power" size={14} /><span>{d.enable}</span></button>}
            {record.status === PropertyStatus.Enabled && <button type="button" className="button button-danger-outline button-sm" onClick={() => setDialog('disable')}><Icon name="ban" size={14} /><span>{d.disable}</span></button>}
          </div>
        </div>
        {notReady && (
          <Banner tone="warning" icon="alert">
            {d.notReady}
            <NotReadyList error={notReady} />
          </Banner>
        )}
        <dl className="kv-grid kv-grid-wide">
          <Kv label={d.id}><span className="copy-row"><code className="mono">{record.id}</code><CopyButton value={record.id} label={d.id} /></span></Kv>
          <Kv label={d.slug}><code className="mono">{record.slug || '-'}</code></Kv>
          <Kv label={d.status}><PropertyStatusBadge record={record} /></Kv>
          <Kv label={d.members}>{record.memberCount}</Kv>
          <Kv label={d.created}>{formatDateTime(record.createdUtc)}</Kv>
          <Kv label={d.updated}>{formatDateTime(record.updatedUtc)}</Kv>
          {record.enabledUtc && <Kv label={d.enabledAt}>{formatDateTime(record.enabledUtc)}</Kv>}
          {record.disabledUtc && <Kv label={d.disabledAt}>{formatDateTime(record.disabledUtc)}</Kv>}
          {record.disabledReason && <Kv label={d.disabledReason}>{record.disabledReason}</Kv>}
          <Kv label={d.stripeWebhook}><span className="copy-row"><code className="mono">{stripeUrl}</code><CopyButton value={stripeUrl} label={d.stripeWebhook} /></span></Kv>
        </dl>
        <p className="muted small">{d.stripeHint}</p>
      </section>

      <section className="panel" aria-labelledby="act-title">
        <div className="panel-header"><h2 id="act-title">{d.actInApp}</h2></div>
        <p className="muted small">{record.status === PropertyStatus.Enabled ? d.actInAppHint : d.actInAppUnavailable}</p>
        <div>
          <button type="button" className="button button-primary" onClick={act} disabled={acting || record.status !== PropertyStatus.Enabled}>
            <Icon name="shield" size={14} /><span>{acting ? d.actStarting : d.actInApp}</span>
          </button>
        </div>
      </section>

      <section className="panel" aria-labelledby="meta-title">
        <div className="panel-header"><h2 id="meta-title">{d.metadataTitle}</h2></div>
        <MetadataEditor value={meta} onChange={(next, problems) => { setMeta(next); setMetaProblems(problems); }} isStaff client={metadataSource} targetType="Property" legend={d.metadataTitle} disabled={savingMeta} />
        <div className="form-actions">
          <button type="button" className="button button-secondary" onClick={saveMeta} disabled={savingMeta || metaProblems.length > 0}>{d.metadataSave}</button>
        </div>
      </section>

      {dialog === 'rename' && (
        <RenameModal
          open
          record={record}
          onCancel={() => setDialog(null)}
          onSave={async (name) => {
            onChange(await call((client) => client.updateProperty(record.id, { name })));
            setDialog(null);
            toast.success(d.renamed);
          }}
        />
      )}
      <ReasonModal
        key={dialog === 'disable' ? 'disable-open' : 'disable-closed'}
        open={dialog === 'disable'}
        title={d.disableTitle}
        message={d.disableBody}
        confirmLabel={d.disable}
        onCancel={() => setDialog(null)}
        onConfirm={async (reason) => {
          onChange(await call((client) => client.disableProperty(record.id, reason)));
          setDialog(null);
          toast.success(d.disabled);
        }}
      />
    </>
  );
}

function PropertyAdmins({ record }) {
  const { call } = usePlatformAuth();
  const toast = useToast();
  const [userId, setUserId] = useState('');
  const [error, setError] = useState('');
  const [revoking, setRevoking] = useState(null);
  const fetcher = useCallback((signal) => call((client) => client.listPropertyAdmins(record.id, { maxResults: 100 }, signal)), [call, record.id]);
  const list = useApiQuery(fetcher);
  const page = list.data ? normalizePage(list.data, 100) : null;

  const grant = async (ev) => {
    ev.preventDefault();
    if (!userId.trim()) return;
    setError('');
    try {
      await call((client) => client.grantPropertyAdmin(record.id, userId.trim()));
      setUserId('');
      toast.success(pa.granted);
      list.reload();
    } catch (err) {
      setError(errorMessage(err, strings.common.errorGeneric));
    }
  };

  const columns = [
    { key: 'name', label: pa.columns.name, render: (row) => <span className="member-cell-text"><span>{row.displayName || '-'}</span>{row.handle && <span className="muted small">{'@' + row.handle}</span>}</span> },
    { key: 'id', label: pa.columns.id, render: (row) => <code className="mono small">{row.id}</code> },
    {
      key: 'actions', className: 'cell-actions', label: <span className="visually-hidden">{pa.columns.actions}</span>,
      render: (row) => <button type="button" className="button button-danger-outline button-sm" onClick={() => setRevoking(row)}>{pa.revoke}</button>
    }
  ];

  return (
    <section className="panel" aria-labelledby="property-admins-title">
      <div className="panel-header"><h2 id="property-admins-title">{pa.title}</h2></div>
      <p className="muted small">{pa.summary}</p>
      <form className="filter-bar" onSubmit={grant} noValidate>
        <label className="field field-compact" htmlFor="grant-user">
          <span className="label-text">{pa.grantLabel}</span>
          <input id="grant-user" className="input-compact mono" placeholder={pa.grantPlaceholder} value={userId} onChange={(x) => setUserId(x.target.value)} />
        </label>
        <button type="submit" className="button button-primary button-sm" disabled={!userId.trim()}>{pa.grant}</button>
      </form>
      <p className="muted small">{pa.grantHint}</p>
      <ErrorBanner message={error} onDismiss={() => setError('')} />
      <DataTable
        caption={pa.title}
        columns={columns}
        rows={page ? page.items : []}
        rowKey={(row) => row.id}
        loading={list.loading}
        error={list.error ? errorMessage(list.error, strings.common.errorGeneric) : null}
        onRetry={list.reload}
        emptyMessage={pa.empty}
      />
      <ConfirmModal
        open={Boolean(revoking)}
        title={pa.revokeTitle}
        message={revoking ? pa.revokeBody(revoking.displayName || revoking.id) : ''}
        confirmLabel={pa.revoke}
        onCancel={() => setRevoking(null)}
        onConfirm={async () => {
          const target = revoking;
          setRevoking(null);
          try {
            await call((client) => client.revokePropertyAdmin(record.id, target.id));
            toast.success(pa.revoked);
            list.reload();
          } catch (err) {
            toast.error(errorMessage(err, strings.common.errorGeneric));
          }
        }}
      />
    </section>
  );
}

function DangerZone({ record, onChange }) {
  const { call } = usePlatformAuth();
  const toast = useToast();
  const [dialog, setDialog] = useState(null);
  const del = propertyDeletionState(record);

  return (
    <section className="panel danger-zone" aria-labelledby="danger-title">
      <div className="panel-header"><h2 id="danger-title">{d.dangerTitle}</h2></div>
      {!del.deleting && !del.deleted && (
        <>
          <h3>{d.deleteTitle}</h3>
          <p>{d.deleteBody}</p>
          <p className="muted small">{d.deleteExportNote}</p>
          {record.status === PropertyStatus.Enabled && <Banner tone="warning" icon="alert">{d.deleteNotDisabled}</Banner>}
          <div>
            <button type="button" className="button button-danger" onClick={() => setDialog('delete')} disabled={record.status !== PropertyStatus.Disabled}>
              <Icon name="trash" size={14} /><span>{d.deleteButton}</span>
            </button>
          </div>
        </>
      )}
      {del.restorable && (
        <>
          <p>{d.restoreBody}</p>
          <div>
            <button type="button" className="button button-primary" onClick={() => setDialog('restore')}><Icon name="refresh" size={14} /><span>{d.restore}</span></button>
          </div>
        </>
      )}
      {(del.deleting || del.deleted) && (
        <>
          <h3>{d.purgeTitle}</h3>
          <p className="muted small">{d.purgeBody}</p>
          {del.purgeAtUtc && <p>{d.purgeScheduled(formatDateTime(del.purgeAtUtc))}</p>}
          {record.purgeStartedUtc && <p>{d.purgeStarted(formatDateTime(record.purgeStartedUtc))}</p>}
        </>
      )}
      {dialog === 'delete' && (
        <DeleteConfirmModal
          open
          record={record}
          onCancel={() => setDialog(null)}
          onConfirm={async (confirm) => {
            onChange(await call((client) => client.deleteProperty(record.id, confirm)));
            setDialog(null);
            toast.success(d.deleted);
          }}
        />
      )}
      <ConfirmModal
        open={dialog === 'restore'}
        title={d.restoreTitle}
        message={d.restoreBody}
        confirmLabel={d.restore}
        danger={false}
        onCancel={() => setDialog(null)}
        onConfirm={async () => {
          setDialog(null);
          try {
            onChange(await call((client) => client.restoreProperty(record.id)));
            toast.success(d.restored);
          } catch (err) {
            toast.error(errorMessage(err, strings.common.errorGeneric));
          }
        }}
      />
    </section>
  );
}

/** The Usage tab: the property console's usage page, read through the platform routes. */
function PropertyUsage({ propertyId }) {
  const { call } = usePlatformAuth();
  const source = useMemo(() => platformUsageSource(call, propertyId), [call, propertyId]);
  return <UsageDashboard load={source.load} exportFile={source.exportFile} />;
}

/** The Branding tab: the property console's Branding editor, through the platform routes. */
function PropertyBranding({ propertyId }) {
  const { call } = usePlatformAuth();
  const load = useCallback((signal) => call((client) => client.getPlatformPropertyBranding(propertyId, signal)), [call, propertyId]);
  const save = useCallback((body) => call((client) => client.updatePlatformPropertyBranding(propertyId, body)), [call, propertyId]);
  const revert = useCallback((to) => call((client) => client.revertPlatformPropertyBranding(propertyId, to)), [call, propertyId]);
  const listVersions = useCallback((query, signal) => call((client) => client.enumeratePlatformPropertyBrandingVersions(propertyId, query, signal)), [call, propertyId]);
  const restoreVersion = useCallback((version) => call((client) => client.restorePlatformPropertyBrandingVersion(propertyId, version)), [call, propertyId]);
  const saveMemberPresets = useCallback((body) => call((client) => client.updatePlatformPropertyMemberPresets(propertyId, body)), [call, propertyId]);
  return <BrandingEditor load={load} save={save} revert={revert} listVersions={listVersions} restoreVersion={restoreVersion} saveMemberPresets={saveMemberPresets} />;
}

/** The Documents tab: the property console's documents manager, through the platform routes. */
function PropertyDocuments({ propertyId }) {
  const { call, client } = usePlatformAuth();
  const source = useMemo(() => ({
    list: (query, signal) => call((cl) => cl.platformListDocuments(propertyId, query, signal)),
    get: (key, signal) => call((cl) => cl.platformGetDocument(propertyId, key, signal)),
    enumerateVersions: (key, query, signal) => call((cl) => cl.platformEnumerateDocumentVersions(propertyId, key, query, signal)),
    getVersion: (key, versionId, signal) => call((cl) => cl.platformGetDocumentVersion(propertyId, key, versionId, signal)),
    publish: (key, body) => call((cl) => cl.platformPublishDocumentVersion(propertyId, key, body)),
    enumerateAcceptances: (query, signal) => call((cl) => cl.platformEnumerateDocumentAcceptances(propertyId, query, signal)),
    metadataClient: platformMetadataSource(client),
    publicLinks: false
  }), [call, client, propertyId]);
  return <DocumentsManager source={source} />;
}

/** The AI tab: the property's AI switch, through the platform routes. */
function PropertyAi({ propertyId }) {
  const { call } = usePlatformAuth();
  const load = useCallback((signal) => call((client) => client.getPlatformPropertyAi(propertyId, signal)), [call, propertyId]);
  const save = useCallback((enabled) => call((client) => client.updatePlatformPropertyAi(propertyId, enabled)), [call, propertyId]);
  return <AiSwitchPanel load={load} save={save} />;
}

/** Property detail: overview and lifecycle, usage, domains and apps (with verification state), branding, the AI switch, integration settings, property admins, audit, and the danger zone. */
function PropertyDetailView() {
  const { id } = useParams();
  const { call } = usePlatformAuth();
  const [searchParams, setSearchParams] = useSearchParams();
  const tab = TABS.includes(searchParams.get('tab')) ? searchParams.get('tab') : 'overview';
  const [override, setOverride] = useState(null);
  const fetcher = useCallback((signal) => call((client) => client.getProperty(id, signal)), [call, id]);
  const query = useApiQuery(fetcher);
  const record = override && override.id === id && (!query.loadedAt || override.at >= query.loadedAt.getTime()) ? override.record : query.data;
  const onChange = useCallback((next) => setOverride({ id, record: next, at: Date.now() }), [id]);
  const del = useMemo(() => propertyDeletionState(record), [record]);

  const setTab = (next) => {
    const params = new URLSearchParams(searchParams);
    // Usage period parameters belong to the Usage tab only.
    if (next !== 'usage') for (const key of ['period', 'from', 'to', 'bucket']) params.delete(key);
    if (next === 'overview') params.delete('tab');
    else params.set('tab', next);
    setSearchParams(params, { replace: true });
  };

  if (query.loading && !record) return <div className="view"><Loading /></div>;
  if (!record) {
    return (
      <div className="view">
        <Link to="/platform/properties" className="small">{d.back}</Link>
        <ErrorBanner message={query.error && query.error.status === 404 ? d.notFound : d.loadError + ' ' + errorMessage(query.error, '')} onRetry={query.reload} />
      </div>
    );
  }

  return (
    <div className="view">
      <Link to="/platform/properties" className="small"><Icon name="reply" size={12} /> {d.back}</Link>
      <PageHeader title={record.name} summary={record.id}>
        <PropertyStatusBadge record={record} />
      </PageHeader>
      {del.restorable && (
        <Banner tone="danger" icon="alert" actions={<button type="button" className="button button-secondary button-sm" onClick={() => setTab('danger')}>{d.restore}</button>}>
          {d.deletingBanner(formatDateTime(del.purgeAtUtc), del.daysLeft)}
        </Banner>
      )}
      {del.purging && <Banner tone="danger" icon="alert">{d.purgingBanner}</Banner>}
      {del.deleted && <Banner tone="info" icon="info">{d.deletedBanner}</Banner>}

      <div className="tabs" role="tablist" aria-label={record.name}>
        {TABS.map((t) => (
          <button key={t} type="button" role="tab" id={'tab-' + t} aria-selected={tab === t} aria-controls={'panel-' + t} className={'tab' + (tab === t ? ' active' : '')} onClick={() => setTab(t)}>
            {d.tabs[t]}
          </button>
        ))}
      </div>
      <div role="tabpanel" id={'panel-' + tab} aria-labelledby={'tab-' + tab}>
        {tab === 'overview' && <Overview key={record.updatedUtc} record={record} onChange={onChange} />}
        {tab === 'usage' && <PropertyUsage key={record.id} propertyId={record.id} />}
        {tab === 'domains' && (
          <>
            <section className="panel">
              <DomainsAppsEditor key={record.id} record={record} disabled={del.deleting || del.deleted} onSave={(body) => call((client) => client.updateProperty(record.id, body))} onSaved={onChange} />
            </section>
            <DomainVerificationPanel record={record} />
          </>
        )}
        {tab === 'branding' && <PropertyBranding key={record.id} propertyId={record.id} />}
        {tab === 'documents' && <PropertyDocuments key={record.id} propertyId={record.id} />}
        {tab === 'ai' && <PropertyAi key={record.id} propertyId={record.id} />}
        {tab === 'integrations' && <PropertyIntegrationsPanel key={record.id} record={record} />}
        {tab === 'admins' && <PropertyAdmins record={record} />}
        {tab === 'audit' && <PlatformAuditView propertyId={record.id} embedded />}
        {tab === 'danger' && <DangerZone record={record} onChange={onChange} />}
      </div>
    </div>
  );
}

export default PropertyDetailView;

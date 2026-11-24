import { useCallback, useMemo, useRef, useState } from 'react';
import { REMOTE_ASSET_CONTENT_TYPES, REMOTE_ASSET_FILTER_KEYS } from '@trelliskit/shared';
import strings from '../../strings.js';
import { useAdminAuth } from '../../context/AdminAuthContext.jsx';
import { useToast } from '../../context/ToastContext.jsx';
import useApiQuery from '../../hooks/useApiQuery.js';
import useTableState from '../../hooks/useTableState.js';
import { errorMessage } from '../../utils/api.js';
import { normalizePage } from '../../utils/query.js';
import { ADMIN_PAGE_SIZES } from '../../utils/adminQuery.js';
import { formatDateTime } from '../../utils/format.js';
import PageHeader from '../../components/PageHeader.jsx';
import AdminFilterPanel from '../../components/AdminFilterPanel.jsx';
import DataTable from '../../components/DataTable.jsx';
import TablePagination from '../../components/TablePagination.jsx';
import ActionMenu from '../../components/ActionMenu.jsx';
import ConfirmModal from '../../components/ConfirmModal.jsx';
import Modal from '../../components/Modal.jsx';
import Icon from '../../components/Icon.jsx';
import { Badge } from '../../components/Badges.jsx';
import { EmptyState, ErrorBanner, Loading } from '../../components/States.jsx';
import ContentPublishPanel from './ContentPublishPanel.jsx';
import { FormDialog, formatBytes } from './LiveUpdateDialogs.jsx';
import { contentQuery, contentStateTone } from './appContentAdmin.js';
import '../../components/appContent.css';

const u = strings.admin.liveUpdates;
const ac = strings.appContent.admin;
const as = ac.assets;
const FILTERS = Object.freeze(['search', ...REMOTE_ASSET_FILTER_KEYS]);
const ASSET_KEY = /^[a-z][a-zA-Z0-9]*(\.[a-z][a-zA-Z0-9]*){0,3}$/;

/** POST /admin/app/assets/enumerate query; `hasDraftChanges` is a boolean. */
export function buildAssetsQuery(filters, paging) {
  return contentQuery(filters, FILTERS, paging, { ordering: 'KeyAscending', booleans: ['hasDraftChanges'] });
}

/** The friendly problem of an asset key, or null when valid. */
export function assetKeyProblem(key) {
  const value = String(key || '').trim();
  return value.length >= 1 && value.length <= 64 && ASSET_KEY.test(value) ? null : as.keyInvalid;
}

/** The friendly problem of a file chosen for upload, or null. */
export function assetFileProblem(file) {
  if (!file) return as.noFile;
  if (!REMOTE_ASSET_CONTENT_TYPES.includes(file.type)) return as.wrongType;
  return null;
}

function AssetCard({ asset, onAction }) {
  return (
    <article className="tk-asset-card" aria-label={asset.key}>
      <div className="tk-asset-thumbs">
        <div className="tk-asset-thumb">{asset.url ? <img src={asset.url} alt={asset.altText || ''} loading="lazy" /> : <span className="muted small">{as.noContent}</span>}</div>
        {asset.darkVariant && asset.darkVariant.url && <div className="tk-asset-thumb tk-asset-thumb-dark"><img src={asset.darkVariant.url} alt={as.darkAlt(asset.altText || asset.key)} loading="lazy" /></div>}
      </div>
      <code className="mono">{asset.key}</code>
      <span className="muted small">{asset.altText || as.noAlt}</span>
      <span className="muted small">{asset.width && asset.height ? as.dimensions(asset.width, asset.height, formatBytes(asset.sizeBytes)) : as.noContent}</span>
      <span className="nowrap">
        <Badge tone={contentStateTone(asset.state)}>{u.flags.states[asset.state] || asset.state}</Badge>
        {asset.hasDraftChanges && <> <Badge tone="warning">{u.flags.draft}</Badge></>}
        {asset.darkVariant && <> <Badge>{as.dark}</Badge></>}
      </span>
      <div className="tk-asset-actions">
        <button type="button" className="button button-secondary button-sm" onClick={() => onAction('upload', asset)}><Icon name="upload" size={14} /><span>{as.upload}</span></button>
        <ActionMenu
          label={strings.admin.common.rowActions(asset.key)}
          items={[
            { label: as.uploadDark, icon: 'upload', onSelect: () => onAction('uploadDark', asset) },
            { label: as.editAlt, icon: 'edit', onSelect: () => onAction('edit', asset) },
            { label: ac.history, icon: 'clock', onSelect: () => onAction('history', asset) },
            ...(asset.publishedInVersion ? [asset.state === 'Archived'
              ? { label: u.flags.restore, icon: 'refresh', onSelect: () => onAction('restore', asset) }
              : { label: u.flags.archive, icon: 'trash', onSelect: () => onAction('archive', asset) }] : []),
            ...(!asset.publishedInVersion ? [{ label: u.flags.remove, icon: 'trash', danger: true, onSelect: () => onAction('delete', asset) }] : [])
          ]}
        />
      </div>
    </article>
  );
}

function AssetHistory({ client, asset, onClose }) {
  const fetcher = useCallback((signal) => client.enumerateRemoteAssetHistory(asset.id, { maxResults: 50 }, signal), [client, asset.id]);
  const history = useApiQuery(fetcher);
  const rows = history.data ? normalizePage(history.data).items.map((row, i) => ({ ...row, rowIndex: i })) : [];
  const columns = [
    { key: 'version', label: u.flags.version, render: (row) => row.publishedInVersion ?? '-' },
    { key: 'hash', label: as.hash, render: (row) => <code className="mono">{row.sha256 ? row.sha256.slice(0, 12) : '-'}</code> },
    { key: 'alt', label: as.altText, render: (row) => row.altText || '' },
    { key: 'state', label: u.flags.state, render: (row) => u.flags.states[row.state] || row.state },
    { key: 'when', label: ac.when, render: (row) => formatDateTime(row.updatedUtc) }
  ];
  return (
    <Modal open size="lg" onClose={onClose} title={ac.history + ': ' + asset.key}>
      <DataTable caption={ac.history} columns={columns} rows={rows} rowKey={(row) => String(row.rowIndex)} loading={history.loading} error={history.error ? errorMessage(history.error, '') : null} onRetry={history.reload} emptyMessage={ac.noHistory} />
    </Modal>
  );
}

/**
 * Remote assets (docs/LIVE_UPDATES_CONTRACT.md section 14.2): a grid of the images the apps load by
 * key, with state, draft, label, and tag filters; create (key, alt text), content and dark variant
 * upload (PNG, JPEG, or WebP; the server re-encodes to WebP without metadata), alt text edits,
 * archive, restore, delete, history, and the pending content drafts with Publish.
 */
function AssetsView() {
  const { client } = useAdminAuth();
  const toast = useToast();
  const table = useTableState({ storageKey: 'tk.admin.assets', filterKeys: FILTERS, defaultSort: '' });
  const [dialog, setDialog] = useState(null);
  const [uploading, setUploading] = useState(null);
  const fileRef = useRef(null);
  const query = useMemo(() => buildAssetsQuery(table.filters, { pageNumber: table.pageNumber, pageSize: table.pageSize, meta: table.meta }), [table.filters, table.pageNumber, table.pageSize, table.meta]);
  const fetcher = useCallback((signal) => client.enumerateRemoteAssets(query, signal), [client, query]);
  const list = useApiQuery(fetcher);
  const draftsFetcher = useCallback((signal) => client.getContentDrafts({ maxResults: 1 }, signal), [client]);
  const drafts = useApiQuery(draftsFetcher);
  const page = list.data ? normalizePage(list.data, table.pageSize) : null;
  const route = strings.appContent.routes.assets;

  const reloadAll = () => {
    list.reload();
    drafts.reload();
  };
  const run = async (fn, message) => {
    try {
      await fn();
      toast.success(message);
      reloadAll();
    } catch (err) {
      toast.error(errorMessage(err, strings.common.errorGeneric));
    }
  };

  const onAction = async (type, asset) => {
    if (type === 'upload' || type === 'uploadDark') {
      setUploading({ asset, dark: type === 'uploadDark' });
      if (fileRef.current) {
        fileRef.current.value = '';
        fileRef.current.click();
      }
      return;
    }
    if (type === 'archive') return run(() => client.archiveRemoteAsset(asset.id), u.flags.archived);
    if (type === 'restore') return run(() => client.restoreRemoteAsset(asset.id), u.flags.restored);
    if (type === 'edit') {
      try {
        const detail = await client.getRemoteAsset(asset.id);
        setDialog({ type: 'edit', asset: detail.asset, etag: detail.asset.eTag });
      } catch (err) {
        toast.error(errorMessage(err, strings.common.errorGeneric));
      }
      return undefined;
    }
    setDialog({ type, asset });
    return undefined;
  };

  const onFile = async (e) => {
    const file = e.target.files && e.target.files[0];
    const target = uploading;
    if (!target || !file) return;
    const problem = assetFileProblem(file);
    if (problem) {
      toast.error(problem);
      return;
    }
    await run(() => client.uploadRemoteAssetContent(target.asset.id, file, file.type, { dark: target.dark }), target.dark ? as.darkUploaded : as.uploaded);
    setUploading(null);
  };

  const yesNo = [{ value: 'true', label: u.yes }, { value: 'false', label: u.no }];
  const filterFields = [
    { key: 'search', label: as.searchLabel, placeholder: as.searchPlaceholder, grow: true },
    { key: 'state', label: u.flags.state, type: 'select', options: Object.entries(u.flags.states).map(([value, label]) => ({ value, label })) },
    { key: 'hasDraftChanges', label: u.flags.draft, type: 'select', options: yesNo }
  ];
  const items = page ? page.items : [];

  return (
    <div className="view">
      <PageHeader
        title={route.label}
        summary={route.summary}
        actions={<button type="button" className="button button-primary button-sm" onClick={() => setDialog({ type: 'create' })}><Icon name="plus" size={14} /><span>{as.create}</span></button>}
      />
      <ContentPublishPanel client={client} drafts={drafts} onChanged={list.reload} />
      <section className="panel">
        <AdminFilterPanel fields={filterFields} table={table} client={client} targetType="RemoteAsset" />
      </section>
      <section className="table-section" aria-label={route.label}>
        <TablePagination pageNumber={table.pageNumber} pageSize={table.pageSize} totalCount={page ? page.totalCount : 0} totalPages={page ? page.totalPages : 1} onPageChange={table.setPage} onPageSizeChange={table.setPageSize} onRefresh={list.reload} loading={list.loading} pageSizes={ADMIN_PAGE_SIZES} />
        {list.error && <ErrorBanner message={errorMessage(list.error, strings.common.errorGeneric)} onRetry={list.reload} />}
        {list.loading && !page ? <Loading /> : items.length === 0 ? <EmptyState icon="image" title={table.hasFilters ? as.emptyFiltered : as.empty} /> : (
          <div className="tk-asset-grid">
            {items.map((asset) => <AssetCard key={asset.id} asset={asset} onAction={onAction} />)}
          </div>
        )}
        <input ref={fileRef} type="file" accept={REMOTE_ASSET_CONTENT_TYPES.join(',')} className="visually-hidden" aria-label={uploading && uploading.dark ? as.uploadDark : as.upload} data-testid="asset-file" onChange={onFile} tabIndex={-1} />
      </section>
      {dialog && dialog.type === 'create' && (
        <FormDialog
          open
          title={as.createTitle}
          submitLabel={ac.saveDraft}
          fields={[
            { key: 'key', label: as.key, required: true, maxLength: 64, help: as.keyHint },
            { key: 'altText', label: as.altText, maxLength: 300 }
          ]}
          onClose={() => setDialog(null)}
          onSubmit={async (v) => {
            const problem = assetKeyProblem(v.key);
            if (problem) throw Object.assign(new Error(problem), { body: { fieldErrors: { key: problem } } });
            await client.createRemoteAsset({ key: v.key.trim(), altText: v.altText.trim() || undefined });
            setDialog(null);
            toast.success(as.created);
            reloadAll();
          }}
        />
      )}
      {dialog && dialog.type === 'edit' && (
        <FormDialog
          open
          title={as.editTitle(dialog.asset.key)}
          submitLabel={ac.saveDraft}
          fields={[{ key: 'altText', label: as.altText, maxLength: 300 }]}
          initial={{ altText: dialog.asset.altText || '' }}
          onClose={() => setDialog(null)}
          onSubmit={async (v) => {
            await client.updateRemoteAsset(dialog.asset.id, { altText: v.altText.trim() }, dialog.etag);
            setDialog(null);
            toast.success(as.saved);
            reloadAll();
          }}
        />
      )}
      {dialog && dialog.type === 'history' && <AssetHistory client={client} asset={dialog.asset} onClose={() => setDialog(null)} />}
      <ConfirmModal
        open={Boolean(dialog && dialog.type === 'delete')}
        title={dialog && dialog.asset ? as.deleteTitle(dialog.asset.key) : ''}
        message={as.deleteBody}
        confirmLabel={u.flags.remove}
        onConfirm={() => { const asset = dialog.asset; setDialog(null); run(() => client.deleteRemoteAsset(asset.id), as.deleted); }}
        onCancel={() => setDialog(null)}
      />
    </div>
  );
}

export default AssetsView;

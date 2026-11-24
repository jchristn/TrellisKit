import { useCallback, useMemo, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import strings from '../../strings.js';
import { useAdminAuth } from '../../context/AdminAuthContext.jsx';
import { useToast } from '../../context/ToastContext.jsx';
import useApiQuery from '../../hooks/useApiQuery.js';
import useLocalStorage from '../../hooks/useLocalStorage.js';
import useTableState from '../../hooks/useTableState.js';
import { errorMessage } from '../../utils/api.js';
import { normalizePage } from '../../utils/query.js';
import { ADMIN_PAGE_SIZES, MEDIA_DEFAULT_SORT, MEDIA_FILTERS, buildMediaQuery } from '../../utils/adminQuery.js';
import { optionsFrom } from '../../utils/labels.js';
import { formatBytes, formatDateTime, formatNumber, formatShortDateTime } from '../../utils/format.js';
import { authorName, postPath } from '../../utils/engagement.js';
import PageHeader from '../../components/PageHeader.jsx';
import KpiCard from '../../components/KpiCard.jsx';
import AdminFilterPanel from '../../components/AdminFilterPanel.jsx';
import BulkMetadataDialog from '../../components/BulkMetadataDialog.jsx';
import BulkBar from '../../components/BulkBar.jsx';
import useRowSelection from '../../hooks/useRowSelection.jsx';
import MetadataPanel from '../../components/MetadataPanel.jsx';
import DataTable from '../../components/DataTable.jsx';
import TablePagination from '../../components/TablePagination.jsx';
import ActionMenu from '../../components/ActionMenu.jsx';
import ConfirmModal from '../../components/ConfirmModal.jsx';
import JsonViewerModal from '../../components/JsonViewerModal.jsx';
import CopyableId from '../../components/CopyableId.jsx';
import Modal from '../../components/Modal.jsx';
import Icon from '../../components/Icon.jsx';
import { Badge } from '../../components/Badges.jsx';
import { Banner, ErrorBanner, Loading } from '../../components/States.jsx';
import ResponsiveImage from '../../components/feed/ResponsiveImage.jsx';
import VideoPlayer from '../../components/feed/VideoPlayer.jsx';
import TableColumns from '../../components/TableColumns.jsx';

const s = strings.admin.mediaLibrary;
const c = strings.admin.common;
const col = s.columns;
const STATUS_TONES = { Uploading: 'info', Processing: 'warning', Ready: 'success', Failed: 'danger' };

function Thumb({ media }) {
  if (media.status !== 'Ready') return <span className="media-thumb-placeholder"><Icon name={media.kind === 'Video' ? 'video' : 'image'} size={18} /></span>;
  if (media.kind === 'Image') return <ResponsiveImage media={media} prefer="thumb" sizes="160px" className="media-thumb" />;
  const poster = media.video && (media.video.posterThumbUrl || media.video.posterUrl);
  return poster ? <img className="media-thumb" src={poster} alt="" loading="lazy" /> : <span className="media-thumb-placeholder"><Icon name="video" size={18} /></span>;
}

function targetLink(attachedTo) {
  if (!attachedTo) return null;
  if (attachedTo.targetType === 'FeedPost') return postPath(attachedTo.targetId);
  return null;
}

function AltDialog({ media, onClose, onSaved }) {
  const { api } = useAdminAuth();
  const [alt, setAlt] = useState(media.altText || '');
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const save = async (e) => {
    e.preventDefault();
    if (alt.length > 1000) { setError(strings.composer.altTooLong); return; }
    setBusy(true);
    try {
      await api.updateAdminMedia(media.id, { altText: alt });
      onSaved();
    } catch (err) {
      setError(errorMessage(err, strings.common.errorGeneric));
      setBusy(false);
    }
  };
  return (
    <Modal open size="sm" onClose={onClose} title={s.altTitle} footer={<><button type="button" className="button button-secondary" onClick={onClose} disabled={busy}>{strings.common.cancel}</button><button type="submit" form="alt-form" className="button button-primary" disabled={busy}>{strings.common.save}</button></>}>
      <form id="alt-form" onSubmit={save} className="stack-form">
        <ErrorBanner message={error} />
        <label className="field"><span className="label-text">{s.altTitle}</span><textarea rows={3} value={alt} onChange={(e) => setAlt(e.target.value)} maxLength={1100} /></label>
      </form>
    </Modal>
  );
}

function MediaDetail({ id, onClose, onAction }) {
  const { api, client } = useAdminAuth();
  const fetcher = useCallback((signal) => api.getAdminMedia(id, signal), [api, id]);
  const query = useApiQuery(fetcher);
  const [jsonOpen, setJsonOpen] = useState(false);
  const item = query.data;
  const media = item && (item.media || item);
  const variants = item ? (Array.isArray(item.variants) ? item.variants : (item.variants && item.variants.objects) || []) : [];
  return (
    <Modal open variant="drawer" size="lg" onClose={onClose} title={s.detailTitle} subtitle={<CopyableId value={id} label={col.id} />}
      headerExtra={item && <button type="button" className="button button-secondary button-sm" onClick={() => setJsonOpen(true)}><Icon name="code" size={14} /><span>{c.viewJson}</span></button>}
    >
      {query.loading && !item && <Loading />}
      {query.error && <ErrorBanner message={errorMessage(query.error, '')} onRetry={query.reload} />}
      {media && (
        <div className="detail-stack">
          <section className="detail-block media-detail-preview">
            {media.status === 'Ready' && media.kind === 'Image' && <ResponsiveImage media={media} prefer="large" sizes="(max-width: 900px) 100vw, 720px" />}
            {media.status === 'Ready' && media.kind === 'Video' && <VideoPlayer media={media} />}
            {media.status !== 'Ready' && <Badge tone={STATUS_TONES[media.status]}>{s.statuses[media.status] || media.status}</Badge>}
          </section>
          <section className="detail-block">
            <dl className="kv-grid">
              <div className="kv-item"><dt>{col.kind}</dt><dd>{s.kinds[media.kind] || media.kind}</dd></div>
              <div className="kv-item"><dt>{col.status}</dt><dd><Badge tone={STATUS_TONES[media.status]}>{s.statuses[media.status] || media.status}</Badge></dd></div>
              <div className="kv-item"><dt>{s.fields.contentType}</dt><dd className="mono">{media.contentType}</dd></div>
              <div className="kv-item"><dt>{col.size}</dt><dd>{formatBytes(item.storedBytes || media.sizeBytes)}</dd></div>
              <div className="kv-item"><dt>{s.fields.dimensions}</dt><dd>{media.width && media.height ? media.width + ' x ' + media.height : '-'}</dd></div>
              {media.durationSeconds && <div className="kv-item"><dt>{s.fields.duration}</dt><dd>{Math.round(media.durationSeconds)} s</dd></div>}
              <div className="kv-item"><dt>{col.owner}</dt><dd>{authorName(item.owner)}</dd></div>
              <div className="kv-item"><dt>{col.fileName}</dt><dd className="truncate">{item.fileName || '-'}</dd></div>
              <div className="kv-item"><dt>{s.fields.processor}</dt><dd>{item.processor || '-'}</dd></div>
              <div className="kv-item"><dt>{s.fields.attempts}</dt><dd>{item.attempts ?? '-'}</dd></div>
              {item.processorJobId && <div className="kv-item"><dt>{s.fields.job}</dt><dd className="mono">{item.processorJobId}</dd></div>}
              {media.failureReason && <div className="kv-item"><dt>{s.fields.failure}</dt><dd>{strings.composer.failureReasons[media.failureReason] || media.failureReason}</dd></div>}
              <div className="kv-item"><dt>{s.fields.ready}</dt><dd>{media.readyUtc ? formatDateTime(media.readyUtc) : '-'}</dd></div>
              <div className="kv-item"><dt>{s.fields.alt}</dt><dd>{media.altText || '-'}</dd></div>
            </dl>
            <div className="row-actions">
              <button type="button" className="button button-secondary button-sm" onClick={() => onAction('alt', item)}>{s.editAlt}</button>
              {media.status === 'Failed' && <button type="button" className="button button-secondary button-sm" onClick={() => onAction('reprocess', item)}>{s.reprocess}</button>}
              <button type="button" className="button button-danger-outline button-sm" onClick={() => onAction('delete', item)}>{strings.common.delete}</button>
            </div>
          </section>
          {variants.length > 0 && (
            <section className="detail-block">
              <h3>{s.variantsTitle}</h3>
              <TableColumns id="tk.admin.MediaLibraryView"><table className="data-table"><caption className="visually-hidden">{s.variantsTitle}</caption>
                <thead><tr>{['name', 'format', 'size', 'bytes'].map((k) => <th key={k} scope="col">{s.variantColumns[k]}</th>)}</tr></thead>
                <tbody>{variants.map((v) => <tr key={v.name + v.format}><td>{v.name}</td><td>{v.format}</td><td>{v.width} x {v.height}</td><td>{formatBytes(v.sizeBytes)}</td></tr>)}</tbody>
              </table></TableColumns>
            </section>
          )}
          <MetadataPanel client={client} targetType="Media" targetId={media.id} isStaff initial={media.labels ? media : null} />
        </div>
      )}
      <JsonViewerModal open={jsonOpen} onClose={() => setJsonOpen(false)} subtitle={id} value={item} />
    </Modal>
  );
}

/**
 * /admin/media (contract section 10.3): summary tiles, filters by kind, status, owner, attachment,
 * and time, label and tag filters, a grid or table layout with thumbnails, and a detail drawer
 * with playback and the actions View, View JSON, Edit alt text, Reprocess, Open target, Delete.
 */
function MediaLibraryView() {
  const { api, client, mode } = useAdminAuth();
  const toast = useToast();
  const [searchParams, setSearchParams] = useSearchParams();
  const table = useTableState({ storageKey: 'tk.admin.media', filterKeys: MEDIA_FILTERS, defaultSort: MEDIA_DEFAULT_SORT });
  const [layout, setLayout] = useLocalStorage('tk.admin.media.layout', 'grid');
  const [dialog, setDialog] = useState(null);
  const [jsonRow, setJsonRow] = useState(null);
  const [busy, setBusy] = useState(false);
  const detailId = searchParams.get('media');

  const query = useMemo(() => buildMediaQuery(table.filters, { pageNumber: table.pageNumber, pageSize: table.pageSize, sort: table.sort, meta: table.meta }), [table.filters, table.pageNumber, table.pageSize, table.sort, table.meta]);
  const fetcher = useCallback((signal) => api.listAdminMedia(query, signal), [api, query]);
  const list = useApiQuery(fetcher);
  const summaryFetcher = useCallback((signal) => api.getMediaSummary(signal), [api]);
  const summary = useApiQuery(summaryFetcher);
  const page = list.data ? normalizePage(list.data, table.pageSize) : null;
  const rows = page ? page.items : [];
  const selection = useRowSelection(rows, JSON.stringify(query), (row) => (row.media || row).id);
  const [metaIds, setMetaIds] = useState(null);
  const sum = summary.data;

  const open = (id) => {
    const next = new URLSearchParams(searchParams);
    if (id) next.set('media', id);
    else next.delete('media');
    setSearchParams(next);
  };
  const act = (kind, item) => setDialog({ kind, item });
  const finish = (message) => {
    toast.success(message);
    setDialog(null);
    list.reload();
    summary.reload();
  };
  const run = async (fn, message) => {
    setBusy(true);
    try {
      await fn();
      finish(message);
      if (dialog && dialog.kind === 'delete') open(null);
    } catch (err) {
      toast.error(errorMessage(err, strings.common.errorGeneric));
    } finally {
      setBusy(false);
    }
  };

  const mediaOf = (row) => row.media || row;
  const menu = (row) => {
    const m = mediaOf(row);
    const items = [
      { label: c.view, icon: 'eye', onSelect: () => open(m.id) },
      { label: c.viewJson, icon: 'code', onSelect: () => setJsonRow(row) },
      { label: s.editAlt, icon: 'edit', onSelect: () => act('alt', row) }
    ];
    if (m.status === 'Failed') items.push({ label: s.reprocess, icon: 'refresh', onSelect: () => act('reprocess', row) });
    const link = targetLink(row.attachedTo);
    if (link) items.push({ label: s.openTarget, icon: 'external', onSelect: () => window.open(link, '_blank', 'noopener') });
    items.push({ label: strings.common.delete, icon: 'trash', danger: true, onSelect: () => act('delete', row) });
    return <ActionMenu label={c.rowActions(m.id)} items={items} />;
  };

  const columns = [
    selection.column((row) => row.fileName || mediaOf(row).id),
    { key: 'preview', label: col.preview, render: (row) => <Thumb media={mediaOf(row)} /> },
    { key: 'kind', label: col.kind, render: (row) => s.kinds[mediaOf(row).kind] || mediaOf(row).kind },
    { key: 'status', label: col.status, render: (row) => <Badge tone={STATUS_TONES[mediaOf(row).status]}>{s.statuses[mediaOf(row).status] || mediaOf(row).status}</Badge> },
    { key: 'owner', label: col.owner, render: (row) => authorName(row.owner) },
    { key: 'attachedTo', label: col.attachedTo, render: (row) => (row.attachedTo ? <span className="cell-stack"><span>{strings.admin.moderation.targetTypes[row.attachedTo.targetType] || row.attachedTo.targetType}</span><code className="mono small">{row.attachedTo.targetId}</code></span> : <span className="muted">{s.unattached}</span>) },
    { key: 'size', label: col.size, sortField: 'sizeBytes', sortDefault: 'desc', className: 'num', render: (row) => formatBytes(row.storedBytes || mediaOf(row).sizeBytes) },
    { key: 'created', label: col.created, sortField: 'createdUtc', sortDefault: 'desc', className: 'nowrap', render: (row) => formatShortDateTime(row.createdUtc || mediaOf(row).createdUtc) },
    { key: 'actions', className: 'cell-actions', label: <span className="visually-hidden">{col.actions}</span>, render: menu }
  ];

  const fields = [
    { key: 'kind', label: s.kind, type: 'select', options: optionsFrom(s.kinds) },
    { key: 'status', label: s.status, type: 'select', options: optionsFrom(s.statuses) },
    { key: 'attached', label: s.attached, type: 'select', options: optionsFrom(s.attachedOptions) },
    { key: 'ownerUserId', label: s.owner, placeholder: 'usr_...', mono: true },
    { key: 'fromUtc', label: s.from, type: 'datetime' },
    { key: 'toUtc', label: s.to, type: 'datetime' }
  ];

  return (
    <div className="view">
      <PageHeader title={strings.admin.routes.media.label} summary={strings.admin.routes.media.summary} />
      {mode === 'token' && <Banner tone="info" icon="info">{s.grantNote}</Banner>}
      {summary.error ? <ErrorBanner message={s.summaryError} onRetry={summary.reload} /> : (
        <div className="kpi-grid">
          <KpiCard label={s.summary.total} value={sum ? formatNumber(sum.totalCount) : '-'} />
          <KpiCard label={s.summary.stored} value={sum ? formatBytes(sum.totalStoredBytes) : '-'} />
          <KpiCard label={s.summary.images} value={sum && sum.byKind ? formatNumber(sum.byKind.Image || 0) : '-'} />
          <KpiCard label={s.summary.videos} value={sum && sum.byKind ? formatNumber(sum.byKind.Video || 0) : '-'} />
          <KpiCard label={s.summary.processing} value={sum && sum.byStatus ? formatNumber(sum.byStatus.Processing || 0) : '-'} />
          <KpiCard label={s.summary.failed24h} value={sum ? formatNumber(sum.failed24h || 0) : '-'} tone={sum && sum.failed24h ? 'danger' : undefined} />
        </div>
      )}
      <section className="panel">
        <AdminFilterPanel fields={fields} table={table} client={client} targetType="Media" />
      </section>
      <section className="table-section">
        <div className="table-toolbar">
          <div className="toolbar-left">
            <div className="segmented" role="radiogroup" aria-label={s.viewLabel}>
              <button type="button" role="radio" aria-checked={layout === 'grid'} className={'segment' + (layout === 'grid' ? ' active' : '')} onClick={() => setLayout('grid')}><Icon name="grid" size={12} /> {s.grid}</button>
              <button type="button" role="radio" aria-checked={layout === 'table'} className={'segment' + (layout === 'table' ? ' active' : '')} onClick={() => setLayout('table')}><Icon name="list" size={12} /> {s.table}</button>
            </div>
            <BulkBar count={selection.selected.length} onClear={selection.clear} onMetadata={() => setMetaIds(selection.selected)} />
          </div>
        </div>
        <TablePagination pageNumber={table.pageNumber} pageSize={table.pageSize} totalCount={page ? page.totalCount : 0} totalPages={page ? page.totalPages : 1} onPageChange={table.setPage} onPageSizeChange={table.setPageSize} onRefresh={list.reload} loading={list.loading || busy} pageSizes={ADMIN_PAGE_SIZES} />
        {layout === 'table' ? (
          <DataTable caption={strings.admin.routes.media.label} columns={columns} rows={rows} rowKey={(row) => mediaOf(row).id} loading={list.loading} error={list.error ? c.unavailable(strings.admin.routes.media.label) + ' ' + errorMessage(list.error, '') : null} onRetry={list.reload} emptyMessage={table.hasFilters ? s.emptyFiltered : s.empty} onRowClick={(row) => open(mediaOf(row).id)} sort={table.sort} onSortChange={table.setSort} />
        ) : (
          <div className="table-frame">
            {list.error && <ErrorBanner message={c.unavailable(strings.admin.routes.media.label) + ' ' + errorMessage(list.error, '')} onRetry={list.reload} />}
            {list.loading && !rows.length && <Loading />}
            {!list.loading && !rows.length && !list.error && <p className="table-empty">{table.hasFilters ? s.emptyFiltered : s.empty}</p>}
            <ul className="media-grid">
              {rows.map((row) => {
                const m = mediaOf(row);
                return (
                  <li key={m.id} className="media-grid-item">
                    <button type="button" className="media-grid-open" onClick={() => open(m.id)} aria-label={c.view + ': ' + (row.fileName || m.id)}><Thumb media={m} /></button>
                    <div className="media-grid-meta">
                      <Badge tone={STATUS_TONES[m.status]}>{s.statuses[m.status] || m.status}</Badge>
                      <span className="muted small truncate">{formatBytes(row.storedBytes || m.sizeBytes)}</span>
                      {menu(row)}
                    </div>
                  </li>
                );
              })}
            </ul>
          </div>
        )}
      </section>
      {detailId && <MediaDetail key={detailId} id={detailId} onClose={() => open(null)} onAction={act} />}
      {dialog && dialog.kind === 'alt' && <AltDialog media={mediaOf(dialog.item)} onClose={() => setDialog(null)} onSaved={() => finish(s.altSaved)} />}
      <ConfirmModal open={Boolean(dialog && dialog.kind === 'reprocess')} title={s.reprocess} message={s.reprocess + '?'} confirmLabel={s.reprocess} danger={false} busy={busy} onCancel={() => setDialog(null)} onConfirm={() => run(() => api.reprocessMedia(mediaOf(dialog.item).id), s.reprocessed)} />
      <ConfirmModal open={Boolean(dialog && dialog.kind === 'delete')} title={s.deleteTitle} message={s.deleteBody} confirmLabel={strings.common.delete} busy={busy} onCancel={() => setDialog(null)} onConfirm={() => run(() => api.deleteAdminMedia(mediaOf(dialog.item).id), s.deleted)} />
      {metaIds && <BulkMetadataDialog open targetType="Media" ids={metaIds} onClose={() => setMetaIds(null)} onDone={() => { selection.clear(); list.reload(); }} />}
      <JsonViewerModal open={Boolean(jsonRow)} onClose={() => setJsonRow(null)} subtitle={jsonRow && mediaOf(jsonRow).id} value={jsonRow} />
    </div>
  );
}

export default MediaLibraryView;

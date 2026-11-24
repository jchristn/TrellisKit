import { loadDailyMetrics } from '../../../utils/metricCollections.js';
import { useCallback, useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { LessonKinds, LessonStatuses, SeriesStatuses, formatDuration, libraryLevels, metadataForSave, metadataOf, moveItem } from '@trelliskit/shared';
import strings from '../../../strings.js';
import { useAdminAuth } from '../../../context/AdminAuthContext.jsx';
import { useProperty } from '../../../context/PropertyContext.jsx';
import { useToast } from '../../../context/ToastContext.jsx';
import useApiQuery from '../../../hooks/useApiQuery.js';
import PageHeader from '../../../components/PageHeader.jsx';
import Modal from '../../../components/Modal.jsx';
import Icon from '../../../components/Icon.jsx';
import ActionMenu from '../../../components/ActionMenu.jsx';
import ConfirmModal from '../../../components/ConfirmModal.jsx';
import CopyableId from '../../../components/CopyableId.jsx';
import MetadataEditor from '../../../components/MetadataEditor.jsx';
import AudienceField, { useAudiencePicker } from '../../../components/AudienceField.jsx';
import KpiCard from '../../../components/KpiCard.jsx';
import DailySeriesChart from '../../../components/usage/DailySeriesChart.jsx';
import { Badge } from '../../../components/Badges.jsx';
import { ErrorBanner, Loading } from '../../../components/States.jsx';
import { AdminListSection, Field, TabBar, useAdminList, useTab } from '../../../components/admin/AdminParts.jsx';
import FileUploadButton from '../../../components/admin/FileUploadButton.jsx';
import { authorName, errorText } from '../../../utils/engagement.js';
import { formatDateTime, formatNumber } from '../../../utils/format.js';
import { appPaths, phase4Errors, useBoundedList } from '../../../utils/phase4.js';
import TableColumns from '../../../components/TableColumns.jsx';

const s = strings.admin.libraryAdmin;
const c = strings.admin.common;
const lib = strings.library;
const ADMIN_TABS = Object.freeze(['series', 'lessons', 'history', 'metrics', 'levels', 'settings']);
const STAFF_TABS = Object.freeze(['series', 'lessons', 'history', 'metrics']);
const SERIES_FILTERS = Object.freeze(['search', 'status', 'audience']);
const LESSON_FILTERS = Object.freeze(['search', 'seriesId', 'status', 'kind', 'hasVideo']);

/** Every series (bounded by `library.maxSeries`), for pickers. */
export function useAllSeries(client) {
  const fetchPage = useCallback((q, signal) => client.adminEnumerateSeries({ ...q, ordering: 'SortOrderAscending' }, signal), [client]);
  return useBoundedList(fetchPage, { pageSize: 500, maxPages: 2 });
}

function statusTone(status) {
  return { Published: 'success', Scheduled: 'info', Processing: 'warning', Archived: 'neutral', Draft: 'neutral' }[status] || 'neutral';
}

function LevelPicker({ value, onChange, disabled }) {
  const { property } = useProperty();
  const levels = libraryLevels(property);
  if (!levels.length) return null;
  return (
    <fieldset className="fieldset">
      <legend>{s.fields.levels}</legend>
      <div className="chip-row">
        {levels.map((l) => {
          const on = value.includes(l.key);
          return <button key={l.key} type="button" className={'chip-toggle' + (on ? ' active' : '')} aria-pressed={on} disabled={disabled || (!on && value.length >= 10)} onClick={() => onChange(on ? value.filter((x) => x !== l.key) : [...value, l.key])}>{l.label}</button>;
        })}
      </div>
    </fieldset>
  );
}

function SeriesEditor({ series, onClose, onSaved }) {
  const { client, isSystemAdmin } = useAdminAuth();
  const [form, setForm] = useState(() => ({
    title: (series && series.title) || '', slug: (series && series.slug) || '', summary: (series && series.summary) || '', description: (series && series.description) || '',
    levels: ((series && series.levels) || []).map((l) => (typeof l === 'object' ? l.key : l)), coverMediaId: null, coverName: '', ...metadataOf(series || {})
  }));
  const audience = useAudiencePicker({ feature: 'library', isSystemAdmin, current: series ? series.audience : null });
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const set = (changes) => setForm((f) => ({ ...f, ...changes }));
  const submit = async (e) => {
    e.preventDefault();
    if (form.title.trim().length < 2) {
      setError(s.problems.titleRequired);
      return;
    }
    setBusy(true);
    setError('');
    const body = { title: form.title.trim(), summary: form.summary.trim(), description: form.description.trim(), levels: form.levels, audience: audience.value, ...metadataForSave({ labels: form.labels, tags: form.tags }, { isStaff: true }) };
    if (form.slug.trim()) body.slug = form.slug.trim();
    if (form.coverMediaId) body.coverMediaId = form.coverMediaId;
    try {
      onSaved(series ? await client.adminUpdateSeries(series.id, body) : await client.adminCreateSeries(body));
    } catch (err) {
      setError(errorText(err, phase4Errors()));
      setBusy(false);
    }
  };
  return (
    <Modal open variant="drawer" size="lg" onClose={busy ? () => {} : onClose} title={series ? s.editSeries(series.title) : s.createSeries}
      footer={<><button type="button" className="button button-secondary" onClick={onClose} disabled={busy}>{strings.common.cancel}</button><button type="submit" form="series-form" className="button button-primary" disabled={busy}>{strings.common.save}</button></>}>
      <form id="series-form" className="stack-form" onSubmit={submit} noValidate>
        <ErrorBanner message={error} />
        <Field id="series-title" label={s.fields.title}><input id="series-title" value={form.title} maxLength={140} onChange={(e) => set({ title: e.target.value })} disabled={busy} /></Field>
        <Field id="series-slug" label={s.fields.slug} hint={s.slugHint}><input id="series-slug" value={form.slug} maxLength={80} onChange={(e) => set({ slug: e.target.value.toLowerCase() })} disabled={busy} /></Field>
        <Field id="series-summary" label={s.fields.summary}><input id="series-summary" value={form.summary} maxLength={300} onChange={(e) => set({ summary: e.target.value })} disabled={busy} /></Field>
        <Field id="series-description" label={s.fields.description}><textarea id="series-description" rows={5} value={form.description} onChange={(e) => set({ description: e.target.value })} disabled={busy} /></Field>
        <LevelPicker value={form.levels} onChange={(levels) => set({ levels })} disabled={busy} />
        <AudienceField id="series-audience" picker={audience} disabled={busy} />
        <div className="field">
          <span className="label-text">{s.fields.cover}</span>
          <FileUploadButton client={client} kind="Image" accept="image/jpeg,image/png,image/webp" label={form.coverName || s.uploadCover} onUploaded={(m) => set({ coverMediaId: m.id, coverName: s.uploaded })} disabled={busy} />
        </div>
        <MetadataEditor value={{ labels: form.labels, tags: form.tags }} onChange={(meta) => set(meta)} isStaff client={client} targetType="Series" disabled={busy} />
      </form>
    </Modal>
  );
}

function SeriesTab() {
  const { client } = useAdminAuth();
  const toast = useToast();
  const fetch = useCallback((q, signal) => client.adminEnumerateSeries(q, signal), [client]);
  const admin = useAdminList({ storageKey: 'tk.admin.series', keys: SERIES_FILTERS, prefix: 's.', fetch, transform: (rest) => ({ ...rest, status: rest.status ? [rest.status] : undefined, audience: rest.audience ? [rest.audience] : undefined }) });
  const [editor, setEditor] = useState(null);
  const [confirm, setConfirm] = useState(null);
  const [order, setOrder] = useState(null);
  const rows = order || admin.rows.slice().sort((a, b) => (a.sortOrder || 0) - (b.sortOrder || 0));
  const run = async (fn, message) => {
    try {
      await fn();
      toast.success(message);
      setOrder(null);
      admin.list.reload();
    } catch (err) {
      toast.error(errorText(err, phase4Errors()));
    } finally {
      setConfirm(null);
    }
  };
  const menu = (row) => [
    { label: c.view, icon: 'eye', onSelect: () => window.open(appPaths.series(row.slug || row.id), '_blank', 'noopener') },
    { label: c.edit, icon: 'edit', onSelect: () => setEditor({ series: row }) },
    row.status !== 'Published' && row.status !== 'Archived' ? { label: s.publish, icon: 'upload', onSelect: () => run(() => client.adminPublishSeries(row.id), s.published) } : null,
    row.status === 'Archived' ? { label: s.unarchive, icon: 'refresh', onSelect: () => run(() => client.adminUnarchiveSeries(row.id), s.unarchived) } : { label: s.archive, icon: 'lock', onSelect: () => run(() => client.adminArchiveSeries(row.id), s.archived) },
    !row.lessonCount ? { label: strings.common.delete, icon: 'trash', danger: true, onSelect: () => setConfirm(row) } : null
  ].filter(Boolean);
  const columns = [
    { key: 'order', label: s.columns.order, render: (row) => {
      const i = rows.indexOf(row);
      return <span className="nowrap"><button type="button" className="icon-button" aria-label={s.moveUp(row.title)} disabled={i <= 0} onClick={() => setOrder(moveItem(rows, i, i - 1))}><Icon name="chevronUp" size={14} /></button><button type="button" className="icon-button" aria-label={s.moveDown(row.title)} disabled={i >= rows.length - 1} onClick={() => setOrder(moveItem(rows, i, i + 1))}><Icon name="chevronDown" size={14} /></button></span>;
    } },
    { key: 'title', label: s.columns.title, render: (row) => <span className="cell-stack"><strong>{row.title}</strong><span className="muted small mono">{row.slug}</span></span> },
    { key: 'status', label: s.columns.status, render: (row) => <Badge tone={statusTone(row.status)}>{s.statuses[row.status] || row.status}</Badge> },
    { key: 'audience', label: s.columns.audience, render: (row) => strings.access.audiences[row.audience] || row.audience },
    { key: 'lessons', label: s.columns.lessons, className: 'num', render: (row) => formatNumber(row.lessonCount || 0) },
    { key: 'id', label: s.columns.id, className: 'cell-id', render: (row) => <CopyableId value={row.id} label={s.columns.id} /> },
    { key: 'actions', label: <span className="visually-hidden">{s.columns.actions}</span>, className: 'cell-actions', render: (row) => <ActionMenu label={c.rowActions(row.title)} items={menu(row)} /> }
  ];
  const filterFields = [
    { key: 'search', label: c.search, grow: true },
    { key: 'status', label: s.columns.status, type: 'select', options: SeriesStatuses.map((x) => ({ value: x, label: s.statuses[x] })) },
    { key: 'audience', label: s.columns.audience, type: 'select', options: ['Public', 'SignedIn', 'Members'].map((x) => ({ value: x, label: strings.access.audiences[x] })) }
  ];
  return (
    <>
      <div className="form-actions-start">
        <button type="button" className="button button-primary button-sm" onClick={() => setEditor({})}><Icon name="plus" size={14} /><span>{s.createSeries}</span></button>
        {order && <button type="button" className="button button-primary button-sm" onClick={() => run(() => client.adminOrderSeries(order.map((x) => x.id)), s.orderSaved)}>{s.saveOrder}</button>}
        {order && <button type="button" className="button button-secondary button-sm" onClick={() => setOrder(null)}>{s.resetOrder}</button>}
      </div>
      <AdminListSection admin={{ ...admin, rows }} filterFields={filterFields} client={client} targetType="Series" columns={columns} caption={s.seriesTitle} empty={s.seriesEmpty} emptyFiltered={s.emptyFiltered} />
      {editor && <SeriesEditor series={editor.series || null} onClose={() => setEditor(null)} onSaved={() => { toast.success(c.saved); setEditor(null); admin.list.reload(); }} />}
      <ConfirmModal open={Boolean(confirm)} title={confirm ? s.deleteSeries(confirm.title) : ''} message={s.deleteSeriesBody} confirmLabel={strings.common.delete} onCancel={() => setConfirm(null)} onConfirm={() => run(() => client.adminDeleteSeries(confirm.id), s.deleted)} />
    </>
  );
}

function NewLessonDialog({ series, onClose, onCreated }) {
  const { client } = useAdminAuth();
  const [seriesId, setSeriesId] = useState((series.items[0] && series.items[0].id) || '');
  const [title, setTitle] = useState('');
  const [kind, setKind] = useState('Video');
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const submit = async (e) => {
    e.preventDefault();
    if (!seriesId || title.trim().length < 2) {
      setError(s.problems.titleRequired);
      return;
    }
    setBusy(true);
    try {
      onCreated(await client.adminCreateLesson(seriesId, { title: title.trim(), kind }));
    } catch (err) {
      setError(errorText(err, phase4Errors()));
      setBusy(false);
    }
  };
  return (
    <Modal open size="sm" onClose={busy ? () => {} : onClose} title={s.createLesson}
      footer={<><button type="button" className="button button-secondary" onClick={onClose} disabled={busy}>{strings.common.cancel}</button><button type="submit" form="new-lesson" className="button button-primary" disabled={busy}>{s.create}</button></>}>
      <form id="new-lesson" className="stack-form" onSubmit={submit} noValidate>
        <ErrorBanner message={error} />
        <Field id="new-lesson-series" label={s.fields.series}>
          <select id="new-lesson-series" value={seriesId} onChange={(e) => setSeriesId(e.target.value)} disabled={busy}>{series.items.map((x) => <option key={x.id} value={x.id}>{x.title}</option>)}</select>
        </Field>
        <Field id="new-lesson-title" label={s.fields.title}><input id="new-lesson-title" value={title} maxLength={140} onChange={(e) => setTitle(e.target.value)} disabled={busy} /></Field>
        <Field id="new-lesson-kind" label={s.fields.kind}>
          <select id="new-lesson-kind" value={kind} onChange={(e) => setKind(e.target.value)} disabled={busy}>{LessonKinds.map((k) => <option key={k} value={k}>{lib.kinds[k]}</option>)}</select>
        </Field>
      </form>
    </Modal>
  );
}

function LessonsTab() {
  const { client } = useAdminAuth();
  const navigate = useNavigate();
  const series = useAllSeries(client);
  const [creating, setCreating] = useState(false);
  const fetch = useCallback((q, signal) => client.adminEnumerateLessons(q, signal), [client]);
  const admin = useAdminList({ storageKey: 'tk.admin.lessons', keys: LESSON_FILTERS, prefix: 'l.', fetch, transform: (rest) => ({ ...rest, status: rest.status ? [rest.status] : undefined }) });
  const open = (row) => navigate('/admin/library/lessons/' + encodeURIComponent(row.id));
  const columns = [
    { key: 'title', label: s.columns.title, render: (row) => <span className="cell-stack"><strong>{row.title}</strong><span className="muted small">{row.series ? row.series.title : ''} #{row.position}</span></span> },
    { key: 'kind', label: s.columns.kind, render: (row) => lib.kinds[row.kind] || row.kind },
    { key: 'status', label: s.columns.status, render: (row) => <Badge tone={statusTone(row.status)}>{s.statuses[row.status] || row.status}</Badge> },
    { key: 'media', label: s.columns.media, render: (row) => (row.kind === 'Video' ? (row.mediaStatus || (row.video ? 'Ready' : s.noVideo)) : '-') },
    { key: 'duration', label: s.columns.duration, className: 'num', render: (row) => formatDuration(row.durationSeconds) || '-' },
    { key: 'publishAt', label: s.columns.publishAt, render: (row) => (row.publishAtUtc ? formatDateTime(row.publishAtUtc) : '-') },
    { key: 'comments', label: s.columns.comments, className: 'num', render: (row) => formatNumber(row.commentCount || 0) },
    { key: 'id', label: s.columns.id, className: 'cell-id', render: (row) => <CopyableId value={row.id} label={s.columns.id} /> }
  ];
  const filterFields = [
    { key: 'search', label: c.search, grow: true },
    { key: 'seriesId', label: s.fields.series, type: 'select', options: series.items.map((x) => ({ value: x.id, label: x.title })) },
    { key: 'status', label: s.columns.status, type: 'select', options: LessonStatuses.map((x) => ({ value: x, label: s.statuses[x] })) },
    { key: 'kind', label: s.columns.kind, type: 'select', options: LessonKinds.map((x) => ({ value: x, label: lib.kinds[x] })) },
    { key: 'hasVideo', label: s.hasVideo, type: 'checkbox' }
  ];
  return (
    <>
      <div className="form-actions-start">
        <button type="button" className="button button-primary button-sm" onClick={() => setCreating(true)} disabled={!series.items.length}><Icon name="plus" size={14} /><span>{s.createLesson}</span></button>
        {!series.loading && !series.items.length && <span className="muted small">{s.needSeries}</span>}
      </div>
      <AdminListSection admin={admin} filterFields={filterFields} client={client} targetType="Lesson" columns={columns} caption={s.lessonsTitle} empty={s.lessonsEmpty} emptyFiltered={s.emptyFiltered} onRowClick={open} />
      {creating && <NewLessonDialog series={series} onClose={() => setCreating(false)} onCreated={(lesson) => { setCreating(false); open(lesson); }} />}
    </>
  );
}

/** Viewing history of one member (resolved decision 17; each read is audited server side). */
export function MemberLibraryHistory({ client, userId }) {
  const fetcher = useCallback((signal) => client.adminEnumerateMemberLibraryProgress(userId, { maxResults: 50 }, signal), [client, userId]);
  const query = useApiQuery(fetcher, { enabled: Boolean(userId) });
  const rows = (query.data && query.data.objects) || [];
  return (
    <div className="stack">
      <p className="muted small">{s.historyAudited}</p>
      {query.loading && <Loading inline />}
      <ErrorBanner message={query.error ? errorText(query.error, phase4Errors()) : ''} onRetry={query.reload} />
      {query.data && !rows.length && <p className="muted small">{s.historyEmpty}</p>}
      {rows.length > 0 && (
        <TableColumns id="tk.admin.library.LibraryAdminView"><table className="data-table">
          <caption className="visually-hidden">{s.historyTitle}</caption>
          <thead><tr><th scope="col">{s.columns.lesson}</th><th scope="col" className="num">{s.columns.percent}</th><th scope="col">{s.columns.completed}</th><th scope="col">{s.columns.lastWatched}</th></tr></thead>
          <tbody>
            {rows.map((r) => (
              <tr key={r.progress.id || r.lesson.id}>
                <td>{r.lesson.title}<div className="muted small">{r.lesson.series ? r.lesson.series.title : ''}</div></td>
                <td className="num">{r.progress.percent || 0}%</td>
                <td>{r.progress.completed ? c.yes : c.no}</td>
                <td>{r.progress.lastWatchedUtc ? formatDateTime(r.progress.lastWatchedUtc) : '-'}</td>
              </tr>
            ))}
          </tbody>
        </table></TableColumns>
      )}
      {query.data && query.data.continuationToken && <p className="muted small">{s.historyMore}</p>}
    </div>
  );
}

function HistoryTab() {
  const { client } = useAdminAuth();
  const [search, setSearch] = useState('');
  const [results, setResults] = useState(null);
  const [user, setUser] = useState(null);
  const find = async (e) => {
    e.preventDefault();
    if (!search.trim()) return;
    try {
      const page = await client.searchMembers({ search: search.trim(), maxResults: 10 });
      setResults((page && (page.objects || page.items)) || []);
    } catch {
      setResults([]);
    }
  };
  return (
    <section className="panel stack">
      <h2 className="section-title">{s.historyTitle}</h2>
      <p className="muted small">{s.historyIntro}</p>
      <form className="inline-form" onSubmit={find} role="search">
        <label className="visually-hidden" htmlFor="history-search">{s.findMember}</label>
        <input id="history-search" className="input-compact" value={search} onChange={(e) => setSearch(e.target.value)} placeholder={s.findMember} />
        <button type="submit" className="button button-secondary button-sm">{c.search}</button>
      </form>
      {results && (
        <div className="chip-row">
          {results.length === 0 && <span className="muted small">{s.noMembers}</span>}
          {results.map((u) => <button key={u.id} type="button" className={'chip-toggle' + (user && user.id === u.id ? ' active' : '')} onClick={() => setUser(u)}>{authorName(u)}</button>)}
        </div>
      )}
      {user && <MemberLibraryHistory key={user.id} client={client} userId={user.id} />}
    </section>
  );
}

function MetricsTab() {
  const { client } = useAdminAuth();
  const [days, setDays] = useState(30);
  const fetcher = useCallback((signal) => loadDailyMetrics((q, sig) => client.adminLibraryMetrics(q, sig), { days: days }, signal), [client, days]);
  const query = useApiQuery(fetcher);
  const m = query.data;
  const series = useMemo(() => (m ? [{ key: 'plays', label: s.metrics.plays, points: m.playsByDay }, { key: 'completions', label: s.metrics.completions, points: m.completionsByDay }] : []), [m]);
  const sum = (list) => (list || []).reduce((total, x) => total + (Number(x.count || x.value) || 0), 0);
  return (
    <section className="stack">
      <label className="inline-field"><span className="label-text">{s.days}</span>
        <select className="select-compact" value={days} onChange={(e) => setDays(Number(e.target.value))}>{[7, 30, 90, 365].map((d) => <option key={d} value={d}>{s.daysValue(d)}</option>)}</select>
      </label>
      {query.loading && <Loading inline />}
      <ErrorBanner message={query.error ? errorText(query.error, phase4Errors()) : ''} onRetry={query.reload} />
      {m && (
        <>
          <div className="kpi-grid">
            <KpiCard label={s.metrics.plays} value={formatNumber(sum(m.playsByDay))} />
            <KpiCard label={s.metrics.completions} value={formatNumber(sum(m.completionsByDay))} />
            <KpiCard label={s.metrics.learners} value={formatNumber(m.activeLearners || 0)} />
          </div>
          <DailySeriesChart title={s.metrics.chartTitle} series={series} />
          <h3>{s.metrics.topLessons}</h3>
          <TableColumns id="tk.admin.library.LibraryAdminView.2"><table className="data-table">
            <thead><tr><th scope="col">{s.columns.lesson}</th><th scope="col" className="num">{s.metrics.plays}</th><th scope="col" className="num">{s.metrics.completions}</th></tr></thead>
            <tbody>{((m.topLessons && m.topLessons.objects) || []).map((x, i) => <tr key={x.lessonId || i}><td>{x.title || x.lessonId}</td><td className="num">{formatNumber(x.plays || 0)}</td><td className="num">{formatNumber(x.completions || 0)}</td></tr>)}</tbody>
          </table></TableColumns>
          <h3>{s.metrics.completionBySeries}</h3>
          <TableColumns id="tk.admin.library.LibraryAdminView.3"><table className="data-table">
            <thead><tr><th scope="col">{s.fields.series}</th><th scope="col" className="num">{s.metrics.rate}</th></tr></thead>
            <tbody>{((m.completionRateBySeries && m.completionRateBySeries.objects) || []).map((x, i) => <tr key={x.seriesId || i}><td>{x.title || x.seriesId}</td><td className="num">{Math.round(100 * (Number(x.rate) || 0))}%</td></tr>)}</tbody>
          </table></TableColumns>
        </>
      )}
    </section>
  );
}

function LevelsTab() {
  const { client } = useAdminAuth();
  const toast = useToast();
  const fetchPage = useCallback((q, signal) => client.adminEnumerateLibraryLevels(q, signal), [client]);
  const list = useBoundedList(fetchPage, { pageSize: 100, maxPages: 1 });
  const [form, setForm] = useState({ key: '', label: '', color: '' });
  const [order, setOrder] = useState(null);
  const rows = order || list.items;
  const run = async (fn, message) => {
    try {
      await fn();
      toast.success(message);
      setOrder(null);
      list.reload();
    } catch (err) {
      toast.error(errorText(err, phase4Errors()));
    }
  };
  return (
    <section className="panel stack">
      <p className="muted small">{s.levelsNote}</p>
      <ErrorBanner message={list.error ? errorText(list.error, phase4Errors()) : ''} onRetry={list.reload} />
      <ul className="history-list">
        {rows.map((l, i) => (
          <li key={l.id} className="history-item badge-row">
            <span className="p4-chip-dot" style={l.color ? { background: l.color } : undefined} /> <strong>{l.label}</strong> <span className="muted mono small">{l.key}</span>
            {l.isRetired && <Badge tone="neutral">{s.retired}</Badge>}
            <button type="button" className="icon-button" aria-label={s.moveUp(l.label)} disabled={i === 0} onClick={() => setOrder(moveItem(rows, i, i - 1))}><Icon name="chevronUp" size={14} /></button>
            <button type="button" className="icon-button" aria-label={s.moveDown(l.label)} disabled={i === rows.length - 1} onClick={() => setOrder(moveItem(rows, i, i + 1))}><Icon name="chevronDown" size={14} /></button>
            {!l.isRetired && <button type="button" className="link-button small" onClick={() => run(() => client.adminDeleteLibraryLevel(l.id), s.levelRetired)}>{s.retire}</button>}
          </li>
        ))}
      </ul>
      {order && <div><button type="button" className="button button-primary button-sm" onClick={() => run(() => client.adminOrderLibraryLevels(order.map((x) => x.id)), s.orderSaved)}>{s.saveOrder}</button></div>}
      <form className="inline-form" onSubmit={(e) => { e.preventDefault(); if (form.key.trim() && form.label.trim()) run(() => client.adminCreateLibraryLevel({ key: form.key.trim(), label: form.label.trim(), color: form.color || null }), s.levelAdded).then(() => setForm({ key: '', label: '', color: '' })); }}>
        <input className="input-compact" aria-label={s.fields.key} placeholder={s.fields.key} value={form.key} onChange={(e) => setForm({ ...form, key: e.target.value })} maxLength={64} />
        <input className="input-compact" aria-label={s.fields.label} placeholder={s.fields.label} value={form.label} onChange={(e) => setForm({ ...form, label: e.target.value })} maxLength={80} />
        <input type="color" aria-label={s.fields.color} value={form.color || '#888888'} onChange={(e) => setForm({ ...form, color: e.target.value })} />
        <button type="submit" className="button button-secondary button-sm">{s.addLevel}</button>
      </form>
    </section>
  );
}

function SettingsTab() {
  const { client } = useAdminAuth();
  const toast = useToast();
  const fetcher = useCallback((signal) => client.adminGetLibrarySettings(signal), [client]);
  const query = useApiQuery(fetcher);
  const [draft, setDraft] = useState(null);
  const value = draft || query.data || {};
  const set = (changes) => setDraft({ ...value, ...changes });
  const save = async (e) => {
    e.preventDefault();
    try {
      setDraft(await client.adminUpdateLibrarySettings(value));
      toast.success(c.saved);
    } catch (err) {
      toast.error(errorText(err, phase4Errors()));
    }
  };
  if (query.loading && !query.data) return <Loading />;
  return (
    <form className="panel stack-form" onSubmit={save}>
      <ErrorBanner message={query.error ? errorText(query.error, phase4Errors()) : ''} onRetry={query.reload} />
      <Field id="lib-level-source" label={s.fields.levelSource}>
        <select id="lib-level-source" value={value.levelSource || 'None'} onChange={(e) => set({ levelSource: e.target.value })}>{['None', 'ProfileField', 'Custom'].map((x) => <option key={x} value={x}>{s.levelSources[x]}</option>)}</select>
      </Field>
      {value.levelSource === 'ProfileField' && <Field id="lib-level-field" label={s.fields.levelFieldKey}><input id="lib-level-field" value={value.levelFieldKey || ''} onChange={(e) => set({ levelFieldKey: e.target.value })} /></Field>}
      <Field id="lib-threshold" label={s.fields.completionThresholdPercent}><input id="lib-threshold" type="number" min={50} max={100} value={value.completionThresholdPercent || 90} onChange={(e) => set({ completionThresholdPercent: Number(e.target.value) })} /></Field>
      <label className="check-field"><input type="checkbox" checked={Boolean(value.downloadsEnabled)} onChange={(e) => set({ downloadsEnabled: e.target.checked })} /><span>{s.fields.downloadsEnabled}</span></label>
      {value.downloadsEnabled && <p className="banner banner-warning small">{s.downloadsWarning}</p>}
      <div className="form-actions"><button type="submit" className="button button-primary">{strings.common.save}</button></div>
    </form>
  );
}

/**
 * /admin/library (docs/PHASE4_CONTRACT.md section 5.10, resolved decisions 3 and 17): series (create,
 * edit, publish, archive, order, delete when empty), lessons (filters, create, open the editor),
 * member viewing history (audited), and metrics for library staff (SystemAdmin or LibraryEditor);
 * levels and settings for SystemAdmin only.
 */
function LibraryAdminView() {
  const { isSystemAdmin } = useAdminAuth();
  const tabs = isSystemAdmin ? ADMIN_TABS : STAFF_TABS;
  const [tab, select] = useTab(tabs);
  const labels = useMemo(() => s.tabs, []);
  return (
    <div className="view">
      <PageHeader title={strings.admin.routes.library.label} summary={strings.admin.routes.library.summary} />
      <TabBar tabs={tabs} labels={labels} current={tab} onSelect={select} idPrefix="lib" label={s.tabsLabel} />
      <div role="tabpanel" id={'lib-panel-' + tab} aria-labelledby={'lib-tab-' + tab} className="tab-panel">
        {tab === 'series' && <SeriesTab />}
        {tab === 'lessons' && <LessonsTab />}
        {tab === 'history' && <HistoryTab />}
        {tab === 'metrics' && <MetricsTab />}
        {tab === 'levels' && <LevelsTab />}
        {tab === 'settings' && <SettingsTab />}
      </div>
    </div>
  );
}

export default LibraryAdminView;

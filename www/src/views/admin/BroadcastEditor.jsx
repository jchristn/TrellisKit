import { useCallback, useEffect, useMemo, useState } from 'react';
import { BroadcastCategory, BroadcastStatus, ENUMERATION_MAX_RESULTS_ADMIN, normalizeEnumerationResult, profileFieldsOf } from '@trelliskit/shared';
import strings from '../../strings.js';
import { useAdminAuth } from '../../context/AdminAuthContext.jsx';
import { useProperty } from '../../context/PropertyContext.jsx';
import { useToast } from '../../context/ToastContext.jsx';
import useApiQuery from '../../hooks/useApiQuery.js';
import useCursorList from '../../hooks/useCursorList.js';
import useDebounce from '../../hooks/useDebounce.js';
import { errorText } from '../../utils/engagement.js';
import { authorName } from '../../utils/engagement.js';
import { formatNumber } from '../../utils/format.js';
import { localInputToIso } from '../../utils/time.js';
import Modal from '../../components/Modal.jsx';
import ConfirmModal from '../../components/ConfirmModal.jsx';
import MetadataEditor from '../../components/MetadataEditor.jsx';
import Icon from '../../components/Icon.jsx';
import { Banner, ErrorBanner, Loading } from '../../components/States.jsx';
import MarkdownBody from '../../components/feed/MarkdownBody.jsx';
import TableColumns from '../../components/TableColumns.jsx';

const s = strings.broadcastsAdmin;
const e = s.editor;
export const PUSH_TITLE_MAX = 65;
export const PUSH_BODY_MAX = 178;
const MIN_LEAD_MS = 5 * 60 * 1000;
const CHANNELS = ['inApp', 'push', 'email'];

/** Built-in segment fields (contract 8.1) with their operators and value choices. */
export const SEGMENT_FIELDS = Object.freeze([
  { field: 'membership', operators: ['eq'], choices: ['Active', 'NotActive', 'Any'] },
  { field: 'membershipSource', operators: ['eq', 'in'], choices: ['Subscription', 'SignupCode', 'AdminComp'] },
  { field: 'store', operators: ['eq', 'in'], choices: ['Apple', 'Google', 'Stripe'] },
  { field: 'labels', operators: ['any', 'all'] },
  { field: 'tags', operators: ['eq', 'exists'], hint: 'key=value' },
  { field: 'signedUpAfterUtc', operators: ['eq'], input: 'date' },
  { field: 'signedUpBeforeUtc', operators: ['eq'], input: 'date' },
  { field: 'activeWithinDays', operators: ['eq'], input: 'number' },
  { field: 'inactiveForDays', operators: ['eq'], input: 'number' },
  { field: 'hasPushToken', operators: ['eq'], choices: ['true', 'false'] },
  { field: 'hasVerifiedEmail', operators: ['eq'], choices: ['true', 'false'] },
  { field: 'role', operators: ['eq', 'in'], choices: ['SystemAdmin', 'Moderator', 'FeedModerator', 'DirectoryModerator', 'EventManager', 'LibraryEditor', 'SupportAgent', 'Communicator', 'ReleaseManager'] },
  { field: 'eventRsvp', operators: ['going', 'interested', 'waitlisted', 'any'], hint: 'evt_...' },
  { field: 'seriesFollowers', operators: ['eq'], hint: 'ser_...' }
]);
const BOOLEAN_FIELDS = Object.freeze(['hasPushToken', 'hasVerifiedEmail']);
const NUMBER_FIELDS = Object.freeze(['activeWithinDays', 'inactiveForDays']);
const LIST_OPERATORS = Object.freeze(['in', 'any', 'all']);
const VALUELESS_OPERATORS = Object.freeze(['hasValue']);
const PROFILE_OPERATORS = ['eq', 'in', 'gte', 'lte', 'prefix', 'hasValue'];

let rowKey = 0;
/** A builder row from a stored filter: `tags.<key>` becomes a Member tags row (`key=value`, or `key` for exists), lists join with commas. */
const newRow = (row = {}) => {
  const field = row.field || 'membership';
  const raw = Array.isArray(row.value) ? row.value.join(',') : row.value === undefined || row.value === null ? '' : String(row.value);
  if (field.startsWith('tags.')) {
    const key = field.slice(5);
    return { key: 'f' + (rowKey += 1), field: 'tags', operator: row.operator === 'exists' ? 'exists' : 'eq', value: row.operator === 'exists' ? key : key + '=' + raw };
  }
  return { key: 'f' + (rowKey += 1), field, operator: row.operator || 'eq', value: VALUELESS_OPERATORS.includes(row.operator) ? '' : raw };
};

function filterValue(field, operator, raw) {
  const text = String(raw).trim();
  if (VALUELESS_OPERATORS.includes(operator)) return true;
  if (BOOLEAN_FIELDS.includes(field)) return text === 'true';
  if (NUMBER_FIELDS.includes(field)) return Number(text);
  if (LIST_OPERATORS.includes(operator)) return text.split(',').map((v) => v.trim()).filter(Boolean);
  return text;
}

/**
 * The filters of a segment for the API (`[{ field, operator, value }]`) with the value types the
 * server checks: booleans for the push and email fields, whole numbers for the day counts, string
 * lists for `in`, `any`, and `all`, `true` for `hasValue`, and `tags.<key>` from a `key=value` (or
 * `key` with exists) Member tags row. Rows without a value are skipped.
 */
export function segmentFilters(rows) {
  return rows.filter((r) => r.field && (VALUELESS_OPERATORS.includes(r.operator) || String(r.value).trim() !== '')).map((r) => {
    if (r.field === 'tags') {
      const text = String(r.value).trim();
      const at = text.indexOf('=');
      const key = (at < 0 ? text : text.slice(0, at)).trim();
      return r.operator === 'exists' ? { field: 'tags.' + key, operator: 'exists', value: true } : { field: 'tags.' + key, operator: 'eq', value: at < 0 ? '' : text.slice(at + 1).trim() };
    }
    return { field: r.field, operator: r.operator, value: filterValue(r.field, r.operator, r.value) };
  });
}

/** Validate a broadcast form; returns `{ field: message }`. */
export function validateBroadcast(form, { now = Date.now(), requireSendAt = false } = {}) {
  const problems = {};
  if (!form.title.trim()) problems.title = e.errors.title;
  if (!CHANNELS.some((ch) => form.channels[ch])) problems.channels = e.errors.channels;
  if (form.channels.inApp && (!form.inApp.title.trim() || !form.inApp.body.trim())) problems.inApp = e.errors.inApp;
  if (form.channels.push && (!form.push.title.trim() || !form.push.body.trim())) problems.push = e.errors.push;
  if (form.channels.email && (!form.email.subject.trim() || !form.email.body.trim())) problems.email = e.errors.email;
  if (form.category === BroadcastCategory.Essential && !form.essentialReason.trim()) problems.essentialReason = e.errors.reason;
  if (requireSendAt) {
    const iso = localInputToIso(form.sendAt);
    if (!iso || new Date(iso).getTime() < now + MIN_LEAD_MS) problems.sendAt = e.errors.sendAt;
  }
  return problems;
}

/** The `POST`/`PUT /admin/broadcasts` body from the form. */
export function broadcastBody(form) {
  const body = {
    title: form.title.trim(),
    channels: { inApp: Boolean(form.channels.inApp), push: Boolean(form.channels.push), email: Boolean(form.channels.email) },
    category: form.category,
    essentialReason: form.category === BroadcastCategory.Essential ? form.essentialReason.trim() : null,
    labels: form.labels,
    tags: form.tags
  };
  if (form.channels.inApp) body.inApp = { title: form.inApp.title.trim(), body: form.inApp.body.trim() };
  if (form.channels.push) body.push = { title: form.push.title.trim(), body: form.push.body.trim() };
  if (form.channels.email) body.email = { subject: form.email.subject.trim(), preheader: form.email.preheader.trim(), body: form.email.body.trim(), ctaLabel: form.email.ctaLabel.trim() };
  if (form.segmentMode === 'saved' && form.segmentId) body.segmentId = form.segmentId;
  else body.segment = { filters: segmentFilters(form.rows) };
  return body;
}

function formFrom(b) {
  const segment = (b && b.segment) || null;
  const savedId = segment && segment.id && !segment.filters ? segment.id : (b && b.segmentId) || '';
  return {
    title: (b && b.title) || '',
    channels: { inApp: b ? Boolean(b.channels && b.channels.inApp) : true, push: Boolean(b && b.channels && b.channels.push), email: Boolean(b && b.channels && b.channels.email) },
    inApp: { title: (b && b.inApp && b.inApp.title) || '', body: (b && b.inApp && b.inApp.body) || '' },
    push: { title: (b && b.push && b.push.title) || '', body: (b && b.push && b.push.body) || '' },
    email: { subject: (b && b.email && b.email.subject) || '', preheader: (b && b.email && b.email.preheader) || '', body: (b && b.email && b.email.body) || '', ctaLabel: (b && b.email && b.email.ctaLabel) || '' },
    segmentMode: savedId ? 'saved' : 'inline',
    segmentId: savedId,
    rows: segment && Array.isArray(segment.filters) ? segment.filters.map(newRow) : [],
    category: (b && b.category) || BroadcastCategory.Optional,
    essentialReason: (b && b.essentialReason) || '',
    labels: (b && b.labels) || [],
    tags: (b && b.tags) || {},
    sendAt: ''
  };
}

function FilterRow({ row, fields, onChange, onRemove }) {
  const def = fields.find((f) => f.field === row.field) || fields[0];
  return (
    <li className="segment-row">
      <select aria-label={e.field} value={row.field} onChange={(ev) => { const next = fields.find((f) => f.field === ev.target.value); onChange({ field: ev.target.value, operator: next.operators[0], value: '' }); }}>
        {fields.map((f) => <option key={f.field} value={f.field}>{f.label}</option>)}
      </select>
      <select aria-label={e.operator} value={row.operator} onChange={(ev) => onChange({ operator: ev.target.value })}>
        {def.operators.map((op) => <option key={op} value={op}>{e.operators[op]}</option>)}
      </select>
      {!VALUELESS_OPERATORS.includes(row.operator) && (def.choices && row.operator === 'eq' ? (
        <select aria-label={e.value} value={row.value} onChange={(ev) => onChange({ value: ev.target.value })}>
          <option value="" />
          {def.choices.map((v) => <option key={v} value={v}>{v}</option>)}
        </select>
      ) : (
        <input aria-label={e.value} type={def.input || 'text'} value={row.value} onChange={(ev) => onChange({ value: ev.target.value })} placeholder={def.choices && row.operator === 'in' ? def.choices.join(',') : def.hint || ''} />
      ))}
      <button type="button" className="icon-button" onClick={onRemove} aria-label={e.removeFilter} title={e.removeFilter}><Icon name="trash" size={14} /></button>
    </li>
  );
}

function SegmentBuilder({ form, setForm, segments, onSaveSegment, onDeleteSegment }) {
  const { property } = useProperty();
  const fields = useMemo(() => [
    ...SEGMENT_FIELDS.map((f) => ({ ...f, label: e.fields[f.field] })),
    ...profileFieldsOf(property).filter((f) => !f.builtIn || ['country', 'region', 'rank'].includes(f.key)).map((f) => ({ field: 'profile.' + f.key, label: e.profileField(f.label || f.key), operators: PROFILE_OPERATORS }))
  ], [property]);
  const [name, setName] = useState('');
  const setRows = (fn) => setForm((v) => ({ ...v, rows: fn(v.rows) }));
  return (
    <fieldset className="detail-block segment-builder">
      <legend><h3>{e.audienceTitle}</h3></legend>
      <div className="segmented" role="radiogroup" aria-label={e.audienceTitle}>
        {['inline', 'saved'].map((mode) => (
          <button key={mode} type="button" role="radio" aria-checked={form.segmentMode === mode} className={'segment' + (form.segmentMode === mode ? ' active' : '')} onClick={() => setForm((v) => ({ ...v, segmentMode: mode }))}>{mode === 'inline' ? e.inlineSegment : e.savedSegment}</button>
        ))}
      </div>
      {form.segmentMode === 'saved' ? (
        <div className="field">
          <label className="label-text" htmlFor="bc-segment">{e.savedSegment}</label>
          <select id="bc-segment" value={form.segmentId} onChange={(ev) => setForm((v) => ({ ...v, segmentId: ev.target.value }))}>
            <option value="" />
            {segments.map((sg) => <option key={sg.id} value={sg.id}>{sg.name}</option>)}
          </select>
          {form.segmentId && onDeleteSegment && (
            <button type="button" className="button button-danger-outline button-sm" onClick={() => onDeleteSegment(segments.find((sg) => sg.id === form.segmentId) || { id: form.segmentId, name: form.segmentId })}>
              <Icon name="trash" size={14} /><span>{e.deleteSegment}</span>
            </button>
          )}
        </div>
      ) : (
        <>
          {form.rows.length === 0 && <p className="muted small">{e.noSegment}</p>}
          <ul className="segment-rows">
            {form.rows.map((row) => (
              <FilterRow key={row.key} row={row} fields={fields} onChange={(patch) => setRows((rows) => rows.map((r) => (r.key === row.key ? { ...r, ...patch } : r)))} onRemove={() => setRows((rows) => rows.filter((r) => r.key !== row.key))} />
            ))}
          </ul>
          <div className="row-actions">
            <button type="button" className="button button-secondary button-sm" onClick={() => setRows((rows) => [...rows, newRow()])}><Icon name="plus" size={14} /><span>{e.addFilter}</span></button>
            <label className="inline-field">
              <span className="visually-hidden">{e.segmentName}</span>
              <input className="input-compact" value={name} placeholder={e.segmentName} maxLength={80} onChange={(ev) => setName(ev.target.value)} />
            </label>
            <button type="button" className="button button-secondary button-sm" disabled={!name.trim()} onClick={() => onSaveSegment(name.trim()).then((ok) => { if (ok) setName(''); })}>{e.saveSegment}</button>
          </div>
        </>
      )}
    </fieldset>
  );
}

function PreviewCount({ preview, loading, error }) {
  return (
    <div className="segment-preview panel" role="status" aria-live="polite" aria-label={e.previewTitle}>
      <span className="label-text">{e.previewTitle}</span>
      {loading && !preview && <span className="muted small">{e.previewing}</span>}
      {error && <span className="field-error small">{errorText(error, e.errors, e.previewError)}</span>}
      {preview && (
        <>
          <strong className="segment-count">{e.previewCount(preview.count)}</strong>
          {preview.byChannel && <span className="muted small">{e.previewByChannel(formatNumber(preview.byChannel.inApp || 0), formatNumber(preview.byChannel.push || 0), formatNumber(preview.byChannel.email || 0))}</span>}
        </>
      )}
    </div>
  );
}

function PushPreview({ title, body }) {
  const { property } = useProperty();
  return (
    <div className="push-preview" aria-label={e.pushPreview}>
      <span className="push-preview-app small">{property.name}</span>
      <strong className="small">{title}</strong>
      <span className="small">{body}</span>
    </div>
  );
}

function EmailPreview({ email }) {
  return (
    <div className="email-preview" aria-label={e.emailPreview}>
      <div className="email-preview-head"><strong>{email.subject}</strong><span className="muted small">{email.preheader}</span></div>
      <MarkdownBody body={email.body || ''} />
      {email.ctaLabel && <span className="button button-primary button-sm" aria-hidden="true">{email.ctaLabel}</span>}
    </div>
  );
}

function Stats({ broadcast }) {
  const { client } = useAdminAuth();
  const stats = broadcast.stats || {};
  const fetchPage = useCallback((cursor, signal) => client.adminEnumerateBroadcastDeliveries(broadcast.id, { maxResults: 50, continuationToken: cursor }, signal), [client, broadcast.id]);
  const list = useCursorList(fetchPage);
  return (
    <section className="detail-block" aria-labelledby="bc-stats-title">
      <h3 id="bc-stats-title">{e.statsTitle}</h3>
      <dl className="kv-grid kv-grid-wide">
        {Object.keys(e.stats).map((key) => <div key={key} className="kv-item"><dt>{e.stats[key]}</dt><dd>{formatNumber(stats[key] || 0)}</dd></div>)}
      </dl>
      <h4>{e.deliveriesTitle}</h4>
      {list.loaded && !list.items.length && <p className="muted small">{e.deliveriesEmpty}</p>}
      {list.items.length > 0 && (
        <div className="table-scroll">
          <TableColumns id="tk.admin.BroadcastEditor"><table className="data-table">
            <caption className="visually-hidden">{e.deliveriesTitle}</caption>
            <thead><tr>{['member', 'inApp', 'push', 'email'].map((k) => <th key={k} scope="col">{e.deliveryColumns[k]}</th>)}</tr></thead>
            <tbody>
              {list.items.map((row) => <tr key={row.id}><td>{authorName(row.user)}</td><td>{row.inAppStatus}</td><td>{row.pushStatus}</td><td>{row.emailStatus}</td></tr>)}
            </tbody>
          </table></TableColumns>
        </div>
      )}
      {list.loaded && !list.done && !list.loading && <button type="button" className="button button-secondary button-sm" onClick={list.loadMore}>{e.loadMore}</button>}
    </section>
  );
}

/**
 * The broadcast drawer (contract section 8; SystemAdmin and Communicator): internal title,
 * audience (an inline segment builder with a live preview count, or a saved segment), channel
 * editors for in app, push, and email with push and email previews, the category (Essential only
 * for SystemAdmin, with a required reason), metadata, and the actions: save draft, test to me,
 * schedule, send now (a confirmation shows the current count and sends it as `confirmCount`),
 * cancel, delete, and the delivery stats once sending.
 * @param {{ id: string|null, onClose: () => void, onChanged: () => void }} props
 */
function BroadcastEditor({ id: initialId, onClose, onChanged }) {
  const { client, isSystemAdmin } = useAdminAuth();
  const toast = useToast();
  const [id, setId] = useState(initialId);
  const fetcher = useCallback((signal) => (id ? client.adminGetBroadcast(id, signal) : Promise.resolve(null)), [client, id]);
  const query = useApiQuery(fetcher);
  const segmentsFetcher = useCallback((signal) => client.adminEnumerateSegments({ maxResults: ENUMERATION_MAX_RESULTS_ADMIN }, signal), [client]);
  const segmentsQuery = useApiQuery(segmentsFetcher);
  const segments = segmentsQuery.data ? normalizeEnumerationResult(segmentsQuery.data).objects : [];
  const [form, setForm] = useState(() => formFrom(null));
  const [loadedId, setLoadedId] = useState(null);
  const [problems, setProblems] = useState({});
  const [busy, setBusy] = useState(false);
  const [confirm, setConfirm] = useState(null);
  const [preview, setPreview] = useState({ data: null, loading: false, error: null });
  const broadcast = query.data;

  if (broadcast && broadcast.id !== loadedId) {
    setLoadedId(broadcast.id);
    setForm(formFrom(broadcast));
  }

  const editable = !broadcast || broadcast.status === BroadcastStatus.Draft || broadcast.status === BroadcastStatus.Scheduled;
  const audienceKey = JSON.stringify(form.segmentMode === 'saved' ? { segmentId: form.segmentId } : { segment: { filters: segmentFilters(form.rows) } });
  const debouncedAudience = useDebounce(audienceKey, 400);

  useEffect(() => {
    const body = JSON.parse(debouncedAudience);
    if (body.segmentId === '') return undefined;
    let active = true;
    setPreview((p) => ({ ...p, loading: true, error: null }));
    client.adminPreviewSegment(body).then(
      (data) => { if (active) setPreview({ data, loading: false, error: null }); },
      (error) => { if (active) setPreview({ data: null, loading: false, error }); }
    );
    return () => { active = false; };
  }, [client, debouncedAudience]);

  const set = (path) => (ev) => {
    const value = ev.target.type === 'checkbox' ? ev.target.checked : ev.target.value;
    setForm((v) => {
      const [a, b] = path.split('.');
      return b ? { ...v, [a]: { ...v[a], [b]: value } } : { ...v, [a]: value };
    });
  };

  const fail = (err) => toast.error(errorText(err, e.errors, strings.common.errorGeneric));

  /** Save the form (create or update); resolves the saved broadcast or null. */
  const persist = async ({ requireSendAt = false } = {}) => {
    const found = validateBroadcast(form, { requireSendAt });
    setProblems(found);
    if (Object.keys(found).length) return null;
    const body = broadcastBody(form);
    const saved = id ? await client.adminUpdateBroadcast(id, body) : await client.adminCreateBroadcast(body);
    if (!id && saved && saved.id) setId(saved.id);
    return saved;
  };

  const act = async (fn) => {
    setBusy(true);
    try {
      await fn();
      onChanged();
    } catch (err) {
      fail(err);
    } finally {
      setBusy(false);
    }
  };

  const save = () => act(async () => { if (await persist()) { toast.success(e.saved); query.reload(); } });
  const test = () => act(async () => {
    const saved = editable ? await persist() : broadcast;
    if (!saved) return;
    await client.adminTestBroadcast(saved.id, CHANNELS.filter((ch) => form.channels[ch]));
    toast.success(e.tested);
  });
  const schedule = () => act(async () => {
    const saved = await persist({ requireSendAt: true });
    if (!saved) return;
    await client.adminScheduleBroadcast(saved.id, localInputToIso(form.sendAt));
    toast.success(e.scheduled);
    query.reload();
  });
  const askSend = () => act(async () => {
    const saved = await persist();
    if (!saved) return;
    const body = saved.segment && saved.segment.id && !saved.segment.filters ? { segmentId: saved.segment.id } : JSON.parse(audienceKey);
    const fresh = await client.adminPreviewSegment(body);
    setPreview({ data: fresh, loading: false, error: null });
    setConfirm({ id: saved.id, count: fresh.count });
  });
  const send = () => act(async () => {
    const target = confirm;
    setConfirm(null);
    await client.adminSendBroadcast(target.id, target.count);
    toast.success(e.sentDone);
    query.reload();
  });
  const cancel = () => act(async () => { await client.adminCancelBroadcast(id); toast.success(e.canceled); query.reload(); });
  const remove = () => act(async () => { await client.adminDeleteBroadcast(id); toast.success(e.deleted); onClose(); });
  const saveSegment = async (name) => {
    try {
      const segment = await client.adminCreateSegment({ name, filters: segmentFilters(form.rows) });
      toast.success(e.segmentSaved);
      segmentsQuery.reload();
      setForm((v) => ({ ...v, segmentMode: 'saved', segmentId: segment.id }));
      return true;
    } catch (err) {
      fail(err);
      return false;
    }
  };

  const [deletingSegment, setDeletingSegment] = useState(null);
  const deleteSegment = async () => {
    try {
      await client.adminDeleteSegment(deletingSegment.id);
      toast.success(e.segmentDeleted);
      setForm((v) => (v.segmentId === deletingSegment.id ? { ...v, segmentId: '' } : v));
      segmentsQuery.reload();
    } catch (err) {
      fail(err);
    } finally {
      setDeletingSegment(null);
    }
  };

  const channelNames = CHANNELS.filter((ch) => form.channels[ch]).map((ch) => s.channels[ch]).join(', ');
  const title = !id ? e.createTitle : broadcast ? (editable ? e.editTitle(broadcast.title) : e.viewTitle(broadcast.title)) : strings.common.loading;
  return (
    <Modal open variant="drawer" size="lg" onClose={busy ? () => {} : onClose} title={title} subtitle={broadcast ? s.statuses[broadcast.status] : null}>
      {id && query.loading && !broadcast && <Loading />}
      {query.error && <ErrorBanner message={errorText(query.error)} onRetry={query.reload} />}
      {(!id || broadcast) && (
        <form className="detail-stack" onSubmit={(ev) => { ev.preventDefault(); save(); }} noValidate>
          {!editable && <Banner tone="info" icon="info">{e.readOnly}</Banner>}
          <fieldset disabled={!editable || busy} className="detail-stack">
            <div className="field">
              <label className="label-text" htmlFor="bc-title">{e.title}</label>
              <input id="bc-title" value={form.title} maxLength={140} onChange={set('title')} aria-invalid={Boolean(problems.title)} />
              {problems.title && <span className="field-error">{problems.title}</span>}
            </div>
            <SegmentBuilder form={form} setForm={setForm} segments={segments} onSaveSegment={saveSegment} onDeleteSegment={setDeletingSegment} />
            <PreviewCount preview={preview.data} loading={preview.loading} error={preview.error} />
            <fieldset className="detail-block">
              <legend><h3>{e.channelsTitle}</h3></legend>
              <div className="row-actions">
                {CHANNELS.map((ch) => <label key={ch} className="checkbox-row"><input type="checkbox" checked={form.channels[ch]} onChange={set('channels.' + ch)} /><span>{s.channels[ch]}</span></label>)}
              </div>
              {problems.channels && <span className="field-error">{problems.channels}</span>}
            </fieldset>
            {form.channels.inApp && (
              <fieldset className="detail-block">
                <legend><h3>{e.inAppTitle}</h3></legend>
                <div className="field"><label className="label-text" htmlFor="bc-inapp-title">{e.fieldTitle}</label><input id="bc-inapp-title" value={form.inApp.title} maxLength={140} onChange={set('inApp.title')} /></div>
                <div className="field"><label className="label-text" htmlFor="bc-inapp-body">{e.fieldBody}</label><textarea id="bc-inapp-body" className="textarea" rows={5} value={form.inApp.body} onChange={set('inApp.body')} /></div>
                {problems.inApp && <span className="field-error">{problems.inApp}</span>}
              </fieldset>
            )}
            {form.channels.push && (
              <fieldset className="detail-block">
                <legend><h3>{e.pushTitle}</h3></legend>
                <div className="field"><label className="label-text" htmlFor="bc-push-title">{e.fieldTitle}</label><input id="bc-push-title" value={form.push.title} maxLength={PUSH_TITLE_MAX} onChange={set('push.title')} /></div>
                <div className="field">
                  <label className="label-text" htmlFor="bc-push-body">{e.fieldBody}</label>
                  <textarea id="bc-push-body" className="textarea" rows={2} value={form.push.body} maxLength={PUSH_BODY_MAX} onChange={set('push.body')} />
                  <span className="muted small">{e.pushLength(form.push.body.length, PUSH_BODY_MAX)}</span>
                </div>
                {problems.push && <span className="field-error">{problems.push}</span>}
                <PushPreview title={form.push.title} body={form.push.body} />
              </fieldset>
            )}
            {form.channels.email && (
              <fieldset className="detail-block">
                <legend><h3>{e.emailTitle}</h3></legend>
                <div className="field"><label className="label-text" htmlFor="bc-email-subject">{e.subject}</label><input id="bc-email-subject" value={form.email.subject} maxLength={140} onChange={set('email.subject')} /></div>
                <div className="field"><label className="label-text" htmlFor="bc-email-preheader">{e.preheader}</label><input id="bc-email-preheader" value={form.email.preheader} maxLength={140} onChange={set('email.preheader')} /></div>
                <div className="field"><label className="label-text" htmlFor="bc-email-body">{e.fieldBody}</label><textarea id="bc-email-body" className="textarea" rows={6} value={form.email.body} onChange={set('email.body')} /></div>
                <div className="field"><label className="label-text" htmlFor="bc-email-cta">{e.ctaLabel}</label><input id="bc-email-cta" value={form.email.ctaLabel} maxLength={40} onChange={set('email.ctaLabel')} /></div>
                {problems.email && <span className="field-error">{problems.email}</span>}
                <EmailPreview email={form.email} />
              </fieldset>
            )}
            <fieldset className="detail-block">
              <legend><h3>{e.categoryTitle}</h3></legend>
              {isSystemAdmin ? (
                <>
                  <label className="checkbox-row">
                    <input type="checkbox" checked={form.category === BroadcastCategory.Essential} onChange={(ev) => setForm((v) => ({ ...v, category: ev.target.checked ? BroadcastCategory.Essential : BroadcastCategory.Optional }))} />
                    <span>{e.essential}</span>
                  </label>
                  {form.category === BroadcastCategory.Essential && (
                    <div className="field">
                      <p className="muted small">{e.essentialNote}</p>
                      <label className="label-text" htmlFor="bc-reason">{e.essentialReason}</label>
                      <input id="bc-reason" value={form.essentialReason} maxLength={500} onChange={set('essentialReason')} aria-invalid={Boolean(problems.essentialReason)} />
                      {problems.essentialReason && <span className="field-error">{problems.essentialReason}</span>}
                    </div>
                  )}
                </>
              ) : <p className="muted small">{s.categories.Optional}. {e.essentialAdminOnly}</p>}
            </fieldset>
            <MetadataEditor value={{ labels: form.labels, tags: form.tags }} onChange={(next) => setForm((v) => ({ ...v, labels: next.labels, tags: next.tags }))} isStaff client={client} targetType="Broadcast" collapsible />
            <div className="field">
              <label className="label-text" htmlFor="bc-send-at">{e.scheduleAt}</label>
              <input id="bc-send-at" type="datetime-local" value={form.sendAt} onChange={set('sendAt')} aria-invalid={Boolean(problems.sendAt)} />
              {problems.sendAt && <span className="field-error">{problems.sendAt}</span>}
            </div>
          </fieldset>
          <div className="form-actions">
            {editable && <button type="submit" className="button button-secondary" disabled={busy}>{e.save}</button>}
            {editable && <button type="button" className="button button-secondary" onClick={test} disabled={busy}>{e.test}</button>}
            {editable && <button type="button" className="button button-secondary" onClick={schedule} disabled={busy}>{e.schedule}</button>}
            {editable && <button type="button" className="button button-primary" onClick={askSend} disabled={busy}>{e.send}</button>}
            {broadcast && (broadcast.status === BroadcastStatus.Scheduled || broadcast.status === BroadcastStatus.Sending) && <button type="button" className="button button-danger-outline" onClick={cancel} disabled={busy}>{e.cancel}</button>}
            {broadcast && broadcast.status === BroadcastStatus.Draft && <button type="button" className="button button-danger-outline" onClick={remove} disabled={busy}>{e.delete}</button>}
          </div>
          {broadcast && !editable && <Stats broadcast={broadcast} />}
        </form>
      )}
      <ConfirmModal open={Boolean(deletingSegment)} title={deletingSegment ? e.deleteSegmentTitle(deletingSegment.name) : ''} message={e.deleteSegmentBody} confirmLabel={e.deleteSegment} onConfirm={deleteSegment} onCancel={() => setDeletingSegment(null)} />
      <ConfirmModal open={Boolean(confirm)} title={e.sendTitle} message={confirm ? e.sendBody(confirm.count, channelNames) : ''} confirmLabel={confirm ? e.sendConfirm(confirm.count) : ''} danger={false} onConfirm={send} onCancel={() => setConfirm(null)} />
    </Modal>
  );
}

export default BroadcastEditor;

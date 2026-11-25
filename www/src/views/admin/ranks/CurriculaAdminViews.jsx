/**
 * `/admin/curricula` and `/admin/curricula/:id`: curriculum editors (SystemAdmin, LibraryEditor)
 * create curricula, group items by the options of a Rank field, pick lessons, series, catalog
 * entries (only with `features.catalog`), or custom text, then publish or archive; rank staff
 * review a member's progress and verify items. Shown only when `features.ranks` is on.
 */
import { useCallback, useId, useMemo, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { RANK_LIMITS, catalogFeatureEnabled, groupCurriculumItems } from '@trelliskit/shared';
import strings from '../../../strings.js';
import { useAdminAuth } from '../../../context/AdminAuthContext.jsx';
import { useProperty } from '../../../context/PropertyContext.jsx';
import useApiQuery from '../../../hooks/useApiQuery.js';
import useCursorList from '../../../hooks/useCursorList.js';
import PageHeader from '../../../components/PageHeader.jsx';
import ConfirmModal from '../../../components/ConfirmModal.jsx';
import EntityPicker from '../../../components/admin/EntityPicker.jsx';
import { Badge } from '../../../components/Badges.jsx';
import { ErrorBanner, Loading } from '../../../components/States.jsx';
import { rankAdminError, useMemberSearch, useRankFields } from './rankAdminParts.jsx';

const c = strings.ranks.curriculaAdmin;
const statusTone = { Draft: 'neutral', Published: 'success', Archived: 'warning' };

export const curriculumAdminPath = (id) => '/admin/curricula/' + encodeURIComponent(id);

function DetailsForm({ initial = {}, fields, busy, onSubmit, submitLabel }) {
  const id = useId();
  const [form, setForm] = useState(() => ({ name: initial.name || '', description: initial.description || '', fieldKey: initial.fieldKey || (fields[0] ? fields[0].key : ''), visibility: initial.visibility || 'Members', position: initial.position || 0 }));
  const [error, setError] = useState('');
  const set = (key) => (e) => setForm((f) => ({ ...f, [key]: e.target.value }));
  const submit = (event) => {
    event.preventDefault();
    if (!form.name.trim() || form.name.trim().length > 120) { setError(strings.ranks.admin.errors.ValidationFailed); return; }
    setError('');
    onSubmit({ name: form.name.trim(), description: form.description.trim(), fieldKey: form.fieldKey || null, visibility: form.visibility, position: Number(form.position) || 0 });
  };
  return (
    <form className="stack" onSubmit={submit} noValidate>
      {error && <p className="field-error" role="alert">{error}</p>}
      <div className="form-grid">
        <label className="field" htmlFor={id + '-name'}><span className="label-text">{c.name}</span><input id={id + '-name'} type="text" maxLength={120} value={form.name} onChange={set('name')} disabled={busy} /></label>
        <label className="field" htmlFor={id + '-field'}><span className="label-text">{c.field}</span>
          <select id={id + '-field'} value={form.fieldKey} onChange={set('fieldKey')} disabled={busy}>
            <option value="">{c.noField}</option>
            {fields.map((f) => <option key={f.key} value={f.key}>{f.label || f.key}</option>)}
          </select>
        </label>
        <label className="field" htmlFor={id + '-vis'}><span className="label-text">{c.visibility}</span>
          <select id={id + '-vis'} value={form.visibility} onChange={set('visibility')} disabled={busy}>{Object.keys(c.visibilities).map((k) => <option key={k} value={k}>{c.visibilities[k]}</option>)}</select>
        </label>
        <label className="field" htmlFor={id + '-pos'}><span className="label-text">{c.position}</span><input id={id + '-pos'} type="number" value={form.position} onChange={set('position')} disabled={busy} /></label>
      </div>
      <label className="field" htmlFor={id + '-desc'}><span className="label-text">{c.description}</span><textarea id={id + '-desc'} rows={3} maxLength={2000} value={form.description} onChange={set('description')} disabled={busy} /></label>
      <div className="form-actions"><button type="submit" className="button button-primary" disabled={busy}>{submitLabel}</button></div>
    </form>
  );
}

export function CurriculaAdminView() {
  const { client } = useAdminAuth();
  const fields = useRankFields();
  const navigate = useNavigate();
  const [status, setStatus] = useState('');
  const [creating, setCreating] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const filterKey = JSON.stringify(status ? { status } : {});
  const fetchPage = useCallback((cursor, signal) => client.adminEnumerateCurricula({ filters: JSON.parse(filterKey), maxResults: 25, continuationToken: cursor }, signal), [client, filterKey]);
  const list = useCursorList(fetchPage);
  const create = async (body) => {
    setBusy(true);
    setError('');
    try {
      const created = await client.adminCreateCurriculum(body);
      navigate(curriculumAdminPath(created.id));
    } catch (err) {
      setError(rankAdminError(err));
      setBusy(false);
    }
  };
  return (
    <div className="view stack">
      <PageHeader title={c.title} summary={strings.ranks.admin.curriculaSummary} actions={!creating && <button type="button" className="button button-primary" onClick={() => setCreating(true)}>{c.create}</button>} />
      {creating && (
        <section className="panel stack" aria-label={c.createTitle}>
          <h2 className="section-title">{c.createTitle}</h2>
          {error && <p className="field-error" role="alert">{error}</p>}
          <DetailsForm fields={fields} busy={busy} onSubmit={create} submitLabel={c.create} />
        </section>
      )}
      <label className="field" htmlFor="curricula-status"><span className="label-text">{c.status}</span>
        <select id="curricula-status" value={status} onChange={(e) => setStatus(e.target.value)}><option value="">{c.anyStatus}</option>{Object.keys(c.statuses).map((k) => <option key={k} value={k}>{c.statuses[k]}</option>)}</select>
      </label>
      <ErrorBanner message={list.error ? rankAdminError(list.error) : ''} onRetry={list.refresh} />
      {list.loaded && !list.items.length && !list.error && <p className="muted">{c.empty}</p>}
      <ul className="p4-list">
        {list.items.map((row) => (
          <li key={row.id} className="panel">
            <Link to={curriculumAdminPath(row.id)}>{row.name}</Link>{' '}
            <Badge tone={statusTone[row.status] || 'neutral'}>{c.statuses[row.status] || row.status}</Badge>{' '}
            <span className="muted small">{strings.ranks.curricula.items(row.itemCount || 0)} · {c.visibilities[row.visibility] || row.visibility}</span>
          </li>
        ))}
      </ul>
      {list.loading && <Loading inline />}
      {list.loaded && !list.done && !list.loading && !list.error && <button type="button" className="button button-secondary" onClick={list.loadMore}>{c.more}</button>}
    </div>
  );
}

let itemKey = 0;
const toRow = (item) => ({
  key: 'i' + (itemKey += 1),
  id: item.id || null,
  optionKey: item.optionKey || '',
  itemType: item.itemType || 'Custom',
  target: item.targetId ? [{ id: item.targetId, label: item.title || item.targetId }] : [],
  customText: item.customText || '',
  required: item.required !== false,
  labels: item.labels,
  tags: item.tags
});

/** PUT /admin/curricula/{id}/items body rows from editor rows (listed ids keep member progress). */
export function curriculumItemsBody(rows) {
  return rows.map((row) => {
    const out = { itemType: row.itemType, required: Boolean(row.required) };
    if (row.id) out.id = row.id;
    if (row.optionKey) out.optionKey = row.optionKey;
    if (row.itemType === 'Custom') out.customText = row.customText.trim();
    else out.targetId = row.target.length ? row.target[0].id : '';
    if (row.labels) out.labels = row.labels;
    if (row.tags) out.tags = row.tags;
    return out;
  });
}

function ItemsEditor({ client, curriculum, field, catalogOn, onSaved }) {
  const [rows, setRows] = useState(() => (curriculum.items || []).map(toRow));
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [status, setStatus] = useState('');
  const types = ['Lesson', 'Series', ...(catalogOn ? ['CatalogEntry'] : []), 'Custom'];
  const searches = useMemo(() => ({
    Lesson: async (term, signal) => ((await client.adminEnumerateLessons({ search: term, maxResults: 10 }, signal)).objects || []).map((x) => ({ id: x.id, label: x.title })),
    Series: async (term, signal) => ((await client.adminEnumerateSeries({ search: term, maxResults: 10 }, signal)).objects || []).map((x) => ({ id: x.id, label: x.title })),
    CatalogEntry: async (term, signal) => ((await client.adminEnumerateCatalogEntries({ search: term, maxResults: 10 }, signal)).objects || []).map((x) => ({ id: x.id, label: x.name }))
  }), [client]);
  const update = (key, patch) => setRows((list) => list.map((row) => (row.key === key ? { ...row, ...patch } : row)));
  const move = (index, delta) => setRows((list) => {
    const next = list.slice();
    const [row] = next.splice(index, 1);
    next.splice(index + delta, 0, row);
    return next;
  });
  const save = async () => {
    if (rows.some((row) => (row.itemType === 'Custom' ? !row.customText.trim() || row.customText.trim().length > RANK_LIMITS.customTextMax : !row.target.length))) { setError(c.itemProblem); return; }
    setBusy(true);
    setError('');
    try {
      const saved = await client.adminReplaceCurriculumItems(curriculum.id, curriculumItemsBody(rows));
      setRows(((saved && saved.items) || []).map(toRow));
      setStatus(c.itemsSaved);
      onSaved(saved);
    } catch (err) {
      setError(rankAdminError(err));
    } finally {
      setBusy(false);
    }
  };
  const options = field ? (field.options || []).filter(Boolean) : [];
  return (
    <section className="panel stack" aria-labelledby="curriculum-items-title">
      <h2 id="curriculum-items-title" className="section-title">{c.itemsTitle}</h2>
      <p className="muted small">{c.itemsIntro} {c.itemsLimit(RANK_LIMITS.maxCurriculumItems)}</p>
      {error && <p className="field-error" role="alert">{error}</p>}
      {status && <p className="success-text small" role="status">{status}</p>}
      {!rows.length && <p className="muted">{c.itemsEmpty}</p>}
      <ol className="curriculum-editor">
        {rows.map((row, index) => {
          const id = 'cui-' + row.key;
          return (
            <li key={row.key} className="fieldset stack" data-row={index}>
              <div className="form-grid">
                {options.length > 0 && (
                  <label className="field" htmlFor={id + '-group'}><span className="label-text">{c.group}</span>
                    <select id={id + '-group'} value={row.optionKey} onChange={(e) => update(row.key, { optionKey: e.target.value })} disabled={busy}>
                      <option value="">{c.noGroup}</option>
                      {options.map((o) => <option key={o.key} value={o.key}>{o.label || o.key}</option>)}
                    </select>
                  </label>
                )}
                <label className="field" htmlFor={id + '-type'}><span className="label-text">{c.type}</span>
                  <select id={id + '-type'} value={row.itemType} onChange={(e) => update(row.key, { itemType: e.target.value, target: [] })} disabled={busy}>
                    {[...new Set([...types, row.itemType])].map((k) => <option key={k} value={k}>{c.types[k] || k}</option>)}
                  </select>
                </label>
                <label className="check-field"><input type="checkbox" checked={row.required} onChange={(e) => update(row.key, { required: e.target.checked })} disabled={busy} /><span>{c.required}</span></label>
              </div>
              {row.itemType === 'Custom'
                ? <label className="field" htmlFor={id + '-text'}><span className="label-text">{c.customText}</span><input id={id + '-text'} type="text" maxLength={RANK_LIMITS.customTextMax} value={row.customText} onChange={(e) => update(row.key, { customText: e.target.value })} disabled={busy} /></label>
                : searches[row.itemType] && <EntityPicker id={id + '-target'} label={c.target} value={row.target} onChange={(items) => update(row.key, { target: items.slice(0, 1) })} search={searches[row.itemType]} placeholder={c.targetPlaceholder} disabled={busy} />}
              <div className="row-actions">
                <button type="button" className="button button-ghost button-sm" onClick={() => move(index, -1)} disabled={busy || index === 0}>{c.moveUp}</button>
                <button type="button" className="button button-ghost button-sm" onClick={() => move(index, 1)} disabled={busy || index === rows.length - 1}>{c.moveDown}</button>
                <button type="button" className="button button-danger-outline button-sm" onClick={() => setRows((list) => list.filter((x) => x.key !== row.key))} disabled={busy}>{c.remove}</button>
              </div>
            </li>
          );
        })}
      </ol>
      <div className="form-actions">
        <button type="button" className="button button-secondary" onClick={() => setRows((list) => [...list, toRow({ itemType: 'Lesson', optionKey: list.length ? list[list.length - 1].optionKey : '' })])} disabled={busy || rows.length >= RANK_LIMITS.maxCurriculumItems}>{c.addItem}</button>
        <button type="button" className="button button-primary" onClick={save} disabled={busy}>{c.saveItems}</button>
      </div>
    </section>
  );
}

/** Member progress drill-down: pick a member, see each item's state, verify items (rank staff). */
export function CurriculumProgressPanel({ client, curriculum, field, search }) {
  const [member, setMember] = useState([]);
  const userId = member.length ? member[0].id : null;
  const query = useApiQuery(useCallback((signal) => client.adminGetCurriculumProgress(curriculum.id, userId, signal), [client, curriculum.id, userId]), { enabled: Boolean(userId) });
  const [verified, setVerified] = useState({});
  const [error, setError] = useState('');
  const items = ((query.data && query.data.items) || []).map((item) => (verified[item.id] ? { ...item, ...verified[item.id] } : item));
  const groups = groupCurriculumItems(field, items);
  const verify = async (item) => {
    setError('');
    try {
      const next = await client.verifyCurriculumItem(curriculum.id, userId, item.id);
      setVerified((v) => ({ ...v, [item.id]: next && next.id ? next : { state: 'Verified', satisfied: true } }));
    } catch (err) {
      setError(rankAdminError(err));
    }
  };
  const done = items.filter((i) => i.satisfied).length;
  return (
    <section className="panel stack" aria-labelledby="curriculum-progress-title">
      <h2 id="curriculum-progress-title" className="section-title">{c.progressTitle}</h2>
      <p className="muted small">{c.progressIntro}</p>
      <EntityPicker id="curriculum-progress-member" label={strings.ranks.admin.member} value={member} onChange={(list) => { setMember(list.slice(0, 1)); setVerified({}); }} search={search} placeholder={strings.ranks.admin.memberPlaceholder} />
      <ErrorBanner message={query.error ? rankAdminError(query.error) : error} onRetry={query.error ? query.reload : undefined} />
      {userId && query.loading && <Loading inline />}
      {userId && query.data && (
        <>
          <p className="small">{c.progressSummary(done, items.length)}</p>
          {groups.map((group) => (
            <div key={group.optionKey || 'none'}>
              <h3 className="label-text">{group.label || strings.ranks.curricula.ungrouped}</h3>
              <ul className="curriculum-items">
                {group.items.map((item) => (
                  <li key={item.id} className="row-between" data-item={item.id}>
                    <span>{item.title || item.customText || item.targetId} <Badge tone={item.satisfied ? 'success' : 'neutral'}>{c.states[item.state] || item.state}</Badge>{item.targetAvailable === false && <span className="muted small"> {c.unavailable}</span>}</span>
                    {item.state !== 'Verified' && <button type="button" className="button button-secondary button-sm" onClick={() => verify(item)}>{c.verify}</button>}
                  </li>
                ))}
              </ul>
            </div>
          ))}
        </>
      )}
    </section>
  );
}

export function CurriculumAdminEditView() {
  const { id } = useParams();
  return <CurriculumEditor key={id} id={id} />;
}

function CurriculumEditor({ id }) {
  const { client, isSystemAdmin } = useAdminAuth();
  const { property } = useProperty();
  const fields = useRankFields();
  const search = useMemberSearch(client, isSystemAdmin);
  const query = useApiQuery(useCallback((signal) => client.adminGetCurriculum(id, signal), [client, id]));
  const [override, setOverride] = useState(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [status, setStatus] = useState('');
  const [confirmArchive, setConfirmArchive] = useState(false);
  const curriculum = override || query.data;
  const field = curriculum ? fields.find((f) => f.key === curriculum.fieldKey) || null : null;
  const run = async (fn, message) => {
    setBusy(true);
    setError('');
    setStatus('');
    try {
      setOverride(await fn());
      setStatus(message);
    } catch (err) {
      setError(rankAdminError(err));
    } finally {
      setBusy(false);
      setConfirmArchive(false);
    }
  };
  return (
    <div className="view stack">
      <Link className="back-link" to="/admin/curricula">{c.back}</Link>
      <ErrorBanner message={query.error ? rankAdminError(query.error) : ''} onRetry={query.reload} />
      {query.loading && !curriculum && <Loading />}
      {curriculum && (
        <>
          <PageHeader title={curriculum.name} summary={<Badge tone={statusTone[curriculum.status] || 'neutral'}>{c.statuses[curriculum.status] || curriculum.status}</Badge>} actions={<>
            {curriculum.status === 'Draft' && <button type="button" className="button button-primary" onClick={() => run(() => client.adminPublishCurriculum(curriculum.id), c.published)} disabled={busy}>{c.publish}</button>}
            {curriculum.status !== 'Archived' && <button type="button" className="button button-danger-outline" onClick={() => setConfirmArchive(true)} disabled={busy}>{c.archive}</button>}
          </>} />
          {error && <p className="field-error" role="alert">{error}</p>}
          {status && <p className="success-text small" role="status">{status}</p>}
          <section className="panel">
            <DetailsForm key={curriculum.updatedUtc || curriculum.id} initial={curriculum} fields={fields} busy={busy} submitLabel={c.save} onSubmit={(body) => run(() => client.adminUpdateCurriculum(curriculum.id, body), c.saved)} />
          </section>
          <ItemsEditor key={'items-' + (curriculum.updatedUtc || '')} client={client} curriculum={curriculum} field={field} catalogOn={catalogFeatureEnabled(property)} onSaved={(saved) => setOverride(saved)} />
          <CurriculumProgressPanel client={client} curriculum={curriculum} field={field} search={search} />
          <ConfirmModal open={confirmArchive} title={c.archive} message={c.confirmArchive} confirmLabel={c.archive} busy={busy} onConfirm={() => run(() => client.adminArchiveCurriculum(curriculum.id), c.archived)} onCancel={() => setConfirmArchive(false)} />
        </>
      )}
    </div>
  );
}

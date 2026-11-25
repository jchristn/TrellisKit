import { useCallback, useState } from 'react';
import CatalogApparatusEditor from './CatalogApparatusEditor.jsx';
import CatalogModalityEditor from './CatalogModalityEditor.jsx';
import CatalogImportPanel from './CatalogImportPanel.jsx';
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { catalogApparatusProblem, catalogModalityProblem, CatalogDifficulties, CatalogKinds, CatalogListFields, CatalogRelationshipKinds, CatalogTextFields, catalogEntryBody, catalogEntryForm, loadCatalogRelationships, normalizeSlugInput, slugProblem, SLUG_MIN, SLUG_MAX, SLUG_PATTERN } from '@trelliskit/shared';
import strings from '../../../strings.js';
import { useAdminAuth } from '../../../context/AdminAuthContext.jsx';
import useApiQuery from '../../../hooks/useApiQuery.js';
import useCursorList from '../../../hooks/useCursorList.js';
import useEnumerationUrlState from '../../../hooks/useEnumerationUrlState.js';
import PageHeader from '../../../components/PageHeader.jsx';
import MetadataEditor from '../../../components/MetadataEditor.jsx';
import MetadataFilterBar from '../../../components/MetadataFilterBar.jsx';
import ConfirmModal from '../../../components/ConfirmModal.jsx';
import EntityPicker from '../../../components/admin/EntityPicker.jsx';
import { ErrorBanner, Loading } from '../../../components/States.jsx';
import { CatalogEntryContent, CatalogFilters, CatalogListState, CatalogRelationships } from '../../catalog/CatalogViews.jsx';

const t = strings.exerciseCatalog;
const fail = (error) => error?.code === 'CatalogChanged' ? t.changed : error?.status === 400 ? Object.values(error.fieldErrors || error.body?.fieldErrors || {}).join(' ') || t.invalid : error?.message || t.saveFailed;

export function CatalogAdminView() {
  const { client, isSystemAdmin } = useAdminAuth();
  const [params, setParams] = useSearchParams();
  const keys = ['kind', 'program', 'difficulty', 'muscle', 'equipment', 'familyKey', 'nameLanguage', 'attireKey', 'levelKey', 'notationSystemKey', 'status'];
  const filters = Object.fromEntries(keys.map((key) => [key, params.get(key) || '']));
  const setFilters = (value) => setParams((current) => { const next = new URLSearchParams(current); for (const key of keys) { if (value[key]) next.set(key, value[key]); else next.delete(key); } return next; });
  const [meta, setMeta, metaKey] = useEnumerationUrlState();
  const filterKey = JSON.stringify(filters);
  const config = useApiQuery(useCallback((signal) => client.getExerciseCatalogConfig(signal), [client]));
  const list = useCursorList(useCallback((cursor, signal) => client.adminEnumerateCatalogEntries({ ...JSON.parse(metaKey), filters: JSON.parse(filterKey), maxResults: 25, continuationToken: cursor }, signal), [client, filterKey, metaKey]));
  return <div className="view stack"><PageHeader title={t.title} summary={t.adminSummary} /><div><Link className="button button-primary" to="/admin/catalog/new">{t.create}</Link></div><MetadataFilterBar value={meta} onChange={setMeta} client={client} showSearch searchLabel={t.search} /><ErrorBanner message={config.error ? t.error : ''} onRetry={config.reload} /><CatalogFilters admin config={config.data} value={filters} onChange={setFilters} />
    <ul className="p4-list">{list.items.map((entry) => <li className="panel" key={entry.id}><Link to={'/admin/catalog/' + encodeURIComponent(entry.id)}>{entry.name}</Link><p>{t.statuses[entry.status]}{' · '}{t.kinds[entry.kind]}</p>{entry.reviewNote && <p>{entry.reviewNote}</p>}</li>)}</ul><CatalogListState list={list} />{isSystemAdmin && <CatalogImportPanel client={client} />}</div>;
}

function SelectField({ label, value, onChange, options, empty = false }) {
  return <label className="field"><span className="label-text">{label}</span><select value={value} onChange={(e) => onChange(e.target.value)}>{empty && <option value="">{t.all}</option>}{options.map((item) => <option key={item.key} value={item.key}>{item.label}</option>)}</select></label>;
}

function VocabularyField({ label, value, onChange, options }) {
  const selected = value.split('\n').filter(Boolean);
  const unknown = selected.filter((key) => !options.some((item) => item.key === key));
  return <fieldset className="fieldset"><legend>{label}</legend><p className="muted small">{t.vocabularyHint}</p><div className="badge-row">{[...options, ...unknown.map((key) => ({ key, label: key }))].map((item) => <label className="check-field" key={item.key}><input type="checkbox" checked={selected.includes(item.key)} onChange={(e) => onChange((e.target.checked ? [...selected, item.key] : selected.filter((key) => key !== item.key)).join('\n'))} /><span>{item.label}</span></label>)}</div></fieldset>;
}

function CatalogForm({ client, entry, config, onSaved, onCancel }) {
  const [form, setForm] = useState(() => catalogEntryForm(entry));
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [metaInvalid, setMetaInvalid] = useState(false);
  const set = (key, value) => setForm((current) => ({ ...current, [key]: key === 'slug' ? normalizeSlugInput(value) : value }));
  const lessonSearch = useCallback(async (search, signal) => (await client.adminEnumerateLessons({ search, maxResults: 10 }, signal)).objects.map((row) => ({ id: row.id, label: row.title })), [client]);
  const [lessons, setLessons] = useState(() => (entry?.lessonIds || []).map((id) => ({ id, label: id })));
  const save = async (event) => {
    event.preventDefault(); if (busy || metaInvalid) return;
    const problem = slugProblem(form.slug);
    if (problem) { setError(strings.publicPages.problems[problem]); return; }
    const descriptorProblem = catalogModalityProblem(form.modalityDetails, config, form.program);
    if (descriptorProblem) { setError(t.descriptorProblems[descriptorProblem]); return; }
    const apparatusProblem = catalogApparatusProblem(form.apparatusSetup, config, form.equipment);
    if (apparatusProblem) { setError(t.apparatus.problems[apparatusProblem]); return; }
    setBusy(true); setError('');
    try { const body = catalogEntryBody(form, entry?.revision); const saved = entry ? await client.adminUpdateCatalogEntry(entry.id, body) : await client.adminCreateCatalogEntry(body); onSaved(saved); }
    catch (err) { setError(fail(err)); }
    finally { setBusy(false); }
  };
  const skip = ['primaryMuscles', 'secondaryMuscles', 'equipment', 'lessonIds'];
  return <form className="stack" onSubmit={save}><p className="banner banner-info">{t.unpublished}</p><p>{t.publicationNeeds}</p><ErrorBanner message={error} />
    <fieldset disabled={busy} className="fieldset stack"><legend>{t.edit}</legend><div className="form-grid" style={{ gridTemplateColumns: 'repeat(auto-fit, minmax(min(100%, 260px), 1fr))' }}>
      <SelectField label={t.kind} value={form.kind} onChange={(value) => set('kind', value)} options={CatalogKinds.map((key) => ({ key, label: t.kinds[key] }))} />
      <SelectField label={t.difficulty} value={form.difficulty} onChange={(value) => set('difficulty', value)} options={CatalogDifficulties.map((key) => ({ key, label: t.difficulties[key] }))} />
      <SelectField label={t.program} value={form.program} onChange={(value) => set('program', value)} options={config?.programs || []} empty />
      {CatalogTextFields.filter((key) => key !== 'program').map((key) => <label className="field" key={key}><span className="label-text">{t.fields[key]}</span>{key === 'summary' ? <textarea value={form[key]} onChange={(e) => set(key, e.target.value)} maxLength={2000} rows={4} /> : <input value={form[key]} onChange={(e) => set(key, e.target.value)} required={key === 'name' || key === 'slug'} minLength={key === 'name' ? 2 : key === 'slug' ? SLUG_MIN : undefined} pattern={key === 'slug' ? SLUG_PATTERN.source : undefined} maxLength={key === 'name' ? 160 : key === 'slug' ? SLUG_MAX : 100} />}</label>)}
    </div>
    <CatalogModalityEditor value={form.modalityDetails} onChange={(value) => set('modalityDetails', value)} program={form.program} config={config} />
    {['primaryMuscles', 'secondaryMuscles', 'equipment'].map((key) => <VocabularyField key={key} label={t.fields[key]} value={form[key]} onChange={(value) => set(key, value)} options={config?.[key === 'equipment' ? 'equipment' : 'muscles'] || []} />)}
    <CatalogApparatusEditor value={form.apparatusSetup} onChange={(value) => set('apparatusSetup', value)} equipment={form.equipment} config={config} />
    <div className="form-grid">{CatalogListFields.filter((key) => !skip.includes(key)).map((key) => <label className="field" key={key}><span className="label-text">{t.fields[key]}</span><textarea value={form[key]} onChange={(e) => set(key, e.target.value)} rows={key === 'instructions' ? 6 : 3} maxLength={key === 'instructions' ? 50050 : key === 'aliases' ? 3030 : 30030} /><span className="muted small">{t.linesHint}</span></label>)}</div>
    <EntityPicker label={t.lessonPicker} value={lessons} onChange={(next) => { setLessons(next); set('lessonIds', next.map((row) => row.id).join('\n')); }} search={lessonSearch} multiple max={20} />
    <section className="stack"><h2 className="section-title">{t.sources}</h2>{form.sources.map((source, i) => <fieldset className="fieldset" key={i}><div className="form-grid">{['url', 'title', 'license', 'attribution'].map((key) => <label className="field" key={key}><span className="label-text">{{ url: t.sourceUrl, title: t.sourceTitle, license: t.license, attribution: t.attribution }[key]}</span><input type={key === 'url' ? 'url' : 'text'} pattern={key === 'url' ? 'https://.*' : undefined} required={key === 'title' || key === 'license' || key === 'url' && source.license !== 'Original'} value={source[key] || ''} maxLength={key === 'license' ? 80 : key === 'title' ? 200 : 2000} onChange={(e) => set('sources', form.sources.map((item, index) => index === i ? { ...item, [key]: e.target.value } : item))} /></label>)}</div><button className="button button-secondary" type="button" onClick={() => set('sources', form.sources.filter((_, index) => index !== i))}>{t.remove}</button></fieldset>)}<button type="button" className="button button-secondary" disabled={form.sources.length >= 20} onClick={() => set('sources', [...form.sources, { url: '', title: '', license: '', attribution: '' }])}>{t.addSource}</button></section>
    <MetadataEditor value={form} onChange={(next, problems) => { setForm((current) => ({ ...current, ...next })); setMetaInvalid(problems.length > 0); }} client={client} isStaff />
    <div className="form-actions"><button type="button" className="button button-secondary" onClick={onCancel}>{t.cancel}</button><button className="button button-primary" disabled={metaInvalid}>{t.save}</button></div>
    </fieldset></form>;
}

function RelationshipEditor({ client, entry, onSaved, onCancel }) {
  const query = useApiQuery(useCallback((signal) => loadCatalogRelationships(client, entry.id, signal), [client, entry.id]));
  const [draft, setDraft] = useState(null);
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const rows = draft || query.data || [];
  const search = useCallback(async (term, signal) => (await client.adminEnumerateCatalogEntries({ search: term, maxResults: 10 }, signal)).objects.filter((row) => row.id !== entry.id).map((row) => ({ id: row.id, label: row.name })), [client, entry.id]);
  const set = (index, changes) => setDraft(rows.map((row, i) => i === index ? { ...row, ...changes } : row));
  const save = async () => {
    if (rows.some((row) => !row.targetId)) { setError(t.chooseTarget); return; }
    setBusy(true); setError('');
    try { const saved = await client.adminSetCatalogRelationships(entry.id, { expectedRevision: entry.revision, relationships: rows.map((row) => ({ targetId: row.targetId, kind: row.kind, note: row.note || '', labels: row.labels || [], tags: row.tags || {} })) }); onSaved(saved); }
    catch (err) { setError(fail(err)); } finally { setBusy(false); }
  };
  return <section className="panel stack"><h2>{t.editRelationships}</h2><p>{t.relationshipLimit}</p><p>{t.unpublished}</p><ErrorBanner message={query.error ? t.relationsIncomplete : error} onRetry={query.error ? query.reload : undefined} />{query.loading && <Loading />}
    {query.data && !query.error && !query.loading && <fieldset className="fieldset stack" disabled={busy}>{rows.map((row, i) => <div className="panel stack" key={i}><EntityPicker label={t.target} value={row.targetId ? [{ id: row.targetId, label: row.targetName || row.targetId }] : []} onChange={(next) => set(i, { targetId: next[0]?.id || '', targetName: next[0]?.label || '' })} search={search} /><SelectField label={t.relationKind} value={row.kind} onChange={(kind) => set(i, { kind })} options={CatalogRelationshipKinds.map((key) => ({ key, label: t.relationshipKinds[key] }))} /><label className="field"><span className="label-text">{t.relationNote}</span><textarea maxLength={1000} value={row.note || ''} onChange={(e) => set(i, { note: e.target.value })} /></label><button type="button" className="button button-secondary" onClick={() => setDraft(rows.filter((_, index) => index !== i))}>{t.remove}</button></div>)}<button type="button" className="button button-secondary" disabled={rows.length >= 100} onClick={() => setDraft([...rows, { targetId: '', kind: 'Regression', note: '' }])}>{t.addRelationship}</button><button type="button" className="button button-primary" onClick={save}>{t.saveRelationships}</button></fieldset>}
    <button type="button" className="button button-secondary" disabled={busy} onClick={onCancel}>{t.cancel}</button></section>;
}

export function CatalogAdminEntryView() {
  const { id } = useParams();
  return <CatalogAdminEntry key={id} id={id} />;
}

function CatalogAdminEntry({ id }) {
  const { client, isSystemAdmin } = useAdminAuth();
  const navigate = useNavigate();
  const creating = id === 'new';
  const query = useApiQuery(useCallback((signal) => client.adminGetCatalogEntry(id, signal), [client, id]), { enabled: !creating });
  const config = useApiQuery(useCallback((signal) => client.getExerciseCatalogConfig(signal), [client]));
  const [mode, setMode] = useState('');
  const [note, setNote] = useState('');
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const [confirm, setConfirm] = useState('');
  const entry = query.data;
  const saved = (value) => { setMode(''); setError(''); setNote(''); if (creating) navigate('/admin/catalog/' + encodeURIComponent(value.id), { replace: true }); else query.reload(); };
  const run = async (action) => {
    if (action === 'reject' && !note.trim()) { setError(t.noteRequired); return; }
    setBusy(true); setError('');
    const methods = { submit: 'adminSubmitCatalogEntry', approve: 'adminApproveCatalogEntry', reject: 'adminRejectCatalogEntry', archive: 'adminArchiveCatalogEntry' };
    try { const value = await client[methods[action]](entry.id, { expectedRevision: entry.revision, note: note.trim() }); saved(value); setConfirm(''); }
    catch (err) { setError(fail(err)); setConfirm(''); } finally { setBusy(false); }
  };
  return <div className="view stack"><Link to="/admin/catalog">{t.back}</Link><PageHeader title={creating ? t.create : entry?.name || t.title} summary={entry ? t.statuses[entry.status] : t.adminSummary} /><ErrorBanner message={query.error || config.error ? t.error : error} onRetry={query.error ? query.reload : config.error ? config.reload : undefined} />{(query.loading && !creating || config.loading) && <Loading />}
    {config.data && (creating || mode === 'edit') && (creating || entry?.viewer?.canEdit) && <CatalogForm key={entry?.revision || 'new'} client={client} entry={creating ? null : entry} config={config.data} onSaved={saved} onCancel={() => creating ? navigate('/admin/catalog') : setMode('')} />}
    {entry && !creating && mode !== 'edit' && <>
      {entry.reviewNote && <p className="banner banner-info">{entry.reviewNote}</p>}
      <div className="badge-row">{entry.viewer?.canEdit && <><button className="button button-secondary" disabled={busy} onClick={() => setMode('edit')}>{t.edit}</button><button className="button button-secondary" disabled={busy} onClick={() => setMode(mode === 'relationships' ? '' : 'relationships')}>{t.editRelationships}</button></>}{entry.viewer?.canSubmit && <button className="button button-primary" disabled={busy} onClick={() => run('submit')}>{t.submit}</button>}{entry.viewer?.canReview && <><button className="button button-primary" disabled={busy} onClick={() => setConfirm('approve')}>{t.approve}</button><button className="button button-secondary" disabled={busy} onClick={() => run('reject')}>{t.reject}</button></>}{isSystemAdmin && entry.status !== 'Archived' && <button className="button button-secondary" disabled={busy} onClick={() => setConfirm('archive')}>{t.archive}</button>}</div>
      {entry.viewer?.canReview && <label className="field"><span className="label-text">{t.reviewNote}</span><textarea value={note} onChange={(e) => setNote(e.target.value)} maxLength={2000} /></label>}
      {mode === 'relationships' ? <RelationshipEditor key={entry.revision} client={client} entry={entry} onSaved={saved} onCancel={() => setMode('')} /> : <><CatalogEntryContent entry={entry} config={config.data} /><CatalogRelationships key={entry.revision} client={client} entryId={entry.id} admin /></>}
    </>}
    <ConfirmModal open={Boolean(confirm)} title={confirm === 'approve' ? t.confirmApprove : t.confirmArchive} message={t.confirmBody} confirmLabel={confirm === 'approve' ? t.approve : t.archive} busy={busy} danger={confirm !== 'approve'} onCancel={() => setConfirm('')} onConfirm={() => run(confirm)} />
  </div>;
}

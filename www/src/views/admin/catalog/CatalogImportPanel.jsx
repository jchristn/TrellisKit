import { useCallback, useState } from 'react';
import { Link } from 'react-router-dom';
import useCursorList from '../../../hooks/useCursorList.js';
import MetadataEditor from '../../../components/MetadataEditor.jsx';
import MetadataFilterBar from '../../../components/MetadataFilterBar.jsx';
import Modal from '../../../components/Modal.jsx';
import { ErrorBanner, Loading } from '../../../components/States.jsx';
import strings from '../../../strings.js';

const t = strings.catalogImport;

const sourceBody = (form) => ({ name: form.name.trim(), url: form.url.trim(), license: form.license, attribution: form.attribution.trim() || null, enabled: form.enabled, maxEntries: Number(form.maxEntries), labels: form.labels, tags: form.tags, ...(form.revision ? { expectedRevision: form.revision } : {}) });
const blank = () => ({ name: '', url: '', license: 'CC0-1.0', attribution: '', enabled: false, maxEntries: 20, labels: [], tags: {} });
const errorText = (error) => error?.code === 'CatalogSourceChanged' ? t.changed : error?.message || t.errors;

function SourceEditor({ client, source, onClose, onSaved }) {
  const [form, setForm] = useState(() => source ? { ...source, attribution: source.attribution || '' } : blank());
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [metadataInvalid, setMetadataInvalid] = useState(false);
  const set = (key, value) => setForm((current) => ({ ...current, [key]: value }));
  async function save(event) {
    event.preventDefault(); if (busy || metadataInvalid) return;
    setBusy(true); setError('');
    try { if (source) await client.adminUpdateCatalogImportSource(source.id, sourceBody(form)); else await client.adminCreateCatalogImportSource(sourceBody(form)); onSaved(); }
    catch (err) { setError(errorText(err)); }
    finally { setBusy(false); }
  }
  return <Modal open title={source ? t.edit : t.create} onClose={busy ? () => {} : onClose} size="lg">
    <form className="stack" onSubmit={save}><p>{t.editHint}</p><p className="muted small">{t.format}</p><ErrorBanner message={error} /><fieldset className="fieldset stack" disabled={busy}>
      <label className="field"><span className="label-text">{t.name}</span><input required minLength={2} maxLength={200} value={form.name} onChange={(e) => set('name', e.target.value)} /></label>
      <label className="field"><span className="label-text">{t.url}</span><input required type="url" pattern="https://.*" maxLength={2000} value={form.url} onChange={(e) => set('url', e.target.value)} /></label>
      <label className="field"><span className="label-text">{t.license}</span><select value={form.license} onChange={(e) => set('license', e.target.value)}>{['CC0-1.0', 'CC-BY-4.0', 'PublicDomain', 'Unlicense'].map((key) => <option key={key}>{key}</option>)}</select></label>
      <label className="field"><span className="label-text">{t.attribution}</span><textarea maxLength={2000} required={form.license === 'CC-BY-4.0'} value={form.attribution} onChange={(e) => set('attribution', e.target.value)} /></label>
      <label className="field"><span className="label-text">{t.maximum}</span><input required type="number" min={1} max={100} step={1} value={form.maxEntries} onChange={(e) => set('maxEntries', e.target.value)} /></label>
      <label className="check-field"><input type="checkbox" checked={form.enabled} onChange={(e) => set('enabled', e.target.checked)} /><span>{t.enabled}</span></label>
      <MetadataEditor value={form} onChange={(next, problems) => { setForm((current) => ({ ...current, ...next })); setMetadataInvalid(problems.length > 0); }} client={client} isStaff />
      <div className="form-actions"><button type="button" className="button button-secondary" onClick={onClose}>{t.cancel}</button><button className="button button-primary" disabled={metadataInvalid}>{t.save}</button></div>
    </fieldset></form>
  </Modal>;
}

function SourceReview({ client, source, onClose, onSaved }) {
  const [acknowledged, setAcknowledged] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  async function review(event) {
    event.preventDefault(); if (!acknowledged || busy) return; setBusy(true); setError('');
    try { await client.adminReviewCatalogImportSource(source.id, { expectedRevision: source.revision, licenseReviewed: true }); onSaved(); }
    catch (err) { setError(errorText(err)); }
    finally { setBusy(false); }
  }
  return <Modal open title={t.review} onClose={busy ? () => {} : onClose}>
    <form className="stack" onSubmit={review}><p>{source.name}</p><a href={source.url} target="_blank" rel="noreferrer">{source.url}</a><p>{source.license}</p>{source.attribution && <p>{source.attribution}</p>}<p>{t.hint}</p><ErrorBanner message={error} />
      <label className="check-field"><input type="checkbox" required disabled={busy} checked={acknowledged} onChange={(e) => setAcknowledged(e.target.checked)} /><span>{t.acknowledge}</span></label>
      <div className="form-actions"><button type="button" className="button button-secondary" disabled={busy} onClick={onClose}>{t.cancel}</button><button className="button button-primary" disabled={busy || !acknowledged}>{t.confirm}</button></div>
    </form>
  </Modal>;
}

function SourceManager({ client }) {
  const [meta, setMeta] = useState({});
  const metaKey = JSON.stringify(meta);
  const list = useCursorList(useCallback((cursor, signal) => client.adminEnumerateCatalogImportSources({ ...JSON.parse(metaKey), maxResults: 20, continuationToken: cursor }, signal), [client, metaKey]));
  const [editor, setEditor] = useState(null);
  const [review, setReview] = useState(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [queued, setQueued] = useState(false);
  async function open(source, mode) {
    setBusy(true); setError('');
    try { const fresh = await client.adminGetCatalogImportSource(source.id); if (mode === 'review') setReview(fresh); else setEditor(fresh); }
    catch (err) { setError(errorText(err)); } finally { setBusy(false); }
  }
  async function run(source, dryRun) {
    setBusy(true); setError(''); setQueued(false);
    try { await client.adminRunJob('catalog.importSources', dryRun, JSON.stringify({ sourceIds: [source.id] })); setQueued(true); }
    catch (err) { setError(errorText(err)); } finally { setBusy(false); }
  }
  const saved = () => { setEditor(null); setReview(null); list.refresh(); };
  return <div className="stack"><p>{t.hint}</p><p>{t.dryHint}</p><div className="badge-row"><button className="button button-secondary" disabled={busy} onClick={() => setEditor({})}>{t.create}</button><Link to="/admin/jobs">{t.jobs}</Link></div>
    <MetadataFilterBar value={meta} onChange={setMeta} client={client} showSearch searchLabel={t.search} />
    <ErrorBanner message={list.error ? t.errors : error} onRetry={list.error ? list.refresh : undefined} />{queued && <p className="banner banner-info" role="status">{t.queued}</p>}{list.loading && <Loading label={t.loading} />}
    {!list.loading && !list.error && !list.items.length && <p>{t.empty}</p>}
    <ul className="p4-list">{list.items.map((source) => <li className="panel stack" key={source.id}><h3>{source.name}</h3><p className="muted small">{source.license} · {source.reviewedUtc ? t.reviewed : t.unreviewed}{!source.enabled && ` · ${t.disabled}`}</p><p className="muted small">{t.lastAttempt}: {source.lastAttemptUtc ? new Date(source.lastAttemptUtc).toLocaleString() : t.never}</p><div className="badge-row"><button className="button button-secondary" disabled={busy} onClick={() => open(source, 'edit')}>{t.edit}</button><button className="button button-secondary" disabled={busy} onClick={() => open(source, 'review')}>{t.review}</button>{source.enabled && source.reviewedUtc && <><button className="button button-secondary" disabled={busy} onClick={() => run(source, true)}>{t.preview}</button><button className="button button-primary" disabled={busy} onClick={() => run(source, false)}>{t.run}</button></>}</div></li>)}</ul>
    {list.loaded && !list.done && list.items.length > 0 && <button className="button button-secondary" disabled={list.loading} onClick={list.loadMore}>{t.more}</button>}
    {editor && <SourceEditor client={client} source={editor.id ? editor : null} onClose={() => setEditor(null)} onSaved={saved} />}
    {review && <SourceReview client={client} source={review} onClose={() => setReview(null)} onSaved={saved} />}
  </div>;
}

export default function CatalogImportPanel({ client }) {
  const [open, setOpen] = useState(false);
  return <section className="panel stack"><h2>{t.title}</h2><button className="button button-secondary" aria-expanded={open} onClick={() => setOpen(!open)}>{open ? t.hide : t.show}</button>{open && <SourceManager client={client} />}</section>;
}

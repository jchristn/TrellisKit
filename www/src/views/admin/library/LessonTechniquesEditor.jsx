import { useCallback, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import strings from '../../../strings.js';
import Icon from '../../../components/Icon.jsx';
import EntityPicker from '../../../components/admin/EntityPicker.jsx';
import { ErrorBanner, Loading } from '../../../components/States.jsx';
import { formatClock, parseClock } from '../../training/trainingModel.js';

const t = strings.training.admin;

/** Most techniques one lesson may link (`PUT /admin/lessons/{id}/catalog-entries`). */
export const LESSON_TECHNIQUES_MAX = 50;

/** The request items for the rows (`{ catalogEntryId, startSeconds, position }`), or null when a start time is malformed. */
export function lessonTechniqueItems(rows) {
  const items = [];
  for (const [index, row] of rows.entries()) {
    const seconds = parseClock(row.start);
    if (Number.isNaN(seconds)) return null;
    items.push({ catalogEntryId: row.id, startSeconds: seconds, position: index });
  }
  return items;
}

function rowOf(entry) {
  return { id: entry.id, name: entry.name || entry.id, status: entry.status || null, start: Number.isFinite(entry.startSeconds) ? formatClock(entry.startSeconds) : '' };
}

/**
 * "Techniques in this lesson" on the admin lesson form: search published catalog entries, set an
 * optional start time (m:ss) and the order, and save the whole list at once. The current links must
 * load before editing, so a failed load never replaces them with an empty list.
 * @param {{ client: object, lessonId: string }} props
 */
function LessonTechniquesEditor({ client, lessonId }) {
  const [rows, setRows] = useState(null);
  const [loadError, setLoadError] = useState(null);
  const [attempt, setAttempt] = useState(0);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [status, setStatus] = useState('');

  useEffect(() => {
    const controller = new AbortController();
    const load = async () => {
      const all = [];
      let token = null;
      do {
        const page = await client.listLessonCatalogEntries(lessonId, { maxResults: LESSON_TECHNIQUES_MAX, continuationToken: token }, controller.signal);
        all.push(...((page && page.objects) || []));
        token = page && !page.endOfResults ? page.continuationToken : null;
      } while (token && all.length < LESSON_TECHNIQUES_MAX);
      return all;
    };
    load().then(
      (entries) => { if (!controller.signal.aborted) { setRows(entries.slice().sort((a, b) => (a.position ?? 0) - (b.position ?? 0)).map(rowOf)); setLoadError(null); } },
      (err) => { if (!controller.signal.aborted) setLoadError(err || new Error('load')); }
    );
    return () => controller.abort();
  }, [client, lessonId, attempt]);

  const search = useCallback(async (term, signal) => {
    const page = await client.adminEnumerateCatalogEntries({ search: term, maxResults: 10, filters: { status: 'Published' } }, signal);
    return ((page && page.objects) || []).map((entry) => ({ id: entry.id, label: entry.name, entry }));
  }, [client]);

  const change = (index, patch) => setRows((current) => current.map((row, i) => (i === index ? { ...row, ...patch } : row)));
  const move = (index, delta) => setRows((current) => {
    const next = current.slice();
    const target = index + delta;
    if (target < 0 || target >= next.length) return current;
    [next[index], next[target]] = [next[target], next[index]];
    return next;
  });
  const add = (picked) => {
    const item = picked && picked[0];
    if (!item || rows.some((row) => row.id === item.id)) return;
    if (rows.length >= LESSON_TECHNIQUES_MAX) { setError(t.tooMany(LESSON_TECHNIQUES_MAX)); return; }
    setError('');
    setRows([...rows, rowOf(item.entry || { id: item.id, name: item.label })]);
  };
  const save = async () => {
    const items = lessonTechniqueItems(rows);
    if (!items) { setError(t.startTimeInvalid); return; }
    setBusy(true);
    setError('');
    setStatus('');
    try {
      const saved = await client.adminSetLessonCatalogEntries(lessonId, items);
      const objects = Array.isArray(saved) ? saved : (saved && saved.objects) || null;
      if (objects) setRows(objects.map(rowOf));
      setStatus(t.saved);
    } catch (err) {
      setError(err && err.code === 'CatalogEntryNotPublished' ? t.notPublished : err && err.status === 400 ? t.startTimeInvalid : t.saveFailed);
    } finally {
      setBusy(false);
    }
  };

  return (
    <section className="panel stack" aria-labelledby="lesson-techniques-admin">
      <h2 id="lesson-techniques-admin" className="section-title">{t.techniquesTitle}</h2>
      <p className="muted small">{t.techniquesIntro}</p>
      {loadError && <ErrorBanner message={t.techniquesLoadError} onRetry={() => { setLoadError(null); setRows(null); setAttempt((n) => n + 1); }} />}
      {!rows && !loadError && <Loading inline />}
      {rows && (
        <>
          <EntityPicker label={t.techniquesSearch} value={[]} onChange={add} search={search} disabled={busy} />
          {!rows.length && <p className="muted small">{t.techniquesEmpty}</p>}
          {rows.length > 0 && (
            <ol className="training-collections" data-testid="lesson-techniques-editor">
              {rows.map((row, index) => (
                <li key={row.id} className="training-collection-row">
                  <span className="stack">
                    <Link to={'/admin/catalog/' + encodeURIComponent(row.id)} className="training-list-title">{row.name}</Link>
                    {row.status && row.status !== 'Published' && <span className="small warning-text">{t.unpublishedWarning}</span>}
                  </span>
                  <span className="training-actions">
                    <label className="field field-compact">
                      <span className="visually-hidden">{t.startTimeFor(row.name)}</span>
                      <input className="input input-compact" inputMode="numeric" placeholder="0:00" size={6} value={row.start} aria-invalid={Number.isNaN(parseClock(row.start)) || undefined} onChange={(e) => change(index, { start: e.target.value })} />
                    </label>
                    <button type="button" className="icon-button" aria-label={t.moveUp(row.name)} disabled={busy || index === 0} onClick={() => move(index, -1)}><Icon name="chevronUp" size={16} /></button>
                    <button type="button" className="icon-button" aria-label={t.moveDown(row.name)} disabled={busy || index === rows.length - 1} onClick={() => move(index, 1)}><Icon name="chevronDown" size={16} /></button>
                    <button type="button" className="icon-button" aria-label={t.remove(row.name)} disabled={busy} onClick={() => setRows((current) => current.filter((_, i) => i !== index))}><Icon name="trash" size={16} /></button>
                  </span>
                </li>
              ))}
            </ol>
          )}
          <ErrorBanner message={error} />
          <span className="visually-hidden" role="status" aria-live="polite">{status}</span>
          {status && <p className="success-text small" aria-hidden="true">{status}</p>}
          <div className="form-actions-start">
            <button type="button" className="button button-primary button-sm" disabled={busy} onClick={save}>{t.save}</button>
          </div>
        </>
      )}
    </section>
  );
}

export default LessonTechniquesEditor;

/**
 * "Taught in lessons" on the admin catalog entry: the lessons linked to this entry
 * (`POST /catalog/entries/{id}/lessons/enumerate`), each opening the lesson editor.
 * @param {{ client: object, entryId: string }} props
 */
export function TaughtInLessons({ client, entryId }) {
  const on = Boolean(client && typeof client.listCatalogEntryLessons === 'function');
  const [state, setState] = useState({ rows: [], token: null, loaded: false, error: null, loading: false });
  const load = useCallback(async (token, signal) => {
    setState((current) => ({ ...current, loading: true, error: null }));
    try {
      const page = await client.listCatalogEntryLessons(entryId, { maxResults: 25, continuationToken: token }, signal);
      setState((current) => ({ rows: [...(token ? current.rows : []), ...((page && page.objects) || [])], token: page && !page.endOfResults ? page.continuationToken : null, loaded: true, error: null, loading: false }));
    } catch (err) {
      if (!signal || !signal.aborted) setState((current) => ({ ...current, loaded: true, error: err, loading: false }));
    }
  }, [client, entryId]);
  useEffect(() => {
    if (!on) return undefined;
    const controller = new AbortController();
    load(null, controller.signal);
    return () => controller.abort();
  }, [on, load]);
  if (!on) return null;
  return (
    <section className="panel stack" aria-labelledby="catalog-taught-in">
      <h2 id="catalog-taught-in" className="section-title">{t.taughtTitle}</h2>
      <ErrorBanner message={state.error ? t.taughtError : ''} onRetry={() => load(state.rows.length ? state.token : null)} />
      {state.loading && !state.loaded && <Loading inline />}
      {state.loaded && !state.rows.length && !state.error && <p className="muted small">{t.taughtEmpty}</p>}
      {state.rows.length > 0 && (
        <ul className="training-list" data-testid="catalog-taught-in">
          {state.rows.map((lesson) => (
            <li key={lesson.id}>
              <Link to={'/admin/library/lessons/' + encodeURIComponent(lesson.id)} className="training-list-link">
                <span className="training-list-title">{lesson.title}</span>
                {lesson.series && <span className="muted small">{lesson.series.title}</span>}
              </Link>
            </li>
          ))}
        </ul>
      )}
      {state.token && !state.loading && <button type="button" className="button button-secondary button-sm" onClick={() => load(state.token)}>{strings.training.search.more}</button>}
    </section>
  );
}

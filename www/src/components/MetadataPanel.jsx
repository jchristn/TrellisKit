import { useCallback, useState } from 'react';
import { metadataForSave, metadataOf } from '@trelliskit/shared';
import strings from '../strings.js';
import useApiQuery from '../hooks/useApiQuery.js';
import { ErrorBanner, Loading } from './States.jsx';
import MetadataEditor from './MetadataEditor.jsx';

const m = strings.metadata;

/**
 * Labels and tags of one object through the generic routes (`GET` and `PUT
 * /metadata/{targetType}/{targetId}`), for detail views without their own update route.
 * `initial` (the object's `labels` and `tags` from its payload) skips the first request.
 * @param {{ client: object, targetType: string, targetId: string, isStaff?: boolean, initial?: object|null, onSaved?: (meta: object) => void, title?: string }} props
 */
function MetadataPanel({ client, targetType, targetId, isStaff = false, initial = null, onSaved, title = m.editorTitle }) {
  const fetcher = useCallback((signal) => client.getMetadata(targetType, targetId, signal), [client, targetType, targetId]);
  const query = useApiQuery(fetcher, { enabled: !initial });
  const loaded = initial ? metadataOf(initial) : (query.data ? metadataOf(query.data) : null);
  const [draft, setDraft] = useState(null);
  const [problems, setProblems] = useState([]);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [savedAt, setSavedAt] = useState(0);
  const value = draft || loaded;

  const save = async () => {
    setBusy(true);
    setError('');
    try {
      const body = metadataForSave(draft, { isStaff });
      const result = await client.putMetadata(targetType, targetId, body);
      setDraft(null);
      setSavedAt(Date.now());
      if (onSaved) onSaved(metadataOf(result && result.labels ? result : body));
    } catch (err) {
      setError((err && err.message) || strings.common.errorGeneric);
    } finally {
      setBusy(false);
    }
  };

  return (
    <section className="detail-block metadata-panel" aria-label={title}>
      {!value && query.loading && <Loading inline />}
      <ErrorBanner message={query.error && !value ? m.loadError : error} onRetry={query.error && !value ? query.reload : undefined} />
      {value && (
        <>
          <MetadataEditor key={savedAt} value={value} onChange={(next, found) => { setDraft(next); setProblems(found); }} isStaff={isStaff} client={client} targetType={targetType} disabled={busy} legend={title} />
          <div className="form-actions">
            <button type="button" className="button button-primary button-sm" onClick={save} disabled={busy || !draft || problems.length > 0}>{m.save}</button>
          </div>
        </>
      )}
    </section>
  );
}

export default MetadataPanel;

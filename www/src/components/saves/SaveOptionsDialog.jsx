import { useCallback, useId, useState } from 'react';
import { SAVE_COLLECTION_NAME_MAX_LENGTH, SAVE_MAX_COLLECTIONS_PER_REQUEST, SAVE_NOTE_MAX_LENGTH } from '@trelliskit/shared';
import strings from '../../strings.js';
import useApiQuery from '../../hooks/useApiQuery.js';
import Modal from '../Modal.jsx';
import { ErrorBanner, Loading } from '../States.jsx';

const t = strings.training.save;

/**
 * Collections and note for one saved item (`PUT /me/saves/{type}/{id}` with `collectionIds` and
 * `note`). Saving here also saves the item when it was not saved yet. A new collection is created
 * at once and selected. The note is visible to the member and moderators.
 * @param {{ client: object, title: string, targetType: string, targetId: string,
 *   initial?: { collectionIds?: string[], note?: string|null }, onClose: () => void,
 *   onSaved: (favorite: object) => void }} props
 */
function SaveOptionsDialog({ client, title, targetType, targetId, initial = null, onClose, onSaved }) {
  const noteId = useId();
  const hintId = useId();
  const fetcher = useCallback((signal) => client.listSaveCollections({ maxResults: 100 }, signal), [client]);
  const collections = useApiQuery(fetcher);
  const [selected, setSelected] = useState(() => new Set((initial && initial.collectionIds) || []));
  const [note, setNote] = useState((initial && initial.note) || '');
  const [created, setCreated] = useState([]);
  const [name, setName] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const rows = [...((collections.data && collections.data.objects) || []), ...created.filter((row) => !((collections.data && collections.data.objects) || []).some((c) => c.id === row.id))];

  const toggle = (id) => setSelected((current) => {
    const next = new Set(current);
    if (next.has(id)) next.delete(id);
    else next.add(id);
    return next;
  });
  const addCollection = async () => {
    const value = name.trim();
    if (!value) { setError(strings.training.saved.nameRequired); return; }
    setBusy(true);
    setError('');
    try {
      const collection = await client.createSaveCollection({ name: value });
      setCreated((current) => [...current, collection]);
      setSelected((current) => new Set(current).add(collection.id));
      setName('');
    } catch {
      setError(strings.training.saved.collectionFailed);
    } finally {
      setBusy(false);
    }
  };
  const apply = async (event) => {
    if (event) event.preventDefault();
    if (note.length > SAVE_NOTE_MAX_LENGTH) { setError(t.noteTooLong(SAVE_NOTE_MAX_LENGTH)); return; }
    if (selected.size > SAVE_MAX_COLLECTIONS_PER_REQUEST) { setError(t.tooManyCollections(SAVE_MAX_COLLECTIONS_PER_REQUEST)); return; }
    setBusy(true);
    setError('');
    try {
      const favorite = await client.saveItem(targetType, targetId, { collectionIds: [...selected], note: note.trim() ? note.trim() : null });
      onSaved(favorite);
    } catch {
      setError(strings.training.save.failed);
      setBusy(false);
    }
  };

  return (
    <Modal
      open
      onClose={busy ? () => {} : onClose}
      title={t.dialogTitle(title)}
      size="sm"
      footer={(
        <>
          <button type="button" className="button button-secondary" onClick={onClose} disabled={busy}>{t.cancel}</button>
          <button type="submit" form={noteId + '-form'} className="button button-primary" disabled={busy}>{t.apply}</button>
        </>
      )}
    >
      <form id={noteId + '-form'} className="stack-form" onSubmit={apply} noValidate>
        <ErrorBanner message={error} />
        <fieldset className="save-collections">
          <legend className="label-text">{t.collectionsLegend}</legend>
          {collections.loading && !collections.data && <Loading inline />}
          <ErrorBanner message={collections.error ? strings.training.saved.collectionsError : ''} onRetry={collections.reload} />
          {collections.data && !rows.length && <p className="muted small">{t.noCollections}</p>}
          {rows.map((collection) => (
            <label key={collection.id} className="check-field">
              <input type="checkbox" checked={selected.has(collection.id)} onChange={() => toggle(collection.id)} />
              <span>{collection.name}</span>
            </label>
          ))}
          <div className="save-new-collection">
            <label className="field field-compact">
              <span className="label-text">{t.newCollection}</span>
              <input className="input" value={name} maxLength={SAVE_COLLECTION_NAME_MAX_LENGTH} onChange={(e) => setName(e.target.value)} onKeyDown={(e) => { if (e.key === 'Enter') { e.preventDefault(); addCollection(); } }} />
            </label>
            <button type="button" className="button button-secondary button-sm" disabled={busy || !name.trim()} onClick={addCollection}>{t.addCollection}</button>
          </div>
        </fieldset>
        <div className="field">
          <label className="label-text" htmlFor={noteId}>{t.note}</label>
          <textarea id={noteId} className="input" rows={3} value={note} maxLength={SAVE_NOTE_MAX_LENGTH} aria-describedby={hintId} onChange={(e) => setNote(e.target.value)} />
          <span id={hintId} className="muted small">{t.noteHint}</span>
        </div>
      </form>
    </Modal>
  );
}

export default SaveOptionsDialog;

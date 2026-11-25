import { useCallback, useState } from 'react';
import { Navigate, useSearchParams } from 'react-router-dom';
import { FavoriteTargetTypes, SAVE_COLLECTION_NAME_MAX_LENGTH, saveAvailable } from '@trelliskit/shared';
import strings from '../../strings.js';
import { useAuth } from '../../context/AuthContext.jsx';
import { useProperty } from '../../context/PropertyContext.jsx';
import useApiQuery from '../../hooks/useApiQuery.js';
import useCursorList from '../../hooks/useCursorList.js';
import Icon from '../../components/Icon.jsx';
import ConfirmModal from '../../components/ConfirmModal.jsx';
import SaveOptionsDialog from '../../components/saves/SaveOptionsDialog.jsx';
import { hasSavesApi, saveStateFor } from '../../components/saves/saveState.js';
import { EmptyState, ErrorBanner, Loading } from '../../components/States.jsx';
import { SavedTargetLink } from './OverviewView.jsx';
import { savesEnabled, trainingPaths } from './trainingModel.js';

const t = strings.training.saved;

/**
 * Collection management: create, rename, reorder (move up and down), and delete. Deleting a
 * collection keeps its saves. Collections are private to the member.
 */
function Collections({ client, collections, onChanged }) {
  const [name, setName] = useState('');
  const [renaming, setRenaming] = useState(null);
  const [deleting, setDeleting] = useState(null);
  const [busy, setBusy] = useState(false);
  const [status, setStatus] = useState('');
  const [error, setError] = useState('');
  const rows = (collections.data && collections.data.objects) || [];

  const run = async (fn, done) => {
    setBusy(true);
    setError('');
    setStatus('');
    try {
      await fn();
      setStatus(done);
      onChanged();
      return true;
    } catch {
      setError(t.collectionFailed);
      return false;
    } finally {
      setBusy(false);
    }
  };
  const create = async (event) => {
    event.preventDefault();
    const value = name.trim();
    if (!value) { setError(t.nameRequired); return; }
    if (await run(() => client.createSaveCollection({ name: value }), t.created)) setName('');
  };
  const rename = async (event) => {
    event.preventDefault();
    const value = renaming.name.trim();
    if (!value) { setError(t.nameRequired); return; }
    if (await run(() => client.updateSaveCollection(renaming.id, { name: value }), t.renamed)) setRenaming(null);
  };
  const move = (index, delta) => {
    const ids = rows.map((row) => row.id);
    const target = index + delta;
    if (target < 0 || target >= ids.length) return;
    [ids[index], ids[target]] = [ids[target], ids[index]];
    run(() => client.orderSaveCollections(ids), t.reordered);
  };

  return (
    <section className="panel stack" aria-labelledby="saved-collections-title" data-testid="saved-collections">
      <h2 id="saved-collections-title" className="section-title">{t.collectionsTitle}</h2>
      <p className="muted small">{t.collectionsIntro}</p>
      <span className="visually-hidden" role="status" aria-live="polite">{status}</span>
      <ErrorBanner message={error || (collections.error ? t.collectionsError : '')} onRetry={collections.error ? collections.reload : undefined} />
      {collections.loading && !collections.data && <Loading inline />}
      {collections.data && !rows.length && <p className="muted small">{t.collectionsEmpty}</p>}
      {rows.length > 0 && (
        <ol className="training-collections">
          {rows.map((row, index) => (
            <li key={row.id} className="training-collection-row" data-testid={'collection-' + row.id}>
              {renaming && renaming.id === row.id ? (
                <form className="training-inline-form" onSubmit={rename}>
                  <label className="visually-hidden" htmlFor={'rename-' + row.id}>{t.renameLabel(row.name)}</label>
                  <input id={'rename-' + row.id} className="input input-compact" value={renaming.name} maxLength={SAVE_COLLECTION_NAME_MAX_LENGTH} onChange={(e) => setRenaming({ ...renaming, name: e.target.value })} />
                  <button type="submit" className="button button-primary button-sm" disabled={busy}>{t.renameSave}</button>
                  <button type="button" className="button button-secondary button-sm" onClick={() => setRenaming(null)}>{strings.training.save.cancel}</button>
                </form>
              ) : (
                <>
                  <span className="training-list-title">{row.name}</span>
                  <span className="training-actions">
                    <button type="button" className="icon-button" aria-label={t.moveUp(row.name)} disabled={busy || index === 0} onClick={() => move(index, -1)}><Icon name="chevronUp" size={16} /></button>
                    <button type="button" className="icon-button" aria-label={t.moveDown(row.name)} disabled={busy || index === rows.length - 1} onClick={() => move(index, 1)}><Icon name="chevronDown" size={16} /></button>
                    <button type="button" className="button button-secondary button-sm" aria-label={t.renameLabel(row.name)} disabled={busy} onClick={() => setRenaming({ id: row.id, name: row.name })}>{t.rename}</button>
                    <button type="button" className="button button-secondary button-sm" aria-label={t.deleteLabel(row.name)} disabled={busy} onClick={() => setDeleting(row)}>{t.delete}</button>
                  </span>
                </>
              )}
            </li>
          ))}
        </ol>
      )}
      <form className="training-inline-form" onSubmit={create}>
        <label className="field field-compact">
          <span className="label-text">{t.collectionName}</span>
          <input className="input input-compact" value={name} maxLength={SAVE_COLLECTION_NAME_MAX_LENGTH} onChange={(e) => setName(e.target.value)} />
        </label>
        <button type="submit" className="button button-secondary button-sm" disabled={busy}>{t.create}</button>
      </form>
      <ConfirmModal
        open={Boolean(deleting)}
        title={deleting ? t.deleteTitle(deleting.name) : ''}
        message={t.deleteBody}
        confirmLabel={t.delete}
        busy={busy}
        onCancel={() => setDeleting(null)}
        onConfirm={async () => { const id = deleting.id; if (await run(() => client.deleteSaveCollection(id), t.deleted)) setDeleting(null); }}
      />
    </section>
  );
}

/**
 * The Library's Saved segment (`/app/training/library/saved`): the member's saves, newest first,
 * filtered by type (`?type=`) and collection (`?collection=`), with each item's note (visible to
 * the member and moderators), Edit (collections and note), and Remove (the row goes at once and
 * comes back when the server refuses). Targets the member can no longer open show as unavailable
 * without their title. Collections are managed beside the list.
 */
function SavedView() {
  const { client, me } = useAuth();
  const { property } = useProperty();
  const [params, setParams] = useSearchParams();
  const type = FavoriteTargetTypes.includes(params.get('type')) ? params.get('type') : '';
  const collectionId = params.get('collection') || '';
  const [removed, setRemoved] = useState(() => new Set());
  const [edits, setEdits] = useState({});
  const [editing, setEditing] = useState(null);
  const [message, setMessage] = useState('');
  const enabled = Boolean(me && savesEnabled(property) && hasSavesApi(client));
  const fetchPage = useCallback((cursor, signal) => {
    const filters = {};
    if (type) filters.targetTypes = [type];
    if (collectionId) filters.collectionId = collectionId;
    return client.listSaves({ maxResults: 25, continuationToken: cursor, filters }, signal);
  }, [client, type, collectionId]);
  const list = useCursorList(fetchPage, { enabled });
  const collectionsFetcher = useCallback((signal) => client.listSaveCollections({ maxResults: 100 }, signal), [client]);
  const collections = useApiQuery(collectionsFetcher, { enabled });
  if (!enabled) return <Navigate to={trainingPaths.segment('courses')} replace />;
  const collectionRows = (collections.data && collections.data.objects) || [];
  const items = list.items.filter((save) => !removed.has(save.id)).map((save) => (edits[save.id] ? { ...save, ...edits[save.id] } : save));

  const setParam = (key, value) => {
    const next = new URLSearchParams(params);
    if (value) next.set(key, value);
    else next.delete(key);
    setParams(next);
  };
  const remove = async (save) => {
    setMessage('');
    setRemoved((current) => new Set(current).add(save.id));
    try {
      await client.unsaveItem(save.targetType, save.targetId);
      const batcher = saveStateFor(client, me.id);
      if (batcher) batcher.set(save.targetType, save.targetId, { saved: false, favoriteId: null });
    } catch {
      setRemoved((current) => { const next = new Set(current); next.delete(save.id); return next; });
      setMessage(t.removeFailed);
    }
  };
  const titleOf = (save) => (saveAvailable(save) ? save.target.title || t.typeOne[save.targetType] : strings.training.savedUnavailable);

  return (
    <div className="training-saved">
      <section className="stack" aria-label={t.title}>
        <div className="chip-row" role="group" aria-label={t.filterLabel}>
          {['', ...FavoriteTargetTypes].map((value) => (
            <button key={value || 'all'} type="button" className={'chip-toggle' + (type === value ? ' active' : '')} aria-pressed={type === value} onClick={() => setParam('type', value)} data-testid={'saved-type-' + (value || 'all')}>
              {value ? t.types[value] : t.all}
            </button>
          ))}
        </div>
        {collectionRows.length > 0 && (
          <label className="field field-compact">
            <span className="label-text">{t.collectionFilterLabel}</span>
            <select className="select-compact" value={collectionId} onChange={(e) => setParam('collection', e.target.value)}>
              <option value="">{t.allCollections}</option>
              {collectionRows.map((row) => <option key={row.id} value={row.id}>{row.name}</option>)}
            </select>
          </label>
        )}
        <span className="visually-hidden" role="status" aria-live="polite">{message}</span>
        <ErrorBanner message={message || (list.error ? t.loadError : '')} onRetry={list.error ? list.refresh : undefined} onDismiss={message ? () => setMessage('') : undefined} />
        {!list.loaded && list.loading && <Loading />}
        {list.loaded && !items.length && !list.loading && !list.error && <EmptyState icon="bookmark" title={type || collectionId ? t.emptyFiltered : t.empty} />}
        {items.some((save) => save.note) && <p className="muted small">{t.notesHint}</p>}
        <ul className="p4-list training-saved-list">
          {items.map((save) => {
            const title = titleOf(save);
            return (
              <li key={save.id} className="p4-row training-saved-row" data-testid={'saved-row-' + save.id}>
                <Icon name="bookmark" size={18} />
                <span className="p4-row-main">
                  <SavedTargetLink save={save} className="training-saved-link" />
                  {save.note && <span className="muted small preserve-lines">{t.noteLabel}: {save.note}</span>}
                </span>
                <span className="training-actions">
                  {saveAvailable(save) && <button type="button" className="button button-secondary button-sm" aria-label={t.editLabel(title)} onClick={() => setEditing(save)}>{t.edit}</button>}
                  <button type="button" className="button button-secondary button-sm" aria-label={t.remove(title)} onClick={() => remove(save)} data-testid={'saved-remove-' + save.id}>{t.removeShort}</button>
                </span>
              </li>
            );
          })}
        </ul>
        {list.loaded && !list.done && !list.loading && !list.error && <button type="button" className="button button-secondary" onClick={list.loadMore}>{t.loadMore}</button>}
      </section>
      <Collections client={client} collections={collections} onChanged={collections.reload} />
      {editing && (
        <SaveOptionsDialog
          client={client}
          title={titleOf(editing)}
          targetType={editing.targetType}
          targetId={editing.targetId}
          initial={{ collectionIds: editing.collectionIds || [], note: editing.note || '' }}
          onClose={() => setEditing(null)}
          onSaved={(favorite) => {
            const id = editing.id;
            setEdits((current) => ({ ...current, [id]: { note: favorite ? favorite.note : null, collectionIds: (favorite && favorite.collectionIds) || [] } }));
            setEditing(null);
            collections.reload();
            if (collectionId) list.refresh();
          }}
        />
      )}
    </div>
  );
}

export default SavedView;

import { useEffect, useRef, useState } from 'react';
import strings from '../strings.js';
import { useAuth } from '../context/AuthContext.jsx';
import { useProperty } from '../context/PropertyContext.jsx';
import { savesEnabled } from '../views/training/trainingModel.js';
import Icon from './Icon.jsx';
import SaveOptionsDialog from './saves/SaveOptionsDialog.jsx';
import { saveStateFor } from './saves/saveState.js';

const t = strings.training.save;

/**
 * The Save toggle for a lesson, series, catalog entry, venue, instructor, event, post, or thread
 * (REST_API.md, Saves). A `aria-pressed` toggle named "Save {title}" (or "Saved") plus a
 * "Collections" button that opens the collection picker and the note. The saved state comes from
 * the page's shared batcher (`createSaveStateBatcher`), so a list of toggles costs one request.
 * Pressing it updates at once and rolls back with a polite message when the server refuses.
 * Hidden for signed-out visitors, when `features.saves` is off, and when the client has no
 * saves routes.
 * @param {{ targetType: string, targetId: string, title: string, compact?: boolean, showOptions?: boolean }} props
 */
function SaveButton({ targetType, targetId, title, compact = false, showOptions = true }) {
  const { client, me } = useAuth();
  const { property } = useProperty();
  const userId = me ? me.id : null;
  const batcher = me && targetId && savesEnabled(property) ? saveStateFor(client, userId) : null;
  const cached = batcher ? batcher.peek(targetType, targetId) : undefined;
  const [state, setState] = useState(cached || null);
  const [message, setMessage] = useState('');
  const [picker, setPicker] = useState(null);
  const busy = useRef(false);
  const live = useRef(true);

  useEffect(() => {
    live.current = true;
    return () => { live.current = false; };
  }, []);

  useEffect(() => {
    if (!batcher) return undefined;
    let active = true;
    batcher.get(targetType, targetId).then((value) => { if (active) setState(value); }).catch(() => { if (active) setState({ saved: false, favoriteId: null }); });
    return () => { active = false; };
  }, [batcher, targetType, targetId]);

  if (!batcher || !state) return null;
  const saved = state.saved;

  const toggle = async () => {
    if (busy.current) return;
    busy.current = true;
    const previous = state;
    const next = { saved: !saved, favoriteId: saved ? null : previous.favoriteId };
    setState(next);
    batcher.set(targetType, targetId, next);
    setMessage('');
    try {
      if (saved) await client.unsaveItem(targetType, targetId);
      else {
        const favorite = await client.saveItem(targetType, targetId, {});
        const confirmed = { saved: true, favoriteId: (favorite && favorite.id) || null };
        batcher.set(targetType, targetId, confirmed);
        if (live.current) setState(confirmed);
      }
    } catch {
      batcher.set(targetType, targetId, previous);
      if (live.current) {
        setState(previous);
        setMessage(t.failed);
      }
    } finally {
      busy.current = false;
    }
  };

  const openPicker = async () => {
    setMessage('');
    if (!saved) { setPicker({ collectionIds: [], note: '' }); return; }
    try {
      // PUT without fields is idempotent and returns the current collections and note.
      const favorite = await client.saveItem(targetType, targetId, {});
      if (live.current) setPicker({ collectionIds: (favorite && favorite.collectionIds) || [], note: (favorite && favorite.note) || '' });
    } catch {
      if (live.current) setMessage(t.failed);
    }
  };

  return (
    <span className="save-toggle-wrap">
      <button
        type="button"
        className={'button button-secondary button-sm save-toggle' + (saved ? ' saved' : '')}
        aria-pressed={saved}
        aria-label={saved ? t.savedLabel : t.saveLabel(title)}
        onClick={toggle}
        data-testid={'save-' + targetType + '-' + targetId}
      >
        <Icon name="bookmark" size={14} />
        {!compact && <span aria-hidden="true">{saved ? t.saved : t.save}</span>}
      </button>
      {showOptions && (
        <button type="button" className="link-button small" aria-haspopup="dialog" aria-label={t.optionsLabel(title)} onClick={openPicker}>{t.options}</button>
      )}
      <span className="visually-hidden" role="status" aria-live="polite">{message}</span>
      {message && <span className="field-error small" aria-hidden="true">{message}</span>}
      {picker && (
        <SaveOptionsDialog
          client={client}
          title={title}
          targetType={targetType}
          targetId={targetId}
          initial={picker}
          onClose={() => setPicker(null)}
          onSaved={(favorite) => {
            const confirmed = { saved: true, favoriteId: (favorite && favorite.id) || null };
            batcher.set(targetType, targetId, confirmed);
            setState(confirmed);
            setPicker(null);
          }}
        />
      )}
    </span>
  );
}

export default SaveButton;

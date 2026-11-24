import { useEffect, useRef, useState } from 'react';
import { applyReaction, formatCount, reactionFields, visibleReactions } from '@trelliskit/shared';
import strings from '../../strings.js';
import { useAuth } from '../../context/AuthContext.jsx';
import { useToast } from '../../context/ToastContext.jsx';
import { errorText } from '../../utils/engagement.js';
import Icon from '../Icon.jsx';
import useReactionSet from './useReactionSet.js';
import WhoReactedDialog from './WhoReactedDialog.jsx';
import ReactionPeoplePreview from './ReactionPeoplePreview.jsx';
import { useAccessPrompt } from '../AccessPrompt.jsx';

const r = strings.reactions;

function ReactionPicker({ set, mine, onPick, onClose }) {
  const ref = useRef(null);
  useEffect(() => {
    const first = ref.current && ref.current.querySelector('button');
    if (first) first.focus();
    const close = (e) => { if (ref.current && !ref.current.contains(e.target)) onClose(); };
    const key = (e) => { if (e.key === 'Escape') onClose(); };
    document.addEventListener('mousedown', close);
    document.addEventListener('keydown', key);
    return () => {
      document.removeEventListener('mousedown', close);
      document.removeEventListener('keydown', key);
    };
  }, [onClose]);
  return (
    <div className="reaction-picker" ref={ref} role="group" aria-label={r.add}>
      {set.items.map((item) => (
        <button
          key={item.key}
          type="button"
          className={'reaction-option' + (mine.includes(item.key) ? ' active' : '')}
          aria-label={mine.includes(item.key) ? r.remove(item.label) : r.react(item.label)}
          aria-pressed={mine.includes(item.key)}
          title={item.label}
          onClick={() => onPick(item.key)}
        >
          <span aria-hidden="true">{item.emoji}</span>
        </button>
      ))}
    </div>
  );
}

/**
 * Reaction bar for a post or comment (contract section 6): counts in the property's order with
 * the caller's own reactions pressed, a one-tap like, a picker with every reaction, and the
 * "who reacted" dialog. Changes apply optimistically and roll back when the server refuses;
 * the server response then replaces the local copy.
 *
 * @param {{ target: { type: string, id: string }, value: object, onChange?: (fields: object) => void, canReact?: boolean, client?: object }} props
 */
function ReactionBar({ target, value, onChange, canReact = true, client: clientProp }) {
  const auth = useAuth();
  const client = clientProp || auth.client;
  const toast = useToast();
  const accessPrompt = useAccessPrompt();
  const set = useReactionSet();
  const [fields, setFields] = useState(() => reactionFields(value));
  const [pickerOpen, setPickerOpen] = useState(false);
  const [whoKey, setWhoKey] = useState(undefined);
  const seq = useRef(0);
  const [lastValue, setLastValue] = useState(value);

  if (lastValue !== value) {
    setLastValue(value);
    setFields(reactionFields(value));
  }

  const commit = (next) => {
    setFields(next);
    if (onChange) onChange(next);
  };

  const toggle = async (key) => {
    setPickerOpen(false);
    if (!canReact) return;
    const before = fields;
    const on = !before.myReactions.includes(key);
    const optimistic = applyReaction(before, key, on);
    const mine = ++seq.current;
    setFields(optimistic);
    try {
      const response = on ? await client.putReaction(target, key) : await client.deleteReaction(target, key);
      if (mine === seq.current) commit(response ? reactionFields(response) : optimistic);
    } catch (err) {
      if (mine === seq.current) setFields(before);
      if (!accessPrompt.handle(err)) toast.error(errorText(err, r.errors, r.errors.generic));
    }
  };

  const like = set.likeKey ? set.items.find((x) => x.key === set.likeKey) : null;
  const likeOn = like ? fields.myReactions.includes(like.key) : false;
  const likeCount = like ? Number((fields.reactionCounts || {})[like.key] || 0) : 0;
  // The one-tap like button carries its own count, so the like reaction is not repeated as a chip.
  const showLikeButton = Boolean(like && canReact);
  const shown = visibleReactions(set, fields.reactionCounts, fields.myReactions).filter((item) => !showLikeButton || item.key !== like.key);

  return (
    <div className="reaction-bar" role="group" aria-label={r.label}>
      {showLikeButton && (
        <ReactionPeoplePreview target={target} reaction={like} count={likeCount} client={client} onShowAll={setWhoKey}>
        {(previewProps) => <button {...previewProps} type="button" className={'reaction-like' + (likeOn ? ' active' : '')} aria-pressed={likeOn} onClick={() => toggle(like.key)} aria-label={likeCount > 0 ? r.count(like.label, likeCount) : r.react(like.label)}>
          <span aria-hidden="true">{like.emoji}</span>
          <span className="reaction-like-label">{like.label}</span>
          {likeCount > 0 && <span className="reaction-count">{formatCount(likeCount)}</span>}
        </button>}
        </ReactionPeoplePreview>
      )}
      {shown.map((item) => (
        <ReactionPeoplePreview key={item.key} target={target} reaction={item} count={item.count} client={client} onShowAll={setWhoKey}>
        {(previewProps) => <button
          {...previewProps}
          type="button"
          className={'reaction-chip' + (item.mine ? ' active' : '')}
          aria-pressed={item.mine}
          aria-label={r.count(item.label, item.count)}
          onClick={() => canReact ? toggle(item.key) : setWhoKey(item.key)}
        >
          <span aria-hidden="true">{item.emoji}</span>
          <span className="reaction-count">{formatCount(item.count)}</span>
        </button>}
        </ReactionPeoplePreview>
      ))}
      {canReact && set.items.length > 1 && (
        <span className="popover-anchor">
          <button type="button" className="icon-button reaction-add" aria-label={r.add} title={r.add} aria-expanded={pickerOpen} onClick={() => setPickerOpen((v) => !v)}>
            <Icon name="smile" size={16} />
          </button>
          {pickerOpen && <ReactionPicker set={set} mine={fields.myReactions} onPick={toggle} onClose={() => setPickerOpen(false)} />}
        </span>
      )}
      {fields.reactionTotal > 0 && (
        <button type="button" className="link-button small reaction-total" onClick={() => setWhoKey('')}>{r.total(fields.reactionTotal)}</button>
      )}
      {whoKey !== undefined && <WhoReactedDialog target={target} counts={fields.reactionCounts} initialKey={whoKey} client={client} onClose={() => setWhoKey(undefined)} />}
    </div>
  );
}

export default ReactionBar;

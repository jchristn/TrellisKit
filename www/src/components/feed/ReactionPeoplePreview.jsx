import { useCallback, useEffect, useId, useLayoutEffect, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import strings from '../../strings.js';
import useCursorList from '../../hooks/useCursorList.js';
import { authorName } from '../../utils/engagement.js';

const r = strings.reactions;
const PREVIEW_LIMIT = 5;

function People({ target, reaction, client, onShowAll }) {
  const fetchPage = useCallback((cursor, signal) => client.listReactions(
    { type: target.type, id: target.id }, { maxResults: PREVIEW_LIMIT, filters: { key: reaction.key } }, signal
  ), [client, target.type, target.id, reaction.key]);
  const list = useCursorList(fetchPage);
  return (
    <>
      <strong>{reaction.emoji} {reaction.label}</strong>
      {list.loading && <p role="status">{r.previewLoading}</p>}
      {list.error && <p role="alert">{r.whoError} <button type="button" className="link-button" onClick={list.refresh}>{r.previewRetry}</button></p>}
      {!list.loading && !list.error && list.loaded && !list.items.length && <p>{r.whoEmpty}</p>}
      <ul className="reaction-preview-names">
        {list.items.map((row) => <li key={row.id || row.user?.id}>{authorName(row.user)}</li>)}
      </ul>
      {!list.error && list.loaded && !list.done && <p className="muted small">{r.previewMore}</p>}
      <button type="button" className="link-button reaction-preview-all" onClick={onShowAll}>{r.previewAll}</button>
    </>
  );
}

/** Lazy, bounded people preview on hover or keyboard focus. Its portal avoids card clipping. */
export default function ReactionPeoplePreview({ target, reaction, count, client, onShowAll, children }) {
  const [open, setOpen] = useState(false);
  const [position, setPosition] = useState({ left: 8, top: 8 });
  const anchor = useRef(null);
  const panel = useRef(null);
  const closeTimer = useRef(null);
  const id = useId();
  const cancelClose = () => clearTimeout(closeTimer.current);
  const show = () => { cancelClose(); if (count > 0) setOpen(true); };
  const scheduleClose = () => { cancelClose(); closeTimer.current = setTimeout(() => setOpen(false), 180); };
  const enterPanel = (event) => {
    if (event.key === 'ArrowDown' && open && anchor.current?.contains(event.target)) {
      event.preventDefault();
      panel.current?.querySelector('button')?.focus();
    }
  };
  const blur = (event) => {
    if (!anchor.current?.contains(event.relatedTarget) && !panel.current?.contains(event.relatedTarget)) scheduleClose();
  };

  useEffect(() => () => clearTimeout(closeTimer.current), []);
  useLayoutEffect(() => {
    if (!open) return undefined;
    const place = () => {
      const rect = anchor.current?.getBoundingClientRect();
      if (!rect || !panel.current) return;
      const { width, height } = panel.current.getBoundingClientRect();
      setPosition({
        left: Math.max(8, Math.min(rect.left, window.innerWidth - width - 8)),
        top: rect.top >= height + 12 ? rect.top - height - 6 : Math.max(8, Math.min(rect.bottom + 6, window.innerHeight - height - 8))
      });
    };
    place();
    const observer = typeof ResizeObserver === 'function' ? new ResizeObserver(place) : null;
    if (observer) observer.observe(panel.current);
    return () => observer?.disconnect();
  }, [open]);

  useEffect(() => {
    if (!open) return undefined;
    const dismiss = (event) => {
      if (!anchor.current?.contains(event.target) && !panel.current?.contains(event.target)) setOpen(false);
    };
    const key = (event) => {
      if (event.key !== 'Escape') return;
      anchor.current?.querySelector('button')?.focus();
      setOpen(false);
    };
    const move = (event) => { if (!panel.current?.contains(event.target)) setOpen(false); };
    document.addEventListener('pointerdown', dismiss);
    document.addEventListener('keydown', key);
    window.addEventListener('scroll', move, true);
    window.addEventListener('resize', move);
    return () => {
      document.removeEventListener('pointerdown', dismiss);
      document.removeEventListener('keydown', key);
      window.removeEventListener('scroll', move, true);
      window.removeEventListener('resize', move);
    };
  }, [open]);

  return (
    <span ref={anchor} className="reaction-preview-anchor" onMouseEnter={show} onMouseLeave={scheduleClose} onFocus={show} onBlur={blur} onKeyDown={enterPanel}>
      {children({ 'aria-haspopup': count > 0 ? 'dialog' : undefined, 'aria-expanded': count > 0 ? open : undefined, 'aria-controls': open ? id : undefined })}
      {open && count > 0 && createPortal(
        <div ref={panel} id={id} role="dialog" aria-label={r.previewTitle(reaction.label)} className="reaction-people-preview" style={position} onMouseEnter={cancelClose} onMouseLeave={scheduleClose} onFocus={cancelClose} onBlur={blur}>
          <People key={target.type + ':' + target.id + ':' + reaction.key + ':' + count} target={target} reaction={reaction} client={client} onShowAll={() => { setOpen(false); onShowAll(reaction.key); }} />
        </div>, document.body
      )}
    </span>
  );
}

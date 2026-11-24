import { useCallback, useState } from 'react';
import { Link } from 'react-router-dom';
import { visibleReactions } from '@trelliskit/shared';
import strings from '../../strings.js';
import Modal from '../Modal.jsx';
import Avatar from '../Avatar.jsx';
import { ErrorBanner, Loading } from '../States.jsx';
import useCursorList from '../../hooks/useCursorList.js';
import { authorName, mediaUrl, memberPath } from '../../utils/engagement.js';
import useReactionSet from './useReactionSet.js';

const r = strings.reactions;

/**
 * "Who reacted" (contract section 6.3): tabs for all reactions and each key with a count,
 * newest first, cursor paged.
 */
function WhoReactedDialog({ target, counts, initialKey = '', client, onClose }) {
  const set = useReactionSet();
  const [key, setKey] = useState(initialKey);
  const tabs = visibleReactions(set, counts);
  const fetchPage = useCallback((cursor, signal) => client.listReactions(target, { continuationToken: cursor, maxResults: 50, filters: { key } }, signal), [client, target, key]);
  const list = useCursorList(fetchPage);
  const emojiFor = (k) => (set.items.find((x) => x.key === k) || {}).emoji || '';
  return (
    <Modal open size="sm" onClose={onClose} title={r.whoTitle}>
      <div className="segmented who-tabs" role="tablist" aria-label={r.whoTitle}>
        <button type="button" role="tab" aria-selected={key === ''} className={'segment' + (key === '' ? ' active' : '')} onClick={() => setKey('')}>{r.all}</button>
        {tabs.map((t) => (
          <button key={t.key} type="button" role="tab" aria-selected={key === t.key} className={'segment' + (key === t.key ? ' active' : '')} onClick={() => setKey(t.key)} aria-label={r.count(t.label, t.count)}>
            <span aria-hidden="true">{t.emoji} {t.count}</span>
          </button>
        ))}
      </div>
      <ErrorBanner message={list.error ? r.whoError : ''} onRetry={list.refresh} />
      <ul className="who-list">
        {list.items.map((row) => (
          <li key={(row.user && row.user.id) + ':' + row.key} className="who-row">
            <Avatar src={row.user && !row.user.isDeleted ? mediaUrl(row.user.avatarUrl) : null} name={authorName(row.user)} size={32} />
            {row.user && row.user.id && !row.user.isDeleted
              ? <Link to={memberPath(row.user.id)} onClick={onClose}>{authorName(row.user)}</Link>
              : <span>{authorName(row.user)}</span>}
            <span className="who-emoji" aria-label={(set.items.find((x) => x.key === row.key) || {}).label}>{emojiFor(row.key)}</span>
          </li>
        ))}
      </ul>
      {list.loading && <Loading inline />}
      {!list.loading && list.loaded && !list.items.length && <p className="muted">{r.whoEmpty}</p>}
      {!list.done && !list.loading && list.loaded && <button type="button" className="button button-secondary button-sm" onClick={list.loadMore}>{r.whoLoadMore}</button>}
    </Modal>
  );
}

export default WhoReactedDialog;

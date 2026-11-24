import { useCallback, useId, useState } from 'react';
import { pollDisplay, pollSelectionProblem, togglePollOption } from '@trelliskit/shared';
import strings from '../../strings.js';
import { useAuth } from '../../context/AuthContext.jsx';
import { useToast } from '../../context/ToastContext.jsx';
import useCursorList from '../../hooks/useCursorList.js';
import { errorText } from '../../utils/engagement.js';
import { formatDateTime } from '../../utils/format.js';
import Modal from '../Modal.jsx';
import { ErrorBanner, Loading } from '../States.jsx';
import { Badge } from '../Badges.jsx';
import AuthorLine from '../feed/AuthorLine.jsx';
import { useAccessPrompt } from '../AccessPrompt.jsx';

const p = strings.forums.poll;

function VotersDialog({ poll, client, onClose }) {
  const fetchPage = useCallback((cursor, signal) => client.enumeratePollVoters(poll.id, { continuationToken: cursor, maxResults: 25 }, signal), [client, poll.id]);
  const list = useCursorList(fetchPage);
  const labelOf = (id) => ((poll.options || []).find((o) => o.id === id) || {}).label || '';
  return (
    <Modal open size="md" onClose={onClose} title={strings.forums.votersTitle}>
      <ErrorBanner message={list.error ? strings.forums.votersError : ''} onRetry={list.refresh} />
      <ul className="voter-list">
        {list.items.map((row) => (
          <li key={row.user ? row.user.id : row.createdUtc}>
            <AuthorLine user={row.user} size={28} time={row.createdUtc} />
            <span className="muted small">{(row.optionIds || []).map(labelOf).filter(Boolean).join(', ')}</span>
          </li>
        ))}
      </ul>
      {list.loading && <Loading inline />}
      {list.loaded && !list.loading && !list.items.length && !list.error && <p className="muted small">{strings.forums.votersEmpty}</p>}
      {list.loaded && !list.done && !list.loading && <button type="button" className="button button-secondary button-sm" onClick={list.loadMore}>{strings.forums.moreVoters}</button>}
    </Modal>
  );
}

/**
 * A poll on a thread (PHASE3_CONTRACT.md section 6): vote (one or several choices), change or
 * remove the vote when allowed, and the results as a bar chart with a text alternative for every
 * option. Display state comes only from the server's fields (`pollDisplay`); hidden results show
 * only the voter count. Polls whose author chose visible voters offer "See who voted".
 *
 * @param {{ poll: object, thread: object, onChange: (poll: object) => void }} props
 */
function PollCard({ poll, thread, onChange }) {
  const { client } = useAuth();
  const toast = useToast();
  const groupId = useId();
  const view = pollDisplay(poll);
  const accessPrompt = useAccessPrompt();
  const [selection, setSelection] = useState(view.myVote);
  const [changing, setChanging] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [voters, setVoters] = useState(false);
  const viewer = thread.viewer || {};
  const canVote = viewer.canVote !== false && !thread.isLocked && !view.isClosed;
  const showForm = canVote && (!view.hasVoted || changing);
  const errors = { ...strings.forums.errors, ...p.errors };

  const run = async (fn, message) => {
    setBusy(true);
    setError('');
    try {
      const next = await fn();
      if (next) onChange(next);
      setChanging(false);
      toast.success(message);
    } catch (err) {
      if (!accessPrompt.handle(err)) setError(errorText(err, errors));
    } finally {
      setBusy(false);
    }
  };

  const submit = (e) => {
    e.preventDefault();
    const problem = pollSelectionProblem(poll, selection);
    if (problem) {
      setError(p.problems[problem]);
      return;
    }
    run(() => client.votePoll(poll.id, selection), p.voted);
  };

  const hint = !view.multipleChoice ? p.chooseOne : view.maxChoices ? p.chooseUpTo(view.maxChoices) : p.chooseAny;
  const closedText = view.isClosed ? p.closed(view.closedUtc ? formatDateTime(view.closedUtc) : '') : view.closesUtc ? p.closes(formatDateTime(view.closesUtc)) : '';

  return (
    <section className="poll-card panel" aria-labelledby={groupId + '-title'}>
      <h2 id={groupId + '-title'} className="poll-title">{p.title}</h2>
      <ErrorBanner message={error} onDismiss={() => setError('')} />
      {showForm ? (
        <form onSubmit={submit} className="poll-form">
          <fieldset className="poll-options" disabled={busy}>
            <legend className="muted small">{hint}</legend>
            {view.options.map((o) => (
              <label key={o.id} className={'poll-option' + (selection.includes(o.id) ? ' selected' : '')}>
                <input
                  type={view.multipleChoice ? 'checkbox' : 'radio'}
                  name={groupId + '-option'}
                  value={o.id}
                  checked={selection.includes(o.id)}
                  onChange={() => setSelection((current) => togglePollOption(poll, current, o.id))}
                />
                <span>{o.label}</span>
              </label>
            ))}
          </fieldset>
          <div className="form-actions">
            {changing && <button type="button" className="button button-secondary button-sm" onClick={() => { setChanging(false); setSelection(view.myVote); }} disabled={busy}>{p.cancelChange}</button>}
            <button type="submit" className="button button-primary button-sm" disabled={busy}>{busy ? strings.common.loading : p.vote}</button>
          </div>
        </form>
      ) : null}
      {view.resultsVisible && !showForm && (
        <ul className="poll-results" aria-label={p.resultsLabel}>
          {view.options.map((o) => (
            <li key={o.id} className={'poll-result' + (o.isLeading ? ' leading' : '')}>
              <span className="visually-hidden">{p.optionResult(o.label, o.voteCount || 0, o.percent === null ? 0 : o.percent)}{o.isMine ? '. ' + p.yourVote : ''}</span>
              <div className="poll-result-head" aria-hidden="true">
                <span className="poll-result-label">{o.label}{o.isMine && <> <Badge tone="info">{p.yourVote}</Badge></>}</span>
                <span className="poll-result-value">{(o.percent === null ? 0 : o.percent) + '%'} <span className="muted">({o.voteCount || 0})</span></span>
              </div>
              <div className="poll-bar" aria-hidden="true"><span className="poll-bar-fill" style={{ width: o.barPercent + '%' }} /></div>
            </li>
          ))}
        </ul>
      )}
      {!view.resultsVisible && !showForm && <p className="muted small">{p.resultsHidden[view.resultsVisibility]}</p>}
      {!view.resultsVisible && showForm && p.resultsHidden[view.resultsVisibility] && <p className="muted small">{p.resultsHidden[view.resultsVisibility]}</p>}
      <div className="poll-foot small muted">
        {view.totalVoters !== null && <span>{p.totalVoters(view.totalVoters)}</span>}
        {closedText && <span>{closedText}</span>}
        {thread.isLocked && !view.isClosed && <span>{p.locked}</span>}
        {canVote && view.hasVoted && !changing && view.canChangeVote && (
          <>
            <button type="button" className="link-button small" onClick={() => { setSelection(view.myVote); setChanging(true); }} disabled={busy}>{p.changeVote}</button>
            <button type="button" className="link-button small" onClick={() => run(() => client.removePollVote(poll.id), p.voteRemoved)} disabled={busy}>{p.removeVote}</button>
          </>
        )}
        {canVote && view.hasVoted && !view.canChangeVote && <span>{p.voteLocked}</span>}
        {view.voterVisibility === 'Visible' && view.resultsVisible && <button type="button" className="link-button small" onClick={() => setVoters(true)}>{strings.forums.viewers}</button>}
      </div>
      {voters && <VotersDialog poll={poll} client={client} onClose={() => setVoters(false)} />}
    </section>
  );
}

export default PollCard;

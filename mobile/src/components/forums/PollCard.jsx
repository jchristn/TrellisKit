import { pollDisplay, pollSelectionProblem, togglePollOption } from '@trelliskit/shared';
import { useCallback, useState } from 'react';
import { View } from 'react-native';
import { useAuth } from '../../auth/AuthProvider';
import { describeContentError } from '../../content/errors';
import { useCursorList } from '../../hooks/useCursorList';
import strings from '../../strings';
import { useTheme } from '../../theme';
import AppText from '../AppText';
import Button from '../Button';
import Card from '../Card';
import ChipGroup from '../ChipGroup';
import Loading from '../Loading';
import Notice from '../Notice';
import Sheet from '../Sheet';
import { useAccessPrompt } from '../AccessPrompt';
import PollEditSheet from './PollEditSheet';

const p = strings.forums.poll;

function VotersSheet({ poll, onClose }) {
  const { api } = useAuth();
  const fetchPage = useCallback((continuationToken) => api.enumeratePollVoters(poll.id, { continuationToken, maxResults: 25 }), [api, poll.id]);
  const list = useCursorList(fetchPage, { key: poll.id });
  return (
    <Sheet visible title={p.votersTitle} onClose={onClose} testID="poll-voters">
      {list.status === 'loading' ? <Loading label={strings.common.loading} /> : null}
      {list.status === 'ready' && !list.items.length ? <AppText color="textMuted">{p.votersEmpty}</AppText> : null}
      {list.items.map((row) => (
        <AppText key={row.user ? row.user.id : row.createdUtc}>{(row.user && row.user.displayName) || ''}</AppText>
      ))}
      {list.hasMore ? <Button label={strings.forums.loadMore} variant="secondary" onPress={list.loadMore} /> : null}
    </Sheet>
  );
}

/**
 * A poll on a thread (docs/PHASE3_CONTRACT.md section 6): choose one or several options and vote,
 * change or remove the vote when allowed, and the results as bars with a spoken summary per
 * option. Everything shown comes from the server's fields through `pollDisplay`.
 */
export default function PollCard({ poll, thread, onChange, canEdit = false, testID = 'poll' }) {
  const accessPrompt = useAccessPrompt();
  const theme = useTheme();
  const { api } = useAuth();
  const view = pollDisplay(poll);
  const [selection, setSelection] = useState(view.myVote);
  const [changing, setChanging] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(null);
  const [voters, setVoters] = useState(false);
  const [editing, setEditing] = useState(false);
  const viewer = thread.viewer || {};
  const canVote = viewer.canVote !== false && !thread.isLocked && !view.isClosed;
  const showForm = canVote && (!view.hasVoted || changing);

  async function run(fn) {
    setBusy(true);
    setError(null);
    try {
      const next = await fn();
      if (next) onChange(next);
      setChanging(false);
    } catch (e) {
      if (!accessPrompt.handle(e)) setError(describeContentError(e));
    } finally {
      setBusy(false);
    }
  }

  function vote() {
    const problem = pollSelectionProblem(poll, selection);
    if (problem) return setError(p.problems[problem]);
    return run(() => api.votePoll(poll.id, selection));
  }

  const hint = !view.multipleChoice ? p.chooseOne : view.maxChoices ? p.chooseUpTo(view.maxChoices) : p.chooseAny;
  const options = view.options.map((o) => ({ value: o.id, label: o.label }));

  return (
    <Card testID={testID}>
      <AppText variant="heading" accessibilityRole="header">
        {p.title}
      </AppText>
      {showForm ? (
        <View style={{ gap: theme.spacing.sm }}>
          {view.multipleChoice ? (
            <ChipGroup label={hint} options={options} values={selection} onToggle={(id) => setSelection((s) => togglePollOption(poll, s, id))} testID={testID + '-options'} />
          ) : (
            <ChipGroup label={hint} options={options} value={selection[0]} onToggle={(id) => setSelection((s) => togglePollOption(poll, s, id))} testID={testID + '-options'} />
          )}
          <Button label={p.vote} onPress={vote} disabled={busy} testID={testID + '-vote'} />
          {changing ? <Button label={p.keepVote} variant="ghost" onPress={() => setChanging(false)} disabled={busy} testID={testID + '-keep'} /> : null}
        </View>
      ) : null}
      {view.resultsVisible && !showForm ? (
        <View style={{ gap: theme.spacing.sm }} testID={testID + '-results'}>
          {view.options.map((o) => {
            const percent = o.percent === null ? 0 : o.percent;
            return (
              <View key={o.id} accessible accessibilityLabel={p.optionResult(o.label, o.voteCount || 0, percent) + (o.isMine ? ', ' + p.yourVote : '')} testID={testID + '-result-' + o.id} style={{ gap: 4 }}>
                <View style={{ flexDirection: 'row', justifyContent: 'space-between' }}>
                  <AppText style={{ fontWeight: o.isLeading ? '700' : '400', flex: 1 }}>
                    {o.label}
                    {o.isMine ? ' (' + p.yourVote + ')' : ''}
                  </AppText>
                  <AppText color="textMuted">{percent + '% (' + (o.voteCount || 0) + ')'}</AppText>
                </View>
                <View style={{ height: 8, borderRadius: 4, backgroundColor: theme.colors.background, overflow: 'hidden' }}>
                  <View style={{ width: o.barPercent + '%', height: 8, backgroundColor: theme.colors.primary }} />
                </View>
              </View>
            );
          })}
        </View>
      ) : null}
      {!view.resultsVisible && p.resultsHidden[view.resultsVisibility] ? <AppText color="textMuted">{p.resultsHidden[view.resultsVisibility]}</AppText> : null}
      <AppText variant="caption" color="textMuted" testID={testID + '-total'}>
        {view.totalVoters !== null ? p.totalVoters(view.totalVoters) : ''}
        {view.isClosed ? ' · ' + p.closed : ''}
      </AppText>
      {thread.isLocked && !view.isClosed ? <AppText color="textMuted">{p.locked}</AppText> : null}
      {canVote && view.hasVoted && !changing && view.canChangeVote ? (
        <View style={{ flexDirection: 'row', gap: theme.spacing.sm, flexWrap: 'wrap' }}>
          <Button
            label={p.changeVote}
            variant="secondary"
            onPress={() => {
              setSelection(view.myVote);
              setChanging(true);
            }}
            disabled={busy}
            testID={testID + '-change'}
          />
          <Button label={p.removeVote} variant="ghost" onPress={() => run(() => api.removePollVote(poll.id))} disabled={busy} testID={testID + '-remove'} />
        </View>
      ) : null}
      {canVote && view.hasVoted && !view.canChangeVote ? <AppText color="textMuted">{p.voteLocked}</AppText> : null}
      {view.voterVisibility === 'Visible' && view.resultsVisible ? <Button label={p.voters} variant="ghost" onPress={() => setVoters(true)} testID={testID + '-voters'} /> : null}
      {canEdit ? <Button label={p.edit} variant="ghost" onPress={() => setEditing(true)} testID={testID + '-edit'} /> : null}
      <Notice tone="error" testID={testID + '-error'}>
        {error}
      </Notice>
      {editing ? <PollEditSheet thread={thread} poll={poll} onSaved={(next) => onChange(next)} onClose={() => setEditing(false)} testID={testID + '-edit-sheet'} /> : null}
      {voters ? <VotersSheet poll={poll} onClose={() => setVoters(false)} /> : null}
    </Card>
  );
}

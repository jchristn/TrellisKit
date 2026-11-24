import { useState } from 'react';
import { View } from 'react-native';
import { useAuth } from '../../auth/AuthProvider';
import { describeContentError } from '../../content/errors';
import { pollEditDraft, pollEditRequest, pollHasVotes } from '../../content/pollEdit';
import strings from '../../strings';
import { useTheme } from '../../theme';
import AppText from '../AppText';
import Button from '../Button';
import Notice from '../Notice';
import Sheet from '../Sheet';
import TextField from '../TextField';
import PollBuilder from './PollBuilder';

const p = strings.forums.poll;

/**
 * Edit a poll (docs/PHASE3_CONTRACT.md section 6.4). Before the first vote the author edits the
 * options and settings with the poll builder; after it only the closing time. A vote arriving
 * meanwhile answers `409 PollOptionsLocked`: the sheet shows why and reloads the poll, which
 * switches it to the closing-time-only form.
 */
export default function PollEditSheet({ thread, poll, onSaved, onClose, testID = 'poll-edit' }) {
  const theme = useTheme();
  const { api } = useAuth();
  const [current, setCurrent] = useState(poll);
  const [draft, setDraft] = useState(() => pollEditDraft(poll));
  const [problems, setProblems] = useState({});
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(null);
  const locked = pollHasVotes(current);

  async function save() {
    const request = pollEditRequest(current, draft);
    setProblems(request.problems);
    if (!request.body) return;
    setBusy(true);
    setError(null);
    try {
      const saved = await api.updateThread(thread.id, request.body);
      onSaved(saved && saved.poll ? saved.poll : current, saved);
      onClose();
    } catch (e) {
      setError(describeContentError(e));
      if (e && e.code === 'PollOptionsLocked') {
        try {
          const fresh = await api.getThread(thread.id);
          if (fresh && fresh.poll) {
            setCurrent(fresh.poll);
            onSaved(fresh.poll, fresh);
          }
        } catch {
          // Keep the sheet as it is; the error explains what happened.
        }
      }
    } finally {
      setBusy(false);
    }
  }

  return (
    <Sheet visible title={p.editTitle} onClose={onClose} testID={testID}>
      {locked ? (
        <View style={{ gap: theme.spacing.sm }}>
          <AppText color="textMuted" testID={testID + '-locked'}>
            {p.editLocked}
          </AppText>
          <TextField label={p.closeInDays} hint={p.editCloseHint} value={draft.closeInDays} onChangeText={(t) => setDraft((d) => ({ ...d, closeInDays: t.replace(/[^\d.]/g, '') }))} keyboardType="decimal-pad" error={problems.closesUtc ? p.problems[problems.closesUtc] : null} testID={testID + '-closes'} />
        </View>
      ) : (
        <View style={{ gap: theme.spacing.sm }}>
          <PollBuilder value={draft} onChange={setDraft} problems={problems} testID={testID + '-builder'} />
          <AppText variant="caption" color="textMuted">
            {p.editCloseHint}
          </AppText>
        </View>
      )}
      <Notice tone="error" testID={testID + '-error'}>
        {error}
      </Notice>
      <Button label={p.saveEdit} onPress={save} disabled={busy} testID={testID + '-save'} />
    </Sheet>
  );
}

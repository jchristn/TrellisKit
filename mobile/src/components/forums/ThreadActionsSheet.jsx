import { MODERATION_REASON_MIN, MODERATION_TEXT_MAX, feedbackTransitions } from '@trelliskit/shared';
import { useRouter } from 'expo-router';
import { useState } from 'react';
import { View } from 'react-native';
import { useAuth } from '../../auth/AuthProvider';
import { isSystemAdmin } from '../../auth/routing';
import { describeContentError } from '../../content/errors';
import { useForumList } from '../../hooks/useForumList';
import strings from '../../strings';
import { useTheme } from '../../theme';
import AppText from '../AppText';
import Button from '../Button';
import CheckRow from '../CheckRow';
import Loading from '../Loading';
import Notice from '../Notice';
import Sheet from '../Sheet';
import TextField from '../TextField';

const a = strings.forums.actions;

/** Keys that belong to the moderator actions sheet (everything else is the member menu). */
export const ModeratorActionKeys = Object.freeze(['pin', 'unpin', 'lock', 'unlock', 'move', 'editMetadata', 'hide', 'unhide', 'remove', 'restore', 'promote', 'unpromote']);

const MODERATION = { hide: 'Hide', unhide: 'Unhide', remove: 'Remove', restore: 'Restore' };

function useRun(onChange, onClose) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(null);
  async function run(fn) {
    setBusy(true);
    setError(null);
    try {
      const next = await fn();
      if (next && next.id) onChange(next);
      onClose();
    } catch (e) {
      setError(describeContentError(e));
    } finally {
      setBusy(false);
    }
  }
  return { busy, error, run, setError };
}

function MoveForm({ thread, run, busy }) {
  const { api } = useAuth();
  const list = useForumList(api);
  const targets = list.forums.filter((f) => !f.isArchived && f.id !== (thread.forum && thread.forum.id) && (f.allowedThreadTypes || []).includes(thread.type));
  if (list.status === 'loading') return <Loading label={strings.common.loading} />;
  if (!targets.length) return <AppText color="textMuted">{a.moveNone}</AppText>;
  return targets.map((f) => <Button key={f.id} label={a.moveTo(f.name)} variant="secondary" disabled={busy} onPress={() => run(() => api.moveThread(thread.id, { forumId: f.id, notifyAuthor: true }))} testID={'move-' + f.id} />);
}

/**
 * The thread menus on mobile (docs/PHASE3_CONTRACT.md sections 1.4 and 19): the member menu
 * (edit, history, close the poll, review status, report, delete) and, when `viewer.canModerate`,
 * the moderator actions sheet (pin, lock with a reason, move, edit metadata, hide, remove,
 * restore, promote). Every item comes from the server's `viewer` through `threadActions`.
 * @param {object} props
 * @param {string|null} props.sheet The open sheet: 'menu', 'moderator', or a flow key.
 */
export default function ThreadActionsSheet({ thread, actions, sheet, setSheet, onChange, onDeleted, isAuthor, testID = 'thread-actions' }) {
  const theme = useTheme();
  const router = useRouter();
  const { api, user } = useAuth();
  const close = () => setSheet(null);
  const { busy, error, run, setError } = useRun(onChange, close);
  const [reason, setReason] = useState('');
  const [cardTitle, setCardTitle] = useState('');
  const [notify, setNotify] = useState(true);
  const [pin, setPin] = useState(false);

  const labels = { edit: a.edit, editMetadata: a.editMetadata, history: a.history, pin: a.pin, unpin: a.unpin, lock: a.lock, unlock: a.unlock, move: a.move, hide: a.hide, unhide: a.unhide, remove: a.remove, restore: a.restore, promote: a.promote, unpromote: a.unpromote, closePoll: a.closePoll, feedbackStatus: a.feedbackStatus, report: a.report, delete: a.delete };
  const press = (key) => {
    setError(null);
    setReason('');
    switch (key) {
      case 'edit': close(); return router.push({ pathname: '/compose-thread', params: { id: thread.id } });
      case 'editMetadata': close(); return router.push({ pathname: '/compose-thread', params: { id: thread.id, mode: 'metadata' } });
      case 'pin': return run(() => api.pinThread(thread.id));
      case 'unpin': return run(() => api.unpinThread(thread.id));
      case 'unlock': return run(() => api.unlockThread(thread.id));
      case 'unpromote': return run(() => api.unpromoteThread(thread.id));
      case 'closePoll': return run(async () => ({ ...thread, poll: await api.closePoll(thread.poll.id) }));
      default: return setSheet(key);
    }
  };

  const menuKeys = actions.filter((k) => !ModeratorActionKeys.includes(k));
  const modKeys = actions.filter((k) => ModeratorActionKeys.includes(k));
  const items = (keys) => keys.map((k) => <Button key={k} label={labels[k]} variant={['delete', 'remove', 'hide'].includes(k) ? 'ghost' : 'secondary'} disabled={busy} onPress={() => press(k)} testID={testID + '-' + k} />);
  const moderation = sheet && MODERATION[sheet];
  const r = reason.trim();

  return (
    <>
      <Sheet visible={sheet === 'menu'} title={a.title} onClose={close} testID={testID + '-menu'}>
        {items(menuKeys)}
        <Notice tone="error">{error}</Notice>
      </Sheet>
      <Sheet visible={sheet === 'moderator'} title={a.moderatorTitle} onClose={close} testID={testID + '-moderator'}>
        {items(modKeys)}
        <Notice tone="error">{error}</Notice>
      </Sheet>
      <Sheet visible={sheet === 'lock'} title={a.lockTitle} onClose={close} testID={testID + '-lock'}>
        <AppText>{a.lockBody}</AppText>
        <TextField label={a.lockReason} value={reason} onChangeText={setReason} maxLength={500} testID={testID + '-lock-reason'} />
        <Notice tone="error">{error}</Notice>
        <Button label={a.lock} onPress={() => run(() => api.lockThread(thread.id, r))} disabled={busy} testID={testID + '-lock-confirm'} />
      </Sheet>
      <Sheet visible={sheet === 'move'} title={a.moveTitle} onClose={close} testID={testID + '-move'}>
        {sheet === 'move' ? <MoveForm thread={thread} run={run} busy={busy} /> : null}
        <Notice tone="error">{error}</Notice>
      </Sheet>
      <Sheet visible={sheet === 'promote'} title={a.promoteTitle} onClose={close} testID={testID + '-promote'}>
        <AppText>{a.promoteBody}</AppText>
        <TextField label={a.promoteCardTitle} value={cardTitle} onChangeText={setCardTitle} maxLength={140} testID={testID + '-promote-title'} />
        <CheckRow label={a.promoteNotify} value={notify} onValueChange={setNotify} testID={testID + '-promote-notify'} />
        {isSystemAdmin(user) ? <CheckRow label={a.promotePin} value={pin} onValueChange={setPin} testID={testID + '-promote-pin'} /> : null}
        <Notice tone="error">{error}</Notice>
        <Button
          label={a.promote}
          disabled={busy}
          onPress={() =>
            run(async () => {
              const card = await api.promoteThread(thread.id, { title: cardTitle.trim() || null, notifyMembers: notify, ...(isSystemAdmin(user) ? { pin } : {}) });
              return { ...thread, promotion: { feedPostId: card && card.id, promotedBy: null, promotedUtc: card && card.publishedUtc } };
            })
          }
          testID={testID + '-promote-confirm'}
        />
      </Sheet>
      <Sheet visible={Boolean(moderation)} title={moderation ? a.moderateTitle(a[sheet]) : ''} onClose={close} testID={testID + '-moderate'}>
        <TextField label={a.moderateReason} value={reason} onChangeText={setReason} maxLength={MODERATION_TEXT_MAX} testID={testID + '-moderate-reason'} />
        <CheckRow label={a.notifyAuthor} value={notify} onValueChange={setNotify} testID={testID + '-moderate-notify'} />
        <Notice tone="error">{error}</Notice>
        <Button
          label={a.confirm}
          disabled={busy}
          onPress={() => {
            if (r.length < MODERATION_REASON_MIN || r.length > MODERATION_TEXT_MAX) return setError(a.moderateReasonInvalid);
            return run(async () => {
              await api.createModerationAction({ targetType: 'Thread', targetId: thread.id, action: moderation, reason: r, notifyAuthor: notify });
              return api.getThread(thread.id);
            });
          }}
          testID={testID + '-moderate-confirm'}
        />
      </Sheet>
      <Sheet visible={sheet === 'feedbackStatus'} title={a.statusTitle} onClose={close} testID={testID + '-feedback'}>
        <View style={{ gap: theme.spacing.sm }}>
          {feedbackTransitions(thread, { isAuthor }).map((status) => (
            <Button key={status} label={status === 'Closed' && !(thread.viewer || {}).canSetFeedbackStatus ? a.withdraw : strings.forums.feedbackStatus[status]} variant="secondary" disabled={busy} onPress={() => run(() => api.setFeedbackStatus(thread.id, status))} testID={testID + '-feedback-' + status} />
          ))}
        </View>
        <Notice tone="error">{error}</Notice>
      </Sheet>
      <Sheet visible={sheet === 'delete'} title={a.deleteTitle} onClose={close} testID={testID + '-delete'}>
        <AppText>{a.deleteBody}</AppText>
        <Notice tone="error">{error}</Notice>
        <Button
          label={a.delete}
          disabled={busy}
          onPress={async () => {
            try {
              await api.deleteThread(thread.id);
              close();
              if (onDeleted) onDeleted(thread.id);
            } catch (e) {
              setError(describeContentError(e));
            }
          }}
          testID={testID + '-delete-confirm'}
        />
      </Sheet>
    </>
  );
}

import { threadCanComment } from '@trelliskit/shared';
import { useCallback, useState } from 'react';
import { View } from 'react-native';
import { useAuth } from '../../auth/AuthProvider';
import strings from '../../strings';
import AppText from '../AppText';
import PostDetail from '../feed/PostDetail';
import ThreadHeader from './ThreadHeader';
import { SaveRow } from '../SaveButton';

const t = strings.forums;
const loadThread = (api, id) => api.getThread(id);

/**
 * A forum thread with its comment tree (docs/PHASE3_CONTRACT.md section 19), built on the shared
 * post detail: comments and reactions go to the thread's `commentTarget` (the feed post for a
 * linked thread), the composer shows only while the thread is open, and the comment menu offers
 * "Accept as answer" (Questions) and "Mark as the review" (Feedback) from the `viewer` object.
 */
export default function ThreadDetail({ threadId, commentId = null, initialThread = null, onThreadChange, onDeleted, testID = 'thread-detail' }) {
  const { api } = useAuth();
  const [thread, setThread] = useState(initialThread);
  const [treeKey, setTreeKey] = useState(0);
  const change = useCallback(
    (next) => {
      setThread(next);
      if (onThreadChange) onThreadChange(next);
    },
    [onThreadChange]
  );
  const viewer = (thread && thread.viewer) || {};

  async function act(fn) {
    const next = await fn();
    if (next && next.id) change(next);
    setTreeKey((k) => k + 1);
  }

  const commentExtras = {
    badges: (comment) =>
      comment.isAcceptedAnswer || comment.isFeedbackReview ? (
        <AppText variant="caption" color="success" style={{ fontWeight: '700' }} testID={'comment-' + comment.id + '-badge'}>
          {comment.isAcceptedAnswer ? t.acceptedAnswer : t.review}
        </AppText>
      ) : null,
    items: (comment) => {
      const out = [];
      if (!thread || comment.state !== 'Visible') return out;
      if (thread.type === 'Question' && viewer.canAcceptAnswer) {
        out.push(comment.isAcceptedAnswer ? { key: 'clear-answer', label: t.clearAnswer, onPress: () => act(() => api.clearAcceptedAnswer(thread.id)).catch(() => {}) } : { key: 'accept', label: t.acceptAnswer, onPress: () => act(() => api.acceptAnswer(thread.id, comment.id)).catch(() => {}) });
      }
      if (thread.type === 'Feedback' && viewer.canSetFeedbackStatus && !comment.isFeedbackReview) out.push({ key: 'review', label: t.chooseReview, onPress: () => act(() => api.setFeedbackStatus(thread.id, 'Reviewed', comment.id)).catch(() => {}) });
      return out;
    }
  };

  return (
    <PostDetail
      postId={threadId}
      initialPost={initialThread}
      commentId={commentId}
      onPostChange={change}
      onDeleted={onDeleted}
      loadItem={loadThread}
      renderCard={({ item, onChange, onAuthorBlocked }) => <View><SaveRow targetType="Thread" targetId={item.id} title={item.title} testID={testID + '-save'} /><ThreadHeader thread={item} onChange={onChange} onDeleted={onDeleted} onAuthorBlocked={onAuthorBlocked} /></View>}
      canComment={(item) => threadCanComment(item)}
      commentExtras={commentExtras}
      treeKey={treeKey}
      goneText={{ title: t.threadGoneTitle, body: t.threadGoneBody }}
      accessFeature="forums"
      testID={testID}
    />
  );
}

import { useLocalSearchParams, useRouter } from 'expo-router';
import { useCallback } from 'react';
import { SafeAreaView } from 'react-native-safe-area-context';
import PostDetail from '../../../src/components/feed/PostDetail';
import { emitContentEvent } from '../../../src/content/events';
import { useTheme } from '../../../src/theme';

/**
 * A post with its threaded comments (`/feed/{id}`). Notification and push deep links add
 * `?comment={commentId}` to show that comment in context.
 */
export default function PostScreen() {
  const theme = useTheme();
  const router = useRouter();
  const { id, comment } = useLocalSearchParams();
  const postId = Array.isArray(id) ? id[0] : id;
  const commentId = Array.isArray(comment) ? comment[0] : comment || null;
  const onPostChange = useCallback((post) => emitContentEvent('post', post), []);
  const onDeleted = useCallback(
    (deletedId) => {
      emitContentEvent('postDeleted', deletedId);
      if (router.canGoBack()) router.back();
      else router.replace('/feed');
    },
    [router]
  );
  return (
    <SafeAreaView testID="screen-post" edges={['left', 'right', 'bottom']} style={{ flex: 1, backgroundColor: theme.colors.background }}>
      <PostDetail key={postId + ':' + (commentId || '')} postId={postId} commentId={commentId} onPostChange={onPostChange} onDeleted={onDeleted} />
    </SafeAreaView>
  );
}

import { useLocalSearchParams, useRouter } from 'expo-router';
import { useCallback } from 'react';
import { SafeAreaView } from 'react-native-safe-area-context';
import ThreadDetail from '../../src/components/forums/ThreadDetail';
import MemberGate from '../../src/components/MemberGate';
import { useTheme } from '../../src/theme';
import FeatureGate from '../../src/components/FeatureGate';

/**
 * A forum thread with its comments (`/threads/{id}`). Notification and push deep links add
 * `?comment={commentId}` to show that comment in context (docs/PHASE3_CONTRACT.md section 9.3).
 */
function ThreadScreenContent() {
  const theme = useTheme();
  const router = useRouter();
  const { id, comment } = useLocalSearchParams();
  const threadId = Array.isArray(id) ? id[0] : id;
  const commentId = Array.isArray(comment) ? comment[0] : comment || null;
  const onDeleted = useCallback(() => {
    if (router.canGoBack()) router.back();
    else router.replace('/forums');
  }, [router]);
  return (
    <MemberGate>
      <SafeAreaView testID="screen-thread" edges={['left', 'right', 'bottom']} style={{ flex: 1, backgroundColor: theme.colors.background }}>
        <ThreadDetail key={threadId + ':' + (commentId || '')} threadId={threadId} commentId={commentId} onDeleted={onDeleted} />
      </SafeAreaView>
    </MemberGate>
  );
}

/** Gated by the access model: the `forums` feature (ACCESS_MODELS_CONTRACT.md section 8.7). */
export default function ThreadScreen() {
  return (
    <FeatureGate feature="forums">
      <ThreadScreenContent />
    </FeatureGate>
  );
}

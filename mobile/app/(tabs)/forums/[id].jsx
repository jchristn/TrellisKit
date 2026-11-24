import { useLocalSearchParams, useNavigation, useRouter } from 'expo-router';
import { useCallback, useEffect, useLayoutEffect, useState } from 'react';
import { View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useAuth } from '../../../src/auth/AuthProvider';
import ErrorState from '../../../src/components/ErrorState';
import Loading from '../../../src/components/Loading';
import ThreadList from '../../../src/components/forums/ThreadList';
import { describeContentError } from '../../../src/content/errors';
import strings from '../../../src/strings';
import { useTheme } from '../../../src/theme';
import FeatureGate from '../../../src/components/FeatureGate';

/** One forum's threads on a phone (`/forums/{id}`, also the `NewThreadInForum` push path). */
function ForumScreenContent() {
  const theme = useTheme();
  const router = useRouter();
  const navigation = useNavigation();
  const { api } = useAuth();
  const { id } = useLocalSearchParams();
  const forumId = Array.isArray(id) ? id[0] : id;
  const [forum, setForum] = useState(null);
  const [status, setStatus] = useState('loading');
  const [error, setError] = useState(null);

  const load = useCallback(async () => {
    setStatus('loading');
    try {
      setForum(await api.getForum(forumId));
      setStatus('ready');
    } catch (e) {
      setError(e);
      setStatus(e && e.status === 404 ? 'gone' : 'error');
    }
  }, [api, forumId]);

  useEffect(() => {
    // The first load starts in the loading state already.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    load();
  }, [load]);

  useLayoutEffect(() => {
    if (forum) navigation.setOptions({ title: forum.name });
  }, [navigation, forum]);

  let content;
  if (status === 'loading') content = <Loading label={strings.forums.threadsLoading} />;
  else if (status !== 'ready')
    content = (
      <View style={{ padding: theme.spacing.lg }}>
        <ErrorState title={status === 'gone' ? strings.forums.forumGoneTitle : strings.forums.threadsFailedTitle} message={describeContentError(error)} onRetry={status === 'gone' ? undefined : load} testID="forum-error" />
      </View>
    );
  else content = <ThreadList forum={forum} onOpen={(thread) => router.push('/threads/' + thread.id)} onForumChange={(next) => setForum((f) => ({ ...f, ...next }))} />;

  return (
    <SafeAreaView testID="screen-forum" edges={['left', 'right']} style={{ flex: 1, backgroundColor: theme.colors.background }}>
      {content}
    </SafeAreaView>
  );
}

/** Gated by the access model: the `forums` feature (ACCESS_MODELS_CONTRACT.md section 8.7). */
export default function ForumScreen() {
  return (
    <FeatureGate feature="forums">
      <ForumScreenContent />
    </FeatureGate>
  );
}

import { useRouter } from 'expo-router';
import { useCallback, useState } from 'react';
import { ScrollView, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useAuth } from '../../../src/auth/AuthProvider';
import { BannerSlot } from '../../../src/appConfig';
import AppText from '../../../src/components/AppText';
import Button from '../../../src/components/Button';
import SplitView from '../../../src/components/SplitView';
import ForumList from '../../../src/components/forums/ForumList';
import ThreadDetail from '../../../src/components/forums/ThreadDetail';
import ThreadList from '../../../src/components/forums/ThreadList';
import { useForumList } from '../../../src/hooks/useForumList';
import { useLayout } from '../../../src/hooks/useLayout';
import strings from '../../../src/strings';
import { useTheme } from '../../../src/theme';
import FeatureGate from '../../../src/components/FeatureGate';
import { ForumIndexViews } from '@trelliskit/shared';
import ChipGroup from '../../../src/components/ChipGroup';
import { useStoredChoice } from '../../../src/hooks/useStoredChoice';

function Prompt({ text, testID }) {
  const theme = useTheme();
  return (
    <View style={{ flex: 1, alignItems: 'center', justifyContent: 'center', padding: theme.spacing.xl }} testID={testID}>
      <AppText color="textMuted">{text}</AppText>
    </View>
  );
}

/**
 * Forums tab (docs/PHASE3_CONTRACT.md section 19). Phones: the forum list grouped by section with
 * unread badges; a forum opens `/forums/{id}`. Tablets: the forum list, the selected forum's
 * threads, and the selected thread side by side in SplitView; below 900 points the forum list
 * collapses into a drawer opened from the thread list.
 */
function ForumsScreenContent() {
  const theme = useTheme();
  const router = useRouter();
  const { api } = useAuth();
  const { isWide, width } = useLayout();
  const forums = useForumList(api);
  // How the forums are shown on a phone (list, cards, or gallery), remembered on this device.
  const [view, setView] = useStoredChoice('forums-view', ForumIndexViews, 'list');
  const [forumId, setForumId] = useState(null);
  const [threadId, setThreadId] = useState(null);
  const [drawer, setDrawer] = useState(false);
  const selected = forums.forums.find((f) => f.id === forumId) || (isWide ? forums.forums[0] : null) || null;
  const openForum = useCallback(
    (forum) => {
      if (isWide) {
        setForumId(forum.id);
        setThreadId(null);
        setDrawer(false);
      } else router.push('/forums/' + forum.id);
    },
    [isWide, router]
  );

  if (!isWide) {
    return (
      <SafeAreaView testID="screen-forums" edges={['left', 'right']} style={{ flex: 1, backgroundColor: theme.colors.background }}>
        <ScrollView contentContainerStyle={{ padding: theme.spacing.lg }}>
          <BannerSlot placement="Forums" style={{ paddingBottom: theme.spacing.md }} />
          {forums.forums.length ? (
            <View style={{ paddingBottom: theme.spacing.md }}>
              <ChipGroup label={strings.forums.viewLabel} hideLabel options={ForumIndexViews.map((value) => ({ value, label: strings.forums.views[value] }))} value={view} onToggle={setView} testID="forums-view" />
            </View>
          ) : null}
          <ForumList list={forums} onOpen={openForum} view={view} />
        </ScrollView>
      </SafeAreaView>
    );
  }

  const narrowTablet = width < theme.layout.sidebarBreakpoint;
  const list = selected ? (
    <View style={{ flex: 1 }}>
      <BannerSlot placement="Forums" style={{ paddingHorizontal: theme.spacing.lg, paddingTop: theme.spacing.md }} />
      {narrowTablet ? (
        <View style={{ paddingHorizontal: theme.spacing.lg, paddingTop: theme.spacing.md }}>
          <Button label={strings.forums.forumsMenu} accessibilityHint={strings.forums.forumsMenuHint} variant="secondary" onPress={() => setDrawer(true)} style={{ alignSelf: 'auto' }} testID="forums-drawer-open" />
        </View>
      ) : null}
      <ThreadList key={selected.id} forum={selected} onOpen={(thread) => setThreadId(thread.id)} selectedId={threadId} onForumChange={forums.replace} filtersInline />
    </View>
  ) : (
    <Prompt text={forums.status === 'ready' && !forums.forums.length ? strings.forums.empty : strings.forums.selectForum} testID="forums-select" />
  );

  return (
    <SplitView
      testID="screen-forums"
      scroll={false}
      sidebar={<ForumList list={forums} onOpen={openForum} selectedId={selected ? selected.id : null} />}
      drawerOpen={drawer}
      onDrawerClose={() => setDrawer(false)}
      drawerTitle={strings.forums.title}
      list={list}
      detail={threadId ? <ThreadDetail key={threadId} threadId={threadId} onDeleted={() => setThreadId(null)} /> : <Prompt text={strings.forums.selectThread} testID="forums-select-thread" />}
    />
  );
}

/** Gated by the access model: the `forums` feature (ACCESS_MODELS_CONTRACT.md section 8.7). */
export default function ForumsScreen() {
  return (
    <FeatureGate feature="forums">
      <ForumsScreenContent />
    </FeatureGate>
  );
}

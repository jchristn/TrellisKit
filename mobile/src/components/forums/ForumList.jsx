import { AccessRequirement, groupForumsBySection } from '@trelliskit/shared';
import { View } from 'react-native';
import { describeContentError } from '../../content/errors';
import { useAuth } from '../../auth/AuthProvider';
import strings from '../../strings';
import { useTheme } from '../../theme';
import { AccessPromptCard } from '../AccessPrompt';
import AppText from '../AppText';
import Card from '../Card';
import ErrorState from '../ErrorState';
import Loading from '../Loading';
import ForumRow from './ForumRow';

/**
 * Forums grouped by section in `sortOrder` order (docs/PHASE3_CONTRACT.md section 2.1), with
 * unread badges. The list is bounded by `forums.maxForums`, so it renders without paging. `view`
 * is `list` (compact rows), `cards` (one taller card per row), or `gallery` (tiles, two across).
 */
export default function ForumList({ list, onOpen, selectedId = null, view = 'list', testID = 'forum-list' }) {
  const theme = useTheme();
  const { session } = useAuth();
  if (list.status === 'loading') return <Loading label={strings.forums.loading} testID={testID + '-loading'} />;
  if (list.status === 'error') return <ErrorState title={strings.forums.loadFailedTitle} message={describeContentError(list.error)} onRetry={list.reload} retryHint={strings.forums.retryHint} testID={testID + '-error'} />;
  const groups = groupForumsBySection(list.forums);
  if (!groups.length) {
    // A guest who sees no forums is looking at forums limited to signed-in accounts, not an empty
    // community: offer sign-in instead of "no forums yet".
    if (session && session.status === 'signedOut') {
      return <AccessPromptCard required={AccessRequirement.SignIn} feature="forums" title={strings.forums.guestEmptyTitle} body={strings.forums.guestEmptyBody} testID={testID + '-guest'} />;
    }
    return (
      <Card testID={testID + '-empty'}>
        <AppText>{strings.forums.empty}</AppText>
      </Card>
    );
  }
  return (
    <View style={{ gap: theme.spacing.lg }} testID={testID}>
      {groups.map((group) => (
        <View key={group.section || '-'} style={{ gap: theme.spacing.sm }}>
          <AppText variant="label" color="textMuted" accessibilityRole="header">
            {group.section || (groups.length > 1 ? strings.forums.noSection : strings.forums.title)}
          </AppText>
          {view === 'gallery' ? (
            <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: theme.spacing.sm }} testID={testID + '-gallery'}>
              {group.forums.map((forum) => (
                <View key={forum.id} style={{ flexBasis: '47%', flexGrow: 1, maxWidth: '50%' }}>
                  <ForumRow forum={forum} onPress={onOpen} selected={forum.id === selectedId} view="gallery" />
                </View>
              ))}
            </View>
          ) : (
            group.forums.map((forum) => <ForumRow key={forum.id} forum={forum} onPress={onOpen} selected={forum.id === selectedId} view={view} />)
          )}
        </View>
      ))}
    </View>
  );
}

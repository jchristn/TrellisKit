import { canUseIdeas } from '@trelliskit/shared';
import { useRouter } from 'expo-router';
import { useState } from 'react';
import { View } from 'react-native';
import { useAuth } from '../../auth/AuthProvider';
import { describeContentError } from '../../content/errors';
import { withVote } from '../../ideas/ideas';
import { useProperty } from '../../property';
import strings from '../../strings';
import { useTheme } from '../../theme';
import { AccessPromptCard } from '../AccessPrompt';
import AppText from '../AppText';
import Button from '../Button';
import Card from '../Card';
import Notice from '../Notice';
import MarkdownBody from '../feed/MarkdownBody';
import PostDetail from '../feed/PostDetail';
import ReactionBar from '../feed/ReactionBar';
import { StatusChip } from './IdeaRow';

const t = strings.ideas;

/** Member-facing text for an ideas error. */
export function describeIdeaError(error) {
  return (error && error.code && t.errors[error.code]) || describeContentError(error);
}

/**
 * The idea card above its comments: title, status with the team's note, the description, the
 * merged banner, Vote (optimistic, with rollback), Follow, and reactions from the property set.
 */
export function IdeaHeader({ idea, onChange, onOpenIdea, testID = 'idea-header' }) {
  const theme = useTheme();
  const { api, user } = useAuth();
  const property = useProperty();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(null);
  const viewer = idea.viewer || {};
  const write = canUseIdeas(property, user, 'write');
  const votes = t.votes(Number(idea.voteCount) || 0);

  async function vote() {
    const voted = !idea.hasVoted;
    const previous = idea;
    setError(null);
    setBusy(true);
    onChange(withVote(idea, voted));
    try {
      const next = voted ? await api.voteIdea(idea.id) : await api.unvoteIdea(idea.id);
      if (next && next.id) onChange({ ...idea, ...next });
    } catch (e) {
      onChange(previous);
      setError(describeIdeaError(e));
    } finally {
      setBusy(false);
    }
  }

  async function follow() {
    const following = !idea.isFollowed;
    setError(null);
    setBusy(true);
    try {
      const result = following ? await api.followIdea(idea.id) : await api.unfollowIdea(idea.id);
      onChange({ ...idea, isFollowed: result && typeof result.isFollowed === 'boolean' ? result.isFollowed : following });
    } catch (e) {
      setError(describeIdeaError(e));
    } finally {
      setBusy(false);
    }
  }

  return (
    <Card testID={testID}>
      <AppText variant="title" accessibilityRole="header" testID={testID + '-title'}>
        {idea.title}
      </AppText>
      <StatusChip status={idea.status} testID={testID + '-status'} />
      {idea.statusNote ? (
        <View style={{ gap: theme.spacing.xxs }} testID={testID + '-status-note'}>
          <AppText variant="label">{t.statusNote}</AppText>
          <AppText color="textMuted">{idea.statusNote}</AppText>
        </View>
      ) : null}
      {idea.mergedInto ? (
        <Notice tone="info" testID={testID + '-merged'}>
          {t.merged(idea.mergedInto.title || '')}
        </Notice>
      ) : null}
      {idea.mergedInto && onOpenIdea ? <Button label={t.openMerged} variant="ghost" onPress={() => onOpenIdea(idea.mergedInto.id)} testID={testID + '-open-merged'} /> : null}
      {idea.body ? <MarkdownBody body={idea.body} testID={testID + '-body'} /> : null}
      {!write.allowed && write.requires ? (
        <AccessPromptCard required={write.requires} feature="ideas" testID={testID + '-locked'} />
      ) : (
        <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: theme.spacing.sm, alignItems: 'center' }}>
          <Button
            label={(idea.hasVoted ? t.voted : t.vote) + ' · ' + votes}
            accessibilityLabel={idea.hasVoted ? t.unvoteLabel(votes) : t.voteLabel(votes)}
            variant={idea.hasVoted ? 'primary' : 'secondary'}
            onPress={vote}
            disabled={busy || (viewer.canVote === false && !idea.hasVoted)}
            style={{ alignSelf: 'auto' }}
            testID={testID + '-vote'}
          />
          <Button label={idea.isFollowed ? t.following : t.follow} accessibilityHint={t.followHint} variant="ghost" onPress={follow} disabled={busy} style={{ alignSelf: 'auto' }} testID={testID + '-follow'} />
        </View>
      )}
      <Notice tone="error" testID={testID + '-error'}>
        {error}
      </Notice>
      <ReactionBar target={{ type: 'Idea', id: idea.id }} fields={idea} onChange={(fields) => onChange({ ...idea, ...fields })} canReact={viewer.canReact !== false && write.allowed} testID={testID + '-reactions'} />
    </Card>
  );
}

const loadIdea = (api, id) => api.getIdea(id);

/**
 * An idea with its comments (Phase 2 comment tree on the idea's `commentTarget`), built on the
 * shared post detail. Merged ideas keep their comments read only.
 */
export default function IdeaDetail({ ideaId, initialIdea = null, onIdeaChange, testID = 'idea-detail' }) {
  const router = useRouter();
  return (
    <PostDetail
      postId={ideaId}
      initialPost={initialIdea}
      onPostChange={onIdeaChange}
      loadItem={loadIdea}
      renderCard={({ item, onChange }) => <IdeaHeader idea={item} onChange={onChange} onOpenIdea={(id) => router.push('/ideas/' + id)} />}
      canComment={(item) => Boolean(item) && !item.mergedInto && !(item.viewer && item.viewer.canComment === false)}
      goneText={{ title: t.goneTitle, body: t.goneBody }}
      accessFeature="ideas"
      testID={testID}
    />
  );
}

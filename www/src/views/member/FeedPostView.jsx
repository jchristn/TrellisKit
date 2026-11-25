import { useCallback, useState } from 'react';
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom';
import strings from '../../strings.js';
import { useAuth } from '../../context/AuthContext.jsx';
import useApiQuery from '../../hooks/useApiQuery.js';
import useDocumentMeta from '../../hooks/useDocumentMeta.js';
import Icon from '../../components/Icon.jsx';
import SaveButton from '../../components/SaveButton.jsx';
import { EmptyState, ErrorBanner, Loading } from '../../components/States.jsx';
import PostCard from '../../components/feed/PostCard.jsx';
import CommentThread from '../../components/feed/CommentThread.jsx';
import { errorText } from '../../utils/engagement.js';
import { accessDenial, isLockedItem } from '@trelliskit/shared';
import { AccessPromptCard } from '../../components/AccessPrompt.jsx';

const f = strings.feed;

/**
 * /app/feed/:id: one post with its comment tree; `?comment=cmt_...` opens the comment permalink
 * (from notifications) with its ancestors and a link back to all comments. Content that is gone
 * shows "This content is no longer available".
 */
function FeedPostView() {
  const { id } = useParams();
  const [searchParams] = useSearchParams();
  const focusId = searchParams.get('comment');
  const navigate = useNavigate();
  const { client } = useAuth();
  const fetcher = useCallback((signal) => client.getFeedPost(id, signal), [client, id]);
  const query = useApiQuery(fetcher);
  const [override, setOverride] = useState(null);
  const post = override && override.id === id ? override : query.data;
  useDocumentMeta(strings.member.metaTitle(post && post.title ? post.title : f.title));
  const setCount = useCallback((count) => setOverride((current) => {
    const base = current && current.id === id ? current : query.data;
    return base && base.commentCount !== count ? { ...base, commentCount: count } : current;
  }), [id, query.data]);

  const back = <Link to="/app/feed" className="back-link small"><Icon name="chevronLeft" size={14} /><span>{f.backToFeed}</span></Link>;

  const denial = accessDenial(query.error);
  if (denial) {
    return (
      <div className="member-view">
        {back}
        <AccessPromptCard required={denial.required} feature={denial.feature || 'feed'} />
      </div>
    );
  }

  if (query.error && query.error.status === 404) {
    return (
      <div className="member-view">
        {back}
        <EmptyState icon="info" title={f.postNotFound} action={<Link to="/app/feed" className="button button-primary">{f.backToFeed}</Link>}>{f.postNotFoundBody}</EmptyState>
      </div>
    );
  }

  return (
    <div className="member-view post-view">
      {back}
      {query.loading && !post && <Loading />}
      {query.error && <ErrorBanner message={errorText(query.error, {}, f.loadError)} onRetry={query.reload} />}
      {post && (
        <>
          {!isLockedItem(post) && <div className="save-row"><SaveButton targetType="Post" targetId={post.id} title={post.title || strings.training.saved.typeOne.Post} /></div>}
          <PostCard post={post} detail headingLevel={1} onChange={setOverride} onDeleted={() => navigate('/app/feed', { replace: true })} onBlocked={() => navigate('/app/feed', { replace: true })} />
          {!isLockedItem(post) && <CommentThread key={post.id + ':' + (focusId || '')} post={post} focusId={focusId} onCountChange={setCount} />}
        </>
      )}
    </div>
  );
}

export default FeedPostView;

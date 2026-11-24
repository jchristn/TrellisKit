import { Link } from 'react-router-dom';
import { AccessRequirement, ForumIndexViews, formatRelativeTime, forumIndexView, groupForumsBySection, hasServerCapability, unreadBadge } from '@trelliskit/shared';
import strings from '../../strings.js';
import { useAuth } from '../../context/AuthContext.jsx';
import { useProperty } from '../../context/PropertyContext.jsx';
import useDocumentMeta from '../../hooks/useDocumentMeta.js';
import useEnumerationUrlState from '../../hooks/useEnumerationUrlState.js';
import useForumList from '../../hooks/useForumList.js';
import useLocalStorage from '../../hooks/useLocalStorage.js';
import Icon from '../../components/Icon.jsx';
import { Badge } from '../../components/Badges.jsx';
import { EmptyState, ErrorBanner, Loading } from '../../components/States.jsx';
import MetadataFilterBar from '../../components/MetadataFilterBar.jsx';
import ForumIcon from '../../components/forums/ForumIcon.jsx';
import FollowButton from '../../components/forums/FollowButton.jsx';
import { AccessPromptCard } from '../../components/AccessPrompt.jsx';
import { errorText } from '../../utils/engagement.js';
import { appForumPath, appThreadPath } from '../../utils/forums.js';

const t = strings.forums;

const VIEW_ICONS = { list: 'list', cards: 'grid', gallery: 'image' };
const ICON_SIZES = { list: 40, cards: 48, gallery: 96 };

function ForumCard({ forum, client, onChange, view }) {
  const badge = unreadBadge(forum.unreadThreadCount);
  const viewer = forum.viewer || {};
  return (
    <li className="forum-card">
      <ForumIcon forum={forum} size={ICON_SIZES[view]} />
      <div className="forum-card-main">
        <h3 className="forum-card-title">
          <Link to={appForumPath(forum)}>{forum.name}</Link>
          {badge && <span className="badge badge-info unread-badge" aria-label={t.unreadLabel(badge)}>{badge}</span>}
          {forum.isArchived && <Badge tone="neutral">{t.archived}</Badge>}
        </h3>
        {forum.description && <p className="muted small forum-card-description">{forum.description}</p>}
        <p className="muted small forum-card-meta">
          <span>{t.threadCount(forum.threadCount || 0)}</span>
          <span aria-hidden="true"> · </span>
          <span>{t.commentCount(forum.commentCount || 0)}</span>
          {forum.lastThread && (
            <span className="forum-card-last">
              <span aria-hidden="true"> · </span>
              <Link to={appThreadPath(forum.lastThread.id)} className="muted">{forum.lastThread.title}</Link>
              {forum.lastThread.lastActivityUtc && <span> ({formatRelativeTime(forum.lastThread.lastActivityUtc)})</span>}
            </span>
          )}
        </p>
      </div>
      {viewer.canFollow !== false && (
        <FollowButton
          followed={Boolean(forum.isFollowed)}
          label={t.followForumLabel(forum.name)}
          unfollowLabel={t.unfollowForumLabel(forum.name)}
          onToggle={async (next) => {
            const updated = next ? await client.followForum(forum.id) : await client.unfollowForum(forum.id);
            onChange(updated && updated.id ? updated : { ...forum, isFollowed: next });
          }}
        />
      )}
    </li>
  );
}

/**
 * /app/forums (PHASE3_CONTRACT.md section 19): every forum the member can see, grouped by
 * section in `sortOrder` order, with unread counts, the latest thread, and follow toggles; a
 * search with label and tag filters (URL state), and links to Following and global search. The
 * member picks how the forums are shown: a list (one per row, with the latest thread), cards (a
 * grid with descriptions), or a gallery (large icons and names); the choice is remembered in the
 * browser.
 */
function ForumsIndexView() {
  const { client, me } = useAuth();
  // The global search box shows only when the server reports search (features.search can be false).
  const searchOn = hasServerCapability(useProperty().property, 'search');
  useDocumentMeta(t.meta, t.summary);
  const [meta, setMeta] = useEnumerationUrlState();
  const query = { search: meta.search, labels: meta.labels, labelMatch: meta.labelMatch, tags: meta.tags };
  const list = useForumList(client, query);
  const filtered = Boolean(meta.search || (meta.labels && meta.labels.length) || (meta.tags && Object.keys(meta.tags).length));
  const groups = groupForumsBySection(list.forums);
  const [storedView, setStoredView] = useLocalStorage('tk.forums.view', 'list');
  const view = forumIndexView(storedView);
  const replace = (next) => list.setForums((forums) => forums.map((f) => (f.id === next.id ? { ...f, ...next } : f)));
  return (
    <div className="member-view forums-view">
      <header className="page-header">
        <div className="page-header-text">
          <h1>{t.title}</h1>
          <p className="muted">{t.summary}</p>
        </div>
        {me && <div className="page-header-actions">
          <Link to="/app/forums/following" className="button button-secondary button-sm"><Icon name="bell" size={14} /><span>{t.followingLink}</span></Link>
          {searchOn && <Link to="/app/search" className="button button-secondary button-sm"><Icon name="search" size={14} /><span>{t.searchAll}</span></Link>}
        </div>}
      </header>
      <MetadataFilterBar value={meta} onChange={setMeta} client={client} targetType="Forum" showSearch searchLabel={t.search} />
      <div className="forum-view-bar">
        <div className="segmented" role="radiogroup" aria-label={t.viewLabel}>
          {ForumIndexViews.map((option) => (
            <button key={option} type="button" role="radio" aria-checked={view === option} className={'segment' + (view === option ? ' active' : '')} onClick={() => setStoredView(option)} data-testid={'forum-view-' + option}>
              <Icon name={VIEW_ICONS[option]} size={12} /> {t.views[option]}
            </button>
          ))}
        </div>
      </div>
      <ErrorBanner message={list.error ? t.loadError + ' ' + errorText(list.error, {}, '') : ''} onRetry={list.reload} />
      {list.loading && !list.forums.length && <Loading />}
      {!list.loading && !list.error && !list.forums.length && (
        // A guest who sees no forums is usually looking at forums limited to signed-in accounts,
        // not an empty community: offer sign-in instead of "no forums yet".
        filtered ? <EmptyState icon="forum" title={t.noResults} />
          : me ? <EmptyState icon="forum" title={t.empty} />
            : <AccessPromptCard required={AccessRequirement.SignIn} feature="forums" title={t.guestEmptyTitle} body={t.guestEmptyBody} />
      )}
      {groups.map((group) => (
        <section key={group.section || '-'} className="forum-section" aria-labelledby={'forum-section-' + (group.section || 'none').replace(/\W+/g, '-')}>
          <h2 id={'forum-section-' + (group.section || 'none').replace(/\W+/g, '-')} className="feed-section-title">{group.section || (groups.length > 1 ? t.noSection : t.title)}</h2>
          <ul className={'forum-list forum-list-' + view} data-testid="forum-list">
            {group.forums.map((forum) => <ForumCard key={forum.id} forum={forum} client={client} onChange={replace} view={view} />)}
          </ul>
        </section>
      ))}
    </div>
  );
}

export default ForumsIndexView;

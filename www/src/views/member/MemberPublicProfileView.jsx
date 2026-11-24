import { useCallback, useMemo, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import strings from '../../strings.js';
import { useAuth } from '../../context/AuthContext.jsx';
import { useToast } from '../../context/ToastContext.jsx';
import { featureEnabled } from './memberRoutes.js';
import { conversationPath } from '../../utils/messaging.js';
import useApiQuery from '../../hooks/useApiQuery.js';
import useDocumentMeta from '../../hooks/useDocumentMeta.js';
import Avatar from '../../components/Avatar.jsx';
import Icon from '../../components/Icon.jsx';
import { CardFields, ProfileFieldList } from '../../components/profile/ProfileFieldDisplay.jsx';
import { builtInLabel } from '../../components/ProfileForm.jsx';
import { Badge } from '../../components/Badges.jsx';
import { EmptyState, ErrorBanner, Loading } from '../../components/States.jsx';
import ReportDialog from '../../components/feed/ReportDialog.jsx';
import { PublicPageLink } from '../../components/PublicPageFields.jsx';
import BlockDialog from '../../components/feed/BlockDialog.jsx';
import { authorName, errorText, mediaUrl } from '../../utils/engagement.js';
import { formatDate } from '../../utils/format.js';
import { useProperty } from '../../context/PropertyContext.jsx';
import { canUseMessaging, cardFields, profileFieldsOf, profilePageFields } from '@trelliskit/shared';

const p = strings.memberProfile;

/**
 * /app/members/:id: another member's profile (mention links land here) with Report and Block or
 * Unblock (contract section 9.2: `isBlockedByMe`; staff cannot be blocked).
 */
function MemberPublicProfileView() {
  const { id } = useParams();
  const { client, me } = useAuth();
  const { property } = useProperty();
  const fetcher = useCallback((signal) => client.getUser(id, signal), [client, id]);
  const query = useApiQuery(fetcher);
  const [blocked, setBlocked] = useState(null);
  const [dialog, setDialog] = useState(null);
  const user = query.data;
  useDocumentMeta(strings.member.metaTitle(user ? authorName(user) : p.title));
  const isSelf = Boolean(me && me.id === id);
  const isBlocked = blocked !== null ? blocked : Boolean(user && user.isBlockedByMe);
  const navigate = useNavigate();
  const toast = useToast();
  const [starting, setStarting] = useState(false);
  // "Message" appears when messaging is on, the caller may send, nobody is blocked, and the
  // profile's viewer (when the server sends one) does not refuse it.
  const canMessage = Boolean(user) && !isSelf && !isBlocked && !user.isDeleted && featureEnabled(property, 'messaging')
    && canUseMessaging(property, me, 'write').allowed && !(user.viewer && user.viewer.canMessage === false);
  const startMessage = async () => {
    setStarting(true);
    try {
      const result = await client.messaging.startConversation(user.id);
      navigate(conversationPath(result.conversation.id));
    } catch (err) {
      toast.error(errorText(err, strings.messaging.errors, strings.messaging.errors.generic));
      setStarting(false);
    }
  };
  const fields = useMemo(() => profileFieldsOf(property), [property]);
  const profile = (user && user.profile) || {};
  const cardKeys = new Set(cardFields(fields, profile).map((f) => f.key));
  const body = profilePageFields(fields, profile).filter((f) => !cardKeys.has(f.key) || f.type === 'History');

  if (query.error && query.error.status === 404) {
    return <div className="member-view"><EmptyState icon="user" title={p.notFound} action={<Link to="/app/feed" className="button button-secondary">{strings.feed.backToFeed}</Link>} /></div>;
  }

  return (
    <div className="member-view">
      {query.loading && !user && <Loading />}
      {query.error && <ErrorBanner message={errorText(query.error)} onRetry={query.reload} />}
      {user && (
        <section className="panel profile-card" aria-labelledby="member-name">
          <div className="profile-head">
            <Avatar src={mediaUrl(user.avatarUrl)} name={authorName(user)} size={72} />
            <div>
              <h1 id="member-name">{authorName(user)}</h1>
              {user.handle && <p className="muted">@{user.handle}</p>}
              <div className="badge-row">
                {user.isStaff && <Badge tone="info">{p.staff}</Badge>}
                {isBlocked && <Badge tone="warning">{p.youBlocked}</Badge>}
                {isSelf && <Badge tone="neutral">{p.yourProfile}</Badge>}
              </div>
            </div>
          </div>
          <div className="profile-chips"><CardFields fields={fields} profile={profile} /></div>
          {user.bio && <div><h2 className="label-text">{builtInLabel(fields, 'bio', strings.member.bioTitle)}</h2><p className="profile-bio preserve-lines">{user.bio}</p></div>}
          <ProfileFieldList fields={fields} profile={profile} showEmpty={body} />
          {user.createdUtc && <p className="muted small">{p.joined(formatDate(user.createdUtc))}</p>}
          <PublicPageLink url={user.publicUrl} />
          {!isSelf && (
            <div className="row-actions">
              {canMessage && <button type="button" className="button button-primary button-sm" onClick={startMessage} disabled={starting}><Icon name="message" size={14} /><span>{starting ? strings.messaging.starting : strings.messaging.messageButton}</span></button>}
              <button type="button" className="button button-secondary button-sm" onClick={() => setDialog('report')}><Icon name="flag" size={14} /><span>{p.reportMember}</span></button>
              {isBlocked && <button type="button" className="button button-secondary button-sm" onClick={() => setDialog('unblock')}><Icon name="ban" size={14} /><span>{p.unblockMember}</span></button>}
              {!isBlocked && !user.isStaff && <button type="button" className="button button-danger-outline button-sm" onClick={() => setDialog('block')}><Icon name="ban" size={14} /><span>{p.blockMember}</span></button>}
            </div>
          )}
        </section>
      )}
      {user && dialog === 'report' && <ReportDialog target={{ type: 'User', id: user.id }} author={user} onClose={() => setDialog(null)} onBlocked={() => setBlocked(true)} />}
      {user && (dialog === 'block' || dialog === 'unblock') && (
        <BlockDialog user={user} unblock={dialog === 'unblock'} onClose={() => setDialog(null)} onDone={(_u, nowBlocked) => setBlocked(nowBlocked)} />
      )}
    </div>
  );
}

export default MemberPublicProfileView;

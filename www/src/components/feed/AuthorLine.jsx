import { Link } from 'react-router-dom';
import strings from '../../strings.js';
import Avatar from '../Avatar.jsx';
import { useMemo } from 'react';
import { cardFields, profileFieldsOf } from '@trelliskit/shared';
import { useProperty } from '../../context/PropertyContext.jsx';
import { CardFields } from '../profile/ProfileFieldDisplay.jsx';
import { authorName, memberPath, mediaUrl } from '../../utils/engagement.js';
import { formatDateTime } from '../../utils/format.js';
import { formatRelativeTime } from '@trelliskit/shared';

/**
 * Author avatar, name (linked to the profile), staff badge, card profile fields (`UserSummary.profile`,
 * section 9.11 member card), and a timestamp that links to
 * the item's permalink. Deleted accounts show "[deleted]" without a link.
 * @param {{ user: object, time?: string, timeTo?: string, size?: number, linkProfile?: boolean, extra?: React.ReactNode }} props
 */
function AuthorLine({ user, time, timeTo, size = 36, linkProfile = true, extra }) {
  const name = authorName(user);
  const deleted = !user || user.isDeleted;
  const nameNode = deleted || !linkProfile || !user.id
    ? <span className="author-name">{name}</span>
    : <Link className="author-name" to={memberPath(user.id)}>{name}</Link>;
  const timeText = time ? formatRelativeTime(time) : '';
  const { property } = useProperty();
  const fields = useMemo(() => profileFieldsOf(property), [property]);
  const profile = (!deleted && user && user.profile) || null;
  const hasCard = Boolean(profile) && cardFields(fields, profile).length > 0;
  return (
    <div className="author-line">
      <Avatar src={deleted ? null : mediaUrl(user.avatarUrl)} name={deleted ? '?' : name} size={size} />
      <div className="author-text">
        <div className="author-row">
          {nameNode}
          {!deleted && user.isStaff && <span className="badge badge-info staff-badge">{strings.feed.staffBadge}</span>}
          {!deleted && user.handle && <span className="muted small author-handle">@{user.handle}</span>}
        </div>
        <div className="author-meta muted small">
          {hasCard && <CardFields fields={fields} profile={profile} />}
          {hasCard && timeText && <span aria-hidden="true">·</span>}
          {timeText && (timeTo
            ? <Link to={timeTo} className="muted" title={formatDateTime(time)}><time dateTime={time}>{timeText}</time></Link>
            : <time dateTime={time} title={formatDateTime(time)}>{timeText}</time>)}
          {extra}
        </div>
      </div>
    </div>
  );
}

export default AuthorLine;

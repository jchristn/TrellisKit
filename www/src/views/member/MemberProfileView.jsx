import { useMemo } from 'react';
import { Link } from 'react-router-dom';
import { builtInField, cardFields, profileFieldsOf, profilePageFields, rankBadgeMap } from '@trelliskit/shared';
import strings from '../../strings.js';
import { useAuth } from '../../context/AuthContext.jsx';
import { useProperty } from '../../context/PropertyContext.jsx';
import useDocumentMeta from '../../hooks/useDocumentMeta.js';
import Avatar from '../../components/Avatar.jsx';
import { CardFields, ProfileFieldList } from '../../components/profile/ProfileFieldDisplay.jsx';
import { builtInLabel } from '../../components/ProfileForm.jsx';
import { PublicPageLink } from '../../components/PublicPageFields.jsx';
import { formatDate } from '../../utils/format.js';
import { useMyRank, useRanksFeature } from '../ranks/rankHooks.js';

const m = strings.member;

/**
 * /app/profile: the member's own profile page (section 9.11): header with avatar, name, handle,
 * and card fields as chips; then the bio and every field with `showOnProfile` and a value, in
 * profile order, rendered from the property's definitions and `Me.profile`.
 */
function MemberProfileView() {
  const { me, client } = useAuth();
  const { property } = useProperty();
  const ranks = useRanksFeature();
  const myRank = useMyRank(client, ranks.labels);
  const rankBadges = useMemo(() => (ranks.labels ? rankBadgeMap(myRank.data) : null), [ranks.labels, myRank.data]);
  const fields = useMemo(() => profileFieldsOf(property), [property]);
  useDocumentMeta(m.metaTitle(m.profileTitle), m.metaDescription);
  const profile = me.profile || {};
  const bio = builtInField(fields, 'bio');
  const showBio = Boolean(me.bio) && (!bio || bio.showOnProfile !== false);
  const cardKeys = new Set(cardFields(fields, profile).map((f) => f.key));
  const body = profilePageFields(fields, profile).filter((f) => !cardKeys.has(f.key) || f.type === 'History');
  return (
    <div className="member-view">
      <section className="panel profile-card">
        <div className="profile-head">
          <Avatar src={me.avatarUrl} name={me.displayName} size={88} alt={strings.profileForm.photoAlt(me.displayName || '')} />
          <div>
            <h1>{me.displayName}</h1>
            {me.handle && <p className="muted mono">@{me.handle}</p>}
            <div className="profile-chips"><CardFields fields={fields} profile={profile} rankBadges={rankBadges} /></div>
            {me.createdUtc && <p className="muted small">{m.memberSince(formatDate(me.createdUtc))}</p>}
            {me.publicPage && me.publicPage.enabled && <PublicPageLink url={me.publicPage.url} />}
          </div>
          <Link to="/app/profile" className="button button-secondary button-sm">{m.profileEdit}</Link>
          {ranks.enabled && <Link to="/app/rank" className="button button-ghost button-sm">{strings.ranks.nav}</Link>}
        </div>
        {showBio && <div><h2 className="label-text">{builtInLabel(fields, 'bio', m.bioTitle)}</h2><p className="preserve-lines">{me.bio}</p></div>}
        <ProfileFieldList fields={fields} profile={profile} showEmpty={body} rankBadges={rankBadges} />
        {!showBio && body.length === 0 && <p className="muted">{m.profileNothing}</p>}
      </section>
    </div>
  );
}

export default MemberProfileView;

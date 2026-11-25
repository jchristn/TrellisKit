import ProfileMedia from '../../src/components/profile/ProfileMedia';
import ThemeToggle from '../../src/components/ThemeToggle';
import { canUseMessaging, propertyFeatureEnabled } from '@trelliskit/shared';
import { useLocalSearchParams, useRouter } from 'expo-router';
import { useCallback, useEffect, useState } from 'react';
import { View } from 'react-native';
import { members as membersApi } from '../../src/api/community';
import { useAuth } from '../../src/auth/AuthProvider';
import AppText from '../../src/components/AppText';
import Avatar from '../../src/components/Avatar';
import Button from '../../src/components/Button';
import Card from '../../src/components/Card';
import ErrorState from '../../src/components/ErrorState';
import { StaffBadge } from '../../src/components/feed/AuthorRow';
import ConfirmSheet from '../../src/components/feed/ConfirmSheet';
import ReportSheet from '../../src/components/feed/ReportSheet';
import { useBlock } from '../../src/components/feed/useBlock';
import Loading from '../../src/components/Loading';
import Notice from '../../src/components/Notice';
import Screen from '../../src/components/Screen';
import { CardFields, ProfileFieldList } from '../../src/components/profile/ProfileDisplay';
import { describeContentError } from '../../src/content/errors';
import { emitContentEvent } from '../../src/content/events';
import { describeMessagingError } from '../../src/messaging/format';
import { useProfileFields } from '../../src/profile/fields';
import { useProperty } from '../../src/property';
import strings from '../../src/strings';
import { useTheme } from '../../src/theme';
import FeatureGate from '../../src/components/FeatureGate';
import { useMemberRankBadges } from '../../src/components/ranks/rankHooks';

/**
 * Another member's public profile (GET /users/{id}): avatar, name, handle, and card fields in the
 * header, then the bio when present and every field of the viewer-filtered `profile` map in
 * profile order (History entries as a list in stored order). Members can message the member
 * (when messaging is on, the caller may write, nobody is blocked, and the profile's viewer does
 * not refuse it; the same rules as the website), block or unblock (staff cannot be blocked, and
 * the screen says why), and report the profile.
 */
function MemberProfileScreenContent() {
  const theme = useTheme();
  const fields = useProfileFields();
  const { api, user } = useAuth();
  const router = useRouter();
  const property = useProperty();
  const { id, reportMedia } = useLocalSearchParams();
  const memberId = Array.isArray(id) ? id[0] : id;
  const [profile, setProfile] = useState(null);
  const [status, setStatus] = useState('loading');
  const [error, setError] = useState(null);
  const [sheet, setSheet] = useState(null);
  const [message, setMessage] = useState(null);
  const [starting, setStarting] = useState(false);
  const [startError, setStartError] = useState(null);
  const blocker = useBlock(api);
  const rankBadges = useMemberRankBadges(api, memberId);
  const t = strings.member;

  const load = useCallback(async () => {
    setError(null);
    try {
      setProfile(await membersApi.get(api, memberId));
      setStatus('ready');
    } catch (e) {
      setError(e);
      setStatus(e && e.status === 404 ? 'gone' : 'error');
    }
  }, [api, memberId]);

  useEffect(() => {
    // load sets state after its request; clearing the previous error is harmless.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    load();
  }, [load]);

  if (status === 'loading') return <Loading label={strings.common.loading} testID="member-loading" />;
  if (status !== 'ready' || !profile) {
    return (
      <Screen testID="screen-member">
        <ErrorState title={status === 'gone' ? t.unavailableTitle : t.loadFailedTitle} message={status === 'gone' ? null : describeContentError(error)} onRetry={status === 'gone' ? undefined : load} testID="member-error" />
      </Screen>
    );
  }

  const isSelf = Boolean(user && user.id === profile.id);
  const isStaff = Boolean(profile.isStaff);
  const blocked = Boolean(profile.isBlockedByMe);
  const values = profile.profile || {};
  const summary = { id: profile.id, displayName: profile.displayName, handle: profile.handle, isStaff, avatarUrl: profile.avatarUrl };
  const handle = profile.handle || profile.displayName;

  const canMessage = !isSelf && !blocked && !profile.isDeleted && propertyFeatureEnabled(property, 'messaging') && canUseMessaging(property, user, 'write').allowed && !(profile.viewer && profile.viewer.canMessage === false);

  async function startMessage() {
    setStartError(null);
    setStarting(true);
    try {
      const result = await api.messaging.startConversation(profile.id);
      router.push('/messages/' + result.conversation.id);
    } catch (e) {
      setStartError(describeMessagingError(e));
    } finally {
      setStarting(false);
    }
  }

  async function toggleBlock() {
    setMessage(null);
    const result = blocked ? await blocker.unblock(summary) : await blocker.block(summary);
    if (result.ok) {
      setSheet(null);
      setProfile((p) => ({ ...p, isBlockedByMe: !blocked }));
      setMessage(blocked ? strings.block.unblocked(handle) : strings.block.blocked(handle));
      if (!blocked) emitContentEvent('blocked', profile.id);
    }
  }

  return (
    <Screen contentStyle={{ gap: theme.spacing.lg }} testID="screen-member">
      <ThemeToggle />
      <Card>
        <View style={{ flexDirection: 'row', alignItems: 'center', gap: theme.spacing.lg }}>
          <Avatar uri={profile.avatarUrl ? api.url(profile.avatarUrl) : null} token={api.accessToken} name={profile.displayName} />
          <View style={{ flex: 1, gap: theme.spacing.xxs }}>
            <View style={{ flexDirection: 'row', alignItems: 'center', flexWrap: 'wrap', gap: theme.spacing.xs }}>
              <AppText variant="heading" accessibilityRole="header" testID="member-name">
                {profile.displayName}
              </AppText>
              {isStaff ? <StaffBadge /> : null}
            </View>
            {profile.handle ? <AppText color="textMuted">{'@' + profile.handle}</AppText> : null}
            <CardFields profile={values} fields={fields} testID="member-card" rankBadges={rankBadges} />
            {profile.createdUtc ? (
              <AppText variant="caption" color="textMuted">
                {t.joined(new Date(profile.createdUtc).toLocaleDateString(undefined, { month: 'long', year: 'numeric' }))}
              </AppText>
            ) : null}
          </View>
        </View>
        {profile.bio ? <AppText testID="member-bio">{profile.bio}</AppText> : null}
        <ProfileFieldList profile={values} fields={fields} testID="member-fields" rankBadges={rankBadges} />
      </Card>

      {!blocked ? <ProfileMedia user={profile} initialReportId={Array.isArray(reportMedia) ? reportMedia[0] : reportMedia} /> : null}
      {isSelf ? <AppText color="textMuted">{t.you}</AppText> : null}
      {!isSelf && blocked ? <Notice tone="info">{t.blockedNote}</Notice> : null}
      <Notice tone={blocker.error ? 'error' : 'success'} testID="member-message">
        {blocker.error || message}
      </Notice>
      <Notice tone="error" testID="member-start-error">
        {startError}
      </Notice>
      {!isSelf ? (
        <View style={{ gap: theme.spacing.md }}>
          {canMessage ? <Button label={starting ? strings.messaging.starting : strings.messaging.messageButton} accessibilityHint={strings.messaging.messageButtonHint} onPress={startMessage} disabled={starting} testID="member-send-message" /> : null}
          {isStaff && !blocked ? (
            <AppText color="textMuted" testID="member-staff-note">
              {strings.block.staff}
            </AppText>
          ) : (
            <Button label={blocked ? t.unblock : t.block} variant="secondary" onPress={() => (blocked ? toggleBlock() : setSheet('block'))} disabled={blocker.busy} testID="member-block" />
          )}
          <Button label={t.report} variant="ghost" onPress={() => setSheet('report')} testID="member-report" />
        </View>
      ) : null}

      <ConfirmSheet visible={sheet === 'block'} title={strings.block.confirmTitle(handle)} body={strings.block.confirmBody} confirmLabel={strings.block.block} onConfirm={toggleBlock} onClose={() => setSheet(null)} busy={blocker.busy} error={blocker.error} testID="member-block-confirm" />
      {sheet === 'report' ? <ReportSheet target={{ targetType: 'User', targetId: profile.id }} author={blocked ? null : summary} onClose={() => setSheet(null)} onBlocked={() => setProfile((p) => ({ ...p, isBlockedByMe: true }))} /> : null}
    </Screen>
  );
}

/** Gated by the access model: the `members` feature (ACCESS_MODELS_CONTRACT.md section 8.7). */
export default function MemberProfileScreen() {
  return (
    <FeatureGate feature="members" allowGuest={false}>
      <MemberProfileScreenContent />
    </FeatureGate>
  );
}

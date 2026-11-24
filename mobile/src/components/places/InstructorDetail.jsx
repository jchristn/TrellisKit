import { isLockedItem, lockedRequirement } from '@trelliskit/shared';
import { useRouter } from 'expo-router';
import { useState } from 'react';
import { ScrollView, View } from 'react-native';
import { useAuth } from '../../auth/AuthProvider';
import { describeFeatureError, imageUrl, instructorRankText, useDirectoryStrings } from '../../content/phase4';
import { useResource } from '../../hooks/useResource';
import { openInAppBrowser } from '../../links';
import { useProperty } from '../../property';
import { useTheme } from '../../theme';
import { AccessPromptCard } from '../AccessPrompt';
import AppText from '../AppText';
import Button from '../Button';
import Card from '../Card';
import ErrorState from '../ErrorState';
import Loading from '../Loading';
import MarkdownBody from '../feed/MarkdownBody';
import MediaImage from '../feed/MediaImage';
import ReportSheet, { DirectoryReportReasons } from '../feed/ReportSheet';
import VenueCard from './VenueCard';

/**
 * An instructor (docs/PHASE4_CONTRACT.md sections 2.3 and 3.8): photo, name, rank from the
 * property's rank field, affiliation, bio, the linked member's profile, the venues they are listed
 * at (paged in place), and "This is me" (claim), Suggest an edit, Report, and "Unlink me" for the
 * linked member. A locked stub shows the membership prompt.
 */
export default function InstructorDetail({ instructorId, testID = 'instructor' }) {
  const theme = useTheme();
  const router = useRouter();
  const property = useProperty();
  const { api, user } = useAuth();
  const t = useDirectoryStrings();
  const resource = useResource(() => api.getInstructor(instructorId), instructorId);
  const [venues, setVenues] = useState(null);
  const [token, setToken] = useState(null);
  const [reporting, setReporting] = useState(false);
  const [notice, setNotice] = useState(null);
  const instructor = resource.data;

  async function moreVenues() {
    const page = await api.enumerateInstructorVenues(instructor.id, { maxResults: 25, continuationToken: token || undefined });
    setVenues((current) => (current || []).concat((page && page.objects) || []));
    setToken(page && !page.endOfResults && page.continuationToken ? page.continuationToken : null);
  }

  async function unlink() {
    try {
      await api.unlinkMyInstructor(instructor.id);
      setNotice(t.unlinked);
      resource.reload({ quiet: true });
    } catch (e) {
      setNotice(describeFeatureError(e, 'places', t));
    }
  }

  if (resource.status === 'loading') return <Loading label={t.loading} testID={testID + '-loading'} />;
  if (resource.status === 'gone') return <ErrorState title={t.instructorGoneTitle} message={t.instructorGoneBody} testID={testID + '-gone'} />;
  if (resource.status === 'error') return <ErrorState title={t.loadFailedTitle} message={describeFeatureError(resource.error, 'places', t)} onRetry={resource.reload} testID={testID + '-error'} />;
  if (isLockedItem(instructor)) {
    return (
      <View style={{ padding: theme.spacing.lg, gap: theme.spacing.md }} testID={testID + '-locked'}>
        <AppText variant="title" accessibilityRole="header">
          {instructor.displayName}
        </AppText>
        <AccessPromptCard required={lockedRequirement(instructor)} feature="directory" body={t.lockedInstructor} />
      </View>
    );
  }

  const viewer = instructor.viewer || {};
  const rank = instructorRankText(property, instructor);
  const preview = (instructor.venues && instructor.venues.objects) || [];
  const total = (instructor.venues && instructor.venues.totalRecords) || preview.length;
  const shown = venues || preview;
  const isMe = Boolean(user && instructor.linkedUser && instructor.linkedUser.id === user.id);

  return (
    <ScrollView contentContainerStyle={{ padding: theme.spacing.lg, gap: theme.spacing.lg }} testID={testID}>
      <View style={{ flexDirection: 'row', gap: theme.spacing.md, alignItems: 'center' }}>
        {imageUrl(instructor.photo, 'thumb') ? <MediaImage url={imageUrl(instructor.photo, 'thumb')} blurhash={instructor.photo.blurhash} alt={null} style={{ width: 72, height: 72, borderRadius: 36 }} /> : null}
        <View style={{ flex: 1, gap: 2 }}>
          <AppText variant="title" accessibilityRole="header" testID={testID + '-name'}>
            {instructor.displayName}
          </AppText>
          {rank ? <AppText testID={testID + '-rank'}>{rank}</AppText> : null}
          {instructor.affiliation ? <AppText color="textMuted">{instructor.affiliation}</AppText> : null}
        </View>
      </View>
      {instructor.bio ? <MarkdownBody body={instructor.bio} headings={false} /> : null}
      {instructor.linkedUser ? <Button label={t.openProfile} accessibilityLabel={t.openProfile + ', ' + instructor.linkedUser.displayName} variant="secondary" onPress={() => router.push('/members/' + instructor.linkedUser.id)} style={{ alignSelf: 'auto' }} testID={testID + '-profile'} /> : null}
      {instructor.websiteUrl ? <Button label={t.website} variant="ghost" onPress={() => openInAppBrowser(instructor.websiteUrl)} style={{ alignSelf: 'auto' }} /> : null}
      <View style={{ gap: theme.spacing.sm }}>
        <AppText variant="label">{t.venues}</AppText>
        {shown.length ? shown.map((venue) => <VenueCard key={venue.id} venue={venue} terms={t} onPress={() => router.push('/venues/' + venue.id)} />) : <AppText color="textMuted">{t.noVenues}</AppText>}
        {(venues ? Boolean(token) : total > preview.length) ? <Button label={t.loadMore} variant="ghost" onPress={() => moreVenues().catch(() => {})} style={{ alignSelf: 'auto' }} testID={testID + '-more-venues'} /> : null}
      </View>
      {notice ? (
        <AppText color="textMuted" accessibilityRole="alert" testID={testID + '-notice'}>
          {notice}
        </AppText>
      ) : null}
      <Card>
        <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: theme.spacing.sm }}>
          {viewer.canClaim ? <Button label={t.claim} accessibilityHint={t.claimHint} onPress={() => router.push({ pathname: '/directory/submit', params: { type: 'ClaimInstructor', targetId: instructor.id } })} style={{ alignSelf: 'auto' }} testID={testID + '-claim'} /> : null}
          {viewer.canSuggestEdit ? <Button label={t.suggestEdit} variant="secondary" onPress={() => router.push({ pathname: '/directory/submit', params: { type: 'EditInstructor', targetId: instructor.id } })} style={{ alignSelf: 'auto' }} testID={testID + '-suggest'} /> : null}
          {isMe ? <Button label={t.unlinkMe} variant="ghost" onPress={unlink} style={{ alignSelf: 'auto' }} testID={testID + '-unlink'} /> : null}
          {viewer.canReport ? <Button label={t.report} variant="ghost" onPress={() => setReporting(true)} style={{ alignSelf: 'auto' }} testID={testID + '-report'} /> : null}
        </View>
      </Card>
      {reporting ? <ReportSheet reasons={DirectoryReportReasons} target={{ targetType: 'Instructor', targetId: instructor.id }} author={null} onClose={() => setReporting(false)} /> : null}
    </ScrollView>
  );
}

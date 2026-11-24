import { formatRating, formatRelativeTime, ratingDistributionRows, ReviewOrderings, validateReview, venueContributionConfig } from '@trelliskit/shared';
import { useCallback, useEffect, useRef, useState } from 'react';
import { View } from 'react-native';
import { useAuth } from '../../auth/AuthProvider';
import { describeFeatureError, useDirectoryStrings } from '../../content/phase4';
import { useLayout } from '../../hooks/useLayout';
import { useProperty } from '../../property';
import { useTheme } from '../../theme';
import AppText from '../AppText';
import Button from '../Button';
import Card from '../Card';
import ChipGroup from '../ChipGroup';
import Notice from '../Notice';
import TextField from '../TextField';
import ConfirmSheet from '../feed/ConfirmSheet';
import ReportSheet, { Phase4ReportReasons } from '../feed/ReportSheet';
import { StarDisplay, StarInput } from './StarRating';

/** Reviews per page of the list. */
export const ReviewPageSize = 10;

/** Review length limits for the property (`directory.reviews` in GET /property, engine defaults otherwise). */
export function reviewLimits(property) {
  return venueContributionConfig(property).reviews;
}

/** Member-facing text of `validateReview` errors. */
export function reviewErrorText(errors, limits, t) {
  const out = {};
  if (errors.rating) out.rating = t.reviews.errorsByCode[errors.rating] || t.reviews.errorsByCode.required;
  if (errors.body === 'required') out.body = t.reviews.bodyRequired(limits.minLength);
  else if (errors.body === 'tooShort') out.body = t.reviews.bodyTooShort(limits.minLength);
  else if (errors.body === 'tooLong') out.body = t.reviews.bodyTooLong(limits.maxLength);
  return out;
}

function authorName(author, t) {
  if (!author || author.isDeleted) return t.reviews.deletedAuthor;
  return author.displayName || author.handle || t.reviews.deletedAuthor;
}

/** Average, count, and the 5 to 1 star breakdown (bars). Side by side on wide windows. */
export function ReviewSummary({ reviews, testID = 'reviews-summary' }) {
  const theme = useTheme();
  const t = useDirectoryStrings();
  const { isWide } = useLayout();
  const count = reviews.count || 0;
  const average = formatRating(reviews.averageRating, count);
  if (!count) return <AppText color="textMuted" testID={testID + '-empty'}>{t.reviews.noneYet}</AppText>;
  const rows = ratingDistributionRows(reviews.distribution);
  return (
    <View testID={testID} style={{ flexDirection: isWide ? 'row' : 'column', gap: theme.spacing.lg, alignItems: isWide ? 'center' : 'stretch' }}>
      <View style={{ gap: theme.spacing.xs, minWidth: 120 }}>
        <AppText variant="title" importantForAccessibility="no" accessibilityElementsHidden testID={testID + '-average'}>
          {average}
        </AppText>
        <StarDisplay value={reviews.averageRating} count={count} size={18} showText={false} testID={testID + '-stars'} />
        <AppText color="textMuted" importantForAccessibility="no" accessibilityElementsHidden>
          {t.rating.count(count)}
        </AppText>
      </View>
      <View style={{ flex: isWide ? 1 : undefined, gap: theme.spacing.xs }} accessibilityLabel={t.reviews.distribution} testID={testID + '-distribution'}>
        {rows.map((row) => (
          <View key={row.stars} accessible accessibilityLabel={t.reviews.distributionRow(row.stars, row.count, row.percent)} style={{ flexDirection: 'row', alignItems: 'center', gap: theme.spacing.sm }} testID={testID + '-row-' + row.stars}>
            <AppText variant="caption" style={{ width: 16, textAlign: 'right' }}>
              {String(row.stars)}
            </AppText>
            <View style={{ flex: 1, height: 8, borderRadius: 4, backgroundColor: theme.colors.surface, overflow: 'hidden', borderWidth: 1, borderColor: theme.colors.border }}>
              <View style={{ width: row.percent + '%', height: '100%', backgroundColor: theme.colors.warning }} />
            </View>
            <AppText variant="caption" color="textMuted" style={{ minWidth: 32, textAlign: 'right' }}>
              {String(row.count)}
            </AppText>
          </View>
        ))}
      </View>
    </View>
  );
}

/**
 * The member's own review form: an accessible star input and the text, validated with
 * `validateReview` against the property's limits. Creates or (with `editing`) updates the review.
 */
export function ReviewForm({ venueId, editing = null, limits, onSaved, onCancel, testID = 'review-form' }) {
  const theme = useTheme();
  const t = useDirectoryStrings();
  const { api } = useAuth();
  const [rating, setRating] = useState(editing ? editing.rating : 0);
  const [body, setBody] = useState(editing ? editing.body || '' : '');
  const [errors, setErrors] = useState({});
  const [error, setError] = useState(null);
  const [busy, setBusy] = useState(false);

  async function submit() {
    setError(null);
    const found = reviewErrorText(validateReview({ rating, body }, limits), limits, t);
    setErrors(found);
    if (Object.keys(found).length) return;
    setBusy(true);
    try {
      const draft = { rating, body: body.trim() };
      const saved = editing ? await api.updateVenueReview(venueId, editing.id, draft) : await api.createVenueReview(venueId, draft);
      onSaved(saved, editing ? 'edit' : 'create');
    } catch (e) {
      setError(describeFeatureError(e, 'places', t));
    } finally {
      setBusy(false);
    }
  }

  return (
    <View style={{ gap: theme.spacing.md }} testID={testID}>
      <AppText variant="heading" accessibilityRole="header">
        {editing ? t.reviews.editYours : t.reviews.write}
      </AppText>
      <StarInput value={rating} onChange={setRating} label={t.reviews.rating} error={errors.rating} disabled={busy} testID={testID + '-rating'} />
      <TextField
        label={t.reviews.body}
        hint={t.reviews.bodyHint(limits.minLength, limits.maxLength)}
        value={body}
        onChangeText={setBody}
        multiline
        maxLength={limits.maxLength}
        error={errors.body}
        status={t.reviews.counter(body.trim().length, limits.maxLength)}
        testID={testID + '-body'}
      />
      <Notice tone="error" testID={testID + '-error'}>
        {error}
      </Notice>
      <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: theme.spacing.sm }}>
        <Button label={busy ? t.reviews.saving : editing ? t.reviews.save : t.reviews.submit} onPress={submit} disabled={busy} style={{ alignSelf: 'auto' }} testID={testID + '-submit'} />
        {onCancel ? <Button label={t.reviews.cancel} variant="ghost" onPress={onCancel} disabled={busy} style={{ alignSelf: 'auto' }} testID={testID + '-cancel'} /> : null}
      </View>
    </View>
  );
}

/** A reply form for a linked instructor (creates or edits the one reply). */
function ReplyForm({ venueId, review, maxLength, onSaved, onCancel, testID }) {
  const theme = useTheme();
  const t = useDirectoryStrings();
  const { api } = useAuth();
  const [body, setBody] = useState(review.reply ? review.reply.body || '' : '');
  const [fieldError, setFieldError] = useState(null);
  const [error, setError] = useState(null);
  const [busy, setBusy] = useState(false);

  async function submit() {
    setError(null);
    const text = body.trim();
    if (!text) return setFieldError(t.reviews.replyRequired);
    if (text.length > maxLength) return setFieldError(t.reviews.replyTooLong(maxLength));
    setFieldError(null);
    setBusy(true);
    try {
      onSaved(await api.replyToVenueReview(venueId, review.id, text));
    } catch (e) {
      setError(describeFeatureError(e, 'places', t));
    } finally {
      setBusy(false);
    }
    return undefined;
  }

  return (
    <View style={{ gap: theme.spacing.sm }} testID={testID}>
      <TextField label={t.reviews.replyLabel} value={body} onChangeText={setBody} multiline maxLength={maxLength} error={fieldError} status={t.reviews.counter(body.trim().length, maxLength)} testID={testID + '-body'} />
      <Notice tone="error" testID={testID + '-error'}>
        {error}
      </Notice>
      <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: theme.spacing.sm }}>
        <Button label={busy ? t.reviews.saving : t.reviews.sendReply} onPress={submit} disabled={busy} style={{ alignSelf: 'auto' }} testID={testID + '-submit'} />
        <Button label={t.reviews.cancel} variant="ghost" onPress={onCancel} disabled={busy} style={{ alignSelf: 'auto' }} testID={testID + '-cancel'} />
      </View>
    </View>
  );
}

/**
 * One review: author, stars, date, body, the instructor reply, and the actions its `viewer`
 * flags allow (reply, delete reply, report). The member's own review is edited in its own card.
 */
export function ReviewItem({ venueId, review, limits, onChange, onReport, testID }) {
  const theme = useTheme();
  const t = useDirectoryStrings();
  const { api } = useAuth();
  const [replying, setReplying] = useState(false);
  const [confirmDelete, setConfirmDelete] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(null);
  const viewer = review.viewer || {};
  const id = testID || 'review-' + review.id;
  const name = authorName(review.author, t);
  const when = formatRelativeTime(review.createdUtc);
  const reply = review.reply;

  async function deleteReply() {
    setBusy(true);
    setError(null);
    try {
      const next = await api.deleteVenueReviewReply(venueId, review.id);
      onChange(next && next.id ? next : { ...review, reply: null });
      setConfirmDelete(false);
    } catch (e) {
      setError(describeFeatureError(e, 'places', t));
    } finally {
      setBusy(false);
    }
  }

  return (
    <View testID={id} style={{ gap: theme.spacing.sm, paddingVertical: theme.spacing.md, borderTopWidth: 1, borderTopColor: theme.colors.border }}>
      <View style={{ flexDirection: 'row', alignItems: 'center', flexWrap: 'wrap', gap: theme.spacing.sm }}>
        <StarDisplay value={review.rating} showText={false} testID={id + '-stars'} />
        <AppText variant="label">{viewer.isAuthor ? t.reviews.you : name}</AppText>
        <AppText variant="caption" color="textMuted">
          {when}
          {review.editedUtc ? ', ' + t.reviews.edited : ''}
        </AppText>
      </View>
      {review.moderationState && review.moderationState !== 'None' && viewer.isAuthor ? <AppText variant="caption" color="warning">{t.reviews.hidden}</AppText> : null}
      <AppText testID={id + '-body'}>{review.body}</AppText>
      {reply && !replying ? (
        <View testID={id + '-reply'} style={{ marginLeft: theme.spacing.lg, padding: theme.spacing.sm, gap: theme.spacing.xs, borderLeftWidth: 3, borderLeftColor: theme.colors.primary, backgroundColor: theme.colors.surface, borderRadius: theme.radii.sm }}>
          <AppText variant="label">{reply.instructorName ? t.reviews.replyFrom(reply.instructorName) : t.reviews.replyFromInstructor}</AppText>
          <AppText>{reply.body}</AppText>
          <AppText variant="caption" color="textMuted">
            {formatRelativeTime(reply.createdUtc)}
            {reply.editedUtc ? ', ' + t.reviews.edited : ''}
          </AppText>
        </View>
      ) : null}
      {replying ? (
        <ReplyForm
          venueId={venueId}
          review={review}
          maxLength={limits.replyMaxLength}
          onSaved={(next) => {
            setReplying(false);
            onChange(next);
          }}
          onCancel={() => setReplying(false)}
          testID={id + '-reply-form'}
        />
      ) : (
        <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: theme.spacing.sm }}>
          {viewer.canReply ? <Button label={reply ? t.reviews.editReply : t.reviews.reply} accessibilityHint={t.reviews.replyHint} variant="secondary" onPress={() => setReplying(true)} style={{ alignSelf: 'auto' }} testID={id + '-reply-open'} /> : null}
          {reply && viewer.canDeleteReply ? <Button label={t.reviews.deleteReply} variant="ghost" onPress={() => setConfirmDelete(true)} style={{ alignSelf: 'auto' }} testID={id + '-reply-delete'} /> : null}
          {viewer.canReport ? <Button label={t.reviews.report} accessibilityLabel={t.reviews.reportReview(name)} variant="ghost" onPress={() => onReport(review)} style={{ alignSelf: 'auto' }} testID={id + '-report'} /> : null}
        </View>
      )}
      <ConfirmSheet visible={confirmDelete} title={t.reviews.deleteReplyTitle} body={t.reviews.deleteReplyBody} confirmLabel={t.reviews.deleteReply} onConfirm={deleteReply} onClose={() => setConfirmDelete(false)} busy={busy} error={error} testID={id + '-reply-confirm'} />
    </View>
  );
}

/**
 * The Reviews section of a venue (docs/PHASE4_CONTRACT.md section 3.12): the summary, the
 * member's own review (write, edit, delete), and the list with ordering and load more. Hidden
 * when reviews are off. Signed-out callers see only the summary. Every action follows the
 * `viewer` flags of the venue (`canReview`) and of each review.
 * @param {object} props
 * @param {object} props.venue Venue with `reviews` and `viewer`.
 * @param {string|null} [props.focusReviewId] A review to show first (a notification's `?review=`).
 * @param {() => void} [props.onChanged] Called after a write so the venue (aggregates) reloads.
 */
export default function VenueReviews({ venue, focusReviewId = null, onChanged, testID = 'reviews' }) {
  const theme = useTheme();
  const t = useDirectoryStrings();
  const property = useProperty();
  const { api, user } = useAuth();
  const limits = reviewLimits(property);
  const reviews = venue.reviews;
  const viewer = venue.viewer || {};
  const [mineOverride, setMineOverride] = useState(undefined);
  const [editingMine, setEditingMine] = useState(false);
  const [confirmDelete, setConfirmDelete] = useState(false);
  const [deleteBusy, setDeleteBusy] = useState(false);
  const [deleteError, setDeleteError] = useState(null);
  const [ordering, setOrdering] = useState(ReviewOrderings[0]);
  const [items, setItems] = useState([]);
  const [token, setToken] = useState(null);
  const [listStatus, setListStatus] = useState('idle');
  const [focused, setFocused] = useState(null);
  const [reporting, setReporting] = useState(null);
  const generation = useRef(0);
  const enabled = Boolean(reviews && reviews.enabled !== false && limits.enabled);
  const signedIn = Boolean(user);
  const venueId = venue.id;

  const load = useCallback(
    async (continuationToken) => {
      const gen = continuationToken ? generation.current : (generation.current += 1);
      setListStatus(continuationToken ? 'more' : 'loading');
      try {
        const page = await api.enumerateVenueReviews(venueId, { maxResults: ReviewPageSize, ordering, continuationToken: continuationToken || undefined });
        if (gen !== generation.current) return;
        const objects = (page && page.objects) || [];
        setItems((current) => (continuationToken ? current.concat(objects) : objects));
        setToken(page && !page.endOfResults && page.continuationToken ? page.continuationToken : null);
        setListStatus('ready');
      } catch {
        if (gen !== generation.current) return;
        setListStatus(continuationToken ? 'moreFailed' : 'failed');
      }
    },
    [api, venueId, ordering]
  );

  useEffect(() => {
    // Fetch the first page when the section becomes visible (the list state is set from the response).
    // eslint-disable-next-line react-hooks/set-state-in-effect
    if (enabled && signedIn) load(null);
  }, [enabled, signedIn, load]);

  useEffect(() => {
    if (!enabled || !signedIn || !focusReviewId) return undefined;
    let active = true;
    api
      .getVenueReview(venueId, focusReviewId)
      .then((review) => active && setFocused(review || null))
      .catch(() => active && setFocused(null));
    return () => {
      active = false;
    };
  }, [api, venueId, focusReviewId, enabled, signedIn]);

  if (!enabled) return null;

  const mine = mineOverride !== undefined ? mineOverride : reviews.mine || null;
  const mineViewer = (mine && mine.viewer) || {};
  const replace = (next) => {
    setItems((current) => current.map((r) => (r.id === next.id ? next : r)));
    if (focused && focused.id === next.id) setFocused(next);
  };
  const afterWrite = () => {
    load(null);
    if (onChanged) onChanged();
  };

  async function deleteMine() {
    setDeleteBusy(true);
    setDeleteError(null);
    try {
      await api.deleteVenueReview(venueId, mine.id);
      setMineOverride(null);
      setConfirmDelete(false);
      setEditingMine(false);
      afterWrite();
    } catch (e) {
      setDeleteError(describeFeatureError(e, 'places', t));
    } finally {
      setDeleteBusy(false);
    }
  }

  const shown = focused ? items.filter((r) => r.id !== focused.id) : items;

  return (
    <View style={{ gap: theme.spacing.md }} testID={testID}>
      <AppText variant="heading" accessibilityRole="header">
        {t.reviews.title}
      </AppText>
      <ReviewSummary reviews={reviews} testID={testID + '-summary'} />
      {!signedIn ? <AppText color="textMuted" testID={testID + '-signed-out'}>{t.reviews.signInToRead}</AppText> : null}
      {signedIn && mine && !editingMine ? (
        <Card testID={testID + '-mine'}>
          <View style={{ gap: theme.spacing.sm }}>
            <AppText variant="label">{t.reviews.yours}</AppText>
            <StarDisplay value={mine.rating} showText={false} testID={testID + '-mine-stars'} />
            <AppText testID={testID + '-mine-body'}>{mine.body}</AppText>
            {mine.moderationState && mine.moderationState !== 'None' ? <AppText variant="caption" color="warning">{t.reviews.hidden}</AppText> : null}
            {mine.reply ? (
              <View style={{ padding: theme.spacing.sm, gap: theme.spacing.xs, borderLeftWidth: 3, borderLeftColor: theme.colors.primary }} testID={testID + '-mine-reply'}>
                <AppText variant="label">{mine.reply.instructorName ? t.reviews.replyFrom(mine.reply.instructorName) : t.reviews.replyFromInstructor}</AppText>
                <AppText>{mine.reply.body}</AppText>
              </View>
            ) : null}
            <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: theme.spacing.sm }}>
              {mineViewer.canEdit ? <Button label={t.reviews.edit} variant="secondary" onPress={() => setEditingMine(true)} style={{ alignSelf: 'auto' }} testID={testID + '-mine-edit'} /> : null}
              {mineViewer.canDelete ? <Button label={t.reviews.delete} variant="ghost" onPress={() => setConfirmDelete(true)} style={{ alignSelf: 'auto' }} testID={testID + '-mine-delete'} /> : null}
            </View>
          </View>
        </Card>
      ) : null}
      {signedIn && ((mine && editingMine) || (!mine && viewer.canReview)) ? (
        <Card>
          <ReviewForm
            key={mine ? 'edit-' + mine.id : 'new'}
            venueId={venueId}
            editing={mine && editingMine ? mine : null}
            limits={limits}
            onSaved={(saved) => {
              setMineOverride(saved);
              setEditingMine(false);
              afterWrite();
            }}
            onCancel={mine ? () => setEditingMine(false) : undefined}
            testID={testID + '-form'}
          />
        </Card>
      ) : null}
      {signedIn && reviews.count ? (
        <ChipGroup label={t.reviews.order} options={ReviewOrderings.map((o) => ({ value: o, label: t.reviews.orderings[o] }))} value={ordering} onToggle={(value) => value && setOrdering(value)} testID={testID + '-order'} />
      ) : null}
      {signedIn && focused ? (
        <View style={{ gap: theme.spacing.xs }} testID={testID + '-focused'}>
          <AppText variant="label">{t.reviews.linked}</AppText>
          <ReviewItem venueId={venueId} review={focused} limits={limits} onChange={replace} onReport={setReporting} testID={testID + '-focused-item'} />
        </View>
      ) : null}
      {signedIn ? (
        <View testID={testID + '-list'}>
          {shown.map((review) => (
            <ReviewItem key={review.id} venueId={venueId} review={review} limits={limits} onChange={replace} onReport={setReporting} />
          ))}
        </View>
      ) : null}
      {listStatus === 'failed' ? <Notice tone="error">{t.loadFailedTitle}</Notice> : null}
      {listStatus === 'moreFailed' ? <Notice tone="error">{t.loadMoreFailed}</Notice> : null}
      {signedIn && token ? <Button label={t.loadMore} variant="ghost" onPress={() => load(token)} disabled={listStatus === 'more'} style={{ alignSelf: 'auto' }} testID={testID + '-more'} /> : null}
      <ConfirmSheet visible={confirmDelete} title={t.reviews.deleteTitle} body={t.reviews.deleteBody} confirmLabel={t.reviews.deleteConfirm} onConfirm={deleteMine} onClose={() => setConfirmDelete(false)} busy={deleteBusy} error={deleteError} testID={testID + '-delete-confirm'} />
      {reporting ? <ReportSheet reasons={Phase4ReportReasons} target={{ targetType: 'VenueReview', targetId: reporting.id }} author={reporting.author || null} onClose={() => setReporting(null)} /> : null}
    </View>
  );
}

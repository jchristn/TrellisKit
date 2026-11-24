import { useCallback, useEffect, useId, useState } from 'react';
import { Link } from 'react-router-dom';
import { ReviewOrderings, formatRating, ratingDistributionRows, validateReview, venueContributionConfig } from '@trelliskit/shared';
import strings from '../../strings.js';
import { useAuth } from '../../context/AuthContext.jsx';
import { useProperty } from '../../context/PropertyContext.jsx';
import { useToast } from '../../context/ToastContext.jsx';
import useApiQuery from '../../hooks/useApiQuery.js';
import useCursorList from '../../hooks/useCursorList.js';
import Icon from '../Icon.jsx';
import ConfirmModal from '../ConfirmModal.jsx';
import { ErrorBanner } from '../States.jsx';
import ReportDialog from '../feed/ReportDialog.jsx';
import { StarInput, StarRating } from './Stars.jsx';
import { authorName, errorText, memberPath } from '../../utils/engagement.js';
import { formatDateTime } from '../../utils/format.js';
import { phase4Errors, useDirectoryTerms } from '../../utils/phase4.js';

const rv = strings.venues.reviews;
const rt = strings.venues.rating;
const PAGE_SIZE = 10;

/** Field messages of a review draft from `validateReview` codes. */
export function reviewFieldErrors(codes, limits) {
  const out = {};
  if (codes.rating) out.rating = rv.problems.ratingRequired;
  if (codes.body === 'required') out.body = rv.problems.bodyRequired;
  else if (codes.body === 'tooShort') out.body = rv.problems.bodyTooShort(limits.minLength);
  else if (codes.body === 'tooLong') out.body = rv.problems.bodyTooLong(limits.maxLength);
  return out;
}

/** Average, count, and the 5-to-1 distribution with bars and an accessible text for each row. */
function RatingSummary({ reviews }) {
  const average = formatRating(reviews.averageRating, reviews.count);
  const rows = ratingDistributionRows(reviews.distribution);
  return (
    <div className="review-summary" aria-label={rv.summaryLabel} role="group">
      <div className="review-summary-average">
        {average ? (
          <>
            <span className="review-summary-number" aria-hidden="true">{average}</span>
            <StarRating value={reviews.averageRating} label={rt.label(average, reviews.count)} size="lg" />
            <span className="muted small" aria-hidden="true">{rv.count(reviews.count)}</span>
          </>
        ) : <span className="muted">{rv.noRatings}</span>}
      </div>
      {average && (
        <ul className="review-distribution" aria-label={rv.distributionLabel}>
          {rows.map((row) => (
            <li key={row.stars} className="review-distribution-row">
              <span className="visually-hidden">{rv.distributionRow(row.stars, row.count, row.percent)}</span>
              <span className="review-distribution-label small" aria-hidden="true">{row.stars}</span>
              <span className="review-distribution-bar" aria-hidden="true"><span style={{ width: row.percent + '%' }} /></span>
              <span className="review-distribution-count small muted" aria-hidden="true">{row.count}</span>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

/**
 * The member's review form (create or edit): star radio group, text with a counter, validation
 * with the property limits before sending.
 */
export function ReviewForm({ venueId, review = null, limits, onSaved, onCancel }) {
  const { client } = useAuth();
  const { d } = useDirectoryTerms();
  const id = useId();
  const [rating, setRating] = useState(review ? review.rating : 0);
  const [body, setBody] = useState(review ? review.body : '');
  const [errors, setErrors] = useState({});
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const submit = async (e) => {
    e.preventDefault();
    const found = reviewFieldErrors(validateReview({ rating, body }, limits), limits);
    setErrors(found);
    setError('');
    if (Object.keys(found).length) return;
    setBusy(true);
    try {
      const payload = { rating, body: body.trim() };
      const saved = review ? await client.updateVenueReview(venueId, review.id, payload) : await client.createVenueReview(venueId, payload);
      setBusy(false);
      onSaved(saved, Boolean(review));
    } catch (err) {
      const fields = (err && err.body && err.body.fieldErrors) || null;
      if (fields) setErrors(reviewFieldErrors({ rating: fields.rating ? 'range' : undefined, body: fields.body ? 'tooShort' : undefined }, limits));
      setError(errorText(err, phase4Errors()));
      setBusy(false);
    }
  };
  return (
    <form className="panel stack-form review-form" onSubmit={submit} noValidate aria-labelledby={id + '-title'}>
      <h3 id={id + '-title'} className="section-title">{review ? rv.editTitle : rv.writeTitle}</h3>
      {!review && <p className="muted small">{d(rv.writeIntro)}</p>}
      <ErrorBanner message={error} />
      <StarInput value={rating} onChange={setRating} label={rv.ratingLegend} error={errors.rating} disabled={busy} placeholder={rv.ratingNone} />
      <div className="field">
        <label className="label-text" htmlFor={id + '-body'}>{rv.bodyLabel}</label>
        <textarea id={id + '-body'} rows={5} value={body} maxLength={limits.maxLength + 100} onChange={(e) => setBody(e.target.value)} disabled={busy}
          aria-invalid={Boolean(errors.body)} aria-describedby={id + '-hint'} />
        <span className="muted small" id={id + '-hint'}>{rv.bodyHint(limits.minLength, limits.maxLength)} {rv.counter(body.trim().length, limits.maxLength)}</span>
        {errors.body && <span className="field-error" role="alert">{errors.body}</span>}
      </div>
      <div className="form-actions">
        {onCancel && <button type="button" className="button button-secondary" onClick={onCancel} disabled={busy}>{strings.common.cancel}</button>}
        <button type="submit" className="button button-primary" disabled={busy}>{busy ? (review ? rv.saving : rv.submitting) : (review ? rv.save : rv.submit)}</button>
      </div>
    </form>
  );
}

function ReplyForm({ venueId, review, maxLength, onSaved, onCancel }) {
  const { client } = useAuth();
  const { d } = useDirectoryTerms();
  const id = useId();
  const [body, setBody] = useState(review.reply ? review.reply.body : '');
  const [problem, setProblem] = useState('');
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const submit = async (e) => {
    e.preventDefault();
    const text = body.trim();
    const found = !text ? rv.problems.replyRequired : text.length > maxLength ? rv.problems.replyTooLong(maxLength) : '';
    setProblem(found);
    setError('');
    if (found) return;
    setBusy(true);
    try {
      const saved = await client.replyToVenueReview(venueId, review.id, text);
      setBusy(false);
      onSaved(saved);
    } catch (err) {
      setError(errorText(err, phase4Errors()));
      setBusy(false);
    }
  };
  return (
    <form className="stack-form review-reply-form" onSubmit={submit} noValidate>
      <ErrorBanner message={error} />
      <div className="field">
        <label className="label-text" htmlFor={id + '-reply'}>{rv.replyLabel}</label>
        <textarea id={id + '-reply'} rows={3} value={body} maxLength={maxLength + 100} onChange={(e) => setBody(e.target.value)} disabled={busy} aria-invalid={Boolean(problem)} />
        <span className="muted small">{d(rv.replyHint)} {rv.counter(body.trim().length, maxLength)}</span>
        {problem && <span className="field-error" role="alert">{problem}</span>}
      </div>
      <div className="form-actions">
        <button type="button" className="button button-secondary button-sm" onClick={onCancel} disabled={busy}>{strings.common.cancel}</button>
        <button type="submit" className="button button-primary button-sm" disabled={busy}>{busy ? rv.replySaving : rv.replySubmit}</button>
      </div>
    </form>
  );
}

/**
 * One review: author, stars, date, body, the instructor reply, and the actions its `viewer`
 * allows (edit and delete for the author, reply and delete reply, report).
 */
function ReviewItem({ review, venueId, replyMaxLength, highlighted = false, onUpdated, onEdit, onDelete, onReport, tag }) {
  const { client } = useAuth();
  const toast = useToast();
  const [replying, setReplying] = useState(false);
  const [confirm, setConfirm] = useState(false);
  const viewer = review.viewer || {};
  const name = authorName(review.author);
  const deleteReply = async () => {
    try {
      const saved = await client.deleteVenueReviewReply(venueId, review.id);
      onUpdated(saved && saved.id ? saved : { ...review, reply: null });
      toast.success(rv.replyDeleted);
    } catch (err) {
      toast.error(errorText(err, phase4Errors()));
    } finally {
      setConfirm(false);
    }
  };
  return (
    <article id={'review-' + review.id} className={'review-item' + (highlighted ? ' is-highlighted' : '')} aria-label={rv.byAuthor(name)} tabIndex={highlighted ? -1 : undefined} data-testid={'review-' + review.id}>
      <header className="review-item-header">
        {tag && <span className="badge badge-neutral">{tag}</span>}
        <StarRating value={review.rating} label={rt.outOf(review.rating)} size="sm" />
        {review.author && review.author.id && !review.author.isDeleted ? <Link to={memberPath(review.author.id)} className="review-author">{name}</Link> : <span className="review-author">{name}</span>}
        <time className="muted small" dateTime={review.createdUtc}>{formatDateTime(review.createdUtc)}</time>
        {review.editedUtc && <span className="muted small">{rv.edited}</span>}
      </header>
      <p className="review-body">{review.body}</p>
      {review.reply && (
        <div className="review-reply" role="group" aria-label={rv.replyFrom(review.reply.instructorName || authorName(review.reply.author))}>
          <div className="review-reply-header small">
            <Icon name="reply" size={14} />
            <strong>{rv.replyFrom(review.reply.instructorName || authorName(review.reply.author))}</strong>
            <time className="muted" dateTime={review.reply.createdUtc}>{formatDateTime(review.reply.createdUtc)}</time>
            {review.reply.editedUtc && <span className="muted">{rv.edited}</span>}
          </div>
          <p className="review-body">{review.reply.body}</p>
        </div>
      )}
      {replying && <ReplyForm venueId={venueId} review={review} maxLength={replyMaxLength} onCancel={() => setReplying(false)} onSaved={(saved) => { setReplying(false); onUpdated(saved); toast.success(rv.replySaved); }} />}
      {!replying && (
        <div className="review-actions">
          {viewer.canEdit && onEdit && <button type="button" className="button button-secondary button-sm" onClick={onEdit} aria-label={rv.editReview}><Icon name="edit" size={14} /><span>{rv.edit}</span></button>}
          {viewer.canDelete && onDelete && <button type="button" className="button button-secondary button-sm" onClick={onDelete} aria-label={rv.deleteReview}><Icon name="trash" size={14} /><span>{rv.delete}</span></button>}
          {viewer.canReply && <button type="button" className="button button-secondary button-sm" onClick={() => setReplying(true)}><Icon name="reply" size={14} /><span>{review.reply ? rv.editReply : rv.reply}</span></button>}
          {viewer.canDeleteReply && review.reply && <button type="button" className="button button-secondary button-sm" onClick={() => setConfirm(true)}>{rv.deleteReply}</button>}
          {viewer.canReport && !viewer.isAuthor && <button type="button" className="button button-secondary button-sm" onClick={onReport} aria-label={rv.report}><Icon name="flag" size={14} /><span>{strings.venues.report}</span></button>}
        </div>
      )}
      <ConfirmModal open={confirm} title={rv.deleteReplyTitle} message={rv.deleteReplyBody} confirmLabel={rv.deleteReply} onCancel={() => setConfirm(false)} onConfirm={deleteReply} />
    </article>
  );
}

/**
 * The Reviews section of a venue (section 3.12): the summary, the member's own review (edit and
 * delete) or the form when `viewer.canReview`, and the paged list with an ordering select.
 * Hidden when the venue's `reviews.enabled` or the property's `directory.reviews.enabled` is off.
 * @param {{ venue: object, onVenueChanged?: () => void, highlightId?: string|null }} props
 */
export function VenueReviewsSection({ venue, onVenueChanged, highlightId = null }) {
  const { client } = useAuth();
  const { property } = useProperty();
  const toast = useToast();
  const config = venueContributionConfig(property).reviews;
  const reviews = venue.reviews || null;
  const enabled = Boolean(reviews) && reviews.enabled !== false && config.enabled;
  const venueId = venue.id;
  const viewer = venue.viewer || {};
  const limits = { minLength: config.minLength, maxLength: config.maxLength };
  const serverMine = reviews ? reviews.mine || null : null;
  const [mine, setMine] = useState(serverMine);
  const [editing, setEditing] = useState(false);
  const [confirmDelete, setConfirmDelete] = useState(false);
  const [reporting, setReporting] = useState(null);
  const [ordering, setOrdering] = useState(ReviewOrderings[0]);

  useEffect(() => {
    setMine(serverMine);
  }, [serverMine]);

  const fetchPage = useCallback((cursor, signal) => client.enumerateVenueReviews(venueId, { ordering, continuationToken: cursor, maxResults: PAGE_SIZE }, signal), [client, venueId, ordering]);
  const list = useCursorList(fetchPage, { enabled });
  const linkedFetcher = useCallback((signal) => client.getVenueReview(venueId, highlightId, signal), [client, venueId, highlightId]);
  const linked = useApiQuery(linkedFetcher, { enabled: enabled && Boolean(highlightId) });

  const mineId = mine ? mine.id : null;
  const listed = list.items.filter((x) => x.id !== mineId);
  const linkedReview = linked.data && linked.data.id && linked.data.id !== mineId && !listed.some((x) => x.id === linked.data.id) ? linked.data : null;
  const highlightReady = Boolean(highlightId) && (mineId === highlightId || listed.some((x) => x.id === highlightId) || Boolean(linkedReview));

  useEffect(() => {
    if (!highlightReady || typeof document === 'undefined') return;
    const node = document.getElementById('review-' + highlightId);
    if (!node) return;
    if (typeof node.scrollIntoView === 'function') node.scrollIntoView({ block: 'center' });
    if (typeof node.focus === 'function') node.focus({ preventScroll: true });
  }, [highlightReady, highlightId]);

  if (!enabled) return null;

  const changed = () => {
    if (onVenueChanged) onVenueChanged();
    list.refresh();
  };
  const saved = (review, wasEdit) => {
    setMine(review);
    setEditing(false);
    toast.success(wasEdit ? rv.updated : rv.posted);
    changed();
  };
  const remove = async () => {
    try {
      await client.deleteVenueReview(venueId, mine.id);
      setMine(null);
      toast.success(rv.deleted);
      changed();
    } catch (err) {
      toast.error(errorText(err, phase4Errors()));
    } finally {
      setConfirmDelete(false);
    }
  };
  const updateListed = (updated) => list.update((items) => items.map((x) => (x.id === updated.id ? updated : x)));
  const commonProps = { venueId, replyMaxLength: config.replyMaxLength };

  return (
    <section className="p4-section venue-reviews" aria-labelledby="venue-reviews">
      <h2 id="venue-reviews">{rv.title}</h2>
      <RatingSummary reviews={reviews} />
      {mine && !editing && (
        <div className="review-mine">
          <h3 className="section-title">{rv.yourReview}</h3>
          <ReviewItem review={mine} {...commonProps} highlighted={highlightId === mine.id} onUpdated={setMine} onEdit={() => setEditing(true)} onDelete={() => setConfirmDelete(true)} onReport={() => setReporting(mine)} />
        </div>
      )}
      {mine && editing && <ReviewForm venueId={venueId} review={mine} limits={limits} onSaved={saved} onCancel={() => setEditing(false)} />}
      {!mine && viewer.canReview && <ReviewForm venueId={venueId} limits={limits} onSaved={saved} />}
      {linkedReview && <ReviewItem review={linkedReview} {...commonProps} highlighted tag={rv.linked} onUpdated={() => linked.reload()} onReport={() => setReporting(linkedReview)} />}
      <div className="review-list-toolbar">
        <label className="label-text" htmlFor="venue-review-order">{rv.sortLabel}</label>
        <select id="venue-review-order" value={ordering} onChange={(e) => setOrdering(e.target.value)}>
          {ReviewOrderings.map((o) => <option key={o} value={o}>{rv.orderings[o]}</option>)}
        </select>
      </div>
      <ErrorBanner message={list.error ? errorText(list.error, phase4Errors(), rv.loadError) : ''} onRetry={list.refresh} />
      {list.loaded && !listed.length && !linkedReview && <p className="muted small">{mine ? rv.emptyOthers : rv.empty}</p>}
      <div className="review-list">
        {listed.map((review) => (
          <ReviewItem key={review.id} review={review} {...commonProps} highlighted={highlightId === review.id} onUpdated={updateListed} onReport={() => setReporting(review)} />
        ))}
      </div>
      {list.loaded && !list.done && <button type="button" className="button button-secondary button-sm" onClick={list.loadMore} disabled={list.loading}>{rv.loadMore}</button>}
      <ConfirmModal open={confirmDelete} title={rv.deleteTitle} message={rv.deleteBody} confirmLabel={rv.delete} onCancel={() => setConfirmDelete(false)} onConfirm={remove} />
      {reporting && <ReportDialog target={{ type: 'VenueReview', id: reporting.id }} author={reporting.author} onClose={() => setReporting(null)} />}
    </section>
  );
}

export default VenueReviewsSection;

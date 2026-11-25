import ThemeToggle from '../../components/ThemeToggle.jsx';
import { useCallback, useState } from 'react';
import { Link, useParams, useSearchParams } from 'react-router-dom';
import { directoryConfig, formatAddress, isLockedItem, lockedRequirement, programLabels } from '@trelliskit/shared';
import strings from '../../strings.js';
import { useAuth } from '../../context/AuthContext.jsx';
import { useProperty } from '../../context/PropertyContext.jsx';
import { useToast } from '../../context/ToastContext.jsx';
import useApiQuery from '../../hooks/useApiQuery.js';
import useCursorList from '../../hooks/useCursorList.js';
import useDocumentMeta from '../../hooks/useDocumentMeta.js';
import Icon from '../../components/Icon.jsx';
import SaveButton from '../../components/SaveButton.jsx';
import Modal from '../../components/Modal.jsx';
import ConfirmModal from '../../components/ConfirmModal.jsx';
import { Badge } from '../../components/Badges.jsx';
import { EmptyState, ErrorBanner, Loading } from '../../components/States.jsx';
import MarkdownBody from '../../components/feed/MarkdownBody.jsx';
import { VenueAbout, VenueSchedule } from '../../components/directory/VenueSchedule.jsx';
import ReportDialog from '../../components/feed/ReportDialog.jsx';
import { AccessPromptCard, useAccessPrompt } from '../../components/AccessPrompt.jsx';
import { EventRow } from '../../components/events/EventParts.jsx';
import { VenueMediaSection } from '../../components/directory/VenueMedia.jsx';
import { VenueReviewsSection } from '../../components/directory/VenueReviews.jsx';
import { VenueRow } from './DirectoryView.jsx';
import { PublicPageLink } from '../../components/PublicPageFields.jsx';
import { instructorRankOptions } from '../../components/directory/DirectoryForms.jsx';
import { errorText, memberPath } from '../../utils/engagement.js';
import { appPaths, phase4Errors, useDirectoryTerms } from '../../utils/phase4.js';
import { venueSessionCheckIn } from '../ranks/CheckIn.jsx';

const v = strings.venues;

/** The label of an instructor's rank from the configured rank field (the key when unknown). */
function rankText(property, instructor) {
  const found = instructorRankOptions(property).find((o) => o.value === instructor.rank);
  return found ? found.label : instructor.rank;
}

function NotFound({ back }) {
  const { d } = useDirectoryTerms();
  return <div className="member-view">{back}<EmptyState icon="pin" title={v.notFound}>{d(v.notFoundBody)}</EmptyState></div>;
}

function useEntity(client, method, id) {
  const fetcher = useCallback((signal) => client[method](id, signal), [client, method, id]);
  return useApiQuery(fetcher);
}

function Links({ websiteUrl, socialLinks }) {
  const links = [...(websiteUrl ? [{ kind: 'Website', url: websiteUrl }] : []), ...(socialLinks || [])].filter((l) => /^https:\/\//i.test(l.url || ''));
  if (!links.length) return null;
  return <ul className="bullet-list">{links.map((l) => <li key={l.url}><a href={l.url} target="_blank" rel="noopener noreferrer">{l.kind === 'Website' ? v.website : l.kind}</a></li>)}</ul>;
}

/** A short note sent as a submission (claims and closure reports). */
function NoteDialog({ title, body, label, minLength = 0, required = false, onSubmit, onClose }) {
  const [note, setNote] = useState('');
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const submit = async (e) => {
    e.preventDefault();
    if (required && note.trim().length < minLength) {
      setError(v.problems.tooShort);
      return;
    }
    setBusy(true);
    setError('');
    try {
      await onSubmit(note.trim());
    } catch (err) {
      setError(errorText(err, phase4Errors()));
      setBusy(false);
    }
  };
  return (
    <Modal open size="sm" onClose={busy ? () => {} : onClose} title={title}
      footer={<><button type="button" className="button button-secondary" onClick={onClose} disabled={busy}>{strings.common.cancel}</button><button type="submit" form="p4-note-form" className="button button-primary" disabled={busy}>{v.submit}</button></>}>
      <form id="p4-note-form" className="stack-form" onSubmit={submit} noValidate>
        <p>{body}</p>
        <ErrorBanner message={error} />
        <div className="field">
          <label className="label-text" htmlFor="p4-note">{label}</label>
          <textarea id="p4-note" rows={4} maxLength={1000} value={note} onChange={(e) => setNote(e.target.value)} disabled={busy} />
        </div>
      </form>
    </Modal>
  );
}

/**
 * /app/venues/:id (section 2.2): name, verification, address by visibility, schedule, programs,
 * visitor policy, contact and links, photos, the instructors (bounded preview plus "load more"),
 * upcoming events at the venue, photos and videos (staff photos first, then member media), reviews
 * (a `?review=` link from a notification scrolls to that review), and "Suggest an edit",
 * "Report as closed", and Report.
 */
export function VenueView() {
  const { id } = useParams();
  const [searchParams] = useSearchParams();
  const highlightReview = searchParams.get('review') || null;
  const { client, me } = useAuth();
  const { property } = useProperty();
  const { d } = useDirectoryTerms();
  const toast = useToast();
  const accessPrompt = useAccessPrompt();
  const query = useEntity(client, 'getVenue', id);
  const [dialog, setDialog] = useState(null);
  const venue = query.data;
  useDocumentMeta(strings.member.metaTitle(venue ? venue.name : d(v.title)));
  const venueId = venue ? venue.id : null;
  const venueLocked = Boolean(venue) && isLockedItem(venue);
  const fetchInstructors = useCallback((cursor, signal) => client.enumerateVenueInstructors(venueId, { continuationToken: cursor, maxResults: 25 }, signal), [client, venueId]);
  const instructors = useCursorList(fetchInstructors, { enabled: Boolean(venueId) && !venueLocked });
  const eventsFetcher = useCallback((signal) => client.enumerateEvents({ maxResults: 5, filters: { venueId, when: 'Upcoming' } }, signal), [client, venueId]);
  const events = useApiQuery(eventsFetcher, { enabled: Boolean(venueId) && property.features && property.features.events !== false });
  const back = <Link to={appPaths.directory} className="back-link small"><Icon name="chevronLeft" size={14} /><span>{d(v.back)}</span></Link>;

  if (query.error && query.error.status === 404) return <NotFound back={back} />;
  if (!venue) return <div className="member-view">{back}{query.loading && <Loading />}<ErrorBanner message={query.error ? errorText(query.error, phase4Errors(), d(v.loadError)) : ''} onRetry={query.reload} /></div>;
  const locked = isLockedItem(venue);
  const viewer = venue.viewer || {};
  const config = directoryConfig(property);
  const reportClosed = async (note) => {
    try {
      await client.createDirectorySubmission({ type: 'ReportClosed', targetId: venue.id, note: note || undefined });
      setDialog(null);
      toast.success(v.reportClosedSent);
    } catch (err) {
      if (accessPrompt.handle(err)) setDialog(null);
      else throw err;
    }
  };
  const precision = venue.geo && venue.geo.precision;
  const upcoming = (events.data && events.data.objects) || [];

  return (
    <div className="member-view venue-view">
      <div className="row-actions"><ThemeToggle /></div>
      {back}
      <header className="page-header">
        <div className="page-header-text">
          <h1>{venue.name}</h1>
          {venue.affiliation && <p className="muted">{venue.affiliation}</p>}
          <span className="badge-row">
            {venue.isVerified && <Badge tone="success">{v.verified}</Badge>}
            {venue.status === 'Closed' && <Badge tone="danger">{v.closed}</Badge>}
            {venue.visitorPolicy && <Badge tone="neutral">{v.visitorPolicies[venue.visitorPolicy]}</Badge>}
          </span>
          <PublicPageLink url={venue.publicUrl} />
        </div>
        {me && !locked && (
          <div className="page-header-actions">
            <SaveButton targetType="Venue" targetId={venue.id} title={venue.name} />
            {config.submissions && viewer.canSuggestEdit !== false && <Link to={appPaths.submit('EditVenue', venue.id)} className="button button-secondary button-sm"><Icon name="edit" size={14} /><span>{v.suggestEdit}</span></Link>}
            {config.submissions && viewer.canReportClosed !== false && venue.status !== 'Closed' && <button type="button" className="button button-secondary button-sm" onClick={() => setDialog('closed')}>{v.reportClosed}</button>}
            {viewer.canReport !== false && <button type="button" className="button button-secondary button-sm" onClick={() => setDialog('report')}><Icon name="flag" size={14} /><span>{v.report}</span></button>}
          </div>
        )}
      </header>
      {venue.summary && <p>{venue.summary}</p>}
      {locked ? <AccessPromptCard required={lockedRequirement(venue)} feature="directory" title={v.locked} body={v.lockedBody} /> : (
        <div className="p4-detail">
          <div className="p4-section">
            <VenueMediaSection venue={venue} />
            {venue.description && <VenueAbout text={venue.description} />}
            {(venue.schedule || venue.scheduleText) && <VenueSchedule schedule={venue.schedule} text={venue.scheduleText} sessionAction={venueSessionCheckIn(venue)} />}
            <section className="p4-section" aria-labelledby="venue-instructors">
              <h2 id="venue-instructors">{d(v.instructorsTitle)}</h2>
              {instructors.loaded && !instructors.items.length && <p className="muted small">{d(v.instructorsEmpty)}</p>}
              <div className="p4-list">
                {instructors.items.map((i) => (
                  <Link key={i.id} to={appPaths.instructor(i.slug || i.id)} className="p4-row">
                    <Icon name="user" size={18} />
                    <span className="p4-row-main"><span className="p4-row-title">{i.displayName}</span>{i.rank && <span className="muted small">{rankText(property, i)}</span>}</span>
                  </Link>
                ))}
              </div>
              {instructors.loaded && !instructors.done && <button type="button" className="button button-secondary button-sm" onClick={instructors.loadMore}>{v.loadMore}</button>}
            </section>
            {upcoming.length > 0 && (
              <section className="p4-section" aria-labelledby="venue-events">
                <h2 id="venue-events">{v.upcomingEvents}</h2>
                <div className="p4-list">{upcoming.map((e) => <EventRow key={e.id} event={e} />)}</div>
              </section>
            )}
            <VenueReviewsSection venue={venue} onVenueChanged={query.reload} highlightId={highlightReview} />
          </div>
          <aside className="p4-detail-side">
            <section className="panel">
              <dl className="p4-kv">
                <dt>{v.address}</dt>
                <dd>
                  {formatAddress(venue.address, venue.addressVisibility)}
                  {precision === 'Approximate' && <div className="muted small">{v.approximate}</div>}
                  {venue.addressVisibility === 'LocalityOnly' && <div className="muted small">{v.localityOnly}</div>}
                </dd>
                {(venue.programs || []).length > 0 && <><dt>{v.programsLabel}</dt><dd>{programLabels(property, venue.programs).join(', ')}</dd></>}
                {venue.visitorPolicyNote && <><dt>{v.visitorPolicyLabel}</dt><dd>{venue.visitorPolicyNote}</dd></>}
                {venue.phone && <><dt>{v.phone}</dt><dd><a href={'tel:' + venue.phone.replace(/[^+0-9]/g, '')}>{venue.phone}</a></dd></>}
                {venue.email && <><dt>{v.email}</dt><dd><a href={'mailto:' + venue.email}>{venue.email}</a></dd></>}
              </dl>
              <Links websiteUrl={venue.websiteUrl} socialLinks={venue.socialLinks} />
            </section>
          </aside>
        </div>
      )}
      {dialog === 'report' && <ReportDialog target={{ type: 'Venue', id: venue.id }} onClose={() => setDialog(null)} />}
      {dialog === 'closed' && <NoteDialog title={d(v.reportClosedTitle)} body={v.reportClosedBody} label={v.noteLabel} onSubmit={reportClosed} onClose={() => setDialog(null)} />}
    </div>
  );
}

/**
 * /app/instructors/:id (section 2.3): name, rank (from the configured rank field), bio, linked
 * member profile, venues (paged), and "This is me" (claim), "Unlink me" for the linked member,
 * "Suggest an edit", and Report.
 */
export function InstructorView() {
  const { id } = useParams();
  const { client, me } = useAuth();
  const { property } = useProperty();
  const { d } = useDirectoryTerms();
  const toast = useToast();
  const accessPrompt = useAccessPrompt();
  const query = useEntity(client, 'getInstructor', id);
  const [dialog, setDialog] = useState(null);
  const [override, setOverride] = useState(null);
  const instructor = query.data ? { ...query.data, ...(override || {}) } : null;
  useDocumentMeta(strings.member.metaTitle(instructor ? instructor.displayName : d(v.title)));
  const instructorId = instructor ? instructor.id : null;
  const fetchVenues = useCallback((cursor, signal) => client.enumerateInstructorVenues(instructorId, { continuationToken: cursor, maxResults: 25 }, signal), [client, instructorId]);
  const venues = useCursorList(fetchVenues, { enabled: Boolean(instructor) && !isLockedItem(instructor) });
  const back = <Link to={appPaths.directory} className="back-link small"><Icon name="chevronLeft" size={14} /><span>{d(v.back)}</span></Link>;

  if (query.error && query.error.status === 404) return <NotFound back={back} />;
  if (!instructor) return <div className="member-view">{back}{query.loading && <Loading />}<ErrorBanner message={query.error ? errorText(query.error, phase4Errors(), d(v.loadError)) : ''} onRetry={query.reload} /></div>;
  const locked = isLockedItem(instructor);
  const viewer = instructor.viewer || {};
  const config = directoryConfig(property);
  const isMe = Boolean(me && instructor.linkedUser && instructor.linkedUser.id === me.id);
  const claim = async (note) => {
    try {
      await client.createDirectorySubmission({ type: 'ClaimInstructor', targetId: instructor.id, note });
      setDialog(null);
      toast.success(v.claimSent);
    } catch (err) {
      if (accessPrompt.handle(err)) setDialog(null);
      else throw err;
    }
  };
  const unlink = async () => {
    try {
      await client.unlinkMyInstructor(instructor.id);
      setOverride({ linkedUser: null });
      toast.success(v.unlinked);
    } catch (err) {
      toast.error(errorText(err, phase4Errors()));
    } finally {
      setDialog(null);
    }
  };

  return (
    <div className="member-view instructor-view">
      {back}
      <header className="page-header">
        <div className="page-header-text">
          <h1>{instructor.displayName}</h1>
          {instructor.rank && <p className="muted">{rankText(property, instructor)}</p>}
          {instructor.affiliation && <p className="muted small">{v.affiliation}: {instructor.affiliation}</p>}
          {instructor.linkedUser && <Link to={memberPath(instructor.linkedUser.id)} className="small">{v.linkedProfile}</Link>}
        </div>
        {me && !locked && (
          <div className="page-header-actions">
            <SaveButton targetType="Instructor" targetId={instructor.id} title={instructor.displayName} />
            {isMe && <button type="button" className="button button-secondary button-sm" onClick={() => setDialog('unlink')}>{v.unlink}</button>}
            {!instructor.linkedUser && config.claims && viewer.canClaim !== false && <button type="button" className="button button-primary button-sm" onClick={() => setDialog('claim')}>{v.claim}</button>}
            {config.submissions && viewer.canSuggestEdit !== false && <Link to={appPaths.submit('EditInstructor', instructor.id)} className="button button-secondary button-sm"><Icon name="edit" size={14} /><span>{v.suggestEdit}</span></Link>}
            {viewer.canReport !== false && <button type="button" className="button button-secondary button-sm" onClick={() => setDialog('report')}><Icon name="flag" size={14} /><span>{v.report}</span></button>}
          </div>
        )}
      </header>
      {locked ? <AccessPromptCard required={lockedRequirement(instructor)} feature="directory" title={v.locked} body={v.lockedBody} /> : (
        <>
          {instructor.bio && <section className="panel"><MarkdownBody body={instructor.bio} allowHeadings={false} linkMentions={false} /></section>}
          <Links websiteUrl={instructor.websiteUrl} socialLinks={instructor.socialLinks} />
          <section className="p4-section" aria-labelledby="instructor-venues">
            <h2 id="instructor-venues">{v.venuesTitle}</h2>
            {venues.loaded && !venues.items.length && <p className="muted small">{d(v.venuesEmpty)}</p>}
            <div className="p4-list">{venues.items.map((venue) => <VenueRow key={venue.id} venue={venue} />)}</div>
            {venues.loaded && !venues.done && <button type="button" className="button button-secondary button-sm" onClick={venues.loadMore}>{v.loadMore}</button>}
          </section>
        </>
      )}
      {dialog === 'report' && <ReportDialog target={{ type: 'Instructor', id: instructor.id }} onClose={() => setDialog(null)} />}
      {dialog === 'claim' && <NoteDialog title={d(v.claimTitle)} body={v.claimBody} label={v.claimNoteLabel} required minLength={10} onSubmit={claim} onClose={() => setDialog(null)} />}
      <ConfirmModal open={dialog === 'unlink'} title={v.unlink} message={v.unlinkConfirm} confirmLabel={v.unlink} onCancel={() => setDialog(null)} onConfirm={unlink} />
    </div>
  );
}

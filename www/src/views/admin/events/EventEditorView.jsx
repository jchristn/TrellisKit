import { useCallback, useMemo, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { AttendeeVisibilities, PublicPageKind, buildRecurrenceRule, defaultRecurrenceForm, eventsConfig, formatEventShort, formatReminderOffset, metadataForSave, metadataOf, recurrenceFormFromRule, validateEventForm, validateRecurrenceForm } from '@trelliskit/shared';
import strings from '../../../strings.js';
import { useAdminAuth } from '../../../context/AdminAuthContext.jsx';
import { useProperty } from '../../../context/PropertyContext.jsx';
import { useToast } from '../../../context/ToastContext.jsx';
import useApiQuery from '../../../hooks/useApiQuery.js';
import useCursorList from '../../../hooks/useCursorList.js';
import PageHeader from '../../../components/PageHeader.jsx';
import Icon from '../../../components/Icon.jsx';
import Modal from '../../../components/Modal.jsx';
import ConfirmModal from '../../../components/ConfirmModal.jsx';
import ActionMenu from '../../../components/ActionMenu.jsx';
import MetadataEditor from '../../../components/MetadataEditor.jsx';
import AudienceField, { useAudiencePicker } from '../../../components/AudienceField.jsx';
import { Badge } from '../../../components/Badges.jsx';
import { Banner, ErrorBanner, Loading } from '../../../components/States.jsx';
import { Field, Toggle } from '../../../components/admin/AdminParts.jsx';
import FileUploadButton from '../../../components/admin/FileUploadButton.jsx';
import EntityPicker, { instructorItem, memberItem, pickerSearches, venueItem } from '../../../components/admin/EntityPicker.jsx';
import EventFormFields, { eventBodyFrom, eventFormFrom, validationShape } from '../../../components/events/EventForm.jsx';
import RecurrenceEditor from '../../../components/events/RecurrenceEditor.jsx';
import { PublicPageLink, SlugField, slugBlocks, slugServerError, useSlugStatus } from '../../../components/PublicPageFields.jsx';
import { authorName, errorText } from '../../../utils/engagement.js';
import { formatDateTime } from '../../../utils/format.js';
import { appPaths, downloadText, phase4Errors } from '../../../utils/phase4.js';
import TableColumns from '../../../components/TableColumns.jsx';

const s = strings.admin.eventsAdmin;
const ev = strings.events;

function advancedFrom(event, property) {
  const defaults = eventsConfig(property).defaultReminderOffsetsMinutes;
  return {
    maxGuestsPerRsvp: event ? String(event.maxGuestsPerRsvp || 0) : '0', waitlistEnabled: event ? event.waitlistEnabled !== false : true,
    rsvpAudience: (event && event.rsvpAudience) || 'SignedIn', attendeeVisibility: (event && event.attendeeVisibility) || 'Attendees', commentsEnabled: event ? event.commentsEnabled !== false : true,
    reminders: ((event && event.reminderOffsetsMinutes) || defaults).map(String).join(', '),
    hosts: ((event && event.hosts) || []).map(memberItem).filter(Boolean), instructors: ((event && event.instructors) || []).map(instructorItem).filter(Boolean),
    coverMediaId: null, ...metadataOf(event || {})
  };
}

function parseList(text) {
  return String(text || '').split(/[\s,]+/).map((x) => x.trim()).filter(Boolean);
}

/** Ids from picker items, or (older callers) from a comma separated id list. */
function idsOf(items, text) {
  if (Array.isArray(items)) return items.map((x) => x && x.id).filter(Boolean);
  return parseList(text);
}

/** The full admin create or edit body: shared fields plus RSVP settings, hosts, reminders, audience, metadata, and the recurrence rule. */
export function adminEventBody(form, advanced, { audience, recurrence = undefined } = {}) {
  const body = {
    ...eventBodyFrom(form),
    maxGuestsPerRsvp: Math.max(0, Math.min(10, Number(advanced.maxGuestsPerRsvp) || 0)), waitlistEnabled: Boolean(advanced.waitlistEnabled), rsvpAudience: advanced.rsvpAudience,
    attendeeVisibility: advanced.attendeeVisibility, commentsEnabled: Boolean(advanced.commentsEnabled), reminderOffsetsMinutes: parseList(advanced.reminders).map(Number).filter((n) => Number.isFinite(n)).slice(0, 3),
    hostUserIds: idsOf(advanced.hosts, advanced.hostUserIds).slice(0, 10), instructorIds: idsOf(advanced.instructors, advanced.instructorIds).slice(0, 10), audience,
    ...metadataForSave({ labels: advanced.labels, tags: advanced.tags }, { isStaff: true })
  };
  if (form.venueId) body.location = { venueId: form.venueId };
  if (advanced.coverMediaId) body.coverMediaId = advanced.coverMediaId;
  if (recurrence !== undefined) body.recurrence = recurrence;
  return body;
}

/** "Apply to" prompt for recurring events: this occurrence, this and following, or all. */
function ScopeDialog({ title, confirmLabel, onPick, onClose, children = null, danger = false }) {
  const [scope, setScope] = useState('One');
  return (
    <Modal open size="sm" onClose={onClose} title={title}
      footer={<><button type="button" className="button button-secondary" onClick={onClose}>{strings.common.cancel}</button><button type="button" className={'button ' + (danger ? 'button-danger' : 'button-primary')} onClick={() => onPick(scope)}>{confirmLabel}</button></>}>
      <div className="stack" role="radiogroup" aria-label={s.scopeLabel}>
        {['One', 'Following', 'All'].map((x) => (
          <label key={x} className="check-field"><input type="radio" name="edit-scope" checked={scope === x} onChange={() => setScope(x)} /><span>{s.scopes[x]}</span></label>
        ))}
        {children}
      </div>
    </Modal>
  );
}

function CancelDialog({ event, onClose, onDone }) {
  const { client } = useAdminAuth();
  const [reason, setReason] = useState('');
  const [notify, setNotify] = useState(true);
  const [scope, setScope] = useState('One');
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const submit = async () => {
    if (reason.trim().length < 3) {
      setError(s.cancelReasonRequired);
      return;
    }
    setBusy(true);
    try {
      await client.adminCancelEvent(event.id, { reason: reason.trim(), notifyAttendees: notify, scope: event.recurrence ? scope : undefined });
      onDone();
    } catch (err) {
      setError(errorText(err, phase4Errors()));
      setBusy(false);
    }
  };
  return (
    <Modal open size="sm" onClose={busy ? () => {} : onClose} title={s.cancelTitle(event.title)}
      footer={<><button type="button" className="button button-secondary" onClick={onClose} disabled={busy}>{strings.common.cancel}</button><button type="button" className="button button-danger" onClick={submit} disabled={busy}>{s.cancelEvent}</button></>}>
      <div className="stack-form">
        <ErrorBanner message={error} />
        <p className="small">{s.cancelBody}</p>
        {event.recurrence && (
          <div className="stack" role="radiogroup" aria-label={s.scopeLabel}>
            {['One', 'Following', 'All'].map((x) => <label key={x} className="check-field"><input type="radio" name="cancel-scope" checked={scope === x} onChange={() => setScope(x)} /><span>{s.scopes[x]}</span></label>)}
          </div>
        )}
        <Field id="cancel-reason" label={s.cancelReason}><textarea id="cancel-reason" rows={3} maxLength={500} value={reason} onChange={(e) => setReason(e.target.value)} disabled={busy} /></Field>
        <Toggle checked={notify} onChange={setNotify} label={s.notifyAttendees} disabled={busy} />
      </div>
    </Modal>
  );
}

/** Whether an RSVP row offers "Check in": only Going RSVPs not yet checked in (the server answers 409 NotGoing otherwise). */
export function canCheckIn(row) {
  return Boolean(row && row.status === 'Going' && !row.checkedInUtc);
}

function RsvpPanel({ event }) {
  const { client } = useAdminAuth();
  const toast = useToast();
  const [status, setStatus] = useState('');
  const fetchPage = useCallback((cursor, signal) => client.adminEnumerateRsvps(event.id, { continuationToken: cursor, maxResults: 100, filters: status ? { status: [status] } : {} }, signal), [client, event.id, status]);
  const list = useCursorList(fetchPage);
  const act = async (row, action) => {
    try {
      const updated = await client.adminRsvpAction(event.id, row.id, action, { notifyMember: action !== 'check-in' && action !== 'undo-check-in' });
      if (action === 'remove') list.update((items) => items.filter((x) => x.id !== row.id));
      else list.update((items) => items.map((x) => (x.id === row.id ? { ...x, ...(updated || {}) } : x)));
      toast.success(s.rsvpActionDone);
    } catch (err) {
      toast.error(errorText(err, phase4Errors()));
    }
  };
  const exportCsv = async () => {
    try {
      downloadText(await client.adminExportRsvpsCsv(event.id), (event.slug || event.id) + '-rsvps.csv', 'text/csv');
    } catch (err) {
      toast.error(errorText(err, phase4Errors()));
    }
  };
  return (
    <section className="panel stack" aria-labelledby="admin-rsvps">
      <div className="p4-player-bar">
        <h2 id="admin-rsvps" className="section-title">{s.rsvpsTitle}</h2>
        <span className="badge-row">
          <select className="select-compact" aria-label={s.columns.status} value={status} onChange={(e) => setStatus(e.target.value)}>
            <option value="">{s.allStatuses}</option>
            {['Going', 'Interested', 'Waitlisted'].map((x) => <option key={x} value={x}>{s.rsvpStatuses[x]}</option>)}
          </select>
          <button type="button" className="button button-secondary button-sm" onClick={exportCsv}><Icon name="download" size={14} /><span>{strings.admin.common.exportCsv}</span></button>
        </span>
      </div>
      <ErrorBanner message={list.error ? errorText(list.error, phase4Errors()) : ''} onRetry={list.refresh} />
      {list.loading && !list.loaded && <Loading inline />}
      <TableColumns id="tk.admin.events.EventEditorView"><table className="data-table">
        <caption className="visually-hidden">{s.rsvpsTitle}</caption>
        <thead><tr><th scope="col">{s.columns.member}</th><th scope="col">{s.columns.status}</th><th scope="col" className="num">{s.columns.guests}</th><th scope="col">{s.columns.checkedIn}</th><th scope="col"><span className="visually-hidden">{s.columns.actions}</span></th></tr></thead>
        <tbody>
          {list.items.map((row) => (
            <tr key={row.id}>
              <td>{authorName(row.user)}{row.hideFromAttendeeLists && <span className="muted small"> ({s.hidden})</span>}</td>
              <td>{s.rsvpStatuses[row.status] || row.status}</td>
              <td className="num">{row.guestCount || 0}</td>
              <td>{row.checkedInUtc ? formatDateTime(row.checkedInUtc) : '-'}</td>
              <td className="cell-actions">
                <ActionMenu label={strings.admin.common.rowActions(authorName(row.user))} items={[
                  ...(row.checkedInUtc ? [{ label: s.undoCheckIn, icon: 'refresh', onSelect: () => act(row, 'undo-check-in') }] : canCheckIn(row) ? [{ label: s.checkIn, icon: 'check', onSelect: () => act(row, 'check-in') }] : []),
                  row.status !== 'Going' ? { label: s.promote, icon: 'chevronUp', onSelect: () => act(row, 'promote') } : { label: s.toWaitlist, icon: 'chevronDown', onSelect: () => act(row, 'move-to-waitlist') },
                  { label: s.removeRsvp, icon: 'trash', danger: true, onSelect: () => act(row, 'remove') }
                ]} />
              </td>
            </tr>
          ))}
          {list.loaded && !list.items.length && <tr><td colSpan={5} className="table-empty">{s.rsvpsEmpty}</td></tr>}
        </tbody>
      </table></TableColumns>
      {list.loaded && !list.done && <button type="button" className="button button-secondary button-sm" onClick={list.loadMore}>{ev.loadMore}</button>}
    </section>
  );
}

function EventEditor({ event, onSaved }) {
  const { client, isSystemAdmin } = useAdminAuth();
  const { property } = useProperty();
  const toast = useToast();
  const navigate = useNavigate();
  const isNew = !event;
  const canFullEdit = isNew || !event.viewer || event.viewer.canEdit !== false;
  const [form, setForm] = useState(() => eventFormFrom(event));
  const [advanced, setAdvanced] = useState(() => advancedFrom(event, property));
  const [venuePick, setVenuePick] = useState(() => (event && event.location && event.location.venue ? [venueItem(event.location.venue)] : []));
  const searches = useMemo(() => pickerSearches(client, { isSystemAdmin }), [client, isSystemAdmin]);
  const [recurrence, setRecurrence] = useState(() => (event && event.recurrence ? recurrenceFormFromRule(event.recurrence.rule, eventFormFrom(event).startLocal) : defaultRecurrenceForm('')));
  const [notifyAttendees, setNotifyAttendees] = useState(true);
  const [problems, setProblems] = useState({});
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const [dialog, setDialog] = useState(null);
  const [duplicateStart, setDuplicateStart] = useState('');
  const audience = useAudiencePicker({ feature: 'events', isSystemAdmin, current: event ? event.audience : null });
  const [slug, setSlug] = useState(() => (event && event.slug) || '');
  const [slugError, setSlugError] = useState('');
  const slugStatus = useSlugStatus({ client, kind: PublicPageKind.Event, slug, objectId: event ? event.id : null, currentSlug: event ? event.slug : null, allowEmpty: isNew });
  const set = (changes) => setForm((f) => ({ ...f, ...changes }));
  const setAdv = (changes) => setAdvanced((a) => ({ ...a, ...changes }));
  const recurring = Boolean(event && event.recurrence);

  const validate = () => {
    const found = validateEventForm(validationShape(form, { reminderOffsetsMinutes: parseList(advanced.reminders) }));
    const rec = validateRecurrenceForm(recurrence, form.startLocal);
    setProblems(found);
    if (Object.keys(found).length || Object.keys(rec).length) {
      setError(s.fixProblems);
      return false;
    }
    if (slugBlocks(slugStatus)) {
      setError(strings.publicPages.fixSlug);
      return false;
    }
    return true;
  };
  const save = async (scope) => {
    setDialog(null);
    setBusy(true);
    setError('');
    const rule = buildRecurrenceRule(recurrence);
    // A new event sends its rule; an edit sends the rule only for "this and following" or "all".
    const recurrenceField = isNew ? rule || undefined : recurring && scope && scope !== 'One' ? rule : undefined;
    const body = adminEventBody(form, advanced, { audience: audience.value, recurrence: recurrenceField });
    if (slug.trim()) body.slug = slug.trim();
    setSlugError('');
    try {
      const saved = isNew ? await client.adminCreateEvent(body) : await client.adminUpdateEvent(event.id, { ...body, notifyAttendees }, recurring ? scope : undefined);
      toast.success(isNew ? s.created : strings.admin.common.saved);
      if (isNew) navigate('/admin/events/' + encodeURIComponent(saved.id), { replace: true });
      else onSaved(saved);
    } catch (err) {
      const { slug: _slugProblem, ...fieldErrors } = (err && err.fieldErrors) || {};
      setProblems((p) => ({ ...p, ...fieldErrors }));
      setSlugError(slugServerError(err));
      setError(errorText(err, phase4Errors()));
    } finally {
      setBusy(false);
    }
  };
  const submit = (e) => {
    e.preventDefault();
    if (!validate()) return;
    if (recurring) setDialog('scope');
    else save(undefined);
  };
  const run = async (fn, message, after) => {
    setBusy(true);
    try {
      const result = await fn();
      toast.success(message);
      setDialog(null);
      if (after) after(result);
    } catch (err) {
      setError(errorText(err, phase4Errors()));
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="view">
      <Link to="/admin/events" className="back-link small"><Icon name="chevronLeft" size={14} /><span>{s.back}</span></Link>
      <PageHeader
        title={isNew ? s.create : event.title}
        summary={event ? formatEventShort(event) : s.createHint}
        actions={event && (
          <>
            <Badge tone={event.status === 'Published' ? 'success' : event.status === 'Canceled' ? 'danger' : 'neutral'}>{s.statuses[event.status] || event.status}</Badge>
            {event.status === 'Published' && <a className="button button-secondary button-sm" href={appPaths.event(event.id)} target="_blank" rel="noopener noreferrer"><Icon name="eye" size={14} /><span>{strings.admin.common.view}</span></a>}
            {event.publicUrl && <PublicPageLink url={event.publicUrl} />}
            {event.status === 'Draft' && event.viewer && event.viewer.canEdit !== false && <button type="button" className="button button-primary button-sm" disabled={busy} onClick={() => setDialog('publish')}>{s.publish}</button>}
            {event.status === 'Published' && event.viewer && event.viewer.canCancel !== false && <button type="button" className="button button-danger-outline button-sm" disabled={busy} onClick={() => setDialog('cancel')}>{s.cancelEvent}</button>}
            {canFullEdit && <button type="button" className="button button-secondary button-sm" disabled={busy} onClick={() => setDialog('duplicate')}>{strings.admin.common.duplicate}</button>}
            {event.status === 'Draft' && <button type="button" className="button button-danger-outline button-sm" disabled={busy} onClick={() => setDialog('delete')}>{strings.common.delete}</button>}
          </>
        )}
      />
      {event && event.status === 'Canceled' && <Banner tone="danger">{ev.canceledBanner(event.cancelReason)}</Banner>}
      {event && event.status === 'Proposed' && <Banner tone="info">{s.proposalBanner(authorName(event.proposedBy))}</Banner>}
      {event && event.recurrence && <Banner tone="info" icon="refresh">{s.partOfSeries(event.recurrence.summary, (event.recurrence.occurrenceIndex || 0) + 1)}</Banner>}
      {!canFullEdit && <Banner tone="info">{s.hostNote}</Banner>}
      <ErrorBanner message={error} />
      <form className="panel stack-form" onSubmit={submit} noValidate aria-label={isNew ? s.create : s.editTitle}>
        <EventFormFields form={form} set={set} problems={problems} idPrefix="admin-event" disabled={busy} venueField={(
          <EntityPicker id="admin-event-venue" label={s.fields.venue} hint={s.venueHint} value={venuePick} onChange={(items) => { setVenuePick(items); set({ venueId: items[0] ? items[0].id : '' }); }} search={searches.venues} disabled={busy || !canFullEdit} placeholder={s.venuePlaceholder} />
        )} />
        <SlugField id="admin-event-slug" kind={PublicPageKind.Event} value={slug} onChange={(next) => { setSlug(next); setSlugError(''); }} status={slugStatus} allowEmpty={isNew}
          disabled={busy} serverError={slugError} previewUrl={event ? event.publicUrl : null}
          note={strings.publicPages.eventNote + (event && event.slug ? ' ' + strings.publicPages.slugRenameNote : '')} />
        {(isNew || recurring) && <RecurrenceEditor form={recurrence} onChange={setRecurrence} startLocal={form.startLocal} disabled={busy || !canFullEdit} />}
        <fieldset className="fieldset">
          <legend>{s.rsvpSettings}</legend>
          <div className="form-grid">
            <Field id="adv-guests" label={s.fields.maxGuestsPerRsvp}><input id="adv-guests" type="number" min={0} max={10} value={advanced.maxGuestsPerRsvp} onChange={(e) => setAdv({ maxGuestsPerRsvp: e.target.value })} disabled={busy} /></Field>
            <Field id="adv-rsvp-audience" label={s.fields.rsvpAudience}><select id="adv-rsvp-audience" value={advanced.rsvpAudience} onChange={(e) => setAdv({ rsvpAudience: e.target.value })} disabled={busy}>{['SignedIn', 'Members'].map((x) => <option key={x} value={x}>{strings.access.audiences[x]}</option>)}</select></Field>
            <Field id="adv-attendees" label={s.fields.attendeeVisibility}><select id="adv-attendees" value={advanced.attendeeVisibility} onChange={(e) => setAdv({ attendeeVisibility: e.target.value })} disabled={busy}>{AttendeeVisibilities.map((x) => <option key={x} value={x}>{s.attendeeVisibilities[x]}</option>)}</select></Field>
          </div>
          <Toggle checked={advanced.waitlistEnabled} onChange={(v) => setAdv({ waitlistEnabled: v })} label={s.fields.waitlistEnabled} disabled={busy} />
          <Toggle checked={advanced.commentsEnabled} onChange={(v) => setAdv({ commentsEnabled: v })} label={s.fields.commentsEnabled} disabled={busy} />
          <Field id="adv-reminders" label={s.fields.reminders} hint={s.remindersHint(parseList(advanced.reminders).map((m) => formatReminderOffset(m)).join(', '))} error={problems.reminderOffsetsMinutes ? ev.problems[problems.reminderOffsetsMinutes] : ''}><input id="adv-reminders" value={advanced.reminders} onChange={(e) => setAdv({ reminders: e.target.value })} disabled={busy} /></Field>
          <div className="form-grid">
            <EntityPicker id="adv-hosts" label={s.fields.hosts} hint={s.hostsHint} value={advanced.hosts} onChange={(items) => setAdv({ hosts: items })} search={searches.members} multiple max={10} disabled={busy || !canFullEdit} placeholder={s.memberPlaceholder} />
            <EntityPicker id="adv-instructors" label={s.fields.instructors} hint={s.instructorsHint} value={advanced.instructors} onChange={(items) => setAdv({ instructors: items })} search={searches.instructors} multiple max={10} disabled={busy || !canFullEdit} placeholder={s.instructorPlaceholder} />
          </div>
        </fieldset>
        <AudienceField id="admin-event-audience" picker={audience} disabled={busy || !canFullEdit} />
        <div className="field"><span className="label-text">{s.fields.cover}</span><FileUploadButton client={client} kind="Image" accept="image/jpeg,image/png,image/webp" label={advanced.coverMediaId ? s.uploaded : s.uploadCover} onUploaded={(m) => setAdv({ coverMediaId: m.id })} disabled={busy} /></div>
        <MetadataEditor value={{ labels: advanced.labels, tags: advanced.tags }} onChange={(meta) => setAdv(meta)} isStaff client={client} targetType="Event" disabled={busy} />
        {!isNew && <Toggle checked={notifyAttendees} onChange={setNotifyAttendees} label={s.notifyOnChange} disabled={busy} />}
        <div className="form-actions"><button type="submit" className="button button-primary" disabled={busy}>{isNew ? s.createDraft : strings.common.save}</button></div>
      </form>
      {event && (event.viewer ? event.viewer.canCheckIn !== false : true) && event.status !== 'Proposed' && <RsvpPanel event={event} />}
      {dialog === 'scope' && <ScopeDialog title={s.applyTo} confirmLabel={strings.common.save} onClose={() => setDialog(null)} onPick={save} />}
      {dialog === 'cancel' && <CancelDialog event={event} onClose={() => setDialog(null)} onDone={() => { setDialog(null); toast.success(s.canceledToast); onSaved(null); }} />}
      {dialog === 'publish' && (
        <ConfirmModal open danger={false} title={s.publishTitle(event.title)} message={s.publishBody} confirmLabel={s.publish} busy={busy} onCancel={() => setDialog(null)} onConfirm={() => run(() => client.adminPublishEvent(event.id, { notifyMembers: false }), s.publishedToast, () => onSaved(null))} />
      )}
      {dialog === 'delete' && <ConfirmModal open title={s.deleteTitle(event.title)} message={s.deleteBody} confirmLabel={strings.common.delete} busy={busy} onCancel={() => setDialog(null)} onConfirm={() => run(() => client.adminDeleteEvent(event.id), s.deleted, () => navigate('/admin/events'))} />}
      {dialog === 'duplicate' && (
        <Modal open size="sm" onClose={() => setDialog(null)} title={strings.admin.common.duplicate}
          footer={<><button type="button" className="button button-secondary" onClick={() => setDialog(null)}>{strings.common.cancel}</button><button type="button" className="button button-primary" disabled={!duplicateStart || busy} onClick={() => run(() => client.adminDuplicateEvent(event.id, { startLocal: duplicateStart, timeZone: form.timeZone }), s.duplicated, (copy) => navigate('/admin/events/' + encodeURIComponent(copy.id)))}>{strings.admin.common.duplicate}</button></>}>
          <Field id="dup-start" label={s.fields.duplicateStart} hint={s.duplicateHint(form.timeZone)}><input id="dup-start" type="datetime-local" value={duplicateStart} onChange={(e) => setDuplicateStart(e.target.value)} /></Field>
        </Modal>
      )}
    </div>
  );
}

/**
 * /admin/events/new and /admin/events/:id (section 4.7): the event editor with local wall time and
 * zone, location by venue or address, online details, RSVP settings, hosts, reminders, audience,
 * cover, labels and tags, the recurrence editor, and the edit scope prompt (this occurrence, this
 * and following, all) for recurring events; publish, cancel (with scope), duplicate, delete drafts,
 * and the RSVP list with check-in and CSV export.
 */
function EventEditorView() {
  const { id } = useParams();
  const { client } = useAdminAuth();
  const isNew = !id || id === 'new';
  const fetcher = useCallback((signal) => client.adminGetEvent(id, signal), [client, id]);
  const query = useApiQuery(fetcher, { enabled: !isNew });
  if (isNew) return <EventEditor event={null} onSaved={() => {}} />;
  if (!query.data) return <div className="view">{query.loading && <Loading />}<ErrorBanner message={query.error ? errorText(query.error, phase4Errors()) : ''} onRetry={query.reload} /></div>;
  return <EventEditor key={query.data.id + ':' + query.data.updatedUtc + ':' + query.data.status} event={query.data} onSaved={() => query.reload()} />;
}

export default EventEditorView;

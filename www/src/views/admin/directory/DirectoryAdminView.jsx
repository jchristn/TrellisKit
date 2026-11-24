import { directorySettingsForm, directorySettingsBody } from '../../../utils/directorySettings.js';
import { loadDailyMetrics } from '../../../utils/metricCollections.js';
import { useCallback, useMemo, useState } from 'react';
import { collectEnumeration, PublicPageKind, SubmissionTypes, VenueStatuses, deriveSlug, directoryConfig, formatAddress, formatChangeValue, isValidPosition, metadataForSave, metadataOf, moveItem } from '@trelliskit/shared';
import strings from '../../../strings.js';
import { useAdminAuth } from '../../../context/AdminAuthContext.jsx';
import { useProperty } from '../../../context/PropertyContext.jsx';
import { useToast } from '../../../context/ToastContext.jsx';
import useApiQuery from '../../../hooks/useApiQuery.js';
import PageHeader from '../../../components/PageHeader.jsx';
import Icon from '../../../components/Icon.jsx';
import Modal from '../../../components/Modal.jsx';
import ActionMenu from '../../../components/ActionMenu.jsx';
import ConfirmModal from '../../../components/ConfirmModal.jsx';
import CopyableId from '../../../components/CopyableId.jsx';
import MetadataEditor from '../../../components/MetadataEditor.jsx';
import AudienceField, { useAudiencePicker } from '../../../components/AudienceField.jsx';
import { Badge } from '../../../components/Badges.jsx';
import KpiCard from '../../../components/KpiCard.jsx';
import DailySeriesChart from '../../../components/usage/DailySeriesChart.jsx';
import { Banner, ErrorBanner, Loading } from '../../../components/States.jsx';
import { AdminListSection, Field, TabBar, Toggle, useAdminList, useTab } from '../../../components/admin/AdminParts.jsx';
import EntityPicker, { pickerSearches } from '../../../components/admin/EntityPicker.jsx';
import PinMap from '../../../components/directory/PinMap.jsx';
import { SlugField, slugBlocks, slugServerError, useSlugStatus } from '../../../components/PublicPageFields.jsx';
import { InstructorFields, VenueFields, instructorFieldsFrom, instructorFormFrom, venueFieldsFrom, venueFormFrom } from '../../../components/directory/DirectoryForms.jsx';
import { authorName, errorText } from '../../../utils/engagement.js';
import { formatDateTime, formatNumber } from '../../../utils/format.js';
import { appPaths, downloadText, phase4Errors, useBoundedList, useDirectoryTerms } from '../../../utils/phase4.js';
import { parseCsv } from '../../../utils/csv.js';

const s = strings.admin.directoryAdmin;
const c = strings.admin.common;
const v = strings.venues;
const ADMIN_TABS = Object.freeze(['venues', 'instructors', 'submissions', 'import', 'metrics', 'programs', 'settings']);
const STAFF_TABS = Object.freeze(['venues', 'instructors', 'submissions', 'import', 'metrics']);
const VENUE_FILTERS = Object.freeze(['search', 'status', 'geoSource', 'verified', 'countryCode', 'locality']);
const INSTRUCTOR_FILTERS = Object.freeze(['search', 'status', 'claimed']);
const SUBMISSION_FILTERS = Object.freeze(['status', 'type', 'submitterUserId', 'targetId']);

/**
 * The manual pin: a map with a draggable marker (click or drag to place, when a map style is
 * configured) and the latitude and longitude fields (5 decimal places), which always work.
 */
function PinFields({ pin, onChange, disabled }) {
  const { property } = useProperty();
  const valid = pin.lat === '' && pin.lng === '' ? true : isValidPosition(pin.lat, pin.lng);
  return (
    <fieldset className="fieldset">
      <legend>{s.pin}</legend>
      <PinMap property={property} pin={pin} onChange={onChange} disabled={disabled} />
      <div className="p4-pin">
        <Field id="pin-lat" label={s.lat}><input id="pin-lat" inputMode="decimal" value={pin.lat} onChange={(e) => onChange({ ...pin, lat: e.target.value })} disabled={disabled} /></Field>
        <Field id="pin-lng" label={s.lng}><input id="pin-lng" inputMode="decimal" value={pin.lng} onChange={(e) => onChange({ ...pin, lng: e.target.value })} disabled={disabled} /></Field>
      </div>
      {!valid && <span className="field-error" role="alert">{s.pinInvalid}</span>}
      <span className="muted small">{s.pinHint}</span>
    </fieldset>
  );
}

/** The review form's latitude and longitude text from a point (a submission's proposedGeo), or empty fields. */
export function pinFrom(geo) {
  return geo && geo.lat !== null && geo.lat !== undefined ? { lat: String(geo.lat), lng: String(geo.lng) } : { lat: '', lng: '' };
}

function pinValue(pin) {
  return pin.lat !== '' && pin.lng !== '' && isValidPosition(pin.lat, pin.lng) ? { lat: Number(pin.lat), lng: Number(pin.lng) } : null;
}

function VenueEditor({ venue, onClose, onSaved }) {
  const { client, isSystemAdmin } = useAdminAuth();
  const { d } = useDirectoryTerms();
  const [form, setForm] = useState(() => venueFormFrom(venue));
  const [pin, setPin] = useState(() => pinFrom(venue && venue.geo));
  const [meta, setMeta] = useState(() => metadataOf(venue || {}));
  const audience = useAudiencePicker({ feature: 'directory', isSystemAdmin, current: venue ? venue.audience : null });
  const [slug, setSlug] = useState(() => (venue && venue.slug) || '');
  const [slugError, setSlugError] = useState('');
  const slugStatus = useSlugStatus({ client, kind: PublicPageKind.Venue, slug, objectId: venue ? venue.id : null, currentSlug: venue ? venue.slug : null, allowEmpty: !venue });
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const set = (changes) => setForm((f) => ({ ...f, ...changes }));
  const submit = async (e) => {
    e.preventDefault();
    if (form.name.trim().length < 2) {
      setError(v.problems.tooShort);
      return;
    }
    if (slugBlocks(slugStatus)) {
      setError(strings.publicPages.fixSlug);
      return;
    }
    setBusy(true);
    setError('');
    setSlugError('');
    const body = { ...venueFieldsFrom(form), audience: audience.value, ...metadataForSave(meta, { isStaff: true }) };
    if (slug.trim()) body.slug = slug.trim();
    const geo = pinValue(pin);
    if (geo) body.geo = geo;
    try {
      onSaved(venue ? await client.adminUpdateVenue(venue.id, body) : await client.adminCreateVenue(body));
    } catch (err) {
      setSlugError(slugServerError(err));
      setError(errorText(err, phase4Errors()));
      setBusy(false);
    }
  };
  return (
    <Modal open variant="drawer" size="lg" onClose={busy ? () => {} : onClose} title={venue ? s.editVenue(venue.name) : d(s.createVenue)}
      footer={<><button type="button" className="button button-secondary" onClick={onClose} disabled={busy}>{strings.common.cancel}</button><button type="submit" form="venue-form" className="button button-primary" disabled={busy}>{strings.common.save}</button></>}>
      <form id="venue-form" className="stack-form" onSubmit={submit} noValidate>
        <ErrorBanner message={error} />
        <VenueFields form={form} set={set} idPrefix="admin-venue" disabled={busy} showVisibility />
        <SlugField id="admin-venue-slug" kind={PublicPageKind.Venue} value={slug} onChange={(next) => { setSlug(next); setSlugError(''); }} status={slugStatus} allowEmpty={!venue}
          placeholder={venue ? '' : deriveSlug(form.name, form.locality, form.region)} disabled={busy} serverError={slugError} previewUrl={venue ? venue.publicUrl : null}
          note={venue && venue.slug ? strings.publicPages.slugRenameNote : ''} />
        <PinFields pin={pin} onChange={setPin} disabled={busy} />
        <AudienceField id="admin-venue-audience" picker={audience} disabled={busy} />
        <MetadataEditor value={meta} onChange={setMeta} isStaff client={client} targetType="Venue" disabled={busy} />
      </form>
    </Modal>
  );
}

function VenuesTab() {
  const { client } = useAdminAuth();
  const { d } = useDirectoryTerms();
  const toast = useToast();
  const [editor, setEditor] = useState(null);
  const [confirm, setConfirm] = useState(null);
  const fetch = useCallback((q, signal) => client.adminEnumerateVenues(q, signal), [client]);
  const admin = useAdminList({ storageKey: 'tk.admin.venues', keys: VENUE_FILTERS, prefix: 'v.', fetch, transform: ({ status, ...rest }) => ({ ...rest, ...(status ? { status: [status] } : {}) }) });
  const run = async (fn, message) => {
    try {
      await fn();
      toast.success(message);
      admin.list.reload();
    } catch (err) {
      toast.error(errorText(err, phase4Errors()));
    } finally {
      setConfirm(null);
    }
  };
  const exportCsv = async () => {
    try {
      downloadText(await client.adminExportDirectoryCsv(), 'directory.csv', 'text/csv');
      toast.success(c.exported);
    } catch (err) {
      toast.error(errorText(err, phase4Errors()));
    }
  };
  const menu = (row) => [
    { label: c.view, icon: 'eye', onSelect: () => window.open(appPaths.venue(row.slug || row.id), '_blank', 'noopener') },
    ...(row.publicUrl ? [{ label: strings.publicPages.view, icon: 'external', onSelect: () => window.open(row.publicUrl, '_blank', 'noopener') }] : []),
    { label: c.edit, icon: 'edit', onSelect: () => setEditor({ venue: row }) },
    row.isVerified ? { label: s.unverify, icon: 'close', onSelect: () => run(() => client.adminVenueAction(row.id, 'unverify'), s.done) } : { label: s.verify, icon: 'check', onSelect: () => run(() => client.adminVenueAction(row.id, 'verify'), s.done) },
    row.status === 'Closed' ? { label: s.reopen, icon: 'refresh', onSelect: () => run(() => client.adminVenueAction(row.id, 'reopen'), s.done) } : { label: s.close, icon: 'lock', onSelect: () => run(() => client.adminVenueAction(row.id, 'close'), s.done) },
    row.status === 'Archived' ? { label: s.unarchive, icon: 'refresh', onSelect: () => run(() => client.adminVenueAction(row.id, 'unarchive'), s.done) } : { label: s.archive, icon: 'lock', onSelect: () => run(() => client.adminVenueAction(row.id, 'archive'), s.done) },
    { label: s.regeocode, icon: 'pin', onSelect: () => run(() => client.adminRegeocode({ venueIds: [row.id], onlyMissing: false }), s.regeocodeQueued) },
    { label: strings.common.delete, icon: 'trash', danger: true, onSelect: () => setConfirm(row) }
  ];
  const columns = [
    { key: 'name', label: s.columns.name, render: (row) => <span className="cell-stack"><strong>{row.name}</strong><span className="muted small">{formatAddress(row.address, 'Exact', { isStaff: true })}</span></span> },
    { key: 'status', label: s.columns.status, render: (row) => <span className="badge-row"><Badge tone={row.status === 'Active' ? 'success' : 'neutral'}>{s.statuses[row.status] || row.status}</Badge>{row.isVerified && <Badge tone="info">{v.verified}</Badge>}</span> },
    { key: 'pin', label: s.columns.pin, render: (row) => (row.geo && row.geo.lat !== null && row.geo.lat !== undefined ? <span className="small">{s.geoSources[row.geo.source] || row.geo.source}</span> : <Badge tone="warning">{s.needsPin}</Badge>) },
    { key: 'visibility', label: s.columns.visibility, render: (row) => v.visibilities[row.addressVisibility] || row.addressVisibility },
    { key: 'instructors', label: d(s.columns.instructors), className: 'num', render: (row) => formatNumber(row.instructorCount || 0) },
    { key: 'pending', label: s.columns.pending, className: 'num', render: (row) => (row.pendingSubmissionCount ? <Badge tone="warning">{row.pendingSubmissionCount}</Badge> : '0') },
    { key: 'reports', label: s.columns.reports, className: 'num', render: (row) => (row.openReportCount ? <Badge tone="warning">{row.openReportCount}</Badge> : '0') },
    { key: 'id', label: s.columns.id, className: 'cell-id', render: (row) => <CopyableId value={row.id} label={s.columns.id} /> },
    { key: 'actions', label: <span className="visually-hidden">{s.columns.actions}</span>, className: 'cell-actions', render: (row) => <ActionMenu label={c.rowActions(row.name)} items={menu(row)} /> }
  ];
  const filterFields = [
    { key: 'search', label: c.search, grow: true },
    { key: 'status', label: s.columns.status, type: 'select', options: VenueStatuses.map((x) => ({ value: x, label: s.statuses[x] })) },
    { key: 'geoSource', label: s.columns.pin, type: 'select', options: Object.keys(s.geoSources).map((x) => ({ value: x, label: x === 'None' ? s.needsPin : s.geoSources[x] })) },
    { key: 'verified', label: v.verified, type: 'checkbox' },
    { key: 'countryCode', label: v.fields.countryCode },
    { key: 'locality', label: v.fields.locality }
  ];
  return (
    <>
      <div className="form-actions-start">
        <button type="button" className="button button-primary button-sm" onClick={() => setEditor({})}><Icon name="plus" size={14} /><span>{d(s.createVenue)}</span></button>
        <button type="button" className="button button-secondary button-sm" onClick={() => run(() => client.adminRegeocode({ onlyMissing: true }), s.regeocodeQueued)}><Icon name="pin" size={14} /><span>{s.regeocodeMissing}</span></button>
        <button type="button" className="button button-secondary button-sm" onClick={exportCsv}><Icon name="download" size={14} /><span>{c.exportCsv}</span></button>
      </div>
      <AdminListSection admin={admin} filterFields={filterFields} client={client} targetType="Venue" columns={columns} caption={d(s.venuesTitle)} empty={d(s.venuesEmpty)} emptyFiltered={s.emptyFiltered} onRowClick={(row) => setEditor({ venue: row })} />
      {editor && <VenueEditor venue={editor.venue || null} onClose={() => setEditor(null)} onSaved={() => { toast.success(c.saved); setEditor(null); admin.list.reload(); }} />}
      <ConfirmModal open={Boolean(confirm)} title={confirm ? s.deleteTitle(confirm.name) : ''} message={s.deleteVenueBody} confirmLabel={strings.common.delete} onCancel={() => setConfirm(null)} onConfirm={() => run(() => client.adminDeleteVenue(confirm.id), s.deleted)} />
    </>
  );
}

function InstructorEditor({ instructor, onClose, onSaved }) {
  const { client, isSystemAdmin } = useAdminAuth();
  const { property } = useProperty();
  const { d } = useDirectoryTerms();
  const toast = useToast();
  const [form, setForm] = useState(() => instructorFormFrom(instructor));
  const [meta, setMeta] = useState(() => metadataOf(instructor || {}));
  const [linkUser, setLinkUser] = useState([]);
  const [venueLink, setVenueLink] = useState({ venue: [], role: 'Regular' });
  const searches = useMemo(() => pickerSearches(client, { isSystemAdmin, directoryStaff: true }), [client, isSystemAdmin]);
  const venueId = venueLink.venue[0] ? venueLink.venue[0].id : '';
  const audience = useAudiencePicker({ feature: 'directory', isSystemAdmin, current: instructor ? instructor.audience : null });
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const showRank = Boolean(directoryConfig(property).instructorRankFieldKey);
  const set = (changes) => setForm((f) => ({ ...f, ...changes }));
  const submit = async (e) => {
    e.preventDefault();
    if (form.displayName.trim().length < 2) {
      setError(v.problems.tooShort);
      return;
    }
    setBusy(true);
    setError('');
    const body = { ...instructorFieldsFrom(form, { withRank: showRank }), audience: audience.value, ...metadataForSave(meta, { isStaff: true }) };
    try {
      onSaved(instructor ? await client.adminUpdateInstructor(instructor.id, body) : await client.adminCreateInstructor(body));
    } catch (err) {
      setError(errorText(err, phase4Errors()));
      setBusy(false);
    }
  };
  const act = async (fn, message) => {
    try {
      await fn();
      toast.success(message);
    } catch (err) {
      setError(errorText(err, phase4Errors()));
    }
  };
  return (
    <Modal open variant="drawer" size="lg" onClose={busy ? () => {} : onClose} title={instructor ? s.editInstructor(instructor.displayName) : d(s.createInstructor)}
      footer={<><button type="button" className="button button-secondary" onClick={onClose} disabled={busy}>{strings.common.cancel}</button><button type="submit" form="instructor-form" className="button button-primary" disabled={busy}>{strings.common.save}</button></>}>
      <form id="instructor-form" className="stack-form" onSubmit={submit} noValidate>
        <ErrorBanner message={error} />
        <InstructorFields form={form} set={set} idPrefix="admin-instructor" disabled={busy} showRank={showRank} />
        <AudienceField id="admin-instructor-audience" picker={audience} disabled={busy} />
        <MetadataEditor value={meta} onChange={setMeta} isStaff client={client} targetType="Instructor" disabled={busy} />
      </form>
      {instructor && (
        <section className="stack" style={{ marginTop: 16 }}>
          <h3>{s.linkedMember}</h3>
          {instructor.linkedUser ? (
            <p className="badge-row">{authorName(instructor.linkedUser)} <button type="button" className="button button-secondary button-sm" onClick={() => act(() => client.adminUnlinkInstructorUser(instructor.id), s.unlinked)}>{s.unlink}</button></p>
          ) : (
            <form className="inline-form picker-form" onSubmit={(e) => { e.preventDefault(); if (linkUser[0]) act(() => client.adminLinkInstructorUser(instructor.id, linkUser[0].id), s.linked); }}>
              <EntityPicker id="instructor-link-member" label={s.member} value={linkUser} onChange={setLinkUser} search={searches.members} placeholder={strings.admin.eventsAdmin.memberPlaceholder} />
              <button type="submit" className="button button-secondary button-sm" disabled={!linkUser[0]}>{s.link}</button>
            </form>
          )}
          <span className="muted small">{s.linkNote}</span>
          <h3>{d(s.venueLinks)}</h3>
          <form className="inline-form picker-form" onSubmit={(e) => { e.preventDefault(); if (venueId) act(() => client.adminLinkVenueInstructor(venueId, instructor.id, venueLink.role), s.linked); }}>
            <EntityPicker id="instructor-link-venue" label={d(s.venueLabel)} value={venueLink.venue} onChange={(items) => setVenueLink({ ...venueLink, venue: items })} search={searches.venues} placeholder={strings.admin.eventsAdmin.venuePlaceholder} />
            <select className="select-compact" aria-label={s.role} value={venueLink.role} onChange={(e) => setVenueLink({ ...venueLink, role: e.target.value })}>{['Head', 'Regular', 'Guest'].map((r) => <option key={r} value={r}>{d(v.roles[r])}</option>)}</select>
            <button type="submit" className="button button-secondary button-sm" disabled={!venueId}>{s.link}</button>
            <button type="button" className="button button-secondary button-sm" disabled={!venueId} onClick={() => { if (venueId) act(() => client.adminUnlinkVenueInstructor(venueId, instructor.id), s.unlinked); }}>{s.unlink}</button>
          </form>
          {(instructor.venues && instructor.venues.objects || []).length > 0 && <p className="small">{instructor.venues.objects.map((x) => x.name + ' (' + x.id + ')').join(', ')}</p>}
        </section>
      )}
    </Modal>
  );
}

function InstructorsTab() {
  const { client } = useAdminAuth();
  const { d } = useDirectoryTerms();
  const toast = useToast();
  const [editor, setEditor] = useState(null);
  const [confirm, setConfirm] = useState(null);
  const fetch = useCallback((q, signal) => client.adminEnumerateInstructors(q, signal), [client]);
  const admin = useAdminList({ storageKey: 'tk.admin.instructors', keys: INSTRUCTOR_FILTERS, prefix: 'i.', fetch, transform: ({ status, ...rest }) => ({ ...rest, ...(status ? { status: [status] } : {}) }) });
  const columns = [
    { key: 'name', label: s.columns.name, render: (row) => <strong>{row.displayName}</strong> },
    { key: 'linked', label: s.linkedMember, render: (row) => (row.linkedUser ? authorName(row.linkedUser) : '-') },
    { key: 'venues', label: d(s.columns.venues), className: 'num', render: (row) => formatNumber(row.venueCount || 0) },
    { key: 'status', label: s.columns.status, render: (row) => s.statuses[row.status] || row.status },
    { key: 'id', label: s.columns.id, className: 'cell-id', render: (row) => <CopyableId value={row.id} label={s.columns.id} /> },
    { key: 'actions', label: <span className="visually-hidden">{s.columns.actions}</span>, className: 'cell-actions', render: (row) => <ActionMenu label={c.rowActions(row.displayName)} items={[
      { label: c.view, icon: 'eye', onSelect: () => window.open(appPaths.instructor(row.slug || row.id), '_blank', 'noopener') },
      { label: c.edit, icon: 'edit', onSelect: () => setEditor({ instructor: row }) },
      { label: strings.common.delete, icon: 'trash', danger: true, onSelect: () => setConfirm(row) }
    ]} /> }
  ];
  const filterFields = [
    { key: 'search', label: c.search, grow: true },
    { key: 'status', label: s.columns.status, type: 'select', options: ['Active', 'Archived'].map((x) => ({ value: x, label: s.statuses[x] })) },
    { key: 'claimed', label: s.claimed, type: 'checkbox' }
  ];
  return (
    <>
      <div className="form-actions-start"><button type="button" className="button button-primary button-sm" onClick={() => setEditor({})}><Icon name="plus" size={14} /><span>{d(s.createInstructor)}</span></button></div>
      <AdminListSection admin={admin} filterFields={filterFields} client={client} targetType="Instructor" columns={columns} caption={d(s.instructorsTitle)} empty={d(s.instructorsEmpty)} emptyFiltered={s.emptyFiltered} onRowClick={(row) => setEditor({ instructor: row })} />
      {editor && <InstructorEditor instructor={editor.instructor || null} onClose={() => setEditor(null)} onSaved={() => { toast.success(c.saved); setEditor(null); admin.list.reload(); }} />}
      <ConfirmModal open={Boolean(confirm)} title={confirm ? s.deleteTitle(confirm.displayName) : ''} message={s.deleteInstructorBody} confirmLabel={strings.common.delete} onCancel={() => setConfirm(null)}
        onConfirm={async () => { try { await client.adminDeleteInstructor(confirm.id); toast.success(s.deleted); admin.list.reload(); } catch (err) { toast.error(errorText(err, phase4Errors())); } finally { setConfirm(null); } }} />
    </>
  );
}

function SubmissionReview({ id, onClose, onDone }) {
  const { client } = useAdminAuth();
  const { d } = useDirectoryTerms();
  const fetcher = useCallback((signal) => client.adminGetSubmission(id, signal), [client, id]);
  const query = useApiQuery(fetcher);
  const sub = query.data;
  // The member's own pin (proposedGeo) is shown until the reviewer edits it.
  const [pinEdit, setPin] = useState(null);
  const pin = pinEdit || pinFrom(sub && sub.proposedGeo);
  const [note, setNote] = useState('');
  const [notify, setNotify] = useState(true);
  const [name, setName] = useState(null);
  const [error, setError] = useState('');
  const [stale, setStale] = useState(false);
  const [busy, setBusy] = useState(false);
  const proposedName = sub ? ((sub.changes || []).find((x) => x.field === 'name' || x.field === 'displayName') || {}).proposed : null;
  const act = async (decision) => {
    if (decision === 'reject' && note.trim().length < 3) {
      setError(s.reviewNoteRequired);
      return;
    }
    setBusy(true);
    setError('');
    setStale(false);
    try {
      if (decision === 'approve') {
        if (name !== null && name.trim() && name.trim() !== proposedName) await client.adminUpdateSubmission(sub.id, sub.type.includes('Instructor') ? { displayName: name.trim() } : { name: name.trim() });
        await client.adminApproveSubmission(sub.id, { reviewNote: note.trim() || undefined, notifySubmitter: notify, geo: pinValue(pin) || undefined });
      } else await client.adminRejectSubmission(sub.id, { reviewNote: note.trim(), notifySubmitter: notify });
      onDone(decision);
    } catch (err) {
      if (err && err.code === 'SubmissionStale') {
        setStale(true);
        query.reload();
      }
      setError(errorText(err, phase4Errors()));
      setBusy(false);
    }
  };
  return (
    <Modal open variant="drawer" size="lg" onClose={busy ? () => {} : onClose} title={sub ? d(v.submissionTypes[sub.type] || sub.type) + (sub.target ? ': ' + sub.target.name : '') : s.review}
      footer={sub && sub.status === 'Pending' && (
        <>
          <button type="button" className="button button-secondary" onClick={onClose} disabled={busy}>{strings.common.cancel}</button>
          <button type="button" className="button button-danger" onClick={() => act('reject')} disabled={busy}>{s.reject}</button>
          <button type="button" className="button button-primary" onClick={() => act('approve')} disabled={busy}>{s.approve}</button>
        </>
      )}>
      {query.loading && !sub && <Loading />}
      <ErrorBanner message={query.error ? errorText(query.error, phase4Errors()) : ''} onRetry={query.reload} />
      {sub && (
        <div className="stack">
          {stale && <Banner tone="warning">{s.staleNote}</Banner>}
          <ErrorBanner message={error} />
          <p className="small">{s.submittedBy(authorName(sub.submitter), formatDateTime(sub.createdUtc))}</p>
          {sub.note && <p><strong>{v.noteLabel}:</strong> {sub.note}</p>}
          <table className="p4-diff">
            <caption className="visually-hidden">{s.changes}</caption>
            <thead><tr><th scope="col">{s.field}</th><th scope="col">{s.current}</th><th scope="col">{s.proposed}</th></tr></thead>
            <tbody>{(sub.changes || []).map((x) => <tr key={x.field}><th scope="row">{v.fields[x.field.split('.').pop()] || x.field}</th><td className="before">{formatChangeValue(x.current) || '-'}</td><td className="after">{formatChangeValue(x.proposed) || '-'}</td></tr>)}</tbody>
          </table>
          {(sub.possibleDuplicates && sub.possibleDuplicates.objects || []).length > 0 && (
            <Banner tone="warning">{s.duplicates(sub.possibleDuplicates.objects.map((x) => x.name || x.displayName).join(', '))}</Banner>
          )}
          {sub.status === 'Pending' && (
            <>
              {(sub.type === 'NewVenue' || sub.type === 'NewInstructor' || sub.type.startsWith('Edit')) && proposedName !== undefined && proposedName !== null && (
                <Field id="review-name" label={s.fixName}><input id="review-name" value={name === null ? String(proposedName) : name} onChange={(e) => setName(e.target.value)} disabled={busy} /></Field>
              )}
              {(sub.type === 'NewVenue' || sub.type === 'EditVenue') && <PinFields pin={pin} onChange={setPin} disabled={busy} />}
              <Field id="review-note" label={s.reviewNote} hint={s.reviewNoteHint}><textarea id="review-note" rows={3} maxLength={1000} value={note} onChange={(e) => setNote(e.target.value)} disabled={busy} /></Field>
              <Toggle checked={notify} onChange={setNotify} label={s.notifySubmitter} disabled={busy} />
            </>
          )}
          {sub.status !== 'Pending' && <p><Badge tone="neutral">{v.submissionStatus[sub.status] || sub.status}</Badge> {sub.reviewNote}</p>}
        </div>
      )}
    </Modal>
  );
}

function SubmissionsTab() {
  const { client } = useAdminAuth();
  const { d } = useDirectoryTerms();
  const toast = useToast();
  const [open, setOpen] = useState(null);
  const fetch = useCallback((q, signal) => client.adminEnumerateSubmissions(q, signal), [client]);
  const admin = useAdminList({ storageKey: 'tk.admin.submissions', keys: SUBMISSION_FILTERS, prefix: 'q.', fetch, transform: ({ status, type, ...rest }) => ({ ...rest, ...(status ? { status: [status] } : {}), ...(type ? { type: [type] } : {}) }) });
  const columns = [
    { key: 'type', label: s.columns.type, render: (row) => d(v.submissionTypes[row.type] || row.type) },
    { key: 'target', label: s.columns.target, render: (row) => (row.target ? row.target.name : formatChangeValue(((row.changes || []).find((x) => x.field === 'name' || x.field === 'displayName') || {}).proposed)) },
    { key: 'submitter', label: s.columns.submitter, render: (row) => authorName(row.submitter) },
    { key: 'status', label: s.columns.status, render: (row) => <Badge tone={row.status === 'Pending' ? 'warning' : 'neutral'}>{v.submissionStatus[row.status] || row.status}</Badge> },
    { key: 'created', label: s.columns.created, sortField: 'createdUtc', render: (row) => formatDateTime(row.createdUtc) },
    { key: 'id', label: s.columns.id, className: 'cell-id', render: (row) => <CopyableId value={row.id} label={s.columns.id} /> }
  ];
  const filterFields = [
    { key: 'status', label: s.columns.status, type: 'select', options: ['Pending', 'Approved', 'Rejected', 'Withdrawn', 'Superseded'].map((x) => ({ value: x, label: v.submissionStatus[x] })) },
    { key: 'type', label: s.columns.type, type: 'select', options: SubmissionTypes.map((x) => ({ value: x, label: d(v.submissionTypes[x]) })) },
    { key: 'submitterUserId', label: s.columns.submitter, mono: true },
    { key: 'targetId', label: s.columns.target, mono: true }
  ];
  return (
    <>
      <p className="muted small">{s.queueIntro}</p>
      <AdminListSection admin={admin} filterFields={filterFields} client={client} targetType="DirectorySubmission" columns={columns} caption={s.submissionsTitle} empty={s.submissionsEmpty} emptyFiltered={s.emptyFiltered} onRowClick={(row) => setOpen(row.id)} />
      {open && <SubmissionReview id={open} onClose={() => setOpen(null)} onDone={(decision) => { toast.success(decision === 'approve' ? s.approved : s.rejected); setOpen(null); admin.list.reload(); }} />}
    </>
  );
}

function ImportTab() {
  const { client } = useAdminAuth();
  const toast = useToast();
  const [text, setText] = useState('');
  const [result, setResult] = useState(null);
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const rowsFrom = () => {
    const table = parseCsv(text);
    if (table.length < 2) return [];
    const [header, ...rest] = table;
    return rest.slice(0, 1000).map((cells) => {
      const row = {};
      const address = {};
      header.forEach((key, i) => {
        const k = String(key || '').trim();
        const value = String(cells[i] || '').trim();
        if (!k || !value) return;
        if (['line1', 'line2', 'locality', 'region', 'postalCode', 'countryCode'].includes(k)) address[k] = value;
        else if (k === 'programs') row.programs = value.split(/[;|]/).map((x) => x.trim()).filter(Boolean);
        else if (k === 'lat' || k === 'lng') row.geo = { ...(row.geo || {}), [k]: Number(value) };
        else row[k] = value;
      });
      if (Object.keys(address).length) row.address = address;
      return row;
    });
  };
  const send = async (dryRun) => {
    setBusy(true);
    setError('');
    try {
      const out = await client.adminImportDirectory({ rows: rowsFrom(), dryRun });
      setResult({ ...out, dryRun });
      if (!dryRun) toast.success(s.imported);
    } catch (err) {
      setError(errorText(err, phase4Errors()));
    } finally {
      setBusy(false);
    }
  };
  return (
    <section className="panel stack">
      <p className="muted small">{s.importIntro}</p>
      <ErrorBanner message={error} />
      <Field id="import-csv" label={s.importCsv} hint={s.importHint}><textarea id="import-csv" rows={10} className="mono" value={text} onChange={(e) => setText(e.target.value)} disabled={busy} /></Field>
      <div className="form-actions-start">
        <button type="button" className="button button-secondary button-sm" disabled={busy || !text.trim()} onClick={() => send(true)}>{s.dryRun}</button>
        <button type="button" className="button button-primary button-sm" disabled={busy || !result || !result.dryRun} onClick={() => send(false)}>{s.runImport}</button>
      </div>
      {result && (
        <div>
          <p>{s.importResult(result.dryRun, result.created || 0, result.updated || 0, result.skipped || 0)}</p>
          {result.errors && (result.errors.objects || []).length > 0 && <ul className="bullet-list small">{result.errors.objects.map((x, i) => <li key={i}>{s.importError(x.row, x.field, x.message)}</li>)}</ul>}
        </div>
      )}
    </section>
  );
}

function ProgramsTab() {
  const { client } = useAdminAuth();
  const toast = useToast();
  const fetchPage = useCallback((q, signal) => client.adminEnumerateDirectoryPrograms(q, signal), [client]);
  const list = useBoundedList(fetchPage, { pageSize: 100, maxPages: 2 });
  const [form, setForm] = useState({ key: '', label: '' });
  const [order, setOrder] = useState(null);
  const rows = order || list.items;
  const run = async (fn, message) => {
    try {
      await fn();
      toast.success(message);
      setOrder(null);
      list.reload();
      return true;
    } catch (err) {
      toast.error(errorText(err, phase4Errors()));
      return false;
    }
  };
  return (
    <section className="panel stack">
      <p className="muted small">{s.programsNote}</p>
      <ErrorBanner message={list.error ? errorText(list.error, phase4Errors()) : ''} onRetry={list.reload} />
      <ul className="history-list">
        {rows.map((p, i) => (
          <li key={p.id} className="history-item badge-row">
            <strong>{p.label}</strong> <span className="muted mono small">{p.key}</span>{p.isRetired && <Badge tone="neutral">{s.retired}</Badge>}
            <button type="button" className="icon-button" aria-label={s.moveUp(p.label)} disabled={i === 0} onClick={() => setOrder(moveItem(rows, i, i - 1))}><Icon name="chevronUp" size={14} /></button>
            <button type="button" className="icon-button" aria-label={s.moveDown(p.label)} disabled={i === rows.length - 1} onClick={() => setOrder(moveItem(rows, i, i + 1))}><Icon name="chevronDown" size={14} /></button>
            {!p.isRetired && <button type="button" className="link-button small" onClick={() => run(() => client.adminDeleteDirectoryProgram(p.id), s.programRetired)}>{s.retire}</button>}
          </li>
        ))}
      </ul>
      {order && <div><button type="button" className="button button-primary button-sm" onClick={() => run(() => client.adminOrderDirectoryPrograms(order.map((x) => x.id)), s.orderSaved)}>{s.saveOrder}</button></div>}
      <form className="inline-form" onSubmit={async (e) => { e.preventDefault(); if (form.key.trim() && form.label.trim() && await run(() => client.adminCreateDirectoryProgram({ key: form.key.trim(), label: form.label.trim() }), s.programAdded)) setForm({ key: '', label: '' }); }}>
        <input className="input-compact" aria-label={s.key} placeholder={s.key} value={form.key} onChange={(e) => setForm({ ...form, key: e.target.value })} maxLength={64} />
        <input className="input-compact" aria-label={s.label} placeholder={s.label} value={form.label} onChange={(e) => setForm({ ...form, label: e.target.value })} maxLength={80} />
        <button type="submit" className="button button-secondary button-sm">{s.addProgram}</button>
      </form>
    </section>
  );
}

function SettingsTab() {
  const { client } = useAdminAuth();
  const toast = useToast();
  const fetcher = useCallback(async (signal) => { const page = await collectEnumeration((q, sig) => client.adminGetDirectorySettings(q, sig), {}, { signal }); return directorySettingsForm({ ...page, settings: page.objects }); }, [client]);
  const query = useApiQuery(fetcher);
  const [draft, setDraft] = useState(null);
  const value = draft || query.data || {};
  const map = value.map || {};
  const geocoder = value.geocoder || {};
  const set = (changes) => setDraft({ ...value, ...changes });
  const save = async (e) => {
    e.preventDefault();
    try {
      setDraft(directorySettingsForm(await client.adminUpdateDirectorySettings(directorySettingsBody(value))));
      toast.success(c.saved);
    } catch (err) {
      toast.error(errorText(err, phase4Errors()));
    }
  };
  if (query.loading && !query.data) return <Loading />;
  const text = (key, label, obj = value, setter = set) => <Field id={'dir-' + key} label={label}><input id={'dir-' + key} value={obj[key] || ''} onChange={(e) => setter({ [key]: e.target.value })} /></Field>;
  return (
    <form className="panel stack-form" onSubmit={save}>
      <ErrorBanner message={query.error ? errorText(query.error, phase4Errors()) : ''} onRetry={query.reload} />
      <h3>{s.labels}</h3>
      <div className="form-grid">
        {text('venueLabel', s.fields.venueLabel)}
        {text('venueLabelPlural', s.fields.venueLabelPlural)}
        {text('instructorLabel', s.fields.instructorLabel)}
        {text('instructorLabelPlural', s.fields.instructorLabelPlural)}
        {text('instructorRankFieldKey', s.fields.instructorRankFieldKey)}
      </div>
      <Toggle checked={value.submissions !== false} onChange={(x) => set({ submissions: x })} label={s.fields.submissions} />
      <Toggle checked={value.claims !== false} onChange={(x) => set({ claims: x })} label={s.fields.claims} />
      <h3>{s.geocoder}</h3>
      <Field id="dir-geocoder" label={s.fields.geocoderProvider}>
        <select id="dir-geocoder" value={geocoder.provider || 'None'} onChange={(e) => set({ geocoder: { ...geocoder, provider: e.target.value } })}>{['None', 'Nominatim', 'AwsLocation', 'Http'].map((x) => <option key={x} value={x}>{s.geocoders[x]}</option>)}</select>
      </Field>
      <span className="muted small">{s.secretsNote}</span>
      <h3>{s.map}</h3>
      <div className="form-grid">
        <Field id="dir-map-provider" label={s.fields.mapProvider}>
          <select id="dir-map-provider" value={map.provider || 'None'} onChange={(e) => set({ map: { ...map, provider: e.target.value } })}>{['None', 'MapLibre', 'PlatformNative'].map((x) => <option key={x} value={x}>{s.mapProviders[x]}</option>)}</select>
        </Field>
        {text('styleUrl', s.fields.styleUrl, map, (ch) => set({ map: { ...map, ...ch } }))}
        {text('darkStyleUrl', s.fields.darkStyleUrl, map, (ch) => set({ map: { ...map, ...ch } }))}
        {text('attribution', s.fields.attribution, map, (ch) => set({ map: { ...map, ...ch } }))}
      </div>
      <div className="form-actions"><button type="submit" className="button button-primary">{strings.common.save}</button></div>
    </form>
  );
}

/** Directory metrics: the counters and the daily submissions, approvals, and new venues as a chart with its table. */
function MetricsTab() {
  const { client } = useAdminAuth();
  const { d } = useDirectoryTerms();
  const [days, setDays] = useState(30);
  const fetcher = useCallback((signal) => loadDailyMetrics((q, sig) => client.adminDirectoryMetrics(q, sig), { days: days }, signal), [client, days]);
  const query = useApiQuery(fetcher);
  const m = query.data;
  const t = s.metrics;
  const series = useMemo(() => (m ? [
    { key: 'submissions', label: t.submissions, points: m.submissionsByDay },
    { key: 'approvals', label: t.approvals, points: m.approvalsByDay },
    { key: 'venues', label: d(t.venuesCreated), points: m.venuesCreatedByDay }
  ] : []), [m, t, d]);
  return (
    <section className="stack">
      <label className="inline-field"><span className="label-text">{t.days}</span>
        <select className="select-compact" value={days} onChange={(e) => setDays(Number(e.target.value))}>{[7, 30, 90, 365].map((n) => <option key={n} value={n}>{t.daysValue(n)}</option>)}</select>
      </label>
      {query.loading && <Loading inline />}
      <ErrorBanner message={query.error ? errorText(query.error, phase4Errors()) : ''} onRetry={query.reload} />
      {m && (
        <>
          <div className="kpi-grid">
            <KpiCard label={d(t.activeVenues)} value={formatNumber(m.activeVenues || 0)} />
            <KpiCard label={d(t.verifiedVenues)} value={formatNumber(m.verifiedVenues || 0)} />
            <KpiCard label={t.needsPin} value={formatNumber(m.needsPin || 0)} tone={m.needsPin ? 'warning' : undefined} />
            <KpiCard label={d(t.instructors)} value={formatNumber(m.instructors || 0)} />
            <KpiCard label={d(t.claimedInstructors)} value={formatNumber(m.claimedInstructors || 0)} />
            <KpiCard label={t.pendingSubmissions} value={formatNumber(m.pendingSubmissions || 0)} />
            <KpiCard label={t.overdueSubmissions} value={formatNumber(m.overdueSubmissions || 0)} tone={m.overdueSubmissions ? 'danger' : undefined} />
          </div>
          <DailySeriesChart title={d(t.chartTitle)} series={series} />
        </>
      )}
    </section>
  );
}

/**
 * /admin/directory (docs/PHASE4_CONTRACT.md sections 3.7 to 3.9, resolved decision 7): venues (filters
 * including "needs a pin", create and edit with the manual pin, verify, close, archive, regeocode,
 * delete, CSV export), instructors (create, edit, link a member, venue links), the submission queue
 * with the diff view, pin editor, approve and reject (stale approvals reload the diff), and CSV import
 * with a dry run, for directory staff (SystemAdmin or DirectoryModerator); programs and settings for
 * SystemAdmin only.
 */
function DirectoryAdminView() {
  const { isSystemAdmin } = useAdminAuth();
  const { d } = useDirectoryTerms();
  const tabs = isSystemAdmin ? ADMIN_TABS : STAFF_TABS;
  const [tab, select] = useTab(tabs);
  const labels = Object.fromEntries(Object.entries(s.tabs).map(([k, label]) => [k, d(label)]));
  return (
    <div className="view">
      <PageHeader title={strings.admin.routes.directory.label} summary={strings.admin.routes.directory.summary} />
      <TabBar tabs={tabs} labels={labels} current={tab} onSelect={select} idPrefix="dir" label={s.tabsLabel} />
      <div role="tabpanel" id={'dir-panel-' + tab} aria-labelledby={'dir-tab-' + tab} className="tab-panel">
        {tab === 'venues' && <VenuesTab />}
        {tab === 'instructors' && <InstructorsTab />}
        {tab === 'submissions' && <SubmissionsTab />}
        {tab === 'import' && <ImportTab />}
        {tab === 'metrics' && <MetricsTab />}
        {tab === 'programs' && <ProgramsTab />}
        {tab === 'settings' && <SettingsTab />}
      </div>
    </div>
  );
}

export default DirectoryAdminView;

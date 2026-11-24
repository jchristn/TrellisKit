import { useCallback, useState } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import { SubmissionType, changedFields, directoryConfig, formatChangeValue, isValidPosition, validateSubmission } from '@trelliskit/shared';
import strings from '../../strings.js';
import { useAuth } from '../../context/AuthContext.jsx';
import { useProperty } from '../../context/PropertyContext.jsx';
import { useToast } from '../../context/ToastContext.jsx';
import useApiQuery from '../../hooks/useApiQuery.js';
import useCursorList from '../../hooks/useCursorList.js';
import useDocumentMeta from '../../hooks/useDocumentMeta.js';
import Icon from '../../components/Icon.jsx';
import PageHeader from '../../components/PageHeader.jsx';
import { Badge } from '../../components/Badges.jsx';
import { Banner, EmptyState, ErrorBanner, Loading } from '../../components/States.jsx';
import { useAccessPrompt } from '../../components/AccessPrompt.jsx';
import AttachmentList from '../../components/feed/AttachmentList.jsx';
import useMediaUploads from '../../components/feed/useMediaUploads.js';
import PinMap from '../../components/directory/PinMap.jsx';
import { InstructorFields, VenueFields, instructorFieldsFrom, instructorFormFrom, venueFieldsFrom, venueFormFrom } from '../../components/directory/DirectoryForms.jsx';
import { VenueRow } from './DirectoryView.jsx';
import { errorText } from '../../utils/engagement.js';
import { formatDateTime } from '../../utils/format.js';
import { appPaths, phase4Errors, useDirectoryTerms } from '../../utils/phase4.js';

const v = strings.venues;
const FORM_TYPES = [SubmissionType.NewVenue, SubmissionType.EditVenue, SubmissionType.NewInstructor, SubmissionType.EditInstructor];

function SubmissionForm({ type, target }) {
  const { client } = useAuth();
  const { property } = useProperty();
  const { d } = useDirectoryTerms();
  const toast = useToast();
  const accessPrompt = useAccessPrompt();
  const navigate = useNavigate();
  const isVenue = type === SubmissionType.NewVenue || type === SubmissionType.EditVenue;
  const isEdit = type === SubmissionType.EditVenue || type === SubmissionType.EditInstructor;
  const initial = isVenue ? venueFormFrom(target, { addressVisibility: 'Exact' }) : instructorFormFrom(target);
  const [form, setForm] = useState(initial);
  const initialPin = target && target.geo && target.geo.lat !== null && target.geo.lat !== undefined ? { lat: String(target.geo.lat), lng: String(target.geo.lng) } : { lat: '', lng: '' };
  const [pin, setPin] = useState(initialPin);
  const pinEmpty = pin.lat === '' && pin.lng === '';
  const pinValid = pinEmpty || isValidPosition(pin.lat, pin.lng);
  const [note, setNote] = useState('');
  const [problems, setProblems] = useState({});
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const [duplicates, setDuplicates] = useState(null);
  const uploads = useMediaUploads({ client, maxCount: isVenue ? 10 : 1, maxVideos: 0, allowMedia: true, allowVideo: false, onError: setError });
  const set = (changes) => setForm((f) => ({ ...f, ...changes }));
  const showRank = Boolean(directoryConfig(property).instructorRankFieldKey);

  const fieldsNow = () => {
    let fields = isVenue ? venueFieldsFrom(form) : instructorFieldsFrom(form, { withRank: showRank });
    if (isEdit) {
      const base = isVenue ? venueFieldsFrom(initial) : instructorFieldsFrom(initial, { withRank: showRank });
      fields = changedFields(base, fields);
    }
    // An optional pin (fields.geo), validated by the server and applied when staff approve.
    if (isVenue && !pinEmpty && pinValid && (!isEdit || pin.lat !== initialPin.lat || pin.lng !== initialPin.lng)) fields.geo = { lat: Number(pin.lat), lng: Number(pin.lng) };
    if (uploads.mediaIds.length) {
      if (isVenue) fields.photoMediaIds = uploads.mediaIds;
      else fields.photoMediaId = uploads.mediaIds[0];
    }
    return fields;
  };

  const send = async (confirmNotDuplicate) => {
    if (!pinValid) {
      setError(v.pinInvalid);
      return;
    }
    const fields = fieldsNow();
    const draft = { type, targetId: target ? target.id : null, fields, note };
    const found = validateSubmission(draft);
    setProblems(found);
    if (Object.keys(found).length) return;
    if (isEdit && !Object.keys(fields).length) {
      setError(v.noChanges);
      return;
    }
    if (uploads.busy) {
      setError(strings.composer.waitForUploads);
      return;
    }
    setBusy(true);
    setError('');
    try {
      const result = await client.createDirectorySubmission({ ...draft, note: note.trim() || undefined, confirmNotDuplicate: Boolean(confirmNotDuplicate) });
      if (result && result.status === 'Draft') {
        setDuplicates((result.possibleDuplicates && result.possibleDuplicates.objects) || []);
        setBusy(false);
        return;
      }
      toast.success(v.submitted);
      navigate(appPaths.mySubmissions);
    } catch (err) {
      if (!accessPrompt.handle(err)) {
        setProblems((p) => ({ ...p, ...((err && err.fieldErrors) || {}) }));
        setError(errorText(err, phase4Errors()));
      }
      setBusy(false);
    }
  };

  return (
    <form className="panel stack-form" onSubmit={(e) => { e.preventDefault(); send(false); }} noValidate aria-label={d(v.submitTitles[type])}>
      <p className="muted small">{v.submitIntro}{isEdit ? ' ' + v.editNote : ''}</p>
      <ErrorBanner message={error} />
      {duplicates && duplicates.length > 0 && (
        <section className="panel" aria-labelledby="dup-title">
          <h2 id="dup-title" className="section-title">{v.duplicatesTitle}</h2>
          <p className="muted small">{v.duplicatesBody}</p>
          <div className="p4-list">{duplicates.map((x) => (isVenue ? <VenueRow key={x.id} venue={x} /> : <Link key={x.id} to={appPaths.instructor(x.id)} className="p4-row">{x.displayName}</Link>))}</div>
          <div className="form-actions-start"><button type="button" className="button button-primary button-sm" disabled={busy} onClick={() => send(true)}>{v.notDuplicate}</button></div>
        </section>
      )}
      {isVenue ? <VenueFields form={form} set={set} problems={problems} idPrefix="submit" disabled={busy} /> : <InstructorFields form={form} set={set} problems={problems} idPrefix="submit" disabled={busy} showRank={showRank} />}
      {isVenue && (
        <fieldset className="fieldset">
          <legend>{v.pinLegend}</legend>
          <PinMap property={property} pin={pin} onChange={setPin} disabled={busy} />
          <div className="p4-pin">
            <div className="field"><label className="label-text" htmlFor="submit-pin-lat">{v.pinLat}</label><input id="submit-pin-lat" inputMode="decimal" value={pin.lat} onChange={(e) => setPin({ ...pin, lat: e.target.value })} disabled={busy} aria-invalid={!pinValid} /></div>
            <div className="field"><label className="label-text" htmlFor="submit-pin-lng">{v.pinLng}</label><input id="submit-pin-lng" inputMode="decimal" value={pin.lng} onChange={(e) => setPin({ ...pin, lng: e.target.value })} disabled={busy} aria-invalid={!pinValid} /></div>
          </div>
          {!pinValid && <span className="field-error" role="alert">{v.pinInvalid}</span>}
          <span className="muted small">{v.pinHint}</span>
        </fieldset>
      )}
      <AttachmentList uploads={uploads} allowMedia allowVideo={false} maxCount={isVenue ? 10 : 1} label={v.photos} disabled={busy} showAlt={isVenue} />
      <span className="muted small">{v.photoHint}</span>
      <div className="field">
        <label className="label-text" htmlFor="submit-note">{v.noteLabel}</label>
        <textarea id="submit-note" rows={3} maxLength={1000} value={note} onChange={(e) => setNote(e.target.value)} disabled={busy} />
        <span className="muted small">{v.noteHint}</span>
      </div>
      <div className="form-actions">
        <Link to={target ? (isVenue ? appPaths.venue(target.id) : appPaths.instructor(target.id)) : appPaths.directory} className="button button-secondary">{strings.common.cancel}</Link>
        <button type="submit" className="button button-primary" disabled={busy}>{busy ? v.submitting : v.submit}</button>
      </div>
    </form>
  );
}

/**
 * /app/directory/submit?type=&target= (sections 3.7, 3.8, resolved decision 7): add a venue or an
 * instructor, or suggest an edit (only changed fields are sent), with photos, a note for the
 * reviewers, and the duplicate check step. Addresses are shown exactly once staff approve, and the
 * form says so; nothing is published before approval.
 */
export function SubmissionView() {
  const { client } = useAuth();
  const { property } = useProperty();
  const { d } = useDirectoryTerms();
  const [params] = useSearchParams();
  const type = FORM_TYPES.includes(params.get('type')) ? params.get('type') : SubmissionType.NewVenue;
  const targetId = params.get('target');
  const needsTarget = type === SubmissionType.EditVenue || type === SubmissionType.EditInstructor;
  const fetcher = useCallback((signal) => (type === SubmissionType.EditVenue ? client.getVenue(targetId, signal) : client.getInstructor(targetId, signal)), [client, type, targetId]);
  const target = useApiQuery(fetcher, { enabled: needsTarget && Boolean(targetId) });
  useDocumentMeta(strings.member.metaTitle(d(v.submitTitles[type])));
  const enabled = directoryConfig(property).submissions;
  return (
    <div className="member-view">
      <Link to={appPaths.directory} className="back-link small"><Icon name="chevronLeft" size={14} /><span>{d(v.back)}</span></Link>
      <PageHeader title={d(v.submitTitles[type])} summary={target.data ? target.data.name || target.data.displayName : null} />
      {!enabled ? <Banner tone="info">{v.submissionsOff}</Banner> : needsTarget && !target.data ? (
        <>{target.loading && <Loading />}<ErrorBanner message={target.error ? errorText(target.error, phase4Errors()) : !targetId ? v.notFound : ''} /></>
      ) : <SubmissionForm key={type + ':' + (targetId || '')} type={type} target={needsTarget ? target.data : null} />}
    </div>
  );
}

/** /app/directory/submissions: the member's suggestions with status, changes, reviewer note, and withdraw. */
export function MySubmissionsView() {
  const { client } = useAuth();
  const { d } = useDirectoryTerms();
  const toast = useToast();
  useDocumentMeta(strings.member.metaTitle(v.submissionsTitle));
  const fetchPage = useCallback((cursor, signal) => client.enumerateMySubmissions({ continuationToken: cursor, maxResults: 25 }, signal), [client]);
  const list = useCursorList(fetchPage);
  const withdraw = async (s) => {
    try {
      const updated = await client.withdrawSubmission(s.id);
      list.update((items) => items.map((x) => (x.id === s.id ? { ...x, ...(updated || {}), status: 'Withdrawn' } : x)));
      toast.success(v.withdrawn);
    } catch (err) {
      toast.error(errorText(err, phase4Errors()));
    }
  };
  return (
    <div className="member-view">
      <Link to={appPaths.directory} className="back-link small"><Icon name="chevronLeft" size={14} /><span>{d(v.back)}</span></Link>
      <PageHeader title={v.submissionsTitle} />
      <ErrorBanner message={list.error ? errorText(list.error, phase4Errors(), d(v.loadError)) : ''} onRetry={list.refresh} />
      {!list.loaded && list.loading && <Loading />}
      {list.loaded && !list.items.length && !list.error && <EmptyState icon="pin" title={v.submissionsEmpty} />}
      <div className="p4-list">
        {list.items.map((s) => (
          <div key={s.id} className="p4-row" data-testid={'submission-' + s.id}>
            <span className="p4-row-main">
              <span className="p4-row-title">{d(v.submissionTypes[s.type] || s.type)}{s.target ? ': ' + s.target.name : (s.changes || []).filter((c) => c.field === 'name' || c.field === 'displayName').map((c) => ': ' + formatChangeValue(c.proposed)).join('')}</span>
              <span className="p4-card-meta">
                <Badge tone={s.status === 'Approved' ? 'success' : s.status === 'Rejected' ? 'danger' : 'neutral'}>{v.submissionStatus[s.status] || s.status}</Badge>
                <span>{formatDateTime(s.createdUtc)}</span>
              </span>
              {s.reviewNote && <span className="small">{v.reviewNote}: {s.reviewNote}</span>}
            </span>
            {s.status === 'Pending' && <button type="button" className="button button-secondary button-sm" onClick={() => withdraw(s)}>{v.withdraw}</button>}
          </div>
        ))}
      </div>
      {list.loaded && !list.done && <button type="button" className="button button-secondary feed-more" onClick={list.loadMore}>{v.loadMore}</button>}
    </div>
  );
}

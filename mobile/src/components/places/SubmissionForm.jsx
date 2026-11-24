import { SubmissionType, changedFields, directoryConfig, mobileMapProvider, programOptions, validateSubmission } from '@trelliskit/shared';
import { useRouter } from 'expo-router';
import { useState } from 'react';
import { View } from 'react-native';
import { useAuth } from '../../auth/AuthProvider';
import { describeFeatureError, useDirectoryStrings } from '../../content/phase4';
import { useResource } from '../../hooks/useResource';
import { useUploads } from '../../media/useUploads';
import { useProperty } from '../../property';
import { useTheme } from '../../theme';
import AppText from '../AppText';
import Button from '../Button';
import Card from '../Card';
import ChipGroup from '../ChipGroup';
import ErrorState from '../ErrorState';
import Loading from '../Loading';
import Notice from '../Notice';
import TextField from '../TextField';
import AttachButtons from '../feed/AttachButtons';
import UploadTray from '../feed/UploadTray';
import PinPicker from './PinPicker';
import VenueCard from './VenueCard';
import { CountrySelect } from '../profile/ProfileFieldInput';

const VENUE_TYPES = [SubmissionType.NewVenue, SubmissionType.EditVenue];
const INSTRUCTOR_TYPES = [SubmissionType.NewInstructor, SubmissionType.EditInstructor];

/** Form values for a venue or instructor (an edit starts from the current entry). */
export function initialValues(type, target) {
  if (INSTRUCTOR_TYPES.includes(type)) {
    const i = target || {};
    return { displayName: i.displayName || '', affiliation: i.affiliation || '', bio: i.bio || '', websiteUrl: i.websiteUrl || '' };
  }
  const v = target || {};
  const a = v.address || {};
  return {
    name: v.name || '',
    affiliation: v.affiliation || '',
    summary: v.summary || '',
    address: { line1: a.line1 || '', line2: a.line2 || '', locality: a.locality || '', region: a.region || '', postalCode: a.postalCode || '', countryCode: a.countryCode || '' },
    phone: v.phone || '',
    email: v.email || '',
    websiteUrl: v.websiteUrl || '',
    scheduleText: v.scheduleText || '',
    programs: (v.programs || []).map((p) => (p && p.key) || p)
  };
}

/**
 * The submission `fields` for a form: everything set for a new entry, and only what changed for an
 * edit (docs/PHASE4_CONTRACT.md section 3.7). Member-submitted addresses are exact (resolved
 * decision 7): no visibility is sent, and nothing is published before review.
 */
export function submissionFieldsFor(type, values, target, photoMediaIds = [], pin = null) {
  const clean = {};
  for (const [key, value] of Object.entries(values)) {
    if (key === 'address') {
      const address = {};
      for (const [k, v] of Object.entries(value)) if (String(v || '').trim()) address[k] = k === 'countryCode' ? String(v).trim().toUpperCase() : String(v).trim();
      if (Object.keys(address).length) clean.address = address;
    } else if (Array.isArray(value)) {
      if (value.length || target) clean[key] = value;
    } else if (String(value || '').trim()) clean[key] = String(value).trim();
    else if (target && target[key]) clean[key] = '';
  }
  if (VENUE_TYPES.includes(type) && photoMediaIds.length) clean.photoMediaIds = photoMediaIds;
  const pinned = VENUE_TYPES.includes(type) && pin && Number.isFinite(pin.lat) && Number.isFinite(pin.lng) ? { lat: pin.lat, lng: pin.lng } : null;
  if (type === SubmissionType.EditVenue || type === SubmissionType.EditInstructor) {
    const before = initialValues(type, target);
    const changed = changedFields(before, { ...before, ...clean }, Object.keys(clean));
    if (clean.address && JSON.stringify(clean.address) !== JSON.stringify(Object.fromEntries(Object.entries(before.address).filter(([, v]) => v)))) changed.address = clean.address;
    else delete changed.address;
    if (clean.photoMediaIds) changed.photoMediaIds = clean.photoMediaIds;
    if (pinned) changed.geo = pinned;
    return changed;
  }
  if (pinned) clean.geo = pinned;
  return clean;
}

const PROBLEM_TEXT = (t) => ({
  name: t.submit.nameRequired,
  displayName: t.submit.nameRequired,
  'address.countryCode': t.submit.countryRequired,
  'address.locality': t.submit.nameRequired,
  websiteUrl: t.submit.httpsRequired,
  note: t.submit.noteRequired
});

/**
 * Add a venue or instructor, suggest an edit, claim an instructor ("This is me"), or report a venue
 * closed (docs/PHASE4_CONTRACT.md sections 3.7 and 3.8): the form says nothing is published until a
 * moderator approves it, venue photos upload through the media pipeline, a possible duplicate
 * answer (`status: "Draft"`) shows the matches and asks before sending again with
 * `confirmNotDuplicate`, and the sent screen links to My submissions.
 */
export default function SubmissionForm({ type, targetId = null, testID = 'submission' }) {
  const theme = useTheme();
  const router = useRouter();
  const property = useProperty();
  const { api } = useAuth();
  const t = useDirectoryStrings();
  const isInstructor = INSTRUCTOR_TYPES.includes(type) || type === SubmissionType.ClaimInstructor;
  const needsTarget = Boolean(targetId);
  const target = useResource(() => (needsTarget ? (isInstructor ? api.getInstructor(targetId) : api.getVenue(targetId)) : Promise.resolve(null)), String(targetId));
  const [values, setValues] = useState(null);
  const [note, setNote] = useState('');
  const [errors, setErrors] = useState({});
  const [error, setError] = useState(null);
  const [busy, setBusy] = useState(false);
  const [duplicates, setDuplicates] = useState(null);
  const [sent, setSent] = useState(false);
  const [pin, setPin] = useState(null);
  const uploads = useUploads({ api });
  const config = directoryConfig(property);

  if (target.status === 'loading') return <Loading label={t.loading} testID={testID + '-loading'} />;
  if (target.status !== 'ready') return <ErrorState title={t.venueGoneTitle} message={describeFeatureError(target.error, 'places', t)} onRetry={target.reload} testID={testID + '-error'} />;
  const form = values || initialValues(type, target.data);
  const set = (key, value) => setValues({ ...form, [key]: value });
  const setAddress = (key, value) => setValues({ ...form, address: { ...form.address, [key]: value } });
  const editsFields = [SubmissionType.NewVenue, SubmissionType.EditVenue, SubmissionType.NewInstructor, SubmissionType.EditInstructor].includes(type);

  async function send(confirmNotDuplicate = false) {
    setError(null);
    const fields = editsFields ? submissionFieldsFor(type, form, target.data, uploads.mediaIds, pin) : {};
    const draft = { type, targetId, fields, note };
    const problems = validateSubmission(draft);
    if (editsFields && targetId && !Object.keys(fields).length) return setError(t.submit.nothingChanged);
    if (Object.keys(problems).length) {
      const text = PROBLEM_TEXT(t);
      setErrors(Object.fromEntries(Object.keys(problems).map((k) => [k, text[k] || t.submit.nameRequired])));
      return undefined;
    }
    if (uploads.busy) return undefined;
    setErrors({});
    setBusy(true);
    try {
      const result = await api.createDirectorySubmission({ type, targetId: targetId || null, fields, note: note.trim() || undefined, confirmNotDuplicate, labels: [], tags: {} });
      if (result && result.status === 'Draft') setDuplicates((result.possibleDuplicates && result.possibleDuplicates.objects) || []);
      else setSent(true);
    } catch (e) {
      setError(describeFeatureError(e, 'places', t));
    } finally {
      setBusy(false);
    }
  }

  if (sent) {
    return (
      <View style={{ gap: theme.spacing.md }} testID={testID + '-sent'}>
        <AppText variant="title" accessibilityRole="header">
          {t.submit.sentTitle}
        </AppText>
        <AppText>{t.submit.sentBody}</AppText>
        <Button label={t.mySubmissions} variant="secondary" onPress={() => router.replace('/directory/submissions')} testID={testID + '-to-submissions'} />
        <Button label={t.submit.done} onPress={() => router.back()} testID={testID + '-done'} />
      </View>
    );
  }

  const field = (key, label, props = {}) => <TextField label={label} value={form[key]} onChangeText={(v) => set(key, v)} error={errors[key]} testID={testID + '-' + key} {...props} />;
  const programs = programOptions(property);

  return (
    <View style={{ gap: theme.spacing.lg }} testID={testID}>
      <AppText variant="title" accessibilityRole="header">
        {t.submit[type]}
      </AppText>
      {target.data ? <AppText color="textMuted">{target.data.name || target.data.displayName}</AppText> : null}
      <Notice tone="info" testID={testID + '-review-note'}>
        {t.submit.reviewNote}
      </Notice>
      {type === SubmissionType.ClaimInstructor ? <AppText>{t.submit.claimNote}</AppText> : null}
      {type === SubmissionType.ReportClosed ? <AppText>{t.submit.closedNote}</AppText> : null}
      {targetId && editsFields ? <AppText color="textMuted">{t.submit.editNote}</AppText> : null}
      {VENUE_TYPES.includes(type) ? (
        <>
          {field('name', t.submit.name, { maxLength: 120 })}
          {field('affiliation', t.submit.affiliation, { maxLength: 120 })}
          {field('summary', t.submit.summary, { maxLength: 300, multiline: true })}
          <AppText color="textMuted" testID={testID + '-address-note'}>
            {t.submit.addressNote}
          </AppText>
          <TextField label={t.submit.line1} value={form.address.line1} onChangeText={(v) => setAddress('line1', v)} maxLength={200} testID={testID + '-line1'} />
          <TextField label={t.submit.locality} value={form.address.locality} onChangeText={(v) => setAddress('locality', v)} maxLength={100} error={errors['address.locality']} testID={testID + '-locality'} />
          <TextField label={t.submit.region} value={form.address.region} onChangeText={(v) => setAddress('region', v)} maxLength={100} testID={testID + '-region'} />
          <TextField label={t.submit.postalCode} value={form.address.postalCode} onChangeText={(v) => setAddress('postalCode', v)} maxLength={20} testID={testID + '-postal'} />
          <CountrySelect label={t.submit.countryCode} showLabel value={form.address.countryCode} onChange={(v) => setAddress('countryCode', v)} error={errors['address.countryCode']} testID={testID + '-country'} />
          <PinPicker provider={mobileMapProvider(property)} config={config.map} value={pin} onChange={setPin} terms={t} testID={testID + '-pin'} />
          {field('phone', t.submit.phone, { maxLength: 40, keyboardType: 'phone-pad' })}
          {field('email', t.submit.email, { maxLength: 254, keyboardType: 'email-address', autoCapitalize: 'none' })}
          {field('websiteUrl', t.submit.websiteUrl, { hint: t.submit.websiteHint, autoCapitalize: 'none', keyboardType: 'url' })}
          {field('scheduleText', t.submit.scheduleText, { maxLength: 500, multiline: true })}
          {programs.length ? <ChipGroup label={t.programs} options={programs.map((p) => ({ value: p.key, label: p.label }))} values={form.programs} onToggle={(v) => set('programs', form.programs.includes(v) ? form.programs.filter((x) => x !== v) : form.programs.concat(v))} testID={testID + '-programs'} /> : null}
          <View style={{ gap: theme.spacing.sm }}>
            <AppText variant="label">{t.submit.photos}</AppText>
            <UploadTray uploads={uploads} testID={testID + '-uploads'} />
            <AttachButtons permissions={{ media: true, video: false }} remaining={10 - uploads.items.length} max={10} videoAllowed={false} onPicked={uploads.add} testID={testID + '-attach'} />
          </View>
        </>
      ) : null}
      {INSTRUCTOR_TYPES.includes(type) ? (
        <>
          {field('displayName', t.submit.name, { maxLength: 120 })}
          {field('affiliation', t.submit.affiliation, { maxLength: 120 })}
          {field('bio', t.submit.bio, { multiline: true, maxLength: 5000 })}
          {field('websiteUrl', t.submit.websiteUrl, { hint: t.submit.websiteHint, autoCapitalize: 'none', keyboardType: 'url' })}
        </>
      ) : null}
      <TextField label={t.submit.note} value={note} onChangeText={setNote} multiline maxLength={1000} error={errors.note} testID={testID + '-note'} />
      {error ? (
        <AppText color="danger" accessibilityRole="alert" testID={testID + '-error-text'}>
          {error}
        </AppText>
      ) : null}
      {duplicates ? (
        <Card testID={testID + '-duplicates'}>
          <AppText variant="heading">{t.submit.duplicatesTitle}</AppText>
          <AppText color="textMuted">{t.submit.duplicatesBody}</AppText>
          {duplicates.map((venue) => (
            <VenueCard key={venue.id} venue={venue} terms={t} onPress={() => router.push('/venues/' + venue.id)} />
          ))}
          <Button label={t.submit.notDuplicate} onPress={() => send(true)} disabled={busy} testID={testID + '-confirm'} />
        </Card>
      ) : (
        <Button label={t.submit.send} onPress={() => send(false)} disabled={busy || (!config.submissions && type !== SubmissionType.ClaimInstructor) || (type === SubmissionType.ClaimInstructor && !config.claims)} testID={testID + '-send'} />
      )}
    </View>
  );
}

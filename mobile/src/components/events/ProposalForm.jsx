import { EventFormats, eventCategories, timeZoneChoices, validateEventForm } from '@trelliskit/shared';
import { useRouter } from 'expo-router';
import { useState } from 'react';
import { View } from 'react-native';
import { useAuth } from '../../auth/AuthProvider';
import { describeFeatureError, deviceTimeZone } from '../../content/phase4';
import { useProperty } from '../../property';
import strings from '../../strings';
import { useTheme } from '../../theme';
import AppText from '../AppText';
import Button from '../Button';
import ChipGroup from '../ChipGroup';
import Notice from '../Notice';
import SelectField from '../SelectField';
import TextField from '../TextField';
import { CountrySelect } from '../profile/ProfileFieldInput';

const t = strings.events.propose;

/** "2026-12-05 18:00" (or with a T) as the API's local wall time "2026-12-05T18:00", or null. */
export function toLocalInput(text) {
  const m = /^(\d{4}-\d{2}-\d{2})[ T](\d{2}):(\d{2})$/.exec(String(text || '').trim());
  return m ? m[1] + 'T' + m[2] + ':' + m[3] : null;
}

/** The `POST /events/proposals` body for a form (no hosts and no recurrence, resolved decision 10). */
export function proposalBody(form) {
  const body = {
    title: form.title.trim(),
    summary: form.summary.trim() || undefined,
    description: form.description.trim() || undefined,
    category: form.category || undefined,
    format: form.format,
    startLocal: toLocalInput(form.start),
    endLocal: toLocalInput(form.end),
    timeZone: form.timeZone.trim(),
    isAllDay: false,
    capacity: form.capacity.trim() ? Number(form.capacity.trim()) : null,
    labels: [],
    tags: {}
  };
  if (form.format !== 'Online') {
    body.location = { name: form.locationName.trim() || undefined, address: form.locality.trim() || form.line1.trim() ? { line1: form.line1.trim() || undefined, locality: form.locality.trim() || undefined, countryCode: form.countryCode.trim().toUpperCase() || undefined } : undefined };
  }
  if (form.format !== 'InPerson') body.online = { platformLabel: form.platformLabel.trim() || undefined, joinUrl: form.joinUrl.trim() || undefined };
  return body;
}

const EMPTY = { title: '', summary: '', description: '', category: null, format: 'InPerson', start: '', end: '', timeZone: '', locationName: '', line1: '', locality: '', countryCode: '', platformLabel: '', joinUrl: '', capacity: '' };

/**
 * "Propose an event" (resolved decision 10): a member suggests an event that event staff review
 * before it is published. The form says so; there are no hosts or repeat rules.
 */
export default function ProposalForm({ testID = 'proposal' }) {
  const theme = useTheme();
  const router = useRouter();
  const property = useProperty();
  const { api } = useAuth();
  const [form, setForm] = useState(() => ({ ...EMPTY, timeZone: deviceTimeZone() }));
  const [errors, setErrors] = useState({});
  const [error, setError] = useState(null);
  const [busy, setBusy] = useState(false);
  const [sent, setSent] = useState(false);
  const set = (key) => (value) => setForm((f) => ({ ...f, [key]: value }));
  const categories = eventCategories(property);

  async function send() {
    setError(null);
    const body = proposalBody(form);
    const problems = validateEventForm({ ...body, location: body.location || {}, online: body.online || {} });
    const next = {};
    if (problems.title) next.title = t.titleRequired;
    if (!body.startLocal) next.start = t.dateRequired;
    if (!body.endLocal) next.end = t.dateRequired;
    else if (problems.endLocal) next.end = t.endAfterStart;
    if (problems.location) next.locationName = t.locationRequired;
    if (problems.online || problems['online.joinUrl']) next.joinUrl = t.joinRequired;
    setErrors(next);
    if (Object.keys(next).length) return;
    setBusy(true);
    try {
      await api.proposeEvent(body);
      setSent(true);
    } catch (e) {
      setError(describeFeatureError(e, 'events'));
    } finally {
      setBusy(false);
    }
  }

  if (sent) {
    return (
      <View style={{ gap: theme.spacing.md }} testID={testID + '-sent'}>
        <AppText variant="title" accessibilityRole="header">
          {t.sentTitle}
        </AppText>
        <AppText>{t.sentBody}</AppText>
        <Button label={strings.events.proposals.open} variant="secondary" onPress={() => router.replace('/events/proposals')} testID={testID + '-to-proposals'} />
        <Button label={t.done} onPress={() => router.back()} testID={testID + '-done'} />
      </View>
    );
  }

  return (
    <View style={{ gap: theme.spacing.lg }} testID={testID}>
      <Notice tone="info" testID={testID + '-review-note'}>
        {t.reviewNote}
      </Notice>
      <TextField label={t.eventTitle} value={form.title} onChangeText={set('title')} maxLength={140} error={errors.title} testID={testID + '-title'} />
      <TextField label={t.summary} value={form.summary} onChangeText={set('summary')} maxLength={300} testID={testID + '-summary'} />
      <TextField label={t.description} value={form.description} onChangeText={set('description')} multiline maxLength={10000} testID={testID + '-description'} />
      {categories.length ? <ChipGroup label={t.category} options={categories.map((c) => ({ value: c.key, label: c.label, color: c.color || null }))} value={form.category} onToggle={(v) => set('category')(form.category === v ? null : v)} testID={testID + '-category'} /> : null}
      <ChipGroup label={t.format} options={EventFormats.map((value) => ({ value, label: strings.events.formats[value] }))} value={form.format} onToggle={set('format')} testID={testID + '-format'} />
      <TextField label={t.start} hint={t.dateHint} value={form.start} onChangeText={set('start')} error={errors.start} testID={testID + '-start'} />
      <TextField label={t.end} hint={t.dateHint} value={form.end} onChangeText={set('end')} error={errors.end} testID={testID + '-end'} />
      <SelectField label={t.timeZone} options={timeZoneChoices(form.timeZone, deviceTimeZone()).map((z) => ({ value: z, label: z.replace(/_/g, ' ') }))} value={form.timeZone} onChange={set('timeZone')} required searchPlaceholder={strings.profileFields.timeZoneSearch} testID={testID + '-zone'} />
      {form.format !== 'Online' ? (
        <>
          <TextField label={t.locationName} value={form.locationName} onChangeText={set('locationName')} maxLength={120} error={errors.locationName} testID={testID + '-place'} />
          <TextField label={t.line1} value={form.line1} onChangeText={set('line1')} maxLength={200} testID={testID + '-line1'} />
          <TextField label={t.locality} value={form.locality} onChangeText={set('locality')} maxLength={100} testID={testID + '-locality'} />
          <CountrySelect label={t.countryCode} showLabel value={form.countryCode} onChange={set('countryCode')} testID={testID + '-country'} />
        </>
      ) : null}
      {form.format !== 'InPerson' ? (
        <>
          <TextField label={t.platformLabel} value={form.platformLabel} onChangeText={set('platformLabel')} maxLength={60} testID={testID + '-platform'} />
          <TextField label={t.joinUrl} value={form.joinUrl} onChangeText={set('joinUrl')} autoCapitalize="none" keyboardType="url" error={errors.joinUrl} testID={testID + '-join'} />
        </>
      ) : null}
      <TextField label={t.capacity} hint={t.capacityHint} value={form.capacity} onChangeText={set('capacity')} keyboardType="number-pad" testID={testID + '-capacity'} />
      {error ? (
        <AppText color="danger" accessibilityRole="alert" testID={testID + '-error'}>
          {error}
        </AppText>
      ) : null}
      <Button label={t.send} onPress={send} disabled={busy} testID={testID + '-send'} />
    </View>
  );
}

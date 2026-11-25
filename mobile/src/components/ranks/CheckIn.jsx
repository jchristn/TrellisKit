import { checkInLink, isRankStaff, localIsoDate, ranksFeatureEnabled, secondsUntil, venueSessionTargetId } from '@trelliskit/shared';
import { useCallback, useEffect, useRef, useState } from 'react';
import { View } from 'react-native';
import { useAuth } from '../../auth/AuthProvider';
import { useCursorList } from '../../hooks/useCursorList';
import { useProperty } from '../../property';
import strings from '../../strings';
import { useTheme } from '../../theme';
import AppText from '../AppText';
import Button from '../Button';
import Loading from '../Loading';
import Sheet from '../Sheet';
import TextField from '../TextField';
import QrCodeView from './QrCodeView';
import { memberName } from './rankHooks';

/**
 * Attendance check-in for events and venue schedule sessions, without a camera module: members
 * enter the six-digit code, or scan the staff QR code with the system camera, which opens the
 * `/check-in` route.
 *
 * - `CheckInControls` props: `targetType` ('Event' | 'VenueSession'), `targetId` (event id or
 *   `{venueId}:{sessionIndex}`), `occurrenceDate` (yyyy-MM-dd; required for sessions), `title`,
 *   `testID`. Renders nothing unless `features.ranks` is on and a member is signed in; rank staff
 *   also get "Show check-in code".
 * - `CheckInForm` props: `api`, `target`, `onDone(attendance, already)`, `testID`.
 * - `StaffCheckIn` props: `api`, `target`, `property`, `testID` (QR, code, countdown, roster).
 */

/** The error text of a failed check-in. */
export function checkInErrorText(err) {
  const t = strings.ranks.checkIn.errors;
  if (err && err.status === 404 && err.code !== 'NotAvailable') return t.NotFound;
  return (err && err.code && t[err.code]) || (err && err.message) || strings.ranks.errors.generic;
}

export function CheckInForm({ api, target, onDone, testID = 'check-in-form' }) {
  const theme = useTheme();
  const t = strings.ranks.checkIn;
  const [code, setCode] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const submit = async () => {
    if (busy) return;
    if (!/^\d{6}$/.test(code)) { setError(t.codeInvalid); return; }
    setBusy(true);
    setError('');
    try {
      const body = { targetType: target.targetType, targetId: target.targetId, code, method: 'SelfWithCode' };
      if (target.occurrenceDate) body.occurrenceDate = target.occurrenceDate;
      const result = await api.checkIn(body);
      onDone(result, false);
    } catch (err) {
      if (err && err.code === 'AlreadyCheckedIn') onDone(null, true);
      else setError(checkInErrorText(err));
    } finally {
      setBusy(false);
    }
  };
  return (
    <View style={{ gap: theme.spacing.md }} testID={testID}>
      <AppText color="textMuted">{t.intro}</AppText>
      <TextField label={t.code} value={code} onChangeText={(v) => setCode(String(v).replace(/\D/g, '').slice(0, 6))} error={error || undefined} keyboardType="number-pad" textContentType="oneTimeCode" maxLength={6} testID={testID + '-code'} />
      <Button label={busy ? t.submitting : t.submit} onPress={submit} disabled={busy} testID={testID + '-submit'} />
    </View>
  );
}

/** The rotating check-in code of a target; fetches the next code when the current one expires. */
export function useCheckInCode(api, target, enabled = true) {
  const [state, setState] = useState({ key: '', data: null, error: null });
  const [now, setNow] = useState(() => Date.now());
  const [nonce, setNonce] = useState(0);
  const key = target ? [target.targetType, target.targetId, target.occurrenceDate || ''].join('|') : '';
  const targetRef = useRef(target);
  useEffect(() => { targetRef.current = target; });
  useEffect(() => {
    if (!enabled || !key) return undefined;
    let live = true;
    let timer = null;
    const load = () => {
      api.getCheckInCode(targetRef.current).then(
        (data) => {
          if (!live) return;
          setState({ key, data, error: null });
          const wait = Date.parse((data && data.expiresUtc) || '') - Date.now();
          timer = setTimeout(load, Number.isFinite(wait) ? Math.max(1000, wait + 250) : 60000);
        },
        (error) => { if (live) setState((s) => ({ ...s, key, error })); }
      );
    };
    load();
    return () => { live = false; clearTimeout(timer); };
  }, [api, key, enabled, nonce]);
  useEffect(() => {
    if (!enabled) return undefined;
    const timer = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(timer);
  }, [enabled]);
  const current = state.key === key;
  const data = current ? state.data : null;
  const error = current ? state.error : null;
  const reload = useCallback(() => setNonce((n) => n + 1), []);
  return { data, error, loading: enabled && !data && !error, remaining: data ? secondsUntil(data.expiresUtc, now) : 0, reload };
}

export function CheckInRoster({ api, target, refreshKey = '', testID = 'check-in-roster' }) {
  const theme = useTheme();
  const t = strings.ranks.checkIn;
  const filters = JSON.stringify({ targetType: target.targetType, targetId: target.targetId, ...(target.occurrenceDate ? { occurrenceDate: target.occurrenceDate } : {}) });
  const [total, setTotal] = useState(null);
  const fetchPage = useCallback((cursor) => api.enumerateAttendance({ filters: JSON.parse(filters), maxResults: 50, continuationToken: cursor }), [api, filters]);
  const onPage = useCallback((page, { first }) => { if (first && page && Number.isInteger(page.totalRecords)) setTotal(page.totalRecords); }, []);
  const list = useCursorList(fetchPage, { key: filters + '|' + refreshKey, onPage });
  return (
    <View style={{ gap: theme.spacing.sm }} testID={testID}>
      <AppText variant="label" accessibilityRole="header">{t.rosterTitle + (total !== null ? ' · ' + t.rosterCount(total) : '')}</AppText>
      {list.status === 'loading' ? <Loading /> : null}
      {list.status === 'ready' && !list.items.length ? <AppText color="textMuted">{t.rosterEmpty}</AppText> : null}
      {list.items.map((row) => (
        <AppText key={row.id} testID={testID + '-' + row.id}>{(row.user ? memberName(row.user) : row.userId) + ' · ' + (t.methods[row.method] || row.method)}</AppText>
      ))}
      {list.hasMore && !list.loadingMore ? <Button label={strings.ranks.curricula.more} variant="secondary" onPress={list.loadMore} /> : null}
    </View>
  );
}

export function StaffCheckIn({ api, target, property, testID = 'staff-check-in' }) {
  const theme = useTheme();
  const t = strings.ranks.checkIn;
  const code = useCheckInCode(api, target);
  const data = code.data;
  const link = data ? checkInLink(property, { ...target, ...data }) : null;
  return (
    <View style={{ gap: theme.spacing.md, alignItems: 'stretch' }} testID={testID}>
      <AppText color="textMuted">{t.staffIntro}</AppText>
      {code.error ? <AppText accessibilityRole="alert" color="danger">{checkInErrorText(code.error) || t.codeError}</AppText> : null}
      {!data && code.loading ? <Loading label={t.refreshing} /> : null}
      {data ? (
        <View style={{ alignItems: 'center', gap: theme.spacing.sm }}>
          <QrCodeView value={link || data.qrPayload} label={t.qrLabel} size={220} />
          <AppText variant="title" accessibilityLabel={t.code + ' ' + data.code.split('').join(' ')} style={{ letterSpacing: 6 }} testID={testID + '-code'}>{data.code}</AppText>
          <AppText color="textMuted" accessibilityLiveRegion="polite">{code.remaining > 0 ? t.countdown(code.remaining) : t.refreshing}</AppText>
        </View>
      ) : null}
      <CheckInRoster api={api} target={target} refreshKey={data ? data.code : ''} testID={testID + '-roster'} />
    </View>
  );
}

export function CheckInControls({ targetType, targetId, occurrenceDate = null, title = '', testID = 'check-in' }) {
  const theme = useTheme();
  const { api, user } = useAuth();
  const property = useProperty();
  const [open, setOpen] = useState(null);
  const [done, setDone] = useState('');
  if (!ranksFeatureEnabled(property) || !user || !targetId) return null;
  const t = strings.ranks.checkIn;
  const target = { targetType, targetId, ...(occurrenceDate ? { occurrenceDate } : {}) };
  return (
    <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: theme.spacing.sm }} testID={testID}>
      <Button label={t.button} variant="secondary" accessibilityLabel={title ? t.buttonLabel(title) : t.button} onPress={() => { setDone(''); setOpen('member'); }} testID={testID + '-open'} />
      {isRankStaff(user) ? <Button label={t.staffButton} variant="ghost" onPress={() => setOpen('staff')} testID={testID + '-staff'} /> : null}
      <Sheet visible={open === 'member'} title={t.title} onClose={() => setOpen(null)} testID={testID + '-sheet'}>
        {done ? <AppText accessibilityRole="alert" testID={testID + '-done'}>{done}</AppText> : (
          <CheckInForm api={api} target={target} testID={testID + '-form'} onDone={(_r, already) => setDone(already ? t.errors.AlreadyCheckedIn : title ? t.doneFor(title) : t.done)} />
        )}
      </Sheet>
      <Sheet visible={open === 'staff'} title={t.staffTitle} onClose={() => setOpen(null)} testID={testID + '-staff-sheet'}>
        <StaffCheckIn api={api} target={target} property={property} testID={testID + '-staff-panel'} />
      </Sheet>
    </View>
  );
}

/** Monday-first weekday index (0 = Monday), matching venue schedule `dayOfWeek`. */
export function mondayFirstDay(date = new Date()) {
  return (date.getDay() + 6) % 7;
}

/**
 * A `VenueSchedule` `sessionAction` that adds check-in controls to today's sessions (the device's
 * date; the server checks the venue's time zone and the check-in window).
 */
export function venueSessionCheckIn(venue, now = new Date()) {
  if (!venue || !venue.id) return null;
  const today = mondayFirstDay(now);
  const date = localIsoDate(now);
  return function SessionCheckIn(entry, index) {
    if (index < 0 || entry.dayOfWeek !== today) return null;
    return <CheckInControls targetType="VenueSession" targetId={venueSessionTargetId(venue.id, index)} occurrenceDate={date} title={entry.name} testID={'venue-session-check-in-' + index} />;
  };
}

export default CheckInControls;

import { parseCheckInPayload } from '@trelliskit/shared';
import { useRouter } from 'expo-router';
import { useEffect, useRef, useState } from 'react';
import { View } from 'react-native';
import { useAuth } from '../../auth/AuthProvider';
import strings from '../../strings';
import { useTheme } from '../../theme';
import AppText from '../AppText';
import Button from '../Button';
import ErrorState from '../ErrorState';
import Loading from '../Loading';
import Screen from '../Screen';
import { CheckInForm, checkInErrorText } from './CheckIn';
import { useRanksFeature } from './rankHooks';

/**
 * The `/check-in?t={targetType}&id={targetId}&d={occurrenceDate}&c={code}` screen a scanned check-in
 * QR code opens (the system camera opens the universal link, which `linkTargets` maps here). With a code it checks the member in at once; without one it asks for the code.
 * Props: `params` (the route's search parameters).
 */
export default function CheckInScreen({ params }) {
  const theme = useTheme();
  const router = useRouter();
  const { api } = useAuth();
  const { enabled } = useRanksFeature();
  const t = strings.ranks.checkIn;
  const body = parseCheckInPayload(params || {});
  const [state, setState] = useState(() => ({ status: body && body.code ? 'working' : 'form', message: '' }));
  const started = useRef(false);
  const bodyKey = JSON.stringify(body);
  useEffect(() => {
    const parsed = JSON.parse(bodyKey);
    if (!enabled || !parsed || !parsed.code || started.current) return;
    started.current = true;
    api.checkIn(parsed).then(
      () => setState({ status: 'done', message: t.done }),
      (err) => setState(err && err.code === 'AlreadyCheckedIn' ? { status: 'done', message: t.errors.AlreadyCheckedIn } : { status: 'form', message: checkInErrorText(err) })
    );
  }, [api, bodyKey, enabled, t]);
  if (!enabled) return <Screen testID="check-in-screen"><ErrorState title={t.errors.NotAvailable} /></Screen>;
  return (
    <Screen testID="check-in-screen">
      <View style={{ gap: theme.spacing.lg }}>
        <AppText variant="title" accessibilityRole="header">{t.title}</AppText>
        {!body ? <AppText accessibilityRole="alert" testID="check-in-invalid">{t.linkInvalid}</AppText> : null}
        {body && state.status === 'working' ? <Loading label={t.working} /> : null}
        {body && state.status === 'done' ? <AppText accessibilityRole="alert" testID="check-in-done">{state.message}</AppText> : null}
        {body && state.status === 'form' ? (
          <>
            {state.message ? <AppText accessibilityRole="alert" color="danger" testID="check-in-error">{state.message}</AppText> : null}
            <CheckInForm api={api} target={body} onDone={(_r, already) => setState({ status: 'done', message: already ? t.errors.AlreadyCheckedIn : t.done })} />
          </>
        ) : null}
        {state.status === 'done' ? <Button label={strings.ranks.title} variant="secondary" onPress={() => router.replace('/rank')} testID="check-in-my-rank" /> : null}
      </View>
    </Screen>
  );
}

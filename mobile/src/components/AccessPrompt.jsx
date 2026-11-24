import Ionicons from '@expo/vector-icons/Ionicons';
import { AccessRequirement, accessAction, accessDenial } from '@trelliskit/shared';
import { useRouter } from 'expo-router';
import { createContext, useCallback, useContext, useMemo, useState } from 'react';
import { View } from 'react-native';
import { useProperty } from '../property';
import strings from '../strings';
import { useTheme } from '../theme';
import AppText from './AppText';
import Button from './Button';
import Card from './Card';
import Sheet from './Sheet';

const s = strings.access;

/** Display name of an access feature key (for example "Forums"), or null. */
export function featureName(feature) {
  return (feature && s.features[feature]) || null;
}

function promptText(property, required, feature) {
  const name = featureName(feature);
  const action = accessAction(property, required);
  if (action === 'signIn') return { title: s.signInTitle, body: s.signInBody(name) };
  if (action === 'upgrade') return { title: s.upgradeTitle, body: s.upgradeBody(name) };
  return { title: s.noPurchaseTitle, body: s.noPurchaseBody(name) };
}

/**
 * The call to action for an access requirement (ACCESS_MODELS_CONTRACT.md sections 8.5 to 8.8 and
 * 8.11): `SignIn` opens sign in; `Membership` opens the native in-app purchase screen when the
 * property sells memberships (never web pricing or sign-up codes); with purchases off nothing is
 * offered.
 * @param {{ required: string, onNavigate?: () => void, onDismiss?: () => void, testID?: string }} props
 */
export function AccessActions({ required, onNavigate, onDismiss, testID = 'access' }) {
  const router = useRouter();
  const property = useProperty();
  const theme = useTheme();
  const action = accessAction(property, required);
  const go = (href) => {
    if (onNavigate) onNavigate();
    router.push(href);
  };
  return (
    <View style={{ gap: theme.spacing.sm }}>
      {action === 'signIn' ? <Button label={s.signInCta} accessibilityHint={s.signInHint} onPress={() => go('/sign-in')} testID={testID + '-sign-in'} /> : null}
      {action === 'upgrade' ? <Button label={s.upgradeCta} accessibilityHint={s.upgradeHint} onPress={() => go('/subscribe')} testID={testID + '-upgrade'} /> : null}
      {onDismiss ? <Button label={s.notNow} variant="ghost" onPress={onDismiss} testID={testID + '-dismiss'} /> : null}
    </View>
  );
}

/**
 * Inline prompt for a locked screen, composer, or denied load.
 * @param {{ required: string, feature?: string|null, title?: string, body?: string, testID?: string }} props
 */
export function AccessPromptCard({ required, feature = null, title, body, testID = 'access-prompt' }) {
  const property = useProperty();
  const theme = useTheme();
  const text = promptText(property, required, feature);
  return (
    <Card testID={testID} accessibilityLabel={(title || text.title) + '. ' + (body || text.body)}>
      <View style={{ flexDirection: 'row', gap: theme.spacing.sm, alignItems: 'center' }}>
        <Ionicons name={required === AccessRequirement.SignIn ? 'person-circle-outline' : 'lock-closed'} size={22} color={theme.colors.textMuted} />
        <AppText variant="heading" accessibilityRole="header" style={{ flex: 1 }}>
          {title || text.title}
        </AppText>
      </View>
      <AppText color="textMuted">{body || text.body}</AppText>
      <AccessActions required={required} testID={testID} />
    </Card>
  );
}

/** A small "Members only" lock label. */
export function LockBadge({ testID }) {
  const theme = useTheme();
  return (
    <View accessible accessibilityLabel={s.lockBadge} style={{ flexDirection: 'row', alignItems: 'center', gap: 4 }} testID={testID}>
      <Ionicons name="lock-closed" size={12} color={theme.colors.textMuted} />
      <AppText variant="caption" color="textMuted" style={{ fontWeight: '700' }}>
        {s.lockBadge}
      </AppText>
    </View>
  );
}

const AccessPromptContext = createContext(null);

/**
 * The one access-denial handler for the app. `handle(error)` turns a `402` (or a `401` with
 * `required: "SignIn"`) into the upgrade or sign-in sheet and returns true; other errors return
 * false so the caller shows its own message. `show(denial)` opens the sheet directly.
 */
export function AccessPromptProvider({ children }) {
  const property = useProperty();
  const [denial, setDenial] = useState(null);
  const show = useCallback((next) => setDenial(next ? { required: next.required || AccessRequirement.Membership, feature: next.feature || null } : null), []);
  const handle = useCallback((error) => {
    const found = accessDenial(error);
    if (!found) return false;
    setDenial(found);
    return true;
  }, []);
  const value = useMemo(() => ({ show, handle }), [show, handle]);
  const close = () => setDenial(null);
  const text = denial ? promptText(property, denial.required, denial.feature) : null;
  return (
    <AccessPromptContext.Provider value={value}>
      {children}
      <Sheet visible={Boolean(denial)} title={text ? text.title : ''} onClose={close} scroll={false} testID="access-sheet">
        {denial ? (
          <View style={{ gap: 12 }}>
            <AppText>{text.body}</AppText>
            <AccessActions required={denial.required} onNavigate={close} onDismiss={close} testID="access-sheet" />
          </View>
        ) : null}
      </Sheet>
    </AccessPromptContext.Provider>
  );
}

const FALLBACK = Object.freeze({ show: () => {}, handle: () => false });

/** `{ show, handle }` from the nearest AccessPromptProvider (outside one nothing is handled). */
export function useAccessPrompt() {
  return useContext(AccessPromptContext) || FALLBACK;
}

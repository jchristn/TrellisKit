import { hasServerCapability } from '@trelliskit/shared';
import { View } from 'react-native';
import { useProperty } from '../property';
import strings from '../strings';
import { useTheme } from '../theme';
import AppText from './AppText';
import Card from './Card';
import Screen from './Screen';

/**
 * Neutral "Coming soon" body for a section that is not available yet: a heading, a summary, a
 * "Coming soon" badge, and what the section will offer.
 * @param {object} props
 * @param {string} props.title
 * @param {string} props.summary
 * @param {string[]} [props.items] What the section will offer.
 * @param {React.ReactNode} [props.children] Extra content rendered after the list.
 */
export default function ComingSoon({ title, summary, items = [], children }) {
  const theme = useTheme();
  return (
    <View style={{ gap: theme.spacing.lg }} testID="coming-soon">
      <View style={{ gap: theme.spacing.xs }}>
        <AppText variant="title" accessibilityRole="header">
          {title}
        </AppText>
        <AppText variant="caption" color="primary" style={{ fontWeight: '600' }}>
          {strings.common.comingSoon}
        </AppText>
        <AppText color="textMuted">{summary}</AppText>
      </View>
      {items.length ? (
        <Card accessible accessibilityLabel={strings.common.comingSoon + '. ' + items.join('. ')}>
          {items.map((item) => (
            <AppText key={item}>{item}</AppText>
          ))}
        </Card>
      ) : null}
      <AppText variant="caption" color="textMuted">
        {strings.common.comingSoonNote}
      </AppText>
      {children}
    </View>
  );
}

/**
 * Render `children` only when the server reports `capability` in GET /api/v1/property
 * `features`; otherwise a full "Coming soon" screen, so nothing below it calls the missing API.
 * @param {object} props
 * @param {string} props.capability A ServerCapability value.
 * @param {string} props.title
 * @param {string} props.summary
 * @param {string[]} [props.items]
 * @param {string} [props.testID] Test id of the coming-soon screen.
 * @param {('top'|'bottom'|'left'|'right')[]} [props.edges]
 */
export function CapabilityGate({ capability, title, summary, items, testID, edges, children }) {
  const property = useProperty();
  if (hasServerCapability(property, capability)) return children;
  return (
    <Screen testID={testID} edges={edges}>
      <ComingSoon title={title} summary={summary} items={items} />
    </Screen>
  );
}

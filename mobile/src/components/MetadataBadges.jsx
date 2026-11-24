import { View } from 'react-native';
import strings from '../strings';
import { useTheme } from '../theme';
import AppText from './AppText';

/**
 * An item's labels and tags as small badges (feed posts, comments): labels as their text, tags as
 * `key: value` (a key alone when the value is empty). At most `max` badges; the rest collapse into
 * one "+N" badge.
 * @param {{ labels?: string[], tags?: Record<string, string>, max?: number, testID?: string }} props
 */
export default function MetadataBadges({ labels = [], tags = {}, max = 6, testID }) {
  const theme = useTheme();
  const items = [
    ...(Array.isArray(labels) ? labels : []).filter(Boolean).map((l) => ({ key: 'l:' + l, text: l, a11y: strings.metadataBadges.label(l) })),
    ...Object.entries(tags && typeof tags === 'object' ? tags : {}).map(([k, v]) => {
      const text = v ? k + ': ' + v : k;
      return { key: 't:' + k, text, a11y: strings.metadataBadges.tag(text) };
    })
  ];
  if (!items.length) return null;
  const shown = items.slice(0, max);
  const rest = items.length - shown.length;
  const badge = (key, text, a11y) => (
    <View key={key} accessible accessibilityLabel={a11y} style={{ paddingHorizontal: theme.spacing.sm, paddingVertical: 2, borderRadius: theme.radii.pill, borderWidth: 1, borderColor: theme.colors.border, backgroundColor: theme.colors.surfaceRaised }}>
      <AppText variant="caption" color="textMuted" style={{ fontWeight: '600' }}>
        {text}
      </AppText>
    </View>
  );
  return (
    <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: theme.spacing.xs }} testID={testID}>
      {shown.map((item) => badge(item.key, item.text, item.a11y))}
      {rest > 0 ? badge('more', strings.metadataBadges.more(rest), strings.metadataBadges.moreLabel(rest)) : null}
    </View>
  );
}

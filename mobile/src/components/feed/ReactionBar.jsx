import { formatCount } from '@trelliskit/shared';
import { useState } from 'react';
import { Pressable, View } from 'react-native';
import { useAuth } from '../../auth/AuthProvider';
import { reactionSet, visibleReactions } from '../../content/reactions';
import { useProperty } from '../../property';
import strings from '../../strings';
import { useTheme } from '../../theme';
import AppText from '../AppText';
import IconButton from '../IconButton';
import Sheet from '../Sheet';
import { useReactionToggle } from './useReactionToggle';
import WhoReactedSheet from './WhoReactedSheet';

/**
 * Reactions under a post or comment: a chip per reaction with a count (in the property's order,
 * highlighted when it is yours), a picker with the property's full set, and the who-reacted
 * sheet (long press a chip, or tap the total). Toggling is optimistic with rollback.
 * @param {object} props
 * @param {{ type: string, id: string }} props.target
 * @param {object} props.fields Reaction fields from the payload.
 * @param {(fields: object) => void} props.onChange
 * @param {boolean} [props.canReact] From the item's viewer.
 * @param {React.ReactNode} [props.trailing] Extra controls on the same row (comment count, reply).
 */
export default function ReactionBar({ target, fields, onChange, canReact = true, trailing, testID }) {
  const theme = useTheme();
  const property = useProperty();
  const { api } = useAuth();
  const set = reactionSet(property);
  const [pickerOpen, setPickerOpen] = useState(false);
  const [whoKey, setWhoKey] = useState(undefined);
  const { toggle, error } = useReactionToggle({ api, target, fields, onChange });
  const chips = visibleReactions(fields, set);
  const total = (fields && fields.reactionTotal) || 0;
  const prefix = testID || 'reactions';

  return (
    <View style={{ gap: theme.spacing.xs }}>
      <View style={{ flexDirection: 'row', flexWrap: 'wrap', alignItems: 'center', gap: theme.spacing.xs }} testID={prefix}>
        {chips.map((chip) => (
          <Pressable
            key={chip.key}
            testID={prefix + '-chip-' + chip.key}
            onPress={canReact ? () => toggle(chip.key) : () => setWhoKey(chip.key)}
            onLongPress={() => setWhoKey(chip.key)}
            accessibilityRole="button"
            accessibilityLabel={strings.reactions.chip(chip.label, chip.count, chip.mine)}
            accessibilityHint={strings.reactions.chipHint}
            accessibilityState={{ selected: chip.mine }}
            hitSlop={4}
            style={({ pressed }) => ({
              minHeight: 36,
              flexDirection: 'row',
              alignItems: 'center',
              gap: theme.spacing.xs,
              paddingHorizontal: theme.spacing.md,
              borderRadius: theme.radii.pill,
              borderWidth: 1,
              borderColor: chip.mine ? theme.colors.primary : theme.colors.border,
              backgroundColor: chip.mine ? theme.colors.accentSoft : theme.colors.surface,
              opacity: pressed ? 0.8 : 1
            })}
          >
            <AppText importantForAccessibility="no">{chip.emoji}</AppText>
            <AppText variant="caption" style={{ fontWeight: chip.mine ? '700' : '400' }}>
              {formatCount(chip.count)}
            </AppText>
          </Pressable>
        ))}
        {canReact && set.items.length ? (
          <IconButton icon="happy-outline" label={strings.reactions.add} accessibilityHint={strings.reactions.addHint} onPress={() => setPickerOpen(true)} color="textMuted" testID={prefix + '-add'} />
        ) : null}
        {total > 0 ? (
          <Pressable onPress={() => setWhoKey(null)} accessibilityRole="button" accessibilityLabel={strings.reactions.whoOpen + ', ' + strings.reactions.total(total)} hitSlop={8} style={{ minHeight: 28, justifyContent: 'center' }} testID={prefix + '-who'}>
            <AppText variant="caption" color="textMuted">
              {strings.reactions.total(total)}
            </AppText>
          </Pressable>
        ) : null}
        <View style={{ flex: 1 }} />
        {trailing}
      </View>
      {error ? (
        <AppText variant="caption" color="danger" accessibilityRole="alert" testID={prefix + '-error'}>
          {error}
        </AppText>
      ) : null}
      <Sheet visible={pickerOpen} title={strings.reactions.pickerTitle} onClose={() => setPickerOpen(false)} testID={prefix + '-picker'}>
        <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: theme.spacing.sm }}>
          {set.items.map((r) => {
            const mine = ((fields && fields.myReactions) || []).includes(r.key);
            return (
              <Pressable
                key={r.key}
                testID={prefix + '-pick-' + r.key}
                onPress={() => {
                  setPickerOpen(false);
                  toggle(r.key, !mine);
                }}
                accessibilityRole="button"
                accessibilityLabel={mine ? strings.reactions.toggleOff(r.label) : strings.reactions.toggleOn(r.label)}
                accessibilityState={{ selected: mine }}
                style={({ pressed }) => ({
                  minWidth: 56,
                  minHeight: 56,
                  alignItems: 'center',
                  justifyContent: 'center',
                  borderRadius: theme.radii.md,
                  borderWidth: 1,
                  borderColor: mine ? theme.colors.primary : theme.colors.border,
                  backgroundColor: mine ? theme.colors.accentSoft : theme.colors.surface,
                  padding: theme.spacing.xs,
                  opacity: pressed ? 0.8 : 1
                })}
              >
                <AppText style={{ fontSize: 26, lineHeight: 32 }} importantForAccessibility="no">
                  {r.emoji}
                </AppText>
                <AppText variant="caption" color="textMuted" importantForAccessibility="no">
                  {r.label}
                </AppText>
              </Pressable>
            );
          })}
        </View>
      </Sheet>
      {whoKey !== undefined ? <WhoReactedSheet target={target} initialKey={whoKey} set={set} onClose={() => setWhoKey(undefined)} /> : null}
    </View>
  );
}

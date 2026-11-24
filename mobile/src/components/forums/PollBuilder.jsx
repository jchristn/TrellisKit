import { POLL_LIMITS, PollResultsVisibilities, PollVoterVisibilities } from '@trelliskit/shared';
import { View } from 'react-native';
import strings from '../../strings';
import { useTheme } from '../../theme';
import AppText from '../AppText';
import Button from '../Button';
import CheckRow from '../CheckRow';
import ChipGroup from '../ChipGroup';
import IconButton from '../IconButton';
import TextField from '../TextField';

const p = strings.forums.poll;

/** ISO close time `days` days after `now`, or null. */
export function closeAfterDays(days, now = Date.now()) {
  const n = Number(days);
  if (!String(days || '').trim() || !Number.isFinite(n) || n <= 0) return null;
  return new Date(now + n * 86400000).toISOString();
}

/**
 * Poll builder for a Poll thread (docs/PHASE3_CONTRACT.md section 6.1, resolved decisions 4 and
 * 5): options (add and remove), single or multiple choice with an optional maximum, an optional
 * close after a number of days, when results show, vote changes, and voter visibility.
 * Controlled: `value` is the shared poll draft plus `closeInDays`.
 */
export default function PollBuilder({ value, onChange, problems = {}, testID = 'poll-builder' }) {
  const theme = useTheme();
  const set = (changes) => onChange({ ...value, ...changes });
  const options = value.options || [];
  return (
    <View style={{ gap: theme.spacing.sm }} testID={testID}>
      <AppText variant="label">{p.builderTitle}</AppText>
      {options.map((o, i) => (
        <View key={i} style={{ flexDirection: 'row', alignItems: 'flex-end', gap: theme.spacing.xs }}>
          <View style={{ flex: 1 }}>
            <TextField label={p.option(i + 1)} value={o.label} onChangeText={(label) => set({ options: options.map((x, j) => (j === i ? { ...x, label } : x)) })} maxLength={POLL_LIMITS.optionMax} testID={testID + '-option-' + i} />
          </View>
          {options.length > POLL_LIMITS.minOptions ? <IconButton icon="trash-outline" label={p.removeOption(i + 1)} onPress={() => set({ options: options.filter((_, j) => j !== i) })} color="textMuted" testID={testID + '-remove-' + i} /> : null}
        </View>
      ))}
      {problems.options ? (
        <AppText color="danger" accessibilityRole="alert" testID={testID + '-problem'}>
          {p.problems[problems.options]}
        </AppText>
      ) : null}
      {options.length < POLL_LIMITS.maxOptions ? <Button label={p.addOption} variant="secondary" onPress={() => set({ options: options.concat({ label: '' }) })} testID={testID + '-add'} /> : null}
      <CheckRow label={p.multipleChoice} value={Boolean(value.multipleChoice)} onValueChange={(v) => set({ multipleChoice: v, maxChoices: v ? value.maxChoices : null })} testID={testID + '-multiple'} />
      {value.multipleChoice ? (
        <TextField label={p.maxChoices} value={value.maxChoices ? String(value.maxChoices) : ''} onChangeText={(t) => set({ maxChoices: t ? Number(t.replace(/\D/g, '')) || null : null })} keyboardType="number-pad" error={problems.maxChoices ? p.problems[problems.maxChoices] : null} testID={testID + '-max'} />
      ) : null}
      <TextField label={p.closeInDays} value={value.closeInDays || ''} onChangeText={(t) => set({ closeInDays: t.replace(/[^\d.]/g, '') })} keyboardType="decimal-pad" error={problems.closesUtc ? p.problems[problems.closesUtc] : null} testID={testID + '-close'} />
      <ChipGroup label={p.resultsVisibility} options={PollResultsVisibilities.map((v) => ({ value: v, label: p.resultsOptions[v] }))} value={value.resultsVisibility || 'AfterVote'} onToggle={(v) => set({ resultsVisibility: v })} testID={testID + '-results'} />
      <ChipGroup label={p.voterVisibility} options={PollVoterVisibilities.map((v) => ({ value: v, label: p.voterOptions[v] }))} value={value.voterVisibility || 'Anonymous'} onToggle={(v) => set({ voterVisibility: v })} testID={testID + '-voters'} />
      <CheckRow label={p.allowVoteChange} value={value.allowVoteChange !== false} onValueChange={(v) => set({ allowVoteChange: v })} testID={testID + '-change'} />
    </View>
  );
}

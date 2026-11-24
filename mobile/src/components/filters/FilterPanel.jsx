import { useState } from 'react';
import { View } from 'react-native';
import { useAuth } from '../../auth/AuthProvider';
import { emptyFilters, QueryLimits } from '../../content/enumeration';
import { isStaffUser } from '../../content/permissions';
import strings from '../../strings';
import { useTheme } from '../../theme';
import AppText from '../AppText';
import Button from '../Button';
import CheckRow from '../CheckRow';
import ChipGroup from '../ChipGroup';
import IconButton from '../IconButton';
import TextField from '../TextField';
import AutocompleteField from './AutocompleteField';
import RemovableChip from './RemovableChip';
import { useMetadataSuggestions } from './useMetadataSuggestions';

function TagValueField({ row, index, suggestions, onChange, hideStaff }) {
  const suggest = (prefix) => suggestions.tagValues(row.key.trim(), prefix);
  if (row.any) return null;
  return (
    <AutocompleteField
      label={strings.filters.tagValue}
      value={row.value}
      onChangeText={(value) => onChange({ ...row, value })}
      onSubmit={(value) => onChange({ ...row, value })}
      suggest={row.key.trim() ? suggest : null}
      pick={(o) => o && o.value}
      hideStaff={hideStaff}
      testID={'filter-tag-' + index + '-value'}
    />
  );
}

/**
 * Search, ordering, label, and tag filters for a list (docs/ENUMERATION_AND_METADATA.md section
 * 3). Edits a draft; Apply hands it back. Labels autocomplete from `labels/enumerate` with an
 * All/Any toggle; tag rows autocomplete keys and values and offer "has key" (`*`).
 * @param {object} props
 * @param {object} props.filters Current filter state.
 * @param {(filters: object) => void} props.onApply
 * @param {string} props.targetType Object type for autocomplete (FeedPost, Comment, Notification, User).
 * @param {{ value: string, label: string }[]} [props.orderings]
 * @param {string} [props.searchLabel]
 * @param {(draft: object, setDraft: (fn: (d: object) => object) => void) => React.ReactNode} [props.extra]
 *   Extra filter controls for the collection (for example the member directory's profile
 *   filters), editing the same draft; shown after the tag filters.
 */
export default function FilterPanel({ filters, onApply, targetType, orderings = [], searchLabel, extra, testID = 'filters' }) {
  const theme = useTheme();
  const { user } = useAuth();
  const staff = isStaffUser(user);
  const suggestions = useMetadataSuggestions(targetType);
  const [draft, setDraft] = useState(() => ({ ...emptyFilters(), ...filters, tags: (filters.tags || []).map((t) => ({ ...t })) }));
  const [labelText, setLabelText] = useState('');
  const t = strings.filters;

  const addLabel = (text) => {
    const label = String(text || '').trim();
    setLabelText('');
    if (!label || draft.labels.length >= QueryLimits.labels) return;
    if (draft.labels.some((l) => l.toLowerCase() === label.toLowerCase())) return;
    setDraft((d) => ({ ...d, labels: d.labels.concat(label) }));
  };
  const setTag = (index, row) => setDraft((d) => ({ ...d, tags: d.tags.map((r, i) => (i === index ? row : r)) }));

  return (
    <View style={{ gap: theme.spacing.lg }} testID={testID}>
      <TextField label={searchLabel || t.search} value={draft.search} onChangeText={(search) => setDraft((d) => ({ ...d, search }))} maxLength={QueryLimits.search} returnKeyType="search" onSubmitEditing={() => onApply(draft)} testID={testID + '-search'} />
      {orderings.length ? <ChipGroup label={t.ordering} options={orderings} value={draft.ordering || orderings[0].value} onToggle={(ordering) => setDraft((d) => ({ ...d, ordering }))} testID={testID + '-ordering'} /> : null}

      <View style={{ gap: theme.spacing.sm }}>
        <AppText variant="label">{t.labels}</AppText>
        {draft.labels.length ? (
          <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: theme.spacing.xs }}>
            {draft.labels.map((label) => (
              <RemovableChip key={label} text={label} removeLabel={t.removeLabel(label)} onRemove={() => setDraft((d) => ({ ...d, labels: d.labels.filter((l) => l !== label) }))} testID={testID + '-label-' + label} />
            ))}
          </View>
        ) : null}
        <AutocompleteField
          label={t.addLabel}
          value={labelText}
          onChangeText={setLabelText}
          onSubmit={addLabel}
          suggest={suggestions.labels}
          pick={(o) => o && o.label}
          exclude={draft.labels}
          hideStaff={!staff}
          testID={testID + '-label-input'}
        />
        <ChipGroup label={t.labelMatch} options={[{ value: 'All', label: t.matchAll }, { value: 'Any', label: t.matchAny }]} value={draft.labelMatch || 'All'} onToggle={(labelMatch) => setDraft((d) => ({ ...d, labelMatch }))} testID={testID + '-label-match'} />
      </View>

      <View style={{ gap: theme.spacing.sm }}>
        <AppText variant="label">{t.tags}</AppText>
        {draft.tags.map((row, index) => (
          <View key={index} style={{ gap: theme.spacing.xs, borderWidth: 1, borderColor: theme.colors.border, borderRadius: theme.radii.md, padding: theme.spacing.sm }} testID={testID + '-tag-' + index}>
            <View style={{ flexDirection: 'row', alignItems: 'flex-start', gap: theme.spacing.xs }}>
              <View style={{ flex: 1 }}>
                <AutocompleteField label={t.tagKey} value={row.key} onChangeText={(key) => setTag(index, { ...row, key })} onSubmit={(key) => setTag(index, { ...row, key })} suggest={suggestions.tagKeys} pick={(o) => o && o.key} hideStaff={!staff} testID={'filter-tag-' + index + '-key'} />
              </View>
              <IconButton icon="trash-outline" label={t.removeTag} onPress={() => setDraft((d) => ({ ...d, tags: d.tags.filter((_, i) => i !== index) }))} color="textMuted" testID={'filter-tag-' + index + '-remove'} />
            </View>
            <CheckRow label={t.hasKey} value={Boolean(row.any)} onValueChange={(any) => setTag(index, { ...row, any })} testID={'filter-tag-' + index + '-any'} />
            <TagValueField row={row} index={index} suggestions={suggestions} onChange={(next) => setTag(index, next)} hideStaff={!staff} />
          </View>
        ))}
        {draft.tags.length < QueryLimits.tags ? <Button label={t.addTag} variant="secondary" onPress={() => setDraft((d) => ({ ...d, tags: d.tags.concat({ key: '', value: '', any: false }) }))} testID={testID + '-add-tag'} /> : null}
      </View>

      {extra ? extra(draft, setDraft) : null}

      <Button label={t.apply} onPress={() => onApply(draft)} testID={testID + '-apply'} />
      <Button
        label={t.clearAll}
        variant="ghost"
        onPress={() => {
          const cleared = { ...emptyFilters(), ordering: draft.ordering };
          setDraft(cleared);
          setLabelText('');
          onApply(cleared);
        }}
        testID={testID + '-clear'}
      />
    </View>
  );
}

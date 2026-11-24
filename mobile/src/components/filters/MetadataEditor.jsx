import { useState } from 'react';
import { View } from 'react-native';
import { useOptionalAuth } from '../../auth/AuthProvider';
import { isStaffKey } from '../../content/enumeration';
import { isValidName, MetadataLimits } from '../../content/metadata';
import { isStaffUser } from '../../content/permissions';
import strings from '../../strings';
import { useTheme } from '../../theme';
import AppText from '../AppText';
import Button from '../Button';
import IconButton from '../IconButton';
import AutocompleteField from './AutocompleteField';
import RemovableChip from './RemovableChip';
import { useMetadataSuggestions } from './useMetadataSuggestions';

/**
 * Labels and tags editor for an object the member owns (posts, comments, media, profile), with
 * autocomplete from the existing labels and tags of that object type. Members never see or add
 * `staff:` labels and tag keys; staff do. Collapsed behind a button until opened (or `expanded`).
 * @param {object} props
 * @param {{ labels: string[], tags: Record<string, string> }} props.value
 * @param {(value: { labels: string[], tags: Record<string, string> }) => void} props.onChange
 * @param {string} props.targetType
 */
export default function MetadataEditor({ value, onChange, targetType, expanded = false, api, user: userProp, testID = 'metadata' }) {
  const theme = useTheme();
  const auth = useOptionalAuth();
  const user = userProp || (auth && auth.user);
  const staff = isStaffUser(user);
  const suggestions = useMetadataSuggestions(targetType, api);
  const [open, setOpen] = useState(expanded);
  const [labelText, setLabelText] = useState('');
  const [error, setError] = useState(null);
  const [rows, setRows] = useState(() => Object.entries(value.tags || {}).filter(([k]) => staff || !isStaffKey(k)).map(([key, v]) => ({ key, value: v })));
  const t = strings.metadata;
  const labels = (value.labels || []).filter((l) => staff || !isStaffKey(l));

  if (!open) {
    const count = labels.length + rows.filter((r) => r.key.trim()).length;
    return <Button label={count ? t.show + ' (' + count + ')' : t.show} accessibilityHint={t.showHint} variant="secondary" onPress={() => setOpen(true)} testID={testID + '-open'} />;
  }

  const emitRows = (next) => {
    setRows(next);
    const tags = {};
    for (const row of next) if (row.key.trim()) tags[row.key.trim()] = row.value;
    onChange({ labels: value.labels || [], tags });
  };

  const addLabel = (text) => {
    const label = String(text || '').trim();
    setError(null);
    setLabelText('');
    if (!label) return;
    if (!staff && isStaffKey(label)) return setError(t.reserved);
    if (!isValidName(label)) return setError(t.invalid);
    if (labels.length >= MetadataLimits.labels) return setError(t.tooMany);
    if (labels.some((l) => l.toLowerCase() === label.toLowerCase())) return undefined;
    onChange({ labels: (value.labels || []).concat(label), tags: value.tags || {} });
    return undefined;
  };

  const setRow = (index, patch) => {
    setError(null);
    const row = { ...rows[index], ...patch };
    if (patch.key !== undefined && patch.key.trim()) {
      if (!staff && isStaffKey(patch.key)) return setError(t.reserved);
      if (!isValidName(patch.key)) return setError(t.invalid);
    }
    emitRows(rows.map((r, i) => (i === index ? row : r)));
    return undefined;
  };

  return (
    <View style={{ gap: theme.spacing.md, borderWidth: 1, borderColor: theme.colors.border, borderRadius: theme.radii.md, padding: theme.spacing.md }} testID={testID}>
      <AppText variant="label" accessibilityRole="header">
        {t.title}
      </AppText>
      <AppText variant="caption" color="textMuted">
        {t.hint}
      </AppText>
      {labels.length ? (
        <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: theme.spacing.xs }}>
          {labels.map((label) => (
            <RemovableChip key={label} text={label} removeLabel={t.removeLabel(label)} onRemove={() => onChange({ labels: (value.labels || []).filter((l) => l !== label), tags: value.tags || {} })} testID={testID + '-label-' + label} />
          ))}
        </View>
      ) : null}
      <AutocompleteField label={t.addLabel} value={labelText} onChangeText={setLabelText} onSubmit={addLabel} suggest={suggestions.labels} pick={(o) => o && o.label} exclude={labels} hideStaff={!staff} testID={testID + '-label-input'} />
      <AppText variant="label">{t.tags}</AppText>
      {rows.map((row, index) => (
        <View key={index} style={{ flexDirection: 'row', alignItems: 'flex-start', gap: theme.spacing.xs }} testID={testID + '-tag-' + index}>
          <View style={{ flex: 1, gap: theme.spacing.xs }}>
            <AutocompleteField label={t.tagKey} value={row.key} onChangeText={(key) => setRow(index, { key })} onSubmit={(key) => setRow(index, { key })} suggest={suggestions.tagKeys} pick={(o) => o && o.key} hideStaff={!staff} testID={testID + '-tag-' + index + '-key'} />
            <AutocompleteField
              label={t.tagValue}
              value={row.value}
              onChangeText={(v) => setRow(index, { value: v.slice(0, MetadataLimits.value) })}
              onSubmit={(v) => setRow(index, { value: v })}
              suggest={row.key.trim() ? (prefix) => suggestions.tagValues(row.key.trim(), prefix) : null}
              pick={(o) => o && o.value}
              hideStaff={!staff}
              testID={testID + '-tag-' + index + '-value'}
            />
          </View>
          <IconButton icon="trash-outline" label={t.removeTag(row.key)} onPress={() => emitRows(rows.filter((_, i) => i !== index))} color="textMuted" testID={testID + '-tag-' + index + '-remove'} />
        </View>
      ))}
      {rows.length < MetadataLimits.tags ? <Button label={t.addTag} variant="secondary" onPress={() => setRows(rows.concat({ key: '', value: '' }))} testID={testID + '-add-tag'} /> : null}
      {error ? (
        <AppText variant="caption" color="danger" accessibilityRole="alert" testID={testID + '-error'}>
          {error}
        </AppText>
      ) : null}
    </View>
  );
}

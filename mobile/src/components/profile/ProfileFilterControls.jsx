import { filterableFields, pickerOptions, profileFilterOperators, rankHasLevel } from '@trelliskit/shared';
import { View } from 'react-native';
import strings from '../../strings';
import { useTheme } from '../../theme';
import AppText from '../AppText';
import ChipGroup from '../ChipGroup';
import TextField from '../TextField';
import { CountryPicker, FieldShell, LevelStepper } from './ProfileFieldInput';

/**
 * Member directory profile filters (docs/MULTIPROPERTY_CONTRACT.md section 9.12), one control per
 * filterable field, editing `{ <key>: { <operator>: value } }`:
 * Rank: minimum option and minimum level; SingleSelect and Country: any of (`in`); MultiSelect:
 * any of (`any`); Text: starts with or is exactly; Email: is; Number and Date: from and until;
 * Boolean: yes or no; types that only allow `hasValue`: has a value or not.
 */

const choices = (field) => pickerOptions(field).map((o) => ({ value: o.key, label: o.label, color: o.color || null }));

function FieldFilter({ field, ops, onChange, testID }) {
  const theme = useTheme();
  const t = strings.directory;
  const set = (op, value) => {
    const next = { ...ops };
    if (value === null || value === undefined || value === '' || (Array.isArray(value) && !value.length)) delete next[op];
    else next[op] = value;
    onChange(next);
  };
  const toggleIn = (op, key) => {
    const list = Array.isArray(ops[op]) ? ops[op] : [];
    set(op, list.includes(key) ? list.filter((k) => k !== key) : list.concat(key));
  };
  const allowed = profileFilterOperators(field.type);
  switch (field.type) {
    case 'Rank': {
      const levelMax = field.rank ? Number(field.rank.maxLevel) || 0 : 0;
      return (
        <View style={{ gap: theme.spacing.xs }}>
          <ChipGroup label={t.minRank(field.label)} options={choices(field)} value={ops.minRank || null} onToggle={(key) => set('minRank', key === ops.minRank ? null : key)} testID={testID + '-minRank'} />
          {rankHasLevel(field) ? <LevelStepper label={t.minLevel(field.rank.levelLabel)} value={Number(ops.minLevel) || 0} max={levelMax} onChange={(n) => set('minLevel', n > 0 ? Math.min(n, levelMax) : null)} testID={testID + '-minLevel'} /> : null}
        </View>
      );
    }
    case 'SingleSelect':
      return <ChipGroup label={field.label} hideLabel options={choices(field)} values={ops.in || []} onToggle={(key) => toggleIn('in', key)} testID={testID + '-in'} />;
    case 'MultiSelect':
      return <ChipGroup label={field.label} hideLabel options={choices(field)} values={ops.any || []} onToggle={(key) => toggleIn('any', key)} testID={testID + '-any'} />;
    case 'Country':
      return <CountryPicker label={field.label} multiple value={ops.in || []} onChange={(list) => set('in', list)} testID={testID + '-in'} />;
    case 'Text': {
      const op = ops.eq !== undefined ? 'eq' : 'prefix';
      return (
        <View style={{ gap: theme.spacing.xs }}>
          <ChipGroup
            label={t.textOperator}
            options={[
              { value: 'prefix', label: t.startsWith },
              { value: 'eq', label: t.exactly }
            ]}
            value={op}
            onToggle={(next) => {
              if (next === op) return;
              const text = ops[op] || '';
              const rest = { ...ops };
              delete rest[op];
              onChange(text ? { ...rest, [next]: text } : rest);
            }}
            testID={testID + '-op'}
          />
          <TextField label={field.label} hideLabel value={ops[op] || ''} onChangeText={(text) => set(op, text)} maxLength={100} autoCorrect={false} testID={testID + '-text'} />
        </View>
      );
    }
    case 'Email':
      return <TextField label={field.label} hideLabel value={ops.eq || ''} onChangeText={(text) => set('eq', text)} keyboardType="email-address" autoCapitalize="none" autoCorrect={false} testID={testID + '-eq'} />;
    case 'Number':
    case 'Date': {
      const props = field.type === 'Number' ? { keyboardType: 'numbers-and-punctuation' } : { keyboardType: 'numbers-and-punctuation', placeholder: t.dateFilterHint, maxLength: 10 };
      return (
        <View style={{ flexDirection: 'row', gap: theme.spacing.sm }}>
          <TextField style={{ flex: 1 }} label={t.from} value={ops.gte === undefined ? '' : String(ops.gte)} onChangeText={(text) => set('gte', text)} testID={testID + '-gte'} {...props} />
          <TextField style={{ flex: 1 }} label={t.until} value={ops.lte === undefined ? '' : String(ops.lte)} onChangeText={(text) => set('lte', text)} testID={testID + '-lte'} {...props} />
        </View>
      );
    }
    case 'Boolean': {
      const current = ops.eq === true || ops.eq === 'true' ? 'true' : ops.eq === false || ops.eq === 'false' ? 'false' : 'any';
      return (
        <ChipGroup
          label={field.label}
          hideLabel
          options={[
            { value: 'any', label: t.any },
            { value: 'true', label: t.yes },
            { value: 'false', label: t.no }
          ]}
          value={current}
          onToggle={(v) => set('eq', v === 'any' ? null : v === 'true')}
          testID={testID + '-eq'}
        />
      );
    }
    default: {
      if (!allowed.includes('hasValue')) return null;
      const current = ops.hasValue === true || ops.hasValue === 'true' ? 'true' : ops.hasValue === false || ops.hasValue === 'false' ? 'false' : 'any';
      return (
        <ChipGroup
          label={field.label}
          hideLabel
          options={[
            { value: 'any', label: t.any },
            { value: 'true', label: t.hasValue },
            { value: 'false', label: t.noValue }
          ]}
          value={current}
          onToggle={(v) => set('hasValue', v === 'any' ? null : v === 'true')}
          testID={testID + '-hasValue'}
        />
      );
    }
  }
}

/**
 * Profile filter controls for the filter panel draft.
 * @param {object} props
 * @param {object[]} props.fields Every definition (filterable ones are offered).
 * @param {Record<string, Record<string, any>>} props.value Profile filter state.
 * @param {(next: Record<string, Record<string, any>>) => void} props.onChange
 */
export default function ProfileFilterControls({ fields, value, onChange, testID = 'profile-filters' }) {
  const theme = useTheme();
  const list = filterableFields(fields);
  if (!list.length) return null;
  const state = value || {};
  return (
    <View style={{ gap: theme.spacing.md }} testID={testID}>
      <AppText variant="label" accessibilityRole="header">
        {strings.directory.profileFilters}
      </AppText>
      {list.map((field) => (
        <FieldShell key={field.key} label={field.label} testID={testID + '-' + field.key}>
          <FieldFilter
            field={field}
            ops={state[field.key] || {}}
            onChange={(ops) => {
              const next = { ...state };
              if (Object.keys(ops).length) next[field.key] = ops;
              else delete next[field.key];
              onChange(next);
            }}
            testID={testID + '-' + field.key}
          />
        </FieldShell>
      ))}
    </View>
  );
}

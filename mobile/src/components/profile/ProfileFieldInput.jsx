import {
  countryName,
  countryOptions,
  emptyHistoryEntry,
  findOption,
  findProfileField,
  isFieldRequired,
  pickerOptions,
  PROFILE_HISTORY_DEFAULT_MAX,
  PROFILE_LONG_TEXT_DEFAULT_MAX,
  PROFILE_OTHER_TEXT_MAX,
  PROFILE_RADIO_MAX_OPTIONS,
  PROFILE_SUB_TEXT_DEFAULT_MAX,
  PROFILE_TEXT_DEFAULT_MAX,
  rankHasLevel,
  rankMaxLevel
} from '@trelliskit/shared';
import { useEffect, useMemo, useRef, useState } from 'react';
import { Pressable, View } from 'react-native';
import { members as membersApi } from '../../api/community';
import { readEnumeration } from '../../content/enumeration';
import strings from '../../strings';
import { useTheme } from '../../theme';
import AppText from '../AppText';
import Button from '../Button';
import Card from '../Card';
import CheckRow from '../CheckRow';
import ChipGroup from '../ChipGroup';
import OptionList from '../OptionList';
import DateField, { parseDateParts } from '../DateField';
import IconButton from '../IconButton';
import SelectField from '../SelectField';
import Sheet from '../Sheet';
import TextField from '../TextField';
import { Swatch } from './ProfileDisplay';

/**
 * Inputs for configurable profile fields (docs/MULTIPROPERTY_CONTRACT.md section 9.11), one per
 * type, driven by the definition: label with a required marker, help text under the label,
 * placeholder in the input, and the inline error for `profile.<key>` (client validation or
 * server `fieldErrors`). Values are the shared form shapes (`toFormValue`). Unknown types render
 * nothing.
 */

const t = () => strings.profileFields;

/** Label, required marker, help text, the control, and the error. */
export function FieldShell({ label, required, helpText, error, children, testID }) {
  const theme = useTheme();
  return (
    <View style={{ gap: theme.spacing.xs }} testID={testID}>
      <View style={{ flexDirection: 'row', alignItems: 'baseline', flexWrap: 'wrap', gap: theme.spacing.xs }}>
        <AppText variant="label">{label}</AppText>
        {required ? (
          <AppText variant="caption" color="danger" testID={testID ? testID + '-required' : undefined}>
            {t().required}
          </AppText>
        ) : null}
      </View>
      {helpText ? (
        <AppText variant="caption" color="textMuted" testID={testID ? testID + '-help' : undefined}>
          {helpText}
        </AppText>
      ) : null}
      {children}
      {error ? (
        <AppText variant="caption" color="danger" accessibilityRole="alert" testID={testID ? testID + '-error' : undefined}>
          {error}
        </AppText>
      ) : null}
    </View>
  );
}

/** Date input as month, day, and year fields; emits `YYYY-MM-DD` (or the raw text while incomplete, which fails validation). */
export function DateInput({ label, value, onChange, error, hint, testID }) {
  const [parts, setParts] = useState(() => {
    const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value || '');
    return m ? { year: m[1], month: String(Number(m[2])), day: String(Number(m[3])) } : { month: '', day: '', year: '' };
  });
  return (
    <DateField
      label={label}
      hint={hint}
      value={parts}
      error={error}
      onChange={(next) => {
        setParts(next);
        if (!next.month && !next.day && !next.year) return onChange('');
        const parsed = parseDateParts(next);
        return onChange(parsed ? parsed.iso : [next.year, next.month, next.day].join('-'));
      }}
      testID={testID}
    />
  );
}

/** Level stepper from 0 to `max`. */
export function LevelStepper({ label, value, max, onChange, testID }) {
  const theme = useTheme();
  const level = Math.max(0, Math.min(max, Number(value) || 0));
  return (
    <View style={{ flexDirection: 'row', alignItems: 'center', gap: theme.spacing.sm }} testID={testID}>
      <AppText variant="label" style={{ flexShrink: 1 }}>
        {t().level(label)}
      </AppText>
      <IconButton icon="remove-circle-outline" label={t().levelDecrease(label)} onPress={() => onChange(level - 1)} disabled={level <= 0} testID={testID + '-dec'} />
      <AppText accessibilityLabel={t().levelValue(label, level)} accessibilityLiveRegion="polite" testID={testID + '-value'}>
        {String(level)}
      </AppText>
      <IconButton icon="add-circle-outline" label={t().levelIncrease(label)} onPress={() => onChange(level + 1)} disabled={level >= max} testID={testID + '-inc'} />
    </View>
  );
}

function optionChoices(field, ...current) {
  return pickerOptions(field, ...current).map((o) => ({ value: o.key, label: o.label, color: o.color || null }));
}

/** Rank picker: options with color swatches, plus a level stepper when the field has levels. */
export function RankInput({ field, value, onChange, label, withLevel = true, hideLabel = true, testID }) {
  const current = value || { key: '', level: 0 };
  const hasLevel = withLevel && current.key && rankHasLevel(field, current.key);
  const max = current.key ? rankMaxLevel(field, current.key) : 0;
  return (
    <View style={{ gap: 8 }}>
      <ChipGroup
        label={label || field.label}
        hideLabel={hideLabel}
        options={optionChoices(field, current.key)}
        value={current.key || null}
        onToggle={(key) => onChange({ key: key === current.key ? '' : key, level: key === current.key ? 0 : Math.min(current.level || 0, rankMaxLevel(field, key)) })}
        testID={testID}
      />
      {hasLevel ? <LevelStepper label={field.rank.levelLabel} value={current.level} max={max} onChange={(level) => onChange({ ...current, level: Math.max(0, Math.min(max, level)) })} testID={testID + '-level'} /> : null}
    </View>
  );
}

/** A dropdown for long option lists: the current choice opens a sheet with the options. */
function OptionSheetPicker({ label, options, value, onChange, required, testID }) {
  const theme = useTheme();
  const [open, setOpen] = useState(false);
  const selected = options.find((o) => o.value === value);
  return (
    <View>
      <Pressable
        onPress={() => setOpen(true)}
        accessibilityRole="button"
        accessibilityLabel={label + ', ' + (selected ? selected.label : t().choose)}
        testID={testID}
        style={{ minHeight: theme.layout.minTouchTarget, flexDirection: 'row', alignItems: 'center', gap: theme.spacing.sm, paddingHorizontal: theme.spacing.md, borderWidth: 1, borderColor: theme.colors.border, borderRadius: theme.radii.md, backgroundColor: theme.colors.surface }}
      >
        {selected && selected.color ? <Swatch color={selected.color} /> : null}
        <AppText color={selected ? 'text' : 'textMuted'} style={{ flex: 1 }}>
          {selected ? selected.label : t().choose}
        </AppText>
      </Pressable>
      <Sheet visible={open} title={t().chooseTitle(label)} onClose={() => setOpen(false)} testID={testID + '-sheet'}>
        {options.map((o) => (
          <Pressable
            key={o.value}
            onPress={() => {
              setOpen(false);
              onChange(o.value);
            }}
            accessibilityRole="radio"
            accessibilityState={{ selected: o.value === value }}
            accessibilityLabel={o.label}
            testID={testID + '-option-' + o.value}
            style={{ minHeight: theme.layout.minTouchTarget, flexDirection: 'row', alignItems: 'center', gap: theme.spacing.sm }}
          >
            {o.color ? <Swatch color={o.color} /> : null}
            <AppText style={{ flex: 1 }} color={o.value === value ? 'primary' : 'text'}>
              {o.label}
            </AppText>
          </Pressable>
        ))}
        {!required && value ? (
          <Button
            label={t().clear}
            variant="ghost"
            onPress={() => {
              setOpen(false);
              onChange('');
            }}
            testID={testID + '-clear'}
          />
        ) : null}
      </Sheet>
    </View>
  );
}

/** Country picker: the chosen country, and a search over the shared country list (inline, so it also works inside sheets). */
export function CountryPicker({ multiple = false, ...props }) {
  return multiple ? <CountryMultiPicker {...props} /> : <CountrySelect {...props} />;
}

/** One country as a dropdown, like the website's country <select>. */
export function CountrySelect({ label, value, onChange, required, showLabel = false, hint, error, testID }) {
  const options = useMemo(() => countryOptions().map((c) => ({ value: c.code, label: c.name })), []);
  return <SelectField label={label} showLabel={showLabel} hint={hint} error={error} options={options} value={value || ''} onChange={onChange} required={required} searchPlaceholder={t().countrySearch} testID={testID} />;
}

/** Several countries (the member filter): an inline search, because the filter already sits in a sheet and a second sheet cannot open on top of it. */
function CountryMultiPicker({ label, value, onChange, required, testID }) {
  const multiple = true;
  const theme = useTheme();
  const [query, setQuery] = useState('');
  const chosen = multiple ? (Array.isArray(value) ? value : []) : value ? [value] : [];
  const search = query.trim().toLowerCase();
  const matches = search
    ? countryOptions()
        .filter((c) => !chosen.includes(c.code) && (c.name.toLowerCase().includes(search) || c.code.toLowerCase() === search))
        .slice(0, 8)
    : [];
  const pick = (code) => {
    setQuery('');
    onChange(multiple ? chosen.concat(code) : code);
  };
  return (
    <View style={{ gap: theme.spacing.xs }}>
      {chosen.length ? (
        <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: theme.spacing.xs }}>
          {chosen.map((code) => (
            <View key={code} style={{ flexDirection: 'row', alignItems: 'center', gap: theme.spacing.xs, paddingHorizontal: theme.spacing.md, minHeight: 36, borderRadius: theme.radii.pill, borderWidth: 1, borderColor: theme.colors.primary, backgroundColor: theme.colors.accentSoft }} testID={testID + '-chosen-' + code}>
              <AppText>{countryName(code)}</AppText>
              {multiple || !required ? <IconButton icon="close" label={t().clearValue(countryName(code))} onPress={() => onChange(multiple ? chosen.filter((c) => c !== code) : '')} testID={testID + '-remove-' + code} /> : null}
            </View>
          ))}
        </View>
      ) : null}
      <TextField label={multiple || !chosen.length ? label : t().countryChange} compact value={query} onChangeText={setQuery} autoCorrect={false} placeholder={t().countrySearch} testID={testID} />
      {search && !matches.length ? (
        <AppText variant="caption" color="textMuted">
          {t().noMatches}
        </AppText>
      ) : null}
      {matches.map((c) => (
        <Pressable key={c.code} onPress={() => pick(c.code)} accessibilityRole="button" accessibilityLabel={c.name} testID={testID + '-option-' + c.code} style={{ minHeight: theme.layout.minTouchTarget, justifyContent: 'center' }}>
          <AppText>{c.name}</AppText>
        </Pressable>
      ))}
    </View>
  );
}

const SEARCH_DEBOUNCE_MS = 250;

/** `Member` sub-field: a name with member search; picking a member links it, typing again keeps a free name. */
export function MemberInput({ label, value, onChange, api, error, testID }) {
  const theme = useTheme();
  const current = value || { name: '', userId: null };
  const [suggestions, setSuggestions] = useState([]);
  const request = useRef(0);
  const canSearch = Boolean(api && typeof api.post === 'function');
  const name = current.name || '';
  const linked = Boolean(current.userId);

  useEffect(() => {
    const q = name.trim();
    if (!canSearch || linked || q.length < 2) {
      // Suggestions are only for an unlinked name being typed.
      // eslint-disable-next-line react-hooks/set-state-in-effect
      setSuggestions([]);
      return undefined;
    }
    const id = (request.current += 1);
    const timer = setTimeout(async () => {
      try {
        const page = readEnumeration(await membersApi.search(api, q, { limit: 5 }));
        if (id === request.current) setSuggestions(page.items.filter((m) => m && m.id));
      } catch {
        if (id === request.current) setSuggestions([]);
      }
    }, SEARCH_DEBOUNCE_MS);
    return () => clearTimeout(timer);
  }, [api, canSearch, linked, name]);

  return (
    <View style={{ gap: theme.spacing.xs }}>
      <TextField label={label} value={name} onChangeText={(text) => onChange({ name: text, userId: null })} hint={t().memberHint} maxLength={100} error={error} autoCorrect={false} testID={testID} />
      {linked ? (
        <View style={{ flexDirection: 'row', alignItems: 'center', gap: theme.spacing.sm }}>
          <AppText variant="caption" color="success" testID={testID + '-linked'}>
            {t().linkedTo(name)}
          </AppText>
          <Button label={t().unlink} variant="ghost" onPress={() => onChange({ name, userId: null })} style={{ alignSelf: 'auto' }} testID={testID + '-unlink'} />
        </View>
      ) : null}
      {suggestions.length ? (
        <View accessibilityLabel={t().memberSuggestions} style={{ gap: theme.spacing.xxs }} testID={testID + '-suggestions'}>
          {suggestions.map((m) => (
            <Pressable
              key={m.id}
              onPress={() => {
                setSuggestions([]);
                onChange({ name: m.displayName, userId: m.id });
              }}
              accessibilityRole="button"
              accessibilityLabel={t().useMember(m.displayName)}
              testID={testID + '-suggestion-' + m.id}
              style={{ minHeight: theme.layout.minTouchTarget, justifyContent: 'center' }}
            >
              <AppText>{m.displayName + (m.handle ? ' @' + m.handle : '')}</AppText>
            </Pressable>
          ))}
        </View>
      ) : null}
    </View>
  );
}

function SubFieldInput({ sub, value, onChange, fields, api, error, testID }) {
  const label = sub.label || sub.key;
  const shownLabel = sub.required ? t().requiredLabel(label) : label;
  switch (sub.type) {
    case 'Text':
      return <TextField label={shownLabel} value={value || ''} onChangeText={onChange} maxLength={sub.maxLength || PROFILE_SUB_TEXT_DEFAULT_MAX} error={error} testID={testID} />;
    case 'Url':
      return <TextField label={shownLabel} value={value || ''} onChangeText={onChange} keyboardType="url" autoCapitalize="none" autoCorrect={false} error={error} testID={testID} />;
    case 'Number':
      return <TextField label={shownLabel} value={value || ''} onChangeText={onChange} keyboardType="numbers-and-punctuation" error={error} testID={testID} />;
    case 'Date':
      return <DateInput label={shownLabel} value={value || ''} onChange={onChange} error={error} testID={testID} />;
    case 'Boolean':
      return <CheckRow label={shownLabel} value={value === true} onValueChange={onChange} error={error} testID={testID} />;
    case 'SingleSelect': {
      const options = (sub.options || []).filter((o) => !o.retired || o.key === value).map((o) => ({ value: o.key, label: o.label }));
      return <ChipGroup label={shownLabel} options={options} value={value || null} onToggle={(key) => onChange(key === value ? '' : key)} error={error} testID={testID} />;
    }
    case 'RankValue': {
      const rankField = findProfileField(fields, sub.rankFieldKey);
      if (!rankField) return null;
      return (
        <View style={{ gap: 4 }}>
          <RankInput field={rankField} label={shownLabel} hideLabel={false} value={value} withLevel={Boolean(sub.withLevel)} onChange={onChange} testID={testID} />
          {error ? (
            <AppText variant="caption" color="danger" accessibilityRole="alert" testID={testID + '-error'}>
              {error}
            </AppText>
          ) : null}
        </View>
      );
    }
    case 'Member':
      return <MemberInput label={shownLabel} value={value} onChange={onChange} api={api} error={error} testID={testID} />;
    default:
      return null;
  }
}

/** History list editor: add, remove, and reorder entries (at most `maxEntries`), each a small form of its sub-fields. */
export function HistoryEditor({ field, value, onChange, fields, api, errors, testID }) {
  const theme = useTheme();
  const entries = Array.isArray(value) ? value : [];
  const max = (field.validation && field.validation.maxEntries) || PROFILE_HISTORY_DEFAULT_MAX;
  const subs = (field.subFields || []).slice().sort((a, b) => (a.position || 0) - (b.position || 0));
  const setEntry = (index, subKey, subValue) => onChange(entries.map((e, i) => (i === index ? { ...e, values: { ...e.values, [subKey]: subValue } } : e)));
  const move = (index, delta) => {
    const next = entries.slice();
    const [item] = next.splice(index, 1);
    next.splice(index + delta, 0, item);
    onChange(next);
  };
  return (
    <View style={{ gap: theme.spacing.sm }}>
      <AppText variant="caption" color="textMuted">
        {t().historyHint(max)}
      </AppText>
      {entries.map((entry, index) => {
        const entryId = testID + '-' + index;
        return (
          <Card key={entry.rowKey} testID={entryId}>
            <View style={{ flexDirection: 'row', alignItems: 'center' }} accessibilityLabel={t().entryActions(index + 1)}>
              <AppText variant="label" style={{ flex: 1 }}>
                {t().entry(index + 1)}
              </AppText>
              <IconButton icon="arrow-up" label={t().moveUp} onPress={() => move(index, -1)} disabled={index === 0} testID={entryId + '-up'} />
              <IconButton icon="arrow-down" label={t().moveDown} onPress={() => move(index, 1)} disabled={index === entries.length - 1} testID={entryId + '-down'} />
              <IconButton icon="trash-outline" label={t().removeEntry} onPress={() => onChange(entries.filter((_, i) => i !== index))} testID={entryId + '-remove'} />
            </View>
            {subs.map((sub) => (
              <SubFieldInput
                key={sub.key}
                sub={sub}
                value={entry.values ? entry.values[sub.key] : null}
                onChange={(v) => setEntry(index, sub.key, v)}
                fields={fields}
                api={api}
                error={errors ? errors['profile.' + field.key + '[' + index + '].' + sub.key] : null}
                testID={entryId + '-' + sub.key}
              />
            ))}
          </Card>
        );
      })}
      {entries.length < max ? <Button label={t().addEntry} variant="secondary" onPress={() => onChange(entries.concat(emptyHistoryEntry(field)))} testID={testID + '-add'} /> : null}
    </View>
  );
}

function freeTextOption(field, keys) {
  return keys.map((k) => findOption(field, k)).find((o) => o && o.freeText) || null;
}

/**
 * The input for one profile field.
 * @param {object} props
 * @param {object} props.field Definition.
 * @param {any} props.value Form value.
 * @param {(value: any) => void} props.onChange
 * @param {Record<string, string>} [props.errors] Error text by key (`profile.<key>`, nested History keys, `profile.<key>.otherText`).
 * @param {object[]} [props.fields] Every definition (History rank lookups).
 * @param {object} [props.api] MobileApiClient (History member search).
 */
export default function ProfileFieldInput({ field, value, onChange, errors, fields = [], api, testID }) {
  const id = testID || 'profile-field-' + field.key;
  const required = isFieldRequired(field);
  const v = field.validation || {};
  const error = errors ? errors['profile.' + field.key] : null;
  const otherError = errors ? errors['profile.' + field.key + '.otherText'] : null;
  const placeholder = field.placeholder || undefined;
  const shell = (control, shellError = error) => (
    <FieldShell label={field.label} required={required} helpText={field.helpText} error={shellError} testID={id + '-field'}>
      {control}
    </FieldShell>
  );
  const text = (props) => <TextField label={field.label} hideLabel value={value || ''} onChangeText={onChange} placeholder={placeholder} error={error} testID={id} {...props} />;

  switch (field.type) {
    case 'Text':
      return shell(text({ maxLength: v.maxLength || PROFILE_TEXT_DEFAULT_MAX }), null);
    case 'LongText':
      return shell(text({ multiline: true, maxLength: v.maxLength || PROFILE_LONG_TEXT_DEFAULT_MAX }), null);
    case 'Number':
      return shell(text({ keyboardType: v.integerOnly ? 'number-pad' : 'decimal-pad', hint: v.integerOnly ? t().integerHint : t().numberHint }), null);
    case 'Url':
      return shell(text({ keyboardType: 'url', autoCapitalize: 'none', autoCorrect: false }), null);
    case 'Email':
      return shell(text({ keyboardType: 'email-address', autoCapitalize: 'none', autoCorrect: false, autoComplete: 'email' }), null);
    case 'Phone':
      return shell(text({ keyboardType: 'phone-pad', autoComplete: 'tel', hint: t().phoneHint }), null);
    case 'Date':
      return shell(<DateInput label={field.label} value={value || ''} onChange={onChange} hint={t().dateHint} testID={id} />);
    case 'Boolean':
      return shell(<CheckRow label={field.label} value={value === true} onValueChange={onChange} testID={id} />);
    case 'Country':
      return shell(<CountryPicker label={field.label} value={value || ''} onChange={onChange} required={required} testID={id} />);
    case 'SingleSelect': {
      const current = value || { key: '', otherText: '' };
      const options = optionChoices(field, current.key);
      const setKey = (key) => onChange({ key, otherText: current.otherText || '' });
      const free = freeTextOption(field, current.key ? [current.key] : []);
      return shell(
        <View style={{ gap: 8 }}>
          {options.length <= PROFILE_RADIO_MAX_OPTIONS ? (
            <OptionList label={field.label} hideLabel options={options} value={current.key || null} onToggle={(key) => setKey(key === current.key && !required ? '' : key)} testID={id} />
          ) : (
            <OptionSheetPicker label={field.label} options={options} value={current.key || ''} onChange={setKey} required={required} testID={id} />
          )}
          {free ? <TextField label={t().otherText(free.label)} value={current.otherText || ''} onChangeText={(otherText) => onChange({ ...current, otherText })} maxLength={PROFILE_OTHER_TEXT_MAX} error={otherError} testID={id + '-other'} /> : null}
        </View>
      );
    }
    case 'MultiSelect': {
      const current = value || { keys: [], otherText: '' };
      const keys = current.keys || [];
      const free = freeTextOption(field, keys);
      return shell(
        <View style={{ gap: 8 }}>
          <OptionList label={field.label} hideLabel options={optionChoices(field, keys)} values={keys} onToggle={(key) => onChange({ ...current, keys: keys.includes(key) ? keys.filter((k) => k !== key) : keys.concat(key) })} testID={id} />
          {free ? <TextField label={t().otherText(free.label)} value={current.otherText || ''} onChangeText={(otherText) => onChange({ ...current, otherText })} maxLength={PROFILE_OTHER_TEXT_MAX} error={otherError} testID={id + '-other'} /> : null}
        </View>
      );
    }
    case 'Rank':
      return shell(<RankInput field={field} value={value} onChange={onChange} testID={id} />);
    case 'History':
      return shell(<HistoryEditor field={field} value={value} onChange={onChange} fields={fields} api={api} errors={errors} testID={id} />);
    default:
      return null;
  }
}


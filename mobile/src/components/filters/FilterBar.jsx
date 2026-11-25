import { useState } from 'react';
import { ScrollView, StyleSheet, View } from 'react-native';
import { activeFilterCount, emptyFilters } from '../../content/enumeration';
import { useLayout } from '../../hooks/useLayout';
import strings from '../../strings';
import { useTheme } from '../../theme';
import Button from '../Button';
import IconButton from '../IconButton';
import Sheet from '../Sheet';
import FilterPanel from './FilterPanel';
import RemovableChip from './RemovableChip';

/**
 * Number of active filters including a collection's extra filters (`extraChips`).
 * @param {object} filters
 * @param {(filters: object) => { key: string, text: string, remove: object }[]} [extraChips]
 */
export function filterCount(filters, extraChips) {
  return activeFilterCount(filters) + (extraChips ? extraChips(filters).length : 0);
}

/**
 * Removable chips for the active filters, with Clear all. `extraChips(filters)` adds chips for a
 * collection's own filters: `[{ key, text, remove }]` where `remove` is the filter state without it.
 */
export function ActiveFilters({ filters, onChange, extraChips, testID = 'filters' }) {
  const theme = useTheme();
  const t = strings.filters;
  if (!filterCount(filters, extraChips)) return null;
  const chips = [];
  if (filters.search && filters.search.trim()) chips.push({ key: 'search', text: t.searchChip(filters.search.trim()), remove: { ...filters, search: '' } });
  for (const label of filters.labels || []) chips.push({ key: 'label-' + label, text: label, remove: { ...filters, labels: filters.labels.filter((l) => l !== label) } });
  (filters.tags || []).forEach((row, i) => {
    if (row.key && row.key.trim()) chips.push({ key: 'tag-' + i, text: row.any ? t.hasKeyChip(row.key) : row.key + ' = ' + (row.value || ''), remove: { ...filters, tags: filters.tags.filter((_, j) => j !== i) } });
  });
  if (extraChips) chips.push(...extraChips(filters));
  return (
    <View style={{ flexDirection: 'row', flexWrap: 'wrap', alignItems: 'center', gap: theme.spacing.xs }} testID={testID + '-active'}>
      {chips.map((chip) => (
        <RemovableChip key={chip.key} text={chip.text} removeLabel={t.removeFilter(chip.text)} onRemove={() => onChange(chip.remove)} testID={testID + '-chip-' + chip.key} />
      ))}
      <Button label={t.clearAll} variant="ghost" onPress={() => onChange({ ...emptyFilters(), ordering: filters.ordering })} style={{ alignSelf: 'auto' }} testID={testID + '-clear-all'} />
    </View>
  );
}

/**
 * Filter button (with the active filter count) and active chips for a list. On phones the panel
 * opens in a bottom sheet; on tablets use FilterSidePanel next to the results instead. `sheetOnly`
 * keeps the sheet on tablets too, for lists that already sit in a detail pane (comments).
 */
export function FilterButton({ filters, onChange, targetType, orderings, searchLabel, showSearch = true, sheetOnly = false, extra, extraChips, testID = 'filters' }) {
  const theme = useTheme();
  const { isWide: wide } = useLayout();
  const isWide = wide && !sheetOnly;
  const [open, setOpen] = useState(false);
  const count = filterCount(filters, extraChips);
  const t = strings.filters;
  return (
    <View style={{ gap: theme.spacing.sm }}>
      {!isWide ? (
        <View style={{ flexDirection: 'row', alignItems: 'center' }}>
          <IconButton icon="options-outline" label={count ? t.buttonActive(count) : t.button} text={count ? t.button + ' (' + count + ')' : t.button} badge={count ? String(count) : null} onPress={() => setOpen(true)} testID={testID + '-button'} />
        </View>
      ) : null}
      <ActiveFilters filters={filters} onChange={onChange} extraChips={extraChips} testID={testID} />
      <Sheet visible={open} title={t.title} onClose={() => setOpen(false)} testID={testID + '-sheet'}>
        <FilterPanel
          filters={filters}
          targetType={targetType}
          orderings={orderings}
          searchLabel={searchLabel}
          showSearch={showSearch}
          extra={extra}
          onApply={(next) => {
            setOpen(false);
            onChange(next);
          }}
          testID={testID + '-panel'}
        />
      </Sheet>
    </View>
  );
}

/**
 * Filters inside a sheet (who reacted, edit history): a Filters toggle with the active count that
 * opens the panel in place (a sheet cannot open another sheet), and the active chips.
 */
export function InlineFilters({ filters, onChange, targetType, searchLabel, showSearch = true, testID = 'filters' }) {
  const theme = useTheme();
  const [open, setOpen] = useState(false);
  const count = activeFilterCount(filters);
  const t = strings.filters;
  return (
    <View style={{ gap: theme.spacing.sm }} testID={testID}>
      <View style={{ flexDirection: 'row', alignItems: 'center' }}>
        <IconButton icon="options-outline" label={count ? t.buttonActive(count) : t.button} text={count ? t.button + ' (' + count + ')' : t.button} badge={count ? String(count) : null} onPress={() => setOpen((v) => !v)} testID={testID + '-button'} />
      </View>
      <ActiveFilters filters={filters} onChange={onChange} testID={testID} />
      {open ? (
        <FilterPanel
          key={JSON.stringify(filters)}
          filters={filters}
          targetType={targetType}
          searchLabel={searchLabel}
          showSearch={showSearch}
          onApply={(next) => {
            setOpen(false);
            onChange(next);
          }}
          testID={testID + '-panel'}
        />
      ) : null}
    </View>
  );
}

/** Tablet side panel with the filters, next to the results (applies on Apply). */
export function FilterSidePanel({ filters, onChange, targetType, orderings, searchLabel, extra, testID = 'filters' }) {
  const theme = useTheme();
  return (
    <View testID={testID + '-side-panel'} style={{ width: theme.layout.filterPanelWidth, borderRightWidth: StyleSheet.hairlineWidth, borderRightColor: theme.colors.border, backgroundColor: theme.colors.background }}>
      <ScrollView contentContainerStyle={{ padding: theme.spacing.lg }} keyboardShouldPersistTaps="handled">
        <FilterPanel key={JSON.stringify(filters)} filters={filters} targetType={targetType} orderings={orderings} searchLabel={searchLabel} extra={extra} onApply={onChange} testID={testID + '-panel'} />
      </ScrollView>
    </View>
  );
}

import { useState } from 'react';
import { View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useLayout } from '../hooks/useLayout';
import strings from '../strings';
import { useTheme } from '../theme';
import AppText from './AppText';
import IconButton from './IconButton';
import SplitView from './SplitView';
import { FilterButton, filterCount } from './filters/FilterBar';
import FilterPanel from './filters/FilterPanel';

/** A centered hint in an empty detail pane ("Choose a conversation to read it here."). */
export function DetailPrompt({ text, testID }) {
  const theme = useTheme();
  return (
    <View style={{ flex: 1, alignItems: 'center', justifyContent: 'center', padding: theme.spacing.xl }} testID={testID}>
      <AppText color="textMuted">{text}</AppText>
    </View>
  );
}

/**
 * A searchable list with label and tag filters (docs/ENUMERATION_AND_METADATA.md section 3) and,
 * on tablets, a detail pane. Phones: the list fills the screen and the filters open in a bottom
 * sheet. Tablets: SplitView with the filter panel as a side panel (a drawer below the sidebar
 * breakpoint), the list, and the detail side by side.
 * @param {object} props
 * @param {object} props.filterProps Props for the filter controls (filters, onChange, targetType, orderings, searchLabel, extra, extraChips, testID).
 * @param {React.ReactNode} [props.header] Above the list (actions, folder chips, errors).
 * @param {React.ReactNode} props.list The list (a FlashList that fills its pane).
 * @param {React.ReactNode} [props.detail] Tablet detail pane.
 */
export default function ListDetailLayout({ filterProps, header, list, detail, testID }) {
  const theme = useTheme();
  const { isWide, width } = useLayout();
  const [drawer, setDrawer] = useState(false);
  const prefix = filterProps.testID || 'filters';

  if (!isWide) {
    return (
      <SafeAreaView testID={testID} edges={['left', 'right']} style={{ flex: 1, backgroundColor: theme.colors.background }}>
        <View style={{ paddingHorizontal: theme.spacing.lg, paddingTop: theme.spacing.md, gap: theme.spacing.md }}>
          {header}
          <FilterButton {...filterProps} />
        </View>
        <View style={{ flex: 1 }}>{list}</View>
      </SafeAreaView>
    );
  }

  const narrowTablet = width < theme.layout.sidebarBreakpoint;
  const count = filterCount(filterProps.filters, filterProps.extraChips);
  const t = strings.filters;
  const panel = (
    <FilterPanel
      key={JSON.stringify(filterProps.filters)}
      filters={filterProps.filters}
      targetType={filterProps.targetType}
      orderings={filterProps.orderings}
      searchLabel={filterProps.searchLabel}
      extra={filterProps.extra}
      onApply={(next) => {
        setDrawer(false);
        filterProps.onChange(next);
      }}
      testID={prefix + '-panel'}
    />
  );
  return (
    <SplitView
      testID={testID}
      scroll={false}
      sidebar={<View testID={prefix + '-side-panel'}>{panel}</View>}
      drawerOpen={drawer}
      onDrawerClose={() => setDrawer(false)}
      drawerTitle={t.title}
      list={
        <View style={{ flex: 1 }}>
          <View style={{ paddingHorizontal: theme.spacing.lg, paddingTop: theme.spacing.md, gap: theme.spacing.md }}>
            {header}
            {narrowTablet ? (
              <View style={{ flexDirection: 'row' }}>
                <IconButton icon="options-outline" label={count ? t.buttonActive(count) : t.button} text={count ? t.button + ' (' + count + ')' : t.button} badge={count ? String(count) : null} onPress={() => setDrawer(true)} testID={prefix + '-button'} />
              </View>
            ) : null}
            <FilterButton {...filterProps} />
          </View>
          <View style={{ flex: 1 }}>{list}</View>
        </View>
      }
      detail={detail}
    />
  );
}

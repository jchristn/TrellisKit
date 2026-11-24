import { useContext } from 'react';
import { Modal, Pressable, ScrollView, StyleSheet, View } from 'react-native';
import { SafeAreaInsetsContext, SafeAreaView } from 'react-native-safe-area-context';
import { useLayout } from '../hooks/useLayout';
import strings from '../strings';
import { useTheme } from '../theme';
import AppText from './AppText';
import Button from './Button';
import KeyboardDoneBar from './KeyboardDoneBar';

/**
 * Two-pane (or three-pane) layout for list and detail screens.
 *
 * On wide windows (tablets, landscape large phones, split screen above the breakpoint) the list
 * pane sits on the left at layout.listPaneWidth and the detail pane fills the rest. On narrow
 * windows only the list pane renders; screens navigate to a detail route instead.
 *
 * An optional `sidebar` (for example the forum list) adds a third pane on the left when the
 * window is at least `layout.sidebarBreakpoint` (900 points) wide; below that it collapses into a
 * drawer that slides in from the left while `drawerOpen` is true.
 *
 * @param {object} props
 * @param {React.ReactNode} props.list List pane content.
 * @param {React.ReactNode} [props.detail] Detail pane content (wide layout only).
 * @param {React.ReactNode} [props.sidebar] Sidebar content (wide layout only).
 * @param {boolean} [props.drawerOpen] Show the sidebar drawer (wide windows below the sidebar breakpoint).
 * @param {() => void} [props.onDrawerClose]
 * @param {string} [props.drawerTitle]
 * @param {boolean} [props.scroll] Wrap panes in a ScrollView (default true); pass false when a pane holds its own virtualized list.
 */
export default function SplitView({ list, detail, sidebar, drawerOpen = false, onDrawerClose, drawerTitle, scroll = true, testID }) {
  const theme = useTheme();
  const insets = useContext(SafeAreaInsetsContext) || { top: 0 };
  const { isWide, width } = useLayout();
  const padding = { padding: isWide ? theme.spacing.xl : theme.spacing.lg };
  const pane = (content, paneTestID, scrollable = scroll) =>
    scrollable ? (
      <ScrollView testID={paneTestID} contentContainerStyle={padding}>
        {content}
      </ScrollView>
    ) : (
      <View testID={paneTestID} style={styles.fill}>
        {content}
      </View>
    );

  if (!isWide) {
    return (
      <SafeAreaView testID={testID} edges={['left', 'right']} style={[styles.fill, { backgroundColor: theme.colors.background }]}>
        {pane(list, 'split-list-pane')}
      </SafeAreaView>
    );
  }

  const showSidebar = Boolean(sidebar) && width >= theme.layout.sidebarBreakpoint;
  return (
    <SafeAreaView testID={testID} edges={['left', 'right']} style={[styles.fill, styles.row, { backgroundColor: theme.colors.background }]}>
      {showSidebar ? <View style={[styles.listPane, { width: theme.layout.sidebarWidth, borderRightColor: theme.colors.border }]}>{pane(sidebar, 'split-sidebar-pane', true)}</View> : null}
      <View style={[styles.listPane, { width: theme.layout.listPaneWidth, borderRightColor: theme.colors.border }]}>{pane(list, 'split-list-pane')}</View>
      <View style={[styles.fill, { backgroundColor: theme.colors.surface }]}>{pane(detail, 'split-detail-pane')}</View>
      {sidebar && !showSidebar && drawerOpen ? (
        <Modal visible transparent animationType="fade" onRequestClose={onDrawerClose} supportedOrientations={['portrait', 'landscape']}>
          <View style={styles.fill}>
          <View style={[styles.fill, styles.row]}>
            <View testID="split-drawer" accessibilityViewIsModal style={{ width: theme.layout.sidebarWidth + 40, backgroundColor: theme.colors.background, paddingTop: insets.top + theme.spacing.md, borderRightWidth: StyleSheet.hairlineWidth, borderRightColor: theme.colors.border }}>
              <View style={{ flexDirection: 'row', alignItems: 'center', paddingHorizontal: theme.spacing.lg }}>
                <AppText variant="heading" accessibilityRole="header" style={styles.fill}>
                  {drawerTitle || ''}
                </AppText>
                <Button label={strings.postMenu.close} variant="ghost" onPress={onDrawerClose} style={{ alignSelf: 'auto' }} testID="split-drawer-close" />
              </View>
              <ScrollView contentContainerStyle={padding}>{sidebar}</ScrollView>
            </View>
            <Pressable style={[styles.fill, { backgroundColor: 'rgba(0,0,0,0.45)' }]} onPress={onDrawerClose} accessibilityElementsHidden importantForAccessibility="no-hide-descendants" />
          </View>
          <KeyboardDoneBar />
          </View>
        </Modal>
      ) : null}
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  fill: { flex: 1 },
  row: { flexDirection: 'row' },
  listPane: { borderRightWidth: StyleSheet.hairlineWidth }
});

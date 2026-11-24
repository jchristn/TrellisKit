import { Platform, ScrollView, StyleSheet, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useTheme } from '../theme';
import ContentColumn from './ContentColumn';

/**
 * Standard screen container: themed background, scrolling, and a centered content column. A tap
 * on empty space closes the keyboard, and on iOS so does dragging the content down.
 * @param {object} props
 * @param {('top'|'bottom'|'left'|'right')[]} [props.edges] Safe area edges to pad. Screens under a
 *   navigation header only need left and right.
 * @param {boolean} [props.scroll] Wrap content in a ScrollView (default true).
 * @param {boolean} [props.column] Constrain content to the reading column (default true).
 */
export default function Screen({ children, edges = ['left', 'right'], scroll = true, column = true, contentStyle, testID }) {
  const theme = useTheme();
  const inner = column ? <ContentColumn style={contentStyle}>{children}</ContentColumn> : <View style={[styles.fill, contentStyle]}>{children}</View>;
  return (
    <SafeAreaView testID={testID} edges={edges} style={[styles.fill, { backgroundColor: theme.colors.background }]}>
      {scroll ? (
        <ScrollView contentContainerStyle={{ paddingVertical: theme.spacing.xl, flexGrow: 1 }} keyboardShouldPersistTaps="handled" keyboardDismissMode={Platform.OS === 'ios' ? 'interactive' : 'none'}>
          {inner}
        </ScrollView>
      ) : (
        inner
      )}
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  fill: { flex: 1 }
});

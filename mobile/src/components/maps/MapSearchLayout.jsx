import { StyleSheet, View } from 'react-native';
import { useTheme } from '../../theme';
import AppText from '../AppText';
import Button from '../Button';

/**
 * The map-first search screen: the map fills the screen, a floating "Search this area" pill shows
 * after the camera moves away from the searched area, an optional map error floats at the top,
 * and the results sheet (children) sits over the bottom of the map.
 * @param {object} props
 * @param {import('react').ReactNode} props.map
 * @param {boolean} [props.inset] Leave the standard section gap above the map (it sits under a segment switch).
 * @param {boolean} props.showSearchArea
 * @param {() => void} props.onSearchArea
 * @param {string} props.searchAreaLabel
 * @param {string|null} [props.mapError]
 */
export default function MapSearchLayout({ map, showSearchArea, onSearchArea, searchAreaLabel, mapError = null, inset = false, children, testID }) {
  const theme = useTheme();
  return (
    <View style={[styles.fill, inset ? { marginTop: theme.spacing.lg } : null]} testID={testID}>
      {map}
      {mapError ? (
        <View style={[styles.top, { backgroundColor: theme.colors.surface, padding: theme.spacing.sm, borderRadius: theme.radii.md }]}>
          <AppText color="danger" accessibilityRole="alert">
            {mapError}
          </AppText>
        </View>
      ) : null}
      {showSearchArea ? (
        <View style={[styles.pill, { top: mapError ? 64 : theme.spacing.md }]} pointerEvents="box-none">
          <Button label={searchAreaLabel} onPress={onSearchArea} style={{ alignSelf: 'auto' }} testID={testID + '-search-area'} />
        </View>
      ) : null}
      {children}
    </View>
  );
}

const styles = StyleSheet.create({
  fill: { flex: 1 },
  top: { position: 'absolute', left: 8, right: 8, top: 8 },
  pill: { position: 'absolute', left: 0, right: 0, alignItems: 'center' }
});

import { useRouter } from 'expo-router';
import { useState } from 'react';
import { View } from 'react-native';
import { BannerSlot } from '../../appConfig';
import { useLayout } from '../../hooks/useLayout';
import strings from '../../strings';
import { useTheme } from '../../theme';
import AppText from '../AppText';
import SplitView from '../SplitView';
import LibraryHome from './LibraryHome';
import SeriesDetail from './SeriesDetail';

/**
 * The library home (the Library tab and the Training Library's Courses segment). Phones: the
 * library home; a series opens `/series/{id}`. Tablets: the home and the selected series' lessons
 * side by side in SplitView.
 * @param {{ testID?: string }} props
 */
export default function LibraryBrowser({ testID = 'screen-library' }) {
  const theme = useTheme();
  const router = useRouter();
  const { isWide } = useLayout();
  const [seriesId, setSeriesId] = useState(null);
  const open = (series) => (isWide ? setSeriesId(series.id) : router.push('/series/' + series.id));
  return (
    <SplitView
      testID={testID}
      scroll={false}
      list={
        <View style={{ flex: 1 }}>
          <BannerSlot placement="Library" style={{ paddingHorizontal: theme.spacing.lg, paddingTop: theme.spacing.md }} />
          <LibraryHome onOpenSeries={open} selectedSeriesId={seriesId} />
        </View>
      }
      detail={
        seriesId ? (
          <SeriesDetail key={seriesId} seriesId={seriesId} />
        ) : (
          <View style={{ flex: 1, alignItems: 'center', justifyContent: 'center', padding: theme.spacing.xl }} testID="library-select">
            <AppText color="textMuted">{strings.library.selectSeries}</AppText>
          </View>
        )
      }
    />
  );
}

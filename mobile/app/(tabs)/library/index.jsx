import { useRouter } from 'expo-router';
import { useState } from 'react';
import { View } from 'react-native';
import { BannerSlot } from '../../../src/appConfig';
import AppText from '../../../src/components/AppText';
import SplitView from '../../../src/components/SplitView';
import LibraryHome from '../../../src/components/library/LibraryHome';
import SeriesDetail from '../../../src/components/library/SeriesDetail';
import { useLayout } from '../../../src/hooks/useLayout';
import strings from '../../../src/strings';
import { useTheme } from '../../../src/theme';

/**
 * Library tab. Phones: the library home; a series opens `/series/{id}`. Tablets: the home and the
 * selected series' lessons side by side in SplitView.
 */
export default function LibraryScreen() {
  const theme = useTheme();
  const router = useRouter();
  const { isWide } = useLayout();
  const [seriesId, setSeriesId] = useState(null);
  const open = (series) => (isWide ? setSeriesId(series.id) : router.push('/series/' + series.id));
  return (
    <SplitView
      testID="screen-library"
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

import { Redirect } from 'expo-router';
import LibraryBrowser from '../../../src/components/library/LibraryBrowser';
import { useAppNavigation } from '../../../src/navigation/AppNavigation';
import { coveredByTraining, segmentHref } from '../../../src/training/hub';

/**
 * Library tab (`/library`). When the property places the Training destination (and not the
 * library itself), the Library lives in Training: this route opens Training's Courses segment, so
 * older links, push targets, and bookmarks keep working. Otherwise it is the library home.
 */
export default function LibraryScreen() {
  const appNavigation = useAppNavigation();
  if (coveredByTraining(appNavigation, 'library')) return <Redirect href={segmentHref('courses')} />;
  return <LibraryBrowser />;
}

import { AccessRequirement, libraryConfig } from '@trelliskit/shared';
import { useRouter } from 'expo-router';
import { useState } from 'react';
import { View } from 'react-native';
import { useAuth } from '../../auth/AuthProvider';
import { useProperty } from '../../property';
import strings from '../../strings';
import { useTheme } from '../../theme';
import { librarySegmentsFor, segmentHref } from '../../training/hub';
import { AccessPromptCard } from '../AccessPrompt';
import Button from '../Button';
import CatalogGate from '../CatalogGate';
import ErrorState from '../ErrorState';
import Screen from '../Screen';
import TextField from '../TextField';
import CatalogHome from '../exerciseCatalog/CatalogHome';
import LibraryBrowser from '../library/LibraryBrowser';
import LibrarySearch from './LibrarySearch';
import SavedList from './SavedList';
import SectionTabs from './SectionTabs';
import TrainingFrame from './TrainingFrame';

const t = strings.training;

/**
 * The Training Library: segments (Courses; Techniques only when the overview lists it, or for
 * guests when the catalog is public; Saved for signed-in members while saves are on), one search
 * across courses, lessons, and techniques, and Downloads in the header. Each segment has its own
 * route (`/training/library/{segment}`); switching segments replaces the screen.
 * @param {{ hub: object, segment?: string|null }} props `hub` from useTrainingOverview.
 */
export default function TrainingLibrary({ hub, segment = null }) {
  const theme = useTheme();
  const router = useRouter();
  const property = useProperty();
  const { user } = useAuth();
  const [query, setQuery] = useState('');
  const segments = librarySegmentsFor({ overview: hub.overview, property, user });
  const current = segments.includes(segment) ? segment : segments[0] || null;
  const searching = query.trim().length > 0 && current !== 'saved';
  const downloads = Boolean(user && libraryConfig(property).downloadsEnabled);

  if (!current) {
    return (
      <Screen testID="training-library-unavailable">
        {user ? <ErrorState title={t.unavailable} /> : <AccessPromptCard required={AccessRequirement.SignIn} title={t.guestTitle} body={t.guestBody} testID="training-guest-prompt" />}
      </Screen>
    );
  }
  const header = (
    <View style={{ gap: theme.spacing.sm }}>
      <View style={{ flexDirection: 'row', alignItems: 'center', gap: theme.spacing.sm }}>
        <View style={{ flex: 1 }}>
          {segments.length > 1 ? <SectionTabs label={t.segmentsLabel} options={segments.map((key) => ({ value: key, label: t.segments[key] }))} value={current} onChange={(next) => router.replace(segmentHref(next))} testID="training-segment" /> : null}
        </View>
        {downloads ? <Button label={t.downloads} accessibilityHint={t.downloadsHint} variant="ghost" onPress={() => router.push('/library/downloads')} style={{ alignSelf: 'auto' }} testID="training-downloads-link" /> : null}
      </View>
      {current !== 'saved' ? (
        <TextField label={t.library.searchLabel} hideLabel placeholder={t.library.searchLabel} accessibilityHint={t.library.searchHint} value={query} onChangeText={setQuery} returnKeyType="search" autoCorrect={false} maxLength={100} testID="training-search" />
      ) : null}
    </View>
  );
  let content;
  if (searching) content = <LibrarySearch query={query} techniques={segments.includes('techniques')} />;
  else if (current === 'techniques') content = <CatalogGate feature="library" optIn="catalog"><CatalogHome /></CatalogGate>;
  else if (current === 'saved') content = <SavedList />;
  else content = <CatalogGate feature="library"><LibraryBrowser testID="screen-library" /></CatalogGate>;
  return (
    <TrainingFrame hub={hub} current="Library" header={header}>
      {content}
    </TrainingFrame>
  );
}

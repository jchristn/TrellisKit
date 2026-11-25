import { useRouter } from 'expo-router';
import { View } from 'react-native';
import strings from '../../strings';
import { useTheme } from '../../theme';
import { sectionHref, visibleSections } from '../../training/hub';
import AppText from '../AppText';
import SectionTabs from './SectionTabs';

const t = strings.training;

/**
 * The Training hub frame: section tabs built from the overview's available sections (only when
 * there is more than one), an offline note while a cached overview is shown, an optional row
 * under the tabs (the Library segments), and the section's content. Switching sections replaces
 * the screen, so Back leaves Training instead of walking through every section visited.
 * @param {{ hub: object, current: string, header?: React.ReactNode, children: React.ReactNode, testID?: string }} props
 */
export default function TrainingFrame({ hub, current, header = null, children, testID = 'screen-training' }) {
  const theme = useTheme();
  const router = useRouter();
  const sections = hub.guest ? [] : visibleSections(hub.overview);
  return (
    <View style={{ flex: 1, backgroundColor: theme.colors.background }} testID={testID}>
      {sections.length > 1 || header || hub.stale ? (
        <View style={{ paddingHorizontal: theme.spacing.lg, paddingTop: theme.spacing.md, gap: theme.spacing.sm }}>
          {sections.length > 1 ? (
            <SectionTabs label={t.sectionsLabel} options={sections.map((key) => ({ value: key, label: t.sections[key] || key }))} value={current} onChange={(key) => router.replace(sectionHref(key))} testID="training-section" />
          ) : null}
          {header}
          {hub.stale ? (
            <AppText variant="caption" color="textMuted" testID="training-offline">
              {t.offlineCached}
            </AppText>
          ) : null}
        </View>
      ) : null}
      <View style={{ flex: 1 }}>{children}</View>
    </View>
  );
}

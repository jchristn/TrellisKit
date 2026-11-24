import { Pressable, View } from 'react-native';
import { imageUrl, instructorRankText } from '../../content/phase4';
import { useProperty } from '../../property';
import { useTheme } from '../../theme';
import AppText from '../AppText';
import MediaImage from '../feed/MediaImage';

/** An InstructorSummary row: photo, name, rank, and affiliation. */
export default function InstructorRow({ instructor, onPress, testID }) {
  const theme = useTheme();
  const id = testID || 'instructor-' + instructor.id;
  const rank = instructorRankText(useProperty(), instructor) || null;
  return (
    <Pressable onPress={onPress} accessibilityRole="button" accessibilityLabel={[instructor.displayName, rank, instructor.affiliation].filter(Boolean).join(', ')} testID={id} style={({ pressed }) => ({ flexDirection: 'row', alignItems: 'center', gap: theme.spacing.md, paddingVertical: theme.spacing.sm, opacity: pressed ? 0.85 : 1 })}>
      {imageUrl(instructor.photo, 'thumb') ? <MediaImage url={imageUrl(instructor.photo, 'thumb')} blurhash={instructor.photo.blurhash} alt={null} style={{ width: 40, height: 40, borderRadius: 20 }} /> : <View style={{ width: 40, height: 40, borderRadius: 20, backgroundColor: theme.colors.border }} />}
      <View style={{ flex: 1 }}>
        <AppText style={{ fontWeight: '600' }}>{instructor.displayName}</AppText>
        {rank || instructor.affiliation ? (
          <AppText variant="caption" color="textMuted">
            {[rank, instructor.affiliation].filter(Boolean).join(' · ')}
          </AppText>
        ) : null}
      </View>
    </Pressable>
  );
}

import { Pressable, View } from 'react-native';
import { isSafeUrl } from '../../content/markdown';
import strings from '../../strings';
import { openAppLink } from '../../links';
import { useTheme } from '../../theme';
import AppText from '../AppText';
import IconButton from '../IconButton';
import MediaImage from './MediaImage';

/**
 * Link preview card (docs/PHASE2_CONTRACT.md 5.9). Everything on it was fetched and re-encoded
 * by the server, so the app never loads a third-party page or image. Tapping opens the link in
 * an embedded external page. Composers pass `onRemove` to drop the preview.
 */
export default function LinkPreviewCard({ preview, onRemove, testID }) {
  const theme = useTheme();
  if (!preview || !preview.url) return null;
  const href = preview.canonicalUrl && isSafeUrl(preview.canonicalUrl) ? preview.canonicalUrl : preview.url;
  let host = '';
  try {
    host = new URL(href).host;
  } catch {
    host = '';
  }
  const label = [preview.siteName || host, preview.title].filter(Boolean).join(', ');
  return (
    <View testID={testID} style={{ flexDirection: 'row', borderWidth: 1, borderColor: theme.colors.border, borderRadius: theme.radii.md, overflow: 'hidden', backgroundColor: theme.colors.surface }}>
      <Pressable
        onPress={() => isSafeUrl(href) && openAppLink(href).catch(() => {})}
        accessibilityRole="link"
        accessibilityLabel={label || href}
        style={({ pressed }) => ({ flex: 1, flexDirection: 'row', minHeight: theme.layout.minTouchTarget, opacity: pressed ? 0.85 : 1 })}
      >
        {preview.imageUrl ? <MediaImage url={preview.imageUrl} style={{ width: 96, minHeight: 96 }} /> : null}
        <View style={{ flex: 1, padding: theme.spacing.md, gap: theme.spacing.xxs }}>
          {preview.siteName || host ? (
            <AppText variant="caption" color="textMuted">
              {preview.siteName || host}
            </AppText>
          ) : null}
          {preview.title ? <AppText variant="label">{preview.title}</AppText> : null}
          {preview.description ? (
            <AppText variant="caption" color="textMuted">
              {preview.description}
            </AppText>
          ) : null}
        </View>
      </Pressable>
      {onRemove ? <IconButton icon="close" label={strings.composer.removePreview} onPress={onRemove} testID={testID ? testID + '-remove' : undefined} /> : null}
    </View>
  );
}

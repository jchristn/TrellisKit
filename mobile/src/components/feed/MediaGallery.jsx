import { useState } from 'react';
import { Modal, Pressable, ScrollView, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import strings from '../../strings';
import { useTheme } from '../../theme';
import AppText from '../AppText';
import Button from '../Button';
import MediaImage from './MediaImage';
import VideoPlayer from './VideoPlayer';

/** Best URL for an image variant (WebP, else JPEG). */
export function variantUrl(media, name) {
  const variants = media && media.image && media.image.variants;
  const v = variants && (variants[name] || variants.medium || variants.large || variants.thumb);
  return v ? v.webpUrl || v.jpegUrl || null : null;
}

/** Width over height for layout, clamped so very tall or wide media stay usable. */
export function aspectOf(media) {
  const w = media && media.width;
  const h = media && media.height;
  if (!w || !h) return 4 / 3;
  return Math.min(2, Math.max(0.6, w / h));
}

function itemLabel(media, index, count) {
  const kind = media.kind === 'Video' ? strings.composer.video : strings.composer.image;
  return kind + ', ' + (index + 1) + ' of ' + count + (media.altText ? ', ' + media.altText : '');
}

function Placeholder({ media, theme }) {
  const failed = media.status === 'Failed';
  const text = failed ? strings.errors.failureReasons[media.failureReason] || strings.composer.status.Failed : strings.composer.status.Processing;
  return (
    <View style={{ flex: 1, alignItems: 'center', justifyContent: 'center', backgroundColor: theme.colors.surface, padding: theme.spacing.lg }}>
      <AppText color={failed ? 'danger' : 'textMuted'}>{text}</AppText>
    </View>
  );
}

/**
 * Attachments of a post or comment: a swipeable gallery of images (expo-image, medium variant,
 * blurhash placeholder) and videos (expo-video). Tapping an image opens it full screen (large
 * variant). Processing and Failed media (shown to the author) render a status panel.
 */
export default function MediaGallery({ attachments, compact = false, testID }) {
  const theme = useTheme();
  const insets = useSafeAreaInsets();
  const [width, setWidth] = useState(0);
  const [page, setPage] = useState(0);
  const [viewer, setViewer] = useState(null);
  const items = Array.isArray(attachments) ? attachments.filter(Boolean) : [];
  if (!items.length) return null;
  const first = items[0];
  const ratio = items.length === 1 ? aspectOf(first) : 1;
  const height = width ? Math.min(width / ratio, compact ? 260 : 480) : compact ? 200 : 260;

  const renderItem = (media, index) => {
    const ready = media.status === 'Ready' || !media.status;
    let content;
    if (!ready) content = <Placeholder media={media} theme={theme} />;
    else if (media.kind === 'Video') content = <VideoPlayer media={media} style={{ width: '100%', height: '100%' }} testID={testID ? testID + '-video-' + index : undefined} />;
    else
      content = (
        <Pressable onPress={() => setViewer(media)} accessibilityRole="imagebutton" accessibilityLabel={itemLabel(media, index, items.length)} style={{ flex: 1 }}>
          <MediaImage url={variantUrl(media, 'medium')} blurhash={media.blurhash} style={{ width: '100%', height: '100%' }} testID={testID ? testID + '-image-' + index : undefined} />
        </Pressable>
      );
    return (
      <View key={media.id || index} style={{ width: width || '100%', height, overflow: 'hidden', borderRadius: theme.radii.md, backgroundColor: theme.colors.surface }}>
        {content}
      </View>
    );
  };

  return (
    <View testID={testID} onLayout={(e) => setWidth(e.nativeEvent.layout.width)} style={{ gap: theme.spacing.xs }}>
      {items.length === 1 ? (
        renderItem(first, 0)
      ) : (
        <ScrollView
          horizontal
          pagingEnabled
          showsHorizontalScrollIndicator={false}
          onMomentumScrollEnd={(e) => width && setPage(Math.round(e.nativeEvent.contentOffset.x / width))}
          accessibilityLabel={strings.composer.attachments}
        >
          {items.map(renderItem)}
        </ScrollView>
      )}
      {items.length > 1 ? (
        <AppText variant="caption" color="textMuted" style={{ textAlign: 'center' }} accessibilityLiveRegion="polite">
          {page + 1 + ' / ' + items.length}
        </AppText>
      ) : null}
      <Modal visible={Boolean(viewer)} animationType="fade" onRequestClose={() => setViewer(null)} supportedOrientations={['portrait', 'landscape']}>
        <View style={{ flex: 1, backgroundColor: '#000000', paddingTop: insets.top, paddingBottom: insets.bottom }}>
          <View style={{ alignItems: 'flex-end', padding: theme.spacing.sm }}>
            <Button label={strings.postMenu.close} variant="secondary" onPress={() => setViewer(null)} style={{ alignSelf: 'auto' }} />
          </View>
          {viewer ? <MediaImage url={variantUrl(viewer, 'large')} blurhash={viewer.blurhash} alt={viewer.altText} contentFit="contain" style={{ flex: 1 }} /> : null}
        </View>
      </Modal>
    </View>
  );
}

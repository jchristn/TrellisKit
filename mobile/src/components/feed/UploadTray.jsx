import { Image } from 'expo-image';
import { View } from 'react-native';
import { describeContentError } from '../../content/errors';
import { UploadStatus } from '../../media/upload';
import strings from '../../strings';
import { useTheme } from '../../theme';
import AppText from '../AppText';
import Button from '../Button';
import IconButton from '../IconButton';
import TextField from '../TextField';
import MetadataEditor from '../filters/MetadataEditor';
import { metadataOf } from '../../content/metadata';
import MediaImage from './MediaImage';
import { variantUrl } from './MediaGallery';

function statusText(item) {
  const s = strings.composer.status;
  if (item.status === UploadStatus.Uploading) return s.Uploading(Math.round((item.progress || 0) * 100));
  if (item.status === UploadStatus.Failed) return describeContentError(item.error);
  if (item.stalled) return strings.composer.stalled;
  return s[item.status] || '';
}

/**
 * Attachments being composed: a thumbnail, upload progress, then the processing state until
 * Ready, the failure reason when an upload or processing fails, a Resume button for an
 * interrupted transfer, an optional alt text field, and Remove.
 */
export default function UploadTray({ uploads, showAltText = false, testID = 'uploads' }) {
  const theme = useTheme();
  if (!uploads.items.length) return null;
  return (
    <View style={{ gap: theme.spacing.md }} testID={testID} accessibilityLabel={strings.composer.attachments}>
      {uploads.items.map((item, index) => {
        const media = item.media;
        const isVideo = (item.file && item.file.kind === 'Video') || (media && media.kind === 'Video');
        const localUri = item.file && item.file.uri;
        const remoteThumb = media && media.status === 'Ready' ? (isVideo ? media.video && media.video.posterThumbUrl : variantUrl(media, 'thumb')) : null;
        const progress = item.status === UploadStatus.Uploading ? item.progress || 0 : item.status === UploadStatus.Preparing ? 0 : 1;
        const failed = item.status === UploadStatus.Failed;
        return (
          <View key={item.localId} testID={testID + '-' + index} style={{ gap: theme.spacing.sm, borderWidth: 1, borderColor: failed ? theme.colors.danger : theme.colors.border, borderRadius: theme.radii.md, padding: theme.spacing.sm }}>
            <View style={{ flexDirection: 'row', alignItems: 'center', gap: theme.spacing.md }}>
              <View style={{ width: 64, height: 64, borderRadius: theme.radii.sm, overflow: 'hidden', backgroundColor: theme.colors.surface }}>
                {localUri && !isVideo ? <Image source={{ uri: localUri }} style={{ width: 64, height: 64 }} contentFit="cover" /> : remoteThumb ? <MediaImage url={remoteThumb} style={{ width: 64, height: 64 }} /> : null}
              </View>
              <View style={{ flex: 1, gap: theme.spacing.xxs }}>
                <AppText variant="label">{isVideo ? strings.composer.video : strings.composer.image}</AppText>
                <AppText variant="caption" color={failed ? 'danger' : 'textMuted'} accessibilityLiveRegion="polite" testID={testID + '-' + index + '-status'}>
                  {statusText(item)}
                </AppText>
                <View
                  accessible
                  accessibilityRole="progressbar"
                  accessibilityValue={{ min: 0, max: 100, now: Math.round(progress * 100) }}
                  style={{ height: 4, backgroundColor: theme.colors.surface, borderRadius: 2, overflow: 'hidden' }}
                >
                  <View style={{ width: Math.round(progress * 100) + '%', height: 4, backgroundColor: failed ? theme.colors.danger : theme.colors.primary }} />
                </View>
              </View>
              <IconButton icon="close-circle" label={strings.composer.removeAttachment} onPress={() => uploads.remove(item.localId)} color="textMuted" testID={testID + '-' + index + '-remove'} />
            </View>
            {item.status === UploadStatus.Interrupted ? <Button label={strings.composer.resume} variant="secondary" onPress={uploads.resumeInterrupted} testID={testID + '-' + index + '-resume'} /> : null}
            {showAltText && !failed ? (
              <TextField label={strings.composer.altText} hint={strings.composer.altTextHint} value={item.altText !== undefined ? item.altText : (media && media.altText) || ''} onChangeText={(text) => uploads.setAltText(item.localId, text)} maxLength={1000} testID={testID + '-' + index + '-alt'} />
            ) : null}
            {showAltText && !failed && media ? <MetadataEditor value={item.metadata || metadataOf(media)} onChange={(value) => uploads.setMetadata(item.localId, value)} targetType="Media" testID={testID + '-' + index + '-metadata'} /> : null}
          </View>
        );
      })}
    </View>
  );
}

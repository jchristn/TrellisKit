import { useState } from 'react';
import { View } from 'react-native';
import { pickMedia } from '../../media/pick';
import strings from '../../strings';
import { useTheme } from '../../theme';
import AppText from '../AppText';
import IconButton from '../IconButton';

/**
 * Library and camera buttons for a composer. Hidden entirely when the member may not attach
 * media (viewer.canAttachMedia false); video is offered only with canAttachVideo.
 * @param {object} props
 * @param {{ media: boolean, video: boolean }} props.permissions
 * @param {number} props.remaining How many more attachments fit.
 * @param {number} props.max The attachment limit (for the message).
 * @param {boolean} props.videoAllowed Whether another video fits (one per post).
 * @param {(assets: object[]) => void} props.onPicked
 */
export default function AttachButtons({ permissions, remaining, max, videoAllowed, onPicked, testID = 'attach' }) {
  const theme = useTheme();
  const [message, setMessage] = useState(null);
  if (!permissions.media) return null;
  const allowVideo = permissions.video && videoAllowed;

  async function pick(source) {
    setMessage(null);
    if (remaining <= 0) return setMessage(strings.composer.attachLimit(max || 1));
    try {
      const result = await pickMedia({ source, allowVideo, limit: remaining });
      if (result === 'denied') return setMessage(strings.composer.permissionDenied);
      if (!result) return undefined;
      const videos = result.filter((a) => a.type === 'video' || String(a.mimeType || '').startsWith('video/'));
      const accepted = videos.length > 1 ? result.filter((a) => !videos.slice(1).includes(a)) : result;
      if (videos.length > 1) setMessage(strings.composer.videoLimit);
      onPicked(accepted);
    } catch {
      setMessage(strings.errors.generic);
    }
    return undefined;
  }

  return (
    <View style={{ gap: theme.spacing.xs }}>
      <View style={{ flexDirection: 'row', gap: theme.spacing.sm }}>
        <IconButton icon="images-outline" label={strings.composer.library} accessibilityHint={strings.composer.libraryHint} text={strings.composer.library} onPress={() => pick('library')} disabled={remaining <= 0} testID={testID + '-library'} />
        <IconButton icon="camera-outline" label={strings.composer.camera} accessibilityHint={strings.composer.cameraHint} text={strings.composer.camera} onPress={() => pick('camera')} disabled={remaining <= 0} testID={testID + '-camera'} />
      </View>
      {message ? (
        <AppText variant="caption" color="danger" accessibilityRole="alert">
          {message}
        </AppText>
      ) : null}
    </View>
  );
}

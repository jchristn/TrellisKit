import { useCallback, useState } from 'react';
import { Modal, Pressable, ScrollView, StyleSheet, View, useWindowDimensions } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { reactions } from '../../api/community';
import { useAuth } from '../../auth/AuthProvider';
import { useCursorList } from '../../hooks/useCursorList';
import strings from '../../strings';
import { useTheme } from '../../theme';
import AppText from '../AppText';
import Button from '../Button';

const PREVIEW_LIMIT = 5;

/** A bounded people summary, anchored to a reaction on pointer hover or a long press. */
export default function ReactionPeoplePreview({ target, reaction, anchor, hover, onClose, onShowAll }) {
  const { api } = useAuth();
  const theme = useTheme();
  const insets = useSafeAreaInsets();
  const window = useWindowDimensions();
  const [height, setHeight] = useState(280);
  const fetchPage = useCallback(() => reactions.list(api, { type: target.type, id: target.id }, { key: reaction.key, maxResults: PREVIEW_LIMIT }), [api, target.type, target.id, reaction.key]);
  const list = useCursorList(fetchPage, { key: target.type + ':' + target.id + ':' + reaction.key });
  const width = Math.min(320, window.width - insets.left - insets.right - 24);
  const minTop = insets.top + 12;
  const maxHeight = window.height - insets.top - insets.bottom - 24;
  const left = Math.max(insets.left + 12, Math.min(anchor ? anchor.x : (window.width - width) / 2, window.width - insets.right - width - 12));
  const desiredTop = anchor ? (anchor.y - height - 8 >= minTop ? anchor.y - height - 8 : anchor.y + anchor.height + 8) : (window.height - height) / 2;
  const top = Math.max(minTop, Math.min(desiredTop, window.height - insets.bottom - Math.min(height, maxHeight) - 12));
  const r = strings.reactions;

  return (
    <Modal visible transparent animationType="none" onRequestClose={onClose} supportedOrientations={['portrait', 'landscape']}>
      <View style={styles.fill}>
        <Pressable style={StyleSheet.absoluteFill} onPress={onClose} accessibilityElementsHidden importantForAccessibility="no-hide-descendants" testID="reaction-preview-backdrop" />
        <View
          testID="reaction-people-preview"
          accessibilityViewIsModal
          accessibilityLabel={r.previewTitle(reaction.label)}
          onPointerLeave={hover ? onClose : undefined}
          onLayout={(event) => setHeight(event.nativeEvent.layout.height)}
          style={{ position: 'absolute', top, left, width, maxHeight, padding: theme.spacing.md, gap: theme.spacing.sm, backgroundColor: theme.colors.surface, borderColor: theme.colors.border, borderWidth: 1, borderRadius: theme.radii.md, elevation: 8, shadowColor: '#000', shadowOpacity: 0.2, shadowRadius: 8, shadowOffset: { width: 0, height: 4 } }}
        >
          <View style={{ flexDirection: 'row', alignItems: 'center', gap: theme.spacing.sm }}>
            <AppText variant="label" accessibilityRole="header" style={{ flex: 1 }}>{reaction.emoji} {reaction.label}</AppText>
            <Button label={strings.postMenu.close} variant="ghost" onPress={onClose} style={{ alignSelf: 'auto' }} testID="reaction-preview-close" />
          </View>
          <ScrollView style={{ flexShrink: 1 }}>
            {list.status === 'loading' ? <AppText accessibilityRole="alert">{r.previewLoading}</AppText> : null}
            {list.status === 'error' ? <View><AppText color="danger" accessibilityRole="alert">{r.loadFailed}</AppText><Button label={r.previewRetry} variant="ghost" onPress={list.refresh} /></View> : null}
            {list.status === 'ready' && !list.items.length ? <AppText color="textMuted">{r.none}</AppText> : null}
            {list.items.map((row) => <AppText key={row.id || row.user?.id} style={{ marginBottom: theme.spacing.xs }}>{row.user && !row.user.isDeleted ? row.user.displayName : strings.feed.deletedMember}</AppText>)}
            {list.hasMore ? <AppText variant="caption" color="textMuted">{r.previewMore}</AppText> : null}
          </ScrollView>
          <Button label={r.previewAll} variant="ghost" onPress={onShowAll} testID="reaction-preview-all" />
        </View>
      </View>
    </Modal>
  );
}

const styles = StyleSheet.create({ fill: { flex: 1 } });

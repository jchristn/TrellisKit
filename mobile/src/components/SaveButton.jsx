import { useCallback, useEffect, useRef, useState } from 'react';
import { AccessibilityInfo, View } from 'react-native';
import { useAuth } from '../auth/AuthProvider';
import { useIsOffline } from '../hooks/useNetworkStatus';
import { useProperty } from '../property';
import strings from '../strings';
import { savesOn } from '../training/hub';
import { saveStateBatcher, sendSave } from '../training/saves';
import IconButton from './IconButton';

const t = strings.training.save;

/**
 * The bookmark toggle for a lesson, series, catalog entry, venue, instructor, event, post, or
 * forum thread. Shown to signed-in members while `features.saves` is on. The saved state comes
 * from batched `POST /me/saves/state` lookups shared by every toggle on the screen. Pressing it
 * updates at once; when the server refuses, the toggle rolls back and announces why. A change
 * made offline (or that fails on the network) is queued on the device and synced later.
 * @param {{ targetType: string, targetId: string, title?: string, initialSaved?: boolean|null, onChange?: (saved: boolean) => void, testID?: string }} props
 */
export default function SaveButton({ targetType, targetId, title = '', initialSaved = null, onChange, testID = 'save-button' }) {
  const { api, user } = useAuth();
  const property = useProperty();
  const offline = useIsOffline();
  const enabled = Boolean(savesOn(property, user) && targetId && api && typeof api.saveState === 'function');
  const [saved, setSaved] = useState(initialSaved === null ? null : Boolean(initialSaved));
  const [hint, setHint] = useState(null);
  const busy = useRef(false);
  const active = useRef(true);
  useEffect(() => {
    active.current = true;
    return () => {
      active.current = false;
    };
  }, []);
  useEffect(() => {
    if (!enabled || initialSaved !== null) return;
    const batcher = saveStateBatcher(api);
    const known = batcher.peek(targetType, targetId);
    if (known) {
      // A state another toggle already fetched applies at once.
      // eslint-disable-next-line react-hooks/set-state-in-effect
      setSaved(known.saved);
      return;
    }
    batcher.get(targetType, targetId).then(
      (state) => active.current && setSaved(state.saved),
      () => active.current && setSaved(false)
    );
  }, [api, enabled, targetType, targetId, initialSaved]);

  const toggle = useCallback(async () => {
    if (busy.current || saved === null) return;
    busy.current = true;
    const previous = saved;
    const next = !previous;
    const batcher = saveStateBatcher(api);
    setSaved(next);
    setHint(null);
    batcher.set(targetType, targetId, { saved: next });
    try {
      const outcome = await sendSave(api, targetType, targetId, next, { offline });
      if (outcome.queued) {
        if (active.current) setHint(t.queued);
        AccessibilityInfo.announceForAccessibility(t.queued);
      }
      if (onChange) onChange(next);
    } catch {
      batcher.set(targetType, targetId, { saved: previous });
      if (active.current) {
        setSaved(previous);
        setHint(t.failed);
      }
      AccessibilityInfo.announceForAccessibility(t.failed);
    } finally {
      busy.current = false;
    }
  }, [api, offline, onChange, saved, targetId, targetType]);

  if (!enabled || saved === null) return null;
  return (
    <IconButton
      icon={saved ? 'bookmark' : 'bookmark-outline'}
      label={saved ? t.removeLabel : title ? t.saveLabelFor(title) : t.saveLabel}
      selected={saved}
      color={saved ? 'primary' : 'text'}
      onPress={toggle}
      accessibilityHint={hint || (saved ? t.removeHint : undefined)}
      testID={testID}
    />
  );
}

/**
 * A detail screen's title with the save toggle beside it. Renders the title alone when saves
 * are not available to the caller.
 * @param {{ targetType: string, targetId: string, title?: string, children?: React.ReactNode, testID?: string }} props
 */
export function SaveRow({ targetType, targetId, title = '', children = null, testID = 'save-button' }) {
  return (
    <View style={{ flexDirection: 'row', alignItems: 'flex-start', justifyContent: children ? 'space-between' : 'flex-end', gap: 8 }}>
      {children ? <View style={{ flex: 1 }}>{children}</View> : null}
      <SaveButton targetType={targetType} targetId={targetId} title={title} testID={testID} />
    </View>
  );
}

import Ionicons from '@expo/vector-icons/Ionicons';
import { FlashList } from '@shopify/flash-list';
import { FavoriteTargetTypes, SAVE_COLLECTION_NAME_MAX_LENGTH, SAVE_NOTE_MAX_LENGTH, saveAvailable, saveKey } from '@trelliskit/shared';
import { useRouter } from 'expo-router';
import { useCallback, useEffect, useState } from 'react';
import { AccessibilityInfo, Alert, Pressable, RefreshControl, View } from 'react-native';
import { useAuth } from '../../auth/AuthProvider';
import { useCursorList } from '../../hooks/useCursorList';
import strings from '../../strings';
import { useTheme } from '../../theme';
import { saveTargetPath } from '../../training/hub';
import { saveStateBatcher } from '../../training/saves';
import AppText from '../AppText';
import Button from '../Button';
import Card from '../Card';
import CheckRow from '../CheckRow';
import ChipGroup from '../ChipGroup';
import ErrorState from '../ErrorState';
import IconButton from '../IconButton';
import Loading from '../Loading';
import Sheet from '../Sheet';
import TextField from '../TextField';

const t = strings.training.saved;
const save = strings.training.save;

/** The member's collections (`POST /me/save-collections/enumerate`, one page of at most 100). */
export function useSaveCollections(api) {
  const [state, setState] = useState({ status: 'loading', items: [] });
  const load = useCallback(async () => {
    try {
      const page = await api.listSaveCollections({ maxResults: 100 });
      setState({ status: 'ready', items: ((page && page.objects) || []).filter(Boolean) });
    } catch {
      setState((current) => ({ status: 'error', items: current.items }));
    }
  }, [api]);
  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect
    load();
  }, [load]);
  return { ...state, reload: load, setItems: (items) => setState({ status: 'ready', items }) };
}

/** Move one id up or down in a list of collections. */
export function moveCollection(items, id, delta) {
  const index = items.findIndex((item) => item.id === id);
  const target = index + delta;
  if (index < 0 || target < 0 || target >= items.length) return items;
  const next = items.slice();
  const [moved] = next.splice(index, 1);
  next.splice(target, 0, moved);
  return next;
}

function collectionError(error) {
  return error && error.code === 'DuplicateName' ? t.duplicateName : t.collectionFailed;
}

/** Create, rename, reorder, and delete collections. Deleting keeps the saves. */
function CollectionsSheet({ api, collections, onClose }) {
  const theme = useTheme();
  const [name, setName] = useState('');
  const [renaming, setRenaming] = useState(null);
  const [renameText, setRenameText] = useState('');
  const [error, setError] = useState(null);
  const valid = (value) => value.trim().length >= 1 && value.trim().length <= SAVE_COLLECTION_NAME_MAX_LENGTH;
  async function run(fn) {
    setError(null);
    try {
      await fn();
      await collections.reload();
      return true;
    } catch (e) {
      setError(collectionError(e));
      return false;
    }
  }
  async function create() {
    if (!valid(name)) return setError(t.nameRequired);
    if (await run(() => api.createSaveCollection({ name: name.trim() }))) setName('');
  }
  async function rename(item) {
    if (!valid(renameText)) return setError(t.nameRequired);
    if (await run(() => api.updateSaveCollection(item.id, { name: renameText.trim() }))) setRenaming(null);
  }
  async function move(item, delta) {
    const next = moveCollection(collections.items, item.id, delta);
    if (next === collections.items) return;
    const before = collections.items;
    collections.setItems(next);
    try {
      await api.orderSaveCollections(next.map((c) => c.id));
    } catch {
      collections.setItems(before);
      setError(t.collectionFailed);
    }
  }
  function remove(item) {
    Alert.alert(t.deleteConfirmTitle, t.deleteConfirmBody, [
      { text: t.cancel, style: 'cancel' },
      { text: t.delete, style: 'destructive', onPress: () => run(() => api.deleteSaveCollection(item.id)) }
    ]);
  }
  return (
    <Sheet visible title={t.manageCollections} onClose={onClose} testID="saved-collections-sheet">
      <View style={{ gap: theme.spacing.sm }}>
        <TextField label={t.newCollection} value={name} onChangeText={setName} maxLength={SAVE_COLLECTION_NAME_MAX_LENGTH} testID="saved-collection-name" />
        <Button label={t.createCollection} variant="secondary" onPress={create} disabled={!name.trim()} testID="saved-collection-create" />
      </View>
      {error ? (
        <AppText color="danger" accessibilityRole="alert" testID="saved-collections-error">
          {error}
        </AppText>
      ) : null}
      {!collections.items.length ? <AppText color="textMuted">{t.noCollections}</AppText> : null}
      {collections.items.map((item, index) => (
        <View key={item.id} style={{ gap: theme.spacing.xs }} testID={'saved-collection-' + item.id}>
          {renaming === item.id ? (
            <View style={{ gap: theme.spacing.xs }}>
              <TextField label={t.renameCollection(item.name)} value={renameText} onChangeText={setRenameText} maxLength={SAVE_COLLECTION_NAME_MAX_LENGTH} testID={'saved-collection-rename-input-' + item.id} />
              <View style={{ flexDirection: 'row', gap: theme.spacing.sm }}>
                <Button label={t.rename} onPress={() => rename(item)} style={{ alignSelf: 'auto' }} testID={'saved-collection-rename-save-' + item.id} />
                <Button label={t.cancel} variant="ghost" onPress={() => setRenaming(null)} style={{ alignSelf: 'auto' }} />
              </View>
            </View>
          ) : (
            <View style={{ flexDirection: 'row', alignItems: 'center', gap: theme.spacing.xs }}>
              <AppText style={{ flex: 1, fontWeight: '600' }}>{item.name}</AppText>
              <IconButton icon="arrow-up" label={t.moveUp(item.name)} disabled={index === 0} onPress={() => move(item, -1)} testID={'saved-collection-up-' + item.id} />
              <IconButton icon="arrow-down" label={t.moveDown(item.name)} disabled={index === collections.items.length - 1} onPress={() => move(item, 1)} testID={'saved-collection-down-' + item.id} />
              <IconButton icon="create-outline" label={t.renameCollection(item.name)} onPress={() => { setRenaming(item.id); setRenameText(item.name); }} testID={'saved-collection-rename-' + item.id} />
              <IconButton icon="trash-outline" label={t.deleteCollection(item.name)} color="danger" onPress={() => remove(item)} testID={'saved-collection-delete-' + item.id} />
            </View>
          )}
        </View>
      ))}
    </Sheet>
  );
}

/** A saved item's note (visible to you and moderators) and its collections. */
function SaveDetailsSheet({ api, item, title, collections, onClose, onSaved }) {
  const theme = useTheme();
  const [note, setNote] = useState(item.note || '');
  const [chosen, setChosen] = useState(() => new Set(item.collectionIds || []));
  const [status, setStatus] = useState(null);
  async function submit() {
    setStatus(null);
    try {
      const next = await api.saveItem(item.targetType, item.targetId, { note: note.trim() ? note.trim() : null, collectionIds: [...chosen] });
      setStatus({ ok: true, text: t.noteSaved });
      onSaved(next || { ...item, note: note.trim() || null, collectionIds: [...chosen] });
    } catch {
      setStatus({ ok: false, text: t.noteFailed });
    }
  }
  return (
    <Sheet visible title={t.editNote(title)} onClose={onClose} testID="saved-item-sheet">
      <TextField label={t.noteLabel} hint={t.noteHint} value={note} onChangeText={setNote} maxLength={SAVE_NOTE_MAX_LENGTH} multiline testID="saved-item-note" />
      {collections.items.length ? (
        <View style={{ gap: theme.spacing.xs }}>
          <AppText variant="label">{t.addToCollectionsFor(title)}</AppText>
          {collections.items.map((collection) => (
            <CheckRow
              key={collection.id}
              label={collection.name}
              value={chosen.has(collection.id)}
              onValueChange={(on) => setChosen((current) => { const next = new Set(current); if (on) next.add(collection.id); else next.delete(collection.id); return next; })}
              testID={'saved-item-collection-' + collection.id}
            />
          ))}
        </View>
      ) : null}
      <Button label={t.noteSave} onPress={submit} testID="saved-item-save" />
      {status ? (
        <AppText color={status.ok ? 'success' : 'danger'} accessibilityRole="alert" testID="saved-item-status">
          {status.text}
        </AppText>
      ) : null}
    </Sheet>
  );
}

/**
 * The Library's Saved segment: the member's saves, newest first, filterable by type and by
 * collection, with collections to create, rename, reorder, and delete. A row opens its target;
 * the bookmark removes it at once and puts it back when the server refuses; the note button edits
 * the note (visible to you and moderators) and the item's collections. Targets the member can no
 * longer open show as unavailable without their title.
 */
export default function SavedList({ testID = 'training-saved' }) {
  const { api } = useAuth();
  const router = useRouter();
  const theme = useTheme();
  const [type, setType] = useState('');
  const [collectionId, setCollectionId] = useState('');
  const [removed, setRemoved] = useState(() => new Set());
  const [managing, setManaging] = useState(false);
  const [editing, setEditing] = useState(null);
  const [edits, setEdits] = useState(() => new Map());
  const collections = useSaveCollections(api);
  const key = type + '|' + collectionId;
  const fetchPage = useCallback(
    (continuationToken) => {
      const filters = {};
      if (type) filters.targetTypes = [type];
      if (collectionId) filters.collectionId = collectionId;
      return api.listSaves({ maxResults: 25, continuationToken, filters });
    },
    [api, type, collectionId]
  );
  const list = useCursorList(fetchPage, { key });
  const items = list.items.filter((item) => !removed.has(saveKey(item.targetType, item.targetId))).map((item) => edits.get(item.id) || item);
  const titleOf = (item) => (saveAvailable(item) ? item.target.title || t.type[item.targetType] || item.targetType : t.unavailable);

  async function remove(item) {
    const k = saveKey(item.targetType, item.targetId);
    setRemoved((current) => new Set(current).add(k));
    try {
      await api.unsaveItem(item.targetType, item.targetId);
      saveStateBatcher(api).set(item.targetType, item.targetId, { saved: false });
    } catch {
      setRemoved((current) => {
        const next = new Set(current);
        next.delete(k);
        return next;
      });
      AccessibilityInfo.announceForAccessibility(save.failed);
    }
  }

  const header = (
    <View style={{ gap: theme.spacing.md, paddingBottom: theme.spacing.md }}>
      <ChipGroup label={t.filterLabel} options={[{ value: '', label: t.all }, ...FavoriteTargetTypes.map((value) => ({ value, label: t.types[value] }))]} value={type} onToggle={(value) => setType(value === type ? '' : value)} testID={testID + '-type'} />
      {collections.items.length ? (
        <ChipGroup label={t.collectionsLabel} options={[{ value: '', label: t.allCollections }, ...collections.items.map((c) => ({ value: c.id, label: c.name }))]} value={collectionId} onToggle={(value) => setCollectionId(value === collectionId ? '' : value)} testID={testID + '-collection'} />
      ) : null}
      <Button label={t.manageCollections} variant="secondary" onPress={() => setManaging(true)} style={{ alignSelf: 'auto' }} testID={testID + '-manage'} />
      <AppText variant="caption" color="textMuted">
        {t.noteHint}
      </AppText>
      {list.status === 'loading' ? <Loading label={t.loading} /> : null}
      {list.status === 'error' ? <ErrorState title={t.loadFailed} onRetry={list.reload} testID={testID + '-error'} /> : null}
      {list.status === 'ready' && !items.length ? (
        <Card testID={testID + '-empty'}>
          <AppText>{type || collectionId ? t.emptyFiltered : t.empty}</AppText>
        </Card>
      ) : null}
    </View>
  );

  return (
    <>
      <FlashList
        testID={testID}
        data={list.status === 'ready' ? items : []}
        keyExtractor={(item) => item.id || saveKey(item.targetType, item.targetId)}
        ListHeaderComponent={header}
        renderItem={({ item }) => {
          const available = saveAvailable(item);
          const path = available ? saveTargetPath(item.targetType, item.targetId) : null;
          const title = titleOf(item);
          const rowId = item.id || item.targetId;
          return (
            <View style={{ flexDirection: 'row', alignItems: 'center', gap: theme.spacing.xs, paddingBottom: theme.spacing.sm }}>
              <Pressable
                onPress={path ? () => router.push(path) : undefined}
                disabled={!path}
                accessibilityRole="button"
                accessibilityLabel={[title, t.type[item.targetType]].filter(Boolean).join(', ')}
                accessibilityState={{ disabled: !path }}
                testID={testID + '-row-' + rowId}
                style={({ pressed }) => ({ flex: 1, minHeight: theme.layout.minTouchTarget, flexDirection: 'row', alignItems: 'center', gap: theme.spacing.sm, padding: theme.spacing.md, borderRadius: theme.radii.lg, borderWidth: 1, borderColor: theme.colors.border, backgroundColor: theme.colors.surface, opacity: !available ? 0.6 : pressed ? 0.85 : 1 })}
              >
                <View style={{ flex: 1 }}>
                  <AppText style={{ fontWeight: '600' }}>{title}</AppText>
                  <AppText variant="caption" color="textMuted">
                    {t.type[item.targetType] || item.targetType}
                  </AppText>
                  {item.note ? <AppText variant="caption">{item.note}</AppText> : null}
                </View>
                {path ? <Ionicons name="chevron-forward" size={18} color={theme.colors.textMuted} /> : null}
              </Pressable>
              <IconButton icon="create-outline" label={t.editNote(title)} onPress={() => setEditing(item)} testID={testID + '-edit-' + rowId} />
              <IconButton icon="bookmark" color="primary" label={t.remove(title)} onPress={() => remove(item)} testID={testID + '-remove-' + rowId} />
            </View>
          );
        }}
        ListFooterComponent={
          <View style={{ paddingVertical: theme.spacing.lg }}>
            {list.loadingMore ? <Loading label={t.loadMore} /> : null}
            {list.moreError ? <Button label={t.loadMore} variant="secondary" onPress={list.loadMore} /> : null}
          </View>
        }
        onEndReached={list.loadMore}
        onEndReachedThreshold={0.5}
        refreshControl={<RefreshControl refreshing={Boolean(list.refreshing)} onRefresh={list.refresh} tintColor={theme.colors.primary} />}
        contentContainerStyle={{ padding: theme.spacing.lg }}
      />
      {managing ? <CollectionsSheet api={api} collections={collections} onClose={() => setManaging(false)} /> : null}
      {editing ? (
        <SaveDetailsSheet
          api={api}
          item={editing}
          title={titleOf(editing)}
          collections={collections}
          onClose={() => setEditing(null)}
          onSaved={(next) => setEdits((current) => new Map(current).set(editing.id, { ...editing, ...next, target: editing.target }))}
        />
      ) : null}
    </>
  );
}

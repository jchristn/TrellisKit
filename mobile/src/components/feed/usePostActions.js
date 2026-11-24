import { useRouter } from 'expo-router';
import { useState } from 'react';
import { feed as feedApi } from '../../api/community';
import { describeContentError } from '../../content/errors';
import strings from '../../strings';

/**
 * Menu actions for a post, driven by its `viewer` capabilities (never re-derived): edit, delete,
 * pin, edit history, report, and block the author.
 */
export function usePostActions({ api, user, post, onChange, onDeleted }) {
  const router = useRouter();
  const [menuOpen, setMenuOpen] = useState(false);
  const [sheet, setSheet] = useState(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(null);
  const viewer = (post && post.viewer) || {};
  const author = post && post.author;
  const isMine = Boolean(user && author && author.id === user.id);

  const items = [];
  if (viewer.canEdit) items.push({ key: 'edit', label: strings.postMenu.edit, onPress: () => router.push({ pathname: '/compose', params: { id: post.id } }) });
  if (post && post.editedUtc) items.push({ key: 'history', label: strings.postMenu.history, onPress: () => setSheet('history') });
  if (viewer.canPin) items.push({ key: 'pin', label: post.isPinned ? strings.postMenu.unpin : strings.postMenu.pin, onPress: () => togglePin() });
  if (viewer.canReport && !isMine) items.push({ key: 'report', label: strings.postMenu.report, onPress: () => setSheet('report') });
  if (user && author && author.id && !author.isDeleted && !author.isStaff && !isMine) items.push({ key: 'block', label: strings.postMenu.block(author.handle || author.displayName), onPress: () => setSheet('block'), destructive: true });
  if (viewer.canDelete) items.push({ key: 'delete', label: strings.postMenu.delete, onPress: () => setSheet('delete'), destructive: true });

  async function togglePin() {
    setError(null);
    try {
      const next = post.isPinned ? await feedApi.unpin(api, post.id) : await feedApi.pin(api, post.id);
      if (next) onChange(next);
    } catch (e) {
      setError(describeContentError(e));
    }
  }

  async function remove() {
    setBusy(true);
    setError(null);
    try {
      await feedApi.remove(api, post.id);
      setSheet(null);
      if (onDeleted) onDeleted(post.id);
    } catch (e) {
      setError(describeContentError(e));
    } finally {
      setBusy(false);
    }
  }

  return { items, menuOpen, setMenuOpen, sheet, setSheet, busy, error, setError, remove };
}

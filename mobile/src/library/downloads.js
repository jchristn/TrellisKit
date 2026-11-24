import { File } from 'expo-file-system';
import { useEffect, useState } from 'react';
import { Platform } from 'react-native';
import { getInstallId } from '../updates/installId';
import { downloadsFolder, fileStorage } from './storage';

/**
 * Offline lesson downloads (docs/PHASE4_CONTRACT.md section 5.9), only when the property turned
 * them on (`library.downloadsEnabled`) and the lesson's `viewer.canDownload` is true. The app asks
 * for a license (`POST /lessons/{id}/downloads`), downloads the progressive MP4 from the short-lived
 * URL into its private document folder (never the photo library or shared storage), and keeps an
 * index by license id. Licenses are renewed online when they are less than 7 days from expiry;
 * files are deleted when the license expires, when renewal answers `403` or `404`, and on
 * sign-out. The `libraryDownloads` kill switch (`503 FeatureDisabled`) pauses downloads and
 * renewals without deleting anything.
 */

const INDEX_KEY = 'downloads-index';
const RENEW_WITHIN_MS = 7 * 86400000;
const listeners = new Set();
let paused = false;

function emit() {
  for (const fn of Array.from(listeners)) fn();
}

/** Subscribe to download index changes. */
export function onDownloadsChange(fn) {
  listeners.add(fn);
  return () => listeners.delete(fn);
}

/** Whether downloads are paused by the kill switch in this session. */
export function downloadsPaused() {
  return paused;
}

/** Clear the paused flag (tests). */
export function resetDownloadsState() {
  paused = false;
}

/** The download index: `[{ id, lessonId, title, durationSeconds, fileUri, sizeBytes, licenseExpiresUtc, downloadedUtc }]`. */
export async function listDownloads() {
  const raw = await fileStorage.getItem(INDEX_KEY);
  if (!raw) return [];
  try {
    const list = JSON.parse(raw);
    return Array.isArray(list) ? list.filter((d) => d && d.id && d.lessonId) : [];
  } catch {
    return [];
  }
}

async function saveIndex(list) {
  if (list.length) await fileStorage.setItem(INDEX_KEY, JSON.stringify(list));
  else await fileStorage.removeItem(INDEX_KEY);
  emit();
}

/** The local download of a lesson, or null. */
export async function findDownload(lessonId) {
  return (await listDownloads()).find((d) => d.lessonId === lessonId) || null;
}

function deleteFile(uri) {
  try {
    const file = new File(uri);
    if (file.exists) file.delete();
  } catch {
    // Already gone.
  }
}

function isPauseError(error) {
  return Boolean(error && (error.code === 'FeatureDisabled' || error.status === 503));
}

/**
 * Download a lesson for offline viewing.
 * @param {object} api
 * @param {object} lesson Lesson (with `viewer.canDownload`).
 * @param {{ onProgress?: (fraction: number) => void }} [options]
 * @returns {Promise<object>} the index entry
 * @throws {ApiError|Error} `DownloadsNotAllowed`, `DownloadLimitReached`, `FeatureDisabled`, or a transfer error
 */
export async function downloadLesson(api, lesson, { onProgress } = {}) {
  const deviceId = await getInstallId();
  let license;
  try {
    license = await api.createLessonDownload(lesson.id, { deviceId, platform: Platform.OS === 'android' ? 'Android' : 'Ios' });
  } catch (error) {
    if (isPauseError(error)) {
      paused = true;
      emit();
    }
    throw error;
  }
  const destination = new File(downloadsFolder(), license.id + '.mp4');
  const file = await File.downloadFileAsync(license.url, destination, {
    idempotent: true,
    onProgress: onProgress ? (p) => p && p.totalBytes && onProgress(Math.min(1, p.bytesWritten / p.totalBytes)) : undefined
  });
  const entry = {
    id: license.id,
    lessonId: lesson.id,
    title: lesson.title,
    seriesTitle: lesson.series ? lesson.series.title : null,
    durationSeconds: lesson.durationSeconds || null,
    fileUri: (file && file.uri) || destination.uri,
    sizeBytes: license.sizeBytes || (file && file.size) || 0,
    licenseExpiresUtc: license.licenseExpiresUtc,
    downloadedUtc: new Date().toISOString()
  };
  const list = (await listDownloads()).filter((d) => d.lessonId !== lesson.id);
  await saveIndex(list.concat(entry));
  return entry;
}

/**
 * Remove a download: the file and the index entry, and the license on the server (best effort).
 * @param {object|null} api Pass null to remove locally only.
 * @param {object} entry
 */
export async function removeDownload(api, entry) {
  deleteFile(entry.fileUri);
  await saveIndex((await listDownloads()).filter((d) => d.id !== entry.id));
  if (api) await api.deleteLessonDownload(entry.id).catch(() => null);
}

/** Delete every local download without calling the server (sign-out, account deletion). */
export async function clearAllDownloads() {
  for (const entry of await listDownloads()) deleteFile(entry.fileUri);
  await saveIndex([]);
}

/**
 * Keep downloads valid: delete expired ones, renew those within 7 days of expiry, and delete those
 * the server no longer allows (`403`, `404`). A `503` pauses and stops (kill switch); a network
 * error leaves everything for the next run.
 * @param {object} api
 * @param {{ now?: number }} [options]
 * @returns {Promise<{ renewed: number, removed: number, paused: boolean }>}
 */
export async function maintainDownloads(api, { now = Date.now() } = {}) {
  let renewed = 0;
  let removed = 0;
  let list = await listDownloads();
  for (const entry of list.slice()) {
    const expires = Date.parse(entry.licenseExpiresUtc);
    if (!Number.isFinite(expires) || expires <= now) {
      deleteFile(entry.fileUri);
      list = list.filter((d) => d.id !== entry.id);
      removed += 1;
      continue;
    }
    if (expires - now > RENEW_WITHIN_MS) continue;
    try {
      const next = await api.renewLessonDownload(entry.id);
      list = list.map((d) => (d.id === entry.id ? { ...d, licenseExpiresUtc: (next && next.licenseExpiresUtc) || d.licenseExpiresUtc } : d));
      renewed += 1;
    } catch (error) {
      if (isPauseError(error)) {
        paused = true;
        await saveIndex(list);
        return { renewed, removed, paused: true };
      }
      if (error && (error.status === 403 || error.status === 404)) {
        deleteFile(entry.fileUri);
        list = list.filter((d) => d.id !== entry.id);
        removed += 1;
      }
    }
  }
  paused = false;
  await saveIndex(list);
  return { renewed, removed, paused: false };
}

/** The local download index as React state, refreshed on every change. */
export function useDownloads() {
  const [state, setState] = useState({ items: [], loaded: false, paused });
  useEffect(() => {
    let active = true;
    const load = () => listDownloads().then((items) => active && setState({ items, loaded: true, paused }));
    load();
    const off = onDownloadsChange(load);
    return () => {
      active = false;
      off();
    };
  }, []);
  return state;
}

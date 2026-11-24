import { useMemo } from 'react';
import useLocalStorage from './useLocalStorage.js';

/** Column keys that are secondary on a wide table: hidden until the viewer turns them on. */
const SECONDARY = /(^id$|Id$|^tags$|^labels$|^slug$|updated|lastUpdate|modified|createdBy|ipAddress|userAgent|hash$|checksum|externalKey|fingerprint)/i;

/** A table wider than this many columns starts with its secondary columns hidden. */
export const WIDE_TABLE_COLUMNS = 8;

/** Whether a column can never be hidden: marked pinned, the first column, a row action or selection column, or one without a name. */
export function isPinnedColumn(col, index) {
  if (col.pinned) return true;
  if (index === 0) return true;
  const cls = String(col.className || '');
  if (cls.includes('cell-actions') || cls.includes('cell-check')) return true;
  return columnName(col) === '';
}

/** The name of a column in the chooser: its text label, else `selectorLabel`. */
export function columnName(col) {
  if (typeof col.selectorLabel === 'string' && col.selectorLabel) return col.selectorLabel;
  return typeof col.label === 'string' ? col.label.trim() : '';
}

/**
 * The columns hidden before the viewer chooses: every column marked `defaultHidden`, and on a
 * table with more than WIDE_TABLE_COLUMNS columns, the secondary ones (ids, updated times, labels
 * and tags, and the like), so the important columns fit without wrapping.
 * @param {Array<object>} columns
 * @returns {string[]}
 */
export function defaultHiddenColumns(columns) {
  const wide = columns.length > WIDE_TABLE_COLUMNS;
  return columns
    .filter((col, index) => !isPinnedColumn(col, index) && (col.defaultHidden === true || (wide && col.defaultHidden !== false && SECONDARY.test(col.key))))
    .map((col) => col.key);
}

/** A stable id for a table from its column keys, used when the caller gives none. */
export function tableSignature(columns) {
  return 'cols:' + columns.map((c) => c.key).join(',');
}

/**
 * Which columns of a table are shown, remembered in this browser per table. The choice is kept in
 * localStorage under `tk.table.<id>.hiddenColumns`, so it is there on the next visit.
 * @param {string|undefined} tableId A stable id; defaults to a signature of the column keys.
 * @param {Array<object>} columns Every column of the table.
 * @returns {{ hidden: string[], setHidden: (keys: string[]) => void, visible: Array<object>, choices: Array<{ key: string, label: string, pinned: boolean }> }}
 */
export function useTableColumns(tableId, columns) {
  const id = tableId || tableSignature(columns);
  const defaults = useMemo(() => defaultHiddenColumns(columns), [columns]);
  const [stored, setStored] = useLocalStorage('tk.table.' + id + '.hiddenColumns', null);
  const hidden = Array.isArray(stored) ? stored : defaults;
  const visible = columns.filter((col, index) => isPinnedColumn(col, index) || !hidden.includes(col.key));
  const choices = columns.map((col, index) => ({ key: col.key, label: columnName(col) || col.key, pinned: isPinnedColumn(col, index) })).filter((c, index) => columnName(columns[index]) !== '');
  return { hidden, setHidden: setStored, visible, choices };
}

export default useTableColumns;

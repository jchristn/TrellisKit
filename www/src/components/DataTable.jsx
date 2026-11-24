import { useRef } from 'react';
import { createPortal } from 'react-dom';
import strings from '../strings.js';
import { Loading, ErrorBanner } from './States.jsx';
import Icon from './Icon.jsx';
import ColumnSelector from './ColumnSelector.jsx';
import useTableColumns from '../hooks/useTableColumns.js';
import useColumnsSlot from '../hooks/useColumnsSlot.js';

const INTERACTIVE = 'button, a, input, select, textarea, label, [role="menu"], [data-ignore-row-click]';

/**
 * Table body with loading, error, and empty states. Toolbar and pagination live outside so
 * they stay visible while the table scrolls horizontally.
 *
 * @param {{ columns: Array<{ key: string, label: React.ReactNode, render?: (row: object) => React.ReactNode, className?: string, headerClassName?: string }>,
 *   rows: Array<object>, rowKey: (row: object) => string, loading?: boolean, error?: string, onRetry?: () => void,
 *   emptyMessage?: React.ReactNode, onRowClick?: (row: object) => void, rowClassName?: (row: object) => string, caption?: string,
 *   tableId?: string, columnsControl?: boolean, columnChoices?: Array<{ key: string, label: string, pinned?: boolean }>,
 *   columnsHidden?: string[], onColumnsChange?: (hidden: string[]) => void }} props
 *
 * Every table has a "Columns" control that chooses which columns show. It sits in the pagination bar
 * that goes with the table (same row as rows per page, paging, and refresh); a table without a
 * pagination bar shows it as an icon in its header corner.
 * The choice is remembered in this browser per table (`tableId`, or a signature of the column
 * keys). A column may set `pinned` (always shown), `defaultHidden` (hidden until chosen), and
 * `selectorLabel` (its name in the chooser when `label` is not text). Wide tables start with their
 * secondary columns hidden (hooks/useTableColumns.js). A page that already offers its own column
 * column state passes `columnChoices`, `columnsHidden`, and `onColumnsChange` (and filtered `columns`);
 * `columnsControl={false}` removes the control.
 */
/** Parse a sort string (`field` or `-field`). */
export function parseSort(sort) {
  const text = String(sort || '');
  if (!text) return { field: '', direction: '' };
  return text.startsWith('-') ? { field: text.slice(1), direction: 'desc' } : { field: text, direction: 'asc' };
}

/** Next sort string after clicking a column: flips the active column, else uses its default direction. */
export function nextSort(current, field, defaultDirection = 'asc') {
  const { field: activeField, direction } = parseSort(current);
  if (activeField === field) return direction === 'asc' ? '-' + field : field;
  return defaultDirection === 'desc' ? '-' + field : field;
}

function SortHeader({ col, sort, onSortChange }) {
  const { field, direction } = parseSort(sort);
  const active = field === col.sortField;
  const ariaSort = active ? (direction === 'asc' ? 'ascending' : 'descending') : 'none';
  const label = typeof col.label === 'string' ? col.label : (col.selectorLabel || col.key);
  return (
    <th scope="col" className={(col.headerClassName || col.className || '') + ' sortable'} aria-sort={ariaSort}>
      <button type="button" className={'sort-button' + (active ? ' active' : '')} onClick={() => onSortChange(nextSort(sort, col.sortField, col.sortDefault))} title={strings.admin.common.sortBy(label)}>
        <span>{col.label}</span>
        <Icon name={active ? (direction === 'asc' ? 'chevronUp' : 'chevronDown') : 'sort'} size={12} />
      </button>
    </th>
  );
}

function DataTable({ columns: allColumns, rows, rowKey, loading = false, error = null, onRetry, emptyMessage, onRowClick, rowClassName, caption, sort, onSortChange, tableId, columnsControl = true, columnChoices, columnsHidden, onColumnsChange }) {
  const frameRef = useRef(null);
  const prefs = useTableColumns(tableId, allColumns);
  // A page that keeps its own column state passes the choices, the hidden keys, and the setter, with `columns`
  // already filtered; otherwise the table keeps the state itself.
  const controlled = Array.isArray(columnChoices);
  const columns = controlled || !columnsControl ? allColumns : prefs.visible;
  const choices = controlled ? columnChoices : prefs.choices;
  const hidden = controlled ? columnsHidden || [] : prefs.hidden;
  const setHidden = controlled ? onColumnsChange : prefs.setHidden;
  const showControl = (controlled || columnsControl) && choices.some((c) => !c.pinned);
  // The control lives in the pagination bar that goes with the table; a table without one shows it in its header corner.
  const slot = useColumnsSlot(frameRef, showControl, loading);
  const handleClick = (e, row) => {
    if (!onRowClick) return;
    if (e.target instanceof Element && e.target.closest(INTERACTIVE)) return;
    onRowClick(row);
  };

  return (
    <div className={'table-frame' + (showControl && !slot ? ' has-columns-corner' : '')} ref={frameRef}>
      {error && <ErrorBanner message={error} onRetry={onRetry} />}
      {showControl && slot && createPortal(<ColumnSelector columns={choices} hidden={hidden} onChange={setHidden} />, slot)}
      {showControl && !slot && (
        <div className="table-columns-corner">
          <ColumnSelector columns={choices} hidden={hidden} onChange={setHidden} compact />
        </div>
      )}
      <div className="table-scroll">
        <table className="data-table data-table-auto">
          {caption && <caption className="visually-hidden">{caption}</caption>}
          <thead>
            <tr>
              {columns.map((col) => (col.sortField && onSortChange
                ? <SortHeader key={col.key} col={col} sort={sort} onSortChange={onSortChange} />
                : <th key={col.key} scope="col" className={col.headerClassName || col.className}>{col.label}</th>))}
            </tr>
          </thead>
          <tbody>
            {rows.map((row) => (
              <tr
                key={rowKey(row)}
                className={(onRowClick ? 'clickable ' : '') + (rowClassName ? rowClassName(row) : '')}
                onClick={(e) => handleClick(e, row)}
              >
                {columns.map((col) => <td key={col.key} className={col.className}>{col.render ? col.render(row) : row[col.key]}</td>)}
              </tr>
            ))}
            {!loading && rows.length === 0 && !error && (
              <tr><td colSpan={columns.length} className="table-empty">{emptyMessage}</td></tr>
            )}
          </tbody>
        </table>
        {loading && <div className="table-loading"><Loading inline /></div>}
      </div>
    </div>
  );
}

export default DataTable;

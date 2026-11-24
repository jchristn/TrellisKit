import { useState } from 'react';
import strings from '../strings.js';

const c = strings.admin.common;

/**
 * Row selection for a server-side table page. The selection is tied to `resetKey` (for example the
 * serialized query), so it clears whenever the page, filters, or sort change. `column(labelOf)`
 * returns a pinned checkbox column for DataTable.
 * @param {Array<object>} rows rows on the current page
 * @param {string} resetKey
 * @param {(row: object) => string} [idOf]
 */
export function useRowSelection(rows, resetKey, idOf = (row) => row.id) {
  const [selection, setSelection] = useState({ key: '', ids: [] });
  const selected = selection.key === resetKey ? selection.ids : [];
  const setSelected = (ids) => setSelection({ key: resetKey, ids });
  const pageIds = rows.map(idOf);
  const allSelected = pageIds.length > 0 && pageIds.every((id) => selected.includes(id));
  const toggle = (id) => setSelected(selected.includes(id) ? selected.filter((x) => x !== id) : [...selected, id]);
  const column = (labelOf) => ({
    key: 'select', pinned: true, selectorLabel: c.selectAll, className: 'cell-check',
    label: <input type="checkbox" checked={allSelected} onChange={() => setSelected(allSelected ? [] : pageIds)} aria-label={c.selectAll} disabled={pageIds.length === 0} />,
    render: (row) => <input type="checkbox" checked={selected.includes(idOf(row))} onChange={() => toggle(idOf(row))} aria-label={c.selectRow(labelOf(row))} />
  });
  return { selected, setSelected, clear: () => setSelected([]), allSelected, toggle, column };
}

export default useRowSelection;

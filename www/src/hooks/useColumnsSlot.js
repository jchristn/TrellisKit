import { useLayoutEffect, useState } from 'react';

/**
 * Finds where a table's "Columns" control belongs: the slot in the pagination bar that goes with
 * the table (TablePagination renders `[data-columns-slot]`), so the control sits on the same row as
 * rows per page, paging, and refresh. The search walks up from the table and stops at the first
 * ancestor that holds a pagination bar; it takes the nearest free slot before the table, else the
 * first free one. A table with no pagination bar gets null, and shows the control in its own
 * header corner instead. A slot serves one table.
 * @param {{ current: Element|null }} ref The table's outer element.
 * @param {boolean} enabled Whether the table has a Columns control at all.
 * @param {unknown} [refreshKey] Changes when the surrounding page may have re-rendered its pagination bar.
 * @returns {Element|null}
 */
export function useColumnsSlot(ref, enabled, refreshKey) {
  const [slot, setSlot] = useState(null);
  useLayoutEffect(() => {
    const table = ref.current;
    if (!enabled || !table) return undefined;
    let found = null;
    let node = table.parentElement;
    for (let depth = 0; node && depth < 8; depth += 1, node = node.parentElement) {
      if (!node.querySelector('[data-columns-slot]')) continue;
      const free = Array.from(node.querySelectorAll('[data-columns-slot]:not([data-claimed])'));
      const before = free.filter((s) => table.compareDocumentPosition(s) & Node.DOCUMENT_POSITION_PRECEDING);
      found = before.length ? before[before.length - 1] : free[0] || null;
      break;
    }
    if (found) found.setAttribute('data-claimed', '');
    setSlot(found);
    return () => {
      if (found) found.removeAttribute('data-claimed');
    };
  }, [ref, enabled, refreshKey]);
  return enabled ? slot : null;
}

export default useColumnsSlot;

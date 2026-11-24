import { useId, useLayoutEffect, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import ColumnSelector from './ColumnSelector.jsx';
import useLocalStorage from '../hooks/useLocalStorage.js';
import useColumnsSlot from '../hooks/useColumnsSlot.js';
import { WIDE_TABLE_COLUMNS } from '../hooks/useTableColumns.js';

/** Header names of secondary columns on a wide hand-written table. */
const SECONDARY_HEADER = /^(id|slug|key|updated|last updated|modified|labels|tags|labels and tags)$/i;

/**
 * A "Columns" control for a hand-written table (one that does not use DataTable). Wrap the table:
 * the column names are read from its header row, the viewer chooses which to show, and the choice
 * is remembered in this browser under `tk.table.<id>.hiddenColumns`. Hidden columns are hidden with
 * a style rule on the cell position, so the table's own markup is untouched. The control sits in
 * the pagination bar that goes with the table, or as an icon in the table's header corner. The first column and
 * columns without a header (row actions) are always shown; cells that span columns are left alone.
 * @param {{ id: string, children: React.ReactNode }} props
 */
function TableColumns({ id, children }) {
  const ref = useRef(null);
  const scope = useId().replace(/[^a-zA-Z0-9]/g, '');
  const [names, setNames] = useState([]);
  const [hidden, setHidden] = useLocalStorage('tk.table.' + id + '.hiddenColumns', null);

  useLayoutEffect(() => {
    const node = ref.current;
    if (!node) return undefined;
    const read = () => {
      const row = node.querySelector('thead tr');
      const next = row ? Array.from(row.children).map((th) => (th.getAttribute('data-column') || th.textContent || '').trim()) : [];
      // Compared as text so an unchanged header does not render again.
      setNames((prev) => (prev.join('\u0001') === next.join('\u0001') ? prev : next));
    };
    read();
    // The header can change after the first render (a table that loads, a tab that swaps it).
    if (typeof MutationObserver !== 'function') return undefined;
    const observer = new MutationObserver(read);
    observer.observe(node, { childList: true, subtree: true, characterData: true });
    return () => observer.disconnect();
  }, []);

  const columns = names.map((label, index) => ({ key: String(index), label, pinned: index === 0 || label === '' })).filter((c) => c.label !== '');
  // Until the viewer chooses, a wide table starts with its secondary columns (ids, slugs, updated times) hidden.
  const defaults = columns.length > WIDE_TABLE_COLUMNS ? columns.filter((c) => !c.pinned && SECONDARY_HEADER.test(c.label)).map((c) => c.key) : [];
  const hiddenNow = (Array.isArray(hidden) ? hidden : defaults).filter((key) => columns.some((c) => c.key === key && !c.pinned));
  const css = hiddenNow
    .map((key) => {
      const n = Number(key) + 1;
      const base = '[data-table-columns="' + scope + '"] ';
      return base + 'thead > tr > :nth-child(' + n + '):not([colspan]), ' + base + 'tbody > tr > :nth-child(' + n + '):not([colspan]) { display: none; }';
    })
    .join('\n');

  const showControl = columns.some((c) => !c.pinned);
  // In the pagination bar that goes with the table when there is one; otherwise an icon in the header corner.
  const slot = useColumnsSlot(ref, showControl, names.length);
  return (
    <div className={'table-columns' + (showControl && !slot ? ' has-columns-corner' : '')} data-table-columns={scope} ref={ref}>
      {showControl && slot && createPortal(<ColumnSelector columns={columns} hidden={hiddenNow} onChange={setHidden} />, slot)}
      {showControl && !slot && (
        <div className="table-columns-corner">
          <ColumnSelector columns={columns} hidden={hiddenNow} onChange={setHidden} compact />
        </div>
      )}
      {css && <style>{css}</style>}
      {children}
    </div>
  );
}

export default TableColumns;

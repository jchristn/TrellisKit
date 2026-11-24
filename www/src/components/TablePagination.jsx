import { useState } from 'react';
import strings from '../strings.js';
import { PAGE_SIZES } from '../utils/query.js';
import { formatNumber } from '../utils/format.js';
import Icon from './Icon.jsx';

const t = strings.admin.table;

/**
 * Above-table pagination bar: total and visible range, page size, first/previous/jump/next/last,
 * refresh, and the table's Columns control (the table places it in the slot at the end). Pages are
 * 1-based.
 */
function TablePagination({ pageNumber, pageSize, totalCount, totalPages, onPageChange, onPageSizeChange, onRefresh, loading = false, pageSizes = PAGE_SIZES }) {
  const [jumpText, setJumpText] = useState('');
  const pages = Math.max(1, totalPages || 1);
  const from = totalCount === 0 ? 0 : (pageNumber - 1) * pageSize + 1;
  const to = Math.min(totalCount, pageNumber * pageSize);

  const jump = (e) => {
    e.preventDefault();
    const n = parseInt(jumpText, 10);
    if (Number.isFinite(n)) onPageChange(Math.min(pages, Math.max(1, n)));
    setJumpText('');
  };

  return (
    <div className="table-pagination" role="navigation" aria-label={t.paginationLabel}>
      <div className="pagination-summary">
        <span>{totalCount === 0 ? t.showingNone : t.showing(formatNumber(from), formatNumber(to), formatNumber(totalCount))}</span>
        <span className="muted">{t.page(pageNumber, pages)}</span>
      </div>
      <div className="pagination-controls">
        <label className="inline-field">
          <span className="label-text">{t.pageSize}</span>
          <select className="select-compact" value={pageSize} onChange={(e) => onPageSizeChange(Number(e.target.value))} disabled={loading}>
            {pageSizes.map((size) => <option key={size} value={size}>{size}</option>)}
          </select>
        </label>
        <button type="button" className="icon-button" onClick={() => onPageChange(1)} disabled={loading || pageNumber <= 1} aria-label={t.first} title={t.first}>
          <Icon name="chevronsLeft" />
        </button>
        <button type="button" className="icon-button" onClick={() => onPageChange(pageNumber - 1)} disabled={loading || pageNumber <= 1} aria-label={t.previous} title={t.previous}>
          <Icon name="chevronLeft" />
        </button>
        <form className="jump-form" onSubmit={jump}>
          <input
            type="number"
            min="1"
            max={pages}
            className="input-compact jump-input"
            value={jumpText}
            onChange={(e) => setJumpText(e.target.value)}
            placeholder={String(pageNumber)}
            aria-label={t.jumpTo}
            disabled={loading}
          />
          <button type="submit" className="button button-secondary button-sm" disabled={loading}>{t.go}</button>
        </form>
        <button type="button" className="icon-button" onClick={() => onPageChange(pageNumber + 1)} disabled={loading || pageNumber >= pages} aria-label={t.next} title={t.next}>
          <Icon name="chevronRight" />
        </button>
        <button type="button" className="icon-button" onClick={() => onPageChange(pages)} disabled={loading || pageNumber >= pages} aria-label={t.last} title={t.last}>
          <Icon name="chevronsRight" />
        </button>
        {onRefresh && (
          <button type="button" className="icon-button" onClick={onRefresh} disabled={loading} aria-label={strings.common.refresh} title={strings.common.refresh}>
            <Icon name="refresh" className={loading ? 'spin' : ''} />
          </button>
        )}
        {/* The table that goes with this bar puts its Columns control here (hooks/useColumnsSlot.js). */}
        <span className="pagination-columns" data-columns-slot="" />
      </div>
    </div>
  );
}

export default TablePagination;

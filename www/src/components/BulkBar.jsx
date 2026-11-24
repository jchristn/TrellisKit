import strings from '../strings.js';
import Icon from './Icon.jsx';

const c = strings.admin.common;

/**
 * Bulk action bar shown while table rows are selected: the selected count, the list's bulk
 * actions (`children`), "Labels and tags" when `onMetadata` is given, and Clear selection.
 * @param {{ count: number, onClear: () => void, onMetadata?: () => void, children?: React.ReactNode }} props
 */
function BulkBar({ count, onClear, onMetadata, children }) {
  if (!count) return null;
  return (
    <div className="bulk-bar" role="region" aria-label={c.selectedCount(count)}>
      <span>{c.selectedCount(count)}</span>
      {children}
      {onMetadata && (
        <button type="button" className="button button-secondary button-sm" onClick={onMetadata}>
          <Icon name="list" size={14} /><span>{strings.admin.bulkMetadata.action}</span>
        </button>
      )}
      <button type="button" className="button button-secondary button-sm" onClick={onClear}>{c.clearSelection}</button>
    </div>
  );
}

export default BulkBar;

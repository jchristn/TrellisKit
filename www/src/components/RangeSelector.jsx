import strings from '../strings.js';
import { RANGE_IDS } from '../utils/time.js';

/** Segmented control for chart ranges: Last Hour, Day, Week, Month. */
function RangeSelector({ value, onChange, disabled = false }) {
  return (
    <div className="segmented" role="group" aria-label={strings.admin.chart.rangeLabel}>
      {RANGE_IDS.map((id) => (
        <button
          key={id}
          type="button"
          className={'segment' + (value === id ? ' active' : '')}
          aria-pressed={value === id}
          onClick={() => onChange(id)}
          disabled={disabled}
        >
          {strings.admin.chart.ranges[id]}
        </button>
      ))}
    </div>
  );
}

export default RangeSelector;

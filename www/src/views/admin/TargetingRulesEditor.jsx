import { FLAG_ATTRIBUTES } from '@trelliskit/shared';
import strings from '../../strings.js';
import { splitList } from './LiveUpdateDialogs.jsx';

const fl = strings.admin.liveUpdates.flags;
const ATTRIBUTES = Object.keys(FLAG_ATTRIBUTES);

/** Editable rows from targeting rules (the flag rule shape; the rule value is not used). */
export function rulesToRows(rules) {
  return (rules || []).map((r) => ({
    id: r.id,
    rolloutPercentage: String(r.rolloutPercentage ?? 100),
    conditions: (r.conditions || []).map((c) => ({ attribute: c.attribute, operator: c.operator, values: (c.values || []).join(', '), argument: c.argument || '' }))
  }));
}

/** Targeting rules from editable rows: the first rule whose conditions all match shows the item; no rules means everyone. */
export function rowsToRules(rows) {
  return (rows || []).map((r) => ({
    id: r.id,
    rolloutPercentage: Number(r.rolloutPercentage) || 100,
    conditions: r.conditions.map((c) => ({
      attribute: c.attribute,
      operator: c.operator,
      values: c.operator === 'SemverRange' ? [String(c.values).trim()] : splitList(c.values),
      ...(c.argument && c.argument.trim() ? { argument: c.argument.trim() } : {})
    }))
  }));
}

/**
 * The targeting rule builder (the flag rule shape, LIVE_UPDATES_CONTRACT.md section 12.2) without a
 * rule value, for banners: each rule is a list of conditions (attribute, operator, values) and a rollout.
 * @param {{ rows: Array<object>, onChange: (rows: Array<object>) => void, errors?: Record<string, string>, help?: string }} props
 */
function TargetingRulesEditor({ rows, onChange, errors = {}, help = fl.rulesHelp }) {
  const setRule = (i, patch) => onChange(rows.map((r, j) => (j === i ? { ...r, ...patch } : r)));
  const setCondition = (i, ci, patch) => setRule(i, { conditions: rows[i].conditions.map((c, j) => (j === ci ? { ...c, ...patch } : c)) });
  const ruleErrors = Object.entries(errors).filter(([x]) => x.startsWith('rules'));
  return (
    <fieldset className="fieldset">
      <legend>{fl.rules}</legend>
      <p className="muted">{help}</p>
      {rows.map((rule, i) => (
        <div key={i} className="panel">
          {rule.conditions.map((cond, ci) => (
            <div key={ci} className="form-grid">
              <select aria-label={fl.attribute + ' ' + (i + 1) + '.' + (ci + 1)} value={cond.attribute} onChange={(e) => setCondition(i, ci, { attribute: e.target.value, operator: FLAG_ATTRIBUTES[e.target.value][0] })}>
                {ATTRIBUTES.map((a) => <option key={a} value={a}>{a}</option>)}
              </select>
              <select aria-label={fl.operator + ' ' + (i + 1) + '.' + (ci + 1)} value={cond.operator} onChange={(e) => setCondition(i, ci, { operator: e.target.value })}>
                {(FLAG_ATTRIBUTES[cond.attribute] || []).map((o) => <option key={o} value={o}>{o}</option>)}
              </select>
              <input aria-label={fl.values + ' ' + (i + 1) + '.' + (ci + 1)} value={cond.values} onChange={(e) => setCondition(i, ci, { values: e.target.value })} />
              <button type="button" className="button button-secondary button-sm" onClick={() => setRule(i, { conditions: rule.conditions.filter((_, j) => j !== ci) })} disabled={rule.conditions.length <= 1}>{strings.appContent.admin.removeCondition}</button>
            </div>
          ))}
          <div className="form-grid">
            <input aria-label={fl.ruleRollout + ' ' + (i + 1)} type="number" min="0.01" max="100" step="0.01" value={rule.rolloutPercentage} onChange={(e) => setRule(i, { rolloutPercentage: e.target.value })} />
          </div>
          <button type="button" className="button button-secondary button-sm" onClick={() => setRule(i, { conditions: [...rule.conditions, { attribute: 'platform', operator: 'In', values: '', argument: '' }] })}>{fl.addCondition}</button>
          {' '}
          <button type="button" className="button button-secondary button-sm" onClick={() => onChange(rows.filter((_, j) => j !== i))}>{fl.removeRule}</button>
        </div>
      ))}
      <button type="button" className="button button-secondary button-sm" onClick={() => onChange([...rows, { rolloutPercentage: '100', conditions: [{ attribute: 'platform', operator: 'In', values: '', argument: '' }] }])}>{fl.addRule}</button>
      {ruleErrors.length > 0 && <span className="field-error">{ruleErrors.map(([x, m]) => x + ': ' + m).join(' ')}</span>}
    </fieldset>
  );
}

export default TargetingRulesEditor;

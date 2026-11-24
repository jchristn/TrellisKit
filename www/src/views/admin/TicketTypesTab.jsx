import { useCallback, useState } from 'react';
import { ENUMERATION_MAX_RESULTS_ADMIN, normalizeEnumerationResult } from '@trelliskit/shared';
import strings from '../../strings.js';
import { useAdminAuth } from '../../context/AdminAuthContext.jsx';
import { useToast } from '../../context/ToastContext.jsx';
import useApiQuery from '../../hooks/useApiQuery.js';
import { errorMessage } from '../../utils/api.js';
import Modal from '../../components/Modal.jsx';
import Icon from '../../components/Icon.jsx';
import { Badge } from '../../components/Badges.jsx';
import { ErrorBanner, Loading } from '../../components/States.jsx';
import TableColumns from '../../components/TableColumns.jsx';

const s = strings.supportAdmin.types;

/** Validate a ticket type form. */
export function validateTicketType(form) {
  const problems = {};
  if (!/^[A-Za-z][A-Za-z0-9]{1,39}$/.test(String(form.key || ''))) problems.key = s.errors.key;
  const label = String(form.label || '').trim();
  if (label.length < 1 || label.length > 60) problems.label = s.errors.label;
  return problems;
}

function TypeEditor({ type, onClose, onSaved }) {
  const { client } = useAdminAuth();
  const toast = useToast();
  const [form, setForm] = useState(() => ({ key: type ? type.key : '', label: type ? type.label : '', description: type ? type.description || '' : '', helpText: type ? type.helpText || '' : '', recipients: type ? (type.staffRecipients || []).join(', ') : '' }));
  const [problems, setProblems] = useState({});
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const set = (key) => (e) => setForm((v) => ({ ...v, [key]: e.target.value }));
  const submit = async (e) => {
    e.preventDefault();
    const found = validateTicketType(form);
    setProblems(found);
    if (Object.keys(found).length) return;
    setBusy(true);
    const body = { key: form.key, label: form.label.trim(), description: form.description.trim(), helpText: form.helpText.trim(), staffRecipients: form.recipients.split(',').map((x) => x.trim()).filter(Boolean) };
    try {
      if (type) await client.adminUpdateTicketType(type.id, body);
      else await client.adminCreateTicketType(body);
      toast.success(s.saved);
      onSaved();
    } catch (err) {
      setError(errorMessage(err, strings.common.errorGeneric));
      setBusy(false);
    }
  };
  return (
    <Modal open onClose={onClose} title={type ? s.editTitle(type.label) : s.createTitle}>
      <form className="form-stack" onSubmit={submit} noValidate>
        <ErrorBanner message={error} onDismiss={() => setError('')} />
        <div className="field">
          <label className="label-text" htmlFor="tty-key">{s.key}</label>
          <input id="tty-key" className="mono" value={form.key} onChange={set('key')} disabled={Boolean(type)} aria-invalid={Boolean(problems.key)} />
          {problems.key && <span className="field-error">{problems.key}</span>}
        </div>
        <div className="field">
          <label className="label-text" htmlFor="tty-label">{s.label}</label>
          <input id="tty-label" value={form.label} maxLength={60} onChange={set('label')} aria-invalid={Boolean(problems.label)} />
          {problems.label && <span className="field-error">{problems.label}</span>}
        </div>
        <div className="field">
          <label className="label-text" htmlFor="tty-description">{s.description}</label>
          <input id="tty-description" value={form.description} maxLength={200} onChange={set('description')} />
        </div>
        <div className="field">
          <label className="label-text" htmlFor="tty-help">{s.helpText}</label>
          <textarea id="tty-help" className="textarea" rows={3} value={form.helpText} maxLength={1000} onChange={set('helpText')} />
        </div>
        <div className="field">
          <label className="label-text" htmlFor="tty-recipients">{s.recipients}</label>
          <input id="tty-recipients" value={form.recipients} onChange={set('recipients')} />
        </div>
        <div className="form-actions">
          <button type="submit" className="button button-primary" disabled={busy}>{strings.common.save}</button>
          <button type="button" className="button button-secondary" onClick={onClose}>{strings.common.cancel}</button>
        </div>
      </form>
    </Modal>
  );
}

/** Ticket types (SystemAdmin): create, edit, retire, and order. */
function TicketTypesTab() {
  const { client } = useAdminAuth();
  const toast = useToast();
  const fetcher = useCallback((signal) => client.adminEnumerateTicketTypes({ maxResults: ENUMERATION_MAX_RESULTS_ADMIN }, signal), [client]);
  const query = useApiQuery(fetcher);
  const loaded = query.data ? normalizeEnumerationResult(query.data).objects : [];
  const [order, setOrder] = useState(null);
  const [editing, setEditing] = useState(null);
  const rows = order ? order.map((id) => loaded.find((x) => x.id === id)).filter(Boolean) : loaded;

  const move = (index, delta) => {
    const ids = rows.map((x) => x.id);
    const target = index + delta;
    if (target < 0 || target >= ids.length) return;
    [ids[index], ids[target]] = [ids[target], ids[index]];
    setOrder(ids);
  };
  const saveOrder = async () => {
    try {
      await client.adminOrderTicketTypes(rows.filter((x) => !x.isRetired).map((x) => x.id));
      toast.success(s.orderSaved);
      setOrder(null);
      query.reload();
    } catch (err) {
      toast.error(errorMessage(err, strings.common.errorGeneric));
    }
  };
  const retire = async (type) => {
    try {
      await client.adminDeleteTicketType(type.id);
      toast.success(s.retiredDone);
      query.reload();
    } catch (err) {
      toast.error(errorMessage(err, strings.common.errorGeneric));
    }
  };

  return (
    <section className="panel">
      <div className="row-between">
        <button type="button" className="button button-primary button-sm" onClick={() => setEditing('new')}><Icon name="plus" size={14} /><span>{s.add}</span></button>
        {order && <span className="row-actions"><span className="muted small">{s.orderChanged}</span><button type="button" className="button button-secondary button-sm" onClick={saveOrder}>{s.saveOrder}</button></span>}
      </div>
      {query.loading && !query.data && <Loading />}
      <ErrorBanner message={query.error ? errorMessage(query.error, '') : ''} onRetry={query.reload} />
      {query.data && rows.length === 0 && <p className="muted">{s.empty}</p>}
      {rows.length > 0 && (
        <div className="table-scroll">
          <TableColumns id="tk.admin.TicketTypesTab"><table className="data-table">
            <caption className="visually-hidden">{strings.supportAdmin.tabs.types}</caption>
            <thead><tr>{['order', 'key', 'label', 'status', 'actions'].map((k) => <th key={k} scope="col">{s.columns[k]}</th>)}</tr></thead>
            <tbody>
              {rows.map((type, i) => (
                <tr key={type.id}>
                  <td className="nowrap">
                    <button type="button" className="icon-button" onClick={() => move(i, -1)} disabled={i === 0} aria-label={s.moveUp(type.label)} title={s.moveUp(type.label)}><Icon name="chevronUp" size={14} /></button>
                    <button type="button" className="icon-button" onClick={() => move(i, 1)} disabled={i === rows.length - 1} aria-label={s.moveDown(type.label)} title={s.moveDown(type.label)}><Icon name="chevronDown" size={14} /></button>
                  </td>
                  <td className="mono">{type.key}</td>
                  <td>{type.label}{type.helpText && <div className="muted small line-clamp">{type.helpText}</div>}</td>
                  <td><Badge tone={type.isRetired ? 'neutral' : 'success'}>{type.isRetired ? s.retired : s.active}</Badge></td>
                  <td className="nowrap">
                    <button type="button" className="button button-secondary button-sm" onClick={() => setEditing(type)}>{strings.admin.common.edit}</button>
                    {!type.isRetired && <button type="button" className="button button-danger-outline button-sm" onClick={() => retire(type)}>{s.retire}</button>}
                  </td>
                </tr>
              ))}
            </tbody>
          </table></TableColumns>
        </div>
      )}
      {editing && <TypeEditor type={editing === 'new' ? null : editing} onClose={() => setEditing(null)} onSaved={() => { setEditing(null); query.reload(); }} />}
    </section>
  );
}

export default TicketTypesTab;

import { useCallback, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { eventsConfig, formatEventShort, validateEventForm } from '@trelliskit/shared';
import strings from '../../strings.js';
import { useAuth } from '../../context/AuthContext.jsx';
import { useProperty } from '../../context/PropertyContext.jsx';
import { useToast } from '../../context/ToastContext.jsx';
import useCursorList from '../../hooks/useCursorList.js';
import useDocumentMeta from '../../hooks/useDocumentMeta.js';
import Icon from '../../components/Icon.jsx';
import PageHeader from '../../components/PageHeader.jsx';
import { Badge } from '../../components/Badges.jsx';
import { Banner, EmptyState, ErrorBanner, Loading } from '../../components/States.jsx';
import { useAccessPrompt } from '../../components/AccessPrompt.jsx';
import EventFormFields, { emptyEventForm, eventBodyFrom, validationShape } from '../../components/events/EventForm.jsx';
import { errorText } from '../../utils/engagement.js';
import { appPaths, phase4Errors } from '../../utils/phase4.js';

const t = strings.events;

/**
 * /app/events/propose (resolved decision 10): a member proposes a single event (no hosts, no
 * recurrence); event staff review it before publication and the proposer becomes a host.
 */
export function EventProposeView() {
  const { client } = useAuth();
  const { property, fill } = useProperty();
  const toast = useToast();
  const accessPrompt = useAccessPrompt();
  const navigate = useNavigate();
  const [form, setForm] = useState(() => emptyEventForm());
  const [note, setNote] = useState('');
  const [problems, setProblems] = useState({});
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  useDocumentMeta(strings.member.metaTitle(t.proposeTitle));
  const enabled = eventsConfig(property).proposals;
  const set = (changes) => setForm((f) => ({ ...f, ...changes }));

  const submit = async (e) => {
    e.preventDefault();
    const found = validateEventForm(validationShape(form));
    setProblems(found);
    if (Object.keys(found).length) return;
    setBusy(true);
    setError('');
    try {
      await client.proposeEvent({ ...eventBodyFrom(form), proposalNote: note.trim() || undefined });
      toast.success(t.proposeSent);
      navigate(appPaths.myProposals);
    } catch (err) {
      if (!accessPrompt.handle(err)) {
        setProblems((p) => ({ ...p, ...((err && err.fieldErrors) || {}) }));
        setError(errorText(err, phase4Errors()));
      }
      setBusy(false);
    }
  };

  return (
    <div className="member-view">
      <Link to={appPaths.events} className="back-link small"><Icon name="chevronLeft" size={14} /><span>{t.back}</span></Link>
      <PageHeader title={t.proposeTitle} summary={fill(t.proposeIntro)} />
      {!enabled ? <Banner tone="info">{t.proposalsOff}</Banner> : (
        <form className="panel stack-form" onSubmit={submit} noValidate aria-label={t.proposeTitle}>
          <ErrorBanner message={error} />
          <EventFormFields form={form} set={set} problems={problems} idPrefix="propose" disabled={busy} />
          <div className="field">
            <label className="label-text" htmlFor="propose-note">{t.proposalNote}</label>
            <textarea id="propose-note" rows={3} maxLength={1000} value={note} onChange={(e) => setNote(e.target.value)} disabled={busy} />
            <span className="muted small">{t.proposalNoteHint}</span>
          </div>
          <div className="form-actions">
            <Link to={appPaths.events} className="button button-secondary">{strings.common.cancel}</Link>
            <button type="submit" className="button button-primary" disabled={busy}>{t.proposeSubmit}</button>
          </div>
        </form>
      )}
    </div>
  );
}

/** /app/events/proposals: the member's proposals with status, reviewer note, and withdraw. */
export function MyEventProposalsView() {
  const { client } = useAuth();
  const toast = useToast();
  useDocumentMeta(strings.member.metaTitle(t.proposalsTitle));
  const fetchPage = useCallback((cursor, signal) => client.enumerateMyEventProposals({ continuationToken: cursor, maxResults: 25 }, signal), [client]);
  const list = useCursorList(fetchPage);
  const withdraw = async (proposal) => {
    try {
      const updated = await client.withdrawEventProposal(proposal.id);
      list.update((items) => items.map((x) => (x.id === proposal.id ? { ...x, ...(updated || {}), status: (updated && updated.status) || 'Withdrawn' } : x)));
      toast.success(t.withdrawn);
    } catch (err) {
      toast.error(errorText(err, phase4Errors()));
    }
  };
  return (
    <div className="member-view">
      <Link to={appPaths.events} className="back-link small"><Icon name="chevronLeft" size={14} /><span>{t.back}</span></Link>
      <PageHeader title={t.proposalsTitle} actions={<Link to={appPaths.proposeEvent} className="button button-primary button-sm"><Icon name="plus" size={14} /><span>{t.propose}</span></Link>} />
      <ErrorBanner message={list.error ? errorText(list.error, phase4Errors(), t.loadError) : ''} onRetry={list.refresh} />
      {!list.loaded && list.loading && <Loading />}
      {list.loaded && !list.items.length && !list.error && <EmptyState icon="calendar" title={t.proposalsEmpty} />}
      <div className="p4-list">
        {list.items.map((p) => (
          <div key={p.id} className="p4-row" data-testid={'proposal-' + p.id}>
            <span className="p4-row-main">
              {p.status === 'Published' ? <Link to={appPaths.event(p.id)} className="p4-row-title">{p.title}</Link> : <span className="p4-row-title">{p.title}</span>}
              <span className="p4-card-meta">
                {p.startUtc && <span>{formatEventShort(p)}</span>}
                <Badge tone={p.status === 'Published' ? 'success' : p.status === 'Rejected' ? 'danger' : 'neutral'}>{t.proposalStatus[p.status] || p.status}</Badge>
              </span>
              {p.reviewNote && <span className="small">{t.reviewNote}: {p.reviewNote}</span>}
            </span>
            {p.status === 'Proposed' && <button type="button" className="button button-secondary button-sm" onClick={() => withdraw(p)}>{t.withdraw}</button>}
          </div>
        ))}
      </div>
      {list.loaded && !list.done && <button type="button" className="button button-secondary feed-more" onClick={list.loadMore}>{t.loadMore}</button>}
    </div>
  );
}

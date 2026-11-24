import useCollectionPage from '../../../hooks/useCollectionPage.js';
import TablePagination from '../../../components/TablePagination.jsx';
import { useCallback, useEffect, useState } from 'react';
import strings from '../../../strings.js';
import { useAdminAuth } from '../../../context/AdminAuthContext.jsx';
import { useToast } from '../../../context/ToastContext.jsx';
import { errorMessage } from '../../../utils/api.js';
import PageHeader from '../../../components/PageHeader.jsx';
import ConfirmModal from '../../../components/ConfirmModal.jsx';
import { Badge } from '../../../components/Badges.jsx';
import { ErrorBanner, Loading } from '../../../components/States.jsx';
import { formatDateTime } from '../../../utils/format.js';

const s = strings.ai.promptsAdmin;
const OPEN_RUN = ['Queued', 'Running'];

function percent(rate) {
  return Math.round((rate || 0) * 1000) / 10 + '%';
}

function parseSummary(run) {
  if (!run || !run.summary) return null;
  try {
    return JSON.parse(run.summary);
  } catch {
    return null;
  }
}

/**
 * What the newest benchmark of a draft says about publishing it.
 * @param {object} prompt The prompt view.
 * @returns {{ tone: string, text: string, open: boolean, summary: object|null }} Status.
 */
function gateStatus(prompt) {
  const run = prompt && prompt.draftBenchmark;
  if (!prompt || !prompt.draft) return null;
  if (!prompt.requiresBenchmark) return { tone: 'neutral', text: s.gateOff, open: false, summary: null };
  if (!run) return { tone: 'warning', text: s.gateNone, open: false, summary: null };
  if (OPEN_RUN.includes(run.status)) {
    const done = (run.itemsSucceeded || 0) + (run.itemsFailed || 0) + (run.itemsDead || 0) + (run.itemsSkipped || 0);
    return { tone: 'info', text: s.gateRunning(done, run.itemsTotal || 0), open: true, summary: null };
  }
  const summary = parseSummary(run);
  if (!summary) return { tone: 'danger', text: s.gateIncomplete, open: false, summary: null };
  if (summary.dryRun) return { tone: 'warning', text: s.gateDry, open: false, summary };
  if (summary.gatePassed) return { tone: 'success', text: s.gatePassed(percent(summary.passRate)), open: false, summary };
  return { tone: 'danger', text: s.gateFailed(percent(summary.passRate)), open: false, summary };
}

/**
 * Admin, AI, Prompts (docs/INTEGRATION.md owner requirement R7): every prompt the platform sends to a model, with the
 * locked engine rules shown read-only above the scope guardrail, length and required-word checks, drafts that go live
 * only through the scope benchmark gate, version history, and reset to the default.
 */
function AiPromptsView() {
  const promptsPage = useCollectionPage();
  const { query: promptsPageQuery, setResult: promptsPageSetResult } = promptsPage;
  const versionsPage = useCollectionPage();
  const { query: versionsPageQuery, setResult: versionsPageSetResult } = versionsPage;

  const { client } = useAdminAuth();
  const toast = useToast();
  const [prompts, setPrompts] = useState(null);
  const [selected, setSelected] = useState('');
  const [draft, setDraft] = useState('');
  const [versions, setVersions] = useState([]);
  const [showDefault, setShowDefault] = useState(false);
  const [confirmReset, setConfirmReset] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(null);

  const textOf = (p) => (p ? (p.draft ? p.draft.content : p.content) : '');

  const load = useCallback(async (keep, keepText) => {
    try {
      const page = await client.adminListPrompts(promptsPageQuery);
      promptsPageSetResult(page);
      const list = page.objects;
      setPrompts(list);
      const key = list.some((item) => item.key === keep) ? keep : (list[0]?.key || '');
      setSelected(key);
      const current = list.find((p) => p.key === key);
      if (!keepText) setDraft(current ? (current.draft ? current.draft.content : current.content) : '');
      setError(null);
    } catch (err) {
      setError(errorMessage(err));
    }
  }, [client, promptsPageQuery, promptsPageSetResult]);

  const loadVersions = useCallback(async (key) => {
    if (!key) return;
    try {
      const page = await client.adminEnumeratePromptVersions(key, versionsPageQuery);
      setVersions(page.objects || []);
      versionsPageSetResult(page);
    } catch {
      setVersions([]);
    }
  }, [client, versionsPageQuery, versionsPageSetResult]);

  useEffect(() => { load(); }, [load]);
  useEffect(() => { loadVersions(selected); }, [selected, loadVersions]);

  const prompt = prompts ? prompts.find((p) => p.key === selected) : null;
  const gate = gateStatus(prompt);
  const running = Boolean(gate && gate.open);

  useEffect(() => {
    if (!running) return undefined;
    const timer = setInterval(() => { load(selected, true); }, 5000);
    return () => clearInterval(timer);
  }, [running, selected, load]);

  const choose = (key) => {
    const next = prompts.find((p) => p.key === key);
    setSelected(key);
    setDraft(textOf(next));
    setShowDefault(false);
  };
  const length = draft.trim().length;
  const missing = prompt ? (prompt.requiredWords || []).filter((w) => !draft.toLowerCase().includes(w.toLowerCase())) : [];
  const valid = prompt && length >= prompt.minLength && length <= prompt.maxLength && missing.length === 0;

  const act = async (work, message) => {
    setBusy(true);
    try {
      await work();
      if (message) toast.success(message);
      await load(selected);
      await loadVersions(selected);
    } catch (err) {
      toast.error(errorMessage(err));
    } finally {
      setBusy(false);
    }
  };

  const save = async (e) => {
    e.preventDefault();
    if (!valid) return;
    await act(() => client.adminSavePrompt(selected, draft), s.saved);
  };
  const benchmark = () => act(() => client.adminBenchmarkPrompt(selected, {}), s.benchmarkStarted);
  const publish = () => act(() => client.adminPublishPrompt(selected, prompt.draft.id), s.published);
  const reset = async () => {
    setConfirmReset(false);
    await act(() => client.adminResetPrompt(selected), s.wasReset);
  };

  const versionLabel = (v) => {
    if (v.isReset) return s.resetVersion;
    if (v.isDraft) return s.draftBadge(v.version);
    if (!v.publishedUtc) return s.customBadge(v.version) + ' (' + s.unpublished + ')';
    return s.customBadge(v.version);
  };

  return (
    <section className="stack">
      <PageHeader title={s.title} summary={s.intro} />
      <ErrorBanner message={error} onRetry={() => load(selected)} />
      <TablePagination {...promptsPage.props} />
      {!prompts && !error && <Loading />}
      {prompts && (
        <div className="stack">
          <label className="inline-field" htmlFor="prompt-select">
            <span className="label-text">{s.choose}</span>
            <select id="prompt-select" value={selected} onChange={(e) => choose(e.target.value)} disabled={busy}>
              {prompts.map((p) => <option key={p.key} value={p.key}>{p.title}</option>)}
            </select>
          </label>
          {prompt && (
            <form className="stack-form" onSubmit={save} noValidate>
              <div className="badge-row">
                {prompt.isDefault ? <Badge tone="neutral">{s.defaultBadge}</Badge> : <Badge tone="info">{s.customBadge(prompt.version)}</Badge>}
                {prompt.draft && <Badge tone="warning">{s.draftBadge(prompt.draft.version)}</Badge>}
                <span className="muted small">{prompt.defaultSource === 'Property' ? s.fromProperty : s.fromEngine}</span>
              </div>
              <p className="muted">{prompt.description}</p>
              {prompt.lockedPrefix && (
                <div className="field">
                  <span className="label-text">{s.locked}</span>
                  <pre className="code-block" aria-readonly="true">{prompt.lockedPrefix}</pre>
                </div>
              )}
              <div className="field">
                <label className="label-text" htmlFor="prompt-content">{s.content}</label>
                <textarea id="prompt-content" rows={10} value={draft} onChange={(e) => setDraft(e.target.value)} disabled={busy} maxLength={prompt.maxLength + 200} aria-describedby="prompt-help" />
                <span id="prompt-help" className={'small ' + (valid ? 'muted' : 'field-error')}>
                  {s.counter(length, prompt.minLength, prompt.maxLength)}
                  {missing.length > 0 ? '. ' + s.required + ': ' + missing.join(', ') : ''}
                </span>
              </div>
              {(prompt.variables || []).length > 0 && <p className="muted small">{s.variables}: {prompt.variables.join(', ')}</p>}
              <div className="form-actions-start">
                <button type="submit" className="button button-primary" disabled={busy || !valid || draft === textOf(prompt)}>{s.save}</button>
                <button type="button" className="button button-secondary" disabled={busy || prompt.isDefault} onClick={() => setConfirmReset(true)}>{s.reset}</button>
                <button type="button" className="button button-ghost" onClick={() => setShowDefault((v) => !v)}>{showDefault ? s.hideDefault : s.showDefault}</button>
              </div>
              {showDefault && <pre className="code-block">{prompt.defaultContent}</pre>}
              {prompt.draft && gate && (
                <section className="stack" aria-labelledby="prompt-gate">
                  <h2 id="prompt-gate" className="section-title">{s.gateTitle}</h2>
                  <p className="muted small">{s.draftNote}</p>
                  <div className="badge-row" role="status">
                    <Badge tone={gate.tone}>{gate.text}</Badge>
                  </div>
                  {gate.summary && (gate.summary.failures || []).length > 0 && (
                    <ul className="stack small">
                      {gate.summary.failures.slice(0, 5).map((f) => <li key={f}>{f}</li>)}
                    </ul>
                  )}
                  {gate.summary && (
                    <p className="muted small">{(gate.summary.categories || []).map((c) => s.categoryRate(c.category, percent(c.passRate))).join(', ')}</p>
                  )}
                  <div className="form-actions-start">
                    {prompt.requiresBenchmark && <button type="button" className="button button-secondary" disabled={busy || running} onClick={benchmark}>{s.benchmark}</button>}
                    <button type="button" className="button button-primary" disabled={busy || running} onClick={publish}>{s.publish}</button>
                    {running && <button type="button" className="button button-ghost" onClick={() => load(selected, true)}>{s.refresh}</button>}
                  </div>
                </section>
              )}
            </form>
          )}
          <section className="stack" aria-labelledby="prompt-history">
            <h2 id="prompt-history" className="section-title">{s.history}</h2>
            <TablePagination {...versionsPage.props} />
            {versions.length === 0 ? <p className="muted">{s.historyEmpty}</p> : (
              <ul className="stack">
                {versions.map((v) => (
                  <li key={v.id} className="cell-stack">
                    <strong>{versionLabel(v)}</strong>
                    <span className="muted small">{formatDateTime(v.publishedUtc || v.createdUtc)}</span>
                  </li>
                ))}
              </ul>
            )}
          </section>
        </div>
      )}
      <ConfirmModal open={confirmReset} title={s.resetTitle} message={s.resetBody} confirmLabel={s.reset} onCancel={() => setConfirmReset(false)} onConfirm={reset} />
    </section>
  );
}

export default AiPromptsView;

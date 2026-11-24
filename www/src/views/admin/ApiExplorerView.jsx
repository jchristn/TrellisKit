import { useMemo, useState } from 'react';
import strings from '../../strings.js';
import { useAdminAuth } from '../../context/AdminAuthContext.jsx';
import useApiExplorer from '../../hooks/useApiExplorer.js';
import { errorMessage } from '../../utils/api.js';
import { filterOperations, groupOperations, isDestructive, methodAllowsBody } from '../../utils/openApi.js';
import { formatBytes, formatDuration, formatShortDateTime, prettyBody } from '../../utils/format.js';
import PageHeader from '../../components/PageHeader.jsx';
import ConfirmModal from '../../components/ConfirmModal.jsx';
import CodeBlock from '../../components/CodeBlock.jsx';
import CopyButton from '../../components/CopyButton.jsx';
import Icon from '../../components/Icon.jsx';
import { Badge, MethodBadge, StatusBadge } from '../../components/Badges.jsx';
import { EmptyState, ErrorBanner, Loading } from '../../components/States.jsx';

const e = strings.admin.explorer;

function ParamInputs({ title, params, values, onChange }) {
  if (params.length === 0) return null;
  return (
    <fieldset className="explorer-fieldset">
      <legend>{title}</legend>
      {params.map((p) => {
        const id = 'param-' + p.in + '-' + p.name;
        const schema = p.schema || {};
        const required = p.required || p.in === 'path';
        return (
          <div key={id} className="param-row">
            <label htmlFor={id} className="param-label">
              <code className="mono">{p.name}</code>
              {required && <span className="required-mark" title={e.required}>*</span>}
              {schema.type && <span className="muted small">{schema.type}{schema.format ? ' (' + schema.format + ')' : ''}</span>}
            </label>
            {Array.isArray(schema.enum) ? (
              <select id={id} value={values[p.name] ?? ''} onChange={(ev) => onChange({ ...values, [p.name]: ev.target.value })}>
                {!required && <option value="" />}
                {schema.enum.map((opt) => <option key={String(opt)} value={String(opt)}>{String(opt)}</option>)}
              </select>
            ) : (
              <input
                id={id}
                type="text"
                value={values[p.name] ?? ''}
                onChange={(ev) => onChange({ ...values, [p.name]: ev.target.value })}
                aria-required={required}
                placeholder={p.description || ''}
              />
            )}
          </div>
        );
      })}
    </fieldset>
  );
}

function HeaderEditor({ rows, onChange }) {
  const update = (index, key, value) => onChange(rows.map((r, i) => (i === index ? { ...r, [key]: value } : r)));
  return (
    <div className="header-editor">
      {rows.map((row, index) => (
        <div key={index} className="header-row">
          <input type="text" value={row.name} placeholder={e.headerName} aria-label={e.headerName} onChange={(ev) => update(index, 'name', ev.target.value)} className="mono" />
          <input type="text" value={row.value} placeholder={e.headerValue} aria-label={e.headerValue} onChange={(ev) => update(index, 'value', ev.target.value)} className="mono" />
          <button type="button" className="icon-button" onClick={() => onChange(rows.filter((_, i) => i !== index))} aria-label={e.removeHeader} title={e.removeHeader}>
            <Icon name="trash" size={14} />
          </button>
        </div>
      ))}
      <button type="button" className="button button-secondary button-sm" onClick={() => onChange([...rows, { name: '', value: '' }])}>
        <Icon name="plus" size={14} /><span>{e.addHeader}</span>
      </button>
    </div>
  );
}

/**
 * OpenAPI-driven API Explorer. Requests inherit the admin bearer token; destructive operations
 * need confirmation; snippets reflect the exact composed request.
 */
function ApiExplorerView() {
  const { api } = useAdminAuth();
  const x = useApiExplorer(api);
  const [search, setSearch] = useState('');
  const [tab, setTab] = useState('body');
  const [snippetTab, setSnippetTab] = useState('curl');
  const [confirming, setConfirming] = useState(false);
  const [showErrors, setShowErrors] = useState(false);

  const groups = useMemo(() => groupOperations(filterOperations(x.operations, search), e.untagged), [x.operations, search]);
  const op = x.operation;

  const run = () => {
    setShowErrors(true);
    if (x.bodyError || x.missingRequired.length) return;
    if (isDestructive(op.method, op.path)) setConfirming(true);
    else x.execute();
  };

  const params = (where) => (op ? op.parameters.filter((p) => p.in === where) : []);
  const res = x.response;
  const headerText = res && res.headers ? Object.entries(res.headers).map(([k, v]) => k + ': ' + v).join('\n') : '';

  return (
    <div className="view view-wide">
      <PageHeader title={strings.admin.routes.explorer.label} summary={strings.admin.routes.explorer.summary} />

      {x.specQuery.loading && !x.specQuery.data && <Loading />}
      {x.specQuery.error && (
        <EmptyState icon="alert" title={e.loadError} action={<button type="button" className="button button-secondary" onClick={x.specQuery.reload}>{strings.common.retry}</button>}>
          {e.missingSpec} {errorMessage(x.specQuery.error, '')}
        </EmptyState>
      )}

      {x.specQuery.data && (
        <div className="explorer">
          <aside className="explorer-pane explorer-ops" aria-label={e.operations}>
            <label className="field">
              <span className="label-text">{e.search}</span>
              <input type="search" value={search} onChange={(ev) => setSearch(ev.target.value)} placeholder={e.searchPlaceholder} />
            </label>
            <div className="ops-list">
              {groups.length === 0 && <p className="muted small">{e.noOperations}</p>}
              {groups.map((group) => (
                <div key={group.tag} className="ops-group">
                  <div className="nav-group-label">{group.tag}</div>
                  {group.operations.map((o) => (
                    <button
                      key={o.id}
                      type="button"
                      className={'op-item' + (op && op.id === o.id ? ' active' : '')}
                      aria-current={op && op.id === o.id ? 'true' : undefined}
                      onClick={() => { x.selectOperation(o.id); setShowErrors(false); }}
                    >
                      <MethodBadge method={o.method} />
                      <span className="op-text">
                        <code className="mono op-path">{o.path}</code>
                        {o.summary && <span className="muted small op-summary">{o.summary}</span>}
                      </span>
                    </button>
                  ))}
                </div>
              ))}
            </div>
            <details className="explorer-history">
              <summary>{e.history} ({x.history.length})</summary>
              {x.history.length === 0 && <p className="muted small">{e.noHistory}</p>}
              <ul>
                {x.history.map((h) => (
                  <li key={h.id} className="history-item">
                    <MethodBadge method={h.method} />
                    <code className="mono truncate" title={h.path}>{h.path}</code>
                    <StatusBadge code={h.status} />
                    <span className="muted small">{formatShortDateTime(h.at)}</span>
                    <span className="history-actions">
                      <button type="button" className="button button-secondary button-sm" onClick={() => x.loadHistory(h)}>{e.load}</button>
                      <button type="button" className="icon-button" onClick={() => x.removeHistory(h.id)} aria-label={e.removeHistory} title={e.removeHistory}>
                        <Icon name="trash" size={14} />
                      </button>
                    </span>
                  </li>
                ))}
              </ul>
            </details>
          </aside>

          <section className="explorer-pane explorer-request" aria-label={op ? op.method + ' ' + op.path : e.selectPrompt}>
            {!op && <EmptyState icon="play" title={e.selectPrompt} />}
            {op && (
              <>
                <div className="op-header">
                  <MethodBadge method={op.method} />
                  <code className="mono op-header-path">{op.path}</code>
                  {op.deprecated && <Badge tone="warning">{e.deprecated}</Badge>}
                </div>
                {op.summary && <p className="op-summary-text">{op.summary}</p>}
                {op.description && <p className="muted small">{op.description}</p>}
                <p className="muted small"><Icon name="lock" size={12} /> {e.authInherited}</p>

                <ParamInputs title={e.pathParams} params={params('path')} values={x.pathParams} onChange={x.setPathParams} />
                <ParamInputs title={e.queryParams} params={params('query')} values={x.queryParams} onChange={x.setQueryParams} />
                <ParamInputs title={e.headerParams} params={params('header')} values={x.headerParams} onChange={x.setHeaderParams} />

                <fieldset className="explorer-fieldset">
                  <legend>{e.extraHeaders}</legend>
                  <HeaderEditor rows={x.extraHeaders} onChange={x.setExtraHeaders} />
                </fieldset>

                {methodAllowsBody(op.method) && (
                  <fieldset className="explorer-fieldset">
                    <legend>{e.requestBody}</legend>
                    <textarea
                      className="mono body-editor"
                      value={x.body}
                      onChange={(ev) => x.setBody(ev.target.value)}
                      rows={8}
                      spellCheck="false"
                      aria-label={e.requestBody}
                      aria-invalid={Boolean(x.bodyError)}
                    />
                    <div className="row-between">
                      {x.bodyError ? <span className="field-error">{x.bodyError}</span> : <span />}
                      <button
                        type="button"
                        className="button button-secondary button-sm"
                        disabled={Boolean(x.bodyError) || !x.body.trim()}
                        onClick={() => x.setBody(prettyBody(x.body))}
                      >
                        {e.formatJson}
                      </button>
                    </div>
                  </fieldset>
                )}

                <div className="resolved-url">
                  <span className="label-text">{e.resolvedUrl}</span>
                  <span className="copy-row">
                    <code className="mono break">{x.resolvedUrl}</code>
                    <CopyButton value={x.resolvedUrl} label={e.resolvedUrl} />
                  </span>
                </div>

                {showErrors && x.missingRequired.length > 0 && (
                  <div className="field-error" role="alert">{x.missingRequired.map((n) => e.requiredMissing(n)).join(' ')}</div>
                )}

                <div className="row-actions">
                  <button type="button" className={'button ' + (isDestructive(op.method, op.path) ? 'button-danger' : 'button-primary')} onClick={run} disabled={x.running}>
                    <Icon name="play" size={14} /><span>{x.running ? e.executing : e.execute}</span>
                  </button>
                  {x.running && <button type="button" className="button button-secondary" onClick={x.cancel}>{e.cancel}</button>}
                </div>

                <section className="explorer-response" aria-label={e.response}>
                  <h2 className="section-title">{e.response}</h2>
                  {!res && !x.running && <p className="muted small">{e.noResponse}</p>}
                  {x.running && !res && <Loading inline />}
                  {res && res.error && <ErrorBanner message={res.error} />}
                  {res && !res.error && (
                    <>
                      <div className="response-meta">
                        <span>{e.status}: <StatusBadge code={res.status} /> <span className="muted">{res.statusText}</span></span>
                        <span>{e.duration}: {formatDuration(res.durationMs)}</span>
                        <span>{e.size}: {formatBytes(res.bytes)}</span>
                      </div>
                      <div className="tabs" role="tablist">
                        {['body', 'headers', 'code'].map((id) => (
                          <button key={id} type="button" role="tab" aria-selected={tab === id} className={'tab' + (tab === id ? ' active' : '')} onClick={() => setTab(id)}>
                            {e.tabs[id]}
                          </button>
                        ))}
                      </div>
                      {tab === 'body' && <CodeBlock value={prettyBody(res.body)} label={e.tabs.body} maxHeight={480} />}
                      {tab === 'headers' && <CodeBlock value={headerText} label={e.tabs.headers} />}
                    </>
                  )}
                  {(tab === 'code' || !res || res.error) && x.snippets && (
                    <div className="snippets">
                      <div className="tabs" role="tablist">
                        {['curl', 'fetch', 'csharp'].map((id) => (
                          <button key={id} type="button" role="tab" aria-selected={snippetTab === id} className={'tab' + (snippetTab === id ? ' active' : '')} onClick={() => setSnippetTab(id)}>
                            {e.snippets[id]}
                          </button>
                        ))}
                      </div>
                      <CodeBlock value={x.snippets[snippetTab]} label={e.copySnippet} />
                      <p className="muted small">{e.tokenPlaceholderNote}</p>
                    </div>
                  )}
                </section>
              </>
            )}
          </section>
        </div>
      )}

      <ConfirmModal
        open={confirming}
        title={e.confirmTitle}
        message={op ? e.confirmBody(op.method, x.relativeUrl) : ''}
        confirmLabel={e.execute}
        onConfirm={() => { setConfirming(false); x.execute(); }}
        onCancel={() => setConfirming(false)}
      />
    </div>
  );
}

export default ApiExplorerView;

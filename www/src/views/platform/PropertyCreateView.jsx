import { useCallback, useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { AppPlatform, DomainKind, domainsAndAppsBody, domainsAndAppsProblems, isValidPropertySlug, parsePropertyDocument } from '@trelliskit/shared';
import strings from '../../strings.js';
import { usePlatformAuth } from '../../context/PlatformAuthContext.jsx';
import { useToast } from '../../context/ToastContext.jsx';
import useApiQuery from '../../hooks/useApiQuery.js';
import useDebounce from '../../hooks/useDebounce.js';
import { errorMessage, getFieldErrors } from '../../utils/api.js';
import { normalizePage } from '../../utils/query.js';
import { platformMetadataSource } from '../../utils/platformQuery.js';
import PageHeader from '../../components/PageHeader.jsx';
import MetadataEditor from '../../components/MetadataEditor.jsx';
import Icon from '../../components/Icon.jsx';
import { ErrorBanner } from '../../components/States.jsx';

const t = strings.platform.create;
const e = strings.admin.domainsEditor;
const route = strings.platform.routes['new-property'];

/**
 * The `POST /platform/properties` body from the form. The server generates the id; `domains`
 * and `apps` are sent only when the operator enters them (otherwise they come from the document).
 */
export function buildCreateBody(form) {
  const body = {};
  if (form.source === 'clone') body.cloneFromPropertyId = form.cloneFromPropertyId;
  else body.document = form.document;
  if (form.name && form.name.trim()) body.name = form.name.trim();
  if (form.slug && form.slug.trim()) body.slug = form.slug.trim();
  if (form.customDomains) {
    const lists = domainsAndAppsBody(form.domains, form.apps);
    body.domains = lists.domains;
    body.apps = lists.apps;
  }
  if (form.meta && (form.meta.labels.length || Object.keys(form.meta.tags).length)) {
    body.labels = form.meta.labels;
    body.tags = form.meta.tags;
  }
  return body;
}

function ClonePicker({ value, onChange }) {
  const { call } = usePlatformAuth();
  const [search, setSearch] = useState('');
  const debounced = useDebounce(search, 250);
  const fetcher = useCallback((signal) => call((client) => client.listProperties({ maxResults: 20, search: debounced || undefined, ordering: 'NameAscending' }, signal)), [call, debounced]);
  const list = useApiQuery(fetcher);
  const items = list.data ? normalizePage(list.data, 20).items.filter((x) => x.status !== 'Deleted') : [];
  return (
    <div className="stack-form">
      <label className="field" htmlFor="clone-search">
        <span className="label-text">{t.cloneSearch}</span>
        <input id="clone-search" type="search" value={search} onChange={(x) => setSearch(x.target.value)} />
      </label>
      <label className="field" htmlFor="clone-source">
        <span className="label-text">{t.cloneLabel}</span>
        <select id="clone-source" value={value} onChange={(x) => onChange(x.target.value)}>
          <option value="">{t.clonePlaceholder}</option>
          {items.map((x) => <option key={x.id} value={x.id}>{x.name + ' (' + x.id + ')'}</option>)}
        </select>
      </label>
      {list.error && <ErrorBanner message={errorMessage(list.error, '')} onRetry={list.reload} />}
    </div>
  );
}

/** New property: from an uploaded or pasted property.json, or a clone of another property. */
/**
 * The inline slug message for a create failure: `409 SlugInUse` (with `fieldErrors.slug`) names
 * the slug field; the slug is never changed automatically. Returns '' for other failures.
 */
export function slugInUseMessage(err, slugEntered) {
  const fe = getFieldErrors(err);
  if (!(err && (err.code === 'SlugInUse' || (err.status === 409 && fe.slug)))) return '';
  return slugEntered ? t.slugInUse : t.slugInUseDocument;
}

function PropertyCreateView() {
  const { call, client } = usePlatformAuth();
  const metadataSource = useMemo(() => platformMetadataSource(client), [client]);
  const toast = useToast();
  const navigate = useNavigate();
  const [source, setSource] = useState('document');
  const [text, setText] = useState('');
  const [cloneFromPropertyId, setCloneFrom] = useState('');
  const [name, setName] = useState('');
  const [slug, setSlug] = useState('');
  const [customDomains, setCustomDomains] = useState(false);
  const [domains, setDomains] = useState([{ host: '', kind: DomainKind.Web }]);
  const [apps, setApps] = useState([]);
  const [meta, setMeta] = useState({ labels: [], tags: {} });
  const [metaProblems, setMetaProblems] = useState([]);
  const [error, setError] = useState('');
  const [fieldErrors, setFieldErrors] = useState({});
  const [busy, setBusy] = useState(false);

  const parsed = useMemo(() => parsePropertyDocument(text), [text]);

  const readFile = async (file) => {
    if (!file) return;
    try {
      setText(await file.text());
    } catch {
      setError(t.fileError);
    }
  };

  const submit = async (ev) => {
    ev.preventDefault();
    setFieldErrors({});
    if (source === 'document' && parsed.error) {
      setError(t.documentProblems[parsed.error]);
      return;
    }
    if (source === 'clone' && !cloneFromPropertyId) {
      setError(t.cloneRequired);
      return;
    }
    if (slug.trim() && !isValidPropertySlug(slug.trim())) {
      setFieldErrors({ slug: t.slugHint });
      setError(t.fieldErrors);
      return;
    }
    if (customDomains) {
      const lists = domainsAndAppsBody(domains, apps);
      const problems = domainsAndAppsProblems(lists.domains, lists.apps);
      if (Object.keys(problems).length) {
        setFieldErrors(Object.fromEntries(Object.entries(problems).map(([k, v]) => [k, e.problems[v] || v])));
        setError(e.fixProblems);
        return;
      }
    }
    if (metaProblems.length) return;
    setError('');
    setBusy(true);
    try {
      const body = buildCreateBody({ source, document: parsed.document, cloneFromPropertyId, name, slug, customDomains, domains, apps, meta });
      const created = await call((client) => client.createProperty(body));
      toast.success(t.created(created.name));
      navigate('/platform/properties/' + encodeURIComponent(created.id));
    } catch (err) {
      const slugProblem = slugInUseMessage(err, Boolean(slug.trim()));
      if (slugProblem) {
        setFieldErrors({ ...getFieldErrors(err), slug: slugProblem });
        setError('');
        setBusy(false);
        return;
      }
      setFieldErrors(getFieldErrors(err));
      setError(err && err.code === 'PropertyLimitReached' ? t.limitReached + ' ' + errorMessage(err, '') : err && err.code === 'PropertyIdInUse' ? t.inUse : errorMessage(err, strings.common.errorGeneric));
      setBusy(false);
    }
  };

  const fieldErrorList = Object.entries(fieldErrors).filter(([k]) => k !== 'slug');

  return (
    <div className="view">
      <PageHeader title={route.label} summary={route.summary} />
      <form className="settings-form" onSubmit={submit} noValidate>
        <ErrorBanner message={error} onDismiss={() => setError('')} />
        {fieldErrorList.length > 0 && (
          <ul className="metadata-problems" role="alert" aria-label={t.fieldErrors}>
            {fieldErrorList.map(([k, v]) => <li key={k} className="field-error"><code className="mono">{k}</code>: {v}</li>)}
          </ul>
        )}

        <fieldset className="settings-section" disabled={busy}>
          <legend><span>{t.sourceLegend}</span></legend>
          <label className="check-field" htmlFor="source-document">
            <input id="source-document" type="radio" name="source" checked={source === 'document'} onChange={() => setSource('document')} />
            <span>{t.sourceDocument}</span>
          </label>
          <label className="check-field" htmlFor="source-clone">
            <input id="source-clone" type="radio" name="source" checked={source === 'clone'} onChange={() => setSource('clone')} />
            <span>{t.sourceClone}</span>
          </label>
          {source === 'document' ? (
            <div className="stack-form">
              <p className="muted small">{t.sourceDocumentHint}</p>
              <label className="field" htmlFor="property-file">
                <span className="label-text">{t.upload}</span>
                <input id="property-file" type="file" accept="application/json,.json" onChange={(x) => readFile(x.target.files && x.target.files[0])} />
              </label>
              <label className="field" htmlFor="property-document">
                <span className="label-text">{t.paste}</span>
                <textarea id="property-document" className="mono" rows={12} spellCheck="false" value={text} onChange={(x) => setText(x.target.value)} aria-invalid={Boolean(text && parsed.error)} />
              </label>
              {text && parsed.error && <span className="field-error" role="alert">{t.documentProblems[parsed.error]}</span>}
            </div>
          ) : (
            <>
              <p className="muted small">{t.sourceCloneHint}</p>
              <ClonePicker value={cloneFromPropertyId} onChange={setCloneFrom} />
            </>
          )}
        </fieldset>

        <fieldset className="settings-section" disabled={busy}>
          <legend><span>{strings.platform.detail.overviewTitle}</span></legend>
          <label className="field" htmlFor="property-name">
            <span className="label-text">{t.name}</span>
            <input id="property-name" value={name} onChange={(x) => setName(x.target.value)} />
            <span className="muted small">{t.nameHint}</span>
          </label>
          <label className="field" htmlFor="property-slug">
            <span className="label-text">{t.slug}</span>
            <input id="property-slug" className="mono" value={slug} onChange={(x) => { setSlug(x.target.value); setFieldErrors(({ slug: _cleared, ...rest }) => rest); }} aria-invalid={Boolean(fieldErrors.slug)} aria-describedby={fieldErrors.slug ? 'property-slug-error' : undefined} />
            {fieldErrors.slug && <span id="property-slug-error" className="field-error" role="alert">{fieldErrors.slug}</span>}
            <span className="muted small">{t.slugHint}</span>
          </label>
        </fieldset>

        <fieldset className="settings-section" disabled={busy}>
          <legend><span>{t.domainsLegend}</span></legend>
          <label className="check-field" htmlFor="domains-from-document">
            <input id="domains-from-document" type="radio" name="domains" checked={!customDomains} onChange={() => setCustomDomains(false)} />
            <span>{t.domainsFromDocument}</span>
          </label>
          <label className="check-field" htmlFor="domains-custom">
            <input id="domains-custom" type="radio" name="domains" checked={customDomains} onChange={() => setCustomDomains(true)} />
            <span>{t.domainsCustom}</span>
          </label>
          <p className="muted small">{t.domainsHint}</p>
          {customDomains && (
            <div className="stack-form">
              {domains.map((d, i) => (
                <div key={'d' + i} className="filter-bar">
                  <input className="mono" aria-label={e.host + ' ' + (i + 1)} placeholder={e.hostPlaceholder} value={d.host} onChange={(x) => setDomains((list) => list.map((y, j) => (j === i ? { ...y, host: x.target.value } : y)))} />
                  <select aria-label={e.kind + ' ' + (i + 1)} value={d.kind} onChange={(x) => setDomains((list) => list.map((y, j) => (j === i ? { ...y, kind: x.target.value } : y)))}>
                    {Object.values(DomainKind).map((k) => <option key={k} value={k}>{strings.platform.domainKinds[k]}</option>)}
                  </select>
                  <button type="button" className="icon-button" aria-label={e.removeDomain(d.host)} onClick={() => setDomains((list) => list.filter((_, j) => j !== i))}><Icon name="trash" size={14} /></button>
                </div>
              ))}
              <div><button type="button" className="button button-secondary button-sm" onClick={() => setDomains((list) => [...list, { host: '', kind: DomainKind.Api }])}><Icon name="plus" size={14} /><span>{e.addDomain}</span></button></div>
              {apps.map((a, i) => (
                <div key={'a' + i} className="filter-bar">
                  <select aria-label={e.platform + ' ' + (i + 1)} value={a.platform} onChange={(x) => setApps((list) => list.map((y, j) => (j === i ? { ...y, platform: x.target.value } : y)))}>
                    {Object.values(AppPlatform).map((p) => <option key={p} value={p}>{e.platforms[p]}</option>)}
                  </select>
                  <input className="mono" aria-label={e.appId + ' ' + (i + 1)} placeholder={e.appIdPlaceholder} value={a.appId} onChange={(x) => setApps((list) => list.map((y, j) => (j === i ? { ...y, appId: x.target.value } : y)))} />
                  <button type="button" className="icon-button" aria-label={e.removeApp(a.appId)} onClick={() => setApps((list) => list.filter((_, j) => j !== i))}><Icon name="trash" size={14} /></button>
                </div>
              ))}
              <div><button type="button" className="button button-secondary button-sm" onClick={() => setApps((list) => [...list, { platform: AppPlatform.Ios, appId: '' }])}><Icon name="plus" size={14} /><span>{e.addApp}</span></button></div>
            </div>
          )}
        </fieldset>

        <fieldset className="settings-section" disabled={busy}>
          <legend><span>{t.metadataLegend}</span></legend>
          <MetadataEditor value={meta} onChange={(next, problems) => { setMeta(next); setMetaProblems(problems); }} isStaff client={metadataSource} targetType="Property" legend={t.metadataLegend} />
        </fieldset>

        <div className="form-actions">
          <button type="submit" className="button button-primary" disabled={busy}>{busy ? t.submitting : t.submit}</button>
        </div>
      </form>
    </div>
  );
}

export default PropertyCreateView;

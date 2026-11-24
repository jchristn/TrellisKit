import { useCallback, useEffect, useMemo, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import {
  ApiError, MemberDirectoryOrderings, buildProfileFilters, countryOptions, filterableFields, isProfileFilterParam, parseProfileFilterParams,
  pickerOptions, profileFieldsOf, profileFilterChips, profileFilterOperators, profileFilterParams, rankHasLevel, rankMaxLevel, removeProfileFilter
} from '@trelliskit/shared';
import strings from '../../strings.js';
import { useAuth } from '../../context/AuthContext.jsx';
import { useProperty } from '../../context/PropertyContext.jsx';
import useCursorList from '../../hooks/useCursorList.js';
import useDocumentMeta from '../../hooks/useDocumentMeta.js';
import useEnumerationUrlState from '../../hooks/useEnumerationUrlState.js';
import Avatar from '../../components/Avatar.jsx';
import Icon from '../../components/Icon.jsx';
import MetadataFilterBar from '../../components/MetadataFilterBar.jsx';
import { Banner, EmptyState, ErrorBanner, Loading } from '../../components/States.jsx';
import { CardFields } from '../../components/profile/ProfileFieldDisplay.jsx';
import { authorName, errorText, mediaUrl, memberPath } from '../../utils/engagement.js';

const d = strings.directory;
const PAGE_SIZE = 24;
const DEFAULT_ORDERING = 'DisplayNameAscending';

/** A text input that commits on blur or Enter (each commit queries the backend). */
function CommitInput({ id, value, onCommit, type = 'text', placeholder, label, min, max }) {
  const [draft, setDraft] = useState(value || '');
  const [last, setLast] = useState(value || '');
  if (last !== (value || '')) {
    setLast(value || '');
    setDraft(value || '');
  }
  const commit = () => { if (draft !== (value || '')) onCommit(draft); };
  return (
    <input
      id={id} type={type} value={draft} placeholder={placeholder} aria-label={label} min={min} max={max}
      onChange={(e) => setDraft(e.target.value)} onBlur={commit}
      onKeyDown={(e) => { if (e.key === 'Enter') { e.preventDefault(); commit(); } }}
    />
  );
}

function TriState({ id, value, onChange, label }) {
  const current = value === true || value === 'true' ? 'true' : value === false || value === 'false' ? 'false' : '';
  return (
    <select id={id} value={current} aria-label={label} onChange={(e) => onChange(e.target.value === '' ? null : e.target.value === 'true')}>
      <option value="">{d.any}</option>
      <option value="true">{d.hasValue}</option>
      <option value="false">{d.hasNoValue}</option>
    </select>
  );
}

function OptionChecks({ options, selected, onChange, name }) {
  const list = Array.isArray(selected) ? selected : [];
  const toggle = (key) => onChange(list.includes(key) ? list.filter((k) => k !== key) : [...list, key]);
  return (
    <div className="chip-options" role="group" aria-label={name}>
      {options.map((o) => (
        <label key={o.key} className={'chip-option' + (list.includes(o.key) ? ' selected' : '')}>
          <input type="checkbox" checked={list.includes(o.key)} onChange={() => toggle(o.key)} />
          {o.color && <span className="rank-swatch" style={{ backgroundColor: o.color }} aria-hidden="true" />}
          <span>{o.label}</span>
        </label>
      ))}
    </div>
  );
}

/**
 * The filter controls for one filterable field, by type and the operators the type allows
 * (REST_API.md "Member filters").
 */
export function ProfileFilterControl({ field, value, onChange }) {
  const ops = value || {};
  const allowed = profileFilterOperators(field.type);
  const set = (op, next) => onChange({ ...ops, [op]: next });
  const id = 'dir-' + field.key;
  const countries = useMemo(() => (field.type === 'Country' ? countryOptions() : []), [field.type]);
  let body = null;
  switch (field.type) {
    case 'Rank': {
      const options = pickerOptions(field, ops.minRank, ops.maxRank);
      const levelKey = ops.minRank || (options[0] && options[0].key);
      body = (
        <>
          <label className="label-text" htmlFor={id + '-min'}>{d.minRank}</label>
          <select id={id + '-min'} value={ops.minRank || ''} onChange={(e) => set('minRank', e.target.value || null)}>
            <option value="">{d.any}</option>
            {options.map((o) => <option key={o.key} value={o.key}>{o.label}</option>)}
          </select>
          <label className="label-text" htmlFor={id + '-max'}>{d.maxRank}</label>
          <select id={id + '-max'} value={ops.maxRank || ''} onChange={(e) => set('maxRank', e.target.value || null)}>
            <option value="">{d.any}</option>
            {options.map((o) => <option key={o.key} value={o.key}>{o.label}</option>)}
          </select>
          {levelKey && rankHasLevel(field, levelKey) && (
            <>
              <label className="label-text" htmlFor={id + '-level'}>{d.minLevel(field.rank.levelLabel)}</label>
              <select id={id + '-level'} value={ops.minLevel || ''} onChange={(e) => set('minLevel', e.target.value || null)}>
                <option value="">{d.any}</option>
                {Array.from({ length: rankMaxLevel(field, levelKey) }, (_, i) => i + 1).map((n) => <option key={n} value={n}>{n}</option>)}
              </select>
            </>
          )}
        </>
      );
      break;
    }
    case 'SingleSelect':
      body = <OptionChecks name={field.label} options={pickerOptions(field, ops.in || [])} selected={ops.in} onChange={(list) => set('in', list.length ? list : null)} />;
      break;
    case 'MultiSelect': {
      const op = ops.all ? 'all' : 'any';
      body = (
        <>
          <OptionChecks name={field.label} options={pickerOptions(field, ops[op] || [])} selected={ops[op]} onChange={(list) => onChange({ ...ops, any: null, all: null, [op]: list.length ? list : null })} />
          <select aria-label={field.label} value={op} onChange={(e) => onChange({ ...ops, any: null, all: null, [e.target.value]: ops[op] || null })}>
            <option value="any">{d.matchAny}</option>
            <option value="all">{d.matchAll}</option>
          </select>
        </>
      );
      break;
    }
    case 'Country': {
      const list = ops.in || [];
      body = (
        <>
          <select id={id} value="" aria-label={field.label} onChange={(e) => e.target.value && set('in', [...list.filter((c) => c !== e.target.value), e.target.value])}>
            <option value="">{d.any}</option>
            {countries.filter((c) => !list.includes(c.code)).map((c) => <option key={c.code} value={c.code}>{c.name}</option>)}
          </select>
        </>
      );
      break;
    }
    case 'Text':
    case 'Email': {
      const op = allowed.includes('prefix') ? 'prefix' : 'eq';
      body = <CommitInput id={id} label={field.label + ' ' + (op === 'prefix' ? d.startsWith : d.equals)} placeholder={op === 'prefix' ? d.startsWith : d.equals} value={ops[op] || ''} onCommit={(text) => set(op, text || null)} />;
      break;
    }
    case 'Number':
    case 'Date': {
      const type = field.type === 'Date' ? 'date' : 'number';
      body = (
        <div className="form-grid">
          <CommitInput id={id + '-gte'} type={type} label={field.label + ' ' + d.from} placeholder={d.from} value={ops.gte || ''} onCommit={(v) => set('gte', v || null)} />
          <CommitInput id={id + '-lte'} type={type} label={field.label + ' ' + d.to} placeholder={d.to} value={ops.lte || ''} onCommit={(v) => set('lte', v || null)} />
        </div>
      );
      break;
    }
    case 'Boolean':
      body = (
        <select id={id} aria-label={field.label} value={ops.eq === true || ops.eq === 'true' ? 'true' : ops.eq === false || ops.eq === 'false' ? 'false' : ''} onChange={(e) => set('eq', e.target.value === '' ? null : e.target.value === 'true')}>
          <option value="">{d.any}</option>
          <option value="true">{strings.profileFields.yes}</option>
          <option value="false">{strings.profileFields.no}</option>
        </select>
      );
      break;
    default:
      body = null;
  }
  const onlyHasValue = !body && allowed.includes('hasValue');
  return (
    <fieldset className="fieldset directory-filter" data-field={field.key}>
      <legend className="label-text">{field.label}</legend>
      {body}
      {(onlyHasValue || field.type === 'History') && <TriState id={id + '-has'} label={field.label + ' ' + d.hasValueLabel} value={ops.hasValue} onChange={(v) => set('hasValue', v)} />}
    </fieldset>
  );
}

function DirectoryResults({ client, meta, filters, fields, onFilterError }) {
  const filterKey = JSON.stringify(filters);
  const metaKey = JSON.stringify(meta);
  const fetchPage = useCallback((cursor, signal) => {
    const m = JSON.parse(metaKey);
    const f = JSON.parse(filterKey);
    return client.enumerateMemberDirectory({
      ...m, ordering: m.ordering || DEFAULT_ORDERING, continuationToken: cursor, maxResults: PAGE_SIZE,
      filters: Object.keys(f).length ? { profile: f } : {}
    }, signal);
  }, [client, metaKey, filterKey]);
  const list = useCursorList(fetchPage);
  const bad = list.error instanceof ApiError && list.error.status === 400 && list.error.body && list.error.body.fieldErrors
    ? Object.keys(list.error.body.fieldErrors).filter((k) => k.startsWith('filters.profile.')).map((k) => k.slice('filters.profile.'.length))
    : [];
  const badKey = bad.join(',');
  useEffect(() => { if (badKey) onFilterError(badKey.split(',')); }, [badKey, onFilterError]);
  const total = typeof list.totalRecords === 'number' ? list.totalRecords : null;
  return (
    <>
      <ErrorBanner message={list.error && !bad.length ? errorText(list.error) : ''} onRetry={list.refresh} />
      {!list.loaded && list.loading && <Loading label={d.loading} />}
      {list.loaded && total !== null && <p className="muted small" role="status">{d.count(total)}</p>}
      {list.loaded && !list.items.length && !list.loading && !list.error && <EmptyState icon="users" title={d.empty}><p className="muted">{d.emptyHint}</p></EmptyState>}
      <ul className="directory-list">
        {list.items.map((user) => (
          <li key={user.id}>
            <Link className="directory-card" to={memberPath(user.id)}>
              <Avatar src={mediaUrl(user.avatarUrl)} name={authorName(user)} size={48} />
              <span className="directory-card-text">
                <strong>{authorName(user)}</strong>
                {user.handle && <span className="muted small">@{user.handle}</span>}
                <span className="small"><CardFields fields={fields} profile={user.profile || {}} /></span>
              </span>
            </Link>
          </li>
        ))}
      </ul>
      {list.loaded && list.loading && <Loading inline />}
      {list.loaded && !list.done && !list.loading && !list.error && <button type="button" className="button button-secondary feed-more" onClick={list.loadMore}>{d.loadMore}</button>}
    </>
  );
}

/**
 * /app/members: the member directory (`POST /api/v1/members/directory/enumerate`). Search,
 * ordering, label and tag filters, and profile filters on every filterable field, all kept in
 * the URL (`profile.<key>.<op>=`). Filters sit in a side panel on wide screens and a bottom
 * sheet on phones, opened from a button showing the active filter count.
 */
function MemberDirectoryView() {
  const { client } = useAuth();
  const { property, fill } = useProperty();
  useDocumentMeta(d.meta, fill(d.summary));
  const fields = useMemo(() => profileFieldsOf(property), [property]);
  const filterable = useMemo(() => filterableFields(fields), [fields]);
  const [searchParams, setSearchParams] = useSearchParams();
  const [meta, setMeta] = useEnumerationUrlState({ defaultOrdering: DEFAULT_ORDERING });
  const [search, setSearch] = useState(meta.search);
  const [lastSearch, setLastSearch] = useState(meta.search);
  const [open, setOpen] = useState(false);
  const [notice, setNotice] = useState('');
  if (lastSearch !== meta.search) {
    setLastSearch(meta.search);
    setSearch(meta.search);
  }
  const stateKey = JSON.stringify(parseProfileFilterParams(searchParams));
  const state = useMemo(() => JSON.parse(stateKey), [stateKey]);
  const filters = useMemo(() => buildProfileFilters(state, fields), [state, fields]);
  const chips = profileFilterChips(filters, fields);

  const writeFilters = useCallback((next) => {
    setSearchParams((current) => {
      const params = new URLSearchParams(current);
      for (const key of Array.from(params.keys())) if (isProfileFilterParam(key)) params.delete(key);
      for (const [key, value] of Object.entries(profileFilterParams(buildProfileFilters(next, fields)))) {
        for (const item of [].concat(value)) params.append(key, item);
      }
      return params;
    });
  }, [setSearchParams, fields]);
  const setField = (key, ops) => writeFilters({ ...state, [key]: ops });
  const onFilterError = useCallback((keys) => {
    let next = state;
    for (const key of keys) next = removeProfileFilter(next, key);
    setNotice(d.filterError);
    writeFilters(next);
  }, [state, writeFilters]);
  const activeCount = chips.length + (meta.labels || []).length + Object.keys(meta.tags || {}).length;
  const clearAll = () => {
    setSearchParams(() => new URLSearchParams());
    setNotice('');
  };
  const orderings = MemberDirectoryOrderings.map((value) => ({ value, label: d.orderings[value] || value }));

  return (
    <div className="member-view directory-view">
      <header className="page-header">
        <div className="page-header-text">
          <h1>{d.title}</h1>
          <p className="muted">{fill(d.summary)}</p>
        </div>
      </header>
      <form className="directory-toolbar" role="search" onSubmit={(e) => { e.preventDefault(); setMeta({ ...meta, search: search.trim() }); }}>
        <label className="visually-hidden" htmlFor="directory-search">{d.searchLabel}</label>
        <input id="directory-search" className="search-input" type="search" value={search} onChange={(e) => setSearch(e.target.value)} placeholder={d.search} maxLength={200} />
        <label className="visually-hidden" htmlFor="directory-ordering">{d.ordering}</label>
        <select id="directory-ordering" value={meta.ordering || DEFAULT_ORDERING} onChange={(e) => setMeta({ ...meta, ordering: e.target.value })}>
          {orderings.map((o) => <option key={o.value} value={o.value}>{o.label}</option>)}
        </select>
        {filterable.length > 0 && (
          <button type="button" className="button button-secondary directory-filter-toggle" aria-expanded={open} aria-controls="directory-filters" onClick={() => setOpen((v) => !v)}>
            <Icon name="filter" size={14} /><span>{activeCount ? d.filterCount(activeCount) : d.filters}</span>
          </button>
        )}
      </form>
      <MetadataFilterBar value={meta} onChange={setMeta} client={client} targetType="User" />
      {(chips.length > 0 || activeCount > 0) && (
        <div className="directory-chips" aria-label={d.filters}>
          {chips.map((chip) => (
            <span key={chip.key + chip.op} className="chip">
              {chip.label} {d.operators[chip.op] || chip.op} {chip.value}
              <button type="button" className="chip-remove" aria-label={d.removeFilter(chip.label)} onClick={() => writeFilters(removeProfileFilter(state, chip.key, chip.op))}><Icon name="close" size={10} /></button>
            </span>
          ))}
          <button type="button" className="button button-ghost button-sm" onClick={clearAll}>{d.clearAll}</button>
        </div>
      )}
      {notice && <Banner tone="warning" icon="alert">{notice}</Banner>}
      <div className={filterable.length ? 'directory-layout' : ''}>
        {filterable.length > 0 && (
          <aside id="directory-filters" className={'directory-filters' + (open ? ' open' : '')} aria-label={d.filters}>
            <div className="row-between">
              <h2 className="label-text">{d.profileFilters}</h2>
              <button type="button" className="icon-button directory-filter-toggle" aria-label={d.close} onClick={() => setOpen(false)}><Icon name="close" size={14} /></button>
            </div>
            {filterable.map((field) => (
              <ProfileFilterControl key={field.key} field={field} value={state[field.key]} onChange={(ops) => setField(field.key, ops)} />
            ))}
            {chips.length > 0 && <button type="button" className="button button-ghost button-sm" onClick={() => writeFilters({})}>{d.clearAll}</button>}
          </aside>
        )}
        <section aria-live="polite">
          <DirectoryResults client={client} meta={meta} filters={filters} fields={fields} onFilterError={onFilterError} />
        </section>
      </div>
    </div>
  );
}

export default MemberDirectoryView;
